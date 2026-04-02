/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.IO;
using System;

namespace ModLoader
{
    /// <summary>
    /// GameComponent that bootstraps mod loading and ticking.
    /// Adds itself to the XNA game's component collection and
    /// executes once per update to initialize and tick mods.
    /// </summary>
    public class ModBootstrapComponent : GameComponent
    {
        private bool _initialized; // Tracks whether we've already initialized the mod system.
        private bool _skipMods;    // Set when user chose "Launch without mods".
        private bool _exiting;     // Set once we request exit (avoid re-entrancy).

        // Constructs the bootstrap component and attaches it to the provided Game.
        public ModBootstrapComponent(Game game) : base(game) { }

        // Called every game update frame. On first call, loads all mods from disk.
        // Every frame thereafter, dispatches Tick to all loaded mods.
        public override void Update(GameTime gameTime)
        {
            // If we're already exiting, do the absolute minimum each frame.
            if (_exiting)
                return;

            if (!_initialized)
            {
                _initialized = true;

                // Ask (or read remember) before loading mods.
                var mode  = StartupModeSelector.ResolveLaunchMode();
                _skipMods = (mode == LaunchMode.WithoutMods);

                switch (mode)
                {
                    case LaunchMode.Abort:
                        // Ask the Game loop to terminate cleanly (fires OnExiting etc.).
                        _exiting = true;
                        Console.WriteLine("[ModLoader] User chose to exit.");
                        try
                        {
                            Game.Exit(); // Use the instance we're attached to, not a global.
                        }
                        catch { }
                        return; // IMPORTANT: No mod loading/ticking this frame.


                    case LaunchMode.WithMods:
                        Console.WriteLine("[ModLoader] Launching with mods.");

                        // === Core hash guard ===
                        try
                        {
                            var decision = HashGuard.VerifyGameCoreHashOrPrompt();
                            if (decision == HashDecision.Abort)
                            {
                                _exiting = true;
                                try { Game.Exit(); } catch { }
                                return;
                            }
                            if (decision == HashDecision.ProceedWithoutMods)
                            {
                                _skipMods = true; // Flip to no-mods for this run.
                                break;
                            }
                            // else: ProceedWithMods -> continue to LoadMods
                        }
                        catch { /* If guard fails for any reason, fall back to normal load. */ }

                        try
                        {
                            // Load all mods from DLLs in the mods directory.
                            string modsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods");

                            bool showProgress = true;
                            if (ModLoaderConfig.TryLoad(out var cfg))
                                showProgress = cfg.ShowModLoadProgress;

                            if (showProgress)
                            {
                                using (var progressWindow = new ModLoadProgressWindow())
                                    ModManager.LoadMods(modsDir, progressWindow);
                            }
                            else
                            {
                                ModManager.LoadMods(modsDir);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[ModLoader] Failed to load mods: " + ex.Message);
                            _skipMods = true; // Optional: Continue without mods if load fails.
                        }
                        break;

                    case LaunchMode.WithoutMods:
                        Console.WriteLine("[ModLoader] Launching without mods.");
                        _skipMods = true;
                        break;
                }
            }

            // Only tick mods if we successfully opted into them.
            if (!_skipMods)
            {
                // Make sure any threads we start inside mods are background threads:
                // thread.IsBackground = true; so Exit() can actually terminate.
                ModManager.TickAll(null, gameTime);
            }

            // Ensure base GameComponent logic still runs.
            base.Update(gameTime);
        }
    }
}
