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

namespace TreeFeller
{
    /// <summary>
    /// Finds connected log / leaf components near a chopped trunk block and removes them
    /// when the structure looks like a natural tree canopy instead of a plain log build.
    /// </summary>
    internal static class TreeFellerSystem
    {
        #region Tuning / Constants

        /// <summary>
        /// Pickup spawn offset so dropped items appear centered within the removed block space.
        /// </summary>
        private static readonly Vector3 PickupOffset = new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>
        /// Neighbor offsets used for connected tree traversal.
        /// Summary: This is a full 3x3x3 neighborhood (26 neighbors), not just 6-axis adjacency.
        /// </summary>
        private static readonly IntVector3[] NeighborOffsets = BuildNeighborOffsets();

        #endregion

        #region Public API

        /// <summary>
        /// Returns whether the provided tool can trigger tree felling.
        /// Summary: Current behavior allows axes and chainsaws.
        /// </summary>
        public static bool IsTreeFellerTool(InventoryItem tool)
        {
            return tool is AxeInventoryItem || tool is ChainsawInventoryItem;
        }

        /// <summary>
        /// Attempts to fell a connected tree structure around the chopped block.
        ///
        /// Flow:
        /// 1) Validate config / tool / network / terrain state.
        /// 2) Gather the connected log / leaf component near the chopped block.
        /// 3) Optionally validate that the component looks like a natural tree.
        /// 4) Order the component for removal.
        /// 5) Remove blocks up to the configured cap and spawn matching drops.
        /// 6) Optionally announce / log the result.
        /// </summary>
        public static void TryFellTree(IntVector3 choppedLocation, InventoryItem tool, LocalNetworkGamer localGamer)
        {
            if (!TreeFeller_Settings.Enabled || tool == null || localGamer == null || !IsTreeFellerTool(tool))
                return;

            BlockTerrain terrain = BlockTerrain.Instance;
            if (terrain == null || !terrain.IsReady)
                return;

            List<TreeCell> component = GatherConnectedTree(choppedLocation, terrain);
            if (component.Count == 0)
                return;

            if (TreeFeller_Settings.RequireNaturalTree && !LooksLikeNaturalTree(component, choppedLocation))
                return;

            List<TreeCell> ordered = OrderForRemoval(component, choppedLocation);
            int removedCount = 0;

            foreach (TreeCell cell in ordered)
            {
                if (removedCount >= TreeFeller_Settings.MaxBlocksToRemove)
                    break;

                if (!IsWorldIndexValid(terrain, cell.Position))
                    continue;

                BlockTypeEnum liveType = terrain.GetBlockWithChanges(cell.Position);
                if (!IsTreeBlock(liveType))
                    continue;

                SpawnDrop(tool, liveType, cell.Position);
                AlterBlockMessage.Send(localGamer, cell.Position, BlockTypeEnum.Empty);
                removedCount++;
            }

            if (removedCount > 0)
            {
                if (TreeFeller_Settings.DoAnnouncement)
                    SendFeedback($"Felled {removedCount} block(s) from tree.", false);

                if (TreeFeller_Settings.DoLogging)
                    Log($"Felled {removedCount} block(s) from tree at ({choppedLocation.X}, {choppedLocation.Y}, {choppedLocation.Z}).");
            }
        }
        #endregion

        #region Tree Discovery

        /// <summary>
        /// Gathers the connected log / leaf component around the chopped location.
        ///
        /// Notes:
        /// - This starts from the surrounding 3x3x3 cube because the original chopped block
        ///   has already been removed by vanilla before this logic runs.
        /// - Search is bounded by MaxTraversalCells and the configured search window limits.
        /// </summary>
        private static List<TreeCell> GatherConnectedTree(IntVector3 choppedLocation, BlockTerrain terrain)
        {
            var visited = new HashSet<IntVector3>();
            var queue = new Queue<IntVector3>();
            var found = new List<TreeCell>();

            // Seed from the 3x3x3 cube around the chopped trunk block because the original trunk
            // block has already been removed by vanilla before our postfix runs.
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0)
                            continue;

                        queue.Enqueue(new IntVector3(choppedLocation.X + dx, choppedLocation.Y + dy, choppedLocation.Z + dz));
                    }
                }
            }

            while (queue.Count > 0 && visited.Count < TreeFeller_Settings.MaxTraversalCells)
            {
                IntVector3 current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                if (!IsInsideSearchWindow(choppedLocation, current))
                    continue;

                if (!IsWorldIndexValid(terrain, current))
                    continue;

                BlockTypeEnum type = terrain.GetBlockWithChanges(current);
                if (!IsTreeBlock(type))
                    continue;

                found.Add(new TreeCell(current, type));

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    queue.Enqueue(current + NeighborOffsets[i]);
                }
            }

            return found;
        }

        /// <summary>
        /// Applies heuristics to decide whether the gathered structure looks like a natural tree.
        ///
        /// Heuristics:
        /// - enough remaining logs
        /// - enough leaves
        /// - canopy sits above the chop point
        /// - leaves are not significantly below the top logs
        /// - enough leaves exist near the canopy
        /// </summary>
        private static bool LooksLikeNaturalTree(List<TreeCell> component, IntVector3 choppedLocation)
        {
            int logCount = 0;
            int leafCount = 0;
            int highestLogY = int.MinValue;
            int highestLeafY = int.MinValue;
            int leavesNearCanopy = 0;

            for (int i = 0; i < component.Count; i++)
            {
                TreeCell cell = component[i];
                if (cell.Type == BlockTypeEnum.Log)
                {
                    logCount++;
                    if (cell.Position.Y > highestLogY)
                        highestLogY = cell.Position.Y;
                }
                else if (cell.Type == BlockTypeEnum.Leaves)
                {
                    leafCount++;
                    if (cell.Position.Y > highestLeafY)
                        highestLeafY = cell.Position.Y;
                }
            }

            if (logCount < TreeFeller_Settings.MinRemainingLogs)
                return false;

            if (leafCount < TreeFeller_Settings.MinLeafCount)
                return false;

            if (highestLeafY < choppedLocation.Y + 1)
                return false;

            if (highestLeafY < highestLogY - 1)
                return false;

            for (int i = 0; i < component.Count; i++)
            {
                TreeCell cell = component[i];
                if (cell.Type == BlockTypeEnum.Leaves && cell.Position.Y >= highestLogY - 2)
                    leavesNearCanopy++;
            }

            if (leavesNearCanopy < TreeFeller_Settings.MinLeavesNearCanopy)
                return false;

            return true;
        }

        /// <summary>
        /// Orders the discovered component for removal.
        /// Summary: Removes leaves first, then higher blocks first, then farther blocks as a final tie-breaker.
        /// </summary>
        private static List<TreeCell> OrderForRemoval(List<TreeCell> component, IntVector3 choppedLocation)
        {
            return component
                .OrderBy(c => c.Type == BlockTypeEnum.Log ? 1 : 0) // remove leaves first
                .ThenByDescending(c => c.Position.Y)                // higher blocks first
                .ThenByDescending(c => IntVector3.DistanceSquared(c.Position, choppedLocation))
                .ToList();
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Returns whether the given block type is considered part of a tree.
        /// Summary: Current behavior includes only logs and leaves.
        /// </summary>
        private static bool IsTreeBlock(BlockTypeEnum type)
        {
            return type == BlockTypeEnum.Log || type == BlockTypeEnum.Leaves;
        }

        /// <summary>
        /// Returns whether a point lies within the configured tree search window.
        /// Summary: Horizontal radius is checked on X/Z independently; vertical range is checked relative to the chop point.
        /// </summary>
        private static bool IsInsideSearchWindow(IntVector3 origin, IntVector3 point)
        {
            int dx = Math.Abs(point.X - origin.X);
            int dz = Math.Abs(point.Z - origin.Z);
            int dy = point.Y - origin.Y;

            if (dx > TreeFeller_Settings.MaxHorizontalRadius || dz > TreeFeller_Settings.MaxHorizontalRadius)
                return false;

            if (dy > TreeFeller_Settings.MaxVerticalSearchUp || dy < -TreeFeller_Settings.MaxVerticalSearchDown)
                return false;

            return true;
        }

        /// <summary>
        /// Returns whether the given world index is valid for the current terrain.
        /// Summary: Converts to local terrain space using worldMin, then checks terrain-local bounds.
        /// </summary>
        private static bool IsWorldIndexValid(BlockTerrain terrain, IntVector3 worldIndex)
        {
            IntVector3 local = IntVector3.Subtract(worldIndex, terrain._worldMin);
            return terrain.IsIndexValid(local);
        }

        /// <summary>
        /// Spawns the drop the tool would normally create when digging the given block.
        /// Summary: Uses vanilla CreatesWhenDug(...) output and PickupManager for consistency.
        /// </summary>
        private static void SpawnDrop(InventoryItem tool, BlockTypeEnum blockType, IntVector3 worldIndex)
        {
            InventoryItem item = tool.CreatesWhenDug(blockType, worldIndex);
            if (item == null)
                return;

            PickupManager.Instance.CreatePickup(item, IntVector3.ToVector3(worldIndex) + PickupOffset, false, false);
        }

        /// <summary>
        /// Builds the 26-neighbor offset table used for connected tree traversal.
        /// Summary: Includes every neighbor in the surrounding 3x3x3 cube except the origin.
        /// </summary>
        private static IntVector3[] BuildNeighborOffsets()
        {
            var offsets = new List<IntVector3>(26);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0)
                            continue;

                        offsets.Add(new IntVector3(dx, dy, dz));
                    }
                }
            }

            return offsets.ToArray();
        }
        #endregion

        #region Private Types

        /// <summary>
        /// Lightweight record for a discovered tree cell.
        /// Summary: Stores both the world position and the live block type captured during discovery.
        /// </summary>
        private readonly struct TreeCell
        {
            public readonly IntVector3 Position;
            public readonly BlockTypeEnum Type;

            public TreeCell(IntVector3 position, BlockTypeEnum type)
            {
                Position = position;
                Type = type;
            }
        }
        #endregion
    }
}