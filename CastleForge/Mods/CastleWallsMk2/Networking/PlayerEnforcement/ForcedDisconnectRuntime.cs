/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Net.Steam;
using System.Collections.Generic;
using DNA.Distribution.Steam;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Net.Lidgren;
using HarmonyLib;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Host-side forced remote disconnect helper for Steam sessions.
    ///
    /// Summary:
    /// - Provides SteamID-based runtime blocking for hard-ban enforcement.
    /// - Provides off-host Gamertag-based runtime blocking for local/private enforcement.
    /// - Provides helpers for private KickMessage sends in non-host scenarios.
    /// - Applies/removes vanilla in-memory BanList entries when needed.
    /// - Performs immediate host-side Steam disconnect and peer removal when the host enforces a drop.
    ///
    /// Behavior:
    /// - Hard Kick : Disconnect now + remove now, but do NOT keep the SteamID session-blocked.
    /// - Hard Ban  : Disconnect now + remove now, and keep the SteamID runtime-blocked
    ///               while also adding it to vanilla BanList.
    ///
    /// Notes:
    /// - This version intentionally restores the immediate host-side RemoveGamer behavior.
    /// - This may reintroduce the earlier risks you were already aware of.
    /// - Off-host "private kick" helpers are local/client-side convenience tools and do not replace
    ///   real host authority.
    /// </summary>
    internal static class ForcedDisconnectRuntime
    {
        #region Reflection Handles

        /// <summary>
        /// Cached reflection handle for NetworkSession._sessionProvider.
        /// </summary>
        private static readonly FieldInfo FI_NetworkSession_SessionProvider =
            AccessTools.Field(typeof(NetworkSession), "_sessionProvider");

        /// <summary>
        /// Cached reflection handle for SteamNetworkSessionProvider._steamAPI.
        /// </summary>
        private static readonly FieldInfo FI_SteamProvider_SteamApi =
            AccessTools.Field(typeof(SteamNetworkSessionProvider), "_steamAPI");

        /// <summary>
        /// Cached reflection handle for SteamNetworkSessionProvider._steamIDToGamer.
        /// </summary>
        private static readonly FieldInfo FI_SteamProvider_SteamIdToGamer =
            AccessTools.Field(typeof(SteamNetworkSessionProvider), "_steamIDToGamer");

        /// <summary>
        /// Cached reflection handle for NetworkSessionProvider.RemoveGamer(...).
        /// </summary>
        private static readonly MethodInfo MI_NetworkSessionProvider_RemoveGamer =
            AccessTools.Method(typeof(NetworkSessionProvider), "RemoveGamer");

        #endregion

        #region Shared Synchronization

        /// <summary>
        /// Shared synchronization object for runtime enforcement collections.
        /// </summary>
        private static readonly object _sync = new object();

        #endregion

        #region Runtime Blocked SteamIDs

        /// <summary>
        /// SteamIDs currently blocked for the active runtime/session.
        ///
        /// Notes:
        /// - Primarily used for host-side hard-ban enforcement.
        /// </summary>
        private static readonly HashSet<ulong> _blockedSteamIds = new HashSet<ulong>();

        /// <summary>
        /// Returns true when the given SteamID is currently runtime-blocked.
        /// </summary>
        internal static bool IsBlockedSteamId(ulong steamId)
        {
            if (steamId == 0UL)
                return false;

            lock (_sync)
                return _blockedSteamIds.Contains(steamId);
        }

        /// <summary>
        /// Adds the given SteamID to the runtime block set.
        /// </summary>
        internal static void BlockSteamId(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            lock (_sync)
                _blockedSteamIds.Add(steamId);
        }

        /// <summary>
        /// Removes the given SteamID from the runtime block set.
        /// </summary>
        internal static void UnblockSteamId(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            lock (_sync)
                _blockedSteamIds.Remove(steamId);
        }

        /// <summary>
        /// Clears all runtime SteamID block entries.
        /// </summary>
        internal static void ClearAllRuntimeBlocks()
        {
            lock (_sync)
                _blockedSteamIds.Clear();
        }

        /// <summary>
        /// Adds a SteamID to the runtime block set for local enforcement use.
        ///
        /// Notes:
        /// - Functionally equivalent to BlockSteamId(...), but kept as a separate helper for intent clarity.
        /// </summary>
        internal static void BlockSteamIdForLocalEnforcement(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            lock (_sync)
                _blockedSteamIds.Add(steamId);
        }
        #endregion

        #region Off-Host Gamertag Blocking Helpers

        /// <summary>
        /// Gamertags currently blocked for off-host local/private enforcement.
        /// </summary>
        private static readonly HashSet<string> _blockedGamertags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Normalizes a Gamertag for local comparison.
        ///
        /// Summary:
        /// - Converts null to empty string.
        /// - Trims surrounding whitespace.
        /// </summary>
        internal static string NormalizeGamertag(string gamertag)
        {
            return (gamertag ?? "").Trim();
        }

        /// <summary>
        /// Returns true when the given Gamertag is currently runtime-blocked.
        /// </summary>
        internal static bool IsBlockedGamertag(string gamertag)
        {
            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            lock (_sync)
                return _blockedGamertags.Contains(key);
        }

        /// <summary>
        /// Adds a Gamertag to the runtime block set.
        /// </summary>
        internal static void BlockGamertag(string gamertag)
        {
            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_sync)
                _blockedGamertags.Add(key);
        }

        /// <summary>
        /// Removes a Gamertag from the runtime block set.
        /// </summary>
        internal static void UnblockGamertag(string gamertag)
        {
            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (_sync)
                _blockedGamertags.Remove(key);
        }
        #endregion

        #region Private Kick Helper

        /// <summary>
        /// Per-Gamertag cooldown tracking for private kick attempts.
        ///
        /// Notes:
        /// - Prevents spammy repeated direct sends for the same target in a short time window.
        /// </summary>
        private static readonly Dictionary<string, DateTime> _lastPrivateKickUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Attempts to privately kick or ban a single remote gamer using a direct KickMessage send.
        ///
        /// Summary:
        /// - Applies a short cooldown per normalized Gamertag.
        /// - Silently skips invalid/local targets.
        ///
        /// Notes:
        /// - This is primarily intended for non-host local/private enforcement flows.
        /// </summary>
        internal static void TryPrivateKick(LocalNetworkGamer from, NetworkGamer to, bool banned)
        {
            if (from == null || to == null || to.IsLocal)
                return;

            string key = NormalizeGamertag(to.Gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return;

            bool allow = false;
            DateTime now = DateTime.UtcNow;

            lock (_sync)
            {
                if (!_lastPrivateKickUtc.TryGetValue(key, out DateTime last) ||
                    (now - last).TotalSeconds >= 2.0)
                {
                    _lastPrivateKickUtc[key] = now;
                    allow = true;
                }
            }

            if (!allow)
                return;

            try
            {
                KickPlayerPrivate(from, to, banned);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Sends a direct KickMessage to a specific remote gamer.
        ///
        /// Summary:
        /// - Builds a KickMessage send-instance.
        /// - Sets PlayerID/Banned.
        /// - Invokes the direct-send bridge.
        ///
        /// Notes:
        /// - This is a direct targeted send, not a broadcast.
        /// </summary>
        public static void KickPlayerPrivate(LocalNetworkGamer from, NetworkGamer to, bool banned = false)
        {
            // Define the send instance message type.
            var sendInstance = MessageBridge.Get<KickMessage>();
            sendInstance.PlayerID = to.Id;
            sendInstance.Banned = banned;
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #region Vanilla BanList Helpers

        /// <summary>
        /// Adds a SteamID to the vanilla in-memory BanList.
        ///
        /// Notes:
        /// - Existing entries are removed first so the timestamp can be refreshed.
        /// </summary>
        internal static void AddVanillaBan(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            try
            {
                CastleMinerZGame.Instance?.PlayerStats?.BanList?.Remove(steamId);
                CastleMinerZGame.Instance?.PlayerStats?.BanList?.Add(steamId, DateTime.UtcNow);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Removes a SteamID from the vanilla in-memory BanList.
        /// </summary>
        internal static void RemoveVanillaBan(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            try
            {
                CastleMinerZGame.Instance?.PlayerStats?.BanList?.Remove(steamId);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Applies both runtime and vanilla in-memory ban state for a SteamID.
        /// </summary>
        internal static void ApplyBanState(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            BlockSteamId(steamId);
            AddVanillaBan(steamId);
        }

        /// <summary>
        /// Clears both runtime and vanilla in-memory ban state for a SteamID.
        /// </summary>
        internal static void ClearBanState(ulong steamId)
        {
            if (steamId == 0UL)
                return;

            UnblockSteamId(steamId);
            RemoveVanillaBan(steamId);
        }
        #endregion

        #region Main Entry

        /// <summary>
        /// Forces a remote gamer out of the current host session.
        ///
        /// Summary:
        /// - Validates host/session/provider state.
        /// - Applies hard-ban runtime + vanilla ban state when requested.
        /// - Sends a standard KickMessage to the target.
        /// - Routes Steam sessions into the Steam-specific disconnect path.
        ///
        /// Returns:
        /// - true  = a disconnect path was executed
        /// - false = invalid target/session/provider or unsupported path
        /// </summary>
        internal static bool ForceDisconnectRemote(NetworkGamer target, bool ban)
        {
            if (target == null || target.IsLocal)
                return false;

            NetworkSession session = target.Session ?? NetworkSession.CurrentNetworkSession;
            if (session == null || !session.IsHost)
                return false;

            if (!(CastleMinerZGame.Instance?.MyNetworkGamer is LocalNetworkGamer hostLocal) || !hostLocal.IsHost)
                return false;

            if (!(FI_NetworkSession_SessionProvider?.GetValue(session) is NetworkSessionProvider provider))
                return false;

            ulong steamId = target.AlternateAddress;

            // Only hard bans should remain blocked / banned in-memory.
            if (ban && steamId != 0UL)
            {
                ApplyBanState(steamId);
            }

            try
            {
                KickMessage.Send(hostLocal, target, ban);
            }
            catch
            {
            }

            if (provider is SteamNetworkSessionProvider steamProvider)
                return ForceDisconnectRemoteSteam(steamProvider, target, ban);

            return false;
        }
        #endregion

        #region Steam Path

        /// <summary>
        /// Performs the Steam-specific disconnect/removal path.
        ///
        /// Summary:
        /// - Optionally keeps the SteamID runtime-blocked for hard bans.
        /// - Sends the Steam-side deny message.
        /// - Broadcasts DropPeer to other clients.
        /// - Removes the gamer from Steam/provider maps and host-side collections.
        ///
        /// Notes:
        /// - This is the immediate-removal variant.
        /// </summary>
        private static bool ForceDisconnectRemoteSteam(
            SteamNetworkSessionProvider steamProvider,
            NetworkGamer target,
            bool ban)
        {
            if (steamProvider == null || target == null)
                return false;

            ulong steamId = target.AlternateAddress;
            byte gamerId  = target.Id;

            if (steamId == 0UL)
                return false;

            if (!(FI_SteamProvider_SteamApi?.GetValue(steamProvider) is SteamWorks steamApi))
                return false;

            // Only hard bans remain runtime-blocked after the disconnect.
            if (ban)
                BlockSteamId(steamId);

            // Tell the client "host kicked us" on the Steam internal path.
            try
            {
                steamApi.Deny(steamId, "Host Kicked Us");
            }
            catch
            {
            }

            // Tell every other peer to remove this gamer immediately.
            try
            {
                BroadcastDropPeerToOthers(steamProvider, steamApi, gamerId, steamId);
            }
            catch
            {
            }

            // Remove from the provider's steamId -> gamer map immediately.
            try
            {
                var map = FI_SteamProvider_SteamIdToGamer?.GetValue(steamProvider) as Dictionary<ulong, Gamer>;
                map?.Remove(steamId);
            }
            catch
            {
            }

            // Remove from provider collections immediately on the host.
            try
            {
                MI_NetworkSessionProvider_RemoveGamer?.Invoke(steamProvider, new object[] { target });
            }
            catch
            {
            }

            return true;
        }

        /// <summary>
        /// Sends the internal DropPeer message (channel 1) to every remote except the target.
        ///
        /// Summary:
        /// - Mirrors the provider's peer-drop behavior.
        /// - Causes other clients to remove the dropped player from their local session lists.
        ///
        /// Notes:
        /// - Skips null entries, invalid alternate addresses, and the dropped peer itself.
        /// </summary>
        private static void BroadcastDropPeerToOthers(
            SteamNetworkSessionProvider steamProvider,
            SteamWorks steamApi,
            byte droppedGamerId,
            ulong droppedSteamId)
        {
            if (steamProvider == null || steamApi == null)
                return;

            DropPeerMessage drop = new DropPeerMessage
            {
                PlayerGID = droppedGamerId
            };

            GamerCollection<NetworkGamer> remotes = steamProvider.RemoteGamers;
            for (int i = 0; i < remotes.Count; i++)
            {
                NetworkGamer ng = remotes[i];
                if (ng == null)
                    continue;

                if (ng.AlternateAddress == 0UL)
                    continue;

                if (ng.AlternateAddress == droppedSteamId)
                    continue;

                var msg = steamApi.AllocSteamNetBuffer();
                msg.Write((byte)InternalMessageTypes.DropPeer);
                msg.Write(drop);

                steamApi.SendPacket(msg, ng.AlternateAddress, NetDeliveryMethod.ReliableOrdered, 1);
            }
        }
        #endregion
    }
}