/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// Unlocks when the player has crafted at least one of each pickaxe variant.
    /// Currently tracked variants:
    ///   StonePickAxe, CopperPickAxe, IronPickAxe, GoldPickAxe,
    ///   DiamondPickAxe, BloodstonePickAxe.
    ///
    /// Uses CastleMinerZPlayerStats.GetItemStats(...).Crafted > 0.
    /// </summary>
    public sealed class MasterPickaxeAchievement : AchievementManager<CastleMinerZPlayerStats>.Achievement, ILocalStatAdjust, IAchievementReward
    {
        public MasterPickaxeAchievement(string apiName, CastleMinerZAchievementManager manager, string name, string description)
           : base(apiName,
                  manager,
                  name,
                  description)
        {
        }

        #region Static Pickaxe Table

        /// <summary>
        /// All pickaxe-like items that must be crafted at least once.
        /// Edit this list if you decide to include/exclude more variants.
        /// </summary>
        public static readonly InventoryItemIDs[] PickaxeIDs =
        {
            InventoryItemIDs.StonePickAxe,
            InventoryItemIDs.CopperPickAxe,
            InventoryItemIDs.IronPickAxe,
            InventoryItemIDs.GoldPickAxe,
            InventoryItemIDs.DiamondPickAxe,
            InventoryItemIDs.BloodstonePickAxe,
        };

        private static int TotalPickaxeCount
        {
            get { return PickaxeIDs.Length; }
        }

        /// <summary>
        /// Counts how many distinct pickaxe variants the player has crafted
        /// at least once.
        /// </summary>
        private static int GetDistinctCraftedPickaxeCount(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int count = 0;

            foreach (var id in PickaxeIDs)
            {
                var s = stats.GetItemStats(id);
                if (s != null && s.Crafted > 0)
                {
                    count++;
                }
            }

            return count;
        }
        #endregion

        #region Fields

        private string _lastProgress;
        private int    _lastHave  = -1;
        private int    _lastTotal = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Satisfied once every pickaxe in PickaxeIDs has Crafted >= 1.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                int total = TotalPickaxeCount;
                if (total <= 0)
                    return false;

                int have = GetDistinctCraftedPickaxeCount(PlayerStats);
                return have >= total;
            }
        }

        /// <summary>
        /// Progress = (# pickaxe variants crafted at least once) / (total variants).
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = TotalPickaxeCount;
                if (total <= 0)
                    return 0f;

                int have = GetDistinctCraftedPickaxeCount(PlayerStats);

                float f = (float)have / (float)total;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return f;
            }
        }

        /// <summary>
        /// Human-readable progress like:
        /// "(3/7) pickaxe variants crafted at least once."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = TotalPickaxeCount;
                int have  = GetDistinctCraftedPickaxeCount(PlayerStats);

                if (have != _lastHave || total != _lastTotal || _lastProgress == null)
                {
                    _lastHave     = have;
                    _lastTotal    = total;
                    _lastProgress =
                        $"({have}/{total}) pickaxe variants crafted at least once.";
                }

                return _lastProgress ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// One-time reward for completing the pickaxe set.
        /// Adjust the loot to taste.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RockBlock, 64), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Ice,       64), false);
            ModLoader.LogSystem.SendFeedback("+64 RockBlock, +64 Ice.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Optional function:
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// • grant == true:  Ensure every pickaxe has Crafted >= 1.
        /// • grant == false: Set Crafted = 0 for all tracked pickaxes.
        ///   (Destructive; mainly for testing / debugging.)
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            foreach (var id in PickaxeIDs)
            {
                var pickaxeStats = stats.GetItemStats(id);
                if (pickaxeStats == null)
                    continue;

                if (grant)
                {
                    if (pickaxeStats.Crafted < 1)
                        pickaxeStats.Crafted = 1;
                }
                else
                {
                    pickaxeStats.Crafted = 0;
                }
            }
        }
        #endregion
    }
}