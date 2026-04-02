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
    /// Example custom achievement:
    /// "Survivor V"
    /// "Survive for 730 in-game days".
    /// Uses MaxDaysSurvived from <see cref="CastleMinerZPlayerStats"/> just like the vanilla day achievements.
    /// </summary>
    internal sealed class SurvivorVAchievement : AchievementManager<CastleMinerZPlayerStats>.Achievement, ILocalStatAdjust, IAchievementReward
    {
        public SurvivorVAchievement(string apiName, CastleMinerZAchievementManager manager, string name, string description)
            : base(apiName,
                   manager,
                   name,
                   description)
        {
            _days = 730;
        }

        #region Fields

        private readonly int _days;

        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                // Normal stat check.
                return PlayerStats.MaxDaysSurvived >= _days;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                return MathHelper.Clamp(
                    (float)PlayerStats.MaxDaysSurvived / _days,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                return $"({PlayerStats.MaxDaysSurvived}/{_days}) days survived.";
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Optional function:
        /// One-time reward for completing the challenge.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Chainsaw1,        1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneLMGGun, 1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondBullets,   512), false);
            ModLoader.LogSystem.SendFeedback("+1 Haunted Chainsaw, +1 BloodStoneLMGGun, +512 DiamondBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Optional function:
        /// Local stat adjustments for grant / revoke of this custom achievement.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
                stats.MaxDaysSurvived = Math.Max(stats.MaxDaysSurvived, 730);
            else
            {
                // Drop days survived back to zero.
                stats.MaxDaysSurvived = Math.Min(stats.MaxDaysSurvived, 0);

                /*
                // Drop back to the 1-year tier but not below it.
                stats.MaxDaysSurvived = Math.Min(stats.MaxDaysSurvived, 365);

                // Push changes to the steam API.
                var api = stats.SteamAPI;
                if (api != null)
                {
                    var ach365 = AchievementResetHelper.GetSteamAchievementByName("ACH_DAYS_365");
                    if (ach365 != null && !string.IsNullOrEmpty(ach365.APIName))
                    {
                        // Mark 365-day vanilla achievement as temporarily locked;
                        // the pipeline will re-evaluate and re-unlock it if stats still qualify.
                        AchievementResetHelper.s_achievedFlagField?.SetValue(ach365, false);
                        api.SetAchievement(ach365.APIName, false);
                    }
                }
                */
            }
        }
        #endregion
    }
}