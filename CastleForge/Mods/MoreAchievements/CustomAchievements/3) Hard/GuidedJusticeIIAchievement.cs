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
    /// "Guided Justice II" (Hard / low-Brutal)
    /// Unlocks when the player has killed a substantial number of dragons using
    /// guided missiles.
    ///
    /// Uses <see cref="CastleMinerZPlayerStats.DragonsKilledWithGuidedMissile"/>.
    /// Tier I is your existing "Guided Justice I" (e.g. 5 kills).
    /// </summary>
    internal sealed class GuidedJusticeIIAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public GuidedJusticeIIAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
            _requiredKills = 50; // Hard / low-Brutal tier.
        }

        #region Fields

        private readonly int _requiredKills;
        private int    _lastKills   = -1;
        private string _lastMessage = string.Empty;

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
                int kills = PlayerStats.DragonsKilledWithGuidedMissile;
                if (_requiredKills <= 0)
                    return 0f;

                return MathHelper.Clamp(
                    (float)kills / (float)_requiredKills,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int kills = PlayerStats.DragonsKilledWithGuidedMissile;

                if (kills != _lastKills)
                {
                    _lastKills   = kills;
                    _lastMessage = $"({kills}/{_requiredKills}) dragons killed with guided missiles.";
                }

                return _lastMessage ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: extra guided launcher and rockets.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.RocketLauncherGuided, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.ExplosivePowder,      64), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.C4,                   32), false);
            ModLoader.LogSystem.SendFeedback("+1 RocketLauncherGuided, +64 ExplosivePowder, +32 C4.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments:
        /// • grant == true  : ensure DragonsKilledWithGuidedMissile >= _requiredKills.
        /// • grant == false : reset DragonsKilledWithGuidedMissile to 0 (Steam-safe).
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                if (stats.DragonsKilledWithGuidedMissile < _requiredKills)
                {
                    stats.DragonsKilledWithGuidedMissile = _requiredKills;
                }
            }
            else
            {
                // Hard reset instead of partial decrement.
                stats.DragonsKilledWithGuidedMissile = 0;
            }
        }
        #endregion
    }
}