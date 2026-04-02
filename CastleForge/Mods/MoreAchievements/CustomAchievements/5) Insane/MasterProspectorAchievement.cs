/*
SPDX-License-Identifier: GPL-3.0-or-laterCopyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// "Mine 1 of every minable block."
    /// "Master Prospector"
    /// A block type counts if:
    ///   • It is not a sentinel (Empty/Uninitialized/NumberOfBlocks).
    ///   • It is not in the explicit blacklist (spawners, bedrock, etc.).
    ///   • BlockType.CanBeDug && BlockType.CanBeTouched.
    ///   • BlockType.Hardness > 0 (skip weird non-diggable types).
    /// Unlocks when the player has dug at least one of each such block type.
    /// </summary>
    internal sealed class MasterProspectorAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public MasterProspectorAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        #region Fields

        private int    _lastDistinctCount  = -1;
        private string _lastProgressString = string.Empty;

        #endregion

        #region Static minable-block table

        // Things we NEVER want to require for this achievement
        // even if the BlockType flags say they're diggable.
        private static readonly HashSet<BlockTypeEnum> _explicitBlacklist =
            new HashSet<BlockTypeEnum>
            {
                BlockTypeEnum.NormalLowerDoor,
                BlockTypeEnum.StrongLowerDoor,
                BlockTypeEnum.TurretBlock,
                BlockTypeEnum.BombBlock,
                BlockTypeEnum.DeepLava,
                BlockTypeEnum.Torch,

                // BlockTypeEnum.Empty, BlockTypeEnum.Uninitialized, BlockTypeEnum.NumberOfBlocks,
            };

        // Cache of "minable" block types decided at runtime.
        public static readonly BlockTypeEnum[] _minableBlockTypes = BuildMinableBlockTypeList();

        // How many different block types you must mine at least once.
        private static readonly int RequiredDistinctTypes = _minableBlockTypes.Length;

        /// <summary>
        /// Build the list of block types that count for the achievement.
        /// Uses BlockType.Hardness / CanBeDug / CanBeTouched plus a manual blacklist.
        /// </summary>
        private static BlockTypeEnum[] BuildMinableBlockTypeList()
        {
            var result = new List<BlockTypeEnum>();

            foreach (BlockTypeEnum value in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                if (IsMinableBlockType(value))
                    result.Add(value);
            }

            // Optional: Log this once somewhere if you want to see the final list.
            // ModLoader.LogSystem.Log($"MineEveryBlock: tracking {result.Count} minable block types: {string.Join(", ", result.Select(b => b.ToString()))}.");

            return result.ToArray();
        }

        /// <summary>
        /// Returns true if the given BlockTypeEnum should count as
        /// "minable" for the purpose of this achievement.
        /// </summary>
        public static bool IsMinableBlockType(BlockTypeEnum block)
        {
            // Hard exclusions first.
            if (_explicitBlacklist.Contains(block))
                return false;

            // Sentinel / meta values you never see in the world.
            if (block == BlockTypeEnum.Empty ||
                block == BlockTypeEnum.Uninitialized ||
                block == BlockTypeEnum.NumberOfBlocks)
            {
                return false;
            }

            // Ask the engine for this type's default properties.
            var bt = BlockType.GetType(block);
            if (bt == null)
                return false;

            // Must be diggable and tangible.
            if (!bt.CanBeDug)
                return false;

            if (!bt.CanBeTouched)
                return false;

            if (bt.Hardness >= 5)
                return false;

            // Skip things with 0 or negative hardness (typically non-solid / weird).
            if (bt.Hardness <= 0)
                return false;

            return true;
        }

        /// <summary>
        /// How many distinct "minable" block types has this player dug at least once?
        /// Uses CastleMinerZPlayerStats.BlocksDugCount(blockType).
        /// </summary>
        private static int GetDistinctMinableBlocksDug(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int distinct = 0;

            foreach (var block in _minableBlockTypes)
            {
                if (stats.BlocksDugCount(block) > 0)
                    distinct++;
            }

            return distinct;
        }
        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// True once the player has dug at least one of every tracked minable block type.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                int distinct = GetDistinctMinableBlocksDug(PlayerStats);
                return distinct >= RequiredDistinctTypes;
            }
        }

        /// <summary>
        /// Normalized progress: 0..1 = (distinct types dug) / (types required).
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                int distinct = GetDistinctMinableBlocksDug(PlayerStats);
                if (RequiredDistinctTypes <= 0)
                    return 0f;

                return MathHelper.Clamp(
                    (float)distinct / (float)RequiredDistinctTypes,
                    0f,
                    1f);
            }
        }

        /// <summary>
        /// Cached text like "(12/47) block types mined at least once."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int distinct = GetDistinctMinableBlocksDug(PlayerStats);

                if (distinct != _lastDistinctCount)
                {
                    _lastDistinctCount = distinct;
                    _lastProgressString =
                        $"({distinct}/{RequiredDistinctTypes}) block types mined at least once.";
                }

                return _lastProgressString;
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

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondDoor,       12), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.SpaceRock,         12), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodstonePickAxe, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,               64), false);
            ModLoader.LogSystem.SendFeedback("+12 DiamondDoor, +12 SpaceRock, +1 BloodstonePickAxe, +64 TNT.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Pushes or pulls the player's dug-block stats to match this
        /// "mine 1 of each minable block" achievement.
        ///
        /// grant == true:
        ///   • For every eligible minable block type, ensure the raw dug count is at least 1.
        ///
        /// grant == false:
        ///   • Zero out at least one minable block type so the "all blocks >= 1" condition
        ///     becomes false again, without nuking the entire table.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                // Ensure: For every minable block type, dug count >= 1.
                foreach (var type in _minableBlockTypes)
                {
                    int current = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, type);
                    if (current <= 0)
                    {
                        // Mark as "dug once".
                        PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, type, 1);
                    }
                }
            }
            else
            {
                // Zero out all minable block types that where mined.
                foreach (var type in _minableBlockTypes)
                {
                    int current = PlayerStatReflectionHelper.GetBlocksDugCountRaw(stats, type);
                    if (current > 0)
                    {
                        PlayerStatReflectionHelper.SetBlocksDugCountRaw(stats, type, 0);

                        // To revoke, we only need to break the "all >= 1" condition.
                        // Zero out one minable block's count; that's enough to make
                        // HasMinedAllBlocks() return false again.
                        // break; // Only touch one type.
                    }
                }
            }
        }
        #endregion
    }
}