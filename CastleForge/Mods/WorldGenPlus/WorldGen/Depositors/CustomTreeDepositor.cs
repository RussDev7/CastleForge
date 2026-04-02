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
    /// Custom tree pass (vanilla-like) with fully configurable density + size + leaves.
    /// Vanilla constants covered:
    /// - treeScale      = 0.4375f
    /// - TreeDescrim    = 0.6f
    /// - TreeHeight     = 5
    /// - heightVarMul   = 9f
    /// - leafRadius     = 3
    /// - leafNoiseScale = 0.5f
    /// - leafCutoff     = 0.25f
    ///
    /// Note: Vanilla tree pass is typically called only inside the inner 10x10 of each chunk
    /// (e.g., local 3..12) to avoid seams on chunk edges.
    /// </summary>
    /// <remarks>
    /// Notes / behavior overview:
    /// - This is a per-column tree attempt: Each (worldX, worldZ) may place at most one tree.
    /// - The pass is intentionally "vanilla-shaped":
    ///   1) Placement gate via Perlin noise threshold (density).
    ///   2) Downward scan for Grass (ground).
    ///   3) Trunk height derived from the same noise sample (size).
    ///   4) Trunk placed upward until blocked.
    ///   5) Leaf canopy carved as a noisy sphere around the trunk top.
    /// - Chunk seam note: If you call this on chunk borders you can get cut-off canopies/trunks.
    /// - Notice the games spelling of original function: 'Depositer'.
    /// </remarks>
    internal sealed class CustomTreeDepositor : Biome
    {
        #region Settings (Parameter Bag)

        /// <summary>
        /// All tunables for trees (covers every constant in vanilla).
        /// </summary>
        /// <remarks>
        /// Design notes:
        /// - This struct is a "dumb" bag of values; clamping/validation is performed in code paths.
        /// - Defaults come from <see cref="Vanilla"/> to mirror the expected vanilla feel.
        /// </remarks>
        internal struct Settings
        {
            #region Tunables (Density)

            // "Density" (how often trees spawn).
            /// <summary>Noise scale applied to (worldX, worldZ) for placement gating.</summary>
            /// <remarks>Summary: Larger values increase noise frequency (more variation in tree placement).</remarks>
            public float TreeScale;        // Vanilla: 0.4375f.

            /// <summary>Placement cutoff; tree attempts only when noise is above this threshold.</summary>
            /// <remarks>Summary: Higher values reduce tree density.</remarks>
            public float TreeThreshold;    // Vanilla: 0.6f (TreeDescrim).

            #endregion

            #region Tunables (Trunk Size)

            // "Size" (how big trees get).
            /// <summary>Base trunk height in blocks before applying noise-based variance.</summary>
            public int   BaseTrunkHeight;  // Vanilla: 5 (TreeHeight).

            /// <summary>Multiplier for noise-derived height variance.</summary>
            /// <remarks>Summary: Controls how strongly (treeNoise - threshold) affects trunk height.</remarks>
            public float HeightVarMul;     // Vanilla: 9f (treeVar = 9f * (treeNoise - 0.6f)).

            #endregion

            #region Tunables (Leaves)

            /// <summary>Leaf canopy radius in blocks (sphere-ish around the trunk top).</summary>
            public int   LeafRadius;       // Vanilla: 3 (TreeWidth).

            /// <summary>Scale applied to 3D noise sampling for leaf density.</summary>
            public float LeafNoiseScale;   // Vanilla: 0.5f (noise = ComputeNoise(worldPos3 * 0.5f)).

            /// <summary>Combined threshold for (leafNoise + distanceBlender).</summary>
            /// <remarks>Summary: Higher values produce sparser leaf canopies.</remarks>
            public float LeafCutoff;       // Vanilla: 0.25f (if noise + distBlender > 0.25f).

            #endregion

            #region Tunables (Ground Scan)

            /// <summary>Starting Y for scanning downward to find Grass.</summary>
            public int   GroundScanStartY; // Vanilla: 124.

            /// <summary>Minimum Y bound for ground scan loop.</summary>
            public int   GroundScanMinY;   // Vanilla: 0.. (loop stops at >0).

            /// <summary>If ground height is <= this value, the tree is aborted.</summary>
            /// <remarks>Summary: Prevents spawning trees on/near the absolute bottom edge.</remarks>
            public int   MinGroundHeight;  // Vanilla: 1 (if <=1 return).

            #endregion

            #region Defaults

            /// <summary>
            /// Returns vanilla-like defaults for this tree depositor.
            /// </summary>
            /// <remarks>
            /// Summary: Single source of truth for default tuning values.
            /// </remarks>
            public static Settings Vanilla()
            {
                return new Settings
                {
                    TreeScale        = 0.4375f,
                    TreeThreshold    = 0.6f,

                    BaseTrunkHeight  = 5,
                    HeightVarMul     = 9f,

                    LeafRadius       = 3,
                    LeafNoiseScale   = 0.5f,
                    LeafCutoff       = 0.25f,

                    GroundScanStartY = 124,
                    GroundScanMinY   = 0,
                    MinGroundHeight  = 1
                };
            }
            #endregion
        }
        #endregion

        #region Fields

        /// <summary>
        /// Shared Perlin noise source used for both placement/height and leaf canopy shaping.
        /// </summary>
        private readonly PerlinNoise _noise;

        /// <summary>
        /// Snapshot of current tuning values for this depositor instance.
        /// </summary>
        private readonly Settings    _s;

        #endregion

        #region Construction

        /// <summary>
        /// Creates a tree depositor for a specific world with a fixed settings snapshot.
        /// </summary>
        /// <param name="worldInfo">World info (seed, etc.).</param>
        /// <param name="settings">Tree tuning values (density, size, leaves, scan bounds).</param>
        public CustomTreeDepositor(WorldInfo worldInfo, Settings settings)
            : base(worldInfo)
        {
            _noise = new PerlinNoise(new Random(worldInfo.Seed));
            _s = settings;
        }
        #endregion

        #region Biome Override (Column Generation)

        /// <summary>
        /// Attempts to place a vanilla-like tree at the specified (worldX, worldZ) column.
        /// </summary>
        /// <remarks>
        /// Core flow (matches your inline comments):
        /// 1) Decide if a tree should attempt here via 2D noise and threshold.
        /// 2) Scan downward for a Grass block to anchor the trunk.
        /// 3) Derive trunk height from (treeNoise - threshold).
        /// 4) Place trunk upward until blocked or out-of-bounds.
        /// 5) Fill leaf canopy as a noisy sphere around the trunk top.
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            // 1) Decide if a tree should attempt here (vanilla: noise(worldX*0.4375, worldZ*0.4375) > 0.6)
            float treeNoise = _noise.ComputeNoise(worldX * _s.TreeScale, worldZ * _s.TreeScale);
            if (treeNoise <= _s.TreeThreshold)
                return;

            // 2) Find grass "ground height" like vanilla (scan downward).
            int groundHeight;
            int startScan = Clamp(_s.GroundScanStartY, 0, 127);
            int minScan   = Clamp(_s.GroundScanMinY, 0, startScan);

            for (groundHeight = startScan; groundHeight > minScan; groundHeight--)
            {
                int worldY = groundHeight + minY;
                var pos    = new IntVector3(worldX, worldY, worldZ);
                if (!TryIndex(terrain, pos, out int idx))
                    continue;

                if (Block.GetTypeIndex(terrain._blocks[idx]) == BlockTypeEnum.Grass)
                    break;
            }

            if (groundHeight <= _s.MinGroundHeight)
                return;

            groundHeight++; // Trunk starts above grass.

            // 3) Compute trunk height (vanilla: 5 + (int)(9f*(treeNoise-0.6f))).
            float treeVar   = _s.HeightVarMul * (treeNoise - _s.TreeThreshold);
            int trunkHeight = _s.BaseTrunkHeight + (int)treeVar;
            if (trunkHeight < 1) trunkHeight = 1;

            // 4) Place trunk upward until blocked.
            for (int y = 0; y < trunkHeight; y++)
            {
                int worldY2 = groundHeight + y + minY;
                var pos2    = new IntVector3(worldX, worldY2, worldZ);
                if (!TryIndex(terrain, pos2, out int idx2))
                {
                    trunkHeight = y;
                    break;
                }

                var existing = Block.GetTypeIndex(terrain._blocks[idx2]);
                if (existing != BlockTypeEnum.Empty && existing != BlockTypeEnum.NumberOfBlocks)
                {
                    trunkHeight = y;
                    break;
                }

                terrain._blocks[idx2] = Biome.LogBlock;
            }

            int endHeight = groundHeight + trunkHeight;

            // 5) Leaves canopy (sphere-ish) with noise + distance blender.
            int R = Math.Max(0, _s.LeafRadius);
            if (R == 0)
                return;

            for (int x = -R; x <= R; x++)
                for (int z = -R; z <= R; z++)
                    for (int y2 = -R; y2 <= R; y2++)
                    {
                        var pos3 = new IntVector3(worldX + x, endHeight + y2 + minY, worldZ + z);
                        if (!TryIndex(terrain, pos3, out int idx3))
                            continue;

                        var existing2 = Block.GetTypeIndex(terrain._blocks[idx3]);
                        if (existing2 != BlockTypeEnum.Empty && existing2 != BlockTypeEnum.NumberOfBlocks)
                            continue;

                        float n           = _noise.ComputeNoise(pos3 * _s.LeafNoiseScale);
                        float dist        = (float)Math.Sqrt(x * x + y2 * y2 + z * z);
                        float distBlender = 1f - (dist / Math.Max(1f, R)); // Vanilla: /3f when R=3.

                        if (n + distBlender > _s.LeafCutoff)
                            terrain._blocks[idx3] = Biome.LeafBlock;
                    }
        }
        #endregion

        #region Utility Helpers

        /// <summary>
        /// Clamps an integer to an inclusive [lo..hi] range.
        /// </summary>
        /// <remarks>Summary: Keeps scan bounds within valid chunk/world Y limits.</remarks>
        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi) ? hi : v;

        /// <summary>
        /// Converts a world position to a terrain block index with bounds safety.
        /// </summary>
        /// <remarks>Summary: Prevents out-of-range access on terrain._blocks.</remarks>
        private static bool TryIndex(BlockTerrain terrain, IntVector3 worldPos, out int idx)
        {
            idx = terrain.MakeIndexFromWorldIndexVector(worldPos);
            return (uint)idx < (uint)terrain._blocks.Length;
        }
        #endregion
    }
}