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
    /// Easy difficulty:
    /// Unlocks once the player has crafted at least one Iron Pickaxe.
    ///
    /// Uses CastleMinerZPlayerStats.GetItemStats(InventoryItemIDs.IronPickAxe).Crafted
    /// and respects AchievementModeRules so it only unlocks in allowed modes.
    /// </summary>
    internal sealed class CraftIronPickaxeAchievement : AchievementManager<CastleMinerZPlayerStats>.Achievement, ILocalStatAdjust, IAchievementReward
    {
        public CraftIronPickaxeAchievement(string apiName, CastleMinerZAchievementManager manager, string name, string description)
           : base(apiName,
                  manager,
                  name,
                  description)
        {
        }

        #region Fields

        private const InventoryItemIDs TargetItem = InventoryItemIDs.IronPickAxe;

        // Small cache for the progress string.
        private string _lastString;
        private int    _lastCrafted = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// True once the Iron Pickaxe has been crafted at least once
        /// (and only in allowed game modes).
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return false;

                var s = stats.GetItemStats(TargetItem);
                return (s != null && s.Crafted >= 1);
            }
        }

        /// <summary>
        /// 0 or 1: not crafted vs crafted.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return 0f;

                var s = stats.GetItemStats(TargetItem);
                if (s == null)
                    return 0f;

                return (s.Crafted >= 1) ? 1f : 0f;
            }
        }

        /// <summary>
        /// Text like:
        ///   "Iron Pickaxes crafted: 0" or "Iron Pickaxes crafted: 3".
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int crafted = 0;

                var stats = PlayerStats;
                if (stats != null)
                {
                    var s = stats.GetItemStats(TargetItem);
                    if (s != null)
                        crafted = s.Crafted;
                }

                if (crafted != _lastCrafted || _lastString == null)
                {
                    _lastCrafted = crafted;
                    _lastString  = $"{crafted} Iron Pickaxe(s) crafted.";
                }

                return _lastString ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Simple one-time reward for getting your first iron pick:
        /// give some extra Iron + Coal so the player can keep mining.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronOre, 12), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Coal,    12), false);
            ModLoader.LogSystem.SendFeedback("+12 IronOre, +12 Coal.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Optional function:
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// • grant == true  -> Ensure Crafted >= 1 for Iron Pickaxe.
        /// • grant == false -> Reset Crafted = 0 so it can be re-earned.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var ironPickaxeStats = stats.GetItemStats(TargetItem);
            if (ironPickaxeStats == null)
                return;

            if (grant)
            {
                if (ironPickaxeStats.Crafted < 1)
                    ironPickaxeStats.Crafted = 1;
            }
            else
            {
                ironPickaxeStats.Crafted = 0;
            }
        }
        #endregion
    }
}