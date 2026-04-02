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
    /// "Torchlighter I" - rely heavily on torches.
    /// Uses <see cref="CastleMinerZPlayerStats.ItemStats.Used"/> for <see cref="InventoryItemIDs.Torch"/>.
    /// </summary>
    internal sealed class TorchlighterIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public TorchlighterIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredUses = 100;
        }

        #region Fields

        private readonly int _requiredUses;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        private static CastleMinerZPlayerStats.ItemStats GetTorchStats(CastleMinerZPlayerStats stats)
        {
            return stats?.GetItemStats(InventoryItemIDs.Torch);
        }

        protected override bool IsSastified
        {
            get
            {
                var torch = GetTorchStats(PlayerStats);
                return torch != null && torch.Used >= _requiredUses;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                var torch = GetTorchStats(PlayerStats);
                int used = torch?.Used ?? 0;

                return MathHelper.Clamp((float)used / _requiredUses, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                var torch = GetTorchStats(PlayerStats);
                int used = torch?.Used ?? 0;

                if (_lastAmount != used)
                {
                    _lastAmount = used;
                    _lastString = $"({used}/{_requiredUses}) torches used.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: lots of torches, naturally.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Clock, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch, 32), false);
            ModLoader.LogSystem.SendFeedback("+1 Clock, +32 Torch.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure Torch.Used meets this tier.
        /// On revoke, clamp just under this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                PlayerStatReflectionHelper.SetItemStatUsedRaw(
                    stats, InventoryItemIDs.Torch, _requiredUses);
            }
            else
            {
                PlayerStatReflectionHelper.SetItemStatUsedRaw(
                    stats, InventoryItemIDs.Torch, 0 /* _requiredUses - 1 */);
            }
        }
        #endregion
    }
}