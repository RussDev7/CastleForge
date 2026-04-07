/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace PhysicsEngine
{
    /// <summary>
    /// Host-authoritative lava simulation.
    ///
    /// Design:
    /// - Incremental frontier-based flow, not global desired-set recompute.
    /// - Gravity first.
    /// - Sideways spread only from supported cells.
    /// - Sideways spread is bounded by horizontal flow distance.
    /// - Downward drops reset horizontal distance so lava can continue down long hills.
    /// - Multiple lava placements do not compete for a global "full rebuild" result.
    ///
    /// Notes:
    /// - This is still a full-voxel approximation because CMZ terrain stores block IDs only.
    /// - This version prioritizes stable forward flow over aggressive global retraction.
    ///   Orphan cleanup is conservative and local.
    /// </summary>
    internal static class LavaPhysics
    {
        #region Tunables

        // Tunables now come from PEConfig / PhysicsEngine_Settings at runtime.
        // See Startup/PEConfig.cs.

        #endregion

        #region State

        #region State Types

        /// <summary>
        /// A queued frontier node for lava simulation.
        /// Summary: Tracks the world position plus the current horizontal flow distance budget.
        /// </summary>
        private struct FlowNode
        {
            public IntVector3 Position;
            public int HorizontalDistance;

            public FlowNode(IntVector3 position, int horizontalDistance)
            {
                Position = position;
                HorizontalDistance = horizontalDistance;
            }
        }
        #endregion

        #region Tracked Simulation Collections

        // Manually placed lava sources.
        private static readonly HashSet<IntVector3> _sources = new HashSet<IntVector3>();

        // All lava cells currently managed by this simulation (sources + flowed cells).
        private static readonly HashSet<IntVector3> _ownedLava = new HashSet<IntVector3>();

        // Non-source managed cells store their current best horizontal distance.
        private static readonly Dictionary<IntVector3, int> _flowDistance = new Dictionary<IntVector3, int>();

        // Incremental frontier.
        private static readonly Queue<FlowNode> _frontier = new Queue<FlowNode>();

        // Best queued distance currently waiting for a given position.
        // This avoids stale / worse duplicate queue entries from dominating the frontier.
        private static readonly Dictionary<IntVector3, int> _queuedDistance = new Dictionary<IntVector3, int>();

        // Pending writes so the solver can reason about its own changes immediately.
        private static readonly HashSet<IntVector3> _pendingAddWrites = new HashSet<IntVector3>();
        private static readonly HashSet<IntVector3> _pendingRemoveWrites = new HashSet<IntVector3>();

        // Pending add flow distance to apply when the add is confirmed.
        private static readonly Dictionary<IntVector3, int> _pendingAddDistance = new Dictionary<IntVector3, int>();

        // Cells participating in the current live simulation run.
        // This is what MaxOwnedCells should cap.
        private static readonly HashSet<IntVector3> _activeLava = new HashSet<IntVector3>();

        // Cells temporarily blocked from immediate refill after being mined/removed.
        private static readonly Dictionary<IntVector3, long> _refillBlockedUntilMs =
            new Dictionary<IntVector3, long>();

        private static long _simulationClockMs = 0;

        #endregion

        #region Direction Constants / Timing

        private static readonly IntVector3 Down = new IntVector3(0, -1, 0);
        private static readonly IntVector3 Up = new IntVector3(0, 1, 0);

        private static readonly IntVector3[] HorizontalNeighbors = new IntVector3[]
        {
            new IntVector3( 1, 0, 0),
            new IntVector3(-1, 0, 0),
            new IntVector3( 0, 0, 1),
            new IntVector3( 0, 0,-1)
        };

        private static TimeSpan _accumulator = TimeSpan.Zero;

        #endregion

        #endregion

        #region Public API

        /// <summary>
        /// Clears all runtime lava simulation state.
        /// Summary: Resets tracked sources, owned cells, queued frontier data, pending writes, and timing.
        /// </summary>
        public static void Reset()
        {
            _sources.Clear();
            _ownedLava.Clear();
            _flowDistance.Clear();
            _frontier.Clear();
            _queuedDistance.Clear();
            _pendingAddWrites.Clear();
            _pendingRemoveWrites.Clear();
            _pendingAddDistance.Clear();
            _accumulator = TimeSpan.Zero;
            _activeLava.Clear();
            _refillBlockedUntilMs.Clear();
            _simulationClockMs = 0;
        }

        /// <summary>
        /// Returns whether this instance is allowed to drive the lava simulation.
        /// Summary: Offline & online worlds simulate locally.
        /// </summary>
        public static bool CanDriveSimulation()
        {
            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (game == null)
                return false;

            // Valid instance, allow the simulation.
            return true;
        }

        /// <summary>
        /// Returns true when the given block type is one of the lava block variants.
        /// Summary: This mod treats both SurfaceLava and DeepLava as simulated lava.
        /// </summary>
        public static bool IsLava(BlockTypeEnum blockType)
        {
            return blockType == BlockTypeEnum.SurfaceLava || blockType == BlockTypeEnum.DeepLava;
        }

        /// <summary>
        /// Advances the lava simulation on its configured interval.
        /// Summary: Accumulates game time and executes at most one simulation step per call.
        /// </summary>
        public static void Tick(GameTime gameTime)
        {
            if (!CanDriveSimulation() || !PhysicsEngine_Settings.Enabled)
                return;

            if (BlockTerrain.Instance == null || !BlockTerrain.Instance.IsReady)
                return;

            _accumulator       += gameTime.ElapsedGameTime;
            _simulationClockMs += (long)gameTime.ElapsedGameTime.TotalMilliseconds;
            ReleaseExpiredRefillBlocks();

            int guard = 0;
            TimeSpan stepInterval = TimeSpan.FromMilliseconds(PhysicsEngine_Settings.StepIntervalMs);
            while (_accumulator >= stepInterval && guard < 1)
            {
                _accumulator -= stepInterval;
                StepSimulation();
                guard++;
            }
        }

        /// <summary>
        /// Registers a user-placed lava source.
        /// Summary: Claims ownership of a manually placed lava cell and seeds the frontier from it.
        /// The terrain write itself is performed by vanilla / the normal network path.
        /// </summary>
        public static void RegisterManualSource(IntVector3 worldIndex, BlockTypeEnum placedType)
        {
            if (!CanDriveSimulation() || !PhysicsEngine_Settings.Enabled)
                return;

            if (!IsLava(placedType))
                return;

            if (!IsWorldIndexValid(worldIndex))
                return;

            if (_sources.Add(worldIndex))
            {
                if (PhysicsEngine_Settings.DoAnnouncement)
                    SendFeedback($"Lava source added.", false);

                if (PhysicsEngine_Settings.DoLogging)
                    Log($"Lava source added at {worldIndex}.");
            }

            _ownedLava.Add(worldIndex);
            _flowDistance[worldIndex] = 0;
            _activeLava.Add(worldIndex);
            Enqueue(worldIndex, 0);
        }

        /// <summary>
        /// Observes all block changes that pass through the game.
        ///
        /// Uses:
        /// - Adopt remote manual lava placements as sources on the host.
        /// - Stop tracking cells when they are dug out or replaced.
        /// - Confirm writes emitted by the simulation.
        /// </summary>
        public static void OnObservedBlockChange(IntVector3 worldIndex, BlockTypeEnum newType)
        {
            if (!CanDriveSimulation() || !PhysicsEngine_Settings.Enabled)
                return;

            bool wasPendingAdd = _pendingAddWrites.Remove(worldIndex);
            bool wasPendingRemove = _pendingRemoveWrites.Remove(worldIndex);

            bool hadPendingDistance = _pendingAddDistance.TryGetValue(worldIndex, out int pendingDistance);
            if (hadPendingDistance)
                _pendingAddDistance.Remove(worldIndex);

            if (!IsLava(newType))
            {
                // Confirmed removal for a simulation-owned cell.
                if (wasPendingRemove)
                {
                    _ownedLava.Remove(worldIndex);
                    _activeLava.Remove(worldIndex);
                    _flowDistance.Remove(worldIndex);
                    EnqueueNeighborsForRecheck(worldIndex);
                    return;
                }

                bool removedSource = _sources.Remove(worldIndex);
                bool removedOwned = _ownedLava.Remove(worldIndex);

                _activeLava.Remove(worldIndex);
                _flowDistance.Remove(worldIndex);

                if (removedSource)
                {
                    if (PhysicsEngine_Settings.DoAnnouncement)
                        SendFeedback($"Lava source removed.", false);

                    if (PhysicsEngine_Settings.DoLogging)
                        Log($"Lava source removed at {worldIndex}.");
                }

                if (removedSource || removedOwned)
                {
                    BlockRefillTemporarily(worldIndex);
                    EnqueueNeighborsForRecheck(worldIndex);
                }

                return;
            }

            // Confirmed simulation add.
            if (wasPendingAdd)
            {
                _ownedLava.Add(worldIndex);
                _activeLava.Add(worldIndex);

                if (!_sources.Contains(worldIndex))
                    _flowDistance[worldIndex] = hadPendingDistance ? pendingDistance : 0;

                Enqueue(worldIndex, GetKnownDistance(worldIndex, 0));
                return;
            }

            // Already tracked lava just needs to be re-queued.
            if (_sources.Contains(worldIndex) || _ownedLava.Contains(worldIndex))
            {
                _ownedLava.Add(worldIndex);
                _activeLava.Add(worldIndex);
                Enqueue(worldIndex, GetKnownDistance(worldIndex, 0));
                return;
            }

            // Otherwise this is most likely a real user/manual lava placement from a remote player.
            RegisterManualSource(worldIndex, newType);
        }
        #endregion

        #region Simulation Core

        /// <summary>
        /// Executes a single lava simulation step.
        /// Summary: Processes queued frontier cells until hitting the configured write/evaluation budgets.
        /// </summary>
        private static void StepSimulation()
        {
            if (_frontier.Count == 0)
            {
                EndSimulationIfIdle();
                return;
            }

            int evaluations = 0;
            int writes = 0;

            while (writes < PhysicsEngine_Settings.MaxBlockWritesPerStep &&
                   evaluations < PhysicsEngine_Settings.MaxCellEvaluationsPerStep &&
                   TryDequeueNext(out FlowNode node))
            {
                evaluations++;

                if (!ShouldSimulateCell(node, out int currentDistance))
                    continue;

                writes += FlowFromCell(node.Position, currentDistance, writes);
            }

            EndSimulationIfIdle();
        }

        /// <summary>
        /// Pops the next valid frontier node.
        /// Summary: Skips stale queue entries when a better distance for the same cell is already known.
        /// </summary>
        private static bool TryDequeueNext(out FlowNode node)
        {
            while (_frontier.Count > 0)
            {
                node = _frontier.Dequeue();

                if (!_queuedDistance.TryGetValue(node.Position, out int bestQueuedDistance))
                    continue;

                if (bestQueuedDistance != node.HorizontalDistance)
                    continue;

                _queuedDistance.Remove(node.Position);
                return true;
            }

            node = default;
            return false;
        }

        /// <summary>
        /// Validates whether a queued lava cell should still be simulated.
        /// Summary: Rejects invalid, removed, or unfed cells and attempts local cleanup when appropriate.
        /// </summary>
        private static bool ShouldSimulateCell(FlowNode node, out int currentDistance)
        {
            currentDistance = 0;

            if (!IsWorldIndexValid(node.Position))
                return false;

            if (_pendingRemoveWrites.Contains(node.Position))
                return false;

            BlockTypeEnum current = GetEffectiveBlock(node.Position);
            if (!IsLava(current))
            {
                _ownedLava.Remove(node.Position);
                _flowDistance.Remove(node.Position);
                return false;
            }

            currentDistance = GetKnownDistance(node.Position, node.HorizontalDistance);

            if (!_sources.Contains(node.Position) && !IsCellFed(node.Position, currentDistance))
            {
                TryRemoveOwnedCell(node.Position);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to flow from one lava cell.
        ///
        /// Returns the number of block writes performed from this cell.
        /// </summary>
        private static int FlowFromCell(IntVector3 cell, int currentDistance, int writesAlreadySpent)
        {
            int writes = 0;

            IntVector3 below = IntVector3.Add(cell, Down);

            // Gravity first.
            if (CanContinueDownward(below))
            {
                if (TryAdvanceInto(below, 0, out bool createdBelow, out bool improvedBelow))
                {
                    if (createdBelow)
                        writes++;

                    // Only wake the target if something actually changed.
                    if (createdBelow || improvedBelow)
                        Enqueue(below, 0);
                }

                return writes;
            }

            // Prefer edge spills that immediately continue downhill.
            for (int i = 0; i < HorizontalNeighbors.Length; i++)
            {
                if (writesAlreadySpent + writes >= PhysicsEngine_Settings.MaxBlockWritesPerStep)
                    return writes;

                IntVector3 lateral = IntVector3.Add(cell, HorizontalNeighbors[i]);
                IntVector3 lateralBelow = IntVector3.Add(lateral, Down);

                if (!IsWorldIndexValid(lateral) || !IsWorldIndexValid(lateralBelow))
                    continue;

                if (GetEffectiveBlock(lateral) != BlockTypeEnum.Empty)
                    continue;

                if (!CanContinueDownward(lateralBelow))
                    continue;

                if (TryAdvanceInto(lateral, 0, out bool createdEdge, out bool improvedEdge))
                {
                    if (createdEdge)
                        writes++;

                    if (createdEdge || improvedEdge)
                        Enqueue(lateral, 0);
                }
            }

            // Flat shelf spreading only from supported cells.
            if (currentDistance >= PhysicsEngine_Settings.MaxHorizontalFlowDistance)
                return writes;

            for (int i = 0; i < HorizontalNeighbors.Length; i++)
            {
                if (writesAlreadySpent + writes >= PhysicsEngine_Settings.MaxBlockWritesPerStep)
                    break;

                IntVector3 lateral = IntVector3.Add(cell, HorizontalNeighbors[i]);

                if (TryAdvanceInto(lateral, currentDistance + 1, out bool createdSide, out bool improvedSide))
                {
                    if (createdSide)
                        writes++;

                    if (createdSide || improvedSide)
                        Enqueue(lateral, currentDistance + 1);
                }
            }

            return writes;
        }

        /// <summary>
        /// Returns true if the lava simulation still has queued frontier, distance, or pending write work remaining.
        /// </summary>
        private static bool HasLiveSimulationWork()
        {
            return _frontier.Count            > 0
                || _queuedDistance.Count      > 0
                || _pendingAddWrites.Count    > 0
                || _pendingRemoveWrites.Count > 0;
        }

        /// <summary>
        /// Ends the current lava simulation run when idle cleanup is enabled and no active work remains.
        /// </summary>
        private static void EndSimulationIfIdle()
        {
            if (!PhysicsEngine_Settings.EndSimulationWhenIdle)
                return;

            if (HasLiveSimulationWork())
                return;

            _frontier.Clear();
            _queuedDistance.Clear();
            _activeLava.Clear();

            if (PhysicsEngine_Settings.DoLogging)
                Log("Lava simulation ended: frontier empty and no pending writes remain.");
        }
        #endregion

        #region Refill Cooldown Helpers

        /// <summary>
        /// Temporarily blocks a removed lava cell from being refilled immediately,
        /// unless KeepLavaAlive is enabled or the cooldown is disabled.
        /// </summary>
        private static void BlockRefillTemporarily(IntVector3 worldIndex)
        {
            if (PhysicsEngine_Settings.KeepLavaAlive)
                return;

            int delayMs = PhysicsEngine_Settings.RemovedCellRespawnDelayMs;
            if (delayMs <= 0)
                return;

            _refillBlockedUntilMs[worldIndex] = _simulationClockMs + delayMs;
        }

        /// <summary>
        /// Returns true if the given cell is still within its temporary no-refill cooldown window.
        /// </summary>
        private static bool IsRefillBlocked(IntVector3 worldIndex)
        {
            if (PhysicsEngine_Settings.KeepLavaAlive)
                return false;

            return _refillBlockedUntilMs.TryGetValue(worldIndex, out long untilMs)
                && untilMs > _simulationClockMs;
        }

        /// <summary>
        /// Releases any expired refill cooldown entries and rechecks nearby lava so flow can resume naturally.
        /// </summary>
        private static void ReleaseExpiredRefillBlocks()
        {
            if (_refillBlockedUntilMs.Count == 0)
                return;

            List<IntVector3> expired = null;

            foreach (var kvp in _refillBlockedUntilMs)
            {
                if (kvp.Value <= _simulationClockMs)
                {
                    if (expired == null)
                        expired = new List<IntVector3>();

                    expired.Add(kvp.Key);
                }
            }

            if (expired == null)
                return;

            for (int i = 0; i < expired.Count; i++)
            {
                IntVector3 cell = expired[i];
                _refillBlockedUntilMs.Remove(cell);

                // Wake the nearby lava back up now that refill is allowed again.
                EnqueueNeighborsForRecheck(cell);
            }
        }
        #endregion

        #region Flow Helpers

        /// <summary>
        /// Attempts to advance lava into the target cell.
        /// Summary: Creates new lava in empty space or improves the stored flow distance for managed lava.
        /// </summary>
        private static bool TryAdvanceInto(
            IntVector3 worldIndex,
            int horizontalDistance,
            out bool created,
            out bool improved)
        {
            created = false;
            improved = false;

            if (!IsWorldIndexValid(worldIndex))
                return false;

            // Already-managed lava: only count as "interesting" if the flow distance improved.
            if (IsManagedLavaCell(worldIndex))
            {
                _ownedLava.Add(worldIndex);
                _activeLava.Add(worldIndex);

                if (!_sources.Contains(worldIndex))
                {
                    int oldDistance = GetKnownDistance(worldIndex, int.MaxValue);
                    if (horizontalDistance < oldDistance)
                    {
                        _flowDistance[worldIndex] = horizontalDistance;
                        improved = true;
                    }
                }

                return true;
            }

            if (IsRefillBlocked(worldIndex))
                return false;

            if (GetEffectiveBlock(worldIndex) != BlockTypeEnum.Empty)
                return false;

            if (_activeLava.Count >= PhysicsEngine_Settings.MaxOwnedCells)
                return false;

            if (!ApplyLavaBlock(worldIndex, horizontalDistance))
                return false;

            _activeLava.Add(worldIndex);

            // Let confirmation own the block for real.
            // Pending distance is already tracked in _pendingAddDistance.
            created = true;
            return true;
        }

        /// <summary>
        /// Determines whether a non-source lava cell is still fed by a valid neighbor path.
        /// Summary: Vertical feed keeps falling columns alive; supported horizontal neighbors sustain shelves.
        /// </summary>
        private static bool IsCellFed(IntVector3 cell, int currentDistance)
        {
            if (_sources.Contains(cell))
                return true;

            // Vertical feed from above always keeps a falling column alive.
            IntVector3 above = IntVector3.Add(cell, Up);
            if (IsManagedLavaCell(above))
                return true;

            // Horizontal feed from a supported neighbor.
            for (int i = 0; i < HorizontalNeighbors.Length; i++)
            {
                IntVector3 neighbor = IntVector3.Add(cell, HorizontalNeighbors[i]);
                if (!IsManagedLavaCell(neighbor))
                    continue;

                IntVector3 neighborBelow = IntVector3.Add(neighbor, Down);
                if (CanContinueDownward(neighborBelow))
                    continue;

                int neighborDistance = GetKnownDistance(neighbor, int.MaxValue);

                // Normal flat shelf propagation.
                if (currentDistance > 0 && neighborDistance != int.MaxValue && neighborDistance + 1 == currentDistance)
                    return true;

                // Edge spill / drop origin.
                if (currentDistance == 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to remove an owned lava cell that is no longer fed.
        /// Summary: Removes only non-source cells and rechecks nearby cells after a successful delete.
        /// </summary>
        private static void TryRemoveOwnedCell(IntVector3 cell)
        {
            if (_sources.Contains(cell))
                return;

            if (!IsWorldIndexValid(cell))
            {
                _ownedLava.Remove(cell);
                _flowDistance.Remove(cell);
                return;
            }

            if (_pendingAddWrites.Contains(cell) || _pendingRemoveWrites.Contains(cell))
                return;

            if (!IsManagedLavaCell(cell))
            {
                _ownedLava.Remove(cell);
                _flowDistance.Remove(cell);
                return;
            }

            if (ApplyEmptyBlock(cell))
                EnqueueNeighborsForRecheck(cell);
        }
        #endregion

        #region Block Placement / Queries

        /// <summary>
        /// Applies a lava block at the given world index.
        /// Summary: Records the add as pending so the solver can reason about it immediately before confirmation.
        /// </summary>
        private static bool ApplyLavaBlock(IntVector3 worldIndex, int horizontalDistance)
        {
            if (!IsWorldIndexValid(worldIndex))
                return false;

            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (game == null)
                return false;

            LocalNetworkGamer localGamer =
                game.LocalPlayer != null ? game.LocalPlayer.Gamer as LocalNetworkGamer : null;

            _pendingRemoveWrites.Remove(worldIndex);
            _pendingAddWrites.Add(worldIndex);
            _pendingAddDistance[worldIndex] = horizontalDistance;

            BlockTypeEnum visualType = SelectVisualLavaType(worldIndex);

            if (localGamer != null)
            {
                AlterBlockMessage.Send(localGamer, worldIndex, visualType);
                return true;
            }

            BlockTerrain terrain = BlockTerrain.Instance;
            if (terrain == null)
            {
                _pendingAddWrites.Remove(worldIndex);
                _pendingAddDistance.Remove(worldIndex);
                return false;
            }

            bool changed = terrain.SetBlock(worldIndex, visualType);
            if (!changed)
            {
                _pendingAddWrites.Remove(worldIndex);
                _pendingAddDistance.Remove(worldIndex);
            }

            return changed;
        }

        /// <summary>
        /// Applies an empty block at the given world index.
        /// Summary: Marks the removal as pending so the solver treats the cell as empty immediately.
        /// </summary>
        private static bool ApplyEmptyBlock(IntVector3 worldIndex)
        {
            if (!IsWorldIndexValid(worldIndex))
                return false;

            CastleMinerZGame game = CastleMinerZGame.Instance;
            if (game == null)
                return false;

            LocalNetworkGamer localGamer =
                game.LocalPlayer != null ? game.LocalPlayer.Gamer as LocalNetworkGamer : null;

            _pendingAddWrites.Remove(worldIndex);
            _pendingAddDistance.Remove(worldIndex);
            _pendingRemoveWrites.Add(worldIndex);

            if (localGamer != null)
            {
                AlterBlockMessage.Send(localGamer, worldIndex, BlockTypeEnum.Empty);
                return true;
            }

            BlockTerrain terrain = BlockTerrain.Instance;
            if (terrain == null)
            {
                _pendingRemoveWrites.Remove(worldIndex);
                return false;
            }

            bool changed = terrain.SetBlock(worldIndex, BlockTypeEnum.Empty);
            if (!changed)
                _pendingRemoveWrites.Remove(worldIndex);

            return changed;
        }

        /// <summary>
        /// Chooses the visible lava block type for a placement.
        /// Summary: SurfaceLava is used when the block above is empty; otherwise DeepLava is used.
        /// </summary>
        private static BlockTypeEnum SelectVisualLavaType(IntVector3 worldIndex)
        {
            IntVector3 above = IntVector3.Add(worldIndex, Up);
            if (!IsWorldIndexValid(above))
                return BlockTypeEnum.SurfaceLava;

            return GetEffectiveBlock(above) == BlockTypeEnum.Empty
                ? BlockTypeEnum.SurfaceLava
                : BlockTypeEnum.DeepLava;
        }

        /// <summary>
        /// Returns whether a world position currently contains lava managed by this system.
        /// Summary: Includes tracked sources, tracked owned cells, and pending simulated lava adds.
        /// </summary>
        private static bool IsManagedLavaCell(IntVector3 worldIndex)
        {
            if (!IsWorldIndexValid(worldIndex))
                return false;

            BlockTypeEnum existing = GetEffectiveBlock(worldIndex);
            if (!IsLava(existing))
                return false;

            return _sources.Contains(worldIndex)
                || _ownedLava.Contains(worldIndex)
                || _pendingAddWrites.Contains(worldIndex);
        }

        /// <summary>
        /// Returns the known flow distance for a cell.
        /// Summary: Sources always resolve to 0; pending-add values override stored values.
        /// </summary>
        private static int GetKnownDistance(IntVector3 worldIndex, int fallback)
        {
            if (_sources.Contains(worldIndex))
                return 0;

            if (_pendingAddDistance.TryGetValue(worldIndex, out int pendingDistance))
                return pendingDistance;

            if (_flowDistance.TryGetValue(worldIndex, out int existingDistance))
                return existingDistance;

            return fallback;
        }

        /// <summary>
        /// Reads the current terrain block at the given world index.
        /// Summary: Uses BlockTerrain.GetBlockWithChanges so pending terrain edits are reflected.
        /// </summary>
        private static BlockTypeEnum GetBlock(IntVector3 worldIndex)
        {
            BlockTerrain terrain = BlockTerrain.Instance;
            if (terrain == null)
                return BlockTypeEnum.Empty;

            return terrain.GetBlockWithChanges(worldIndex);
        }

        /// <summary>
        /// Reads the effective block state seen by the solver.
        /// Summary: Pending adds behave like lava immediately and pending removals behave like empty.
        /// </summary>
        private static BlockTypeEnum GetEffectiveBlock(IntVector3 worldIndex)
        {
            if (!IsWorldIndexValid(worldIndex))
                return BlockTypeEnum.Empty;

            if (_pendingAddWrites.Contains(worldIndex))
                return SelectVisualLavaTypeRaw(worldIndex);

            if (_pendingRemoveWrites.Contains(worldIndex))
                return BlockTypeEnum.Empty;

            return GetBlock(worldIndex);
        }

        /// <summary>
        /// Raw lava visual selection that ignores pending effective-state recursion.
        /// Summary: Used internally when pending adds need a block type without re-entering effective reads.
        /// </summary>
        private static BlockTypeEnum SelectVisualLavaTypeRaw(IntVector3 worldIndex)
        {
            IntVector3 above = IntVector3.Add(worldIndex, Up);
            if (!IsWorldIndexValid(above))
                return BlockTypeEnum.SurfaceLava;

            return GetBlock(above) == BlockTypeEnum.Empty
                ? BlockTypeEnum.SurfaceLava
                : BlockTypeEnum.DeepLava;
        }

        /// <summary>
        /// Returns whether lava may continue downward into the target cell.
        /// Summary: Lava can continue through empty space or through lava cells already managed by the simulation.
        /// </summary>
        private static bool CanContinueDownward(IntVector3 worldIndex)
        {
            if (!IsWorldIndexValid(worldIndex))
                return false;

            BlockTypeEnum existing = GetEffectiveBlock(worldIndex);

            if (existing == BlockTypeEnum.Empty)
                return true;

            if (IsManagedLavaCell(worldIndex))
                return true;

            return false;
        }

        /// <summary>
        /// Validates a terrain world index.
        /// Summary: Uses MakeIndexFromWorldIndexVector and rejects positions outside the world volume.
        /// </summary>
        private static bool IsWorldIndexValid(IntVector3 worldIndex)
        {
            BlockTerrain terrain = BlockTerrain.Instance;
            if (terrain == null || !terrain.IsReady)
                return false;

            return terrain.MakeIndexFromWorldIndexVector(worldIndex) != -1;
        }
        #endregion

        #region Queue Helpers

        /// <summary>
        /// Queues a lava cell for future simulation.
        /// Summary: Suppresses worse duplicate queue entries by keeping only the best known queued distance.
        /// </summary>
        private static void Enqueue(IntVector3 worldIndex, int horizontalDistance)
        {
            if (!IsWorldIndexValid(worldIndex))
                return;

            if (_queuedDistance.TryGetValue(worldIndex, out int bestDistance) && bestDistance <= horizontalDistance)
                return;

            _queuedDistance[worldIndex] = horizontalDistance;
            _frontier.Enqueue(new FlowNode(worldIndex, horizontalDistance));
        }

        /// <summary>
        /// Re-queues a changed cell and its immediate neighbors.
        /// Summary: Used after adds/removals so nearby lava can re-evaluate support and flow.
        /// </summary>
        private static void EnqueueNeighborsForRecheck(IntVector3 center)
        {
            Enqueue(center, GetKnownDistance(center, 0));
            Enqueue(IntVector3.Add(center, Down), 0);
            Enqueue(IntVector3.Add(center, Up), 0);

            for (int i = 0; i < HorizontalNeighbors.Length; i++)
                Enqueue(IntVector3.Add(center, HorizontalNeighbors[i]), 0);
        }
        #endregion
    }
}