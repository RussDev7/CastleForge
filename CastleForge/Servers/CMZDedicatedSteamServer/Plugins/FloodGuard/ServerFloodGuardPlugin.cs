/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Plugins.FloodGuard
{
    /// <summary>
    /// Packet-rate flood guard for dedicated servers.
    /// </summary>
    /// <remarks>
    /// Responsibilities:
    /// - Tracks inbound packet counts per sender.
    /// - Drops packets when a sender exceeds the configured per-second limit.
    /// - Temporarily "blackholes" abusive senders for a configurable duration.
    /// - Allows trusted players/senders to bypass protection through the allow list.
    ///
    /// Notes:
    /// - This plugin runs at the raw inbound packet layer, before decoded host/world message handling.
    /// - It does not consume normal decoded host messages through <see cref="BeforeHostMessage"/>.
    /// - Sender id 0 is treated as the authoritative host/server and is never rate-limited.
    /// </remarks>
    internal sealed class ServerFloodGuardPlugin :
        IServerWorldPlugin,
        IServerInboundPacketPlugin,
        IServerPlayerEventPlugin
    {
        #region Fields

        #region Logging

        /// <summary>
        /// Server-provided logger.
        /// Defaults to a no-op logger until the plugin is initialized.
        /// </summary>
        private Action<string> _log = _ => { };

        #endregion

        #region Config State

        /// <summary>
        /// Current in-memory FloodGuard configuration loaded from FloodGuard.Config.ini.
        /// </summary>
        private readonly FloodGuardConfig _config = new();

        /// <summary>
        /// Full plugin directory path:
        /// Plugins/FloodGuard
        /// </summary>
        private string _pluginDir;

        /// <summary>
        /// Full path to the FloodGuard config file:
        /// Plugins/FloodGuard/FloodGuard.Config.ini
        /// </summary>
        private string _configPath;

        #endregion

        #region Runtime Tracking

        /// <summary>
        /// Per-sender packet rate state.
        /// Keyed by CastleMiner Z sender/player id.
        /// </summary>
        private readonly Dictionary<byte, SenderFloodState> _senderStates = [];

        /// <summary>
        /// Cached player names.
        /// Used so AllowedPlayers can match visible player names instead of only numeric ids.
        /// </summary>
        private readonly Dictionary<byte, string> _playerNames = [];

        #endregion

        #endregion

        #region Properties

        /// <summary>
        /// Plugin display name used by the server plugin manager.
        /// </summary>
        public string Name => "FloodGuard";

        #endregion

        #region Plugin Lifecycle

        /// <summary>
        /// Initializes FloodGuard, creates the default config if missing, and reloads file-backed settings.
        /// </summary>
        /// <remarks>
        /// This also clears previous runtime sender state so hot reloads or plugin reloads do not keep
        /// stale packet counters or blackhole timers.
        /// </remarks>
        public void Initialize(ServerPluginContext context)
        {
            _log = context?.Log ?? (_ => { });

            string serverDir = context?.BaseDir;
            if (string.IsNullOrWhiteSpace(serverDir))
                serverDir = AppDomain.CurrentDomain.BaseDirectory;

            serverDir = Path.GetFullPath(serverDir);

            _pluginDir = Path.Combine(serverDir, "Plugins", "FloodGuard");
            Directory.CreateDirectory(_pluginDir);

            _configPath = Path.Combine(_pluginDir, "FloodGuard.Config.ini");

            EnsureDefaultConfig();
            LoadConfig();

            lock (_senderStates)
            {
                _senderStates.Clear();
            }

            _log($"[FloodGuard] Config: {_configPath}.");
            _log(
                $"[FloodGuard] Enabled={_config.Enabled}, " +
                $"PerSenderMaxPacketsPerSec={_config.PerSenderMaxPacketsPerSec}, " +
                $"BlackholeMs={_config.BlackholeMs}, " +
                $"AllowedPlayers={_config.AllowedPlayers.Count}.");
        }

        /// <summary>
        /// FloodGuard operates at packet level and does not consume decoded host/world messages.
        /// </summary>
        /// <remarks>
        /// Returning false keeps normal decoded message flow unchanged.
        /// Packet filtering is handled by <see cref="BeforeInboundPacket"/>.
        /// </remarks>
        public bool BeforeHostMessage(HostMessageContext context)
        {
            return false;
        }
        #endregion

        #region Packet Guard

        /// <summary>
        /// Consumes packets from a sender while they are over the configured packet-rate limit.
        /// </summary>
        /// <remarks>
        /// Return values:
        /// - true  = consume/drop this inbound packet.
        /// - false = allow the packet to continue through normal server handling.
        ///
        /// Packet flow:
        /// 1) Ignore disabled/null/host packets.
        /// 2) Resolve the sender name.
        /// 3) Skip allow-listed senders.
        /// 4) Track packet counts in a rolling one-second window.
        /// 5) Blackhole senders that exceed the configured limit.
        /// </remarks>
        public bool BeforeInboundPacket(ServerInboundPacketContext context)
        {
            if (context == null || !_config.Enabled)
                return false;

            // Sender 0 is the authoritative host/server in CastleMiner Z's GID model.
            if (context.SenderId == 0)
                return false;

            string senderName = ResolveSenderName(context);

            if (IsAllowed(context.SenderId, senderName, context.RemoteId))
                return false;

            DateTime now = context.UtcNow == default ? DateTime.UtcNow : context.UtcNow;

            lock (_senderStates)
            {
                if (!_senderStates.TryGetValue(context.SenderId, out SenderFloodState state))
                {
                    state = new SenderFloodState
                    {
                        WindowStartUtc = now
                    };

                    _senderStates[context.SenderId] = state;
                }

                // If the sender is currently blackholed, consume the packet immediately.
                if (state.BlackholeUntilUtc > now)
                {
                    state.DroppedWhileBlackholed++;
                    return true;
                }

                // If the blackhole period expired, reset the sender's temporary punishment state.
                if (state.BlackholeUntilUtc != DateTime.MinValue && state.BlackholeUntilUtc <= now)
                {
                    if (state.DroppedWhileBlackholed > 0)
                    {
                        _log(
                            $"[FloodGuard] Released {senderName} (id={context.SenderId}); " +
                            $"dropped {state.DroppedWhileBlackholed} packets while blackholed.");
                    }

                    state.BlackholeUntilUtc = DateTime.MinValue;
                    state.DroppedWhileBlackholed = 0;
                    state.WindowStartUtc = now;
                    state.PacketCount = 0;
                }

                // Start a new one-second packet-count window.
                if ((now - state.WindowStartUtc).TotalSeconds >= 1.0)
                {
                    state.WindowStartUtc = now;
                    state.PacketCount = 0;
                }

                state.PacketCount++;

                // Sender is still within the configured packet limit.
                if (state.PacketCount <= _config.PerSenderMaxPacketsPerSec)
                    return false;

                // Sender exceeded the packet limit; start the blackhole period.
                state.BlackholeUntilUtc = now.AddMilliseconds(_config.BlackholeMs);
                state.DroppedWhileBlackholed = 0;

                _log(
                    $"[FloodGuard] Blackholing {senderName} (id={context.SenderId}) for {_config.BlackholeMs}ms; " +
                    $"rate={state.PacketCount}/{_config.PerSenderMaxPacketsPerSec} packets in current 1s window; " +
                    $"{context.DescribePacket()}.");

                return true;
            }
        }
        #endregion

        #region Player Events

        /// <summary>
        /// Tracks player names so AllowedPlayers can match by visible gamertag.
        /// </summary>
        /// <remarks>
        /// If no player name is available, this falls back to the stable Player{id} format.
        /// </remarks>
        public void OnPlayerJoined(ServerPlayerEventContext context)
        {
            if (context == null)
                return;

            lock (_playerNames)
            {
                _playerNames[context.PlayerId] = string.IsNullOrWhiteSpace(context.PlayerName)
                    ? "Player" + context.PlayerId
                    : context.PlayerName;
            }
        }

        /// <summary>
        /// Clears cached sender state when a player leaves.
        /// </summary>
        /// <remarks>
        /// This prevents old packet windows or blackhole timers from being reused if the same id is reused later.
        /// </remarks>
        public void OnPlayerLeft(ServerPlayerEventContext context)
        {
            if (context == null)
                return;

            lock (_playerNames)
            {
                _playerNames.Remove(context.PlayerId);
            }

            lock (_senderStates)
            {
                _senderStates.Remove(context.PlayerId);
            }
        }
        #endregion

        #region Config

        #region Config File

        /// <summary>
        /// Writes the default FloodGuard config if it does not already exist.
        /// </summary>
        /// <remarks>
        /// Existing configs are never overwritten here.
        /// </remarks>
        private void EnsureDefaultConfig()
        {
            if (File.Exists(_configPath))
                return;

            string text =
@"[General]
Enabled = true
PerSenderMaxPacketsPerSec = 120
BlackholeMs = 3000

[AllowedPlayers]
# Comma-separated allow list. Entries may be player names, Player1-style fallback names,
# numeric GIDs, or SteamIDs on the Steam server.
AllowedPlayers =
";

            File.WriteAllText(_configPath, text);
        }

        /// <summary>
        /// Loads FloodGuard.Config.ini.
        /// </summary>
        /// <remarks>
        /// Values are clamped to safe ranges so invalid or extreme config values cannot break packet handling.
        /// </remarks>
        private void LoadConfig()
        {
            Dictionary<string, string> values = ReadIni(_configPath);

            _config.Enabled = GetBool(values, "General.Enabled", true);
            _config.PerSenderMaxPacketsPerSec = Clamp(GetInt(values, "General.PerSenderMaxPacketsPerSec", 120), 1, 10000);
            _config.BlackholeMs = Clamp(GetInt(values, "General.BlackholeMs", 3000), 0, 600000);

            _config.AllowedPlayers.Clear();

            string allowedPlayers = GetString(values, "AllowedPlayers.AllowedPlayers", string.Empty);
            foreach (string part in SplitList(allowedPlayers))
            {
                string normalized = NormalizeAllowedValue(part);
                if (!string.IsNullOrWhiteSpace(normalized))
                    _config.AllowedPlayers.Add(normalized);
            }
        }
        #endregion

        #region INI Parsing

        /// <summary>
        /// Reads a small INI file into section.key values.
        /// </summary>
        /// <remarks>
        /// Example:
        /// [General]
        /// Enabled = true
        ///
        /// Becomes:
        /// General.Enabled = true
        /// </remarks>
        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;

            if (!File.Exists(path))
                return values;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = (raw ?? string.Empty).Trim();

                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                string fullKey = string.IsNullOrWhiteSpace(section) ? key : section + "." + key;
                values[fullKey] = value;
            }

            return values;
        }
        #endregion

        #region Config Value Helpers

        /// <summary>
        /// Gets a boolean config value or returns the fallback when missing/invalid.
        /// </summary>
        private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out string raw) && bool.TryParse(raw, out bool value)
                ? value
                : fallback;
        }

        /// <summary>
        /// Gets an integer config value or returns the fallback when missing/invalid.
        /// </summary>
        private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out string raw) && int.TryParse(raw, out int value)
                ? value
                : fallback;
        }

        /// <summary>
        /// Gets a string config value or returns the fallback when missing/null.
        /// </summary>
        private static string GetString(Dictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out string raw) && raw != null
                ? raw
                : fallback;
        }

        /// <summary>
        /// Clamps a value between the provided minimum and maximum.
        /// </summary>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
        #endregion

        #endregion

        #region Helpers

        #region Sender Helpers

        /// <summary>
        /// Resolves the best known display name for a packet sender.
        /// </summary>
        /// <remarks>
        /// Resolution order:
        /// 1) Packet context sender name.
        /// 2) Cached player join name.
        /// 3) Player{id} fallback.
        /// </remarks>
        private string ResolveSenderName(ServerInboundPacketContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.SenderName))
                return context.SenderName;

            lock (_playerNames)
            {
                if (_playerNames.TryGetValue(context.SenderId, out string cachedName) &&
                    !string.IsNullOrWhiteSpace(cachedName))
                {
                    return cachedName;
                }
            }

            return "Player" + context.SenderId;
        }
        #endregion

        #region Allow List Helpers

        /// <summary>
        /// Returns true when the sender is on the FloodGuard allow list.
        /// </summary>
        /// <remarks>
        /// Supported allow-list values:
        /// - Player display name.
        /// - Player{id} fallback name.
        /// - Numeric sender/player id.
        /// - Remote id / SteamID when available.
        /// </remarks>
        private bool IsAllowed(byte senderId, string senderName, ulong remoteId)
        {
            if (_config.AllowedPlayers.Count == 0)
                return false;

            if (_config.AllowedPlayers.Contains(NormalizeAllowedValue(senderName)))
                return true;

            if (_config.AllowedPlayers.Contains(NormalizeAllowedValue("Player" + senderId)))
                return true;

            if (_config.AllowedPlayers.Contains(NormalizeAllowedValue(senderId.ToString())))
                return true;

            return remoteId != 0UL && _config.AllowedPlayers.Contains(NormalizeAllowedValue(remoteId.ToString()));
        }

        /// <summary>
        /// Splits a comma/semicolon-separated config list into trimmed values.
        /// </summary>
        private static IEnumerable<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;

            char[] separators = [',', ';'];
            string[] parts = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
                yield return part.Trim();
        }

        /// <summary>
        /// Normalizes allow-list values for stable case-insensitive matching.
        /// </summary>
        private static string NormalizeAllowedValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
        #endregion

        #endregion

        #region Nested Types

        /// <summary>
        /// In-memory FloodGuard configuration.
        /// </summary>
        private sealed class FloodGuardConfig
        {
            /// <summary>
            /// Enables or disables packet flood protection.
            /// </summary>
            public bool Enabled = true;

            /// <summary>
            /// Maximum packets a single sender can send within one second before being blackholed.
            /// </summary>
            public int PerSenderMaxPacketsPerSec = 120;

            /// <summary>
            /// Duration, in milliseconds, to drop packets from a sender after they exceed the packet limit.
            /// </summary>
            public int BlackholeMs = 3000;

            /// <summary>
            /// Normalized allow-list entries that bypass FloodGuard checks.
            /// </summary>
            public readonly HashSet<string> AllowedPlayers = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Runtime packet-rate state for a single sender.
        /// </summary>
        private sealed class SenderFloodState
        {
            /// <summary>
            /// Start time of the current one-second packet-count window.
            /// </summary>
            public DateTime WindowStartUtc;

            /// <summary>
            /// Number of packets seen during the current one-second window.
            /// </summary>
            public int PacketCount;

            /// <summary>
            /// UTC time until which packets from this sender should be dropped.
            /// </summary>
            public DateTime BlackholeUntilUtc;

            /// <summary>
            /// Number of packets dropped during the current blackhole period.
            /// </summary>
            public int DroppedWhileBlackholed;
        }
        #endregion
    }
}