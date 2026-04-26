/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Common;
using CMZDedicatedSteamServer.Config;
using System.Linq.Expressions;
using System.Reflection;
using System;

namespace CMZDedicatedSteamServer.Hosting
{
    /// <summary>
    /// Owns Steam lobby creation / refresh for the dedicated Steam host account.
    ///
    /// Notes:
    /// - This wraps the same SteamWorks.CreateLobby / UpdateHostLobbyData flow used by the game.
    /// - The callback is intentionally kept simple here: once the lobby exists,
    ///   the dedicated host can begin processing joins and packets.
    /// </summary>
    internal sealed class SteamLobbyHost(
        SteamServerConfig config,
        Assembly commonAssembly,
        Assembly steamAssembly,
        object steamWorks,
        Action<string> log)
    {
        private readonly SteamServerConfig _config = config;
        private readonly Assembly _commonAssembly = commonAssembly;
        private readonly Assembly _steamAssembly = steamAssembly;
        private readonly object _steamWorks = steamWorks;
        private readonly Action<string> _log = log ?? (_ => { });

        public bool IsLobbyReady { get; private set; }
        public object HostSessionInfo { get; private set; }

        public void BeginCreateLobby(string displayName, string displayMessage)
        {
            Type createSessionInfoType = ReflectEx.GetRequiredType(_commonAssembly, "DNA.Net.MatchMaking.CreateSessionInfo");
            Type joinPolicyType = ReflectEx.GetRequiredType(_commonAssembly, "DNA.Net.GamerServices.JoinGamePolicy");
            Type lobbyCreatedDelegateType = ReflectEx.GetRequiredType(_steamAssembly, "DNA.Distribution.Steam.LobbyCreatedDelegate");

            object createSessionInfo = Activator.CreateInstance(createSessionInfoType);
            ReflectEx.SetRequiredMemberValue(createSessionInfo, "Name", displayName);
            TrySetMemberValue(createSessionInfo, "Message", displayMessage);
            ReflectEx.SetRequiredMemberValue(createSessionInfo, "PasswordProtected", !string.IsNullOrWhiteSpace(_config.Password));
            ReflectEx.SetRequiredMemberValue(createSessionInfo, "IsPublic", _config.SteamLobbyVisible);
            ReflectEx.SetRequiredMemberValue(
                createSessionInfo,
                "JoinGamePolicy",
                Enum.Parse(joinPolicyType, _config.SteamFriendsOnly ? "FriendsOnly" : "Anyone"));
            ReflectEx.SetRequiredMemberValue(createSessionInfo, "MaxPlayers", _config.MaxPlayers);
            ReflectEx.SetRequiredMemberValue(createSessionInfo, "NetworkPort", _config.Port);

            object sessionProperties = ReflectEx.GetRequiredMemberValue(createSessionInfo, "SessionProperties");

            // Match CastleMiner Z Steam host layout so the in-game server browser query sees us.
            // [0] = 4
            // [1] = 0
            // [2] = GameMode
            // [3] = Difficulty
            // [4] = InfiniteResourceMode ? 1 : 0
            // [5] = PVPState
            for (int i = 0; i < 8; i++)
                SetSessionPropertyNullable(sessionProperties, i, null);

            SetSessionPropertyNullable(sessionProperties, 0, 4);
            SetSessionPropertyNullable(sessionProperties, 1, 0);
            SetSessionPropertyNullable(sessionProperties, 2, _config.GameMode);
            SetSessionPropertyNullable(sessionProperties, 3, _config.Difficulty);
            SetSessionPropertyNullable(sessionProperties, 4, _config.InfiniteResourceMode ? 1 : 0);
            SetSessionPropertyNullable(sessionProperties, 5, _config.PvpState);

            Delegate callback = BuildLobbyCreatedCallback(lobbyCreatedDelegateType);

            MethodInfo createLobby = ReflectEx.GetRequiredMethod(_steamWorks.GetType(), "CreateLobby", 3);
            bool started = (bool)createLobby.Invoke(_steamWorks, [createSessionInfo, callback, null]);

            if (!started)
                throw new InvalidOperationException("SteamWorks.CreateLobby returned false.");

            _log("[SteamLobby] Lobby creation requested.");
        }

        /// <summary>
        /// Updates the reflected host session name before publishing lobby metadata.
        /// </summary>
        public void SetLobbyName(string displayName)
        {
            if (HostSessionInfo == null)
                return;

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = _config.ServerName;

            ReflectEx.SetRequiredMemberValue(HostSessionInfo, "Name", displayName);
        }

        /// <summary>
        /// Updates the reflected host session message before publishing lobby metadata.
        /// </summary>
        public void SetLobbyMessage(string displayMessage)
        {
            if (HostSessionInfo == null)
                return;

            if (string.IsNullOrWhiteSpace(displayMessage))
                displayMessage = "Welcome to this CastleForge dedicated server.";

            TrySetMemberValue(HostSessionInfo, "Message", displayMessage);
        }

        /// <summary>
        /// Publishes the current reflected host session metadata to the Steam lobby.
        /// </summary>
        public void RefreshLobbyMetadata()
        {
            if (HostSessionInfo == null)
                return;

            MethodInfo updateLobby = ReflectEx.GetRequiredMethod(_steamWorks.GetType(), "UpdateHostLobbyData", 1);
            updateLobby.Invoke(_steamWorks, [HostSessionInfo]);
        }

        private Delegate BuildLobbyCreatedCallback(Type lobbyCreatedDelegateType)
        {
            MethodInfo invoke = lobbyCreatedDelegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("LobbyCreatedDelegate.Invoke was not found.");

            ParameterInfo[] invokeParams = invoke.GetParameters();
            if (invokeParams.Length != 2)
                throw new InvalidOperationException("LobbyCreatedDelegate was expected to have 2 parameters.");

            MethodInfo adapter = typeof(SteamLobbyHost).GetMethod(
                nameof(OnLobbyCreatedAdapter),
                BindingFlags.Instance | BindingFlags.NonPublic);

            var p0 = Expression.Parameter(invokeParams[0].ParameterType, invokeParams[0].Name ?? "hostInfo");
            var p1 = Expression.Parameter(invokeParams[1].ParameterType, invokeParams[1].Name ?? "context");

            var call = Expression.Call(
                Expression.Constant(this),
                adapter,
                Expression.Convert(p0, typeof(object)));

            return Expression.Lambda(lobbyCreatedDelegateType, call, p0, p1).Compile();
        }

        private void OnLobbyCreatedAdapter(object hostSessionInfo)
        {
            HostSessionInfo = hostSessionInfo ?? throw new InvalidOperationException("Steam lobby creation failed; hostSessionInfo was null.");
            IsLobbyReady = true;

            object altSessionId = ReflectEx.GetRequiredMemberValue(hostSessionInfo, "AltSessionID");
            _log($"[SteamLobby] Lobby created. AltSessionID={altSessionId}");
        }

        private static void SetSessionPropertyNullable(object sessionProperties, int index, int? value)
        {
            PropertyInfo indexer = sessionProperties.GetType().GetProperty("Item");
            indexer.SetValue(sessionProperties, value, [index]);
        }

        #region Reflection Helpers

        /// <summary>
        /// Reflected member setter for optional session fields.
        /// </summary>
        private static bool TrySetMemberValue(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            Type type = target.GetType();

            FieldInfo field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null)
            {
                field.SetValue(target, value);
                return true;
            }

            PropertyInfo property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return true;
            }

            return false;
        }
        #endregion
    }
}
