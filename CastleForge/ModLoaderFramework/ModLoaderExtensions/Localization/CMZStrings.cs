/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Globalization;
using DNA.CastleMinerZ;
using System.Resources;

namespace ModLoaderExt
{
    #region Localization (CMZ Resource Strings)

    /// <summary>
    /// Simple helper for accessing CastleMiner Z's localized resource strings at runtime.
    ///
    /// Purpose:
    /// - Provides a lightweight, reflection-friendly way to fetch entries from the game's
    ///   Resources (.resx) without referencing the internal generated Strings class directly.
    /// </summary>
    public static class CMZStrings
    {
        private static readonly ResourceManager RM =
            new ResourceManager("DNA.CastleMinerZ.Globalization.Strings", typeof(CastleMinerZGame).Assembly);

        /// <summary>
        /// Gets a localized string by resource key using the current UI culture.
        /// </summary>
        public static string Get(string key)
        {
            // You can also use CultureInfo.CurrentUICulture or null
            // (null lets ResourceManager decide).
            return RM.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
    }
    #endregion
}