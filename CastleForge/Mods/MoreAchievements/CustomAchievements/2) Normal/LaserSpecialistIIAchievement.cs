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
    /// "Laser Specialist II" - kill enemies with any laser weapon.
    /// Uses <see cref="CastleMinerZPlayerStats.EnemiesKilledWithLaserWeapon"/>.
    /// </summary>
    internal sealed class LaserSpecialistIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public LaserSpecialistIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredLaserKills = 50;
        }

        #region Fields

        private readonly int _requiredLaserKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.EnemiesKilledWithLaserWeapon >= _requiredLaserKills;
            }
        }

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
        /// Reward: a pile of laser ammo.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldLaserSword, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.LaserBullets,   256), false);
            ModLoader.LogSystem.SendFeedback("+1 GoldLaserSword, +256 LaserBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure the laser kill stat meets this tier.
        /// On revoke, clamp to one less than this tier.
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