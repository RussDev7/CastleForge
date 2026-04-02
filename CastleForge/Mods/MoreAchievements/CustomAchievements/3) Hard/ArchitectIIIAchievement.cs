/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// Architect III (Difficulty: Hard)
    /// Place a huge number of basic building blocks (bigger grind tier).
    ///
    /// Uses the same tracked block IDs as Architect I and a higher
    /// RequiredPlacements threshold.
    /// </summary>
    internal sealed class ArchitectIIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ArchitectIIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        /// <summary>
        /// Total placements required across all tracked block IDs.
        /// </summary>
        public const int RequiredPlacements = 10000;

        /// <summary>
        /// Block IDs that count toward Architect I / II.
        /// Extend this list if you want more building blocks to count.
        /// </summary>
        internal static readonly InventoryItemIDs[] s_trackedBlockIds =
        {
            #region Core Terrain / Structural

            InventoryItemIDs.DirtBlock,
            InventoryItemIDs.RockBlock,
            InventoryItemIDs.WoodBlock,
            InventoryItemIDs.SandBlock,
            InventoryItemIDs.Snow,
            InventoryItemIDs.Ice,
            InventoryItemIDs.LogBlock,
            InventoryItemIDs.BloodStoneBlock,
            InventoryItemIDs.SpaceRockInventory,

            #endregion

            #region Walls

            InventoryItemIDs.IronWall,
            InventoryItemIDs.CopperWall,
            InventoryItemIDs.GoldenWall,
            InventoryItemIDs.DiamondWall,

            #endregion

            #region Lighting / Glass

            InventoryItemIDs.LanternBlock,
            InventoryItemIDs.LanternFancyBlock,

            InventoryItemIDs.GlassWindowWood,
            InventoryItemIDs.GlassWindowIron,
            InventoryItemIDs.GlassWindowGold,
            InventoryItemIDs.GlassWindowDiamond,

            // Don't include torches, this is to prevent light-spam builds from counting:
            // InventoryItemIDs.Torch,

            #endregion

            #region Crates / Storage

            InventoryItemIDs.Crate,
            InventoryItemIDs.CopperContainer,
            InventoryItemIDs.StoneContainer,
            InventoryItemIDs.IronContainer,
            InventoryItemIDs.GoldContainer,
            InventoryItemIDs.DiamondContainer,
            InventoryItemIDs.BloodstoneContainer,

            #endregion

            #region Utility / Workstations

            InventoryItemIDs.TeleportStation,

            #endregion

            #region Optional: Spawn Points

            InventoryItemIDs.SpawnBasic,

            #endregion
        };

        private string _lastMessage;
        private int    _lastTotalPlaced = -1;

        #region Helper: Total Placements

        /// <summary>
        /// Returns the total "Used" count across all tracked building blocks.
        /// </summary>
        internal static int GetTotalPlacedBuildingBlocks(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;

            foreach (var id in s_trackedBlockIds)
            {
                var s = stats.GetItemStats(id);
                if (s != null)
                    total += s.Used;
            }

            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                int total = GetTotalPlacedBuildingBlocks(PlayerStats);
                return total >= RequiredPlacements;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalPlacedBuildingBlocks(PlayerStats);
                return MathHelper.Clamp(
                    (float)total / RequiredPlacements,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalPlacedBuildingBlocks(PlayerStats);
                if (total != _lastTotalPlaced)
                {
                    _lastTotalPlaced = total;
                    _lastMessage =
                        $"({total}/{RequiredPlacements}) basic building blocks placed.";
                }

                return _lastMessage ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a stack of wood to keep building.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.WoodBlock,   256), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RockBlock,   64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondWall, 64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldenWall,  64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronWall,    64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.CopperWall,  64),  false);
            ModLoader.LogSystem.SendFeedback("+256 WoodBlock, +256 RockBlock, +64 DiamondWall, +64 GoldenWall +64 IronWall, +64 CopperWall.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// grant  == true : top up block "Used" counts so the combined total
        ///                  is at least RequiredPlacements.
        /// grant  == false: zero out Used for all tracked block IDs.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                int total = GetTotalPlacedBuildingBlocks(stats);
                if (total < RequiredPlacements)
                {
                    int missing = RequiredPlacements - total;

                    // Dump the missing placements into WoodBlock by default.
                    var s = stats.GetItemStats(InventoryItemIDs.WoodBlock);
                    if (s != null)
                    {
                        s.Used += missing;
                    }
                }
            }
            else
            {
                // Clear Used counts for all tracked building blocks.
                foreach (var id in s_trackedBlockIds)
                {
                    var s = stats.GetItemStats(id);
                    if (s != null)
                        s.Used = 0;
                }
            }
        }
        #endregion
    }
}