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
    /// "Zed Killer III" - reach a very high lifetime kill count.
    /// Uses <see cref="CastleMinerZPlayerStats.TotalKills"/>.
    /// </summary>
    internal sealed class ZedKillerIIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ZedKillerIIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 2000;
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
        /// Reward: high-end ammo and explosives.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;

            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondBullets, 256), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,            32),  false);
            ModLoader.LogSystem.SendFeedback("+256 DiamondBullets, +32 TNT.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure TotalKills meets this tier.
        /// On revoke, clamp to one less than this tier.
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
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int maxWhenRevoked = _requiredKills - 1;
                stats.TotalKills   = Math.Min(stats.TotalKills, maxWhenRevoked);
                if (stats.TotalKills < 0)
                    stats.TotalKills = 0;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.TotalKills = 0;
            }
        }
        #endregion
    }
}