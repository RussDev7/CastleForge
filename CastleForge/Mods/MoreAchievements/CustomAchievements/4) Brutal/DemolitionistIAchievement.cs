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
    /// "Demolitionist I" - kill 1,000 enemies with explosives.
    /// Uses <see cref="CastleMinerZPlayerStats.EnemiesKilledWithTNT"/> +
    /// <see cref="CastleMinerZPlayerStats.EnemiesKilledWithGrenade"/>.
    /// </summary>
    internal sealed class DemolitionistIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public DemolitionistIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 1000;
        }

        #region Fields

        private readonly int _requiredKills;
        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetTotalExplosiveKills(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            return stats.EnemiesKilledWithTNT + stats.EnemiesKilledWithGrenade;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetTotalExplosiveKills(PlayerStats) >= _requiredKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalExplosiveKills(PlayerStats);
                return MathHelper.Clamp((float)total / _requiredKills, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalExplosiveKills(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredKills}) explosive kills.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: more TNT, grenades, and C4.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,                     128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Grenade,                 128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.C4,                      64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.AdvancedGrenadeLauncher, 1),   false); // Special reward (unuused ingame).
            ModLoader.LogSystem.SendFeedback("+128 TNT, +128 Grenade, +64 C4, +1 AdvancedGrenadeLauncher.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure combined TNT+grenade kills meet this tier by bumping TNT kills.
        /// On revoke, clamp below this tier by reducing TNT kills.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            int total  = GetTotalExplosiveKills(stats);
            int target = _requiredKills;

            if (grant)
            {
                if (total < target)
                {
                    int needed = target - total;
                    stats.EnemiesKilledWithTNT += needed;
                }
            }
            else
            {
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int maxWhenRevoked = target - 1;
                if (total > maxWhenRevoked)
                {
                    int over = total - maxWhenRevoked;
                    stats.EnemiesKilledWithTNT =
                        Math.Max(0, stats.EnemiesKilledWithTNT - over);
                }
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.EnemiesKilledWithTNT = 0;
            }
        }
        #endregion
    }
}