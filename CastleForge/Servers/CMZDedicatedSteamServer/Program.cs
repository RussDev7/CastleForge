/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Config;
using CMZDedicatedSteamServer.Hosting;
using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace CMZDedicatedSteamServer
{
    /// <summary>
    /// Console entry point for the Steam-native CastleMiner Z dedicated server.
    /// </summary>
    /// <remarks>
    /// This host starts the Steam dedicated server runtime, applies optional runtime patches,
    /// loads configuration, and runs the main server update loop until shutdown.
    /// </remarks>
    internal static class Program
    {
        #region Entry Point

        /// <summary>
        /// Program entry point for the Steam-native dedicated server host.
        /// </summary>
        /// <returns>
        /// 0 when the server exits normally; 1 when a fatal startup/runtime exception reaches Main.
        /// </returns>
        /// <remarks>
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
        /// </remarks>
        private static int Main()
        {
            Console.Title = "CMZ Dedicated Steam Server";

            try
            {
                #region Resolve Base Paths

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Log($"Config file       : {Path.Combine(baseDir, "server.properties")}");
                SteamServerConfig config = SteamServerConfig.Load(baseDir);

                string gamePath = string.IsNullOrWhiteSpace(config.GamePath)
                    ? Path.Combine(baseDir, "Game")
                    : (Path.IsPathRooted(config.GamePath)
                        ? config.GamePath
                        : Path.GetFullPath(Path.Combine(baseDir, config.GamePath)));

                string libsPath = Path.Combine(baseDir, "Libs");

                #endregion

                #region Assembly Resolution

                AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
                {
                    var asmName = new AssemblyName(resolveArgs.Name);

                    // 1) Support libraries beside the server, inside /Libs.
                    string libsDllPath = Path.Combine(libsPath, asmName.Name + ".dll");
                    if (File.Exists(libsDllPath))
                        return Assembly.LoadFrom(libsDllPath);

                    // 2) Game folder DLLs.
                    string gameDllPath = Path.Combine(gamePath, asmName.Name + ".dll");
                    if (File.Exists(gameDllPath))
                        return Assembly.LoadFrom(gameDllPath);

                    // 3) Game folder EXE assemblies.
                    string gameExeAsmPath = Path.Combine(gamePath, asmName.Name + ".exe");
                    if (File.Exists(gameExeAsmPath))
                        return Assembly.LoadFrom(gameExeAsmPath);

                    return null;
                };
                #endregion

                #region Apply Runtime Patches

                ServerPatches.ApplyAllPatches();

                #endregion

                using var server = new SteamDedicatedServer(baseDir, config, Log);

                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    Log("Ctrl+C received. Shutting down...");
                    server.Stop();
                };

                server.Start();

                StartConsoleCommandThread(server, Log);

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

        #region Console Commands

        /// <summary>
        /// Starts a background console command reader for runtime server commands.
        /// </summary>
        private static Thread StartConsoleCommandThread(SteamDedicatedServer server, Action<string> log)
        {
            Thread thread = new(() =>
            {
                while (server.IsRunning)
                {
                    string line;

                    try
                    {
                        line = Console.ReadLine();
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                        break;

                    HandleConsoleCommand(server, line, log);
                }
            })
            {
                IsBackground = true,
                Name = "ConsoleCommandThread"
            };
            thread.Start();

            return thread;
        }

        /// <summary>
        /// Handles a single console command.
        /// </summary>
        private static void HandleConsoleCommand(SteamDedicatedServer server, string line, Action<string> log)
        {
            string command = (line ?? string.Empty).Trim();

            if (command.Length == 0)
                return;

            if (command.Equals("players", StringComparison.OrdinalIgnoreCase))
            {
                log(server.GetPlayerListText());
                return;
            }

            if (command.StartsWith("kick ", StringComparison.OrdinalIgnoreCase))
            {
                string args = command.Substring(5).Trim();

                if (!TryParseTargetAndReason(args, out string target, out string reason))
                {
                    log("Usage: kick <id|steamid|name> [reason]");
                    log("Example: kick 2 Being annoying");
                    log("Example: kick \"Jacob Ladders\" Being annoying");
                    return;
                }

                server.KickPlayer(target, reason);
                return;
            }

            if (command.StartsWith("ban ", StringComparison.OrdinalIgnoreCase))
            {
                string args = command.Substring(4).Trim();

                if (!TryParseTargetAndReason(args, out string target, out string reason))
                {
                    log("Usage: ban <id|steamid|name> [reason]");
                    log("Example: ban 2 Griefing protected areas");
                    log("Example: ban \"Jacob Ladders\" Griefing protected areas");
                    return;
                }

                server.BanPlayer(target, reason);
                return;
            }

            if (command.StartsWith("unban ", StringComparison.OrdinalIgnoreCase))
            {
                string target = command.Substring(6).Trim();

                if (target.StartsWith("\"") && target.EndsWith("\"") && target.Length > 1)
                    target = target.Substring(1, target.Length - 2).Trim();

                server.UnbanPlayer(target);
                return;
            }

            switch (command.ToLowerInvariant())
            {
                case "help":
                case "?":
                    log("Console commands:");
                    log("  reload                          Reload server.properties and plugin files");
                    log("  reload properties               Reload runtime-safe server.properties values");
                    log("  reload plugins                  Reload plugin config/region/announcement files");
                    log("  stop                            Stop the server");
                    log("  players                         List connected players");
                    log("  bans                            List saved bans");
                    log("  kick <id|steamid|name> [reason] Hard-kick a connected player");
                    log("  ban <id|steamid|name> [reason]  Hard-ban and drop a connected player");
                    log("  unban <steamid|ip|name>         Remove a saved ban");
                    log("  help                            Show this command list");
                    break;

                case "reload":
                    server.Reload();
                    break;

                case "reload properties":
                case "properties reload":
                case "reload config":
                case "config reload":
                    server.ReloadServerProperties();
                    break;

                case "reload plugins":
                case "plugins reload":
                    server.ReloadPlugins();
                    break;

                case "stop":
                case "exit":
                case "quit":
                    log("Console stop command received. Shutting down...");
                    server.Stop();
                    break;

                default:
                    log("Unknown command. Type 'help' for console commands.");
                    break;
            }
        }
        #endregion

        #region Parsing Helpers

        /// <summary>
        /// Parses commands that use:
        ///   command <target> [reason...]
        ///
        /// Supported examples:
        ///   kick 2 Being annoying
        ///   ban 76561198000000000 Griefing
        ///   kick "Jacob Ladders" Being annoying
        ///   ban "Jacob Ladders" Griefing protected areas
        ///
        /// Notes:
        /// - Quoted targets allow player names with spaces.
        /// - Numeric targets should be player IDs or SteamIDs.
        /// - If no reason is provided, the server will use its default kick/ban reason.
        /// </summary>
        private static bool TryParseTargetAndReason(string args, out string target, out string reason)
        {
            target = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(args))
                return false;

            args = args.Trim();

            // Quoted target: "Jacob Ladders" optional reason here
            if (args.StartsWith("\""))
            {
                int closingQuote = args.IndexOf('"', 1);

                if (closingQuote <= 1)
                    return false;

                target = args.Substring(1, closingQuote - 1).Trim();

                if (closingQuote + 1 < args.Length)
                    reason = args.Substring(closingQuote + 1).Trim();

                return target.Length > 0;
            }

            // Normal target: first token is the target, everything after it is the reason.
            int firstSpace = args.IndexOf(' ');

            if (firstSpace < 0)
            {
                target = args;
                return target.Length > 0;
            }

            target = args.Substring(0, firstSpace).Trim();
            reason = args.Substring(firstSpace + 1).Trim();

            return target.Length > 0;
        }
        #endregion

        #region Logging

        /// <summary>
        /// Writes a timestamped server message to the console.
        /// </summary>
        /// <param name="message">Message to write.</param>
        private static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
        #endregion
    }
}