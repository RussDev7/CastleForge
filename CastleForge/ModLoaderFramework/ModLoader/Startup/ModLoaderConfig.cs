/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

namespace ModLoader
{
    /// <summary>
    /// Persisted startup choice for the mod loader.
    /// Minimal key=value file at "ModLoader.ini" next to the EXE:
    ///   # comment
    ///   Remember=true|false
    ///   Mode=WithMods|WithoutMods
    ///
    /// Notes:
    /// • Parser is tolerant: Ignores blank lines, comments, and malformed lines.
    /// • IO failures are swallowed; not fatal to gameplay.
    /// • We intentionally do not store Abort (exit) - that should never be sticky.
    /// </summary>
    public sealed class ModLoaderConfig
    {
        public bool Remember                      { get; set; }
        public LaunchMode Mode                    { get; set; } = LaunchMode.WithMods;
        public bool ShowModLoadProgress           { get; set; } = true;
        public ExceptionCaptureMode ExceptionMode { get; set; } = ExceptionCaptureMode.CaughtOnly;
        public string LastAcceptedGameExeSHA256   { get; set; }
        public string LastAcceptedDNACommonSHA256 { get; set; }

        /// <summary>Config path: Sibling to the game EXE.</summary>
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModLoader.ini");

        /// <summary>
        /// Attempts to read ModLoader.ini into a <see cref="ModLoaderConfig"/>.
        /// Returns false if file missing or unreadable; cfg will be default-initialized.
        /// </summary>
        public static bool TryLoad(out ModLoaderConfig cfg)
        {
            cfg = new ModLoaderConfig();
            if (!File.Exists(ConfigPath))
                return false;

            try
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();
                    dict[key] = val;
                }

                if (dict.TryGetValue("LastAcceptedGameExeSHA256", out var okGameExeHash))
                    cfg.LastAcceptedGameExeSHA256 = okGameExeHash;

                if (dict.TryGetValue("LastAcceptedDNACommonSHA256", out var okDNACommonHash))
                    cfg.LastAcceptedDNACommonSHA256 = okDNACommonHash;

                if (dict.TryGetValue("Remember", out var rem))
                    cfg.Remember = string.Equals(rem, "true", StringComparison.OrdinalIgnoreCase);

                if (dict.TryGetValue("Mode", out var modeStr) &&
                    Enum.TryParse(modeStr, ignoreCase: true, out LaunchMode mode))
                    cfg.Mode = mode;

                if (dict.TryGetValue("ShowModLoadProgress", out var smlp))
                    cfg.ShowModLoadProgress = string.Equals(smlp, "true", StringComparison.OrdinalIgnoreCase);

                // Accept either key for future-proofing.
                if (dict.TryGetValue("ExceptionCaptureMode", out var exStr) ||
                    dict.TryGetValue("ExceptionMode", out exStr))
                {
                    if (Enum.TryParse(exStr, true, out ExceptionCaptureMode em))
                        cfg.ExceptionMode = em;
                }

                return true;
            }
            catch
            {
                // Treat as "no config" on any failure.
                return false;
            }
        }

        /// <summary>
        /// Writes the current settings to ModLoader.ini.
        /// Safe to call even if the directory is read-only (errors are swallowed).
        /// </summary>
        public void Save()
        {
            try
            {
                var lines = new[]
                {
                    $"# ======================================================================",
                    $"# CastleMinerZ ModLoader - Startup configuration:",
                    $"#",
                    $"# This file controls the startup prompt shown when the ModLoader boots.",
                    $"# Lines beginning with '#' are comments and are ignored by the loader.",
                    $"#",
                    $"# Keys:",
                    $"#   LastAcceptedGameExeSHA256 / LastAcceptedDNACommonSHA256",
                    $"#     - Set automatically when you accept a new game core hash",
                    $"#       in the ModLoader warning dialog. If these match your current",
                    $"#       install, the warning is skipped next launch.",
                    $"#     - You can clear these to force the warning to show again.",
                    $"#",
                    $"#   Remember = true|false",
                    $"#     - true  -> Skip the prompt next launch and use the Mode below.",
                    $"#     - false -> Always show the prompt on startup.",
                    $"#",
                    $"#   Mode = WithMods|WithoutMods",
                    $"#     - WithMods    -> Load mods from the !Mods folder.",
                    $"#     - WithoutMods -> Start the game without loading mods.",
                    $"#     - (Abort is not persisted; use the dialog's Exit button to quit.)",
                    $"#",
                    $"#   ShowModLoadProgress = true|false",
                    $"#     - true  -> Show the temporary mod loading progress window.",
                    $"#     - false -> Load mods silently with no progress window.",
                    $"#     - Default: true",
                    $"#",
                    $"#   ExceptionCaptureMode = Off|CaughtOnly|FirstChance",
                    $"#   Requires ModLoaderExt. If not present, this setting is ignored.",
                    $"#     - Off         -> No extra exception logging.",
                    $"#     - CaughtOnly  -> Log what Program/Main/crash-tap sees.",
                    $"#     - FirstChance -> Log EVERY thrown exception (very noisy).",
                    $"#",
                    $"# Tips:",
                    $"#   • To reset to defaults, delete this file or set Remember=false.",
                    $"#   • Invalid or unknown keys are ignored.",
                    $"#   • This file is read once at startup.",
                    $"#",
                    $"# Saved: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}.",
                    $"# ======================================================================",
                    $"",
                    $"LastAcceptedGameExeSHA256=" + (LastAcceptedGameExeSHA256 ?? ""),
                    $"LastAcceptedDNACommonSHA256=" + (LastAcceptedDNACommonSHA256 ?? ""),
                    $"",
                    $"Remember={(Remember ? "true" : "false")}",
                    $"Mode={Mode}",
                    $"ShowModLoadProgress={(ShowModLoadProgress ? "true" : "false")}",
                    $"ExceptionCaptureMode={ExceptionMode}"
                };

                File.WriteAllLines(ConfigPath, lines);
            }
            catch
            {
                // Non-fatal: Failure to save should not break the game.
            }
        }
    }

    /// <summary> .</summary>
    public enum HashDecision
    {
        ProceedWithMods,
        ProceedWithoutMods,
        Abort
    }

    /// <summary>Selected startup behavior for this run.</summary>
    public enum LaunchMode
    {
        WithMods,
        WithoutMods,
        Abort        // Caller should terminate the game loop; never persisted.
    }

    /// <summary>
    /// Controls how aggressively we capture/log exceptions.
    /// Off         -> No extra logging.
    /// CaughtOnly  -> Log what the game would normally "catch & report" (plus Unhandled/Thread/Task events).
    /// FirstChance -> Log EVERY thrown exception via AppDomain.FirstChanceException (very noisy; throttled).
    /// </summary>
    public enum ExceptionCaptureMode
    {
        Off,
        CaughtOnly,
        FirstChance
    }
}