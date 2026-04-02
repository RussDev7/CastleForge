/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Input;
using System.Linq;
using System;

namespace TexturePacks
{
    /// <summary>
    /// Parses and evaluates keyboard shortcuts (hotkeys) with both generic and side-specific modifiers.
    /// <para>
    /// Examples of accepted strings:
    ///   • "F12".
    ///   • "Ctrl+Shift+F12".
    ///   • "LAlt+RShift+D1" or simply "Alt+Shift+1".
    ///   • "None" (disables the hotkey).
    /// </para>
    /// <remarks>
    /// Priority rule: If any side-specific modifier is specified (e.g., LCtrl), it must be pressed,
    /// regardless of generic flags. Generic flags (Ctrl/Alt/Shift) accept either side.
    /// </remarks>
    /// </summary>
    class KeyboardListener
    {
        #region Fields & Flags

        /// <summary>Main trigger key (e.g., <see cref="Keys.F12"/>). Use <see cref="Keys.None"/> to disable.</summary>
        public Keys Key;                                      // Main key (e.g., F12).

        /// <summary>Generic modifier flags (either side is accepted when set).</summary>
        public bool Ctrl, Alt, Shift;                         // Generic modifiers.

        /// <summary>Side-specific modifier flags (must match the exact side when set).</summary>
        public bool LCtrl, RCtrl, LAlt, RAlt, LShift, RShift; // Specific sides.

        /// <summary>
        /// A sentinel instance representing a disabled hotkey (Key = <see cref="Keys.None"/>).
        /// </summary>
        public static readonly KeyboardListener Disabled = new KeyboardListener { Key = Keys.None };

        #endregion

        #region State Queries

        /// <summary>
        /// Returns true if the configured key + required modifiers are currently pressed.
        ///   1) Rejects immediately if <see cref="Key"/> is <see cref="Keys.None"/>.
        ///   2) Checks side-specific modifiers first (must match exactly when set).
        ///   3) Checks generic modifiers next (either side may satisfy them).
        ///   4) Finally, requires the main <see cref="Key"/> to be down.
        /// </summary>
        public bool IsDown(KeyboardState kb)
        {
            if (Key == Keys.None) return false;

            // Sided modifiers take precedence if specified.
            if (LCtrl  && !kb.IsKeyDown(Keys.LeftControl))  return false;
            if (RCtrl  && !kb.IsKeyDown(Keys.RightControl)) return false;
            if (LAlt   && !kb.IsKeyDown(Keys.LeftAlt))      return false;
            if (RAlt   && !kb.IsKeyDown(Keys.RightAlt))     return false;
            if (LShift && !kb.IsKeyDown(Keys.LeftShift))    return false;
            if (RShift && !kb.IsKeyDown(Keys.RightShift))   return false;

            // Generic modifiers (accept either side).
            if (Ctrl   && !(kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl))) return false;
            if (Alt    && !(kb.IsKeyDown(Keys.LeftAlt)     || kb.IsKeyDown(Keys.RightAlt)))     return false;
            if (Shift  && !(kb.IsKeyDown(Keys.LeftShift)   || kb.IsKeyDown(Keys.RightShift)))   return false;

            return kb.IsKeyDown(Key);
        }

        /// <summary>
        /// Returns true only on the rising edge: currently down (with modifiers) but was not down previously.
        /// Useful for "on-press" semantics to avoid repeat while held.
        /// </summary>
        public bool IsEdge(KeyboardState now, KeyboardState prev)
        {
            return IsDown(now) && !IsDown(prev);
        }
        #endregion

        #region Parsing

        /// <summary>
        /// Parses a human-friendly hotkey string (e.g., "Ctrl+Shift+F12") into a <see cref="KeyboardListener"/>.
        /// <para>
        /// Rules:
        ///   • Case-insensitive; tokens split on '+' with optional spaces.
        ///   • "None" (any casing) returns <see cref="Disabled"/>.
        ///   • Digits '0'..'9' map to D0..D9 keys if not parsed as named keys.
        ///   • Generic modifiers: Ctrl/Control, Alt, Shift.
        ///   • Side-specific modifiers: L/R Ctrl, L/R Alt, L/R Shift
        ///     (accepts forms like LCTRL, LeftControl, RightAlt, etc.).
        ///   • If any token is unrecognized, returns <see cref="Disabled"/> to fail safe.
        /// </para>
        /// </summary>
        public static KeyboardListener Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Disabled;
            s = s.Trim();

            // Allow "None" to disable.
            if (string.Equals(s, "None", StringComparison.OrdinalIgnoreCase)) return Disabled;

            var hk = new KeyboardListener { Key = Keys.None };

            // Split on '+' and optional spaces.
            var parts = s.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(p => p.Trim())
                         .Where(p => p.Length > 0);

            foreach (var partRaw in parts)
            {
                var part = partRaw.ToLowerInvariant();

                // --- Generic modifiers (either side ok) ---
                if (part == "ctrl"   || part == "control")    { hk.Ctrl = true; continue; }
                if (part == "shift") { hk.Shift = true; continue; }
                if (part == "alt")   { hk.Alt = true; continue; }

                // --- Side-specific modifiers ---
                if (part == "lctrl"  || part == "leftctrl"  || part == "leftcontrol")  { hk.LCtrl = true; continue; }
                if (part == "rctrl"  || part == "rightctrl" || part == "rightcontrol") { hk.RCtrl = true; continue; }

                if (part == "lshift" || part == "leftshift")  { hk.LShift = true; continue; }
                if (part == "rshift" || part == "rightshift") { hk.RShift = true; continue; }

                if (part == "lalt"   || part == "leftalt")    { hk.LAlt = true; continue; }
                if (part == "ralt"   || part == "rightalt")   { hk.RAlt = true; continue; }

                // Otherwise treat as the main key name (Keys enum).
                if (Enum.TryParse(partRaw, true, out Keys parsed))
                {
                    hk.Key = parsed;
                    continue;
                }

                // Common aliases for digits (map '0'..'9' -> D0..D9).
                if (part.Length == 1 && part[0] >= '0' && part[0] <= '9')
                {
                    hk.Key = (Keys)Enum.Parse(typeof(Keys), "D" + part, true);
                    continue;
                }

                // If we get here, it's unrecognized; disable to be safe.
                hk = Disabled;
                break;
            }

            return hk;
        }
        #endregion
    }
}