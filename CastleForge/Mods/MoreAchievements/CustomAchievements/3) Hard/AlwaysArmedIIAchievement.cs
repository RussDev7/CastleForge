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
    /// "Always Armed II" - spend a long time with an assault rifle equipped.
    /// Uses <see cref="CastleMinerZPlayerStats.ItemStats.TimeHeld"/> for <see cref="InventoryItemIDs.AssultRifle"/>.
    /// </summary>
    internal sealed class AlwaysArmedIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public AlwaysArmedIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredMinutesHeld = 60.0;
        }

        #region Fields

        private readonly double _requiredMinutesHeld;

        private string _lastString;
        private double _lastMinutes = -1.0;

        #endregion

        #region Helpers

        private static CastleMinerZPlayerStats.ItemStats GetAssaultRifleStats(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return null;

            return stats.GetItemStats(InventoryItemIDs.AssultRifle);
        }

        private double GetMinutesHeld(CastleMinerZPlayerStats stats)
        {
            var assaultStats = GetAssaultRifleStats(stats);
            if (assaultStats == null)
                return 0.0;

            return assaultStats.TimeHeld.TotalMinutes;
        }
        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Unlocks once TimeHeld (minutes) with an assault rifle ≥ required.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return false;

                double minutes = GetMinutesHeld(stats);
                return minutes >= _requiredMinutesHeld;
            }
        }

        /// <summary>
        /// 0-1 based on minutes held / required.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return 0f;

                double minutes = GetMinutesHeld(stats);
                return MathHelper.Clamp(
                    (float)(minutes / _requiredMinutesHeld),
                    0f,
                    1f);
            }
        }

        /// <summary>
        /// Cached progress string showing minutes held so far.
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                var stats   = PlayerStats;
                double mins = stats != null ? GetMinutesHeld(stats) : 0.0;

                // Round to one decimal place for display.
                double rounded = Math.Round(mins, 1);

                if (Math.Abs(_lastMinutes - rounded) > double.Epsilon)
                {
                    _lastMinutes = rounded;
                    _lastString   = $"({rounded:0.0}/{_requiredMinutesHeld:0.0}) minutes holding an assault rifle.";
                }

                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: an extra assault rifle and ammo for your loyalty to firepower.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondAssultRifle, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldBullets,        128), false);
            ModLoader.LogSystem.SendFeedback("+1 DiamondAssultRifle, +128 GoldBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump TimeHeld up to the required minutes if needed.
        /// On revoke, reset assault-rifle TimeHeld to zero to cleanly drop this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var assaultStats = GetAssaultRifleStats(stats);
            if (assaultStats == null)
                return;

            if (grant)
            {
                double currentMinutes = assaultStats.TimeHeld.TotalMinutes;
                if (currentMinutes < _requiredMinutesHeld)
                {
                    assaultStats.TimeHeld = TimeSpan.FromMinutes(_requiredMinutesHeld);
                }
            }
            else
            {
                // Clearing only this item's hold time is safe and targeted.
                assaultStats.TimeHeld = TimeSpan.Zero;
            }
        }
        #endregion
    }
}