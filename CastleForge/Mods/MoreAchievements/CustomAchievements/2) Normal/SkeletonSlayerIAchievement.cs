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
    /// "Skeleton Slayer I" - rack up skeleton kills with any weapon.
    /// Sums <see cref="CastleMinerZPlayerStats.ItemStats.KillsSkeleton"/> across all <see cref="InventoryItemIDs"/>.
    /// </summary>
    internal sealed class SkeletonSlayerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public SkeletonSlayerIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 50;
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
        /// Reward: some higher-tier bullets for future hunts.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronBullets, 128), false);
            ModLoader.LogSystem.SendFeedback("+128 IronBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump kills for a representative weapon to satisfy the tier.
        /// On revoke, lower that weapon's skeleton-kill stat to bring the total under the tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            // Representative bucket: Plain assault rifle.
            const InventoryItemIDs repItem = InventoryItemIDs.AssultRifle;

            int total  = GetTotalSkeletonKills(stats);
            int target = _requiredKills;

            if (grant)
            {
                // Ensure at least 'target' skeleton kills across all weapons.
                if (total < target)
                {
                    int currentRep = PlayerStatReflectionHelper
                        .GetItemStatKillsSkeletonRaw(stats, repItem);

                    int needed = target - total;
                    int newRep = currentRep + needed;

                    PlayerStatReflectionHelper
                        .SetItemStatKillsSkeletonRaw(stats, repItem, newRep);
                }
            }
            else
            {
                // When revoking, clamp total skeleton kills back to (target - 1).
                int maxWhenRevoked = 0; // target - 1;
                if (total > maxWhenRevoked)
                {
                    // Heavy-handed "BlockBreaker-style" reset:
                    // 1) Clear all per-item stats.
                    // 2) Give back maxWhenRevoked skeleton kills on the representative weapon.
                    // PlayerStatReflectionHelper.ClearAllItemStatsRaw(stats);

                    // Instead of wiping ALL item stats, just wipe skeleton kills:
                    PlayerStatReflectionHelper.ClearAllItemStatsSkeletonKillsRaw(stats);

                    PlayerStatReflectionHelper
                        .SetItemStatKillsSkeletonRaw(stats, repItem, maxWhenRevoked);

                    // Optional debug:
                    // ModLoader.LogSystem.Log("Cleared AllItemStats + set AssaultRifle.KillsSkeleton for revoke.");
                }
            }
        }
        #endregion
    }
}