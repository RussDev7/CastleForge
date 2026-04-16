/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain.WorldBuilders;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.UI;
using DNA.Drawing.Noise;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus hell-floor overlay that preserves vanilla bloodstone / lava shaping
    /// while allowing hell boss spawner block generation to be toggled separately.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - This intentionally mirrors vanilla <see cref="HellFloorBiome"/> flow as closely as possible.
    /// - Only the new boss-spawner placement path is gated.
    /// - Hell terrain shaping, post-chunk processing flow, and multiplayer block-broadcast behavior stay intact.
    /// </remarks>
    internal sealed class WorldGenPlusHellFloorBiome : Biome
    {
        #region Fields / Constants

        private const int HellHeight = 32;
        private const int LavaLevel = 4;
        private const int MaxHillHeight = 32;
        private const float WorldScale = 0.03125f;
        private const int MaxBossSpawnsDefault = 50;

        private readonly bool _enableBossSpawnerBlocks;
        private readonly object _bossSpawnerLock = new object();
        private readonly PerlinNoise _noiseFunction;

        private List<IntVector3> _bossSpawnerLocs;
        private Random _rnd;
        private int _bossSpawnBlockCountdown;

        #endregion

        #region Construction

        /// <summary>
        /// Creates the WorldGenPlus hell-floor overlay wrapper.
        /// </summary>
        /// <param name="worldInfo">Current world info / seed source.</param>
        /// <param name="enableBossSpawnerBlocks">
        /// When true, vanilla-style hell boss spawner blocks may be emitted.
        /// When false, hell terrain still generates but no new boss spawner blocks are placed.
        /// </param>
        public WorldGenPlusHellFloorBiome(WorldInfo worldInfo, bool enableBossSpawnerBlocks)
            : base(worldInfo)
        {
            _enableBossSpawnerBlocks = enableBossSpawnerBlocks;
            _noiseFunction = new PerlinNoise(new Random(worldInfo.Seed));
            InitializeBossSpawnParameters(worldInfo);
        }

        #endregion

        #region Hell Generation

        /// <summary>
        /// Builds a single hell-floor overlay column.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Mirrors vanilla hell-floor terrain generation.
        /// - Boss spawner placement is routed through <see cref="CheckForBossSpawns"/> and can be disabled independently.
        /// </remarks>
        public override void BuildColumn(BlockTerrain terrain, int worldX, int worldZ, int minY, float blender)
        {
            int freq = 1;
            float noise = 0f;
            int octives = 4;

            for (int i = 0; i < octives; i++)
            {
                noise += _noiseFunction.ComputeNoise(WorldScale * (float)worldX * (float)freq + 200f, WorldScale * (float)worldZ * (float)freq + 200f) / (float)freq;
                freq *= 2;
            }

            int groundlevel = 4 + (int)(noise * 10f) + 3;

            for (int y = 0; y < HellHeight; y++)
            {
                int worldY = y + minY;
                IntVector3 worldPos = new IntVector3(worldX, worldY, worldZ);
                int index = terrain.MakeIndexFromWorldIndexVector(worldPos);

                if (y < groundlevel)
                    terrain._blocks[index] = Biome.BloodSToneBlock;
                else if (y <= LavaLevel)
                    terrain._blocks[index] = Biome.deepLavablock;

                if (_enableBossSpawnerBlocks)
                    CheckForBossSpawns(terrain, worldPos, index, y, groundlevel);
            }
        }

        /// <summary>
        /// Checks whether the current hell floor block should become a new boss spawner.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Mirrors vanilla countdown-based hell boss placement.
        /// - This method is only entered when the setting is enabled, so no boss counters advance while disabled.
        /// </remarks>
        private void CheckForBossSpawns(BlockTerrain terrain, IntVector3 worldPos, int index, int y, int groundlevel)
        {
            if (CastleMinerZGame.Instance.CurrentWorld.HellBossesSpawned >= CastleMinerZGame.Instance.CurrentWorld.MaxHellBossSpawns)
            {
                _bossSpawnBlockCountdown = 0;
                return;
            }

            if (_bossSpawnBlockCountdown != 0 && y == groundlevel && y > LavaLevel)
            {
                _bossSpawnBlockCountdown--;
                if (_bossSpawnBlockCountdown <= 0)
                {
                    CastleMinerZGame.Instance.CurrentWorld.HellBossesSpawned++;
                    _bossSpawnBlockCountdown = GetNextBossBlockCountdown(CastleMinerZGame.Instance.CurrentWorld.HellBossesSpawned);
                    terrain._blocks[index] = Biome.bossSpawnOff;

                    lock (_bossSpawnerLock)
                    {
                        _bossSpawnerLocs.Add(worldPos);
                    }

                    long zsqu = (long)worldPos.Z * (long)worldPos.Z;
                    Math.Sqrt((double)((long)worldPos.X * (long)worldPos.X + zsqu));
                }
            }
        }

        #endregion

        #region Boss Spawner Flow

        /// <summary>
        /// Returns true when the active game mode should process boss spawner networking.
        /// </summary>
        private bool IsBossSpawnerGameMode()
        {
            return CastleMinerZGame.Instance.GameMode == GameModeTypes.Scavenger ||
                   CastleMinerZGame.Instance.GameMode == GameModeTypes.Survival ||
                   CastleMinerZGame.Instance.GameMode == GameModeTypes.Exploration ||
                   CastleMinerZGame.Instance.GameMode == GameModeTypes.Creative;
        }

        /// <summary>
        /// Computes the next countdown distance before another hell boss spawner may appear.
        /// </summary>
        private int GetNextBossBlockCountdown(int spawnCount)
        {
            float randonVariance = 0.2f;
            float distanceScalar = 1.1f;

            if (_rnd != null)
            {
                _rnd.RandomDouble(-(double)randonVariance, (double)randonVariance);
                double finalPercent = 1.0;
                int numberOfBlocksToCountdown = 4000 * (int)Math.Pow((double)(spawnCount + 1), (double)distanceScalar);
                return (int)(finalPercent * (double)numberOfBlocksToCountdown);
            }

            return 0;
        }

        /// <summary>
        /// Initializes countdown state and boss-spawner tracking for this builder instance.
        /// </summary>
        private void InitializeBossSpawnParameters(WorldInfo worldInfo)
        {
            _rnd = new Random(worldInfo.Seed);
            _bossSpawnBlockCountdown = GetNextBossBlockCountdown(CastleMinerZGame.Instance.CurrentWorld.HellBossesSpawned);
            _bossSpawnerLocs = new List<IntVector3>();

            if (CastleMinerZGame.Instance.CurrentWorld.MaxHellBossSpawns == 0)
                CastleMinerZGame.Instance.CurrentWorld.MaxHellBossSpawns = MaxBossSpawnsDefault;
        }

        /// <summary>
        /// Sends generated boss spawner locations back through vanilla-style networking after chunk generation.
        /// </summary>
        private void ProcessBossSpawns()
        {
            if (!_enableBossSpawnerBlocks)
                return;

            if (!IsBossSpawnerGameMode())
                return;

            if (CastleMinerZGame.Instance == null || CastleMinerZGame.Instance.CurrentNetworkSession == null || CastleMinerZGame.Instance.LocalPlayer == null || CastleMinerZGame.Instance.LocalPlayer.Gamer == null || CastleMinerZGame.Instance.LocalPlayer.Gamer.Session == null)
                return;

            List<IntVector3> toSend = null;
            lock (_bossSpawnerLock)
            {
                if (_bossSpawnerLocs.Count > 0)
                {
                    toSend = new List<IntVector3>(_bossSpawnerLocs);
                    _bossSpawnerLocs.Clear();
                }
            }

            if (toSend != null)
            {
                for (int i = 0; i < toSend.Count; i++)
                    AlterBlockMessage.Send((LocalNetworkGamer)CastleMinerZGame.Instance.LocalPlayer.Gamer, toSend[i], BlockTypeEnum.BossSpawnOff);
            }
        }

        /// <summary>
        /// Vanilla-style end-of-chunk hook used by the builder after hell generation completes.
        /// </summary>
        public void PostChunkProcess()
        {
            ProcessBossSpawns();
        }

        #endregion
    }
}
