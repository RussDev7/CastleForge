/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Globalization;
using System.IO;
using System;

namespace WeaponAddons
{
    // =========================================================================================
    // .CLAG Parsing (Weapon Addons)
    // =========================================================================================
    //
    // Summary:
    // - Reads a simple, line-based "CLAG" definition file into a dictionary.
    // - First non-empty line is treated as the document "Type" (e.g., "Firearm").
    // - Remaining lines are key/value pairs in the form:
    //      <sigils...><KEY>: <VALUE>
    //
    // Supported behaviors:
    // - Comment lines begin with ';' or '//' (kept distinct so '#CLIP_SIZE' remains valid).
    // - Keys may include leading sigils (e.g. $, ", %, #, =, *) which are stripped for tolerance.
    //   This intentionally makes the format forgiving (ex: accidental $"MODEL" is accepted).
    //
    // Helpers:
    // - ParseFloat: accepts both "0.8" and "0,8" (comma decimal).
    // - ParseInt / ParseBool: simple conversions with safe defaults.
    // - ParseRgb / ParseRgb01: supports "R,G,B" and returns Color or normalized Vector4.
    // =========================================================================================

    #region Data Model

    /// <summary>
    /// Parsed representation of a .clag file.
    ///
    /// Summary:
    /// - Type: first non-empty line (category token such as "Firearm").
    /// - Raw: key/value map (case-insensitive).
    ///
    /// Notes:
    /// - Keys are stored WITHOUT their sigil prefixes ($, ", %, #, =, *).
    /// - Missing keys resolve to defaults via Get(...).
    /// </summary>
    internal sealed class ClagDoc
    {
        public string Type; // First line (e.g. "Firearm").
        public Dictionary<string, string> Raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string key, string def = null)
            => Raw.TryGetValue(key, out var v) ? v : def;

        public bool GetBool(string key, bool def)
            => ClagParser.ParseBool(Get(key, null), def);

        public int GetInt(string key, int def)
            => ClagParser.ParseInt(Get(key, null), def);
    }
    #endregion

    #region Parser + Conversion Helpers

    /// <summary>
    /// Minimal parser + typed conversion helpers for .clag data.
    ///
    /// Summary:
    /// - ParseFile: loads a .clag file into a ClagDoc (Type + Raw dictionary).
    /// - ParseFloat / ParseInt / ParseBool: safe typed conversions with defaults.
    /// - ParseRgb / ParseRgb01: convert "R,G,B" into Color or normalized Vector4.
    ///
    /// Notes:
    /// - All parsing is "best-effort": invalid/malformed lines are ignored.
    /// - IO exceptions produce an empty ClagDoc (Type null, Raw empty).
    /// </summary>
    internal static class ClagParser
    {
        /// <summary>
        /// Reads a .clag file from disk and produces a ClagDoc.
        ///
        /// Summary:
        /// - The first non-empty, non-comment line becomes doc.Type.
        /// - Each subsequent non-empty, non-comment line with "KEY: VALUE" is stored in Raw.
        /// - Leading sigils ($, ", %, #, =, *) are stripped before key parsing.
        ///
        /// Notes:
        /// - Comment lines:
        ///   • ';' (INI-style)
        ///   • '//' (C++ style)
        /// - Intentionally does NOT treat '#' as a comment, because '#CLIP_SIZE' is a valid key form.
        /// </summary>
        public static ClagDoc ParseFile(string path)
        {
            var doc = new ClagDoc();

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return doc; }

            bool firstNonEmpty = true;

            foreach (var raw in lines)
            {
                var line = (raw ?? "").Trim();
                if (line.Length == 0) continue;

                // Comments (keep ';' and '//' as comments so '#CLIP_SIZE' still works).
                if (line.StartsWith(";") || line.StartsWith("//"))
                    continue;

                if (firstNonEmpty)
                {
                    doc.Type = line.Trim();
                    firstNonEmpty = false;
                    continue;
                }

                // Allow accidental leading $" or "" on keys (your sample had $"MODEL).
                while (line.Length > 0 && (line[0] == '$' || line[0] == '"' || line[0] == '%' || line[0] == '#' || line[0] == '=' || line[0] == '*'))
                {
                    // We keep *one* sigil meaning by stripping all and relying on key name;
                    // value parsing is handled in helpers by key.
                    // This makes the format forgiving.
                    line = line.Substring(1).TrimStart();
                }

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                var key = line.Substring(0, colon).Trim();
                var val = line.Substring(colon + 1).Trim();

                if (key.Length == 0) continue;
                doc.Raw[key] = val;
            }

            return doc;
        }

        /// <summary>
        /// Parses a float using invariant culture.
        ///
        /// Summary:
        /// - Accepts both "0.8" and "0,8" by normalizing ',' -> '.'.
        /// - Returns def on missing/invalid values.
        /// </summary>
        public static float ParseFloat(string s, float def = 0f)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;

            // Accept "0,8" as "0.8".
            s = s.Trim().Replace(',', '.');

            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : def;
        }

        /// <summary>
        /// Parses an int using invariant culture.
        /// Summary: Returns def on missing/invalid values.
        /// </summary>
        public static int ParseInt(string s, int def = 0)
            => int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Parses a bool from:
        /// - "true/false" (case-insensitive), or
        /// - "0/1" (and other integers: non-zero = true).
        ///
        /// Summary: Returns def on missing/invalid values.
        /// </summary>
        public static bool ParseBool(string s, bool def = false)
        {
            s = (s ?? "").Trim();
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }

        /// <summary>
        /// Parses "R,G,B" into a Color (alpha forced to 255).
        ///
        /// Summary:
        /// - Returns def if:
        ///   • input is missing,
        ///   • fewer than 3 components,
        ///   • any component fails byte parsing.
        /// </summary>
        public static Color ParseRgb(string s, Color def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            var parts = s.Split(',');
            if (parts.Length < 3) return def;

            bool okR = byte.TryParse(parts[0].Trim(), out var r);
            bool okG = byte.TryParse(parts[1].Trim(), out var g);
            bool okB = byte.TryParse(parts[2].Trim(), out var b);

            if (!okR || !okG || !okB) return def;
            return new Color(r, g, b, 255);
        }

        /// <summary>
        /// Parses "R,G,B" into a normalized Vector4 (0..1), alpha forced to 1.
        ///
        /// Summary:
        /// - Uses ParseRgb for validation/fallback behavior.
        /// - Output is suitable for shader parameters and Color(Vector4)-style usages.
        /// </summary>
        public static Vector4 ParseRgb01(string s)
        {
            var c = ParseRgb(s, Color.White);
            // Vector4 is 0..1 for Color(Vector4) usage patterns.
            return new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
        }
    }
    #endregion
}