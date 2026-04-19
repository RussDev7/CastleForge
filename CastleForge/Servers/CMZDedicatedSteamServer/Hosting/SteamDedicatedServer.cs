/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Common;
using CMZDedicatedSteamServer.Config;
using CMZDedicatedSteamServer.Steam;
using System.Collections.Generic;
using CMZDedicatedLidgrenServer;
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
        private readonly string _baseDir = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;
        private readonly SteamServerConfig _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly Action<string> _log = log ?? (_ => { });

        private bool _running;
        private ServerAssemblyLoader _assemblyLoader;
        private SteamServerBootstrap _steam;
        private SteamConnectionApproval _approval;
        private SteamPeerRegistry _peers;
        private SteamLobbyHost _lobbyHost;
        private object _hostGamer;
        private ServerWorldHandler _worldHandler;
        private CmzMessageCodec _codec;

        private readonly Dictionary<object, object> _connectionToGamer = [];
        private readonly Dictionary<byte, byte[]> _playerExistsPayloadById = [];
        private byte? _playerExistsMessageId;

        private float _timeOfDay = 0.41f;
        private DateTime _lastTimeOfDaySend = DateTime.MinValue;
        private DateTime _lastTimeOfDayAdvance = DateTime.UtcNow;

        private const float SecondsPerFullDay = 960f;

        public bool IsRunning => _running;

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
            _lobbyHost = new SteamLobbyHost(
                _config,
                _assemblyLoader.CommonAssembly,
                _assemblyLoader.SteamAssembly,
                _steam.SteamWorksInstance,
                _log);

            _lobbyHost.BeginCreateLobby();

            ulong effectiveSaveOwnerSteamId = _config.SaveOwnerSteamId != 0UL ? _config.SaveOwnerSteamId : _steam.SteamPlayerId;
            if (!string.IsNullOrWhiteSpace(_config.WorldFolder) && effectiveSaveOwnerSteamId != 0UL)
            {
                _worldHandler = new ServerWorldHandler(
                    gamePath,
                    _config.WorldFolder,
                    _baseDir,
                    effectiveSaveOwnerSteamId,
                    _log,
                    _config.ViewDistanceChunks);

                _worldHandler.Init(_assemblyLoader.GameAssembly, _assemblyLoader.CommonAssembly);
                _codec = new CmzMessageCodec(_assemblyLoader.GameAssembly, _assemblyLoader.CommonAssembly, _log);
                _log($"[World] World handler initialized. WorldFolder={_config.WorldFolder}, SaveOwner={effectiveSaveOwnerSteamId}");
            }
            else
            {
                _log("[World] World handler not initialized. Make sure world-guid is set and Steam resolved a valid host account.");
            }

            _running = true;
        }

        public void Stop()
        {
            _running = false;
        }

        public void Update()
        {
            if (!_running)
                return;

            AdvanceTimeOfDay();
            BroadcastTimeOfDayIfNeeded();

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

        private void ProcessPacket(object packet)
        {
            object messageType = ReflectEx.GetRequiredMemberValue(packet, "MessageType");
            string messageTypeName = messageType.ToString();
            ulong senderSteamId = Convert.ToUInt64(ReflectEx.GetRequiredMemberValue(packet, "SenderId"));

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

        private void HandleConnectionApproval(object packet, ulong senderSteamId)
        {
            ApprovalDecision decision = _approval.ValidateRequest(packet, senderSteamId);
            if (decision.Allowed)
            {
                _steam.AcceptConnection(senderSteamId);
                _log($"[ConnectionApproval] Accepted {decision.Gamertag} ({senderSteamId}).");
                return;
            }

            _steam.DenyConnection(senderSteamId, decision.ResultCode);
            _log($"[ConnectionApproval] Denied {senderSteamId} -> {decision.ResultCode}");
        }

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
                    BroadcastNewPeerToOthers(newPeer);
                    SendInitialTimeOfDayToJoiner(senderSteamId, gid);

                    _log($"[StatusChanged] Peer connected. SteamID={senderSteamId}, GID={gid}");
                    _log($"[Handshake] ResponseToConnection sent to {pending.Gamertag} ({senderSteamId}) with GID {gid}.");
                    return;
                }

                case 7:
                {
                    if (_peers.RemoveBySteamId(senderSteamId, out ConnectedSteamPeer peer))
                    {
                        _approval.RemovePeer(senderSteamId);
                        _connectionToGamer.Remove(senderSteamId);
                        _playerExistsPayloadById.Remove(peer.Gid);
                        _worldHandler?.OnClientDisconnected(peer.Gid);
                        BroadcastDropPeerToOthers(peer);
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

                _log($"CH1 OP4 recv: sender={senderId}, payload={DescribeInnerPayload(payloadBytes)}, bytes={payloadBytes.Length}");

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

            _log($"CH0 recv: recipient={recipientId}, sender={senderId}, payload={DescribeInnerPayload(data)}, bytes={data.Length}");

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

        private object CreateHostGamer()
        {
            Type simpleGamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.SimpleGamer");
            Type playerIdType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.PlayerID");
            object nullPlayerId = ReflectEx.GetRequiredField(playerIdType, "Null").GetValue(null);
            return Activator.CreateInstance(simpleGamerType, [nullPlayerId, "Server"]);
        }

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

        private void WriteChannel0Packet(object packet, byte recipientId, byte senderId, byte[] payload)
        {
            WriteByte(packet, recipientId);
            WriteByte(packet, senderId);
            WriteByteArray(packet, payload);
        }

        private void SendChannel0PayloadToSteam(ulong recipientSteamId, byte recipientId, byte senderId, byte[] payload, object delivery = null)
        {
            if (recipientSteamId == 0UL || payload == null)
                return;

            delivery ??= GetReliableOrderedDeliveryMethod();

            object packet = _steam.AllocPacket();
            WriteChannel0Packet(packet, recipientId, senderId, payload);
            _steam.SendPacket(packet, recipientSteamId, delivery, 0);
        }

        private void SendChannel0PayloadToClient(object conn, byte recipientId, byte senderId, byte[] payload, object delivery = null)
        {
            if (!TryConvertConnectionObjectToSteamId(conn, out ulong recipientSteamId))
                return;

            SendChannel0PayloadToSteam(recipientSteamId, recipientId, senderId, payload, delivery);
        }

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

        private bool TryGetSteamIdForPlayer(byte gid, out ulong steamId)
        {
            return _peers.TryGetSteamId(gid, out steamId);
        }

        private IEnumerable GetLiveConnectionObjects()
        {
            return _peers.GetConnectedPeersSnapshot().Select(p => (object)p.SteamId).ToList();
        }

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

        private void AdvanceTimeOfDay()
        {
            DateTime now = DateTime.UtcNow;
            double deltaSeconds = (now - _lastTimeOfDayAdvance).TotalSeconds;
            _lastTimeOfDayAdvance = now;

            if (deltaSeconds <= 0)
                return;

            _timeOfDay += (float)(deltaSeconds / SecondsPerFullDay);
            while (_timeOfDay >= 1f)
                _timeOfDay -= 1f;
        }

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

        private void SendInitialTimeOfDayToJoiner(ulong joinerSteamId, byte joinerGid)
        {
            if (_worldHandler == null)
                return;

            byte[] todPayload = _worldHandler.BuildTimeOfDayPayload(_timeOfDay);
            if (todPayload == null || todPayload.Length == 0)
                return;

            SendChannel0PayloadToSteam(joinerSteamId, joinerGid, 0, todPayload, GetReliableOrderedDeliveryMethod());
        }

        private object GetReliableOrderedDeliveryMethod()
        {
            Type deliveryType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetDeliveryMethod");
            return Enum.ToObject(deliveryType, 67);
        }

        private void WriteByte(object packet, byte value)
        {
            MethodInfo writeByte = ReflectEx.GetRequiredMethod(packet.GetType(), "Write", typeof(byte));
            writeByte.Invoke(packet, [value]);
        }

        private void WriteGamer(object packet, object gamer)
        {
            Type netBufferType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetBuffer");
            Type gamerType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.Gamer");
            Type extensionsType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.LidgrenExtensions");
            MethodInfo writeGamer = ReflectEx.GetRequiredMethod(extensionsType, "Write", netBufferType, gamerType);
            writeGamer.Invoke(null, [packet, gamer]);
        }

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

        private void WriteByteArray(object packet, byte[] ids)
        {
            Type netBufferType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.Lidgren.NetBuffer");
            Type extensionsType = ReflectEx.GetRequiredType(_assemblyLoader.CommonAssembly, "DNA.Net.GamerServices.LidgrenExtensions");
            MethodInfo writeArray = ReflectEx.GetRequiredMethod(extensionsType, "WriteArray", netBufferType, typeof(byte[]));
            writeArray.Invoke(null, [packet, ids]);
        }

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
    }
}
