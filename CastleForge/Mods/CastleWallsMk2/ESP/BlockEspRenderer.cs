/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060 // Silence IDE0060.
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;
using System;
using DNA;

namespace CastleWallsMk2
{
    /// <summary>
    /// Chunk-cached Block ESP renderer.
    ///
    /// Responsibilities:
    /// - Scans nearby chunks for the selected block type (Update).
    /// - Caches matching block positions so we do not rescan every frame.
    /// - Draws cached boxes/tracers during GameScreen.gameScreen_AfterDraw (Draw).
    ///
    /// Notes:
    /// - Cached positions are stored as world/local-space block minimum corners.
    /// - Draw() only renders from cache; it does not scan terrain.
    /// </summary>
    internal static class BlockEspRenderer
    {
        #region Reflection Cache

        private static readonly FieldInfo _fiMainView = AccessTools.Field(typeof(GameScreen), "mainView");
        private static readonly FieldInfo _fiGame     = AccessTools.Field(typeof(GameScreen), "_game");

        #endregion

        #region Tunables

        /// <summary>Block size in CMZ terrain coordinates.</summary>
        private const int ChunkSize = 16;

        /// <summary>Vanilla CMZ world height in blocks.</summary>
        private const int WorldHeight = 128;

        /// <summary>
        /// Vanilla CMZ worlds are typically 384x384 blocks = 24x24 chunks at 16 blocks each.
        /// If your target build differs, adjust this.
        /// </summary>
        private const int WorldChunkCountXZ = 24;

        /// <summary>
        /// How many chunks in each direction to scan around the player.
        /// 2 = 5x5 chunk area centered on player.
        /// </summary>
        private static int ChunkRadius => CastleWallsMk2._blockEspChunkRadValue;

        /// <summary>
        /// Spread chunk scanning cost across frames.
        /// Increase for faster refresh, decrease for lower per-frame cost.
        /// </summary>
        private const int ChunksPerUpdate = 1;

        /// <summary>
        /// Safety cap so very common block selections do not spam too many boxes.
        /// </summary>
        private static int MaxBoxesToDraw => CastleWallsMk2._blockEspMaxBoxesValue;

        #endregion

        #region Cached State

        /// <summary>
        /// Lightweight cached Block ESP entry.
        /// Stores the matched block type plus the block's minimum world/local corner
        /// so draw code can render per-block colors and box/tracer positions.
        /// </summary>
        private struct BlockEspMatch
        {
            public BlockTypeEnum Type;
            public Vector3       Min;

            public BlockEspMatch(BlockTypeEnum type, Vector3 min)
            {
                Type = type;
                Min  = min;
            }
        }

        /// <summary>Chunks still waiting to be scanned.</summary>
        private static readonly Queue<Point> _scanQueue = new Queue<Point>();

        /// <summary>
        /// Per-chunk cached matching block minimum corners.
        /// Key = chunk (X,Z), Value = matching block positions in that chunk.
        /// </summary>
        private static readonly Dictionary<Point, List<BlockEspMatch>> _chunkMatches =
            new Dictionary<Point, List<BlockEspMatch>>();

        /// <summary>Last chunk center we built a scan plan around.</summary>
        private static Point _lastCenterChunk = new Point(int.MinValue, int.MinValue);

        /// <summary>
        /// Dirty flag used when toggles/settings change and we need a full rebuild.
        /// </summary>
        private static bool _dirty = true;

        private static int _lastSelectionStamp = int.MinValue;

        private static int GetSelectionStamp()
        {
            unchecked
            {
                int hash = 17;

                foreach (var type in CastleWallsMk2._blockEspTypes.OrderBy(t => (int)t))
                    hash = (hash * 31) + (int)type;

                return hash;
            }
        }
        #endregion

        #region Public API

        /// <summary>
        /// Marks the Block ESP cache dirty so it will rebuild around the player again.
        /// Call when:
        /// - Block ESP is toggled
        /// - Selected block type changes
        /// - You want to force-refresh cached matches
        /// </summary>
        public static void Invalidate()
        {
            _dirty              = true;
            _scanQueue.Clear();
            _chunkMatches.Clear();
            _lastCenterChunk    = new Point(int.MinValue, int.MinValue);
            _lastSelectionStamp = int.MinValue;
        }

        /// <summary>
        /// Performs lightweight cache maintenance/scanning for Block ESP.
        /// Call this from your Tick loop, not from Draw.
        /// </summary>
        public static void Update()
        {
            if (!CastleWallsMk2._blockEspEnabled)
                return;

            var game    = CastleMinerZGame.Instance;
            var terrain = BlockTerrain.Instance;
            var player  = game?.LocalPlayer;

            if (terrain == null || player == null)
                return;

            // Determine the player's current chunk from their local position.
            IntVector3 centerChunkIV = terrain.GetChunkVectorIndex(player.LocalPosition);
            Point centerChunk        = new Point(centerChunkIV.X, centerChunkIV.Z);

            int selectionStamp    = GetSelectionStamp();
            bool blockTypeChanged = (selectionStamp != _lastSelectionStamp);
            bool chunkChanged     = (centerChunk != _lastCenterChunk);

            // Rebuild the scan plan when:
            // - Settings changed (dirty),
            // - Selected target block changed,
            // - Player moved into a different chunk.
            if (_dirty || blockTypeChanged || chunkChanged)
            {
                RebuildQueue(centerChunk);
                _lastCenterChunk = centerChunk;
                _lastSelectionStamp = selectionStamp;
                _dirty = false;
            }

            // Scan a small number of chunks per update to spread the workload.
            for (int i = 0; i < ChunksPerUpdate && _scanQueue.Count > 0; i++)
            {
                Point chunk = _scanQueue.Dequeue();
                _chunkMatches[chunk] = ScanChunk(chunk.X, chunk.Y, terrain);
            }
        }

        /// <summary>
        /// Draws the currently cached Block ESP matches for the active frame.
        /// Resolves camera/device state from GameScreen, sorts nearby matches first,
        /// then renders per-block boxes and tracers using each match's runtime color.
        /// </summary>
        public static void Draw(GameScreen __instance, object sender, DrawEventArgs e)
        {
            if (!CastleWallsMk2._blockEspEnabled)
                return;

            if (__instance == null || e == null || e.Device == null)
                return;

            var game = (CastleMinerZGame)_fiGame?.GetValue(__instance);
            var mainView = (CameraView)_fiMainView?.GetValue(__instance);

            if (game == null || mainView == null || mainView.Camera == null)
                return;

            var localPlayer = game.LocalPlayer;
            if (localPlayer == null)
                return;

            var device  = e.Device;
            Matrix view = mainView.Camera.View;
            Matrix proj = mainView.Camera.GetProjection(device);

            // Flatten chunk cache into a temporary list.
            // We sort nearest-first so common block types do not overwhelm the screen.
            List<BlockEspMatch> allMatches = new List<BlockEspMatch>(256);
            foreach (var kv in _chunkMatches)
            {
                if (kv.Value != null && kv.Value.Count > 0)
                    allMatches.AddRange(kv.Value);
            }

            if (allMatches.Count == 0)
                return;

            allMatches.Sort((a, b) =>
            {
                float da = Vector3.DistanceSquared(localPlayer.LocalPosition, a.Min);
                float db = Vector3.DistanceSquared(localPlayer.LocalPosition, b.Min);
                return da.CompareTo(db);
            });

            int count = Math.Min(MaxBoxesToDraw, allMatches.Count);

            // Generate tracers at the FPS camera origin.
            var cam = localPlayer.FPSCamera;
            Vector3 tracerStart =
                cam != null
                    ? cam.WorldPosition + cam.LocalToWorld.Forward * 0.05f
                    : localPlayer.WorldPosition + new Vector3(0f, FPSRig.EyePointHeight, 0f);

            for (int i = 0; i < count; i++)
            {
                BlockEspMatch match = allMatches[i];

                Vector3 blockMin = match.Min;
                Vector3 blockMax = blockMin + Vector3.One;
                Vector3 blockCenter = blockMin + new Vector3(0.5f, 0.5f, 0.5f);

                Color tracerColor = CastleWallsMk2.GetBlockEspTracerColor(match.Type);
                Color boxColor = tracerColor; // Or keep a separate box color if you want.

                // Block hitboxes.
                GraphicsDeviceExtensions.DrawWireBox(device, view, proj,
                    blockMin + new Vector3(0.01f, 0.01f, 0.01f),
                    blockMax - new Vector3(0.01f, 0.01f, 0.01f),
                    boxColor, boxColor);

                // Block tracers.
                if (!CastleWallsMk2._blockEspNoTraceEnabled)
                    GraphicsDeviceExtensions.DrawTracer(device, view, proj, tracerStart, blockCenter, tracerColor);
            }
        }
        #endregion

        #region Scan Helpers

        /// <summary>
        /// Rebuilds the nearby chunk scan plan around the supplied center chunk.
        /// Clears prior cached matches because the nearby search window changed.
        /// </summary>
        private static void RebuildQueue(Point centerChunk)
        {
            _scanQueue.Clear();
            _chunkMatches.Clear();

            for (int dz = -ChunkRadius; dz <= ChunkRadius; dz++)
            {
                for (int dx = -ChunkRadius; dx <= ChunkRadius; dx++)
                {
                    int chunkX = centerChunk.X + dx;
                    int chunkZ = centerChunk.Y + dz;

                    if (chunkX < 0 || chunkX >= WorldChunkCountXZ ||
                        chunkZ < 0 || chunkZ >= WorldChunkCountXZ)
                    {
                        continue;
                    }

                    _scanQueue.Enqueue(new Point(chunkX, chunkZ));
                }
            }
        }

        /// <summary>
        /// Scans a single chunk for any currently selected Block ESP types.
        /// Matching blocks are returned as cached BlockEspMatch entries containing
        /// both the block type and its world/local minimum corner for later drawing.
        /// </summary>
        private static List<BlockEspMatch> ScanChunk(int chunkX, int chunkZ, BlockTerrain terrain)
        {
            List<BlockEspMatch> results = new List<BlockEspMatch>();

            if (terrain == null || CastleWallsMk2._blockEspTypes.Count == 0)
                return results;

            int baseX = chunkX * ChunkSize;
            int baseZ = chunkZ * ChunkSize;

            for (int y = 0; y < WorldHeight; y++)
            {
                for (int z = 0; z < ChunkSize; z++)
                {
                    for (int x = 0; x < ChunkSize; x++)
                    {
                        IntVector3 local = new IntVector3(baseX + x, y, baseZ + z);
                        int rawBlock = terrain.GetBlockAt(local);

                        // Block storage may pack flags/light/etc, so compare via type index.
                        BlockTypeEnum type = (BlockTypeEnum)Block.GetTypeIndex(rawBlock);
                        if (!CastleWallsMk2._blockEspTypes.Contains(type))
                            continue;

                        // Convert terrain index back to world/local draw position.
                        Vector3 worldMin = terrain.MakePositionFromIndexVector(local);
                        results.Add(new BlockEspMatch(type, worldMin));
                    }
                }
            }

            return results;
        }
        #endregion
    }
}