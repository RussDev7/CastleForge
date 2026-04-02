/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Text;
using System.IO;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Streams every <see cref="ChatLog"/> entry (chat + Console) to a timestamped file while enabled.
    /// File path: !Mods/<Namespace>/!Logs/CW2_yyyyMMdd_HHmmss_UTC.log.
    ///
    /// Notes:
    /// • Uses UTF-8 (no BOM), FileShare.Read so you can tail the log live.
    /// • AutoFlush = true so crashes lose at most the last line; set to false if you prefer fewer disk flushes.
    /// • Subscribes to ChatLog.OnEntryAdded; keep <see cref="OnEntry(ChatLog.Entry)"/> lightweight.
    /// </summary>
    internal static class ConsoleLogStreamer
    {
        #region Fields / Paths

        private static StreamWriter _writer;
        private static string _filePath;

        /// <summary>Per-mod logs folder (e.g., !Mods/<Namespace>/!Logs).</summary>
        public static string LogsFolder =>
            Path.Combine(ModConfig.FolderPath, "!Logs");

        #endregion

        #region Lifecycle

        /// <summary>
        /// Start streaming into a fresh timestamped file. No-ops if already enabled.
        /// </summary>
        public static void Enable()
        {
            if (_writer != null) return; // Already on.

            Directory.CreateDirectory(LogsFolder);
            _filePath = Path.Combine(
                LogsFolder,
                $"CW2_{DateTime.UtcNow:yyyyMMdd_HHmmss}_UTC.log");

            // Open with share-read so external tools can tail the file.
            _writer = new StreamWriter(
                new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) // UTF-8 no BOM.
            )
            { AutoFlush = true };

            // Header (human-friendly).
            _writer.WriteLine("# CastleWallsMk2 live log.");
            _writer.WriteLine($"# Started: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}.");
            _writer.WriteLine();

            // Begin streaming: Subscribe to the log sink.
            ChatLog.OnEntryAdded += OnEntry;
            ChatLog.Append("Log", $"Streaming to {Path.GetFileName(_filePath)}.");
        }

        /// <summary>
        /// Stop streaming and close the file. Safe to call multiple times.
        /// </summary>
        public static void Disable()
        {
            if (_writer == null) return;
            ChatLog.OnEntryAdded -= OnEntry;
            try { _writer.Flush(); _writer.Dispose(); } catch { /* Swallow. */ }
            _writer = null;
        }

        #endregion

        #region Handler

        /// <summary>
        /// Append one line matching the UI format: [HH:mm:ss] OP Source: Text.
        /// Keep this fast-it's called for every entry.
        /// </summary>
        private static void OnEntry(ChatLog.Entry e)
        {
            if (_writer == null) return;

            // Operator legend: !!=stderr, == =stdout, >>=outbound chat, <<=inbound chat.
            string op = e.IsConsole
                ? (string.Equals(e.Source, "Console-ERR", StringComparison.Ordinal) ? "!!" : "==")
                : (e.Outbound ? ">>" : "<<");

            var line = $"[{e.TimeUtc:HH:mm:ss}] {op} {e.Source}: {e.Text}";
            try { _writer.WriteLine(line); } catch { /* Swallow I/O blips. */ }
        }

        #endregion

        #region Diagnostics / Info

        /// <summary>Full path of the current log file (null if disabled).</summary>
        public static string CurrentFilePath => _filePath;

        #endregion
    }
}