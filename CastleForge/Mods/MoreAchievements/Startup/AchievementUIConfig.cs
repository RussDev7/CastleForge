/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

namespace MoreAchievements
{
    /// <summary>
    /// What kinds of achievements should show a HUD popup.
    /// </summary>
    internal enum AchievementPopupMode
    {
        None,   // No HUD toast at all.
        Custom, // Only MoreAchievements custom achievements.
        Steam,  // Only vanilla/Steam achievements.
        All,    // Both vanilla + custom.
    }

    /// <summary>
    /// How to sort achievements within difficulty buckets.
    /// </summary>
    internal enum AchievementSortMode
    {
        ByName, // Alphabetical by achievement name.
        ById    // Numeric / ID-based order.
    }

    /// <summary>
    /// Live values used by the achievement UI. These get filled from the INI.
    /// </summary>
    internal static class AchievementUIConfig
    {
        // Layout (defaults - used if config missing or invalid).
        public static int  RowH       = 80;
        public static int  RowPad     = 4;
        public static int  PanelPad   = 20;
        public static int  TitleGap   = 12;
        public static int  ButtonsGap = 16;
        public static int  IconPad    = 8;
        public static int  IconGap    = 12;

        // Behaviour.
        public static bool PlaySounds = true;

        /// <summary>
        /// If true, send a chat BroadcastTextMessage when an achievement pops.
        /// </summary>
        public static bool AnnounceChat = true;

        /// <summary>
        /// If true, show HUD toasts / announcements for *all* achievements
        /// (vanilla + custom). If false, only custom achievements from
        /// MoreAchievements get toasts.
        /// </summary>
        public static bool AnnounceAllAchievements = false;

        /// <summary>
        /// If true and a custom Award.(mp3|wav) exists in
        /// !Mods\MoreAchievements\CustomSounds, use it instead of
        /// the stock "Award" sound cue.
        /// </summary>
        public static bool UseCustomAwardSound = true;

        /// <summary>
        /// What kind of achievements should show HUD popups.
        /// </summary>
        public static AchievementPopupMode ShowAchievementPopupMode = AchievementPopupMode.All;

        /// <summary>
        /// Current sort mode for the browser.
        /// </summary>
        public static AchievementSortMode SortMode = AchievementSortMode.ByName;

        /// <summary>
        /// Parse "None|Custom|Steam|All" into an enum, with a safe default.
        /// </summary>
        public static AchievementPopupMode ParsePopupMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return AchievementPopupMode.All;

            switch (value.Trim().ToLowerInvariant())
            {
                case "none":   return AchievementPopupMode.None;
                case "custom": return AchievementPopupMode.Custom;
                case "steam":  return AchievementPopupMode.Steam;
                case "all":    return AchievementPopupMode.All;
                default:       return AchievementPopupMode.All;
            }
        }

        /// <summary>
        /// Parses the sort mode from config. Accepts "Name" or "Id"
        /// (case-insensitive), plus a few synonyms.
        /// </summary>
        internal static AchievementSortMode ParseSortMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return AchievementSortMode.ByName;

            switch (value.Trim().ToLowerInvariant())
            {
                case "id":
                case "numeric":
                case "numericid":
                case "byid":
                    return AchievementSortMode.ById;

                case "name":
                case "byname":
                default:
                    return AchievementSortMode.ByName;
            }
        }
    }
}