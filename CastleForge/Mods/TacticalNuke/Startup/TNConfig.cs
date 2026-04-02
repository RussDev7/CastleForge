/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using System.Globalization;
using System.IO;
using System;

using static TacticalNuke.GamePatches;

namespace TacticalNuke
{
    /// <summary>
    /// INI-backed config for TacticalNuke. Mirrors NukeRegistry & NukeFuseConfig.
    /// </summary>
    internal sealed class TNConfig
    {
        // ===== [Nuke] ===== (maps to NukeRegistry).
        public string NukeId                      = "SpaceKnife";
        public string TextureSurrogate            = "BombBlock";
        public string TextureSurrogateName        = "Tactical Nuke";                                           // Blank => Use vanilla name (localized).
        public string TextureSurrogateDescription = "Extreme yield demolition device. Detonate with caution."; // blank => Keep default description.
        public bool   DoBlockRetexture            = true;

        public int    NUKE_BLOCK_RADIUS           = 50;
        public double NUKE_KILL_RADIUS            = 32.0;
        public double NUKE_DMG_RADIUS             = 128.0;

        public bool   NukeIgnoresHardness         = true;

        // Comma-separated list of BlockTypeEnum names or numeric IDs.
        public string ExtraBreakables             = "Bedrock, FixedLantern";

        // ===== [Recipe] ===== (maps to NukeRecipeConfig).
        public bool   RecipeEnabled               = true;
        public int    RecipeOutputCount           = 1;
        public bool   RecipeInsertAfterC4         = true;

        // Comma-separated list: "<ItemIdOrName>:<Count>" entries.
        // Names are matched case-insensitively and ignore spaces/underscores.
        public string RecipeIngredients           = "ExplosivePowder:30, C4:30, TNT:30, SandBlock:30, Coal:30, SpaceRockInventory:30";

        // ===== [Fuse] ===== (maps to NukeFuseConfig).
        public int    NukeFuseSeconds             = 15;
        public double FastBlinkLastSeconds        = 5.0;
        public double MidBlinkLastSeconds         = 10.0;

        public double SlowBlink                   = 0.50;
        public double MidBlink                    = 0.25;
        public double FastBlink                   = 0.125;

        public double FuseVolume                  = 0.75;     // 0.30.

        // ===== [Crater] ===== (maps to CraterConfig).
        public string CraterShape                 = "Sphere"; // Enum name, case-insensitive.
        public bool   Hollow                      = false;
        public int    ShellThick                  = 2;        // Blocks.
        public double YScale                      = 0.70;     // <1 flatter, >1 taller.
        public double XZScale                     = 1.00;     // Horizontal scale.
        public double EdgeJitter                  = 0.15;     // 0..0.5.
        public int    NoiseSeed                   = 1337;

        // ===== [Announcement] =====
        public bool   AnnouncementEnabled         = true;
        public bool   AnnounceToServer            = false;
        public int    MinRepeatSeconds            = 2;

        // ===== [AsyncExplosionManager] ===== (maps to AsyncExplosionManager_Settings).
        public bool   AEMEnabled                  = true;
        public bool   IncludeDefaults             = false;
        public int    MaxBlocksPerFrame           = 500;

        // ===== [VanillaExplosives] ===== (maps to VanillaExplosiveConfig).
        public bool   TweakVanillaExplosives      = true;
        public int    TNT_BlockRadius             = 2;
        public int    C4_BlockRadius              = 3;
        public double TNT_DmgRadius               = 6.0;
        public double TNT_KillRadius              = 3.0;
        public double C4_DmgRadius                = 12.0;
        public double C4_KillRadius               = 6.0;

        // ===== [Hotkeys] =====
        public string ReloadConfigHotkey          = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "TacticalNuke", "TacticalNuke.Config.ini");

        public static TNConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# TacticalNuke - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[Nuke]",
                    "; Item ID (InventoryItemIDs) for the Nuke (must be a free InventoryItemIDs slot on your build).",
                    "NukeId                      = SpaceKnife",
                    "",
                    "; Which block's tile (BlockTypeEnum) indices to re-skin as the Nuke in the terrain atlas.",
                    "TextureSurrogate            = BombBlock",
                    "DoBlockRetexture            = true",
                    "; Optional display name override for the TextureSurrogate block.",
                    "; Leave blank to use the vanilla localized name(e.g. \"Space Goo\").",
                    "TextureSurrogateName        = Tactical Nuke",
                    "; Tooltip/description string used for the surrogate's inventory item entry.",
                    "; Leave blank to keep the default description.",
                    "TextureSurrogateDescription = Extreme yield demolition device. Detonate with caution.",
                    "",
                    "; Explosion tuning (per-detonation radius table is temporarily overridden when a Nuke explodes).",
                    "; When playing online, and without using the AsyncExplosionManager (see below), do not exceed a",
                    "; NUKE_BLOCK_RADIUS of 50, or you run the risk of crashing.",
                    "NUKE_BLOCK_RADIUS           = 50",
                    "NUKE_KILL_RADIUS            = 32",
                    "NUKE_DMG_RADIUS             = 128",
                    "",
                    "; If true, hardness is ignored during Nuke detonation checks.",
                    "NukeIgnoresHardness         = true",
                    "",
                    "; Extra breakable block types (names or numeric IDs, comma-separated).",
                    "ExtraBreakables             = Bedrock, FixedLantern",
                    "",
                    "[Recipe]",
                    "; Adds a crafting recipe for the Nuke item (your configured NukeId).",
                    "Enabled                     = true",
                    "; Output stack size for the crafted Nuke item.",
                    "OutputCount                 = 1",
                    "; If true, inserts the Nuke recipe right after the vanilla C4 recipe in the Explosives tab.",
                    "InsertAfterC4               = true",
                    "; Ingredients list format: ItemIdOrName:Count, ... (commas/semicolons/newlines supported).",
                    "; Examples: ExplosivePowder:30, C4:30, TNT:30, SandBlock:30, Coal:30, SpaceRockInventory:30",
                    "Ingredients                 = ExplosivePowder:30, C4:30, TNT:30, SandBlock:30, Coal:30, SpaceRockInventory:30",
                    "",
                    "[Fuse]",
                    "; Total fuse time for nukes (seconds).",
                    "NukeFuseSeconds             = 15",
                    "; Blink cadence windows (last N seconds).",
                    "FastBlinkLastSeconds        = 5",
                    "MidBlinkLastSeconds         = 10",
                    "",
                    "; Blink intervals (seconds).",
                    "SlowBlink                   = 0.50",
                    "MidBlink                    = 0.25",
                    "FastBlink                   = 0.125",
                    "",
                    "; Fuse sfx volume (0..1).",
                    "FuseVolume                  = 0.75",
                    "",
                    "[Crater]",
                    "; Shape options: Vanilla, Cube, Sphere, Diamond, CylinderY, Ellipsoid, Bowl.",
                    "Shape                       = Sphere",
                    "; If true, only remove the outer shell (hollow crater).",
                    "Hollow                      = false",
                    "; Shell thickness in blocks when Hollow = true.",
                    "ShellThick                  = 2",
                    "; Vertical scale: < 1      = flatter, > 1 = taller.",
                    "YScale                      = 0.70",
                    "; Horizontal scale (applies to X and Z).",
                    "XZScale                     = 1.00",
                    "; Edge jitter amount (0..0.5). Higher = more ragged rim.",
                    "EdgeJitter                  = 0.15",
                    "; Noise seed used for deterministic rim/jitter.",
                    "NoiseSeed                   = 1337",
                    "",
                    "[Announcement]",
                    "; Master switch for nuke status messages (armed/detonated/chain/etc.).",
                    "Enabled                     = true",
                    "; If true -> broadcast to all players; if false -> local HUD only.",
                    "AnnounceToServer            = false",
                    "; Minimum seconds before the same message at the same spot can repeat (spam guard).",
                    "MinRepeatSeconds            = 2",
                    "",
                    "[AsyncExplosionManager]",
                    "; Toggle the async explosion pipeline (producer/consumer). If false, vanilla detonation path is used.",
                    "Enabled                     = true",
                    "; If true, queue TNT/C4 explosions as well; if false, only nukes use the async path.",
                    "IncludeDefaults             = false",
                    "; Per-frame cap on blocks cleared during explosion cleanup. Higher = faster but can hitch.",
                    "MaxBlocksPerFrame           = 500",
                    "",
                    "[VanillaExplosives]",
                    "; If true, apply TNT/C4 tweaks at load using the values below.",
                    "TweakVanillaExplosives      = true",
                    "; TNT/C4 destruction radii in blocks (in-game values). The engine uses (value + 1) internally.",
                    "TNT_BlockRadius             = 2",
                    "C4_BlockRadius              = 3",
                    "; TNT damage / kill radii.",
                    "TNT_DmgRadius               = 6",
                    "TNT_KillRadius              = 3",
                    "; C4 damage / kill radii.",
                    "C4_DmgRadius                = 12",
                    "C4_KillRadius               = 6",
                    "",
                    "[Hotkeys]",
                    "; Reload the TacticalNuke config while in-game:",
                    "ReloadConfig                = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new TNConfig
            {
                // [Nuke].
                NukeId                      = ini.GetString      ("Nuke", "NukeId",                      "SpaceKnife"),    // SpaceKnfie.
                TextureSurrogate            = ini.GetString      ("Nuke", "TextureSurrogate",            "BombBlock"),
                TextureSurrogateName        = ini.GetString      ("Nuke", "TextureSurrogateName",        "Tactical Nuke"),
                TextureSurrogateDescription = ini.GetString      ("Nuke", "TextureSurrogateDescription", "Extreme yield demolition device. Detonate with caution."),
                DoBlockRetexture            = ini.GetBool        ("Nuke", "DoBlockRetexture",            true),
                NUKE_BLOCK_RADIUS           = Clamp(ini.GetInt   ("Nuke", "NUKE_BLOCK_RADIUS",           100),  1,  9999), // 768.
                NUKE_KILL_RADIUS            = Clamp(ini.GetDouble("Nuke", "NUKE_KILL_RADIUS",            32.0), 0,  9999),
                NUKE_DMG_RADIUS             = Clamp(ini.GetDouble("Nuke", "NUKE_DMG_RADIUS",             128.0), 0, 9999),
                NukeIgnoresHardness         = ini.GetBool        ("Nuke", "NukeIgnoresHardness",         true),
                ExtraBreakables             = ini.GetString      ("Nuke", "ExtraBreakables",             "Bedrock, FixedLantern"),

                // [Recipe].
                RecipeEnabled               = ini.GetBool        ("Recipe", "Enabled",                   true),
                RecipeOutputCount           = ini.GetClamp       ("Recipe", "OutputCount",               1, 1, 9999),
                RecipeInsertAfterC4         = ini.GetBool        ("Recipe", "InsertAfterC4",             true),
                RecipeIngredients           = ini.GetString      ("Recipe", "Ingredients",               "ExplosivePowder:30, C4:30, TNT:30, SandBlock:30, Coal:30, SpaceRockInventory:30"),

                // [Fuse].
                NukeFuseSeconds             = Clamp(ini.GetInt   ("Fuse", "NukeFuseSeconds",             15),   1, 3600),
                FastBlinkLastSeconds        = Clamp(ini.GetDouble("Fuse", "FastBlinkLastSeconds",        5.0),  0, 3600),
                MidBlinkLastSeconds         = Clamp(ini.GetDouble("Fuse", "MidBlinkLastSeconds",         10.0), 0, 3600),
                SlowBlink                   = Clamp(ini.GetDouble("Fuse", "SlowBlink",                   0.50), 0.01, 10.0),
                MidBlink                    = Clamp(ini.GetDouble("Fuse", "MidBlink",                    0.25), 0.01, 10.0),
                FastBlink                   = Clamp(ini.GetDouble("Fuse", "FastBlink",                   0.125),0.01, 10.0),
                FuseVolume                  = Clamp(ini.GetDouble("Fuse", "FuseVolume",                  0.75), 0.0,  1.0),

                // [Crater].
                CraterShape                 = ini.GetString      ("Crater", "Shape",                     "Sphere"),
                Hollow                      = ini.GetBool        ("Crater", "Hollow",                    false),
                ShellThick                  = ini.GetClamp       ("Crater", "ShellThick",                2, 0,  128),
                YScale                      = Clamp(ini.GetDouble("Crater", "YScale",                    0.70), 0.05, 8.0),
                XZScale                     = Clamp(ini.GetDouble("Crater", "XZScale",                   1.00), 0.05, 8.0),
                EdgeJitter                  = Clamp(ini.GetDouble("Crater", "EdgeJitter",                0.15), 0.0,  0.5),
                NoiseSeed                   = ini.GetInt         ("Crater", "NoiseSeed",                 1337),

                // [Announcement].
                AnnouncementEnabled         = ini.GetBool        ("Announcement", "Enabled",                     true),
                AnnounceToServer            = ini.GetBool        ("Announcement", "AnnounceToServer",            false),
                MinRepeatSeconds            = ini.GetInt         ("Announcement", "MinRepeatSeconds",            2),

                // [AsyncExplosionManager].
                AEMEnabled                  = ini.GetBool        ("AsyncExplosionManager", "Enabled",            true),
                IncludeDefaults             = ini.GetBool        ("AsyncExplosionManager", "IncludeDefaults",    false),
                MaxBlocksPerFrame           = ini.GetClamp       ("AsyncExplosionManager", "MaxBlocksPerFrame",  500, 1, 20000),

                // [VanillaExplosives].
                TweakVanillaExplosives      = ini.GetBool        ("VanillaExplosives", "TweakVanillaExplosives", true),
                TNT_BlockRadius             = ini.GetClamp       ("VanillaExplosives", "TNT_BlockRadius",        2, 0, 32),
                C4_BlockRadius              = ini.GetClamp       ("VanillaExplosives", "C4_BlockRadius",         3, 0, 32),
                TNT_DmgRadius               = Clamp(ini.GetDouble("VanillaExplosives", "TNT_DmgRadius",          6.0), 0, 9999),
                TNT_KillRadius              = Clamp(ini.GetDouble("VanillaExplosives", "TNT_KillRadius",         3.0), 0, 9999),
                C4_DmgRadius                = Clamp(ini.GetDouble("VanillaExplosives", "C4_DmgRadius",           12.0), 0, 9999),
                C4_KillRadius               = Clamp(ini.GetDouble("VanillaExplosives", "C4_KillRadius",          6.0), 0, 9999),

                // [Hotkeys].
                ReloadConfigHotkey          = ini.GetString      ("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };

            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                cfg.ApplyToStatics();
                TNHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // ModLoader.LogSystem.Log($"[Config] Applied from {PathShortener.ShortenForLog(ConfigPath)}.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[Config] Failed to load/apply: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            // NukeRegistry.
            NukeRegistry.NukeId                    = (int)ParseEnum(InventoryItemIDs.SpaceKnife, NukeId);
            NukeRegistry.TextureSurrogate          = ParseEnum(BlockTypeEnum.BombBlock, TextureSurrogate);
            NukeConfig.TextureSurrogateName        = TextureSurrogateName;
            NukeConfig.TextureSurrogateDescription = TextureSurrogateDescription;
            NukeRegistry.DoBlockRetexture          = DoBlockRetexture;

            NukeRegistry.NUKE_BLOCK_RADIUS         = NUKE_BLOCK_RADIUS;
            NukeRegistry.NUKE_KILL_RADIUS          = (float)NUKE_KILL_RADIUS;
            NukeRegistry.NUKE_DMG_RADIUS           = (float)NUKE_DMG_RADIUS;

            NukeRegistry.NukeIgnoresHardness       = NukeIgnoresHardness;

            // ExtraBreakables (clear and refill).
            try
            {
                NukeRegistry.ExtraBreakables.Clear();
                foreach (var e in ParseBlockList(ExtraBreakables))
                    NukeRegistry.ExtraBreakables.Add(e);
            }
            catch { /* Keep defaults if parsing fails. */ }

            // NukeRecipeConfig.
            NukeRecipeConfig.Enabled               = RecipeEnabled;
            NukeRecipeConfig.OutputCount           = RecipeOutputCount;
            NukeRecipeConfig.InsertAfterC4         = RecipeInsertAfterC4;
            NukeRecipeConfig.Ingredients           = RecipeIngredients;

            // NukeFuseConfig.
            NukeFuseConfig.NukeFuseSeconds         = NukeFuseSeconds;
            NukeFuseConfig.FastBlinkLastSeconds    = (float)FastBlinkLastSeconds;
            NukeFuseConfig.MidBlinkLastSeconds     = (float)MidBlinkLastSeconds;

            NukeFuseConfig.SlowBlink               = (float)SlowBlink;
            NukeFuseConfig.MidBlink                = (float)MidBlink;
            NukeFuseConfig.FastBlink               = (float)FastBlink;

            NukeFuseConfig.FuseVolume              = (float)FuseVolume;

            // CraterConfig.
            try
            {
                CraterConfig.Shape                 = SimpleIni.ParseCraterShape(CraterShape);
                CraterConfig.Hollow                = Hollow;
                CraterConfig.ShellThick            = ShellThick;
                CraterConfig.YScale                = (float)YScale;
                CraterConfig.XZScale               = (float)XZScale;
                CraterConfig.EdgeJitter            = (float)EdgeJitter;
                CraterConfig.NoiseSeed             = NoiseSeed;
            }
            catch { /* Keep existing values if something goes sideways. */ }

            // AnnouncementConfig.
            AnnouncementConfig.Enabled             = AnnouncementEnabled;
            AnnouncementConfig.AnnounceToServer    = AnnounceToServer;
            AnnouncementConfig.MinRepeatSeconds    = MinRepeatSeconds;

            // AsyncExplosionManager.
            AsyncExplosionManagerConfig.Enabled              = AEMEnabled;
            AsyncExplosionManagerConfig.IncludeDefaults      = IncludeDefaults;
            AsyncExplosionManagerConfig.MaxBlocksPerFrame    = MaxBlocksPerFrame;

            // If the async pipeline is disabled, flush any pending explosion jobs.
            if (!AsyncExplosionManagerConfig.Enabled)
                AsyncExplosionManager.ClearAllPending();

            // Vanilla TNT/C4 tweaks.
            VanillaExplosiveConfig.Enabled            = TweakVanillaExplosives;
            VanillaExplosiveConfig.TNT_BlockRadius    = TNT_BlockRadius;
            VanillaExplosiveConfig.C4_BlockRadius     = C4_BlockRadius;
            VanillaExplosiveConfig.TNT_DmgRadius      = (float)TNT_DmgRadius;
            VanillaExplosiveConfig.TNT_KillRadius     = (float)TNT_KillRadius;
            VanillaExplosiveConfig.C4_DmgRadius       = (float)C4_DmgRadius;
            VanillaExplosiveConfig.C4_KillRadius      = (float)C4_KillRadius;
        }

        // Helpers.
        private static int    Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
        private static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);

        private static InventoryItemIDs ParseEnum(InventoryItemIDs def, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;

            // Allow numeric.
            if (int.TryParse(s.Trim(), out var i) && Enum.IsDefined(typeof(InventoryItemIDs), i))
                return (InventoryItemIDs)i;

            // Allow forgiving name match: Case-insensitive, ignores spaces/underscores.
            string norm = s.Trim().Replace("_", "").Replace(" ", "");
            foreach (InventoryItemIDs e in Enum.GetValues(typeof(InventoryItemIDs)))
            {
                var en = e.ToString().Replace("_", "").Replace(" ", "");
                if (string.Equals(en, norm, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return def;
        }

        private static BlockTypeEnum ParseEnum(BlockTypeEnum def, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;

            // Allow numeric.
            if (int.TryParse(s.Trim(), out var i) && Enum.IsDefined(typeof(BlockTypeEnum), i))
                return (BlockTypeEnum)i;

            // Allow forgiving name match: Case-insensitive, ignores spaces/underscores.
            string norm = s.Trim().Replace("_", "").Replace(" ", "");
            foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                var en = e.ToString().Replace("_", "").Replace(" ", "");
                if (string.Equals(en, norm, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return def;
        }

        private static IEnumerable<BlockTypeEnum> ParseBlockList(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) yield break;

            foreach (var raw in csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                yield return ParseEnum(BlockTypeEnum.Bedrock, token);
            }
        }
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
            => int.TryParse(GetString(section, key, def.ToString()), out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public double GetDouble(string section, string key, double def)
            => double.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a bool value from [section] key=... accepting:
        ///   "true/false" (case-insensitive) or "1/0".
        /// Returns <paramref name="def"/> on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }

        /// <summary>
        /// Parses a <see cref="CraterShape"/> from a user/INI string in a forgiving way:
        /// - Case-insensitive enum parse first
        /// - Then compares again after stripping spaces/underscores
        /// Falls back to <see cref="CraterShape.Sphere"/> if unrecognized.
        /// </summary>
        public static CraterShape ParseCraterShape(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return CraterShape.Sphere;

            // Try direct, case-insensitive.
            if (Enum.TryParse<CraterShape>(s, true, out var v)) return v;

            // Forgiving: Remove spaces/underscores and compare.
            string norm = s.Trim().Replace("_", "").Replace(" ", "");
            foreach (CraterShape e in Enum.GetValues(typeof(CraterShape)))
            {
                string en = e.ToString().Replace("_", "").Replace(" ", "");
                if (string.Equals(en, norm, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return CraterShape.Sphere;
        }
    }
}
