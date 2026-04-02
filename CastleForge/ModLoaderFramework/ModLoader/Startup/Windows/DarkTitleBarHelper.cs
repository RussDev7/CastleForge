/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.InteropServices;
using System;

namespace ModLoader
{
    /// <summary>
    /// Requests the OS-provided "immersive dark" title bar (dark caption & frame)
    /// for a Win32 window. This is a Windows 10/11-only attribute exposed via DWM.
    ///
    /// Notes:
    /// - Works on Win10 1809+ and Win11. On older builds the call safely no-ops.
    /// - Tries attribute 20 (modern) first, then 19 (older) for 1809 support.
    /// - Only affects the non-client area (title bar / frame). You should still set
    ///   Form.BackColor / ForeColor to match your dark theme if desired.
    /// - High-contrast or system policies can override the result.
    /// </summary>
    internal static class DarkTitleBarHelper
    {
        #region DWM interop

        // Attribute IDs:
        //  - 20: DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 1903+, Win11).
        //  - 19: DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 1809) - same meaning, older value.
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE             = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        // Minimal P/Invoke wrapper for setting a single DWM window attribute.
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int    dwAttribute,
            ref int pvAttribute,
            int    cbAttribute);

        #endregion

        #region Public API

        /// <summary>
        /// Attempts to enable or disable the OS dark title bar for the given HWND.
        /// Returns true if the OS accepted the request (hr == 0), false otherwise.
        /// Safe to call multiple times; safe on unsupported Windows (returns false).
        /// </summary>
        /// <param name="hWnd">A valid top-level window handle (Form.Handle).</param>
        /// <param name="enabled">true = dark; false = light.</param>
        public static bool Apply(IntPtr hWnd, bool enabled = true)
        {
            if (hWnd == IntPtr.Zero)
                return false; // No handle yet (call from OnHandleCreated or Shown).

            int useDark = enabled ? 1 : 0;

            // Try the modern attribute first (Win10 1903+ / Win11).
            int hr = DwmSetWindowAttribute(
                hWnd,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useDark,
                sizeof(int));

            // If that failed (e.g., running on 1809), try the older ID.
            if (hr != 0)
            {
                hr = DwmSetWindowAttribute(
                    hWnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1,
                    ref useDark,
                    sizeof(int));
            }

            // hr == 0 means S_OK.
            return hr == 0;
        }
        #endregion
    }
}