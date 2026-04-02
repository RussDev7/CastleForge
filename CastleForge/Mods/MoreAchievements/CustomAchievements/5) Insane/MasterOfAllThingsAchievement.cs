/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Achievements;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// Insane difficulty:
    /// Unlocks when the player has held every craftable item in their hands
    /// for at least 10 minutes each (real-time).
    ///
    /// Uses the same "valid craftable items" list as MasterCrafterAchievement,
    /// via MasterCrafterAchievement._craftableItemIDs.
    /// </summary>
    public sealed class MasterOfAllThingsAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public const double RequiredMinutesPerItem = 10.0;

        // Cached progress string so we're not allocating every frame.
        private string _cachedProgress;
        private int    _cachedHave  = -1;
        private int    _cachedTotal = -1;

        public MasterOfAllThingsAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        #region Core helpers

        /// <summary>
        /// Counts how many craftable items have TimeHeld >= RequiredMinutesPerItem.
        /// </summary>
        private static int CountItemsMeetingRequirement(
            CastleMinerZPlayerStats stats,
            out int total)
        {
            var ids = MasterCrafterAchievement._craftableItemIDs;
            total = (ids != null) ? ids.Length : 0;

            if (stats == null || total == 0)
                return 0;

            int have = 0;

            foreach (var id in ids)
            {
                var s = stats.GetItemStats(id);
                if (s != null && s.TimeHeld.TotalMinutes >= RequiredMinutesPerItem)
                {
                    have++;
                }
            }

            return have;
        }
        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// True when *every* craftable item has been held for at least
        /// RequiredMinutesPerItem minutes.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                int have = CountItemsMeetingRequirement(PlayerStats, out int total);
                return (total > 0) && (have >= total);
            }
        }

        /// <summary>
        /// Progress is fraction of craftable items that meet the hold-time requirement.
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                int have = CountItemsMeetingRequirement(PlayerStats, out int total);

                if (total <= 0)
                    return 0f;

                float f = (float)have / (float)total;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return f;
            }
        }

        /// <summary>
        /// Text like "(23/87) craftable items held for at least 10 minutes each."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int have = CountItemsMeetingRequirement(PlayerStats, out int total);

                if (have != _cachedHave || total != _cachedTotal || _cachedProgress == null)
                {
                    _cachedHave = have;
                    _cachedTotal = total;
                    _cachedProgress =
                        $"({have}/{total}) craftable items held for at least {RequiredMinutesPerItem:0} minutes each.";
                }

                return _cachedProgress ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// One-time reward for going fully hands-on with every craftable item.
        /// Tune the loot to taste.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodstonePickAxe, 1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondSpade,      1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondAxe,        1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneKnife,   1),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneBullets, 64), false);
            ModLoader.LogSystem.SendFeedback("+1 BloodstonePickAxe, +1 DiamondSpade, +1 DiamondAxe, +1 BloodStoneKnife, +64 BloodStoneBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// Local stat adjustments when grant/revoke is used from your reset helper:
        /// • grant == true: ensure every craftable item has TimeHeld >= RequiredMinutesPerItem.
        /// • grant == false: reset TimeHeld to zero for all craftable items.
        ///   (Destructive; mainly for testing / debugging.)
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            var ids = MasterCrafterAchievement._craftableItemIDs;
            if (ids == null || ids.Length == 0)
                return;

            foreach (var id in ids)
            {
                var s = stats.GetItemStats(id);
                if (s == null)
                    continue;

                if (grant)
                {
                    if (s.TimeHeld.TotalMinutes < RequiredMinutesPerItem)
                    {
                        s.TimeHeld = TimeSpan.FromMinutes(RequiredMinutesPerItem);
                    }
                }
                else
                {
                    // Make sure the achievement can be legitimately re-done.
                    s.TimeHeld = TimeSpan.Zero;
                }
            }
        }
        #endregion
    }
}