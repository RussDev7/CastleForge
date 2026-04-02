/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using System.Windows.Forms;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace WorldEditPixelart
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions", "WorldEdit")]
    public class WorldEditPixelart : ModBase
    {
        /// <summary>
        /// Entrypoint for the WorldEditPixelart mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public WorldEditPixelart() : base("WorldEditPixelart", new Version("1.0.0"))
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
            var ns    = typeof(WorldEditPixelart).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Load or create config once at mod startup.
            // Then push values into ConfigGlobals (the shared, global "truth" the mod reads everywhere).
            var cfg = WEPConfig.LoadOrCreate();
            ApplyConfig(cfg);

            // NOTE: ConfigGlobals are read across the whole mod (ticks, patches, UI).
            void ApplyConfig(WEPConfig c)
            {
                ConfigGlobals.ToggleKey    = c.ToggleKey;
                ConfigGlobals.EmbedAsChild = c.EmbedAsChild;
            }

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
                try { GamePatches.DisableAll();  } catch (Exception ex) { Log($"Disable hooks failed: {ex.Message}.");     } // Unpatch Harmony.
                try { WinFormsOverlay.Dispose(); } catch (Exception ex) { Log($"Disable win-forms failed: {ex.Message}."); } // Unpatch WinForms.

                // Notify in log that the mod teardown was complete.
                // Lazy: Use this namespace as the 'mods' name.
                Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            { Log($"Error shutting down mod: {ex}."); }
        }

        /// <summary>
        /// Called once per game tick.
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            // Only OPEN from the game (has focus). Closing is handled inside the form.
            if (!WinFormsOverlay.IsOpen          &&
                ConfigGlobals.ToggleKey.HasValue &&
                Hotkey.PressedOnce(ConfigGlobals.ToggleKey.Value))
            {
                WinFormsOverlay.Show();
            }
        }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // General commands.
            ("pixelart", "Launch the ImageToPixelart tool."),
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /pixelart

        [Command("//imagetopixelart")]
        [Command("/imagetopixelart")]
        [Command("//pixelarttool")]
        [Command("/pixelarttool")]
        [Command("//pixelart")]
        [Command("/pixelart")]
        [Command("//pixel")]
        [Command("/pixel")]
        [Command("//pa")]
        [Command("/pa")]
        private static void ExecuteExample()
        {
            try
            {
                // Only OPEN from the game (has focus). Closing is handled inside the form.
                if (!WinFormsOverlay.IsOpen)
                    WinFormsOverlay.Show();
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}");
            }
        }
        #endregion

        #endregion

        #endregion
    }

    #region Hotkey Manager

    /// <summary>
    /// Hotkey: Tiny global hotkey helper for edge-detected key presses.
    ///
    /// What it does:
    ///   • Uses Win32 GetAsyncKeyState so it works even when the game window is NOT focused.
    ///   • Returns TRUE exactly once on the Up->Down transition (rising edge).
    ///
    /// Notes:
    ///   • This is polled every frame; if the window is truly inactive and your Update
    ///     is throttled, very fast taps can land between ticks. That's why we close
    ///     the overlay from the form itself (form KeyDown) and only use this to OPEN.
    /// </summary>
    internal static class Hotkey
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vkey);

        // Tracks last "down" state per VK to detect edges.
        private static readonly bool[] _down = new bool[256];

        /// <summary>
        /// Returns true exactly once when the key transitions from Up->Down,
        /// even if the game window does not have focus (global).
        /// </summary>
        public static bool PressedOnce(Keys key)
        {
            int vk = (int)key;
            short s = GetAsyncKeyState(vk);
            bool isDown = (s & 0x8000) != 0;

            bool fired = isDown && !_down[vk];
            _down[vk] = isDown;
            return fired;
        }
    }
    #endregion
}