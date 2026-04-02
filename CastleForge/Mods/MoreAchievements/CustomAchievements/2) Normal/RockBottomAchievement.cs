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
    /// "Rock Bottom" - reach the deepest meaningful depth.
    /// Uses <see cref="CastleMinerZPlayerStats.MaxDepth"/> (positive depth magnitude).
    /// </summary>
    internal sealed class RockBottomAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public RockBottomAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredDepth = 62f;
        }

        #region Fields

        private readonly float _requiredDepth;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.MaxDepth <= -_requiredDepth;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                float depth = -PlayerStats.MaxDepth;
                return MathHelper.Clamp(depth / _requiredDepth, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int depth = -(int)PlayerStats.MaxDepth;

                if (_lastAmount != depth)
                {
                    _lastAmount = depth;
                    _lastString = $"({depth}/{_requiredDepth}) depth reached.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: explosives for mining out the underworld.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT, 16),  false);
            ModLoader.LogSystem.SendFeedback("+16 TNT.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure MaxDepth meets this tier.
        /// On revoke, drop back to a lower tier (e.g. 40f - vanilla deep achievement).
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.MaxDepth = Math.Min(stats.MaxDepth, -_requiredDepth);
            }
            else
            {
                // Drop depth back to 40f at most, preserving vanilla deep-ach tiers.
                /*
                float lowerTier = -40f;
                float newVal    = Math.Max(stats.MaxDepth, lowerTier);
                if (newVal > 0f) newVal = 0f;
                stats.MaxDepth  = newVal;
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.MaxDepth = 0;
            }
        }
        #endregion
    }
}