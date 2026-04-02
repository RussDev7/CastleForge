/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain.WorldBuilders;
using DNA.CastleMinerZ.Terrain;
using DNA.Drawing.Noise;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace WorldGenPlus
{
    /// <summary>
    /// Custom crash-site pass (vanilla-like) with configurable density/shape and slime rules.
    /// Vanilla constants covered:
    /// - worldScale  = 0.0046875f
    /// - GroundPlane = 66
    /// - Noise thresholds (0.5 / 0.55)
    /// - craterDepth formula multipliers (7*20)
    /// - moundHeight formula multipliers (10*20)
    /// - Loop Y range (20..126)
    /// - Carve padding (-10)
    /// - Slime noise offsets/divs/thresholds and the (265 - intblend) rule
    /// </summary>
    /// <remarks>
    /// Notes / behavior overview:
    /// - This is a Biome-based "depositor" that runs per (worldX, worldZ) column.
    /// - A crash-site "activates" for the column when Perlin noise at (worldX, worldZ) exceeds NoiseThreshold.
    /// - The crater is formed by carving below a computed depth and clearing above a computed top cut.
    /// - If enabled and noise is strong enough, a mound of SpaceRock is built in the mid-band.
    /// - Optional slime ore is embedded inside SpaceRock based on an IntNoise field and the blender-driven threshold.
    /// - Bloodstone can be protected from carving (vanilla-ish safety behavior).
    /// - Notice the games spelling of original function: 'Depositer'.
    /// </remarks>
    internal sealed class CustomCrashSiteDepositor : Biome
    {
        #region Settings (Parameter Bag)

        /// <summary>
        /// Full parameter bag controlling crash-site carving, mound creation, and slime placement.
        /// Intended to be INI-backed and snapshotted into the builder.
        /// </summary>
        /// <remarks>
        /// Keep this struct "dumb":
        /// - No validation here beyond what the generation code defensively clamps.
        /// - Defaults come from <see cref="Vanilla"/> to mirror vanilla-like constants.
        /// </remarks>
        internal struct Settings
        {
            #region Crash-Site Placement / Macro-Shape

            public float WorldScale;              // Vanilla: 0.0046875f.
            public float NoiseThreshold;          // Vanilla: 0.5f (crash happens if noise > threshold).

            public int   GroundPlane;             // Vanilla: 66.
            public int   StartY;                  // Vanilla: 20.
            public int   EndYExclusive;           // Vanilla: 126.

            public float CraterDepthMul;          // Vanilla: 7f * 20f = 140f.
            public bool  EnableMound;             // Vanilla: yes.
            public float MoundThreshold;          // Vanilla: 0.55f.
            public float MoundHeightMul;          // Vanilla: 10f * 20f = 200f.

            public int   CarvePadding;            // Vanilla: 10 (the "- 10" in deep carve threshold).
            public bool  ProtectBloodStone;       // Vanilla behavior: don't carve through BloodSToneBlock.

            #endregion

            #region Slime Ore Inside Space Rock

            public bool  EnableSlime;             // Vanilla: yes.
            public int   SlimePosOffset;          // Vanilla: 777.
            public int   SlimeCoarseDiv;          // Vanilla: 2.
            public int   SlimeAdjustCenter;       // Vanilla: 128.
            public int   SlimeAdjustDiv;          // Vanilla: 8.
            public int   SlimeThresholdBase;      // Vanilla: 265.
            public float SlimeBlendToIntBlendMul; // Vanilla: intblend = (int)(blender*10).
            public float SlimeThresholdBlendMul;  // Vanilla: (base - intblend*1).
            public int   SlimeTopPadding;         // Vanilla: 3 (moundHeight - 3).

            #endregion

            #region Defaults

            /// <summary>
            /// Returns the vanilla-like defaults for this crash-site depositor.
            /// </summary>
            /// <remarks>
            /// Summary: Single source of truth for default tuning values.
            /// </remarks>
            public static Settings Vanilla()
            {
                return new Settings
                {
                    WorldScale              = 0.0046875f,
                    NoiseThreshold          = 0.5f,

                    GroundPlane             = 66,
                    StartY                  = 20,
                    EndYExclusive           = 126,

                    CraterDepthMul          = 140f,
                    EnableMound             = true,
                    MoundThreshold          = 0.55f,
                    MoundHeightMul          = 200f,

                    CarvePadding            = 10,
                    ProtectBloodStone       = true,

                    EnableSlime             = true,
                    SlimePosOffset          = 777,
                    SlimeCoarseDiv          = 2,
                    SlimeAdjustCenter       = 128,
                    SlimeAdjustDiv          = 8,
                    SlimeThresholdBase      = 265,
                    SlimeBlendToIntBlendMul = 10f,
                    SlimeThresholdBlendMul  = 1f,
                    SlimeTopPadding         = 3
                };
            }
            #endregion
        }
        #endregion

        #region Fields

        /// <summary>
        /// Perlin noise used to decide whether a crash-site exists at (worldX, worldZ),
        /// and to derive crater depth / mound height from the noise magnitude.
        /// </summary>
        private readonly PerlinNoise _noise;

        /// <summary>
        /// Integer noise used to place slime ore inside SpaceRock.
        /// </summary>
        private readonly IntNoise _slimeNoise;

        /// <summary>
        /// Snapshot of current tuning values for this depositor instance.
        /// </summary>
        private readonly Settings _s;

        #endregion

        #region Construction

        /// <summary>
        /// Creates a crash-site depositor for a specific world with a fixed settings snapshot.
        /// </summary>
        /// <param name="worldInfo">World info (seed, etc.).</param>
        /// <param name="settings">Tuning values for crash-site + slime generation.</param>
        public CustomCrashSiteDepositor(WorldInfo worldInfo, Settings settings)
            : base(worldInfo)
        {
            _noise      = new PerlinNoise(new Random(worldInfo.Seed));
            _slimeNoise = new IntNoise(new Random(worldInfo.Seed));
            _s          = settings;
        }
        #endregion

        #region Biome Override (Column Generation)

        /// <summary>
        /// Performs crash-site carving and optional slime placement for a single (worldX, worldZ) column across Y.
        /// </summary>
        /// <remarks>
        /// Core flow:
        /// 1) Sample Perlin noise at (worldX, worldZ). If <= threshold, do nothing.
        /// 2) Compute craterDepth from (noise - NoiseThreshold) * CraterDepthMul.
        /// 3) Optionally compute moundHeight if noise > MoundThreshold.
        /// 4) Derive vertical cut bands:
        ///    - deepCarveLimit: Below this, carve to empty (optionally protect BloodStone).
        ///    - topCut: At/above this, force empty.
        ///    - Between those (if moundHeight>0): fill SpaceRockBlock.
        /// 5) If enabled: Embed SlimeBlock inside SpaceRockBlock (below the mound top padding),
        ///    using IntNoise and a blender-scaled threshold.
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            float ws    = _s.WorldScale;
            float noise = _noise.ComputeNoise(ws * worldX, ws * worldZ);

            if (noise <= _s.NoiseThreshold)
                return;

            // Vanilla: craterDepth = (noise - 0.5) * 7 * 20.
            int craterDepth = (int)((noise - _s.NoiseThreshold) * _s.CraterDepthMul);
            if (craterDepth < 0) craterDepth = 0;

            int moundHeight = 0;
            if (_s.EnableMound && noise > _s.MoundThreshold)
            {
                moundHeight = (int)((noise - _s.MoundThreshold) * _s.MoundHeightMul);
                if (moundHeight < 0) moundHeight = 0;
                if (moundHeight > craterDepth) moundHeight = craterDepth; // Vanilla: Math.Min(craterDepth, ...).
            }

            int ground = _s.GroundPlane;
            int startY = Math.Max(0, _s.StartY);
            int endY = Math.Max(startY, _s.EndYExclusive);

            // Matches vanilla thresholds:
            // Deep carve if y < (66 - craterDepth - moundHeight - 10).
            int deepCarveLimit = ground - craterDepth - moundHeight - _s.CarvePadding;

            // Top cut if y >= (66 - craterDepth + moundHeight).
            int topCut = ground - craterDepth + moundHeight;

            // Slime threshold uses blender.
            int intblend = (int)(blender * _s.SlimeBlendToIntBlendMul);

            for (int y = startY; y < endY; y++)
            {
                int worldY = y + minY;
                var worldPos = new IntVector3(worldX, worldY, worldZ);
                if (!TryIndex(terrain, worldPos, out int index))
                    continue;

                int belowWorldY = (y - 1) + minY;
                var belowPos = new IntVector3(worldX, belowWorldY, worldZ);
                int belowIndex = terrain.MakeIndexFromWorldIndexVector(belowPos);

                int cur = terrain._blocks[index];
                int below = (uint)belowIndex < (uint)terrain._blocks.Length ? terrain._blocks[belowIndex] : Biome.emptyblock;

                if (y < deepCarveLimit)
                {
                    if (!_s.ProtectBloodStone ||
                        (cur != Biome.BloodSToneBlock && below != Biome.BloodSToneBlock))
                    {
                        terrain._blocks[index] = Biome.emptyblock;
                    }
                }
                else if (moundHeight > 0 && y < topCut)
                {
                    terrain._blocks[index] = Biome.SpaceRockBlock;
                }
                else if (y >= topCut)
                {
                    terrain._blocks[index] = Biome.emptyblock;
                }

                // Slime inside SpaceRock, but not near the very top of the mound.
                if (_s.EnableSlime &&
                    terrain._blocks[index] == Biome.SpaceRockBlock &&
                    moundHeight > 0 &&
                    y < (topCut - _s.SlimeTopPadding))
                {
                    int offset    = _s.SlimePosOffset;
                    var slimePos  = worldPos + new IntVector3(offset, offset, offset);

                    int coarseDiv = Math.Max(1, _s.SlimeCoarseDiv);
                    int n1        = _slimeNoise.ComputeNoise(slimePos / coarseDiv);
                    int n2        = _slimeNoise.ComputeNoise(slimePos);

                    int adjustDiv = Math.Max(1, _s.SlimeAdjustDiv);
                    int combined  = n1 + (n2 - _s.SlimeAdjustCenter) / adjustDiv;

                    int thresh = (int)(_s.SlimeThresholdBase - (intblend * _s.SlimeThresholdBlendMul));
                    if (combined > thresh)
                        terrain._blocks[index] = Biome.SlimeBlock;
                }
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