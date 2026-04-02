/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace DedicatedServerLauncher
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class DedicatedServerLauncher : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public DedicatedServerLauncher() : base("DedicatedServerLauncher", new Version("0.0.1"))
        {
            EmbeddedResolver.Init();                    // Load any native & managed DLLs embedded as resources (e.g., Harmony, cimgui, other libs).
            _dispatcher = new CommandDispatcher(this);  // Create the command dispatcher, pointing it at this instance so it can find [Command]-annotated methods.

            var game = CastleMinerZGame.Instance;       // Hook into the game's shutdown event to clean up patches and resources on exit.
            if (game != null)
                game.Exiting += (s, e) => Shutdown();
        }

        /// <summary>
        /// Called once when the mod is first loaded by the ModLoader.
        /// Good place to:
        /// 1) Verify the game is running.
        /// 2) Install any Harmony patches or interceptors.
        /// 3) Register your command handlers.
        /// </summary>
        public override void Start()
        {
            // Acquire game and world references.
            var game = CastleMinerZGame.Instance;
            if (game == null)
            {
                Log("Game instance is null.");
                return;
            }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(DedicatedServerLauncher).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Register this plugin's command dispatcher with the interceptor.
            // Each time a player types "/command", our dispatcher will be invoked.
            // Also register this plugin's command list to the global help registry.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));
            HelpRegistry.Register(this.Name, commands);

            // Notify in log that the mod is ready.
            // Lazy: Use this namespace as the 'mods' name.
            Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} loaded.");
        }

        /// <summary>
        /// Called when the game exits or mod is unloaded.
        /// Used to safely dispose patches and resources.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                try { GamePatches.DisableAll(); } catch (Exception ex) { Log($"Disable hooks failed: {ex.Message}."); } // Unpatch Harmony.

                // Notify in log that the mod teardown was complete.
                // Lazy: Use this namespace as the 'mods' name.
                Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            { Log($"Error shutting down mod: {ex}."); }
        }

        /// <summary>
        /// Called once per game tick. We use this to detect when the world object
        /// becomes available and then cache it for later commands (like /foo).
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            // If we haven't yet grabbed the world reference, try now.
            /*
            if (_world == null)
            {
                var candidate = CastleMinerZGame.Instance?.CurrentWorld;
                if (candidate != null)
                {
                    _world = candidate;               // Cache for future ticks so we don't repeat this work.
                    Log("World reference acquired."); // Log that we finally acquired the world.
                }
            }
            */

            // Once _world is set you could invoke per-tick world edits here if desired.
        }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            ("launchdedicated", "Launch a second CastleMinerZ instance in dedicated mode."),
            ("dedicated",       "Alias of /launchdedicated.")
        };

        #endregion

        #region Command Functions

        [Command("/launchdedicated")]
        [Command("/dedicated")]
        private static void ExecuteLaunchDedicated(string[] args)
        {
            try
            {
                int port = 61903;
                if (args.Length >= 1)
                    int.TryParse(args[0], out port);

                if (port <= 0 || port >= 65536)
                {
                    SendFeedback("ERROR: Invalid port. Use a value from 1 to 65535.");
                    return;
                }

                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string serverName = Screen.CurrentGamer != null
                    ? Screen.CurrentGamer.Gamertag + "'s Dedicated Server"
                    : "Dedicated Server";

                string markerDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "DedicatedServerLauncher");
                Directory.CreateDirectory(markerDir);

                string markerPath = Path.Combine(markerDir, "PendingDedicatedLaunch.txt");

                File.WriteAllLines(markerPath, new string[]
                {
            "TimestampUtc=" + DateTime.UtcNow.ToString("o"),
            "Port=" + port.ToString(),
            "Name=" + serverName,
            "Password="
                });

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false
                };

                Process.Start(psi);
                SendFeedback($"Launched dedicated instance on port {port}.");
            }
            catch (Exception ex)
            {
                SendFeedback("ERROR: " + ex.Message);
            }
        }
        #endregion

        #endregion
    }
}