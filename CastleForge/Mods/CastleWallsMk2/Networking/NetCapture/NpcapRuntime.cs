/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using System;

namespace CastleWallsMk2
{
    #region NpcapRuntime (Presence / Loadability Checks For Capture Stack)

    /// <summary>
    /// Runtime probe for whether packet capture can function on this machine.
    ///
    /// Context:
    /// - SharpPcap (WinPcap/Npcap API) ultimately needs wpcap.dll to be loadable.
    /// - When Npcap isn't installed (or isn't on the loader path), P/Invoke attempts
    ///   will throw "Unable to load DLL 'wpcap'".
    ///
    /// What this class checks:
    ///  1) Try loading "wpcap.dll" via the normal Windows DLL search rules.
    ///  2) If that fails, try loading from the common Npcap install directory:
    ///       %SystemRoot%\System32\Npcap\wpcap.dll
    ///  3) Also check a registry key as a best-effort "evidence of install".
    ///
    /// Notes / Caveats:
    /// - "Registry says present" does not guarantee the DLL is loadable (PATH/bitness/etc.).
    /// - Conversely, the DLL might be loadable even if the registry probe fails (custom installs).
    /// - This method is intentionally conservative: it only returns true when it can actually load
    ///   the library at runtime.
    /// - The Win32 error captured (Marshal.GetLastWin32Error) reflects the *last* load attempt.
    /// </summary>
    internal static class NpcapRuntime
    {
        #region Native Interop (kernel32 LoadLibrary / FreeLibrary)

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        #endregion

        #region Public API

        /// <summary>
        /// Returns true if wpcap.dll is loadable (meaning capture can likely run).
        ///
        /// Output detail:
        /// - Null when successful.
        /// - On failure, contains a human-readable summary including the Win32 error code
        ///   and whether the registry probe indicates Npcap is installed.
        /// </summary>
        internal static bool IsCaptureLibraryAvailable(out string detail)
        {
            detail = null;

            // 1) Normal DLL search (works when Npcap/WinPcap is properly installed).
            IntPtr h = LoadLibraryW("wpcap.dll");
            if (h != IntPtr.Zero)
            {
                FreeLibrary(h);
                return true;
            }
            int err = Marshal.GetLastWin32Error();

            // 2) Common Npcap location: C:\Windows\System32\Npcap\wpcap.dll
            // (Npcap commonly places DLLs under System32\Npcap).
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string npcapWpcap = Path.Combine(sys, "Npcap", "wpcap.dll");
            if (File.Exists(npcapWpcap))
            {
                h = LoadLibraryW(npcapWpcap);
                if (h != IntPtr.Zero)
                {
                    FreeLibrary(h);
                    return true;
                }
                err = Marshal.GetLastWin32Error();
            }

            // 3) Optional: Registry evidence of Npcap install.
            bool hasNpcapReg = false;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npcap\Parameters"))
                    hasNpcapReg = (k != null);
            }
            catch { }

            detail = $"wpcap.dll not loadable (Win32Error={err}). Registry says Npcap={(hasNpcapReg ? "present" : "absent")}.";
            return false;
        }
        #endregion
    }
    #endregion
}