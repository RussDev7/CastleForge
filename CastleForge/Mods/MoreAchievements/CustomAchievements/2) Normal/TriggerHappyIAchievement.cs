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
    /// "Trigger Happy I" - rely heavily on any assault rifle variant.
    /// Uses <see cref="CastleMinerZPlayerStats.ItemStats.Used"/> across all
    /// <see cref="InventoryItemIDs.*AssultRifle"/> entries.
    /// </summary>
    internal sealed class TriggerHappyIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public TriggerHappyIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredUses = 1000;
        }

        #region Fields

        private readonly int _requiredUses;

        private string _lastString;
        private int    _lastAmount = -1;

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
        /// Canonical item we "write back" to when granting / revoking,
        /// so we don't have to touch every variant.
        /// </summary>
        private const InventoryItemIDs PrimaryAssault =
            InventoryItemIDs.AssultRifle;

        #endregion

        #region Helpers

        /// <summary>
        /// Sum Used across all assault rifle variants.
        /// </summary>
        private int GetCurrentUses(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in AssaultIds)
            {
                total += PlayerStatReflectionHelper
                    .GetItemStatUsedRaw(stats, id);
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetCurrentUses(PlayerStats) >= _requiredUses;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int used = GetCurrentUses(PlayerStats);
                return MathHelper.Clamp((float)used / _requiredUses, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int used = GetCurrentUses(PlayerStats);
                if (_lastAmount != used)
                {
                    _lastAmount = used;
                    _lastString = $"({used}/{_requiredUses}) shots fired with any assault rifle.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a pile of bullets to keep firing.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.AssultRifle, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Bullets,     256), false);
            ModLoader.LogSystem.SendFeedback("+1 AssultRifle, +256 Bullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure the combined assault-rifle Used total meets this tier
        /// by topping up the primary rifle's Used.
        /// On revoke, zero all assault rifle Used counts so the tier can be
        /// re-earned cleanly.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                int totalUses = GetCurrentUses(stats);
                if (totalUses < _requiredUses)
                {
                    // Push the missing uses onto the primary assault rifle.
                    int primaryUses = PlayerStatReflectionHelper
                        .GetItemStatUsedRaw(stats, PrimaryAssault);

                    int missing = _requiredUses - totalUses;
                    if (missing < 0)
                        missing = 0;

                    PlayerStatReflectionHelper.SetItemStatUsedRaw(
                        stats,
                        PrimaryAssault,
                        primaryUses + missing);
                }
            }
            else
            {
                // Drop all assault-rifle Used counts to zero.
                // This avoids weirdness where another variant keeps you above the threshold.
                foreach (var id in AssaultIds)
                {
                    PlayerStatReflectionHelper.SetItemStatUsedRaw(stats, id, 0);
                }
            }
        }

        #endregion
    }
}