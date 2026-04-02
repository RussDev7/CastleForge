/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using System.Reflection;
using DNA.CastleMinerZ;
using System;

namespace MoreAchievements
{
    internal static class PlayerStatReflectionHelper
    {
        #region Private Reflection Handles

        // Private Dictionary<BlockTypeEnum,int> BlocksDug field on CastleMinerZPlayerStats.
        private static readonly FieldInfo _blocksDugField =
            typeof(CastleMinerZPlayerStats).GetField(
                "BlocksDug",
                BindingFlags.Instance | BindingFlags.NonPublic);


        /// <summary>
        /// Backing field for the private "AllItemStats" dictionary on
        /// <see cref="CastleMinerZPlayerStats"/>.
        /// Signature:
        ///   private Dictionary<InventoryItemIDs, ItemStats> AllItemStats;
        /// </summary>
        private static readonly FieldInfo s_allItemStatsField =
            typeof(CastleMinerZPlayerStats).GetField(
                "AllItemStats",
                BindingFlags.Instance | BindingFlags.NonPublic);

        #endregion

        #region BlocksDug Helpers

        /// <summary>
        /// Returns the underlying BlocksDug dictionary, or null if reflection failed.
        /// </summary>
        public static Dictionary<BlockTypeEnum, int> GetBlocksDugDict(CastleMinerZPlayerStats stats)
        {
            if (stats == null || _blocksDugField == null)
                return null;

            return _blocksDugField.GetValue(stats) as Dictionary<BlockTypeEnum, int>;
        }

        /// <summary>
        /// Read the raw dug-count for a specific block type using the private dictionary.
        /// </summary>
        public static int GetBlocksDugCountRaw(CastleMinerZPlayerStats stats, BlockTypeEnum type)
        {
            var dict = GetBlocksDugDict(stats);
            if (dict == null)
                return 0;

            return dict.TryGetValue(type, out int value) ? value : 0;
        }

        public static int GetBlocksDugTotalRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetBlocksDugDict(stats);
            if (dict == null)
                return 0;

            int sum = 0;
            foreach (var kvp in dict)
                sum += kvp.Value;

            return sum;
        }

        /// <summary>
        /// Set the dug-count for a specific block type using reflection.
        /// </summary>
        public static void SetBlocksDugCountRaw(CastleMinerZPlayerStats stats, BlockTypeEnum type, int value)
        {
            var dict = GetBlocksDugDict(stats);
            if (dict == null)
                return;

            dict[type] = Math.Max(0, value);
        }

        /// <summary>
        /// Clears the private BlocksDug dictionary for the given stats instance.
        /// Use with care: This wipes ALL per-block dug counts back to zero.
        /// </summary>
        public static void ClearBlocksDugRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetBlocksDugDict(stats);
            dict?.Clear();
        }
        #endregion

        #region ItemStats Dictionary Helpers

        /// <summary>
        /// Returns the raw AllItemStats dictionary for the given stats object, or null
        /// if the field cannot be resolved (defensive against version changes).
        /// </summary>
        private static Dictionary<InventoryItemIDs, CastleMinerZPlayerStats.ItemStats>
            GetAllItemStatsDictionary(CastleMinerZPlayerStats stats)
        {
            if (stats == null || s_allItemStatsField == null)
                return null;

            return s_allItemStatsField.GetValue(stats) as
                Dictionary<InventoryItemIDs, CastleMinerZPlayerStats.ItemStats>;
        }

        /// <summary>
        /// Gets the raw ItemStats entry for the given item ID.
        /// If <paramref name="createIfMissing"/> is true and there is no entry yet,
        /// this will use <see cref="CastleMinerZPlayerStats.GetItemStats"/> to
        /// create/populate one and then return it.
        /// </summary>
        public static CastleMinerZPlayerStats.ItemStats GetItemStatsRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            bool createIfMissing = true)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return null;

            if (dict.TryGetValue(id, out var existing) && existing != null)
                return existing;

            if (!createIfMissing)
                return null;

            // Use the game's public helper to ensure the entry exists.
            var created = stats.GetItemStats(id);
            if (created != null)
                dict[id] = created;

            return created;
        }

        /// <summary>
        /// Clears the entire AllItemStats dictionary.
        /// WARNING:
        ///   This effectively wipes all per-item stats (kills, crafted, used, etc.)
        ///   and should only be used from explicit "reset" commands.
        /// </summary>
        public static void ClearAllItemStatsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            dict?.Clear();
        }
        #endregion

        #region AllItemStats Selective Clear Helpers

        /// <summary>
        /// Sets TimeHeld = TimeSpan.Zero for every ItemStats entry in AllItemStats.
        /// Leaves all other fields (Crafted, Used, Kills*, etc.) untouched.
        /// </summary>
        public static void ClearAllItemStatsTimeHeldRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.TimeHeld = TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Sets KillsZombies = 0 for every ItemStats entry in AllItemStats.
        /// Leaves all other fields (Crafted, Used, KillsSkeleton, etc.) untouched.
        /// </summary>
        public static void ClearAllItemStatsZombieKillsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.KillsZombies = 0;
            }
        }

        /// <summary>
        /// Sets KillsSkeleton = 0 for every ItemStats entry in AllItemStats.
        /// Leaves all other fields untouched.
        /// </summary>
        public static void ClearAllItemStatsSkeletonKillsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.KillsSkeleton = 0;
            }
        }

        /// <summary>
        /// Clears both zombie and skeleton kill fields for every ItemStats entry.
        /// Convenience wrapper around the two clears above.
        /// </summary>
        public static void ClearAllItemStatsUndeadKillsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.KillsZombies  = 0;
                item.KillsSkeleton = 0;
            }
        }

        /// <summary>
        /// Sets Crafted = 0 for every ItemStats entry in AllItemStats.
        /// Leaves all other fields (Used, kills, etc.) untouched.
        /// </summary>
        public static void ClearAllItemStatsCraftedRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.Crafted = 0;
            }
        }

        /// <summary>
        /// Sets Hits = 0 for every ItemStats entry in AllItemStats.
        /// Leaves all other fields (Crafted, Used, Kills*, etc.) untouched.
        /// </summary>
        public static void ClearAllItemStatsHitsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.Hits = 0;
            }
        }

        /// <summary>
        /// Sets Used = 0 for every ItemStats entry in AllItemStats.
        /// Leaves all other fields untouched.
        /// </summary>
        public static void ClearAllItemStatsUsedRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.Used = 0;
            }
        }

        /// <summary>
        /// Sets KillsHell = 0 for every ItemStats entry in AllItemStats.
        /// Leaves all other fields untouched.
        /// </summary>
        public static void ClearAllItemStatsHellKillsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.KillsHell = 0;
            }
        }

        /// <summary>
        /// Convenience wrapper: clears Crafted and Used for all items.
        /// Does NOT touch any kill fields.
        /// </summary>
        public static void ClearAllItemStatsCraftedAndUsedRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return;

            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                item.Crafted = 0;
                item.Used = 0;
            }
        }
        #endregion

        #region ItemStats Field Helpers (TimeHeld/Crafted/Hits/Used/Kills*)

        /// <summary>
        /// Gets the TimeHeld value for a specific inventory item.
        /// Returns TimeSpan.Zero if the entry does not exist.
        /// </summary>
        public static TimeSpan GetItemStatTimeHeldRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.TimeHeld ?? TimeSpan.Zero;
        }

        /// <summary>
        /// Helper to read TimeHeld as total minutes.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static double GetItemStatTimeHeldMinutesRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            return GetItemStatTimeHeldRaw(stats, id).TotalMinutes;
        }

        /// <summary>
        /// Sets the TimeHeld value for a specific inventory item.
        /// </summary>
        public static void SetItemStatTimeHeldRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            TimeSpan timeHeld)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.TimeHeld = timeHeld;
        }

        /// <summary>
        /// Gets the Crafted count for a specific inventory item.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static int GetItemStatCraftedRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.Crafted ?? 0;
        }

        /// <summary>
        /// Sets the Crafted count for a specific inventory item.
        /// </summary>
        public static void SetItemStatCraftedRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            int crafted)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.Crafted = crafted;
        }

        /// <summary>
        /// Gets the Hits count for a specific inventory item.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static int GetItemStatHitsRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.Hits ?? 0;
        }

        /// <summary>
        /// Sets the Hits count for a specific inventory item.
        /// </summary>
        public static void SetItemStatHitsRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            int hits)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.Hits = hits;
        }

        /// <summary>
        /// Sums ItemStats.Hits over all entries in AllItemStats.
        /// Returns 0 if the dictionary is unavailable.
        /// </summary>
        public static int GetTotalItemStatsHitsRaw(CastleMinerZPlayerStats stats)
        {
            var dict = GetAllItemStatsDictionary(stats);
            if (dict == null)
                return 0;

            int sum = 0;
            foreach (var kvp in dict)
            {
                var item = kvp.Value;
                if (item == null)
                    continue;

                sum += item.Hits;
            }
            return sum;
        }

        /// <summary>
        /// Gets the Used count for a specific inventory item.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static int GetItemStatUsedRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.Used ?? 0;
        }

        /// <summary>
        /// Sets the Used count for a specific inventory item.
        /// </summary>
        public static void SetItemStatUsedRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            int used)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.Used = used;
        }

        /// <summary>
        /// Gets the zombie kill count for a specific inventory item.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static int GetItemStatKillsZombiesRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.KillsZombies ?? 0;
        }

        /// <summary>
        /// Sets the zombie kill count for a specific inventory item.
        /// </summary>
        public static void SetItemStatKillsZombiesRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            int kills)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.KillsZombies = kills;
        }

        /// <summary>
        /// Gets the skeleton kill count for a specific inventory item.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static int GetItemStatKillsSkeletonRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.KillsSkeleton ?? 0;
        }

        /// <summary>
        /// Sets the skeleton kill count for a specific inventory item.
        /// </summary>
        public static void SetItemStatKillsSkeletonRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            int kills)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.KillsSkeleton = kills;
        }

        /// <summary>
        /// Gets the Hell-enemy kill count for a specific inventory item.
        /// Returns 0 if the entry does not exist.
        /// </summary>
        public static int GetItemStatKillsHellRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: false);
            return item?.KillsHell ?? 0;
        }

        /// <summary>
        /// Sets the Hell-enemy kill count for a specific inventory item.
        /// </summary>
        public static void SetItemStatKillsHellRaw(
            CastleMinerZPlayerStats stats,
            InventoryItemIDs id,
            int kills)
        {
            var item = GetItemStatsRaw(stats, id, createIfMissing: true);
            if (item == null)
                return;

            item.KillsHell = kills;
        }
        #endregion
    }
}