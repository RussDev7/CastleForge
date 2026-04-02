/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Threading;
using System.IO;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    /// <summary>
    /// LanternRingScanner
    /// -------------------
    /// Utility for empirically probing the "lantern ring" structure in CastleMiner Z.
    ///
    /// Behavior:
    /// - Iterates N = 1..1000 and computes a radial distance:
    ///     r(N) = floor( sqrt( 2 * N * 2^31 ) )
    /// - For each ring index it:
    ///     • Teleports the player to (r, 0, 0) with spawnOnTop = true.
    ///     • Waits for the async teleport / chunk loading to finish.
    ///     • Scans a small area beneath the player for FixedLantern blocks.
    ///     • Logs "Found" / "N/A" per ring index to a desktop .txt file.
    ///
    /// Integration:
    /// - Designed to be driven from a UI toggle or command that passes in a
    ///   CancellationToken; unchecking the toggle cancels the scan loop cleanly.
    /// - Uses reflection against BlockTerrain's async teleport state to detect
    ///   when the teleport screen is still active.
    ///
    /// Notes:
    /// - Non-destructive: It only teleports and reads blocks; no writes to terrain.
    /// - Logging path defaults to the user's Desktop as "LanternRings.txt".
    ///
    /// Usage:
    ///
    /// // Shared CTS so we can cancel the scan when the checkbox is unchecked.
    /// private static CancellationTokenSource _lanternScanCts;
    ///
    /// Callbacks.OnLanternRingScanner = enabled =>
    /// {
    ///     try
    ///     {
    ///         if (enabled)
    ///         {
    ///             // If already running, cancel and restart.
    ///             _lanternScanCts?.Cancel();
    ///             _lanternScanCts = new CancellationTokenSource();
    ///
    ///             // Fire-and-forget async scan; cancellation is via _lanternScanCts.
    ///             _ = LanternRingScanner.RunAsync(_lanternScanCts.Token);
    ///         }
    ///         else
    ///         {
    ///             // Unchecked: Stop any in-progress scan.
    ///             _lanternScanCts?.Cancel();
    ///         }
    ///     }
    ///     catch (Exception ex) { Log($"{ex}."); }
    /// };
    /// </summary>
    internal static class LanternRingScanner
    {
        // Where to write results. Adjust path if you prefer a subfolder.
        private static readonly string LogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LanternRings.txt");

        /// <summary>
        /// Teleports the player through 100 ring locations, using:
        ///     r(N) = floor( sqrt( 2 * N * 2^31 ) )
        ///
        /// For each N:
        /// - Teleport to (r, 0, 0) with spawnOnTop = true (surface).
        /// - Wait a short time for chunks to load.
        /// - Sample a 3x3 block area one block below the player.
        /// - If any block is FixedLantern, log "RingN: Found", else "RingN: N/A".
        ///
        /// The operation is cancellable via the provided token; if the user
        /// unchecks the checkbox, the token is cancelled and the loop exits.
        /// </summary>
        public static async Task RunAsync(CancellationToken token)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game == null || game.GameScreen == null || game.LocalPlayer == null)
                {
                    Log("[LRScanner] Game not ready; aborting.");
                    return;
                }

                // Start with a fresh log for this run.
                TrySafeDelete(LogPath);

                using (var writer = new StreamWriter(LogPath, append: true))
                {
                    writer.WriteLine($"Lantern ring scan started at {DateTime.Now}.");
                    writer.Flush();

                    for (int n = 1; n <= 1000; n++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            writer.WriteLine("Scan cancelled by user.");
                            writer.Flush();
                            break;
                        }

                        // r = floor( sqrt(2 * n * 2^31) )
                        long value = 2L * n * (1L << 31); // 2 * n * 2^31
                        double root = Math.Sqrt(value);
                        int r = (int)Math.Floor(root);

                        // Teleport along +X axis at that radius; Y=0 with spawnOnTop=true
                        // lets the game place you on the surface.
                        var target = new IntVector3(r, 0, 0);

                        Log($"[LRScanner] Ring {n}: teleporting to X={target.X}, Z={target.Z} (r={r}).");

                        game.GameScreen.TeleportToLocation(target, spawnOnTop: true);

                        // Give the game some time to load/generate terrain at the new location.
                        try
                        {
                            while (IsTeleportPending())
                            {
                                await Task.Delay(50, token); // Still in teleport screen.
                            }
                            await Task.Delay(500, token); // Wait 0.5s while chunks load after teleporting.
                        }
                        catch (TaskCanceledException)
                        {
                            writer.WriteLine("Scan cancelled during delay.");
                            writer.Flush();
                            break;
                        }

                        if (token.IsCancellationRequested)
                            break;

                        bool foundLantern = ScanForFixedLantern(game);

                        string line = foundLantern
                            ? $"Ring{n} [{target}]: Found"
                            : $"Ring{n} [{target}]: N/A";

                        writer.WriteLine(line);
                        writer.Flush();

                        Log($"[LRScanner] {line}");
                    }

                    writer.WriteLine($"Lantern ring scan finished at {DateTime.Now}.");
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                Log($"[LRScanner] ERROR: {ex}");
            }
        }

        /// <summary>
        /// Scans a 3x3 area one block below the player's feet for any FixedLantern block.
        /// Returns true if at least one lantern is found.
        /// </summary>
        private static bool ScanForFixedLantern(CastleMinerZGame game)
        {
            // Current player position in world-space.
            Vector3 lp = game.LocalPlayer.LocalPosition;

            // Base block one below the player.
            var baseBlock = new IntVector3(
                (int)Math.Floor(lp.X),
                (int)Math.Floor(lp.Y) - 2,
                (int)Math.Floor(lp.Z));

            // Enum ID for the lantern type we care about.
            int lanternId = (int)BlockTypeEnum.FixedLantern;

            for (int dy = 0; dy <= 10; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var probe = new IntVector3(
                            baseBlock.X + dx,
                            baseBlock.Y - dy,
                            baseBlock.Z + dz);

                        // InGameHUD.GetBlock(...) is assumed to return packed block data (int).
                        int blockData = (int)InGameHUD.GetBlock(probe);

                        // Decode block type; compare to FixedLantern.
                        if (blockData == lanternId)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to delete the given file path:
        /// - Skips if the file does not exist.
        /// - Logs (but ignores) any exceptions thrown during deletion.
        /// </summary>
        private static void TrySafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log($"[LRScanner] Failed to delete old log: {ex}");
            }
        }

        /// <summary>
        /// Returns true if the terrain system is currently processing
        /// an async teleport (chunk init still in progress).
        /// </summary>
        private static bool IsTeleportPending()
        {
            var bt = BlockTerrain.Instance;
            if (bt == null)
                return false;

            // Get private field "_asyncInitData"
            var asyncField = typeof(BlockTerrain).GetField(
                "_asyncInitData",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var asyncObj = asyncField?.GetValue(bt);
            if (asyncObj == null)
                return false; // no pending init at all

            // Get the "teleporting" field on the nested AsynchInitData type
            var teleField = asyncObj.GetType().GetField(
                "teleporting",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (teleField == null)
                return false;

            return (bool)teleField.GetValue(asyncObj);
        }
    }
}
