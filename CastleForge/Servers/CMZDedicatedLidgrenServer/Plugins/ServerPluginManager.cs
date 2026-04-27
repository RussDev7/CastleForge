/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System;

namespace CMZDedicatedLidgrenServer.Plugins
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
        private readonly List<IServerWorldPlugin> _plugins = [];

        /// <summary>
        /// Guards plugin initialization/reload while packets are being inspected.
        /// </summary>
        private readonly object _pluginLock = new();

        /// <summary>
        /// Last initialization context. Stored so the console reload command can reload plugins from disk.
        /// </summary>
        private ServerPluginContext _lastContext;

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
        public void Register(IServerWorldPlugin plugin)
        {
            if (plugin == null)
                return;

            _plugins.Add(plugin);
            _log($"[Plugins] Registered {plugin.Name}.");
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

                foreach (IServerWorldPlugin plugin in _plugins)
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

                foreach (IServerWorldPlugin plugin in _plugins)
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
            foreach (IServerWorldPlugin plugin in _plugins)
            {
                try
                {
                    if (plugin.BeforeHostMessage(context))
                        return true;
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
            foreach (IServerWorldPlugin plugin in _plugins)
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
            foreach (IServerWorldPlugin plugin in _plugins)
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
            foreach (IServerWorldPlugin plugin in _plugins)
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
            foreach (IServerWorldPlugin plugin in _plugins)
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
            foreach (IServerWorldPlugin plugin in _plugins)
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