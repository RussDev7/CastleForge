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
    /// "Lumberjack I" - chop a large number of wood/tree blocks.
    /// Uses <see cref="CastleMinerZPlayerStats.BlocksDugCount(BlockTypeEnum)"/> for Log and Wood.
    /// </summary>
    internal sealed class LumberjackIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public LumberjackIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        #region Fields

        private const int RequiredBlocks = 250;

        private static readonly BlockTypeEnum[] _woodTypes =
        {
            BlockTypeEnum.Log,
            // BlockTypeEnum.Wood
        };

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        private static int GetWoodBlocksDug(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var type in _woodTypes)
                total += stats.BlocksDugCount(type);

            return total;
        }

        protected override bool IsSastified
        {
            get
            {
                return GetWoodBlocksDug(PlayerStats) >= RequiredBlocks;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetWoodBlocksDug(PlayerStats);
                return MathHelper.Clamp((float)total / RequiredBlocks, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetWoodBlocksDug(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{RequiredBlocks}) wood/tree blocks chopped.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a big bundle of wood.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.WoodBlock, 1), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.StoneAxe,  1), false);
            ModLoader.LogSystem.SendFeedback("+1 WoodBlock, +1 StoneAxe.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure at least RequiredBlocks worth of Wood are recorded.
        /// On revoke, clamp the primary wood type just under the threshold.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            // Representative block type: Log
            const BlockTypeEnum repType = BlockTypeEnum.Log;

            int current = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, repType);
            int target  = RequiredBlocks;    // 250.
            int maxWhenRevoked = target - 1; // 249.

            if (grant)
            {
                // Ensure at least 250 logs dug.
                if (current < target)
                {
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, repType, target);
                }
            }
            else
            {
                // Clamp just below the threshold so it doesn't re-unlock immediately.
                if (current > maxWhenRevoked)
                {
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, repType, maxWhenRevoked);
                }
            }
        }
        #endregion
    }
}