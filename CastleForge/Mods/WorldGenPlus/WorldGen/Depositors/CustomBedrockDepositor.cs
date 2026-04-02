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
    /// Custom bedrock pass (vanilla-like) with configurable height and noise frequency.
    /// Vanilla: bedRockLevel = 1 + (noise * 3 / 256) => 1..3 blocks.
    /// </summary>
    /// <remarks>
    /// Notes / behavior overview:
    /// - This pass is column-based (worldX, worldZ) and only writes the bottom N blocks as bedrock.
    /// - Bedrock thickness is derived from an IntNoise sample and mapped into a clamped range:
    ///   [MinLevel .. MinLevel + Variance].
    /// - CoordDiv acts like a "frequency" control by scaling the (x,z) coordinates before sampling noise.
    /// - The vanilla-like mapping uses integer math: min + (noise * variance / 256).
    /// - Notice the games spelling of original function: 'Depositer'.
    /// </remarks>
    internal sealed class CustomBedrockDepositor : Biome
    {
        #region Settings (Parameter Bag)

        /// <summary>
        /// All tunables for the bedrock depositor (covers every constant in vanilla).
        /// </summary>
        /// <remarks>
        /// Keep this struct simple and serializable:
        /// - Validation/clamping is done at call sites (BuildColumn).
        /// - Defaults come from <see cref="Vanilla"/> to mirror expected vanilla behavior.
        /// </remarks>
        internal struct Settings
        {
            #region Tunables

            /// <summary>
            /// Noise frequency control. Vanilla uses worldX/worldZ directly => CoordDiv = 1.
            /// </summary>
            /// <remarks>
            /// Summary: Larger values produce broader, slower-changing bedrock thickness bands.
            /// </remarks>
            public int CoordDiv;

            /// <summary>
            /// Minimum bedrock thickness in blocks. Vanilla: 1.
            /// </summary>
            public int MinLevel;

            /// <summary>
            /// Variance range. Vanilla: 3 (yields +0..+2 due to /256 scaling).
            /// </summary>
            /// <remarks>
            /// Summary: Controls the maximum additional thickness contributed by noise.
            /// </remarks>
            public int Variance;

            #endregion

            #region Defaults

            /// <summary>
            /// Returns the vanilla-like defaults for this bedrock depositor.
            /// </summary>
            /// <remarks>
            /// Summary: Single source of truth for default tuning values.
            /// </remarks>
            public static Settings Vanilla()
            {
                return new Settings
                {
                    CoordDiv = 1,
                    MinLevel = 1,
                    Variance = 3
                };
            }
            #endregion
        }
        #endregion

        #region Fields

        /// <summary>
        /// Integer noise source used to vary bedrock thickness across the world.
        /// </summary>
        private readonly IntNoise _noise;

        /// <summary>
        /// Snapshot of current tuning values for this depositor instance.
        /// </summary>
        private readonly Settings _s;

        #endregion

        #region Construction

        /// <summary>
        /// Creates a bedrock depositor for a specific world with a fixed settings snapshot.
        /// </summary>
        /// <param name="worldInfo">World info (
        /// , etc.).</param>
        /// <param name="settings">Tuning values controlling bedrock thickness/frequency.</param>
        public CustomBedrockDepositor(WorldInfo worldInfo, Settings settings)
            : base(worldInfo)
        {
            _noise = new IntNoise(new Random(worldInfo.Seed));
            _s     = settings;
        }
        #endregion

        #region Biome Override (Column Generation)

        /// <summary>
        /// Writes bedrock into the bottom bedRockLevel blocks of the column.
        /// </summary>
        /// <remarks>
        /// Core flow:
        /// 1) Scale coordinates by CoordDiv to control noise frequency.
        /// 2) Sample IntNoise at (sx, sz).
        /// 3) Convert noise into a bedrock thickness using vanilla-like integer mapping.
        /// 4) Clamp thickness to [MinLevel .. MinLevel+Variance].
        /// 5) Fill blocks [0..bedRockLevel) (offset by minY) with bedrock.
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            int coordDiv = Math.Max(1, _s.CoordDiv);
            int sx       = worldX / coordDiv;
            int sz       = worldZ / coordDiv;

            // Vanilla-ish: Noise ∈ [0..255] (typically), then 1 + noise*3/256.
            int noise    = _noise.ComputeNoise(sx, sz);

            int minLevel = Math.Max(0, _s.MinLevel);
            int variance = Math.Max(0, _s.Variance);

            int bedRockLevel = minLevel;
            if (variance > 0)
                bedRockLevel = minLevel + (noise * variance / 256);

            // Clamp to a sensible range (min..min+variance).
            int maxLevel = minLevel + variance;
            if (bedRockLevel < minLevel) bedRockLevel = minLevel;
            if (bedRockLevel > maxLevel) bedRockLevel = maxLevel;

            for (int y = 0; y < bedRockLevel; y++)
            {
                int worldY = y + minY;
                var worldPos = new IntVector3(worldX, worldY, worldZ);
                if (!TryIndex(terrain, worldPos, out int index))
                    continue;

                terrain._blocks[index] = Biome.bedrockBlock;
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