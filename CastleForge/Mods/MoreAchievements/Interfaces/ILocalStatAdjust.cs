/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

namespace MoreAchievements
{
    /// <summary>
    /// Optional interface for achievements that want to control
    /// their own local stat adjustments when granted/revoked.
    /// </summary>
    public interface ILocalStatAdjust
    {
        /// <summary>
        /// Adjust local player stats for this achievement.
        /// grant == true  -> bump stats to satisfy this achievement.
        /// grant == false -> roll back / clear stats for this achievement.
        /// </summary>
        void AdjustLocalStats(DNA.CastleMinerZ.CastleMinerZPlayerStats stats, bool grant);
    }
}