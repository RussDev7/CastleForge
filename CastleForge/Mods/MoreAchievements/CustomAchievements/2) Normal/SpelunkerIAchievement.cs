/*
SPDX-License-Identifier: GPL-3.0-or-laterCopyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// "Spelunker I" - mine a large number of stone/rock blocks.
    /// Uses <see cref="CastleMinerZPlayerStats.BlocksDugCount(BlockTypeEnum)"/> for <see cref="BlockTypeEnum.Rock"/>.
    /// </summary>
    internal sealed class SpelunkerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public SpelunkerIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        #region Fields

        private const int       RequiredBlocks = 250;
        private const BlockTypeEnum TargetType = BlockTypeEnum.Rock;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        private static int GetStoneBlocksDug(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            return stats.BlocksDugCount(TargetType);
        }

        protected override bool IsSastified
        {
            get
            {
                return GetStoneBlocksDug(PlayerStats) >= RequiredBlocks;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int count = GetStoneBlocksDug(PlayerStats);
                return MathHelper.Clamp((float)count / RequiredBlocks, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int count = GetStoneBlocksDug(PlayerStats);
                if (_lastAmount != count)
                {
                    _lastAmount = count;
                    _lastString = $"({count}/{RequiredBlocks}) stone blocks mined.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: TNT to continue your mining career.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronPickAxe, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,         16), false);
            ModLoader.LogSystem.SendFeedback("+1 IronPickAxe, +16 TNT.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, bump the stone count up to the target if needed.
        /// On revoke, bring it just under the requirement.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            // Use reflection helpers to keep behavior consistent with BlockBreaker.
            var current = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, TargetType);

            if (grant)
            {
                if (current < RequiredBlocks)
                {
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, TargetType, RequiredBlocks);
                }
            }
            else
            {
                int maxWhenRevoked = 0; // RequiredBlocks - 1;
                if (current > maxWhenRevoked)
                {
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, TargetType, maxWhenRevoked);
                }
            }
        }
        #endregion
    }
}