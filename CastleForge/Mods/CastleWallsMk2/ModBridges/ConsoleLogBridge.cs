/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Text;
using System.IO;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// This file provides:
    ///  1) ChatLog              - Thread-safe log buffer for both chat and console lines, with stable ordering.
    ///  2) TeeTextWriter        - A "tee" TextWriter that forwards Console.Out/Error to the log (and still writes to original).
    ///  3) ConsoleCapture       - Installs the tee wrappers and filters out echo duplicates from chat.
    ///  4) MessageTextExtractor - Tiny reflection helper to pull text from chat-like DNA.Net.Message instances.
    ///
    /// Design notes:
    ///  - Ordering:    Entries carry both a UTC timestamp and a monotonic Seq to break ties inside one frame/tick.
    ///  - Time base:   Console lines pass in the exact capture time; chat entries use DateTime.UtcNow at add time.
    ///  - Echo filter: When chat is sent/received, the game often writes a duplicate to Console.*; we drop those
    ///                 via a small recent-text TTL window so the log shows each event once.
    ///  - Threading:   Console.* can originate from any thread; we queue -> drain from UI thread.
    /// </summary>

    #region Log Sink (Capture + Render Data)

    /// <summary>
    /// Central log buffer for UI. Holds in/out chat + console lines in a single consistent stream.
    /// </summary>
    internal static class ChatLog
    {
        internal struct Entry
        {
            public DateTime TimeUtc;   // UTC time of the event (console lines pass capture time; chat uses UtcNow).
            public string   Source;    // Source tag (player name, "Console", "Console-ERR", etc.).
            public string   Text;      // Message text payload.
            public bool     Outbound;  // True if this was sent by the local user (">>"), false if inbound ("<<").
            public bool     IsConsole; // True if this came from Console.Out/Console.Error ("==").
            public long     Sequence;  // Monotonic sequence for stable ordering of same-timestamp entries. // Tie-breaker.
        }

        private static readonly List<Entry> _entries        = new List<Entry>(8192);
        private static readonly object      _lock           = new object();
        private static long                 _seq;           // Interlocked increment.
        private static int                  _maxEntries     = 1000;
        public static int MaxEntries
        {
            get => Volatile.Read(ref _maxEntries);
            set
            {
                // Clamp to sensible range.
                int v = Math.Max(100, Math.Min(value, 200_000));
                Interlocked.Exchange(ref _maxEntries, v);
                lock (_lock) TrimIfNeeded_NoLock();
            }
        }

        /// <summary>Sink for streaming, may be null.</summary>summary>
        public static Action<Entry> OnEntryAdded;

        /// <summary>
        /// Removes the oldest entries if the buffer exceeds the configured cap.
        /// PRECONDITION: Caller must hold _lock.
        /// Complexity: O(n) when trimming because RemoveRange(0, k) shifts the array.
        /// For huge logs or very frequent trims, consider a ring buffer.
        /// </summary>
        private static void TrimIfNeeded_NoLock()
        {
            // Read the current cap. (We're under _lock so a simple read is fine.).
            int max = _maxEntries;

            // Only do work when we are actually over the cap.
            if (_entries.Count > max)
            {
                // Drop oldest items first to retain the most recent 'max' events.
                // If you want to reclaim backing memory, you could also
                // consider '_entries.Capacity = Math.Max(_entries.Capacity, max);'
                // after the trim, but it's usually not necessary.
                _entries.RemoveRange(0, _entries.Count - max);
            }
        }

        /// <summary>Total entries currently stored.</summary>
        public static int Count { get { lock (_lock) return _entries.Count; } }

        /// <summary>
        /// Removes redundant "Name: " prefix that some sources include in text payloads.
        /// Keeps display consistent (we render "Name: " ourselves when needed).
        /// </summary>
        private static string StripSourcePrefix(string who, string text)
        {
            if (string.IsNullOrEmpty(who) || string.IsNullOrEmpty(text)) return text;
            string prefix = who + ": ";
            return text.StartsWith(prefix, StringComparison.Ordinal) ? text.Substring(prefix.Length) : text;
        }

        /// <summary>
        /// Detects exception-tagged lines.
        /// Summary: Returns true when <paramref name="text"/> begins with the literal "[Exception]" marker
        /// (ignoring leading whitespace), which we treat as an error row in the log.
        /// </summary>
        private static bool IsExceptionTagged(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Allow leading whitespace (common in console formatting).
            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;

            return text.Length - i >= 11
                && text.StartsWith("[Exception]", StringComparison.Ordinal);
        }

        // Console lines: Use supplied time (caller captures the moment the line was formed).
        public static void AddConsole(string text, DateTime whenUtc, bool isError)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Treat stdout "[Exception]..." as an error row.
            if (IsExceptionTagged(text))
            {
                Add("ERR", text, outbound: true, isConsole: true, whenUtc);
                return;
            }

            Add(isError ? "Console-ERR" : "Console", text, outbound: true, isConsole: true, whenUtc);
        }

        // Chat lines: Timestamp with UtcNow so all entries share the same clock base.
        public static void AddInbound(string who, string text)
        {
            text = StripSourcePrefix(who, text);

            // If the inbound line is tagged as an exception, render it like an error:
            // "!! ERR: <message>"
            if (IsExceptionTagged(text))
                Add("ERR", text, outbound: false, isConsole: true, DateTime.UtcNow);
            else
                Add(who, text, outbound: false, isConsole: false, DateTime.UtcNow);
        }

        public static void AddOutbound(string who, string text)
            => Add(who, StripSourcePrefix(who, text), outbound: true, isConsole: false, DateTime.UtcNow);

        // Core append (thread-safe). Enforces max buffer and assigns tie-break sequence.
        private static void Add(string who, string text, bool outbound, bool isConsole, DateTime whenUtc)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_lock)
            {
                _entries.Add(new Entry
                {
                    TimeUtc   = whenUtc,
                    Source    = string.IsNullOrEmpty(who) ? (isConsole ? "Console" : "?") : who,
                    Text      = text,
                    Outbound  = outbound,
                    IsConsole = isConsole,
                    Sequence  = Interlocked.Increment(ref _seq)
                });
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
                TrimIfNeeded_NoLock();

                OnEntryAdded?.Invoke(_entries[_entries.Count - 1]);
            }
        }

        // Returns a snapshot sorted by (TimeUtc, Seq). Use this for rendering to keep the UI stable.
        public static List<Entry> SnapshotSorted()
        {
            lock (_lock)
            {
                var copy = new List<Entry>(_entries);
                copy.Sort((a, b) => {
                    int c = a.TimeUtc.CompareTo(b.TimeUtc);
                    return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
                });
                return copy;
            }
        }

        // Clears all entries.
        public static void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        // Append a plain log line (utility; uses local time). Intended for occasional UI notices.
        public static void Append(string source, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_lock)
            {
                _entries.Add(new Entry { TimeUtc = DateTime.Now, Source = source ?? "Log", Text = text, Sequence = Interlocked.Increment(ref _seq) });
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
                TrimIfNeeded_NoLock();
            }
        }

        // Moves any queued Console.* lines into the log. Call once per frame (e.g., from your UI draw).
        public static void DrainConsoleQueues()
        {
            while (ConsoleCapture._consoleLines.TryDequeue(out var l))    AddConsoleWithTime(l.Text, l.Time, l.IsError);
            while (ConsoleCapture._consoleErrLines.TryDequeue(out var e)) AddConsoleWithTime(e.Text, e.Time, e.IsError);
        }

        // Small helper to keep all Console adds going through a single path.
        public static void AddConsoleWithTime(string text, DateTime when, bool isError)
        {
            AddConsole(text, when, isError);
        }

        // Produces a flat text dump of the log with optional timestamps and traffic operators.
        public static string DumpText(bool includeTimestamps = true, bool includeTrafficDirection = true)
        {
            var sb = new StringBuilder(Math.Min(1_000_000, _entries.Count * 64));
            lock (_lock)
            {
                foreach (var e in _entries)
                {
                    if (includeTimestamps)
                        sb.Append('[').Append(e.TimeUtc.ToString("HH:mm:ss")).Append("] ");

                    if (includeTrafficDirection)
                    {
                        string op = e.IsConsole
                            ? ((string.Equals(e.Source, "Console-ERR", StringComparison.Ordinal) ||
                                string.Equals(e.Source, "ERR", StringComparison.Ordinal)) ? "!!" : "==")
                            : (e.Outbound ? ">>" : "<<");
                        sb.Append(op).Append(' ');
                    }

                    if (!string.IsNullOrEmpty(e.Source))
                        sb.Append(e.Source).Append(": ");

                    sb.AppendLine(e.Text);
                }
            }
            return sb.ToString();
        }

        #region Deletion Helpers (Thread-Safe)

        /// <summary>
        /// Removes a single entry by its <see cref="Entry.Sequence"/> key.
        /// Returns true if a row was removed; false if not found.
        /// Thread-safe.
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
                return false;
            }
        }

        /// <summary>
        /// Removes all entries whose Sequence is in the given set.
        /// Returns number removed. Thread-safe.
        /// </summary>
        public static int RemoveMany(IEnumerable<long> sequences)
        {
            if (sequences == null) return 0;
            // Hash for O(1) membership checks.
            var toRemove = new HashSet<long>(sequences);
            int removed  = 0;

            lock (_lock)
            {
                // Walk backwards so indices remain valid as we remove.
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (toRemove.Contains(_entries[i].Sequence))
                    {
                        _entries.RemoveAt(i);
                        removed++;
                    }
                }
            }
            return removed;
        }

        /// <summary>
        /// Removes all entries older than the given UTC cutoff (exclusive).
        /// Returns number removed. Thread-safe.
        /// </summary>
        public static int RemoveBefore(DateTime utcExclusive)
        {
            int removed = 0;
            lock (_lock)
            {
                // Because entries are appended over time, older items tend to be near the front,
                // but Console time vs. Add time can interleave. A full scan is simplest.
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].TimeUtc < utcExclusive)
                    {
                        _entries.RemoveAt(i);
                        removed++;
                    }
                }
            }
            return removed;
        }
        #endregion
    }
    #endregion

    #region Console Capture - Tee Writer

    /// <summary>
    /// A "tee" writer that forwards Console.Out/Error to our sink while preserving the original stream writes.
    /// We buffer until newline to capture complete lines and attach a capture timestamp at flush time.
    /// </summary>
    sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter               _inner;
        private readonly Action<DateTime, string> _sink;    // Pass time + text.
        private readonly bool                     _isError; // Stdout=false, stderr=true.
        private readonly StringBuilder            _buf = new StringBuilder(256);

        public TeeTextWriter(TextWriter inner)
            : this(inner, isError: false) { }

        public TeeTextWriter(TextWriter inner, bool isError)
        {
            _inner   = inner;
            _isError = isError;
            NewLine  = inner?.NewLine ?? Environment.NewLine;

            // default sink: Add directly to ChatLog with capture time.
            _sink = (t, s) => ChatLog.AddConsole(s, t, _isError);
        }

        public override Encoding Encoding => _inner?.Encoding ?? Encoding.UTF8;

        public override void Write(char value)
        {
            _inner?.Write(value);
            if (value == '\n') FlushCapture();
            else _buf.Append(value);
        }

        public override void Write(string value)
        {
            _inner?.Write(value);
            if (string.IsNullOrEmpty(value)) return;

            int i = 0;
            while (true)
            {
                int nl = value.IndexOf('\n', i);
                if (nl < 0) break;
                _buf.Append(value, i, nl - i);
                FlushCapture();
                i = nl + 1;
            }
            if (i < value.Length) _buf.Append(value, i, value.Length - i);
        }

        public override void WriteLine(string value)
        {
            _inner?.WriteLine(value);
            if (!string.IsNullOrEmpty(value)) _buf.Append(value);
            FlushCapture();
        }

        // Emits a captured line into the sink with the current UTC time. Drops recent chat echoes.
        private void FlushCapture()
        {
            var s = _buf.ToString().TrimEnd('\r');
            _buf.Clear();
            if (s.Length == 0) return;

            // Drop chat-echo lines you've tagged recently.
            if (ConsoleCapture.ShouldIgnoreConsoleLine(s)) return;

            _sink(DateTime.UtcNow, s); // Capture time here.
        }
    }
    #endregion

    #region Console Capture - Hook

    /// <summary>
    /// Installs TeeTextWriter wrappers on Console.Out and Console.Error, and filters out Console echoes that
    /// are duplicates of already-logged chat messages (small TTL).
    /// </summary>
    internal static class ConsoleCapture
    {
        // Value type used for cross-thread console line passing.
        internal readonly struct ConsoleLine
        {
            public readonly DateTime Time;
            public readonly string   Text;
            public readonly bool     IsError;
            public ConsoleLine(DateTime time, string text, bool isError)
            {
                Time = time; Text = text; IsError = isError;
            }
        }

        // Queues drained from UI thread.
        public static readonly ConcurrentQueue<ConsoleLine> _consoleLines      = new ConcurrentQueue<ConsoleLine>();
        public static readonly ConcurrentQueue<ConsoleLine> _consoleErrLines   = new ConcurrentQueue<ConsoleLine>();

        // Dedupe/ignore set for chat echoes coming through Console.Write*
        private static readonly ConcurrentDictionary<string, long> _recentChat = new ConcurrentDictionary<string, long>();
        private const int CHAT_TTL_MS = 1500;

        private static bool _armed;

        // Call once at startup to hook Console.Out/Err.
        public static void Init()
        {
            if (_armed) return;
            _armed = true;
            TryWrapConsoleOut();
            TryWrapConsoleErr();
        }

        // Wrap Console.Out (stdout).
        public static void TryWrapConsoleOut()
        {
            var current = Console.Out;
            if (current is TeeTextWriter) return;
            Console.SetOut(new TeeTextWriter(current, isError: false));
        }

        // Wrap Console.Error (stderr).
        public static void TryWrapConsoleErr()
        {
            var current = Console.Error;
            if (current is TeeTextWriter) return;
            Console.SetError(new TeeTextWriter(current, isError: true));
        }

        // Marks a chat string as "recently seen" so the Console echo of the same string can be dropped.
        public static void MarkChatEcho(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _recentChat[text] = Environment.TickCount;
        }

        // Returns true if a given console line should be ignored as a duplicate of a recent chat line.
        public static bool ShouldIgnoreConsoleLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (_recentChat.TryGetValue(text, out var t))
            {
                if (Environment.TickCount - t <= CHAT_TTL_MS)
                    return true; // Drop this console line (we already logged it as chat).
                _recentChat.TryRemove(text, out _);
            }
            return false;
        }
    }
    #endregion

    #region Message Text Extractor

    /// <summary>
    /// Locates a likely "text" payload inside DNA.Net.Message via reflection (fields or properties named
    /// "Message" or "Text") but only for types whose names look chat-ish (Chat/Text/Broadcast).
    /// </summary>
    internal static class MessageTextExtractor
    {
        public static bool TryGetChatText(DNA.Net.Message msg, out string text)
        {
            text = null;
            if (msg == null) return false;

            var t = msg.GetType();

            // Heuristic:  Only consider likely chat-ish types.
            // Older .NET: No Contains(..., StringComparison). Use IndexOf instead.
            var name = t.Name;
            if (!(ContainsIgnoreCase(name, "Chat") ||
                  ContainsIgnoreCase(name, "Text") ||
                  ContainsIgnoreCase(name, "Broadcast")))
            {
                return false;
            }

            // Common field/property names used across CMZ net messages.
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Field named "Message" or "Text".
            var f = t.GetField("Message", BF) ?? t.GetField("Text", BF);
            if (f != null && f.FieldType == typeof(string))
            {
                text = (string)f.GetValue(msg);
                return !string.IsNullOrEmpty(text);
            }

            // Property named "Message" or "Text".
            var p = t.GetProperty("Message", BF) ?? t.GetProperty("Text", BF);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead)
            {
                try
                {
                    text = (string)p.GetValue(msg, null);
                    return !string.IsNullOrEmpty(text);
                }
                catch { /* ignore */ }
            }

            return false; // Not a chat-like message, or no string content.
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (haystack == null || needle == null) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
    #endregion
}