/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.UI;
using DNA.CastleMinerZ;
using System;

namespace MoreAchievements
{
    /// <summary>
    /// Central rules for which game modes allow custom achievements to unlock.
    /// Backed by MAConfig.CustomUnlockGameModes (comma-separated list or "All").
    /// </summary>
    internal static class AchievementModeRules
    {
        #region GameModeMask Enum

        [Flags]
        internal enum GameModeMask
        {
            None            = 0,
            Endurance       = 1 << 0,
            Survival        = 1 << 1,
            DragonEndurance = 1 << 2,
            Creative        = 1 << 3,
            Exploration     = 1 << 4,
            Scavenger       = 1 << 5,

            All = Endurance | Survival | DragonEndurance |
                  Creative | Exploration | Scavenger
        }
        #endregion

        #region Public State

        /// <summary>
        /// Bitmask of allowed game modes for custom achievements.
        /// Default is Endurance-only (matches vanilla).
        /// </summary>
        public static GameModeMask CustomUnlockMask { get; private set; } =
            GameModeMask.Endurance;

        #endregion

        #region Apply From Config

        /// <summary>
        /// Parse a CSV like "Endurance,Survival" or "All" into a mask.
        /// Called once from MAConfig.ApplyToStatics(...).
        /// </summary>
        public static void ApplyFromConfig(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                CustomUnlockMask = GameModeMask.Endurance;
                return;
            }

            var parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            GameModeMask mask = GameModeMask.None;

            foreach (var raw in parts)
            {
                var token = raw.Trim();

                // "All" overrides everything else.
                if (token.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    mask = GameModeMask.All;
                    break;
                }

                if      (token.Equals(nameof(GameModeTypes.Endurance),       StringComparison.OrdinalIgnoreCase)) mask |= GameModeMask.Endurance;
                else if (token.Equals(nameof(GameModeTypes.Survival),        StringComparison.OrdinalIgnoreCase)) mask |= GameModeMask.Survival;
                else if (token.Equals(nameof(GameModeTypes.DragonEndurance), StringComparison.OrdinalIgnoreCase)) mask |= GameModeMask.DragonEndurance;
                else if (token.Equals(nameof(GameModeTypes.Creative),        StringComparison.OrdinalIgnoreCase)) mask |= GameModeMask.Creative;
                else if (token.Equals(nameof(GameModeTypes.Exploration),     StringComparison.OrdinalIgnoreCase)) mask |= GameModeMask.Exploration;
                else if (token.Equals(nameof(GameModeTypes.Scavenger),       StringComparison.OrdinalIgnoreCase)) mask |= GameModeMask.Scavenger;
            }

            // Fallback: If nothing valid was parsed, default to vanilla behavior.
            if (mask == GameModeMask.None)
                mask = GameModeMask.Endurance;

            CustomUnlockMask = mask;
        }
        #endregion

        #region Query

        /// <summary>
        /// Returns true if custom achievements are allowed to unlock in the
        /// game's current GameModeTypes value, according to CustomUnlockMask.
        /// </summary>
        public static bool IsCustomAchievementAllowedNow()
        {
            var modeOpt = TryGetCurrentGameMode();
            if (!modeOpt.HasValue)
                return false;

            var mode = modeOpt.Value;

            if (CustomUnlockMask == GameModeMask.All)
                return true;

            switch (mode)
            {
                case GameModeTypes.Endurance:
                    return (CustomUnlockMask & GameModeMask.Endurance) != 0;

                case GameModeTypes.Survival:
                    return (CustomUnlockMask & GameModeMask.Survival) != 0;

                case GameModeTypes.DragonEndurance:
                    return (CustomUnlockMask & GameModeMask.DragonEndurance) != 0;

                case GameModeTypes.Creative:
                    return (CustomUnlockMask & GameModeMask.Creative) != 0;

                case GameModeTypes.Exploration:
                    return (CustomUnlockMask & GameModeMask.Exploration) != 0;

                case GameModeTypes.Scavenger:
                    return (CustomUnlockMask & GameModeMask.Scavenger) != 0;

                default:
                    return false;
            }
        }
        #endregion

        #region Current Game Mode Helper

        /// <summary>
        /// Helper that reads the current GameModeTypes from CastleMinerZGame.
        /// </summary>
        private static GameModeTypes? TryGetCurrentGameMode()
        {
            var game = CastleMinerZGame.Instance;
            if (game == null)
                return null;

            return game.GameMode;
        }
        #endregion
    }
}