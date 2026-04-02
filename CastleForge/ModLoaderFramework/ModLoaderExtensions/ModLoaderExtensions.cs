/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using System.Linq;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

namespace ModLoaderExt
{
    [Priority(Priority.Bootstrap)] // Always make this the first priority (when present).
    [RequiredDependencies("")]
    public class ModLoaderExtensions : ModBase
    {
        /// <summary>
        /// Entrypoint for ModLoaderExtensions:
        /// Provides core enhancements for the ModLoader and its mods,
        /// such as slash-command support via a centraliMob chat interceptor.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public ModLoaderExtensions() : base("ModLoaderExtensions", new Version("0.1.0"))
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
                LogSystem.Log("Game instance is null.");
                return;
            }

            // Extract embedded resources for this mod into the
            // !Mods/<Namespace> folder; skipped if nothing embedded.
            var ns    = typeof(ModLoaderExtensions).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) LogSystem.Log($"Extracted {wrote} file(s) to {dest}.");

            // Load ModLoader.ini (or fall back to defaults if missing),
            // then arm the exception capture system using the configured mode
            // (ExceptionMode: Off | CaughtOnly | FirstChance).
            if (!ModLoaderConfig.TryLoad(out var cfg)) cfg = new ModLoaderConfig();
            ExceptionTap.Arm(cfg.ExceptionMode);

            // Load or create config.
            // Apply persisted defaults for the in-game entity limiter.
            MLEConfig.LoadApply();

            // Install the shared chat interceptor once.
            // This patches the game's BroadcastTextMessage.Send method so
            // we can handle slash-commands centrally.
            ChatInterceptor.Install();

            // Register this plugin's command dispatcher with the interceptor.
            // Each time a player types "/command", our dispatcher will be invoked.
            ChatInterceptor.RegisterHandler(raw => _dispatcher.TryInvoke(raw));

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Notify in log that the mod is ready.
            LogSystem.Log("ModLoaderExtensions loaded.");
        }

        /// <summary>
        /// Called when the game exits or mod is unloaded.
        /// Used to safely dispose patches and resources.
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                // Notify in log that the mod teardown was started.
                LogSystem.WriteLogSessionSeparator("Shutdown Initiated.", false);

                try { GamePatches.DisableAll(); } catch (Exception ex) { LogSystem.Log($"Disable hooks failed: {ex.Message}."); } // Unpatch Harmony.

                // Notify in log that the mod teardown was complete.
                // Lazy: Use this namespace as the 'mods' name.
                LogSystem.Log($"{MethodBase.GetCurrentMethod().DeclaringType.Namespace} shutdown complete.");
            }
            catch (Exception ex)
            { LogSystem.Log($"Error shutting down mod: {ex}."); }
        }

        /// <summary>
        /// Called once per game tick.
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            try { EntityLimiterSystem.Tick(); } catch { } // Run the entity limiter each frame.
        }
        #endregion

        /// <summary>
        /// Core, cross-mod commands (shared handlers registered by ModLoaderExtensions).
        /// </summary>
        #region Core Commands

        #region /help

        // Aggregates commands from ALL loaded mods via HelpRegistry and renders a paged, sectioned help list to chat.
        // args can be: []            -> all mods, page 1.
        //              ["2"]         -> all mods, page 2.
        //              ["example"]   -> "example" only, page 1.
        //              ["example","2"] or ["2","example"].
        // Render + send lines via ChatSystem (inside Render will call SendFeedback).
        [Command("/help")]
        private static void ExecuteHelp(string[] args)
        {
            // How many rows we print per page (headers/spacers count as rows).
            int pageSize = GetHelpPageSize();

            // Snapshot the global registry once so results are stable during rendering.
            // Snap: Dictionary<string modName, List<CommandInfo>>.
            var snap = HelpRegistry.Snapshot();
            if (snap.Count == 0)
            {
                LogSystem.SendFeedback("No commands registered.");
                return;
            }

            // Parse: /help | /help <page> | /help <mod> | /help <mod> <page> | /help <page> <mod>.
            string modFilter = null; // Null => show all mods.
            int page = 1;

            if (args.Length == 1)
            {
                // Single arg: Either a page number or a mod filter.
                if (int.TryParse(args[0], out int p) && p >= 1) page = p;
                else modFilter = args[0].Trim();
            }
            else if (args.Length >= 2)
            {
                // Support BOTH orders:
                //   /help <mod> <page>.
                //   /help <page> <mod>.
                if (int.TryParse(args[0], out var p0) && p0 >= 1)
                {
                    page = p0;
                    modFilter = args[1].Trim();
                }
                else if (int.TryParse(args[1], out var p1) && p1 >= 1)
                {
                    modFilter = args[0].Trim();
                    page = p1;
                }
                else
                {
                    LogSystem.SendFeedback("Usage: /help [modName] [page].");
                    return;
                }
            }

            // Branch A: Show ALL mods (with headers and spacer rows).
            if (string.IsNullOrEmpty(modFilter))
            {
                // We create a flat sequence of rows with special sentinel rows:
                // - ("* Mod *", null)     => section header row.
                // - ("", null)            => spacer (blank line).
                // - (cmd, desc)           => actual command entries.
                var flat = new List<(string cmd, string desc)>();
                bool first = true;

                // Keep mod names in alpha order for predictable output.
                foreach (var mod in snap.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    // Insert a blank spacer *between* sections (but not before the first).
                    if (!first)
                        flat.Add(("", null)); // Spacer line (renders as empty line).
                    first = false;

                    // Section header: "* ModName *"
                    // We use desc==null to mark a non-command row.
                    flat.Add(($"* {mod} *", null));

                    // Append that mod's commands
                    foreach (var row in snap[mod])
                        flat.Add((row.Command, row.Description));
                }

                // Compute pagination.
                int totalPages = Math.Max(1, (int)Math.Ceiling(flat.Count / (double)pageSize));
                if (page > totalPages)
                {
                    LogSystem.SendFeedback($"Page out of range. Max is {totalPages}.");
                    return;
                }

                // Slice the requested page.
                int start = (page - 1) * pageSize;
                int count = Math.Min(pageSize, flat.Count - start);

                // Render header.
                LogSystem.SendFeedback($"== Help (all mods) page {page}/{totalPages} ==");

                // Render the rows on this page.
                for (int i = 0; i < count; i++)
                {
                    var (cmd, desc) = flat[start + i];

                    if (desc == null)
                    {
                        // Non-command row (header or spacer).
                        if (!string.IsNullOrEmpty(cmd))
                            LogSystem.SendFeedback(cmd); // Section header.
                        else
                            LogSystem.SendFeedback("");  // Spacer: print a blank line.
                    }
                    else
                    {
                        // Command row: "cmd - desc".
                        LogSystem.SendFeedback($"{cmd} - {desc}");
                    }
                }

                // Next-page hint.
                if (page < totalPages) LogSystem.SendFeedback($"Use /help {page + 1} for more.");
                return;
            }

            // Branch B: Show a SINGLE mod (exact or fuzzy match).

            // Exact match first; If none, allow partial case-insensitive match (first hit wins).
            var match = snap.Keys.FirstOrDefault(k => k.Equals(modFilter, StringComparison.OrdinalIgnoreCase))
                     ?? snap.Keys.FirstOrDefault(k => k.IndexOf(modFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            if (match == null)
            {
                LogSystem.SendFeedback($"No commands for '{modFilter}'.");
                return;
            }

            var list = snap[match]; // 'List<CommandInfo>' for that mod.

            // Per-mod pagination.
            int modPages = Math.Max(1, (int)Math.Ceiling(list.Count / (double)pageSize));
            if (page > modPages)
            {
                LogSystem.SendFeedback($"Page out of range for '{match}'. Max is {modPages}.");
                return;
            }

            int mstart = (page - 1) * pageSize;
            int mend = Math.Min(mstart + pageSize, list.Count);

            // Section header with total count + paging.
            LogSystem.SendFeedback($"* {match} * - {list.Count} command(s) page {page}/{modPages}");

            // Render the mod's commands for this page.
            for (int i = mstart; i < mend; i++)
                LogSystem.SendFeedback($"{list[i].Command} - {list[i].Description}");

            // Next-page hint (mod-filtered variant).
            if (page < modPages) LogSystem.SendFeedback($"Use /help {match} {page + 1} for more.");
        }

        #region Pagination Helpers

        /// <summary>
        /// Calculates how many help rows can fit on screen based on the current resolution.
        /// Reserves space for the page header and navigation hint.
        /// </summary>
        private static int GetHelpPageSize()
        {
            int screenHeight = Screen.Adjuster.ScreenRect.Height;

            // Rough visible line capacity:
            // 720p  -> 7.
            // 1080p -> 10.
            int totalVisibleRows = (int)Math.Floor(screenHeight / 100f);

            // Reserve lines for:
            // 1) page header.
            // 2) next-page hint.
            int reservedRows = 2;

            // Actual command rows allowed on the page.
            int pageSize = totalVisibleRows - reservedRows;

            // Safety clamp so extremes do not get silly.
            return Math.Max(4, Math.Min(12, pageSize));
        }
        #endregion

        #endregion

        #endregion
    }
}