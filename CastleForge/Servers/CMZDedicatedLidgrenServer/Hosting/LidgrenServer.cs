/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedLidgrenServer.Plugins.Announcements;
using CMZDedicatedLidgrenServer.Plugins.RegionProtect;
using CMZDedicatedLidgrenServer.Plugins.FloodGuard;
using CMZDedicatedLidgrenServer.Plugins;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer
{
    /// <summary>
    /// Runs a Lidgren NetPeer as a dedicated server using reflection against DNA.Common / game assemblies.
    ///
    /// Purpose:
    /// - Hosts a dedicated CastleMiner Z compatible server endpoint.
    /// - Accepts direct client connections by IP.
    /// - Handles discovery, approval, status changes, channel-0 game packets, and channel-1 internal packets.
    /// - Bridges dedicated-server state into the game's expected networking shapes.
    ///
    /// Notes:
    /// - Uses reflection heavily so the server can operate without compile-time references to all game-side types.
    /// - Game name defaults to "CastleMinerZSteam".
    /// - Network version defaults to 4.
    /// - Channel 0 is the normal gameplay relay / host-authoritative packet path.
    /// - Channel 1 is used for internal/system/bootstrap traffic.
    /// </summary>
    public class LidgrenServer
    {
        #region Fields: Server Identity / Session Settings

        /// <summary>
        /// Network game name used by discovery / connection validation.
        /// </summary>
        private readonly string _gameName;

        /// <summary>
        /// Network version used by discovery / connection validation.
        /// </summary>
        private readonly int _networkVersion;

        #endregion

        #region Fields: Save / World Configuration

        /// <summary>
        /// Relative world folder path (for example Worlds\{guid}).
        /// </summary>
        private readonly string _worldFolder;

        /// <summary>
        /// Absolute server save root.
        /// </summary>
        private readonly string _saveRoot;

        /// <summary>
        /// Steam user id used for save-device compatibility / encryption key derivation.
        /// </summary>
        private readonly ulong _saveOwnerSteamId;

        #endregion

        #region Fields: Startup / Runtime Configuration

        /// <summary>
        /// Folder containing game binaries such as DNA.Common.dll.
        /// </summary>
        private readonly string _gamePath;

        /// <summary>
        /// Port the Lidgren server listens on.
        /// </summary>
        private readonly int _port;

        /// <summary>
        /// Local address to bind to. Defaults to IPAddress.Any.
        /// </summary>
        private readonly IPAddress _bindAddress;

        /// <summary>
        /// Maximum number of connected clients allowed.
        /// </summary>
        private readonly int _maxPlayers;

        /// <summary>
        /// Human-readable server name advertised to clients.
        /// </summary>
        private string _serverName;

        /// <summary>
        /// Configured server message template shown in server/session info.
        /// </summary>
        private string _serverMessage;

        /// <summary>
        /// Game mode value sent in discovery / server-info responses.
        /// </summary>
        private int _gameMode;

        /// <summary>
        /// PVP state sent in discovery / server-info responses.
        /// </summary>
        private int _pvpState;

        /// <summary>
        /// Difficulty value sent in discovery / server-info responses.
        /// </summary>
        private int _difficulty;

        /// <summary>
        /// Logging callback used throughout server lifecycle.
        /// </summary>
        private readonly Action<string> _log;

        /// <summary>
        /// Enables verbose raw network packet logging, such as CH0/CH1 packet receive traces.
        /// </summary>
        private readonly bool _logNetworkPackets;

        /// <summary>
        /// Enables verbose host/world message logging, such as block edits, crate edits, doors, and pickups.
        /// </summary>
        private readonly bool _logHostMessages;

        #endregion

        #region Fields: Networking Runtime State

        /// <summary>
        /// Reflected Lidgren NetPeer instance.
        /// </summary>
        private object _netPeer;

        /// <summary>
        /// Reflected connections collection from NetPeer.
        /// </summary>
        //private object _connections;

        /// <summary>
        /// Next player GID assigned to newly joined remote clients.
        /// </summary>
        private byte _nextPlayerGid = 1;

        /// <summary>
        /// All currently connected remote gamer proxies.
        /// </summary>
        private readonly List<object> _allGamers = [];

        /// <summary>
        /// Connection -> gamer map for resolving senders / recipients.
        /// </summary>
        private readonly Dictionary<object, object> _connectionToGamer = [];

        /// <summary>
        /// Whether the server is currently running.
        /// </summary>
        private bool _running;

        /// <summary>
        /// Cached discovery-request message enum value.
        /// </summary>
        private object _discoveryRequestType;

        /// <summary>
        /// Loaded DNA.Common assembly.
        /// </summary>
        private Assembly _commonAsm;

        /// <summary>
        /// Dedicated world / chunk / inventory host handler.
        /// </summary>
        private readonly ServerWorldHandler _worldHandler;

        /// <summary>
        /// Optional loaded game assembly used by ServerWorldHandler.
        /// </summary>
        private readonly Assembly _gameAsm;

        /// <summary>
        /// View radius used when initializing the world handler.
        /// </summary>
        private readonly int _viewRadiusChunks;

        private CmzMessageCodec _codec;

        /// <summary>
        /// Server-side world plugin manager used for host-authoritative protections.
        //// </summary>
        private readonly ServerPluginManager _plugins;

        /// <summary>
        /// Cached raw PlayerExistsMessage payloads keyed by player id.
        ///
        /// Purpose:
        /// - Makes player-presence recovery resilient if the original request/response
        ///   exchange is missed or arrives before a client is fully ready.
        /// - Allows the dedicated host to replay all known existing players back to a
        ///   newly joined client once that client announces itself with requestResponse=true.
        /// </summary>
        private readonly Dictionary<byte, byte[]> _playerExistsPayloadById = [];

        /// <summary>
        /// Cached message id for DNA.CastleMinerZ.Net.PlayerExistsMessage.
        /// Initialized lazily once the codec is ready.
        /// </summary>
        private byte? _playerExistsMessageId;

        #endregion

        #region Fields: Time Of Day Broadcast State

        /// <summary>
        /// Simulated time-of-day value broadcast to clients.
        /// </summary>
        private float _timeOfDay = 0.41f;

        /// <summary>
        /// Last time-of-day broadcast timestamp.
        /// </summary>
        private DateTime _lastTimeOfDaySend = DateTime.MinValue;

        private DateTime _lastTimeOfDayAdvance = DateTime.UtcNow;

        // 0..1 is a full day.
        private const float SecondsPerFullDay = 960f; // match GameScreen.LengthOfDay = 16 minutes

        /// <summary>
        /// Last resolved display name logged by the server.
        /// Used only to avoid repeated log spam when the day has not changed.
        /// </summary>
        private string _lastResolvedServerName = string.Empty;

        /// <summary>
        /// Last player-facing day number logged by the server.
        /// </summary>
        private int _lastResolvedServerNameDay = -1;

        #endregion

        #region Feilds: Gameplay sync settings

        /// <summary>
        /// Whether client-sent TimeOfDayMessage packets are allowed to update the server's authoritative day value.
        /// </summary>
        // private readonly bool _allowClientTimeSync;

        #endregion

        #region Properties

        /// <summary>
        /// True while the server has been started and not yet stopped.
        /// </summary>
        public bool IsRunning
        {
            get { return _running; }
        }

        #endregion

        #region Construction

        /// <summary>
        /// Creates a new dedicated Lidgren server wrapper.
        ///
        /// Purpose:
        /// - Captures runtime settings.
        /// - Normalizes defaults / bounds.
        /// - Optionally prepares the world handler if enough save/world info is supplied.
        ///
        /// Notes:
        /// - The world handler is only created when gameAsm, worldFolder, saveRoot, and saveOwnerSteamId are all available.
        /// - gameName defaults to "CastleMinerZSteam".
        /// - networkVersion defaults to 4.
        /// </summary>
        public LidgrenServer(
            string gamePath,
            int port,
            int maxPlayers,
            Action<string> log,
            Assembly gameAsm = null,
            string worldFolder = null,
            string saveRoot = null,
            ulong saveOwnerSteamId = 0UL,
            IPAddress bindAddress = null,
            int viewRadiusChunks = 8,
            string serverName = null,
            string serverMessage = null,
            int gameMode = 1,
            int pvpState = 0,
            int difficulty = 1,
            string gameName = "CastleMinerZSteam",
            int networkVersion = 4,
            bool logNetworkPackets = false,
            bool logHostMessages = false)
        {
            _gamePath = gamePath ?? throw new ArgumentNullException(nameof(gamePath));
            _port = port;
            _bindAddress = bindAddress ?? IPAddress.Any;
            _maxPlayers = maxPlayers;
            _serverName = string.IsNullOrWhiteSpace(serverName) ? "CMZ Server" : serverName;
            _serverMessage = string.IsNullOrWhiteSpace(serverMessage)
                ? "Welcome to this CastleForge dedicated server."
                : serverMessage;
            _gameMode = gameMode;
            _pvpState = pvpState < 0 ? 0 : (pvpState > 2 ? 2 : pvpState);
            _difficulty = difficulty < 0 ? 0 : (difficulty > 3 ? 3 : difficulty);
            _log = log ?? (_ => { });
            _gameAsm = gameAsm;

            _worldFolder = worldFolder;
            _saveRoot = saveRoot;
            _saveOwnerSteamId = saveOwnerSteamId;

            _viewRadiusChunks = viewRadiusChunks;
            _gameName = string.IsNullOrWhiteSpace(gameName) ? "CastleMinerZSteam" : gameName;
            _networkVersion = networkVersion > 0 ? networkVersion : 4;

            _logNetworkPackets = logNetworkPackets;
            _logHostMessages = logHostMessages;

            if (_gameAsm != null &&
                !string.IsNullOrWhiteSpace(_worldFolder) &&
                !string.IsNullOrWhiteSpace(_saveRoot) &&
                _saveOwnerSteamId != 0UL)
            {
                _plugins = new ServerPluginManager(_log);
                _plugins.Register(new ServerFloodGuardPlugin());
                _plugins.Register(new ServerRegionProtectPlugin());
                _plugins.Register(new ServerAnnouncementsPlugin());

                _plugins.InitializeAll(new ServerPluginContext
                {
                    // saveRoot is the server exe directory.
                    // This creates:
                    // CMZDedicatedLidgrenServer\Plugins\RegionProtect
                    BaseDir = _saveRoot,

                    // Use only the final world folder name/GUID instead of "Worlds\GUID".
                    WorldGuid = GetWorldKey(_worldFolder),

                    Log = _log
                });

                _worldHandler = new ServerWorldHandler(
                    _gamePath,
                    _worldFolder,   // relative, e.g. Worlds\{guid}
                    _saveRoot,      // absolute server root
                    _saveOwnerSteamId,
                    _log,
                    _viewRadiusChunks,
                    _plugins,
                    _logHostMessages);
            }
        }
        #endregion

        #region Host Gamer State

        /// <summary>
        /// Synthetic local host gamer used to mirror vanilla host behavior where needed.
        /// </summary>
        private object _hostGamer;

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts the dedicated server.
        ///
        /// Purpose:
        /// - Loads DNA.Common and XNA.
        /// - Creates and configures the NetPeer.
        /// - Enables required incoming message types.
        /// - Starts the peer and initializes host/world state.
        ///
        /// Notes:
        /// - Throws when required runtime assemblies are missing.
        /// - Host gamer is created before world handler init.
        /// </summary>
        public void Start()
        {
            var commonPath = Path.Combine(_gamePath, "DNA.Common.dll");
            if (!File.Exists(commonPath))
            {
                throw new InvalidOperationException("DNA.Common.dll not found in game path: " + _gamePath);
            }

            _commonAsm = Assembly.LoadFrom(commonPath) ?? throw new InvalidOperationException("Failed to load DNA.Common");
            Assembly xnaAsm = null;
            try
            {
                // Try to load from the GAC (Global Assembly Cache)
                xnaAsm = Assembly.Load("Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
                _log("Loaded Microsoft.Xna.Framework.dll from GAC.");
            }
            catch (Exception ex)
            {
                _log("Failed to load XNA Framework from GAC: " + ex.Message);
            }

            if (xnaAsm == null)
            {
                throw new InvalidOperationException("Could not load Microsoft.Xna.Framework.dll. Please ensure the XNA 4.0 Redistributable is installed.");
            }

            var configType = _commonAsm.GetType("DNA.Net.Lidgren.NetPeerConfiguration") ?? throw new InvalidOperationException("NetPeerConfiguration not found");

            var peerType = _commonAsm.GetType("DNA.Net.Lidgren.NetPeer") ?? throw new InvalidOperationException("NetPeer not found");

            var config = Activator.CreateInstance(configType, _gameName) ?? throw new InvalidOperationException("Failed to create NetPeerConfiguration");
            configType.GetProperty("LocalAddress").SetValue(config, _bindAddress);
            configType.GetProperty("Port").SetValue(config, _port);
            configType.GetProperty("AcceptIncomingConnections").SetValue(config, true);
            configType.GetProperty("MaximumConnections").SetValue(config, _maxPlayers);
            configType.GetProperty("UseMessageRecycling").SetValue(config, true);
            configType.GetProperty("NetworkThreadName").SetValue(config, "CMZ Server");

            var discoveryType = _commonAsm.GetType("DNA.Net.Lidgren.NetIncomingMessageType") ?? throw new InvalidOperationException("NetIncomingMessageType not found");
            _discoveryRequestType = Enum.Parse(discoveryType, "DiscoveryRequest");
            var connApproval = Enum.Parse(discoveryType, "ConnectionApproval");
            var statusChanged = Enum.Parse(discoveryType, "StatusChanged");
            var dataVal = Enum.Parse(discoveryType, "Data");

            configType.GetMethod("EnableMessageType").Invoke(config, [_discoveryRequestType]);
            configType.GetMethod("EnableMessageType").Invoke(config, [connApproval]);
            configType.GetMethod("EnableMessageType").Invoke(config, [statusChanged]);
            configType.GetMethod("EnableMessageType").Invoke(config, [dataVal]);

            _netPeer = Activator.CreateInstance(peerType, config);
            try
            {
                peerType.GetMethod("Start").Invoke(_netPeer, null);
            }
            catch (System.Reflection.TargetInvocationException tex)
            {
                var inner = tex.InnerException;
                throw new InvalidOperationException("NetPeer.Start failed: " + (inner?.Message ?? tex.Message), inner);
            }

            // _connections = peerType.GetProperty("Connections").GetValue(_netPeer);

            CreateHostGamer(xnaAsm);

            if (_worldHandler != null && _gameAsm != null)
            {
                if (_gameAsm == null)
                    throw new InvalidOperationException("_gameAsm is null.");

                if (_commonAsm == null)
                    throw new InvalidOperationException("_commonAsm is null.");

                _worldHandler?.Init(_gameAsm, _commonAsm);
                ApplyCurrentServerMessageToWorldInfo(saveToDisk: false);

                _codec = new CmzMessageCodec(_gameAsm, _commonAsm, _log);
            }

            _running = true;
            var bindStr = _bindAddress.Equals(IPAddress.Any) ? "0.0.0.0 (all interfaces)" : _bindAddress.ToString();
            _log($"Lidgren server started on {bindStr}:{_port}");
        }

        /// <summary>
        /// Stops the dedicated server and shuts down the underlying NetPeer.
        /// </summary>
        public void Stop()
        {
            _running = false;
            if (_netPeer != null)
            {
                try
                {
                    _netPeer.GetType().GetMethod("Shutdown").Invoke(_netPeer, ["Server stopped"]);
                }
                catch
                {
                }

                _netPeer = null;
            }
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
        /// 
        /// Notes:
        /// - This does not restart the socket.
        /// - Network/bootstrap values such as port, bind IP, game path, world GUID,
        ///   save owner, game name, and network version still require a restart.
        /// </summary>
        public void Reload()
        {
            ReloadServerProperties();
            ReloadPlugins();
        }

        /// <summary>
        /// Reloads runtime-safe values from server.properties.
        /// </summary>
        public void ReloadServerProperties()
        {
            if (string.IsNullOrWhiteSpace(_saveRoot))
            {
                _log("[Config] Reload skipped: save root/base directory is not available.");
                return;
            }

            ServerConfig config;

            try
            {
                config = ServerConfig.Load(_saveRoot);
            }
            catch (Exception ex)
            {
                _log("[Config] Reload failed: " + ex.Message);
                return;
            }

            _serverName = string.IsNullOrWhiteSpace(config.ServerName)
                ? "CMZ Server"
                : config.ServerName;

            _serverMessage = string.IsNullOrWhiteSpace(config.ServerMessage)
                ? "Welcome to this CastleForge dedicated server."
                : config.ServerMessage;

            // These affect newly advertised/session-info values.
            _gameMode = config.GameMode;
            _pvpState = config.PvpState < 0 ? 0 : (config.PvpState > 2 ? 2 : config.PvpState);
            _difficulty = config.Difficulty < 0 ? 0 : (config.Difficulty > 3 ? 3 : config.Difficulty);

            // Do not hot-apply _maxPlayers to Lidgren's already-created NetPeer.
            // The socket was created with the old MaximumConnections value.
            _log("[Config] Reloaded runtime properties from server.properties.");
            _log("[Config] Note: port/ip/game-path/world/save-owner/max-players require restart.");

            string configPath = Path.Combine(_saveRoot, "server.properties");
            string resolvedMessage = BuildServerDisplayMessage();

            _log($"[Config] Reloaded from: {configPath}");
            _log($"[Config] server-message raw: '{_serverMessage}'");
            _log($"[Config] server-message resolved: '{resolvedMessage}'");

            ApplyCurrentServerMessageToWorldInfo(saveToDisk: false);
        }

        /// <summary>
        /// Pumps incoming messages and performs periodic server-side tasks.
        ///
        /// Purpose:
        /// - Reads and dispatches all pending Lidgren messages.
        /// - Recycles processed messages.
        /// - Periodically broadcasts time-of-day updates to connected clients.
        ///
        /// Notes:
        /// - Message dispatch is reflection-driven.
        /// - Non-data message types are logged for visibility.
        /// </summary>
        public void Update()
        {
            if (_netPeer == null || !_running)
                return;

            var peerType = _netPeer.GetType();
            var readMsg = peerType.GetMethod("ReadMessage");
            var incomingMsgType = _commonAsm.GetType("DNA.Net.Lidgren.NetIncomingMessage");
            var recycle = peerType.GetMethod("Recycle", [incomingMsgType]);

            object msg;
            while ((msg = readMsg.Invoke(_netPeer, null)) != null)
            {
                try
                {
                    var msgType = msg.GetType();
                    var messageType = msgType.GetProperty("MessageType").GetValue(msg);
                    var msgTypeEnum = messageType.GetType();
                    var connApprovalVal = Enum.Parse(msgTypeEnum, "ConnectionApproval");
                    var statusChangedVal = Enum.Parse(msgTypeEnum, "StatusChanged");
                    var dataVal = Enum.Parse(msgTypeEnum, "Data");
                    var discoveryVal = Enum.Parse(msgTypeEnum, "DiscoveryRequest");

                    try
                    {
                        string msgName = messageType != null ? messageType.ToString() : "(null)";

                        if (msgName != "Data")
                            _log("[Server] Incoming MessageType = " + msgName);
                    }
                    catch
                    {
                    }

                    if (messageType.Equals(discoveryVal))
                    {
                        _log("Discovery message received, handling...");
                        HandleDiscoveryRequest(msg);
                    }
                    else if (messageType.Equals(connApprovalVal))
                    {
                        HandleConnectionApproval(msg);
                    }
                    else if (messageType.Equals(statusChangedVal))
                    {
                        HandleStatusChanged(msg);
                    }
                    else if (messageType.Equals(dataVal))
                    {
                        HandleDataMessage(msg);
                    }
                }
                catch (Exception ex)
                {
                    _log($"Message handling error: {ex.GetType().Name}: {ex.Message}");
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        foreach (var line in ex.StackTrace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                            _log($"  {line.Trim()}");
                    }
                }
                finally
                {
                    recycle?.Invoke(_netPeer, [msg]);
                }
            }

            // Time of day: Advance using real elapsed time and broadcast periodically.
            if (_worldHandler != null)
            {
                DateTime now = DateTime.UtcNow;
                float deltaSeconds = (float)(now - _lastTimeOfDayAdvance).TotalSeconds;
                _lastTimeOfDayAdvance = now;

                if (deltaSeconds < 0f)
                    deltaSeconds = 0f;
                if (deltaSeconds > 1f)
                    deltaSeconds = 1f; // Avoids huge jumps if paused/debugged.

                _timeOfDay += deltaSeconds / SecondsPerFullDay;

                // This is mainly for logs. Actual Lidgren discovery/server-info packets
                // resolve the name at the moment they are sent.
                LogServerDisplayNameIfNeeded();

                if ((now - _lastTimeOfDaySend).TotalSeconds >= 5.0)
                {
                    _lastTimeOfDaySend = now;

                    var payload = _worldHandler.BuildTimeOfDayPayload(_timeOfDay);
                    if (payload != null && payload.Length > 0)
                    {
                        var reliableOrdered = Enum.Parse(_commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod"), "ReliableOrdered");
                        var liveConnections = GetLiveConnections();

                        if (liveConnections != null)
                        {
                            foreach (var conn in liveConnections)
                            {
                                if (!_connectionToGamer.TryGetValue(conn, out var gamer))
                                    continue;

                                var gamerId = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer);
                                var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                                WriteChannel0Packet(om, gamerId, 0, payload);

                                conn.GetType()
                                    .GetMethod("SendMessage", [om.GetType(), reliableOrdered.GetType(), typeof(int)])
                                    ?.Invoke(conn, [om, reliableOrdered, 0]);
                            }
                        }
                    }
                }
            }

            // Run optional tick-based plugins, such as Announcements.
            UpdateServerPlugins();
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
                ConnectedPlayers = _allGamers.Count,
                MaxPlayers = _maxPlayers,
                BroadcastMessage = BroadcastServerText,
                Log = _log
            });
        }

        /// <summary>
        /// Notifies optional player-event plugins that a player joined.
        /// </summary>
        private void NotifyPluginsPlayerJoined(object senderConn, byte playerId, object remoteGamer)
        {
            if (_plugins == null)
                return;

            string playerName = "Player" + playerId;

            try
            {
                object name = remoteGamer?.GetType()
                    .GetProperty("Gamertag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(remoteGamer, null);

                if (name != null && !string.IsNullOrWhiteSpace(Convert.ToString(name)))
                    playerName = Convert.ToString(name);
            }
            catch
            {
            }

            _plugins.NotifyPlayerJoined(new ServerPlayerEventContext
            {
                PlayerId = playerId,
                PlayerName = playerName,
                ConnectedPlayers = _allGamers.Count,
                MaxPlayers = _maxPlayers,
                SendPrivateMessage = message => SendPrivateServerText(senderConn, playerId, message),
                BroadcastMessage = BroadcastServerText,
                Log = _log
            });
        }

        /// <summary>
        /// Notifies optional player-event plugins that a player left.
        /// </summary>
        private void NotifyPluginsPlayerLeft(byte playerId)
        {
            if (_plugins == null)
                return;

            _plugins.NotifyPlayerLeft(new ServerPlayerEventContext
            {
                PlayerId = playerId,
                PlayerName = "Player" + playerId,
                ConnectedPlayers = _allGamers.Count,
                MaxPlayers = _maxPlayers,
                BroadcastMessage = BroadcastServerText,
                Log = _log
            });
        }
        #endregion

        #region Host Gamer Creation

        /// <summary>
        /// Creates the synthetic host gamer object used by the dedicated server.
        ///
        /// Purpose:
        /// - Mimics the host-side gamer shape expected by connected clients.
        /// - Ensures the host has a non-null PlayerID payload compatible with peer serialization.
        ///
        /// Notes:
        /// - Uses a random 16-byte host hash because some client-side gamer reads expect non-null PlayerID.Data.
        /// </summary>
        private void CreateHostGamer(Assembly xnaAsm)
        {
            var networkGamerType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkGamer");
            _ = _commonAsm.GetType("DNA.Net.GamerServices.SimpleGamer");
            var signedInGamerType = _commonAsm.GetType("DNA.Net.GamerServices.SignedInGamer");
            var playerIndexType = xnaAsm.GetType("Microsoft.Xna.Framework.PlayerIndex");
            var playerIDType = _commonAsm.GetType("DNA.PlayerID");

            var playerIndex = Enum.ToObject(playerIndexType, 0);

            // Host must have non-null PlayerID.Data (16 bytes like clients) or Write(Gamer) emits -1
            // and client ReadGamer fails.
            byte[] hostHash = Guid.NewGuid().ToByteArray();
            var playerID = Activator.CreateInstance(playerIDType, [hostHash]);

            var serverGamer = Activator.CreateInstance(signedInGamerType, [playerIndex, playerID, "Server"]);

            _hostGamer = Activator.CreateInstance(networkGamerType, [serverGamer, null, true, true, (byte)0]);
        }
        #endregion

        #region Discovery Handling

        /// <summary>
        /// Handles discovery requests and replies with host/session information.
        ///
        /// Purpose:
        /// - Validates incoming discovery requests against game name and network version.
        /// - Returns current session metadata such as player count, max players, and session properties.
        ///
        /// Notes:
        /// - Uses HostDiscoveryResponseMessage.
        /// - PasswordProtected is currently always false.
        /// </summary>
        private void HandleDiscoveryRequest(object msg)
        {
            try
            {
                var msgType = msg.GetType();
                if (msgType.GetProperty("SenderEndPoint")?.GetValue(msg) is not IPEndPoint senderEndPoint)
                    return;

                var lidgrenExt = _commonAsm.GetType("DNA.Net.GamerServices.LidgrenExtensions");
                var readDiscovery = lidgrenExt?.GetMethod("ReadDiscoveryRequestMessage", BindingFlags.Public | BindingFlags.Static);
                if (readDiscovery == null)
                {
                    _log("Discovery: ReadDiscoveryRequestMessage method not found");
                    return;
                }

                var request = readDiscovery.Invoke(null, [msg, _gameName, _networkVersion]);
                if (request == null)
                    return;

                var readResult = request.GetType().GetField("ReadResult", BindingFlags.Public | BindingFlags.Instance)?.GetValue(request);
                var successEnum = Enum.Parse(readResult?.GetType(), "Success");
                if (readResult == null || !readResult.Equals(successEnum))
                {
                    _log("Discovery: request read failed or validation failed (ReadResult=" + (readResult?.ToString() ?? "null") + ")");
                    return;
                }

                var requestId = (int)(request.GetType().GetField("RequestID", BindingFlags.Public | BindingFlags.Instance)?.GetValue(request) ?? 0);
                string displayName = BuildServerDisplayName();
                string displayMessage = BuildServerDisplayMessage();

                _log("Discovery request from " + senderEndPoint + " RequestID=" + requestId + ", sending response (server name='" + displayName + "', message='" + displayMessage + "')");

                var resultCodeType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkSession+ResultCode");
                var succeeded = Enum.ToObject(resultCodeType, 0); // Succeeded = 0

                var sessionPropsType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkSessionProperties");
                var sessionProps = Activator.CreateInstance(sessionPropsType);
                if (sessionProps != null)
                {
                    var itemProp = sessionPropsType.GetProperty("Item", [typeof(int)]);
                    itemProp?.SetValue(sessionProps, (int?)_gameMode, [2]);
                    itemProp?.SetValue(sessionProps, (int?)_difficulty, [3]);
                    itemProp?.SetValue(sessionProps, (int?)_pvpState, [5]);
                }

                var responseType = _commonAsm.GetType("DNA.Net.GamerServices.HostDiscoveryResponseMessage");
                var response = Activator.CreateInstance(responseType);
                if (response == null)
                    return;

                responseType.GetField("Result", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, succeeded);
                responseType.GetField("RequestID", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, requestId);
                responseType.GetField("SessionID", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, 1);
                responseType.GetField("CurrentPlayers", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, _allGamers.Count);
                responseType.GetField("MaxPlayers", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, _maxPlayers);
                responseType.GetField("Message", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, displayMessage);
                responseType.GetField("HostUsername", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, displayName);
                responseType.GetField("PasswordProtected", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, false);
                responseType.GetField("SessionProperties", BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, sessionProps);

                var peerType = _netPeer.GetType();
                var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                var writeExt = lidgrenExt?.GetMethod(
                    "Write",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [_commonAsm.GetType("DNA.Net.Lidgren.NetBuffer"), responseType, typeof(string), typeof(int)],
                    null);

                writeExt?.Invoke(null, [om, response, _gameName, _networkVersion]);

                var sendDiscovery = peerType.GetMethod("SendDiscoveryResponse", [om.GetType(), typeof(IPEndPoint)]);
                if (sendDiscovery == null)
                {
                    _log("Discovery: SendDiscoveryResponse method not found");
                }
                else
                {
                    sendDiscovery.Invoke(_netPeer, [om, senderEndPoint]);
                    _log("Discovery response sent to " + senderEndPoint);
                }
            }
            catch (Exception ex)
            {
                _log("Discovery response error: " + ex.Message);
            }
        }
        #endregion

        #region Connection Approval

        /// <summary>
        /// Handles incoming connection approval.
        ///
        /// Purpose:
        /// - Reads the RequestConnectToHost message.
        /// - Validates read result / version compatibility.
        /// - Copies the approved gamer object into the connection Tag.
        /// - Approves or denies the connection.
        ///
        /// Notes:
        /// - The approval path preserves the incoming gamer object until post-connect handling.
        /// - "unknow ghost" is normalized to "Player".
        /// </summary>
        private void HandleConnectionApproval(object msg)
        {
            var senderConn = msg.GetType().GetProperty("SenderConnection").GetValue(msg);
            if (senderConn == null)
            {
                _log("ConnectionApproval: SenderConnection is null");
                return;
            }

            var lidgrenExt = _commonAsm.GetType("DNA.Net.GamerServices.LidgrenExtensions");
            var netBufferType = _commonAsm.GetType("DNA.Net.Lidgren.NetBuffer");
            var readMethod = lidgrenExt?.GetMethod(
                "ReadRequestConnectToHostMessage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                [netBufferType, typeof(string), typeof(int)],
                null);

            if (readMethod == null)
                return;

            var crm = readMethod.Invoke(null, [msg, _gameName, _networkVersion]);
            if (crm == null)
                return;

            var readResultField = crm.GetType().GetField("ReadResult", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            var readResult = readResultField?.GetValue(crm);
            if (readResult == null)
            {
                _log("ConnectionApproval: ReadResult is null");
                return;
            }

            var successVal = Enum.Parse(readResult.GetType(), "Success");

            var connType = _commonAsm.GetType("DNA.Net.Lidgren.NetConnection");
            if (connType == null)
            {
                _log("NetConnection type not found");
                return;
            }

            if (!readResult.Equals(successVal))
            {
                var denyReason = readResult.ToString();
                connType.GetMethod("Deny", [typeof(string)]).Invoke(senderConn, [denyReason]);
                _log($"Connection denied: {denyReason}");
                return;
            }

            var gamer = crm.GetType().GetField("Gamer")?.GetValue(crm);
            if (gamer == null)
            {
                _log("ConnectionApproval: Gamer is null");
                return;
            }

            var gamerType = gamer.GetType();
            var gamerTag = gamerType.GetProperty("Gamertag")?.GetValue(gamer);
            var displayName = gamerType.GetProperty("DisplayName")?.GetValue(gamer);
            _log($"ConnectionApproval: Gamer object received. Gamertag: {gamerTag}, DisplayName: {displayName}");

            if (gamerTag as string == "unknow ghost")
            {
                gamerType.GetProperty("Gamertag")?.SetValue(gamer, "Player");
                _log("ConnectionApproval: Overwrote 'unknow ghost' with 'Player'");
            }

            var tagProp = connType.GetProperty("Tag", BindingFlags.Public | BindingFlags.Instance);
            if (tagProp == null)
            {
                _log("ConnectionApproval: Tag property not found");
                return;
            }

            tagProp.SetValue(senderConn, gamer);

            connType.GetMethod("Approve", Type.EmptyTypes).Invoke(senderConn, null);
            _log($"Connection approved from {gamer?.GetType().GetProperty("Gamertag")?.GetValue(gamer)}");
        }

        private System.Collections.IEnumerable GetLiveConnections()
        {
            if (_netPeer == null)
                return null;

            var peerType = _netPeer.GetType();
            return peerType.GetProperty("Connections")?.GetValue(_netPeer) as System.Collections.IEnumerable;
        }
        #endregion

        #region Status / Join / Leave Handling

        /// <summary>
        /// Handles connection status changes.
        ///
        /// Purpose:
        /// - Removes disconnected players from runtime collections.
        /// - Builds and sends the ConnectedMessage for newly connected players.
        /// - Sends server-info and initial time-of-day bootstrap data.
        /// - Broadcasts NewPeer messages to already connected clients.
        ///
        /// Notes:
        /// - Status 7 is treated as disconnect.
        /// - Status 5 is treated as connected.
        /// - ConnectedMessage is intentionally built before adding the new remote gamer to _allGamers,
        ///   matching vanilla ordering expectations and avoiding duplicate self-proxy creation on clients.
        /// </summary>
        private void HandleStatusChanged(object msg)
        {
            var msgType = msg.GetType();
            var readByte = msgType.GetMethod("ReadByte", Type.EmptyTypes);
            if (readByte == null)
                return;

            var status = (byte)readByte.Invoke(msg, null);

            if (status == 7)
            {
                var conn = msgType.GetProperty("SenderConnection").GetValue(msg);
                if (conn != null && _connectionToGamer.TryGetValue(conn, out var disconnectedGamer))
                {
                    byte disconnectedId = (byte)disconnectedGamer.GetType().GetProperty("Id").GetValue(disconnectedGamer);

                    // Notify remaining clients to remove this peer from their session/player state.
                    try
                    {
                        var dropPeerHostPeerType = _netPeer.GetType();
                        var dropPeerDelivery = Enum.Parse(_commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod"), "ReliableOrdered");
                        var dropPeerConnections = GetLiveConnections();

                        var dropPeerNetBufferType = _commonAsm.GetType("DNA.Net.Lidgren.NetBuffer");
                        var dropPeerMsgType = _commonAsm.GetType("DNA.Net.GamerServices.DropPeerMessage");
                        var dropPeerLidgrenExt = _commonAsm.GetType("DNA.Net.GamerServices.LidgrenExtensions");
                        var writeDropPeer = dropPeerLidgrenExt?.GetMethod(
                            "Write",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            [dropPeerNetBufferType, dropPeerMsgType],
                            null);

                        if (dropPeerMsgType != null && writeDropPeer != null && dropPeerConnections != null)
                        {
                            object dropPeer = Activator.CreateInstance(dropPeerMsgType);
                            dropPeerMsgType.GetField("PlayerGID")?.SetValue(dropPeer, disconnectedId);

                            foreach (var otherConn in dropPeerConnections)
                            {
                                if (otherConn == null || ReferenceEquals(otherConn, conn))
                                    continue;

                                if (!_connectionToGamer.TryGetValue(otherConn, out _))
                                    continue;

                                var om = dropPeerHostPeerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                                var omType = om.GetType();

                                // Internal session/system message type 2 = DropPeer
                                omType.GetMethod("Write", [typeof(byte)]).Invoke(om, [(byte)2]);
                                writeDropPeer.Invoke(null, [om, dropPeer]);

                                otherConn.GetType()
                                    .GetMethod("SendMessage", [om.GetType(), dropPeerDelivery.GetType(), typeof(int)])
                                    ?.Invoke(otherConn, [om, dropPeerDelivery, 1]);

                                if (_connectionToGamer.TryGetValue(otherConn, out var otherGamer))
                                {
                                    byte otherId = (byte)otherGamer.GetType().GetProperty("Id").GetValue(otherGamer);
                                    _log($"Sent DropPeer for player {disconnectedId} to recipient {otherId}.");
                                }
                            }
                        }
                        else
                        {
                            _log($"DropPeer broadcast skipped for player {disconnectedId} (missing reflected type/method or live connections).");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"Broadcast DropPeer failed: {ex.GetType().FullName}: {ex.Message}.");
                    }

                    _connectionToGamer.Remove(conn);
                    _allGamers.Remove(disconnectedGamer);
                    _playerExistsPayloadById.Remove(disconnectedId);

                    _worldHandler?.OnClientDisconnected(disconnectedId);
                    NotifyPluginsPlayerLeft(disconnectedId);

                    ApplyCurrentServerMessageToWorldInfo(saveToDisk: false);

                    _log($"Player disconnected: id={disconnectedId}, {_allGamers.Count} remaining.");

                    if (_allGamers.Count == 0)
                        _nextPlayerGid = 1;
                }

                return;
            }

            if (status != 5)
                return;

            var senderConn = msgType.GetProperty("SenderConnection").GetValue(msg);
            if (senderConn == null)
            {
                _log("StatusChanged: SenderConnection is null");
                return;
            }

            var gamer = senderConn.GetType().GetProperty("Tag").GetValue(senderConn);
            if (gamer == null)
            {
                _log("StatusChanged: Tag (gamer) is null");
                return;
            }

            var networkGamerType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkGamer");
            var sessionType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkSession");
            var providerType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkSessionProvider");
            var ngCtor = networkGamerType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                null,
                [_commonAsm.GetType("DNA.Net.GamerServices.Gamer"), sessionType, typeof(bool), typeof(bool), typeof(byte), typeof(System.Net.IPAddress)],
                null);

            var remoteEp = senderConn.GetType().GetProperty("RemoteEndPoint").GetValue(senderConn);
            if (remoteEp == null)
            {
                _log("StatusChanged: RemoteEndPoint is null");
                return;
            }

            var addr = remoteEp.GetType().GetProperty("Address").GetValue(remoteEp);

            object remoteGamer = null;
            if (ngCtor != null)
            {
                try
                {
                    remoteGamer = ngCtor.Invoke([gamer, null, false, false, _nextPlayerGid, addr]);
                }
                catch (Exception ex)
                {
                    _log($"NetworkGamer create failed: {ex.Message}");
                }
            }

            if (remoteGamer == null)
                return;

            networkGamerType.GetField("NetConnectionObject")?.SetValue(remoteGamer, senderConn);
            // Tag stays as approval Gamer until after ConnectedMessage - matches game host order.

            if (_hostGamer == null)
            {
                _log("Host gamer is null; cannot send ConnectedMessage");
                return;
            }

            // Game host sends ConnectedMessage with SetPeerList(_allGamers) BEFORE AddRemoteGamer - peer list
            // must NOT include the joining client or client does AddLocalGamer(..., gid) + AddProxyGamer(self, gid) -> duplicate _idToGamer.
            var netBufferType = _commonAsm.GetType("DNA.Net.Lidgren.NetBuffer");
            var connectedMsgType = _commonAsm.GetType("DNA.Net.GamerServices.ConnectedMessage");
            var connectedMsg = Activator.CreateInstance(connectedMsgType);
            connectedMsgType.GetField("PlayerGID")?.SetValue(connectedMsg, _nextPlayerGid);

            var gamerType = _commonAsm.GetType("DNA.Net.GamerServices.Gamer");

            // Peers = host + existing remotes only (joiner added after send).
            var peerCount = 1 + _allGamers.Count;
            var peersArray = Array.CreateInstance(gamerType, peerCount);
            var idsArray = new byte[peerCount];

            peersArray.SetValue(_hostGamer, 0);
            idsArray[0] = 0;

            for (int i = 0; i < _allGamers.Count; i++)
            {
                var g = _allGamers[i];
                peersArray.SetValue(g, i + 1);
                idsArray[i + 1] = (byte)g.GetType().GetProperty("Id").GetValue(g);
            }

            connectedMsgType.GetField("Peers")?.SetValue(connectedMsg, peersArray);
            connectedMsgType.GetField("ids")?.SetValue(connectedMsg, idsArray);

            var peerType = _netPeer.GetType();
            var createMsg = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
            var createMsgType = createMsg.GetType();
            createMsgType.GetMethod("Write", [typeof(byte)]).Invoke(createMsg, [(byte)1]);

            var lidgrenExt = _commonAsm.GetType("DNA.Net.GamerServices.LidgrenExtensions");
            var writeConnected = lidgrenExt?.GetMethod("Write", BindingFlags.Public | BindingFlags.Static, null, [netBufferType, connectedMsgType], null);
            writeConnected?.Invoke(null, [createMsg, connectedMsg]);

            // Hex dump of payload (channel 1) for comparing to real host - read-only diagnostic
            try
            {
                var lenObj = createMsgType.GetProperty("LengthBytes")?.GetValue(createMsg);
                if (createMsgType.GetMethod("PeekDataBuffer", Type.EmptyTypes)?.Invoke(createMsg, null) is byte[] peekData && lenObj is int len && len > 0)
                {
                    int n = Math.Min(len, 96);
                    var sb = new System.Text.StringBuilder(n * 3);
                    for (int i = 0; i < n; i++)
                        sb.Append(peekData[i].ToString("X2")).Append(i + 1 < n ? " " : "");
                    _log($"ConnectedMessage payload hex ({n}/{len} bytes): {sb}");
                }
            }
            catch
            {
                /* ignore log failures */
            }

            _log($"ConnectedMessage Contents:");
            _log($"  PlayerGID: {connectedMsgType.GetField("PlayerGID")?.GetValue(connectedMsg)}");

            if (connectedMsgType.GetField("Peers")?.GetValue(connectedMsg) is Array peers)
            {
                _log($"  Peers ({peers.Length}):");
                for (int i = 0; i < peers.Length; i++)
                {
                    var peer = peers.GetValue(i);
                    var gamerTag = peer.GetType().GetProperty("Gamertag")?.GetValue(peer);
                    var id = peer.GetType().GetProperty("Id")?.GetValue(peer);
                    _log($"    - Gamertag: {gamerTag}, Id: {id}");
                }
            }

            _log($"Sending ConnectedMessage: PlayerGID={_nextPlayerGid}, PeerCount={peersArray.Length}");

            var reliableOrdered = Enum.Parse(_commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod"), "ReliableOrdered");
            var sendMsgMethod = senderConn.GetType().GetMethod("SendMessage", [createMsg.GetType(), reliableOrdered.GetType(), typeof(int)]);
            sendMsgMethod?.Invoke(senderConn, [createMsg, reliableOrdered, 1]);

            // Send server info (name, max players, game mode) so client can cache it.
            // Channel-1 type 255 = CMZ server info.
            try
            {
                string displayName = BuildServerDisplayName();

                var serverInfoMsg = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                var siType = serverInfoMsg.GetType();
                siType.GetMethod("Write", [typeof(byte)]).Invoke(serverInfoMsg, [(byte)255]);
                siType.GetMethod("Write", [typeof(string)]).Invoke(serverInfoMsg, [displayName]);
                siType.GetMethod("Write", [typeof(int)]).Invoke(serverInfoMsg, [_maxPlayers]);
                siType.GetMethod("Write", [typeof(int)]).Invoke(serverInfoMsg, [_gameMode]);
                siType.GetMethod("Write", [typeof(int)]).Invoke(serverInfoMsg, [_difficulty]);
                sendMsgMethod?.Invoke(senderConn, [serverInfoMsg, reliableOrdered, 1]);

                _log("Sent server info to client: name='" + displayName + "' max=" + _maxPlayers + " gameMode=" + _gameMode + " difficulty=" + _difficulty);
            }
            catch (Exception ex)
            {
                _log("Send server info: " + ex.Message);
            }

            // Now add joiner and set Tag - same order as game host after send.
            _allGamers.Add(remoteGamer);
            _connectionToGamer[senderConn] = remoteGamer;
            NotifyPluginsPlayerJoined(senderConn, _nextPlayerGid, remoteGamer);

            ApplyCurrentServerMessageToWorldInfo(saveToDisk: false);

            // Send current time of day so joiner sees correct time immediately.
            try
            {
                if (_worldHandler != null)
                {
                    var todPayload = _worldHandler.BuildTimeOfDayPayload(_timeOfDay);
                    if (todPayload != null && todPayload.Length > 0)
                    {
                        var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                        WriteChannel0Packet(om, _nextPlayerGid, 0, todPayload);
                        sendMsgMethod?.Invoke(senderConn, [om, reliableOrdered, 0]);
                    }
                }
            }
            catch (Exception ex)
            {
                _log("Send time of day on join: " + ex.Message);
            }

            _commonAsm.GetType("DNA.Net.Lidgren.NetConnection").GetProperty("Tag", BindingFlags.Public | BindingFlags.Instance)?.SetValue(senderConn, remoteGamer);

            // NewPeer: AddNewPeer uses ReadByte() for type then id - must write bytes, not int.
            var liveConnections = GetLiveConnections();
            if (liveConnections == null)
                return;

            foreach (var conn in liveConnections)
            {
                if (conn == senderConn)
                    continue;

                var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                var omType = om.GetType();
                omType.GetMethod("Write", [typeof(byte)]).Invoke(om, [(byte)0]);
                omType.GetMethod("Write", [typeof(byte)]).Invoke(om, [_nextPlayerGid]);

                var writeGamer = lidgrenExt?.GetMethod("Write", BindingFlags.Public | BindingFlags.Static, null, [netBufferType, gamerType], null);
                writeGamer?.Invoke(null, [om, remoteGamer]);

                sendMsgMethod?.Invoke(conn, [om, reliableOrdered, 1]);
            }

            _nextPlayerGid++;
            _log($"Player {_nextPlayerGid - 1} joined");
        }
        #endregion

        #region Packet Writing Helpers

        /// <summary>
        /// Writes a channel-0 packet in the shape the client expects:
        /// recipient byte, sender byte, byte-array payload.
        ///
        /// Notes:
        /// - Client-side channel-0 reading expects ReadByte, ReadByte, then ReadByteArray.
        /// - LidgrenExtensions.WriteArray is preferred because raw NetBuffer.Write(byte[]) does not prepend the length.
        /// - The fallback path is only a last resort and may not match the exact expected wire shape.
        /// </summary>
        private void WriteChannel0Packet(object om, byte recipient, byte sender, byte[] payload)
        {
            var omType = om.GetType();
            omType.GetMethod("Write", [typeof(byte)])?.Invoke(om, [recipient]);
            omType.GetMethod("Write", [typeof(byte)])?.Invoke(om, [sender]);

            var netBufferType = _commonAsm.GetType("DNA.Net.Lidgren.NetBuffer");
            var lidgrenExt = _commonAsm.GetType("DNA.Net.GamerServices.LidgrenExtensions");
            var writeArray = lidgrenExt?.GetMethod("WriteArray", BindingFlags.Public | BindingFlags.Static, null, [netBufferType, typeof(byte[])], null);

            if (writeArray != null && payload != null)
                writeArray.Invoke(null, [om, payload]);
            else if (payload != null)
                omType.GetMethod("Write", [typeof(byte[])])?.Invoke(om, [payload]); // wrong wire shape; last resort
        }

        /// <summary>
        /// Sends a raw CMZ inner payload to exactly one client using the standard channel-0 packet shape.
        ///
        /// Notes:
        /// - recipientId is the local gamer id on the receiving client.
        /// - senderId is the logical network sender id the client should see for the payload.
        /// - This is used for replaying cached player-presence payloads with their original sender ids.
        /// </summary>
        private void SendChannel0PayloadToClient(object conn, byte recipientId, byte senderId, byte[] payload, object delivery = null)
        {
            if (_netPeer == null || conn == null || payload == null)
                return;

            var deliveryType = _commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod");
            if (deliveryType == null)
                return;

            delivery ??= Enum.Parse(deliveryType, "ReliableOrdered");

            var peerType = _netPeer.GetType();
            var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);

            WriteChannel0Packet(om, recipientId, senderId, payload);

            conn.GetType()
                .GetMethod("SendMessage", [om.GetType(), deliveryType, typeof(int)])
                ?.Invoke(conn, [om, delivery, 0]);
        }

        /// <summary>
        /// Returns true if the supplied CMZ payload is a PlayerExistsMessage.
        ///
        /// Notes:
        /// - CMZ payload layout is [msgId][SendData body][checksum].
        /// - For PlayerExistsMessage, the first body byte is RequestResponse.
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

                // PlayerExistsMessage.SendData writes RequestResponse first.
                requestResponse = payload[1] != 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Replays all cached PlayerExistsMessage payloads for existing players to the specified joiner.
        ///
        /// Purpose:
        /// - Allows a new client to reconstruct already-connected remote players even if the
        ///   original request/response handshake was missed or raced during join.
        /// </summary>
        private void ReplayCachedPlayerExistsToJoiner(object joinerConn, byte joinerId)
        {
            if (joinerConn == null)
                return;

            foreach (var kv in _playerExistsPayloadById)
            {
                byte existingPlayerId = kv.Key;
                byte[] payload = kv.Value;

                if (existingPlayerId == joinerId)
                    continue;

                if (payload == null || payload.Length < 2)
                    continue;

                SendChannel0PayloadToClient(joinerConn, joinerId, existingPlayerId, payload);

                _log($"Replayed cached PlayerExistsMessage: existing={existingPlayerId} -> joiner={joinerId}");
            }
        }

        private void SendChannel0PayloadToAll(byte[] payload, byte senderId, byte? exceptPlayerId = null)
        {
            if (_netPeer == null || payload == null)
                return;

            var deliveryType = _commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod");
            var reliableOrdered = Enum.Parse(deliveryType, "ReliableOrdered");
            var peerType = _netPeer.GetType();

            var liveConnections = GetLiveConnections();
            if (liveConnections == null)
                return;

            foreach (var conn in liveConnections)
            {
                if (!_connectionToGamer.TryGetValue(conn, out var gamer))
                    continue;

                byte recipientId = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer);

                if (exceptPlayerId.HasValue && recipientId == exceptPlayerId.Value)
                    continue;

                var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);
                WriteChannel0Packet(om, recipientId, senderId, payload);

                conn.GetType()
                    .GetMethod("SendMessage", [om.GetType(), deliveryType, typeof(int)])
                    ?.Invoke(conn, [om, reliableOrdered, 0]);
            }
        }
        #endregion

        #region Data Message Dispatch

        /// <summary>
        /// Handles incoming data messages for both primary packet channels.
        ///
        /// Channel 0:
        /// - recipient 0 => host-authoritative messages (inventory/world/system)
        /// - recipient X => direct peer relay
        ///
        /// Channel 1:
        /// - internal wrapper packets used by the stock client for:
        ///   - direct client -> peer sends via host (opcode 3)
        ///   - client broadcast -> host -> all peers (opcode 4)
        ///
        /// Notes:
        /// - Channel 1 packets are wrapper packets, not raw gameplay payloads.
        /// - The wrapped payload still needs to be forwarded to clients as a normal channel-0 packet.
        /// - Host-directed packets are offered to the world handler before any fallback relay occurs.
        /// - PlayerExistsMessage payloads are cached so existing players can be replayed to newly joined clients.
        /// </summary>
        private void HandleDataMessage(object msg)
        {
            var msgType = msg.GetType();
            var seqChannel = msgType.GetProperty("SequenceChannel")?.GetValue(msg);
            byte recipientId = 0;
            System.Collections.IEnumerable liveConnections = GetLiveConnections();

            #region Channel 1: Internal / Wrapper Packets

            // ------------------------------------------------------------
            // CHANNEL 1
            // ------------------------------------------------------------
            // Stock wrapper formats:
            //
            // Opcode 3 (direct proxy send):
            //   byte 3
            //   byte recipientId
            //   byte flags
            //   byte senderId
            //   int  len
            //   byte[len] payload
            //
            // Opcode 4 (broadcast-to-host):
            //   byte 4
            //   byte flags
            //   byte senderId
            //   int  len
            //   byte[len] payload
            // ------------------------------------------------------------
            if (seqChannel is int ch && ch == 1)
            {
                var rb = msgType.GetMethod("ReadByte", Type.EmptyTypes);
                var ri = msgType.GetMethod("ReadInt32", Type.EmptyTypes);
                var rbytes = msgType.GetMethod("ReadBytes", [typeof(int)]);

                if (rb == null || ri == null || rbytes == null)
                    return;

                var deliveryType = _commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod");
                if (deliveryType == null)
                    return;

                byte opcode = (byte)rb.Invoke(msg, null);

                // --------------------------------------------------------
                // Opcode 3:
                // client -> host wrapper for direct send to a specific peer
                // --------------------------------------------------------
                if (opcode == 3)
                {
                    recipientId = (byte)rb.Invoke(msg, null);
                    object delivery = Enum.ToObject(deliveryType, (byte)rb.Invoke(msg, null));
                    byte senderId = (byte)rb.Invoke(msg, null);

                    int dataSize = (int)ri.Invoke(msg, null);
                    byte[] payloadBytes =
                        dataSize > 0
                            ? rbytes.Invoke(msg, [dataSize]) as byte[]
                            : [];

                    if (payloadBytes == null)
                        return;

                    object senderConn = msgType.GetProperty("SenderConnection")?.GetValue(msg);
                    if (ShouldDropInboundPacket(senderId, recipientId, 1, 3, payloadBytes, senderConn, 0UL))
                        return;

                    var recipientConn = FindConnectionByPlayerId(recipientId);
                    if (recipientConn == null)
                        return;

                    SendChannel0PayloadToClient(recipientConn, recipientId, senderId, payloadBytes, delivery);
                    return;
                }

                // --------------------------------------------------------
                // Opcode 4:
                // client -> host broadcast wrapper, host may consume and/or relay
                // --------------------------------------------------------
                if (opcode == 4)
                {
                    object delivery = Enum.ToObject(deliveryType, (byte)rb.Invoke(msg, null));
                    byte senderId = (byte)rb.Invoke(msg, null);

                    int dataSize = (int)ri.Invoke(msg, null);
                    byte[] payloadBytes =
                        dataSize > 0
                            ? rbytes.Invoke(msg, [dataSize]) as byte[]
                            : [];

                    if (payloadBytes == null || payloadBytes.Length < 1)
                        return;

                    var senderConn = msgType.GetProperty("SenderConnection")?.GetValue(msg);
                    if (ShouldDropInboundPacket(senderId, 0, 1, 4, payloadBytes, senderConn, 0UL))
                        return;

                    if (_logNetworkPackets)
                    {
                        _log($"CH1 OP4 recv: sender={senderId}, payload={DescribeInnerPayload(payloadBytes)}, bytes={payloadBytes.Length}");
                    }

                    bool acceptedClientTimeSync = TryApplyIncomingTimeOfDay(senderId, payloadBytes);
                    if (acceptedClientTimeSync)
                    {
                        // Optional: Force an immediate authoritative rebroadcast instead of waiting for the next timer tick.
                        _lastTimeOfDaySend = DateTime.MinValue;
                    }

                    if (senderConn == null)
                        return;

                    bool isPlayerExists = TryParsePlayerExistsHeader(payloadBytes, out bool requestResponse);

                    if (isPlayerExists)
                    {
                        _playerExistsPayloadById[senderId] = (byte[])payloadBytes.Clone();
                        _log($"Cached PlayerExistsMessage for player {senderId}; requestResponse={requestResponse}");
                    }

                    void SendToClient(object conn, byte[] payload, byte recipient)
                    {
                        SendChannel0PayloadToClient(conn, recipient, 0, payload, Enum.Parse(deliveryType, "ReliableOrdered"));
                    }

                    // 1) Let authoritative host/world logic consume packets that should
                    //    stay host-only (world info, chunk bootstrap, inventory, etc.).
                    if (_worldHandler != null &&
                        _worldHandler.TryHandleHostMessage(0, senderId, payloadBytes, senderConn, liveConnections, _connectionToGamer, SendToClient))
                    {
                        return;
                    }

                    // 2) If this was a PlayerExists(requestResponse=true), replay all known
                    //    existing players back to the joining client before normal relay.
                    if (isPlayerExists && requestResponse)
                    {
                        ReplayCachedPlayerExistsToJoiner(senderConn, senderId);
                    }

                    // 3) Relay the original payload to all peers except sender,
                    //    preserving the original wrapped delivery flags.
                    if (liveConnections == null)
                        return;

                    foreach (var conn in liveConnections)
                    {
                        if (!_connectionToGamer.TryGetValue(conn, out var gamer))
                            continue;

                        recipientId = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer);

                        if (recipientId == senderId)
                            continue;

                        SendChannel0PayloadToClient(conn, recipientId, senderId, payloadBytes, delivery);
                    }

                    return;
                }

                return;
            }
            #endregion

            #region Channel 0: Standard Game Packets

            // ------------------------------------------------------------
            // CHANNEL 0
            // ------------------------------------------------------------
            // Normal game packet path:
            //   byte recipientId
            //   byte senderId
            //   int  len
            //   byte[len] payload
            // ------------------------------------------------------------
            var readByte = msgType.GetMethod("ReadByte", Type.EmptyTypes);
            var readInt32 = msgType.GetMethod("ReadInt32", Type.EmptyTypes);
            var readBytes = msgType.GetMethod("ReadBytes", [typeof(int)]);
            if (readByte == null || readInt32 == null || readBytes == null)
                return;

            recipientId = (byte)readByte.Invoke(msg, null);
            var senderId0 = (byte)readByte.Invoke(msg, null);

            int len = (int)readInt32.Invoke(msg, null);
            byte[] data;
            if (len == -1)
                data = null;
            else if (len == 0)
                data = [];
            else
                data = readBytes.Invoke(msg, [len]) as byte[];

            if (data == null || data.Length < 1)
                return;

            object senderConn0 = msgType.GetProperty("SenderConnection")?.GetValue(msg);
            if (ShouldDropInboundPacket(senderId0, recipientId, 0, 0, data, senderConn0, 0UL))
                return;

            if (_logNetworkPackets)
            {
                _log($"CH0 recv: recipient={recipientId}, sender={senderId0}, payload={DescribeInnerPayload(data)}, bytes={data.Length}");
            }

            var reliableOrdered = Enum.Parse(_commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod"), "ReliableOrdered");

            // Host-directed packet:
            // give authoritative handlers a chance BEFORE relay.
            if (recipientId == 0)
            {
                if (senderConn0 != null)
                {
                    void SendToClient(object conn, byte[] payload, byte recipient)
                    {
                        SendChannel0PayloadToClient(conn, recipient, 0, payload, reliableOrdered);
                    }

                    if (_worldHandler != null &&
                        _worldHandler.TryHandleHostMessage(recipientId, senderId0, data, senderConn0, liveConnections, _connectionToGamer, SendToClient))
                    {
                        return;
                    }
                }
            }
            #endregion

            #region Fallback Relay

            if (liveConnections == null)
                return;

            foreach (var conn in liveConnections)
            {
                if (!_connectionToGamer.TryGetValue(conn, out var gamer))
                    continue;

                var gamerId = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer);

                if (gamerId == senderId0)
                    continue;

                if (recipientId != 0 && gamerId != recipientId)
                    continue;

                SendChannel0PayloadToClient(conn, gamerId, senderId0, data, reliableOrdered);
            }
            #endregion
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
            object senderConn,
            ulong remoteId)
        {
            if (_plugins == null)
                return false;

            string senderName = ResolvePacketSenderName(senderId, senderConn);

            return _plugins.BeforeInboundPacket(new ServerInboundPacketContext
            {
                SenderId = senderId,
                SenderName = senderName,
                RemoteId = remoteId,
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
        private string ResolvePacketSenderName(byte senderId, object senderConn)
        {
            try
            {
                if (senderConn != null &&
                    _connectionToGamer.TryGetValue(senderConn, out object gamer) &&
                    gamer != null)
                {
                    object name = gamer.GetType()
                        .GetProperty("Gamertag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(gamer, null);

                    if (name != null && !string.IsNullOrWhiteSpace(Convert.ToString(name)))
                        return Convert.ToString(name);
                }
            }
            catch
            {
            }

            return "Player" + senderId;
        }

        private string DescribeInnerPayload(byte[] payload)
        {
            if (_codec == null || payload == null || payload.Length < 1)
                return "<null>";

            try
            {
                byte msgId = payload[0];
                var field = typeof(CmzMessageCodec).GetField("_messageIdToType", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field?.GetValue(_codec) is Dictionary<byte, string> map && map.TryGetValue(msgId, out var typeName))
                    return $"{msgId}={typeName}";
                return $"{msgId}=<unknown>";
            }
            catch
            {
                return "<decode failed>";
            }
        }
        #endregion

        #region Time sync helpers

        /// <summary>
        /// Attempts to apply an incoming TimeOfDayMessage payload to the server's authoritative day value.
        ///
        /// Purpose:
        /// - Lets the server adopt a client-supplied day value when explicitly allowed.
        /// - Keeps the dedicated server authoritative by updating _timeOfDay first, then letting the server
        ///   continue broadcasting that value to all clients.
        ///
        /// Notes:
        /// - Packet layout is [msgId][float dayValue][checksum].
        /// - Returns true when the payload was recognized as a TimeOfDayMessage and successfully parsed.
        /// </summary>
        private bool TryApplyIncomingTimeOfDay(byte senderId, byte[] payload)
        {
            if (/* !_allowClientTimeSync || */ _codec == null || payload == null || payload.Length < 6)
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
        /// Returns the player-facing day number used in the server-name template.
        /// </summary>
        /// <remarks>
        /// The internal time value starts around 0.41 on the first day.
        /// floor(_timeOfDay) + 1 makes that display as Day 1.
        /// </remarks>
        private int GetDisplayDay()
        {
            return Math.Max(1, (int)Math.Floor(_timeOfDay) + 1);
        }

        /// <summary>
        /// Resolves the configured server-name template into the current display name.
        /// </summary>
        /// <remarks>
        /// Supported tokens:
        /// {day}        = player-facing day number
        /// {day00}      = day number padded to two digits
        /// {players}    = current connected player count
        /// {maxplayers} = configured max players
        /// </remarks>
        private string BuildServerDisplayName()
        {
            int day = GetDisplayDay();

            string name = string.IsNullOrWhiteSpace(_serverName)
                ? "CMZ Server"
                : _serverName;

            name = name.Replace("{day}", day.ToString());
            name = name.Replace("{day00}", day.ToString("00"));
            name = name.Replace("{players}", _allGamers.Count.ToString());
            name = name.Replace("{maxplayers}", _maxPlayers.ToString());

            // Keep it safe for old UI/session list display.
            if (name.Length > 64)
                name = name.Substring(0, 64);

            return name;
        }

        /// <summary>
        /// Resolves the configured server-message template into the current display message.
        /// </summary>
        /// <remarks>
        /// Supported tokens:
        /// {day}        = player-facing day number
        /// {day00}      = day number padded to two digits
        /// {players}    = current connected player count
        /// {maxplayers} = configured max players
        /// </remarks>
        private string BuildServerDisplayMessage()
        {
            int day = GetDisplayDay();

            string message = string.IsNullOrWhiteSpace(_serverMessage)
                ? "Welcome to this CastleForge dedicated server."
                : _serverMessage;

            message = message.Replace("{day}", day.ToString());
            message = message.Replace("{day00}", day.ToString("00"));
            message = message.Replace("{players}", _allGamers.Count.ToString());
            message = message.Replace("{maxplayers}", _maxPlayers.ToString());

            // Keep it safe for old UI/session display.
            if (message.Length > 128)
                message = message.Substring(0, 128);

            return message;
        }

        /// <summary>
        /// Applies the currently resolved server-message template to the loaded WorldInfo.
        /// Use saveToDisk=false for dynamic tokens like {players}, {day}, and {maxplayers}
        /// so world.info does not permanently store a stale resolved message.
        /// </summary>
        private void ApplyCurrentServerMessageToWorldInfo(bool saveToDisk)
        {
            _worldHandler?.ApplyServerMessage(BuildServerDisplayMessage(), saveToDisk);
        }

        /// <summary>
        /// Logs when the resolved server display name changes.
        /// Lidgren does not need active lobby metadata refreshing; discovery responses
        /// and join-time server-info packets resolve the name when sent.
        /// </summary>
        private void LogServerDisplayNameIfNeeded()
        {
            int day = GetDisplayDay();
            string name = BuildServerDisplayName();

            if (day == _lastResolvedServerNameDay &&
                string.Equals(name, _lastResolvedServerName, StringComparison.Ordinal))
            {
                return;
            }

            _lastResolvedServerNameDay = day;
            _lastResolvedServerName = name;

            _log("[Server] Display name resolved to: " + name);
        }
        #endregion

        #region General Send Helpers

        private object FindConnectionByPlayerId(byte playerId)
        {
            var liveConnections = GetLiveConnections();
            if (liveConnections == null)
                return null;

            foreach (var conn in liveConnections)
            {
                if (!_connectionToGamer.TryGetValue(conn, out var gamer))
                    continue;

                byte gid = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer, null);
                if (gid == playerId)
                    return conn;
            }

            return null;
        }

        private void SendChannel0PayloadToConnection(object conn, byte recipientId, byte senderId, byte[] payload)
        {
            if (conn == null || payload == null || payload.Length == 0)
                return;

            var peerType = _netPeer.GetType();
            var om = peerType.GetMethod("CreateMessage", Type.EmptyTypes).Invoke(_netPeer, null);

            WriteChannel0Packet(om, recipientId, senderId, payload);

            var deliveryType = _commonAsm.GetType("DNA.Net.Lidgren.NetDeliveryMethod");
            var reliableOrdered = Enum.Parse(deliveryType, "ReliableOrdered");

            var sendMethod = conn.GetType().GetMethod("SendMessage", [om.GetType(), deliveryType, typeof(int)]);
            sendMethod?.Invoke(conn, [om, reliableOrdered, 0]);
        }

        private void SendChannel0PayloadToPlayer(byte recipientId, byte senderId, byte[] payload)
        {
            var conn = FindConnectionByPlayerId(recipientId);
            if (conn == null)
                return;

            SendChannel0PayloadToConnection(conn, recipientId, senderId, payload);
        }

        private void BroadcastChannel0Payload(byte senderId, byte[] payload, byte? excludePlayerId = null)
        {
            if (payload == null || payload.Length == 0)
                return;

            var liveConnections = GetLiveConnections();
            if (liveConnections == null)
                return;

            foreach (var conn in liveConnections)
            {
                if (!_connectionToGamer.TryGetValue(conn, out var gamer))
                    continue;

                byte recipientId = (byte)gamer.GetType().GetProperty("Id").GetValue(gamer, null);

                if (excludePlayerId.HasValue && recipientId == excludePlayerId.Value)
                    continue;

                SendChannel0PayloadToConnection(conn, recipientId, senderId, payload);
            }
        }
        #endregion

        #region Broadcast Relays

        /// <summary>
        /// Sends a server-originated broadcast text message to all connected clients.
        /// </summary>
        /// <param name="text">Message text to display to connected players.</param>
        private void SendBroadcastText(string text)
        {
            byte[] payload = _codec.BuildPayload(
                "DNA.CastleMinerZ.Net.BroadcastTextMessage",
                msg =>
                {
                    _codec.SetMember(msg, "Message", text ?? string.Empty);
                });

            // SenderId = 0 because the dedicated server is acting as host.
            BroadcastChannel0Payload(0, payload);
        }

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
        /// Sends a private server text message to one connected Lidgren client.
        /// </summary>
        private void SendPrivateServerText(object conn, byte recipientId, string text)
        {
            byte[] payload = BuildServerTextPayload(text);

            if (payload == null || payload.Length == 0)
                return;

            SendChannel0PayloadToConnection(conn, recipientId, 0, payload);
        }

        /// <summary>
        /// Broadcasts a server text message to all connected Lidgren clients.
        /// </summary>
        private void BroadcastServerText(string text)
        {
            byte[] payload = BuildServerTextPayload(text);

            if (payload == null || payload.Length == 0)
                return;

            BroadcastChannel0Payload(0, payload);
        }
        #endregion

        #region Plugin Helpers

        /// <summary>
        /// Extracts the stable world key used by plugins for per-world config folders.
        /// </summary>
        private static string GetWorldKey(string worldFolder)
        {
            if (string.IsNullOrWhiteSpace(worldFolder))
                return "default";

            string trimmed = worldFolder.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            string name = Path.GetFileName(trimmed);

            return string.IsNullOrWhiteSpace(name)
                ? "default"
                : name;
        }
        #endregion
    }
}