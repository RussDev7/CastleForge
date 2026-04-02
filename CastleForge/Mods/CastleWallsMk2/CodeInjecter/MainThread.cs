/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Main-thread dispatcher for game-safe work.
    /// <para>
    /// Many CMZ / XNA APIs must run on the game thread. Use <see cref="Post(Action)"/>
    /// from worker threads (code editor, tasks, timers) and call <see cref="Pump"/>
    /// once per frame on the game thread (e.g., from the mod's Tick or a Harmony postfix).
    /// </para>
    /// </summary>
    internal static class MainThread
    {
        #region Queue Internals

        // Lock-free, thread-safe queue for actions scheduled to run on the game thread.
        private static readonly ConcurrentQueue<Action> _q = new ConcurrentQueue<Action>();

        #endregion

        #region Public API

        /// <summary>
        /// Enqueue an action to run on the game thread.
        /// Safe to call from any thread; returns immediately.
        /// </summary>
        /// <param name="a">The action to execute on the main thread (ignored if null).</param>
        public static void Post(Action a)
        {
            if (a != null)
                _q.Enqueue(a);
        }

        /// <summary>
        /// Execute all queued actions on the current (game) thread.
        /// Call exactly once per frame from a safe place (e.g., mod Tick).
        /// </summary>
        public static void Pump()
        {
            while (_q.TryDequeue(out var a))
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    // Never throw from the pump; log and continue so one bad task
                    // cannot kill the whole frame.
                    ChatLog.Append("Script", $"MainThread error: {ex}.");
                }
            }
        }
        #endregion
    }
}