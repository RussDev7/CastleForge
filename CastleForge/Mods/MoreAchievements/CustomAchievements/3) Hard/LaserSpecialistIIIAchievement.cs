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
    /// "Laser Specialist III" - achieve a very high kill count with laser weapons.
    /// Uses <see cref="CastleMinerZPlayerStats.EnemiesKilledWithLaserWeapon"/>.
    /// </summary>
    internal sealed class LaserSpecialistIIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public LaserSpecialistIIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredLaserKills = 250;
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
        /// Reward: a stack of laser bullets and a PrecisionLaser.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;

            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronSpacePistol, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.PrecisionLaser,  512), false);
            ModLoader.LogSystem.SendFeedback("+1 IronSpacePistol, +512 LaserBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure laser kill stat meets this tier.
        /// On revoke, clamp just below this tier.
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
                int maxWhenRevoked = _requiredLaserKills - 1;
                int cur            = stats.EnemiesKilledWithLaserWeapon;
                stats.EnemiesKilledWithLaserWeapon = Math.Min(cur, maxWhenRevoked);
                if (stats.EnemiesKilledWithLaserWeapon < 0)
                    stats.EnemiesKilledWithLaserWeapon = 0;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.EnemiesKilledWithLaserWeapon = 0;
            }
        }
        #endregion
    }
}