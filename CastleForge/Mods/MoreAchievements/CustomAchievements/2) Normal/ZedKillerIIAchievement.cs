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
    /// "Zed Killer II" - reach a higher lifetime kill count.
    /// Uses <see cref="CastleMinerZPlayerStats.TotalKills"/>.
    /// </summary>
    internal sealed class ZedKillerIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ZedKillerIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 250;
        }

        #region Fields

        private readonly int _requiredKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Unlocks once <see cref="CastleMinerZPlayerStats.TotalKills"/> ≥ required kill count.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
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
        /// Simple numeric progress string, cached.
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
        /// Reward: small bundle of building blocks for your trouble.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Bullets,     128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronBullets, 64),  false);
            ModLoader.LogSystem.SendFeedback("+128 Bullets, +64 IronBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensures the local TotalKills stat is at least the required threshold.
        /// On revoke, reduce TotalKills to just below the threshold for this achievement.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.TotalKills = Math.Max(stats.TotalKills, _requiredKills);
            }
            else
            {
                // Drop back to one less than this achievement's threshold.
                // stats.TotalKills = Math.Min(stats.TotalKills, _requiredKills - 1);

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.TotalKills = 0;
            }
        }
        #endregion
    }
}