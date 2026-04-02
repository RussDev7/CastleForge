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
    /// "Dragon Slayer I" - kill 5 undead dragons.
    /// Sums all per-type dragon kill counters on <see cref="CastleMinerZPlayerStats"/>.
    /// </summary>
    internal sealed class DragonSlayerIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public DragonSlayerIAchievement(
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

        #region Helpers

        private static int GetTotalDragonKills(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            return
                stats.UndeadDragonKills;
                // stats.ForestDragonKills +
                // stats.IceDragonKills    +
                // stats.FireDragonKills   +
                // stats.SandDragonKills;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                int total = GetTotalDragonKills(PlayerStats);
                return total >= _requiredKills;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalDragonKills(PlayerStats);
                return MathHelper.Clamp((float)total / _requiredKills, 0f, 1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalDragonKills(PlayerStats);
                if (_lastAmount != total)
                {
                    _lastAmount = total;
                    _lastString = $"({total}/{_requiredKills}) dragons killed.";
                }
                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: rockets and a guided launcher for dragon hunting.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RocketLauncherGuided, 1),  false);
            ModLoader.LogSystem.SendFeedback("+1 RocketLauncherGuided.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// On grant, ensure total dragon kills ≥ 100 by bumping UndeadDragonKills.
        /// On revoke, clamp total just below this tier by reducing UndeadDragonKills.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            int total = GetTotalDragonKills(stats);
            int target = _requiredKills;

            if (grant)
            {
                if (total < target)
                {
                    int needed = target - total;
                    stats.UndeadDragonKills += needed;
                }
            }
            else
            {
                // Drop kills one less, preserving the previous vanilla kill tiers.
                /*
                int maxWhenRevoked = target - 1;
                if (total > maxWhenRevoked)
                {
                    int over = total - maxWhenRevoked;
                    stats.UndeadDragonKills =
                        Math.Max(0, stats.UndeadDragonKills - over);
                }
                */

                // Drop to zero. Steam is hostile to partial decreases on this stat.
                stats.UndeadDragonKills = 0;
            }
        }
        #endregion
    }
}