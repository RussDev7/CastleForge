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
    /// Easy building achievement:
    /// "Place Spawn Point" - place your first basic spawn point.
    ///
    /// Uses ItemStats.Used for <see cref="InventoryItemIDs.SpawnBasic"/>.
    /// </summary>
    internal sealed class PlaceSpawnPointAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public PlaceSpawnPointAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        private const int RequiredPlaced = 1;

        #region Fields

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetSpawnPlacedCount(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            var s = stats.GetItemStats(InventoryItemIDs.SpawnBasic);
            return (s != null) ? s.Used : 0;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetSpawnPlacedCount(PlayerStats) >= RequiredPlaced;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int placed = GetSpawnPlacedCount(PlayerStats);
                return MathHelper.Clamp((float)placed / RequiredPlaced, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int placed = GetSpawnPlacedCount(PlayerStats);
                if (_lastAmount != placed)
                {
                    _lastAmount = placed;
                    _lastString = $"({placed}/{RequiredPlaced}) spawn points placed.";
                }
                return _lastString;
            }
        }

        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a small starter kit to decorate your first base.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Crate, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Door,  1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch, 16), false);

            ModLoader.LogSystem.SendFeedback("+1 Crate, +1 Door, +16 Torch.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// grant == true:
        ///   Ensure SpawnBasic.Used >= 1.
        /// grant == false:
        ///   Reset SpawnBasic.Used back to 0.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var s = stats.GetItemStats(InventoryItemIDs.SpawnBasic);
            if (s == null)
                return;

            if (grant)
            {
                if (s.Used < RequiredPlaced)
                    s.Used = RequiredPlaced;
            }
            else
            {
                s.Used = 0;
            }
        }
        #endregion
    }
}