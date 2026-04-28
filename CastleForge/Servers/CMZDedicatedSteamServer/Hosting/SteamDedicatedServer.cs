/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Plugins.Announcements;
using CMZDedicatedSteamServer.Plugins.RegionProtect;
using CMZDedicatedSteamServer.Plugins.RememberTime;
using CMZDedicatedSteamServer.Plugins.FloodGuard;
using CMZDedicatedSteamServer.Plugins;
using CMZDedicatedSteamServer.Common;
using CMZDedicatedSteamServer.Config;
using CMZDedicatedSteamServer.Steam;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Hosting
{
    /// <summary>
    /// Steam-native dedicated host bootstrap / runtime shell.
    ///
    /// Purpose:
    /// - Resolves the Steam game/runtime assemblies from the configured game path.
    /// - Initializes the existing DNA.Steam SteamWorks wrapper.
    /// - Creates/owns the Steam lobby as the active logged-in Steam account.
    /// - Pumps Steam packet traffic and applies host-side join approval.
    /// - Bridges channel-0 / channel-1 traffic into the dedicated host world pipeline.
    ///
    /// Notes:
    /// - This intentionally does NOT launch CastleMiner Z.
    /// - Steam must already be running for the same Windows user.
    /// - Username/password login from config is not supported by the normal Steam client API path.
    /// </summary>
    internal sealed class SteamDedicatedServer(string baseDir, SteamServerConfig config, Action<string> log) : IDisposable
    {
        #region Fields

        #region Constructor-provided state

        /// <summary>
        /// Server executable/base directory used for config, plugin storage, and relative path resolution.
        /// </summary>
        private readonly string _baseDir = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Loaded Steam dedicated server configuration.
        /// </summary>
        private SteamServerConfig _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>
        /// Configured in-game server message/template shown in Steam session info or sent privately on join.
        /// </summary>
        private string _serverMessage = string.IsNullOrWhiteSpace(config?.ServerMessage)
            ? "Welcome to this CastleForge dedicated server."
            : config.ServerMessage;

        /// <summary>
        /// Logging callback supplied by the host process.
        /// </summary>
        private readonly Action<string> _log = log ?? (_ => { });

        #endregion

        #region Runtime services

        /// <summary>
        /// True while the server update loop should continue processing Steam events and packets.
        /// </summary>
        private bool _running;

        /// <summary>
        /// Resolves and loads CastleMiner Z, DNA.Common, and Steam runtime assemblies.
        /// </summary>
        private ServerAssemblyLoader _assemblyLoader;

        /// <summary>
        /// Steam bootstrap wrapper responsible for initializing Steam and reading packets.
        /// </summary>
        private SteamServerBootstrap _steam;

        /// <summary>
        /// Validates incoming connection approval requests.
        /// </summary>
        private SteamConnectionApproval _approval;

        /// <summary>
        /// Tracks connected Steam peers and their allocated CastleMiner Z gamer ids.
        /// </summary>
        private SteamPeerRegistry _peers;

        /// <summary>
        /// Owns lobby creation and lobby metadata publishing.
        /// </summary>
        private SteamLobbyHost _lobbyHost;

        /// <summary>
        /// Reflected host/server gamer object sent in connection responses.
        /// </summary>
        private object _hostGamer;

        /// <summary>
        /// Host-side world handler for recipient id 0 packets.
        /// </summary>
        private ServerWorldHandler _worldHandler;

        /// <summary>
        /// Message id/type lookup helper used for logging and selected packet detection.
        /// </summary>
        private CmzMessageCodec _codec;

        /// <summary>
        /// Optional server plugin manager used by the world handler.
        /// </summary>
        private ServerPluginManager _plugins;

        /// <summary>
        /// Persistent SteamID / name ban store for server-side player enforcement.
        /// </summary>
        private ServerBanStore _banStore;

        /// <summary>
        /// SteamIDs that were force-dropped and should have late packets ignored.
        /// Cleared when a fresh connection approval is accepted.
        /// </summary>
        private readonly HashSet<ulong> _forceDroppedSteamIds = [];

        #endregion

        #region Peer / packet state

        /// <summary>
        /// Maps Steam connection objects/ids to reflected NetworkGamer objects.
        /// </summary>
        private readonly Dictionary<object, object> _connectionToGamer = [];

        /// <summary>
        /// Cached PlayerExistsMessage payloads keyed by player id for replaying to late joiners.
        /// </summary>
        private readonly Dictionary<byte, byte[]> _playerExistsPayloadById = [];

        /// <summary>
        /// Cached message id for DNA.CastleMinerZ.Net.PlayerExistsMessage.
        /// </summary>
        private byte? _playerExistsMessageId;

        #endregion

        #region Time-of-day state

        /// <summary>
        /// Authoritative server day value. The fractional part controls visual time; the integer part controls day number.
        /// </summary>
        private float _timeOfDay = 0.41f;

        /// <summary>
        /// Last time a TimeOfDayMessage was broadcast to connected clients.
        /// </summary>
        private DateTime _lastTimeOfDaySend = DateTime.MinValue;

        /// <summary>
        /// Last wall-clock timestamp used to advance the authoritative day value.
        /// </summary>
        private DateTime _lastTimeOfDayAdvance = DateTime.UtcNow;

        /// <summary>
        /// Number of real seconds per full CastleMiner Z day cycle.
        /// </summary>
        private const float SecondsPerFullDay = 960f;

        /// <summary>
        /// Last player-facing day number published into the Steam lobby name.
        /// Used so the lobby name only refreshes when the displayed day changes.
        /// </summary>
        private int _lastPublishedLobbyDay = -1;

        /// <summary>
        /// Last resolved Steam lobby display name.
        /// Used to avoid spamming Steam lobby metadata updates.
        /// </summary>
        private string _lastPublishedLobbyName = string.Empty;

        /// <summary>
        /// Tracks the last resolved Steam lobby message that was published.
        /// Used to avoid refreshing Steam lobby metadata when the message has not changed.
        /// </summary>
        private string _lastPublishedLobbyMessage = string.Empty;

        #endregion

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether the server is currently running.
        /// </summary>
        public bool IsRunning => _running;

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts the Steam dedicated server, initializes Steam/lobby state, loads assemblies, and initializes the world handler.
        /// </summary>
        public void Start()
        {
            string gamePath = string.IsNullOrWhiteSpace(_config.GamePath)
                ? Path.Combine(_baseDir, "Game")
                : (Path.IsPathRooted(_config.GamePath)
                    ? _config.GamePath
                    : Path.GetFullPath(Path.Combine(_baseDir, _config.GamePath)));

            _log("CMZ Dedicated Steam Server");
            _log("--------------------------");
            _log($"GameName       : {_config.GameName}");
            _log($"NetworkVersion : {_config.NetworkVersion}");
            _log($"GamePath       : {gamePath}");
            _log($"ServerName     : {_config.ServerName}");
            _log($"ServerMessage  : {_config.ServerMessage}");
            _log($"MaxPlayers     : {_config.MaxPlayers}");
            _log($"WorldGuid      : {_config.WorldGuid}");
            _log($"SteamAppId     : {_config.SteamAppId}");

            _assemblyLoader = new ServerAssemblyLoader(gamePath);
            _assemblyLoader.Load();

            _steam = new SteamServerBootstrap(_baseDir, gamePath, _config, _assemblyLoader.SteamAssembly, _log);
            _steam.Initialize();

            if (_config.SteamAccountRequired && _steam.SteamPlayerId == 0UL)
                throw new InvalidOperationException("Steam account was required, but the active SteamPlayerID was 0.");

            _hostGamer = CreateHostGamer();
            _approval = new SteamConnectionApproval(_config, _assemblyLoader.CommonAssembly, _log);
            _peers = new SteamPeerRegistry();
            _banStore = new ServerBanStore(_baseDir, _log);
            _lobbyHost = new SteamLobbyHost(
                _config,
                _assemblyLoader.CommonAssembly,
                _assemblyLoader.SteamAssembly,
                _steam.SteamWorksInstance,
                _log);

            _lobbyHost.BeginCreateLobby(BuildServerDisplayName(), BuildServerDisplayMessage());
            _lastPublishedLobbyMessage = BuildServerDisplayMessage();
            _lastPublishedLobbyName = BuildServerDisplayName();
            _lastPublishedLobbyDay = GetDisplayDay();

            ulong effectiveSaveOwnerSteamId = _config.SaveOwnerSteamId != 0UL ? _config.SaveOwnerSteamId : _steam.SteamPlayerId;
            if (!string.IsNullOrWhiteSpace(_config.WorldFolder) && effectiveSaveOwnerSteamId != 0UL)
            {
                _plugins = new ServerPluginManager(_log);
                _plugins.Register(new ServerFloodGuardPlugin());
                _plugins.Register(new ServerRegionProtectPlugin());
                _plugins.Register(new ServerAnnouncementsPlugin());
                _plugins.Register(new ServerRememberTimePlugin());

                _plugins.InitializeAll(new ServerPluginContext
                {
                    BaseDir = _baseDir,
                    WorldGuid = _config.WorldGuid,
                    Log = _log,
                    GetTimeOfDay = () => _timeOfDay,

                    SetTimeOfDay = value =>
                    {
                        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                            return;

                        _timeOfDay = value;

                        // Prevent the next tick from adding a large elapsed-time jump after restore.
                        _lastTimeOfDayAdvance = DateTime.UtcNow;

                        // Force connected clients to receive restored time quickly.
                        _lastTimeOfDaySend = DateTime.MinValue;

                        // Force lobby metadata to refresh with the restored day.
                        _lastPublishedLobbyDay = -1;
                    },
                });

                _worldHandler = new ServerWorldHandler(
                    gamePath,
                    _config.WorldFolder,
                    _baseDir,
                    effectiveSaveOwnerSteamId,
                    _log,
                    _config.ViewDistanceChunks,
                    _plugins,
                    _config.LogHostMessages,
                    _config.GameMode,
                    () => _timeOfDay);

                _worldHandler.Init(_assemblyLoader.GameAssembly, _assemblyLoader.CommonAssembly);
                _worldHandler.ApplyServerMessage(BuildServerDisplayMessage(), saveToDisk: false);

                _codec = new CmzMessageCodec(_assemblyLoader.GameAssembly, _assemblyLoader.CommonAssembly, _log);
                _log($"[World] World handler initialized. WorldFolder={_config.WorldFolder}, SaveOwner={effectiveSaveOwnerSteamId}");
            }
            else
            {
                _log("[World] World handler not initialized. Make sure world-guid is set and Steam resolved a valid host account.");
            }

            _running = true;
        }

        /// <summary>
        /// Stops the server update loop.
        /// </summary>
        public void Stop()
        {
            _plugins?.NotifyServerStopping(new ServerPluginShutdownContext
            {
                UtcNow = DateTime.UtcNow,
                TimeOfDay = _timeOfDay,
                Log = _log
            });

            _running = false;
        }

        /// <summary>
        /// Reloads server plugin files without restarting the dedicated server.
        /// </summary>
        /// <remarks>
        /// This reloads file-backed plugin state such as RegionProtect regions/config
        /// and Announcements config. It does not reload external plugin assemblies.
        /// </remarks>
        public void ReloadPlugins()
        {
            if (_plugins == null)
            {
                _log("[Plugins] Reload skipped: plugin manager is not initialized.");
                return;
            }

            _plugins.ReloadAll();
        }

        /// <summary>
        /// Reloads server.properties runtime-safe values and plugin-backed files.
        /// </summary>
        public void Reload()
        {
            ReloadServerProperties();
            ReloadPlugins();
        }

        /// <summary>
        /// Reloads runtime-safe values from server.properties.
        /// 
        /// Notes:
        /// - This does not recreate the Steam lobby, Steam bootstrap, loaded assemblies, or world handler.
        /// - Startup/bootstrap values still require a restart.
        /// </summary>
        public void ReloadServerProperties()
        {
            SteamServerConfig newConfig;

            try
            {
                newConfig = SteamServerConfig.Load(_baseDir);
            }
            catch (Exception ex)
            {
                _log("[Config] Reload failed: " + ex.Message);
                return;
            }

            _config = newConfig;

            _serverMessage = string.IsNullOrWhiteSpace(newConfig.ServerMessage)
                ? "Welcome to this CastleForge dedicated server."
                : newConfig.ServerMessage;

            _approval?.ReloadConfig(newConfig);

            string configPath = Path.Combine(_baseDir, "server.properties");
            string resolvedMessage = BuildServerDisplayMessage();

            _log($"[Config] Reloaded from: {configPath}");
            _log($"[Config] server-message raw: '{_serverMessage}'");
            _log($"[Config] server-message resolved: '{resolvedMessage}'");

            _worldHandler?.ApplyServerMessage(resolvedMessage, saveToDisk: false);

            PublishCurrentLobbyMetadata();

            _log("[Config] Reloaded runtime properties from server.properties.");
            _log("[Config] Note: Steam app id/game path/world/save-owner/port require restart.");
        }

        /// <summary>
        /// Immediately republishes the current lobby name/message after a config reload.
        /// </summary>
        private void PublishCurrentLobbyMetadata()
        {
            if (_lobbyHost == null || !_lobbyHost.IsLobbyReady)
                return;

            string currentName = BuildServerDisplayName();
            string currentMessage = BuildServerDisplayMessage();

            _lobbyHost.SetLobbyName(currentName);
            _lobbyHost.SetLobbyMessage(currentMessage);
            _lobbyHost.RefreshLobbyMetadata();

            _lastPublishedLobbyDay = GetDisplayDay();
            _lastPublishedLobbyName = currentName;
            _lastPublishedLobbyMessage = currentMessage;

            _log($"[SteamLobby] Republished lobby metadata: {currentName} | {currentMessage}");
        }

        /// <summary>
        /// Pumps the server runtime once: advances time, broadcasts time updates, polls Steam, and processes packets.
        /// </summary>
        public void Update()
        {
            if (!_running)
                return;

            AdvanceTimeOfDay();
            BroadcastTimeOfDayIfNeeded();
            RefreshLobbyMetadataIfNeeded();

            // Run optional tick-based plugins, such as Announcements.
            UpdateServerPlugins();

            bool anyPackets = _steam.Update();
            if (!anyPackets)
                return;

            object packet;
            while ((packet = _steam.GetPacketOrNull()) != null)
            {
                try
                {
                    ProcessPacket(packet);
                }
                finally
                {
                    _steam.FreePacket(packet);
                }
            }
        }
        #endregion

        #region Server Plugin Events

        /// <summary>
        /// Updates optional tick-based server plugins such as Announcements.
        /// </summary>
        private void UpdateServerPlugins()
        {
            if (_plugins == null)
                return;

            _plugins.UpdateAll(new ServerPluginTickContext
            {
                UtcNow = DateTime.UtcNow,
                ConnectedPlayers = _peers.GetConnectedPeersSnapshot().Count,
                MaxPlayers = _config.MaxPlayers,
                TimeOfDay = _timeOfDay,
                BroadcastMessage = BroadcastServerText,
                Log = _log
            });
        }

        /// <summary>
        /// Notifies optional player-event plugins that a Steam player joined.
        /// </summary>
        private void NotifyPluginsPlayerJoined(ulong steamId, byte playerId, string playerName)
        {
            if (_plugins == null)
                return;

            _plugins.NotifyPlayerJoined(new ServerPlayerEventContext
            {
                PlayerId = playerId,
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" + playerId : playerName,
                ConnectedPlayers = _peers.GetConnectedPeersSnapshot().Count,
                MaxPlayers = _config.MaxPlayers,
                SendPrivateMessage = message => SendPrivateServerTextToSteam(steamId, playerId, message),
                BroadcastMessage = BroadcastServerText,
                Log = _log
            });
        }

        /// <summary>
        /// Notifies optional player-event plugins that a Steam player left.
        /// </summary>
        private void NotifyPluginsPlayerLeft(byte playerId, string playerName)
        {
            if (_plugins == null)
                return;

            _plugins.NotifyPlayerLeft(new ServerPlayerEventContext
            {
                PlayerId = playerId,
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" + playerId : playerName,
                ConnectedPlayers = _peers.GetConnectedPeersSnapshot().Count,
                MaxPlayers = _config.MaxPlayers,
                BroadcastMessage = BroadcastServerText,
                Log = _log
            });
        }
        #endregion

        #region Steam Packet Dispatch

        /// <summary>
        /// Dispatches a raw Steam packet to the appropriate high-level handler based on MessageType.
        /// </summary>
        private void ProcessPacket(object packet)
        {
            object messageType = ReflectEx.GetRequiredMemberValue(packet, "MessageType");
            string messageTypeName = messageType.ToString();
            ulong senderSteamId = Convert.ToUInt64(ReflectEx.GetRequiredMemberValue(packet, "SenderId"));

            if (messageTypeName == "Data" && _forceDroppedSteamIds.Contains(senderSteamId))
            {
                // The peer was already removed from the authoritative server state.
                // Ignore late packets so a modified client cannot keep interacting.
                return;
            }

            switch (messageTypeName)
            {
                case "ConnectionApproval":
                    HandleConnectionApproval(packet, senderSteamId);
                    return;

                case "StatusChanged":
                    HandleStatusChanged(packet, senderSteamId);
                    return;

                case "Data":
                    HandleData(packet, senderSteamId);
                    return;
            }
        }

        /// <summary>
        /// Validates and accepts/denies an incoming Steam connection approval request.
        /// </summary>
        private void HandleConnectionApproval(object packet, ulong senderSteamId)
        {
            ApprovalDecision decision = _approval.ValidateRequest(packet, senderSteamId);

            if (!decision.Allowed)
            {
                _steam.DenyConnection(senderSteamId, decision.ResultCode);
                _log($"[ConnectionApproval] Denied {senderSteamId} -> {decision.ResultCode}");
                return;
            }

            if (_banStore != null &&
                _banStore.IsBanned(senderSteamId, null, decision.Gamertag, out ServerBanRecord ban))
            {
                string reason = string.IsNullOrWhiteSpace(ban.Reason)
                    ? "Banned from this server."
                    : ban.Reason;

                _steam.DenyConnection(senderSteamId, reason);
                _log($"[PlayerEnforcement] Denied banned Steam peer {decision.Gamertag} ({senderSteamId}). Reason={reason}");
                return;
            }

            // Allow a kicked player to reconnect unless they were actually banned.
            _forceDroppedSteamIds.Remove(senderSteamId);

            _steam.AcceptConnection(senderSteamId);
            _log($"[ConnectionApproval] Accepted {decision.Gamertag} ({senderSteamId}).");
        }

        /// <summary>
        /// Handles Steam connection status changes and updates peer/gamer tracking.
        /// </summary>
        private void HandleStatusChanged(object packet, ulong senderSteamId)
        {
            byte statusCode = Convert.ToByte(ReflectEx.GetRequiredMethod(packet.GetType(), "ReadByte", Type.EmptyTypes).Invoke(packet, null));

            switch (statusCode)
            {
                case 5:
                {
                    if (_peers.TryGetConnectedPeer(senderSteamId, out ConnectedSteamPeer existingPeer))
                    {
                        _log($"[StatusChanged] Duplicate connected event ignored. SteamID={senderSteamId}, GID={existingPeer.Gid}");
                        return;
                    }

                    if (!_approval.TryGetPending(senderSteamId, out PendingApprovalInfo pending))
                    {
                        _log($"[StatusChanged] Connected event without a pending approval. SteamID={senderSteamId}");
                        return;
                    }

                    byte gid = _peers.AllocateGid(senderSteamId);
                    object remoteGamer = CreateConnectedRemoteGamer(pending.Gamer, gid, senderSteamId);

                    SendConnectedResponse(senderSteamId, gid);

                    ConnectedSteamPeer newPeer = _peers.AddConnectedPeer(senderSteamId, gid, pending.Gamertag, remoteGamer);
                    _connectionToGamer[senderSteamId] = remoteGamer;
                    _approval.MarkConnected(senderSteamId, pending.Gamertag, remoteGamer);
                    _worldHandler?.ApplyServerMessage(BuildServerDisplayMessage(), saveToDisk: false);
                    PublishCurrentLobbyMetadata();
                    BroadcastNewPeerToOthers(newPeer);
                    SendInitialTimeOfDayToJoiner(senderSteamId, gid);
                    NotifyPluginsPlayerJoined(senderSteamId, gid, pending.Gamertag);

                    _log($"[StatusChanged] Peer connected. SteamID={senderSteamId}, GID={gid}");
                    _log($"[Handshake] ResponseToConnection sent to {pending.Gamertag} ({senderSteamId}) with GID {gid}.");
                    return;
                }

                case 7:
                {
                    if (_peers.RemoveBySteamId(senderSteamId, out ConnectedSteamPeer peer))
                    {
                        // Re-add temporarily so RemoveSteamPeerFromServer can run safely if you want one shared path,
                        // or just keep your existing block. The important part is to avoid double notifications.
                        _approval.RemovePeer(senderSteamId);
                        _connectionToGamer.Remove(senderSteamId);
                        _playerExistsPayloadById.Remove(peer.Gid);

                        _worldHandler?.OnClientDisconnected(peer.Gid);
                        NotifyPluginsPlayerLeft(peer.Gid, peer.Gamertag);
                        BroadcastDropPeerToOthers(peer);

                        _worldHandler?.ApplyServerMessage(BuildServerDisplayMessage(), saveToDisk: false);
                        PublishCurrentLobbyMetadata();

                        _log($"[StatusChanged] Peer disconnected. SteamID={senderSteamId}, GID={peer.Gid}");
                        return;
                    }

                    _approval.RemovePeer(senderSteamId);
                    _connectionToGamer.Remove(senderSteamId);
                    _log($"[StatusChanged] Pending/untracked peer disconnected. SteamID={senderSteamId}");
                    return;
                }
            }
        }

        /// <summary>
        /// Routes incoming Steam data packets by CastleMiner Z channel.
        /// </summary>
        private void HandleData(object packet, ulong senderSteamId)
        {
            int channel = Convert.ToInt32(ReflectEx.GetRequiredMemberValue(packet, "Channel"));

            if (channel == 1)
            {
                HandleChannel1Data(packet, senderSteamId);
                return;
            }

            HandleChannel0Data(packet, senderSteamId);
        }

        /// <summary>
        /// Handles channel-1 packets, including peer-directed channel-0 forwarding and broadcast-style payloads.
        /// </summary>
        private void HandleChannel1Data(object packet, ulong senderSteamId)
        {
            MethodInfo readByte = ReflectEx.GetRequiredMethod(packet.GetType(), "ReadByte", Type.EmptyTypes);
            MethodInfo readInt32 = ReflectEx.GetRequiredMethod(packet.GetType(), "ReadInt32", Type.EmptyTypes);
            MethodInfo readBytes = ReflectEx.GetRequiredMethod(packet.GetType(), "ReadBytes", typeof(int));
            Type deliveryType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetDeliveryMethod");

            byte opcode = Convert.ToByte(readByte.Invoke(packet, null));

            if (opcode == 3)
            {
                byte recipientId = Convert.ToByte(readByte.Invoke(packet, null));
                object delivery = Enum.ToObject(deliveryType, Convert.ToByte(readByte.Invoke(packet, null)));
                byte senderId = Convert.ToByte(readByte.Invoke(packet, null));

                int dataSize = Convert.ToInt32(readInt32.Invoke(packet, null));
                byte[] payloadBytes = dataSize > 0 ? (byte[])readBytes.Invoke(packet, [dataSize]) : [];
                if (payloadBytes == null)
                    return;

                if (ShouldDropInboundPacket(senderId, recipientId, 1, 3, payloadBytes, senderSteamId))
                    return;

                if (!TryGetSteamIdForPlayer(recipientId, out ulong recipientSteamId))
                    return;

                SendChannel0PayloadToSteam(recipientSteamId, recipientId, senderId, payloadBytes, delivery);
                return;
            }

            if (opcode == 4)
            {
                object delivery = Enum.ToObject(deliveryType, Convert.ToByte(readByte.Invoke(packet, null)));
                byte senderId = Convert.ToByte(readByte.Invoke(packet, null));

                int dataSize = Convert.ToInt32(readInt32.Invoke(packet, null));
                byte[] payloadBytes = dataSize > 0 ? (byte[])readBytes.Invoke(packet, [dataSize]) : [];
                if (payloadBytes == null || payloadBytes.Length < 1)
                    return;

                if (ShouldDropInboundPacket(senderId, 0, 1, 4, payloadBytes, senderSteamId))
                    return;

                if (_config.LogNetworkPackets)
                {
                    _log($"CH1 OP4 recv: sender={senderId}, payload={DescribeInnerPayload(payloadBytes)}, bytes={payloadBytes.Length}");
                }

                bool acceptedClientTimeSync = TryApplyIncomingTimeOfDay(senderId, payloadBytes);
                if (acceptedClientTimeSync)
                    _lastTimeOfDaySend = DateTime.MinValue;

                bool isPlayerExists = TryParsePlayerExistsHeader(payloadBytes, out bool requestResponse);
                if (isPlayerExists)
                {
                    _playerExistsPayloadById[senderId] = (byte[])payloadBytes.Clone();
                    _log($"Cached PlayerExistsMessage for player {senderId}; requestResponse={requestResponse}");
                }

                IEnumerable liveConnections = GetLiveConnectionObjects();
                void SendToClient(object conn, byte[] payload, byte recipient)
                {
                    SendChannel0PayloadToClient(conn, recipient, 0, payload, GetReliableOrderedDeliveryMethod());
                }

                if (_worldHandler != null &&
                    _worldHandler.TryHandleHostMessage(0, senderId, payloadBytes, senderSteamId, liveConnections, _connectionToGamer, SendToClient))
                {
                    return;
                }

                if (isPlayerExists && requestResponse)
                    ReplayCachedPlayerExistsToJoiner(senderSteamId, senderId);

                foreach (ConnectedSteamPeer peer in _peers.GetConnectedPeersSnapshot())
                {
                    if (peer.Gid == senderId)
                        continue;

                    SendChannel0PayloadToSteam(peer.SteamId, peer.Gid, senderId, payloadBytes, delivery);
                }
            }
        }

        /// <summary>
        /// Handles normal channel-0 gameplay packets and forwards them through the host/world pipeline.
        /// </summary>
        private void HandleChannel0Data(object packet, ulong senderSteamId)
        {
            MethodInfo readByte = ReflectEx.GetRequiredMethod(packet.GetType(), "ReadByte", Type.EmptyTypes);
            MethodInfo readInt32 = ReflectEx.GetRequiredMethod(packet.GetType(), "ReadInt32", Type.EmptyTypes);
            MethodInfo readBytes = ReflectEx.GetRequiredMethod(packet.GetType(), "ReadBytes", typeof(int));

            byte recipientId = Convert.ToByte(readByte.Invoke(packet, null));
            byte senderId = Convert.ToByte(readByte.Invoke(packet, null));

            int len = Convert.ToInt32(readInt32.Invoke(packet, null));
            byte[] data = len < 0 ? null : (len == 0 ? [] : (byte[])readBytes.Invoke(packet, [len]));
            if (data == null || data.Length < 1)
                return;

            if (ShouldDropInboundPacket(senderId, recipientId, 0, 0, data, senderSteamId))
                return;

            if (_config.LogNetworkPackets)
            {
                _log($"CH0 recv: recipient={recipientId}, sender={senderId}, payload={DescribeInnerPayload(data)}, bytes={data.Length}");
            }

            object reliableOrdered = GetReliableOrderedDeliveryMethod();
            IEnumerable liveConnections = GetLiveConnectionObjects();

            if (recipientId == 0)
            {
                void SendToClient(object conn, byte[] payload, byte recipient)
                {
                    SendChannel0PayloadToClient(conn, recipient, 0, payload, reliableOrdered);
                }

                if (_worldHandler != null &&
                    _worldHandler.TryHandleHostMessage(recipientId, senderId, data, senderSteamId, liveConnections, _connectionToGamer, SendToClient))
                {
                    return;
                }
            }

            foreach (ConnectedSteamPeer peer in _peers.GetConnectedPeersSnapshot())
            {
                if (peer.Gid == senderId)
                    continue;

                if (recipientId != 0 && peer.Gid != recipientId)
                    continue;

                SendChannel0PayloadToSteam(peer.SteamId, peer.Gid, senderId, data, reliableOrdered);
            }
        }

        /// <summary>
        /// Runs packet-level guard plugins before normal host handling or relay.
        /// </summary>
        private bool ShouldDropInboundPacket(
            byte senderId,
            byte recipientId,
            int channel,
            int wrappedOpcode,
            byte[] payload,
            ulong senderSteamId)
        {
            if (_plugins == null)
                return false;

            string senderName = ResolvePacketSenderName(senderId, senderSteamId);

            return _plugins.BeforeInboundPacket(new ServerInboundPacketContext
            {
                SenderId = senderId,
                SenderName = senderName,
                RemoteId = senderSteamId,
                RecipientId = recipientId,
                Channel = channel,
                WrappedOpcode = wrappedOpcode,
                Payload = payload,
                PayloadLength = payload == null ? 0 : payload.Length,
                UtcNow = DateTime.UtcNow,
                Log = _log
            });
        }

        /// <summary>
        /// Resolves the best known gamertag for a packet sender.
        /// </summary>
        private string ResolvePacketSenderName(byte senderId, ulong senderSteamId)
        {
            try
            {
                if (_peers != null &&
                    _peers.TryGetConnectedPeer(senderSteamId, out ConnectedSteamPeer peer) &&
                    peer != null &&
                    !string.IsNullOrWhiteSpace(peer.Gamertag))
                {
                    return peer.Gamertag;
                }
            }
            catch
            {
            }

            return "Player" + senderId;
        }
        #endregion

        #region Gamer / Peer Construction

        /// <summary>
        /// Creates the reflected SimpleGamer instance used to represent the dedicated server host.
        /// </summary>
        private object CreateHostGamer()
        {
            Type simpleGamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.SimpleGamer");
            Type playerIdType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.PlayerID");
            object nullPlayerId = ReflectEx.GetRequiredField(playerIdType, "Null").GetValue(null);
            return Activator.CreateInstance(simpleGamerType, [nullPlayerId, "Server"]);
        }

        /// <summary>
        /// Creates a reflected NetworkGamer object for a newly connected remote Steam peer.
        /// </summary>
        private object CreateConnectedRemoteGamer(object baseGamer, byte gid, ulong steamId)
        {
            Type gamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.Gamer");
            Type networkSessionType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.NetworkSession");
            Type networkGamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.NetworkGamer");
            ConstructorInfo ctor = networkGamerType.GetConstructor(
            [
                gamerType,
                networkSessionType,
                typeof(bool),
                typeof(bool),
                typeof(byte),
                typeof(ulong)
            ]);

            return ctor == null
                ? throw new InvalidOperationException("NetworkGamer(Gamer, NetworkSession, bool, bool, byte, ulong) ctor was not found.")
                : ctor.Invoke([baseGamer, null, false, false, gid, steamId]);
        }

        /// <summary>
        /// Sends the initial ResponseToConnection packet containing the joiner's assigned gid and known peers.
        /// </summary>
        private void SendConnectedResponse(ulong recipientSteamId, byte playerGid)
        {
            object packet = _steam.AllocPacket();
            object reliableOrdered = GetReliableOrderedDeliveryMethod();

            WriteByte(packet, 1); // InternalMessageTypes.ResponseToConnection
            WriteByte(packet, playerGid);

            List<object> peers = [_hostGamer];
            List<byte> ids = [0];

            foreach (ConnectedSteamPeer peer in _peers.GetConnectedPeersSnapshot().OrderBy(p => p.Gid))
            {
                peers.Add(peer.Gamer);
                ids.Add(peer.Gid);
            }

            WriteGamerArray(packet, peers);
            WriteByteArray(packet, [.. ids]);
            _steam.SendPacket(packet, recipientSteamId, reliableOrdered, 1);
        }

        /// <summary>
        /// Notifies existing peers that a new peer joined the session.
        /// </summary>
        private void BroadcastNewPeerToOthers(ConnectedSteamPeer newPeer)
        {
            object reliableOrdered = GetReliableOrderedDeliveryMethod();

            foreach (ConnectedSteamPeer existingPeer in _peers.GetConnectedPeersSnapshot())
            {
                if (existingPeer.SteamId == newPeer.SteamId)
                    continue;

                object packet = _steam.AllocPacket();
                WriteByte(packet, 0); // InternalMessageTypes.NewPeer
                WriteByte(packet, newPeer.Gid);
                WriteGamer(packet, newPeer.Gamer);
                _steam.SendPacket(packet, existingPeer.SteamId, reliableOrdered, 1);
            }
        }

        /// <summary>
        /// Notifies existing peers that a peer left the session.
        /// </summary>
        private void BroadcastDropPeerToOthers(ConnectedSteamPeer droppedPeer)
        {
            object reliableOrdered = GetReliableOrderedDeliveryMethod();

            foreach (ConnectedSteamPeer existingPeer in _peers.GetConnectedPeersSnapshot())
            {
                if (existingPeer.SteamId == droppedPeer.SteamId)
                    continue;

                object packet = _steam.AllocPacket();
                WriteByte(packet, 2); // InternalMessageTypes.DropPeer
                WriteByte(packet, droppedPeer.Gid);
                _steam.SendPacket(packet, existingPeer.SteamId, reliableOrdered, 1);
            }
        }
        #endregion

        #region Player Enforcement / Forced Drop

        /// <summary>
        /// Returns a console-friendly connected player list.
        /// </summary>
        public string GetPlayerListText()
        {
            if (_peers == null)
                return "No peer registry is available yet.";

            IReadOnlyList<ConnectedSteamPeer> peers = _peers.GetConnectedPeersSnapshot();
            if (peers.Count == 0)
                return "No players connected.";

            string text = "Connected players:";

            foreach (ConnectedSteamPeer peer in peers)
                text += Environment.NewLine + $"  {peer.Gid}: {peer.Gamertag} ({peer.SteamId})";

            return text;
        }

        /// <summary>
        /// Force-kicks a connected Steam peer without relying on KickMessage.
        /// </summary>
        public bool KickPlayer(string target, string reason = null)
        {
            if (!TryResolveSteamPeer(target, out ConnectedSteamPeer peer))
            {
                _log($"[PlayerEnforcement] Kick failed. Could not find player: {target}");
                return false;
            }

            return ForceDropSteamPeer(peer, ban: false, reason: reason);
        }

        /// <summary>
        /// Force-bans a connected Steam peer, persists the SteamID ban, then drops them.
        /// </summary>
        public bool BanPlayer(string target, string reason = null)
        {
            if (!TryResolveSteamPeer(target, out ConnectedSteamPeer peer))
            {
                _log($"[PlayerEnforcement] Ban failed. Could not find player: {target}");
                return false;
            }

            return ForceDropSteamPeer(peer, ban: true, reason: reason);
        }

        /// <summary>
        /// Removes a persisted SteamID / name ban.
        /// </summary>
        public bool UnbanPlayer(string target)
        {
            bool removed = _banStore != null && _banStore.Remove(target);

            _log(removed
                ? $"[PlayerEnforcement] Removed ban: {target}"
                : $"[PlayerEnforcement] No ban matched: {target}");

            return removed;
        }

        /// <summary>
        /// Resolves a connected Steam peer by GID, SteamID, exact name, or partial name.
        /// </summary>
        private bool TryResolveSteamPeer(string target, out ConnectedSteamPeer peer)
        {
            peer = null;

            if (_peers == null || string.IsNullOrWhiteSpace(target))
                return false;

            target = target.Trim();

            if (ulong.TryParse(target, out ulong steamId) &&
                _peers.TryGetConnectedPeer(steamId, out peer))
            {
                return true;
            }

            IReadOnlyList<ConnectedSteamPeer> peers = _peers.GetConnectedPeersSnapshot();

            if (byte.TryParse(target, out byte gid))
            {
                foreach (ConnectedSteamPeer current in peers)
                {
                    if (current.Gid == gid)
                    {
                        peer = current;
                        return true;
                    }
                }
            }

            foreach (ConnectedSteamPeer current in peers)
            {
                if (string.Equals(current.Gamertag, target, StringComparison.OrdinalIgnoreCase))
                {
                    peer = current;
                    return true;
                }
            }

            ConnectedSteamPeer partial = null;

            foreach (ConnectedSteamPeer current in peers)
            {
                if (current.Gamertag != null &&
                    current.Gamertag.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (partial != null)
                    {
                        _log($"[PlayerEnforcement] Target '{target}' is ambiguous.");
                        return false;
                    }

                    partial = current;
                }
            }

            peer = partial;
            return peer != null;
        }

        /// <summary>
        /// Performs the actual hard kick/ban.
        /// This is the dedicated-server equivalent of CastleWalls Mk2 hard enforcement.
        /// </summary>
        private bool ForceDropSteamPeer(ConnectedSteamPeer peer, bool ban, string reason)
        {
            if (peer == null || peer.SteamId == 0UL)
                return false;

            reason = string.IsNullOrWhiteSpace(reason)
                ? (ban ? "Banned from this server." : "Kicked from this server.")
                : reason.Trim();

            if (ban)
                _banStore?.BanSteam(peer.SteamId, peer.Gamertag, reason);

            _forceDroppedSteamIds.Add(peer.SteamId);

            // Tell the target connection to close on the Steam transport path.
            // This does not depend on the client-side KickMessage pipeline.
            try
            {
                _steam?.DenyConnection(peer.SteamId, reason);
            }
            catch (Exception ex)
            {
                _log($"[PlayerEnforcement] Steam deny failed for {peer.SteamId}: {ex.Message}");
            }

            RemoveSteamPeerFromServer(peer);

            _log(ban
                ? $"[PlayerEnforcement] Hard banned {peer.Gamertag} ({peer.SteamId})."
                : $"[PlayerEnforcement] Hard kicked {peer.Gamertag} ({peer.SteamId}).");

            return true;
        }

        /// <summary>
        /// Removes a Steam peer from all authoritative server-side state and broadcasts DropPeer.
        /// </summary>
        private void RemoveSteamPeerFromServer(ConnectedSteamPeer peer)
        {
            if (peer == null)
                return;

            _peers?.RemoveBySteamId(peer.SteamId, out _);
            _approval?.RemovePeer(peer.SteamId);
            _connectionToGamer.Remove(peer.SteamId);
            _playerExistsPayloadById.Remove(peer.Gid);

            _worldHandler?.OnClientDisconnected(peer.Gid);
            NotifyPluginsPlayerLeft(peer.Gid, peer.Gamertag);

            // Important: tell every remaining client to remove this peer immediately.
            BroadcastDropPeerToOthers(peer);

            _worldHandler?.ApplyServerMessage(BuildServerDisplayMessage(), saveToDisk: false);
            PublishCurrentLobbyMetadata();
        }
        #endregion

        #region Channel Send Helpers

        /// <summary>
        /// Writes the CastleMiner Z channel-0 packet envelope into a Steam packet object.
        /// </summary>
        private void WriteChannel0Packet(object packet, byte recipientId, byte senderId, byte[] payload)
        {
            WriteByte(packet, recipientId);
            WriteByte(packet, senderId);
            WriteByteArray(packet, payload);
        }

        /// <summary>
        /// Sends a channel-0 gameplay payload directly to a Steam peer.
        /// </summary>
        private void SendChannel0PayloadToSteam(ulong recipientSteamId, byte recipientId, byte senderId, byte[] payload, object delivery = null)
        {
            if (recipientSteamId == 0UL || payload == null)
                return;

            delivery ??= GetReliableOrderedDeliveryMethod();

            object packet = _steam.AllocPacket();
            WriteChannel0Packet(packet, recipientId, senderId, payload);
            _steam.SendPacket(packet, recipientSteamId, delivery, 0);
        }

        /// <summary>
        /// Sends a channel-0 gameplay payload to a connection object after converting it to a Steam id.
        /// </summary>
        private void SendChannel0PayloadToClient(object conn, byte recipientId, byte senderId, byte[] payload, object delivery = null)
        {
            if (!TryConvertConnectionObjectToSteamId(conn, out ulong recipientSteamId))
                return;

            SendChannel0PayloadToSteam(recipientSteamId, recipientId, senderId, payload, delivery);
        }

        /// <summary>
        /// Converts a server connection object into a Steam id when possible.
        /// </summary>
        private bool TryConvertConnectionObjectToSteamId(object conn, out ulong steamId)
        {
            if (conn is ulong u)
            {
                steamId = u;
                return true;
            }

            try
            {
                steamId = Convert.ToUInt64(conn);
                return steamId != 0UL;
            }
            catch
            {
                steamId = 0UL;
                return false;
            }
        }

        /// <summary>
        /// Resolves a CastleMiner Z gamer id to its connected Steam id.
        /// </summary>
        private bool TryGetSteamIdForPlayer(byte gid, out ulong steamId)
        {
            return _peers.TryGetSteamId(gid, out steamId);
        }

        /// <summary>
        /// Gets a snapshot of currently connected peer connection objects.
        /// </summary>
        private IEnumerable GetLiveConnectionObjects()
        {
            return _peers.GetConnectedPeersSnapshot().Select(p => (object)p.SteamId).ToList();
        }
        #endregion

        #region Broadcast Relays

        /// <summary>
        /// Builds a server-originated BroadcastTextMessage payload.
        /// </summary>
        private byte[] BuildServerTextPayload(string text)
        {
            if (_codec == null)
                return null;

            return _codec.BuildPayload(
                "DNA.CastleMinerZ.Net.BroadcastTextMessage",
                msg =>
                {
                    _codec.SetMember(msg, "Message", text ?? string.Empty);
                });
        }

        /// <summary>
        /// Sends a private server text message to one Steam client.
        /// </summary>
        private void SendPrivateServerTextToSteam(ulong steamId, byte recipientId, string text)
        {
            byte[] payload = BuildServerTextPayload(text);

            if (payload == null || payload.Length == 0)
                return;

            SendChannel0PayloadToSteam(steamId, recipientId, 0, payload, GetReliableOrderedDeliveryMethod());
        }

        /// <summary>
        /// Broadcasts a server text message to every connected Steam client.
        /// </summary>
        private void BroadcastServerText(string text)
        {
            byte[] payload = BuildServerTextPayload(text);

            if (payload == null || payload.Length == 0)
                return;

            object delivery = GetReliableOrderedDeliveryMethod();

            foreach (ConnectedSteamPeer peer in _peers.GetConnectedPeersSnapshot())
                SendChannel0PayloadToSteam(peer.SteamId, peer.Gid, 0, payload, delivery);
        }
        #endregion

        #region PlayerExists Replay Helpers

        /// <summary>
        /// Detects whether a payload is PlayerExistsMessage and extracts its request-response flag.
        /// </summary>
        private bool TryParsePlayerExistsHeader(byte[] payload, out bool requestResponse)
        {
            requestResponse = false;

            if (_codec == null || payload == null || payload.Length < 3)
                return false;

            try
            {
                if (!_playerExistsMessageId.HasValue)
                    _playerExistsMessageId = _codec.GetMessageId("DNA.CastleMinerZ.Net.PlayerExistsMessage");

                if (payload[0] != _playerExistsMessageId.Value)
                    return false;

                requestResponse = payload[1] != 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Replays cached PlayerExistsMessage payloads to a new joiner so existing players appear correctly.
        /// </summary>
        private void ReplayCachedPlayerExistsToJoiner(ulong joinerSteamId, byte joinerId)
        {
            foreach (KeyValuePair<byte, byte[]> kvp in _playerExistsPayloadById.OrderBy(p => p.Key))
            {
                byte existingPlayerId = kvp.Key;
                byte[] payload = kvp.Value;

                if (existingPlayerId == joinerId || payload == null || payload.Length == 0)
                    continue;

                SendChannel0PayloadToSteam(joinerSteamId, joinerId, existingPlayerId, payload, GetReliableOrderedDeliveryMethod());
                _log($"Replayed cached PlayerExistsMessage: existing={existingPlayerId} -> joiner={joinerId}");
            }
        }
        #endregion

        #region Packet Debug Helpers

        /// <summary>
        /// Returns a readable message-id/type description for logging an inner channel payload.
        /// </summary>
        private string DescribeInnerPayload(byte[] payload)
        {
            if (_codec == null || payload == null || payload.Length < 1)
                return "<null>";

            try
            {
                byte msgId = payload[0];
                string typeName = _codec.GetTypeName(msgId);
                return string.IsNullOrWhiteSpace(typeName) ? $"{msgId}=<unknown>" : $"{msgId}={typeName}";
            }
            catch
            {
                return "<decode failed>";
            }
        }
        #endregion

        #region Time-of-Day Sync

        /// <summary>
        /// Applies a client-provided TimeOfDayMessage when client time sync is enabled.
        /// </summary>
        private bool TryApplyIncomingTimeOfDay(byte senderId, byte[] payload)
        {
            if (!_config.AllowClientTimeSync || _codec == null || payload == null || payload.Length < 6)
                return false;

            try
            {
                byte msgId = payload[0];
                string typeName = _codec.GetTypeName(msgId);
                if (!string.Equals(typeName, "DNA.CastleMinerZ.Net.TimeOfDayMessage", StringComparison.Ordinal))
                    return false;

                float newDayValue = BitConverter.ToSingle(payload, 1);
                _timeOfDay = newDayValue;
                _log($"Accepted client TimeOfDayMessage from player {senderId}; server day set to {_timeOfDay:0.000}");
                return true;
            }
            catch (Exception ex)
            {
                _log("TryApplyIncomingTimeOfDay failed: " + ex.GetType().FullName + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Advances the authoritative server day value using real elapsed time.
        ///
        /// Important:
        /// - This value is not just a 0..1 time-of-day value.
        /// - The vanilla game expects GameScreen.Day to keep increasing forever.
        /// - The client derives visual time-of-day from the fractional part.
        /// - The HUD derives the visible day number from the integer part.
        /// </summary>
        private void AdvanceTimeOfDay()
        {
            DateTime now = DateTime.UtcNow;
            double deltaSeconds = (now - _lastTimeOfDayAdvance).TotalSeconds;
            _lastTimeOfDayAdvance = now;

            if (deltaSeconds < 0)
                deltaSeconds = 0;

            // Avoid large jumps if the process pauses, breaks in debugger, or stalls.
            if (deltaSeconds > 1.0)
                deltaSeconds = 1.0;

            _timeOfDay += (float)(deltaSeconds / SecondsPerFullDay);
        }

        /// <summary>
        /// Periodically broadcasts the authoritative server day value to all connected peers.
        /// </summary>
        private void BroadcastTimeOfDayIfNeeded()
        {
            if (_worldHandler == null)
                return;

            if ((DateTime.UtcNow - _lastTimeOfDaySend).TotalSeconds < 2.0)
                return;

            byte[] todPayload = _worldHandler.BuildTimeOfDayPayload(_timeOfDay);
            if (todPayload == null || todPayload.Length == 0)
                return;

            object reliableOrdered = GetReliableOrderedDeliveryMethod();
            foreach (ConnectedSteamPeer peer in _peers.GetConnectedPeersSnapshot())
                SendChannel0PayloadToSteam(peer.SteamId, peer.Gid, 0, todPayload, reliableOrdered);

            _lastTimeOfDaySend = DateTime.UtcNow;
        }

        /// <summary>
        /// Sends the current authoritative day value to a newly connected peer.
        /// </summary>
        private void SendInitialTimeOfDayToJoiner(ulong joinerSteamId, byte joinerGid)
        {
            if (_worldHandler == null)
                return;

            byte[] todPayload = _worldHandler.BuildTimeOfDayPayload(_timeOfDay);
            if (todPayload == null || todPayload.Length == 0)
                return;

            SendChannel0PayloadToSteam(joinerSteamId, joinerGid, 0, todPayload, GetReliableOrderedDeliveryMethod());
        }

        /// <summary>
        /// Returns the player-facing day number used in the server name template.
        /// </summary>
        /// <remarks>
        /// The server's internal _timeOfDay starts near 0.41 on the first day, so the
        /// player-facing day should normally be floor(value) + 1.
        /// </remarks>
        private int GetDisplayDay()
        {
            return Math.Max(1, (int)Math.Floor(_timeOfDay) + 1);
        }

        /// <summary>
        /// Resolves the configured server-name template into the current lobby display name.
        /// </summary>
        /// <remarks>
        /// Supported tokens:
        /// {day}        = player-facing day number
        /// {day00}      = day number padded to two digits
        /// {players}    = connected player count
        /// {maxplayers} = configured max players
        /// </remarks>
        private string BuildServerDisplayName()
        {
            int day = GetDisplayDay();
            int players = _peers?.GetConnectedPeersSnapshot().Count ?? 0;

            string name = _config.ServerName ?? "CMZ Steam Server";

            name = name.Replace("{day}", day.ToString());
            name = name.Replace("{day00}", day.ToString("00"));
            name = name.Replace("{players}", players.ToString());
            name = name.Replace("{maxplayers}", _config.MaxPlayers.ToString());

            // Keep the lobby name reasonably safe for Steam/browser display.
            if (name.Length > 64)
                name = name.Substring(0, 64);

            return name;
        }

        /// <summary>
        /// Resolves the configured server-message template into the current Steam session message.
        /// </summary>
        private string BuildServerDisplayMessage()
        {
            int day = GetDisplayDay();
            int players = _peers?.GetConnectedPeersSnapshot().Count ?? 0;

            string message = string.IsNullOrWhiteSpace(_serverMessage)
                ? "Welcome to this CastleForge dedicated server."
                : _serverMessage;

            message = message.Replace("{day}", day.ToString());
            message = message.Replace("{day00}", day.ToString("00"));
            message = message.Replace("{players}", players.ToString());
            message = message.Replace("{maxplayers}", _config.MaxPlayers.ToString());

            if (message.Length > 128)
                message = message.Substring(0, 128);

            return message;
        }

        /// <summary>
        /// Refreshes Steam lobby metadata when the resolved server display name or message changes.
        /// </summary>
        private void RefreshLobbyMetadataIfNeeded()
        {
            if (_lobbyHost == null || !_lobbyHost.IsLobbyReady)
                return;

            int currentDay = GetDisplayDay();
            string currentName = BuildServerDisplayName();
            string currentMessage = BuildServerDisplayMessage();

            if (currentDay == _lastPublishedLobbyDay &&
                string.Equals(currentName, _lastPublishedLobbyName, StringComparison.Ordinal) &&
                string.Equals(currentMessage, _lastPublishedLobbyMessage, StringComparison.Ordinal))
            {
                return;
            }

            _lobbyHost.SetLobbyName(currentName);
            _lobbyHost.SetLobbyMessage(currentMessage);
            _lobbyHost.RefreshLobbyMetadata();

            _lastPublishedLobbyDay = currentDay;
            _lastPublishedLobbyName = currentName;
            _lastPublishedLobbyMessage = currentMessage;

            _log($"[SteamLobby] Updated lobby metadata: {currentName} | {currentMessage}");
        }
        #endregion

        #region Reflected Write Helpers

        /// <summary>
        /// Gets the reflected reliable-ordered Lidgren delivery enum value used by CastleMiner Z.
        /// </summary>
        private object GetReliableOrderedDeliveryMethod()
        {
            Type deliveryType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetDeliveryMethod");
            return Enum.ToObject(deliveryType, 67);
        }

        /// <summary>
        /// Writes a byte into a reflected Steam/Lidgren packet object.
        /// </summary>
        private void WriteByte(object packet, byte value)
        {
            MethodInfo writeByte = ReflectEx.GetRequiredMethod(packet.GetType(), "Write", typeof(byte));
            writeByte.Invoke(packet, [value]);
        }

        /// <summary>
        /// Writes a reflected Gamer object into a packet using DNA.Net.GamerServices.LidgrenExtensions.
        /// </summary>
        private void WriteGamer(object packet, object gamer)
        {
            Type netBufferType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetBuffer");
            Type gamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.Gamer");
            Type extensionsType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.LidgrenExtensions");
            MethodInfo writeGamer = ReflectEx.GetRequiredMethod(extensionsType, "Write", netBufferType, gamerType);
            writeGamer.Invoke(null, [packet, gamer]);
        }

        /// <summary>
        /// Writes an array of reflected Gamer objects into a packet.
        /// </summary>
        private void WriteGamerArray(object packet, List<object> gamers)
        {
            Type netBufferType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetBuffer");
            Type gamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.Gamer");
            Type extensionsType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.LidgrenExtensions");
            MethodInfo writeArray = ReflectEx.GetRequiredMethod(extensionsType, "WriteArray", netBufferType, gamerType.MakeArrayType());

            Array gamerArray = Array.CreateInstance(gamerType, gamers.Count);
            for (int i = 0; i < gamers.Count; i++)
                gamerArray.SetValue(gamers[i], i);

            writeArray.Invoke(null, [packet, gamerArray]);
        }

        /// <summary>
        /// Writes a byte array into a packet using DNA.Net.GamerServices.LidgrenExtensions.
        /// </summary>
        private void WriteByteArray(object packet, byte[] ids)
        {
            Type netBufferType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetBuffer");
            Type extensionsType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.LidgrenExtensions");
            MethodInfo writeArray = ReflectEx.GetRequiredMethod(extensionsType, "WriteArray", netBufferType, typeof(byte[]));
            writeArray.Invoke(null, [packet, ids]);
        }
        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes the Steam bootstrap and marks the server as stopped.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _steam?.Dispose();
            }
            finally
            {
                _running = false;
            }
        }
        #endregion
    }
}