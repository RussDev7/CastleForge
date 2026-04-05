/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Utils.Threading;
using DNA.CastleMinerZ;
using System.Threading;
using System;

namespace ModLoader
{
    /// <summary>
    /// Custom AppDomainManager that installs assembly resolution early, then waits for
    /// CastleMinerZGame + TaskDispatcher to become available before attaching the
    /// ModBootstrapComponent on the game's main thread.
    ///
    /// Threading notes:
    /// - Initializes before the game is fully ready.
    /// - Uses a background wait only for readiness detection.
    /// - Marshals the actual component insertion onto the main thread.
    /// - Prevents duplicate bootstrap attachment.
    /// </summary>
    public class ModDomainManager : AppDomainManager
    {
        private static int _attachQueued;    // Set once a main-thread attach task has been queued.
        private static int _attachCompleted; // Set once bootstrap attachment has successfully completed.

        /// <summary>
        /// Called by the CLR when the AppDomain is created.
        /// Installs assembly resolution, then begins a background readiness wait
        /// that will safely marshal bootstrap attachment onto the game thread.
        /// </summary>
        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);

            // Install CLR assembly resolution fallback (top-level !Mods only).
            // Prevents reflection/type-load failures during mod discovery when exe.config probing is disabled.
            string modsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods");
            ModsAssemblyResolver.Install(modsDir);

            // Important:
            // This background thread does NOT attach the component directly.
            // It only waits for CastleMinerZGame + TaskDispatcher readiness, then queues
            // the actual Components.Add(...) onto the game's main thread.
            ThreadPool.QueueUserWorkItem(_ => WaitAndQueueAttach());
        }

        /// <summary>
        /// Waits briefly for both CastleMinerZGame.Instance and TaskDispatcher.Instance
        /// to exist, then queues a main-thread attach task.
        /// </summary>
        private static void WaitAndQueueAttach()
        {
            for (int i = 0; i < 300; i++)
            {
                // Another path may have already finished attachment.
                if (Interlocked.CompareExchange(ref _attachCompleted, 0, 0) != 0)
                    return;

                try
                {
                    CastleMinerZGame gameInstance = CastleMinerZGame.Instance;
                    TaskDispatcher   dispatcher   = TaskDispatcher.Instance;

                    if (gameInstance != null && dispatcher != null)
                    {
                        // Another queued / retried path may have already completed.
                        if (Interlocked.CompareExchange(ref _attachQueued, 1, 0) == 0)
                        {
                            dispatcher.AddTaskForMainThread(() =>
                            {
                                try
                                {
                                    // Another queued/retried path may have already completed.
                                    if (Interlocked.CompareExchange(ref _attachCompleted, 0, 0) != 0)
                                        return;

                                    // Defensive duplicate guard:
                                    // If bootstrap is already present, mark completion and stop.
                                    if (HasBootstrapComponent(gameInstance))
                                    {
                                        Interlocked.Exchange(ref _attachCompleted, 1);
                                        return;
                                    }

                                    // Main-thread-safe bootstrap insertion.
                                    gameInstance.Components.Add(new ModBootstrapComponent(gameInstance));
                                    Interlocked.Exchange(ref _attachCompleted, 1);
                                }
                                catch (Exception)
                                {
                                    // Allow a retry if something transient failed before completion.
                                    // This clears only the "queued" flag; completion remains unset.
                                    Interlocked.Exchange(ref _attachQueued, 0);
                                }
                            });
                        }

                        return;
                    }
                }
                catch
                {
                    // Very early startup can still be in flux.
                    // Swallow and retry briefly.
                }

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Checks whether the game's component collection already contains a bootstrap component.
        /// This is only called from the queued main-thread task.
        /// </summary>
        private static bool HasBootstrapComponent(CastleMinerZGame gameInstance)
        {
            foreach (var component in gameInstance.Components)
            {
                if (component is ModBootstrapComponent)
                    return true;
            }

            return false;
        }
    }
}