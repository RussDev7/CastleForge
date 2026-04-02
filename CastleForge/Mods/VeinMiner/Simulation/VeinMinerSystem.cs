/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ;
using System.Linq;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace VeinMiner
{
    /// <summary>
    /// Finds connected blocks of the same ore type near the originally mined block and removes them
    /// in one event-driven pass, spawning the same item output vanilla would normally create.
    /// </summary>
    internal static class VeinMinerSystem
    {
        #region Tuning / Constants

        /// <summary>
        /// Offset used when spawning dropped pickups so they appear centered within the mined block space.
        /// </summary>
        private static readonly Vector3 PickupOffset = new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>
        /// 6-directional adjacency used for connected-vein discovery.
        /// Summary: Veins are searched orthogonally, not diagonally.
        /// </summary>
        private static readonly IntVector3[] NeighborOffsets =
        {
            new IntVector3( 1,  0,  0),
            new IntVector3(-1,  0,  0),
            new IntVector3( 0,  1,  0),
            new IntVector3( 0, -1,  0),
            new IntVector3( 0,  0,  1),
            new IntVector3( 0,  0, -1)
        };
        #endregion

        #region Public API

        /// <summary>
        /// Returns whether the provided tool is allowed to trigger vein mining.
        /// Summary: Current behavior allows pick-type tools only.
        /// </summary>
        public static bool IsVeinMinerTool(InventoryItem tool)
        {
            return tool is PickInventoryItem;
        }

        /// <summary>
        /// Returns whether the provided block type is eligible for vein mining.
        /// Summary: This acts as the ore whitelist for the system.
        /// </summary>
        public static bool IsVeinMinable(BlockTypeEnum type)
        {
            switch (type)
            {
                case BlockTypeEnum.GoldOre:
                case BlockTypeEnum.IronOre:
                case BlockTypeEnum.CopperOre:
                case BlockTypeEnum.CoalOre:
                case BlockTypeEnum.DiamondOre:
                case BlockTypeEnum.Slime:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Attempts to mine the connected vein around an already-mined origin block.
        ///
        /// Flow:
        /// 1) Validate config / tool / network / terrain state.
        /// 2) Gather the connected component of matching ore blocks.
        /// 3) Order them for removal.
        /// 4) Remove extra blocks up to the configured cap and spawn matching drops.
        /// 5) Optionally announce / log the result.
        /// </summary>
        public static void TryMineVein(IntVector3 minedLocation, BlockTypeEnum veinType, InventoryItem tool, LocalNetworkGamer localGamer)
        {
            if (!VeinMiner_Settings.Enabled || tool == null || localGamer == null || !IsVeinMinerTool(tool) || !IsVeinMinable(veinType))
                return;

            BlockTerrain terrain = BlockTerrain.Instance;
            if (terrain == null || !terrain.IsReady)
                return;

            List<IntVector3> component = GatherConnectedVein(minedLocation, veinType, terrain);
            if (component.Count == 0)
                return;

            List<IntVector3> ordered = OrderForMining(component, minedLocation);
            int removedCount = 0;

            foreach (IntVector3 cell in ordered)
            {
                if (removedCount >= VeinMiner_Settings.MaxBlocksToMine)
                    break;

                if (!IsWorldIndexValid(terrain, cell))
                    continue;

                BlockTypeEnum liveType = terrain.GetBlockWithChanges(cell);
                if (liveType != veinType)
                    continue;

                SpawnDrop(tool, liveType, cell);
                AlterBlockMessage.Send(localGamer, cell, BlockTypeEnum.Empty);
                removedCount++;
            }

            if (removedCount > 0)
            {
                if (VeinMiner_Settings.DoAnnouncement)
                    SendFeedback($"Mined {removedCount} extra block(s) from vein.", false);

                if (VeinMiner_Settings.DoLogging)
                    Log($"Mined {removedCount} extra block(s) of {veinType} from vein at ({minedLocation.X}, {minedLocation.Y}, {minedLocation.Z}).");
            }
        }
        #endregion

        #region Vein Discovery

        /// <summary>
        /// Gathers the connected vein component around the original mined block.
        ///
        /// Notes:
        /// - This starts from the six neighboring cells instead of the origin because vanilla
        ///   has already removed the originally mined block before this logic runs.
        /// - Search is bounded by MaxTraversalCells and MaxAxisRadius.
        /// </summary>
        private static List<IntVector3> GatherConnectedVein(IntVector3 minedLocation, BlockTypeEnum veinType, BlockTerrain terrain)
        {
            var visited = new HashSet<IntVector3>();
            var queue   = new Queue<IntVector3>();
            var found   = new List<IntVector3>();

            // Seed from the 6 neighboring cells because the originally mined ore block
            // has already been removed by vanilla before our postfix runs.
            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                queue.Enqueue(minedLocation + NeighborOffsets[i]);
            }

            while (queue.Count > 0 && visited.Count < VeinMiner_Settings.MaxTraversalCells)
            {
                IntVector3 current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                if (!IsInsideSearchWindow(minedLocation, current))
                    continue;

                if (!IsWorldIndexValid(terrain, current))
                    continue;

                BlockTypeEnum liveType = terrain.GetBlockWithChanges(current);
                if (liveType != veinType)
                    continue;

                found.Add(current);

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    queue.Enqueue(current + NeighborOffsets[i]);
                }
            }

            return found;
        }

        /// <summary>
        /// Orders discovered vein cells before mining.
        /// Summary: Prioritizes nearest cells first, then higher cells for tie-breaking.
        /// </summary>
        private static List<IntVector3> OrderForMining(List<IntVector3> component, IntVector3 origin)
        {
            return component
                .OrderBy(c => IntVector3.DistanceSquared(c, origin))
                .ThenByDescending(c => c.Y)
                .ToList();
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Returns whether a point lies within the configured bounded search window around the origin.
        /// Summary: Uses per-axis radius limits instead of spherical distance.
        /// </summary>
        private static bool IsInsideSearchWindow(IntVector3 origin, IntVector3 point)
        {
            int dx = Math.Abs(point.X - origin.X);
            int dy = Math.Abs(point.Y - origin.Y);
            int dz = Math.Abs(point.Z - origin.Z);

            return dx <= VeinMiner_Settings.MaxAxisRadius && dy <= VeinMiner_Settings.MaxAxisRadius && dz <= VeinMiner_Settings.MaxAxisRadius;
        }

        /// <summary>
        /// Returns whether the given world position maps to a valid terrain index.
        /// Summary: Protects all terrain reads from out-of-bounds access.
        /// </summary>
        private static bool IsWorldIndexValid(BlockTerrain terrain, IntVector3 worldIndex)
        {
            return terrain != null && terrain.MakeIndexFromWorldIndexVector(worldIndex) >= 0;
        }

        /// <summary>
        /// Spawns the item drop that the provided tool would normally create for the given block.
        /// Summary: Uses vanilla CreatesWhenDug(...) behavior and PickupManager for consistency.
        /// </summary>
        private static void SpawnDrop(InventoryItem tool, BlockTypeEnum blockType, IntVector3 location)
        {
            if (tool == null)
                return;

            InventoryItem item = tool.CreatesWhenDug(blockType, location);
            if (item == null)
                return;

            PickupManager pickupManager = PickupManager.Instance;
            if (pickupManager == null)
                return;

            pickupManager.CreatePickup(item, IntVector3.ToVector3(location) + PickupOffset, false, false);
        }
        #endregion
    }
}