/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using HarmonyLib;
using System;
using DNA;

using static ModLoader.LogSystem; // For Log(...).

namespace ModLoaderExt
{
    /// <summary>
    /// Multiplayer TNT/C4 explosion safety patches.
    ///
    /// Goal:
    /// - Prevent non-host clients from hard-crashing when very large TNT/C4 chains detonate.
    ///
    /// Notes:
    /// - Vanilla TNT/C4 terrain removal is calculated by the player who detonated the explosive.
    /// - In multiplayer, that means a non-host client can become responsible for calculating and
    ///   sending a very large RemoveBlocksMessage.
    /// - This system preserves vanilla TNT/C4 terrain removal and chunks large
    ///   RemoveBlocksMessage payloads into smaller reliable messages.
    /// </summary>
    internal static class TntExplosionSafety
    {
        // ======================================================================================
        // Summary
        // ======================================================================================
        // This system protects multiplayer TNT/C4 explosions in two ways:
        //   1) Vanilla TNT/C4 chain calculation is preserved.
        //   2) Large RemoveBlocksMessage payloads are split into smaller chunks.
        //
        // Design intent:
        // - Keep single-player and host-local detonations close to vanilla behavior.
        // - Avoid replacing the entire explosion system.
        // - Fail open wherever possible so TNT/C4 still behaves normally if this patch has a problem.
        // ======================================================================================

        #region Settings

        /// <summary>Lowest accepted RemoveBlocksMessage chunk size.</summary>
        private const int MinBlocksPerRemoveBlocksMessage = 16;

        /// <summary>Highest accepted RemoveBlocksMessage chunk size.</summary>
        private const int MaxAllowedBlocksPerRemoveBlocksMessage = 4096;

        /// <summary>Fallback chunk size if config somehow contains an invalid value.</summary>
        private const int DefaultBlocksPerRemoveBlocksMessage = 256;

        /// <summary>
        /// Master toggle read from ModLoaderExt.Config.ini.
        /// </summary>
        private static bool Enabled
        {
            get { return TntExplosionSafetyConfig.Enabled; }
        }

        /// <summary>
        /// Configured RemoveBlocksMessage chunk size, clamped to a safe range.
        /// </summary>
        private static int MaxBlocksPerRemoveBlocksMessage
        {
            get
            {
                int value = TntExplosionSafetyConfig.MaxBlocksPerRemoveBlocksMessage;

                if (value <= 0)
                    value = DefaultBlocksPerRemoveBlocksMessage;

                if (value < MinBlocksPerRemoveBlocksMessage)
                    return MinBlocksPerRemoveBlocksMessage;

                if (value > MaxAllowedBlocksPerRemoveBlocksMessage)
                    return MaxAllowedBlocksPerRemoveBlocksMessage;

                return value;
            }
        }
        #endregion

        #region Internals

        // Prevent recursion when this patch calls RemoveBlocksMessage.Send(...) for each chunk.
        [ThreadStatic]
        private static bool _insideChunkedRemoveBlocksSend;

        #endregion

        #region Policy

        /// <summary>
        /// Determines whether a RemoveBlocksMessage should be split into smaller messages.
        /// </summary>
        private static bool ShouldChunkRemoveBlocksMessage(
            LocalNetworkGamer from,
            int numblocks,
            IntVector3[] blocks,
            out int safeCount,
            out int maxBlocksPerMessage)
        {
            safeCount = 0;
            maxBlocksPerMessage = MaxBlocksPerRemoveBlocksMessage;

            if (!Enabled)
                return false;

            if (_insideChunkedRemoveBlocksSend)
                return false;

            if (from == null || blocks == null || numblocks <= 0)
                return false;

            safeCount = Math.Min(numblocks, blocks.Length);

            if (safeCount <= maxBlocksPerMessage)
                return false;

            return true;
        }
        #endregion

        #region Network Chunking

        /// <summary>
        /// Sends a large RemoveBlocksMessage as multiple smaller RemoveBlocksMessage chunks.
        /// Returns true when the original vanilla send should be skipped.
        /// </summary>
        private static bool TrySendChunkedRemoveBlocksMessage(
            LocalNetworkGamer from,
            int numblocks,
            IntVector3[] blocks,
            bool doEffects)
        {
            bool guardSet = false;

            try
            {

                if (!ShouldChunkRemoveBlocksMessage(from, numblocks, blocks, out int safeCount, out int maxBlocksPerMessage))
                    return false;

                _insideChunkedRemoveBlocksSend = true;
                guardSet = true;

                int sent = 0;
                while (sent < safeCount)
                {
                    int take = Math.Min(maxBlocksPerMessage, safeCount - sent);
                    var chunk = new IntVector3[take];

                    Array.Copy(blocks, sent, chunk, 0, take);
                    RemoveBlocksMessage.Send(from, take, chunk, doEffects);

                    sent += take;
                }

                Log($"[TNT Safety] Split RemoveBlocksMessage: {safeCount} blocks into chunks of {maxBlocksPerMessage}.");

                return true;
            }
            catch (Exception ex)
            {
                Log($"[TNT Safety] RemoveBlocksMessage chunking failed open: {ex.GetType().Name}: {ex.Message}.");
                return false;
            }
            finally
            {
                if (guardSet)
                    _insideChunkedRemoveBlocksSend = false;
            }
        }
        #endregion

        #region Harmony Patches

        /// <summary>
        /// Splits oversized RemoveBlocksMessage sends into smaller chunks.
        /// This preserves vanilla TNT/C4 chain behavior while reducing the chance of one massive
        /// reliable packet hard-crashing a multiplayer non-host client.
        /// </summary>
        [HarmonyPatch(typeof(RemoveBlocksMessage), nameof(RemoveBlocksMessage.Send))]
        private static class RemoveBlocksMessage_Send_ChunkLargePayloads
        {
            private static bool Prefix(LocalNetworkGamer from, int numblocks, IntVector3[] blocks, bool doEffects)
            {
                if (TrySendChunkedRemoveBlocksMessage(from, numblocks, blocks, doEffects))
                    return false;

                return true;
            }
        }
        #endregion
    }
}