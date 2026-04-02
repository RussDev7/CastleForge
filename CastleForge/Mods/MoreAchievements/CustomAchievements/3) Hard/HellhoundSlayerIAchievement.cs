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
    /// "Hellhound Slayer I" - get a high number of Hell-enemy kills.
    /// Sums <see cref="CastleMinerZPlayerStats.ItemStats.KillsHell"/> over all items.
    /// </summary>
    internal sealed class HellhoundSlayerAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public HellhoundSlayerAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 250;
        }

        #region Fields

        private readonly int _requiredKills;

        private static readonly InventoryItemIDs[] _allItems =
            (InventoryItemIDs[])Enum.GetValues(typeof(InventoryItemIDs));

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetTotalHellKills(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in _allItems)
            {
                var s = stats.GetItemStats(id);
                if (s != null)
                    total += s.KillsHell;
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetTotalHellKills(PlayerStats) >= _requiredKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalHellKills(PlayerStats);
                return MathHelper.Clamp((float)total / _requiredKills, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalHellKills(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredKills}) Hell enemies killed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: high-tier gear suited for Hell runs.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneAssultRifle, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondBullets,        256), false);
            ModLoader.LogSystem.SendFeedback("+1 BloodStoneAssultRifle, +256 DiamondBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump a representative weapon's Hell-kills to reach the tier.
        /// On revoke, clear Hell kills and restore a small amount on that weapon.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            const InventoryItemIDs repItem = InventoryItemIDs.AssultRifle;

            int total  = GetTotalHellKills(stats);
            int target = _requiredKills;

            if (grant)
            {
                if (total < target)
                {
                    int currentRep = PlayerStatReflectionHelper
                        .GetItemStatKillsHellRaw(stats, repItem);

                    int needed = target - total;
                    PlayerStatReflectionHelper
                        .SetItemStatKillsHellRaw(stats, repItem, currentRep + needed);
                }
            }
            else
            {
                int maxWhenRevoked = 0;

                if (total > maxWhenRevoked)
                {
                    PlayerStatReflectionHelper.ClearAllItemStatsHellKillsRaw(stats);

                    PlayerStatReflectionHelper
                        .SetItemStatKillsHellRaw(stats, repItem, maxWhenRevoked);
                }
            }
        }
        #endregion
    }
}