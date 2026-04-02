/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Threading;
using System;

namespace ModLoader
{
    /// <summary>
    /// Custom AppDomainManager that injects the ModBootstrapComponent into the game's AppDomain.
    /// Runs on domain initialization and attaches the mod loader component once the game instance is available.
    /// </summary>
    public class ModDomainManager : AppDomainManager
    {
        // Called by the CLR when a new AppDomain is created.
        // Queues an attachment task to wait for the game instance to be ready.
        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);

            // Install CLR assembly resolution fallback (top-level !Mods only).
            // Prevents reflection/type-load failures during mod discovery when exe.config probing is disabled.
            string modsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods");
            ModsAssemblyResolver.Install(modsDir);

            // Asynchronously attempt to attach our mod bootstrap to the main game.
            ThreadPool.QueueUserWorkItem(_ => TryAttach());
        }

        // Attempts to locate the CastleMinerZGame instance and add the ModBootstrapComponent.
        // Retries up to 100 times, sleeping briefly between attempts.
        private void TryAttach()
        {
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    // Obtain the singleton game instance.
                    var gameInstance = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                    if (gameInstance != null)
                    {
                        // Add our bootstrap component into the game's component collection.
                        gameInstance.Components.Add(new ModBootstrapComponent(gameInstance));
                        break; // Success: Exit retry loop.
                    }
                }
                catch
                {
                    // If any reflection or access fails, swallow and retry.
                }

                Thread.Sleep(100); // Wait briefly before retrying to avoid spinning.
            }
        }
    }
}
