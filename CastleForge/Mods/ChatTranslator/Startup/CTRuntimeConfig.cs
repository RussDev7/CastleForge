/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

namespace ChatTranslator
{
    /// <summary>
    /// Live values used by ChatTranslator at runtime.
    /// Filled from CTConfig.LoadApply(), so the rest of the mod
    /// can just read these without touching INI parsing.
    /// </summary>
    internal static class CTRuntimeConfig
    {
        /// <summary>
        /// Your own baseline language (what you type/read in), normalized.
        /// Example: "en", "es", "de".
        /// </summary>
        public static string BaseLanguage = "en";

        /// <summary>
        /// Optional default remote language when manual mode is used.
        /// Empty string means "start with translation off".
        /// </summary>
        public static string DefaultRemoteLanguage = string.Empty;

        /// <summary>
        /// Hotkey string to reload the ChatTranslator config at runtime.
        /// </summary>
        public static string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Future knobs example:
        // public static bool EnableFileLogging = true;
        // public static bool AutoModeOnByDefault = false;
    }
}