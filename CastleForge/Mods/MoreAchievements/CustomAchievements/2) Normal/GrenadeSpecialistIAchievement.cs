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
    /// "Grenade Specialist I" - rack up kills using grenades.
    /// Uses <see cref="CastleMinerZPlayerStats.EnemiesKilledWithGrenade"/>.
    /// </summary>
    internal sealed class GrenadeSpecialistIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public GrenadeSpecialistIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredGrenadeKills = 25;
        }

        #region Fields

        private readonly int _requiredGrenadeKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.EnemiesKilledWithGrenade >= _requiredGrenadeKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.EnemiesKilledWithGrenade / _requiredGrenadeKills,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int kills = PlayerStats.EnemiesKilledWithGrenade;
                if (_lastAmount != kills)
                {
                    _lastAmount = kills;
                    _lastString = $"({kills}/{_requiredGrenadeKills}) grenade kills.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: more grenades, obviously.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Grenade, 32),  false);
            ModLoader.LogSystem.SendFeedback("+32 Grenade.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure the grenade kill stat meets this tier.
        /// On revoke, clamp to one less than this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.EnemiesKilledWithGrenade =
                    Math.Max(stats.EnemiesKilledWithGrenade, _requiredGrenadeKills);
            }
            else
            {
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int newVal = Math.Min(stats.EnemiesKilledWithGrenade, _requiredGrenadeKills - 1);
                if (newVal < 0) newVal = 0;
                stats.EnemiesKilledWithGrenade = newVal;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.EnemiesKilledWithGrenade = 0;
            }
        }
        #endregion
    }
}