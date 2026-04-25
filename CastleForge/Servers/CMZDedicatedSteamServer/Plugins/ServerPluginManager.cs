/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
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
        private readonly List<IServerWorldPlugin> _plugins = [];

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

        #region Initialization

        /// <summary>
        /// Initializes all registered plugins using the supplied server plugin context.
        /// </summary>
        /// <param name="context">Initialization context shared with each plugin.</param>
        /// <remarks>
        /// Initialization failures are logged per plugin and do not prevent later plugins from initializing.
        /// </remarks>
        public void InitializeAll(ServerPluginContext context)
        {
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
    }
}