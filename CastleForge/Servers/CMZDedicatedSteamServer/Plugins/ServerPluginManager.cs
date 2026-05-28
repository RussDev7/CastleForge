/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedServer.Plugins;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Plugins
{
    /// <summary>
    /// Manages server-side world plugins and forwards host/world messages to them.
    /// </summary>
    /// <remarks>
    /// Plugins are executed in registration order. If any plugin returns true from
    /// <see cref="IServerWorldPlugin.BeforeHostMessage(HostMessageContext)"/>, the packet is treated
    /// as consumed and later plugins are not called for that packet.
    /// </remarks>
    internal sealed class ServerPluginManager(Action<string> log)
    {
        #region Fields

        /// <summary>
        /// Registered world plugins.
        /// </summary>
        private readonly List<IServerPlugin> _plugins = [];

        /// <summary>
        /// Guards plugin initialization/reload while packets are being inspected.
        /// </summary>
        private readonly object _pluginLock = new();

        /// <summary>
        /// Last initialization context. Stored so the console reload command can reload plugins from disk.
        /// </summary>
        private ServerPluginContext _lastContext;

        /// <summary>
        /// Prevents the plugin dependency resolver from being registered more than once.
        /// </summary>
        private static bool _pluginDependencyResolverInstalled;

        /// <summary>
        /// Server log callback.
        /// </summary>
        private readonly Action<string> _log = log ?? (_ => { });

        #endregion

        #region Registration

        /// <summary>
        /// Registers a world plugin with the manager.
        /// </summary>
        /// <param name="plugin">Plugin instance to register.</param>
        /// <remarks>
        /// Null plugins are ignored. Registered plugins are initialized later by
        /// <see cref="InitializeAll(ServerPluginContext)"/>.
        /// </remarks>
        public void Register(IServerPlugin plugin)
        {
            if (plugin == null)
                return;

            _plugins.Add(plugin);
            _log($"[Plugins] Registered {plugin.Name}.");
        }
        #endregion

        #region External Plugin Loading

        /// <summary>
        /// Loads external plugin DLLs from the server Plugins folder.
        /// </summary>
        /// <param name="pluginsRoot">
        /// Root plugin folder, usually:
        /// CMZDedicatedLidgrenServer\Plugins
        /// or
        /// CMZDedicatedSteamServer\Plugins.
        /// </param>
        /// <remarks>
        /// This loads plugin assemblies into the server process. Assemblies loaded into the default
        /// AppDomain cannot be unloaded on .NET Framework, so replacing plugin DLLs still requires
        /// a server restart.
        /// </remarks>
        public void LoadExternalPlugins(string pluginsRoot)
        {
            if (string.IsNullOrWhiteSpace(pluginsRoot))
                return;

            EnsurePluginDependencyResolverInstalled();

            Directory.CreateDirectory(pluginsRoot);

            string[] pluginDllPaths = [.. Directory
                .GetFiles(pluginsRoot, "*.dll", SearchOption.AllDirectories)
                .Where(path => !IsPluginDependencyPath(path))
                .Where(path => !IsServerPluginApiAssembly(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

            if (pluginDllPaths.Length == 0)
            {
                _log("[Plugins] No external plugin DLLs found.");
                return;
            }

            foreach (string pluginDllPath in pluginDllPaths)
            {
                try
                {
                    Assembly assembly = Assembly.LoadFrom(pluginDllPath);
                    RegisterPluginsFromAssembly(assembly, pluginDllPath);
                }
                catch (BadImageFormatException)
                {
                    _log($"[Plugins] Skipped non-.NET DLL: {pluginDllPath}");
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] Failed to load {pluginDllPath}: {ex.Message}.");
                }
            }
        }

        /// <summary>
        /// Registers all plugin classes found in a loaded assembly.
        /// </summary>
        /// <param name="assembly">Loaded plugin assembly.</param>
        /// <param name="sourcePath">Original DLL path used for logging.</param>
        private void RegisterPluginsFromAssembly(Assembly assembly, string sourcePath)
        {
            Type[] types = GetLoadableTypes(assembly);

            foreach (Type type in types)
            {
                if (type == null)
                    continue;

                if (!typeof(IServerPlugin).IsAssignableFrom(type))
                    continue;

                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    _log($"[Plugins] Skipped {type.FullName}: missing public parameterless constructor.");
                    continue;
                }

                try
                {
                    IServerPlugin plugin = (IServerPlugin)Activator.CreateInstance(type);
                    Register(plugin);

                    _log($"[Plugins] Loaded external plugin {plugin.Name} from {Path.GetFileName(sourcePath)}.");
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] Failed to create plugin {type.FullName}: {ex.Message}.");
                }
            }
        }

        /// <summary>
        /// Safely returns loadable types from an assembly.
        /// </summary>
        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return [.. ex.Types.Where(type => type != null)];
            }
        }

        /// <summary>
        /// Returns true when a DLL lives under a plugin Dependencies folder.
        /// </summary>
        private static bool IsPluginDependencyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Replace('/', '\\');

            return normalized.IndexOf("\\Dependencies\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true when the DLL is the shared server plugin API assembly.
        /// </summary>
        private static bool IsServerPluginApiAssembly(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);

            return fileName.Equals("CastleForge.ServerPluginAPI", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("CMZDedicatedServer.PluginAPI", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region Initialization / Reload

        /// <summary>
        /// Initializes all registered plugins using the supplied server plugin context.
        /// </summary>
        /// <param name="context">Initialization context shared with each plugin.</param>
        /// <remarks>
        /// Initialization failures are logged per plugin and do not prevent later plugins from initializing.
        /// </remarks>
        public void InitializeAll(ServerPluginContext context)
        {
            lock (_pluginLock)
            {
                _lastContext = context;

                foreach (IServerPlugin plugin in _plugins)
                {
                    try
                    {
                        plugin.Initialize(context);
                        _log($"[Plugins] Initialized {plugin.Name}.");
                    }
                    catch (Exception ex)
                    {
                        _log($"[Plugins] Failed to initialize {plugin.Name}: {ex.Message}.");
                    }
                }
            }
        }

        /// <summary>
        /// Reloads all registered plugins from their files using the last known plugin context.
        /// </summary>
        /// <remarks>
        /// This does not unload/reload plugin assemblies. It re-runs each plugin's Initialize method,
        /// which should reload config, regions, announcements, and other file-backed state.
        /// </remarks>
        public void ReloadAll()
        {
            lock (_pluginLock)
            {
                if (_lastContext == null)
                {
                    _log("[Plugins] Reload skipped: plugins have not been initialized yet.");
                    return;
                }

                _log("[Plugins] Reloading plugin files...");

                foreach (IServerPlugin plugin in _plugins)
                {
                    try
                    {
                        plugin.Initialize(_lastContext);
                        _log($"[Plugins] Reloaded {plugin.Name}.");
                    }
                    catch (Exception ex)
                    {
                        _log($"[Plugins] Failed to reload {plugin.Name}: {ex.Message}.");
                    }
                }

                _log("[Plugins] Reload complete.");
            }
        }
        #endregion

        #region Dependency Resolution

        /// <summary>
        /// Installs the plugin dependency resolver once per AppDomain.
        /// </summary>
        private static void EnsurePluginDependencyResolverInstalled()
        {
            if (_pluginDependencyResolverInstalled)
                return;

            _pluginDependencyResolverInstalled = true;

            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;
        }

        /// <summary>
        /// Resolves plugin helper DLLs from either the plugin root or its Dependencies folder.
        /// </summary>
        private static Assembly ResolvePluginDependency(object sender, ResolveEventArgs args)
        {
            AssemblyName requestedName = new(args.Name);

            Assembly alreadyLoaded = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                    string.Equals(
                        assembly.GetName().Name,
                        requestedName.Name,
                        StringComparison.OrdinalIgnoreCase));

            if (alreadyLoaded != null)
                return alreadyLoaded;

            Assembly requestingAssembly = args.RequestingAssembly;
            if (requestingAssembly == null || string.IsNullOrEmpty(requestingAssembly.Location))
                return null;

            string requestingDirectory = Path.GetDirectoryName(requestingAssembly.Location);
            if (string.IsNullOrEmpty(requestingDirectory))
                return null;

            string requestedFileName = requestedName.Name + ".dll";

            string dependencyPath = Path.Combine(requestingDirectory, "Dependencies", requestedFileName);
            if (File.Exists(dependencyPath))
                return Assembly.LoadFrom(dependencyPath);

            dependencyPath = Path.Combine(requestingDirectory, requestedFileName);
            if (File.Exists(dependencyPath))
                return Assembly.LoadFrom(dependencyPath);

            DirectoryInfo directory = new(requestingDirectory);

            if (directory.Name.Equals("Dependencies", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent != null)
            {
                dependencyPath = Path.Combine(directory.FullName, requestedFileName);
                if (File.Exists(dependencyPath))
                    return Assembly.LoadFrom(dependencyPath);

                dependencyPath = Path.Combine(directory.Parent.FullName, "Dependencies", requestedFileName);
                if (File.Exists(dependencyPath))
                    return Assembly.LoadFrom(dependencyPath);
            }

            return null;
        }
        #endregion

        #region Host Message Dispatch

        /// <summary>
        /// Gives each registered plugin a chance to inspect or consume a host/world message.
        /// </summary>
        /// <param name="context">Host message context supplied by the server world handler.</param>
        /// <returns>
        /// True when a plugin consumed the packet and normal server handling should stop.
        /// False when no plugin consumed the packet.
        /// </returns>
        /// <remarks>
        /// Plugin exceptions are logged and swallowed so one failing plugin does not crash the server
        /// or prevent later packets from being processed.
        /// </remarks>
        public bool BeforeHostMessage(HostMessageContext context)
        {
            foreach (IServerPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin is IServerWorldPlugin worldPlugin &&
                        worldPlugin.BeforeHostMessage(context))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] {plugin.Name} failed while handling {context.TypeName}: {ex.Message}.");
                }
            }

            return false;
        }
        #endregion

        #region Inbound Packet Dispatch

        /// <summary>
        /// Gives packet-level guard plugins a chance to consume an inbound packet before host handling or relay.
        /// </summary>
        public bool BeforeInboundPacket(ServerInboundPacketContext context)
        {
            foreach (IServerPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin is IServerInboundPacketPlugin packetPlugin &&
                        packetPlugin.BeforeInboundPacket(context))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] {plugin.Name} failed while inspecting inbound packet: {ex.Message}.");
                }
            }

            return false;
        }
        #endregion

        #region Player Events

        /// <summary>
        /// Notifies plugins that a player joined the server.
        /// </summary>
        public void NotifyPlayerJoined(ServerPlayerEventContext context)
        {
            foreach (IServerPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin is IServerPlayerEventPlugin playerEvents)
                        playerEvents.OnPlayerJoined(context);
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] {plugin.Name} failed during player join event: {ex.Message}.");
                }
            }
        }

        /// <summary>
        /// Notifies plugins that a player left the server.
        /// </summary>
        public void NotifyPlayerLeft(ServerPlayerEventContext context)
        {
            foreach (IServerPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin is IServerPlayerEventPlugin playerEvents)
                        playerEvents.OnPlayerLeft(context);
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] {plugin.Name} failed during player leave event: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Tick Events

        /// <summary>
        /// Runs optional per-tick plugin updates.
        /// </summary>
        public void UpdateAll(ServerPluginTickContext context)
        {
            foreach (IServerPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin is IServerTickPlugin tickPlugin)
                        tickPlugin.Update(context);
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] {plugin.Name} failed during update: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Shutdown Events

        /// <summary>
        /// Notifies optional shutdown-aware plugins that the server is stopping.
        /// </summary>
        public void NotifyServerStopping(ServerPluginShutdownContext context)
        {
            foreach (IServerPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin is IServerShutdownPlugin shutdownPlugin)
                        shutdownPlugin.OnServerStopping(context);
                }
                catch (Exception ex)
                {
                    _log($"[Plugins] {plugin.Name} failed during server shutdown: {ex.Message}.");
                }
            }
        }
        #endregion
    }
}