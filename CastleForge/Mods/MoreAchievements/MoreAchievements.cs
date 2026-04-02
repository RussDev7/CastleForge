/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using ModLoaderExt;
using System.Linq;
using DNA.Input;
using ModLoader;
using System.IO;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace MoreAchievements
{
    [Priority(Priority.Normal)]
    [RequiredDependencies("ModLoaderExtensions")]
    public class MoreAchievements : ModBase
    {
        /// <summary>
        /// Entrypoint for the Example mod: Sets up command dispatching, patches, and world lookup.
        /// </summary>
        #region Mod Initiation

        private readonly CommandDispatcher _dispatcher; // Dispatcher that routes incoming "/commands" to attributed methods.
        // private object                  _world;      // Holds the reference to the game's world object once it becomes available.

        // Mod constructor: Invoked by the ModLoader when instantiating your mod.
        public MoreAchievements() : base("MoreAchievements", new Version("0.0.1"))
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
            var ns = typeof(MoreAchievements).Namespace;
            var dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            var wrote = EmbeddedExporter.ExtractFolder(ns, dest);
            if (wrote > 0) Log($"Extracted {wrote} file(s) to {dest}.");

            // Apply game patches.
            GamePatches.ApplyAllPatches();

            // Load or create config.
            MAConfig.LoadApply();

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
            // General commands.
            ("achievement",   "Available: grant|revoke all[steam|custom|easy|normal|hard|brutal|insane]|<id>|<apiName>, stats, list."),

            // Statistic commands.
            ("mineallblocks", "Available: list|remaining."),
            ("craftallitems", "Available: list|remaining."),
            ("holdallitems",  "Available: list|remaining."),
        };
        #endregion

        #region Command Functions

        // General Commands.

        #region /achievement

        /// <summary>
        /// Implements the logic for:
        ///   /achievement grant all
        ///   /achievement grant <idOrName>
        ///   /achievement revoke all
        ///   /achievement revoke <idOrName>
        ///   /achievement stats
        /// without touching game source.
        /// </summary>
        [Command("/achievement")]
        [Command("/ach")]
        private static void ExecuteAchievement(string[] args)
        {
            try
            {
                var game = CastleMinerZGame.Instance;

                if (game == null || !(game?.AcheivmentManager is AchievementManager<CastleMinerZPlayerStats> mgr))
                {
                    SendFeedback("/achievement: Game or achievement manager not ready.");
                    return;
                }

                if (args == null || args.Length == 0)
                {
                    SendFeedback("Usage: /achievement grant|revoke all[steam|custom|easy|normal|hard|brutal|insane]|<id>|<apiName>, stats, list");
                    return;
                }

                var verb = args[0].ToLowerInvariant();

                switch (verb)
                {
                    case "grant":
                        if (args.Length >= 2)
                        {
                            // /achievement grant all [...]
                            if (string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
                            {
                                // Optional scope:
                                //   /achievement grant all
                                //   /achievement grant all steam
                                //   /achievement grant all custom
                                //   /achievement grant all easy|normal|hard|brutal|insane
                                string scope = (args.Length >= 3) ? args[2] : null;

                                if (string.IsNullOrWhiteSpace(scope))
                                {
                                    AchievementResetHelper.GrantAll(mgr);
                                }
                                else
                                {
                                    scope = scope.Trim().ToLowerInvariant();

                                    if (scope == "steam" || scope == "vanilla")
                                    {
                                        AchievementResetHelper.GrantAllSteam(mgr);
                                    }
                                    else if (scope == "custom")
                                    {
                                        AchievementResetHelper.GrantAllCustom(mgr);
                                    }
                                    else if (AchievementResetHelper.TryParseDifficultyToken(scope, out var diff))
                                    {
                                        AchievementResetHelper.GrantAllByDifficulty(mgr, diff);
                                    }
                                    else
                                    {
                                        SendFeedback("Usage: /achievement grant all [steam|custom|vanilla|easy|normal|hard|brutal|insane].");
                                        return;
                                    }
                                }
                            }
                            // Shortcut: /achievement grant easy|normal|... (alias for "grant all <difficulty>")
                            else if (AchievementResetHelper.TryParseDifficultyToken(args[1], out var diffSingle))
                            {
                                AchievementResetHelper.GrantAllByDifficulty(mgr, diffSingle);
                            }
                            else
                            {
                                // /ach grant <idOrName>.
                                var token = args[1];

                                if (!AchievementResetHelper.TryResolveAchievement(
                                        mgr,
                                        token,
                                        out var ach,
                                        out var index))
                                {
                                    SendFeedback($"Grant: Unknown achievement '{token}'. Use index 0-{mgr.Count - 1} or API name.");
                                    return;
                                }

                                // Single achievement by ID/API name.
                                AchievementResetHelper.GrantOne(mgr, index.ToString());
                            }
                        }
                        else
                        {
                            SendFeedback("Usage: /achievement grant all[steam|custom|easy|normal|hard|brutal|insane]|<id>|<apiName>.");
                        }
                        break;

                    case "revoke":
                        if (args.Length >= 2)
                        {
                            // /achievement revoke all [...]
                            if (string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
                            {
                                // Optional scope:
                                //   /achievement revoke all
                                //   /achievement revoke all steam
                                //   /achievement revoke all custom
                                //   /achievement revoke all easy|normal|hard|brutal|insane
                                string scope = (args.Length >= 3) ? args[2] : null;

                                if (string.IsNullOrWhiteSpace(scope))
                                {
                                    AchievementResetHelper.RevokeAll(mgr);
                                }
                                else
                                {
                                    scope = scope.Trim().ToLowerInvariant();

                                    if (scope == "steam" || scope == "vanilla")
                                    {
                                        AchievementResetHelper.RevokeAllSteam(mgr);
                                    }
                                    else if (scope == "custom")
                                    {
                                        AchievementResetHelper.RevokeAllCustom(mgr);
                                    }
                                    else if (AchievementResetHelper.TryParseDifficultyToken(scope, out var diff))
                                    {
                                        AchievementResetHelper.RevokeAllByDifficulty(mgr, diff);
                                    }
                                    else
                                    {
                                        SendFeedback("Usage: /achievement revoke all [steam|custom|vanilla|easy|normal|hard|brutal|insane].");
                                        return;
                                    }
                                }
                            }
                            // Shortcut: /achievement revoke easy|normal|... (alias for "revoke all <difficulty>")
                            else if (AchievementResetHelper.TryParseDifficultyToken(args[1], out var diffSingle))
                            {
                                AchievementResetHelper.RevokeAllByDifficulty(mgr, diffSingle);
                            }
                            else
                            {
                                // /ach revoke <idOrName>.
                                var token = args[1];

                                if (!AchievementResetHelper.TryResolveAchievement(
                                        mgr,
                                        token,
                                        out var ach,
                                        out var index))
                                {
                                    SendFeedback($"Revoke: Unknown achievement '{token}'. Use index 0-{mgr.Count - 1} or API name.");
                                    return;
                                }

                                // Single achievement by ID/API name.
                                AchievementResetHelper.RevokeOne(mgr, index.ToString());
                            }
                        }
                        else
                        {
                            SendFeedback("Usage: /achievement revoke all[steam|custom|easy|normal|hard|brutal|insane]|<id>|<apiName>.");
                        }
                        break;

                    case "stats":
                        AchievementResetHelper.PrintStats(mgr);
                        break;

                    case "list":
                        AchievementResetHelper.PrintAPIList(mgr);
                        break;

                    default:
                        SendFeedback("Usage: /achievement grant all[steam|custom]|<id>|<apiName> | revoke all[steam|custom]|<id>|<apiName> | stats.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /mineallblocks

        [Command("/mineallblocks")]
        [Command("/mab")]
        private static void ExecuteMineAllBlocks(string[] args)
        {
            try
            {
                var game  = CastleMinerZGame.Instance;
                var stats = game?.PlayerStats;

                if (game == null || !(game?.AcheivmentManager is AchievementManager<CastleMinerZPlayerStats> mgr))
                {
                    SendFeedback("/mineallblocks: Game or achievement manager not ready.");
                    return;
                }

                if (args == null || args.Length == 0)
                {
                    SendFeedback("Usage: /mineallblocks list|remaining.");
                    return;
                }

                var verb = args[0].ToLowerInvariant();

                switch (verb)
                {
                    case "list":
                        var result = new List<BlockTypeEnum>();
                        foreach (BlockTypeEnum value in Enum.GetValues(typeof(BlockTypeEnum)))
                        {
                            if (MasterProspectorAchievement.IsMinableBlockType(value))
                                result.Add(value);
                        }
                        SendFeedback($"MineEveryBlock: Minable block types: {string.Join(", ", result.Select(b => b.ToString()))}.");
                        break;

                    case "remaining":
                        var remainingBlocks = new List<BlockTypeEnum>();
                        foreach (var type in MasterProspectorAchievement._minableBlockTypes)
                        {
                            int current = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, type);
                            if (current <= 0)
                            {
                                remainingBlocks.Add(type);
                            }
                        }
                        SendFeedback($"MineEveryBlock: Remaining blocks: {string.Join(", ", remainingBlocks.Select(b => b.ToString()))}.");
                        break;

                    default:
                        SendFeedback("Usage: /mineallblocks list|remaining.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /craftallitems

        [Command("/craftallitems")]
        [Command("/cai")]
        private static void ExecuteCraftAllItems(string[] args)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                var stats = game?.PlayerStats;

                if (game == null || !(game?.AcheivmentManager is AchievementManager<CastleMinerZPlayerStats> mgr))
                {
                    SendFeedback("/craftallitems: Game or achievement manager not ready.");
                    return;
                }

                if (args == null || args.Length == 0)
                {
                    SendFeedback("Usage: /craftallitems list|remaining.");
                    return;
                }

                var verb = args[0].ToLowerInvariant();

                switch (verb)
                {
                    case "list":
                        var result = new List<InventoryItemIDs>();
                        foreach (var itemID in MasterCrafterAchievement._craftableItemIDs)
                        {
                            var s = stats.GetItemStats(itemID);
                            if (s != null)
                            {
                                result.Add(itemID);
                            }
                        }
                        SendFeedback($"CraftEveryItem: Craftable item types: {string.Join(", ", result.Select(b => b.ToString()))}.");
                        break;

                    case "remaining":
                        var remainingItems = new List<InventoryItemIDs>();
                        foreach (var itemID in MasterCrafterAchievement._craftableItemIDs)
                        {
                            var s = stats.GetItemStats(itemID);
                            if (s != null && s.Crafted < 1)
                            {
                                remainingItems.Add(itemID);
                            }
                        }
                        SendFeedback($"CraftEveryItem: Remaining items: {string.Join(", ", remainingItems.Select(b => b.ToString()))}.");
                        break;

                    default:
                        SendFeedback("Usage: /craftallitems list|remaining.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #region /holdallitems

        [Command("/holdallitems")]
        [Command("/hai")]
        private static void ExecuteHoldAllItems(string[] args)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                var stats = game?.PlayerStats;

                if (game == null || !(game?.AcheivmentManager is AchievementManager<CastleMinerZPlayerStats> mgr))
                {
                    SendFeedback("/holdallitems: Game or achievement manager not ready.");
                    return;
                }

                if (args == null || args.Length == 0)
                {
                    SendFeedback("Usage: /holdallitems list|remaining.");
                    return;
                }

                var verb = args[0].ToLowerInvariant();

                // Must match HandsOnEverythingAchievement's per-item requirement.
                const double RequiredMinutesPerItem = MasterOfAllThingsAchievement.RequiredMinutesPerItem;

                // Shared craftable item list from MasterCrafterAchievement.
                var craftable = MasterCrafterAchievement._craftableItemIDs;
                if (craftable == null || craftable.Length == 0)
                {
                    SendFeedback("HoldEveryItem: No craftable item IDs discovered.");
                    return;
                }

                switch (verb)
                {
                    case "list":
                        {
                            // Just list all craftable item IDs.
                            var result = new List<InventoryItemIDs>();

                            foreach (var itemID in craftable)
                            {
                                var s = stats.GetItemStats(itemID);
                                if (s != null)
                                {
                                    result.Add(itemID);
                                }
                            }

                            SendFeedback(
                                $"HoldEveryItem: Craftable item types: {string.Join(", ", result.Select(i => i.ToString()))}.");
                            break;
                        }

                    case "remaining":
                        {
                            var remainingItems = new List<InventoryItemIDs>();
                            int total = craftable.Length;
                            int have = 0;

                            foreach (var itemID in craftable)
                            {
                                var s = stats.GetItemStats(itemID);
                                if (s == null)
                                    continue;

                                double mins = s.TimeHeld.TotalMinutes;
                                if (mins >= RequiredMinutesPerItem)
                                {
                                    have++;
                                }
                                else
                                {
                                    remainingItems.Add(itemID);
                                }
                            }

                            if (remainingItems.Count == 0)
                            {
                                SendFeedback(
                                    $"HoldEveryItem: All craftable items have been held for at least {RequiredMinutesPerItem:0} minutes each. ({have}/{total})");
                            }
                            else
                            {
                                SendFeedback(
                                    $"HoldEveryItem: Remaining items (<{RequiredMinutesPerItem:0} min held): {string.Join(", ", remainingItems.Select(i => i.ToString()))}.");
                                SendFeedback(
                                    $"HoldEveryItem: Progress - {have}/{total} craftable items have been held for at least {RequiredMinutesPerItem:0} minutes.");
                            }

                            break;
                        }

                    default:
                        SendFeedback("Usage: /holdallitems list|remaining.");
                        break;
                }
            }
            catch (Exception ex)
            {
                SendFeedback($"ERROR: {ex.Message}.");
            }
        }
        #endregion

        #endregion

        #endregion
    }
}