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
    /// "Place Glass Windows" - place a small number of glass windows.
    ///
    /// Counts ItemStats.Used across:
    ///   GlassWindowWood, GlassWindowIron, GlassWindowGold, GlassWindowDiamond.
    /// </summary>
    internal sealed class PlaceGlassWindowsAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public PlaceGlassWindowsAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        private const int RequiredPlaced = 10;

        #region Static Glass ID List

        private static readonly InventoryItemIDs[] s_glassIds =
        {
            InventoryItemIDs.GlassWindowWood,
            InventoryItemIDs.GlassWindowIron,
            InventoryItemIDs.GlassWindowGold,
            InventoryItemIDs.GlassWindowDiamond,
        };

        #endregion

        #region Fields

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Helpers

        private static int GetGlassPlacedTotal(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in s_glassIds)
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
                return GetGlassPlacedTotal(PlayerStats) >= RequiredPlaced;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int placed = GetGlassPlacedTotal(PlayerStats);
                return MathHelper.Clamp((float)placed / RequiredPlaced, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int placed = GetGlassPlacedTotal(PlayerStats);
                if (_lastAmount != placed)
                {
                    _lastAmount = placed;
                    _lastString = $"({placed}/{RequiredPlaced}) glass windows placed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: some extra glass windows for further decorating.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GlassWindowWood, 8), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GlassWindowIron, 4), false);

            ModLoader.LogSystem.SendFeedback("+8 GlassWindowWood, +4 GlassWindowIron.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// grant == true:
        ///   Ensure the combined Used count across all glass window IDs is >= RequiredPlaced.
        ///   (Adds to the first glass ID as needed.)
        /// grant == false:
        ///   Reset Used = 0 for all glass window IDs.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                int total = GetGlassPlacedTotal(stats);
                if (total < RequiredPlaced && s_glassIds.Length > 0)
                {
                    int needed = RequiredPlaced - total;
                    var primaryStats = stats.GetItemStats(s_glassIds[0]);
                    if (primaryStats != null)
                        primaryStats.Used += needed;
                }
            }
            else
            {
                foreach (var id in s_glassIds)
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