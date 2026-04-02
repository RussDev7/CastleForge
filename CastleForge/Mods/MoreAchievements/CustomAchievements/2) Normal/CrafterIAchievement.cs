/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// "Crafter I" - craft a significant number of items.
    /// Uses <see cref="CastleMinerZPlayerStats.TotalItemsCrafted"/>.
    /// </summary>
    internal sealed class CrafterIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public CrafterIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredCrafts = 250;
        }

        #region Fields

        private readonly int _requiredCrafts;

        private string       _lastString;
        private int          _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.TotalItemsCrafted >= _requiredCrafts;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.TotalItemsCrafted / _requiredCrafts,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = PlayerStats.TotalItemsCrafted;
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredCrafts}) items crafted.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: extra materials for the next build.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronOre,   16), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Coal,      16), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.CopperOre, 8),  false);
            ModLoader.LogSystem.SendFeedback("+16 IronOre, +16 Coal, +8 CopperOre.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure TotalItemsCrafted meets this tier.
        /// On revoke, clamp just below this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.TotalItemsCrafted = Math.Max(stats.TotalItemsCrafted, _requiredCrafts);
            }
            else
            {
                // Drop crafted items one less, preserving the previous vanilla crafted items tiers.
                // stats.TotalItemsCrafted = Math.Min(stats.TotalItemsCrafted, _requiredCrafts - 1);

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.TotalItemsCrafted = 0;
            }
        }
        #endregion
    }
}