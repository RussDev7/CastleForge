/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// Unlocks the first time the player crafts at least one WoodBlock.
    /// </summary>
    public class CraftWoodBlockAchievement : AchievementManager<CastleMinerZPlayerStats>.Achievement, ILocalStatAdjust, IAchievementReward
    {
        public CraftWoodBlockAchievement(string apiName, CastleMinerZAchievementManager manager, string name, string description)
            : base(apiName,
                   manager,
                   name,
                   description)
        {
        }

        #region Fields

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                // Normal stat check.
                CastleMinerZPlayerStats.ItemStats itemStats =
                    CastleMinerZGame.Instance.PlayerStats.GetItemStats(InventoryItemIDs.WoodBlock);

                return itemStats.Crafted > 0;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                CastleMinerZPlayerStats.ItemStats itemStats =
                    CastleMinerZGame.Instance.PlayerStats.GetItemStats(InventoryItemIDs.WoodBlock);

                return (itemStats.Crafted > 0) ? 1f : 0f;
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                CastleMinerZPlayerStats.ItemStats itemStats =
                    CastleMinerZGame.Instance.PlayerStats.GetItemStats(InventoryItemIDs.WoodBlock);

                int total = itemStats.Crafted;

                if (_lastAmount != total)
                {
                    _lastAmount = total;

                    // Simple English string; replace with a localized string if you like,
                    // e.g. " + Strings.WoodBlock_Crafted" if you add it to the Strings resx.
                    _lastString = total + " Wood Block(s) crafted.";
                }

                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Optional function:
        /// One-time reward for completing the challenge.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.WoodBlock, 16), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Stick,     8),  false);
            ModLoader.LogSystem.SendFeedback("+16 WoodBlock, +8 Stick.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Optional function:
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var woodStats = stats.GetItemStats(InventoryItemIDs.WoodBlock);
            if (woodStats == null)
                return;

            if (grant)
            {
                // Ensure at least one wood block craft is recorded.
                woodStats.Crafted = Math.Max(woodStats.Crafted, 1);
            }
            else
            {
                // Clear just the wood-block craft count for this achievement.
                woodStats.Crafted = 0;
            }
        }
        #endregion
    }
}