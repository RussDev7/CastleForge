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
    /// Block Breaker IV - World Eater (Difficulty: Insane)
    /// Dig an absolutely enormous number of minable blocks.
    ///
    /// Reuses MasterProspectorAchievement._minableBlockTypes and unlocks
    /// once the total dug count reaches RequiredTotalDug.
    /// </summary>
    internal sealed class BlockBreakerIVAchievement
        : AchievementManager<CastleMinerZPlayerStats>.Achievement,
          ILocalStatAdjust,
          IAchievementReward
    {
        public BlockBreakerIVAchievement(
            string apiName,
            CastleMinerZAchievementManager manager,
            string name,
            string description)
            : base(apiName, manager, name, description)
        {
        }

        public const int RequiredTotalDug = 50000;

        private string _lastMessage;
        private int _lastTotal = -1;

        #region Helper

        private static int GetTotalMinableBlocksDug(CastleMinerZPlayerStats stats)
        {
            if (stats == null)
                return 0;

            int total = 0;

            foreach (var type in MasterProspectorAchievement._minableBlockTypes)
            {
                total += stats.BlocksDugCount(type);
            }

            return total;
        }
        #endregion

        #region Achievement Condition / Progress

        protected override bool IsSastified
        {
            get
            {
                int total = GetTotalMinableBlocksDug(PlayerStats);
                return total >= RequiredTotalDug;
            }
        }

        public override float ProgressTowardsUnlock
        {
            get
            {
                int total = GetTotalMinableBlocksDug(PlayerStats);
                return MathHelper.Clamp(
                    (float)total / RequiredTotalDug,
                    0f,
                    1f);
            }
        }

        public override string ProgressTowardsUnlockMessage
        {
            get
            {
                int total = GetTotalMinableBlocksDug(PlayerStats);
                if (total != _lastTotal)
                {
                    _lastTotal = total;
                    _lastMessage =
                        $"({total}/{RequiredTotalDug}) minable blocks dug.";
                }

                return _lastMessage ?? string.Empty;
            }
        }
        #endregion

        #region IAchievementReward

        /// <summary>
        /// Reward: obscene explosives and ammo to celebrate eating the world.
        /// </summary>
        public void GrantReward(CastleMinerZPlayerStats stats)
        {
            var game   = CastleMinerZGame.Instance;
            var player = game?.LocalPlayer;
            var inv    = player?.PlayerInventory;
            if (inv == null)
                return;

            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.TNT,                  128), false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.C4,                   64),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.GunPowder,            16),  false);
            inv.AddInventoryItem(InventoryItem.CreateItem(InventoryItemIDs.BloodStoneLaserSword, 1),   false);
            ModLoader.LogSystem.SendFeedback("+128 TNT, +64 C4, +16 GunPowder, +1 BloodStoneLaserSword.");
        }
        #endregion

        #region ILocalStatAdjust

        /// <summary>
        /// grant  == true : ensure summed raw dug counts across all minable
        ///                  types is at least RequiredTotalDug.
        /// grant  == false: zero out raw dug counts for all minable types.
        /// </summary>
        public void AdjustLocalStats(CastleMinerZPlayerStats stats, bool grant)
        {
            if (stats == null)
                return;

            if (grant)
            {
                int total = GetTotalMinableBlocksDug(stats);
                if (total < RequiredTotalDug)
                {
                    int missing = RequiredTotalDug - total;

                    if (MasterProspectorAchievement._minableBlockTypes.Length > 0)
                    {
                        var type =
                            MasterProspectorAchievement._minableBlockTypes[0];

                        int currentRaw =
                            PlayerStatReflectionHelper.GetBlocksDugCountRaw(
                                stats, type);

                        PlayerStatReflectionHelper.SetBlocksDugCountRaw(
                            stats,
                            type,
                            currentRaw + missing);
                    }
                }
            }
            else
            {
                foreach (var type in MasterProspectorAchievement._minableBlockTypes)
                {
                    PlayerStatReflectionHelper.SetBlocksDugCountRaw(
                        stats,
                        type,
                        0);
                }
            }
        }
        #endregion
    }
}