/*
SPDX-License-Identifier: GPL-3.0-or-laterCopyright (c) 2025 RussDev7
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
    /// "Zed Killer I" - reach a moderate lifetime kill count.
    /// Uses <see cref="CastleMinerZPlayerStats.TotalKills"/>.
    /// </summary>
    internal sealed class ZedKillerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ZedKillerIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName,
                   manager,
                   name,
                   description)
        {
            _requiredKills = 50;
        }

        #region Fields

        private readonly int _requiredKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Unlocks once <see cref="CastleMinerZPlayerStats.TotalKills"/> ≥ <see cref="_requiredKills"/>.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                // Normal stat check.
                return PlayerStats.TotalKills >= _requiredKills;
            }
        }

        /// <summary>
        /// 0-1 based on TotalKills / requiredKills.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.TotalKills / _requiredKills,
                    0f,
                    1f);
            }
        }

        /// <summary>
        /// Simple numeric progress string, cached for stability.
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int kills = PlayerStats.TotalKills;
                if (_lastAmount != kills)
                {
                    _lastAmount = kills;
                    _lastString = $"({kills}/{_requiredKills}) total kills.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward for showing consistent combat performance.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Bullets, 64), false);
            ModLoader.LogSystem.SendFeedback("+64 Bullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensures the local TotalKills stat is at least the required threshold.
        /// On revoke, reduce the TotalKills stat just bellow the required threshold.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                // Ensure at least the required kills for this achievement is recorded.
                stats.TotalKills = Math.Max(stats.TotalKills, _requiredKills);
            }
            else
            {
                // Reset just below the total required kill count for this achievement.
                stats.TotalKills = Math.Min(stats.TotalKills, 0 /* _requiredKills - 1 */);
            }
        }
        #endregion
    }
}