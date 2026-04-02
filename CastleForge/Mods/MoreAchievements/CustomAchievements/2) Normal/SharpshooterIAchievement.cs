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
    /// "Sharpshooter I" - land accurate shots with any bolt-action rifle variant.
    /// Uses <see cref="CastleMinerZPlayerStats.ItemStats.Hits"/> across all bolt-action rifles.
    /// </summary>
    internal sealed class SharpshooterIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public SharpshooterIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredHits = 250;
        }

        #region Fields

        private readonly int _requiredHits;

        private string _lastString;
        private int    _lastAmount = -1;

        /// <summary>
        /// All inventory IDs that count as "bolt-action rifles" for this achievement.
        /// </summary>
        private static readonly InventoryItemIDs[] BoltActionIds =
        {
            InventoryItemIDs.BoltActionRifle,
            InventoryItemIDs.GoldBoltActionRifle,
            InventoryItemIDs.DiamondBoltActionRifle,
            InventoryItemIDs.BloodStoneBoltActionRifle,
            InventoryItemIDs.IronSpaceBoltActionRifle,
            InventoryItemIDs.CopperSpaceBoltActionRifle,
            InventoryItemIDs.GoldSpaceBoltActionRifle,
            InventoryItemIDs.DiamondSpaceBoltActionRifle,
        };

        /// <summary>
        /// Canonical item we "write back" to when granting / revoking,
        /// so we don't have to touch every variant.
        /// </summary>
        private const InventoryItemIDs PrimaryBoltAction =
            InventoryItemIDs.BoltActionRifle;

        #endregion

        #region Helpers

        /// <summary>
        /// Sum Hits across all bolt-action rifle variants.
        /// </summary>
        private int GetCurrentHits(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;
            foreach (var id in BoltActionIds)
            {
                total += PlayerStatReflectionHelper.GetItemStatHitsRaw(stats, id);
            }
            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return GetCurrentHits(PlayerStats) >= _requiredHits;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int hits = GetCurrentHits(PlayerStats);
                return MathHelper.Clamp((float)hits / _requiredHits, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int hits = GetCurrentHits(PlayerStats);
                if (_lastAmount != hits)
                {
                    _lastAmount = hits;
                    _lastString = $"({hits}/{_requiredHits}) hits with any bolt-action rifle.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: extra high-caliber ammo for your precision.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BoltActionRifle, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.IronBullets,     128), false);
            ModLoader.LogSystem.SendFeedback("+1 BoltActionRifle, +128 IronBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, make sure the bolt-action hit total is at least the threshold
        /// by topping up the primary rifle's Hits.
        /// On revoke, zero out bolt-action hits so the tier can be re-earned cleanly.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                int totalHits = GetCurrentHits(stats);
                if (totalHits < _requiredHits)
                {
                    int missing = _requiredHits - totalHits;

                    // Push the missing hits onto the primary bolt-action rifle.
                    int primaryHits = PlayerStatReflectionHelper
                        .GetItemStatHitsRaw(stats, PrimaryBoltAction);

                    PlayerStatReflectionHelper.SetItemStatHitsRaw(
                        stats,
                        PrimaryBoltAction,
                        primaryHits + missing);
                }
            }
            else
            {
                // Drop all bolt-action Hits to zero.
                // This mirrors the "reset to 0" pattern used in other achievements to
                // avoid partial-decrease weirdness with Steam.
                foreach (var id in BoltActionIds)
                {
                    PlayerStatReflectionHelper.SetItemStatHitsRaw(stats, id, 0);
                }
            }
        }
        #endregion
    }
}