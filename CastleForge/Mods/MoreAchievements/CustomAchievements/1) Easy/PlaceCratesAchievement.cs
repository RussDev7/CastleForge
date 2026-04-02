/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
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
    /// Easy building achievement:
    /// "Place Crates" - place a small number of crates/containers.
    ///
    /// Counts ItemStats.Used for:
    ///   Crate, StoneContainer, CopperContainer, IronContainer,
    ///   GoldContainer, DiamondContainer, BloodstoneContainer.
    /// </summary>
    internal sealed class PlaceCratesAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public PlaceCratesAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        private const int RequiredPlaced = 10;

        #region Static crate ID list

        private static readonly InventoryItemIDs[] s_crateIds =
        {
            InventoryItemIDs.Crate,
            InventoryItemIDs.StoneContainer,
            InventoryItemIDs.CopperContainer,
            InventoryItemIDs.IronContainer,
            InventoryItemIDs.GoldContainer,
            InventoryItemIDs.DiamondContainer,
            InventoryItemIDs.BloodstoneContainer,
        };

        #endregion

        #region Fields

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetCratesPlacedTotal(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in s_crateIds)
            {
                var s = stats.GetItemStats(id);
                if (s != null)
                    total += Math.Max(0, s.Used);
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetCratesPlacedTotal(PlayerStats) >= RequiredPlaced;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int placed = GetCratesPlacedTotal(PlayerStats);
                return MathHelper.Clamp((float)placed / RequiredPlaced, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int placed = GetCratesPlacedTotal(PlayerStats);
                if (_lastAmount != placed)
                {
                    _lastAmount = placed;
                    _lastString = $"({placed}/{RequiredPlaced}) crates or containers placed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a few more containers to expand storage.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Crate,          2), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.StoneContainer, 2), false);

            ModLoader.LogSystem.SendFeedback("+2 Crate, +2 StoneContainer.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// grant == true:
        ///   Ensure the combined Used count across all crate/container IDs is >= RequiredPlaced.
        ///   (Adds the missing count to the first crate ID.)
        /// grant == false:
        ///   Reset Used = 0 for all crate/container IDs.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                int total = GetCratesPlacedTotal(stats);
                if (total < RequiredPlaced && s_crateIds.Length > 0)
                {
                    int needed = RequiredPlaced - total;
                    var primaryStats = stats.GetItemStats(s_crateIds[0]);
                    if (primaryStats != null)
                        primaryStats.Used += needed;
                }
            }
            else
            {
                foreach (var id in s_crateIds)
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