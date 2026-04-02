/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060   // Silence IDE0060.
using System.Collections.Generic;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using HarmonyLib;
using System.IO;
using System;

using static CastleWallsMk2.IGMainUI;

namespace CastleWallsMk2
{
    /// <summary>
    /// Harmony patches for the Network-Calls system:
    /// - Dynamically discovers all CMZ message types under DNA.CastleMinerZ.Net.
    /// - Hooks message serialization methods:
    ///   • SendData(BinaryWriter) for outgoing payloads.
    ///   • Receive/RecieveData(BinaryReader) for incoming payloads.
    /// - (Optional) Captures a raw byte slice of the message span for RX/TX when enabled.
    /// - Enforces Rules-tab block modes:
    ///   • IN  => drop packets early (AppendNewDataPacket) / skip decode.
    ///   • OUT => suppress sends (Message.DoSend), with hot-path overrides (ex: PlayerUpdateMessage.Send).
    ///
    /// IMPORTANT:
    /// - Discovery must run BEFORE Harmony patching (call DiscoverTargets() early in GamePatches.ApplyAllPatches()).
    /// - Patches stay installed for the life of the process.
    ///   • NetCallsSettings.Enabled gates logging work.
    ///   • NetCallsBlockRules gates hard IN/OUT suppression.
    /// </summary>
    /// <remarks>
    /// High-level flow:
    /// 1) Discover RX/TX target methods from CMZ network message types.
    /// 2) Harmony applies dynamic target patch sets (RX + TX) plus enforcement patches (IN drop / OUT suppress).
    /// 3) Prefix captures starting stream position (for optional raw slice).
    /// 4) Postfix logs a compact description + optional raw hex preview.
    ///
    /// Safety goals:
    /// - Never break game networking if logging/block logic fails (exceptions are swallowed; default is "allow").
    /// - Keep expensive work behind runtime gates (Enabled/LogRX/LogTX/RawEnabled).
    /// </remarks>
    internal static class NetCallsMessagePatches
    {
        #region Message Catalog (Discovery -> UI Rows)

        /// <summary>
        /// Discovery-time message catalog row.
        /// Used by the Rules UI to show which message types have RX/TX methods.
        /// </summary>
        internal struct NetCallMessageRow
        {
            public Type Type;
            public bool HasRx;
            public bool HasTx;
        }

        /// <summary>
        /// Backing list for the message catalog.
        /// Populated during discovery (NetCallsMessagePatches.DiscoverTargets()).
        /// </summary>
        private static readonly List<NetCallMessageRow> _rows = new List<NetCallMessageRow>();

        /// <summary>
        /// Public read-only view of the discovered message rows for UI consumption.
        /// </summary>
        public static IReadOnlyList<NetCallMessageRow> Rows => _rows;

        #endregion

        #region Target Method Caches (Discovered Once Before Patching)

        // RX targets:
        // - RecieveData(BinaryReader) [legacy typo spelling]
        // - ReceiveData(BinaryReader) [correct spelling]
        private static readonly List<MethodBase> _rxTargets = new List<MethodBase>();

        // TX targets:
        // - SendData(BinaryWriter)
        private static readonly List<MethodBase> _txTargets = new List<MethodBase>();

        #endregion

        #region Discovery (Must Run Before Harmony Patch Scan)

        /// <summary>
        /// Scans CMZ network message types and builds the RX/TX Harmony target lists.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Uses a known message type to resolve the correct assembly.
        /// - Limits search to classes in DNA.CastleMinerZ.Net*
        /// - Prefers exact method signatures (BinaryReader/BinaryWriter)
        /// - Skips abstract/generic methods
        /// </remarks>
        public static void DiscoverTargets()
        {
            _rxTargets.Clear();
            _txTargets.Clear();
            _rows.Clear();

            // Find the assembly that contains CMZ network messages by referencing a known type.
            var netAsm = typeof(DNA.CastleMinerZ.Net.BroadcastTextMessage).Assembly;
            var nsPrefix = "DNA.CastleMinerZ.Net";

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var types = netAsm.GetTypes()
                              .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null &&
                                          t.Namespace.StartsWith(nsPrefix, StringComparison.Ordinal))
                              .ToArray();

            foreach (var t in types)
            {
                // RX method: Prefer exact signature with BinaryReader.
                var rx = t.GetMethod("RecieveData", flags, null, new[] { typeof(BinaryReader) }, null)
                      ?? t.GetMethod("ReceiveData", flags, null, new[] { typeof(BinaryReader) }, null);

                // TX method: SendData(BinaryWriter).
                var tx = t.GetMethod("SendData", flags, null, new[] { typeof(BinaryWriter) }, null);

                bool hasRx = (rx is MethodInfo miRx && !miRx.IsAbstract && !miRx.ContainsGenericParameters);
                bool hasTx = (tx is MethodInfo miTx && !miTx.IsAbstract && !miTx.ContainsGenericParameters);

                if (hasRx) _rxTargets.Add(rx);
                if (hasTx) _txTargets.Add(tx);

                // UI row.
                _rows.Add(new NetCallMessageRow { Type = t, HasRx = hasRx, HasTx = hasTx });
            }

            // Optional sort (helps usability)
            _rows.Sort((a, b) => string.Compare(a.Type?.Name, b.Type?.Name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Returns a compact discovery count summary for diagnostics/logging.
        /// </summary>
        public static string TargetCountSummary()
            => "RX:" + _rxTargets.Count + " TX:" + _txTargets.Count;

        #endregion

        #region Harmony Patch - Receive Side (BinaryReader)

        // ---------- Receive side ----------
        [HarmonyPatch]
        private static class Rx_PrefixPostfix
        {
            #region Harmony Target Binding

            /// <summary>
            /// Harmony dynamic target list for RX methods discovered earlier.
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods() => _rxTargets;

            #endregion

            #region Prefix (Capture Start Stream Position)

            /// <summary>
            /// Captures stream position before Receive/RecieveData runs.
            /// This allows Postfix to compute the raw byte span consumed by the message.
            /// </summary>
            static void Prefix(BinaryReader __0, out long __state)
            {
                var s   = __0?.BaseStream;
                __state = (s != null && s.CanSeek) ? s.Position : -1;
            }
            #endregion

            #region Postfix (Pretty Log + Optional Raw Slice)

            /// <summary>
            /// Logs pretty RX output and optional raw bytes after message decode completes.
            /// </summary>
            /// <remarks>
            /// Runtime gates:
            /// - NetCallsSettings.Enabled
            /// - NetCallsSettings.LogRX
            /// - NetCallsSettings.RawEnabled (inside raw helper)
            ///
            /// Safety:
            /// - Swallows all exceptions to avoid disrupting the receive pipeline.
            /// </remarks>
            static void Postfix(object __instance, BinaryReader __0, long __state)
            {
                try
                {
                    if (!NetCallsSettings.Enabled || !NetCallsSettings.LogRX) return;

                    var typeName = __instance?.GetType()?.Name ?? "<null>";

                    string pretty = NetCallsDescribe.Describe(__instance);
                    if (NetCallsSettings.IgnoreEmpties && NetCallsDescribe.IsTrivial(pretty))
                        return;

                    NetCallsLog.AddRX(typeName, pretty);

                    TryLogRaw(__0, __state, "RXRAW", typeName);
                }
                catch
                {
                    // Never break the RX pipeline.
                }
            }
            #endregion
        }
        #endregion

        #region Harmony Patch - Send Side (BinaryWriter)

        // ---------- Send side ----------
        [HarmonyPatch]
        private static class Tx_PrefixPostfix
        {
            #region Harmony Target Binding

            /// <summary>
            /// Harmony dynamic target list for TX methods discovered earlier.
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods() => _txTargets;

            #endregion

            #region Prefix (Capture Start Stream Position)

            /// <summary>
            /// Captures stream position before SendData runs.
            /// This allows Postfix to compute the raw byte span written by the message.
            /// </summary>
            static void Prefix(BinaryWriter __0, out long __state)
            {
                var s   = __0?.BaseStream;
                __state = (s != null && s.CanSeek) ? s.Position : -1;
            }
            #endregion

            #region Postfix (Pretty Log + Optional Raw Slice)

            /// <summary>
            /// Logs pretty TX output and optional raw bytes after message encode completes.
            /// </summary>
            /// <remarks>
            /// Runtime gates:
            /// - NetCallsSettings.Enabled
            /// - NetCallsSettings.LogTX
            /// - NetCallsSettings.RawEnabled (inside raw helper)
            ///
            /// Safety:
            /// - Swallows all exceptions to avoid disrupting the send pipeline.
            /// </remarks>
            static void Postfix(object __instance, BinaryWriter __0, long __state)
            {
                try
                {
                    if (!NetCallsSettings.Enabled || !NetCallsSettings.LogTX) return;

                    var typeName = __instance?.GetType()?.Name ?? "<null>";

                    string pretty = NetCallsDescribe.Describe(__instance);
                    if (NetCallsSettings.IgnoreEmpties && NetCallsDescribe.IsTrivial(pretty))
                        return;

                    NetCallsLog.AddTX(typeName, pretty);

                    TryLogRaw(__0, __state, "TXRAW", typeName);
                }
                catch
                {
                    // Never break the TX pipeline.
                }
            }
            #endregion
        }
        #endregion

        #region Network Calls - Enforcement (IN/OUT Block Rules)

        #region MessageId -> Type Mapping (DNA.Net.Message._messageTypes)

        /// <summary>
        /// Lightweight map from message id (first byte of packet) to the message Type.
        /// </summary>
        /// <remarks>
        /// Implementation detail:
        /// - Reads DNA.Net.Message private static Type[] _messageTypes via reflection (AccessTools.Field).
        /// - Lazy initializes once and caches the array.
        /// - If reflection fails (field missing/renamed), mapping stays disabled (TryGetType returns false).
        /// </remarks>
        internal static class NetCallsMsgIdMap
        {
            private static readonly object _lock = new object();
            private static Type[] _types;

            #region Init / Reflection Bootstrap

            /// <summary>
            /// Loads DNA.Net.Message._messageTypes into a cached Type[] (once).
            /// </summary>
            /// <remarks>
            /// This array is expected to be indexed by message id byte:
            /// - packet[0] = messageId.
            /// - _messageTypes[messageId] = Type of that message.
            /// </remarks>
            private static void EnsureInit()
            {
                if (_types != null) return;
                lock (_lock)
                {
                    if (_types != null) return;

                    try
                    {
                        // DNA.Net.Message private static Type[] _messageTypes;
                        var f = HarmonyLib.AccessTools.Field(typeof(DNA.Net.Message), "_messageTypes");
                        _types = (Type[])f.GetValue(null);
                    }
                    catch
                    {
                        _types = null;
                    }
                }
            }
            #endregion

            #region Public Lookup

            /// <summary>
            /// Attempts to resolve the message Type for a given message id byte.
            /// </summary>
            /// <remarks>
            /// Returns false when:
            /// - mapping is unavailable (reflection failed).
            /// - messageId is out of range.
            /// - mapped type is null.
            /// </remarks>
            public static bool TryGetType(byte messageId, out Type t)
            {
                EnsureInit();
                t = null;
                if (_types == null) return false;
                if (messageId >= _types.Length) return false;
                t = _types[messageId];
                return t != null;
            }
            #endregion
        }
        #endregion

        #region IN Block - Drop Packets Before Queueing (AppendNewDataPacket)

        /// <summary>
        /// Drops incoming packets before they are queued when the message type is blocked IN.
        /// </summary>
        /// <remarks>
        /// Overload: AppendNewDataPacket(byte[] data, NetworkGamer sender)
        ///
        /// Safety:
        /// - Always allow local echo packets (sender.IsLocal) to prevent self-effect breakage.
        /// - Default to allow on any exception.
        /// </remarks>
        [HarmonyPatch(typeof(LocalNetworkGamer),
              nameof(LocalNetworkGamer.AppendNewDataPacket),
              new[] { typeof(byte[]), typeof(NetworkGamer) })]
        private static class Lng_AppendNewDataPacket_Filter
        {
            static bool Prefix(byte[] data, NetworkGamer sender)
            {
                try
                {
                    if (data == null || data.Length < 1) return true;

                    // IMPORTANT: Don't block local echo packets (prevents grenade/gunshot self-breakage).
                    if (sender != null && sender.IsLocal) return true;

                    byte id = data[0];
                    if (NetCallsMsgIdMap.TryGetType(id, out var t) && t != null && NetCallsBlockRules.BlockIn(t))
                        return false; // DROP packet before it is queued.
                }
                catch { }
                return true;
            }
        }

        /// <summary>
        /// Drops incoming packets before they are queued when the message type is blocked IN.
        /// </summary>
        /// <remarks>
        /// Overload: AppendNewDataPacket(byte[] data, int offset, int length, NetworkGamer sender)
        ///
        /// Safety:
        /// - Validates offset/length bounds before reading.
        /// - Always allow local echo packets (sender.IsLocal).
        /// - Default to allow on any exception.
        /// </remarks>
        [HarmonyPatch(typeof(LocalNetworkGamer),
              nameof(LocalNetworkGamer.AppendNewDataPacket),
              new[] { typeof(byte[]), typeof(int), typeof(int), typeof(NetworkGamer) })]
        private static class Lng_AppendNewDataPacket_Offset_Filter
        {
            static bool Prefix(byte[] data, int offset, int length, NetworkGamer sender)
            {
                try
                {
                    if (data == null || length < 1) return true;
                    if (offset < 0 || offset >= data.Length) return true;
                    if (offset + length > data.Length) return true;

                    // IMPORTANT: don't block local echo packets.
                    if (sender != null && sender.IsLocal) return true;

                    byte id = data[offset];
                    if (NetCallsMsgIdMap.TryGetType(id, out var t) && t != null && NetCallsBlockRules.BlockIn(t))
                        return false; // DROP packet before it is queued
                }
                catch { }
                return true;
            }
        }
        #endregion

        #region OUT Block - Skip Sending Entirely (Message.DoSend Overloads)

        /// <summary>
        /// Blocks outbound network messages by skipping DNA.Net.Message.DoSend(...) when the message type is blocked OUT.
        /// </summary>
        /// <remarks>
        /// Why patch DoSend:
        /// - Prevents any serialization/writes for that message.
        /// - Safer than skipping SendData() which can risk partial packet/frame state.
        ///
        /// Patch behavior:
        /// - Applies to all DoSend overloads (dynamic TargetMethods).
        /// - Default to allow on any exception.
        /// </remarks>
        [HarmonyPatch]
        private static class DoSend_BlockOut
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var t = typeof(DNA.Net.Message);
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Patch all DoSend overloads.
                return t.GetMethods(flags)
                        .Where(m => m.Name == "DoSend" && !m.IsAbstract && !m.ContainsGenericParameters);
            }

            static bool Prefix(object __instance)
            {
                try
                {
                    var msgType = __instance?.GetType();
                    if (msgType != null && NetCallsBlockRules.BlockOut(msgType))
                        return false; // Skip sending entirely.
                }
                catch { }

                return true;
            }
        }
        #endregion

        #region OUT Block - Hot Path Special Case (PlayerUpdateMessage.Send)

        /// <summary>
        /// Explicit block for PlayerUpdateMessage.Send(...) to prevent player update spam when blocked OUT.
        /// </summary>
        /// <remarks>
        /// Priority.First:
        /// - Ensures this prefix runs before other gameplay-related prefixes that may also hook Send().
        /// - This message is high-frequency and is commonly used for movement replication.
        /// </remarks>
        [HarmonyPatch(typeof(PlayerUpdateMessage), nameof(PlayerUpdateMessage.Send))]
        [HarmonyPriority(Priority.First)] // Run before Hug/Vanish prefixes.
        internal static class PlayerUpdateMessage_Send_BlockOut
        {
            static bool Prefix(LocalNetworkGamer from, Player player, CastleMinerZControllerMapping input)
            {
                try
                {
                    // OUT or BOTH => block sending player updates.
                    if (NetCallsBlockRules.BlockOut(typeof(PlayerUpdateMessage)))
                        return false;
                }
                catch { }

                return true;
            }
        }
        #endregion

        #endregion

        #region Raw Byte Logging Helpers (Optional)

        // ---------- Raw dump helper ----------

        /// <summary>
        /// Reader overload: forwards to the shared stream-based raw logger.
        /// </summary>
        private static void TryLogRaw(BinaryReader reader, long startPos, string tag, string typeName)
            => TryLogRaw(reader?.BaseStream, startPos, tag, typeName);

        /// <summary>
        /// Writer overload: forwards to the shared stream-based raw logger.
        /// </summary>
        private static void TryLogRaw(BinaryWriter writer, long startPos, string tag, string typeName)
            => TryLogRaw(writer?.BaseStream, startPos, tag, typeName);

        /// <summary>
        /// Reads a capped slice of bytes from the stream span [startPos, currentPos) and logs it as hex.
        /// </summary>
        /// <remarks>
        /// Behavior:
        /// - No-op if raw logging is disabled
        /// - No-op for non-seekable streams / invalid positions
        /// - Caps read length using NetCallsSettings.RawCap
        /// - Preserves stream position after read
        ///
        /// Safety:
        /// - Swallows all exceptions (logging must never break message flow)
        /// </remarks>
        private static void TryLogRaw(Stream s, long startPos, string tag, string typeName)
        {
            try
            {
                if (!NetCallsSettings.RawEnabled) return;
                if (s == null || !s.CanSeek || startPos < 0) return;

                long end   = s.Position;
                long len   = end - startPos;
                if (len <= 0) return;

                int cap = Math.Max(0, NetCallsSettings.RawCap);
                int toRead = (int)Math.Min((long)cap, len);
                if (toRead <= 0) return;

                byte[] buf = new byte[toRead];

                long save  = s.Position;
                s.Position = startPos;
                int got    = s.Read(buf, 0, toRead);
                s.Position = save;

                if (got > 0)
                {
                    bool truncated = len > got;
                    string hex = BytesToHex(buf, got, truncated);
                    NetCallsLog.AddRaw(tag, typeName, hex);
                }
            }
            catch
            {
                // Swallow.
            }
        }

        /// <summary>
        /// Converts a byte slice to a space-separated uppercase hex string.
        /// Appends " ..." when the raw span was truncated by the configured cap.
        /// </summary>
        private static string BytesToHex(byte[] data, int len, bool truncated)
        {
            var sb = new System.Text.StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            if (truncated) sb.Append(" ...");
            return sb.ToString();
        }
        #endregion
    }
}