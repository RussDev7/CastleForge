/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Security.Cryptography;
using System.Windows.Forms;
using System.Text;
using System.IO;
using System;

namespace ModLoader
{
    /// <summary>
    /// Guard that detects when the *installed* game core files differ from the
    /// *compiled-against* baselines and asks the user what to do.
    ///
    /// When to call:
    ///   • After the launcher hands control to the game EXE
    ///   • Before the ModLoader discovers/loads any mods
    ///
    /// Baselines:
    ///   • CoreHashes.GameExe_SHA256
    ///   • CoreHashes.DNACommon_SHA256
    ///
    /// Behavior:
    ///   1) If both baselines are present (64 hex chars) and the on-disk hashes differ,
    ///      show a "continue with/without mods or exit" dialog.
    ///   2) If the user previously accepted the exact current hashes, skip the dialog.
    /// </summary>
    internal static class HashGuard
    {
        #region Public API

        /// <summary>
        /// Compare baseline (compiled-with) vs on-disk (current) hashes for
        /// CastleMinerZ.exe and DNA.Common.dll. If mismatched and not previously
        /// accepted, prompt the user. Returns the user's choice.
        /// </summary>
        public static HashDecision VerifyGameCoreHashOrPrompt()
        {
            // Read built-in baselines (generated at build by your MSBuild task).
            var baselineGameExe   = (CoreHashes.GameExe_SHA256   ?? "").Trim();
            var baselineDNACommon = (CoreHashes.DNACommon_SHA256 ?? "").Trim();

            // No baselines? Treat as "OK to proceed with mods".
            // (Both should be 64 hex chars if present.)
            if (baselineGameExe.Length != 64 || baselineDNACommon.Length != 64) return HashDecision.ProceedWithMods;

            // Compute current on-disk hashes (files next to the game EXE).
            string path_GameExe   = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CastleMinerZ.exe");
            string path_DNACommon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DNA.Common.dll");
            var currentGameExe    = Sha256Hex(path_GameExe);
            var currentDNACommon  = Sha256Hex(path_DNACommon);

            // Couldn't compute hashes? Fail open (don't block mods).
            if (string.IsNullOrEmpty(currentGameExe) || string.IsNullOrEmpty(currentDNACommon)) return HashDecision.ProceedWithMods;

            // Exact match -> silently proceed with mods.
            if (string.Equals(baselineGameExe  , currentGameExe  , StringComparison.OrdinalIgnoreCase) &&
                string.Equals(baselineDNACommon, currentDNACommon, StringComparison.OrdinalIgnoreCase))
                return HashDecision.ProceedWithMods;

            // If the user already accepted this exact pair of hashes before, allow without nagging.
            if (!ModLoaderConfig.TryLoad(out var cfg)) cfg = new ModLoaderConfig();
            if (!string.IsNullOrEmpty(cfg.LastAcceptedGameExeSHA256)   && string.Equals(cfg.LastAcceptedGameExeSHA256  , currentGameExe  , StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(cfg.LastAcceptedDNACommonSHA256) && string.Equals(cfg.LastAcceptedDNACommonSHA256, currentDNACommon, StringComparison.OrdinalIgnoreCase))
            {
                return HashDecision.ProceedWithMods; // User accepted this exact hash earlier.
            }

            // Mismatch (and not accepted before): Warn & ask what to do.
            var msg                                                                        =
                $"CastleMinerZ core appears to have been updated.\n"                       +
                $"\n"                                                                      +
                $"Mods were built for:\n"                                                  +
                $"    CastleMinerZ.exe SHA-256: {Short(baselineGameExe)}\n"                +
                $"    DNA.Common.dll SHA-256: {Short(baselineDNACommon)}\n"                +
                $"\n"                                                                      +
                $"Your game currently has:\n"                                              +
                $"    CastleMinerZ.exe SHA-256: {Short(currentGameExe)}\n"                 +
                $"    DNA.Common.dll SHA-256: {Short(currentDNACommon)}\n"                 +
                $"\n"                                                                      +
                $"Running with mods on a new build can cause crashes or weird behavior.\n" +
                $"\n"                                                                      +
                $"Choose an action:\n"                                                     +
                $"    • Yes     = Continue WITH mods (at your own risk).\n"                +
                $"    • No      = Continue WITHOUT mods.\n"                                +
                $"    • Cancel = Exit game.\n"                                             ;

            var res = MessageBox.Show(
                msg, "ModLoader - Core Hash Mismatch",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            switch (res)
            {
                case DialogResult.Yes:
                    // Remember this pair so we don't ask again next run.
                    cfg.LastAcceptedGameExeSHA256   = currentGameExe;
                    cfg.LastAcceptedDNACommonSHA256 = currentDNACommon;
                    try { cfg.Save(); } catch { }
                    return HashDecision.ProceedWithMods;

                case DialogResult.No:
                    return HashDecision.ProceedWithoutMods;

                case DialogResult.Cancel:
                default:
                    return HashDecision.Abort;
            }
        }
        #endregion

        #region Helpers

        /// <summary>Show shortened hashes for readability (first 20 chars), but keep them aligned.</summary>
        private static string Short(string hex) => (string.IsNullOrEmpty(hex) || hex.Length < 20) ? hex : hex.Substring(0, 20) + "...";

        /// <summary>
        /// Computes the SHA-256 of a file and returns a lowercase hex string.
        /// Uses FileShare.Read to avoid fighting with other readers.
        /// </summary>
        private static string Sha256Hex(string path)
        {
            try
            {
                using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; } // Never throw from guard code.
        }
        #endregion
    }
}
