/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Hosting;
using CMZDedicatedSteamServer.Config;
using System.Threading;
using System;

namespace CMZDedicatedSteamServer
{
    internal static class Program
    {
        #region Entry Point

        /// <summary>
        /// Program entry point for the Steam-native dedicated server host.
        ///
        /// Purpose:
        /// - Loads server configuration from disk.
        /// - Resolves the CastleMiner Z game/runtime assemblies.
        /// - Initializes the Steam API against the currently logged-in Steam client.
        /// - Creates a Steam lobby owned by that active account.
        /// - Enters the main update loop for packet processing and future world ticks.
        ///
        /// Notes:
        /// - This host intentionally does NOT launch the game window.
        /// - This host also does NOT support username/password Steam login from config.
        ///   Steam's normal client API path requires an already-running Steam client.
        /// </summary>
        private static int Main()
        {
            Console.Title = "CMZ Dedicated Steam Server";

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                SteamServerConfig config = SteamServerConfig.Load(baseDir);

                using var server = new SteamDedicatedServer(baseDir, config, Log);
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    Log("Ctrl+C received. Shutting down...");
                    server.Stop();
                };

                server.Start();

                int sleepMs = Math.Max(1, 1000 / Math.Max(1, config.TickRateHz));
                while (server.IsRunning)
                {
                    try
                    {
                        server.Update();
                    }
                    catch (Exception ex)
                    {
                        Log("[MainLoop] " + ex);
                    }

                    Thread.Sleep(sleepMs);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log("FATAL: " + ex);
                return 1;
            }
        }

        #endregion

        #region Logging

        private static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }

        #endregion
    }
}
