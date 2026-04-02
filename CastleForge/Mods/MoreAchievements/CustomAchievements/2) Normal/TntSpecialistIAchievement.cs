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
    /// "Tnt Specialist I" - rack up kills using TNT.
    /// Uses <see cref="CastleMinerZPlayerStats.EnemiesKilledWithTNT"/>.
    /// </summary>
    internal sealed class TntSpecialistIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public TntSpecialistIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredTntKills = 25;
        }

        #region Fields

        private readonly int _requiredTntKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.EnemiesKilledWithTNT >= _requiredTntKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.EnemiesKilledWithTNT / _requiredTntKills,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int kills = PlayerStats.EnemiesKilledWithTNT;
                if (_lastAmount != kills)
                {
                    _lastAmount = kills;
                    _lastString = $"({kills}/{_requiredTntKills}) TNT kills.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: more TNT, obviously.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT, 32),  false);
            ModLoader.LogSystem.SendFeedback("+32 TNT.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure the TNT kill stat meets this tier.
        /// On revoke, clamp to one less than this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.EnemiesKilledWithTNT =
                    Math.Max(stats.EnemiesKilledWithTNT, _requiredTntKills);
            }
            else
            {
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int newVal = Math.Min(stats.EnemiesKilledWithTNT, _requiredTntKills - 1);
                if (newVal < 0) newVal = 0;
                stats.EnemiesKilledWithTNT = newVal;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.EnemiesKilledWithTNT = 0;
            }
        }
        #endregion
    }
}