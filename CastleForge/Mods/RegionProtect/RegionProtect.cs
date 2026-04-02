/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using DNA.Input;
using ModLoader;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace RegionProtect
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class RegionProtect : ModBase
    {
        /// <summary>
        /// Entrypoint for the RegionProtect mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public RegionProtect() : base("RegionProtect", new Version("0.0.1"))
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
            var ns    = typeof(RegionProtect).Namespace;
            var dest  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Load INI config + region DB.
            RPConfig.LoadApply();
            RegionProtectStore.LoadApply();

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
        /// Called once per game tick.
        /// </summary>
        public override void Tick(InputManager inputManager, GameTime gameTime)
        {
            if (RegionProtectCore.IsInGame())
                RegionProtectStore.EnsureLoadedForCurrentWorld();
        }
        #endregion

        /// <summary>
        /// This is the main command logic for the mod.
        /// </summary>
        #region Chat Command Functions

        #region Help Command List

        private static readonly (string command, string description)[] commands = new (string, string)[]
        {
            // General Commands.

            ("regionpos",         "Set selection corner: /regionpos 1|2"),
            ("regioncreate",      "Create region from selection: /regioncreate <name> [allowedPlayersCsv]"),
            ("regionedit",        "Edit region at your position: /regionedit add|remove <player> (or /regionedit <name> add|remove <player>)"),
            ("regiondelete",      "Delete region: /regiondelete <name>"),
            ("region",            "Show regions you are inside: /region"),

            ("spawnprotect",      "Toggle spawn protection: /spawnprotect on|off"),
            ("spawnprotectrange", "Set spawn protection range: /spawnprotectrange <blocks>"),
            ("spawnprotectedit",  "Edit spawn whitelist: /spawnprotectedit add|remove <player>"),

            ("regionprotect",     "Admin utilities: /regionprotect reload | /regionprotect save | /regionprotect list (page)"),
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /regionpos

        /// <summary>
        /// /regionpos 1
        /// /regionpos 2
        ///
        /// Sets selection corners using the local player's current position (floored to block coords).
        /// </summary>
        [Command("/regionpos")]
        [Command("/rpos")]
        private static void CmdRegionPos(string[] args)
        {
            if (args.Length != 1 || (args[0] != "1" && args[0] != "2"))
            {
                SendFeedback("Usage: /regionpos 1|2");
                return;
            }

            var p = CastleMinerZGame.Instance?.LocalPlayer;
            if (p == null || p.Gamer == null)
            {
                SendFeedback("ERROR: Local player not available.");
                return;
            }

            var hud = InGameHUD.Instance?.ConstructionProbe._worldIndex;
            var pos = RegionProtectCore.ToBlockPos(hud ?? p.LocalPosition);
            if (args[0] == "1")
            {
                RegionProtectCore.SelectionPos1 = pos;
                RegionProtectCore.HasSelectionPos1 = true;
                SendFeedback($"Pos1 set to {pos}.");
            }
            else
            {
                RegionProtectCore.SelectionPos2 = pos;
                RegionProtectCore.HasSelectionPos2 = true;
                SendFeedback($"Pos2 set to {pos}.");
            }
        }
        #endregion

        #region /regioncreate

        /// <summary>
        /// /regioncreate <name> [allowedPlayersCsv]
        /// </summary>
        [Command("/regioncreate")]
        [Command("/rcreate")]
        [Command("/radd")]
        private static void CmdRegionCreate(string[] args)
        {
            if (args.Length < 1)
            {
                SendFeedback("Usage: /regioncreate [name] (allowedPlayersCsv)");
                return;
            }

            if (!RegionProtectStore.EnsureLoadedForCurrentWorld())
            {
                SendFeedback("ERROR: World not loaded yet. Join/host a world first.");
                return;
            }

            if (!RegionProtectCore.HasSelectionPos1 || !RegionProtectCore.HasSelectionPos2)
            {
                SendFeedback("ERROR: You must set both corners first: /regionpos 1 and /regionpos 2");
                return;
            }

            string name = args[0].Trim();
            if (name.Length == 0)
            {
                SendFeedback("ERROR: Region name is empty.");
                return;
            }

            string allowedCsv = (args.Length >= 2) ? string.Join(" ", args, 1, args.Length - 1) : "";
            var allowed = RegionProtectCore.ParsePlayerCsv(allowedCsv);

            var min = RegionProtectCore.Min(RegionProtectCore.SelectionPos1, RegionProtectCore.SelectionPos2);
            var max = RegionProtectCore.Max(RegionProtectCore.SelectionPos1, RegionProtectCore.SelectionPos2);

            if (!RegionProtectStore.UpsertRegion(name, min, max, allowed))
            {
                SendFeedback($"ERROR: Region '{name}' already exists.");
                return;
            }

            RegionProtectStore.Save();
            SendFeedback($"Region '{name}' created. Bounds: {min} .. {max}. Allowed: {RegionProtectCore.FormatPlayers(allowed)}.");
        }
        #endregion

        #region /regionedit

        /// <summary>
        /// /regionedit add|remove <player>
        /// /regionedit <name> add|remove <player>
        /// </summary>
        [Command("/regionedit")]
        [Command("/redit")]
        private static void CmdRegionEdit(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("Usage: /regionedit add|remove [player] (or /regionedit [name] add|remove [player])");
                return;
            }

            if (!RegionProtectStore.EnsureLoadedForCurrentWorld())
            {
                SendFeedback("ERROR: World not loaded yet. Join/host a world first.");
                return;
            }

            string regionName;
            string verb;
            string player;

            // Support both:
            // 1) /regionedit add|remove player
            // 2) /regionedit regionName add|remove player
            if (RegionProtectStore.RegionExists(args[0]))
            {
                if (args.Length < 3)
                {
                    SendFeedback("Usage: /regionedit [name] add|remove [player]");
                    return;
                }

                regionName = args[0];
                verb = args[1];
                player = string.Join(" ", args, 2, args.Length - 2);
            }
            else
            {
                verb = args[0];
                player = string.Join(" ", args, 1, args.Length - 1);

                var p = CastleMinerZGame.Instance?.LocalPlayer;
                if (p == null)
                {
                    SendFeedback("ERROR: Local player not available.");
                    return;
                }

                var here = RegionProtectCore.ToBlockPos(p.LocalPosition);
                var r = RegionProtectStore.FindFirstRegionAt(here);
                if (r == null)
                {
                    SendFeedback("ERROR: You are not inside any region.");
                    return;
                }

                regionName = r.Name;
            }

            if (string.IsNullOrWhiteSpace(player))
            {
                SendFeedback("ERROR: Player is empty.");
                return;
            }

            bool add;
            if (verb.Equals("add", StringComparison.OrdinalIgnoreCase)) add = true;
            else if (verb.Equals("remove", StringComparison.OrdinalIgnoreCase) || verb.Equals("del", StringComparison.OrdinalIgnoreCase)) add = false;
            else
            {
                SendFeedback("Usage: /regionedit add|remove <player>");
                return;
            }

            if (!RegionProtectStore.TryEditRegion(regionName, add, player))
            {
                SendFeedback($"ERROR: Region '{regionName}' not found.");
                return;
            }

            RegionProtectStore.Save();
            SendFeedback($"Region '{regionName}': {(add ? "added" : "removed")} '{player}'.");
        }
        #endregion

        #region /regiondelete

        /// <summary>
        /// /regiondelete <name>;
        /// </summary>
        [Command("/regiondelete")]
        [Command("/rdel")]
        private static void CmdRegionDelete(string[] args)
        {
            if (args.Length != 1)
            {
                SendFeedback("Usage: /regiondelete [name]");
                return;
            }

            if (!RegionProtectStore.EnsureLoadedForCurrentWorld())
            {
                SendFeedback("ERROR: World not loaded yet. Join/host a world first.");
                return;
            }

            string name = args[0];
            if (!RegionProtectStore.RemoveRegion(name))
            {
                SendFeedback($"ERROR: Region '{name}' not found.");
                return;
            }

            RegionProtectStore.Save();
            SendFeedback($"Region '{name}' deleted.");
        }
        #endregion

        #region /region

        /// <summary>
        /// /region
        /// Shows which regions contain your current block position.
        /// </summary>
        [Command("/region")]
        [Command("/rg")]
        private static void CmdRegionHere()
        {
            var p = CastleMinerZGame.Instance?.LocalPlayer;
            if (p == null || p.Gamer == null)
            {
                SendFeedback("ERROR: Local player not available.");
                return;
            }

            var here = RegionProtectCore.ToBlockPos(p.LocalPosition);
            var list = RegionProtectStore.FindRegionsAt(here);

            if (list.Count == 0)
            {
                SendFeedback("You are not inside any protected region.");
                return;
            }

            // Display per-region permission.
            string you = p.Gamer.Gamertag;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                bool allowed = r.IsPlayerAllowed(you);
                SendFeedback($"Inside '{r.Name}' (Allowed={allowed}).");
            }
        }
        #endregion

        #region /spawnprotect

        /// <summary>
        /// /spawnprotect on|off
        /// </summary>
        [Command("/spawnprotect")]
        [Command("/sprot")]
        private static void CmdSpawnProtect(string[] args)
        {
            if (args.Length != 1)
            {
                SendFeedback("Usage: /spawnprotect on|off");
                return;
            }

            bool enabled;
            if (args[0].Equals("on", StringComparison.OrdinalIgnoreCase) || args[0].Equals("true", StringComparison.OrdinalIgnoreCase)) enabled = true;
            else if (args[0].Equals("off", StringComparison.OrdinalIgnoreCase) || args[0].Equals("false", StringComparison.OrdinalIgnoreCase)) enabled = false;
            else
            {
                SendFeedback("Usage: /spawnprotect on|off");
                return;
            }

            RegionProtectStore.Spawn.Enabled = enabled;
            RegionProtectStore.Save();
            SendFeedback($"Spawn protection: {(enabled ? "ON" : "OFF")} (Range={RegionProtectStore.Spawn.Range}).");
        }
        #endregion

        #region /spawnprotectrange

        /// <summary>
        /// /spawnprotectrange <blocks>
        /// </summary>
        [Command("/spawnprotectrange")]
        [Command("/sprange")]
        [Command("/spr")]
        private static void CmdSpawnProtectRange(string[] args)
        {
            if (args.Length != 1 || !int.TryParse(args[0], out int range))
            {
                SendFeedback("Usage: /spawnprotectrange [blocks]");
                return;
            }

            range = Math.Max(0, Math.Min(512, range));
            RegionProtectStore.Spawn.Range = range;
            RegionProtectStore.Save();
            SendFeedback($"Spawn protection range set to {range}.");
        }
        #endregion

        #region /spawnprotectedit

        /// <summary>
        /// /spawnprotectedit add|remove <player>
        /// </summary>
        [Command("/spawnprotectedit")]
        [Command("/spe")]
        private static void CmdSpawnProtectEdit(string[] args)
        {
            if (args.Length < 2)
            {
                SendFeedback("Usage: /spawnprotectedit add|remove [player]");
                return;
            }

            string verb = args[0];
            string player = string.Join(" ", args, 1, args.Length - 1);

            bool add;
            if (verb.Equals("add", StringComparison.OrdinalIgnoreCase)) add = true;
            else if (verb.Equals("remove", StringComparison.OrdinalIgnoreCase) || verb.Equals("del", StringComparison.OrdinalIgnoreCase)) add = false;
            else
            {
                SendFeedback("Usage: /spawnprotectedit add|remove [player]");
                return;
            }

            if (string.IsNullOrWhiteSpace(player))
            {
                SendFeedback("ERROR: Player is empty.");
                return;
            }

            if (add) RegionProtectStore.Spawn.AllowedPlayers.Add(RegionProtectCore.NormalizePlayer(player));
            else RegionProtectStore.Spawn.AllowedPlayers.Remove(RegionProtectCore.NormalizePlayer(player));

            RegionProtectStore.Save();
            SendFeedback($"Spawn whitelist: {(add ? "added" : "removed")} '{player}'.");
        }
        #endregion

        #region /regionprotect

        /// <summary>
        /// /regionprotect reload
        /// /regionprotect save
        /// /regionprotect list (page)
        /// </summary>
        [Command("/regionprotect")]
        [Command("/rprot")]
        [Command("/rp")]
        private static void CmdRegionProtectAdmin(string[] args)
        {
            if (args.Length < 1)
            {
                SendFeedback("Usage: /regionprotect reload | save | list (page)");
                return;
            }

            string verb = args[0].ToLowerInvariant();

            if (verb == "reload")
            {
                RPConfig.LoadApply();
                RegionProtectStore.LoadApply();
                SendFeedback("RegionProtect: reloaded config + region database.");
                return;
            }

            if (verb == "save")
            {
                RegionProtectStore.Save();
                SendFeedback("RegionProtect: saved region database.");
                return;
            }

            if (verb == "list")
            {
                if (!RegionProtectStore.EnsureLoadedForCurrentWorld())
                {
                    SendFeedback("ERROR: World not loaded yet. Join/host a world first.");
                    return;
                }

                int page = 1;
                if (args.Length >= 2) int.TryParse(args[1], out page);
                if (page < 1) page = 1;

                const int perPage = 10;

                var lines = RegionProtectStore.GetRegionSummaries();
                if (lines.Count == 0)
                {
                    SendFeedback("No regions defined for this world.");
                    return;
                }

                int pages = (lines.Count + perPage - 1) / perPage;
                if (page > pages) page = pages;

                int start = (page - 1) * perPage;
                int end = Math.Min(start + perPage, lines.Count);

                SendFeedback($"Regions: {lines.Count} (page {page}/{pages}).");
                for (int i = start; i < end; i++)
                    SendFeedback(" - " + lines[i]);

                if (page < pages)
                    SendFeedback($"Next: /regionprotect list {page + 1}.");

                return;
            }

            SendFeedback("Usage: /regionprotect reload | save | list (page)");
        }
        #endregion

        #endregion

        #endregion
    }
}