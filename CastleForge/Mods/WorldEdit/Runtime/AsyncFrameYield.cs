/*
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge.

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

using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldEdit
{
    #region AsyncFrameYield

    /*
       =======================================================================================================
       Async Frame Yield / Next-Frame Awaiter
       -------------------------------------------------------------------------------------------------------
       Purpose
       • Provide a safe, awaitable "yield until next frame" primitive for long-running world-edit operations.
       • Prevent hard stalls by breaking large loops across frames while staying on the game thread.

       Threading Model
       • Any thread:  Can call NextFrame() to obtain a Task that completes on the next Update tick.
       • Game thread: Must call Pump() once per frame (e.g., from a Harmony Postfix on DNAGame.Update).
         - Pump() completes all queued waiters and runs their continuations inline on the game thread.

       Why NOT RunContinuationsAsynchronously?
       • We intentionally want continuations to execute on the Pump() call site (the game thread) so that
         follow-up code can safely touch engine singletons (BlockTerrain, networking, UI) without cross-thread
         access violations.

       Integration Points
       • Harmony patch: Call FrameYield.Pump() once per frame (ideally before other per-frame pumps).
       • Long commands: Sprinkle "await FrameYield.NextFrame()" (or a time-budgeted helper) inside heavy loops.

       Typical Usage
       • var sw = Stopwatch.StartNew();
         for (...) {
             ...heavy work...
             if (sw.ElapsedMilliseconds > 6) { sw.Restart(); await FrameYield.NextFrame(); }
         }

       Safety Notes
       • Pump() must be called regularly. If Pump() is not running (paused menu / no Update), NextFrame()
         tasks will not complete.
       • This class intentionally has no cancellation; callers should add CancellationToken checks as needed.
       =======================================================================================================
    */

    /// <summary>
    /// Awaitable "resume next frame" queue. Call <see cref="Pump"/> once per frame on the game thread.
    /// </summary>
    internal class AsyncFrameYield
    {
        private static readonly Queue<TaskCompletionSource<bool>> _waiters = new Queue<TaskCompletionSource<bool>>();

        /// <summary>Await this to resume on the next game Update tick.</summary>
        public static Task NextFrame()
        {
            // IMPORTANT: Do NOT use RunContinuationsAsynchronously here.
            // We WANT continuations to run inline on the Pump() thread (the game thread).
            var tcs = new TaskCompletionSource<bool>();
            lock (_waiters) _waiters.Enqueue(tcs);
            return tcs.Task;
        }

        /// <summary>Call once per frame on the game thread.</summary>
        public static void Pump()
        {
            TaskCompletionSource<bool>[] toRelease;
            lock (_waiters)
            {
                if (_waiters.Count == 0) return;
                toRelease = _waiters.ToArray();
                _waiters.Clear();
            }

            // Continuations will run on THIS thread (game thread).
            foreach (var tcs in toRelease)
                tcs.TrySetResult(true);
        }
    }
    #endregion
}