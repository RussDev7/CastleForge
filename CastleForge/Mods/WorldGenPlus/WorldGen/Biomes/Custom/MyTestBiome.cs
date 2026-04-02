/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

/*
NOTES / QUICK CONTEXT
- This is a minimal "rolling hills" biome using Perlin noise to pick a per-column ground height.
- Stratification is intentionally simple: grass (top), dirt (3 blocks), then rock.
- Uses the world seed to initialize Perlin noise (deterministic per world).
- References internal terrain members (e.g., _resetRequested, _blocks). If CastleMinerZ access changes,
  these may need refactoring to supported APIs.
*/

using Microsoft.Xna.Framework;
using DNA.Drawing.Noise;
using System;

namespace DNA.CastleMinerZ.Terrain.WorldBuilders
{
    /// <summary>
    /// Test biome implementation.
    /// Summary: Builds a simple rolling terrain surface (grass/dirt/rock) using Perlin noise.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - Public + sealed so it can be instantiated cross-assembly (e.g., Activator/CreateInstance).
    /// - Intended as a lightweight template for custom biome experiments.
    /// - Blender parameter is currently unused (kept to match base signature).
    /// </remarks>
    // Make it public so Activator/CreateInstance from another assembly can instantiate it.
    public sealed class MyTestBiome : Biome
    {
        #region Constants

        /// <summary>
        /// Noise scale used to sample world-space Perlin noise.
        /// Summary: Controls "feature size" (higher = tighter hills, lower = broader hills).
        /// </summary>
        private const float WorldScale = 0.009375f; // Matches many vanilla biomes.

        #endregion

        #region Fields

        /// <summary>
        /// Perlin noise generator.
        /// Summary: Seeded with the world seed for deterministic terrain.
        /// </summary>
        private readonly PerlinNoise _noise;

        #endregion

        #region Construction

        /// <summary>
        /// Creates the biome using the world seed for deterministic noise.
        /// Summary: Initializes Perlin noise based on <paramref name="worldInfo"/> seed.
        /// </summary>
        public MyTestBiome(WorldInfo worldInfo)
            : base(worldInfo)
        {
            _noise = new PerlinNoise(new Random(worldInfo.Seed));
        }
        #endregion

        #region Biome Overrides

        /// <summary>
        /// Builds a single vertical column for this biome.
        /// Summary: Computes a noise-based surface height, clamps it, then fills blocks up to that height.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - "worldX/worldZ" are the column coordinates in world-space.
        /// - "minY" is the base Y offset used by the terrain builder for this column range.
        /// - "blender" is provided by the world builder for transitions; not used here.
        /// - Early-out respects terrain reset requests to match vanilla build patterns.
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            #region Height Computation (Perlin)

            // Simple rolling ground similar to TestBiome-ish behavior.
            int   freq = 1;
            float n    = 0f;
            const int octaves = 4;

            for (int i = 0; i < octaves; i++)
            {
                n += _noise.ComputeNoise(WorldScale * worldX * freq, WorldScale * worldZ * freq) / freq;
                freq *= 2;
            }

            int groundLimit = 64 + (int)(n * 16f);
            if (groundLimit < 20) groundLimit = 20;
            if (groundLimit > 127) groundLimit = 127;

            int dirtLimit = groundLimit - 3;

            #endregion

            #region Column Fill (Grass / Dirt / Rock)

            for (int y = 0; y <= groundLimit; y++)
            {
                // If you have access to terrain._resetRequested, you can early-out like vanilla.
                if (terrain._resetRequested) return;

                int worldY = y + minY;
                IntVector3 worldPos = new IntVector3(worldX, worldY, worldZ);
                int index = terrain.MakeIndexFromWorldIndexVector(worldPos);

                // Very basic stratification.
                if (y == groundLimit)
                    terrain._blocks[index] = Biome.grassblock;
                else if (y >= dirtLimit)
                    terrain._blocks[index] = Biome.dirtblock;
                else
                    terrain._blocks[index] = Biome.rockblock;
            }
            #endregion
        }
        #endregion
    }
}