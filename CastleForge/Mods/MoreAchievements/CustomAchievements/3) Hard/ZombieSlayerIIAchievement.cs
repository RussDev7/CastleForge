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
    /// "Zombie Slayer II" - reach a very high zombie kill count.
    /// Sums <see cref="CastleMinerZPlayerStats.ItemStats.KillsZombies"/> over all items.
    /// </summary>
    internal sealed class ZombieSlayerIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ZombieSlayerIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 500;
        }

        #region Fields

        private readonly int _requiredKills;

        private static readonly InventoryItemIDs[] _allItems =
            (InventoryItemIDs[])Enum.GetValues(typeof(InventoryItemIDs));

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetTotalZombieKills(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in _allItems)
            {
                var s = stats.GetItemStats(id);
                if (s != null)
                    total += s.KillsZombies;
            }
            return total;
        }

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetTotalZombieKills(PlayerStats) >= _requiredKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalZombieKills(PlayerStats);
                return MathHelper.Clamp((float)total / _requiredKills, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalZombieKills(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredKills}) zombies killed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: diamond bullets and explosives for the apocalypse.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;

            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondBullets, 256), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,            64),  false);
            ModLoader.LogSystem.SendFeedback("+512 DiamondBullets, +64 TNT.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump a representative weapon's zombie kills to reach the tier.
        /// On revoke, clear zombie kills and restore a small amount on that weapon.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            const InventoryItemIDs repItem = InventoryItemIDs.AssultRifle;

            int total = GetTotalZombieKills(stats);
            int target = _requiredKills;

            if (grant)
            {
                if (total < target)
                {
                    int currentRep = PlayerStatReflectionHelper
                        .GetItemStatKillsZombiesRaw(stats, repItem);

                    int needed = target - total;
                    PlayerStatReflectionHelper
                        .SetItemStatKillsZombiesRaw(stats, repItem, currentRep + needed);
                }
            }
            else
            {
                int maxWhenRevoked = 0; // target - 1;

                if (total > maxWhenRevoked)
                {
                    // Clear zombie kills across all items, then give back maxWhenRevoked on rep weapon.
                    PlayerStatReflectionHelper.ClearAllItemStatsZombieKillsRaw(stats);

                    PlayerStatReflectionHelper
                        .SetItemStatKillsZombiesRaw(stats, repItem, maxWhenRevoked);
                }
            }
        }
        #endregion
    }
}