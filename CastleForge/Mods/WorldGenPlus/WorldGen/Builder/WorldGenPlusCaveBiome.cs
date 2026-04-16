/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain.WorldBuilders;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.UI;
using DNA.Drawing.Noise;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus cave overlay that preserves vanilla cave carving / loot behavior
    /// while allowing cave spawner block generation to be toggled separately.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - This intentionally mirrors vanilla <see cref="CaveBiome"/> flow as closely as possible.
    /// - Only the enemy-spawner placement branch is gated; cave carving and loot placement still run normally.
    /// - This keeps the "Caves" overlay toggle separate from the new "Spawners" gameplay toggle.
    /// </remarks>
    internal sealed class WorldGenPlusCaveBiome : Biome
    {
        #region Fields / Constants

        private const float CaveDensity = 0.0625f;

        private readonly bool _enableSpawnerBlocks;
        private readonly PerlinNoise _noiseFunction;

        private int _lootBlockModifier = 5000;
        private int _luckyLootBlockModifier = 10001;
        private int _enemyBlockModifier = 2100;
        private int _emptyBlockCount;

        #endregion

        #region Construction

        /// <summary>
        /// Creates the WorldGenPlus cave overlay wrapper.
        /// </summary>
        /// <param name="worldInfo">Current world info / seed source.</param>
        /// <param name="enableSpawnerBlocks">
        /// When true, vanilla-style cave spawner blocks may be emitted.
        /// When false, cave carving still occurs but no new cave spawner blocks are placed.
        /// </param>
        public WorldGenPlusCaveBiome(WorldInfo worldInfo, bool enableSpawnerBlocks)
            : base(worldInfo)
        {
            _enableSpawnerBlocks = enableSpawnerBlocks;
            _noiseFunction = new PerlinNoise(new Random(worldInfo.Seed));
        }

        #endregion

        #region Cave Generation

        /// <summary>
        /// Chooses which cave spawner block to place when the cave pass hits an enemy-spawn interval.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Mirrors vanilla selection behavior.
        /// - The current pool intentionally matches vanilla cave generation: rare enemy, normal enemy, or alien.
        /// </remarks>
        private int GetEnemyBlock(IntVector3 worldPos, float noise)
        {
            int[] midBlockIDs = new int[]
            {
                Biome.enemyBlockRareOff,
                Biome.enemyBlockOff,
                Biome.alienSpawnOff
            };

            int roll = MathTools.RandomInt(midBlockIDs.Length);
            return midBlockIDs[roll];
        }

        /// <summary>
        /// Builds a single cave overlay column.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Mirrors vanilla cave carving density / cadence.
        /// - Loot and lucky-loot behavior remain unchanged.
        /// - New enemy spawner block placement is skipped when <see cref="_enableSpawnerBlocks"/> is false.
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            bool lastBlockNotEmpty = true;

            for (int y = 0; y < 128; y++)
            {
                int worldY = y + minY;
                IntVector3 worldPos = new IntVector3(worldX, worldY, worldZ);
                int index = terrain.MakeIndexFromWorldIndexVector(worldPos);
                int existing = terrain._blocks[index];

                if (Biome.emptyblock != existing && Biome.uninitblock != existing && Biome.sandBlock != existing)
                {
                    Vector3 wv = worldPos * CaveDensity * new Vector3(1f, 1.5f, 1f);
                    float noise = _noiseFunction.ComputeNoise(wv);
                    noise += _noiseFunction.ComputeNoise(wv * 2f) / 2f;

                    if (noise < -0.35f)
                    {
                        _emptyBlockCount++;
                        terrain._blocks[index] = Biome.emptyblock;

                        if (lastBlockNotEmpty && terrain._blocks[index] != Biome.dirtblock && terrain._blocks[index] != Biome.grassblock)
                        {
                            if (_emptyBlockCount % _lootBlockModifier == 0)
                                terrain._blocks[index] = Biome.lootBlock;

                            if (_enableSpawnerBlocks && _emptyBlockCount % _enemyBlockModifier == 0)
                                terrain._blocks[index] = GetEnemyBlock(worldPos, noise);

                            if (_emptyBlockCount % _luckyLootBlockModifier == 0)
                                terrain._blocks[index] = Biome.luckyLootBlock;

                            lastBlockNotEmpty = false;
                        }
                    }
                }
                else
                {
                    lastBlockNotEmpty = true;
                }
            }
        }

        #endregion

        #region Game-Mode Tuning

        /// <summary>
        /// Applies vanilla cave loot/spawner cadence per game mode.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Mirrors vanilla <see cref="CaveBiome.SetLootModifiersByGameMode"/> values.
        /// - The enemy modifier is still updated even when cave spawners are disabled so flow stays consistent
        ///   if the setting is later re-enabled and the builder is rebuilt.
        /// </remarks>
        public void SetLootModifiersByGameMode()
        {
            switch (CastleMinerZGame.Instance.GameMode)
            {
                case GameModeTypes.Endurance:
                    _lootBlockModifier = 1000000;
                    _luckyLootBlockModifier = 1000000;
                    return;

                case GameModeTypes.Survival:
                    _lootBlockModifier = 20000;
                    _luckyLootBlockModifier = 35000;
                    _enemyBlockModifier = 2000;
                    return;

                case GameModeTypes.DragonEndurance:
                    _lootBlockModifier = 1000000;
                    _luckyLootBlockModifier = 1000000;
                    return;

                case GameModeTypes.Creative:
                    _lootBlockModifier = 1000000;
                    _luckyLootBlockModifier = 1000000;
                    _enemyBlockModifier = 2000;
                    return;

                case GameModeTypes.Exploration:
                    _lootBlockModifier = 2100;
                    _luckyLootBlockModifier = 7000;
                    _enemyBlockModifier = 1000;
                    return;

                case GameModeTypes.Scavenger:
                    _lootBlockModifier = 150;
                    _luckyLootBlockModifier = 1000;
                    _enemyBlockModifier = 200;
                    return;

                default:
                    return;
            }
        }

        #endregion
    }
}
