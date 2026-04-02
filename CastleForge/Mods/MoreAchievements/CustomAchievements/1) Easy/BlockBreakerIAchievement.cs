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
    /// "Block Breaker I" - dig a combined total of blocks across all block types.
    /// Uses <see cref="CastleMinerZPlayerStats.BlocksDugCount(BlockTypeEnum)"/>.
    /// </summary>
    internal sealed class BlockBreakerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public BlockBreakerIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName,
                   manager,
                   name,
                   description)
        {
        }

        #region Fields

        /// <summary>
        /// Total blocks dug required to unlock this achievement.
        /// </summary>
        private const int RequiredBlocks = 100;

        /// <summary>
        /// Cached list of all block types in the game so we can sum their dug counts.
        /// </summary>
        private static readonly BlockTypeEnum[] _allBlockTypes =
            (BlockTypeEnum[])Enum.GetValues(typeof(BlockTypeEnum));

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Computes the total number of blocks dug across all <see cref="BlockTypeEnum"/> values.
        /// </summary>
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

        /// <summary>
        /// Unlocks once total blocks dug ≥ <see cref="RequiredBlocks"/>.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                // Normal stat check.
                return GetTotalBlocksDug(PlayerStats) >= RequiredBlocks;
            }
        }

        /// <summary>
        /// 0-1 based on total blocks dug / RequiredBlocks.
        /// </summary>
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

        /// <summary>
        /// Simple numeric progress string, cached.
        /// </summary>
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
        /// Reward: a small pile of building material for proving you can dig.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.StonePickAxe, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch,        16), false);
            ModLoader.LogSystem.SendFeedback("+1 StonePickAxe, +16 Torch.");
        }

        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments for this achievement when granting or revoking it.
        /// Grant:
        ///   • Ensures the player has at least a target number of blocks dug (any type),
        ///     by bumping a representative block type (e.g. Dirt).
        /// Revoke:
        ///   • Optionally clamps or resets the BlocksDug stats (via reflection) so the
        ///     player sits just below the unlock threshold again.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            // For the "grant" case we'll bump a representative type (Dirt),
            // but we'll measure against total dug across all types.
            var blockType = BlockTypeEnum.Dirt;

            int total = PlayerStatReflectionHelper.GetBlocksDugTotalRaw(stats);
            // ModLoader.LogSystem.Log($"Total={total}.");

            if (grant)
            {
                // Ensure at least 100 blocks (any type) are recorded as dug.
                int target = 100;
                if (total < target)
                {
                    // Just bump Dirt up to fill the gap.
                    int currentDirt = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, blockType);
                    int newDirt     = currentDirt + (target - total);
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, blockType, newDirt);
                }
            }
            else
            {
                // When revoking, clamp total dug back to 99.
                int maxWhenRevoked = 0;
                if (total > maxWhenRevoked)
                {
                    // Nuke the entire BlocksDug list...
                    PlayerStatReflectionHelper.ClearBlocksDugRaw(stats);

                    // Give some progress back (e.g., 99 Dirt).
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, blockType, maxWhenRevoked);

                    // ModLoader.LogSystem.Log("Cleared + set Dirt to 99 for revoke.");
                }
            }
        }
        #endregion
    }
}