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
    /// Unlocks when the player has crafted at least one of each spade variant.
    /// Currently tracked variants:
    ///   StoneSpade, CopperSpade, IronSpade, GoldSpade, DiamondSpade.
    ///
    /// Uses CastleMinerZPlayerStats.GetItemStats(...).Crafted > 0.
    /// </summary>
    public sealed class MasterSpadeAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public MasterSpadeAchievement(
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

        #region Static Spade Table

        /// <summary>
        /// All spade items that must be crafted at least once.
        /// Edit this list if you decide to include/exclude more variants.
        /// </summary>
        public static readonly InventoryItemIDs[] SpadeIDs =
        {
            InventoryItemIDs.StoneSpade,
            InventoryItemIDs.CopperSpade,
            InventoryItemIDs.IronSpade,
            InventoryItemIDs.GoldSpade,
            InventoryItemIDs.DiamondSpade,
        };

        private static int TotalSpadeCount
        {
            get { return SpadeIDs.Length; }
        }

        /// <summary>
        /// Counts how many distinct spade variants the player has crafted
        /// at least once.
        /// </summary>
        private static int GetDistinctCraftedSpadeCount(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int count = 0;

            foreach (var id in SpadeIDs)
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
        /// Satisfied once every spade in SpadeIDs has Crafted >= 1.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                int total = TotalSpadeCount;
                if (total <= 0)
                    return false;

                int have = GetDistinctCraftedSpadeCount(PlayerStats);
                return have >= total;
            }
        }

        /// <summary>
        /// Progress = (# spade variants crafted at least once) / (total variants).
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = TotalSpadeCount;
                if (total <= 0)
                    return 0f;

                int have = GetDistinctCraftedSpadeCount(PlayerStats);

                float f = (float)have / (float)total;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return f;
            }
        }

        /// <summary>
        /// Human-readable progress like:
        /// "(3/5) spade variants crafted at least once."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = TotalSpadeCount;
                int have  = GetDistinctCraftedSpadeCount(PlayerStats);

                if (have != _lastHave || total != _lastTotal || _lastProgress == null)
                {
                    _lastHave     = have;
                    _lastTotal    = total;
                    _lastProgress =
                        $"({have}/{total}) spade variants crafted at least once.";
                }

                return _lastProgress ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// One-time reward for completing the spade set.
        /// Adjust the loot to taste.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DirtBlock, 64), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.SandBlock, 64), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Snow,      64), false);
            ModLoader.LogSystem.SendFeedback("+64 DirtBlock, +64 SandBlock, +64 Snow.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// • grant == true:  Ensure every spade has Crafted >= 1.
        /// • grant == false: Set Crafted = 0 for all tracked spades.
        ///   (Destructive; mainly for testing / debugging.)
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            foreach (var id in SpadeIDs)
            {
                var spadeStats = stats.GetItemStats(id);
                if (spadeStats == null)
                    continue;

                if (grant)
                {
                    if (spadeStats.Crafted < 1)
                        spadeStats.Crafted = 1;
                }
                else
                {
                    spadeStats.Crafted = 0;
                }
            }
        }
        #endregion
    }
}