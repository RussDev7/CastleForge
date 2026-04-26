/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Plugins;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using System;

namespace CMZDedicatedSteamServer
{
    /// <summary>
    /// Handles host-side CastleMiner Z world, terrain, and inventory message flow for recipientId 0.
    ///
    /// Responsibilities:
    /// - Initializes game/common assembly references and message-id lookup tables.
    /// - Loads world metadata and a save device using the same save-key derivation style as vanilla.
    /// - Responds to host-consumed world/chunk/inventory requests.
    /// - Applies host-side terrain, crate, door, and custom-block mutations before relay.
    /// - Reads and writes saved chunk delta files and player inventory files.
    ///
    /// Notes:
    /// - This class intentionally uses reflection so it can run outside the original game process.
    /// - Most message parsing is performed by instantiating the original game message type and invoking
    ///   its internal RecieveData(BinaryReader) method.
    /// - Chunk delta files are handled in the same general format used by CastleMiner Z chunk cache storage.
    /// </summary>
    /// <remarks>
    /// Creates a new host-side world handler.
    ///
    /// Notes:
    /// - The view radius is clamped to a reasonable range.
    /// - No reflection work is performed until <see cref="Init(Assembly, Assembly)"/>.
    /// </remarks>
    internal sealed class ServerWorldHandler(
        string gamePath,
        string worldFolder,
        string saveRoot,
        ulong saveOwnerSteamId,
        Action<string> log,
        int viewRadiusChunks = 8,
        ServerPluginManager plugins = null,
        bool logHostMessages = false)
    {
        #region Fields

        #region Verbose packet / debug logging

        /// <summary>
        /// Enables verbose host/world mutation logging.
        /// </summary>
        private readonly bool _logHostMessages = logHostMessages;

        #endregion

        #region Config / ctor-provided state

        /// <summary>Relative world folder path, e.g. Worlds\GUID.</summary>
        private readonly string _worldFolder = worldFolder ?? throw new ArgumentNullException(nameof(worldFolder));

        /// <summary>Absolute save root path, e.g. build\ServerHost.</summary>
        private readonly string _saveRoot = saveRoot ?? throw new ArgumentNullException(nameof(saveRoot));

        /// <summary>Steam user id used to derive the save-device encryption key.</summary>
        private readonly ulong _saveOwnerSteamId = saveOwnerSteamId;

        /// <summary>Absolute game path used externally by the host application.</summary>
        private readonly string _gamePath = gamePath ?? throw new ArgumentNullException(nameof(gamePath));

        /// <summary>Logging callback supplied by the host.</summary>
        private readonly Action<string> _log = log ?? (_ => { });

        /// <summary>Configured radius used by the spawn-based chunk-list builder.</summary>
        private readonly int _viewRadiusChunks = viewRadiusChunks < 2 ? 2 : (viewRadiusChunks > 32 ? 32 : viewRadiusChunks);

        #endregion

        #region Reflected runtime state

        /// <summary>CastleMinerZ game assembly.</summary>
        private Assembly _gameAsm;

        /// <summary>Common/DNA assembly.</summary>
        private Assembly _commonAsm;

        /// <summary>Loaded WorldInfo instance.</summary>
        private object _worldInfo;

        /// <summary>Reflected FileSystemSaveDevice instance.</summary>
        private object _saveDevice;

        /// <summary>
        /// Protects WorldInfo mutation, save, and send serialization.
        /// </summary>
        private readonly object _worldInfoLock = new();

        #endregion

        #region Message registry

        /// <summary>Maps network message id to full reflected type name.</summary>
        private Dictionary<byte, string> _messageIdToType;

        /// <summary>Maps full reflected type name to network message id.</summary>
        private Dictionary<string, byte> _typeToMessageId;

        #endregion

        #region Chunk cache

        /// <summary>In-memory LRU cache of chunk deltas keyed by "chunkX_chunkY_chunkZ".</summary>
        private readonly Dictionary<string, int[]> _chunkCache = [];

        /// <summary>LRU order for chunk cache entries.</summary>
        private readonly LinkedList<string> _chunkCacheOrder = new();

        /// <summary>Lookup for LRU linked-list nodes.</summary>
        private readonly Dictionary<string, LinkedListNode<string>> _chunkCacheNodes = [];

        /// <summary>Maximum number of cached chunk entries retained in memory.</summary>
        private const int MaxChunkCacheEntries = 2048;

        #endregion

        #region Per-player tracking

        /// <summary>
        /// Tracks whether the first chunk request has been logged for a player so the log stays concise.
        /// </summary>
        private readonly HashSet<byte> _chunkRequestLoggedForPlayer = [];

        #endregion

        #region Spawn hint

        // World / spawn hint
        /// <summary>Spawn hint X loaded from WorldInfo.LastPosition or backing field.</summary>
        private float _spawnX = 8f;

        /// <summary>Spawn hint Y loaded from WorldInfo.LastPosition or backing field.</summary>
        private float _spawnY = 128f;

        /// <summary>Spawn hint Z loaded from WorldInfo.LastPosition or backing field.</summary>
        private float _spawnZ = -8f;

        #endregion

        #region Server plugin bridge state

        /// <summary>
        /// Optional plugin manager used to intercept host-side world mutations before apply/relay.
        /// </summary>
        private readonly ServerPluginManager _plugins = plugins;

        /// <summary>
        /// Recent DigMessage block snapshots by player.
        /// Used to restore natural/procedural blocks when a denied AlterBlockMessage tries to mine them.
        /// </summary>
        private readonly Dictionary<byte, RecentDigBlock> _recentDigBlockByPlayer =
            [];

        /// <summary>
        /// Stores the most recent original block type reported by a player's DigMessage.
        /// </summary>
        /// <remarks>
        /// This is a short-lived visual correction aid. It lets server-side RegionProtect restore
        /// natural terrain after a denied mine action even when the block was never present in the
        /// saved chunk delta.
        /// </remarks>
        private struct RecentDigBlock
        {
            /// <summary>
            /// Raw BlockTypeEnum integer value reported by the client's DigMessage.
            /// </summary>
            public int BlockTypeValue;

            /// <summary>
            /// UTC timestamp used to expire stale dig snapshots.
            /// </summary>
            public DateTime UtcTime;
        }
        #endregion

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes reflected assembly references, builds the message registry, initializes the save device,
        /// loads world.info, and reads the spawn hint.
        ///
        /// Notes:
        /// - ReflectionTools.RegisterAssembly is attempted in both assembly orders to mimic the original environment.
        /// - DNA.Net.Message static constructor is forced so its internal message registry becomes available.
        /// </summary>
        public void Init(Assembly gameAsm, Assembly commonAsm)
        {
            _gameAsm = gameAsm;
            _commonAsm = commonAsm;

            _messageIdToType = [];
            _typeToMessageId = [];

            try
            {
                var reflectionToolsType = commonAsm.GetType("DNA.Reflection.ReflectionTools");
                var registerMethod = reflectionToolsType?.GetMethod("RegisterAssembly", BindingFlags.Public | BindingFlags.Static);

                if (registerMethod != null)
                {
                    try
                    {
                        registerMethod.Invoke(null, [_gameAsm, _commonAsm]);
                        registerMethod.Invoke(null, [_commonAsm, _gameAsm]);
                    }
                    catch (Exception ex)
                    {
                        _log("ServerWorld RegisterAssembly: " + ex.Message);
                    }
                }

                var messageType = commonAsm.GetType("DNA.Net.Message");
                if (messageType == null)
                {
                    _log("ServerWorld: DNA.Net.Message type not found.");
                    return;
                }

                RuntimeHelpers.RunClassConstructor(messageType.TypeHandle);

                var typesField = messageType.GetField("_messageTypes", BindingFlags.NonPublic | BindingFlags.Static);
                var idsField = messageType.GetField("_messageIDs", BindingFlags.NonPublic | BindingFlags.Static);

                if (typesField?.GetValue(null) is not Type[] types || idsField?.GetValue(null) is not System.Collections.IDictionary ids)
                {
                    _log("ServerWorld: Could not get message type registry.");
                    return;
                }

                for (byte i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t != null)
                    {
                        _messageIdToType[i] = t.FullName;
                        _typeToMessageId[t.FullName] = i;
                    }
                }

                _log("ServerWorld: Loaded " + _messageIdToType.Count + " message types.");

                InitSaveDevice();
                InitInventoryRegistry();
                LoadWorldInfo();
                ReadSpawnHintFromWorldInfo();
            }
            catch (Exception ex)
            {
                _log("ServerWorld Init: " + ex.Message);
            }
        }

        /// <summary>
        /// Initializes the vanilla InventoryItem registry with headless-safe placeholder item classes.
        /// </summary>
        /// <remarks>
        /// The normal game calls InventoryItem.Initalize(ContentManager), but that initializer creates
        /// render/model-heavy item classes and some of them directly access CastleMinerZGame.Instance.
        /// The dedicated server only needs item classes for crate/inventory serialization, so lightweight
        /// placeholder classes are enough as long as each placeholder preserves the correct InventoryItemIDs.
        /// </remarks>
        private void InitInventoryRegistry()
        {
            try
            {
                Type inventoryItemType = ResolveType("DNA.CastleMinerZ.Inventory.InventoryItem");
                Type inventoryItemIdsType = ResolveType("DNA.CastleMinerZ.Inventory.InventoryItemIDs");
                Type blockInventoryItemClassType = ResolveType("DNA.CastleMinerZ.Inventory.BlockInventoryItemClass");
                Type blockTypeEnumType = ResolveType("DNA.CastleMinerZ.Terrain.BlockTypeEnum");

                if (inventoryItemType == null)
                {
                    _log("ServerWorld: InventoryItem type not found.");
                    return;
                }

                if (inventoryItemIdsType == null)
                {
                    _log("ServerWorld: InventoryItemIDs type not found.");
                    return;
                }

                if (blockInventoryItemClassType == null)
                {
                    _log("ServerWorld: BlockInventoryItemClass type not found.");
                    return;
                }

                if (blockTypeEnumType == null)
                {
                    _log("ServerWorld: BlockTypeEnum type not found.");
                    return;
                }

                FieldInfo allItemsField = inventoryItemType.GetField(
                    "AllItems",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                if (allItemsField == null)
                {
                    _log("ServerWorld: InventoryItem.AllItems field not found.");
                    return;
                }

                object allItems = allItemsField.GetValue(null);

                if (allItems is System.Collections.ICollection beforeCollection &&
                    beforeCollection.Count > 0)
                {
                    _log("ServerWorld: Inventory registry already initialized. Items=" + beforeCollection.Count);
                    return;
                }

                MethodInfo registerMethod = inventoryItemType.GetMethod(
                    "RegisterItemClass",
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (registerMethod == null)
                {
                    _log("ServerWorld: InventoryItem.RegisterItemClass(...) not found.");
                    return;
                }

                ConstructorInfo placeholderCtor = blockInventoryItemClassType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    [
                        inventoryItemIdsType,
                        blockTypeEnumType,
                        typeof(string),
                        typeof(float)
                    ],
                    null);

                if (placeholderCtor == null)
                {
                    _log("ServerWorld: BlockInventoryItemClass placeholder constructor not found.");
                    return;
                }

                object dirtBlockType = Enum.Parse(blockTypeEnumType, "Dirt");

                int registered = 0;

                foreach (object itemId in Enum.GetValues(inventoryItemIdsType))
                {
                    try
                    {
                        // Server-only placeholder.
                        // The ID is the important part. This lets InventoryItem.Write(...) persist
                        // the original item id back to world.info.
                        object placeholderClass = placeholderCtor.Invoke(
                        [
                            itemId,
                            dirtBlockType,
                            "Server placeholder item class",
                            0.01f
                        ]);

                        registerMethod.Invoke(null, [placeholderClass]);
                        registered++;
                    }
                    catch (Exception ex)
                    {
                        Exception inner = ex is TargetInvocationException tie && tie.InnerException != null
                            ? tie.InnerException
                            : ex;

                        _log(
                            "ServerWorld: Failed to register placeholder inventory item " +
                            itemId +
                            ": " +
                            inner.GetType().FullName +
                            ": " +
                            inner.Message);
                    }
                }

                allItems = allItemsField.GetValue(null);

                int afterCount = 0;
                if (allItems is System.Collections.ICollection afterCollection)
                    afterCount = afterCollection.Count;

                _log("ServerWorld: Headless inventory registry initialized. Items=" + afterCount + ", attempted=" + registered);

                if (afterCount <= 0)
                    _log("ServerWorld: WARNING - Inventory registry is still empty after headless initialization.");
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;

                _log("ServerWorld InitInventoryRegistry: " + inner.GetType().FullName + ": " + inner.Message);
                _log(inner.StackTrace ?? "(no stack trace)");
            }
        }
        #endregion

        #region World Info / Spawn

        /// <summary>
        /// Loads the current world's world.info file through the reflected save device.
        ///
        /// Notes:
        /// - Prefers the WorldInfo(BinaryReader) constructor when available.
        /// - Falls back to default construction plus Load(BinaryReader).
        /// - Attempts to set SavePath on the loaded WorldInfo so later host-side logic has the correct relative path.
        /// </summary>
        private void LoadWorldInfo()
        {
            try
            {
                var worldInfoType = _gameAsm?.GetType("DNA.CastleMinerZ.WorldInfo");
                if (worldInfoType == null)
                {
                    _log("ServerWorld: WorldInfo type not found.");
                    return;
                }

                string relativePath = Path.Combine(_worldFolder, "world.info");

                object worldInfo = null;
                bool ok = InvokeSaveDeviceLoad(relativePath, stream =>
                {
                    using var reader = new BinaryReader(stream);
                    // Prefer the BinaryReader constructor because the game source confirms it exists.
                    var ctor = worldInfoType.GetConstructor(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        [typeof(BinaryReader)],
                        null);

                    if (ctor != null)
                    {
                        worldInfo = ctor.Invoke([reader]);
                        return;
                    }

                    // Fallback to default ctor + Load(BinaryReader)
                    var defaultCtor = worldInfoType.GetConstructor(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null) ?? throw new InvalidOperationException("WorldInfo constructors not found.");
                    worldInfo = defaultCtor.Invoke(null);

                    var loadMethod = worldInfoType.GetMethod(
                        "Load",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        [typeof(BinaryReader)],
                        null) ?? throw new InvalidOperationException("WorldInfo.Load(BinaryReader) not found.");
                    loadMethod.Invoke(worldInfo, [reader]);
                });

                if (!ok || worldInfo == null)
                {
                    _log("ServerWorld: FAILED to load world.info via SaveDevice. Trying world.info.bak...");

                    string backupPath = Path.Combine(_worldFolder, "world.info.bak");

                    object backupWorldInfo = null;
                    bool backupOk = InvokeSaveDeviceLoad(backupPath, stream =>
                    {
                        using var reader = new BinaryReader(stream);

                        var ctor = worldInfoType.GetConstructor(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            [typeof(BinaryReader)],
                            null);

                        if (ctor == null)
                            throw new InvalidOperationException("WorldInfo(BinaryReader) constructor not found.");

                        backupWorldInfo = ctor.Invoke([reader]);
                    });

                    if (!backupOk || backupWorldInfo == null)
                    {
                        _log("ServerWorld: FAILED to load world.info.bak.");
                        return;
                    }

                    _worldInfo = backupWorldInfo;
                    _log("ServerWorld: Recovered WorldInfo from world.info.bak.");
                    return;
                }

                _worldInfo = worldInfo;

                try
                {
                    var savePathProp = worldInfoType.GetProperty(
                        "SavePath",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (savePathProp != null && savePathProp.CanWrite)
                    {
                        savePathProp.SetValue(_worldInfo, _worldFolder, null);
                    }
                    else
                    {
                        var savePathField = worldInfoType.GetField(
                            "_savePath",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                        savePathField?.SetValue(_worldInfo, _worldFolder);
                    }
                }
                catch
                {
                }

                _log("ServerWorld: WorldInfo loaded.");
            }
            catch (TargetInvocationException tie)
            {
                Exception inner = tie.InnerException ?? tie;
                _log("ServerWorld LoadWorldInfo inner: " + inner.GetType().FullName + ": " + inner.Message);
                _log(inner.StackTrace ?? "(no stack trace)");
            }
            catch (Exception ex)
            {
                _log("ServerWorld LoadWorldInfo: " + ex.GetType().FullName + ": " + ex.Message);
                _log(ex.StackTrace ?? "(no stack trace)");
            }
        }

        /// <summary>
        /// Reads a spawn/location hint from the loaded WorldInfo.
        ///
        /// Notes:
        /// - Prefers LastPosition when present.
        /// - Falls back to the private _lastPosition field.
        /// - This position is later used by the spawn-based chunk-id list builder.
        /// </summary>
        private void ReadSpawnHintFromWorldInfo()
        {
            if (_worldInfo == null)
                return;

            try
            {
                var t = _worldInfo.GetType();

                // Prefer LastPosition if present.
                object vec =
                    t.GetProperty("LastPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_worldInfo, null) ??
                    t.GetField("_lastPosition", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_worldInfo);

                if (vec != null && TryReadVector3(vec, out float x, out float y, out float z))
                {
                    if (!float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z))
                    {
                        _spawnX = x;
                        _spawnY = y;
                        _spawnZ = z;
                    }
                }

                _log($"ServerWorld: Spawn hint = ({_spawnX}, {_spawnY}, {_spawnZ})");
            }
            catch (Exception ex)
            {
                _log("ServerWorld ReadSpawnHintFromWorldInfo: " + ex.Message);
            }
        }

        /// <summary>
        /// Attempts to read X/Y/Z components from a reflected vector-like object.
        ///
        /// Notes:
        /// - Supports both property-backed and field-backed vector types.
        /// - Used for WorldInfo position values.
        /// </summary>
        private static bool TryReadVector3(object vec, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;

            if (vec == null)
                return false;

            var t = vec.GetType();

            var px = t.GetProperty("X");
            var py = t.GetProperty("Y");
            var pz = t.GetProperty("Z");

            if (px != null && py != null && pz != null)
            {
                x = Convert.ToSingle(px.GetValue(vec, null));
                y = Convert.ToSingle(py.GetValue(vec, null));
                z = Convert.ToSingle(pz.GetValue(vec, null));
                return true;
            }

            var fx = t.GetField("X");
            var fy = t.GetField("Y");
            var fz = t.GetField("Z");

            if (fx != null && fy != null && fz != null)
            {
                x = Convert.ToSingle(fx.GetValue(vec));
                y = Convert.ToSingle(fy.GetValue(vec));
                z = Convert.ToSingle(fz.GetValue(vec));
                return true;
            }

            return false;
        }
        #endregion

        #region Public Host Message Entry

        /// <summary>
        /// Processes messages addressed to the host (recipient id 0).
        ///
        /// Return behavior:
        /// - true: the message was consumed by the host and should not be relayed.
        /// - false: the message was not consumed and the outer network layer may relay it normally.
        ///
        /// Notes:
        /// - World-info, chunk-list, chunk-data, and inventory-request/store messages are host-consumed.
        /// - Several mutation messages are applied on the host and then still relayed to peers.
        /// </summary>
        public bool TryHandleHostMessage(
            byte recipientId,
            byte senderId,
            byte[] data,
            object senderConn,
            object connections,
            Dictionary<object, object> connectionToGamer,
            Action<object, byte[], byte> sendToClient)
        {
            if (recipientId != 0)
                return false;

            if (data == null || data.Length < 2)
                return false;

            byte messageId = data[0];
            if (!_messageIdToType.TryGetValue(messageId, out string typeName))
                return false;

            // Consumed by host, not relayed.
            // These messages are direct client-to-host requests. The host answers them and stops relay.
            if (typeName == "DNA.CastleMinerZ.Net.RequestWorldInfoMessage")
            {
                HandleRequestWorldInfo(senderId, senderConn, sendToClient);
                return true;
            }

            if (typeName == "DNA.CastleMinerZ.Net.ClientReadyForChunksMessage")
            {
                HandleClientReadyForChunks(senderId, senderConn, sendToClient);
                return true;
            }

            if (typeName == "DNA.CastleMinerZ.Net.RequestChunkMessage")
            {
                HandleRequestChunk(senderId, data, senderConn, sendToClient);
                return true;
            }

            if (typeName == "DNA.CastleMinerZ.Net.RequestInventoryMessage")
            {
                HandleRequestInventoryMessage(senderId, senderConn, connectionToGamer, sendToClient);
                return true;
            }

            if (typeName == "DNA.CastleMinerZ.Net.InventoryStoreOnServerMessage")
            {
                HandleInventoryStoreOnServerMessage(senderId, data, senderConn, connectionToGamer);
                return true;
            }

            // Apply on host, then allow normal relay to peers.
            // These messages mutate host-side state but still need to reach other clients when allowed.
            if (typeName == "DNA.CastleMinerZ.Net.PlayerExistsMessage")
            {
                HandlePlayerExistsMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.AlterBlockMessage")
            {
                // Give server plugins first chance to consume protected edits.
                if (TryRunWorldPlugins(typeName, senderId, data, senderConn, connectionToGamer, sendToClient))
                    return true;

                HandleAlterBlockMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.DetonateExplosiveMessage")
            {
                if (TryRunWorldPlugins(typeName, senderId, data, senderConn, connectionToGamer, sendToClient))
                    return true;

                // Not consumed. Let normal relay continue.
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.RemoveBlocksMessage")
            {
                if (TryRunWorldPlugins(typeName, senderId, data, senderConn, connectionToGamer, sendToClient))
                    return true;

                HandleRemoveBlocksMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.DestroyCustomBlockMessage")
            {
                HandleDestroyCustomBlockMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.DoorOpenCloseMessage")
            {
                HandleDoorOpenCloseMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.ItemCrateMessage")
            {
                if (TryRunWorldPlugins(typeName, senderId, data, senderConn, connectionToGamer, sendToClient))
                    return true;

                HandleItemCrateMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.DestroyCrateMessage")
            {
                if (TryRunWorldPlugins(typeName, senderId, data, senderConn, connectionToGamer, sendToClient))
                    return true;

                HandleDestroyCrateMessage(senderId, data);
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.DigMessage")
            {
                CacheDigMessage(senderId, data);

                // Do not consume it. Let the normal relay continue.
                return false;
            }

            if (typeName == "DNA.CastleMinerZ.Net.CreatePickupMessage")
            {
                HandleCreatePickupMessage(senderId, data);
                return false; // still relay so peers can see the spawned pickup
            }

            if (typeName == "DNA.CastleMinerZ.Net.RequestPickupMessage")
            {
                HandleRequestPickupMessage(senderId, data, senderConn, connections, connectionToGamer, sendToClient);

                // RequestPickupMessage is host-authoritative.
                // The host resolves it by sending ConsumePickupMessage, so the original request
                // should not be relayed afterward.
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears per-player transient tracking when a client disconnects.
        /// </summary>
        public void OnClientDisconnected(byte playerId)
        {
            _chunkRequestLoggedForPlayer.Remove(playerId);
        }
        #endregion

        #region Message Handlers

        #region World / chunk request handlers

        /// <summary>
        /// Serializes the loaded WorldInfo into a WorldInfoMessage payload and sends it to the requesting client.
        /// </summary>
        private void HandleRequestWorldInfo(byte senderId, object senderConn, Action<object, byte[], byte> sendToClient)
        {
            if (_worldInfo == null)
            {
                _log("ServerWorld: No WorldInfo available to send.");
                return;
            }

            try
            {
                if (!_typeToMessageId.TryGetValue("DNA.CastleMinerZ.Net.WorldInfoMessage", out byte msgId))
                {
                    _log("ServerWorld: WorldInfoMessage ID not found.");
                    return;
                }

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(msgId);

                    var saveMethod = _worldInfo.GetType().GetMethod("Save", [typeof(BinaryWriter)]);
                    saveMethod?.Invoke(_worldInfo, [writer]);

                    writer.Flush();

                    int len = (int)ms.Position;
                    writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

                    sendToClient(senderConn, ms.ToArray(), senderId);

                    _log(
                        $"ServerWorld: Sent WorldInfo to player {senderId}. " +
                        $"Bytes={ms.Length}, " +
                        $"Crates={GetWorldInfoDictionaryCount("Crates")}, " +
                        $"Doors={GetWorldInfoDictionaryCount("Doors")}, " +
                        $"Spawners={GetWorldInfoDictionaryCount("Spawners")}");
                }

                _log("ServerWorld: Sent WorldInfo to player " + senderId);
            }
            catch (Exception ex)
            {
                _log("ServerWorld HandleRequestWorldInfo: " + ex.Message);
            }
        }

        /// <summary>
        /// Builds and sends the chunk-id list used by the client to determine which deltas it may request next.
        ///
        /// Notes:
        /// - This implementation uses a spawn-centered radius list builder.
        /// </summary>
        private void HandleClientReadyForChunks(byte senderId, object senderConn, Action<object, byte[], byte> sendToClient)
        {
            try
            {
                if (!_typeToMessageId.TryGetValue("DNA.CastleMinerZ.Net.ProvideDeltaListMessage", out byte msgId))
                    return;

                int[] chunkIds = BuildChunkIdListFromSavedFiles();

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(msgId);
                    writer.Write(chunkIds.Length);

                    for (int i = 0; i < chunkIds.Length; i++)
                        writer.Write(chunkIds[i]);

                    writer.Flush();

                    int len = (int)ms.Position;
                    writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

                    sendToClient(senderConn, ms.ToArray(), senderId);
                }

                _log($"ServerWorld: Sent chunk list ({chunkIds.Length} chunks) to player {senderId}");
            }
            catch (Exception ex)
            {
                _log("ServerWorld HandleClientReadyForChunks: " + ex.Message);
            }
        }

        /// <summary>
        /// Loads a single chunk delta and sends it back as a ProvideChunkMessage payload.
        ///
        /// Notes:
        /// - The request packet layout is read directly from the raw byte array.
        /// - A null or empty delta means the client should fall back to procedural terrain generation.
        /// </summary>
        private void HandleRequestChunk(byte senderId, byte[] data, object senderConn, Action<object, byte[], byte> sendToClient)
        {
            if (data == null || data.Length < 14)
                return;

            try
            {
                int blockX = BitConverter.ToInt32(data, 1);
                int blockY = BitConverter.ToInt32(data, 5);
                int blockZ = BitConverter.ToInt32(data, 9);
                int priority = data[13];

                int chunkX = FloorToChunk(blockX);
                int chunkY = FloorToChunk(blockY);
                int chunkZ = FloorToChunk(blockZ);

                string chunkKey = BuildChunkKey(chunkX, chunkY, chunkZ);

                if (!_chunkCache.TryGetValue(chunkKey, out int[] delta))
                {
                    delta = LoadChunkDelta(chunkX, chunkY, chunkZ);
                    AddChunkToCache(chunkKey, delta);
                }
                else
                {
                    TouchChunkCacheEntry(chunkKey);
                }

                if (!_typeToMessageId.TryGetValue("DNA.CastleMinerZ.Net.ProvideChunkMessage", out byte msgId))
                    return;

                if (_chunkRequestLoggedForPlayer.Add(senderId))
                    _log($"ServerWorld: First chunk request from player {senderId} near ({chunkX},{chunkY},{chunkZ})");

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writer.Write(msgId);
                writer.Write(chunkX);
                writer.Write(chunkY);
                writer.Write(chunkZ);
                writer.Write((byte)priority);

                if (delta != null && delta.Length > 0)
                {
                    writer.Write(delta.Length);
                    for (int i = 0; i < delta.Length; i++)
                        writer.Write(delta[i]);
                }
                else
                {
                    // Null / empty delta tells the client to fall back to procedural terrain from world seed.
                    writer.Write(0);
                }

                writer.Flush();

                int len = (int)ms.Position;
                writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

                sendToClient(senderConn, ms.ToArray(), senderId);
            }
            catch (Exception ex)
            {
                _log("ServerWorld HandleRequestChunk: " + ex.Message);
            }
        }
        #endregion

        #region Inventory request / store handlers

        /// <summary>
        /// Loads an inventory for the requesting gamer and sends an InventoryRetrieveFromServerMessage payload.
        ///
        /// Notes:
        /// - Falls back to a default inventory template when no saved inventory exists.
        /// - Inventory bytes are sent in the same raw format produced by PlayerInventory.Save(...).
        /// </summary>
        private void HandleRequestInventoryMessage(
            byte senderId,
            object senderConn,
            Dictionary<object, object> connectionToGamer,
            Action<object, byte[], byte> sendToClient)
        {
            try
            {
                if (senderConn == null)
                {
                    _log("ServerWorld: RequestInventory senderConn was null.");
                    return;
                }

                if (!connectionToGamer.TryGetValue(senderConn, out var gamer) || gamer == null)
                {
                    _log("ServerWorld: RequestInventory gamer not found.");
                    return;
                }

                bool isDefault = false;
                byte[] invBytes = LoadRawInventoryBytesForGamer(gamer);

                if (invBytes == null || invBytes.Length == 0)
                {
                    isDefault = true;
                    invBytes = LoadDefaultInventoryTemplate();
                }

                if (invBytes == null || invBytes.Length == 0)
                {
                    _log("ServerWorld: Failed to get inventory bytes.");
                    return;
                }

                byte[] payload = BuildInventoryRetrieveFromServerPayload(invBytes, senderId, isDefault);
                if (payload == null || payload.Length == 0)
                {
                    _log("ServerWorld: Failed to build InventoryRetrieveFromServerMessage payload.");
                    return;
                }

                sendToClient(senderConn, payload, senderId);
                _log("ServerWorld: Sent inventory to player " + senderId + (isDefault ? " [default]" : ""));
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;

                _log("ServerWorld HandleRequestInventoryMessage: " + inner.GetType().FullName + ": " + inner.Message);
                _log(inner.StackTrace ?? "(no stack trace)");
            }
        }

        /// <summary>
        /// Loads a default inventory template from storage.
        ///
        /// Notes:
        /// - Expects a known-good default inventory file at build\ServerHost\Inventory\DEFAULT.inv.
        /// </summary>
        private byte[] LoadDefaultInventoryTemplate()
        {
            try
            {
                // Put a known-good template inventory in the world folder.
                string relativePath = Path.Combine(_saveRoot, "Inventory", "DEFAULT.inv");

                byte[] result = null;

                bool ok = InvokeSaveDeviceLoad(relativePath, stream =>
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    result = ms.ToArray();
                });

                if (!ok || result == null || result.Length == 0)
                {
                    _log("ServerWorld: Failed to load default inventory template from " + relativePath);
                    return null;
                }

                return result;
            }
            catch (Exception ex)
            {
                _log("ServerWorld LoadDefaultInventoryTemplate: " + ex.GetType().FullName + ": " + ex.Message);
                _log(ex.StackTrace ?? "(no stack trace)");
                return null;
            }
        }

        /// <summary>
        /// Extracts raw inventory bytes from an InventoryStoreOnServerMessage packet and persists them for the sender.
        ///
        /// Notes:
        /// - The raw PlayerInventory bytes are copied directly out of the packet body.
        /// - The trailing FinalSave flag is logged but not otherwise interpreted here.
        /// </summary>
        private void HandleInventoryStoreOnServerMessage(
            byte senderId,
            byte[] data,
            object senderConn,
            Dictionary<object, object> connectionToGamer)
        {
            try
            {
                if (senderConn == null)
                {
                    _log("ServerWorld: InventoryStore senderConn was null.");
                    return;
                }

                if (!connectionToGamer.TryGetValue(senderConn, out var gamer) || gamer == null)
                {
                    _log("ServerWorld: InventoryStore gamer not found.");
                    return;
                }

                // Layout:
                // [0]     = message id
                // [1..N]  = raw PlayerInventory.Save(...) bytes
                // [N+1]   = FinalSave bool
                // [last]  = checksum
                if (data == null || data.Length < 4)
                {
                    _log("ServerWorld: InventoryStore payload too small.");
                    return;
                }

                int invLen = data.Length - 3; // remove message id + finalSave + checksum
                if (invLen <= 0)
                {
                    _log("ServerWorld: InventoryStore inventory length invalid.");
                    return;
                }

                byte[] invBytes = new byte[invLen];
                Buffer.BlockCopy(data, 1, invBytes, 0, invLen);

                bool finalSave = data[data.Length - 2] != 0;

                SaveRawInventoryBytesForGamer(gamer, invBytes);
                _log("ServerWorld: Saved inventory for player " + senderId + (finalSave ? " [final]" : ""));
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;

                _log("ServerWorld HandleInventoryStoreOnServerMessage: " + inner.GetType().FullName + ": " + inner.Message);
                _log(inner.StackTrace ?? "(no stack trace)");
            }
        }

        /// <summary>
        /// Attempts to load a saved inventory for a gamer using current and legacy hash paths.
        /// </summary>
        private byte[] LoadRawInventoryBytesForGamer(object gamer)
        {
            string primaryPath = GetInventoryRelativePathForGamer(gamer, useOldHash: false);
            string oldPath = GetInventoryRelativePathForGamer(gamer, useOldHash: true);

            byte[] data = TryLoadRawFileFromStorage(primaryPath);
            if (data != null && data.Length > 0)
                return data;

            if (!string.Equals(primaryPath, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                data = TryLoadRawFileFromStorage(oldPath);
                if (data != null && data.Length > 0)
                    return data;
            }

            return null;
        }

        /// <summary>
        /// Saves raw inventory bytes for a gamer to the resolved inventory path.
        /// </summary>
        private void SaveRawInventoryBytesForGamer(object gamer, byte[] invBytes)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));
            if (invBytes == null)
                throw new ArgumentNullException(nameof(invBytes));

            string relativePath = GetInventoryRelativePathForGamer(gamer, useOldHash: false);

            string dir = Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrWhiteSpace(dir) && !SaveDeviceDirectoryExists(dir))
            {
                SaveDeviceCreateDirectory(dir);
            }

            InvokeSaveDeviceSave(relativePath, stream =>
            {
                stream.Write(invBytes, 0, invBytes.Length);
                stream.Flush();
            });
        }

        /// <summary>
        /// Loads a raw file from the reflected save device.
        /// </summary>
        private byte[] TryLoadRawFileFromStorage(string relativePath)
        {
            if (_saveDevice == null || string.IsNullOrWhiteSpace(relativePath))
                return null;

            byte[] result = null;

            bool ok = InvokeSaveDeviceLoad(relativePath, stream =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                result = ms.ToArray();
            });

            return !ok ? null : result;
        }

        /// <summary>
        /// Invokes SaveDevice.Save(string, bool, bool, FileAction) via reflection.
        /// </summary>
        private bool InvokeSaveDeviceSave(string relativePath, Action<Stream> callback)
        {
            if (_saveDevice == null)
                return false;

            try
            {
                MethodInfo saveMethod = null;

                foreach (var m in _saveDevice.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (m.Name != "Save")
                        continue;

                    var pars = m.GetParameters();
                    if (pars.Length == 4 &&
                        pars[0].ParameterType == typeof(string) &&
                        pars[1].ParameterType == typeof(bool) &&
                        pars[2].ParameterType == typeof(bool))
                    {
                        saveMethod = m;
                        break;
                    }
                }

                if (saveMethod == null)
                {
                    _log("ServerWorld: SaveDevice.Save(string, bool, bool, FileAction) not found.");
                    return false;
                }

                Type fileActionType = saveMethod.GetParameters()[3].ParameterType;

                var proxy = new StreamLoadProxy { Callback = callback };
                Delegate fileAction = Delegate.CreateDelegate(fileActionType, proxy, nameof(StreamLoadProxy.Invoke));

                saveMethod.Invoke(_saveDevice, [relativePath, true, true, fileAction]);
                return true;
            }
            catch (Exception ex)
            {
                _log("ServerWorld InvokeSaveDeviceSave: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Checks whether a directory exists in the reflected save device.
        /// </summary>
        private bool SaveDeviceDirectoryExists(string relativePath)
        {
            try
            {
                MethodInfo mi = _saveDevice.GetType().GetMethod("DirectoryExists", [typeof(string)]);
                return mi != null && (bool)mi.Invoke(_saveDevice, [relativePath]);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a directory in the reflected save device.
        /// </summary>
        private void SaveDeviceCreateDirectory(string relativePath)
        {
            try
            {
                MethodInfo mi = _saveDevice.GetType().GetMethod("CreateDirectory", [typeof(string)]);
                mi?.Invoke(_saveDevice, [relativePath]);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Resolves the relative inventory save path for a gamer.
        ///
        /// Notes:
        /// - Prefers AlternateAddress when not using the legacy path mode.
        /// - Falls back to hashed Gamertag.
        /// </summary>
        private string GetInventoryRelativePathForGamer(object gamer, bool useOldHash)
        {
            if (gamer == null)
                throw new ArgumentNullException(nameof(gamer));

            Type gamerType = gamer.GetType();

            string gamertag =
                gamerType.GetProperty("Gamertag", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         ?.GetValue(gamer, null) as string;

            if (string.IsNullOrWhiteSpace(gamertag))
                gamertag = "Player";

            if (!useOldHash)
            {
                try
                {
                    object altObj =
                        gamerType.GetProperty("AlternateAddress", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?.GetValue(gamer, null);

                    if (altObj != null)
                    {
                        ulong alt = Convert.ToUInt64(altObj);
                        if (alt != 0UL)
                            return Path.Combine(_worldFolder, ComputeVanillaHashString(alt.ToString()) + ".inv");
                    }
                }
                catch
                {
                }
            }

            return Path.Combine(_worldFolder, ComputeVanillaHashString(gamertag) + ".inv");
        }

        /// <summary>
        /// Computes the game's MD5-based hashed save-name string.
        /// </summary>
        private string ComputeVanillaHashString(string value)
        {
            Type md5Type = ResolveType("DNA.Security.Cryptography.MD5HashProvider") ?? throw new InvalidOperationException("MD5HashProvider type not found.");
            object md5 = Activator.CreateInstance(md5Type);

            MethodInfo computeMethod = md5Type.GetMethod(
                "Compute",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [typeof(byte[])],
                null) ?? throw new InvalidOperationException("MD5HashProvider.Compute(byte[]) not found.");

            object hash = computeMethod.Invoke(md5, [Encoding.UTF8.GetBytes(value ?? string.Empty)]) ?? throw new InvalidOperationException("MD5 hash result was null.");
            return hash.ToString();
        }

        /// <summary>
        /// Builds an InventoryRetrieveFromServerMessage payload from raw inventory bytes.
        ///
        /// Notes:
        /// - Packet layout is: message id + raw inventory bytes + playerId + isDefault + checksum.
        /// </summary>
        private byte[] BuildInventoryRetrieveFromServerPayload(byte[] invBytes, byte playerId, bool isDefault)
        {
            if (invBytes == null || invBytes.Length == 0)
                return null;

            if (!_typeToMessageId.TryGetValue("DNA.CastleMinerZ.Net.InventoryRetrieveFromServerMessage", out byte msgId))
            {
                _log("ServerWorld: InventoryRetrieveFromServerMessage ID not found.");
                return null;
            }

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(msgId);

            // Raw contents of PlayerInventory.Save(...)
            writer.Write(invBytes);

            writer.Write(playerId);
            writer.Write(isDefault);
            writer.Flush();

            int len = (int)ms.Position;
            writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

            return ms.ToArray();
        }
        #endregion

        #region Reflected message application handlers

        /// <summary>
        /// Logs sender identity details from a PlayerExistsMessage.
        ///
        /// Notes:
        /// - The dedicated host does not instantiate the original Player type here.
        /// - Connection/gamer mapping is already sufficient for inventory persistence.
        /// </summary>
        private void HandlePlayerExistsMessage(byte senderId, byte[] data)
        {
            try
            {
                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.PlayerExistsMessage", data);
                object gamer = GetMemberValue(msg, "Gamer");
                string gamertag = gamer != null ? Convert.ToString(GetMemberValue(gamer, "Gamertag")) : "<unknown>";
                bool requestResponse = Convert.ToBoolean(GetMemberValue(msg, "RequestResponse") ?? false);

                _log($"[HostMsg] PlayerExistsMessage from player {senderId}, gamertag='{gamertag}', requestResponse={requestResponse}");

                // Dedicated host does not need to instantiate the real game Player object
                // unless you want full sender.Tag parity with vanilla.
                // Your inventory system is already keyed by gamer/connection, which is enough.
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandlePlayerExistsMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }

        /// <summary>
        /// Applies an in-memory door open/close mutation to WorldInfo.Doors.
        /// </summary>
        private void HandleDoorOpenCloseMessage(byte senderId, byte[] data)
        {
            try
            {
                if (_worldInfo == null)
                    return;

                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.DoorOpenCloseMessage", data);
                object location = GetMemberValue(msg, "Location");
                bool opened = Convert.ToBoolean(GetMemberValue(msg, "Opened") ?? false);

                object doors = GetMemberValue(_worldInfo, "Doors");
                if (doors == null || location == null)
                    return;

                if (TryDictionaryTryGetValue(doors, location, out object door) && door != null)
                {
                    if (!TrySetMemberValue(door, "Open", opened))
                        TrySetMemberValue(door, "_open", opened);

                    _log($"[HostMsg] DoorOpenCloseMessage from player {senderId} at {FormatIntVector3(location)} -> Open={opened}");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandleDoorOpenCloseMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }

        /// <summary>
        /// Applies a crate inventory update to WorldInfo without allowing bogus/null crate
        /// messages to create permanent empty crate entries in world.info.
        /// </summary>
        /// <remarks>
        /// Vanilla ItemCrateMessage.Apply(WorldInfo) calls GetCrate(location, true), which means
        /// even a null-item update can create a new Crate entry. On a public dedicated server,
        /// that can bloat or poison world.info until new clients fail during WorldInfoMessage load.
        /// </remarks>
        private void HandleItemCrateMessage(byte senderId, byte[] data)
        {
            try
            {
                if (_worldInfo == null)
                    return;

                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.ItemCrateMessage", data);
                object location = GetMemberValue(msg, "Location");
                object item = GetMemberValue(msg, "Item");
                object crates = GetMemberValue(_worldInfo, "Crates");

                if (location == null || crates == null)
                    return;

                int index;
                try
                {
                    index = Convert.ToInt32(GetMemberValue(msg, "Index") ?? -1);
                }
                catch
                {
                    _log($"[HostMsg] Rejected ItemCrateMessage from player {senderId}: invalid index value.");
                    return;
                }

                if (index < 0 || index >= 32)
                {
                    _log($"[HostMsg] Rejected ItemCrateMessage from player {senderId}: index {index} is outside 0-31.");
                    return;
                }

                lock (_worldInfoLock)
                {
                    bool existed = TryDictionaryTryGetValue(crates, location, out object crate) && crate != null;

                    // Do not let a null-item packet create a brand-new empty crate entry.
                    if (!existed && item == null)
                    {
                        _log($"[HostMsg] Ignored null ItemCrateMessage from player {senderId} for non-existing crate at {FormatIntVector3(location)}.");
                        return;
                    }

                    // If this is a brand-new crate entry, make sure the location is actually a saved crate/container block.
                    // This prevents clients from creating fake crate records anywhere in world.info.
                    if (!existed && !IsSavedCrateBlockLocation(location))
                    {
                        _log($"[HostMsg] Rejected ItemCrateMessage from player {senderId}: no saved crate block at {FormatIntVector3(location)}.");
                        return;
                    }

                    if (!existed)
                    {
                        MethodInfo getCrateMethod = _worldInfo.GetType().GetMethod(
                            "GetCrate",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            [location.GetType(), typeof(bool)],
                            null);

                        if (getCrateMethod == null)
                        {
                            _log("ServerWorld: WorldInfo.GetCrate(IntVector3, bool) not found.");
                            return;
                        }

                        crate = getCrateMethod.Invoke(_worldInfo, [location, true]);
                    }

                    if (crate == null)
                        return;

                    if (GetMemberValue(crate, "Inventory") is not Array inventory || index >= inventory.Length)
                    {
                        _log($"[HostMsg] Rejected ItemCrateMessage from player {senderId}: crate inventory was invalid.");
                        return;
                    }

                    inventory.SetValue(item, index);

                    // If the crate has no stored items left, remove it from world.info.
                    // The actual crate block is stored in chunk delta files, not in this crate inventory dictionary.
                    if (IsCrateEmpty(crate))
                        DictionaryRemove(crates, location);

                    int pruned = PruneEmptyCrates();

                    SaveWorldInfo();

                    _log($"[HostMsg] ItemCrateMessage from player {senderId} saved. Location={FormatIntVector3(location)}, Index={index}, PrunedEmptyCrates={pruned}");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandleItemCrateMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }

        /// <summary>
        /// Marks a crate as destroyed and removes it from the reflected WorldInfo.Crates dictionary.
        /// </summary>
        private void HandleDestroyCrateMessage(byte senderId, byte[] data)
        {
            try
            {
                if (_worldInfo == null)
                    return;

                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.DestroyCrateMessage", data);
                object location = GetMemberValue(msg, "Location");
                object crates = GetMemberValue(_worldInfo, "Crates");

                if (crates == null || location == null)
                    return;

                lock (_worldInfoLock)
                {
                    if (TryDictionaryTryGetValue(crates, location, out object crate) && crate != null)
                    {
                        TrySetMemberValue(crate, "Destroyed", true);
                        TrySetMemberValue(crate, "_destroyed", true);
                        DictionaryRemove(crates, location);

                        // Important:
                        // Destroying a crate mutates WorldInfo.Crates, so save world.info immediately.
                        SaveWorldInfo();

                        _log($"[HostMsg] DestroyCrateMessage from player {senderId} at {FormatIntVector3(location)} saved to world.");
                    }
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandleDestroyCrateMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }

        /// <summary>
        /// Marks a custom block/door as destroyed and removes it from the reflected WorldInfo.Doors dictionary.
        /// </summary>
        private void HandleDestroyCustomBlockMessage(byte senderId, byte[] data)
        {
            try
            {
                if (_worldInfo == null)
                    return;

                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.DestroyCustomBlockMessage", data);
                object location = GetMemberValue(msg, "Location");
                object doors = GetMemberValue(_worldInfo, "Doors");

                if (doors == null || location == null)
                    return;

                if (TryDictionaryTryGetValue(doors, location, out object door) && door != null)
                {
                    TrySetMemberValue(door, "Destroyed", true);
                    TrySetMemberValue(door, "_destroyed", true);
                    DictionaryRemove(doors, location);

                    _log($"[HostMsg] DestroyCustomBlockMessage from player {senderId} at {FormatIntVector3(location)}");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandleDestroyCustomBlockMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }

        /// <summary>
        /// Applies a single block mutation to the authoritative saved terrain delta.
        /// </summary>
        private void HandleAlterBlockMessage(byte senderId, byte[] data)
        {
            try
            {
                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.AlterBlockMessage", data);
                object location = GetMemberValue(msg, "BlockLocation");
                object blockType = GetMemberValue(msg, "BlockType");

                if (!TryReadIntVector3(location, out int x, out int y, out int z))
                    return;

                int blockTypeValue = Convert.ToInt32(blockType);

                ApplyTerrainBlockChange(x, y, z, blockTypeValue);

                if (_logHostMessages)
                {
                    _log($"[HostMsg] AlterBlockMessage from player {senderId} at ({x},{y},{z}) -> {blockTypeValue}");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandleAlterBlockMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }

        /// <summary>
        /// Applies a batch of block removals to the authoritative saved terrain delta.
        /// </summary>
        private void HandleRemoveBlocksMessage(byte senderId, byte[] data)
        {
            try
            {
                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.RemoveBlocksMessage", data);

                int numBlocks = Convert.ToInt32(GetMemberValue(msg, "NumBlocks") ?? 0);

                if (GetMemberValue(msg, "BlocksToRemove") is not Array blocks || numBlocks <= 0)
                    return;

                int count = Math.Min(numBlocks, blocks.Length);

                for (int i = 0; i < count; i++)
                {
                    object location = blocks.GetValue(i);
                    if (!TryReadIntVector3(location, out int x, out int y, out int z))
                        continue;

                    ApplyTerrainBlockChange(x, y, z, 0);
                }

                _log($"[HostMsg] RemoveBlocksMessage from player {senderId}, count={count}");
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _log("ServerWorld HandleRemoveBlocksMessage: " + inner.GetType().FullName + ": " + inner.Message);
            }
        }
        #endregion

        #region Terrain delta persistence helpers

        /// <summary>
        /// Applies or updates a single block entry in the saved delta for its terrain chunk.
        ///
        /// Notes:
        /// - Coordinates are normalized into the chunk-local delta format used by CastleMiner Z.
        /// - If an existing delta entry is found for the same location, it is replaced.
        /// - Otherwise a new entry is appended.
        /// </summary>
        private void ApplyTerrainBlockChange(int worldX, int worldY, int worldZ, int blockTypeValue)
        {
            int chunkX = MakeTerrainChunkCorner(worldX);
            int chunkZ = MakeTerrainChunkCorner(worldZ);

            int localX = worldX - chunkX;
            int localY = worldY - TerrainChunkMinY;
            int localZ = worldZ - chunkZ;

            if (localX < 0 || localX > 15 || localZ < 0 || localZ > 15 || localY < 0 || localY >= TerrainChunkHeight)
            {
                _log($"ServerWorld ApplyTerrainBlockChange: out-of-range local coord for ({worldX},{worldY},{worldZ})");
                return;
            }

            string chunkKey = BuildChunkKey(chunkX, TerrainChunkMinY, chunkZ);

            if (!_chunkCache.TryGetValue(chunkKey, out int[] delta))
            {
                delta = LoadChunkDelta(chunkX, TerrainChunkMinY, chunkZ);
            }

            delta ??= [];

            int newEntry = MakeDeltaEntry(localX, localY, localZ, blockTypeValue);

            bool found = false;
            int[] working = delta;

            for (int i = 0; i < working.Length; i++)
            {
                if (SameDeltaLocation(working[i], newEntry))
                {
                    found = true;

                    if (working[i] != newEntry)
                    {
                        // clone only when changing
                        working = (int[])working.Clone();
                        working[i] = newEntry;
                    }

                    break;
                }
            }

            if (!found)
            {
                int oldLen = working.Length;
                Array.Resize(ref working, oldLen + 1);
                working[oldLen] = newEntry;
            }

            SaveChunkDelta(chunkX, chunkZ, working);
            AddChunkToCache(chunkKey, working);
        }

        /// <summary>Chunk disk format magic used by newer chunk delta files.</summary>
        private const uint ChunkDiskMagic = 3203334144U;

        /// <summary>CastleMiner Z terrain chunk minimum Y.</summary>
        private const int TerrainChunkMinY = -64;

        /// <summary>CastleMiner Z terrain chunk vertical height.</summary>
        private const int TerrainChunkHeight = 128;

        /// <summary>
        /// Converts a world coordinate to the minimum block coordinate of its 16-wide terrain chunk.
        /// </summary>
        private static int MakeTerrainChunkCorner(int worldCoord)
        {
            return (int)(Math.Floor(worldCoord / 16.0) * 16.0);
        }

        /// <summary>
        /// Packs a local block position plus block type into the same int layout used by terrain delta entries.
        /// </summary>
        private static int MakeDeltaEntry(int localX, int localY, int localZ, int blockTypeValue)
        {
            return ((blockTypeValue & 0xFF) << 24)
                 | ((localX & 0x0F) << 16)
                 | ((localY & 0x7F) << 8)
                 | (localZ & 0x0F);
        }

        /// <summary>
        /// Returns true when two packed delta entries refer to the same local block location.
        /// </summary>
        private static bool SameDeltaLocation(int a, int b)
        {
            // same mask used by DeltaEntry.SameLocation(...)
            return ((a ^ b) & 0x0F7F0F) == 0;
        }

        /// <summary>
        /// Saves a chunk delta file in the newer magic + count + raw-int format.
        /// </summary>
        private void SaveChunkDelta(int chunkX, int chunkZ, int[] delta)
        {
            try
            {
                string relativePath = Path.Combine(_worldFolder, $"X{chunkX}Y{TerrainChunkMinY}Z{chunkZ}.dat");

                bool ok = InvokeSaveDeviceSave(relativePath, stream =>
                {
                    using var writer = new BinaryWriter(stream);
                    writer.Write(ChunkDiskMagic);
                    writer.Write(delta?.Length ?? 0);

                    if (delta != null)
                    {
                        for (int i = 0; i < delta.Length; i++)
                            writer.Write(delta[i]);
                    }

                    writer.Flush();
                });

                if (!ok)
                    _log($"ServerWorld: Failed to save chunk X{chunkX}Y{TerrainChunkMinY}Z{chunkZ}.dat");
            }
            catch (Exception ex)
            {
                _log($"ServerWorld SaveChunkDelta({chunkX},{chunkZ}): {ex.Message}");
            }
        }
        #endregion

        #region Reflection / object member helpers

        /// <summary>
        /// Safely converts an arbitrary object to bool, returning false on failure.
        /// </summary>
        private bool ToBool(object value)
        {
            if (value == null)
                return false;

            try { return Convert.ToBoolean(value); }
            catch { return false; }
        }

        /// <summary>
        /// Persists the current reflected WorldInfo using a temp file + validation + backup replacement.
        /// This prevents a crash/interruption from leaving the live world.info half-written.
        /// </summary>
        private void SaveWorldInfo()
        {
            if (_worldInfo == null || _saveDevice == null)
            {
                _log("ServerWorld: SaveWorldInfo skipped because worldInfo or saveDevice was null.");
                return;
            }

            lock (_worldInfoLock)
            {
                try
                {
                    MethodInfo saveMethod = _worldInfo.GetType().GetMethod(
                        "Save",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        [typeof(BinaryWriter)],
                        null);

                    if (saveMethod == null)
                    {
                        _log("ServerWorld: WorldInfo.Save(BinaryWriter) not found.");
                        return;
                    }

                    // Remove empty crate inventory entries before saving.
                    int pruned = PruneEmptyCrates();

                    string liveRelativePath = Path.Combine(_worldFolder, "world.info");
                    string tempRelativePath = Path.Combine(_worldFolder, "world.info.tmp");
                    string backupRelativePath = Path.Combine(_worldFolder, "world.info.bak");

                    bool ok = InvokeSaveDeviceSave(tempRelativePath, stream =>
                    {
                        using var writer = new BinaryWriter(stream);
                        saveMethod.Invoke(_worldInfo, [writer]);
                        writer.Flush();
                    });

                    if (!ok)
                    {
                        _log("ServerWorld: FAILED to save temporary world.info.");
                        return;
                    }

                    if (!TryValidateWorldInfoFile(tempRelativePath))
                    {
                        _log("ServerWorld: Temporary world.info failed validation. Live world.info was not replaced.");
                        TryDeletePhysicalSaveFile(tempRelativePath);
                        return;
                    }

                    ReplacePhysicalSaveFile(tempRelativePath, liveRelativePath, backupRelativePath);

                    _log($"ServerWorld: world.info saved atomically. PrunedEmptyCrates={pruned}");
                }
                catch (Exception ex)
                {
                    Exception inner = ex is TargetInvocationException tie && tie.InnerException != null
                        ? tie.InnerException
                        : ex;

                    _log("ServerWorld SaveWorldInfo: " + inner.GetType().FullName + ": " + inner.Message);
                    _log(inner.StackTrace ?? "(no stack trace)");
                }
            }
        }

        /// <summary>
        /// Reconstructs a reflected game message instance from raw packet bytes.
        ///
        /// Notes:
        /// - Expects packet layout: [messageId][payload...][checksum].
        /// - Invokes the original RecieveData(BinaryReader) implementation.
        /// </summary>
        private object DeserializeGameMessage(string fullTypeName, byte[] packetData)
        {
            if (packetData == null || packetData.Length < 2)
                throw new ArgumentException("packetData was null or too small.", nameof(packetData));

            Type msgType = ResolveType(fullTypeName) ?? throw new InvalidOperationException("Message type not found: " + fullTypeName);

            ConstructorInfo ctor = msgType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null) ?? throw new InvalidOperationException("Default ctor not found for " + fullTypeName);
            object msg = ctor.Invoke(null);

            MethodInfo recv = msgType.GetMethod(
                "RecieveData",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [typeof(BinaryReader)],
                null) ?? throw new InvalidOperationException("RecieveData(BinaryReader) not found for " + fullTypeName);

            // packet layout: [messageId][payload...][checksum]
            using (var ms = new MemoryStream(packetData, 1, packetData.Length - 2, false))
            using (var reader = new BinaryReader(ms))
            {
                recv.Invoke(msg, [reader]);
            }

            return msg;
        }

        /// <summary>
        /// Reads a reflected property or field value by name.
        /// </summary>
        private object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
                return null;

            Type t = instance.GetType();

            PropertyInfo pi = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null)
                return pi.GetValue(instance, null);

            FieldInfo fi = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return fi?.GetValue(instance);
        }

        /// <summary>
        /// Returns the Count value from a reflected WorldInfo dictionary-like member.
        /// Used for logging WorldInfo size/debug details before sending it to clients.
        /// </summary>
        private int GetWorldInfoDictionaryCount(string memberName)
        {
            try
            {
                if (_worldInfo == null)
                    return -1;

                object dictionary = GetMemberValue(_worldInfo, memberName);
                if (dictionary == null)
                    return -1;

                object count = GetMemberValue(dictionary, "Count");
                return Convert.ToInt32(count);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Attempts to set a reflected property or field by name.
        /// </summary>
        private bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null)
                return false;

            Type t = instance.GetType();

            PropertyInfo pi = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.CanWrite)
            {
                pi.SetValue(instance, value, null);
                return true;
            }

            FieldInfo fi = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null)
            {
                fi.SetValue(instance, value);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Invokes TryGetValue on a reflected dictionary-like object.
        /// </summary>
        private bool TryDictionaryTryGetValue(object dictionary, object key, out object value)
        {
            value = null;
            if (dictionary == null || key == null)
                return false;

            MethodInfo mi = dictionary.GetType().GetMethod("TryGetValue");
            if (mi == null)
                return false;

            object[] args = [key, null];
            bool found = (bool)mi.Invoke(dictionary, args);
            value = args[1];
            return found;
        }

        /// <summary>
        /// Removes a key from a reflected dictionary-like object.
        /// </summary>
        private bool DictionaryRemove(object dictionary, object key)
        {
            if (dictionary == null || key == null)
                return false;

            MethodInfo mi = dictionary.GetType().GetMethod("Remove", [key.GetType()]);
            if (mi == null)
                return false;

            object result = mi.Invoke(dictionary, [key]);
            return result is not bool b || b;
        }

        /// <summary>
        /// Formats a reflected IntVector3-like value for logging.
        /// </summary>
        private string FormatIntVector3(object value)
        {
            if (value == null)
                return "<null>";

            object x = GetMemberValue(value, "X");
            object y = GetMemberValue(value, "Y");
            object z = GetMemberValue(value, "Z");

            return "(" + x + "," + y + "," + z + ")";
        }

        /// <summary>
        /// Attempts to read X/Y/Z integer components from a reflected IntVector3-like object.
        /// </summary>
        private bool TryReadIntVector3(object value, out int x, out int y, out int z)
        {
            x = 0;
            y = 0;
            z = 0;

            if (value == null)
                return false;

            object ox = GetMemberValue(value, "X");
            object oy = GetMemberValue(value, "Y");
            object oz = GetMemberValue(value, "Z");

            if (ox == null || oy == null || oz == null)
                return false;

            x = Convert.ToInt32(ox);
            y = Convert.ToInt32(oy);
            z = Convert.ToInt32(oz);
            return true;
        }

        /// <summary>
        /// Removes a chunk entry from the in-memory delta cache.
        ///
        /// Notes:
        /// - This affects only the in-memory cache, not disk state.
        /// </summary>
        private void InvalidateChunkCacheForBlock(int blockX, int blockY, int blockZ)
        {
            int chunkX = FloorToChunk(blockX);
            int chunkY = FloorToChunk(blockY);
            int chunkZ = FloorToChunk(blockZ);

            string key = BuildChunkKey(chunkX, chunkY, chunkZ);

            _chunkCache.Remove(key);

            if (_chunkCacheNodes.TryGetValue(key, out var node) && node != null)
                _chunkCacheOrder.Remove(node);

            _chunkCacheNodes.Remove(key);
        }
        #endregion

        #region Pickup handlers

        /// <summary>
        /// Caches a newly created pickup so the host can later validate and resolve RequestPickupMessage.
        ///
        /// Notes:
        /// - The original CreatePickupMessage is still relayed normally to peers by the transport layer.
        /// - The host only stores enough state to later build a ConsumePickupMessage.
        /// </summary>
        private void HandleCreatePickupMessage(byte senderId, byte[] data)
        {
            try
            {
                // Full payload layout:
                // [msgId][SpawnPosition:12][SpawnVector:12][PickupID:4][Item:variable][Dropped:1][DisplayOnPickup:1][checksum:1]

                if (data == null || data.Length < 32)
                {
                    _log($"[HostMsg] CreatePickupMessage from player {senderId} ignored; packet too small.");
                    return;
                }

                int msgIdOffset = 0;
                int bodyOffset = msgIdOffset + 1;
                int checksumOffset = data.Length - 1;

                int spawnPosOffset = bodyOffset + 0;
                int pickupIdOffset = bodyOffset + 24;
                int itemOffset = bodyOffset + 28;

                int flagsOffset = checksumOffset - 2 + 1; // last two bytes before checksum are Dropped, DisplayOnPickup
                                                          // simpler:
                flagsOffset = data.Length - 3;

                int itemLength = flagsOffset - itemOffset;
                if (itemLength < 0)
                {
                    _log($"[HostMsg] CreatePickupMessage from player {senderId} ignored; invalid item length.");
                    return;
                }

                int pickupId = BitConverter.ToInt32(data, pickupIdOffset);

                var pickupPositionBytes = new byte[12];
                Buffer.BlockCopy(data, spawnPosOffset, pickupPositionBytes, 0, 12);

                var itemBytes = new byte[itemLength];
                if (itemLength > 0)
                    Buffer.BlockCopy(data, itemOffset, itemBytes, 0, itemLength);

                bool displayOnPickup = data[flagsOffset + 1] != 0;

                _pendingPickupsById[pickupId] = new PendingPickup
                {
                    PickupID = pickupId,
                    PickupPositionBytes = pickupPositionBytes,
                    ItemBytes = itemBytes,
                    DisplayOnPickup = displayOnPickup
                };

                _log($"[HostMsg] CreatePickupMessage from player {senderId}, pickupId={pickupId}, itemBytes={itemLength}, display={displayOnPickup}");
            }
            catch (Exception ex)
            {
                _log("ServerWorld HandleCreatePickupMessage: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves a pickup request on the host and broadcasts a ConsumePickupMessage to all connected clients.
        ///
        /// Purpose:
        /// - Removes the pending pickup from the host cache.
        /// - Tells every client that the pickup was consumed by the requesting player.
        ///
        /// Notes:
        /// - The local game client is expected to apply the pickup/inventory effect when it receives
        ///   ConsumePickupMessage through PickupManager.HandleMessage(...).
        /// - This mirrors the host-authoritative pattern better than relaying RequestPickupMessage itself.
        /// </summary>
        private void HandleRequestPickupMessage(
            byte senderId,
            byte[] data,
            object senderConn,
            object connections,
            Dictionary<object, object> connectionToGamer,
            Action<object, byte[], byte> sendToClient)
        {
            try
            {
                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.RequestPickupMessage", data);
                if (msg == null)
                    return;

                int pickupId = Convert.ToInt32(GetMemberValue(msg, "PickupID"));
                int spawnerId = Convert.ToInt32(GetMemberValue(msg, "SpawnerID"));

                if (!_pendingPickupsById.TryGetValue(pickupId, out PendingPickup pending))
                {
                    _log($"[HostMsg] RequestPickupMessage from player {senderId} ignored; unknown pickupId={pickupId}, spawnerId={spawnerId}");
                    return;
                }


                byte[] consumePayload = BuildConsumePickupMessagePayload(
                    pending.PickupPositionBytes,
                    pending.ItemBytes,
                    pickupId,
                    spawnerId,
                    senderId,
                    pending.DisplayOnPickup);

                if (consumePayload == null || consumePayload.Length < 2)
                {
                    _log($"[HostMsg] Failed to build ConsumePickupMessage for pickupId={pickupId}");
                    return;
                }

                bool sentToAnyone = false;

                // Always send directly back to the requester first.
                // This is the most reliable connection object we have, because the request arrived on it.
                if (senderConn != null)
                {
                    _log($"[HostMsg] Sending ConsumePickupMessage pickupId={pickupId} directly to requester recipientId={senderId}");
                    sendToClient(senderConn, consumePayload, senderId);
                    sentToAnyone = true;
                }

                // Optionally fan out to any other connected peers so they also remove the pickup visually.
                if (connections is System.Collections.IEnumerable liveConnections)
                {
                    foreach (var conn in liveConnections)
                    {
                        if (ReferenceEquals(conn, senderConn))
                            continue;

                        if (!connectionToGamer.TryGetValue(conn, out var gamer))
                            continue;

                        byte recipientId = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer);

                        // Steam-safe skip. Boxed ulong connection objects may not pass ReferenceEquals.
                        if (recipientId == senderId)
                            continue;

                        _log($"[HostMsg] Sending ConsumePickupMessage pickupId={pickupId} to recipientId={recipientId}");
                        sendToClient(conn, consumePayload, recipientId);
                        sentToAnyone = true;
                    }
                }

                if (sentToAnyone)
                {
                    _pendingPickupsById.Remove(pickupId);
                    _log($"[HostMsg] Resolved pickup pickupId={pickupId}, spawnerId={spawnerId}, picker={senderId}");
                }
                else
                {
                    _log($"[HostMsg] ConsumePickupMessage was built but not sent to any clients for pickupId={pickupId}");
                }
            }
            catch (TargetInvocationException tie)
            {
                Exception inner = tie.InnerException ?? tie;
                _log("ServerWorld HandleRequestPickupMessage inner: " + inner.GetType().FullName + ": " + inner.Message);
            }
            catch (Exception ex)
            {
                _log("ServerWorld HandleRequestPickupMessage: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Builds a raw ConsumePickupMessage packet using the original reflected game message type.
        ///
        /// Notes:
        /// - Reuses the game's protected SendData(BinaryWriter) implementation so the wire format stays exact.
        /// - Packet layout emitted here is [msgId][payload...][checksum].
        /// </summary>
        private byte[] BuildConsumePickupMessagePayload(
            byte[] pickupPositionBytes,
            byte[] itemBytes,
            int pickupId,
            int spawnerId,
            byte pickerUpper,
            bool displayOnPickup)
        {
            const string fullTypeName = "DNA.CastleMinerZ.Net.ConsumePickupMessage";

            try
            {
                if (!_typeToMessageId.TryGetValue(fullTypeName, out byte msgId))
                {
                    _log("ServerWorld: ConsumePickupMessage ID not found.");
                    return null;
                }

                if (pickupPositionBytes == null || pickupPositionBytes.Length != 12)
                {
                    _log("ServerWorld: Invalid pickup position bytes for ConsumePickupMessage.");
                    return null;
                }

                itemBytes ??= [];

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writer.Write(msgId);

                // ConsumePickupMessage.SendData order:
                // PickupPosition
                // PickupID
                // SpawnerID
                // PickerUpper
                // DisplayOnPickup
                // Item.Write(writer)

                writer.Write(pickupPositionBytes);
                writer.Write(pickupId);
                writer.Write(spawnerId);
                writer.Write(pickerUpper);
                writer.Write(displayOnPickup);

                if (itemBytes.Length > 0)
                    writer.Write(itemBytes);

                writer.Flush();

                int len = (int)ms.Position;
                writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

                byte[] payload = ms.ToArray();
                _log($"[HostMsg] Built ConsumePickupMessage payload: pickupId={pickupId}, bytes={payload.Length}, spawnerId={spawnerId}, picker={pickerUpper}");
                return payload;
            }
            catch (Exception ex)
            {
                _log("ServerWorld BuildConsumePickupMessagePayload: " + ex.GetType().FullName + ": " + ex.Message);
                return null;
            }
        }
        #endregion

        #region WorldInfo / Crate Helpers

        /// <summary>
        /// Validates that a saved world.info file can be loaded back through the same SaveDevice path.
        /// </summary>
        private bool TryValidateWorldInfoFile(string relativePath)
        {
            try
            {
                Type worldInfoType = _gameAsm?.GetType("DNA.CastleMinerZ.WorldInfo");
                if (worldInfoType == null)
                    return false;

                object testWorldInfo = null;

                bool ok = InvokeSaveDeviceLoad(relativePath, stream =>
                {
                    using var reader = new BinaryReader(stream);

                    ConstructorInfo ctor = worldInfoType.GetConstructor(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        [typeof(BinaryReader)],
                        null);

                    if (ctor == null)
                        throw new InvalidOperationException("WorldInfo(BinaryReader) constructor not found.");

                    testWorldInfo = ctor.Invoke([reader]);
                });

                return ok && testWorldInfo != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Atomically replaces the live save file with a validated temporary save file.
        /// </summary>
        private void ReplacePhysicalSaveFile(string tempRelativePath, string liveRelativePath, string backupRelativePath)
        {
            string tempPath = Path.GetFullPath(Path.Combine(_saveRoot, tempRelativePath));
            string livePath = Path.GetFullPath(Path.Combine(_saveRoot, liveRelativePath));
            string backupPath = Path.GetFullPath(Path.Combine(_saveRoot, backupRelativePath));

            Directory.CreateDirectory(Path.GetDirectoryName(livePath));

            if (!File.Exists(tempPath))
                throw new FileNotFoundException("Temporary world.info save was missing.", tempPath);

            if (File.Exists(livePath))
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                File.Replace(tempPath, livePath, backupPath, true);
            }
            else
            {
                File.Move(tempPath, livePath);
            }
        }

        /// <summary>
        /// Best-effort cleanup for failed temporary save files.
        /// </summary>
        private void TryDeletePhysicalSaveFile(string relativePath)
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(_saveRoot, relativePath));
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
        #endregion

        #region WorldInfo Crate Helpers

        /// <summary>
        /// Returns true when the reflected crate has no stored inventory items.
        /// </summary>
        private bool IsCrateEmpty(object crate)
        {
            if (crate == null)
                return true;

            if (GetMemberValue(crate, "Inventory") is not Array inventory)
                return false;

            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory.GetValue(i) != null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Removes empty crate inventory records from WorldInfo.Crates.
        /// The crate block itself is stored in terrain chunk deltas, so empty inventory records do not need to persist.
        /// </summary>
        private int PruneEmptyCrates()
        {
            if (_worldInfo == null)
                return 0;

            object crates = GetMemberValue(_worldInfo, "Crates");
            if (crates is not System.Collections.IEnumerable enumerable)
                return 0;

            List<object> removeKeys = [];

            foreach (object entry in enumerable)
            {
                object key = GetMemberValue(entry, "Key");
                object crate = GetMemberValue(entry, "Value");

                if (key != null && IsCrateEmpty(crate))
                    removeKeys.Add(key);
            }

            for (int i = 0; i < removeKeys.Count; i++)
                DictionaryRemove(crates, removeKeys[i]);

            return removeKeys.Count;
        }

        /// <summary>
        /// Returns true if a crate inventory record is being created for a location that is actually saved as a crate/container block.
        /// </summary>
        private bool IsSavedCrateBlockLocation(object location)
        {
            if (!TryGetIntVector3(location, out int x, out int y, out int z))
                return false;

            if (!TryGetSavedBlockType(x, y, z, out int blockTypeValue))
                return false;

            return IsCrateLikeBlockType(blockTypeValue);
        }

        /// <summary>
        /// Vanilla BlockTypeEnum values:
        /// Crate = 33,
        /// CrateStone = 49,
        /// CrateCopper = 50,
        /// CrateIron = 51,
        /// CrateGold = 52,
        /// CrateDiamond = 53,
        /// CrateBloodstone = 54,
        /// CrateSafe = 55.
        /// </summary>
        private static bool IsCrateLikeBlockType(int blockTypeValue)
        {
            return blockTypeValue == 33 || (blockTypeValue >= 49 && blockTypeValue <= 55);
        }

        /// <summary>
        /// Reads X/Y/Z from a reflected DNA.IntVector3 value.
        /// </summary>
        private bool TryGetIntVector3(object value, out int x, out int y, out int z)
        {
            x = 0;
            y = 0;
            z = 0;

            if (value == null)
                return false;

            try
            {
                x = Convert.ToInt32(GetMemberValue(value, "X"));
                y = Convert.ToInt32(GetMemberValue(value, "Y"));
                z = Convert.ToInt32(GetMemberValue(value, "Z"));
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #endregion

        #region Pending pickups

        /// <summary>
        /// Minimal host-side record for a spawned pickup that may later be claimed by RequestPickupMessage.
        /// </summary>
        private sealed class PendingPickup
        {
            public int PickupID;
            public byte[] PickupPositionBytes; // 12 bytes.
            public byte[] ItemBytes;           // Opaque raw InventoryItem bytes.
            public bool DisplayOnPickup;
        }

        /// <summary>
        /// Active pickup records keyed by pickup id.
        ///
        /// Notes:
        /// - CreatePickupMessage does not carry SpawnerID, so the host tracks by PickupID.
        /// - When RequestPickupMessage arrives, the host consumes the cached entry and broadcasts
        ///   a ConsumePickupMessage back out to all clients.
        /// </summary>
        private readonly Dictionary<int, PendingPickup> _pendingPickupsById = [];

        #endregion

        #region Chunk List / Chunk Load

        /// <summary>
        /// Builds the remote chunk-id list from every saved chunk delta file in the active world folder.
        /// This mirrors vanilla ChunkCache.LoadChunkList() behavior better than using a spawn-centered radius.
        /// </summary>
        /// <remarks>
        /// The client uses this list to decide which chunks should be requested from the host.
        /// If a saved chunk is not in this list, the client may fall back to procedural world-gen when traveling there.
        /// </remarks>
        private int[] BuildChunkIdListFromSavedFiles()
        {
            var chunkIds = new HashSet<int>();

            try
            {
                string absoluteWorldDir = Path.Combine(_saveRoot, _worldFolder);

                if (!Directory.Exists(absoluteWorldDir))
                {
                    _log("ServerWorld: World directory not found while building chunk list: " + absoluteWorldDir);
                    return [];
                }

                foreach (string file in Directory.GetFiles(absoluteWorldDir, "X*Y*Z*.dat", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileNameWithoutExtension(file);

                    if (!TryParseChunkFileName(name, out int chunkX, out int chunkY, out int chunkZ))
                        continue;

                    // CastleMiner Z terrain chunk files should normally be Y -64.
                    // Keep this guard so inventory/other future .dat files do not get advertised as terrain.
                    if (chunkY != TerrainChunkMinY)
                        continue;

                    int chunkXIndex = chunkX / 16;
                    int chunkZIndex = chunkZ / 16;

                    chunkIds.Add(PackChunkId2D(chunkXIndex, chunkZIndex));
                }

                _log("ServerWorld: Built saved chunk list from disk. Count=" + chunkIds.Count);
            }
            catch (Exception ex)
            {
                _log("ServerWorld BuildChunkIdListFromSavedFiles: " + ex.GetType().FullName + ": " + ex.Message);
            }

            return [.. chunkIds];
        }

        /// <summary>
        /// Parses vanilla CastleMiner Z terrain chunk filenames:
        /// X{chunkX}Y{chunkY}Z{chunkZ}.dat
        /// Example: X800Y-64Z0.dat
        /// </summary>
        private static bool TryParseChunkFileName(string fileNameWithoutExtension, out int chunkX, out int chunkY, out int chunkZ)
        {
            chunkX = 0;
            chunkY = 0;
            chunkZ = 0;

            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
                return false;

            int xIndex = fileNameWithoutExtension.IndexOf('X');
            int yIndex = fileNameWithoutExtension.IndexOf('Y');
            int zIndex = fileNameWithoutExtension.IndexOf('Z');

            if (xIndex != 0 || yIndex <= xIndex || zIndex <= yIndex)
                return false;

            string xText = fileNameWithoutExtension.Substring(xIndex + 1, yIndex - xIndex - 1);
            string yText = fileNameWithoutExtension.Substring(yIndex + 1, zIndex - yIndex - 1);
            string zText = fileNameWithoutExtension.Substring(zIndex + 1);

            return int.TryParse(xText, out chunkX)
                && int.TryParse(yText, out chunkY)
                && int.TryParse(zText, out chunkZ);
        }

        /// <summary>
        /// Builds a 2D list of chunk ids around the spawn hint using a ring-based order.
        ///
        /// Notes:
        /// - Chunk ids are packed as (chunkZ << 16) | chunkX.
        /// - This is a spawn-centered list, not a scan of saved chunk files.
        /// </summary>
        private int[] BuildChunkIdListAroundSpawn()
        {
            List<int> list = [];

            int baseChunkX = FloorToChunk(_spawnX) / 16;
            int baseChunkZ = FloorToChunk(_spawnZ) / 16;
            int radius = _viewRadiusChunks;

            // Near-first spiral-ish order: ring by ring around spawn.
            for (int ring = 0; ring < radius; ring++)
            {
                int minX = baseChunkX - ring;
                int maxX = baseChunkX + ring;
                int minZ = baseChunkZ - ring;
                int maxZ = baseChunkZ + ring;

                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        bool onEdge = (cx == minX || cx == maxX || cz == minZ || cz == maxZ);
                        if (!onEdge)
                            continue;

                        list.Add(PackChunkId2D(cx, cz));
                    }
                }
            }

            return [.. list];
        }

        /// <summary>
        /// Floors a float world coordinate to its containing 16-block chunk corner.
        /// </summary>
        private static int FloorToChunk(float worldCoord)
        {
            return (int)Math.Floor(worldCoord / 16.0f) * 16;
        }

        /// <summary>
        /// Floors an integer world coordinate to its containing 16-block chunk corner.
        /// </summary>
        private static int FloorToChunk(int worldCoord)
        {
            return (int)Math.Floor(worldCoord / 16.0) * 16;
        }

        /// <summary>
        /// Packs 2D chunk indices into the same int form used by the game chunk list.
        /// </summary>
        private static int PackChunkId2D(int chunkXIndex, int chunkZIndex)
        {
            uint uz = (uint)(chunkZIndex & 65535);
            uint ux = (uint)(chunkXIndex & 65535);
            return (int)((uz << 16) | ux);
        }

        /// <summary>
        /// Builds a unique cache key from chunk coordinates.
        /// </summary>
        private static string BuildChunkKey(int chunkX, int chunkY, int chunkZ)
        {
            return chunkX + "_" + chunkY + "_" + chunkZ;
        }

        /// <summary>
        /// Adds or refreshes a chunk entry in the in-memory LRU cache.
        /// </summary>
        private void AddChunkToCache(string key, int[] delta)
        {
            _chunkCache[key] = delta;

            if (_chunkCacheNodes.TryGetValue(key, out var existingNode))
            {
                _chunkCacheOrder.Remove(existingNode);
            }

            var node = _chunkCacheOrder.AddLast(key);
            _chunkCacheNodes[key] = node;

            while (_chunkCacheOrder.Count > MaxChunkCacheEntries)
            {
                var first = _chunkCacheOrder.First;
                if (first == null)
                    break;

                string evictKey = first.Value;
                _chunkCacheOrder.RemoveFirst();
                _chunkCacheNodes.Remove(evictKey);
                _chunkCache.Remove(evictKey);
            }
        }

        /// <summary>
        /// Marks an existing cache entry as recently used.
        /// </summary>
        private void TouchChunkCacheEntry(string key)
        {
            if (!_chunkCacheNodes.TryGetValue(key, out var node) || node == null)
                return;

            _chunkCacheOrder.Remove(node);
            _chunkCacheNodes[key] = _chunkCacheOrder.AddLast(key);
        }

        /// <summary>
        /// Loads the saved chunk delta from X{minX}Y{minY}Z{minZ}.dat.
        /// Returns null when absent so the client uses procedural terrain.
        ///
        /// Notes:
        /// - Supports both the newer fixed-format file and the older byte-swapped format.
        /// - Uses TerrainChunkMinY for file path generation.
        /// </summary>
        private int[] LoadChunkDelta(int chunkX, int chunkY, int chunkZ)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_worldFolder))
                    return null;

                string relativePath = Path.Combine(_worldFolder, $"X{chunkX}Y{TerrainChunkMinY}Z{chunkZ}.dat");
                int[] result = null;

                bool ok = InvokeSaveDeviceLoad(relativePath, stream =>
                {
                    using var reader = new BinaryReader(stream);
                    uint version = reader.ReadUInt32();

                    // Newer format: magic + count + raw ints
                    if (version == 3203334144U)
                    {
                        int modsToRead = reader.ReadInt32();
                        if (modsToRead <= 0)
                        {
                            result = null;
                            return;
                        }

                        int[] data = new int[modsToRead];
                        for (int i = 0; i < modsToRead; i++)
                            data[i] = reader.ReadInt32();

                        result = data;
                        return;
                    }

                    int totalSize;
                    if ((version & 0xFFFFU) == 2U)
                    {
                        stream.Position = 0L;
                        totalSize = (int)stream.Length;
                    }
                    else
                    {
                        totalSize = (int)version;
                    }

                    int skipSize = reader.ReadByte();
                    for (int j = 0; j < skipSize - 1; j++)
                        reader.ReadByte();

                    int mods = (totalSize - skipSize) / 4;
                    if (mods <= 0)
                    {
                        result = null;
                        return;
                    }

                    int[] data2 = new int[mods];
                    for (int k = 0; k < mods; k++)
                    {
                        uint s = reader.ReadUInt32();
                        uint f = (s & 0xFF000000U) >> 24;
                        f |= (s & 0x00FF0000U) >> 8;
                        f |= (s & 0x0000FF00U) << 8;
                        f |= (s & 0x000000FFU) << 24;
                        data2[k] = unchecked((int)f);
                    }

                    result = data2;
                });

                return !ok ? null : result;
            }
            catch (Exception ex)
            {
                _log($"ServerWorld LoadChunkDelta error ({chunkX},{chunkY},{chunkZ}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Delegate proxy used to bridge Action<Stream> into reflected FileAction signatures.
        /// </summary>
        private sealed class StreamLoadProxy
        {
            /// <summary>Callback invoked with the save-device stream.</summary>
            public Action<Stream> Callback;

            /// <summary>Invokes the proxied callback.</summary>
            public void Invoke(Stream stream)
            {
                Callback?.Invoke(stream);
            }
        }
        #endregion

        #region Type Resolution / Save Device

        /// <summary>
        /// Resolves a type by full name across the game assembly, common assembly, and loaded app-domain assemblies.
        /// </summary>
        private Type ResolveType(string fullName)
        {
            if (_gameAsm != null)
            {
                var t = _gameAsm.GetType(fullName, false);
                if (t != null)
                    return t;
            }

            if (_commonAsm != null)
            {
                var t = _commonAsm.GetType(fullName, false);
                if (t != null)
                    return t;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null)
                    return t;
            }

            return null;
        }

        /// <summary>
        /// Initializes the reflected FileSystemSaveDevice using the same key derivation pattern as the game.
        ///
        /// Notes:
        /// - Key material = MD5(steamUserId + "CMZ778").
        /// </summary>
        private void InitSaveDevice()
        {
            try
            {
                if (_saveOwnerSteamId == 0UL)
                {
                    _log("ServerWorld: save-owner-steam-id is required.");
                    return;
                }

                Type md5Type = ResolveType("DNA.Security.Cryptography.MD5HashProvider");
                Type fsSaveType = ResolveType("DNA.IO.Storage.FileSystemSaveDevice");

                if (md5Type == null)
                {
                    _log("ServerWorld: MD5HashProvider type not found.");
                    return;
                }

                if (fsSaveType == null)
                {
                    _log("ServerWorld: FileSystemSaveDevice type not found.");
                    return;
                }

                object md5 = Activator.CreateInstance(md5Type);
                byte[] sourceBytes = Encoding.UTF8.GetBytes(_saveOwnerSteamId.ToString() + "CMZ778");

                MethodInfo computeMethod = md5Type.GetMethod("Compute", [typeof(byte[])]);
                if (computeMethod == null)
                {
                    _log("ServerWorld: MD5HashProvider.Compute(byte[]) not found.");
                    return;
                }

                object hashObj = computeMethod.Invoke(md5, [sourceBytes]);
                if (hashObj == null)
                {
                    _log("ServerWorld: MD5 hash result was null.");
                    return;
                }

                PropertyInfo dataProp = hashObj.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Instance);
                if (dataProp == null)
                {
                    _log("ServerWorld: hash.Data property not found.");
                    return;
                }

                if (dataProp.GetValue(hashObj, null) is not byte[] key)
                {
                    _log("ServerWorld: hash.Data returned null.");
                    return;
                }

                _saveDevice = Activator.CreateInstance(fsSaveType, [_saveRoot, key]);
                _log("ServerWorld: SaveDevice initialized.");
            }
            catch (Exception ex)
            {
                _log("ServerWorld InitSaveDevice: " + ex.GetType().FullName + ": " + ex.Message);
                _log(ex.StackTrace ?? "(no stack trace)");
            }
        }

        /// <summary>
        /// Invokes SaveDevice.Load(string, FileAction) via reflection.
        /// </summary>
        private bool InvokeSaveDeviceLoad(string relativePath, Action<Stream> callback)
        {
            if (_saveDevice == null)
                return false;

            try
            {
                MethodInfo loadMethod = null;

                foreach (var m in _saveDevice.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (m.Name != "Load")
                        continue;

                    var pars = m.GetParameters();
                    if (pars.Length == 2 && pars[0].ParameterType == typeof(string))
                    {
                        loadMethod = m;
                        break;
                    }
                }

                if (loadMethod == null)
                {
                    _log("ServerWorld: SaveDevice.Load(string, FileAction) not found.");
                    return false;
                }

                Type fileActionType = loadMethod.GetParameters()[1].ParameterType;

                var proxy = new StreamLoadProxy { Callback = callback };
                Delegate fileAction = Delegate.CreateDelegate(fileActionType, proxy, nameof(StreamLoadProxy.Invoke));

                loadMethod.Invoke(_saveDevice, [relativePath, fileAction]);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Time Of Day Payload

        /// <summary>
        /// Builds a raw TimeOfDayMessage payload suitable for the host relay path.
        ///
        /// Notes:
        /// - Packet layout is: message id + float timeOfDay + checksum.
        /// </summary>
        public byte[] BuildTimeOfDayPayload(float timeOfDay)
        {
            if (_typeToMessageId == null)
                return null;

            if (!_typeToMessageId.TryGetValue("DNA.CastleMinerZ.Net.TimeOfDayMessage", out byte msgId))
                return null;

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(msgId);
            writer.Write(timeOfDay);
            writer.Flush();

            int len = (int)ms.Position;
            writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

            return ms.ToArray();
        }
        #endregion

        #region Checksum

        /// <summary>
        /// Computes the simple XOR checksum used by these raw message payload builders.
        /// </summary>
        private static byte XorChecksum(byte[] data, int offset, int count)
        {
            byte c = 0;
            int end = offset + count;
            if (data == null)
                return 0;

            for (int i = offset; i < end && i < data.Length; i++)
                c ^= data[i];

            return c;
        }
        #endregion

        #region RegionProtect / Server Plugin Bridge

        /// <summary>
        /// Runs registered world plugins before a mutation packet is applied or relayed.
        /// </summary>
        /// <remarks>
        /// The plugin context exposes safe callbacks for warning the player, correcting local visuals,
        /// reading saved/original block state, and requesting chunk resyncs.
        /// </remarks>
        /// <returns>
        /// True when a plugin consumed the packet and normal handling should stop.
        /// False when the packet should continue through normal host handling.
        /// </returns>
        private bool TryRunWorldPlugins(
            string typeName,
            byte senderId,
            byte[] data,
            object senderConn,
            Dictionary<object, object> connectionToGamer,
            Action<object, byte[], byte> sendToClient)
        {
            if (_plugins == null)
                return false;

            string senderName = ResolveSenderGamertag(senderId, senderConn, connectionToGamer);

            var context = new HostMessageContext
            {
                TypeName = typeName,
                SenderId = senderId,
                SenderName = senderName,
                Payload = data,

                DeserializeGameMessage = DeserializeGameMessage,
                GetMemberValue = GetMemberValue,
                TryReadIntVector3 = TryReadIntVector3,

                ResyncChunkForBlock = (x, y, z) =>
                {
                    SendChunkForBlockToClient(x, y, z, senderId, senderConn, sendToClient);
                },

                SendWarningToPlayer = message =>
                {
                    SendWarningToClient(message, senderId, senderConn, sendToClient);
                },

                SendAlterBlockToPlayer = (x, y, z, blockTypeValue) =>
                {
                    SendAlterBlockToClient(x, y, z, blockTypeValue, senderId, senderConn, sendToClient);
                },

                TryGetSavedBlockType = TryGetSavedBlockType,
                TryGetOriginalBlockType = TryGetOriginalBlockType,

                Log = _log
            };

            return _plugins.BeforeHostMessage(context);
        }

        /// <summary>
        /// Resolves the player's visible gamertag from the server's connection-to-gamer map.
        /// </summary>
        private string ResolveSenderGamertag(
            byte senderId,
            object senderConn,
            Dictionary<object, object> connectionToGamer)
        {
            try
            {
                if (connectionToGamer != null &&
                    senderConn != null &&
                    connectionToGamer.TryGetValue(senderConn, out object gamer) &&
                    gamer != null)
                {
                    string gamertag = Convert.ToString(GetMemberValue(gamer, "Gamertag"));

                    if (!string.IsNullOrWhiteSpace(gamertag))
                        return gamertag;
                }
            }
            catch
            {
            }

            return "Player" + senderId;
        }

        /// <summary>
        /// Sends the authoritative terrain chunk back to the offending client after a blocked edit.
        /// </summary>
        /// <remarks>
        /// CastleMiner Z terrain chunks are column-style chunks with a fixed Y origin of TerrainChunkMinY.
        /// The supplied blockY is still validated so invalid/non-terrain positions do not trigger useless
        /// chunk resync packets.
        /// </remarks>
        private void SendChunkForBlockToClient(
            int blockX,
            int blockY,
            int blockZ,
            byte senderId,
            object senderConn,
            Action<object, byte[], byte> sendToClient)
        {
            if (senderConn == null || sendToClient == null)
                return;

            try
            {
                int localY = blockY - TerrainChunkMinY;

                if (localY < 0 || localY >= TerrainChunkHeight)
                {
                    _log(
                        $"[World] Skipped chunk resync for invalid terrain Y. " +
                        $"Block=({blockX},{blockY},{blockZ}), Sender={senderId}");

                    return;
                }

                int chunkX = MakeTerrainChunkCorner(blockX);
                int chunkY = TerrainChunkMinY;
                int chunkZ = MakeTerrainChunkCorner(blockZ);

                string chunkKey = BuildChunkKey(chunkX, chunkY, chunkZ);

                if (!_chunkCache.TryGetValue(chunkKey, out int[] delta))
                {
                    delta = LoadChunkDelta(chunkX, chunkY, chunkZ);
                    AddChunkToCache(chunkKey, delta);
                }
                else
                {
                    TouchChunkCacheEntry(chunkKey);
                }

                if (!_typeToMessageId.TryGetValue("DNA.CastleMinerZ.Net.ProvideChunkMessage", out byte msgId))
                    return;

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                writer.Write(msgId);
                writer.Write(chunkX);
                writer.Write(chunkY);
                writer.Write(chunkZ);
                writer.Write((byte)0);

                if (delta != null && delta.Length > 0)
                {
                    writer.Write(delta.Length);

                    for (int i = 0; i < delta.Length; i++)
                        writer.Write(delta[i]);
                }
                else
                {
                    writer.Write(0);
                }

                writer.Flush();

                int len = (int)ms.Position;
                writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

                sendToClient(senderConn, ms.ToArray(), senderId);

                _log($"[World] Resynced chunk ({chunkX},{chunkY},{chunkZ}) to player {senderId} after denied edit.");
            }
            catch (Exception ex)
            {
                _log("ServerWorld SendChunkForBlockToClient: " + ex.Message);
            }
        }

        /// <summary>
        /// Sends a private server warning to one client using BroadcastTextMessage.
        /// </summary>
        /// <remarks>
        /// This is used by RegionProtect-style plugins to notify only the offending player instead of
        /// broadcasting a warning to the whole server.
        /// </remarks>
        private void SendWarningToClient(
            string message,
            byte senderId,
            object senderConn,
            Action<object, byte[], byte> sendToClient)
        {
            if (senderConn == null || sendToClient == null)
                return;

            byte[] payload = BuildBroadcastTextMessagePayload(message);

            if (payload == null || payload.Length == 0)
                return;

            sendToClient(senderConn, payload, senderId);
        }

        /// <summary>
        /// Builds a raw BroadcastTextMessage payload.
        /// Packet layout:
        /// [message id][BinaryWriter string][checksum]
        /// </summary>
        private byte[] BuildBroadcastTextMessagePayload(string message)
        {
            const string fullTypeName = "DNA.CastleMinerZ.Net.BroadcastTextMessage";

            if (_typeToMessageId == null)
                return null;

            if (!_typeToMessageId.TryGetValue(fullTypeName, out byte msgId))
                return null;

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(msgId);
            writer.Write(message ?? string.Empty);

            writer.Flush();

            int len = (int)ms.Position;
            writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

            return ms.ToArray();
        }

        /// <summary>
        /// Sends a single corrective AlterBlockMessage to one client.
        /// This is used to instantly undo a denied local mine/place visual.
        /// </summary>
        /// <remarks>
        /// This does not apply the block server-side. It only tells the denied client to correct its
        /// local view after the original packet was consumed by a plugin.
        /// </remarks>
        private void SendAlterBlockToClient(
            int x,
            int y,
            int z,
            int blockTypeValue,
            byte senderId,
            object senderConn,
            Action<object, byte[], byte> sendToClient)
        {
            if (senderConn == null || sendToClient == null)
                return;

            byte[] payload = BuildAlterBlockMessagePayload(x, y, z, blockTypeValue);

            if (payload == null || payload.Length == 0)
                return;

            sendToClient(senderConn, payload, senderId);
        }

        /// <summary>
        /// Builds a raw AlterBlockMessage payload.
        /// Packet layout:
        /// [message id][IntVector3 X/Y/Z][BlockType int][checksum]
        /// </summary>
        private byte[] BuildAlterBlockMessagePayload(int x, int y, int z, int blockTypeValue)
        {
            const string fullTypeName = "DNA.CastleMinerZ.Net.AlterBlockMessage";

            if (_typeToMessageId == null)
                return null;

            if (!_typeToMessageId.TryGetValue(fullTypeName, out byte msgId))
                return null;

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(msgId);

            // DNA.IntVector3.Write(BinaryWriter) writes X, Y, Z as Int32.
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);

            // AlterBlockMessage writes BlockTypeEnum as Int32.
            writer.Write(blockTypeValue);

            writer.Flush();

            int len = (int)ms.Position;
            writer.Write(XorChecksum(ms.GetBuffer(), 0, len));

            return ms.ToArray();
        }

        /// <summary>
        /// Attempts to read the current saved terrain-delta block type at a world position.
        /// Returns false when the block is not present in the saved delta.
        /// 
        /// Important:
        /// - This can read modified/saved terrain entries.
        /// - It cannot always know the original procedural terrain block if the block
        ///   was never saved into the delta file.
        /// </summary>
        private bool TryGetSavedBlockType(int worldX, int worldY, int worldZ, out int blockTypeValue)
        {
            blockTypeValue = 0;

            int chunkX = MakeTerrainChunkCorner(worldX);
            int chunkZ = MakeTerrainChunkCorner(worldZ);

            int localX = worldX - chunkX;
            int localY = worldY - TerrainChunkMinY;
            int localZ = worldZ - chunkZ;

            if (localX < 0 || localX > 15 ||
                localZ < 0 || localZ > 15 ||
                localY < 0 || localY >= TerrainChunkHeight)
            {
                return false;
            }

            string chunkKey = BuildChunkKey(chunkX, TerrainChunkMinY, chunkZ);

            if (!_chunkCache.TryGetValue(chunkKey, out int[] delta))
            {
                delta = LoadChunkDelta(chunkX, TerrainChunkMinY, chunkZ);

                if (delta != null)
                    AddChunkToCache(chunkKey, delta);
            }

            if (delta == null || delta.Length == 0)
                return false;

            int target = MakeDeltaEntry(localX, localY, localZ, 0);

            for (int i = 0; i < delta.Length; i++)
            {
                int entry = delta[i];

                if (!SameDeltaLocation(entry, target))
                    continue;

                blockTypeValue = (entry >> 24) & 0xFF;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Caches the block type from the player's most recent DigMessage.
        /// The vanilla client sends DigMessage before AlterBlockMessage, and DigMessage contains
        /// the original block type being dug.
        /// </summary>
        private void CacheDigMessage(byte senderId, byte[] data)
        {
            try
            {
                object msg = DeserializeGameMessage("DNA.CastleMinerZ.Net.DigMessage", data);

                object placingObj = GetMemberValue(msg, "Placing");
                object blockTypeObj = GetMemberValue(msg, "BlockType");

                if (placingObj == null || blockTypeObj == null)
                    return;

                bool placing = Convert.ToBoolean(placingObj);

                // For restoring mined terrain, only cache non-placing dig messages.
                if (placing)
                    return;

                int blockTypeValue = Convert.ToInt32(blockTypeObj);

                if (blockTypeValue <= 0)
                    return;

                _recentDigBlockByPlayer[senderId] = new RecentDigBlock
                {
                    BlockTypeValue = blockTypeValue,
                    UtcTime = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _log("ServerWorld CacheDigMessage: " + ex.Message);
            }
        }

        /// <summary>
        /// Gets the best known original block type for a denied terrain edit.
        /// First checks saved chunk deltas, then falls back to the player's recent DigMessage.
        /// </summary>
        /// <remarks>
        /// Saved deltas are best for player-modified terrain. The recent DigMessage fallback is best for
        /// natural/procedural terrain that has not yet been written into a chunk delta.
        /// </remarks>
        private bool TryGetOriginalBlockType(
            byte senderId,
            int worldX,
            int worldY,
            int worldZ,
            out int blockTypeValue)
        {
            // Best source: Saved/player-modified terrain.
            if (TryGetSavedBlockType(worldX, worldY, worldZ, out blockTypeValue))
                return true;

            // Fallback source: The player's immediately preceding DigMessage.
            if (_recentDigBlockByPlayer.TryGetValue(senderId, out RecentDigBlock recent))
            {
                double ageSeconds = (DateTime.UtcNow - recent.UtcTime).TotalSeconds;

                if (ageSeconds <= 2.0 && recent.BlockTypeValue > 0)
                {
                    blockTypeValue = recent.BlockTypeValue;
                    return true;
                }
            }

            blockTypeValue = 0;
            return false;
        }
        #endregion
    }
}