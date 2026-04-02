/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain.WorldBuilders;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus world builder.
    ///
    /// Responsibilities:
    /// - Builds the surface biome per-column using one of four surface modes:
    ///   1) VanillaRings  (radial + repeat/mirror pattern)
    ///   2) SquareBands   (Chebyshev distance + repeat/mirror pattern)
    ///   3) SingleBiome   (one biome everywhere)
    ///   4) RandomRegions (Voronoi/cellular features with explicit border blending)
    ///
    /// Notes:
    /// - Overlay distance can be decoupled from surface distance so overlays behave correctly in RandomRegions mode.
    /// - DecentBiome is treated as a special fade band to match vanilla quirks.
    /// - Optional "Biome overlay guards" suppress hell/bedrock overlays on special/test surface biomes.
    /// </summary>
    internal sealed class WorldGenPlusBuilder : WorldBuilder
    {
        #region Fields / Enums / Constants

        #region Fields: World + Config

        /// <summary>WorldInfo provided by the game; may hold the authoritative seed.</summary>
        private readonly WorldInfo _worldInfo;

        /// <summary>
        /// Snapshot of mod configuration used to drive generation. Never null (falls back to defaults).
        /// </summary>
        private readonly WorldGenPlusSettings _cfg;

        #endregion

        #region Fields: Vanilla-Style Overlay Stack

        // Vanilla overlay stack (same types CastleMinerZBuilder uses).
        private readonly CustomCrashSiteDepositor _crash;
        private readonly CaveBiome                _caves;
        private readonly CustomOreDepositor       _ore;
        private readonly HellCeilingBiome         _hellCeiling;
        private readonly HellFloorBiome           _hellFloor;
        private readonly CustomBedrockDepositor   _bedrock;
        private readonly OriginBiome              _origin;

        // Vanilla tree pass.
        private readonly CustomTreeDepositor      _trees;

        #endregion

        #region Fields: Biome Caching / Suppression Sets

        /// <summary>
        /// Surface biome instance cache keyed by full type name (case-insensitive).
        /// This avoids reflection + allocator churn while building columns.
        /// </summary>
        private readonly Dictionary<string, Biome> _biomeCache =
            new Dictionary<string, Biome>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Surface biomes that should NOT get the hell overlays applied on top.
        /// (These are "special / test / low terrain" biomes where hell becomes visible everywhere.)
        /// </summary>
        private static readonly HashSet<string> HellSuppressedSurfaceBiomes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Unused special/test/unusual biomes.
                "DNA.CastleMinerZ.Terrain.WorldBuilders.OriginBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.CostalBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.FlatLandBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.TestBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.TreeTestBiome",
                "DNA.CastleMinerZ.Terrain.WorldBuilders.OceanBiome",

                // Depositors.
                // "DNA.CastleMinerZ.Terrain.WorldBuilders.OreDepositer",
                // "DNA.CastleMinerZ.Terrain.WorldBuilders.BedrockDepositer",
                // "DNA.CastleMinerZ.Terrain.WorldBuilders.CrashSiteDepositer",
                // "DNA.CastleMinerZ.Terrain.WorldBuilders.TreeDepositer",
            };

        #endregion

        #region Enums / Constants

        /// <summary>
        /// Surface generation modes:
        /// - VanillaRings:  Radial rings with optional repeat/mirror (vanilla-like).
        /// - SquareBands:   Concentric square "rings" (Chebyshev distance) with optional repeat/mirror.
        /// - SingleBiome:   One configured biome everywhere.
        /// - RandomRegions: Voronoi/cellular regions with explicit border blending.
        /// </summary>
        internal enum SurfaceGenMode
        {
            VanillaRings  = 0,
            SquareBands   = 1,
            SingleBiome   = 2,
            RandomRegions = 3,
        }

        /// <summary>
        /// Vanilla special-case biome type name (vanilla treats Decent in a quirky way).
        /// </summary>
        private const string DecentBiomeTypeName = "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome";

        #endregion

        #endregion

        #region Construction / Initialization

        /// <summary>
        /// Constructs the builder and all overlay biome/depositer instances.
        ///
        /// Notes:
        /// - Seed override is applied BEFORE constructing biome/depositer objects to keep deterministic results.
        /// - Caves loot modifiers are configured the same way vanilla does in its builder constructor.
        /// - Ring biome cache is "primed" (optional optimization) to front-load reflection cost.
        /// - CustomBiomeRegistry is refreshed and discovered types are logged.
        /// </summary>
        public WorldGenPlusBuilder(WorldInfo worldInfo, WorldGenPlusSettings cfg)
            : base(worldInfo)
        {
            _worldInfo = worldInfo;
            _cfg = cfg ?? new WorldGenPlusSettings();

            // Custom water rendering depth.
            // Vanilla-style builders can override this from the WorldBuilder default.
            this.WaterDepth = 12f;

            // APPLY SEED OVERRIDE FIRST (before constructing any Biome/Depositer)
            if (_cfg.SeedOverrideEnabled)
                TrySetSeed(_worldInfo, _cfg.SeedOverride);

            _crash = new CustomCrashSiteDepositor(worldInfo, new CustomCrashSiteDepositor.Settings
            {
                WorldScale                    = _cfg.Crash_WorldScale,
                NoiseThreshold                = _cfg.Crash_NoiseThreshold,

                GroundPlane                   = _cfg.Crash_GroundPlane,
                StartY                        = _cfg.Crash_StartY,
                EndYExclusive                 = _cfg.Crash_EndYExclusive,

                CraterDepthMul                = _cfg.Crash_CraterDepthMul,
                EnableMound                   = _cfg.Crash_EnableMound,
                MoundThreshold                = _cfg.Crash_MoundThreshold,
                MoundHeightMul                = _cfg.Crash_MoundHeightMul,

                CarvePadding                  = _cfg.Crash_CarvePadding,
                ProtectBloodStone             = _cfg.Crash_ProtectBloodStone,

                EnableSlime                   = _cfg.Crash_EnableSlime,
                SlimePosOffset                = _cfg.Crash_SlimePosOffset,
                SlimeCoarseDiv                = _cfg.Crash_SlimeCoarseDiv,
                SlimeAdjustCenter             = _cfg.Crash_SlimeAdjustCenter,
                SlimeAdjustDiv                = _cfg.Crash_SlimeAdjustDiv,
                SlimeThresholdBase            = _cfg.Crash_SlimeThresholdBase,
                SlimeBlendToIntBlendMul       = _cfg.Crash_SlimeBlendToIntBlendMul,
                SlimeThresholdBlendMul        = _cfg.Crash_SlimeThresholdBlendMul,
                SlimeTopPadding               = _cfg.Crash_SlimeTopPadding
            });
            _caves = new CaveBiome(worldInfo);
            _ore = new CustomOreDepositor(worldInfo, new CustomOreDepositor.Settings
            {
                MaxY                          = _cfg.Ore_MaxY,
                BlendToIntBlendMul            = _cfg.Ore_BlendToIntBlendMul,

                NoiseAdjustCenter             = _cfg.Ore_NoiseAdjustCenter,
                NoiseAdjustDiv                = _cfg.Ore_NoiseAdjustDiv,

                CoalCoarseDiv                 = _cfg.Ore_CoalCoarseDiv,
                CoalThresholdBase             = _cfg.Ore_CoalThresholdBase,
                CopperThresholdOffset         = _cfg.Ore_CopperThresholdOffset,

                IronPosOffset                 = new IntVector3(_cfg.Ore_IronOffset, _cfg.Ore_IronOffset, _cfg.Ore_IronOffset),
                IronCoarseDiv                 = _cfg.Ore_IronCoarseDiv,
                IronThresholdBase             = _cfg.Ore_IronThresholdBase,
                GoldThresholdOffset           = _cfg.Ore_GoldThresholdOffset,
                GoldMaxY                      = _cfg.Ore_GoldMaxY,

                DeepPassMaxY                  = _cfg.Ore_DeepPassMaxY,
                DiamondPosOffset              = new IntVector3(_cfg.Ore_DiamondOffset, _cfg.Ore_DiamondOffset, _cfg.Ore_DiamondOffset),
                DiamondCoarseDiv              = _cfg.Ore_DiamondCoarseDiv,
                LavaThresholdBase             = _cfg.Ore_LavaThresholdBase,
                DiamondThresholdOffset        = _cfg.Ore_DiamondThresholdOffset,
                DiamondMaxY                   = _cfg.Ore_DiamondMaxY,

                LootEnabled                   = _cfg.Ore_LootEnabled,
                LootOnNonRockBlocks           = _cfg.Ore_LootOnNonRockBlocks,
                LootSandSnowMaxY              = _cfg.Ore_LootSandSnowMaxY,

                LootPosOffset                 = new IntVector3(_cfg.Ore_LootOffset, _cfg.Ore_LootOffset, _cfg.Ore_LootOffset),
                LootCoarseDiv                 = _cfg.Ore_LootCoarseDiv,
                LootFineDiv                   = _cfg.Ore_LootFineDiv,

                LootSurvivalMainThreshold     = _cfg.Ore_LootSurvivalMainThreshold,
                LootSurvivalLuckyThreshold    = _cfg.Ore_LootSurvivalLuckyThreshold,
                LootSurvivalRegularThreshold  = _cfg.Ore_LootSurvivalRegularThreshold,
                LootLuckyBandMinY             = _cfg.Ore_LootLuckyBandMinY,
                LootLuckyBandMaxYStart        = _cfg.Ore_LootLuckyBandMaxYStart,

                LootScavengerTargetMod        = _cfg.Ore_LootScavengerTargetMod,
                LootScavengerMainThreshold    = _cfg.Ore_LootScavengerMainThreshold,
                LootScavengerLuckyThreshold   = _cfg.Ore_LootScavengerLuckyThreshold,
                LootScavengerLuckyExtraPerMod = _cfg.Ore_LootScavengerLuckyExtraPerMod,
                LootScavengerRegularThreshold = _cfg.Ore_LootScavengerRegularThreshold
            });
            _hellCeiling = new HellCeilingBiome(worldInfo);
            _hellFloor = new HellFloorBiome(worldInfo);
            _bedrock = new CustomBedrockDepositor(worldInfo, new CustomBedrockDepositor.Settings
            {
                CoordDiv = _cfg.Bedrock_CoordDiv,
                MinLevel = _cfg.Bedrock_MinLevel,
                Variance = _cfg.Bedrock_Variance
            });
            _origin = new OriginBiome(worldInfo);

            _trees = new CustomTreeDepositor(worldInfo, new CustomTreeDepositor.Settings
            {
                TreeScale        = _cfg.Tree_TreeScale,
                TreeThreshold    = _cfg.Tree_TreeThreshold,

                BaseTrunkHeight  = _cfg.Tree_BaseTrunkHeight,
                HeightVarMul     = _cfg.Tree_HeightVarMul,

                LeafRadius       = _cfg.Tree_LeafRadius,
                LeafNoiseScale   = _cfg.Tree_LeafNoiseScale,
                LeafCutoff       = _cfg.Tree_LeafCutoff,

                GroundScanStartY = _cfg.Tree_GroundScanStartY,
                GroundScanMinY   = _cfg.Tree_GroundScanMinY,
                MinGroundHeight  = _cfg.Tree_MinGroundHeight
            });

            // Vanilla does this in CastleMinerZBuilder ctor.
            _caves.SetLootModifiersByGameMode();

            // Prime cache for ring biomes (optional).
            if (_cfg.Rings != null)
            {
                for (int i = 0; i < _cfg.Rings.Count; i++)
                {
                    string t = _cfg.Rings[i].BiomeType;
                    if (IsRingRandomToken(t)) continue; // Don't instantiate "@Random".
                    GetOrCreateBiome(t);
                }
            }

            // Discover custom biome DLLs (safe to call repeatedly).
            CustomBiomeRegistry.Refresh();
            var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();

            ModLoader.LogSystem.Log("Custom biome types discovered:");
            if (custom.Length == 0)
                ModLoader.LogSystem.Log(" - 0.");
            else
                for (int i = 0; i < custom.Length; i++)
                    ModLoader.LogSystem.Log($"  - {custom[i]}.");
        }
        #endregion

        #region World Building: Chunk Pipeline

        /// <summary>
        /// Builds a 16x16 world chunk at minLoc.
        ///
        /// High-level pipeline per column:
        /// 1) Choose surface biome (rings/squares/random regions).
        /// 2) Compute overlay blending and hell fade rules.
        /// 3) Apply overlays (hell ceiling, crash, caves, ore, hell floor, bedrock, origin).
        ///
        /// End-of-chunk:
        /// - Run the vanilla tree pass (inner area only, to avoid seams).
        /// - Run HellFloor post-process if hell was used.
        /// </summary>
        public override void BuildWorldChunk(BlockTerrain terrain, IntVector3 minLoc)
        {
            // Vanilla water behavior (optional toggle).
            if (_cfg.EnableWater)
                terrain.WaterLevel = 1.5f;

            const int CHUNK = 16;

            bool didHellThisChunk = false;

            for (int z = 0; z < CHUNK; z++)
            {
                int worldZ = minLoc.Z + z;
                long zsqu = (long)worldZ * (long)worldZ;

                for (int x = 0; x < CHUNK; x++)
                {
                    int worldX = minLoc.X + x;

                    // True radial distance from origin (vanilla uses long to avoid overflow).
                    float radialDist = (float)Math.Sqrt((double)((long)worldX * (long)worldX + zsqu));

                    // IMPORTANT FIX:
                    // - surfaceDist is used ONLY for surface selection (rings/square bands/random regions).
                    // - overlayDist is used for ALL vanilla-style overlays (crash/ore/hell fades) so they work in non-ring modes.
                    float surfaceDist = radialDist;

                    // ------------------------------------------------------------
                    // Decide what "distance" overlays should use.
                    // VanillaRings/SquareBands: overlays follow the repeated/mirrored PATTERN distance.
                    // RandomRegions: overlays follow true radial distance.
                    // ------------------------------------------------------------
                    bool overlaysFollowPattern =
                        (_cfg.SurfaceMode == SurfaceGenMode.VanillaRings) ||
                        (_cfg.SurfaceMode == SurfaceGenMode.SquareBands);

                    float overlayDist = overlaysFollowPattern ? surfaceDist : radialDist;


                    bool suppressHellForThisColumn;
                    // ============================================================
                    // 1) Surface generation mode (what surface biome gets built).
                    // ============================================================
                    switch (_cfg.SurfaceMode)
                    {
                        case SurfaceGenMode.SquareBands:
                            {
                                // Chebyshev distance => concentric squares.
                                int ax = worldX < 0 ? -worldX : worldX;
                                int az = worldZ < 0 ? -worldZ : worldZ;
                                float squareDist = (ax > az) ? ax : az;

                                // Apply the same repeat/mirror logic (but on square distance).
                                float tmp = squareDist;
                                int flips = 0;
                                float period = Math.Max(1f, (float)_cfg.RingPeriod);

                                while (tmp > period)
                                {
                                    tmp -= period;
                                    flips++;
                                }

                                if (_cfg.MirrorRepeat && ((flips & 1) == 1))
                                    tmp = period - tmp;

                                surfaceDist = tmp;

                                // Use your ring list as square "band" breakpoints.
                                int patternIndex = flips;
                                BuildSurfaceFromRings(terrain, worldX, worldZ, minLoc.Y, surfaceDist, patternIndex, out suppressHellForThisColumn);
                                break;
                            }

                        case SurfaceGenMode.SingleBiome:
                            {
                                string biomeType = _cfg.SingleSurfaceBiome;
                                if (string.IsNullOrWhiteSpace(biomeType))
                                    biomeType = "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome";

                                suppressHellForThisColumn = IsHellSuppressedSurface(biomeType);
                                GetOrCreateBiome(biomeType)?.BuildColumn(terrain, worldX, worldZ, minLoc.Y, 1f);
                                break;
                            }

                        case SurfaceGenMode.RandomRegions:
                            {
                                // Minecraft-like blobs (deterministic).
                                // NOTE: BuildSurfaceRandomRegions should choose from a pool that includes DecentBiome.
                                // (your old random generator likely had it in the pool).
                                BuildSurfaceRandomRegions(terrain, worldX, worldZ, minLoc.Y, out suppressHellForThisColumn);

                                // Keep overlays vanilla-ish (radial fade) even though surface is random.
                                // overlayDist remains radialDist by design.
                                break;
                            }

                        case SurfaceGenMode.VanillaRings:
                        default:
                            {
                                // Your existing ring repeat/mirror logic (radial).
                                float tmp = radialDist;
                                int flips = 0;
                                float period = Math.Max(1f, (float)_cfg.RingPeriod);

                                while (tmp > period)
                                {
                                    tmp -= period;
                                    flips++;
                                }

                                if (_cfg.MirrorRepeat && ((flips & 1) == 1))
                                    tmp = period - tmp;

                                surfaceDist = tmp;

                                int patternIndex = flips;
                                BuildSurfaceFromRings(terrain, worldX, worldZ, minLoc.Y, surfaceDist, patternIndex, out suppressHellForThisColumn);
                                break;
                            }
                    }

                    // If guards are disabled, treat every column as "not suppressed".
                    if (!_cfg.EnableBiomeOverlayGuards)
                        suppressHellForThisColumn = false;

                    bool doHell = _cfg.EnableHell && !suppressHellForThisColumn;
                    bool doHellCeiling = _cfg.EnableHellCeiling && !suppressHellForThisColumn;
                    bool doBedrock = _cfg.EnableBedrock && !suppressHellForThisColumn;

                    // ============================================================
                    // 2) Overlay blending (vanilla-ish).
                    // ============================================================
                    float worldBlendRadius = Math.Max(1f, (float)_cfg.WorldBlendRadius);
                    float worldBlender = MathHelper.Clamp(overlayDist / worldBlendRadius, 0f, 1f);

                    // ------------------------------------------------------------
                    // Vanilla DecentBiome logic:
                    // hellBlender = 1 everywhere except it fades OUT across the Decent band.
                    // For RandomRegions, keep your previous behavior (use worldBlender) unless you want vanilla-too.
                    // ------------------------------------------------------------
                    float hellBlender =
                        overlaysFollowPattern ? ComputeVanillaHellBlender(surfaceDist)
                                              : worldBlender;

                    // ============================================================
                    // 3) Overlays.
                    // ============================================================

                    // Hell ceiling.
                    if (doHellCeiling)
                        _hellCeiling.BuildColumn(terrain, worldX, worldZ, minLoc.Y, hellBlender);

                    // Crash sites.
                    // VanillaRings/SquareBands (vanilla-ish): 300 < dist < worldBlendRadius.
                    // RandomRegions: Allow ANY range past 300 (no upper cap).
                    if (_cfg.EnableCrashSites)
                    {
                        if (_cfg.SurfaceMode == SurfaceGenMode.RandomRegions)
                        {
                            if (overlayDist > 300f)
                                _crash.BuildColumn(terrain, worldX, worldZ, minLoc.Y, worldBlender);
                        }
                        else
                        {
                            if (overlayDist > 300f && overlayDist < worldBlendRadius)
                                _crash.BuildColumn(terrain, worldX, worldZ, minLoc.Y, worldBlender);
                        }
                    }

                    // Caves / Ore.
                    if (_cfg.EnableCaves)
                        _caves.BuildColumn(terrain, worldX, worldZ, minLoc.Y, 1f);

                    if (_cfg.EnableOre)
                        _ore.BuildColumn(terrain, worldX, worldZ, minLoc.Y, worldBlender);

                    // Hell floor (and remember we did hell in this chunk for post-process).
                    if (doHell)
                    {
                        _hellFloor.BuildColumn(terrain, worldX, worldZ, minLoc.Y, hellBlender);
                        didHellThisChunk = true;
                    }

                    // Bedrock.
                    if (doBedrock)
                        _bedrock.BuildColumn(terrain, worldX, worldZ, minLoc.Y, 1f);

                    // Origin overlay.
                    if (_cfg.EnableOrigin)
                        _origin.BuildColumn(terrain, worldX, worldZ, minLoc.Y, 1f);
                }
            }

            // --- VANILLA TREE PASS (MUST happen AFTER terrain/overlays are done) ---
            // Vanilla only tests inner 10x10 cells (3..12) to avoid chunk edge seams.
            int R = Math.Max(0, _cfg.Tree_LeafRadius);
            int start = R;
            int endExclusive = 16 - R; // Because x2 < endExclusive => x2 <= 15-R.

            if (_cfg.EnableTrees && endExclusive > start)
            {
                for (int z2 = start; z2 < endExclusive; z2++)
                    for (int x2 = start; x2 < endExclusive; x2++)
                        _trees.BuildColumn(terrain, minLoc.X + x2, minLoc.Z + z2, minLoc.Y, 1f);
            }

            // Vanilla hell biome does a post process at end of chunk.
            if (didHellThisChunk)
                _hellFloor.PostChunkProcess();
        }
        #endregion

        #region Surface Selection: Rings / SquareBands (Shared Path)

        // IMPORTANT: worldX/worldZ must be actual world coords (NOT minLoc.X/minLoc.Z)

        /// <summary>
        /// Builds surface biomes using configured ring cores + optional TransitionWidth blending.
        ///
        /// Notes:
        /// - Supports optional "A|B|C" pipe patterns inside BiomeType strings (patternIndex chooses segment).
        /// - Supports "@Random" token which resolves deterministically to a biome type.
        /// - Treats DecentBiome as a special fade band to mimic vanilla behavior:
        ///   - If current ring is Decent: fade across the ring width.
        ///   - If next ring is Decent: treat [coreEnd..decentEnd) as a Decent fade band (no TransitionWidth inserted).
        /// - If the last ring is Decent: do not fill beyond it (vanilla-like).
        /// </summary>
        private void BuildSurfaceFromRings(
            BlockTerrain terrain,
            int worldX,
            int worldZ,
            int minY,
            float dist,
            int patternIndex,
            out bool suppressHellForThisColumn)
        {
            suppressHellForThisColumn = false;

            var rings = _cfg.Rings;
            if (rings == null || rings.Count == 0)
                return;

            // Negative distances don't make sense here; clamp for safety.
            if (dist < 0f) dist = 0f;

            float W = Math.Max(0, _cfg.TransitionWidth);

            // ------------------------------------------------------------
            // Optional: pattern selection via "A|B|C" in BiomeType strings.
            // If you don't use '|', this is a no-op.
            // ------------------------------------------------------------
            int Mod(int x, int m)
            {
                if (m <= 0) return 0;
                int r = x % m;
                return (r < 0) ? (r + m) : r;
            }

            // NOTE: resolve in THIS order:
            //  1) "A|B|C" pattern pick (optional).
            //  2) "@Random" token -> deterministic pick from bag.
            string ResolveBiomeType(string raw, int coreIndex)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return raw;

                string pick = raw;

                // 1) Optional pipe-pattern support.
                int pipe = raw.IndexOf('|');
                if (pipe >= 0)
                {
                    var parts = raw.Split('|');
                    if (parts.Length > 0)
                    {
                        int pi = Mod(patternIndex, parts.Length);
                        pick = parts[pi].Trim();
                    }
                }

                // 2) Resolve "@Random" token to a real biome type.
                pick = ResolveRingBiomeType(pick, coreIndex, patternIndex);

                return pick;
            }

            string RingTypeAt(int idx) => ResolveBiomeType(rings[idx].BiomeType, idx);

            // Running start radius for the next "core" band (includes any inserted transitions).
            float coreStart = 0f;

            for (int i = 0; i < rings.Count; i++)
            {
                float coreEnd = rings[i].EndRadius;
                if (coreEnd < coreStart) coreEnd = coreStart; // Guard misordered config.

                string aType = RingTypeAt(i);

                // ------------------------------------------------------------
                // If the CURRENT ring is Decent, treat it like a fade band
                // across its full width [coreStart..coreEnd) using its blender.
                // (This makes behavior resilient even if Decent appears "unexpectedly".)
                // ------------------------------------------------------------
                if (IsDecentBiomeType(aType))
                {
                    if (dist >= coreStart && dist < coreEnd)
                    {
                        float t = (coreEnd > coreStart) ? (dist - coreStart) / (coreEnd - coreStart) : 1f;
                        t = MathHelper.Clamp(t, 0f, 1f);

                        suppressHellForThisColumn = IsHellSuppressedSurface(aType);
                        GetOrCreateBiome(aType)?.BuildColumn(terrain, worldX, worldZ, minY, t);
                        return;
                    }

                    // Vanilla-ish: If Decent is the last ring, do NOT fill beyond it.
                    if (i == rings.Count - 1)
                        return;

                    coreStart = coreEnd;
                    continue;
                }

                // -------------------------
                // Core band: [coreStart .. coreEnd).
                // -------------------------
                if (dist >= coreStart && dist < coreEnd)
                {
                    suppressHellForThisColumn = IsHellSuppressedSurface(aType);
                    GetOrCreateBiome(aType)?.BuildColumn(terrain, worldX, worldZ, minY, 1f);
                    return;
                }

                // ------------------------------------------------------------
                // VANILLA DECENT LOGIC:
                // If the NEXT ring is DecentBiome, then the band [coreEnd..decentEnd)
                // is handled as a special fade-in using DecentBiome's blender.
                // There is NO normal TransitionWidth inserted before Decent.
                // ------------------------------------------------------------
                if (i + 1 < rings.Count)
                {
                    string nextType = RingTypeAt(i + 1);
                    if (IsDecentBiomeType(nextType))
                    {
                        float decentStart = coreEnd;
                        float decentEnd = rings[i + 1].EndRadius;
                        if (decentEnd < decentStart) decentEnd = decentStart;

                        if (dist >= decentStart && dist < decentEnd)
                        {
                            float t = (decentEnd > decentStart) ? (dist - decentStart) / (decentEnd - decentStart) : 1f;
                            t = MathHelper.Clamp(t, 0f, 1f);

                            // Be conservative: if either side wants hell suppressed, suppress it.
                            suppressHellForThisColumn =
                                IsHellSuppressedSurface(aType) || IsHellSuppressedSurface(nextType);

                            GetOrCreateBiome(nextType)?.BuildColumn(terrain, worldX, worldZ, minY, t);
                            return;
                        }

                        // Skip over the Decent ring entry entirely (we handled it as a band).
                        coreStart = decentEnd;
                        i++; // Consume the Decent entry.
                        continue;
                    }
                }

                // -------------------------
                // Normal transition: [coreEnd .. coreEnd + wHere)
                // (Clamp width so it can't overshoot the next ring's end.)
                // -------------------------
                if (i + 1 < rings.Count && W > 0f)
                {
                    float nextEnd = rings[i + 1].EndRadius;
                    if (nextEnd < coreEnd) nextEnd = coreEnd;

                    float wHere = Math.Min(W, Math.Max(0f, nextEnd - coreEnd));
                    if (wHere > 0f)
                    {
                        float tStart = coreEnd;
                        float tEnd = coreEnd + wHere;

                        if (dist >= tStart && dist < tEnd)
                        {
                            float t = MathHelper.Clamp((dist - tStart) / (tEnd - tStart), 0f, 1f);

                            string bType = RingTypeAt(i + 1);

                            suppressHellForThisColumn =
                                IsHellSuppressedSurface(aType) || IsHellSuppressedSurface(bType);

                            var a = GetOrCreateBiome(aType);
                            var b = GetOrCreateBiome(bType);

                            a?.BuildColumn(terrain, worldX, worldZ, minY, 1f - t);
                            b?.BuildColumn(terrain, worldX, worldZ, minY, t);
                            return;
                        }

                        coreStart = tEnd;
                    }
                    else
                    {
                        coreStart = coreEnd;
                    }
                }
                else
                {
                    coreStart = coreEnd;
                }
            }

            // ------------------------------------------------------------
            // Fallback beyond last ring:
            // Vanilla behavior is effectively "no surface biome" beyond Decent end.
            // So: If the last ring is DecentBiome, do NOT fill.
            // ------------------------------------------------------------
            string lastType = RingTypeAt(rings.Count - 1);
            if (IsDecentBiomeType(lastType))
                return;

            // Non-decent worlds: keep the old "fill with last ring" fallback.
            suppressHellForThisColumn = IsHellSuppressedSurface(lastType);
            GetOrCreateBiome(lastType)?.BuildColumn(terrain, worldX, worldZ, minY, 1f);
        }
        #endregion

        #region Biome Factory: Custom DLLs + Vanilla Type Lookup + Caching

        /// <summary>
        /// Returns a cached biome instance for a type name, creating it via reflection if needed.
        ///
        /// Resolution order:
        /// 1) CustomBiomeRegistry: external/custom biome types (DLLs).
        /// 2) Vanilla/already-loaded types: Type.GetType then game assembly lookup.
        ///
        /// Notes:
        /// - Returns null if the type cannot be resolved or instantiated.
        /// - Stores null in cache for unknown/failed types to avoid repeated spam work.
        /// </summary>
        private Biome GetOrCreateBiome(string biomeTypeName)
        {
            if (string.IsNullOrWhiteSpace(biomeTypeName))
                return null;

            if (_biomeCache.TryGetValue(biomeTypeName, out var cached))
                return cached;

            // -----------------------------
            // 1) Custom biome DLLs (try + swallow, then fall back).
            // -----------------------------
            if (CustomBiomeRegistry.TryResolveCustomBiomeType(biomeTypeName, out var ct))
            {
                try
                {
                    if (ct != null && typeof(Biome).IsAssignableFrom(ct))
                    {
                        var customBiome = (Biome)Activator.CreateInstance(
                            ct,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            binder: null,
                            args: new object[] { _worldInfo },
                            culture: null
                        );

                        if (customBiome != null)
                        {
                            _biomeCache[biomeTypeName] = customBiome;
                            return customBiome;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Swallow, but log so you can see why it failed.
                    Console.WriteLine($"Custom biome failed '{biomeTypeName}': {ex}.");
                }
            }

            // -----------------------------
            // 2) Vanilla / already-loaded types.
            // -----------------------------
            try
            {
                // Try Type.GetType first; then fall back to the game assembly.
                Type t =
                    // Assembly-qualified names / already-loaded assemblies
                    Type.GetType(biomeTypeName, throwOnError: false)
                    // Vanilla game assembly lookup
                    ?? typeof(Biome).Assembly.GetType(biomeTypeName, throwOnError: false);

                if (t == null || !typeof(Biome).IsAssignableFrom(t))
                {
                    Console.WriteLine($"Unknown biome type: {biomeTypeName}.");
                    _biomeCache[biomeTypeName] = null;
                    return null;
                }

                var biome = (Biome)Activator.CreateInstance(
                    t,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { _worldInfo },
                    culture: null
                );

                _biomeCache[biomeTypeName] = biome;
                return biome;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create biome '{biomeTypeName}': {ex}.");
                _biomeCache[biomeTypeName] = null;
                return null;
            }
        }
        #endregion

        #region Helpers

        #region Helpers: DecentBiome Quirks / Vanilla Hell Blending

        /// <summary>
        /// Vanilla never calls DecentBiome with blender == 1.0f (it uses dist < end).
        /// If DecentBiome treats 1.0f as "done / no-op", force it slightly under 1.
        /// </summary>
        private static float AdjustBiomeBlenderForVanillaQuirks(string biomeType, float blender)
        {
            // Vanilla never calls DecentBiome with blender == 1.0f (it uses dist < end).
            // If DecentBiome treats 1.0f as "done / no-op", force it slightly under 1.
            if (IsDecentBiomeType(biomeType))
                return Math.Min(blender, 0.999f);

            return blender;
        }

        /// <summary>
        /// Returns true if the type name identifies DecentBiome (supports either full name or suffix match).
        /// </summary>
        private static bool IsDecentBiomeType(string biomeTypeName)
        {
            return !string.IsNullOrWhiteSpace(biomeTypeName) &&
                   (biomeTypeName.EndsWith(".DecentBiome", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(biomeTypeName, DecentBiomeTypeName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds the Decent band range from the configured rings.
        /// Vanilla expects Decent to start at the previous ring's EndRadius and end at Decent's EndRadius.
        /// </summary>
        private bool TryGetDecentBand(out float start, out float end)
        {
            start = 0f;
            end = 0f;

            var rings = _cfg?.Rings;
            if (rings == null || rings.Count == 0) return false;

            // Ensure sorted (WGConfig already sorts, but be defensive).
            // NOTE: If you want to avoid allocations, don't sort here.
            // We'll just scan assuming increasing order is typical.
            for (int i = 0; i < rings.Count; i++)
            {
                if (!IsDecentBiomeType(rings[i].BiomeType)) continue;

                end = rings[i].EndRadius;
                start = (i == 0) ? 0f : rings[i - 1].EndRadius;

                if (end > start) return true;
                return false;
            }

            return false;
        }

        /// <summary>
        /// Vanilla: hellBlender is 1 everywhere except it fades out across the Decent band.
        /// </summary>
        private float ComputeVanillaHellBlender(float patternDist)
        {
            if (!TryGetDecentBand(out float ds, out float de))
                return 1f;

            if (patternDist < ds || patternDist >= de)
                return 1f;

            float t = (patternDist - ds) / (de - ds);
            t = MathHelper.Clamp(t, 0f, 1f);
            return 1f - t;
        }
        #endregion

        #region Helpers: Blend Shaping / Voronoi Boundary Math

        /// <summary>Classic smoothstep curve for soft transitions.</summary>
        private static float SmoothStep(float t)
        {
            // Classic smoothstep.
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Symmetric power curve around 0.5.
        /// power=1 => no change, >1 => sharper, <1 => softer (you already clamp power >= 0.5)
        /// </summary>
        private static float ApplySymmetricPower(float t, float power)
        {
            if (power <= 0f || Math.Abs(power - 1f) < 1e-6f) return t;

            if (t < 0.5f)
            {
                float u = t / 0.5f;        // 0..1.
                u = (float)Math.Pow(u, power);
                return 0.5f * u;
            }
            else
            {
                float u = (1f - t) / 0.5f; // 0..1.
                u = (float)Math.Pow(u, power);
                return 1f - 0.5f * u;
            }
        }

        /// <summary>
        /// Returns signed distance to the bisector plane between a and b.
        /// Negative => a-side, Positive => b-side.
        /// </summary>
        private static bool TryGetBisectorSignedDistance(int worldX, int worldZ, Feature a, Feature b, out double signedDist)
        {
            double dx = b.X - a.X;
            double dz = b.Z - a.Z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6)
            {
                signedDist = 0.0;
                return false;
            }

            // Midpoint of segment AB.
            double mx = (a.X + b.X) * 0.5;
            double mz = (a.Z + b.Z) * 0.5;

            // Unit direction from A -> B.
            double nx = dx / len;
            double nz = dz / len;

            // Signed distance from P to the bisector plane along n.
            signedDist = ((worldX - mx) * nx) + ((worldZ - mz) * nz);
            return true;
        }
        #endregion

        #region Random Regions (Voronoi / Cellular + 4-way Blending)

        /// <summary>
        /// Feature point for RandomRegions:
        /// - X/Z is the deterministic point inside a cell.
        /// - BiomeType is the surface biome assigned to that feature.
        /// - D2 is filled during nearest-feature search (squared distance to query point).
        /// </summary>
        private struct Feature
        {
            public double X;
            public double Z;
            public string BiomeType;
            public double D2;        // Squared distance to (worldX, worldZ).
        }

        /// <summary>
        /// Builds surface biomes using deterministic per-cell feature points (Voronoi-style).
        ///
        /// Key notes:
        /// - Uses the TWO nearest features for a clean A/B border (no noisy 4-way mixes).
        /// - Border blend uses signed distance to the perpendicular bisector between feature points.
        /// - DecentBiome is treated specially (forced small blender and bypass blending).
        /// </summary>
        private void BuildSurfaceRandomRegions(
            BlockTerrain terrain,
            int worldX,
            int worldZ,
            int minY,
            out bool suppressHellForThisColumn)
        {
            suppressHellForThisColumn = false;

            int cellSize = Math.Max(32, _cfg.RegionCellSize);

            // "Blend width" is the TOTAL width across the border (not per-side).
            // So if BlendWidth=64, you get 32 blocks on each side of the boundary.
            float blendWidth = Math.Max(0f, _cfg.RegionBlendWidth);

            // Used as border sharpness shaping (not distance weighting anymore).
            float power = Math.Max(0.10f, _cfg.RegionBlendPower);

            int cx = FloorDiv(worldX, cellSize);
            int cz = FloorDiv(worldZ, cellSize);

            // Use ONLY the two nearest regions for clean results.
            Feature[] best = FindNearestFeatures(worldX, worldZ, cx, cz, cellSize, 2);
            if (string.IsNullOrWhiteSpace(best[0].BiomeType))
                return;

            string aType = best[0].BiomeType;
            string bType = best[1].BiomeType;

            // -----------------------------
            // SPECIAL CASE: DecentBiome.
            // -----------------------------
            const float DecentRegionBlender = 0.08f; // try 0.35..0.75.

            if (IsDecentBiomeType(aType))
            {
                suppressHellForThisColumn = IsHellSuppressedSurface(aType);

                float b = AdjustBiomeBlenderForVanillaQuirks(aType, DecentRegionBlender);
                GetOrCreateBiome(aType)?.BuildColumn(terrain, worldX, worldZ, minY, b);
                return;
            }

            // If the *secondary* is DecentBiome, ignore it for blending.
            if (IsDecentBiomeType(bType))
                bType = null;

            // If no blend requested or no valid second biome: hard pick A.
            if (blendWidth <= 0f || string.IsNullOrWhiteSpace(bType))
            {
                suppressHellForThisColumn = IsHellSuppressedSurface(aType);

                float b = AdjustBiomeBlenderForVanillaQuirks(aType, 1f);
                GetOrCreateBiome(aType)?.BuildColumn(terrain, worldX, worldZ, minY, b);
                return;
            }

            // ------------------------------------------------------------
            // NEW: Compute blend using signed distance to the true boundary
            // (perpendicular bisector between the two feature points).
            //
            // signedDist < 0 => A side.
            // signedDist > 0 => B side.
            // ------------------------------------------------------------
            float aW, bW;

            bool ok = TryGetBisectorSignedDistance(worldX, worldZ, best[0], best[1], out double signedDist);

            if (ok)
            {
                float half = blendWidth * 0.5f;

                // If we're outside the blend zone, hard pick nearest (keeps interiors stable).
                // Note: best[0] is nearest by construction, but we'll still compute t for the zone.
                if (Math.Abs(signedDist) >= half)
                {
                    bool onASide = signedDist < 0.0;

                    string pick = onASide ? aType : bType;

                    suppressHellForThisColumn = IsHellSuppressedSurface(pick);

                    float b = AdjustBiomeBlenderForVanillaQuirks(pick, 1f);
                    GetOrCreateBiome(pick)?.BuildColumn(terrain, worldX, worldZ, minY, b);
                    return;
                }

                // Map signed distance into t in [0..1] across TOTAL blendWidth.
                // t=0 => A, t=1 => B
                float t = (float)((signedDist + half) / blendWidth);
                t = MathHelper.Clamp(t, 0f, 1f);

                // Shape the transition (power) + smooth it (smoothstep).
                t = ApplySymmetricPower(t, power);
                t = SmoothStep(t);

                bW = t;
                aW = 1f - t;
            }
            else
            {
                // Fallback: Old inverse-distance weighting (should be rare).
                double f1 = Math.Sqrt(best[0].D2);
                double f2 = Math.Sqrt(best[1].D2);

                const double EPS = 1e-6;
                double wa = 1.0 / Math.Pow(f1 + EPS, power);
                double wb = 1.0 / Math.Pow(f2 + EPS, power);
                double sum = wa + wb;

                bW = (sum > 0.0) ? (float)(wb / sum) : 0f;

                // Optional: Smooth the fallback a bit too.
                bW = SmoothStep(MathHelper.Clamp(bW, 0f, 1f));
                aW = 1f - bW;
            }

            // Hell suppression: if either surface biome wants it suppressed, suppress.
            suppressHellForThisColumn =
                IsHellSuppressedSurface(aType) || IsHellSuppressedSurface(bType);

            // Call A then B (matches your ring transition ordering).
            GetOrCreateBiome(aType)?.BuildColumn(terrain, worldX, worldZ, minY,
                AdjustBiomeBlenderForVanillaQuirks(aType, aW));

            GetOrCreateBiome(bType)?.BuildColumn(terrain, worldX, worldZ, minY,
                AdjustBiomeBlenderForVanillaQuirks(bType, bW));
        }

        /// <summary>Finds the K nearest cell features around the current cell (3x3 neighborhood scan).</summary>
        private Feature[] FindNearestFeatures(int worldX, int worldZ, int cellX, int cellZ, int cellSize, int k)
        {
            var best = new Feature[k];
            for (int i = 0; i < k; i++)
            {
                best[i].D2 = double.MaxValue;
                best[i].BiomeType = null;
            }

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = cellX + dx;
                    int cz = cellZ + dz;

                    Feature f = MakeFeature(cx, cz, cellSize);

                    double ddx = worldX - f.X;
                    double ddz = worldZ - f.Z;
                    f.D2 = ddx * ddx + ddz * ddz;

                    InsertBest(best, f);
                }
            }

            return best;
        }

        /// <summary>Inserts candidate feature into the sorted "best" array by D2 (ascending).</summary>
        private void InsertBest(Feature[] best, Feature cand)
        {
            for (int i = 0; i < best.Length; i++)
            {
                if (cand.D2 < best[i].D2)
                {
                    // shift down
                    for (int j = best.Length - 1; j > i; j--)
                        best[j] = best[j - 1];

                    best[i] = cand;
                    return;
                }
            }
        }

        /// <summary>
        /// Creates a deterministic feature point in a given cell.
        /// Notes:
        /// - Uses a simple hash PRNG (no allocations) seeded by world seed + cell coords.
        /// - Picks a biome type from the configured bag (or fallback bag).
        /// </summary>
        private Feature MakeFeature(int cellX, int cellZ, int cellSize)
        {
            // Deterministic per-cell using world seed.
            // Use _worldInfo.Seed if accessible; otherwise use _cfg.SeedOverride / etc.
            int seed = 0;
            try { seed = _worldInfo.Seed; } catch { /* Ignore. */ }

            uint h = Hash2D(cellX, cellZ, seed);

            // Random offset inside cell [0..1).
            float ox = U01(h);
            h = Hash(h ^ 0xB5297A4Du);
            float oz = U01(h);

            // Choose biome for this feature point.
            h = Hash(h ^ 0x68E31DA4u);
            var bag = _cfg.RandomSurfaceBiomeChoices;
            if (bag == null || bag.Count == 0) bag = DefaultRandomRegionBag;

            int idx = (int)(h % (uint)bag.Count);
            string biomeType = bag[idx];

            double fx = (cellX * (double)cellSize) + ox * cellSize;
            double fz = (cellZ * (double)cellSize) + oz * cellSize;

            return new Feature { X = fx, Z = fz, BiomeType = biomeType, D2 = double.MaxValue };
        }

        /// <summary>
        /// Inverse-distance weights for top-K (4-way corners look natural).
        /// NOTE: Presently unused by the "two nearest" signed-boundary blending path,
        /// but kept for experimentation / fallback approaches.
        /// </summary>
        private float[] ComputeWeights(Feature[] best, int k, float power)
        {
            var w = new float[k];
            const double EPS = 1e-6;
            double sum = 0.0;

            for (int i = 0; i < k; i++)
            {
                double d = Math.Sqrt(best[i].D2) + EPS;
                double wi = 1.0 / Math.Pow(d, power);
                w[i] = (float)wi;
                sum += wi;
            }

            if (sum > 0.0)
            {
                float inv = (float)(1.0 / sum);
                for (int i = 0; i < k; i++)
                    w[i] *= inv;
            }

            return w;
        }

        #region Hash Helpers (Fast, Deterministic, No RNG Allocations)

        /// <summary>Mix function for deterministic hashing.</summary>
        private static uint Hash(uint x)
        {
            unchecked
            {
                x += 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>2D hash using (x,z,seed) packed into a single uint stream.</summary>
        private static uint Hash2D(int x, int z, int seed)
        {
            unchecked
            {
                uint h = 0u;
                h ^= (uint)x * 0x8DA6B343u;
                h ^= (uint)z * 0xD8163841u;
                h ^= (uint)seed * 0xCB1AB31Fu;
                return Hash(h);
            }
        }

        /// <summary>Maps a hashed uint to a [0..1) float using 24-bit mantissa fraction.</summary>
        private static float U01(uint h)
        {
            // 24-bit mantissa fraction => [0..1).
            return (h & 0x00FFFFFFu) / 16777216f;
        }
        #endregion

        #region Coordinate Helpers

        /// <summary>Correct tiling for negative world coordinates.</summary>
        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            int r = a % b;
            if (r != 0 && a < 0) q--;
            return q;
        }
        #endregion

        #region Default Bags / Tokens

        private static readonly List<string> DefaultRandomRegionBag = new List<string>
        {
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",
        };

        // Special config token stored in INI for "random ring biome".
        private const string RingRandomToken = "@Random";

        /// <summary>Returns true if the string is treated as the "random ring biome" token.</summary>
        private static bool IsRingRandomToken(string s)
        {
            return !string.IsNullOrWhiteSpace(s) &&
                   (string.Equals(s, RingRandomToken, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, "Random", StringComparison.OrdinalIgnoreCase));
        }

        // Fallback if cfg bag is empty/null.
        private static readonly List<string> DefaultRandomRingBag = new List<string>
        {
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",
        };
        #endregion

        /// <summary>
        /// Resolves a ring biome token into a concrete biome type name.
        /// Notes:
        /// - Supports "@Random"/"Random" as a deterministic bag pick.
        /// - When RandomRingsVaryByPeriod is false, repeats the same random selection each period.
        /// </summary>
        private string ResolveRingBiomeType(string rawType, int coreIndex, int patternIndex)
        {
            if (!IsRingRandomToken(rawType))
                return rawType;

            var bag = _cfg.RandomRingBiomeChoices;
            if (bag == null || bag.Count == 0)
                bag = DefaultRandomRingBag;

            int seed = 0;
            try { seed = _worldInfo.Seed; } catch { /* Ignore. */ }

            int periodKey = _cfg.RandomRingsVaryByPeriod ? patternIndex : 0;

            unchecked
            {
                uint h = 0u;
                h ^= (uint)seed * 0xCB1AB31Fu;
                h ^= (uint)coreIndex * 0x8DA6B343u;
                h ^= (uint)periodKey * 0xD8163841u;
                h = Hash(h ^ 0xA2C79D1Fu);

                int idx = (int)(h % (uint)bag.Count);
                return bag[idx];
            }
        }
        #endregion

        #region Helpers: Hell Suppression Set

        /// <summary>
        /// Returns true if this surface biome should suppress hell/bedrock overlays (guard-rail for special biomes).
        /// </summary>
        private static bool IsHellSuppressedSurface(string biomeTypeName)
        {
            return !string.IsNullOrWhiteSpace(biomeTypeName) &&
                   HellSuppressedSurfaceBiomes.Contains(biomeTypeName);
        }
        #endregion

        #region Seed Override Helper

        /// <summary>
        /// Best-effort seed setter:
        /// - Tries a writable "Seed" property first, then a "Seed" field.
        /// - Swallows exceptions (worldgen should not crash if seed cannot be set this way).
        ///
        /// NOTE:
        /// Some builds store seed in a private backing field (e.g., "_seed") rather than a public property/field.
        /// In those cases, a separate patch (outside this file) typically sets the private field directly.
        /// </summary>
        private static void TrySetSeed(WorldInfo worldInfo, int seed)
        {
            if (worldInfo == null) return;

            try
            {
                var p = worldInfo.GetType().GetProperty("Seed");
                if (p != null && p.CanWrite) { p.SetValue(worldInfo, seed, null); return; }

                var f = worldInfo.GetType().GetField("Seed");
                if (f != null) { f.SetValue(worldInfo, seed); return; }
            }
            catch { /* Swallow. */ }
        }
        #endregion

        #endregion
    }
}