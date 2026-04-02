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
    /// "Survivor II" - survive 30 in-game days.
    /// Uses <see cref="CastleMinerZPlayerStats.MaxDaysSurvived"/>.
    /// </summary>
    internal sealed class SurvivorIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public SurvivorIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _days = 30;
        }

        #region Fields

        private readonly int _days;
        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.MaxDaysSurvived >= _days;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.MaxDaysSurvived / _days,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int days = PlayerStats.MaxDaysSurvived;
                if (_lastAmount != days)
                {
                    _lastAmount = days;
                    _lastString = $"({days}/{_days}) days survived.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: mid-tier tools for continued survival.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldPickAxe,     1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldSpade,       1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldAssultRifle, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronBullets,     512), false);
            ModLoader.LogSystem.SendFeedback("+1 GoldPickAxe, +1 GoldSpade, +1 GoldAssultRifle, +512 IronBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure MaxDaysSurvived ≥ 30.
        /// On revoke, clamp to 29 or less.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, _days);
            }
            else
            {
                // Drop days survived back to zero.
                stats.MaxDaysSurvived = Math.Min(stats.MaxDaysSurvived, 0);
            }
        }
        #endregion
    }
}