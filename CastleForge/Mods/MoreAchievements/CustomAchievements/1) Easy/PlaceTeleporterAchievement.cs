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
    /// "Place Teleporter" - place your first Teleport Station.
    ///
    /// Uses <see cref="CastleMinerZPlayerStats.GetItemStats(InventoryItemIDs)"/> for
    /// <see cref="InventoryItemIDs.TeleportStation"/> and reads Used as the
    /// "placed" counter.
    /// </summary>
    internal sealed class PlaceTeleporterAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public PlaceTeleporterAchievement(
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

        private static int GetTeleporterPlacedCount(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            var s = stats.GetItemStats(InventoryItemIDs.TeleportStation);
            return (s != null) ? s.Used : 0;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetTeleporterPlacedCount(PlayerStats) >= RequiredPlaced;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int placed = GetTeleporterPlacedCount(PlayerStats);
                return MathHelper.Clamp((float)placed / RequiredPlaced, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int placed = GetTeleporterPlacedCount(PlayerStats);
                if (_lastAmount != placed)
                {
                    _lastAmount = placed;
                    _lastString = $"({placed}/{RequiredPlaced}) Teleport Stations placed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a TeleportGPS and some torches to celebrate the first base.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TeleportGPS, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch,       32), false);

            ModLoader.LogSystem.SendFeedback("+1 TeleportGPS, +32 Torch.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// grant == true:
        ///   Ensure TeleportStation.Used >= 1.
        /// grant == false:
        ///   Reset TeleportStation.Used back to 0 (safe for Steam).
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var s = stats.GetItemStats(InventoryItemIDs.TeleportStation);
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