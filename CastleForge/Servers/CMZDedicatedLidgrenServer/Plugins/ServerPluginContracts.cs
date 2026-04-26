/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System;

namespace CMZDedicatedLidgrenServer.Plugins
{
    #region Plugin Contracts

    /// <summary>
    /// Defines a server-side world plugin that can inspect and optionally consume host/world messages.
    /// </summary>
    /// <remarks>
    /// Plugins are called before selected world mutation packets are applied or relayed by the server.
    /// Returning true from <see cref="BeforeHostMessage(HostMessageContext)"/> means the plugin consumed
    /// the packet and normal server handling should stop.
    /// </remarks>
    internal interface IServerWorldPlugin
    {
        /// <summary>
        /// Display name used by the plugin manager for logging.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Initializes the plugin with server-provided paths, world identity, and logging services.
        /// </summary>
        /// <param name="context">Plugin initialization context supplied by the dedicated server.</param>
        void Initialize(ServerPluginContext context);

        /// <summary>
        /// Inspects a host/world message before normal server handling.
        /// </summary>
        /// <param name="context">Context for the message being processed.</param>
        /// <returns>
        /// True to consume/block the packet.
        /// False to allow normal server handling and relay.
        /// </returns>
        bool BeforeHostMessage(HostMessageContext context);
    }
    #endregion

    #region Optional Plugin Events

    /// <summary>
    /// Optional plugin interface for player join/leave notifications.
    /// </summary>
    internal interface IServerPlayerEventPlugin
    {
        /// <summary>
        /// Called after a player has joined and the server can send messages to them.
        /// </summary>
        void OnPlayerJoined(ServerPlayerEventContext context);

        /// <summary>
        /// Called after a player has disconnected.
        /// </summary>
        void OnPlayerLeft(ServerPlayerEventContext context);
    }

    /// <summary>
    /// Optional plugin interface for periodic server updates.
    /// </summary>
    internal interface IServerTickPlugin
    {
        /// <summary>
        /// Called once per server update loop.
        /// </summary>
        void Update(ServerPluginTickContext context);
    }
    #endregion

    #region Player / Tick Contexts

    /// <summary>
    /// Context supplied to plugins when a player joins or leaves.
    /// </summary>
    internal sealed class ServerPlayerEventContext
    {
        public byte PlayerId { get; set; }

        public string PlayerName { get; set; }

        public int ConnectedPlayers { get; set; }

        public int MaxPlayers { get; set; }

        public Action<string> SendPrivateMessage { get; set; }

        public Action<string> BroadcastMessage { get; set; }

        public Action<string> Log { get; set; }
    }

    /// <summary>
    /// Context supplied to plugins during the server update loop.
    /// </summary>
    internal sealed class ServerPluginTickContext
    {
        public DateTime UtcNow { get; set; }

        public int ConnectedPlayers { get; set; }

        public int MaxPlayers { get; set; }

        public Action<string> BroadcastMessage { get; set; }

        public Action<string> Log { get; set; }
    }
    #endregion

    #region Plugin Initialization Context

    /// <summary>
    /// Provides server-level initialization data to plugins.
    /// </summary>
    /// <remarks>
    /// This context is passed once during plugin startup. It should contain stable information
    /// such as the server executable directory and the active world key used for per-world config files.
    /// </remarks>
    internal sealed class ServerPluginContext
    {
        /// <summary>
        /// Base server directory used as the root for plugin config/storage folders.
        /// </summary>
        public string BaseDir { get; set; }

        /// <summary>
        /// Stable world identifier used by plugins for per-world config folders.
        /// </summary>
        /// <remarks>
        /// Even though this is currently named WorldGuid, callers may pass a cleaned world folder key.
        /// </remarks>
        public string WorldGuid { get; set; }

        /// <summary>
        /// Server log callback.
        /// </summary>
        public Action<string> Log { get; set; }
    }
    #endregion

    #region Delegates

    /// <summary>
    /// Attempts to read integer X/Y/Z components from a reflected IntVector3-like object.
    /// </summary>
    /// <param name="value">Reflected vector object.</param>
    /// <param name="x">Output X coordinate.</param>
    /// <param name="y">Output Y coordinate.</param>
    /// <param name="z">Output Z coordinate.</param>
    /// <returns>True when the vector was read successfully; otherwise false.</returns>
    internal delegate bool TryReadIntVector3Delegate(object value, out int x, out int y, out int z);

    /// <summary>
    /// Attempts to read the server's currently saved block type at a world block position.
    /// </summary>
    /// <remarks>
    /// This usually reads from saved chunk delta data. It may return false for natural/procedural
    /// terrain that has not been written into a chunk delta yet.
    /// </remarks>
    internal delegate bool TryGetSavedBlockTypeDelegate(
        int x,
        int y,
        int z,
        out int blockTypeValue);

    /// <summary>
    /// Attempts to read the best known original block type for a denied terrain edit.
    /// </summary>
    /// <remarks>
    /// This can use saved delta data or a recent DigMessage cache to restore denied mining visuals.
    /// </remarks>
    internal delegate bool TryGetOriginalBlockTypeDelegate(
        byte senderId,
        int x,
        int y,
        int z,
        out int blockTypeValue);

    #endregion

    #region Host Message Context

    /// <summary>
    /// Provides plugins with information and safe server callbacks for a host/world message.
    /// </summary>
    /// <remarks>
    /// This object is created by the server world handler for each plugin-intercepted message.
    /// Plugins should use the provided callbacks instead of directly touching server internals.
    /// </remarks>
    internal sealed class HostMessageContext
    {
        #region Message Identity

        /// <summary>
        /// Full reflected network message type name.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Server-side sender/player id.
        /// </summary>
        public byte SenderId { get; set; }

        /// <summary>
        /// Best known display name for the sending player.
        /// </summary>
        public string SenderName { get; set; }

        /// <summary>
        /// Raw packet payload, including message id and checksum.
        /// </summary>
        public byte[] Payload { get; set; }

        #endregion

        #region Reflection Helpers

        /// <summary>
        /// Deserializes a raw packet into the original reflected CastleMiner Z message object.
        /// </summary>
        public Func<string, byte[], object> DeserializeGameMessage { get; set; }

        /// <summary>
        /// Reads a reflected field or property value from an object by name.
        /// </summary>
        public Func<object, string, object> GetMemberValue { get; set; }

        /// <summary>
        /// Reads integer X/Y/Z coordinates from a reflected IntVector3-like object.
        /// </summary>
        public TryReadIntVector3Delegate TryReadIntVector3 { get; set; }

        #endregion

        #region Correction / Resync Callbacks

        /// <summary>
        /// Requests that the server send the authoritative chunk back to the offending client.
        /// </summary>
        public Action<int, int, int> ResyncChunkForBlock { get; set; }

        /// <summary>
        /// Sends a private warning to the denied player.
        /// </summary>
        public Action<string> SendWarningToPlayer { get; set; }

        /// <summary>
        /// Sends a single corrective AlterBlockMessage to the denied player.
        /// </summary>
        public Action<int, int, int, int> SendAlterBlockToPlayer { get; set; }

        #endregion

        #region Block Lookup Callbacks

        /// <summary>
        /// Reads the current saved block type from the server-side world delta data.
        /// </summary>
        public TryGetSavedBlockTypeDelegate TryGetSavedBlockType { get; set; }

        /// <summary>
        /// Attempts to read the original block type that should be restored for a denied edit.
        /// This can use saved delta data or a recent DigMessage cache.
        /// </summary>
        public TryGetOriginalBlockTypeDelegate TryGetOriginalBlockType { get; set; }

        #endregion

        #region Logging

        /// <summary>
        /// Server log callback.
        /// </summary>
        public Action<string> Log { get; set; }

        #endregion
    }
    #endregion
}