/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Audio;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace SetHomes
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class SetHomes : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public SetHomes() : base("SetHomes", new Version("0.0.1"))
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
            var ns    = typeof(SetHomes).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load or create config.
            SHConfig.LoadApply();

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
        /// Called once per game tick.
        /// Not used by this mod (but required by ModBase).
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime) { }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // General Commands.
            ("sethome", "Set a home at your current position. Usage: /sethome (name)."),
            ("home",    "Teleport to a saved home. Usage: /home (name)."),
            ("delhome", "Delete a saved home. Usage: /delhome (name)."),
            ("homes",   "List homes saved for the current world. Usage: /homes."),
            ("spawn",   "Teleport to world spawn. Usage: /spawn."),
        };
        #endregion

        #region Command Functions

        // General Commands.
        // NOTE
        // ----
        // All commands operate on the LOCAL player only (client-side) and persist per-world,
        // keyed by WorldID. That means each world has its own independent set of names
        // (including an independent "default" home).

        #region /sethome

        /// <summary>
        /// /sethome [name]
        /// - If [name] is omitted, sets the per-world default home.
        /// - Otherwise, saves/overwrites a named home for the current world.
        /// </summary>
        [Command("/sethome")]
        private static void ExecuteSetHome(string[] args)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game?.LocalPlayer == null || game.CurrentWorld == null)
                {
                    SendFeedback("ERROR: No world/player loaded yet.");
                    return;
                }

                string worldKey = HomesStorage.CurrentWorldKey();
                var    pos      = game.LocalPlayer.LocalPosition;
                var    rot      = game.LocalPlayer.LocalRotation;
                var    pit      = game.LocalPlayer.TorsoPitch;

                // Allow multi-word names.
                string displayName = (args != null && args.Length > 0)
                    ? string.Join(" ", args).Trim()
                    : null;

                HomesStorage.SaveHome(worldKey, displayName, pos, rot, pit, overwrite: true);

                string shown = HomesStorage.ToDisplayName(displayName);
                SendFeedback($"Home '{shown}' saved for this world.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /home

        /// <summary>
        /// /home [name]
        /// - If [name] is omitted, teleports to the per-world default home.
        /// - Otherwise, teleports to the named home.
        /// </summary>
        [Command("/home")]
        private static void ExecuteHome(string[] args)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game?.LocalPlayer == null || game.CurrentWorld == null)
                {
                    SendFeedback("ERROR: No world/player loaded yet.");
                    return;
                }

                string worldKey = HomesStorage.CurrentWorldKey();

                string displayName = (args != null && args.Length > 0)
                    ? string.Join(" ", args).Trim()
                    : null;

                if (!HomesStorage.TryGetHome(worldKey, displayName, out var pos, out var rot, out var pit))
                {
                    SendFeedback($"ERROR: Home '{HomesStorage.ToDisplayName(displayName)}' not found for this world.");
                    return;
                }

                game.LocalPlayer.LocalPosition = pos;
                game.LocalPlayer.LocalRotation = rot;
                game.LocalPlayer.TorsoPitch    = pit;
                TryPlayTeleportSound(TeleportSoundContext.Home); // Config-gated SFX.
                SendFeedback($"Teleported to '{HomesStorage.ToDisplayName(displayName)}'.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /delhome

        /// <summary>
        /// /delhome [name]
        /// - If [name] is omitted, deletes the per-world default home.
        /// - Otherwise, deletes the named home.
        /// </summary>
        [Command("/delhome")]
        private static void ExecuteDelHome(string[] args)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game?.CurrentWorld == null)
                {
                    SendFeedback("ERROR: No world loaded yet.");
                    return;
                }

                string worldKey = HomesStorage.CurrentWorldKey();

                string displayName = (args != null && args.Length > 0)
                    ? string.Join(" ", args).Trim()
                    : null;

                bool removed = HomesStorage.RemoveHome(worldKey, displayName);
                if (!removed)
                    SendFeedback($"ERROR: Home '{HomesStorage.ToDisplayName(displayName)}' not found for this world.");
                else
                    SendFeedback($"Home '{HomesStorage.ToDisplayName(displayName)}' deleted.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /homes

        /// <summary>
        /// /homes
        /// Lists all homes saved for the current world (including the default if set).
        /// </summary>
        [Command("/homes")]
        private static void ExecuteHomes()
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game?.CurrentWorld == null)
                {
                    SendFeedback("ERROR: No world loaded yet.");
                    return;
                }

                string worldKey = HomesStorage.CurrentWorldKey();
                var    names    = HomesStorage.GetHomeDisplayNames(worldKey, includeDefault: true);

                if (names.Count == 0)
                {
                    SendFeedback("No homes saved for this world.");
                    return;
                }

                // Chat is narrow; keep it compact.
                // Example: "Homes (3): <default>, Base, Mine"
                SendFeedback($"Homes ({names.Count}): {string.Join(", ", names)}.");
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /spawn

        /// <summary>
        /// /spawn
        /// -----
        /// Teleports the local player to the world's spawn/start location.
        /// Ignores any arguments.
        /// </summary>
        [Command("/spawn")]
        private static void ExecuteSpawn()
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game?.GameScreen == null)
                {
                    // If you have a chat-print helper, use it here; otherwise log.
                    ModLoader.LogSystem.Log("Cannot teleport: game screen not ready.");
                    return;
                }

                // Teleport to spawn.
                game.GameScreen.TeleportToLocation(WorldInfo.DefaultStartLocation, true);
                TryPlayTeleportSound(TeleportSoundContext.Spawn); // Config-gated SFX.
                SendFeedback($"Teleported to 'Spawn'.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"/spawn failed: {ex}.");
            }
        }
        #endregion

        #endregion

        #region Teleport Sound (Config)

        /// Teleport sound "context" used to decide which config toggle applies
        /// when playing a teleport sound effect.
        ///
        /// Example:
        /// - Home  -> Obeys PlayOnHomeTeleport.
        /// - Spawn -> Obeys PlayOnSpawnTeleport.
        /// </summary>
        private enum TeleportSoundContext
        {
            Home,
            Spawn
        }

        /// <summary>
        /// Plays a teleport sound depending on SetHomesConfig.
        /// Never throws (missing cue/etc. won't break commands).
        /// </summary>
        private static void TryPlayTeleportSound(TeleportSoundContext context)
        {
            var cfg = SHConfig.Active;
            if (cfg == null) return;

            if (!cfg.TeleportSoundEnabled) return;

            if (context == TeleportSoundContext.Home && !cfg.PlaySoundOnHomeTeleport) return;
            if (context == TeleportSoundContext.Spawn && !cfg.PlaySoundOnSpawnTeleport) return;

            string cue = (cfg.TeleportSoundName ?? "").Trim();
            if (string.IsNullOrEmpty(cue))
                cue = "Teleport";

            try
            {
                SoundManager.Instance.PlayInstance(cue);
            }
            catch
            {
                // Intentionally swallow: invalid cue / audio not ready / etc.
            }
        }
        #endregion

        #endregion
    }
}