/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain.WorldBuilders;
using DNA.CastleMinerZ.Terrain;
using DNA.CastleMinerZ.UI;
using DNA.Drawing.Noise;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace WorldGenPlus
{
    /// <summary>
    /// Custom ore pass (vanilla-like) with FULL parameterization of all constants:
    /// - Y loop max.
    /// - Blender->intblend scaling.
    /// - All noise divisors, offsets, combine formula.
    /// - All ore thresholds (coal/copper/iron/gold/lava/diamond).
    /// - All depth limits (gold, deep-pass, diamond).
    /// - Loot block rules/thresholds for Survival and Scavenger.
    /// </summary>
    /// <remarks>
    /// Notes / behavior overview:
    /// - This is a Biome-based "depositor" that runs a vertical scan per (worldX, worldZ) column.
    /// - Ores are placed on rock only (Biome.rockblock).
    /// - Loot placement is mode-gated (Survival/Exploration vs Scavenger/Creative; Endurance modes disabled).
    /// - Loot can optionally be applied to non-rock blocks (sand/snow/bloodstone) to mimic vanilla behavior.
    /// - All tuning values are driven by <see cref="Settings"/>, enabling INI-backed control.
    /// - Notice the games spelling of original function: 'Depositer'.
    /// </remarks>
    internal sealed class CustomOreDepositor : Biome
    {
        #region Settings (Parameter Bag)

        /// <summary>
        /// Full parameter bag controlling ore + loot placement.
        /// Intended to be INI-backed and snapshotted into the builder.
        /// </summary>
        /// <remarks>
        /// Keep this struct "dumb":
        /// - No validation here (callers can clamp; code defensively uses Math.Max in a few places).
        /// - Defaults come from <see cref="Vanilla"/> which matches the game's vanilla-like constants.
        /// </remarks>
        internal struct Settings
        {
            #region Column Loop / Blender Behavior

            public int   MaxY;                          // Vanilla: 128.
            public float BlendToIntBlendMul;            // Vanilla: 10f.

            #endregion

            #region Shared Combine Formula For Ore Noises

            public int   NoiseAdjustCenter;             // Vanilla: 128.
            public int   NoiseAdjustDiv;                // Vanilla: 8.

            #endregion

            #region Coal / Copper Pass

            public int   CoalCoarseDiv;                 // Vanilla: 4 (worldPos / 4).
            public int   CoalThresholdBase;             // Vanilla: 255 (noise > 255 - intblend).
            public int   CopperThresholdOffset;         // Vanilla: -5  (noise < intblend - 5).

            #endregion

            #region Iron / Gold Pass

            public IntVector3 IronPosOffset;            // Vanilla: (1000,1000,1000).
            public int   IronCoarseDiv;                 // Vanilla: 3 (ironPos / 3).
            public int   IronThresholdBase;             // Vanilla: 264 (noise > 264 - intblend).
            public int   GoldThresholdOffset;           // Vanilla: -9  (noise < -9 + intblend).
            public int   GoldMaxY;                      // Vanilla: 50  (y < 50).

            #endregion

            #region Deep pass (Lava / Diamonds)

            public int   DeepPassMaxY;                  // Vanilla: 50  (if y < 50 do this pass).
            public IntVector3 DiamondPosOffset;         // Vanilla: (777,777,777).
            public int   DiamondCoarseDiv;              // Vanilla: 2.
            public int   LavaThresholdBase;             // Vanilla: 266 (noise > 266 - intblend).
            public int   DiamondThresholdOffset;        // Vanilla: -11 (noise < -11 + intblend).
            public int   DiamondMaxY;                   // Vanilla: 40  (y < 40).

            #endregion

            #region Loot Blocks

            public bool  LootEnabled;                   // Vanilla: yes (mode-gated).
            public bool  LootOnNonRockBlocks;           // Vanilla: yes for sand/snow/bloodstone.
            public int   LootSandSnowMaxY;              // Vanilla: 60 (survival + scavenger).

            public IntVector3 LootPosOffset;            // Vanilla: (333,333,333).
            public int   LootCoarseDiv;                 // Vanilla: 5   (lootPos / 5).
            public int   LootFineDiv;                   // Vanilla: 2   (lootPos / 2).

            // Survival thresholds.
            public int   LootSurvivalMainThreshold;     // Vanilla: 268 (noise > 268).
            public int   LootSurvivalLuckyThreshold;    // Vanilla: 249 (noise3 > 249).
            public int   LootSurvivalRegularThreshold;  // Vanilla: 145 (noise3 > 145).
            public int   LootLuckyBandMinY;             // Vanilla: 55  (allowed if <55 OR >=100).
            public int   LootLuckyBandMaxYStart;        // Vanilla: 100.

            // Scavenger thresholds.
            public int   LootScavengerTargetMod;        // Vanilla: 1 (if sand/snow && y > 60).
            public int   LootScavengerMainThreshold;    // Vanilla: 267 (noise > 267 + mod).
            public int   LootScavengerLuckyThreshold;   // Vanilla: 250 (noise3 > 250 + mod*3).
            public int   LootScavengerLuckyExtraPerMod; // Vanilla: 3.
            public int   LootScavengerRegularThreshold; // Vanilla: 165.

            #endregion

            #region Defaults

            /// <summary>
            /// Returns the vanilla-like defaults for this depositor.
            /// </summary>
            /// <remarks>
            /// Summary: Single source of truth for default tuning values.
            /// </remarks>
            public static Settings Vanilla()
            {
                return new Settings
                {
                    MaxY                          = 128,
                    BlendToIntBlendMul            = 10f,

                    NoiseAdjustCenter             = 128,
                    NoiseAdjustDiv                = 8,

                    CoalCoarseDiv                 = 4,
                    CoalThresholdBase             = 255,
                    CopperThresholdOffset         = -5,

                    IronPosOffset                 = new IntVector3(1000, 1000, 1000),
                    IronCoarseDiv                 = 3,
                    IronThresholdBase             = 264,
                    GoldThresholdOffset           = -9,
                    GoldMaxY                      = 50,

                    DeepPassMaxY                  = 50,
                    DiamondPosOffset              = new IntVector3(777, 777, 777),
                    DiamondCoarseDiv              = 2,
                    LavaThresholdBase             = 266,
                    DiamondThresholdOffset        = -11,
                    DiamondMaxY                   = 40,

                    LootEnabled                   = true,
                    LootOnNonRockBlocks           = true,
                    LootSandSnowMaxY              = 60,

                    LootPosOffset                 = new IntVector3(333, 333, 333),
                    LootCoarseDiv                 = 5,
                    LootFineDiv                   = 2,

                    LootSurvivalMainThreshold     = 268,
                    LootSurvivalLuckyThreshold    = 249,
                    LootSurvivalRegularThreshold  = 145,
                    LootLuckyBandMinY             = 55,
                    LootLuckyBandMaxYStart        = 100,

                    LootScavengerTargetMod        = 1,
                    LootScavengerMainThreshold    = 267,
                    LootScavengerLuckyThreshold   = 250,
                    LootScavengerLuckyExtraPerMod = 3,
                    LootScavengerRegularThreshold = 165
                };
            }
            #endregion
        }
        #endregion

        #region Fields

        /// <summary>
        /// Perlin-like integer noise source, seeded by world seed.
        /// </summary>
        private readonly IntNoise _noise;

        /// <summary>
        /// Snapshot of current tuning values for this depositor instance.
        /// </summary>
        private readonly Settings _s;

        #endregion

        #region Construction

        /// <summary>
        /// Creates an ore depositor for a specific world with a fixed settings snapshot.
        /// </summary>
        /// <param name="worldInfo">World info (seed, etc.).</param>
        /// <param name="settings">Tuning values for ore + loot generation.</param>
        public CustomOreDepositor(WorldInfo worldInfo, Settings settings)
            : base(worldInfo)
        {
            _noise = new IntNoise(new Random(worldInfo.Seed));
            _s     = settings;
        }
        #endregion

        #region Biome Override (Column Generation)

        /// <summary>
        /// Performs ore + loot placement for a single (worldX, worldZ) column across Y.
        /// </summary>
        /// <remarks>
        /// Core flow per Y:
        /// 1) Resolve index (bounds-safe).
        /// 2) If rock:    Run ore passes + loot (mode-gated).
        /// 3) Optionally: Run loot on sand/snow/bloodstone too (vanilla-ish).
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            int maxY = Math.Max(0, _s.MaxY);
            int intblend = (int)(blender * _s.BlendToIntBlendMul);

            for (int y = 0; y < maxY; y++)
            {
                int worldY = y + minY;
                var worldPos = new IntVector3(worldX, worldY, worldZ);
                if (!TryIndex(terrain, worldPos, out int index))
                    continue;

                // --- Ores (rock only) ---
                if (terrain._blocks[index] == Biome.rockblock)
                {
                    // Coal/Copper noise.
                    int n = CombineNoise(worldPos, _s.CoalCoarseDiv);

                    if (n > (_s.CoalThresholdBase - intblend))
                    {
                        terrain._blocks[index] = Biome.coalBlock;
                    }
                    else if (n < (intblend + _s.CopperThresholdOffset))
                    {
                        terrain._blocks[index] = Biome.copperBlock;
                    }

                    // Loot (mode-gated).
                    GenerateLootBlock(terrain, y, worldPos, index);

                    // Iron/Gold noise (offset).
                    var ironPos = worldPos + _s.IronPosOffset;
                    n = CombineNoise(ironPos, _s.IronCoarseDiv);

                    if (n > (_s.IronThresholdBase - intblend))
                    {
                        terrain._blocks[index] = Biome.ironBlock;
                    }
                    else if (n < (intblend + _s.GoldThresholdOffset) && y < _s.GoldMaxY)
                    {
                        terrain._blocks[index] = Biome.goldBlock;
                    }

                    // Deep pass (lava/diamonds).
                    if (y < _s.DeepPassMaxY)
                    {
                        var diaPos = worldPos + _s.DiamondPosOffset;
                        n = CombineNoise(diaPos, _s.DiamondCoarseDiv);

                        if (n > (_s.LavaThresholdBase - intblend))
                        {
                            terrain._blocks[index] = Biome.surfaceLavablock;
                        }
                        else if (n < (intblend + _s.DiamondThresholdOffset) && y < _s.DiamondMaxY)
                        {
                            terrain._blocks[index] = Biome.diamondsBlock;
                        }
                    }
                }

                // --- Loot on non-rock blocks (vanilla: sand/snow/bloodstone) ---
                if (_s.LootOnNonRockBlocks)
                {
                    int b = terrain._blocks[index];
                    if (b == Biome.sandBlock || b == Biome.snowBlock || b == Biome.BloodSToneBlock)
                        GenerateLootBlock(terrain, y, worldPos, index);
                }
            }
        }
        #endregion

        #region Noise Helpers

        /// <summary>
        /// Computes the combined noise value for ore/loot decisions.
        /// </summary>
        /// <param name="pos">World position to sample.</param>
        /// <param name="coarseDiv">Coarse divisor; clamped to at least 1.</param>
        /// <returns>Combined noise value using coarse + fine sampling with an adjustment curve.</returns>
        /// <remarks>
        /// Combine formula:
        /// - n1 = noise(pos / coarseDiv)  (large-scale blobs).
        /// - n2 = noise(pos)             (fine detail).
        /// - return n1 + (n2 - center) / div.
        /// </remarks>
        private int CombineNoise(IntVector3 pos, int coarseDiv)
        {
            int cd = Math.Max(1, coarseDiv);
            int n1 = _noise.ComputeNoise(pos / cd);
            int n2 = _noise.ComputeNoise(pos);

            int ad = Math.Max(1, _s.NoiseAdjustDiv);
            return n1 + (n2 - _s.NoiseAdjustCenter) / ad;
        }
        #endregion

        #region Loot Generation (Mode-Gated)

        /// <summary>
        /// Entry point for loot placement. Respects <see cref="Settings.LootEnabled"/> and game mode.
        /// </summary>
        /// <remarks>
        /// Mode behavior:
        /// - Endurance / DragonEndurance: Disabled (returns immediately).
        /// - Survival / Exploration:      Uses Survival thresholds.
        /// - Creative / Scavenger:        Uses Scavenger thresholds.
        /// </remarks>
        private void GenerateLootBlock(BlockTerrain terrain, int worldLevel, IntVector3 worldPos, int index)
        {
            if (!_s.LootEnabled)
                return;

            switch (CastleMinerZGame.Instance.GameMode)
            {
                case GameModeTypes.Endurance:
                case GameModeTypes.DragonEndurance:
                    return;

                case GameModeTypes.Survival:
                    GenerateLootBlockSurvivalMode(terrain, worldLevel, worldPos, index);
                    return;

                case GameModeTypes.Creative:
                case GameModeTypes.Scavenger:
                    GenerateLootBlockScavengerMode(terrain, worldLevel, worldPos, index);
                    return;

                case GameModeTypes.Exploration:
                    GenerateLootBlockSurvivalMode(terrain, worldLevel, worldPos, index);
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Loot placement rules for Survival / Exploration.
        /// </summary>
        /// <remarks>
        /// - Sand/Snow above LootSandSnowMaxY: no loot (vanilla safety).
        /// - Uses a "main" combined noise gate plus a secondary fine noise (n3) to pick Lucky vs Regular.
        /// - Lucky loot is restricted to a vertical "band" rule (below MinY OR above MaxYStart).
        /// </remarks>
        private void GenerateLootBlockSurvivalMode(BlockTerrain terrain, int worldLevel, IntVector3 worldPos, int index)
        {
            // Vanilla: Sand/snow above 60 -> no loot.
            int cur = terrain._blocks[index];
            if ((cur == Biome.sandBlock || cur == Biome.snowBlock) && worldLevel > _s.LootSandSnowMaxY)
                return;

            var lootPos = worldPos + _s.LootPosOffset;

            int main    = CombineNoise(lootPos, _s.LootCoarseDiv);
            int fineDiv = Math.Max(1, _s.LootFineDiv);
            int n3      = _noise.ComputeNoise(lootPos / fineDiv);

            if (main > _s.LootSurvivalMainThreshold)
            {
                bool luckyBandOk = (worldLevel < _s.LootLuckyBandMinY) || (worldLevel >= _s.LootLuckyBandMaxYStart);

                if (n3 > _s.LootSurvivalLuckyThreshold && luckyBandOk)
                {
                    terrain._blocks[index] = Biome.luckyLootBlock;
                    return;
                }

                if (n3 > _s.LootSurvivalRegularThreshold)
                    terrain._blocks[index] = Biome.lootBlock;
            }
        }

        /// <summary>
        /// Loot placement rules for Scavenger / Creative.
        /// </summary>
        /// <remarks>
        /// - Applies an optional "target mod" when in sand/snow above LootSandSnowMaxY (vanilla-ish scavenger tuning).
        /// - Thresholds are offset by the mod, and Lucky has an additional multiplier per mod step.
        /// </remarks>
        private void GenerateLootBlockScavengerMode(BlockTerrain terrain, int worldLevel, IntVector3 worldPos, int index)
        {
            int noiseTargetMod = 0;

            int cur = terrain._blocks[index];
            if ((cur == Biome.sandBlock || cur == Biome.snowBlock) && worldLevel > _s.LootSandSnowMaxY)
                noiseTargetMod = _s.LootScavengerTargetMod;

            var lootPos = worldPos + _s.LootPosOffset;

            int main    = CombineNoise(lootPos, _s.LootCoarseDiv);
            int fineDiv = Math.Max(1, _s.LootFineDiv);
            int n3      = _noise.ComputeNoise(lootPos / fineDiv);

            if (main > (_s.LootScavengerMainThreshold + noiseTargetMod))
            {
                int luckyThresh = _s.LootScavengerLuckyThreshold + noiseTargetMod * _s.LootScavengerLuckyExtraPerMod;
                if (n3 > luckyThresh)
                {
                    terrain._blocks[index] = Biome.luckyLootBlock;
                    return;
                }

                if (n3 > _s.LootScavengerRegularThreshold)
                    terrain._blocks[index] = Biome.lootBlock;
            }
        }
        #endregion

        #region Indexing Helpers

        /// <summary>
        /// Converts a world position to a terrain block index with bounds safety.
        /// </summary>
        /// <remarks>
        /// Summary: Prevents out-of-range access on terrain._blocks.
        /// </remarks>
        private static bool TryIndex(BlockTerrain terrain, IntVector3 worldPos, out int idx)
        {
            idx = terrain.MakeIndexFromWorldIndexVector(worldPos);
            return (uint)idx < (uint)terrain._blocks.Length;
        }
        #endregion
    }
}