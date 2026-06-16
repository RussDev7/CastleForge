/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Text;
using System.Net;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Handles optional name text transforms before Castle Walls writes to
    /// Gamer.Gamertag / SignedInGamer.Gamertag.
    ///
    /// This is useful because CastleMiner Z's Steam name path can display UTF-8
    /// names as ANSI/CP1251 mojibake, while directly setting true Unicode through
    /// Castle Walls may render unsupported glyphs as '*'.
    /// </summary>
    internal static class NameEncodingHelper
    {
        private const string Cp1251Prefix = "cp1251:";

        /// <summary>
        /// Prepares a user-entered Castle Walls name.
        ///
        /// Normal input:
        ///     TestName
        ///
        /// Mojibake input:
        ///     cp1251:БЕ-Z-ТУНДРЫЧ
        ///
        /// Result:
        ///     Р‘Р•-Z-РўРЈРќР”Р Р«Р§
        /// </summary>
        public static string PrepareName(string raw)
        {
            string name = raw ?? string.Empty;

            // Decode HTML entities -> Unicode.
            // "<test>"           => "<test>".
            // "&#33;hello&Delta;&#33;" => "!helloΔ!".
            name = WebUtility.HtmlDecode(raw);

            // Optional: Decode twice to handle "&amp;lt;" => "<" => "<".
            name = WebUtility.HtmlDecode(name);

            if (name.StartsWith(Cp1251Prefix, StringComparison.OrdinalIgnoreCase))
            {
                string value = name.Substring(Cp1251Prefix.Length);
                return Utf8BytesDecodedAsCodePage(value, 1251);
            }

            return name;
        }

        /// <summary>
        /// Recreates the Steam-style mojibake bug:
        /// UTF-8 bytes interpreted as a legacy ANSI code page.
        /// </summary>
        private static string Utf8BytesDecodedAsCodePage(string text, int codePage)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);
            return Encoding.GetEncoding(codePage).GetString(utf8Bytes);
        }
    }
}