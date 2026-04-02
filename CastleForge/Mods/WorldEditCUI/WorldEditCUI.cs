/*
Copyright (c) 2025 RussDev7

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

/*
Sections of this class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp.
*/

using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static WorldEditCUI.CUIOverlayRenderer;
using static WorldEditCUI.CUIConfig;
using static ModLoader.LogSystem;

namespace WorldEditCUI
{
    [Priority(Priority.HigherThanNormal)]
    [RequiredDependencies("ModLoaderExtensions", "WorldEdit")]
    public class WorldEditCUI : ModBase
    {
        /// <summary>
        /// Entrypoint for the WorldEditCUI mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public WorldEditCUI() : base("WorldEditCUI", new Version("0.1.0"))
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
            var ns    = typeof(WorldEditCUI).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load or create config.
            CUIConfig.LoadApply();

            // Register this mod's command dispatcher with the interceptor.
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
        /// This is the main function logic for the mod.
        /// This example code was taken from 'WorldEdit-CSharp' by RussDev7.
        /// Main Project: https://github.com/RussDev7/WorldEdit-CSharp
        /// </summary>
        #region WorldEditCUI Mod

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // General Commands.
            ("cui [on/off]",                  "Show the graphical user interface for WorldEdit."),                            // Handeled by WorldEdit.cs.
            ("cuimode [grid|outline]",        "Toggle or set the base selection outline mode (default Grid)."),
            ("cuichunks [on/off/toggle]",     "Toggle 16x16 chunk outlines within the current selection (default yellow)."),
            ("cuichunksgrid [on/off/toggle]", "Toggle the 24x24 chunk mega-grid overlay (every 384 blocks, default green)."),
            ("cuicolor <r,g,b,a>",            "Set the CUI selection outline color (saved to config)."),
            ("cuichunkscolor <r,g,b,a>",      "Set the 16x16 chunk outline color (saved to config)."),
            ("cuichunksgridcolor <r,g,b,a>",  "Set the mega-grid color (saved to config)."),
            ("cuireload",                     "Reload WorldEditCUI.Config.ini from disk."),
        };
        #endregion

        #region Chat Command Functions

        #region /cuimode

        /// <summary>
        /// /cuimode [grid|outline]
        /// Controls the base selection outline mode:
        ///  - No args  : Toggles Grid <-> Outline.
        ///  - "grid"   : Use OutlineSelectionWithGrid (default).
        ///  - "outline": Use OutlineSelection (edges only).
        /// Persists to WorldEditCUI.Config.ini.
        /// </summary>
        [Command("//cuimode")]
        [Command("/cuimode")]
        private void ExecuteCUIMode(string[] args)
        {
            // No args => toggle.
            if (args == null || args.Length == 0)
            {
                UseGridBaseOutline = !UseGridBaseOutline;
                CUIConfig.SaveFromStatics();
                SendFeedback($"WE_CUI: BaseMode set to {(UseGridBaseOutline ? "Grid" : "Outline")}.");
                return;
            }

            // One arg => parse.
            var s = (args[0] ?? "").Trim();

            if (s.Equals("grid", StringComparison.OrdinalIgnoreCase))
            {
                UseGridBaseOutline = true;
                CUIConfig.SaveFromStatics();
                SendFeedback("WE_CUI: BaseMode set to Grid.");
                return;
            }

            if (s.Equals("outline", StringComparison.OrdinalIgnoreCase))
            {
                UseGridBaseOutline = false;
                CUIConfig.SaveFromStatics();
                SendFeedback("WE_CUI: BaseMode set to Outline.");
                return;
            }

            // Unknown arg => show usage and current mode.
            SendFeedback($"Usage: /cuimode [grid|outline] (Current: {(UseGridBaseOutline ? "Grid" : "Outline")}.)");
        }
        #endregion

        #region /cuichunks

        /// <summary>
        /// /cuichunks [on/off/toggle]
        /// Toggles drawing 16x16 chunk outlines inside the current CUI selection.
        /// </summary>
        [Command("//cuichunks")]
        [Command("/cuichunks")]
        [Command("//cuichunk")]
        [Command("/cuichunk")]
        private void ExecuteCUIChunks(string[] args)
        {
            ShowChunkOutlines = ResolveToggle(args, ShowChunkOutlines);

            // Persist to INI.
            CUIConfig.SaveFromStatics();

            SendFeedback($"WE_CUI: Chunk outlines (16x16): {(ShowChunkOutlines ? "ON" : "OFF")}.");
        }
        #endregion

        #region /cuichunksgrid

        /// <summary>
        /// /cuichunksgrid [on/off/toggle]
        /// Toggles drawing the 24x24-chunk mega-grid (every 384 blocks) inside the selection.
        /// </summary>
        [Command("//cuichunksgrid")]
        [Command("/cuichunksgrid")]
        [Command("//cuichunkgrid")]
        [Command("/cuichunkgrid")]
        private void ExecuteCUIChunksGrid(string[] args)
        {
            ShowChunkGrid = ResolveToggle(args, ShowChunkGrid);

            // Persist to INI.
            CUIConfig.SaveFromStatics();

            SendFeedback($"WE_CUI: Chunk grid (24x24 / 384-block): {(ShowChunkGrid ? "ON" : "OFF")}.");
        }
        #endregion

        #region /cuicolor

        /// <summary>
        /// /cuicolor <r,g,b,a>
        /// Sets the main CUI selection outline color and saves it to config.
        /// </summary>
        [Command("//cuicolor")]
        [Command("/cuicolor")]
        private void ExecuteCUIColor(string[] args)
        {
            if (!TryParseColorArgs(args, out var c))
            {
                SendFeedback("Usage: /cuicolor <r,g,b,a>  (alpha optional, default 255)");
                return;
            }

            CUIOutlineColor = c;
            CUIConfig.SaveFromStatics();
            SendFeedback($"WE_CUI: CUI outline color set to {ColorToIni(c)}.");
        }
        #endregion

        #region /cuichunkscolor

        /// <summary>
        /// /cuichunkscolor <r,g,b,a>
        /// Sets the 16x16 chunk outline color and saves it to config.
        /// </summary>
        [Command("//cuichunkscolor")]
        [Command("/cuichunkscolor")]
        [Command("//cuichunkcolor")]
        [Command("/cuichunkcolor")]
        private void ExecuteCUIChunksColor(string[] args)
        {
            if (!TryParseColorArgs(args, out var c))
            {
                SendFeedback("Usage: /cuichunkscolor <r,g,b,a>  (alpha optional, default 255)");
                return;
            }

            ChunkOutlineColor = c;
            CUIConfig.SaveFromStatics();
            SendFeedback($"WE_CUI: Chunk outline color set to {ColorToIni(c)}.");
        }
        #endregion

        #region /cuichunksgridcolor

        /// <summary>
        /// /cuichunksgridcolor <r,g,b,a>
        /// Sets the mega-grid color and saves it to config.
        /// </summary>
        [Command("//cuichunksgridcolor")]
        [Command("/cuichunksgridcolor")]
        [Command("//cuichunkgridcolor")]
        [Command("/cuichunkgridcolor")]
        private void ExecuteCUIChunksGridColor(string[] args)
        {
            if (!TryParseColorArgs(args, out var c))
            {
                SendFeedback("Usage: /cuichunksgridcolor <r,g,b,a>  (alpha optional, default 255)");
                return;
            }

            ChunkGridOutlineColor = c;
            CUIConfig.SaveFromStatics();
            SendFeedback($"WE_CUI: Chunk grid color set to {ColorToIni(c)}.");
        }
        #endregion

        #region /cuireload

        /// <summary>
        /// /cuireload
        /// Reloads WorldEditCUI.Config.ini from disk and applies it.
        /// </summary>
        [Command("//cuireload")]
        [Command("/cuireload")]
        [Command("//cuir")]
        [Command("/cuir")]
        private void ExecuteCUIReload()
        {
            CUIConfig.LoadApply();
            SendFeedback("WE_CUI: Reloaded config.");
        }
        #endregion

        #endregion

        #region Command Parsing Helpers

        /// <summary>
        /// Parses "on/off/toggle/true/false/1/0". If args empty/unrecognized, returns !current.
        /// </summary>
        private static bool ResolveToggle(string[] args, bool current)
        {
            if (args == null || args.Length == 0) return !current;

            var s = (args[0] ?? "").Trim().ToLowerInvariant();
            if (s == "toggle") return !current;
            if (s == "on" || s == "true" || s == "1") return true;
            if (s == "off" || s == "false" || s == "0") return false;

            // Unknown arg => toggle.
            return !current;
        }
        #endregion

        #endregion
    }
}