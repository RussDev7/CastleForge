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
    /// "Always Armed I" - spend a long time holding any assault rifle.
    /// Uses <see cref="CastleMinerZPlayerStats.ItemStats.TimeHeld"/>
    /// across all assault rifle variants.
    /// </summary>
    internal sealed class AlwaysArmedIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public AlwaysArmedIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredHeld = TimeSpan.FromMinutes(10.0); // 10 min total.
        }

        #region Fields

        private readonly TimeSpan _requiredHeld;

        private string _lastString;
        private double _lastMinutes = -1.0;

        /// <summary>
        /// All inventory IDs that count as "assault rifles" for this achievement.
        /// </summary>
        private static readonly InventoryItemIDs[] AssaultIds =
        {
            InventoryItemIDs.AssultRifle,
            InventoryItemIDs.GoldAssultRifle,
            InventoryItemIDs.DiamondAssultRifle,
            InventoryItemIDs.BloodStoneAssultRifle,
            InventoryItemIDs.IronSpaceAssultRifle,
            InventoryItemIDs.CopperSpaceAssultRifle,
            InventoryItemIDs.GoldSpaceAssultRifle,
            InventoryItemIDs.DiamondSpaceAssultRifle,
        };

        /// <summary>
        /// Canonical item we write back to when granting/revoking
        /// so we don't have to touch every variant.
        /// </summary>
        private const InventoryItemIDs PrimaryAssault =
            InventoryItemIDs.AssultRifle;

        #endregion

        #region Helpers

        /// <summary>
        /// Sum TimeHeld across all assault rifle variants.
        /// </summary>
        private TimeSpan GetCurrentHeld(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return TimeSpan.Zero;

            TimeSpan total = TimeSpan.Zero;
            foreach (var id in AssaultIds)
            {
                total += PlayerStatReflectionHelper.GetItemStatTimeHeldRaw(stats, id);
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                // Optional: gate by mode if you ever want Endurance-only:
                // if (!AchievementModeRules.IsCustomAchievementAllowedNow())
                //     return false;

                return GetCurrentHeld(PlayerStats) >= _requiredHeld;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                // if (!AchievementModeRules.IsCustomAchievementAllowedNow())
                //     return 0f;

                TimeSpan held = GetCurrentHeld(PlayerStats);
                if (_requiredHeld <= TimeSpan.Zero)
                    return 0f;

                float ratio = (float)(held.TotalMinutes / _requiredHeld.TotalMinutes);
                return MathHelper.Clamp(ratio, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                TimeSpan held = GetCurrentHeld(PlayerStats);
                double minutes = held.TotalMinutes;
                double requiredMinutes = _requiredHeld.TotalMinutes;

                if (Math.Abs(_lastMinutes - minutes) > 0.01)
                {
                    _lastMinutes = minutes;
                    _lastString = $"({minutes:F1}/{requiredMinutes:F1}) minutes holding any assault rifle.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: extra bullets for your dedication.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Bullets, 256), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Clock,   1),   false);
            ModLoader.LogSystem.SendFeedback("+256 Bullets, +1 Clock.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure the combined assault-rifle TimeHeld total meets this tier
        /// by topping up the primary rifle's TimeHeld.
        /// On revoke, zero all assault-rifle TimeHeld so the tier can be
        /// re-earned cleanly.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                TimeSpan totalHeld = GetCurrentHeld(stats);
                if (totalHeld < _requiredHeld)
                {
                    TimeSpan missing = _requiredHeld - totalHeld;
                    if (missing < TimeSpan.Zero)
                        missing = TimeSpan.Zero;

                    TimeSpan primaryHeld =
                        PlayerStatReflectionHelper.GetItemStatTimeHeldRaw(stats, PrimaryAssault);

                    PlayerStatReflectionHelper.SetItemStatTimeHeldRaw(
                        stats,
                        PrimaryAssault,
                        primaryHeld + missing);
                }
            }
            else
            {
                // Drop all assault-rifle TimeHeld values to zero.
                foreach (var id in AssaultIds)
                {
                    PlayerStatReflectionHelper.SetItemStatTimeHeldRaw(
                        stats, id, TimeSpan.Zero);
                }
            }
        }
        #endregion
    }
}