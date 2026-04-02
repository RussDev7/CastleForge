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
    /// "Zed Killer V" - reach a massive lifetime kill count.
    /// Uses <see cref="CastleMinerZPlayerStats.TotalKills"/>.
    /// </summary>
    internal sealed class ZedKillerVAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ZedKillerVAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 50000;
        }

        #region Fields

        private readonly int _requiredKills;
        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.TotalKills >= _requiredKills;
            }
        }

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
        /// Reward: a large stockpile of high-tier ammo.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneLMGGun, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondBullets,   512), false);
            ModLoader.LogSystem.SendFeedback("+1 BloodStoneLMGGun, +512 DiamondBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure TotalKills ≥ 50,000.
        /// On revoke, clamp to 49,999.
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
                int maxWhenRevoked = 0; // _requiredKills - 1;
                stats.TotalKills   = Math.Min(stats.TotalKills, maxWhenRevoked);
                if (stats.TotalKills < 0)
                    stats.TotalKills = 0;
            }
        }
        #endregion
    }
}