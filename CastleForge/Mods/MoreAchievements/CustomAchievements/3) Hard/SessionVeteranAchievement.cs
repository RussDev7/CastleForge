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
    /// "Session Veteran I" - play a large number of games.
    /// Uses <see cref="CastleMinerZPlayerStats.GamesPlayed"/>.
    /// </summary>
    internal sealed class SessionVeteranAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public SessionVeteranAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredGames = 100;
        }

        #region Fields

        private readonly int _requiredGames;

        private string _lastString;
        private int    _lastAmount = -1;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                return PlayerStats.GamesPlayed >= _requiredGames;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.GamesPlayed / _requiredGames,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = PlayerStats.GamesPlayed;
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredGames}) games played.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: some general-purpose supplies for the next run.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Clock,   1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Compass, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GPS,     1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Torch,   128), false);
            ModLoader.LogSystem.SendFeedback("+1 Clock, +1 Compass, +1x GPS, +128 Torch.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure GamesPlayed meets this tier.
        /// On revoke, clamp to one less than this tier.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                stats.GamesPlayed = Math.Max(stats.GamesPlayed, _requiredGames);
            }
            else
            {
                int maxWhenRevoked = _requiredGames - 1;
                // stats.GamesPlayed  = Math.Min(stats.GamesPlayed, maxWhenRevoked);
                if (stats.GamesPlayed < 0)
                    stats.GamesPlayed = 0;

                stats.GamesPlayed = 0;
            }
        }
        #endregion
    }
}