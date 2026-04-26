/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Common;
using CMZDedicatedSteamServer.Config;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace CMZDedicatedSteamServer.Hosting
{
    /// <summary>
    /// Handles host-side validation of RequestConnectToHostMessage for the Steam transport.
    ///
    /// Purpose:
    /// - Reproduces the same major validation gates used by SteamNetworkSessionProvider.
    /// - Separates "pending approval" from "fully connected" so a client can retry during
    ///   the handshake window without being permanently rejected as already connected.
    /// </summary>
    internal sealed class SteamConnectionApproval(SteamServerConfig config, Assembly commonAssembly, Action<string> log)
    {
        private SteamServerConfig _config = config;
        private readonly Assembly _commonAssembly = commonAssembly;
        private readonly Action<string> _log = log ?? (_ => { });
        private readonly Dictionary<ulong, PendingApprovalInfo> _pendingBySteamId = [];
        private readonly Dictionary<ulong, ConnectedApprovalInfo> _connectedBySteamId = [];
        private readonly Dictionary<string, ulong> _connectedGamertagToSteamId = new(StringComparer.OrdinalIgnoreCase);

        public ApprovalDecision ValidateRequest(object steamNetBuffer, ulong senderSteamId)
        {
            Type extensionsType = ReflectEx.GetRequiredType(_commonAssembly, "DNA.Net.GamerServices.LidgrenExtensions");
            MethodInfo readMethod = ReflectEx.GetRequiredMethod(extensionsType, "ReadRequestConnectToHostMessage", 3);

            object crm = readMethod.Invoke(null,
            [
                steamNetBuffer,
                _config.GameName,
                _config.NetworkVersion
            ]);

            object readResult = ReflectEx.GetRequiredMemberValue(crm, "ReadResult");
            string readResultName = readResult.ToString();

            switch (readResultName)
            {
                case "GameNameInvalid":
                    return ApprovalDecision.Deny("GameNamesDontMatch");
                case "VersionInvalid":
                case "LocalVersionIsLower":
                    return ApprovalDecision.Deny("ServerHasOlderVersion");
                case "LocalVersionIsHIgher":
                    return ApprovalDecision.Deny("ServerHasNewerVersion");
            }

            string password = (string)ReflectEx.GetRequiredMemberValue(crm, "Password") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_config.Password) && !string.Equals(password, _config.Password, StringComparison.Ordinal))
                return ApprovalDecision.Deny("IncorrectPassword");

            object sessionProperties = ReflectEx.GetRequiredMemberValue(crm, "SessionProperties");
            if (!ValidateSessionProperties(sessionProperties))
                return ApprovalDecision.Deny("SessionPropertiesDontMatch");

            object gamer = ReflectEx.GetRequiredMemberValue(crm, "Gamer");
            string gamertag = (string)ReflectEx.GetRequiredMemberValue(gamer, "Gamertag") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(gamertag))
                return ApprovalDecision.Deny("ConnectionDenied");

            if (_connectedBySteamId.ContainsKey(senderSteamId))
                return ApprovalDecision.Deny("GamerAlreadyConnected");

            if (_connectedGamertagToSteamId.ContainsKey(gamertag))
                return ApprovalDecision.Deny("GamerAlreadyConnected");

            if (_pendingBySteamId.TryGetValue(senderSteamId, out PendingApprovalInfo existingPending))
            {
                if (string.Equals(existingPending.Gamertag, gamertag, StringComparison.OrdinalIgnoreCase))
                {
                    _log($"[Approve] Re-accepted pending Steam join for '{gamertag}' ({senderSteamId}).");
                    return ApprovalDecision.Allow(existingPending.Gamertag, existingPending.Gamer);
                }

                return ApprovalDecision.Deny("GamerAlreadyConnected");
            }

            PendingApprovalInfo pending = new(senderSteamId, gamertag, gamer);
            _pendingBySteamId[senderSteamId] = pending;
            _log($"[Approve] Accepted pending Steam join for '{gamertag}' ({senderSteamId}).");
            return ApprovalDecision.Allow(gamertag, gamer);
        }

        /// <summary>
        /// Replaces the runtime config used for future Steam connection approval checks.
        /// 
        /// This is used by the console "reload" command after server.properties
        /// is re-read. Existing connected players and pending joins are left alone.
        /// </summary>
        public void ReloadConfig(SteamServerConfig config)
        {
            if (config == null)
                return;

            _config = config;
        }

        public bool TryGetPending(ulong steamId, out PendingApprovalInfo pending)
        {
            return _pendingBySteamId.TryGetValue(steamId, out pending);
        }

        public void MarkConnected(ulong steamId, string gamertag, object gamer)
        {
            _pendingBySteamId.Remove(steamId);
            _connectedBySteamId[steamId] = new ConnectedApprovalInfo(steamId, gamertag, gamer);
            _connectedGamertagToSteamId[gamertag] = steamId;
        }

        public void RemovePeer(ulong steamId)
        {
            _pendingBySteamId.Remove(steamId);

            if (_connectedBySteamId.TryGetValue(steamId, out ConnectedApprovalInfo connected))
            {
                _connectedBySteamId.Remove(steamId);
                _connectedGamertagToSteamId.Remove(connected.Gamertag);
            }
        }

        private bool ValidateSessionProperties(object incomingProps)
        {
            if (incomingProps == null)
                return false;

            int?[] expected = new int?[8];
            expected[0] = 4;
            expected[1] = 0;
            expected[2] = _config.GameMode;
            expected[3] = _config.Difficulty;
            expected[4] = _config.InfiniteResourceMode ? 1 : 0;
            expected[5] = _config.PvpState;

            PropertyInfo countProp = incomingProps.GetType().GetProperty("Count");
            if (countProp == null)
                return false;

            int count = Convert.ToInt32(countProp.GetValue(incomingProps, null));
            if (count != expected.Length)
                return false;

            PropertyInfo indexer = incomingProps.GetType().GetProperty("Item");
            if (indexer == null)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                object current = indexer.GetValue(incomingProps, [i]);
                int? actual = current == null ? (int?)null : Convert.ToInt32(current);
                if (expected[i] != null && expected[i] != actual)
                    return false;
            }

            return true;
        }
    }

    internal sealed class PendingApprovalInfo(ulong steamId, string gamertag, object gamer)
    {
        public ulong SteamId { get; } = steamId;
        public string Gamertag { get; } = gamertag;
        public object Gamer { get; } = gamer;
    }

    internal sealed class ConnectedApprovalInfo(ulong steamId, string gamertag, object gamer)
    {
        public ulong SteamId { get; } = steamId;
        public string Gamertag { get; } = gamertag;
        public object Gamer { get; } = gamer;
    }

    internal sealed class ApprovalDecision
    {
        private ApprovalDecision(bool allowed, string resultCode, string gamertag, object gamer)
        {
            Allowed = allowed;
            ResultCode = resultCode;
            Gamertag = gamertag;
            Gamer = gamer;
        }

        public bool Allowed { get; }
        public string ResultCode { get; }
        public string Gamertag { get; }
        public object Gamer { get; }

        public static ApprovalDecision Allow(string gamertag, object gamer) => new(true, null, gamertag, gamer);
        public static ApprovalDecision Deny(string resultCode) => new(false, resultCode, null, null);
    }
}
