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
    /// "Explorer III" - reach a distance of 20,000 units from spawn.
    /// Uses <see cref="CastleMinerZPlayerStats.MaxDistanceTraveled"/>.
    /// </summary>
    internal sealed class ExplorerIIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public ExplorerIIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredDistance = 20000f;
        }

        #region Fields

        private readonly float _requiredDistance;
        private string _lastString;
        private float  _lastAmount = -1f;

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
                return MathHelper.Clamp(
                    PlayerStats.MaxDistanceTraveled / _requiredDistance,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                float d = PlayerStats.MaxDistanceTraveled;
                if (Math.Abs(_lastAmount - d) > float.Epsilon)
                {
                    _lastAmount = d;
                    _lastString = $"({d:0}/{_requiredDistance:0}) max distance from spawn.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: a GPS and torches to keep exploring.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TeleportGPS,     1), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TeleportGPS,     1), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TeleportStation, 1), false);
            ModLoader.LogSystem.SendFeedback("+2 TeleportGPS, +1 TeleportStation.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure MaxDistanceTraveled ≥ 10,000.
        /// On revoke, clamp just below this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.MaxDistanceTraveled =
                    Math.Max(stats.MaxDistanceTraveled, _requiredDistance);
            }
            else
            {
                float maxWhenRevoked = 0f; // _requiredDistance - 1f;
                stats.MaxDistanceTraveled =
                    Math.Min(stats.MaxDistanceTraveled, maxWhenRevoked);
                if (stats.MaxDistanceTraveled < 0f)
                    stats.MaxDistanceTraveled = 0f;
            }
        }
        #endregion
    }
}