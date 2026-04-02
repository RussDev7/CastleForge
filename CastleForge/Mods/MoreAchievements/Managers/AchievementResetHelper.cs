/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System.Reflection;
using DNA.CastleMinerZ;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace MoreAchievements
{
    /// <summary>
    /// Centralized helpers for bulk achievement operations (grant all / revoke all).
    /// Handles:
    ///   • Syncing CastleMinerZPlayerStats <-> SteamWorks stats layer
    ///   • Clearing the private Achievement._acheived flag
    ///   • Optional Steam achievement toggling (SetAchievement / ClearAchievement)
    /// </summary>
    internal static class AchievementResetHelper
    {
        #region Reflection Caches

        /// <summary>
        /// Private field on CastleMinerZPlayerStats:
        ///   private Dictionary<InventoryItemIDs, ItemStats> AllItemStats;
        /// </summary>
        public static readonly FieldInfo s_allItemStatsField =
            typeof(CastleMinerZPlayerStats)
                .GetField("AllItemStats", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Private field on AchievementManager<T>.Achievement:
        ///   protected bool _acheived;
        /// </summary>
        public static readonly FieldInfo s_achievedFlagField =
            typeof(AchievementManager<CastleMinerZPlayerStats>.Achievement)
                .GetField("_acheived", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Strongly-typed accessor for the private AchievementManager<T>._stats
        /// field on the vanilla CastleMinerZ achievement manager.
        /// </summary>
        private static readonly HarmonyLib.AccessTools.FieldRef<AchievementManager<CastleMinerZPlayerStats>, CastleMinerZPlayerStats> s_mgrStatsRef =
            HarmonyLib.AccessTools.FieldRefAccess<AchievementManager<CastleMinerZPlayerStats>, CastleMinerZPlayerStats>("_stats");

        #endregion

        #region Per-Achievement Stat Presets

        /// <summary>
        /// Maps achievement API names to a small lambda that adjusts the
        /// local CastleMinerZPlayerStats when that achievement is granted
        /// or revoked.
        ///
        /// grant == true  -> bump stats up to at least the required threshold.
        /// grant == false -> lower or clear the relevant stats.
        ///
        /// You can extend this dictionary with your own custom achievements.
        /// </summary>
        private static readonly Dictionary<string, Action<CastleMinerZPlayerStats, bool>> s_perAchievementLocalStatAdjust
        = new Dictionary<string, Action<CastleMinerZPlayerStats, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            #region Vanilla Achievements

            #region TIME PLAYED ACHIEVEMENTS

            // 1h, 10h, 100h
            ["ACH_TIME_PLAYED_1"] = (stats, grant) =>
            {
                if (grant)
                    stats.TimeOnline = TimeSpan.FromHours(Math.Max(stats.TimeOnline.TotalHours, 1));
                else
                    stats.TimeOnline = TimeSpan.Zero;
            },

            ["ACH_TIME_PLAYED_10"] = (stats, grant) =>
            {
                if (grant)
                    stats.TimeOnline = TimeSpan.FromHours(Math.Max(stats.TimeOnline.TotalHours, 10));
                else
                    stats.TimeOnline = s_bulkRevokeAll ? TimeSpan.Zero : TimeSpan.FromHours(1); // Keep enough for ACH_TIME_PLAYED_1.
            },

            ["ACH_TIME_PLAYED_100"] = (stats, grant) =>
            {
                if (grant)
                    stats.TimeOnline = TimeSpan.FromHours(Math.Max(stats.TimeOnline.TotalHours, 100));
                else
                    stats.TimeOnline = s_bulkRevokeAll ? TimeSpan.Zero : TimeSpan.FromHours(10); // Keep enough for ACH_TIME_PLAYED_10.
            },
            #endregion

            #region KILLS ACHIEVEMENTS

            // Self Defense / No Fear / Zombie Slayer
            ["ACH_TOTAL_KILLS_1"] = (stats, grant) =>
            {
                if (grant)
                    stats.TotalKills = Math.Max(stats.TotalKills, 1);
                else
                    stats.TotalKills = 0;
            },

            ["ACH_TOTAL_KILLS_100"] = (stats, grant) =>
            {
                if (grant)
                    stats.TotalKills = Math.Max(stats.TotalKills, 100);
                else
                    stats.TotalKills = s_bulkRevokeAll ? 0 : Math.Min(stats.TotalKills, 99);
            },

            ["ACH_TOTAL_KILLS_1000"] = (stats, grant) =>
            {
                if (grant)
                    stats.TotalKills = Math.Max(stats.TotalKills, 1000);
                else
                    stats.TotalKills = s_bulkRevokeAll ? 0 : Math.Min(stats.TotalKills, 999);
            },
            #endregion

            #region DAYS SURVIVED ACHIEVEMENTS

            ["ACH_DAYS_1"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 1);
                else
                    stats.MaxDaysSurvived = 0;
            },

            ["ACH_DAYS_7"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 7);
                else
                    stats.MaxDaysSurvived = s_bulkRevokeAll ? 0 : Math.Min(stats.MaxDaysSurvived, 1); // Keep enough for ACH_DAYS_1.
            },

            ["ACH_DAYS_28"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 28);
                else
                    stats.MaxDaysSurvived = s_bulkRevokeAll ? 0 : Math.Min(stats.MaxDaysSurvived, 7); // Keep enough for ACH_DAYS_7.
            },

            ["ACH_DAYS_100"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 100);
                else
                    stats.MaxDaysSurvived = s_bulkRevokeAll ? 0 : Math.Min(stats.MaxDaysSurvived, 28); // Keep enough for ACH_DAYS_28.
            },

            ["ACH_DAYS_196"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 196);
                else
                    stats.MaxDaysSurvived = s_bulkRevokeAll ? 0 : Math.Min(stats.MaxDaysSurvived, 100); // Keep enough for ACH_DAYS_100.
            },

            ["ACH_DAYS_365"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 365);
                else
                    stats.MaxDaysSurvived = s_bulkRevokeAll ? 0 : Math.Min(stats.MaxDaysSurvived, 196); // Keep enough for ACH_DAYS_196.
            },
            #endregion

            #region DISTANCE ACHIEVEMENTS

            // (These mirror your "GrantAll" presets: 5k covers all of them.)
            ["ACH_DISTANCE_50"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 50f);
                else
                    stats.MaxDistanceTraveled = 0f;
            },

            ["ACH_DISTANCE_200"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 200f);
                else
                    stats.MaxDistanceTraveled = s_bulkRevokeAll ? 0f : Math.Min(stats.MaxDistanceTraveled, 199f);
            },

            ["ACH_DISTANCE_1000"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 1000f);
                else
                    stats.MaxDistanceTraveled = s_bulkRevokeAll ? 0f : Math.Min(stats.MaxDistanceTraveled, 999f);
            },

            ["ACH_DISTANCE_2300"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 2300f);
                else
                    stats.MaxDistanceTraveled = s_bulkRevokeAll ? 0f : Math.Min(stats.MaxDistanceTraveled, 2299f);
            },

            ["ACH_DISTANCE_3000"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 3000f);
                else
                    stats.MaxDistanceTraveled = s_bulkRevokeAll ? 0f : Math.Min(stats.MaxDistanceTraveled, 2999f);
            },

            ["ACH_DISTANCE_3600"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 3600f);
                else
                    stats.MaxDistanceTraveled = s_bulkRevokeAll ? 0f : Math.Min(stats.MaxDistanceTraveled, 3599f);
            },

            ["ACH_DISTANCE_5000"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, 5000f);
                else
                    stats.MaxDistanceTraveled = s_bulkRevokeAll ? 0f : Math.Min(stats.MaxDistanceTraveled, 4999f);
            },
            #endregion

            #region DEPTH ACHIEVEMENTS

            // Game uses positive MaxDepth; achievements use negative world depth.
            ["ACH_DEPTH_20"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDepth = Math.Min(stats.MaxDepth, -20f);
                else
                    stats.MaxDepth = 0f;
            },

            ["ACH_DEPTH_40"] = (stats, grant) =>
            {
                if (grant)
                    stats.MaxDepth = Math.Min(stats.MaxDepth, -40f);
                else
                {
                    // stats.MaxDepth -= s_bulkRevokeAll ? 0f : Math.Max(stats.MaxDepth, -39f);

                    // Drop to zero. CMZ stats does not do well to subtract depth.
                    stats.MaxDepth = 0f;
                }
            },
            #endregion

            #region CRAFTED ACHIEVEMENTS

            ["ACH_CRAFTED_1"] = (stats, grant) =>
            {
                if (grant)
                    stats.TotalItemsCrafted = Math.Max(stats.TotalItemsCrafted, 1);
                else
                    stats.TotalItemsCrafted = 0;
            },

            ["ACH_CRAFTED_100"] = (stats, grant) =>
            {
                if (grant)
                    stats.TotalItemsCrafted = Math.Max(stats.TotalItemsCrafted, 100);
                else
                    stats.TotalItemsCrafted = s_bulkRevokeAll ? 0 : Math.Min(stats.TotalItemsCrafted, 99);
            },

            ["ACH_CRAFTED_1000"] = (stats, grant) =>
            {
                if (grant)
                    stats.TotalItemsCrafted = Math.Max(stats.TotalItemsCrafted, 1000);
                else
                    stats.TotalItemsCrafted = s_bulkRevokeAll ? 0 : Math.Min(stats.TotalItemsCrafted, 999);
            },
            #endregion

            #region DRAGONS, ALIENS, LASERS, TNT, GRENADES

            ["ACH_UNDEAD_DRAGON_KILLED"] = (stats, grant) =>
            {
                stats.UndeadDragonKills = grant
                    ? Math.Max(stats.UndeadDragonKills, 1)
                    : 0;
            },

            ["ACH_ALIEN_ENCOUNTER"] = (stats, grant) =>
            {
                stats.AlienEncounters = grant
                    ? Math.Max(stats.AlienEncounters, 1)
                    : 0;
            },

            ["ACH_LASER_KILLS"] = (stats, grant) =>
            {
                stats.EnemiesKilledWithLaserWeapon = grant
                    ? Math.Max(stats.EnemiesKilledWithLaserWeapon, 1)
                    : 0;
            },

            ["ACH_CRAFT_TNT"] = (stats, grant) =>
            {
                if (grant)
                {
                    stats.TotalItemsCrafted = Math.Max(stats.TotalItemsCrafted, 1);

                    var tntStats = stats.GetItemStats(InventoryItemIDs.TNT);
                    if (tntStats != null)
                        tntStats.Crafted = Math.Max(tntStats.Crafted, 1);
                }
                else
                {
                    // "Undo" TNT craft stats.
                    var tntStats = stats.GetItemStats(InventoryItemIDs.TNT);
                    if (tntStats != null)
                        tntStats.Crafted = 0;
                }
            },

            ["ACH_GUIDED_MISSILE_KILL"] = (stats, grant) =>
            {
                stats.DragonsKilledWithGuidedMissile = grant
                    ? Math.Max(stats.DragonsKilledWithGuidedMissile, 1)
                    : 0;
            },

            ["ACH_TNT_KILL"] = (stats, grant) =>
            {
                stats.EnemiesKilledWithTNT = grant
                    ? Math.Max(stats.EnemiesKilledWithTNT, 1)
                    : 0;
            },

            ["ACH_GRENADE_KILL"] = (stats, grant) =>
            {
                stats.EnemiesKilledWithGrenade = grant
                    ? Math.Max(stats.EnemiesKilledWithGrenade, 1)
                    : 0;
            },
            #endregion

            #endregion

            #region Custom Achievements

            /// <summary>
            /// Handeled per mod via <see cref="ILocalStatAdjust"/>.
            /// </summary>

            #endregion
        };

        /// <summary>
        /// Flag indicating a bulk revoke-all operation is in progress.
        /// </summary>
        private static bool s_bulkRevokeAll = false;

        /// <summary>
        /// Apply the local stat changes for a single achievement (if we have a preset).
        /// Priority:
        ///   1) Achievement implements ILocalStatAdjust (custom achievements).
        ///   2) Fallback to vanilla mapping in s_perAchievementLocalStatAdjust.
        /// Safe no-op if neither applies.
        /// </summary>
        private static void ApplyLocalStatPresetForAchievement(
            CastleMinerZPlayerStats stats,
            AchievementManager<CastleMinerZPlayerStats>.Achievement ach,
            bool grant)
        {
            if (stats == null || ach == null)
                return;

            // 1) Custom achievements that know how to adjust their own stats.
            if (ach is ILocalStatAdjust customAdjust)
            {
                customAdjust.AdjustLocalStats(stats, grant);
                return;
            }

            // 2) Vanilla (or custom) achievements that use the central lookup table.
            if (!string.IsNullOrEmpty(ach.APIName) &&
                s_perAchievementLocalStatAdjust.TryGetValue(ach.APIName, out var adjust))
            {
                adjust(stats, grant);
            }
        }
        #endregion

        #region Internal Helpers

        /// <summary>
        /// Gets the live CastleMinerZPlayerStats instance being used by the game.
        /// This is the same object passed into CastleMinerZAchievementManager.
        /// </summary>
        private static CastleMinerZPlayerStats GetStats()
        {
            return CastleMinerZGame.Instance?.PlayerStats as CastleMinerZPlayerStats;
        }

        /// <summary>
        /// Treat everything in the MoreAchievements namespace as "custom" for filtering.
        /// </summary>
        private static bool IsCustomAchievement(AchievementManager<CastleMinerZPlayerStats>.Achievement achievement)
        {
            if (achievement == null)
                return false;

            var ns = achievement.GetType().Namespace;
            return string.Equals(ns, "MoreAchievements", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Finds an achievement by API name or display name; returns null if not found.
        /// </summary>
        public static AchievementManager<CastleMinerZPlayerStats>.Achievement GetSteamAchievementByName(string apiname)
        {
            var mgr = CastleMinerZGame.Instance.AcheivmentManager;
            if (mgr == null) return null;

            for (int i = 0; i < mgr.Count; i++)
            {
                var ach = mgr[i];
                if (ach == null) continue;

                if (string.Equals(ach.APIName, apiname, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ach.Name, apiname, StringComparison.OrdinalIgnoreCase))
                {
                    return mgr[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the CastleMinerZPlayerStats instance backing the given
        /// AchievementManager<CastleMinerZPlayerStats>.
        /// </summary>
        public static CastleMinerZPlayerStats GetManagerStats(AchievementManager<CastleMinerZPlayerStats> mgr)
            => s_mgrStatsRef(mgr);

        /// <summary>
        /// Resolves an achievement by either:
        ///   • numeric index (e.g. "5")
        ///   • APIName (e.g. "ACH_TIME_PLAYED_1")
        ///   • display name (e.g. "Self Defense")
        /// Returns true on success.
        /// </summary>
        public static bool TryResolveAchievement(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            string token,
            out AchievementManager<CastleMinerZPlayerStats>.Achievement achievement,
            out int index)
        {
            achievement = null;
            index = -1;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            token = token.Trim();

            // First: Numeric index (0-based).
            if (int.TryParse(token, out var idx))
            {
                if (idx >= 0 && idx < mgr.Count)
                {
                    achievement = mgr[idx];
                    index = idx;
                    return achievement != null;
                }
            }

            // Second: Match by APIName or Name.
            for (int i = 0; i < mgr.Count; i++)
            {
                var ach = mgr[i];
                if (ach == null) continue;

                if (string.Equals(ach.APIName, token, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ach.Name, token, StringComparison.OrdinalIgnoreCase))
                {
                    achievement = ach;
                    index = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parses difficulty tokens like "easy", "normal", "hard", etc.
        /// into the ExtendedAchievementManager.AchievementDifficulty enum.
        /// Returns true on success.
        /// </summary>
        internal static bool TryParseDifficultyToken(
            string token,
            out ExtendedAchievementManager.AchievementDifficulty difficulty)
        {
            difficulty = ExtendedAchievementManager.AchievementDifficulty.Vanilla;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "vanilla":
                    difficulty = ExtendedAchievementManager.AchievementDifficulty.Vanilla;
                    return true;

                case "easy":
                    difficulty = ExtendedAchievementManager.AchievementDifficulty.Easy;
                    return true;

                case "normal":
                case "medium":
                    difficulty = ExtendedAchievementManager.AchievementDifficulty.Normal;
                    return true;

                case "hard":
                    difficulty = ExtendedAchievementManager.AchievementDifficulty.Hard;
                    return true;

                case "brutal":
                    difficulty = ExtendedAchievementManager.AchievementDifficulty.Brutal;
                    return true;

                case "insane":
                    difficulty = ExtendedAchievementManager.AchievementDifficulty.Insane;
                    return true;
            }

            return false;
        }
        #endregion

        #region Local Re-Sync Helpers

        /// <summary>
        /// After we manually edit CastleMinerZPlayerStats via presets, keep the
        /// GamePatches.ExtraStatsCache in sync. If we don't, the
        /// CastleMinerZGame_Update_ExtraStatsPatch will see the old cached values
        /// (e.g., distance=2000) and "heal" the stats back up next Update(),
        /// instantly re-unlocking the achievements we just revoked.
        /// </summary>
        private static void SyncExtraStatsCache(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return;

            GamePatches.ExtraStatsCache.MaxDistance       = stats.MaxDistanceTraveled;
            GamePatches.ExtraStatsCache.MaxDepth          = stats.MaxDepth;
            GamePatches.ExtraStatsCache.GuidedKills       = stats.DragonsKilledWithGuidedMissile;
            GamePatches.ExtraStatsCache.UndeadDragonKills = stats.UndeadDragonKills;
            GamePatches.ExtraStatsCache.HasData           = true;
        }
        #endregion

        #region Steam Re-Sync Helpers

        /// <summary>
        /// Cache of the non-public IsSastified property per achievement type.
        /// </summary>
        private static readonly Dictionary<Type, PropertyInfo> s_isSatisfiedPropCache =
            new Dictionary<Type, PropertyInfo>();

        /// <summary>
        /// Uses reflection to read the protected IsSastified property on an achievement.
        /// Returns false if the property cannot be read.
        /// </summary>
        private static bool TryGetConditionSatisfied(
            AchievementManager<CastleMinerZPlayerStats>.Achievement ach,
            out bool satisfied)
        {
            satisfied = false;
            if (ach == null)
                return false;

            var type = ach.GetType();
            if (!s_isSatisfiedPropCache.TryGetValue(type, out var prop))
            {
                // Note: Base class/property is spelled "IsSastified" in CMZ.
                prop = type.GetProperty("IsSastified", BindingFlags.Instance | BindingFlags.NonPublic);
                s_isSatisfiedPropCache[type] = prop;
            }

            if (prop == null)
                return false;

            satisfied = (bool)prop.GetValue(ach, null);
            return true;
        }

        /// <summary>
        /// After we change stats, walk all achievements and clear any that are now
        /// unsatisfied, on both local and Steam. This fixes chains like
        /// Survive 1 Day -> Survive 365 Days where revoking the first should
        /// also clear the higher tiers.
        /// </summary>
        public static void ResyncLockedAchievementsFromStats(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            CastleMinerZPlayerStats stats)
        {
            if (mgr == null || stats == null)
                return;

            var api = stats.SteamAPI;
            if (api == null)
                return;

            // Achievements must be cleared backwards to avoid progressive unlocks.
            for (int i = mgr.Count - 1; i >= 0; i--)
            {
                var ach = mgr[i];
                if (ach == null || string.IsNullOrEmpty(ach.APIName))
                    continue;

                // Can we evaluate the condition?
                if (!TryGetConditionSatisfied(ach, out var satisfied))
                    continue;

                // If currently unlocked but condition is false,
                // force it back to locked on both sides.
                if (ach.Acheived && !satisfied)
                {
                    // Local flag.
                    s_achievedFlagField?.SetValue(ach, false);

                    // Steam flag.
                    api.SetAchievement(ach.APIName, false);
                }
            }
        }
        #endregion

        #region Public API

        #region Grant One

        /// <summary>
        /// Grants a single achievement identified by ID/API name.
        /// Does NOT touch underlying stat counters; it just flips the flag
        /// and pushes to Steam.
        /// </summary>
        public static void GrantOne(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            string token)
        {
            if (!TryResolveAchievement(mgr, token, out var achievement, out var index))
            {
                SendFeedback($"GrantOne: Could not find achievement '{token}'.");
                return;
            }

            var stats = GetManagerStats(mgr);
            var api = stats.SteamAPI;

            if (achievement.Acheived)
            {
                SendFeedback($"Achievement [{index}] '{achievement.APIName ?? achievement.Name}' is already unlocked.");
                return;
            }

            // Everything below should work even when IsCustomAchievementAllowedNow() == false.
            using (GamePatches.VanillaStatGate.BeginBypass("GrantOne"))
            {
                // Optionally pull latest Steam stats first so we start from real values.
                // api?.RetrieveStats();

                #region Grant Local

                // Update local stats for just this achievement.
                ApplyLocalStatPresetForAchievement(stats, achievement, grant: true);
                CastleMinerZGame.Instance.SavePlayerStats(stats); // Persist to stats.sav.

                #endregion

                #region Grant Steam

                // Flip local achievement flag.
                s_achievedFlagField?.SetValue(achievement, true);

                // Let the manager perform its normal unlock pipeline
                // (Steam + our ExtendedAchievementManager.OnAchieved HUD toast).
                mgr.OnAchieved(achievement);

                // Push to Steam if possible.
                if (api != null && !string.IsNullOrEmpty(achievement.APIName))
                {
                    api.SetAchievement(achievement.APIName, true);
                    api.StoreStats();
                }
                #endregion
            }

            SendFeedback($"Unlocked achievement [{index}] '{achievement.APIName ?? achievement.Name}'.");
        }
        #endregion

        #region Revoke One

        /// <summary>
        /// Revokes a single achievement identified by ID/API name.
        /// NOTE: if the player's stats still satisfy the condition,
        /// the vanilla manager may re-award it later on Update().
        /// </summary>
        public static void RevokeOne(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            string token)
        {
            if (!TryResolveAchievement(mgr, token, out var achievement, out var index))
            {
                SendFeedback($"RevokeOne: Could not find achievement '{token}'.");
                return;
            }

            var stats = GetManagerStats(mgr);
            var api = stats.SteamAPI;

            if (!achievement.Acheived)
            {
                SendFeedback($"Achievement [{index}] '{achievement.APIName ?? achievement.Name}' is already locked.");
                return;
            }

            using (GamePatches.VanillaStatGate.BeginBypass("RevokeOne"))
            {
                api?.SuspendStorage(); // Stop auto StoreStats calls while we're messing with everything.
                api?.RetrieveStats();  // Make sure we're working from current Steam values.

                #region Revoke Local

                // Update local stats for just this achievement.
                ApplyLocalStatPresetForAchievement(stats, achievement, grant: false);
                CastleMinerZGame.Instance.SavePlayerStats(stats); // Persist to stats.sav.

                // Keep the extra-stats cache in sync so the Update() patch doesn't
                // resurrect the old values and re-award the achievement.
                SyncExtraStatsCache(stats);

                #endregion

                #region Revoke Steam

                // Clear all now-unsatisfied achievements (including this one and its chain).
                ResyncLockedAchievementsFromStats(mgr, stats);

                if (api != null)
                {
                    api.StoreStats();
                    api.ResumeStorage();
                    // api.RetrieveStats();
                }
                #endregion
            }

            SendFeedback($"Revoked achievement [{index}] '{achievement.APIName ?? achievement.Name}'.");
        }
        #endregion

        #region Grant All

        /// <summary>
        /// Core "grant all" implementation with optional filtering.
        /// If <paramref name="predicate"/> is null, applies to every achievement.
        /// </summary>
        private static void GrantAllCore(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            Func<AchievementManager<CastleMinerZPlayerStats>.Achievement, bool> predicate)
        {
            var stats = GetManagerStats(mgr);
            var api = stats.SteamAPI;

            if (api == null)
            {
                SendFeedback("GrantAll: Steam API not ready; granting locally only.");
            }

            int unlocked = 0;

            using (GamePatches.VanillaStatGate.BeginBypass("GrantAll"))
            {
                for (int i = 0; i < mgr.Count; i++)
                {
                    var achievement = mgr[i];
                    if (achievement == null)
                        continue;

                    // Filter by steam/custom if requested.
                    if (predicate != null && !predicate(achievement))
                        continue;

                    if (!achievement.Acheived)
                    {
                        #region Grant Local

                        ApplyLocalStatPresetForAchievement(stats, achievement, grant: true);
                        CastleMinerZGame.Instance.SavePlayerStats(stats);

                        #endregion

                        #region Grant Steam

                        s_achievedFlagField?.SetValue(achievement, true);
                        mgr.OnAchieved(achievement);

                        if (api != null && !string.IsNullOrEmpty(achievement.APIName))
                        {
                            api.SetAchievement(achievement.APIName, true);
                        }
                        #endregion

                        unlocked++;
                    }
                }

                api?.StoreStats();
                // api?.RetrieveStats();
            }

            SendFeedback($"Unlocked {unlocked} achievement(s).");
        }

        /// <summary>
        /// Bulk "grant all" - all achievements (vanilla + custom).
        /// </summary>
        public static void GrantAll(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            GrantAllCore(mgr, predicate: null);
        }

        /// <summary>
        /// Bulk "grant all steam" - only vanilla / non-custom achievements.
        /// </summary>
        public static void GrantAllSteam(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            GrantAllCore(mgr, ach => !IsCustomAchievement(ach));
        }

        /// <summary>
        /// Bulk "grant all custom" - only custom achievements (MoreAchievements namespace).
        /// </summary>
        public static void GrantAllCustom(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            GrantAllCore(mgr, ach => IsCustomAchievement(ach));
        }

        /// <summary>
        /// Bulk "grant all" for a single difficulty bucket (Easy/Normal/etc.).
        /// Uses ExtendedAchievementManager's difficulty map; no-op if the
        /// manager is not an ExtendedAchievementManager.
        /// </summary>
        public static void GrantAllByDifficulty(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            ExtendedAchievementManager.AchievementDifficulty difficulty)
        {
            GrantAllCore(
                mgr,
                ach =>
                {
                    if (!(mgr is ExtendedAchievementManager extended) || ach == null)
                        return false;

                    return extended.GetDifficulty(ach) == difficulty;
                });
        }
        #endregion

        #region Revoke All

        /// <summary>
        /// Core "revoke all" implementation with optional filtering.
        /// If <paramref name="predicate"/> is null, applies to every achievement.
        /// </summary>
        private static void RevokeAllCore(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            Func<AchievementManager<CastleMinerZPlayerStats>.Achievement, bool> predicate)
        {
            var stats = GetManagerStats(mgr);
            var api = stats.SteamAPI;

            if (api == null)
            {
                SendFeedback("RevokeAll: No Steam API; cleared local stats only.");
                return;
            }

            api.SuspendStorage();
            // api.RetrieveStats();
            s_bulkRevokeAll = true;

            int revoked = 0;

            using (GamePatches.VanillaStatGate.BeginBypass("RevokeAll"))
            {
                // Achievements must be cleared backwards to avoid progressive unlocks.
                for (int i = mgr.Count - 1; i >= 0; i--)
                {
                    var achievement = mgr[i];
                    if (achievement == null)
                        continue;

                    // Filter by steam/custom if requested.
                    if (predicate != null && !predicate(achievement))
                        continue;

                    if (achievement.Acheived)
                        revoked++;

                    #region Revoke Local

                    ApplyLocalStatPresetForAchievement(stats, achievement, grant: false);
                    CastleMinerZGame.Instance.SavePlayerStats(stats);

                    #endregion

                    #region Revoke Steam

                    s_achievedFlagField?.SetValue(achievement, false);
                    if (!string.IsNullOrEmpty(achievement.APIName))
                    {
                        api.SetAchievement(achievement.APIName, false);
                    }
                    #endregion
                }

                // All local stats have been updated and saved; make sure the extra-stats
                // cache reflects the *new* values so our CastleMinerZGame_Update_ExtraStatsPatch
                // does not restore the old maxima (distance, guided kills, undead kills).
                SyncExtraStatsCache(stats);

                s_bulkRevokeAll = false;
                api.StoreStats();
                api.ResumeStorage();
                // api.RetrieveStats();
            }

            SendFeedback($"Revoked {revoked} achievement(s) (local + Steam).");
        }

        /// <summary>
        /// Bulk "revoke all" - all achievements (vanilla + custom).
        /// </summary>
        public static void RevokeAll(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            RevokeAllCore(mgr, predicate: null);
        }

        /// <summary>
        /// Bulk "revoke all steam" - only vanilla / non-custom achievements.
        /// </summary>
        public static void RevokeAllSteam(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            RevokeAllCore(mgr, ach => !IsCustomAchievement(ach));
        }

        /// <summary>
        /// Bulk "revoke all custom" - only custom achievements (MoreAchievements namespace).
        /// </summary>
        public static void RevokeAllCustom(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            RevokeAllCore(mgr, ach => IsCustomAchievement(ach));
        }

        /// <summary>
        /// Bulk "revoke all" for a single difficulty bucket (Easy/Normal/etc.).
        /// </summary>
        public static void RevokeAllByDifficulty(
            AchievementManager<CastleMinerZPlayerStats> mgr,
            ExtendedAchievementManager.AchievementDifficulty difficulty)
        {
            RevokeAllCore(
                mgr,
                ach =>
                {
                    if (!(mgr is ExtendedAchievementManager extended) || ach == null)
                        return false;

                    return extended.GetDifficulty(ach) == difficulty;
                });
        }
        #endregion

        #region Print Stats

        /// <summary>
        /// Prints a simple unlocked/total summary.
        /// </summary>
        public static void PrintStats(AchievementManager<CastleMinerZPlayerStats> mgr)
        {
            int unlocked = 0;

            for (int i = 0; i < mgr.Count; i++)
            {
                if (mgr[i]?.Acheived == true)
                    unlocked++;
            }

            SendFeedback($"Achievement stats: {unlocked}/{mgr.Count} unlocked.");
        }
        #endregion

        #region Print API List

        /// <summary>
        /// Prints a simple achievement API name summary.
        /// </summary>
        public static void PrintAPIList(AchievementManager<CastleMinerZPlayerStats> mgr, bool all = false)
        {
            List<string> APINames = new List<string>();

            for (int i = 0; i < mgr.Count; i++)
            {
                if (!all && !IsCustomAchievement(mgr[i]))
                    APINames.Add(mgr[i]?.APIName);
            }

            SendFeedback($"Achievement API Names: {string.Join(", ", APINames)}.");
        }
        #endregion

        #endregion
    }
}