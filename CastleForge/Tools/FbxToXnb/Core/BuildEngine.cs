/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Build.Framework;
using System.IO;
using System;

namespace XNAConverter
{
    /// <summary>
    /// BuildEngine (Minimal MSBuild Host)
    /// ------------------------------------------------------------
    /// Implements <see cref="IBuildEngine"/> so Content Pipeline MSBuild tasks
    /// (e.g., Microsoft.Xna.Framework.Content.Pipeline.Tasks.BuildContent)
    /// have a host to report messages, warnings, and errors to.
    ///
    /// Summary:
    /// - Captures errors/warnings/messages in memory for later inspection.
    /// - Optionally appends all logged entries to a file (logfile-style).
    ///
    /// Notes:
    /// - This is not a full MSBuild implementation; it intentionally stubs out
    ///   project-building behavior (<see cref="BuildProjectFile"/>) because the
    ///   XNA Content Pipeline task invocation here is self-contained.
    /// - Logging is controlled by <see cref="log"/> and the presence of <see cref="_logPath"/>.
    /// </summary>
    public sealed class BuildEngine : IBuildEngine
    {
        #region Captured Output (In-Memory)

        // Collected output buffers:
        // - _errors   : errors emitted by the pipeline/task
        // - _warnings : warnings emitted by the pipeline/task
        // - _messages : informational messages emitted by the pipeline/task
        private readonly List<string> _errors   = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _messages = new List<string>();

        #endregion

        #region Logging (Optional File Append)

        /// <summary>
        /// Optional log file path. If null/empty, file logging is disabled even when <see cref="log"/> is true.
        /// </summary>
        private readonly string _logPath;

        /// <summary>
        /// Master logging switch. When false, Begin/End and event handlers skip file output.
        /// </summary>
        public bool log = true;

        #endregion

        #region Construction

        /// <summary>
        /// Create a BuildEngine with in-memory capture only (no file path configured).
        /// </summary>
        public BuildEngine() { }

        /// <summary>
        /// Create a BuildEngine configured to append messages to <paramref name="logFilePath"/>.
        /// </summary>
        public BuildEngine(string logFilePath)
        {
            _logPath = logFilePath;
        }
        #endregion

        #region Session (Begin/End)

        /// <summary>
        /// Start a build session.
        /// Summary: Writes a "Build start" banner if file logging is enabled.
        /// </summary>
        public void Begin()
        {
            if (!log) return;
            TryAppend($"=== Build start: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        /// <summary>
        /// End a build session.
        /// Summary: Writes a "Build end" banner if file logging is enabled.
        /// </summary>
        public void End()
        {
            if (!log) return;
            TryAppend($"=== Build end: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        #endregion

        #region Public Accessors

        /// <summary>
        /// Returns a copy of captured error lines (safe for callers to mutate).
        /// </summary>
        public List<string> GetErrors() => new List<string>(_errors);

        #endregion

        #region IBuildEngine Contract

        /// <summary>
        /// When false, tasks may treat errors as fatal and stop early.
        /// </summary>
        public bool ContinueOnError => false;

        // These are required by the interface but not meaningful in this minimal host.
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        #endregion

        #region IBuildEngine Event Sinks

        /// <summary>
        /// MSBuild task callback: error event.
        /// Summary: Formats the entry, stores it, and (optionally) appends it to the log file.
        /// </summary>
        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            string msg = Format("ERROR", e?.Message, e?.File, (int)(e?.LineNumber), (int)(e?.ColumnNumber));
            _errors.Add(msg);
            if (log) TryAppend(msg);
        }

        /// <summary>
        /// MSBuild task callback: warning event.
        /// Summary: Formats the entry, stores it, and (optionally) appends it to the log file.
        /// </summary>
        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            string msg = Format("WARN", e?.Message, e?.File, (int)(e?.LineNumber), (int)(e?.ColumnNumber));
            _warnings.Add(msg);
            if (log) TryAppend(msg);
        }

        /// <summary>
        /// MSBuild task callback: message event.
        /// Summary: Formats the entry, stores it, and (optionally) appends it to the log file.
        /// </summary>
        public void LogMessageEvent(BuildMessageEventArgs e)
        {
            string msg = Format("MSG", e?.Message, e?.File, (int)(e?.LineNumber), (int)(e?.ColumnNumber));
            _messages.Add(msg);
            if (log) TryAppend(msg);
        }

        /// <summary>
        /// MSBuild task callback: custom event.
        /// Summary: Formats the entry, stores it, and (optionally) appends it to the log file.
        /// </summary>
        public void LogCustomEvent(CustomBuildEventArgs e)
        {
            string msg = Format("CUSTOM", e?.Message, null, 0, 0);
            _messages.Add(msg);
            if (log) TryAppend(msg);
        }

        /// <summary>
        /// MSBuild task callback: build another project file.
        /// Summary: Stubbed out for this minimal host (pipeline builds are executed directly).
        /// </summary>
        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs)
        {
            // Not needed for the Content Pipeline task usage.
            return false;
        }
        #endregion

        #region Helpers (Formatting + Safe File Append)

        /// <summary>
        /// Formats an MSBuild message into a compact single-line string.
        /// </summary>
        private static string Format(string tag, string message, string file, int line, int col)
        {
            message = message ?? "";
            if (!string.IsNullOrEmpty(file) && line > 0)
                return $"[{tag}] {file}({line},{col}): {message}";
            return $"[{tag}] {message}";
        }

        /// <summary>
        /// Best-effort append to the configured log file.
        /// </summary>
        /// <remarks>
        /// - No-op if <see cref="_logPath"/> is null/empty.
        /// - Swallows IO errors so logging never kills a build.
        /// </remarks>
        private void TryAppend(string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logPath)) return;
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { /* ignore */ }
        }
        #endregion
    }
}