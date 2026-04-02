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
    /// "Master Toolsmith" - unlocks when the player has crafted at least one of
    /// every basic tool variant:
    ///   • Pickaxes: Stone, Copper, Iron, Gold, Diamond, Bloodstone.
    ///   • Spades: Stone, Copper, Iron, Gold, Diamond.
    ///   • Axes:   Stone, Copper, Iron, Gold, Diamond.
    ///
    /// Uses CastleMinerZPlayerStats.GetItemStats(id).Crafted to check progress.
    /// </summary>
    public sealed class MasterToolsmithAchievement : AchievementManager<CastleMinerZPlayerStats>.Achievement, ILocalStatAdjust, IAchievementReward
    {
        public MasterToolsmithAchievement(string apiName, CastleMinerZAchievementManager manager, string name, string description)
           : base(apiName,
                  manager,
                  name,
                  description)
        {
        }

        #region Fields

        /// <summary>
        /// All required tool IDs for this achievement.
        /// </summary>
        private static readonly InventoryItemIDs[] _requiredToolIds =
        {
            // Pickaxes.
            InventoryItemIDs.StonePickAxe,
            InventoryItemIDs.CopperPickAxe,
            InventoryItemIDs.IronPickAxe,
            InventoryItemIDs.GoldPickAxe,
            InventoryItemIDs.DiamondPickAxe,
            InventoryItemIDs.BloodstonePickAxe,

            // Spades.
            InventoryItemIDs.StoneSpade,
            InventoryItemIDs.CopperSpade,
            InventoryItemIDs.IronSpade,
            InventoryItemIDs.GoldSpade,
            InventoryItemIDs.DiamondSpade,

            // Axes.
            InventoryItemIDs.StoneAxe,
            InventoryItemIDs.CopperAxe,
            InventoryItemIDs.IronAxe,
            InventoryItemIDs.GoldAxe,
            InventoryItemIDs.DiamondAxe,

            // Drills.
            InventoryItemIDs.LaserDrill,
        };

        // Cached progress string.
        private string _lastProgress;
        private int    _lastHave  = -1;
        private int    _lastTotal = -1;

        #endregion

        #region Core helpers

        /// <summary>
        /// Counts how many required tools have Crafted ≥ 1.
        /// </summary>
        private static int CountToolsCrafted(
            CastleMinerZPlayerStats stats,
            out int total)
        {
            total = _requiredToolIds.Length;

            if (stats == null || total == 0)
                return 0;

            int have = 0;

            foreach (var id in _requiredToolIds)
            {
                var s = stats.GetItemStats(id);
                if (s != null && s.Crafted > 0)
                    have++;
            }

            return have;
        }
        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// True once the player has crafted at least one of every tool variant
        /// in <see cref="_requiredToolIds"/>.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                int have = CountToolsCrafted(PlayerStats, out int total);
                return (total > 0) && (have >= total);
            }
        }

        /// <summary>
        /// Progress: fraction of required tools that have been crafted.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                int have = CountToolsCrafted(PlayerStats, out int total);

                if (total <= 0)
                    return 0f;

                float f = (float)have / (float)total;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return f;
            }
        }

        /// <summary>
        /// Text like "(12/16) tool variants crafted at least once."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int have = CountToolsCrafted(PlayerStats, out int total);

                if (have != _lastHave || total != _lastTotal || _lastProgress == null)
                {
                    _lastHave     = have;
                    _lastTotal    = total;
                    _lastProgress =
                        $"({have}/{total}) tool variants crafted at least once.";
                }

                return _lastProgress ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// One-time "master toolsmith" reward.
        /// Adjust to taste.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RockBlock,       8), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Copper,          8), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Iron,            8), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Gold,            8), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Diamond,         8), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneBlock, 8), false);
            ModLoader.LogSystem.SendFeedback("+8 RockBlock, +8 Copper, +8 Iron, +8 Gold, +8 Diamond, +8 BloodStoneBlock.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments when grant/revoke is used from your reset helper:
        /// • grant == true: ensure each required tool has Crafted ≥ 1.
        /// • grant == false: clear Crafted count for all required tools.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            foreach (var id in _requiredToolIds)
            {
                var s = stats.GetItemStats(id);
                if (s == null)
                    continue;

                if (grant)
                {
                    if (s.Crafted < 1)
                        s.Crafted = 1;
                }
                else
                {
                    s.Crafted = 0;
                }
            }
        }
        #endregion
    }
}