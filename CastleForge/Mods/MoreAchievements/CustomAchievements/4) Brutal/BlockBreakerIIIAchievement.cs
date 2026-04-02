/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// "Block Breaker III" - dig a combined total of 25,000 blocks across all block types.
    /// Uses <see cref="CastleMinerZPlayerStats.BlocksDugCount(BlockTypeEnum)"/> summed across all types.
    /// </summary>
    internal sealed class BlockBreakerIIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public BlockBreakerIIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        #region Fields

        private const int RequiredBlocks = 25000;

        private static readonly BlockTypeEnum[] _allBlockTypes =
            (BlockTypeEnum[])Enum.GetValues(typeof(BlockTypeEnum));

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetTotalBlocksDug(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (BlockTypeEnum type in _allBlockTypes)
            {
                total += stats.BlocksDugCount(type);
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetTotalBlocksDug(PlayerStats) >= RequiredBlocks;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalBlocksDug(PlayerStats);
                return MathHelper.Clamp(
                    (float)total / RequiredBlocks,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalBlocksDug(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{RequiredBlocks}) blocks dug.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: explosives and materials for serious mining.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;

            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodstonePickAxe, 32), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,               64), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.C4,                32), false);
            ModLoader.LogSystem.SendFeedback("+1 BloodstonePickAxe, +64 TNT, +32 C4.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump a representative block type (Dirt) to reach the target total.
        /// On revoke, clear and restore progress to just below this tier (4,999).
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var blockType = BlockTypeEnum.Dirt;
            int total     = PlayerStatReflectionHelper.GetBlocksDugTotalRaw(stats);

            if (grant)
            {
                int target = RequiredBlocks;
                if (total < target)
                {
                    int currentDirt = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, blockType);
                    int newDirt     = currentDirt + (target - total);
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, blockType, newDirt);
                }
            }
            else
            {
                int maxWhenRevoked = 0; // RequiredBlocks - 1;
                if (total > maxWhenRevoked)
                {
                    PlayerStatReflectionHelper.ClearBlocksDugRaw(stats);
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, blockType, maxWhenRevoked);
                }
            }
        }
        #endregion
    }
}