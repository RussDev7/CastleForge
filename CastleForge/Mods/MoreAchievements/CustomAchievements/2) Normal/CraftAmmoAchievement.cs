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
    /// "Craft Ammo" - craft a large number of bullet stacks.
    /// Uses per-item <see cref="CastleMinerZPlayerStats.ItemStats.Crafted"/> for <see cref="InventoryItemIDs.Bullets"/>.
    /// </summary>
    internal sealed class CraftAmmoAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public CraftAmmoAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredStacksCrafted = 250;
        }

        #region Fields

        private readonly int _requiredStacksCrafted;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// Unlocks once Bullets.Crafted ≥ required stacks.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return false;

                int crafted = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, InventoryItemIDs.Bullets);
                return crafted >= _requiredStacksCrafted;
            }
        }

        /// <summary>
        /// 0-1 based on bullet stacks crafted / required.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                var stats = PlayerStats;
                if (stats == null)
                    return 0f;

                int crafted = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, InventoryItemIDs.Bullets);
                return MathHelper.Clamp(
                    (float)crafted / _requiredStacksCrafted,
                    0f,
                    1f);
            }
        }

        /// <summary>
        /// Cached numeric progress string for UI.
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                var stats = PlayerStats;
                int crafted = 0;

                if (stats != null)
                    crafted = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, InventoryItemIDs.Bullets);

                if (_lastAmount != crafted)
                {
                    _lastAmount = crafted;
                    _lastString = $"({crafted}/{_requiredStacksCrafted}) bullet stacks crafted.";
                }

                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a hefty pile of bullets.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.ExplosivePowder, 128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GunPowder,       128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BrassCasing,     128), false);
            ModLoader.LogSystem.SendFeedback("+128 ExplosivePowder, +128 GunPowder, +128 BrassCasing.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure Bullets.Crafted meets this tier.
        /// On revoke, clamp to one below this tier (or 0).
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var id      = InventoryItemIDs.Bullets;
            int current = PlayerStatReflectionHelper.GetItemStatCraftedRaw(stats, id);

            if (grant)
            {
                if (current < _requiredStacksCrafted)
                {
                    PlayerStatReflectionHelper.SetItemStatCraftedRaw(stats, id, _requiredStacksCrafted);
                }
            }
            else
            {
                if (current >= _requiredStacksCrafted)
                {
                    int newVal = Math.Min(current, _requiredStacksCrafted - 1);
                    if (newVal < 0)
                        newVal = 0;

                    PlayerStatReflectionHelper.SetItemStatCraftedRaw(stats, id, newVal);
                }
            }
        }
        #endregion
    }
}