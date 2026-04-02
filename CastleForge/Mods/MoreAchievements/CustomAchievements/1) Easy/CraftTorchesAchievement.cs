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
    /// "Craft Torches" - craft a small batch of torches.
    /// Uses per-item <see cref="CastleMinerZPlayerStats.ItemStats.Crafted"/> for <see cref="InventoryItemIDs.Torch"/>.
    /// </summary>
    internal sealed class CraftTorchesAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public CraftTorchesAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredTorchesCrafted = 3; // 4 torches per craft.
        }

        #region Fields

        private readonly int _requiredTorchesCrafted;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Unlocks once Torch.Crafted ≥ required count.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return false;

                int crafted = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, InventoryItemIDs.Torch);
                return crafted >= _requiredTorchesCrafted;
            }
        }

        /// <summary>
        /// 0-1 progress based on torch crafts / required.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return 0f;

                int crafted = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, InventoryItemIDs.Torch);
                return MathHelper.Clamp(
                    (float)crafted / _requiredTorchesCrafted,
                    0f,
                    1f);
            }
        }

        /// <summary>
        /// Cached numeric progress string.
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                var stats = PlayerStats;
                int crafted = 0;

                if (stats != null)
                    crafted = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, InventoryItemIDs.Torch);

                if (_lastAmount != crafted)
                {
                    _lastAmount = crafted;
                    _lastString = $"({crafted}/{_requiredTorchesCrafted}) torches crafted.";
                }

                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a starter stack of torches.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch, 32), false);
            ModLoader.LogSystem.SendFeedback("+32 Torch.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, make sure we have at least the required number of torch crafts.
        /// On revoke, clamp to one below this tier (or 0).
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var id      = InventoryItemIDs.Torch;
            int current = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, id);

            if (grant)
            {
                if (current < _requiredTorchesCrafted)
                {
                    PlayerStatReflectionHelper.SetItemStatCraftedRaw(stats, id, _requiredTorchesCrafted);
                }
            }
            else
            {
                if (current >= _requiredTorchesCrafted)
                {
                    int newVal = Math.Min(current, _requiredTorchesCrafted - 1);
                    if (newVal < 0)
                        newVal = 0;

                    PlayerStatReflectionHelper.SetItemStatCraftedRaw(stats, id, newVal);
                }
            }
        }
        #endregion
    }
}