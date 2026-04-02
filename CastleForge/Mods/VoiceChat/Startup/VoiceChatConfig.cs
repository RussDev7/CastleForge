/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Input;
using System.Globalization;
using System.IO;
using System;

namespace VoiceChat
{
    /// <summary>
    /// Strongly-typed in-memory settings for the VoiceChat mod.
    ///
    /// Source format on disk: Simple INI-like "key=value" lines (no sections).
    /// Parsing rules:
    ///   • Blank lines or lines starting with '#' are ignored.
    ///   • Keys are case-sensitive in this loader; values are parsed per type.
    ///   • Numeric parsing uses invariant culture.
    ///
    /// Defaults below are used if a key is missing or fails to parse.
    /// </summary>
    internal sealed class VoiceChatConfig
    {
        // Core.
        public Keys   PttKey           = Keys.V;
        public int    FragmentSize     = 3500;      // Bytes.
        public bool   EnsureStart      = true;
        public bool   MuteSelf         = true;

        // Logging.
        public bool   LogReverbSkips   = false;

        // HUD.
        public bool   ShowSpeakerHud   = true;
        public string HudAnchor        = "TopLeft"; // TL/TR/BL/BR (string for simplicity).
        public double HudSeconds       = 1.20;
        public double HudFadeSeconds   = 0.30;
    }

    /// <summary>
    /// Loads/saves <see cref="VoiceChatConfig"/> from !Mods/VoiceChat/VoiceChat.ini,
    /// exposes a hot-reloaded singleton (<see cref="Current"/>), and watches the file for changes.
    /// </summary>
    internal static class VoiceChatConfigStore
    {
        /// <summary>Current in-memory config; updated on load or file change.</summary>
        public static VoiceChatConfig Current { get; private set; } = new VoiceChatConfig();

        #region Paths

        /// <summary>Root mods folder (next to the game EXE): .../!Mods.</summary>
        public static string ModsRoot =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods");

        /// <summary>VoiceChat mod folder: !Mods/VoiceChat.</summary>
        public static string VoiceChatFolder =>
            Path.Combine(ModsRoot, "VoiceChat");

        /// <summary>Config file path: !Mods/VoiceChat/VoiceChat.ini.</summary>
        public static string ConfigPath =>
            Path.Combine(VoiceChatFolder, "VoiceChat.Config.ini");

        private static FileSystemWatcher _watcher;

        #endregion

        #region Public API

        /// <summary>
        /// Ensure folder exists, create config with defaults if missing, then load it
        /// and start a file watcher for hot-reload on save.
        /// </summary>
        public static void LoadOrCreateDefaults()
        {
            Directory.CreateDirectory(VoiceChatFolder);
            if (!File.Exists(ConfigPath))
                SaveDefaults(); // One-time scaffold with helpful comments.

            Load();
            StartWatcher();
        }

        /// <summary>
        /// Load config from disk. Unknown keys are ignored; bad values fall back to defaults.
        /// This method is safe to call repeatedly and is triggered by the watcher on file changes.
        /// </summary>
        public static void Load()
        {
            var cfg = new VoiceChatConfig(); // Start from defaults.

            foreach (var line in SafeReadAllLines(ConfigPath))
            {
                var s = line.Trim();
                if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("#")) continue; // Comments / blanks.
                if (line.StartsWith("[") && line.EndsWith("]")) continue;              // Sections.

                int eq = s.IndexOf('=');
                if (eq <= 0) continue;

                string key = s.Substring(0, eq).Trim();
                string val = s.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "PttKey":
                        if (Enum.TryParse(val, true, out Keys k)) cfg.PttKey = k;
                        break;

                    case "FragmentSize":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frag))
                            cfg.FragmentSize = Math.Max(600, Math.Min(4000, frag)); // Clamp to safe window.
                        break;

                    case "EnsureStart":
                        cfg.EnsureStart = ParseBool(val, cfg.EnsureStart);
                        break;

                    case "MuteSelf":
                        cfg.MuteSelf = ParseBool(val, cfg.MuteSelf);
                        break;

                    case "LogReverbSkips":
                        cfg.LogReverbSkips = ParseBool(val, cfg.LogReverbSkips);
                        break;

                    case "ShowSpeakerHud":
                        cfg.ShowSpeakerHud = ParseBool(val, cfg.ShowSpeakerHud);
                        break;

                    case "HudAnchor":
                        cfg.HudAnchor = val;
                        break;

                    case "HudSeconds":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
                            cfg.HudSeconds = Math.Max(0.2, Math.Min(3.0, secs));
                        break;

                    case "HudFadeSeconds":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double fade))
                            cfg.HudFadeSeconds = Math.Max(0.05, Math.Min(1.0, fade));
                        break;
                }
            }

            Current = cfg;
            Log($"[Voice][Config] Loaded: PTT={Current.PttKey}, Frag={Current.FragmentSize}, EnsureStart={Current.EnsureStart}, MuteSelf={Current.MuteSelf}.");
        }

        /// <summary>
        /// Write a commented starter config to disk. Safe to call only if file doesn't exist.
        /// </summary>
        public static void SaveDefaults()
        {
            var lines = new[]
            {
                $"# VoiceChat - Configuration",
                $"# Lines starting with ';' or '#' are comments.",

                $"",
                $"# =================================================================================================",

                $"",
                $"[Hotkeys]",
                $"; Push-to-talk hotkey. Must be a Microsoft.Xna.Framework.Input.Keys name.",
                $"; Examples: V, LeftShift, CapsLock, T, NumPad0 (case-insensitive).",
                $"PttKey=V",

                $"",
                $"[Voice]",
                $"; Voice fragment size in bytes for sending audio.",
                $"; Bigger fragments = smoother audio (fewer packets), but risk packet drops on some peers.",
                $"; Recommended range: 1800-2400. Clamp is 600..4000.",
                $"FragmentSize=3500",

                $"",
                $"; If true, ensure a VoiceChat instance is started automatically once a session + local gamer exist.",
                $"; Set false if you prefer to start it only via your join hook.",
                $"EnsureStart=true",

                $"",
                $"[Startup]",
                $"; If true, do NOT play your own voice locally (mutes sidetone).",
                $"MuteSelf=true",

                $"",
                $"[Audio]",
                $"; Developer-only logging: When true, writes one log line the first time each",
                $"; missing 'Reverb*' XACT global is skipped by the patch.",
                $"; Leave false for players to keep logs quiet.",
                $"LogReverbSkips=false",

                $"",
                $"# =================================================================================================",

                $"",
                $"[HUD]",
                $"; Show the on-screen \"<Gamertag> is talking\" banner.",
                $"ShowSpeakerHud=true",

                $"",
                $"; Banner corner: TopLeft | TopRight | BottomLeft | BottomRight",
                $"; Shorthands TL/TR/BL/BR are also accepted.",
                $"HudAnchor=TopLeft",

                $"",
                $"; How long (in seconds) to keep the banner visible after the last received voice packet.",
                $"HudSeconds=1.20",

                $"",
                $"; Fade-out duration (in seconds) at the end of the banner's lifetime.",
                $"HudFadeSeconds=0.30",

                $"",
                $"# ================================================================================================="
            };
            File.WriteAllLines(ConfigPath, lines);
        }
        #endregion

        #region Helpers (Parsing, IO, Watcher)

        /// <summary>
        /// Parse a variety of boolean styles. Accepts: true/false, 1/0, yes/no, y/n, on/off.
        /// Falls back to the provided default if unrecognized.
        /// </summary>
        private static bool ParseBool(string s, bool fallback)
        {
            if (bool.TryParse(s, out var b)) return b;
            s = s.Trim().ToLowerInvariant();
            if (s == "1" || s == "yes" || s == "y" || s == "on")  return true;
            if (s == "0" || s == "no"  || s == "n" || s == "off") return false;
            return fallback;
        }

        /// <summary>
        /// Read all lines with a small retry loop to avoid transient "file in use"
        /// while editors are saving. Returns an empty array if it still fails.
        /// </summary>
        private static string[] SafeReadAllLines(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try { return File.ReadAllLines(path); }
                catch (IOException) { System.Threading.Thread.Sleep(15); }
            }
            return new string[0];
        }

        /// <summary>
        /// Start a file watcher that reloads the config when the file is modified.
        /// Idempotent: Does nothing if already running.
        /// </summary>
        private static void StartWatcher()
        {
            if (_watcher != null) return;
            _watcher = new FileSystemWatcher(VoiceChatFolder, Path.GetFileName(ConfigPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (s, e) =>
            {
                try { Load(); } catch { /* Swallow to keep watcher resilient. */ }
            };
        }

        /// <summary>
        /// Minimal logging wrapper; safe if the host logger isn't available.
        /// </summary>
        private static void Log(string s)
        {
            try { ModLoader.LogSystem.Log(s); } catch { /* Fallback ok. */ }
        }
        #endregion
    }
}