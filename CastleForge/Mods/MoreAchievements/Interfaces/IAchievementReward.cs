/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

namespace MoreAchievements
{
    /// <summary>
    /// Optional interface for achievements that want to grant a
    /// one-time reward when they are first unlocked.
    ///
    /// Pattern mirrors <see cref="ILocalStatAdjust"/>:
    ///   • Implement on custom achievement classes.
    ///   • ExtendedAchievementManager.OnAchieved will call
    ///     <see cref="GrantReward"/> exactly once when the
    ///     achievement is granted.
    /// </summary>
    public interface IAchievementReward
    {
        /// <summary>
        /// Called after the achievement is unlocked and the
        /// vanilla manager has run its normal pipeline.
        ///
        /// Use this to:
        ///   • Grant items / currency / XP.
        ///   • Flip custom mod flags.
        ///   • Do any other one-shot side effects.
        ///
        /// You get the live <see cref="DNA.CastleMinerZ.CastleMinerZPlayerStats"/>
        /// backing the achievement manager for convenience.
        /// </summary>
        void GrantReward(DNA.CastleMinerZ.CastleMinerZPlayerStats stats);
    }
}