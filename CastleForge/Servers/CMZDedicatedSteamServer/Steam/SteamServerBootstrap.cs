/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Common;
using CMZDedicatedSteamServer.Config;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Steam
{
    /// <summary>
    /// Initializes and owns the DNA.Steam SteamWorks runtime for the dedicated Steam host account.
    ///
    /// Purpose:
    /// - Calls the existing DNA.Steam wrapper against a currently running Steam client session.
    /// - Preloads the native steam_api.dll from the configured game path.
    /// - Writes steam_appid.txt next to the server executable when configured to do so.
    /// - Exposes the active Steam account identity to the dedicated host.
    ///
    /// Notes:
    /// - This uses the normal Steam client API path, not SteamGameServer.
    /// - It does NOT support username/password login from config.
    /// </summary>
    internal sealed class SteamServerBootstrap(string baseDir, string gamePath, SteamServerConfig config, Assembly steamAssembly, Action<string> log) : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private readonly string _baseDir = baseDir;
        private readonly string _gamePath = gamePath;
        private readonly SteamServerConfig _config = config;
        private readonly Assembly _steamAssembly = steamAssembly;
        private readonly Action<string> _log = log ?? (_ => { });
        private string _steamAppIdPath;

        public object SteamWorksInstance { get; private set; }
        public ulong SteamPlayerId { get; private set; }
        public string SteamName { get; private set; }

        public void Initialize()
        {
            EnsureSteamNativeDllLoaded();
            EnsureSteamAppIdContext();

            Type steamWorksType = ReflectEx.GetRequiredType(_steamAssembly, "DNA.Distribution.Steam.SteamWorks");
            SteamWorksInstance = Activator.CreateInstance(steamWorksType, [_config.SteamAppId]);

            PropertyInfo minimalUpdates = steamWorksType.GetProperty("AllowMinimalUpdates");
            minimalUpdates?.SetValue(SteamWorksInstance, _config.SteamAllowMinimalUpdates, null);

            bool ok = (bool)ReflectEx.GetRequiredMemberValue(SteamWorksInstance, "OperationWasSuccessful");
            bool isInitialized = (bool)ReflectEx.GetRequiredMemberValue(SteamWorksInstance, "IsInitialized");
            if (!ok || !isInitialized)
            {
                throw new InvalidOperationException(
                    "SteamWorks failed to initialize. Make sure Steam is already running under the same Windows user, " +
                    "the active account owns CastleMiner Z, and steam_appid.txt / steam_api.dll are discoverable.");
            }

            SteamPlayerId = Convert.ToUInt64(ReflectEx.GetRequiredMemberValue(SteamWorksInstance, "SteamPlayerID"));
            SteamName = Convert.ToString(ReflectEx.GetRequiredMemberValue(SteamWorksInstance, "SteamName"));

            _log($"[Steam] Initialized as '{SteamName}' ({SteamPlayerId}).");
        }

        public bool Update()
        {
            MethodInfo update = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "Update", 0);
            return (bool)update.Invoke(SteamWorksInstance, null);
        }

        public object GetPacketOrNull()
        {
            MethodInfo getPacket = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "GetPacket", 0);
            return getPacket.Invoke(SteamWorksInstance, null);
        }

        public void FreePacket(object packet)
        {
            if (packet == null)
                return;

            MethodInfo freePacket = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "FreeSteamNetBuffer", 1);
            freePacket.Invoke(SteamWorksInstance, [packet]);
        }

        public object AllocPacket()
        {
            MethodInfo alloc = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "AllocSteamNetBuffer", 0);
            return alloc.Invoke(SteamWorksInstance, null);
        }

        public void SendPacket(object packet, ulong destination, object deliveryMethod, int channel)
        {
            MethodInfo send = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "SendPacket", 4);
            send.Invoke(SteamWorksInstance, [packet, (object)destination, deliveryMethod, (object)channel]);
        }

        public void AcceptConnection(ulong steamId)
        {
            MethodInfo accept = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "AcceptConnection", 1);
            accept.Invoke(SteamWorksInstance, [steamId]);
        }

        public void DenyConnection(ulong steamId, string reason)
        {
            MethodInfo deny = ReflectEx.GetRequiredMethod(SteamWorksInstance.GetType(), "Deny", 2);
            deny.Invoke(SteamWorksInstance, [steamId, reason]);
        }

        private void EnsureSteamNativeDllLoaded()
        {
            string nativePath = Path.Combine(_gamePath, "steam_api.dll");
            if (!File.Exists(nativePath))
                throw new FileNotFoundException("Missing steam_api.dll in configured game-path.", nativePath);

            IntPtr handle = LoadLibrary(nativePath);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to load steam_api.dll from: " + nativePath);

            _log("[Steam] Loaded native steam_api.dll from game-path.");
        }

        private void EnsureSteamAppIdContext()
        {
            if (!_config.WriteSteamAppIdFile)
                return;

            _steamAppIdPath = Path.Combine(_baseDir, "steam_appid.txt");
            string expected = _config.SteamAppId.ToString();
            string existing = File.Exists(_steamAppIdPath) ? (File.ReadAllText(_steamAppIdPath) ?? string.Empty).Trim() : null;

            if (!string.Equals(existing, expected, StringComparison.Ordinal))
            {
                File.WriteAllText(_steamAppIdPath, expected);
                _log($"[Steam] Wrote steam_appid.txt with AppID {expected}.");
            }
        }

        public void Dispose()
        {
            try
            {
                if (SteamWorksInstance != null)
                {
                    MethodInfo leaveSession = SteamWorksInstance.GetType().GetMethod("LeaveSession", BindingFlags.Public | BindingFlags.Instance);
                    leaveSession?.Invoke(SteamWorksInstance, null);

                    MethodInfo shutdown = SteamWorksInstance.GetType().GetMethod("Unintialize", BindingFlags.Public | BindingFlags.Instance);
                    shutdown?.Invoke(SteamWorksInstance, null);
                }
            }
            finally
            {
                SteamWorksInstance = null;
            }
        }
    }
}
