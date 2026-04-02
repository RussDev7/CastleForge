/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static SetHomes.GamePatches;
using static ModLoader.LogSystem;

namespace SetHomes
{
    /// <summary>
    /// Lightweight config for SetHomes, backed by an INI on disk.
    ///
    /// Storage:
    ///   !Mods\SetHomes\SetHomes.Config.ini
    ///
    /// Current knobs:
    /// - Teleport sound enable/disable
    /// - Teleport sound cue name
    /// - Per-action toggles (Home vs Spawn)
    /// </summary>
    internal sealed class SHConfig
    {
        #region Active Instance

        /// <summary>
        /// The currently active config used by the mod at runtime.
        /// Swapped by <see cref="LoadApply"/>.
        /// </summary>
        internal static volatile SHConfig Active = new SHConfig();

        #endregion

        #region File Location

        /// <summary>
        /// Absolute path to the config file.
        /// Matches the extraction folder used by SetHomes.Start ("!Mods/<Namespace>" = "!Mods/SetHomes").
        /// </summary>
        public static string ConfigPath
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(SetHomes).Namespace, "SetHomes.Config.ini");

        #endregion

        #region Settings

        /// <summary>Master toggle for playing a sound when teleporting.</summary>
        public bool TeleportSoundEnabled = true;

        /// <summary>
        /// The sound cue name passed to SoundManager.Instance.PlayInstance(...).
        /// Default is "Teleport".
        /// </summary>
        public string TeleportSoundName = "Teleport";

        /// <summary>Whether to play sound on /home teleports.</summary>
        public bool PlaySoundOnHomeTeleport = true;

        /// <summary>Whether to play sound on /spawn teleports.</summary>
        public bool PlaySoundOnSpawnTeleport = true;

        /// <summaryHotkey to reload this config at runtime.</summary>
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        #endregion

        #region Load / Save

        /// <summary>
        /// Loads the config from disk; if missing, creates a default file.
        /// Any parsing failure falls back to defaults.
        /// </summary>
        public static SHConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# SetHomes - Configuration",
                    "# Controls teleport sound effects for SetHomes commands.",
                    "",
                    "; Available sounds (use exactly as listed; asset names are typically case-sensitive):",
                    "; Click, Error, Award, Popup, Teleport, Reload, BulletHitHuman, thunderBig,",
                    "; craft, dropitem, pickupitem, punch, punchMiss, arrow, AssaultReload, Shotgun,",
                    "; ShotGunReload, Song1, Song2, lostSouls, CreatureUnearth, HorrorStinger,",
                    "; Fireball, Iceball, DoorClose, DoorOpen, Song5, Song3, Song4, Song6, locator,",
                    "; Fuse, LaserGun1, LaserGun2, LaserGun3, LaserGun4, LaserGun5, Beep, SolidTone,",
                    "; RPGLaunch, Alien, SpaceTheme, GrenadeArm, RocketWhoosh, LightSaber,",
                    "; LightSaberSwing, GroundCrash, ZombieDig, ChainSawIdle, ChainSawSpinning,",
                    "; ChainSawCutting, Birds, FootStep, Theme, Pick, Place, Crickets, Drips,",
                    "; BulletHitDirt, GunShot1, GunShot2, GunShot3, GunShot4, BulletHitSpray,",
                    "; thunderLow, Sand, leaves, dirt, Skeleton, ZombieCry, ZombieGrowl, Hit, Fall,",
                    "; Douse, DragonScream, Explosion, WingFlap, DragonFall, Freeze, Felguard",
                    "",
                    "[Teleport]",
                    "; Master toggle: if false, no teleport sound is played.",
                    "PlaySound           = true",
                    "",
                    "; Sound cue name passed to SoundManager.Instance.PlayInstance(...)",
                    "SoundName           = Teleport",
                    "",
                    "; Per-command toggles.",
                    "PlayOnHomeTeleport  = true",
                    "PlayOnSpawnTeleport = true",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig        = Ctrl+Shift+R",
                    ""
                });
            }

            try
            {
                var ini = SimpleIni.Load(ConfigPath);

                return new SHConfig
                {
                    // [Teleport].
                    TeleportSoundEnabled     = ini.GetBool("Teleport", "PlaySound", true),
                    TeleportSoundName        = ini.GetString("Teleport", "SoundName", "Teleport"),
                    PlaySoundOnHomeTeleport  = ini.GetBool("Teleport", "PlayOnHomeTeleport", true),
                    PlaySoundOnSpawnTeleport = ini.GetBool("Teleport", "PlayOnSpawnTeleport", true),

                    // [Hotkeys].
                    ReloadConfigHotkey       = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
                };
            }
            catch
            {
                return new SHConfig();
            }
        }

        /// <summary>
        /// Loads from disk and replaces <see cref="Active"/>.
        /// Safe to call at startup and also from a reload command.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active = cfg;
                SHHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                Log($"Config loaded: TeleportSoundEnabled={Active.TeleportSoundEnabled}, SoundName='{Active.TeleportSoundName}'.");
            }
            catch (Exception ex)
            {
                Log($"Config load failed: {ex.GetType().Name}: {ex.Message}.");
            }
        }
        #endregion
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
        /// Reads an int from the INI and clamps it to the inclusive range [min..max].
        /// Returns <paramref name="def"/> if missing/invalid before clamping.
        /// </summary>
        public int GetClamp(string sec, string key, int def, int min, int max)
        {
            var v = GetInt(sec, key, def);
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        /// <summary>
        /// Reads a string value from [section] key=... and returns <paramref name="def"/> if missing.
        /// </summary>
        public string GetString(string section, string key, string def)
            => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

        /// <summary>
        /// Reads an int value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public int GetInt(string section, string key, int def)
            => int.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public double GetDouble(string section, string key, double def)
            => double.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
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