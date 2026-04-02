/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Text;
using System.IO;
using System;

namespace ChatTranslator
{
    /// <summary>
    /// Streams translated chat lines to a timestamped file while translation is active.
    /// File path: !Mods/<Namespace>/!Logs/CT_yyyyMMdd_HHmmss_UTC.log.
    ///
    /// Notes:
    /// • Uses UTF-8 (no BOM), FileShare.Read so you can tail the log live.
    /// • AutoFlush = true so you lose at most one line on crash.
    /// • The file is opened lazily on first translation while ChatTranslator is active.
    /// </summary>
    internal static class CTTranslationLogger
    {
        #region Fields / Paths

        private static readonly object _lock = new object();
        private static StreamWriter    _writer;
        private static string          _filePath;

        /// <summary>Per-mod logs folder: !Mods/ChatTranslator/!Logs.</summary>
        public static string LogsFolder =>
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "!Mods",
                typeof(ChatTranslator).Namespace ?? "ChatTranslator",
                "!Logs");

        #endregion

        #region Lifecycle

        /// <summary>
        /// Append one translated line to the current log file, creating it on first use.
        /// Does nothing if translation is not active.
        /// Format:
        ///   [HH:mm:ss] [DIRTAG] Sender: "original" -> "translated"
        /// </summary>
        public static void LogTranslation(string dirTag, string sender, string originalText, string translatedText)
        {
            // Only log while the translator is active / recently used.
            if (!ChatTranslationState.IsActive &&
                string.IsNullOrEmpty(ChatTranslationState.LastDetectedLanguage))
            {
                return;
            }

            if (string.IsNullOrEmpty(originalText) && string.IsNullOrEmpty(translatedText))
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    if (_writer == null)
                    {
                        Directory.CreateDirectory(LogsFolder);

                        _filePath = Path.Combine(
                            LogsFolder,
                            $"CT_{DateTime.UtcNow:yyyyMMdd_HHmmss}_UTC.log");

                        // Open with share-read so external tools can tail the file.
                        _writer = new StreamWriter(
                            new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                        {
                            AutoFlush = true
                        };

                        // Header.
                        _writer.WriteLine("# ChatTranslator live translation log.");
                        _writer.WriteLine($"# Started: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}.");
                        _writer.WriteLine("# Format: [HH:mm:ss] [DIRTAG] Sender: \"original\" -> \"translated\"");
                        _writer.WriteLine();
                    }

                    string ts   = DateTime.UtcNow.ToString("HH:mm:ss");
                    string dir  = (dirTag ?? string.Empty).Trim();
                    string snd  = sender ?? "(null)";

                    string line = $"[{ts}] {dir} {snd}: \"{originalText}\" -> \"{translatedText}\"";
                    _writer.WriteLine(line);
                }
                catch
                {
                    // Swallow I/O issues; logging is non-critical.
                }
            }
        }

        /// <summary>
        /// Stop logging and close the file. Safe to call multiple times.
        /// </summary>
        public static void Disable()
        {
            lock (_lock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch
                {
                    // Swallow.
                }

                _writer = null;
                _filePath = null;
            }
        }

        /// <summary>Full path of the current log file (null if disabled).</summary>
        public static string CurrentFilePath
        {
            get
            {
                lock (_lock)
                {
                    return _filePath;
                }
            }
        }
        #endregion
    }
}