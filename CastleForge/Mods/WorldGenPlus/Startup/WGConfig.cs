/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using System.Globalization;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace WorldGenPlus
{
    #region WGConfig (INI-Backed Configuration)

    /// <summary>
    /// INI-backed config for WorldGenPlus.
    ///
    /// Design goals:
    /// - Works like WEConfig (LoadOrCreate + LoadApply + ApplyToStatics).
    /// - ALSO exposes a static facade (WGConfig.Load/Save/ResetToVanilla + WGConfig.Enabled/etc)
    ///   so your patches/UI can keep calling WGConfig.* like a static config.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - This file contains BOTH the instance config (file-backed fields) and a static facade over a singleton instance.
    /// - Be careful when adding new knobs: you typically need to update
    ///   (1) instance fields, (2) LoadOrCreate parsing, (3) Save/BuildIniLines writing, (4) ResetToVanillaInMemory, (5) Snapshot.
    /// </remarks>
    internal sealed class WGConfig
    {
        #region Instance Fields (File-Backed)

        // NOTE: These are the authoritative values that get loaded from disk and written back out.

        #region [WorldGenPlus]

        /// <summary>
        /// Master toggle. When true, host swaps BlockTerrain's builder to WorldGenPlusBuilder.
        /// </summary>
        public bool Enabled = false;

        #endregion

        #region [Seed]

        /// <summary>
        /// Enables overriding the seed used by WorldInfo.
        /// </summary>
        public bool SeedOverrideEnabled = false;

        /// <summary>
        /// Seed value to apply when <see cref="SeedOverrideEnabled"/> is true.
        /// </summary>
        public int SeedOverride = 0;

        #endregion

        #region [Surface]

        /// <summary>
        /// Surface generation mode selector.
        /// Stored as int for INI compatibility; cast to <see cref="WorldGenPlusBuilder.SurfaceGenMode"/> in Snapshot().
        /// </summary>
        public int SurfaceMode = (int)WorldGenPlusBuilder.SurfaceGenMode.VanillaRings;

        #endregion

        #region [Rings]

        /// <summary>
        /// Period of the ring pattern (vanilla-ish: 4400).
        /// </summary>
        public int RingPeriod = 4400;

        /// <summary>
        /// If true, repeats mirror every period (vanilla-ish behavior).
        /// </summary>
        public bool MirrorRepeat = true;

        /// <summary>
        /// Width of the blend zone between ring cores (vanilla: 100).
        /// </summary>
        public int TransitionWidth = 100;

        // Random regions tuning.

        /// <summary>
        /// Size of a "biome region" in blocks for Random Regions mode.
        /// </summary>
        public int RegionCellSize = 512;    // Size of a "biome region" in blocks.

        /// <summary>
        /// Blend width at region edges in blocks (0 = hard edge) for Random Regions mode.
        /// </summary>
        public int RegionBlendWidth = 240;  // Blend width at region edges (0 = hard edge).

        /// <summary>
        /// Blend power (2..4 typical). Higher = sharper borders.
        /// </summary>
        public float RegionBlendPower = 3f; // 2..4 typical (higher = sharper borders).

        // Use a constant-width Voronoi bisector blend instead of f2-f1 / inverse-distance only.

        /// <summary>
        /// When true, uses a constant-width Voronoi bisector blend for smoother region borders.
        /// </summary>
        public bool RegionSmoothEdges = true;

        /// <summary>
        /// Shared overlay blending radius used for fades / biome-overlay modulation.
        /// </summary>
        public int WorldBlendRadius = 3600;

        /// <summary>
        /// Ring core definitions (end radii + biome type names).
        /// Note: ring math expects increasing core ends; LoadOrCreate sorts these.
        /// </summary>
        public List<RingCore> Rings = new List<RingCore>();

        #endregion

        #region [RingsRandom]

        /// <summary>
        /// Used when a ring core's biome is set to "@Random".
        /// Duplicates allowed (weights).
        /// </summary>
        public List<string> RandomRingBiomeChoices = new List<string>();

        /// <summary>
        /// If true, each repeated ring "period" gets a different random assignment.
        /// If false, the random assignment repeats every period (same picks forever).
        /// </summary>
        public bool RandomRingsVaryByPeriod = true;

        /// <summary>
        /// Optional: add discovered custom biome types into the ring-random bag (weight=1).
        /// </summary>
        public bool AutoIncludeCustomBiomesForRandomRings = false;

        #endregion

        #region [SingleBiome]

        /// <summary>
        /// Full biome type name used when <see cref="SurfaceMode"/> is set to Single.
        /// </summary>
        public string SingleSurfaceBiome = "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome";

        #endregion

        #region [RandomRegions]

        /// <summary>
        /// Bag of biome type names used for Random Regions surface mode.
        /// Duplicates allowed (weights).
        /// </summary>
        public List<string> RandomSurfaceBiomeChoices = new List<string>();

        /// <summary>
        /// Optional: add discovered custom biome types into the Random Regions bag (weight=1).
        /// </summary>
        public bool AutoIncludeCustomBiomesForRandomRegions = false;

        #endregion

        #region [Overlays]

        /// <summary>Enables crash site overlay pass.</summary>
        public bool EnableCrashSites = true;

        /// <summary>Enables caves overlay pass.</summary>
        public bool EnableCaves = true;

        /// <summary>Enables ore overlay pass.</summary>
        public bool EnableOre = true;

        /// <summary>
        /// When true, prevents certain overlays (Hell/HellCeiling/Bedrock) from running on special/test/unusual biomes.
        /// When false, overlays run everywhere (may cause Hell to appear in those biomes).
        /// </summary>
        public bool EnableBiomeOverlayGuards = true;

        /// <summary>Enables Hell Ceiling overlay pass.</summary>
        public bool EnableHellCeiling = true;

        /// <summary>Enables Hell overlay pass.</summary>
        public bool EnableHell = true;

        /// <summary>Enables Bedrock overlay pass.</summary>
        public bool EnableBedrock = true;

        /// <summary>Enables Origin/LanternLand overlay pass.</summary>
        public bool EnableOrigin = true;

        /// <summary>Enables Water overlay pass.</summary>
        public bool EnableWater = true;

        /// <summary>Enables Trees overlay pass.</summary>
        public bool EnableTrees = true;

        #endregion

        #region [Spawners]

        /// <summary>
        /// When true, the cave overlay may emit vanilla-style enemy / alien spawner blocks while carving caves.
        /// </summary>
        public bool EnableCaveSpawners = true;

        /// <summary>
        /// When true, the hell floor overlay may emit vanilla-style boss spawner blocks.
        /// </summary>
        public bool EnableHellBossSpawners = true;

        #endregion

        #region [Bedrock]

        /// <summary>Noise frequency control for bedrock thickness. Higher => smoother, slower variation.</summary>
        public int Bedrock_CoordDiv = 1;

        /// <summary>Minimum bedrock thickness in blocks (baseline thickness).</summary>
        public int Bedrock_MinLevel = 1;

        /// <summary>Additional thickness variation in blocks (wobble amplitude). 0 => perfectly flat thickness.</summary>
        public int Bedrock_Variance = 3;

        #endregion

        #region [CrashSite]

        public float Crash_WorldScale              = 0.0046875f;
        public float Crash_NoiseThreshold          = 0.5f;

        public int   Crash_GroundPlane             = 66;
        public int   Crash_StartY                  = 20;
        public int   Crash_EndYExclusive           = 126;

        public float Crash_CraterDepthMul          = 140f;  // 7*20.
        public bool  Crash_EnableMound             = true;
        public float Crash_MoundThreshold          = 0.55f;
        public float Crash_MoundHeightMul          = 200f;  // 10*20.

        public int   Crash_CarvePadding            = 10;
        public bool  Crash_ProtectBloodStone       = true;

        // Slime settings (only affects SpaceRock interior).
        public bool  Crash_EnableSlime             = true;
        public int   Crash_SlimePosOffset          = 777;
        public int   Crash_SlimeCoarseDiv          = 2;
        public int   Crash_SlimeAdjustCenter       = 128;
        public int   Crash_SlimeAdjustDiv          = 8;
        public int   Crash_SlimeThresholdBase      = 265;
        public float Crash_SlimeBlendToIntBlendMul = 10f;
        public float Crash_SlimeThresholdBlendMul  = 1f;
        public int   Crash_SlimeTopPadding         = 3;

        #endregion

        #region [Ore]

        // Builder-side blender shaping knobs:
        public int   Ore_BlendRadius                   = 0;    // 0 = use WorldBlendRadius.
        public float Ore_BlendMul                      = 1f;
        public float Ore_BlendAdd                      = 0f;

        // CustomOreDepositer.Settings knobs.
        public int   Ore_MaxY                          = 128;
        public float Ore_BlendToIntBlendMul            = 10f;

        public int   Ore_NoiseAdjustCenter             = 128;
        public int   Ore_NoiseAdjustDiv                = 8;

        public int   Ore_CoalCoarseDiv                 = 4;
        public int   Ore_CoalThresholdBase             = 255;
        public int   Ore_CopperThresholdOffset         = -5;

        public int   Ore_IronOffset                    = 1000; // Applied to x,y,z equally.
        public int   Ore_IronCoarseDiv                 = 3;
        public int   Ore_IronThresholdBase             = 264;
        public int   Ore_GoldThresholdOffset           = -9;
        public int   Ore_GoldMaxY                      = 50;

        public int   Ore_DeepPassMaxY                  = 50;
        public int   Ore_DiamondOffset                 = 777;  // Applied to x,y,z equally.
        public int   Ore_DiamondCoarseDiv              = 2;
        public int   Ore_LavaThresholdBase             = 266;
        public int   Ore_DiamondThresholdOffset        = -11;
        public int   Ore_DiamondMaxY                   = 40;

        // Loot knobs.
        public bool  Ore_LootEnabled                   = true;
        public bool  Ore_LootOnNonRockBlocks           = true;
        public int   Ore_LootSandSnowMaxY              = 60;

        public int   Ore_LootOffset                    = 333;  // Applied to x,y,z equally.
        public int   Ore_LootCoarseDiv                 = 5;
        public int   Ore_LootFineDiv                   = 2;

        // Survival loot thresholds.
        public int   Ore_LootSurvivalMainThreshold     = 268;
        public int   Ore_LootSurvivalLuckyThreshold    = 249;
        public int   Ore_LootSurvivalRegularThreshold  = 145;
        public int   Ore_LootLuckyBandMinY             = 55;
        public int   Ore_LootLuckyBandMaxYStart        = 100;

        // Scavenger loot thresholds
        public int   Ore_LootScavengerTargetMod        = 1;
        public int   Ore_LootScavengerMainThreshold    = 267;
        public int   Ore_LootScavengerLuckyThreshold   = 250;
        public int   Ore_LootScavengerLuckyExtraPerMod = 3;
        public int   Ore_LootScavengerRegularThreshold = 165;

        #endregion

        #region [Trees]

        public float Tree_TreeScale      = 0.4375f;
        public float Tree_TreeThreshold  = 0.6f;

        public int Tree_BaseTrunkHeight  = 5;
        public float Tree_HeightVarMul   = 9f;

        public int Tree_LeafRadius       = 3;
        public float Tree_LeafNoiseScale = 0.5f;
        public float Tree_LeafCutoff     = 0.25f;

        public int Tree_GroundScanStartY = 124;
        public int Tree_GroundScanMinY   = 0;
        public int Tree_MinGroundHeight  = 1;

        #endregion

        #region [Hotkeys]

        /// <summary>
        /// Optional reload binding (wire it up like WEHotkeys if desired).
        /// </summary>
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        #endregion

        #endregion

        #region Paths

        /// <summary>
        /// Absolute path to the INI file on disk.
        /// Note: Stored under "!Mods/WorldGenPlus/" relative to the game's base directory.
        /// </summary>
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "WorldGenPlus", "WorldGenPlus.Config.ini");

        #endregion

        #region LoadOrCreate / Apply (WEConfig Style)

        /// <summary>
        /// Loads config from disk (creating it first if missing) and returns a fully-populated instance.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Uses vanilla defaults as fallbacks for missing keys.
        /// - Reads depositor vanilla settings from their Settings.Vanilla() to keep them as single source of truth.
        /// - Rings are rebuilt from INI and then sorted by EndRadius (ring math expects ascending ends).
        /// </remarks>
        public static WGConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
                WriteDefaultFile();

            var ini = SimpleIni.Load(ConfigPath);

            // Vanilla defaults (used as fallbacks).
            var vanilla = new List<RingCore>(VanillaRings());

            // Vanilla depositor defaults (single source of truth).
            var bedV   = CustomBedrockDepositor.Settings.Vanilla();
            var crashV = CustomCrashSiteDepositor.Settings.Vanilla();
            var oreV   = CustomOreDepositor.Settings.Vanilla();

            // NOTE: These 3 are the extra "blend shaping" knobs (not in the depositor Settings struct).
            const int   OreBlendRadiusDefault = 3600;
            const float OreBlendMulDefault    = 1f;
            const float OreBlendAddDefault    = 0f;

            var cfg = new WGConfig
            {
                #region [WorldGenPlus]

                Enabled = ini.GetBool("WorldGenPlus", "Enabled", false),

                #endregion

                #region [Seed]

                SeedOverrideEnabled = ini.GetBool("Seed", "Enabled", false),
                SeedOverride        = ini.GetInt("Seed", "Value", 0),

                #endregion

                #region [Multiplayer]

                Multiplayer_SyncFromHost       = ini.GetBool("Multiplayer", "SyncFromHost", true),
                Multiplayer_BroadcastToClients = ini.GetBool("Multiplayer", "BroadcastToClients", true),

                #endregion

                #region [Surface]

                SurfaceMode = Clamp(ini.GetInt("Surface", "Mode", (int)WorldGenPlusBuilder.SurfaceGenMode.VanillaRings), 0, 3),
                
                #endregion

                #region [Rings]

                // Backward compatible:
                // - prefer Region* keys
                // - fall back to Random* keys if present
                RegionCellSize = Clamp(ini.GetInt("Surface", "RegionCellSize", ini.GetInt("Surface", "RandomCellSize", 512)), 32, 16384),
                RegionBlendWidth  = Clamp(ini.GetInt("Surface", "RegionBlendWidth", ini.GetInt("Surface", "RandomBlendWidth", 240)), 0, 4096),
                RegionBlendPower  = Clamp(ini.GetFloat("Surface", "RegionBlendPower", 3f), 0.5f, 8f),

                RingPeriod        = Clamp(ini.GetInt("Rings", "Period", 4400), 1, 2_000_000),
                MirrorRepeat      = ini.GetBool("Rings", "MirrorRepeat", true),
                TransitionWidth   = Clamp(ini.GetInt("Rings", "TransitionWidth", 100), 0, 50_000),

                RegionSmoothEdges = ini.GetBool("Surface", "RegionSmoothEdges", true),
                WorldBlendRadius  = Clamp(ini.GetInt("Overlays", "WorldBlendRadius", 3600), 1, 2_000_000),

                Rings             = new List<RingCore>(),

                #endregion

                #region [SingleBiome]

                SingleSurfaceBiome = (ini.GetString("SingleBiome", "SingleSurfaceBiome", "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome") ?? "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome").Trim(),

                #endregion

                #region [Overlays]

                EnableCrashSites = ini.GetBool("Overlays", "CrashSites", true),
                EnableCaves              = ini.GetBool("Overlays", "Caves", true),
                EnableOre                = ini.GetBool("Overlays", "Ore", true),
                EnableBiomeOverlayGuards = ini.GetBool("Overlays", "BiomeGuards", true),
                EnableHellCeiling        = ini.GetBool("Overlays", "HellCeiling", true),
                EnableHell               = ini.GetBool("Overlays", "Hell", true),
                EnableBedrock            = ini.GetBool("Overlays", "Bedrock", true),
                EnableOrigin             = ini.GetBool("Overlays", "Origin", true),
                EnableWater              = ini.GetBool("Overlays", "Water", true),
                EnableTrees              = ini.GetBool("Overlays", "Trees", true),

                #endregion

                #region [Spawners]

                EnableCaveSpawners     = ini.GetBool("Spawners", "EnableCaveSpawners", true),
                EnableHellBossSpawners = ini.GetBool("Spawners", "EnableHellBossSpawners", true),

                #endregion

                #region [Bedrock]

                Bedrock_CoordDiv = Clamp(ini.GetInt("Bedrock", "CoordDiv", bedV.CoordDiv), 1, 4096),
                Bedrock_MinLevel = Clamp(ini.GetInt("Bedrock", "MinLevel", bedV.MinLevel), 0, 128),
                Bedrock_Variance = Clamp(ini.GetInt("Bedrock", "Variance", bedV.Variance), 0, 128),

                #endregion

                #region [CrashSite]

                Crash_WorldScale              = Clamp(ini.GetFloat("CrashSite", "WorldScale", crashV.WorldScale), 0.000001f, 1f),
                Crash_NoiseThreshold          = Clamp(ini.GetFloat("CrashSite", "NoiseThreshold", crashV.NoiseThreshold), 0f, 1f),

                Crash_GroundPlane             = Clamp(ini.GetInt("CrashSite", "GroundPlane", crashV.GroundPlane), 0, 1024),
                Crash_StartY                  = Clamp(ini.GetInt("CrashSite", "StartY", crashV.StartY), 0, 4096),
                Crash_EndYExclusive           = Clamp(ini.GetInt("CrashSite", "EndYExclusive", crashV.EndYExclusive), 0, 4096),

                Crash_CraterDepthMul          = Clamp(ini.GetFloat("CrashSite", "CraterDepthMul", crashV.CraterDepthMul), 0f, 100000f),

                Crash_EnableMound             = ini.GetBool("CrashSite", "EnableMound", crashV.EnableMound),
                Crash_MoundThreshold          = Clamp(ini.GetFloat("CrashSite", "MoundThreshold", crashV.MoundThreshold), 0f, 1f),
                Crash_MoundHeightMul          = Clamp(ini.GetFloat("CrashSite", "MoundHeightMul", crashV.MoundHeightMul), 0f, 100000f),

                Crash_CarvePadding            = Clamp(ini.GetInt("CrashSite", "CarvePadding", crashV.CarvePadding), 0, 4096),
                Crash_ProtectBloodStone       = ini.GetBool("CrashSite", "ProtectBloodStone", crashV.ProtectBloodStone),

                Crash_EnableSlime             = ini.GetBool("CrashSite", "EnableSlime", crashV.EnableSlime),
                Crash_SlimePosOffset          = Clamp(ini.GetInt("CrashSite", "SlimePosOffset", crashV.SlimePosOffset), -1_000_000, 1_000_000),
                Crash_SlimeCoarseDiv          = Clamp(ini.GetInt("CrashSite", "SlimeCoarseDiv", crashV.SlimeCoarseDiv), 1, 4096),
                Crash_SlimeAdjustCenter       = Clamp(ini.GetInt("CrashSite", "SlimeAdjustCenter", crashV.SlimeAdjustCenter), -1_000_000, 1_000_000),
                Crash_SlimeAdjustDiv          = Clamp(ini.GetInt("CrashSite", "SlimeAdjustDiv", crashV.SlimeAdjustDiv), 1, 4096),
                Crash_SlimeThresholdBase      = Clamp(ini.GetInt("CrashSite", "SlimeThresholdBase", crashV.SlimeThresholdBase), -1_000_000, 1_000_000),
                Crash_SlimeBlendToIntBlendMul = Clamp(ini.GetFloat("CrashSite", "SlimeBlendToIntBlendMul", crashV.SlimeBlendToIntBlendMul), 0f, 1000f),
                Crash_SlimeThresholdBlendMul  = Clamp(ini.GetFloat("CrashSite", "SlimeThresholdBlendMul", crashV.SlimeThresholdBlendMul), -1000f, 1000f),
                Crash_SlimeTopPadding         = Clamp(ini.GetInt("CrashSite", "SlimeTopPadding", crashV.SlimeTopPadding), 0, 256),

                #endregion

                #region [Ore]

                // Extra "blend shaping" knobs (not part of CustomOreDepositer.Settings).
                Ore_BlendRadius                   = Clamp(ini.GetInt("Ore", "BlendRadius", OreBlendRadiusDefault), 0, 2_000_000),
                Ore_BlendMul                      = Clamp(ini.GetFloat("Ore", "BlendMul", OreBlendMulDefault), -1000f, 1000f),
                Ore_BlendAdd                      = Clamp(ini.GetFloat("Ore", "BlendAdd", OreBlendAddDefault), -1000f, 1000f),

                Ore_MaxY                          = Clamp(ini.GetInt("Ore", "MaxY", oreV.MaxY), 0, 4096),
                Ore_BlendToIntBlendMul            = Clamp(ini.GetFloat("Ore", "BlendToIntBlendMul", oreV.BlendToIntBlendMul), 0f, 1000f),

                Ore_NoiseAdjustCenter             = Clamp(ini.GetInt("Ore", "NoiseAdjustCenter", oreV.NoiseAdjustCenter), -1_000_000, 1_000_000),
                Ore_NoiseAdjustDiv                = Clamp(ini.GetInt("Ore", "NoiseAdjustDiv", oreV.NoiseAdjustDiv), 1, 4096),

                Ore_CoalCoarseDiv                 = Clamp(ini.GetInt("Ore", "CoalCoarseDiv", oreV.CoalCoarseDiv), 1, 4096),
                Ore_CoalThresholdBase             = Clamp(ini.GetInt("Ore", "CoalThresholdBase", oreV.CoalThresholdBase), -1_000_000, 1_000_000),
                Ore_CopperThresholdOffset         = Clamp(ini.GetInt("Ore", "CopperThresholdOffset", oreV.CopperThresholdOffset), -1_000_000, 1_000_000),

                // Vanilla uses (1000,1000,1000) so a single int offset is fine here.
                Ore_IronOffset                    = Clamp(ini.GetInt("Ore", "IronOffset", oreV.IronPosOffset.X), -1_000_000, 1_000_000),
                Ore_IronCoarseDiv                 = Clamp(ini.GetInt("Ore", "IronCoarseDiv", oreV.IronCoarseDiv), 1, 4096),
                Ore_IronThresholdBase             = Clamp(ini.GetInt("Ore", "IronThresholdBase", oreV.IronThresholdBase), -1_000_000, 1_000_000),
                Ore_GoldThresholdOffset           = Clamp(ini.GetInt("Ore", "GoldThresholdOffset", oreV.GoldThresholdOffset), -1_000_000, 1_000_000),
                Ore_GoldMaxY                      = Clamp(ini.GetInt("Ore", "GoldMaxY", oreV.GoldMaxY), 0, 4096),

                Ore_DeepPassMaxY                  = Clamp(ini.GetInt("Ore", "DeepPassMaxY", oreV.DeepPassMaxY), 0, 4096),
                Ore_DiamondOffset                 = Clamp(ini.GetInt("Ore", "DiamondOffset", oreV.DiamondPosOffset.X), -1_000_000, 1_000_000),
                Ore_DiamondCoarseDiv              = Clamp(ini.GetInt("Ore", "DiamondCoarseDiv", oreV.DiamondCoarseDiv), 1, 4096),
                Ore_LavaThresholdBase             = Clamp(ini.GetInt("Ore", "LavaThresholdBase", oreV.LavaThresholdBase), -1_000_000, 1_000_000),
                Ore_DiamondThresholdOffset        = Clamp(ini.GetInt("Ore", "DiamondThresholdOffset", oreV.DiamondThresholdOffset), -1_000_000, 1_000_000),
                Ore_DiamondMaxY                   = Clamp(ini.GetInt("Ore", "DiamondMaxY", oreV.DiamondMaxY), 0, 4096),

                Ore_LootEnabled                   = ini.GetBool("Ore", "LootEnabled", oreV.LootEnabled),
                Ore_LootOnNonRockBlocks           = ini.GetBool("Ore", "LootOnNonRockBlocks", oreV.LootOnNonRockBlocks),
                Ore_LootSandSnowMaxY              = Clamp(ini.GetInt("Ore", "LootSandSnowMaxY", oreV.LootSandSnowMaxY), 0, 4096),

                Ore_LootOffset                    = Clamp(ini.GetInt("Ore", "LootOffset", oreV.LootPosOffset.X), -1_000_000, 1_000_000),
                Ore_LootCoarseDiv                 = Clamp(ini.GetInt("Ore", "LootCoarseDiv", oreV.LootCoarseDiv), 1, 4096),
                Ore_LootFineDiv                   = Clamp(ini.GetInt("Ore", "LootFineDiv", oreV.LootFineDiv), 1, 4096),

                Ore_LootSurvivalMainThreshold     = Clamp(ini.GetInt("Ore", "LootSurvivalMainThreshold", oreV.LootSurvivalMainThreshold), -1_000_000, 1_000_000),
                Ore_LootSurvivalLuckyThreshold    = Clamp(ini.GetInt("Ore", "LootSurvivalLuckyThreshold", oreV.LootSurvivalLuckyThreshold), -1_000_000, 1_000_000),
                Ore_LootSurvivalRegularThreshold  = Clamp(ini.GetInt("Ore", "LootSurvivalRegularThreshold", oreV.LootSurvivalRegularThreshold), -1_000_000, 1_000_000),
                Ore_LootLuckyBandMinY             = Clamp(ini.GetInt("Ore", "LootLuckyBandMinY", oreV.LootLuckyBandMinY), 0, 4096),
                Ore_LootLuckyBandMaxYStart        = Clamp(ini.GetInt("Ore", "LootLuckyBandMaxYStart", oreV.LootLuckyBandMaxYStart), 0, 4096),

                Ore_LootScavengerTargetMod        = Clamp(ini.GetInt("Ore", "LootScavengerTargetMod", oreV.LootScavengerTargetMod), -1_000_000, 1_000_000),
                Ore_LootScavengerMainThreshold    = Clamp(ini.GetInt("Ore", "LootScavengerMainThreshold", oreV.LootScavengerMainThreshold), -1_000_000, 1_000_000),
                Ore_LootScavengerLuckyThreshold   = Clamp(ini.GetInt("Ore", "LootScavengerLuckyThreshold", oreV.LootScavengerLuckyThreshold), -1_000_000, 1_000_000),
                Ore_LootScavengerLuckyExtraPerMod = Clamp(ini.GetInt("Ore", "LootScavengerLuckyExtraPerMod", oreV.LootScavengerLuckyExtraPerMod), -1_000_000, 1_000_000),
                Ore_LootScavengerRegularThreshold = Clamp(ini.GetInt("Ore", "LootScavengerRegularThreshold", oreV.LootScavengerRegularThreshold), -1_000_000, 1_000_000),

                #endregion

                #region [Trees]

                Tree_TreeScale        = Clamp(ini.GetFloat("Trees", "Scale", 0.4375f), 0.01f, 4f),
                Tree_TreeThreshold    = Clamp(ini.GetFloat("Trees", "Threshold", 0.6f), 0f, 1f),
                Tree_BaseTrunkHeight  = Clamp(ini.GetInt("Trees", "BaseTrunkHeight", 5), 1, 64),
                Tree_HeightVarMul     = Clamp(ini.GetFloat("Trees", "HeightVarMul", 9f), 0f, 64f),
                Tree_LeafRadius       = Clamp(ini.GetInt("Trees", "LeafRadius", 3), 0, 17),
                Tree_LeafNoiseScale   = Clamp(ini.GetFloat("Trees", "LeafNoiseScale", 0.5f), 0.01f, 4f),
                Tree_LeafCutoff       = Clamp(ini.GetFloat("Trees", "LeafCutoff", 0.25f), -1f, 2f),
                Tree_GroundScanStartY = Clamp(ini.GetInt("Trees", "GroundScanStartY", 124), 0, 127),
                Tree_GroundScanMinY   = Clamp(ini.GetInt("Trees", "GroundScanMinY", 0), 0, 127),
                Tree_MinGroundHeight  = Clamp(ini.GetInt("Trees", "MinGroundHeight", 1), 0, 127),

                #endregion

                #region [Hotkeys]

                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),

                #endregion
            };

            #region [Rings]

            int ringCount = Clamp(ini.GetInt("Rings", "CoreCount", vanilla.Count), 0, 256);

            for (int i = 0; i < ringCount; i++)
            {
                int defEnd = (i < vanilla.Count) ? vanilla[i].EndRadius : 0;
                string defBiome = (i < vanilla.Count) ? vanilla[i].BiomeType : "";

                int end = ini.GetInt("Rings", "Core" + i + "End", defEnd);
                string biome = ini.GetString("Rings", "Core" + i + "Biome", defBiome);

                if (end <= 0) continue;
                if (string.IsNullOrWhiteSpace(biome)) continue;

                cfg.Rings.Add(new RingCore(Clamp(end, 1, 2_000_000), biome.Trim()));
            }

            if (cfg.Rings.Count == 0)
                cfg.Rings.AddRange(vanilla);

            // Ring math expects increasing core ends.
            cfg.Rings.Sort((a, b) => a.EndRadius.CompareTo(b.EndRadius));

            #endregion

            #region [RingsRandom]

            cfg.RandomRingsVaryByPeriod =
                ini.GetBool("RingsRandom", "VaryByPeriod", true);

            cfg.AutoIncludeCustomBiomesForRandomRings =
                ini.GetBool("RingsRandom", "AutoIncludeCustomBiomes", false);

            {
                var def = DefaultRandomRingBiomeChoices();
                int count = Clamp(ini.GetInt("RingsRandom", "BiomeCount", def.Length), 0, 512);

                cfg.RandomRingBiomeChoices = new List<string>(count);

                for (int i = 0; i < count; i++)
                {
                    string s = ini.GetString("RingsRandom", "Biome" + i, (i < def.Length) ? def[i] : "");
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    cfg.RandomRingBiomeChoices.Add(s.Trim()); // Keep duplicates (weights).
                }

                if (cfg.RandomRingBiomeChoices.Count == 0)
                    cfg.RandomRingBiomeChoices.AddRange(def);

                if (cfg.AutoIncludeCustomBiomesForRandomRings)
                {
                    CustomBiomeRegistry.Refresh();
                    var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();

                    for (int i = 0; i < custom.Length; i++)
                    {
                        string t = custom[i];
                        bool has = cfg.RandomRingBiomeChoices.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
                        if (!has)
                            cfg.RandomRingBiomeChoices.Add(t);
                    }
                }
            }
            #endregion

            #region [RandomRegions]

            cfg.AutoIncludeCustomBiomesForRandomRegions = ini.GetBool("RandomRegions", "AutoIncludeCustomBiomes", false);

            // Biome bag (duplicates allowed)
            {
                var def = DefaultRandomSurfaceBiomeChoices();

                int count = Clamp(ini.GetInt("RandomRegions", "BiomeCount", def.Length), 0, 512);

                cfg.RandomSurfaceBiomeChoices = new List<string>(count);

                for (int i = 0; i < count; i++)
                {
                    string s = ini.GetString("RandomRegions", "Biome" + i, (i < def.Length) ? def[i] : "");
                    if (string.IsNullOrWhiteSpace(s)) continue;

                    // IMPORTANT: Keep duplicates (do NOT de-dupe).
                    cfg.RandomSurfaceBiomeChoices.Add(s.Trim());
                }

                // Fallback if user nuked the list.
                if (cfg.RandomSurfaceBiomeChoices.Count == 0)
                    cfg.RandomSurfaceBiomeChoices.AddRange(def);

                if (cfg.AutoIncludeCustomBiomesForRandomRegions)
                {
                    CustomBiomeRegistry.Refresh();
                    var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();

                    for (int i = 0; i < custom.Length; i++)
                    {
                        string t = custom[i];
                        bool has = cfg.RandomSurfaceBiomeChoices.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
                        if (!has)
                            cfg.RandomSurfaceBiomeChoices.Add(t); // Add once (weight=1).
                    }
                }
            }
            #endregion

            return cfg;
        }
        #endregion

        #region Static Facade (What The Patches/UI Can Call)

        // NOTE: The static facade exists so older call sites can do WGConfig.EnabledValue, etc.
        //       It also centralizes lock/loaded state for hot reload and UI edits.

        private static readonly object _sync = new object();
        private static bool            _loaded;
        private static WGConfig        _current;

        #region [Multiplayer]

        /// <summary>
        /// When true (client-side), accept host-sent world-gen settings and use them for this session.
        /// This does NOT have to persist to disk unless you call Save().
        /// </summary>
        public bool Multiplayer_SyncFromHost = true;

        /// <summary>
        /// When true (host-side), respond to clients requesting world-gen settings.
        /// </summary>
        public bool Multiplayer_BroadcastToClients = true;

        #endregion

        #region Network Override (Multiplayer Session)

        private static bool            _hasNetworkOverride;
        private static WGConfig        _baselineBeforeNetworkOverride;

        /// <summary>
        /// Returns true if a host-provided override is active for this session.
        /// Summary: Used to gate client-side builder swapping to prevent desync.
        /// </summary>
        public static bool HasNetworkOverride
        {
            get { lock (_sync) return _hasNetworkOverride; }
        }

        /// <summary>
        /// Applies host-provided settings to the in-memory config ONLY (no disk writes).
        /// Summary: Makes clients use the host's WorldGenPlus settings for this session so everyone stays in sync.
        /// </summary>
        public static void ApplyNetworkOverride(WorldGenPlusSettings fromHost)
        {
            if (fromHost == null) return;

            EnsureLoaded();

            lock (_sync)
            {
                // Save a baseline once so we can restore on disconnect / return to menu.
                if (!_hasNetworkOverride)
                {
                    _baselineBeforeNetworkOverride = _current.CloneShallowForRestore();
                    _hasNetworkOverride = true;
                }

                // Overwrite the live in-memory instance (do NOT Save()).
                _current.ImportFromSettings(fromHost);

                _loaded = true;
            }
            TryRefreshLiveTerrainBuilder("ApplyNetworkOverride");
        }

        /// <summary>
        /// Applies the current effective builder to the live terrain instance (if any).
        /// Summary: Fixes the case where the host override arrives AFTER BlockTerrain.Init already ran.
        /// </summary>
        private static void TryRefreshLiveTerrainBuilder(string reason)
        {
            try
            {
                var terrain = BlockTerrain.Instance;
                if (terrain == null || terrain.WorldInfo == null) return;

                var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                bool isHost = game?.MyNetworkGamer?.IsHost ?? true; // Default true for offline/safe.

                var s = Snapshot();

                bool allowWgp =
                    (isHost && s != null && s.Enabled)
                    || (!isHost
                        && Current.Multiplayer_SyncFromHost
                        && HasNetworkOverride
                        && s != null
                        && s.Enabled);

                if (allowWgp)
                {
                    terrain._worldBuilder = new WorldGenPlusBuilder(terrain.WorldInfo, s);
                    ModLoader.LogSystem.Log($"Live terrain builder refreshed (WGP) due to: {reason}.");
                }
                else
                {
                    terrain._worldBuilder = terrain.WorldInfo.GetBuilder();
                    ModLoader.LogSystem.Log($"Live terrain builder refreshed (Vanilla) due to: {reason}.");
                }

                terrain.Reset();
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"Live terrain builder refresh FAILED ({reason}): {ex}.");
            }
        }

        /// <summary>
        /// Restores your local config after leaving a multiplayer session.
        /// Summary: Reverts any host override so your personal INI-backed settings take effect again.
        /// </summary>
        public static void ClearNetworkOverride()
        {
            lock (_sync)
            {
                if (!_hasNetworkOverride) return;

                if (_baselineBeforeNetworkOverride != null)
                    _current = _baselineBeforeNetworkOverride;
                else
                    _current = LoadOrCreate();

                _baselineBeforeNetworkOverride = null;
                _hasNetworkOverride = false;
                _loaded = true;
            }
            TryRefreshLiveTerrainBuilder("ClearNetworkOverride");
        }
        #endregion

        /// <summary>
        /// Ensures config has been loaded at least once. Safe to call repeatedly.
        /// </summary>
        public static void Load()
        {
            EnsureLoaded();
        }

        /// <summary>
        /// Loads config lazily, one time, in a threadsafe manner.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_sync)
            {
                if (_loaded) return;

                try
                {
                    _current = LoadOrCreate();
                    // _current.ApplyToStatics();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WGConfig] Failed to load: " + ex.Message);
                    _current = new WGConfig();
                    _current.ResetToVanillaInMemory();
                }

                _loaded = true;
            }
        }

        /// <summary>
        /// Returns the current in-memory config instance (after EnsureLoaded).
        /// </summary>
        public static WGConfig Current
        {
            get { EnsureLoaded(); return _current; }
        }

        /// <summary>
        /// Reloads config from disk and replaces the current in-memory instance.
        /// </summary>
        public static void ReloadFromDisk()
        {
            lock (_sync)
            {
                _current = LoadOrCreate();
                // _current.ApplyToStatics();
                _loaded = true;
            }
        }

        // --- Static getters/setters so old call sites can do WGConfig.EnabledValue, etc. ---

        #region Depositor Knobs (Static Facade)

        #region [WorldGenPlus]

        /// <summary>Static facade for <see cref="Enabled"/>.</summary>
        public static bool EnabledValue { get { EnsureLoaded(); return _current.Enabled; } set { EnsureLoaded(); _current.Enabled = value; } }

        #endregion

        #region [Seed]

        /// <summary>Static facade for <see cref="SeedOverrideEnabled"/>.</summary>
        public static bool SeedOverrideEnabledValue { get { EnsureLoaded(); return _current.SeedOverrideEnabled; } set { EnsureLoaded(); _current.SeedOverrideEnabled = value; } }

        /// <summary>Static facade for <see cref="SeedOverride"/>.</summary>
        public static int SeedOverrideValue         { get { EnsureLoaded(); return _current.SeedOverride;        } set { EnsureLoaded(); _current.SeedOverride = value; } }

        #endregion

        #region [Multiplayer]

        /// <summary>Client-side: when ON, accept host-sent WorldGenPlus settings for this session.</summary>
        public static bool Multiplayer_SyncFromHostValue
        {
            get { EnsureLoaded(); return _current.Multiplayer_SyncFromHost; }
            set { EnsureLoaded(); _current.Multiplayer_SyncFromHost = value; }
        }

        /// <summary>Host-side: when ON, respond to clients requesting WorldGenPlus settings.</summary>
        public static bool Multiplayer_BroadcastToClientsValue
        {
            get { EnsureLoaded(); return _current.Multiplayer_BroadcastToClients; }
            set { EnsureLoaded(); _current.Multiplayer_BroadcastToClients = value; }
        }
        #endregion

        #region [Surface]

        /// <summary>Static facade for <see cref="SurfaceMode"/> with clamping.</summary>
        public static int SurfaceModeValue { get { EnsureLoaded(); return _current.SurfaceMode; } set { EnsureLoaded(); _current.SurfaceMode = Clamp(value, 0, 3); } }

        #endregion

        #region [Rings]

        public static int RegionCellSizeValue     { get { EnsureLoaded(); return _current.RegionCellSize;    } set { EnsureLoaded(); _current.RegionCellSize = Clamp(value, 32, 16384); } }
        public static int RegionBlendWidthValue   { get { EnsureLoaded(); return _current.RegionBlendWidth;  } set { EnsureLoaded(); _current.RegionBlendWidth = Clamp(value, 0, 4096); } }
        public static float RegionBlendPowerValue { get { EnsureLoaded(); return _current.RegionBlendPower;  } set { EnsureLoaded(); _current.RegionBlendPower = Clamp(value, 0.5f, 8f); } }
        public static bool RegionSmoothEdgesValue { get { EnsureLoaded(); return _current.RegionSmoothEdges; } set { EnsureLoaded(); _current.RegionSmoothEdges = value; } }
        public static int RingPeriodValue         { get { EnsureLoaded(); return _current.RingPeriod;        } set { EnsureLoaded(); _current.RingPeriod = Clamp(value, 1, 2_000_000); } }
        public static bool MirrorRepeatValue      { get { EnsureLoaded(); return _current.MirrorRepeat;      } set { EnsureLoaded(); _current.MirrorRepeat = value; } }
        public static int TransitionWidthValue    { get { EnsureLoaded(); return _current.TransitionWidth;   } set { EnsureLoaded(); _current.TransitionWidth = Clamp(value, 0, 50_000); } }
        public static int WorldBlendRadiusValue   { get { EnsureLoaded(); return _current.WorldBlendRadius;  } set { EnsureLoaded(); _current.WorldBlendRadius = Clamp(value, 1, 2_000_000); } }

        #endregion

        #region [Bedrock]

        public static int Bedrock_CoordDivValue { get { EnsureLoaded(); return _current.Bedrock_CoordDiv; } set { EnsureLoaded(); _current.Bedrock_CoordDiv = Clamp(value, 1, 4096); } }
        public static int Bedrock_MinLevelValue { get { EnsureLoaded(); return _current.Bedrock_MinLevel; } set { EnsureLoaded(); _current.Bedrock_MinLevel = Clamp(value, 0, 128); } }
        public static int Bedrock_VarianceValue { get { EnsureLoaded(); return _current.Bedrock_Variance; } set { EnsureLoaded(); _current.Bedrock_Variance = Clamp(value, 0, 128); } }

        #endregion

        #region [CrashSite]

        // NOTE:
        // - Several static-facade clamps differ from the LoadOrCreate clamps (which follow depositor vanilla ranges).
        // - This is not a behavior change here; just something to keep in mind when tuning via UI:
        //   UI edits go through these Value setters and may clamp more tightly than disk load.

        public static float Crash_WorldScaleValue              { get { EnsureLoaded(); return _current.Crash_WorldScale;              } set { EnsureLoaded(); _current.Crash_WorldScale = Clamp(value, 0.000001f, 1f); } }

        // NOTE: Clamp range here is -1..1, but LoadOrCreate clamps NoiseThreshold to 0..1.
        public static float Crash_NoiseThresholdValue          { get { EnsureLoaded(); return _current.Crash_NoiseThreshold;          } set { EnsureLoaded(); _current.Crash_NoiseThreshold = Clamp(value, -1f, 1f); } }

        // NOTE: These Y clamps are narrower than LoadOrCreate (which allows up to 1024/4096).
        public static int Crash_GroundPlaneValue               { get { EnsureLoaded(); return _current.Crash_GroundPlane;             } set { EnsureLoaded(); _current.Crash_GroundPlane = Clamp(value, 0, 127); } }
        public static int Crash_StartYValue                    { get { EnsureLoaded(); return _current.Crash_StartY;                  } set { EnsureLoaded(); _current.Crash_StartY = Clamp(value, 0, 127); } }
        public static int Crash_EndYExclusiveValue             { get { EnsureLoaded(); return _current.Crash_EndYExclusive;           } set { EnsureLoaded(); _current.Crash_EndYExclusive = Clamp(value, 1, 128); } }

        public static float Crash_CraterDepthMulValue          { get { EnsureLoaded(); return _current.Crash_CraterDepthMul;          } set { EnsureLoaded(); _current.Crash_CraterDepthMul = Clamp(value, 0f, 100000f); } }
        public static bool Crash_EnableMoundValue              { get { EnsureLoaded(); return _current.Crash_EnableMound;             } set { EnsureLoaded(); _current.Crash_EnableMound = value; } }

        // NOTE: Clamp range here is -1..1, but LoadOrCreate clamps MoundThreshold to 0..1.
        public static float Crash_MoundThresholdValue          { get { EnsureLoaded(); return _current.Crash_MoundThreshold;          } set { EnsureLoaded(); _current.Crash_MoundThreshold = Clamp(value, -1f, 1f); } }

        public static float Crash_MoundHeightMulValue          { get { EnsureLoaded(); return _current.Crash_MoundHeightMul;          } set { EnsureLoaded(); _current.Crash_MoundHeightMul = Clamp(value, 0f, 100000f); } }

        // NOTE: Clamp range here is 0..128, but LoadOrCreate allows up to 4096.
        public static int Crash_CarvePaddingValue              { get { EnsureLoaded(); return _current.Crash_CarvePadding;            } set { EnsureLoaded(); _current.Crash_CarvePadding = Clamp(value, 0, 128); } }

        public static bool Crash_ProtectBloodStoneValue        { get { EnsureLoaded(); return _current.Crash_ProtectBloodStone;       } set { EnsureLoaded(); _current.Crash_ProtectBloodStone = value; } }

        // Slime knobs
        public static bool Crash_EnableSlimeValue              { get { EnsureLoaded(); return _current.Crash_EnableSlime;             } set { EnsureLoaded(); _current.Crash_EnableSlime = value; } }
        public static int Crash_SlimePosOffsetValue            { get { EnsureLoaded(); return _current.Crash_SlimePosOffset;          } set { EnsureLoaded(); _current.Crash_SlimePosOffset = Clamp(value, -100000, 100000); } }
        public static int Crash_SlimeCoarseDivValue            { get { EnsureLoaded(); return _current.Crash_SlimeCoarseDiv;          } set { EnsureLoaded(); _current.Crash_SlimeCoarseDiv = Clamp(value, 1, 4096); } }
        public static int Crash_SlimeAdjustCenterValue         { get { EnsureLoaded(); return _current.Crash_SlimeAdjustCenter;       } set { EnsureLoaded(); _current.Crash_SlimeAdjustCenter = Clamp(value, -100000, 100000); } }
        public static int Crash_SlimeAdjustDivValue            { get { EnsureLoaded(); return _current.Crash_SlimeAdjustDiv;          } set { EnsureLoaded(); _current.Crash_SlimeAdjustDiv = Clamp(value, 1, 4096); } }
        public static int Crash_SlimeThresholdBaseValue        { get { EnsureLoaded(); return _current.Crash_SlimeThresholdBase;      } set { EnsureLoaded(); _current.Crash_SlimeThresholdBase = Clamp(value, -100000, 100000); } }
        public static float Crash_SlimeBlendToIntBlendMulValue { get { EnsureLoaded(); return _current.Crash_SlimeBlendToIntBlendMul; } set { EnsureLoaded(); _current.Crash_SlimeBlendToIntBlendMul = Clamp(value, 0f, 100000f); } }
        public static float Crash_SlimeThresholdBlendMulValue  { get { EnsureLoaded(); return _current.Crash_SlimeThresholdBlendMul;  } set { EnsureLoaded(); _current.Crash_SlimeThresholdBlendMul = Clamp(value, -100000f, 100000f); } }
        public static int Crash_SlimeTopPaddingValue           { get { EnsureLoaded(); return _current.Crash_SlimeTopPadding;         } set { EnsureLoaded(); _current.Crash_SlimeTopPadding = Clamp(value, 0, 128); } }

        #endregion

        #region [Ore]

        public static int Ore_BlendRadiusValue                   { get { EnsureLoaded(); return _current.Ore_BlendRadius;                   } set { EnsureLoaded(); _current.Ore_BlendRadius = Clamp(value, 0, 2_000_000); } }
        public static float Ore_BlendMulValue                    { get { EnsureLoaded(); return _current.Ore_BlendMul;                      } set { EnsureLoaded(); _current.Ore_BlendMul = Clamp(value, -1000f, 1000f); } }
        public static float Ore_BlendAddValue                    { get { EnsureLoaded(); return _current.Ore_BlendAdd;                      } set { EnsureLoaded(); _current.Ore_BlendAdd = Clamp(value, -1000f, 1000f); } }

        public static int Ore_MaxYValue                          { get { EnsureLoaded(); return _current.Ore_MaxY;                          } set { EnsureLoaded(); _current.Ore_MaxY = Clamp(value, 1, 256); } }
        public static float Ore_BlendToIntBlendMulValue          { get { EnsureLoaded(); return _current.Ore_BlendToIntBlendMul;            } set { EnsureLoaded(); _current.Ore_BlendToIntBlendMul = Clamp(value, 0f, 1000f); } }

        public static int Ore_NoiseAdjustCenterValue             { get { EnsureLoaded(); return _current.Ore_NoiseAdjustCenter;             } set { EnsureLoaded(); _current.Ore_NoiseAdjustCenter = Clamp(value, -100000, 100000); } }
        public static int Ore_NoiseAdjustDivValue                { get { EnsureLoaded(); return _current.Ore_NoiseAdjustDiv;                } set { EnsureLoaded(); _current.Ore_NoiseAdjustDiv = Clamp(value, 1, 4096); } }

        public static int Ore_CoalCoarseDivValue                 { get { EnsureLoaded(); return _current.Ore_CoalCoarseDiv;                 } set { EnsureLoaded(); _current.Ore_CoalCoarseDiv = Clamp(value, 1, 4096); } }
        public static int Ore_CoalThresholdBaseValue             { get { EnsureLoaded(); return _current.Ore_CoalThresholdBase;             } set { EnsureLoaded(); _current.Ore_CoalThresholdBase = Clamp(value, -100000, 100000); } }
        public static int Ore_CopperThresholdOffsetValue         { get { EnsureLoaded(); return _current.Ore_CopperThresholdOffset;         } set { EnsureLoaded(); _current.Ore_CopperThresholdOffset = Clamp(value, -100000, 100000); } }

        public static int Ore_IronOffsetValue                    { get { EnsureLoaded(); return _current.Ore_IronOffset;                    } set { EnsureLoaded(); _current.Ore_IronOffset = Clamp(value, -100000, 100000); } }
        public static int Ore_IronCoarseDivValue                 { get { EnsureLoaded(); return _current.Ore_IronCoarseDiv;                 } set { EnsureLoaded(); _current.Ore_IronCoarseDiv = Clamp(value, 1, 4096); } }
        public static int Ore_IronThresholdBaseValue             { get { EnsureLoaded(); return _current.Ore_IronThresholdBase;             } set { EnsureLoaded(); _current.Ore_IronThresholdBase = Clamp(value, -100000, 100000); } }
        public static int Ore_GoldThresholdOffsetValue           { get { EnsureLoaded(); return _current.Ore_GoldThresholdOffset;           } set { EnsureLoaded(); _current.Ore_GoldThresholdOffset = Clamp(value, -100000, 100000); } }
        public static int Ore_GoldMaxYValue                      { get { EnsureLoaded(); return _current.Ore_GoldMaxY;                      } set { EnsureLoaded(); _current.Ore_GoldMaxY = Clamp(value, 0, 256); } }

        public static int Ore_DeepPassMaxYValue                  { get { EnsureLoaded(); return _current.Ore_DeepPassMaxY;                  } set { EnsureLoaded(); _current.Ore_DeepPassMaxY = Clamp(value, 0, 256); } }
        public static int Ore_DiamondOffsetValue                 { get { EnsureLoaded(); return _current.Ore_DiamondOffset;                 } set { EnsureLoaded(); _current.Ore_DiamondOffset = Clamp(value, -100000, 100000); } }
        public static int Ore_DiamondCoarseDivValue              { get { EnsureLoaded(); return _current.Ore_DiamondCoarseDiv;              } set { EnsureLoaded(); _current.Ore_DiamondCoarseDiv = Clamp(value, 1, 4096); } }
        public static int Ore_LavaThresholdBaseValue             { get { EnsureLoaded(); return _current.Ore_LavaThresholdBase;             } set { EnsureLoaded(); _current.Ore_LavaThresholdBase = Clamp(value, -100000, 100000); } }
        public static int Ore_DiamondThresholdOffsetValue        { get { EnsureLoaded(); return _current.Ore_DiamondThresholdOffset;        } set { EnsureLoaded(); _current.Ore_DiamondThresholdOffset = Clamp(value, -100000, 100000); } }
        public static int Ore_DiamondMaxYValue                   { get { EnsureLoaded(); return _current.Ore_DiamondMaxY;                   } set { EnsureLoaded(); _current.Ore_DiamondMaxY = Clamp(value, 0, 256); } }

        // Loot knobs.
        public static bool Ore_LootEnabledValue                  { get { EnsureLoaded(); return _current.Ore_LootEnabled;                   } set { EnsureLoaded(); _current.Ore_LootEnabled = value; } }
        public static bool Ore_LootOnNonRockBlocksValue          { get { EnsureLoaded(); return _current.Ore_LootOnNonRockBlocks;           } set { EnsureLoaded(); _current.Ore_LootOnNonRockBlocks = value; } }
        public static int Ore_LootSandSnowMaxYValue              { get { EnsureLoaded(); return _current.Ore_LootSandSnowMaxY;              } set { EnsureLoaded(); _current.Ore_LootSandSnowMaxY = Clamp(value, 0, 256); } }

        public static int Ore_LootOffsetValue                    { get { EnsureLoaded(); return _current.Ore_LootOffset;                    } set { EnsureLoaded(); _current.Ore_LootOffset = Clamp(value, -100000, 100000); } }
        public static int Ore_LootCoarseDivValue                 { get { EnsureLoaded(); return _current.Ore_LootCoarseDiv;                 } set { EnsureLoaded(); _current.Ore_LootCoarseDiv = Clamp(value, 1, 4096); } }
        public static int Ore_LootFineDivValue                   { get { EnsureLoaded(); return _current.Ore_LootFineDiv;                   } set { EnsureLoaded(); _current.Ore_LootFineDiv = Clamp(value, 1, 4096); } }

        // Survival loot thresholds.
        public static int Ore_LootSurvivalMainThresholdValue     { get { EnsureLoaded(); return _current.Ore_LootSurvivalMainThreshold;     } set { EnsureLoaded(); _current.Ore_LootSurvivalMainThreshold = Clamp(value, -100000, 100000); } }
        public static int Ore_LootSurvivalLuckyThresholdValue    { get { EnsureLoaded(); return _current.Ore_LootSurvivalLuckyThreshold;    } set { EnsureLoaded(); _current.Ore_LootSurvivalLuckyThreshold = Clamp(value, -100000, 100000); } }
        public static int Ore_LootSurvivalRegularThresholdValue  { get { EnsureLoaded(); return _current.Ore_LootSurvivalRegularThreshold;  } set { EnsureLoaded(); _current.Ore_LootSurvivalRegularThreshold = Clamp(value, -100000, 100000); } }
        public static int Ore_LootLuckyBandMinYValue             { get { EnsureLoaded(); return _current.Ore_LootLuckyBandMinY;             } set { EnsureLoaded(); _current.Ore_LootLuckyBandMinY = Clamp(value, 0, 256); } }
        public static int Ore_LootLuckyBandMaxYStartValue        { get { EnsureLoaded(); return _current.Ore_LootLuckyBandMaxYStart;        } set { EnsureLoaded(); _current.Ore_LootLuckyBandMaxYStart = Clamp(value, 0, 256); } }

        // Scavenger loot thresholds.
        public static int Ore_LootScavengerTargetModValue        { get { EnsureLoaded(); return _current.Ore_LootScavengerTargetMod;        } set { EnsureLoaded(); _current.Ore_LootScavengerTargetMod = Clamp(value, 0, 32); } }
        public static int Ore_LootScavengerMainThresholdValue    { get { EnsureLoaded(); return _current.Ore_LootScavengerMainThreshold;    } set { EnsureLoaded(); _current.Ore_LootScavengerMainThreshold = Clamp(value, -100000, 100000); } }
        public static int Ore_LootScavengerLuckyThresholdValue   { get { EnsureLoaded(); return _current.Ore_LootScavengerLuckyThreshold;   } set { EnsureLoaded(); _current.Ore_LootScavengerLuckyThreshold = Clamp(value, -100000, 100000); } }
        public static int Ore_LootScavengerLuckyExtraPerModValue { get { EnsureLoaded(); return _current.Ore_LootScavengerLuckyExtraPerMod; } set { EnsureLoaded(); _current.Ore_LootScavengerLuckyExtraPerMod = Clamp(value, 0, 128); } }
        public static int Ore_LootScavengerRegularThresholdValue { get { EnsureLoaded(); return _current.Ore_LootScavengerRegularThreshold; } set { EnsureLoaded(); _current.Ore_LootScavengerRegularThreshold = Clamp(value, -100000, 100000); } }

        #endregion

        #region [Trees]

        public static float Tree_ScaleValue          { get { EnsureLoaded(); return _current.Tree_TreeScale;        } set { EnsureLoaded(); _current.Tree_TreeScale = Clamp(value, 0.01f, 4f); } }
        public static float Tree_ThresholdValue      { get { EnsureLoaded(); return _current.Tree_TreeThreshold;    } set { EnsureLoaded(); _current.Tree_TreeThreshold = Clamp(value, 0f, 1f); } }
        public static int Tree_BaseTrunkHeightValue  { get { EnsureLoaded(); return _current.Tree_BaseTrunkHeight;  } set { EnsureLoaded(); _current.Tree_BaseTrunkHeight = Clamp(value, 1, 64); } }
        public static float Tree_HeightVarMulValue   { get { EnsureLoaded(); return _current.Tree_HeightVarMul;     } set { EnsureLoaded(); _current.Tree_HeightVarMul = Clamp(value, 0f, 64f); } }
        public static int Tree_LeafRadiusValue       { get { EnsureLoaded(); return _current.Tree_LeafRadius;       } set { EnsureLoaded(); _current.Tree_LeafRadius = Clamp(value, 0, 17); } }
        public static float Tree_LeafNoiseScaleValue { get { EnsureLoaded(); return _current.Tree_LeafNoiseScale;   } set { EnsureLoaded(); _current.Tree_LeafNoiseScale = Clamp(value, 0.01f, 4f); } }
        public static float Tree_LeafCutoffValue     { get { EnsureLoaded(); return _current.Tree_LeafCutoff;       } set { EnsureLoaded(); _current.Tree_LeafCutoff = Clamp(value, -1f, 2f); } }
        public static int Tree_GroundScanStartYValue { get { EnsureLoaded(); return _current.Tree_GroundScanStartY; } set { EnsureLoaded(); _current.Tree_GroundScanStartY = Clamp(value, 0, 127); } }
        public static int Tree_GroundScanMinYValue   { get { EnsureLoaded(); return _current.Tree_GroundScanMinY;   } set { EnsureLoaded(); _current.Tree_GroundScanMinY = Clamp(value, 0, 127); } }
        public static int Tree_MinGroundHeightValue  { get { EnsureLoaded(); return _current.Tree_MinGroundHeight;  } set { EnsureLoaded(); _current.Tree_MinGroundHeight = Clamp(value, 0, 127); } }

        #endregion

        #region [Overlays]

        public static bool EnableCrashSitesValue         { get { EnsureLoaded(); return _current.EnableCrashSites;         } set { EnsureLoaded(); _current.EnableCrashSites = value; } }
        public static bool EnableCavesValue              { get { EnsureLoaded(); return _current.EnableCaves;              } set { EnsureLoaded(); _current.EnableCaves = value; } }
        public static bool EnableOreValue                { get { EnsureLoaded(); return _current.EnableOre;                } set { EnsureLoaded(); _current.EnableOre = value; } }
        public static bool EnableBiomeOverlayGuardsValue { get { EnsureLoaded(); return _current.EnableBiomeOverlayGuards; } set { EnsureLoaded(); _current.EnableBiomeOverlayGuards = value; } }
        public static bool EnableHellCeilingValue        { get { EnsureLoaded(); return _current.EnableHellCeiling;        } set { EnsureLoaded(); _current.EnableHellCeiling = value; } }
        public static bool EnableHellValue               { get { EnsureLoaded(); return _current.EnableHell;               } set { EnsureLoaded(); _current.EnableHell = value; } }
        public static bool EnableBedrockValue            { get { EnsureLoaded(); return _current.EnableBedrock;            } set { EnsureLoaded(); _current.EnableBedrock = value; } }
        public static bool EnableOriginValue             { get { EnsureLoaded(); return _current.EnableOrigin;             } set { EnsureLoaded(); _current.EnableOrigin = value; } }
        public static bool EnableWaterValue              { get { EnsureLoaded(); return _current.EnableWater;              } set { EnsureLoaded(); _current.EnableWater = value; } }
        public static bool EnableTreesValue              { get { EnsureLoaded(); return _current.EnableTrees;              } set { EnsureLoaded(); _current.EnableTrees = value; } }

        #endregion

        #region [Spawners]

        public static bool EnableCaveSpawnersValue     { get { EnsureLoaded(); return _current.EnableCaveSpawners;     } set { EnsureLoaded(); _current.EnableCaveSpawners = value; } }
        public static bool EnableHellBossSpawnersValue { get { EnsureLoaded(); return _current.EnableHellBossSpawners; } set { EnsureLoaded(); _current.EnableHellBossSpawners = value; } }

        #endregion

        #region [RingsRandom]

        public static bool RandomRingsVaryByPeriodValue               { get { EnsureLoaded(); return _current.RandomRingsVaryByPeriod;               } set { EnsureLoaded(); _current.RandomRingsVaryByPeriod = value;               } }
        public static bool AutoIncludeCustomBiomesForRandomRingsValue { get { EnsureLoaded(); return _current.AutoIncludeCustomBiomesForRandomRings; } set { EnsureLoaded(); _current.AutoIncludeCustomBiomesForRandomRings = value; } }

        /// <summary>
        /// Mutable list (UI can edit entries). Duplicates are allowed and act as weights.
        /// </summary>
        public static List<string> RandomRingBiomeChoicesValue
        {
            get
            {
                EnsureLoaded();
                if (_current.RandomRingBiomeChoices == null || _current.RandomRingBiomeChoices.Count == 0)
                    _current.RandomRingBiomeChoices = new List<string>(DefaultRandomRingBiomeChoices());
                return _current.RandomRingBiomeChoices;
            }
        }
        #endregion

        #region [SingleBiome]

        /// <summary>Static facade for <see cref="SingleSurfaceBiome"/> with null/blank fallback.</summary>
        public static string SingleSurfaceBiomeValue
        {
            get
            {
                EnsureLoaded();
                return string.IsNullOrWhiteSpace(_current.SingleSurfaceBiome)
                    ? "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome"
                    : _current.SingleSurfaceBiome;
            }
            set
            {
                EnsureLoaded();
                _current.SingleSurfaceBiome = string.IsNullOrWhiteSpace(value)
                    ? "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome"
                    : value.Trim();
            }
        }
        #endregion

        #region [RandomRegions]

        public static bool AutoIncludeCustomBiomesForRandomRegionsValue { get { EnsureLoaded(); return _current.AutoIncludeCustomBiomesForRandomRegions; } set { EnsureLoaded(); _current.AutoIncludeCustomBiomesForRandomRegions = value; } }

        #endregion

        #endregion

        /// <summary>
        /// Mutable list (UI can edit entries). Call WGConfig.Save() after editing so it persists.
        /// </summary>
        public static List<RingCore> RingsValue
        {
            get
            {
                EnsureLoaded();
                if (_current.Rings == null) _current.Rings = new List<RingCore>(VanillaRings());
                return _current.Rings;
            }
        }

        #region Save / Reset

        /// <summary>
        /// Writes the current in-memory config to disk (overwrites file).
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Ensures Rings are present and sorted before writing.
        /// - Optionally auto-includes custom biome names into the relevant bags before writing.
        /// </remarks>
        public static void Save()
        {
            EnsureLoaded();
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Keep rings sane.
                if (_current.Rings == null || _current.Rings.Count == 0)
                    _current.Rings = new List<RingCore>(VanillaRings());
                _current.Rings.Sort((a, b) => a.EndRadius.CompareTo(b.EndRadius));

                if (_current.AutoIncludeCustomBiomesForRandomRings)
                {
                    CustomBiomeRegistry.Refresh();
                    var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();

                    if (_current.RandomRingBiomeChoices == null)
                        _current.RandomRingBiomeChoices = new List<string>(DefaultRandomRingBiomeChoices());

                    for (int i = 0; i < custom.Length; i++)
                    {
                        string t = custom[i];
                        bool has = _current.RandomRingBiomeChoices.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
                        if (!has)
                            _current.RandomRingBiomeChoices.Add(t);
                    }
                }

                if (_current.AutoIncludeCustomBiomesForRandomRegions)
                {
                    CustomBiomeRegistry.Refresh();
                    var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();

                    if (_current.RandomSurfaceBiomeChoices == null)
                        _current.RandomSurfaceBiomeChoices = new List<string>(DefaultRandomSurfaceBiomeChoices());

                    for (int i = 0; i < custom.Length; i++)
                    {
                        string t = custom[i];
                        bool has = _current.RandomSurfaceBiomeChoices.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
                        if (!has)
                            _current.RandomSurfaceBiomeChoices.Add(t);
                    }
                }

                File.WriteAllLines(ConfigPath, BuildIniLines(_current));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WGConfig] Save failed: {ex.Message}.");
            }
        }

        /// <summary>
        /// Resets to vanilla defaults (in memory) and persists to disk.
        /// </summary>
        public static void ResetToVanilla()
        {
            EnsureLoaded();
            _current.ResetToVanillaInMemory();
            Save();
        }
        #endregion

        #region Snapshot / Runtime Application Helpers

        /// <summary>
        /// Snapshot object passed into the builder.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - This is where int-backed config values are converted into strongly-typed enums.
        /// - Lists are cloned so the builder receives a stable view of settings (no UI mutation mid-build).
        /// </remarks>
        public static WorldGenPlusSettings Snapshot()
        {
            EnsureLoaded();

            var s = new WorldGenPlusSettings
            {
                #region [WorldGenPlus]

                Enabled = _current.Enabled,

                #endregion

                #region [Seed]

                SeedOverrideEnabled = _current.SeedOverrideEnabled,
                SeedOverride        = _current.SeedOverride,

                #endregion

                #region [Surface]

                SurfaceMode = (WorldGenPlusBuilder.SurfaceGenMode)Clamp(_current.SurfaceMode, 0, 3),

                #endregion

                #region [Rings]

                RingPeriod        = _current.RingPeriod,
                MirrorRepeat      = _current.MirrorRepeat,
                TransitionWidth   = _current.TransitionWidth,

                RegionCellSize    = _current.RegionCellSize,
                RegionBlendWidth  = _current.RegionBlendWidth,
                RegionBlendPower  = _current.RegionBlendPower,

                RegionSmoothEdges = _current.RegionSmoothEdges,
                WorldBlendRadius  = _current.WorldBlendRadius,

                Rings = new List<RingCore>(_current.Rings ?? new List<RingCore>(VanillaRings())),

                #endregion

                #region [RingsRandom]

                RandomRingsVaryByPeriod = _current.RandomRingsVaryByPeriod,
                RandomRingBiomeChoices = new List<string>(
                    _current.RandomRingBiomeChoices ?? new List<string>(DefaultRandomRingBiomeChoices())
                ),
                #endregion

                #region [SingleBiome]

                SingleSurfaceBiome = string.IsNullOrWhiteSpace(_current.SingleSurfaceBiome)
                    ? "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome"
                    : _current.SingleSurfaceBiome,

                #endregion

                #region [RandomRegions]

                AutoIncludeCustomBiomesForRandomRegions = _current.AutoIncludeCustomBiomesForRandomRegions,
                RandomSurfaceBiomeChoices = new List<string>(
                    _current.RandomSurfaceBiomeChoices ?? new List<string>(DefaultRandomSurfaceBiomeChoices())
                ),

                #endregion

                #region [Overlay]

                EnableCrashSites         = _current.EnableCrashSites,
                EnableCaves              = _current.EnableCaves,
                EnableOre                = _current.EnableOre,
                EnableBiomeOverlayGuards = _current.EnableBiomeOverlayGuards,
                EnableHellCeiling        = _current.EnableHellCeiling,
                EnableHell               = _current.EnableHell,
                EnableBedrock            = _current.EnableBedrock,
                EnableOrigin             = _current.EnableOrigin,
                EnableWater              = _current.EnableWater,
                EnableTrees              = _current.EnableTrees,

                #endregion

                #region [Spawners]

                EnableCaveSpawners     = _current.EnableCaveSpawners,
                EnableHellBossSpawners = _current.EnableHellBossSpawners,

                #endregion

                #region [Bedrock]

                Bedrock_CoordDiv = _current.Bedrock_CoordDiv,
                Bedrock_MinLevel = _current.Bedrock_MinLevel,
                Bedrock_Variance = _current.Bedrock_Variance,

                #endregion

                #region [CrashSite]

                Crash_WorldScale              = _current.Crash_WorldScale,
                Crash_NoiseThreshold          = _current.Crash_NoiseThreshold,
                Crash_GroundPlane             = _current.Crash_GroundPlane,
                Crash_StartY                  = _current.Crash_StartY,
                Crash_EndYExclusive           = _current.Crash_EndYExclusive,
                Crash_CraterDepthMul          = _current.Crash_CraterDepthMul,
                Crash_EnableMound             = _current.Crash_EnableMound,
                Crash_MoundThreshold          = _current.Crash_MoundThreshold,
                Crash_MoundHeightMul          = _current.Crash_MoundHeightMul,
                Crash_CarvePadding            = _current.Crash_CarvePadding,
                Crash_ProtectBloodStone       = _current.Crash_ProtectBloodStone,
                Crash_EnableSlime             = _current.Crash_EnableSlime,
                Crash_SlimePosOffset          = _current.Crash_SlimePosOffset,
                Crash_SlimeCoarseDiv          = _current.Crash_SlimeCoarseDiv,
                Crash_SlimeAdjustCenter       = _current.Crash_SlimeAdjustCenter,
                Crash_SlimeAdjustDiv          = _current.Crash_SlimeAdjustDiv,
                Crash_SlimeThresholdBase      = _current.Crash_SlimeThresholdBase,
                Crash_SlimeBlendToIntBlendMul = _current.Crash_SlimeBlendToIntBlendMul,
                Crash_SlimeThresholdBlendMul  = _current.Crash_SlimeThresholdBlendMul,
                Crash_SlimeTopPadding         = _current.Crash_SlimeTopPadding,

                #endregion

                #region [Ore]

                Ore_BlendRadius                   = _current.Ore_BlendRadius,
                Ore_BlendMul                      = _current.Ore_BlendMul,
                Ore_BlendAdd                      = _current.Ore_BlendAdd,

                Ore_MaxY                          = _current.Ore_MaxY,
                Ore_BlendToIntBlendMul            = _current.Ore_BlendToIntBlendMul,
                Ore_NoiseAdjustCenter             = _current.Ore_NoiseAdjustCenter,
                Ore_NoiseAdjustDiv                = _current.Ore_NoiseAdjustDiv,

                Ore_CoalCoarseDiv                 = _current.Ore_CoalCoarseDiv,
                Ore_CoalThresholdBase             = _current.Ore_CoalThresholdBase,
                Ore_CopperThresholdOffset         = _current.Ore_CopperThresholdOffset,

                Ore_IronOffset                    = _current.Ore_IronOffset,
                Ore_IronCoarseDiv                 = _current.Ore_IronCoarseDiv,
                Ore_IronThresholdBase             = _current.Ore_IronThresholdBase,
                Ore_GoldThresholdOffset           = _current.Ore_GoldThresholdOffset,
                Ore_GoldMaxY                      = _current.Ore_GoldMaxY,

                Ore_DeepPassMaxY                  = _current.Ore_DeepPassMaxY,
                Ore_DiamondOffset                 = _current.Ore_DiamondOffset,
                Ore_DiamondCoarseDiv              = _current.Ore_DiamondCoarseDiv,
                Ore_LavaThresholdBase             = _current.Ore_LavaThresholdBase,
                Ore_DiamondThresholdOffset        = _current.Ore_DiamondThresholdOffset,
                Ore_DiamondMaxY                   = _current.Ore_DiamondMaxY,

                Ore_LootEnabled                   = _current.Ore_LootEnabled,
                Ore_LootOnNonRockBlocks           = _current.Ore_LootOnNonRockBlocks,
                Ore_LootSandSnowMaxY              = _current.Ore_LootSandSnowMaxY,

                Ore_LootOffset                    = _current.Ore_LootOffset,
                Ore_LootCoarseDiv                 = _current.Ore_LootCoarseDiv,
                Ore_LootFineDiv                   = _current.Ore_LootFineDiv,

                Ore_LootSurvivalMainThreshold     = _current.Ore_LootSurvivalMainThreshold,
                Ore_LootSurvivalLuckyThreshold    = _current.Ore_LootSurvivalLuckyThreshold,
                Ore_LootSurvivalRegularThreshold  = _current.Ore_LootSurvivalRegularThreshold,
                Ore_LootLuckyBandMinY             = _current.Ore_LootLuckyBandMinY,
                Ore_LootLuckyBandMaxYStart        = _current.Ore_LootLuckyBandMaxYStart,

                Ore_LootScavengerTargetMod        = _current.Ore_LootScavengerTargetMod,
                Ore_LootScavengerMainThreshold    = _current.Ore_LootScavengerMainThreshold,
                Ore_LootScavengerLuckyThreshold   = _current.Ore_LootScavengerLuckyThreshold,
                Ore_LootScavengerLuckyExtraPerMod = _current.Ore_LootScavengerLuckyExtraPerMod,
                Ore_LootScavengerRegularThreshold = _current.Ore_LootScavengerRegularThreshold,

                #endregion

                #region [Trees]

                Tree_TreeScale        = _current.Tree_TreeScale,
                Tree_TreeThreshold    = _current.Tree_TreeThreshold,
                Tree_BaseTrunkHeight  = _current.Tree_BaseTrunkHeight,
                Tree_HeightVarMul     = _current.Tree_HeightVarMul,
                Tree_LeafRadius       = _current.Tree_LeafRadius,
                Tree_LeafNoiseScale   = _current.Tree_LeafNoiseScale,
                Tree_LeafCutoff       = _current.Tree_LeafCutoff,
                Tree_GroundScanStartY = _current.Tree_GroundScanStartY,
                Tree_GroundScanMinY   = _current.Tree_GroundScanMinY,
                Tree_MinGroundHeight  = _current.Tree_MinGroundHeight,

                #endregion
            };

            return s;
        }

        /// <summary>
        /// Best-effort seed override based on CURRENT loaded config.
        /// </summary>
        public static bool TryApplySeedOverride(object worldInfoObj)
        {
            EnsureLoaded();

            // Uncomment to make "Seed Override" tied to "Enabled".
            // if (!_current.Enabled) return false;
            if (!_current.SeedOverrideEnabled) return false;
            if (worldInfoObj == null) return false;

            try
            {
                return TrySetSeed(worldInfoObj, _current.SeedOverride);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Writes the seed onto a WorldInfo-like object via property or backing field.
        /// </summary>
        private static bool TrySetSeed(object worldInfoObj, int seed)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = worldInfoObj.GetType();

            var f = t.GetField("_seed", F);
            if (f != null && f.FieldType == typeof(int))
            {
                f.SetValue(worldInfoObj, seed);
                return true;
            }

            return false;
        }
        #endregion

        #region Network Override Helpers

        /// <summary>
        /// Copies snapshot DTO values INTO this config instance.
        /// Summary: Reverse of Snapshot(); used to apply host settings in-memory.
        /// </summary>
        private void ImportFromSettings(WorldGenPlusSettings s)
        {
            #region [WorldGenPlus]

            Enabled = s.Enabled;

            #endregion

            #region [Seed]

            SeedOverrideEnabled = s.SeedOverrideEnabled;
            SeedOverride        = s.SeedOverride;

            #endregion

            #region [Surface]

            SurfaceMode = (int)s.SurfaceMode;

            #endregion

            #region [Rings]

            RingPeriod        = s.RingPeriod;
            MirrorRepeat      = s.MirrorRepeat;
            TransitionWidth   = s.TransitionWidth;

            RegionCellSize    = s.RegionCellSize;
            RegionBlendWidth  = s.RegionBlendWidth;
            RegionBlendPower  = s.RegionBlendPower;

            WorldBlendRadius  = s.WorldBlendRadius;
            RegionSmoothEdges = s.RegionSmoothEdges;

            Rings = (s.Rings != null) ? new List<RingCore>(s.Rings) : new List<RingCore>(VanillaRings());
            Rings.Sort((a, b) => a.EndRadius.CompareTo(b.EndRadius));

            #endregion

            #region [RingsRandom]

            RandomRingsVaryByPeriod = s.RandomRingsVaryByPeriod;
            RandomRingBiomeChoices  = (s.RandomRingBiomeChoices != null)
                ? new List<string>(s.RandomRingBiomeChoices)
                : new List<string>(DefaultRandomRingBiomeChoices());

            #endregion

            #region [SingleBiome]

            SingleSurfaceBiome = string.IsNullOrWhiteSpace(s.SingleSurfaceBiome)
                ? "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome"
                : s.SingleSurfaceBiome;

            #endregion

            #region [RandomRegions]

            AutoIncludeCustomBiomesForRandomRegions = s.AutoIncludeCustomBiomesForRandomRegions;
            RandomSurfaceBiomeChoices = (s.RandomSurfaceBiomeChoices != null)
                ? new List<string>(s.RandomSurfaceBiomeChoices)
                : new List<string>(DefaultRandomSurfaceBiomeChoices());

            #endregion

            #region [Overlays]

            EnableCrashSites         = s.EnableCrashSites;
            EnableCaves              = s.EnableCaves;
            EnableOre                = s.EnableOre;
            EnableBiomeOverlayGuards = s.EnableBiomeOverlayGuards;
            EnableHellCeiling        = s.EnableHellCeiling;
            EnableHell               = s.EnableHell;
            EnableBedrock            = s.EnableBedrock;
            EnableOrigin             = s.EnableOrigin;
            EnableWater              = s.EnableWater;
            EnableTrees              = s.EnableTrees;

            #endregion

            #region [Spawners]

            EnableCaveSpawners     = s.EnableCaveSpawners;
            EnableHellBossSpawners = s.EnableHellBossSpawners;

            #endregion

            #region [Bedrock]

            Bedrock_CoordDiv = s.Bedrock_CoordDiv;
            Bedrock_MinLevel = s.Bedrock_MinLevel;
            Bedrock_Variance = s.Bedrock_Variance;

            #endregion

            #region [CrashSite]

            Crash_WorldScale              = s.Crash_WorldScale;
            Crash_NoiseThreshold          = s.Crash_NoiseThreshold;

            Crash_GroundPlane             = s.Crash_GroundPlane;
            Crash_StartY                  = s.Crash_StartY;
            Crash_EndYExclusive           = s.Crash_EndYExclusive;

            Crash_CraterDepthMul          = s.Crash_CraterDepthMul;
            Crash_EnableMound             = s.Crash_EnableMound;
            Crash_MoundThreshold          = s.Crash_MoundThreshold;
            Crash_MoundHeightMul          = s.Crash_MoundHeightMul;

            Crash_CarvePadding            = s.Crash_CarvePadding;
            Crash_ProtectBloodStone       = s.Crash_ProtectBloodStone;

            Crash_EnableSlime             = s.Crash_EnableSlime;
            Crash_SlimePosOffset          = s.Crash_SlimePosOffset;
            Crash_SlimeCoarseDiv          = s.Crash_SlimeCoarseDiv;
            Crash_SlimeAdjustCenter       = s.Crash_SlimeAdjustCenter;
            Crash_SlimeAdjustDiv          = s.Crash_SlimeAdjustDiv;
            Crash_SlimeThresholdBase      = s.Crash_SlimeThresholdBase;
            Crash_SlimeBlendToIntBlendMul = s.Crash_SlimeBlendToIntBlendMul;
            Crash_SlimeThresholdBlendMul  = s.Crash_SlimeThresholdBlendMul;
            Crash_SlimeTopPadding         = s.Crash_SlimeTopPadding;

            #endregion

            #region [Ore]

            Ore_BlendRadius                   = s.Ore_BlendRadius;
            Ore_BlendMul                      = s.Ore_BlendMul;
            Ore_BlendAdd                      = s.Ore_BlendAdd;

            Ore_MaxY                          = s.Ore_MaxY;
            Ore_BlendToIntBlendMul            = s.Ore_BlendToIntBlendMul;

            Ore_NoiseAdjustCenter             = s.Ore_NoiseAdjustCenter;
            Ore_NoiseAdjustDiv                = s.Ore_NoiseAdjustDiv;

            Ore_CoalCoarseDiv                 = s.Ore_CoalCoarseDiv;
            Ore_CoalThresholdBase             = s.Ore_CoalThresholdBase;
            Ore_CopperThresholdOffset         = s.Ore_CopperThresholdOffset;

            Ore_IronOffset                    = s.Ore_IronOffset;
            Ore_IronCoarseDiv                 = s.Ore_IronCoarseDiv;
            Ore_IronThresholdBase             = s.Ore_IronThresholdBase;
            Ore_GoldThresholdOffset           = s.Ore_GoldThresholdOffset;
            Ore_GoldMaxY                      = s.Ore_GoldMaxY;

            Ore_DeepPassMaxY                  = s.Ore_DeepPassMaxY;
            Ore_DiamondOffset                 = s.Ore_DiamondOffset;
            Ore_DiamondCoarseDiv              = s.Ore_DiamondCoarseDiv;
            Ore_LavaThresholdBase             = s.Ore_LavaThresholdBase;
            Ore_DiamondThresholdOffset        = s.Ore_DiamondThresholdOffset;
            Ore_DiamondMaxY                   = s.Ore_DiamondMaxY;

            Ore_LootEnabled                   = s.Ore_LootEnabled;
            Ore_LootOnNonRockBlocks           = s.Ore_LootOnNonRockBlocks;
            Ore_LootSandSnowMaxY              = s.Ore_LootSandSnowMaxY;

            Ore_LootOffset                    = s.Ore_LootOffset;
            Ore_LootCoarseDiv                 = s.Ore_LootCoarseDiv;
            Ore_LootFineDiv                   = s.Ore_LootFineDiv;

            Ore_LootSurvivalMainThreshold     = s.Ore_LootSurvivalMainThreshold;
            Ore_LootSurvivalLuckyThreshold    = s.Ore_LootSurvivalLuckyThreshold;
            Ore_LootSurvivalRegularThreshold  = s.Ore_LootSurvivalRegularThreshold;
            Ore_LootLuckyBandMinY             = s.Ore_LootLuckyBandMinY;
            Ore_LootLuckyBandMaxYStart        = s.Ore_LootLuckyBandMaxYStart;

            Ore_LootScavengerTargetMod        = s.Ore_LootScavengerTargetMod;
            Ore_LootScavengerMainThreshold    = s.Ore_LootScavengerMainThreshold;
            Ore_LootScavengerLuckyThreshold   = s.Ore_LootScavengerLuckyThreshold;
            Ore_LootScavengerLuckyExtraPerMod = s.Ore_LootScavengerLuckyExtraPerMod;
            Ore_LootScavengerRegularThreshold = s.Ore_LootScavengerRegularThreshold;

            #endregion

            #region [Trees]

            Tree_TreeScale        = s.Tree_TreeScale;
            Tree_TreeThreshold    = s.Tree_TreeThreshold;
            Tree_BaseTrunkHeight  = s.Tree_BaseTrunkHeight;
            Tree_HeightVarMul     = s.Tree_HeightVarMul;
            Tree_LeafRadius       = s.Tree_LeafRadius;
            Tree_LeafNoiseScale   = s.Tree_LeafNoiseScale;
            Tree_LeafCutoff       = s.Tree_LeafCutoff;
            Tree_GroundScanStartY = s.Tree_GroundScanStartY;
            Tree_GroundScanMinY   = s.Tree_GroundScanMinY;
            Tree_MinGroundHeight  = s.Tree_MinGroundHeight;

            #endregion
        }

        /// <summary>
        /// Creates a restore copy of the current config.
        /// Summary: Used so we can revert after a host override without re-reading disk.
        /// </summary>
        private WGConfig CloneShallowForRestore()
        {
            // MemberwiseClone is fine here because we replace lists below.
            var c = (WGConfig)this.MemberwiseClone();

            c.Rings = (this.Rings != null) ? new List<RingCore>(this.Rings) : null;

            c.RandomRingBiomeChoices = (this.RandomRingBiomeChoices != null)
                ? new List<string>(this.RandomRingBiomeChoices)
                : null;

            c.RandomSurfaceBiomeChoices = (this.RandomSurfaceBiomeChoices != null)
                ? new List<string>(this.RandomSurfaceBiomeChoices)
                : null;

            return c;
        }
        #endregion

        #endregion

        #region Vanilla Defaults / INI Writing

        /// <summary>
        /// Resets ONLY the current instance fields to vanilla defaults (does not write to disk).
        /// </summary>
        private void ResetToVanillaInMemory()
        {
            #region [WorldGenPlus]

            Enabled = false;

            #endregion

            #region [Seed]

            SeedOverrideEnabled = false;
            SeedOverride        = 0;

            #endregion

            #region [Multiplayer]

            Multiplayer_SyncFromHost       = true;
            Multiplayer_BroadcastToClients = true;

            #endregion

            #region [Surface]

            SurfaceMode = (int)WorldGenPlusBuilder.SurfaceGenMode.VanillaRings;

            #endregion

            #region [Rings]

            RingPeriod        = 4400;
            MirrorRepeat      = true;
            TransitionWidth   = 100;

            RegionCellSize    = 512;
            RegionBlendWidth  = 240;
            RegionBlendPower  = 3f;

            WorldBlendRadius  = 3600;
            RegionSmoothEdges = true;

            Rings = new List<RingCore>(VanillaRings());

            #endregion

            #region [RingsRandom]

            RandomRingsVaryByPeriod               = true;
            AutoIncludeCustomBiomesForRandomRings = false;
            RandomRingBiomeChoices                = new List<string>(DefaultRandomRingBiomeChoices());

            #endregion

            #region [SingleBiome]

            SingleSurfaceBiome = "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome";

            #endregion

            #region [RandomRegions]

            AutoIncludeCustomBiomesForRandomRegions = false;
            RandomSurfaceBiomeChoices               = new List<string>(DefaultRandomSurfaceBiomeChoices());

            #endregion

            #region [Overlays]

            EnableCrashSites         = true;
            EnableCaves              = true;
            EnableOre                = true;
            EnableBiomeOverlayGuards = true;
            EnableHellCeiling        = true;
            EnableHell               = true;
            EnableBedrock            = true;
            EnableOrigin             = true;
            EnableWater              = true;
            EnableTrees              = true;

            #endregion

            #region [Spawners]

            EnableCaveSpawners     = true;
            EnableHellBossSpawners = true;

            #endregion

            #region [Bedrock]

            Bedrock_CoordDiv = 1;
            Bedrock_MinLevel = 1;
            Bedrock_Variance = 3;

            #endregion

            #region [CrashSite]

            Crash_WorldScale              = 0.0046875f;
            Crash_NoiseThreshold          = 0.5f;
            Crash_GroundPlane             = 66;
            Crash_StartY                  = 20;
            Crash_EndYExclusive           = 126;
            Crash_CraterDepthMul          = 140f;
            Crash_EnableMound             = true;
            Crash_MoundThreshold          = 0.55f;
            Crash_MoundHeightMul          = 200f;
            Crash_CarvePadding            = 10;
            Crash_ProtectBloodStone       = true;
            Crash_EnableSlime             = true;
            Crash_SlimePosOffset          = 777;
            Crash_SlimeCoarseDiv          = 2;
            Crash_SlimeAdjustCenter       = 128;
            Crash_SlimeAdjustDiv          = 8;
            Crash_SlimeThresholdBase      = 265;
            Crash_SlimeBlendToIntBlendMul = 10f;
            Crash_SlimeThresholdBlendMul  = 1f;
            Crash_SlimeTopPadding         = 3;

            #endregion

            #region [Ore]

            Ore_BlendRadius                   = 0;
            Ore_BlendMul                      = 1f;
            Ore_BlendAdd                      = 0f;

            Ore_MaxY                          = 128;
            Ore_BlendToIntBlendMul            = 10f;
            Ore_NoiseAdjustCenter             = 128;
            Ore_NoiseAdjustDiv                = 8;

            Ore_CoalCoarseDiv                 = 4;
            Ore_CoalThresholdBase             = 255;
            Ore_CopperThresholdOffset         = -5;

            Ore_IronOffset                    = 1000;
            Ore_IronCoarseDiv                 = 3;
            Ore_IronThresholdBase             = 264;
            Ore_GoldThresholdOffset           = -9;
            Ore_GoldMaxY                      = 50;

            Ore_DeepPassMaxY                  = 50;
            Ore_DiamondOffset                 = 777;
            Ore_DiamondCoarseDiv              = 2;
            Ore_LavaThresholdBase             = 266;
            Ore_DiamondThresholdOffset        = -11;
            Ore_DiamondMaxY                   = 40;

            Ore_LootEnabled                   = true;
            Ore_LootOnNonRockBlocks           = true;
            Ore_LootSandSnowMaxY              = 60;

            Ore_LootOffset                    = 333;
            Ore_LootCoarseDiv                 = 5;
            Ore_LootFineDiv                   = 2;

            Ore_LootSurvivalMainThreshold     = 268;
            Ore_LootSurvivalLuckyThreshold    = 249;
            Ore_LootSurvivalRegularThreshold  = 145;
            Ore_LootLuckyBandMinY             = 55;
            Ore_LootLuckyBandMaxYStart        = 100;

            Ore_LootScavengerTargetMod        = 1;
            Ore_LootScavengerMainThreshold    = 267;
            Ore_LootScavengerLuckyThreshold   = 250;
            Ore_LootScavengerLuckyExtraPerMod = 3;
            Ore_LootScavengerRegularThreshold = 165;

            #endregion

            #region [Trees]

            Tree_TreeScale        = 0.4375f;
            Tree_TreeThreshold    = 0.6f;
            Tree_BaseTrunkHeight  = 5;
            Tree_HeightVarMul     = 9f;
            Tree_LeafRadius       = 3;
            Tree_LeafNoiseScale   = 0.5f;
            Tree_LeafCutoff       = 0.25f;
            Tree_GroundScanStartY = 124;
            Tree_GroundScanMinY   = 0;
            Tree_MinGroundHeight  = 1;

            #endregion
        }

        /// <summary>
        /// Vanilla ring layout used as fallback when no rings are provided by the user.
        /// </summary>
        private static RingCore[] VanillaRings()
        {
            return new[]
            {
                new RingCore(200,  "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome"),
                new RingCore(900,  "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome"),
                new RingCore(1600, "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome"),
                new RingCore(2300, "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome"),
                new RingCore(3000, "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome"),
                new RingCore(3600, "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome"),
            };
        }

        /// <summary>
        /// Default Random Regions biome bag (duplicates allowed and act as weights).
        /// </summary>
        private static string[] DefaultRandomSurfaceBiomeChoices()
        {
            return new[]
            {
                "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",
            };
        }

        /// <summary>
        /// Default ring-random biome bag used when a ring core biome is set to "@Random".
        /// </summary>
        private static string[] DefaultRandomRingBiomeChoices()
        {
            // "Normal biomes" by default (safe, no Decent/Hell special casing).
            return new[]
            {
                "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",
            };
        }

        /// <summary>
        /// Creates and writes a default INI file to <see cref="ConfigPath"/>.
        /// </summary>
        private static void WriteDefaultFile()
        {
            var cfg = new WGConfig();
            cfg.ResetToVanillaInMemory();

            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllLines(ConfigPath, BuildIniLines(cfg));
        }

        /// <summary>
        /// Converts a config object into INI lines for writing to disk.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - This method is the canonical serialized representation of the config.
        /// - The line strings in here directly affect the on-disk INI format.
        /// - The line "; NOTE: Set Core{i}Biome = @Random ..." is not interpolated; it is a literal "{i}" in the output.
        ///   (Left as-is per "no code changes" request.)
        /// </remarks>
        private static string[] BuildIniLines(WGConfig cfg)
        {
            var rings = (cfg.Rings == null || cfg.Rings.Count == 0)
                ? new List<RingCore>(VanillaRings())
                : cfg.Rings;

            var lines = new List<string>
            {
                "# WorldGenPlus - Configuration",
                "# Lines starting with ';' or '#' are comments.",
                "",
                "[WorldGenPlus]",
                "; Master toggle. When true, host swaps BlockTerrain's builder to WorldGenPlusBuilder.",
                "Enabled = " + (cfg.Enabled ? "true" : "false"),
                "",
                "[Seed]",
                "; Optional seed override.",
                "Enabled = " + (cfg.SeedOverrideEnabled ? "true" : "false"),
                "Value   = " + cfg.SeedOverride.ToString(CultureInfo.InvariantCulture),
                "",
                "[Multiplayer]",
                "; SyncFromHost: When true (client-side), accept host-sent world-gen settings for this session.",
                "SyncFromHost       = " + (cfg.Multiplayer_SyncFromHost ? "true" : "false"),
                "; BroadcastToClients: When true (host-side), append WGP settings to the WorldInfo join payload.",
                "BroadcastToClients = " + (cfg.Multiplayer_BroadcastToClients ? "true" : "false"),
                "",
                "[Surface]",
                "; Surface generation mode:",
                ";   0 = Vanilla Rings",
                ";   1 = Square Bands (concentric squares)",
                ";   2 = Single Biomes (one biome everywhere)",
                ";   3 = Random Regions (minecraft-like blobs)",
                "Mode              = " + cfg.SurfaceMode.ToString(CultureInfo.InvariantCulture),
                "; Region cell size in blocks (only used when Mode=3). Bigger = bigger biomes.",
                "RegionCellSize    = " + cfg.RegionCellSize.ToString(CultureInfo.InvariantCulture),
                "; Blend width at region borders in blocks (0 = hard edges).",
                "RegionBlendWidth  = " + cfg.RegionBlendWidth.ToString(CultureInfo.InvariantCulture),
                "; Blend power for multi-way blending (2..4 typical). Higher = sharper borders.",
                "RegionBlendPower  = " + cfg.RegionBlendPower.ToString(CultureInfo.InvariantCulture),
                "; When true, uses a constant-width Voronoi bisector blend for smoother region borders.",
                "RegionSmoothEdges = " + (cfg.RegionSmoothEdges ? "true" : "false"),
                "",
                "[Rings]",
                "; Period of the ring pattern (vanilla-ish: 4400).",
                "Period          = " + cfg.RingPeriod.ToString(CultureInfo.InvariantCulture),
                "; If true, repeats mirror every period (vanilla-ish behavior).",
                "MirrorRepeat    = " + (cfg.MirrorRepeat ? "true" : "false"),
                "; Width of the blend zone between ring cores (vanilla: 100).",
                "TransitionWidth = " + cfg.TransitionWidth.ToString(CultureInfo.InvariantCulture),
                "",
                "; How many core ring entries to read below:",
                "CoreCount = " + rings.Count.ToString(CultureInfo.InvariantCulture),
                "",
                "; Each core is defined by:",
                ";   Core{i}End   = <radius at end of core>",
                ";   Core{i}Biome = <full type name>",
                ""
            };

            for (int i = 0; i < rings.Count; i++)
            {
                lines.Add("; NOTE: Set Core{i}Biome = @Random to pick a biome from [RingsRandom].");
                lines.Add("Core" + i + "End   = " + rings[i].EndRadius.ToString(CultureInfo.InvariantCulture));
                lines.Add("Core" + i + "Biome = " + rings[i].BiomeType);
                lines.Add("");
            }

            // --- RingsRandom biome bag ---
            var ringBag = (cfg.RandomRingBiomeChoices == null || cfg.RandomRingBiomeChoices.Count == 0)
                ? new List<string>(DefaultRandomRingBiomeChoices())
                : cfg.RandomRingBiomeChoices;

            lines.Add("[RingsRandom]");
            lines.Add("; Used when a ring core biome is set to '@Random'.");
            lines.Add("; Duplicates allowed (weights).");
            lines.Add("; If VaryByPeriod=true, each repeated ring period gets a new random assignment.");
            lines.Add("VaryByPeriod            = " + (cfg.RandomRingsVaryByPeriod ? "true" : "false"));
            lines.Add("BiomeCount              = " + ringBag.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            for (int i = 0; i < ringBag.Count; i++)
                lines.Add("Biome" + i + " = " + ringBag[i]);
            lines.Add("");
            lines.Add("AutoIncludeCustomBiomes = " + (cfg.AutoIncludeCustomBiomesForRandomRings ? "true" : "false"));
            lines.Add("");

            // --- RandomRegions biome bag ---
            var bag = (cfg.RandomSurfaceBiomeChoices == null || cfg.RandomSurfaceBiomeChoices.Count == 0)
                ? new List<string>(DefaultRandomSurfaceBiomeChoices())
                : cfg.RandomSurfaceBiomeChoices;

            lines.Add("");
            lines.Add("[SingleBiome]");
            lines.Add("; Full biome type name used ONLY when Mode=2 (Single).");
            lines.Add("SingleSurfaceBiome      = " + (string.IsNullOrWhiteSpace(cfg.SingleSurfaceBiome) ? "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome" : cfg.SingleSurfaceBiome));
            lines.Add("");

            lines.Add("[RandomRegions]");
            lines.Add("; Biome bag used ONLY when Surface.Mode = 3 (Random Regions).");
            lines.Add("; Duplicates are allowed and act as weights:");
            lines.Add(";   More copies => that biome appears MORE often.");
            lines.Add(";   Fewer copies => that biome appears LESS often.");
            lines.Add("BiomeCount              = " + bag.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("");

            for (int i = 0; i < bag.Count; i++)
            {
                lines.Add("Biome" + i + " = " + bag[i]);
            }
            lines.Add("");
            lines.Add("AutoIncludeCustomBiomes = " + (cfg.AutoIncludeCustomBiomesForRandomRegions ? "true" : "false"));
            lines.Add("");

            lines.Add("[Overlays]");
            lines.Add("; Matches vanilla-style overlay blending radius used for hell/bedrock/origin fades.");
            lines.Add("WorldBlendRadius = " + cfg.WorldBlendRadius.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; Overlay toggles (still run after surface rings).");
            lines.Add("; Note: The 'Origin' biome is commonly referred to as \"Lantern Land\".");
            lines.Add("CrashSites  = " + (cfg.EnableCrashSites ? "true" : "false"));
            lines.Add("Caves       = " + (cfg.EnableCaves ? "true" : "false"));
            lines.Add("Ore         = " + (cfg.EnableOre ? "true" : "false"));
            lines.Add("HellCeiling = " + (cfg.EnableHellCeiling ? "true" : "false"));
            lines.Add("Hell        = " + (cfg.EnableHell ? "true" : "false"));
            lines.Add("Bedrock     = " + (cfg.EnableBedrock ? "true" : "false"));
            lines.Add("Origin      = " + (cfg.EnableOrigin ? "true" : "false"));
            lines.Add("Water       = " + (cfg.EnableWater ? "true" : "false"));
            lines.Add("Trees       = " + (cfg.EnableTrees ? "true" : "false"));
            lines.Add("");
            lines.Add("; BiomeGuards: When true, prevents certain overlays (Hell/HellCeiling/Bedrock) from running on special/test/unusual biomes.");
            lines.Add(";              When false, overlays run everywhere (may cause Hell to appear in those biomes).");
            lines.Add("BiomeGuards  = " + (cfg.EnableBiomeOverlayGuards ? "true" : "false"));
            lines.Add("");

            lines.Add("[Spawners]");
            lines.Add("; Controls whether WorldGenPlus may place new spawner blocks while generating terrain.");
            lines.Add("; Cave spawners come from the caves overlay; hell boss spawners come from the hell floor overlay.");
            lines.Add("EnableCaveSpawners     = " + (cfg.EnableCaveSpawners ? "true" : "false"));
            lines.Add("EnableHellBossSpawners = " + (cfg.EnableHellBossSpawners ? "true" : "false"));
            lines.Add("");

            lines.Add("[Bedrock]");
            lines.Add("; CustomBedrockDepositer tuning.");
            lines.Add("; These control how thick the bottom bedrock layer is and how much it varies across X/Z.");
            lines.Add("; Tip: If you see jagged/noisy thickness changes, increase CoordDiv (smoother).");
            lines.Add("; CoordDiv: Noise frequency control for bedrock thickness. Higher => smoother, slower variation.");
            lines.Add("CoordDiv = " + cfg.Bedrock_CoordDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; MinLevel: Minimum bedrock thickness in blocks (baseline thickness).");
            lines.Add("MinLevel = " + cfg.Bedrock_MinLevel.ToString(CultureInfo.InvariantCulture));
            lines.Add("; Variance: Additional thickness variation in blocks (wobble amplitude). 0 => perfectly flat thickness.");
            lines.Add("Variance = " + cfg.Bedrock_Variance.ToString(CultureInfo.InvariantCulture));
            lines.Add("");

            lines.Add("[CrashSite]");
            lines.Add("; CustomCrashSiteDepositer tuning.");
            lines.Add("; Crash sites are typically placed using a 2D noise field sampled over world X/Z, then carved in Y.");
            lines.Add("; Tip: For fewer crash sites, raise NoiseThreshold. For more crash sites, lower it.");
            lines.Add("; WorldScale: Scales world coordinates before sampling noise. Higher => larger, slower-changing placement blobs.");
            lines.Add("WorldScale              = " + cfg.Crash_WorldScale.ToString(CultureInfo.InvariantCulture));
            lines.Add("; NoiseThreshold: Minimum noise value required to spawn a crash site at a location (higher => fewer sites).");
            lines.Add("NoiseThreshold          = " + cfg.Crash_NoiseThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("; GroundPlane: Reference Y used for carving logic / crater baseline (depends on depositor implementation).");
            lines.Add("GroundPlane             = " + cfg.Crash_GroundPlane.ToString(CultureInfo.InvariantCulture));
            lines.Add("; StartY: Starting Y for vertical carving/scan (inclusive).");
            lines.Add("StartY                  = " + cfg.Crash_StartY.ToString(CultureInfo.InvariantCulture));
            lines.Add("; EndYExclusive: Ending Y for vertical carving/scan (exclusive).");
            lines.Add("EndYExclusive           = " + cfg.Crash_EndYExclusive.ToString(CultureInfo.InvariantCulture));
            lines.Add("; CraterDepthMul: Multiplies crater depth effect (higher => deeper crater cut).");
            lines.Add("CraterDepthMul          = " + cfg.Crash_CraterDepthMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; EnableMound: When true, allows a rim/mound to be built around the crater.");
            lines.Add("EnableMound             = " + (cfg.Crash_EnableMound ? "true" : "false"));
            lines.Add("; MoundThreshold: Noise cutoff controlling when/where mounds form (higher => fewer mound placements).");
            lines.Add("MoundThreshold          = " + cfg.Crash_MoundThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("; MoundHeightMul: Height multiplier for mound/rim (higher => taller mound).");
            lines.Add("MoundHeightMul          = " + cfg.Crash_MoundHeightMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; CarvePadding: Extra padding around crater carve bounds (helps avoid harsh edges / leaves breathing room).");
            lines.Add("CarvePadding            = " + cfg.Crash_CarvePadding.ToString(CultureInfo.InvariantCulture));
            lines.Add("; ProtectBloodStone: When true, avoids carving/overwriting BloodStone blocks (safety guard).");
            lines.Add("ProtectBloodStone       = " + (cfg.Crash_ProtectBloodStone ? "true" : "false"));
            lines.Add("; EnableSlime: When true, applies a slime/ooze layer pass in the crash site area (if depositor supports it).");
            lines.Add("EnableSlime             = " + (cfg.Crash_EnableSlime ? "true" : "false"));
            lines.Add("; SlimePosOffset: Shifts slime noise sampling position (useful to decorrelate slime from crater noise).");
            lines.Add("SlimePosOffset          = " + cfg.Crash_SlimePosOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeCoarseDiv: Coarse noise frequency divisor for slime field. Higher => smoother, larger blobs.");
            lines.Add("SlimeCoarseDiv          = " + cfg.Crash_SlimeCoarseDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeAdjustCenter: Center point for any blend/adjust curve applied to slime thresholding.");
            lines.Add("SlimeAdjustCenter       = " + cfg.Crash_SlimeAdjustCenter.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeAdjustDiv: Width/divisor for slime adjust curve. Higher => gentler adjustment.");
            lines.Add("SlimeAdjustDiv          = " + cfg.Crash_SlimeAdjustDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeThresholdBase: Base threshold for slime placement (higher => less slime).");
            lines.Add("SlimeThresholdBase      = " + cfg.Crash_SlimeThresholdBase.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeBlendToIntBlendMul: Scales influence of ring/biome blend on slime thresholding (higher => more blend influence).");
            lines.Add("SlimeBlendToIntBlendMul = " + cfg.Crash_SlimeBlendToIntBlendMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeThresholdBlendMul: Multiplier for how strongly blend modifies the slime threshold (higher => stronger modulation).");
            lines.Add("SlimeThresholdBlendMul  = " + cfg.Crash_SlimeThresholdBlendMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; SlimeTopPadding: Prevents slime from appearing too close to the top of the carved region.");
            lines.Add("SlimeTopPadding         = " + cfg.Crash_SlimeTopPadding.ToString(CultureInfo.InvariantCulture));
            lines.Add("");

            lines.Add("[Ore]");
            lines.Add("; CustomOreDepositer tuning.");
            lines.Add("; These parameters shape the ore noise field and how it is modulated by biome/ring blending.");
            lines.Add("; Tip: If ore seams look too harsh at biome borders, raise BlendRadius or soften BlendMul/BlendAdd.");
            lines.Add("; BlendRadius: Radius in blocks over which biome/ring blending influences ore (bigger => smoother transitions).");
            lines.Add("BlendRadius = " + cfg.Ore_BlendRadius.ToString(CultureInfo.InvariantCulture));
            lines.Add("; BlendMul: Multiplier applied to the computed blend factor before mixing into ore thresholds.");
            lines.Add("BlendMul    = " + cfg.Ore_BlendMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; BlendAdd: Additive bias applied to the blend factor (shifts overall blend influence up/down).");
            lines.Add("BlendAdd    = " + cfg.Ore_BlendAdd.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; MaxY: global maximum Y for the main ore pass (ores above this Y won't be placed).");
            lines.Add("MaxY               = " + cfg.Ore_MaxY.ToString(CultureInfo.InvariantCulture));
            lines.Add("; BlendToIntBlendMul: Scales how strongly blend factor is converted into the depositor's internal blend value.");
            lines.Add("BlendToIntBlendMul = " + cfg.Ore_BlendToIntBlendMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; NoiseAdjustCenter: Center point for any threshold/curve adjustment applied to the ore noise.");
            lines.Add("NoiseAdjustCenter  = " + cfg.Ore_NoiseAdjustCenter.ToString(CultureInfo.InvariantCulture));
            lines.Add("; NoiseAdjustDiv: Width/divisor for the noise adjust curve. Higher => gentler adjustment.");
            lines.Add("NoiseAdjustDiv     = " + cfg.Ore_NoiseAdjustDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; --- Coal/Copper pass ---");
            lines.Add("; CoalCoarseDiv: Coarse noise divisor for coal/copper distribution. Higher => larger, smoother blobs.");
            lines.Add("CoalCoarseDiv         = " + cfg.Ore_CoalCoarseDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; CoalThresholdBase: Base threshold for coal placement (higher => less coal).");
            lines.Add("CoalThresholdBase     = " + cfg.Ore_CoalThresholdBase.ToString(CultureInfo.InvariantCulture));
            lines.Add("; CopperThresholdOffset: Offset applied for copper compared to coal thresholding (positive => less copper, negative => more).");
            lines.Add("CopperThresholdOffset = " + cfg.Ore_CopperThresholdOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; --- Iron/Gold pass ---");
            lines.Add("; IronOffset: Shifts the iron noise sampling/thresholding relative to coal/copper (decorrelation knob).");
            lines.Add("IronOffset          = " + cfg.Ore_IronOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("; IronCoarseDiv: Coarse noise divisor for iron/gold distribution. Higher => larger, smoother blobs.");
            lines.Add("IronCoarseDiv       = " + cfg.Ore_IronCoarseDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; IronThresholdBase: Base threshold for iron placement (higher => less iron).");
            lines.Add("IronThresholdBase   = " + cfg.Ore_IronThresholdBase.ToString(CultureInfo.InvariantCulture));
            lines.Add("; GoldThresholdOffset: Gold threshold offset relative to iron (positive => less gold, negative => more).");
            lines.Add("GoldThresholdOffset = " + cfg.Ore_GoldThresholdOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("; GoldMaxY: Gold will not spawn above this Y (keeps gold deeper).");
            lines.Add("GoldMaxY            = " + cfg.Ore_GoldMaxY.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; --- Deep pass (Diamond/Lava, etc.) ---");
            lines.Add("; DeepPassMaxY: Maximum Y for the deep-only pass (limits deep ores to below this).");
            lines.Add("DeepPassMaxY           = " + cfg.Ore_DeepPassMaxY.ToString(CultureInfo.InvariantCulture));
            lines.Add("; DiamondOffset: Shifts diamond noise sampling/thresholding (decorrelation knob).");
            lines.Add("DiamondOffset          = " + cfg.Ore_DiamondOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("; DiamondCoarseDiv: Coarse noise divisor for diamonds. Higher => larger, smoother clusters.");
            lines.Add("DiamondCoarseDiv       = " + cfg.Ore_DiamondCoarseDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LavaThresholdBase: Threshold for lava pocket placement (higher => less lava).");
            lines.Add("LavaThresholdBase      = " + cfg.Ore_LavaThresholdBase.ToString(CultureInfo.InvariantCulture));
            lines.Add("; DiamondThresholdOffset: Diamond threshold offset relative to deep baseline (positive => fewer diamonds).");
            lines.Add("DiamondThresholdOffset = " + cfg.Ore_DiamondThresholdOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("; DiamondMaxY: Diamonds will not spawn above this Y (keeps diamonds very deep).");
            lines.Add("DiamondMaxY            = " + cfg.Ore_DiamondMaxY.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; --- Loot (chest/loot pockets) ---");
            lines.Add("; LootEnabled: Enables/disables the loot placement pass.");
            lines.Add("LootEnabled         = " + (cfg.Ore_LootEnabled ? "true" : "false"));
            lines.Add("; LootOnNonRockBlocks: If true, allows loot pockets in non-rock blocks (risk: odd placements).");
            lines.Add("LootOnNonRockBlocks = " + (cfg.Ore_LootOnNonRockBlocks ? "true" : "false"));
            lines.Add("; LootSandSnowMaxY: Max Y for loot in sand/snow regions (helps keep surface clean).");
            lines.Add("LootSandSnowMaxY    = " + cfg.Ore_LootSandSnowMaxY.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; LootOffset: Shifts loot noise sampling/thresholding (decorrelation knob).");
            lines.Add("LootOffset    = " + cfg.Ore_LootOffset.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LootCoarseDiv: Coarse noise divisor for loot distribution. Higher => larger, smoother loot regions.");
            lines.Add("LootCoarseDiv = " + cfg.Ore_LootCoarseDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LootFineDiv: Fine noise divisor for loot details. Higher => smaller-scale variation within loot regions.");
            lines.Add("LootFineDiv   = " + cfg.Ore_LootFineDiv.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; Survival thresholds: How strict the loot noise cutoff is for different loot bands.");
            lines.Add("; Higher threshold => fewer spawns in that band.");
            lines.Add("LootSurvivalMainThreshold     = " + cfg.Ore_LootSurvivalMainThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("LootSurvivalLuckyThreshold    = " + cfg.Ore_LootSurvivalLuckyThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("LootSurvivalRegularThreshold  = " + cfg.Ore_LootSurvivalRegularThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("; Lucky band vertical gating (limits where 'lucky' loot can appear).");
            lines.Add("LootLuckyBandMinY             = " + cfg.Ore_LootLuckyBandMinY.ToString(CultureInfo.InvariantCulture));
            lines.Add("LootLuckyBandMaxYStart        = " + cfg.Ore_LootLuckyBandMaxYStart.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; Scavenger tuning: Modifies how aggressively loot is placed in Scavenger mode.");
            lines.Add("LootScavengerTargetMod        = " + cfg.Ore_LootScavengerTargetMod.ToString(CultureInfo.InvariantCulture));
            lines.Add("LootScavengerMainThreshold    = " + cfg.Ore_LootScavengerMainThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("LootScavengerLuckyThreshold   = " + cfg.Ore_LootScavengerLuckyThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LuckyExtraPerMod: Extra lucky chance per 'mod' step (higher => more lucky spawns when mod increases).");
            lines.Add("LootScavengerLuckyExtraPerMod = " + cfg.Ore_LootScavengerLuckyExtraPerMod.ToString(CultureInfo.InvariantCulture));
            lines.Add("LootScavengerRegularThreshold = " + cfg.Ore_LootScavengerRegularThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("");

            lines.Add("[Trees]");
            lines.Add("; CustomTreeDepositer tuning (used when Overlays.Trees = true).");
            lines.Add("; These control tree placement density, height, canopy shape, and the ground scan that finds valid grass.");
            lines.Add("; Tip: If trees are too frequent, raise Threshold. If canopies clip into chunk seams, reduce LeafRadius.");
            lines.Add("; Scale: Noise frequency. Higher => more variation / more frequent changes. Vanilla = 0.4375.");
            lines.Add("Scale           = " + cfg.Tree_TreeScale.ToString(CultureInfo.InvariantCulture));
            lines.Add("; Threshold: Higher => fewer trees (0..1). Vanilla = 0.6");
            lines.Add("Threshold       = " + cfg.Tree_TreeThreshold.ToString(CultureInfo.InvariantCulture));
            lines.Add("; BaseTrunkHeight: Minimum trunk height in blocks (before noise-based variation). Vanilla = 5.");
            lines.Add("BaseTrunkHeight = " + cfg.Tree_BaseTrunkHeight.ToString(CultureInfo.InvariantCulture));
            lines.Add("; HeightVarMul: How strongly (noise - threshold) adds trunk height. Higher => taller trees. Vanilla-ish = 9.");
            lines.Add("HeightVarMul    = " + cfg.Tree_HeightVarMul.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LeafRadius: Canopy radius (0..7 typical). Also shrinks the seam-safe interior inside each chunk.");
            lines.Add("LeafRadius      = " + cfg.Tree_LeafRadius.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LeafNoiseScale: Multiplies leaf sample position before Perlin noise (canopy texture). Vanilla = 0.5.");
            lines.Add("LeafNoiseScale  = " + cfg.Tree_LeafNoiseScale.ToString(CultureInfo.InvariantCulture));
            lines.Add("; LeafCutoff: Leaves placed when (noise + distBlender) > cutoff. Higher => fewer leaves. Vanilla = 0.25.");
            lines.Add("LeafCutoff      = " + cfg.Tree_LeafCutoff.ToString(CultureInfo.InvariantCulture));
            lines.Add("");
            lines.Add("; --- Advanced ground scan (controls how the depositor finds valid ground for a tree) ---");
            lines.Add("; GroundScanStartY: Starting Y for scanning downward to find grass/ground (higher => starts scan higher).");
            lines.Add("GroundScanStartY = " + cfg.Tree_GroundScanStartY.ToString(CultureInfo.InvariantCulture));
            lines.Add("; GroundScanMinY: Lowest Y the scan is allowed to reach (safety clamp to avoid scanning too deep).");
            lines.Add("GroundScanMinY   = " + cfg.Tree_GroundScanMinY.ToString(CultureInfo.InvariantCulture));
            lines.Add("; MinGroundHeight: Minimum required solid ground height/column validity (prevents trees on tiny slivers).");
            lines.Add("MinGroundHeight  = " + cfg.Tree_MinGroundHeight.ToString(CultureInfo.InvariantCulture));
            lines.Add("");

            lines.Add("[Hotkeys]");
            lines.Add("; Optional reload binding (wire it up like WEHotkeys if desired).");
            lines.Add("ReloadConfig = " + (cfg.ReloadConfigHotkey ?? "Ctrl+Shift+R"));
            lines.Add("");

            return lines.ToArray();
        }

        /// <summary>Clamps an int value to [lo..hi].</summary>
        public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>Clamps a float value to [lo..hi].</summary>
        public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>Clamps a double value to [lo..hi].</summary>
        public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);

        #endregion
    }
    #endregion

    #region WorldGenPlusSettings (Snapshot DTO)

    /// <summary>
    /// Data object passed into the builder (snapshot of settings).
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - This is intentionally "dumb data" (no file IO), built by <see cref="WGConfig.Snapshot"/>.
    /// - Lists are usually cloned in Snapshot() to avoid mid-generation mutations.
    /// </remarks>
    internal sealed class WorldGenPlusSettings
    {
        #region [WorldGenPlus]

        public bool Enabled;

        #endregion

        #region [Seed]

        public bool SeedOverrideEnabled;
        public int  SeedOverride;

        #endregion

        #region [Surface]

        public WorldGenPlusBuilder.SurfaceGenMode SurfaceMode;

        #endregion

        #region [Rings]

        public int   RingPeriod;
        public bool  MirrorRepeat;
        public int   TransitionWidth;

        public int   RegionCellSize;
        public int   RegionBlendWidth;
        public float RegionBlendPower;

        public int   WorldBlendRadius;
        public bool  RegionSmoothEdges;

        public List<RingCore> Rings = new List<RingCore>();

        #endregion

        #region [RingsRandom]

        public bool RandomRingsVaryByPeriod;
        public List<string> RandomRingBiomeChoices = new List<string>();

        #endregion

        #region [SingleBiome]

        public string SingleSurfaceBiome;

        #endregion

        #region [RandomRegions]

        public bool AutoIncludeCustomBiomesForRandomRegions;
        public List<string> RandomSurfaceBiomeChoices = new List<string>();

        #endregion

        #region [Overlays]

        public bool EnableCrashSites;
        public bool EnableCaves;
        public bool EnableOre;
        public bool EnableBiomeOverlayGuards;
        public bool EnableHellCeiling;
        public bool EnableHell;
        public bool EnableBedrock;
        public bool EnableOrigin;
        public bool EnableWater;
        public bool EnableTrees;

        #endregion

        #region [Spawners]

        public bool EnableCaveSpawners;
        public bool EnableHellBossSpawners;

        #endregion

        #region [Bedrock]

        public int Bedrock_CoordDiv;
        public int Bedrock_MinLevel;
        public int Bedrock_Variance;

        #endregion

        #region [CrashSite]

        public float Crash_WorldScale;
        public float Crash_NoiseThreshold;

        public int   Crash_GroundPlane;
        public int   Crash_StartY;
        public int   Crash_EndYExclusive;

        public float Crash_CraterDepthMul;
        public bool  Crash_EnableMound;
        public float Crash_MoundThreshold;
        public float Crash_MoundHeightMul;

        public int   Crash_CarvePadding;
        public bool  Crash_ProtectBloodStone;

        public bool  Crash_EnableSlime;
        public int   Crash_SlimePosOffset;
        public int   Crash_SlimeCoarseDiv;
        public int   Crash_SlimeAdjustCenter;
        public int   Crash_SlimeAdjustDiv;
        public int   Crash_SlimeThresholdBase;
        public float Crash_SlimeBlendToIntBlendMul;
        public float Crash_SlimeThresholdBlendMul;
        public int   Crash_SlimeTopPadding;

        #endregion

        #region [Ore]

        public int   Ore_BlendRadius;
        public float Ore_BlendMul;
        public float Ore_BlendAdd;

        public int   Ore_MaxY;
        public float Ore_BlendToIntBlendMul;

        public int   Ore_NoiseAdjustCenter;
        public int   Ore_NoiseAdjustDiv;

        public int   Ore_CoalCoarseDiv;
        public int   Ore_CoalThresholdBase;
        public int   Ore_CopperThresholdOffset;

        public int   Ore_IronOffset;
        public int   Ore_IronCoarseDiv;
        public int   Ore_IronThresholdBase;
        public int   Ore_GoldThresholdOffset;
        public int   Ore_GoldMaxY;

        public int   Ore_DeepPassMaxY;
        public int   Ore_DiamondOffset;
        public int   Ore_DiamondCoarseDiv;
        public int   Ore_LavaThresholdBase;
        public int   Ore_DiamondThresholdOffset;
        public int   Ore_DiamondMaxY;

        public bool  Ore_LootEnabled;
        public bool  Ore_LootOnNonRockBlocks;
        public int   Ore_LootSandSnowMaxY;

        public int   Ore_LootOffset;
        public int   Ore_LootCoarseDiv;
        public int   Ore_LootFineDiv;

        public int   Ore_LootSurvivalMainThreshold;
        public int   Ore_LootSurvivalLuckyThreshold;
        public int   Ore_LootSurvivalRegularThreshold;
        public int   Ore_LootLuckyBandMinY;
        public int   Ore_LootLuckyBandMaxYStart;

        public int   Ore_LootScavengerTargetMod;
        public int   Ore_LootScavengerMainThreshold;
        public int   Ore_LootScavengerLuckyThreshold;
        public int   Ore_LootScavengerLuckyExtraPerMod;
        public int   Ore_LootScavengerRegularThreshold;

        #endregion

        #region [Tree]

        // NOTE: Region name is "[Tree]" here, while config sections are "[Trees]".
        //       Keeping as-is per "no code changes"; it's purely an organizational directive.

        public float Tree_TreeScale;
        public float Tree_TreeThreshold;
        public int   Tree_BaseTrunkHeight;
        public float Tree_HeightVarMul;
        public int   Tree_LeafRadius;
        public float Tree_LeafNoiseScale;
        public float Tree_LeafCutoff;
        public int   Tree_GroundScanStartY;
        public int   Tree_GroundScanMinY;
        public int   Tree_MinGroundHeight;

        #endregion
    }
    #endregion

    #region RingCore (Ring Definition)

    /// <summary>
    /// Defines a ring core entry: the ending radius of the core, and the biome type name used within that core.
    /// </summary>
    internal struct RingCore
    {
        public int    EndRadius;
        public string BiomeType;

        /// <summary>
        /// Creates a ring core definition.
        /// </summary>
        public RingCore(int endRadius, string biomeType)
        {
            EndRadius = endRadius;
            BiomeType = biomeType;
        }
    }
    #endregion

    #region SimpleIni

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
        /// Reads a float value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public float GetFloat(string section, string key, float def)
            => float.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var v) ? v : def;

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
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i != 0;
            return def;
        }
    }
    #endregion
}