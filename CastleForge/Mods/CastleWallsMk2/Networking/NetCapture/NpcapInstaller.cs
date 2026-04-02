/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System;

namespace CastleWallsMk2
{
    #region NpcapInstaller (Embedded Installer Launcher)

    /// <summary>
    /// Helper: launches the embedded Npcap installer (Windows) with an elevated UAC prompt.
    ///
    /// Why this exists:
    /// - SharpPcap requires a capture provider (Npcap/WinPcap) to load wpcap.dll.
    /// - If Npcap is missing, capture enable should optionally offer an install path.
    ///
    /// Behavior:
    /// - Extracts an embedded resource (the installer EXE) to %TEMP%.
    /// - Launches it with "runas" so Windows requests admin elevation (UAC).
    /// - Returns false with a human-readable error string on failure/cancel.
    ///
    /// Notes / Caveats:
    /// - This does not wait for install completion; caller should instruct user to re-enable capture.
    /// - The temp filename is fixed ("npcap-1.87.exe"); repeated launches overwrite it.
    /// - If the resource name is wrong, the installer stream will be null and an error is returned.
    /// - Win32Exception NativeErrorCode 1223 indicates the user cancelled the UAC prompt.
    /// </summary>
    internal static class NpcapInstaller
    {
        #region Public API

        /// <summary>
        /// Extracts the embedded Npcap installer to a temp file and launches it elevated.
        ///
        /// Inputs:
        /// - resourceName: full manifest resource name for the embedded "npcap-1.87.exe"
        ///
        /// Outputs:
        /// - error: null on success, otherwise a user-facing error string.
        ///
        /// Returns:
        /// - true if the installer process was started successfully.
        /// - false if extraction failed, resource was missing, or user cancelled UAC.
        /// </summary>
        internal static bool LaunchEmbeddedNpcapInstaller(string resourceName, out string error)
        {
            error = null;

            try
            {
                string tempExe = Path.Combine(Path.GetTempPath(), "npcap-1.87.exe");

                // Extract embedded resource -> Temp file.
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (s == null)
                    {
                        error = $"Embedded installer resource not found: {resourceName}";
                        return false;
                    }
                    using (var fs = File.Create(tempExe))
                        s.CopyTo(fs);
                }

                var psi = new ProcessStartInfo(tempExe)
                {
                    UseShellExecute = true,
                    Verb = "runas" // Triggers UAC prompt.
                };

                Process.Start(psi);
                return true;
            }
            catch (Win32Exception wex)
            {
                // 1223 = User cancelled UAC prompt.
                if (wex.NativeErrorCode == 1223)
                {
                    error = "Npcap install cancelled (UAC prompt dismissed).";
                    return false;
                }

                error = wex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        #endregion
    }
    #endregion
}