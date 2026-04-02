/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// Unlocks the first time the player has crafted at least one of every craftable item.
    /// "Craftable" is defined as any recipe output found in Recipe.CookBook (after filtering).
    /// </summary>
    public class MasterCrafterAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public MasterCrafterAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        #region Static Craftable-Item Table

        /// <summary>
        /// Snapshot of all craftable item IDs discovered from Recipe.CookBook.
        /// Built once on first use.
        /// </summary>
        public static readonly InventoryItemIDs[] _craftableItemIDs = BuildCraftableItemList();

        /// <summary>
        /// Builds the deduplicated, sorted list of craftable item IDs.
        /// You can tweak filtering inside ShouldIncludeInCraftAll(...) if needed.
        /// </summary>
        private static InventoryItemIDs[] BuildCraftableItemList()
        {
            var set = new HashSet<InventoryItemIDs>();

            try
            {
                foreach (var recipe in Recipe.CookBook)
                {
                    if (recipe == null)
                        continue;

                    var result = recipe.Result;
                    if (result == null)
                        continue;

                    // Use the InventoryItem's ItemClass.ID to get the underlying InventoryItemIDs.
                    InventoryItemIDs id = result.ItemClass.ID;

                    if (!ShouldIncludeInCraftAll(id))
                        continue;

                    set.Add(id);
                }

                if (set.Count == 0)
                {
                    ModLoader.LogSystem.Log(
                        "CraftAllItemsAchievement: No craftable items discovered in Recipe.CookBook.");
                }
                else
                {
                    ModLoader.LogSystem.Log(
                        $"CraftAllItemsAchievement: Discovered {set.Count} craftable item IDs.");
                }
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log(
                    $"CraftAllItemsAchievement.BuildCraftableItemList error: {ex}.");
            }

            var arr = new InventoryItemIDs[set.Count];
            set.CopyTo(arr);
            Array.Sort(arr);
            return arr;
        }

        /// <summary>
        /// Central filter for which recipe outputs should count towards this achievement.
        /// Right now it includes everything; customize to skip ammo, debug items, etc.
        /// </summary>
        private static bool ShouldIncludeInCraftAll(InventoryItemIDs id)
        {
            switch (id)
            {
                 case InventoryItemIDs.PrecisionLaser:
                     return false;
            }

            return true;
        }

        /// <summary>
        /// Counts how many distinct craftable items the player has crafted at least once.
        /// </summary>
        private static int GetDistinctCraftedItemCount(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int count = 0;

            foreach (var itemID in _craftableItemIDs)
            {
                var itemStats = stats.GetItemStats(itemID);
                if (itemStats != null && itemStats.Crafted > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int TotalCraftableItemCount
        {
            get { return _craftableItemIDs.Length; }
        }

        #endregion

        #region Fields

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                int distinctCrafted = GetDistinctCraftedItemCount(PlayerStats);
                return (TotalCraftableItemCount > 0) &&
                       (distinctCrafted >= TotalCraftableItemCount);
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total          = TotalCraftableItemCount;
                if (total <= 0)
                    return 0f;

                int distinctCrafted = GetDistinctCraftedItemCount(PlayerStats);

                // Ratio in [0,1].
                float f = (float)distinctCrafted / (float)total;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return f;
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int distinctCrafted = GetDistinctCraftedItemCount(PlayerStats);
                int total           = TotalCraftableItemCount;

                if (_lastAmount != distinctCrafted)
                {
                    _lastAmount = distinctCrafted;
                    _lastString = $"({distinctCrafted}/{total}) craftable items crafted at least once.";
                }

                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Optional one-time reward for completing the challenge.
        /// Adjust to taste (this is just a sample).
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BasicGrenadeLauncher, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RocketAmmo,           512), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondWall,          32),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldenWall,           16),  false);
            ModLoader.LogSystem.SendFeedback("+1 BasicGrenadeLauncher, +512 RocketAmmo, +32 DiamondWall, +16 GoldenWall.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// • grant == true: ensure every craftable item has Crafted >= 1.
        /// • grant == false: reset Crafted = 0 for all craftable items.
        ///   (This is inherently destructive and meant mainly for testing.)
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                // Make sure all craftable items are marked as crafted at least once.
                foreach (var itemID in _craftableItemIDs)
                {
                    var s = stats.GetItemStats(itemID);
                    if (s != null && s.Crafted < 1)
                    {
                        s.Crafted = 1;
                    }
                }
            }
            else
            {
                // Clear the craft count for all craftable items.
                // This makes the achievement "un-earned" again.
                foreach (var itemID in _craftableItemIDs)
                {
                    var s = stats.GetItemStats(itemID);
                    if (s != null)
                    {
                        s.Crafted = 0;
                    }
                }
            }
        }
        #endregion
    }
}