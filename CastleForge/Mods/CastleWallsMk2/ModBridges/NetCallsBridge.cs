/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Threading;
using System.Text;
using System.IO;
using System;

namespace CastleWallsMk2
{
    #region Network Calls - Settings

    /// <summary>
    /// Runtime knobs for the Network-Calls (SendData/ReceiveData) logger.
    /// UI drives these via Start/Stop and (optionally) advanced toggles later.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - Patches remain installed for the life of the process.
    /// - These flags only control whether logging work is performed / emitted.
    /// - Designed to be lightweight and safe to read from patch paths.
    /// </remarks>
    internal static class NetCallsSettings
    {
        #region Master / Direction Toggles

        // Master on/off switch. Patches stay installed; this gate controls logging work.
        public static volatile bool Enabled = false;

        // Direction gating (keeps volume manageable).
        public static bool LogRX = true;
        public static bool LogTX = true;

        #endregion

        #region Raw Payload Preview

        // Raw hex slice (off by default).
        public static bool RawEnabled = false;
        public static int  RawCap     = 256;

        #endregion

        #region Noise Trimming / Pretty Output Cleanup

        // Noise trimming.
        public static bool IgnoreEmpties     = true;
        public static bool PruneEmptyMembers = true;

        #endregion
    }
    #endregion

    #region Network Calls - In-Memory Log

    /// <summary>
    /// Thread-safe in-memory log buffer for network message call traces.
    /// Mirrors ChatLog's design: stable ordering via (TimeUtc, Sequence).
    /// </summary>
    /// <remarks>
    /// Intended use:
    /// - UI reads snapshots for rendering/filtering.
    /// - Patches append entries from game/network threads.
    /// - Optional sinks (file streamer) can subscribe to EntryAdded.
    /// </remarks>
    internal static class NetCallsLog
    {
        #region Entry Model

        /// <summary>
        /// One log row in the in-memory buffer.
        /// </summary>
        /// <remarks>
        /// Tag examples:
        /// - RX / TX         = pretty message lines
        /// - RXRAW / TXRAW   = optional raw byte previews
        /// - ERR             = patch/formatter errors
        /// - INFO            = UI/system status messages
        /// </remarks>
        internal struct Entry
        {
            public DateTime TimeUtc;
            public string   Tag;       // RX / TX / RXRAW / TXRAW / ERR / INFO.
            public string   TypeName;  // Message type name.
            public string   Text;      // Pretty payload (or raw hex line).
            public long     Sequence;
        }

        #endregion

        #region Backing Storage / Synchronization

        private static readonly List<Entry> _entries    = new List<Entry>(8192);
        private static readonly object      _lock       = new object();
        private static long                 _seq;
        private static int                  _maxEntries = 2000;

        #endregion

        #region Configuration / Stats

        /// <summary>
        /// Maximum number of rows kept in memory for the UI buffer.
        /// Clamped to a safe range and trims immediately when lowered.
        /// </summary>
        public static int MaxEntries
        {
            get => Volatile.Read(ref _maxEntries);
            set
            {
                int v = Math.Max(100, Math.Min(value, 200_000));
                Interlocked.Exchange(ref _maxEntries, v);
                lock (_lock) TrimIfNeeded_NoLock();
            }
        }

        /// <summary>
        /// Current number of rows stored in memory.
        /// </summary>
        public static int Count { get { lock (_lock) return _entries.Count; } }

        #endregion

        #region Events / Streaming Hooks

        // Optional streaming sink(s).
        public static event Action<Entry> EntryAdded;

        #endregion

        #region Internal Helpers

        /// <summary>
        /// Trims oldest entries when the buffer exceeds MaxEntries.
        /// Caller must hold _lock.
        /// </summary>
        private static void TrimIfNeeded_NoLock()
        {
            int max = _maxEntries;
            if (_entries.Count > max)
                _entries.RemoveRange(0, _entries.Count - max);
        }
        #endregion

        #region Add / Append API

        /// <summary>
        /// Adds a new entry to the in-memory log (thread-safe).
        /// </summary>
        /// <remarks>
        /// Behavior:
        /// - Skips null/empty text.
        /// - Assigns a monotonically increasing Sequence for stable ordering.
        /// - Trims if over cap.
        /// - Raises EntryAdded for optional sinks (file streamer, etc.).
        /// </remarks>
        public static void Add(string tag, string typeName, string text, DateTime whenUtc)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_lock)
            {
                _entries.Add(new Entry
                {
                    TimeUtc   = whenUtc,
                    Tag       = tag ?? "INFO",
                    TypeName  = typeName ?? "(unknown)",
                    Text      = text,
                    Sequence  = Interlocked.Increment(ref _seq)
                });
                TrimIfNeeded_NoLock();

                // Fire outside callers with latest entry snapshot.
                try { EntryAdded?.Invoke(_entries[_entries.Count - 1]); } catch { }
            }
        }

        /// <summary>
        /// Convenience helper for pretty receive lines.
        /// </summary>
        public static void AddRX(string typeName, string pretty)
            => Add("RX", typeName, pretty, DateTime.UtcNow);

        /// <summary>
        /// Convenience helper for pretty send lines.
        /// </summary>
        public static void AddTX(string typeName, string pretty)
            => Add("TX", typeName, pretty, DateTime.UtcNow);

        /// <summary>
        /// Convenience helper for raw byte preview lines (RXRAW/TXRAW).
        /// </summary>
        public static void AddRaw(string tag, string typeName, string hex)
            => Add(tag, typeName, hex, DateTime.UtcNow);

        /// <summary>
        /// Appends a general informational/system/UI line.
        /// </summary>
        public static void Append(string source, string text)
            => Add("INFO", source ?? "UI", text ?? string.Empty, DateTime.UtcNow);

        #endregion

        #region Mutation / Maintenance

        /// <summary>
        /// Clears the in-memory buffer only (does not affect auto file logs).
        /// </summary>
        public static void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        /// <summary>
        /// Removes a single entry by sequence id.
        /// Returns true if a row was found and removed.
        /// </summary>
        public static bool Remove(long sequence)
        {
            lock (_lock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Sequence == sequence)
                    {
                        _entries.RemoveAt(i);
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion

        #region Snapshot / Export

        /// <summary>
        /// Returns a sorted copy suitable for UI rendering/filtering.
        /// </summary>
        /// <remarks>
        /// Sort order is stable and deterministic:
        /// 1) TimeUtc
        /// 2) Sequence (tie-breaker)
        /// </remarks>
        public static List<Entry> SnapshotSorted()
        {
            lock (_lock)
            {
                var copy = new List<Entry>(_entries);
                copy.Sort((a, b) =>
                {
                    int c = a.TimeUtc.CompareTo(b.TimeUtc);
                    return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
                });
                return copy;
            }
        }

        /// <summary>
        /// Dumps the current in-memory log to a plain text string.
        /// Used by the UI "Save" action for custom file export.
        /// </summary>
        public static string DumpText(bool includeTimestamps = true, bool includeTag = true)
        {
            List<Entry> snapshot;
            lock (_lock)
                snapshot = new List<Entry>(_entries);

            int alignColumn = 0;

            foreach (var e in snapshot)
            {
                var prefix = new StringBuilder();

                if (includeTimestamps)
                    prefix.Append('[').Append(e.TimeUtc.ToString("HH:mm:ss")).Append(']');

                if (includeTag)
                    prefix.Append('[').Append(e.Tag).Append(']');

                prefix.Append('[').Append(e.TypeName).Append(']');

                alignColumn = Math.Max(alignColumn, prefix.Length + 1);
            }

            var sb = new StringBuilder(Math.Min(1_000_000, snapshot.Count * 96));

            foreach (var e in snapshot)
            {
                var prefix = new StringBuilder();

                if (includeTimestamps)
                    prefix.Append('[').Append(e.TimeUtc.ToString("HH:mm:ss")).Append(']');

                if (includeTag)
                    prefix.Append('[').Append(e.Tag).Append(']');

                prefix.Append('[').Append(e.TypeName).Append(']');

                int pad = Math.Max(1, alignColumn - prefix.Length);

                sb.Append(prefix)
                  .Append(' ', pad)
                  .AppendLine(e.Text ?? string.Empty);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a Network-Calls log entry into an aligned single-line string by
        /// growing a shared prefix column width and padding the message text to match.
        /// </summary>
        public static int _alignColumn = 46;
        public static string FormatAlignedLine(NetCallsLog.Entry e)
        {
            string prefix = $"[{e.TimeUtc:HH:mm:ss}][{e.Tag}][{e.TypeName}]";

            lock (_lock)
            {
                if (prefix.Length + 1 > _alignColumn)
                    _alignColumn = prefix.Length + 1;

                int pad = Math.Max(1, _alignColumn - prefix.Length);
                return prefix + new string(' ', pad) + (e.Text ?? string.Empty);
            }
        }

        /// <summary>
        /// Formats a single entry as a human-readable one-line string.
        /// Shared by UI rendering and the auto file streamer.
        /// </summary>
        public static string ToPrettyLine(in Entry e)
        {
            return $"[{e.TimeUtc:HH:mm:ss}][{e.Tag}][{e.TypeName}] {e.Text}";
        }
        #endregion
    }
    #endregion

    #region Network Calls - Auto File Streamer

    /// <summary>
    /// When capture is enabled, stream every NetCallsLog entry to a session file under:
    ///   !Mods\CastleWallsMk2\!NetLogs
    /// This is intentionally independent of the UI (leaving the tab doesn't stop it).
    /// </summary>
    /// <remarks>
    /// Design notes:
    /// - Non-blocking queue decouples patch logging from disk I/O.
    /// - Worker thread performs batched writes + periodic flushes.
    /// - Start/Stop only toggles streaming state (thread may remain alive).
    /// </remarks>
    internal static class NetCallsFileStreamer
    {
        #region Sync / Thread State

        private static readonly object _lock = new object();

        private static Thread          _thread;
        private static volatile bool   _threadRunning;
        private static volatile bool   _enabled;
        private static volatile bool   _shutdown;

        #endregion

        #region Writer / Queue

        private static StreamWriter                     _writer;
        private static readonly ConcurrentQueue<string> _q   = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent          _sig = new AutoResetEvent(false);

        #endregion

        #region Session Path State

        private static string _autoPath;

        /// <summary>
        /// Default folder for automatic session net logs.
        /// </summary>
        public static string AutoDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(IGMainUI).Namespace, "!NetLogs");

        /// <summary>
        /// Current session file path (if a session has been started).
        /// </summary>
        public static string AutoPath
        {
            get { lock (_lock) return _autoPath; }
        }

        /// <summary>
        /// Whether auto streaming is currently enabled.
        /// </summary>
        public static bool Enabled => _enabled;

        #endregion

        #region Static Initialization

        static NetCallsFileStreamer()
        {
            // One-time subscription.
            NetCallsLog.EntryAdded += OnEntryAdded;
        }
        #endregion

        #region Public Lifecycle

        /// <summary>
        /// Starts (or restarts) auto file streaming to a new session file.
        /// </summary>
        /// <remarks>
        /// - Creates the output directory if needed.
        /// - Opens a new timestamped file.
        /// - Writes a session header line.
        /// - Ensures the background worker thread is running.
        /// </remarks>
        public static void EnableNewSession()
        {
            lock (_lock)
            {
                if (_shutdown) return;

                Directory.CreateDirectory(AutoDir);

                _autoPath = Path.Combine(AutoDir, $"NetCalls-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                OpenWriter_NoLock(_autoPath);

                // Reset text alignment.
                NetCallsLog._alignColumn = 46;

                // Session header.
                try
                {
                    _writer?.WriteLine($"[{DateTime.Now:O}][NetCalls] ===================== New Session =====================");
                    _writer?.Flush();
                }
                catch { }

                _enabled = true;
                EnsureThread_NoLock();
            }
        }

        /// <summary>
        /// Stops auto file streaming and closes the current writer.
        /// In-memory logging/UI buffer remain unaffected.
        /// </summary>
        public static void Disable()
        {
            lock (_lock)
            {
                _enabled = false;
                try { _writer?.Flush();   } catch { }
                try { _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }

        /// <summary>
        /// Final shutdown for process/mod unload scenarios.
        /// Stops streaming, disposes writer, and signals the worker to exit.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                _shutdown = true;
                _enabled  = false;
                try { _writer?.Flush();   } catch { }
                try { _writer?.Dispose(); } catch { }
                _writer = null;
            }

            // Stop worker loop.
            _sig.Set();
        }
        #endregion

        #region Internal Writer / Thread Helpers

        /// <summary>
        /// Opens the output writer for the given file path.
        /// Caller must hold _lock.
        /// </summary>
        private static void OpenWriter_NoLock(string path)
        {
            try
            {
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = false };
            }
            catch
            {
                _writer = null;
            }
        }

        /// <summary>
        /// Ensures the worker thread exists and is running.
        /// Caller must hold _lock.
        /// </summary>
        private static void EnsureThread_NoLock()
        {
            if (_threadRunning) return;

            _threadRunning = true;
            _thread = new Thread(Worker) { IsBackground = true, Name = "NetCallsFileStreamer" };
            _thread.Start();
        }
        #endregion

        #region Event Sink (NetCallsLog -> Queue)

        /// <summary>
        /// Receives new log entries and enqueues them for async disk writing.
        /// </summary>
        /// <remarks>
        /// This should stay cheap/non-blocking:
        /// - Formatting a single line
        /// - Queue enqueue
        /// - Signal worker
        /// </remarks>
        private static void OnEntryAdded(NetCallsLog.Entry e)
        {
            if (!_enabled) return;

            // Keep formatting work cheap.
            string line = NetCallsLog.FormatAlignedLine(e);
            _q.Enqueue(line);
            _sig.Set();
        }
        #endregion

        #region Worker Thread

        /// <summary>
        /// Background writer loop that drains the queue and writes lines to disk.
        /// </summary>
        /// <remarks>
        /// Safety:
        /// - Swallows exceptions intentionally (never crash the mod/game from a logger thread).
        /// - Flushes/disposes writer in finally.
        /// </remarks>
        private static void Worker()
        {
            try
            {
                while (true)
                {
                    _sig.WaitOne(250);

                    if (_shutdown)
                        break;

                    // Drain queue.
                    while (_q.TryDequeue(out var line))
                    {
                        if (!_enabled) continue;

                        lock (_lock)
                        {
                            if (_writer == null) continue;
                            try { _writer.WriteLine(line); } catch { }
                        }
                    }

                    // Periodic flush while enabled.
                    if (_enabled)
                    {
                        lock (_lock)
                        {
                            try { _writer?.Flush(); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // Never throw off background thread.
            }
            finally
            {
                _threadRunning = false;
                lock (_lock)
                {
                    try { _writer?.Flush();   } catch { }
                    try { _writer?.Dispose(); } catch { }
                    _writer = null;
                }
            }
        }
        #endregion
    }
    #endregion

    #region Network Calls - Pretty Printer (MsgDescribe Port)

    /// <summary>
    /// Port of NetworkSniffer.MsgDescribe into CastleWallsMk2:
    /// Converts a message object into a compact readable one-liner.
    /// </summary>
    /// <remarks>
    /// Goals:
    /// - Safe-ish reflection-based inspection for unknown message types.
    /// - Compact output for UI rows and file logs.
    /// - Avoid fragile getters that can throw in some game builds/types.
    /// </remarks>
    internal static class NetCallsDescribe
    {
        #region Formatter Registration / Caches

        // Optional per-type custom formatters (fast path for known message types).
        private static readonly Dictionary<Type, Func<object, string>> _formatters =
            new Dictionary<Type, Func<object, string>>();

        // Reflection member cache to avoid repeatedly discovering fields/properties.
        private static readonly ConcurrentDictionary<Type, MemberInfo[]> _memberCache =
            new ConcurrentDictionary<Type, MemberInfo[]>();

        #endregion

        #region Public API

        /// <summary>
        /// Registers a custom formatter for a specific message type.
        /// </summary>
        /// <remarks>
        /// If present, custom formatters are used before the reflection dump path.
        /// Exceptions are swallowed to avoid breaking capture.
        /// </remarks>
        public static void RegisterFormatter<T>(Func<T, string> f) where T : class
        {
            _formatters[typeof(T)] = o => f((T)o);
        }

        /// <summary>
        /// Returns a compact, readable one-line description for a message object.
        /// </summary>
        public static string Describe(object msg)
        {
            if (msg == null) return "null";

            var t = msg.GetType();
            if (_formatters.TryGetValue(t, out var fmt))
            {
                try { return fmt(msg); } catch { }
            }
            return DumpObject(msg);
        }
        #endregion

        #region Reflection Dump Core

        /// <summary>
        /// Reflection-based object dumper with depth/item limits to prevent runaway output.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Special-cases NetworkGamer to avoid unsafe getter access patterns.
        /// - Prunes trivial/empty members when enabled in settings.
        /// - Handles primitives, enums, vectors, byte arrays, enumerables, and complex objects.
        /// </remarks>
        private static string DumpObject(object obj, int depth = 0, int maxDepth = 2, int maxItems = 8)
        {
            if (obj == null) return "null";
            var t = obj.GetType();

            // IMPORTANT: Some NetworkGamer getters can throw in certain builds; avoid getters entirely.
            if (obj is DNA.Net.GamerServices.NetworkGamer)
                return DescribeNetworkGamer(obj);

            // Primitives & common leaf cases.
            if (t.IsPrimitive || obj is string || obj is decimal) return PrimitiveToString(obj);
            if (obj is Guid)         return obj.ToString();
            if (obj is Enum)         return obj.ToString();
            if (obj is Vector2 v2)   return $"{{X:{v2.X},Y:{v2.Y}}}";
            if (obj is Vector3 v3)   return $"{{X:{v3.X},Y:{v3.Y},Z:{v3.Z}}}";
            if (obj is byte[] bytes) return HexPreview(bytes, 64);

            // IEnumerable (arrays, lists, etc.).
            if (obj is System.Collections.IEnumerable en && !(obj is string))
            {
                if (depth >= maxDepth) return "[...]";
                var sb = new StringBuilder();
                sb.Append("[");
                int i = 0;
                foreach (var it in en)
                {
                    if (i > 0) sb.Append(", ");
                    if (i >= maxItems) { sb.Append("..."); break; }
                    sb.Append(DumpObject(it, depth + 1, maxDepth, maxItems));
                    i++;
                }
                sb.Append("]");
                return sb.ToString();
            }

            if (depth >= maxDepth) return "{...}";

            var members = _memberCache.GetOrAdd(t, DiscoverMembers);
            var parts   = new List<string>(members.Length);

            foreach (var m in members)
            {
                object val = null;
                try
                {
                    if (m is PropertyInfo pi && pi.CanRead && pi.GetIndexParameters().Length == 0)
                        val = pi.GetValue(obj, null);
                    else if (m is FieldInfo fi)
                        val = fi.GetValue(obj);

                    var child = DumpObject(val, depth + 1, maxDepth, maxItems);

                    if (NetCallsSettings.IgnoreEmpties && NetCallsSettings.PruneEmptyMembers && IsTrivial(child))
                        continue;

                    parts.Add($"{m.Name}: {child}");
                }
                catch { }
            }

            if (parts.Count == 0) return "{...}";
            return "{ " + string.Join(", ", parts) + " }";
        }
        #endregion

        #region Reflection Member Discovery

        /// <summary>
        /// Discovers readable properties + fields for a type (cached).
        /// </summary>
        /// <remarks>
        /// Filtering:
        /// - Skips indexers
        /// - Skips compiler backing field names
        /// - Includes public and non-public instance members
        /// </remarks>
        private static MemberInfo[] DiscoverMembers(Type t)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var props  = t.GetProperties(flags);
            var fields = t.GetFields(flags);

            var list = new List<MemberInfo>(props.Length + fields.Length);

            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (p.Name.Contains("k__BackingField")) continue;
                list.Add(p);
            }

            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f == null) continue;
                if (f.Name.Contains("k__BackingField")) continue;
                list.Add(f);
            }

            return list.ToArray();
        }
        #endregion

        #region Formatting Helpers

        /// <summary>
        /// Formats primitive/leaf values into stable readable text.
        /// </summary>
        private static string PrimitiveToString(object v)
        {
            if (v is string s)
            {
                if (s.Length > 160) s = s.Substring(0, 157) + "...";
                return "\"" + s.Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
            }
            if (v is bool b) return b ? "true" : "false";
            if (v is char c) return "'" + c + "'";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns a capped hex preview of a byte array.
        /// </summary>
        /// <remarks>
        /// Used for raw payload previews and byte[] leaf dumps.
        /// </remarks>
        private static string HexPreview(byte[] data, int max)
        {
            int n = Math.Min(data.Length, max);
            var sb = new StringBuilder(n * 3);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            if (data.Length > n) sb.Append(" ...");
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if a formatted child value is considered empty/trivial noise.
        /// </summary>
        /// <remarks>
        /// Used to prune object dumps when IgnoreEmpties + PruneEmptyMembers are enabled.
        /// </remarks>
        internal static bool IsTrivial(string s)
        {
            if (s == null) return true;
            var t = s.Trim();
            if (t.Length == 0) return true;

            if (string.Equals(t, "null", StringComparison.OrdinalIgnoreCase)) return true;
            if (t == "{...}" || t == "{}" || t == "[]" || t == "[...]") return true;

            if ((t.StartsWith("{") && t.EndsWith("}")) ||
                (t.StartsWith("[") && t.EndsWith("]")))
            {
                var inner = t.Substring(1, t.Length - 2).Trim();
                if (inner.Length == 0) return true;
                if (inner == "...") return true;
            }
            return false;
        }
        #endregion

        #region NetworkGamer Special Handling

        /// <summary>
        /// Deterministic formatter for NetworkGamer using a small whitelist of known-safe members.
        /// No field-name guessing, no broad reflection scan, no session lookup fallback.
        /// </summary>
        /// <remarks>
        /// Safe members in this build:
        /// - Gamertag (from Gamer._gamerTag)
        /// - Id
        /// - IsLocal
        /// - IsHost
        /// - AlternateAddress
        ///
        /// Avoid generic reflection dumping on NetworkGamer because some properties in the type
        /// throw NotImplementedException in certain builds (HasVoice, IsReady, Machine, etc.).
        /// </remarks>
        private static string DescribeNetworkGamer(object gamer)
        {
            if (!(gamer is DNA.Net.GamerServices.NetworkGamer ng))
                return "<NetworkGamer>";

            bool weAreHost = CastleMinerZGame.Instance?.CurrentNetworkSession?.IsHost ?? false;

            #pragma warning disable IDE0059
            #pragma warning disable CS0219
            string tag      = null;
            byte   id       = 0;
            bool   gotId    = false;
            bool   isLocal  = false;
            bool   gotLocal = false;
            bool   isHost   = false;
            bool   gotHost  = false;
            ulong  alt      = 0;
            bool   gotAlt   = false;
            #pragma warning restore IDE0059
            #pragma warning restore CS0219

            try { tag     = ng.Gamertag;                          } catch { }
            try { id      = ng.Id;               gotId    = true; } catch { }
            try { isLocal = ng.IsLocal;          gotLocal = true; } catch { }
            try { isHost  = ng.IsHost;           gotHost  = true; } catch { }
            try { alt     = ng.AlternateAddress; gotAlt   = true; } catch { }

            var sb = new StringBuilder();
            sb.Append('<');

            if (!string.IsNullOrWhiteSpace(tag))
                sb.Append(tag);
            else
                sb.Append("NetworkGamer");

            // if (gotId)                sb.Append(" #").Append(id);
            // if (gotLocal)             sb.Append(isLocal ? " local" : " remote");
            // if (gotHost && isHost)    sb.Append(" host");
            // if (gotAlt && alt != 0UL) sb.Append(" alt:").Append(alt);

            sb.Append('>');

            if (gotId)                             sb.Append(" (id:").Append(id).Append(")");
            if (gotAlt && alt != 0UL && weAreHost) sb.Append(" (alt:").Append(alt).Append(")"); // Append only if we're host. We capture host's alt elsewhere.

            return sb.ToString();
        }
        #endregion
    }
    #endregion

    #region Network Calls - Controller (Start/Stop)

    /// <summary>
    /// Small UI-facing controller for starting/stopping Network-Calls capture.
    /// </summary>
    /// <remarks>
    /// Responsibilities:
    /// - Flip the runtime capture gate (NetCallsSettings.Enabled).
    /// - Start/stop auto file streaming session.
    /// - Append informational UI log lines.
    /// </remarks>
    internal static class NetCallsController
    {
        #region State

        /// <summary>
        /// True when capture is currently enabled.
        /// </summary>
        public static bool IsEnabled => NetCallsSettings.Enabled;

        #endregion

        #region Public API - Start / Stop

        /// <summary>
        /// Starts capture if not already running.
        /// </summary>
        /// <remarks>
        /// Patches are already installed elsewhere; this only enables logging behavior.
        /// Also starts a new auto-file session.
        /// </remarks>
        public static void Start()
        {
            if (NetCallsSettings.Enabled) return;

            NetCallsSettings.Enabled = true;

            // Auto file streaming for this session.
            NetCallsFileStreamer.EnableNewSession();

            NetCallsLog.Append("UI", "Network-Calls capture started.");
        }

        /// <summary>
        /// Stops capture if currently running.
        /// </summary>
        /// <remarks>
        /// Disables logging work and stops auto file streaming, but does not remove patches.
        /// </remarks>
        public static void Stop()
        {
            if (!NetCallsSettings.Enabled) return;

            NetCallsSettings.Enabled = false;

            // Stop auto streaming.
            NetCallsFileStreamer.Disable();

            NetCallsLog.Append("UI", "Network-Calls capture stopped.");
        }
        #endregion
    }
    #endregion
}