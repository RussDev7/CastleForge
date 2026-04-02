/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static Restore360Water.GamePatches;

/// <summary>
/// NOTES / INTENT
/// - This file owns Restore360Water's config-backed runtime state.
/// - It exposes:
///   1) R360W_Settings: live static runtime values consumed by the mod.
///   2) R360WBiomeBandConfig: per-biome water-band configuration.
///   3) R360WConfig: INI-backed persisted configuration with load/save/apply helpers.
///   4) SimpleIni: lightweight INI parser used by the config loader.
/// -
/// - Design goals:
///   - Keep config simple and human-editable.
///   - Keep runtime values centralized and easy to inspect/debug.
///   - Allow biome band overrides, fallback water, custom sounds, and optional
///     WorldGenPlus integration without forcing external dependencies.
/// </summary>
namespace Restore360Water
{
    #region Runtime State

    /// <summary>
    /// Live runtime values consumed by Restore360Water.
    ///
    /// Summary:
    /// - These values are populated from <see cref="R360WConfig.ApplyToStatics"/>.
    /// - They represent the currently active runtime settings used by patches,
    ///   biome resolution, sound playback, and scene logic.
    /// </summary>
    internal static class R360W_Settings
    {
        #region Core Water Runtime

        public static volatile bool Enabled                    = true;
        public static bool          AttachWaterPlane           = true;
        public static bool          EnableReflection           = true;
        public static bool          UseVanillaUnderwaterEngine = true;
        public static bool          UseBiomeOverrides          = true;
        public static bool          MirrorRepeat               = true;
        public static float         RingPeriod                 = 4400f;
        public static bool          GlobalFallbackEnabled      = false;
        public static float         GlobalFallbackMinY         = -64f;
        public static float         GlobalFallbackMaxY         = -31.5f;
        public static bool          DoLogging                  = false;

        #endregion

        #region Optional WorldGen Integration

        /// <summary>
        /// Enables optional WorldGenPlus-aware biome resolution when available.
        /// </summary>
        public static bool EnableWorldGenPlusIntegration = true;

        /// <summary>
        /// Controls how WorldGenPlus surface lookup should resolve biome identity.
        /// Expected values: Auto, Rings, Single, RandomRegions.
        /// </summary>
        public static string WorldGenPlusSurfaceMode = "Auto";

        #endregion

        #region Custom Water Audio

        public static bool  EnableCustomSounds = true;
        public static float SoundVolume        = 0.75f;

        public static string WaterEnterFile = "water_enter.wav";
        public static string WaterExitFile  = "water_exit.wav";
        public static string WaterWadeFile  = "water_wade.wav";
        public static string WaterSwimFile  = "water_swim.wav";

        public static bool EnableWaterEnter = true;
        public static bool EnableWaterExit  = true;
        public static bool EnableWaterWade  = true;
        public static bool EnableWaterSwim  = true;

        #endregion

        #region Live Resolved Biome State

        /// <summary>
        /// Current biome state resolved at runtime by biome/water-band logic.
        /// These are informational/live values rather than persisted config defaults.
        /// </summary>
        public static string CurrentBiomeName                 = "None";
        public static bool   CurrentBiomeEnabled              = false;
        public static float  CurrentWaterMinY                 = -64f;
        public static float  CurrentWaterMaxY                 = -31.5f;
        public static bool   CurrentUseNativeBiomeWaterValues = false;

        #endregion
    }
    #endregion

    #region Config Models

    /// <summary>
    /// Per-biome water band definition.
    ///
    /// Summary:
    /// - Enabled determines whether the band is eligible for runtime use.
    /// - MinY / MaxY define the active vertical water range.
    /// - Normalize clamps values and ensures MinY is never above MaxY.
    /// </summary>
    internal sealed class R360WBiomeBandConfig
    {
        public bool  Enabled              = false;
        public float MinY                 = -64f;
        public float MaxY                 = -31.5f;
        public bool  UseNativeWaterValues = false;

        public R360WBiomeBandConfig() { }

        public R360WBiomeBandConfig(bool enabled, float minY, float maxY, bool useNativeWaterValues)
        {
            Enabled              = enabled;
            MinY                 = minY;
            MaxY                 = maxY;
            UseNativeWaterValues = useNativeWaterValues;
        }

        /// <summary>
        /// Clamps and normalizes band limits into a safe, ordered range.
        /// </summary>
        public void Normalize()
        {
            MinY = Clamp(MinY, -256f, 256f);
            MaxY = Clamp(MaxY, -256f, 256f);
            if (MinY > MaxY)
            {
                (MaxY, MinY) = (MinY, MaxY);
            }
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }

    /// <summary>
    /// INI-backed config container for Restore360Water.
    ///
    /// Summary:
    /// - Owns all persisted settings.
    /// - Can load, create, normalize, save, and apply values to runtime statics.
    /// - Also stores per-biome water bands and optional audio/worldgen settings.
    /// </summary>
    internal sealed class R360WConfig
    {
        #region Version / Active Instance

        /// <summary>
        /// Current config version.
        /// Bumped so older files will be rewritten with the new WorldGen section.
        /// </summary>
        private const int CurrentConfigVersion = 5;

        internal static volatile R360WConfig Active;

        #endregion

        #region Persisted Core Water Settings

        public int   ConfigVersion              = CurrentConfigVersion;
        public bool  Enabled                    = true;
        public bool  AttachWaterPlane           = true;
        public bool  EnableReflection           = true;
        public bool  UseVanillaUnderwaterEngine = true;
        public bool  UseBiomeOverrides          = true;
        public bool  MirrorRepeat               = true;
        public float RingPeriod                 = 4400f;
        public bool  GlobalFallbackEnabled      = false;
        public float GlobalFallbackMinY         = -64f;
        public float GlobalFallbackMaxY         = -31.5f;
        public bool  DoLogging                  = false;

        #endregion

        #region Persisted WorldGen Integration Settings

        public bool   EnableWorldGenPlusIntegration = true;
        public string WorldGenPlusSurfaceMode       = "Auto";

        #endregion

        #region Persisted Audio Settings

        public bool  EnableCustomSounds = true;
        public float SoundVolume        = 0.75f;

        public string WaterEnterFile = "water_enter.wav";
        public string WaterExitFile  = "water_exit.wav";
        public string WaterWadeFile  = "water_wade.wav";
        public string WaterSwimFile  = "water_swim.wav";

        public bool EnableWaterEnter = true;
        public bool EnableWaterExit  = true;
        public bool EnableWaterWade  = true;
        public bool EnableWaterSwim  = true;

        #endregion

        #region Persisted Biome Bands

        public R360WBiomeBandConfig Classic  = new R360WBiomeBandConfig(false, -64f,   -31.5f, false);
        public R360WBiomeBandConfig Lagoon   = new R360WBiomeBandConfig(true,  -3.5f,  9.5f,   false);
        public R360WBiomeBandConfig Desert   = new R360WBiomeBandConfig(false, -43.5f, -31.5f, false);
        public R360WBiomeBandConfig Mountain = new R360WBiomeBandConfig(false, -64f,   -31.5f, false);
        public R360WBiomeBandConfig Arctic   = new R360WBiomeBandConfig(false, -43.5f, -31.5f, false);
        public R360WBiomeBandConfig Decent   = new R360WBiomeBandConfig(false, -64f,   -31.5f, false);
        public R360WBiomeBandConfig Coastal  = new R360WBiomeBandConfig(true,  -64f,   -31.5f, false);
        public R360WBiomeBandConfig Ocean    = new R360WBiomeBandConfig(true,  -64f,   -31.5f, false);
        public R360WBiomeBandConfig Hell     = new R360WBiomeBandConfig(false, -64f,   -64f,   false);

        #endregion

        #region Hotkeys

        /// <summary>
        /// Hotkey used to reload Restore360Water.Config.ini at runtime.
        /// Examples:
        /// - Ctrl+Shift+R
        /// - F9
        /// - Alt+F3
        /// - Win+R
        /// Empty / invalid bindings disable the hotkey.
        /// </summary>
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        #endregion

        #region Paths / Entry Points

        /// <summary>
        /// Full path to the INI file used by Restore360Water.
        /// </summary>
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "Restore360Water", "Restore360Water.Config.ini");

        /// <summary>
        /// Loads the config if present, otherwise creates a new file with defaults.
        /// </summary>
        public static R360WConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                R360WConfig created = new R360WConfig();
                Save(created);
                return created;
            }

            SimpleIni ini = SimpleIni.Load(ConfigPath);
            R360WConfig cfg = new R360WConfig
            {
                ConfigVersion              = ini.GetInt("Restore360Water", "ConfigVersion", CurrentConfigVersion),
                Enabled                    = ini.GetBool("Restore360Water", "Enabled", true),
                AttachWaterPlane           = ini.GetBool("Restore360Water", "AttachWaterPlane", true),
                EnableReflection           = ini.GetBool("Restore360Water", "EnableReflection", true),
                UseVanillaUnderwaterEngine = ini.GetBool("Restore360Water", "UseVanillaUnderwaterEngine", true),
                UseBiomeOverrides          = ini.GetBool("Restore360Water", "UseBiomeOverrides", true),
                MirrorRepeat               = ini.GetBool("Restore360Water", "MirrorRepeat", true),
                RingPeriod                 = Clamp(ini.GetFloat("Restore360Water", "RingPeriod", 4400f), 100f, 20000f),
                GlobalFallbackEnabled      = ini.GetBool("Restore360Water", "GlobalFallbackEnabled", false),
                GlobalFallbackMinY         = Clamp(ini.GetFloat("Restore360Water", "GlobalFallbackMinY", -64f), -256f, 256f),
                GlobalFallbackMaxY         = Clamp(ini.GetFloat("Restore360Water", "GlobalFallbackMaxY", -31.5f), -256f, 256f),
                DoLogging                  = ini.GetBool("Restore360WaterLogging", "DoLogging", false),

                EnableWorldGenPlusIntegration = ini.GetBool("Restore360WaterWorldGen", "EnableWorldGenPlusIntegration", true),
                WorldGenPlusSurfaceMode       = ini.GetString("Restore360WaterWorldGen", "WorldGenPlusSurfaceMode", "Auto"),

                EnableCustomSounds = ini.GetBool("Restore360WaterAudio", "EnableCustomSounds", true),
                SoundVolume        = Clamp(ini.GetFloat("Restore360WaterAudio", "SoundVolume", 0.75f), 0f, 1f),
                WaterEnterFile     = ini.GetString("Restore360WaterAudio", "WaterEnterFile", "water_enter.wav"),
                WaterExitFile      = ini.GetString("Restore360WaterAudio", "WaterExitFile", "water_exit.wav"),
                WaterWadeFile      = ini.GetString("Restore360WaterAudio", "WaterWadeFile", "water_wade.wav"),
                WaterSwimFile      = ini.GetString("Restore360WaterAudio", "WaterSwimFile", "water_swim.wav"),
                EnableWaterEnter   = ini.GetBool("Restore360WaterAudio", "EnableWaterEnter", true),
                EnableWaterExit    = ini.GetBool("Restore360WaterAudio", "EnableWaterExit", true),
                EnableWaterWade    = ini.GetBool("Restore360WaterAudio", "EnableWaterWade", true),
                EnableWaterSwim    = ini.GetBool("Restore360WaterAudio", "EnableWaterSwim", true),
            };

            cfg.Classic  = LoadBand(ini, "Biome.Classic",  cfg.Classic);
            cfg.Lagoon   = LoadBand(ini, "Biome.Lagoon",   cfg.Lagoon);
            cfg.Desert   = LoadBand(ini, "Biome.Desert",   cfg.Desert);
            cfg.Mountain = LoadBand(ini, "Biome.Mountain", cfg.Mountain);
            cfg.Arctic   = LoadBand(ini, "Biome.Arctic",   cfg.Arctic);
            cfg.Decent   = LoadBand(ini, "Biome.Decent",   cfg.Decent);
            cfg.Coastal  = LoadBand(ini, "Biome.Coastal",  cfg.Coastal);
            cfg.Ocean    = LoadBand(ini, "Biome.Ocean",    cfg.Ocean);
            cfg.Hell     = LoadBand(ini, "Biome.Hell",     cfg.Hell);

            cfg.ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R");

            Normalize(cfg);

            if (cfg.ConfigVersion < CurrentConfigVersion)
            {
                cfg.ConfigVersion = CurrentConfigVersion;
                Save(cfg);
            }

            return cfg;
        }

        /// <summary>
        /// Loads and normalizes a per-biome band from a named INI section.
        /// </summary>
        private static R360WBiomeBandConfig LoadBand(SimpleIni ini, string section, R360WBiomeBandConfig fallback)
        {
            R360WBiomeBandConfig band = new R360WBiomeBandConfig(
                 ini.GetBool(section, "Enabled", fallback.Enabled),
                 ini.GetFloat(section, "MinY", fallback.MinY),
                 ini.GetFloat(section, "MaxY", fallback.MaxY),
                 ini.GetBool(section, "UseNativeWaterValues", fallback.UseNativeWaterValues));
            band.Normalize();
            return band;
        }

        /// <summary>
        /// Normalizes persisted config state before save/apply.
        /// </summary>
        private static void Normalize(R360WConfig cfg)
        {
            if (cfg == null) return;

            cfg.GlobalFallbackMinY = Clamp(cfg.GlobalFallbackMinY, -256f, 256f);
            cfg.GlobalFallbackMaxY = Clamp(cfg.GlobalFallbackMaxY, -256f, 256f);
            if (cfg.GlobalFallbackMinY > cfg.GlobalFallbackMaxY)
            {
                (cfg.GlobalFallbackMaxY, cfg.GlobalFallbackMinY) = (cfg.GlobalFallbackMinY, cfg.GlobalFallbackMaxY);
            }

            cfg.Classic?.Normalize();
            cfg.Lagoon?.Normalize();
            cfg.Desert?.Normalize();
            cfg.Mountain?.Normalize();
            cfg.Arctic?.Normalize();
            cfg.Decent?.Normalize();
            cfg.Coastal?.Normalize();
            cfg.Ocean?.Normalize();
            cfg.Hell?.Normalize();
        }

        /// <summary>
        /// Saves the config to disk in INI-style format.
        /// </summary>
        private static void Save(R360WConfig cfg)
        {
            if (cfg == null) cfg = new R360WConfig();
            Normalize(cfg);

            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllLines(ConfigPath, new[]
            {
                "# Restore360Water - Configuration",
                "# Restores dormant 360-style surface water using biome-aware vertical water bands.",
                "# Water is resolved from the classic CMZ ring-biome distance math unless you disable UseBiomeOverrides.",
                "# Optional WorldGenPlus integration can override biome identity lookup when supported.",
                "# WorldGenPlusSurfaceMode: Auto, Rings, Single, RandomRegions",
                "",
                "[Restore360Water]",
                $"ConfigVersion              = {CurrentConfigVersion.ToString(CultureInfo.InvariantCulture)}",
                $"Enabled                    = {FormatBool(cfg.Enabled)}",
                $"AttachWaterPlane           = {FormatBool(cfg.AttachWaterPlane)}",
                $"EnableReflection           = {FormatBool(cfg.EnableReflection)}",
                $"UseVanillaUnderwaterEngine = {FormatBool(cfg.UseVanillaUnderwaterEngine)}",
                $"UseBiomeOverrides          = {FormatBool(cfg.UseBiomeOverrides)}",
                $"MirrorRepeat               = {FormatBool(cfg.MirrorRepeat)}",
                $"RingPeriod                 = {cfg.RingPeriod.ToString(CultureInfo.InvariantCulture)}",
                $"GlobalFallbackEnabled      = {FormatBool(cfg.GlobalFallbackEnabled)}",
                $"GlobalFallbackMinY         = {cfg.GlobalFallbackMinY.ToString(CultureInfo.InvariantCulture)}",
                $"GlobalFallbackMaxY         = {cfg.GlobalFallbackMaxY.ToString(CultureInfo.InvariantCulture)}",
                "",
                "# Example lagoon band:",
                "# [Biome.Lagoon]",
                "# Enabled = true",
                "# MinY    = -3.5",
                "# MaxY    = 9.5",
                "# UseNativeWaterValues = false",
                "",
                WriteBandHeader("Biome.Classic", cfg.Classic),
                WriteBandHeader("Biome.Lagoon", cfg.Lagoon),
                WriteBandHeader("Biome.Desert", cfg.Desert),
                WriteBandHeader("Biome.Mountain", cfg.Mountain),
                WriteBandHeader("Biome.Arctic", cfg.Arctic),
                WriteBandHeader("Biome.Decent", cfg.Decent),
                WriteBandHeader("Biome.Coastal", cfg.Coastal),
                WriteBandHeader("Biome.Ocean", cfg.Ocean),
                WriteBandHeader("Biome.Hell", cfg.Hell),
                "",
                "[Restore360WaterWorldGen]",
                $"EnableWorldGenPlusIntegration = {FormatBool(cfg.EnableWorldGenPlusIntegration)}",
                $"WorldGenPlusSurfaceMode       = {cfg.WorldGenPlusSurfaceMode}",
                "",
                "[Restore360WaterAudio]",
                $"EnableCustomSounds = {FormatBool(cfg.EnableCustomSounds)}",
                $"SoundVolume        = {cfg.SoundVolume.ToString(CultureInfo.InvariantCulture)}",
                $"WaterEnterFile     = {cfg.WaterEnterFile}",
                $"WaterExitFile      = {cfg.WaterExitFile}",
                $"WaterWadeFile      = {cfg.WaterWadeFile}",
                $"WaterSwimFile      = {cfg.WaterSwimFile}",
                $"EnableWaterEnter   = {FormatBool(cfg.EnableWaterEnter)}",
                $"EnableWaterExit    = {FormatBool(cfg.EnableWaterExit)}",
                $"EnableWaterWade    = {FormatBool(cfg.EnableWaterWade)}",
                $"EnableWaterSwim    = {FormatBool(cfg.EnableWaterSwim)}",
                "",
                "[Restore360WaterLogging]",
                $"DoLogging = {FormatBool(cfg.DoLogging)}",
                "",
                "[Hotkeys]",
                "; Reload this config while in-game:",
                "; Examples: Ctrl+Shift+R, F9, Alt+F3, Win+R",
                "ReloadConfig = Ctrl+Shift+R",
            });
        }
        #endregion

        #region Formatting Helpers

        /// <summary>
        /// Writes a biome band section in the same format used by the config file.
        /// </summary>
        private static string WriteBandHeader(string section, R360WBiomeBandConfig band)
        {
            return string.Join(Environment.NewLine, new[]
            {
                $"[{section}]",
                $"Enabled = {FormatBool(band != null && band.Enabled)}",
                $"MinY    = {(band?.MinY ?? -64f).ToString(CultureInfo.InvariantCulture)}",
                $"MaxY    = {(band?.MaxY ?? -31.5f).ToString(CultureInfo.InvariantCulture)}",
                $"UseNativeWaterValues = {FormatBool(band != null && band.UseNativeWaterValues)}",
                ""
            });
        }

        /// <summary>
        /// Formats a bool using lower-case INI-friendly values.
        /// </summary>
        private static string FormatBool(bool v) => v ? "true" : "false";

        /// <summary>
        /// Simple float clamp helper used by config parsing/normalization.
        /// </summary>
        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
        #endregion

        #region Apply To Runtime

        /// <summary>
        /// Copies persisted config values into live runtime statics and registers biome bands.
        /// </summary>
        public void ApplyToStatics()
        {
            Active = this;

            R360W_Settings.Enabled                    = Enabled;
            R360W_Settings.AttachWaterPlane           = AttachWaterPlane;
            R360W_Settings.EnableReflection           = EnableReflection;
            R360W_Settings.UseVanillaUnderwaterEngine = UseVanillaUnderwaterEngine;
            R360W_Settings.UseBiomeOverrides          = UseBiomeOverrides;
            R360W_Settings.MirrorRepeat               = MirrorRepeat;
            R360W_Settings.RingPeriod                 = RingPeriod;
            R360W_Settings.GlobalFallbackEnabled      = GlobalFallbackEnabled;
            R360W_Settings.GlobalFallbackMinY         = GlobalFallbackMinY;
            R360W_Settings.GlobalFallbackMaxY         = GlobalFallbackMaxY;
            R360W_Settings.DoLogging                  = DoLogging;

            R360W_Settings.EnableWorldGenPlusIntegration = EnableWorldGenPlusIntegration;
            R360W_Settings.WorldGenPlusSurfaceMode       = WorldGenPlusSurfaceMode;

            R360W_Settings.EnableCustomSounds = EnableCustomSounds;
            R360W_Settings.SoundVolume        = SoundVolume;
            R360W_Settings.WaterEnterFile     = WaterEnterFile;
            R360W_Settings.WaterExitFile      = WaterExitFile;
            R360W_Settings.WaterWadeFile      = WaterWadeFile;
            R360W_Settings.WaterSwimFile      = WaterSwimFile;
            R360W_Settings.EnableWaterEnter   = EnableWaterEnter;
            R360W_Settings.EnableWaterExit    = EnableWaterExit;
            R360W_Settings.EnableWaterWade    = EnableWaterWade;
            R360W_Settings.EnableWaterSwim    = EnableWaterSwim;

            R360WBiomeWaterRuntime.SetBand("Classic",  Classic);
            R360WBiomeWaterRuntime.SetBand("Lagoon",   Lagoon);
            R360WBiomeWaterRuntime.SetBand("Desert",   Desert);
            R360WBiomeWaterRuntime.SetBand("Mountain", Mountain);
            R360WBiomeWaterRuntime.SetBand("Arctic",   Arctic);
            R360WBiomeWaterRuntime.SetBand("Decent",   Decent);
            R360WBiomeWaterRuntime.SetBand("Coastal", Coastal);
            R360WBiomeWaterRuntime.SetBand("CoastalBiome", Coastal);
            R360WBiomeWaterRuntime.SetBand("CostalBiome", Coastal);
            R360WBiomeWaterRuntime.SetBand("Ocean",    Ocean);
            R360WBiomeWaterRuntime.SetBand("Hell",     Hell);
        }

        /// <summary>
        /// Convenience entry point: load config from disk and immediately apply it to runtime statics.
        /// </summary>
        public static R360WConfig LoadApply()
        {
            var cfg = LoadOrCreate();
            cfg.ApplyToStatics();
            R360WHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
            return cfg;
        }

        #endregion
    }
    #endregion

    #region SimpleIni

    /// <summary>
    /// Minimal INI reader used by Restore360Water.
    ///
    /// Notes:
    /// - Supports sections, key/value pairs, comments, bools, ints, and floats.
    /// - Keeps parsing intentionally lightweight and dependency-free.
    /// </summary>
    internal sealed class SimpleIni
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Enumerates section names currently loaded in memory.
        /// </summary>
        public IEnumerable<string> SectionNames
        {
            get { return _data.Keys; }
        }

        /// <summary>
        /// Loads an INI file from disk.
        /// </summary>
        public static SimpleIni Load(string path)
        {
            SimpleIni ini = new SimpleIni();
            string section = "";

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]") && line.Length >= 2)
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!ini._data.ContainsKey(section))
                        ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                if (!ini._data.TryGetValue(section, out Dictionary<string, string> sec))
                {
                    sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ini._data[section] = sec;
                }

                sec[key] = value;
            }

            return ini;
        }

        /// <summary>
        /// Reads a raw string value or returns the fallback.
        /// </summary>
        public string GetString(string section, string key, string fallback)
        {
            if (_data.TryGetValue(section, out Dictionary<string, string> sec) && sec.TryGetValue(key, out string value))
                return value;
            return fallback;
        }

        /// <summary>
        /// Reads a bool value using common INI-style aliases.
        /// </summary>
        public bool GetBool(string section, string key, bool fallback)
        {
            string s = GetString(section, key, fallback ? "true" : "false");
            if (bool.TryParse(s, out bool b)) return b;

            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "1":
                case "yes":
                case "y":
                case "on":
                case "true":
                    return true;

                case "0":
                case "no":
                case "n":
                case "off":
                case "false":
                    return false;

                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Reads an int value or returns the fallback.
        /// </summary>
        public int GetInt(string section, string key, int fallback)
        {
            string s = GetString(section, key, fallback.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        /// <summary>
        /// Reads a float value or returns the fallback.
        /// </summary>
        public float GetFloat(string section, string key, float fallback)
        {
            string s = GetString(section, key, fallback.ToString(CultureInfo.InvariantCulture));
            return float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
    #endregion
}