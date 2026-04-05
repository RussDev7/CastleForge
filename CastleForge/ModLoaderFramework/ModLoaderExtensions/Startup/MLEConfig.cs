/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static ModLoaderExt.GamePatches;
using static ModLoader.LogSystem;

namespace ModLoaderExt
{
    #region Shared Configs

    #region Ads

    /// <summary>
    /// Shared runtime knobs for menu-ad behavior.
    /// Written by MLEConfig.ApplyToStatics(), read by ad-related patches.
    /// </summary>
    internal static class AdsConfig
    {
        // [Ads].
        public static volatile bool HideMenuAd = true;
    }

    #endregion

    #region Network Flood Guard

    /// <summary>
    /// Runtime knobs used by FloodGuard patch (read-only from patch; written by config ApplyToStatics()).
    /// </summary>
    internal static class FloodGuard_Settings
    {
        // [FloodGuard].
        
        public static volatile bool FloodGuardEnabled         = true;
        public static volatile int  PerSenderMaxPacketsPerSec = 256;
        public static volatile int  BlackholeMs               = 30000;
        public static volatile bool DoNotExemptHost           = true;

        // [FloodGuardAllowlist].

        // Allowlisted MessageIDs are allowed even while blackholed, but still throttled.
        public static volatile int  AllowlistMaxPacketsPerSec = 256;

        // CLR type names (full names recommended) that are allowed through during blackhole.
        public static volatile string[] AllowMessageTypes     =
        {
            "DNA.CastleMinerZ.Net.BroadcastTextMessage",
        };

        // [PickupThrottle].

        // Limits how many pickups the LOCAL client requests per second when sweeping large piles.
        // Burst:    Instant "grab" budget before throttling kicks in.
        // RefillMs: Refill rate (1 token per X ms). Example: 5 => (1000ms / 5ms) ~200/sec sustained.
        public static volatile bool PickupThrottleEnabled     = true;
        public static volatile int PickupTouchBurst           = 25;
        public static volatile int PickupTouchRefillMs        = 5;
    }
    #endregion

    #region Gamertag Sanitizer

    /// <summary>
    /// Shared limits + master toggle for ALL sanitization behavior.
    /// </summary>
    /// <remarks>
    /// - Enabled:        Master kill-switch for name/chat sanitizers (not impersonation logic).
    /// - MaxNameLen:     Max length for cleaned/safe names (before "[id]" fallback).
    /// - MaxChatLineLen: Max total chat line length after prefixing.
    /// </remarks>
    internal static class GamertagSanitizerConfig
    {
        // [ChatProtections].

        // Master toggle for ALL sanitization (names + chat text).
        public static volatile bool GamertagSanitizerEnabled        = true;

        // When a joining or leaving players name is sanitized, also broadcast
        // a public join / left notice so everyone knows it was sanitized.
        public static volatile bool AnnounceSanitizedJoinLeaveNames = true;

        // Maximum number of chars allowed for a cleaned/safe name (before "[id]" fallback).
        public static volatile int  MaxNameLen                      = 24;

        // Maximum total chat line length (includes name + message after prefixing).
        public static volatile int  MaxChatLineLen                  = 220;
    }
    #endregion

    #region Impersonation Protection

    /// <summary>
    /// Controls spoof detection and how (or whether) to respond when spoofing is detected.
    /// </summary>
    /// <remarks>
    /// - Enabled:         Master toggle for impersonation protection only.
    /// - UseClearChat:    If true, sends 10 newlines instead of the quote warning.
    /// - ProtectEveryone: If true, protect all players; otherwise only local players.
    /// - HostOnlyRespondWhenProtectEveryone: Limits counter-broadcast spam.
    /// </remarks>
    internal static class ImpersonationConfig
    {
        // [ChatProtections].

        // 1) Master toggle.
        public static volatile bool NoImpersonationEnabled             = true;

        // 2) Response mode:
        //    false = Broadcast quote: "[SENDER (id: X) -> Impersonated -> TARGET (id: Y)]: msg".
        //    true  = Send 10 newlines to clear chat.
        public static volatile bool UseClearChat                       = false;

        // 3) Scope:
        //    false = Only protect locals (you / split-screen locals).
        //    true  = Protect everyone in session.
        public static volatile bool ProtectEveryone                    = true;

        // Optional safety: Only the host should emit counter-broadcasts.
        // (prevents every modded client from spamming responses in ProtectEveryone mode).
        public static volatile bool HostOnlyRespondWhenProtectEveryone = false;
    }

    public static class NewlineChatConfig
    {
        // [ChatProtections].

        // Ignore messages that are empty/newline chat spam.
        public static volatile bool IgnoreChatNewlines = false;
    }
    #endregion

    #endregion

    /// <summary>
    /// INI-backed config for ModLoaderExt (FloodGuard section).
    /// Matches the WEConfig/SimpleIni style: baseline file creation + simple reader + targeted Upsert edits.
    /// </summary>
    internal sealed class MLEConfig
    {
        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile MLEConfig Active;

        // [Ads].
        public bool HideMenuAd = true;

        // [FloodGuard].
        public bool FloodGuardEnabled         = true;
        public int  PerSenderMaxPacketsPerSec = 256;
        public int  BlackholeMs               = 30000;
        public bool DoNotExemptHost           = true;

        // [FloodGuardAllowlist].
        public int      AllowlistMaxPacketsPerSec = 256;
        public string[] AllowMessageTypes         = new[]
        {
            "DNA.CastleMinerZ.Net.BroadcastTextMessage",
        };

        // [PickupThrottle].
        public bool PickupThrottleEnabled = true;
        public int  PickupTouchBurst      = 25;
        public int  PickupTouchRefillMs   = 1;

        // [ChatProtections].
        public bool GamertagSanitizerEnabled        = true;
        public bool AnnounceSanitizedJoinLeaveNames = true;
        public int  MaxNameLen                      = 24;
        public int  MaxChatLineLen                  = 220;

        public bool NoImpersonationEnabled             = true;
        public bool UseClearChat                       = false;
        public bool ProtectEveryone                    = true;
        public bool HostOnlyRespondWhenProtectEveryone = false;

        public bool IgnoreChatNewlines = false;

        // [EntityLimits]
        public bool LimitEntities     = true;
        public int  MaxGlobalEntities = 500;

        // [Hotkeys].
        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "ModLoaderExt", "ModLoaderExt.Config.ini");

        public static MLEConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# ModLoaderExt - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[Ads]",
                    "; Hide the CMZ-Resurrection menu ad on the main menu.",
                    "HideMenuAd                = true",
                    "",
                    "[FloodGuard]",
                    "; Master toggle for inbound flood protection / blackhole logic.",
                    "Enabled                   = true",
                    "; Per-sender inbound packet cap (1 second window).",
                    "; Don't set this too low unless you're OK dropping legit bursts.",
                    "PerSenderMaxPacketsPerSec = 256",
                    "; How long (ms) to blackhole a sender who exceeds the cap.",
                    "BlackholeMs               = 30000",
                    "; IMPORTANT: If true, host is NOT exempt (safer if attacker can be host).",
                    "DoNotExemptHost           = true",
                    "",
                    "[FloodGuardAllowlist]",
                    "; Allowlisted messages are still allowed during blackhole, but throttled.",
                    "; This is a per-sender (1 second) cap for allowlisted traffic.",
                    "AllowlistMaxPacketsPerSec = 256",
                    "; Comma-separated CLR type names (full names recommended).",
                    "; Example: DNA.CastleMinerZ.Net.BroadcastTextMessage, DNA.CastleMinerZ.Net.GunshotMessage",
                    "AllowMessageTypes         = DNA.CastleMinerZ.Net.BroadcastTextMessage",
                    "",
                    "[PickupThrottle]",
                    "; Master toggle for local pickup request throttling.",
                    "Enabled             = true",
                    "; Client-side throttle for pickup requests (RequestPickupMessage).",
                    "; Burst:    Instant requests allowed before throttling.",
                    "; RefillMs: Refill rate (1 token per X ms). Example: 5 => (1000ms / 5ms) ~200/sec sustained.",
                    "PickupTouchBurst    = 25",
                    "PickupTouchRefillMs = 5",
                    "",
                    "[ChatProtections]",
                    "; Master toggle for sanitizing gamertags + incoming chat text.",
                    "GamertagSanitizerEnabled        = true",
                    "; Broadcast a public join/left notice when a player's name was sanitized (blank/newline/control spam).",
                    "AnnounceSanitizedJoinLeaveNames = true",
                    "; Max cleaned/safe name length (clamped 4..64).",
                    "GamertagSanitizerMaxNameLen     = 24",
                    "; Max total chat line length after prefixing (clamped 40..512).",
                    "GamertagSanitizerMaxChatLineLen = 220",
                    "",
                    "; Detect 'Name: msg' spoofing + choose response behavior.",
                    "NoImpersonationEnabled                          = true",
                    "; true -> send 10 newlines instead of warning quote.",
                    "ImpersonationUseClearChat                       = false",
                    "; true -> protect everyone; false -> only local players.",
                    "ImpersonationProtectEveryone                    = true",
                    "; Optional spam guard when ProtectEveryone=true.",
                    "ImpersonationHostOnlyRespondWhenProtectEveryone = false",
                    "",
                    "; Ignore incoming messages that are empty/newline spam.",
                    "IgnoreChatNewlines = false",
                    "",
                    "[EntityLimits]",
                    "; Global entity limiter toggle.",
                    "LimitEntities     = true",
                    "; Maximum visible/global entities when LimitEntities=true.",
                    "MaxGlobalEntities = 500",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig       = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);

            var cfg = new MLEConfig
            {
                // [Ads].
                HideMenuAd = ini.GetBool("Ads", "HideMenuAd", true),

                // [FloodGuard].
                FloodGuardEnabled         = ini.GetBool("FloodGuard", "Enabled", true),
                PerSenderMaxPacketsPerSec = Clamp(ini.GetInt("FloodGuard", "PerSenderMaxPacketsPerSec", 256), 1, 50000),
                BlackholeMs               = Clamp(ini.GetInt("FloodGuard", "BlackholeMs", 30000), 0, 600000),
                DoNotExemptHost           = ini.GetBool("FloodGuard", "DoNotExemptHost", true),

                // [FloodGuardAllowlist].
                AllowlistMaxPacketsPerSec = Clamp(ini.GetInt("FloodGuardAllowlist", "AllowlistMaxPacketsPerSec", 256), 0, 50000),
                AllowMessageTypes         = SplitCsv(ini.GetString("FloodGuardAllowlist", "AllowMessageTypes",
                                                                   "DNA.CastleMinerZ.Net.BroadcastTextMessage")),

                // [PickupThrottle].
                PickupThrottleEnabled = ini.GetBool("PickupThrottle", "Enabled", true),
                PickupTouchBurst      = Clamp(ini.GetInt("PickupThrottle", "PickupTouchBurst", 25), 1, 5000),
                PickupTouchRefillMs   = Clamp(ini.GetInt("PickupThrottle", "PickupTouchRefillMs", 5), 1, 60000),

                // [ChatProtections].
                GamertagSanitizerEnabled        = ini.GetBool("ChatProtections", "GamertagSanitizerEnabled", true),
                AnnounceSanitizedJoinLeaveNames = ini.GetBool("ChatProtections", "AnnounceSanitizedJoinLeaveNames", true),
                MaxNameLen                      = Clamp(ini.GetInt("ChatProtections", "GamertagSanitizerMaxNameLen", 24), 4, 64),
                MaxChatLineLen                  = Clamp(ini.GetInt("ChatProtections", "GamertagSanitizerMaxChatLineLen", 220), 40, 512),

                NoImpersonationEnabled             = ini.GetBool("ChatProtections", "NoImpersonationEnabled", true),
                UseClearChat                       = ini.GetBool("ChatProtections", "ImpersonationUseClearChat", false),
                ProtectEveryone                    = ini.GetBool("ChatProtections", "ProtectEveryone", true),
                HostOnlyRespondWhenProtectEveryone = ini.GetBool("ChatProtections", "HostOnlyRespondWhenProtectEveryone", false),

                IgnoreChatNewlines = ini.GetBool("ChatProtections", "IgnoreChatNewlines", false),

                // [EntityLimits].
                LimitEntities     = ini.GetBool("EntityLimits", "LimitEntities", true),
                MaxGlobalEntities = Clamp(ini.GetInt("EntityLimits", "MaxGlobalEntities", 500), 0, 50000),

                // [Hotkeys].
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };

            // Ensure non-null arrays for callers.
            if (cfg.AllowMessageTypes == null) cfg.AllowMessageTypes = Array.Empty<string>();

            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active = cfg;
                cfg.ApplyToStatics();
                MLEHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);

                // Log($"[MLEConfig] Applied from {ConfigPath}.");
            }
            catch (Exception ex)
            {
                Log($"[MLEConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            // [Ads].
            AdsConfig.HideMenuAd = HideMenuAd;

            // [FloodGuard].
            FloodGuard_Settings.FloodGuardEnabled         = FloodGuardEnabled;
            FloodGuard_Settings.PerSenderMaxPacketsPerSec = PerSenderMaxPacketsPerSec;
            FloodGuard_Settings.BlackholeMs               = BlackholeMs;
            FloodGuard_Settings.DoNotExemptHost           = DoNotExemptHost;

            // [FloodGuardAllowlist].
            FloodGuard_Settings.AllowlistMaxPacketsPerSec = AllowlistMaxPacketsPerSec;
            FloodGuard_Settings.AllowMessageTypes         = AllowMessageTypes ?? Array.Empty<string>();

            // [PickupThrottle].
            FloodGuard_Settings.PickupThrottleEnabled = PickupThrottleEnabled;
            FloodGuard_Settings.PickupTouchBurst      = PickupTouchBurst;
            FloodGuard_Settings.PickupTouchRefillMs   = PickupTouchRefillMs;

            // IMPORTANT: allowlist type list changed -> message-id cache must be rebuilt.
            try { GamePatches.Patch_LocalNetworkGamer_FloodGuard.InvalidateAllowlistCache(); } catch { }

            // [ChatProtections].
            GamertagSanitizerConfig.GamertagSanitizerEnabled        = GamertagSanitizerEnabled;
            GamertagSanitizerConfig.AnnounceSanitizedJoinLeaveNames = AnnounceSanitizedJoinLeaveNames;
            GamertagSanitizerConfig.MaxNameLen                      = MaxNameLen;
            GamertagSanitizerConfig.MaxChatLineLen                  = MaxChatLineLen;

            ImpersonationConfig.NoImpersonationEnabled             = NoImpersonationEnabled;
            ImpersonationConfig.UseClearChat                       = UseClearChat;
            ImpersonationConfig.ProtectEveryone                    = ProtectEveryone;
            ImpersonationConfig.HostOnlyRespondWhenProtectEveryone = HostOnlyRespondWhenProtectEveryone;

            NewlineChatConfig.IgnoreChatNewlines = IgnoreChatNewlines;

            // [EntityLimits].
            EntityLimiterSystem.LimitEntities     = LimitEntities;
            EntityLimiterSystem.MaxGlobalEntities = MaxGlobalEntities;
        }

        /// <summary>
        /// Updates FloodGuard numeric/bool keys on disk (preserves comments + unrelated lines).
        /// </summary>
        public static void UpdateFloodGuardConfig(bool enabled, int perSenderMaxPacketsPerSec, int blackholeMs, bool doNotExemptHost, int allowlistMaxPacketsPerSec)
        {
            LoadOrCreate();

            var lines = new List<string>(File.ReadAllLines(ConfigPath));

            /// [FloodGuard]
            UpsertIniKey(lines, "FloodGuard",          "Enabled",                   enabled ? "true" : "false", 26);
            UpsertIniKey(lines, "FloodGuard",          "PerSenderMaxPacketsPerSec", perSenderMaxPacketsPerSec.ToString(CultureInfo.InvariantCulture), 26);
            UpsertIniKey(lines, "FloodGuard",          "BlackholeMs",               blackholeMs.ToString(CultureInfo.InvariantCulture), 26);
            UpsertIniKey(lines, "FloodGuard",          "DoNotExemptHost",           doNotExemptHost ? "true" : "false", 26);

            /// [FloodGuardAllowlist]
            UpsertIniKey(lines, "FloodGuardAllowlist", "AllowlistMaxPacketsPerSec", allowlistMaxPacketsPerSec.ToString(CultureInfo.InvariantCulture), 26);

            File.WriteAllLines(ConfigPath, lines.ToArray());
        }

        /// <summary>
        /// Updates PickupThrottle numeric/bool keys on disk (preserves comments + unrelated lines).
        /// </summary>
        public static void UpdatePickupThrottleConfig(bool enabled, int burst, int refillMs)
        {
            LoadOrCreate();

            var lines = new List<string>(File.ReadAllLines(ConfigPath));

            // [PickupThrottle].
            UpsertIniKey(lines, "PickupThrottle", "Enabled",             enabled ? "true" : "false", 26);
            UpsertIniKey(lines, "PickupThrottle", "PickupTouchBurst",    burst.ToString(CultureInfo.InvariantCulture), 26);
            UpsertIniKey(lines, "PickupThrottle", "PickupTouchRefillMs", refillMs.ToString(CultureInfo.InvariantCulture), 26);

            File.WriteAllLines(ConfigPath, lines.ToArray());
        }

        /// <summary>
        /// Updates AllowMessageTypes on disk as a single comma-separated line.
        /// </summary>
        public static void UpdateAllowMessageTypesConfig(string[] allowMessageTypes)
        {
            LoadOrCreate();

            string joined = JoinCsv(allowMessageTypes);

            var lines = new List<string>(File.ReadAllLines(ConfigPath));
            UpsertIniKey(lines, "FloodGuardAllowlist", "AllowMessageTypes", joined, 26);
            File.WriteAllLines(ConfigPath, lines.ToArray());
        }

        // Helpers.

        private static string[] SplitCsv(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            // Accept commas or semicolons.
            var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return Array.Empty<string>();

            var list = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var s = (parts[i] ?? "").Trim();
                if (s.Length == 0) continue;
                list.Add(s);
            }
            return list.ToArray();
        }

        private static string JoinCsv(string[] items)
        {
            if (items == null || items.Length == 0) return "";

            var list = new List<string>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var s = (items[i] ?? "").Trim();
                if (s.Length == 0) continue;
                list.Add(s);
            }

            return string.Join(", ", list.ToArray());
        }

        /// <summary>
        /// Inserts or updates a key/value pair inside an INI section (case-insensitive).
        /// Summary: This edits only the requested key and preserves unrelated lines and comments.
        /// </summary>
        private static void UpsertIniKey(List<string> lines, string sectionName, string key, string value, int padWidth)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("Section is required.", nameof(sectionName));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

            if (!TryFindSectionRange(lines, sectionName, out int startIndex, out int endIndex))
            {
                // Create the section at the end if it does not exist.
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.Add("");

                lines.Add("[" + sectionName + "]");
                startIndex = lines.Count - 1;
                endIndex = lines.Count;
            }

            // Try to replace an existing key line inside the section.
            for (int i = startIndex + 1; i < endIndex; i++)
            {
                string raw = lines[i] ?? "";
                string trimmed = raw.TrimStart();

                if (trimmed.Length == 0) continue;
                if (trimmed[0] == ';' || trimmed[0] == '#') continue;
                if (trimmed[0] == '[') break;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                string k = trimmed.Substring(0, eq).Trim();
                if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

                string leading = raw.Substring(0, raw.Length - trimmed.Length);
                lines[i] = leading + key.PadRight(padWidth) + "= " + (value ?? "");
                return;
            }

            // Not found -> insert before the next section header (endIndex).
            lines.Insert(endIndex, key.PadRight(padWidth) + "= " + (value ?? ""));
        }

        /// <summary>
        /// Finds the bounds of a section in an INI file.
        /// Summary: Returns the index of the section header and the index of the next header (or EOF).
        /// </summary>
        private static bool TryFindSectionRange(List<string> lines, string sectionName, out int startIndex, out int endIndex)
        {
            startIndex = -1;
            endIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Length < 3) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (name.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                    {
                        startIndex = i;
                        endIndex = lines.Count;

                        // Find next section header.
                        for (int j = i + 1; j < lines.Count; j++)
                        {
                            string n = (lines[j] ?? "").Trim();
                            if (n.StartsWith("[") && n.EndsWith("]"))
                            {
                                endIndex = j;
                                break;
                            }
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Clamps an integer to an inclusive range.
        /// Summary: Ensures v stays within lo..hi.
        /// </summary>
        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
    }

    /// <summary>
    /// Tiny, case-insensitive INI reader.
    /// Supports [Section], key=value, ';' or '#' comments. No escaping, no multi-line.
    /// </summary>
    internal sealed class SimpleIni
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads an INI file from disk into a simple nested dictionary:
        ///   section -> (key -> value).
        /// Unknown / malformed lines are ignored.
        /// </summary>
        public static SimpleIni Load(string path)
        {
            var ini = new SimpleIni();
            string section = "";

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                // Section header: [SectionName].
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!ini._data.ContainsKey(section))
                        ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                // Key/value pair: key = value.
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (!ini._data.TryGetValue(section, out var dict))
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ini._data[section] = dict;
                }
                dict[key] = val;
            }

            return ini;
        }

        /// <summary>
        /// Reads a string value from [section] key=... and returns def if missing.
        /// </summary>
        public string GetString(string section, string key, string def)
            => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

        /// <summary>
        /// Reads an int value from [section] key=... using invariant culture; returns def on failure.
        /// </summary>
        public int GetInt(string section, string key, int def)
            => int.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a bool value from [section] key=...; accepts true/false or 1/0; returns def on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }
    }
}