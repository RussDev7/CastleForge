/*
SPDX-License-Identifier: GPL-3.0-or-laterCopyright (c) 2025 RussDev7
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
    /// "Explorer I" - reach a moderate max distance from spawn.
    /// Uses <see cref="CastleMinerZPlayerStats.MaxDistanceTraveled"/>.
    /// </summary>
    internal sealed class ExplorerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ExplorerIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredDistance = 2000f;
        }

        #region Fields

        private readonly float _requiredDistance;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.MaxDistanceTraveled >= _requiredDistance;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                float dist = PlayerStats.MaxDistanceTraveled;
                return MathHelper.Clamp(dist / _requiredDistance, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                float dist = PlayerStats.MaxDistanceTraveled;
                int   dInt = (int)Math.Floor(dist);

                if (_lastAmount != dInt)
                {
                    _lastAmount = dInt;
                    _lastString = $"({dInt}/{(int)_requiredDistance}) distance traveled.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: some TNT for carving your path.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GPS,     1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Compass, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch,   32), false);
            ModLoader.LogSystem.SendFeedback("+1 GPS, +1 Compass, +32 Torch.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure MaxDistanceTraveled meets this tier.
        /// On revoke, clamp to just under this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.MaxDistanceTraveled = Math.Max(stats.MaxDistanceTraveled, _requiredDistance);
            }
            else
            {
                float newVal = 0f; // Math.Min(stats.MaxDistanceTraveled, _requiredDistance - 1f);
                if (newVal < 0f) newVal = 0f;
                stats.MaxDistanceTraveled = newVal;
            }
        }
        #endregion
    }
}