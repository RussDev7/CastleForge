/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace ModLoader
{
    /// <summary>
    /// Tag-aware, column-aligned file logger.
    /// Format per line:
    ///   [ISO-8601 timestamp][caller namespace][optional [Tags]] Message
    ///
    /// Highlights:
    /// - Per-file alignment column so all message bodies line up across the run.
    /// - Multiline messages: every continuation line is padded to the same body column.
    /// - Leading "[Tag]" blocks in your message are lifted into the prefix.
    /// - Thread-safe (per-file locks) and "never-throw" (errors swallowed).
    /// </summary>
    public static class LogSystem
    {
        #region Paths & Defaults

        // Base log directory and default file used when 'optionalLogName' isn't supplied.
        private static readonly string                               _logDir      = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Logs");
        private static readonly string                               _logFile     = Path.Combine(_logDir, "ModLoader.log");

        #endregion

        #region Per-File State (Alignment & Locks)

        // Per-file alignment cache and per-file locks.
        //  - _alignByPath: Remembers the widest prefix we've seen for a file.
        //  - _lockByPath : Lock object per file path (avoids one global lock).
        private static readonly ConcurrentDictionary<string, int>    _alignByPath = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, object> _lockByPath  = new ConcurrentDictionary<string, object>();

        #endregion

        #region Public API: Log

        /// <summary>
        /// Write one formatted line (or a multi-line block) to the selected log file.
        /// - <paramref name="optionalLogName"/>  : E.g. "caught_exceptions.log" to log elsewhere in !Logs.
        /// - <paramref name="skipTypeFullNames"/>: Types to ignore when resolving the caller namespace.
        /// - <paramref name="_alignColumn"/>     : Initial body column (will grow as needed for that file).
        /// </summary>
        public static void Log(string message, string optionalLogName = null, string[] skipTypeFullNames = null, int _alignColumn = 65)
        {
            try { Directory.CreateDirectory(_logDir); } catch { /* ignore */ }

            // Derive "[caller namespace]" (optionally skipping helper types in the call stack).
            string callerNamespace = ResolveCallerNamespace(skipTypeFullNames);

            // Decide output file (default: ModLoader.log).
            string resolvedPath = string.IsNullOrEmpty(optionalLogName)
                                    ? _logFile
                                    : Path.Combine(_logDir, optionalLogName);

            // Split "[Tag]" blocks off the front of 'message' so they live in the prefix.
            SplitLeadingTags(message ?? string.Empty, out string tags, out string body);

            // Build bracketed prefix (no trailing space yet).
            string prefix = $"[{DateTime.Now:O}][{callerNamespace}]{tags}";

            // One lock per file (keeps writes together and alignment stable).
            var gate = _lockByPath.GetOrAdd(resolvedPath, _ => new object());

            lock (gate)
            {
                // Grow alignment column for this file as needed so bodies line up.
                int align = _alignByPath.GetOrAdd(resolvedPath, _alignColumn);
                if (prefix.Length + 1 > align)
                    _alignByPath[resolvedPath] = align = prefix.Length + 1;

                // Spaces to place the FIRST line's body at the alignment column.
                int pad           = Math.Max(1, align - prefix.Length);
                string firstPad   = new string(' ', pad);

                // Spaces to start all CONTINUATION lines exactly at the body column.
                string contPad    = new string(' ', align);

                // Normalize newlines and split so each line can be padded consistently.
                string normalized = body.Replace("\r\n", "\n").Replace("\r", "\n");
                string[] lines    = normalized.Split('\n');

                var stringBuilder = new System.Text.StringBuilder(prefix.Length + (lines.Length * (align + 64)));

                // First line: Prefix + pad + first content line.
                stringBuilder.Append(prefix).Append(firstPad);
                if (lines.Length > 0)
                    stringBuilder.Append(lines[0]);
                stringBuilder.Append(Environment.NewLine);

                // Continuation lines: Just the fixed continuation padding + the line.
                for (int i = 1; i < lines.Length; i++)
                    stringBuilder.Append(contPad).Append(lines[i]).Append(Environment.NewLine);

                try { File.AppendAllText(resolvedPath, stringBuilder.ToString()); }
                catch { /* Swallow any logging errors. */ }
            }
        }
        #endregion

        #region Public API: WriteLogSessionSeparator

        /// <summary>
        /// Writes a visually obvious "new session" header to <paramref name="logPath"/>.
        /// - Adds a blank line first only if the file already has content (prevents
        ///   an awkward empty line at the very top on first run).
        /// - Includes an ISO-8601 timestamp and the caller's namespace for context.
        /// </summary>
        public static void WriteLogSessionSeparator(string separatorText, bool usePrefixNewline = true, string optionalLogName = null)
        {
            try
            {
                Directory.CreateDirectory(_logDir);
                string resolvedPath  = !string.IsNullOrEmpty(optionalLogName) ? Path.Combine(_logDir, optionalLogName) : _logFile;
                bool fileHasContent  = File.Exists(resolvedPath) && new FileInfo(resolvedPath).Length > 0;
                string prefixNewline = (usePrefixNewline && fileHasContent) ? Environment.NewLine : string.Empty;

                File.AppendAllText(
                    resolvedPath,
                    $"{prefixNewline}[{DateTime.Now:O}][{typeof(LogSystem).Namespace}] " +
                    $"================= {separatorText} =================\n"
                );
            }
            catch { /* Swallow any logging errors. */ }
        }
        #endregion

        #region Public API: SendFeedback

        /// <summary>Return s with a trailing '.' if (and only if) it doesn't already have one.</summary>
        private static string EnsureTrailingDot(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = s.TrimEnd(); // Ignore trailing spaces.
            return s.EndsWith(".", StringComparison.Ordinal) ? s : s + ".";
        }

        /// <summary>
        /// Sends a message to in-game chat via Console; optionally also logs to file.
        /// Appends a '.' to the log text if one is not already present.
        /// </summary>
        public static void SendFeedback(string message, bool alsoLogToFile = true, string optionalLogName = null)
        {
            string resolvedPath = !string.IsNullOrEmpty(optionalLogName) ? Path.Combine(_logDir, optionalLogName) : _logFile;
            bool   ok           = TrySendInGameMessage(message); // Don't touch the in-game text.
            if (alsoLogToFile)
            {
                string logBody = EnsureTrailingDot(message);  // Dot only for the log.

                if (ok) Log($"[FB] {logBody}", resolvedPath);
                else    Log($"[FB][Error] Failed to send message: {logBody}", resolvedPath);
            }
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Attempts to write to in-game chat by writing to the console.
        /// Returns true if no exception was thrown.
        /// </summary>
        private static bool TrySendInGameMessage(string message)
        {
            try { Console.WriteLine(message); Console.Out.Flush(); return true; }
            catch { return false; }
        }

        /// <summary>
        /// Walks the stack (skipping this Log() frame) to find the first declaring type
        /// not in the provided skip set; returns its namespace (or this class's namespace if unknown).
        /// </summary>
        private static string ResolveCallerNamespace(string[] skipTypeFullNames)
        {
            try
            {
                // Built-ins we always ignore.
                var builtIns = new[] {
                    typeof(LogSystem).FullName,
                    typeof(ModManager).FullName,
                };

                // Merge built-ins + caller-supplied names; HashSet = fast lookup + no dups.
                var skipTypes = new HashSet<string>(
                    builtIns.Concat(skipTypeFullNames ?? Array.Empty<string>()),
                    StringComparer.Ordinal);

                // Build a stack trace *excluding* this Log() frame.
                var st = new StackTrace(1, false);
                Type caller = null;

                // Walk up until the first frame with a declaring type that is NOT in skipTypes.
                foreach (var frame in st.GetFrames() ?? Enumerable.Empty<StackFrame>())
                {
                    var type = frame.GetMethod()?.DeclaringType;
                    if (type == null) continue;
                    if (!skipTypes.Contains(type.FullName))
                    {
                        caller = type;
                        break;
                    }
                }

                // Use the caller's namespace, or fall back to this class's namespace.
                return caller?.Namespace ??
                    MethodBase.GetCurrentMethod().DeclaringType.Namespace;
            }
            catch { /* Ignore. */ }

            return typeof(LogSystem).Namespace;
        }

        /// <summary>
        /// Splits a log line into two parts:
        ///   1) A concatenated string of leading tag tokens in the form "[...]" (e.g., "[A][B][C]")
        ///   2) The remaining message (with one optional leading space trimmed)
        ///
        /// Examples:
        ///   "[Harmony] Patched" -> tags = "[Harmony]", rest = "Patched"
        ///   "[A][B] Message"    -> tags = "[A][B]",    rest = "Message"
        ///   "NoTags here"       -> tags = "",          rest = "NoTags here"
        ///
        /// Notes:
        /// - Stops at the first malformed token (missing closing ']') and leaves the rest untouched.
        /// - Allows either back-to-back tags ("[A][B]") or a single space between them ("[A] [B]").
        /// - Trims exactly one leading space before the rest (if present).
        /// - Designed for display alignment: you can pad 'tags' to a fixed width, then append 'rest'.
        /// </summary>
        public static void SplitLeadingTags(string s, out string tags, out string rest)
        {
            tags = string.Empty;
            rest = s ?? string.Empty;
            if (rest.Length == 0 || rest[0] != '[') return;

            int i = 0;
            while (i < rest.Length && rest[i] == '[')
            {
                int close = rest.IndexOf(']', i + 1);
                if (close < 0) break;

                tags += rest.Substring(i, close - i + 1);
                i = close + 1;
                if (i < rest.Length && rest[i] == ' ') i++; // Tolerate single space between tags.
                if (i >= rest.Length || rest[i] != '[') break;
            }

            rest = (i < rest.Length) ? rest.Substring(i) : string.Empty;
            if (rest.StartsWith(" ")) rest = rest.Substring(1);
        }
        #endregion
    }
}