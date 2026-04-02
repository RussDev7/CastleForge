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
    /// Unlocks when the player has crafted at least one of each axe variant.
    /// Currently tracked variants:
    ///   StoneAxe, CopperAxe, IronAxe, GoldAxe, DiamondAxe.
    ///
    /// Uses CastleMinerZPlayerStats.GetItemStats(...).Crafted > 0.
    /// </summary>
    public sealed class MasterAxeAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public MasterAxeAchievement(
            string                        apiName,
            CastleMinerZAchievementManager manager,
            string                        name,
            string                        description)
            : base(apiName,
                   manager,
                   name,
                   description)
        {
        }

        #region Static Axe Table

        /// <summary>
        /// All axe items that must be crafted at least once.
        /// Edit this list if you decide to include/exclude more variants.
        /// </summary>
        public static readonly InventoryItemIDs[] AxeIDs =
        {
            InventoryItemIDs.StoneAxe,
            InventoryItemIDs.CopperAxe,
            InventoryItemIDs.IronAxe,
            InventoryItemIDs.GoldAxe,
            InventoryItemIDs.DiamondAxe,
        };

        private static int TotalAxeCount
        {
            get { return AxeIDs.Length; }
        }

        /// <summary>
        /// Counts how many distinct axe variants the player has crafted
        /// at least once.
        /// </summary>
        private static int GetDistinctCraftedAxeCount(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int count = 0;

            foreach (var id in AxeIDs)
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
        /// Satisfied once every axe in AxeIDs has Crafted >= 1.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                int total = TotalAxeCount;
                if (total <= 0)
                    return false;

                int have = GetDistinctCraftedAxeCount(PlayerStats);
                return have >= total;
            }
        }

        /// <summary>
        /// Progress = (# axe variants crafted at least once) / (total variants).
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = TotalAxeCount;
                if (total <= 0)
                    return 0f;

                int have = GetDistinctCraftedAxeCount(PlayerStats);

                float f = (float)have / (float)total;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return f;
            }
        }

        /// <summary>
        /// Human-readable progress like:
        /// "(2/5) axe variants crafted at least once."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = TotalAxeCount;
                int have  = GetDistinctCraftedAxeCount(PlayerStats);

                if (have != _lastHave || total != _lastTotal || _lastProgress == null)
                {
                    _lastHave     = have;
                    _lastTotal    = total;
                    _lastProgress =
                        $"({have}/{total}) axe variants crafted at least once.";
                }

                return _lastProgress ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// One-time reward for completing the axe set.
        /// Adjust the loot to taste.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.LogBlock,  64), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.WoodBlock, 64), false);
            ModLoader.LogSystem.SendFeedback("+64 LogBlock, +64 WoodBlock.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// • grant == true:  Ensure every axe has Crafted >= 1.
        /// • grant == false: Set Crafted = 0 for all tracked axes.
        ///   (Destructive; mainly for testing / debugging.)
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            foreach (var id in AxeIDs)
            {
                var axeStats = stats.GetItemStats(id);
                if (axeStats == null)
                    continue;

                if (grant)
                {
                    if (axeStats.Crafted < 1)
                        axeStats.Crafted = 1;
                }
                else
                {
                    axeStats.Crafted = 0;
                }
            }
        }
        #endregion
    }
}