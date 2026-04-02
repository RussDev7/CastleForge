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
    /// "Alien Stalker I" - trigger repeated alien encounters.
    /// Uses <see cref="CastleMinerZPlayerStats.AlienEncounters"/>.
    /// </summary>
    internal sealed class AlienStalkerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public AlienStalkerIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredEncounters = 10;
        }

        #region Fields

        private readonly int _requiredEncounters;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.AlienEncounters >= _requiredEncounters;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.AlienEncounters / _requiredEncounters,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = PlayerStats.AlienEncounters;
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredEncounters}) alien encounters.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: space ammo for your troubles.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;

            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldBullets, 256), false);
            ModLoader.LogSystem.SendFeedback("+256 GoldBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure AlienEncounters meets this tier.
        /// On revoke, clamp to one less than this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.AlienEncounters = Math.Max(stats.AlienEncounters, _requiredEncounters);
            }
            else
            {
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int maxWhenRevoked    = _requiredEncounters - 1;
                stats.AlienEncounters = Math.Min(stats.AlienEncounters, maxWhenRevoked);
                if (stats.AlienEncounters < 0)
                    stats.AlienEncounters = 0;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.AlienEncounters = 0;
            }
        }
        #endregion
    }
}