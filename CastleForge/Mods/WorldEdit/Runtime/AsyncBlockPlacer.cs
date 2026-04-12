/*
Copyright (c) 2025 RussDev7

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

/*
Sections of this class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp.
*/

using System.Collections.Concurrent;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ;
using System;
using DNA;

namespace WorldEdit
{
    #region AsyncBlockPlacer

    /*
       =======================================================================================================
       Async Block Placement Queue
       -------------------------------------------------------------------------------------------------------
       Purpose
       • Decouple world-edit block placement from command execution.
       • Pace block edits with a per-frame budget to avoid stalls/hitches while large regions are applied.
       • Use the vanilla network message (AlterBlockMessage) for both host and client -> vanilla-safe.

       Threading Model
       • Producer threads: Call Enqueue(...) from any thread to queue a (position, blockID) edit.
       • Main thread:      Pump() drains up to MaxBlocksPerFrame each frame and sends AlterBlockMessage.
         - Host:   The host's own message is received/applied locally by the engine (consistent with client path).
         - Client: Messages go to the host using ReliableInOrder.
       • IMPORTANT: Never touch BlockTerrain or other engine singletons off-thread. Pump() runs on main thread.

       Integration Points
       • Commands (/set, /sphere, /replace, ...): replace direct PlaceBlock(...) with AsyncBlockPlacer.Enqueue(...).
       • Harmony patch: Call Pump() once per frame via a Postfix on CastleMinerZGame.Update (or DNAGame.Update).

       Performance Knobs
       • MaxBlocksPerFrame (int): Per-frame budget. Higher = faster throughput, but more risk of small hitches.
         Start 1500-4000; tune by feel and target hardware/network conditions.

       Safety Notes
       • Validate block IDs before enqueue if you compute IDs dynamically; invalid IDs can cause host lookups to fail.
       • Pump() early-outs if there's no active LocalNetworkGamer (session not ready yet).
       • This implementation uses only AlterBlockMessage (no custom bulk message) to remain compatible with vanilla hosts.
       • If you need to wait for all queued edits (e.g., before screenshot/undo fences), add a simple IsIdle check/poll.

       Diagnostics
       • A lightweight telemetry line (see bottom of Pump) shows per-frame drain, pending count, and current budget.
       • If the log never shows drains while you're enqueuing edits, your Harmony Postfix isn't firing each frame.

       Known Limits
       • Throughput for clients is ultimately limited by the session's ReliableInOrder pipeline and network latency.
       • On host, sending AlterBlockMessage to self is slower than calling SetBlock directly, but preserves vanilla flow.

       Fences & Utilities
       • IsIdle: Returns true when the local queue is empty (i.e., everything you enqueued has been sent).
         Note:   On clients this does not guarantee the host has *applied* yet-only that you've finished sending.
       • WaitUntilIdleAsync(pollMs): Simple awaitable helper if you want to delay a follow-up action (e.g., screenshot).
         Example:
             foreach (var p in region) AsyncBlockPlacer.Enqueue(p, id);
             await AsyncBlockPlacer.WaitUntilIdleAsync(10); // Wait until our queue is drained.
       =======================================================================================================
    */

    /// <summary>
    /// Frame-budgeted queue for networked block placement. Producers enqueue edits from any thread;
    /// the main thread calls <see cref="Pump"/> once per frame to send up to <see cref="MaxBlocksPerFrame"/> edits
    /// via the vanilla <see cref="AlterBlockMessage"/> (reliable, in-order).
    /// </summary>
    public static class AsyncBlockPlacer
    {
        // ===== Tunable =====

        /// <summary>
        /// Per-frame budget for sends. Higher = faster, but can cause brief hitches if the host is busy.
        /// Typical range: 1500-4000 depending on region size and network conditions.
        /// </summary>
        public static int MaxBlocksPerFrame = AsyncBlockPlacer_Settings.MaxBlocksPerFrame;

        // ===== State =====

        /// <summary>Pending work from any thread; consumed on the main thread by <see cref="Pump"/>.</summary>
        private static readonly ConcurrentQueue<BlockEdit> _pending = new ConcurrentQueue<BlockEdit>();

        /// <summary>Convenience accessor for the local gamer (null until a session exists).</summary>
        private static LocalNetworkGamer Me => CastleMinerZGame.Instance?.MyNetworkGamer;

        /// <summary>One block edit: set voxel at <see cref="Pos"/> to <see cref="Type"/>.</summary>
        public struct BlockEdit
        {
            public IntVector3 Pos;
            public BlockTypeEnum Type;
            public BlockEdit(IntVector3 pos, BlockTypeEnum type) { Pos = pos; Type = type; }
        }

        /// <summary>
        /// Enqueue a block placement. Safe to call from any thread. Actual send occurs in <see cref="Pump"/>.
        /// </summary>
        public static void Enqueue(Vector3 location, int blockId)
        {
            // Convert once, off the hot path:
            var pos  = new DNA.IntVector3((int)location.X, (int)location.Y, (int)location.Z);
            var type = (BlockTypeEnum)blockId;
            _pending.Enqueue(new BlockEdit(pos, type));
        }

        /// <summary>
        /// Drives paced application. Call from a Harmony Postfix on CastleMinerZGame.Update (or DNAGame.Update).
        /// Sends up to <see cref="MaxBlocksPerFrame"/> vanilla <see cref="AlterBlockMessage"/> instances per frame.
        /// </summary>
        public static void Pump()
        {
            // Not in a session yet? Nothing to send.
            var me = Me;
            if (me == null) return;

            int budget  = AsyncBlockPlacer_Settings.MaxBlocksPerFrame;
            int drained = 0;

            while (budget-- > 0 && _pending.TryDequeue(out var e))
            {
                // Host or client, same path: Send to the session; host will receive and apply locally.
                try
                {
                    AlterBlockMessage.Send(me, e.Pos, e.Type);
                }
                catch (KeyNotFoundException)
                {
                    // Defensive: Bad ID -> drop silently or log once.
                    // ModLoader.LogSystem.SendFeedback($"[ABP] Dropped invalid block id {(int)e.Type} at {e.Pos}");
                }
                drained++;
            }

            // Optional: Light telemetry to see the budget working.
            if (AsyncBlockPlacer_Settings.ShowTelemetry && (Environment.TickCount & 1023) == 0 && drained > 0)
                ModLoader.LogSystem.SendFeedback($"[ABP] frame={drained}, pending={_pending.Count}, budget={MaxBlocksPerFrame}.");
        }
    }
    #endregion
}