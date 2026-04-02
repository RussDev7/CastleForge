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
    /// "Laser Specialist I" - kill a number of enemies with any laser weapon.
    /// Uses <see cref="CastleMinerZPlayerStats.EnemiesKilledWithLaserWeapon"/>.
    /// </summary>
    internal sealed class LaserSpecialistIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public LaserSpecialistIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName,
                   manager,
                   name,
                   description)
        {
            _requiredLaserKills = 10;
        }

        #region Fields

        private readonly int _requiredLaserKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Unlocks once <see cref="CastleMinerZPlayerStats.EnemiesKilledWithLaserWeapon"/> ≥ required.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                // Normal stat check.
                return PlayerStats.EnemiesKilledWithLaserWeapon >= _requiredLaserKills;
            }
        }

        /// <summary>
        /// 0-1 based on laser kills / required.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.EnemiesKilledWithLaserWeapon / _requiredLaserKills,
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
                int kills = PlayerStats.EnemiesKilledWithLaserWeapon;
                if (_lastAmount != kills)
                {
                    _lastAmount = kills;
                    _lastString = $"({kills}/{_requiredLaserKills}) laser kills.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a basic laser sword for embracing the light.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (player == null || inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.CopperLaserSword, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.LaserBullets,     128), false);
            ModLoader.LogSystem.SendFeedback("+1 CopperLaserSword, +128 LaserBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensures the laser kill stat is at least the required threshold.
        /// On revoke, leaves stats unchanged.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.EnemiesKilledWithLaserWeapon =
                    Math.Max(stats.EnemiesKilledWithLaserWeapon, _requiredLaserKills);
            }
            else
            {
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int newVal = Math.Min(stats.EnemiesKilledWithLaserWeapon, _requiredLaserKills - 1);
                if (newVal < 0) newVal = 0;
                stats.EnemiesKilledWithLaserWeapon = newVal;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.EnemiesKilledWithLaserWeapon = 0;
            }
        }
        #endregion
    }
}
