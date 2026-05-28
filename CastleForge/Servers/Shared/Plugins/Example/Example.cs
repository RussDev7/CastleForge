// SPDX-License-Identifier: GPL-3.0-or-later
//
// CastleForge Server Plugin Example
// Example plugin for CastleForge dedicated servers.
//
// This file demonstrates the basic shape of a shared server plugin that can be
// loaded by both CMZDedicatedSteamServer and CMZDedicatedLidgrenServer.

using CMZDedicatedServer.Plugins;

namespace CastleForge.ServerPlugins.Example
{
    /// <summary>
    /// Example CastleForge dedicated server plugin.
    /// </summary>
    /// <remarks>
    /// This plugin demonstrates how to:
    ///  • Register a plugin name for logging and diagnostics.
    ///  • Initialize using the shared server plugin context.
    ///  • Inspect inbound packets before normal server handling.
    ///  • React to player join and leave events.
    ///
    /// Returning false from <see cref="BeforeInboundPacket"/> allows the
    /// server to continue processing the packet normally. Returning true
    /// consumes/blocks the packet.
    /// </remarks>
    public sealed class ServerExamplePlugin :
        IServerPlugin,
        IServerInboundPacketPlugin,
        IServerPlayerEventPlugin
    {
        #region Properties

        /// <summary>
        /// Gets the display name used by the plugin manager for logs and diagnostics.
        /// </summary>
        public string Name
        {
            get { return "Example"; }
        }
        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the plugin after it has been registered by the server.
        /// </summary>
        /// <param name="context">
        /// Server-provided plugin context containing paths, world identity, logging,
        /// and optional host callbacks.
        /// </param>
        public void Initialize(ServerPluginContext context)
        {
            context.Log?.Invoke("[Example] Initialized.");
        }
        #endregion

        #region Packet Hooks

        /// <summary>
        /// Called before the dedicated server processes an inbound packet from a client.
        /// </summary>
        /// <param name="context">
        /// Context describing the inbound packet, sender identity, packet size, and
        /// any transport-neutral metadata exposed by the host.
        /// </param>
        /// <returns>
        /// true to consume/block the packet; otherwise, false to let the
        /// server continue normal processing.
        /// </returns>
        public bool BeforeInboundPacket(ServerInboundPacketContext context)
        {
            // Example:
            // Inspect context data here and return true if the packet should be blocked.
            //
            // This sample does not block anything.

            return false;
        }
        #endregion

        #region Player Events

        /// <summary>
        /// Called after a player joins the dedicated server.
        /// </summary>
        /// <param name="context">
        /// Context containing player identity, player count, messaging callbacks,
        /// and server logging.
        /// </param>
        public void OnPlayerJoined(ServerPlayerEventContext context)
        {
            context.Log?.Invoke($"[Example] Player joined: {context.PlayerName}");

            // Optional example:
            // context.SendPrivateMessage?.Invoke("Welcome to this CastleForge dedicated server!");
        }

        /// <summary>
        /// Called after a player leaves the dedicated server.
        /// </summary>
        /// <param name="context">
        /// Context containing the departing player's identity and server logging.
        /// </param>
        public void OnPlayerLeft(ServerPlayerEventContext context)
        {
            context.Log?.Invoke($"[Example] Player left: {context.PlayerName}");
        }
        #endregion
    }
}