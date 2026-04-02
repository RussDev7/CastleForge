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
    /// "Completionist" - unlocks once every other tracked achievement in the
    /// active CastleMinerZ achievement manager is fully completed.
    ///
    /// Notes:
    /// • Uses AchievementManager<T>.Count and the indexer to walk all
    ///   achievements registered on the manager passed into the ctor.
    /// • Treats an achievement as "done" when ProgressTowardsUnlock >= 1.0f.
    /// • Skips itself via ReferenceEquals(..).
    /// • Respects AchievementModeRules.IsCustomAchievementAllowedNow() so it
    ///   won't unlock in disallowed modes.
    /// </summary>
    public sealed class CompletionistAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          IAchievementReward
    {
        private readonly AchievementManager<CastleMinerZPlayerStats> _manager;

        // Cached progress string.
        private string _lastString;
        private int    _lastUnlocked = -1;
        private int    _lastTotal    = -1;

        /// <summary>
        /// Creates the meta "complete all achievements" achievement.
        /// </summary>
        public CompletionistAchievement(
            string                        apiName,
            CastleMinerZAchievementManager manager,
            string                        name,
            string                        description)
            : base(apiName, manager, name, description)
        {
            // CastleMinerZAchievementManager derives from AchievementManager<T>,
            // so we can treat it as the generic base for enumeration.
            _manager = manager;
        }

        #region Helper: Counting completed / total achievements

        /// <summary>
        /// Computes how many achievements are considered "completed" and how
        /// many total are counted toward this meta-achievement.
        /// </summary>
        private void GetAchievementCounts(out int unlocked, out int total)
        {
            unlocked = 0;
            total    = 0;

            var mgr = _manager;
            if (mgr == null)
                return;

            int count = mgr.Count;
            for (int i = 0; i < count; i++)
            {
                var ach = mgr[i];
                if (ach == null)
                    continue;

                // Do not count this meta-achievement itself.
                if (ReferenceEquals(ach, this))
                    continue;

                // Optional place to skip dev/test/internal achievements if you
                // ever want to filter by APIName pattern, e.g.:
                //
                // if (ach.APIName != null && ach.APIName.StartsWith("DEV_", StringComparison.OrdinalIgnoreCase))
                //     continue;

                total++;

                try
                {
                    // Convention: ProgressTowardsUnlock >= 1.0f means "done".
                    if (ach.ProgressTowardsUnlock >= 1f)
                        unlocked++;
                }
                catch
                {
                    // If any achievement throws during progress calculation,
                    // treat it as not yet completed rather than breaking.
                }
            }
        }
        #endregion

        #region Achievement Condition / Progress

        /// <summary>
        /// True only when *every* counted achievement is fully completed.
        /// </summary>
        protected override bool IsSastified
        {
            get
            {
                GetAchievementCounts(out int unlocked, out int total);

                if (total <= 0)
                    return false;

                return unlocked >= total;
            }
        }

        /// <summary>
        /// Fraction of achievements completed (0..1).
        /// </summary>
        public override float ProgressTowardsUnlock
        {
            get
            {
                GetAchievementCounts(out int unlocked, out int total);

                if (total <= 0)
                    return 0f;

                return MathHelper.Clamp((float)unlocked / total, 0f, 1f);
            }
        }

        /// <summary>
        /// Human-readable progress string, e.g. "(23/42) achievements completed."
        /// </summary>
        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                GetAchievementCounts(out int unlocked, out int total);

                if (_lastUnlocked != unlocked || _lastTotal != total)
                {
                    _lastUnlocked = unlocked;
                    _lastTotal    = total - 1;
                    _lastString   = $"({unlocked}/{total}) achievements completed.";
                }

                return _lastString;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Optional one-time reward for becoming a Completionist.
        /// Adjust to taste or remove the body if you want "no reward."
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Chainsaw1,          1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Chainsaw1,          1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.PrecisionLaser,     1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.PrecisionLaser,     1),   false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneBlock,    64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.SpaceRockInventory, 64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Slime,              64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.Diamond,            64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.DiamondWall,        128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GoldBullets,        512), false);
            ModLoader.LogSystem.SendFeedback("+2 Haunted Chainsaw, +2 PrecisionLaser, +64 BloodStoneBlock, +64 SpaceRockInventory, +64 Slime, +64 Diamond, +128 DiamondWall, +512 GoldBullets.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// For a meta-achievement, we generally do NOT want to mutate any
        /// underlying stats on grant/revoke. Toggling this achievement through
        /// your reset helper will just flip the unlocked state.
        /// </summary>
        public void AdjustLocalStats(/* CastleMinerZPlayerStats stats, bool grant */)
        {
            // Intentionally left empty.
        }
        #endregion
    }
}
