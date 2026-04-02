/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace Example
{
    /// <summary>
    /// Declares that this mod cannot load unless "ModLoaderExtensions" is present,
    /// and sets the mod's load priority to the default ("Normal").
    ///
    /// [RequiredDependencies(...)]
    /// When the mod loader loads plugins, it scans for the [RequiredDependencies(...)] attribute.
    /// If any of the named mods are not already loaded, this mod will be skipped and a message will be logged.
    /// Use this to specify a hard dependency on other mods or core extensions so required features are available
    /// before your mod runs. e.g., requiring "ModLoaderExtensions" guarantees its shared features are available.
    /// Usage:
    ///     [RequiredDependencies("ModLoaderExtensions")]
    ///     [RequiredDependencies("ModA", "ModB")]
    ///
    /// [Priority(...)]
    /// The loader orders mods by priority (higher loads earlier) and then by type name as a stable tiebreaker.
    /// If omitted, priority defaults to Priority.Normal (400).
    /// Common values: Last=0, VeryLow=100, Low=200, LowerThanNormal=300, Normal=400,
    ///                HigherThanNormal=500, High=600, VeryHigh=700, First=800.
    /// Note: Dependencies still take precedence-this mod will not start until its required mods are loaded,
    /// even if its priority is higher.
    /// Usage:
    ///     [Priority(Priority.Normal)]         // default priority
    ///     [Priority(Priority.High)]           // load earlier than Normal
    /// </summary>
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class Example : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public Example() : base("Example", new Version("0.0.1"))
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
        /// 3) Create and load the config.
        /// 4) Register your command handlers.
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
            var ns    = typeof(Example).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load or create config.
            ExConfig.LoadApply();

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
            // Testing commands.
            ("launch", "Launch the game."),
            ("exit",   "Exit the game."),
            ("debug",  "Debugs something.")
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /test
        #pragma warning disable IDE0060 // Remove unused parameter.

        [Command("/experiment")]
        [Command("/trial")]
        [Command("/test")]
        private static void ExecuteTest(string[] args)
        {
            try
            {
                // Do something here.
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}");
            }
        }
        #pragma warning restore IDE0060
        #endregion

        #region /example

        [Command("/example")]
        private static void ExecuteExample(string[] args)
        {
            if (args.Length < 1)
            {
                SendFeedback("ERROR: Command usage /example [switch]");
                return;
            }

            try
            {
                switch (args[0].ToLower())
                {
                    case "a":
                        if (args.Length > 0 && args.Length < 2) // Ensure the command is more then one, less then 2.
                        {
                            // Do something here for switch 'a'.
                        }
                        else
                        {
                            // Thrown if too many parameters.
                            SendFeedback("ERROR: Missing parameter. Usage: /example [switch]");
                            return;
                        }
                        break;

                    case "b":
                        if (args.Length > 0 && args.Length < 2) // Ensure the command is more then one, less then 2.
                        {
                            // Do something here for switch 'b'.
                        }
                        else
                        {
                            // Thrown if too many parameters.
                            SendFeedback("ERROR: Missing parameter. Usage: /example [switch]");
                            return;
                        }
                        break;

                    default:
                        // Unkown parameter.
                        SendFeedback("ERROR: Command usage /example [switch]");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}");
            }
        }
        #endregion

        #endregion

        #region Other Functions...
        #endregion

        #endregion
    }
}