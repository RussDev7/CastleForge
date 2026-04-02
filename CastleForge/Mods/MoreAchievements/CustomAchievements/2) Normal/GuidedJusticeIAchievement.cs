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
    /// "Guided Justice I" - slay dragons with guided missiles.
    /// Uses <see cref="CastleMinerZPlayerStats.DragonsKilledWithGuidedMissile"/>.
    /// </summary>
    internal sealed class GuidedJusticeIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public GuidedJusticeIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 5;
        }

        #region Fields

        private readonly int _requiredKills;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.DragonsKilledWithGuidedMissile >= _requiredKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.DragonsKilledWithGuidedMissile / _requiredKills,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = PlayerStats.DragonsKilledWithGuidedMissile;
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredKills}) dragons killed with guided missiles.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: guided rocket ammo.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RocketLauncherGuided, 1), false);
            ModLoader.LogSystem.SendFeedback("+1 RocketLauncherGuided.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure Guided-Dragon kills meet this tier.
        /// On revoke, clamp to one less than this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.DragonsKilledWithGuidedMissile =
                    Math.Max(stats.DragonsKilledWithGuidedMissile, _requiredKills);
            }
            else
            {
                /*
                int maxWhenRevoked = _requiredKills - 1;
                int cur            = stats.DragonsKilledWithGuidedMissile;
                stats.DragonsKilledWithGuidedMissile = Math.Min(cur, maxWhenRevoked);
                if (stats.DragonsKilledWithGuidedMissile < 0)
                    stats.DragonsKilledWithGuidedMissile = 0;
                */

                stats.DragonsKilledWithGuidedMissile = 0;
            }
        }
        #endregion
    }
}