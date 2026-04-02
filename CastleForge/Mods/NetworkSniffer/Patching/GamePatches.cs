/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060    // Silence IDE0060.
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Linq;
using HarmonyLib;                     // Harmony patching library.
using System.IO;
using System;

using static ModLoader.LogSystem;     // For Log(...).

namespace NetworkSniffer
{
    /// <summary>
    /// Patch bootstrapper. Applies all message Send/Receive hooks and logs a summary.
    /// </summary>
    class GamePatches
    {
        #region Patcher Initiation

        private static Harmony _harmony;
        private static string  _harmonyId;

        /// <summary>
        /// Entry point to discover message targets, patch them, and start logging.
        /// Idempotent: early-out if already installed.
        /// </summary>
        public static void ApplyAllPatches()
        {
            if (_harmony != null) return;

            Log("[Harmony] Starting game patching.");

            // Prepare log file and write a "New Session" header.
            NetSnifferLog.Init();

            // Find the assembly that contains CMZ network messages by referencing a known type.
            var netAsm   = typeof(DNA.CastleMinerZ.Net.BroadcastTextMessage).Assembly;
            var nsPrefix = "DNA.CastleMinerZ.Net";

            // Build RX/TX target lists (only concrete message types under the namespace).
            AllMessagePatches.DiscoverTargets(netAsm, nsPrefix);

            // Create a Harmony instance unique to this patch set and apply all patches in this assembly.
            _harmonyId = $"castleminerz.mods.{MethodBase.GetCurrentMethod().DeclaringType.Namespace}.patches";
            _harmony   = new Harmony(_harmonyId);
            _harmony.PatchAll(typeof(AllMessagePatches).Assembly);

            // Emit a short summary to both the mods log and the sniffers log.
            Log($"[Harmony] Patched TX/RX on {AllMessagePatches.TargetCountSummary()} message types.");
            NetSnifferLog.Write("INFO", "Sniffer", "Patched TX/RX on " + AllMessagePatches.TargetCountSummary() + " message types.");

            // List the default-exempt messages (if any).
            try
            {
                var excluded = (SnifferSettings.Exclude ?? new HashSet<string>())
                               .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                               .ToArray();

                if (excluded.Length > 0)
                    NetSnifferLog.Write("INFO", "Sniffer", "Default excluded messages: " + string.Join(", ", excluded) + ".");
                else
                    NetSnifferLog.Write("INFO", "Sniffer", "No default message exclusions.");
            }
            catch { /* never block startup logging */ }

            Log($"[Harmony] Patching complete. Success={{{AllMessagePatches.TargetCountSummary()}}}.");
        }

        /// <summary>
        /// Optional: Undo all patches done by this mod (restores original methods).
        /// </summary>
        public static void DisableAll()
        {
            if (_harmony != null)
            {
                Log($"[Harmony] Unpatching all ({_harmonyId}).");
                _harmony.UnpatchAll(_harmonyId);
            }
        }
        #endregion
    }

    #region Harmony Patches (dynamic)

    /// <summary>
    /// Discovers all message types and patches their SendData/Receive(Recieve)Data methods.
    /// RX: Capture start position, then log the message and (optionally) a raw hex slice.
    /// TX: Same on the write side.
    /// </summary>
    internal static class AllMessagePatches
    {
        // Concrete methods we'll patch at runtime.
        private static readonly List<MethodBase> _rxTargets = new List<MethodBase>();
        private static readonly List<MethodBase> _txTargets = new List<MethodBase>();

        /// <summary>
        /// Find all non-abstract classes in the target namespace and collect
        /// their RX/TX methods (handling the common "RecieveData" misspelling).
        /// Also captures type names for /sniff types.
        /// </summary>
        public static void DiscoverTargets(Assembly netAsm, string nsPrefix)
        {
            _rxTargets.Clear();
            _txTargets.Clear();

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var types = netAsm.GetTypes()
                              .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.StartsWith(nsPrefix, StringComparison.Ordinal))
                              .ToArray();

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in types)
            {
                // RX method: Prefer exact signature with BinaryReader.
                var rx = t.GetMethod("RecieveData", flags, null, new[] { typeof(BinaryReader) }, null)
                      ?? t.GetMethod("ReceiveData", flags, null, new[] { typeof(BinaryReader) }, null);
                if (rx is MethodInfo miRx && !miRx.IsAbstract && !miRx.ContainsGenericParameters)
                    _rxTargets.Add(rx);

                // TX method: SendData(BinaryWriter).
                var tx = t.GetMethod("SendData", flags, null, new[] { typeof(BinaryWriter) }, null);
                if (tx is MethodInfo miTx && !miTx.IsAbstract && !miTx.ContainsGenericParameters)
                    _txTargets.Add(tx);

                names.Add(t.Name);
            }

            // Provide a stable list for /sniff types listing.
            SnifferSettings.KnownTypes = names.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Helper for the "Patched TX/RX on ..." summary.</summary>
        public static string TargetCountSummary()
        {
            return "RX:" + _rxTargets.Count.ToString(CultureInfo.InvariantCulture) +
                   " TX:" + _txTargets.Count.ToString(CultureInfo.InvariantCulture);
        }

        // ---------- Receive side ----------
        [HarmonyPatch]
        private static class Rx_PrefixPostfix
        {
            // Harmony will patch each target returned here.
            static IEnumerable<MethodBase> TargetMethods() => _rxTargets;

            // Save the starting position (if the stream is seekable) so we can hex-dump later.
            static void Prefix(BinaryReader __0, out long __state)
            {
                var s = __0?.BaseStream;
                __state = (s != null && s.CanSeek) ? s.Position : -1;
            }

            // After the message object populated itself from the stream, pretty print + raw slice.
            static void Postfix(object __instance, BinaryReader __0, long __state)
            {
                try
                {
                    if (!SnifferSettings.LogRX) return;

                    var typeName = __instance?.GetType()?.Name ?? "<null>";

                    // Fast-path filters (enabled, include/exclude, sample). Avoid building strings if not needed.
                    if (!SnifferSettings.Enabled) return;
                    if (SnifferSettings.Include.Count > 0 && !SnifferSettings.Include.Contains(typeName)) return;
                    if (SnifferSettings.Exclude.Contains(typeName)) return;
                    if (!SnifferSettings.ShouldLog(typeName)) return;

                    // Pretty payload string.
                    string pretty = MsgDescribe.Describe(__instance);

                    // Optional: Skip trivial payloads entirely.
                    if (SnifferSettings.IgnoreEmpties && MsgDescribe.IsTrivial(pretty))
                        return;

                    NetSnifferLog.Write("RX", typeName, pretty);

                    // Optional: Raw bytes from the stream portion this message consumed.
                    TryLogRaw(__0, __state, "RXRAW", typeName);
                }
                catch { /* never break RX */ }
            }
        }

        // ---------- Send side ----------
        [HarmonyPatch]
        private static class Tx_PrefixPostfix
        {
            static IEnumerable<MethodBase> TargetMethods() => _txTargets;

            static void Prefix(BinaryWriter __0, out long __state)
            {
                var s = __0?.BaseStream;
                __state = (s != null && s.CanSeek) ? s.Position : -1;
            }

            static void Postfix(object __instance, BinaryWriter __0, long __state)
            {
                try
                {
                    if (!SnifferSettings.LogTX) return;

                    var typeName = __instance?.GetType()?.Name ?? "<null>";

                    if (!SnifferSettings.Enabled) return;
                    if (SnifferSettings.Include.Count > 0 && !SnifferSettings.Include.Contains(typeName)) return;
                    if (SnifferSettings.Exclude.Contains(typeName)) return;
                    if (!SnifferSettings.ShouldLog(typeName)) return;

                    string pretty = MsgDescribe.Describe(__instance);
                    if (SnifferSettings.IgnoreEmpties && MsgDescribe.IsTrivial(pretty))
                        return;

                    NetSnifferLog.Write("TX", typeName, pretty);

                    TryLogRaw(__0, __state, "TXRAW", typeName);
                }
                catch { /* never break TX */ }
            }
        }

        // ---------- Raw dump helper overloads ----------
        private static void TryLogRaw(BinaryReader reader, long startPos, string tag, string typeName)
            => TryLogRaw(reader?.BaseStream, startPos, tag, typeName);

        private static void TryLogRaw(BinaryWriter writer, long startPos, string tag, string typeName)
            => TryLogRaw(writer?.BaseStream, startPos, tag, typeName);

        /// <summary>
        /// Dumps a bounded slice of bytes written/read by the message between Prefix and Postfix.
        /// Respects RawEnabled and RawCap.
        /// </summary>
        private static void TryLogRaw(Stream s, long startPos, string tag, string typeName)
        {
            try
            {
                if (!SnifferSettings.RawEnabled) return;
                if (s == null || !s.CanSeek || startPos < 0) return;

                long end = s.Position;
                long len = end - startPos;
                if (len <= 0) return;

                int cap    = Math.Max(0, SnifferSettings.RawCap);
                int toRead = (int)Math.Min((long)cap, len);
                if (toRead <= 0) return;

                byte[] buf = new byte[toRead];
                long save = s.Position;
                s.Position = startPos;
                int got = s.Read(buf, 0, toRead);
                s.Position = save;

                if (got > 0)
                {
                    bool truncated = len > got;
                    NetSnifferLog.WriteRaw(tag, typeName, buf, got, truncated);
                }
            }
            catch { /* swallow - don't disturb pipeline */ }
        }
    }
    #endregion
}