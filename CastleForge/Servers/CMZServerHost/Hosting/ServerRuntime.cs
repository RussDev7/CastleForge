/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServer - see LICENSE for details.
*/

#pragma warning disable IDE0130
using System.Reflection;
using System.Threading;
using System;

namespace CMZServerHost
{
    /// <summary>
    /// Minimal dedicated-server runtime bootstrap.
    ///
    /// Purpose:
    /// - Owns the loaded game/common assemblies and the active server configuration.
    /// - Creates a synthetic host gamer placeholder so server-side networking can be initialized.
    /// - Provides a simple start / run / stop lifecycle for the dedicated host process.
    ///
    /// Notes:
    /// - This version is still a scaffold.
    /// - Networking startup, discovery replies, approvals, and live world ticking are intended
    ///   to be added in later milestones.
    /// - The synthetic host gamer currently exists to mirror the host identity expected by parts
    ///   of the CastleMiner Z networking stack.
    /// </summary>
    /// <remarks>
    /// Creates a new runtime wrapper for the dedicated server process.
    ///
    /// Purpose:
    /// - Stores configuration and loaded assemblies for later runtime initialization.
    ///
    /// Notes:
    /// - No networking or world state is started here.
    /// - Startup work begins in <see cref="Start"/>.
    /// </remarks>
    internal sealed class ServerRuntime(ServerConfig config, Assembly gameAsm, Assembly commonAsm)
    {
        #region Fields

        /// <summary>
        /// Immutable server configuration used for bind address, port, tick rate, world folder, etc.
        /// </summary>
        private readonly ServerConfig _config = config;

        /// <summary>
        /// Loaded CastleMinerZ game assembly.
        /// </summary>
        private readonly Assembly _gameAsm = gameAsm;

        /// <summary>
        /// Loaded shared/common DNA assembly.
        /// </summary>
        private readonly Assembly _commonAsm = commonAsm;

        /// <summary>
        /// Indicates whether the main server loop should continue running.
        /// </summary>
        private volatile bool _running;

        /// <summary>
        /// Reflection-created synthetic host gamer used as a placeholder host identity.
        /// </summary>
        private object _hostGamer;

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts the dedicated runtime scaffold.
        ///
        /// Purpose:
        /// - Prints the active server configuration to the console.
        /// - Creates the synthetic host gamer placeholder.
        /// - Marks the runtime as running so the outer loop can begin.
        ///
        /// Notes:
        /// - This currently does not start the actual Lidgren host.
        /// - The console output intentionally documents the next milestone steps.
        /// </summary>
        public void Start()
        {
            Console.WriteLine("Starting dedicated server runtime...");
            Console.WriteLine($"GameName        : {_config.GameName}");
            Console.WriteLine($"NetworkVersion  : {_config.NetworkVersion}");
            Console.WriteLine($"Bind            : {_config.BindAddress}:{_config.Port}");
            Console.WriteLine($"ServerName      : {_config.ServerName}");
            Console.WriteLine($"WorldFolder     : {_config.WorldFolder}");
            Console.WriteLine($"MaxPlayers      : {_config.MaxPlayers}");

            CreateSyntheticHostGamer();

            // Milestone 1:
            // Hook up your Lidgren host bootstrap here.
            //
            // For example, next you will:
            // 1) reflect NetPeerConfiguration / NetServer or your CMZ Lidgren wrapper
            // 2) bind to _config.BindAddress/_config.Port
            // 3) enable discovery + approval + data message types
            // 4) answer host discovery requests
            // 5) begin accepting client joins
            //
            // Keep this version simple and honest:
            Console.WriteLine("Synthetic server gamer created.");
            Console.WriteLine("Next step: wire Lidgren server start and discovery/join.");
            Console.WriteLine();
            Console.WriteLine($"Connect target for local test: 127.0.0.1:{_config.Port}");

            _running = true;
        }

        /// <summary>
        /// Runs the main server loop until <see cref="Stop"/> is called.
        ///
        /// Purpose:
        /// - Calculates a sleep interval from the configured tick rate.
        /// - Repeatedly calls <see cref="Tick"/> while the runtime is active.
        /// - Prevents loop-breaking exceptions from immediately terminating the process.
        ///
        /// Notes:
        /// - This is currently a lightweight placeholder loop.
        /// - Future versions can replace the fixed sleep with more precise timing if needed.
        /// </summary>
        public void RunLoop()
        {
            int sleepMs = Math.Max(1, 1000 / Math.Max(1, _config.TickRateHz));

            while (_running)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ServerRuntime] Tick error: " + ex);
                }

                Thread.Sleep(sleepMs);
            }
        }

        /// <summary>
        /// Requests the server loop to stop.
        ///
        /// Purpose:
        /// - Signals <see cref="RunLoop"/> to exit cleanly on its next iteration.
        /// </summary>
        public void Stop()
        {
            _running = false;
        }

        #endregion

        #region Tick

        /// <summary>
        /// Per-iteration server update hook.
        ///
        /// Purpose:
        /// - Serves as the placeholder for future runtime work performed each loop iteration.
        ///
        /// Notes:
        /// - Current behavior is intentionally a no-op.
        /// - Planned future work includes:
        ///   - polling Lidgren messages
        ///   - processing discovery requests
        ///   - processing connection approvals
        ///   - updating world state
        ///   - saving on interval
        /// </summary>
        private void Tick()
        {
            // Milestone 1:
            // no-op loop placeholder.
            //
            // Milestone 2:
            // - poll Lidgren messages
            // - process discovery requests
            // - process connection approvals
            // - update world tick
            // - save on interval
        }

        #endregion

        #region Synthetic Host Gamer

        /// <summary>
        /// Creates a reflection-based synthetic host gamer object.
        ///
        /// Purpose:
        /// - Resolves the required runtime types from the loaded assemblies.
        /// - Creates a temporary PlayerID and SignedInGamer identity for the server.
        /// - Builds a NetworkGamer marked as local host with global ID 0.
        ///
        /// Notes:
        /// - This uses reflection because the dedicated host is bootstrapping against the game assemblies.
        /// - The NetworkGamer is created with a null session for now.
        /// - This is a placeholder host identity until the real network session is wired up.
        /// </summary>
        private void CreateSyntheticHostGamer()
        {
            if (_commonAsm == null)
                throw new InvalidOperationException("DNA.Common.dll was not loaded.");

            Type playerIdType = _commonAsm.GetType("DNA.PlayerID");
            Type signedInGamerType = _commonAsm.GetType("DNA.Net.GamerServices.SignedInGamer");
            Type networkGamerType = _commonAsm.GetType("DNA.Net.GamerServices.NetworkGamer");

            if (playerIdType == null)
                throw new InvalidOperationException("Could not find DNA.PlayerID.");
            if (signedInGamerType == null)
                throw new InvalidOperationException("Could not find SignedInGamer.");
            if (networkGamerType == null)
                throw new InvalidOperationException("Could not find NetworkGamer.");

            Type xnaAsmPlayerIndexType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                xnaAsmPlayerIndexType = asm.GetType("Microsoft.Xna.Framework.PlayerIndex");
                if (xnaAsmPlayerIndexType != null)
                    break;
            }

            if (xnaAsmPlayerIndexType == null)
                throw new InvalidOperationException("Could not find Microsoft.Xna.Framework.PlayerIndex.");

            object playerIndexOne = Enum.ToObject(xnaAsmPlayerIndexType, 0);

            byte[] hostHash = Guid.NewGuid().ToByteArray();
            object playerId = Activator.CreateInstance(playerIdType, [hostHash]);

            object signedInGamer = Activator.CreateInstance(
                signedInGamerType,
                [playerIndexOne, playerId, "Server"]);

            // session = null for now, local = true, host = true, globalID = 0
            _hostGamer = Activator.CreateInstance(
                networkGamerType,
                [signedInGamer, null, true, true, (byte)0]);
        }

        #endregion
    }
}