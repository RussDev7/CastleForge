/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using HarmonyLib;
using DNA.Net;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace RegionProtect
{
    /// <summary>
    /// Runtime state + enforcement helpers for RegionProtect.
    ///
    /// Responsibilities:
    /// - Holds local admin selection corners used by /regionpos + /regioncreate.
    /// - Maintains lock-free snapshots of config + region data (pushed from RegionProtectStore).
    /// - Evaluates whether a given player action (mine/place) should be denied at a block position.
    /// - Handles deny side-effects (private message, logging, and block correction).
    /// - Provides a small unicast chat helper for sending private deny notifications.
    /// </summary>
    internal static class RegionProtectCore
    {
        #region Selection (Local Admin)

        /// <summary>
        /// True if selection corner #1 has been set via /regionpos 1.
        /// </summary>
        internal static bool HasSelectionPos1;

        /// <summary>
        /// True if selection corner #2 has been set via /regionpos 2.
        /// </summary>
        internal static bool HasSelectionPos2;

        /// <summary>
        /// Selection corner #1 in block coordinates.
        /// </summary>
        internal static IntVector3 SelectionPos1;

        /// <summary>
        /// Selection corner #2 in block coordinates.
        /// </summary>
        internal static IntVector3 SelectionPos2;

        #endregion

        #region Config Snapshot

        /// <summary>
        /// Current config snapshot (volatile so readers can access without locking).
        /// Updated via <see cref="ApplyConfig"/>.
        /// </summary>
        private static volatile RPConfig _cfg = new RPConfig();

        /// <summary>
        /// Updates the active config snapshot used by enforcement logic.
        /// </summary>
        internal static void ApplyConfig(RPConfig cfg)
        {
            _cfg = cfg ?? new RPConfig();
        }
        #endregion

        #region Region Snapshot (Lock-Free Reads)

        /// <summary>
        /// Current region list snapshot (volatile so readers can access without locking).
        /// Updated via <see cref="ApplyStoreSnapshot"/>.
        /// </summary>
        private static volatile List<RegionProtectStore.RegionDef> _regions = new List<RegionProtectStore.RegionDef>();

        /// <summary>
        /// Current spawn-protection snapshot (volatile so readers can access without locking).
        /// Updated via <see cref="ApplyStoreSnapshot"/>.
        /// </summary>
        private static volatile RegionProtectStore.SpawnProtectionDef _spawn = new RegionProtectStore.SpawnProtectionDef();

        /// <summary>
        /// Pushes new snapshots from RegionProtectStore into the runtime (lock-free reads thereafter).
        /// </summary>
        internal static void ApplyStoreSnapshot(List<RegionProtectStore.RegionDef> regions, RegionProtectStore.SpawnProtectionDef spawn)
        {
            _regions = regions ?? new List<RegionProtectStore.RegionDef>();
            _spawn = spawn ?? new RegionProtectStore.SpawnProtectionDef();
        }
        #endregion

        #region Deny-Notify Throttle

        /// <summary>
        /// Throttle map for private deny notifications to avoid spamming chat.
        /// Keyed by gamertag (case-insensitive), value is last notify time in Environment.TickCount ms.
        /// </summary>
        private static readonly object _throttleLock = new object();

        /// <summary>
        /// Stores the last time (in ms, using Environment.TickCount) that a player was notified.
        /// </summary>
        private static readonly Dictionary<string, long> _lastNotifyMs =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true if we are allowed to send a deny notification to <paramref name="player"/> right now.
        /// Uses RPConfig.DenyNotifyCooldownMs to rate-limit messages per player.
        /// </summary>
        private static bool CanNotifyNow(string player)
        {
            if (string.IsNullOrWhiteSpace(player)) return false;

            long now = Environment.TickCount;
            int cooldown = _cfg?.DenyNotifyCooldownMs ?? 0;

            lock (_throttleLock)
            {
                if (_lastNotifyMs.TryGetValue(player, out var last))
                {
                    long dt = now - last;
                    if (dt >= 0 && dt < cooldown) return false;
                }
                _lastNotifyMs[player] = now;
            }
            return true;
        }
        #endregion

        #region Public Helpers

        /// <summary>
        /// Normalizes player names/gamertags for consistent comparisons.
        /// Current behavior: Trim + UpperInvariant.
        /// </summary>
        internal static string NormalizePlayer(string name)
            => (name ?? "").Trim().ToUpperInvariant();

        /// <summary>
        /// Parses a comma-separated list of player names into a case-insensitive hash set.
        /// Empty/whitespace input returns an empty set.
        /// </summary>
        internal static HashSet<string> ParsePlayerCsv(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(csv))
                return set;

            // Comma-separated, allow spaces.
            foreach (var raw in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = NormalizePlayer(raw);
                if (p.Length > 0) set.Add(p);
            }

            return set;
        }

        /// <summary>
        /// Formats a player set for display in chat/log output.
        /// Returns "(none)" when empty.
        /// </summary>
        internal static string FormatPlayers(HashSet<string> set)
        {
            if (set == null || set.Count == 0) return "(none)";
            return string.Join(",", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the component-wise minimum of two block positions.
        /// Used to normalize region AABB bounds.
        /// </summary>
        internal static IntVector3 Min(IntVector3 a, IntVector3 b)
            => new IntVector3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

        /// <summary>
        /// Returns the component-wise maximum of two block positions.
        /// Used to normalize region AABB bounds.
        /// </summary>
        internal static IntVector3 Max(IntVector3 a, IntVector3 b)
            => new IntVector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        /// <summary>
        /// Converts a world position to block coordinates by flooring each component.
        /// </summary>
        internal static IntVector3 ToBlockPos(Vector3 pos)
        {
            // CMZ blocks are 1 unit per block; use floor.
            return new IntVector3(
                (int)Math.Floor(pos.X),
                (int)Math.Floor(pos.Y),
                (int)Math.Floor(pos.Z));
        }

        /// <summary>
        /// Returns true when the game is actively in a session (i.e., not at main menu/loading).
        /// Useful as a guard before running commands or per-tick world checks.
        /// </summary>
        public static bool IsInGame()
        {
            var g = CastleMinerZGame.Instance;
            return g != null && g?.GameScreen != null && g?.CurrentNetworkSession != null;
        }
        #endregion

        #region Enforcement

        /// <summary>
        /// What the player is trying to do at a protected location.
        /// Replaces the old bool isDig (mine vs build) so we can include crates.
        /// </summary>
        internal enum ProtectAction
        {
            Mine,       // Digging blocks, explosions (block -> Empty).
            Build,      // Placing/replacing blocks.
            UseCrate,   // Taking/placing items in a crate (ItemCrateMessage).
            BreakCrate, // Destroying/removing the crate object (DestroyCrateMessage).
        }

        /// <summary>
        /// High-level deny reason categories used for messaging/logging.
        /// </summary>
        internal enum DenyReasonKind
        {
            None,
            Spawn,
            Region,
        }

        /// <summary>
        /// Detailed deny reason payload (category + optional region name).
        /// </summary>
        internal struct DenyReason
        {
            public DenyReasonKind Kind;
            public string RegionName;
        }

        /// <summary>
        /// Central permission check. Decides whether a given action at a position should be denied
        /// based on spawn protection and region whitelists.
        /// </summary>
        internal static bool ShouldDeny(string gamerTag, IntVector3 blockPos, ProtectAction action, out DenyReason reason)
        {
            reason = default;

            var cfg = _cfg;
            if (cfg == null || !cfg.Enabled) return false;

            // Action -> feature toggle gates.
            // If the relevant feature is disabled, we do NOT deny.
            switch (action)
            {
                case ProtectAction.Mine:
                    if (!cfg.ProtectMining) return false;
                    break;

                case ProtectAction.Build:
                    if (!cfg.ProtectPlacing) return false;
                    break;

                case ProtectAction.UseCrate:
                    if (!cfg.ProtectCrateItems) return false;
                    break;

                case ProtectAction.BreakCrate:
                    if (!cfg.ProtectCrateMining) return false;
                    break;

                default:
                    return false;
            }

            string who = NormalizePlayer(gamerTag);

            // Spawn protection (unchanged logic).
            var spawn = _spawn;
            if (spawn != null && spawn.Enabled && spawn.Range > 0)
            {
                int dx = blockPos.X;
                int dz = blockPos.Z;

                int r = spawn.Range;
                long dsq = (long)dx * dx + (long)dz * dz;
                long rsq = (long)r * r;

                if (dsq <= rsq && !spawn.AllowedPlayers.Contains(who))
                {
                    reason.Kind = DenyReasonKind.Spawn;
                    return true;
                }
            }

            // Region checks (unchanged logic).
            var regions = _regions;
            if (regions != null)
            {
                foreach (var r in regions)
                {
                    if (r == null) continue;
                    if (!r.Contains(blockPos)) continue;

                    if (!r.IsPlayerAllowed(who))
                    {
                        reason.Kind = DenyReasonKind.Region;
                        reason.RegionName = r.Name;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Handles the side-effects of a denied AlterBlockMessage action:
        /// 1) Optional private chat notification to the offender (rate-limited).
        /// 2) Optional logging to console.
        /// 3) Broadcasts an AlterBlockMessage correction to restore the old block on clients.
        /// </summary>
        internal static void HandleDenied(CastleMinerZGame game, Message message, AlterBlockMessage abm, BlockTypeEnum oldBlock, DenyReason reason, ProtectAction action)
        {
            if (game == null || message == null || abm == null) return;

            var cfg = _cfg;
            string offender = message.Sender?.Gamertag ?? "?";

            // 1) Notify offender (private).
            if (cfg != null && cfg.NotifyDeniedPlayer && CanNotifyNow(offender))
            {
                string txt = BuildDeniedMessage(offender, reason, action);
                TrySendPrivateChat(game, message.Sender, txt);
            }

            // 2) Log.
            if (cfg != null && cfg.LogDenied)
            {
                string act =
                    (action == ProtectAction.Mine)       ? "mine"         :
                    (action == ProtectAction.Build)      ? "build"        :
                    (action == ProtectAction.UseCrate)   ? "use crates"   :
                    (action == ProtectAction.BreakCrate) ? "break crates" :
                                                           "do that";

                Log($"DENY {offender} {act} at {abm.BlockLocation} (old={oldBlock}, new={abm.BlockType}) reason={reason.Kind} {reason.RegionName}.");
            }

            // 3) Broadcast correction (restore old block) so clients revert any prediction.
            // Note: This is safe even if the host never applied the change, since SetBlock will no-op.
            try
            {
                if (game.MyNetworkGamer != null)
                    AlterBlockMessage.Send(game.MyNetworkGamer, abm.BlockLocation, oldBlock);
            }
            catch (Exception ex)
            {
                Log($"Failed to send correction AlterBlockMessage: {ex.Message}.");
            }
        }

        /// <summary>
        /// Convenience wrapper for sending a private deny message outside the AlterBlockMessage path
        /// (e.g., explosion filtering), using the same throttling and message formatting rules.
        /// </summary>
        internal static void NotifyDenied(CastleMinerZGame game, NetworkGamer to,
                                  DenyReason reason, ProtectAction action)
        {
            var cfg = _cfg;
            if (cfg == null || !cfg.NotifyDeniedPlayer) return;

            string offender = to?.Gamertag ?? "";
            if (!CanNotifyNow(offender)) return;

            TrySendPrivateChat(game, to, BuildDeniedMessage(offender, reason, action));
        }

        /// <summary>
        /// Builds a human-readable deny message for chat display.
        /// </summary>
        private static string BuildDeniedMessage(string offender, DenyReason reason, ProtectAction action)
        {
            _ = offender;

            string act =
                (action == ProtectAction.Mine)       ? "mine"         :
                (action == ProtectAction.Build)      ? "build"        :
                (action == ProtectAction.UseCrate)   ? "use crates"   :
                (action == ProtectAction.BreakCrate) ? "break crates" :
                                                       "do that";

            switch (reason.Kind)
            {
                case DenyReasonKind.Spawn:
                    return $"Spawn is protected. You are not allowed to {act} here.";
                case DenyReasonKind.Region:
                    return $"Region '{reason.RegionName}' is protected. You are not allowed to {act} here.";
                default:
                    return $"This area is protected. You are not allowed to {act} here.";
            }
        }
        #endregion

        #region Private Chat (Unicast)

        /// <summary>
        /// Cached reflection handle for Message.DoSend(LocalNetworkGamer, NetworkGamer),
        /// used to unicast chat packets to a single recipient.
        /// </summary>
        // Create typed accessors once.
        private static readonly MethodInfo DoSendDirect = AccessTools.Method(typeof(Message), "DoSend",
            new[] { typeof(LocalNetworkGamer), typeof(NetworkGamer) });

        /// <summary>
        /// Typed bridge for creating send instances (Message.GetSendInstance<T>()).
        /// </summary>
        // Send private messages to DNA.CastleMinerZ.Net calls.
        public abstract class MessageBridge : Message
        {
            public static T Get<T>() where T : Message => GetSendInstance<T>();
        }

        /// <summary>
        /// Sends a single-recipient (unicast) BroadcastTextMessage to <paramref name="to"/>.
        /// Fails silently to avoid crashing the game if reflection/send fails.
        /// </summary>
        private static void TrySendPrivateChat(CastleMinerZGame game, NetworkGamer to, string message)
        {
            try
            {
                if (game == null || game.MyNetworkGamer == null || to == null) return;
                if (string.IsNullOrWhiteSpace(message)) return;

                if (DoSendDirect == null)
                {
                    // Fallback: If DoSendDirect isn't found, do nothing (better than crashing).
                    return;
                }

                // Define the send instance message type.
                var sendInstance     = MessageBridge.Get<BroadcastTextMessage>();
                sendInstance.Message = message; // Packet fields.

                // Send only to the targeted player.
                DoSendDirect.Invoke(sendInstance, new object[] { game.MyNetworkGamer, to });
            }
            catch
            {
                // Silent: Do not crash gameplay.
            }
        }
        #endregion
    }
}