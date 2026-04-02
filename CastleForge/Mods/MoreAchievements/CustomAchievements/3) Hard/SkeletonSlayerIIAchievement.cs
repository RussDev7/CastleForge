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
    /// "Skeleton Slayer II" - reach a very high skeleton kill count.
    /// Sums <see cref="CastleMinerZPlayerStats.ItemStats.KillsSkeleton"/> over all items.
    /// </summary>
    internal sealed class SkeletonSlayerIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public SkeletonSlayerIIAchievement(
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

        private static int GetTotalSkeletonKills(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in _allItems)
            {
                var s = stats.GetItemStats(id);
                if (s != null)
                    total += s.KillsSkeleton;
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetTotalSkeletonKills(PlayerStats) >= _requiredKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalSkeletonKills(PlayerStats);
                return MathHelper.Clamp((float)total / _requiredKills, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalSkeletonKills(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredKills}) skeletons killed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: upgraded bullets and tools for dungeon runs.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;

            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldBullets,  256), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondSpade, 1),   false);
            ModLoader.LogSystem.SendFeedback("+256 GoldBullets, +1 DiamondSpade.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump a representative weapon's skeleton kills to reach the tier.
        /// On revoke, clear skeleton kills and restore a small amount on that weapon.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            const InventoryItemIDs repItem = InventoryItemIDs.AssultRifle;

            int total  = GetTotalSkeletonKills(stats);
            int target = _requiredKills;

            if (grant)
            {
                if (total < target)
                {
                    int currentRep = PlayerStatReflectionHelper
                        .GetItemStatKillsSkeletonRaw(stats, repItem);

                    int needed = target - total;
                    PlayerStatReflectionHelper
                        .SetItemStatKillsSkeletonRaw(stats, repItem, currentRep + needed);
                }
            }
            else
            {
                int maxWhenRevoked = 0; // target - 1;

                if (total > maxWhenRevoked)
                {
                    PlayerStatReflectionHelper.ClearAllItemStatsSkeletonKillsRaw(stats);

                    PlayerStatReflectionHelper
                        .SetItemStatKillsSkeletonRaw(stats, repItem, maxWhenRevoked);
                }
            }
        }
        #endregion
    }
}