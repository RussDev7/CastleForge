/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Reflection;
using System.Threading;
using ModLoader;
using System.IO;
using System;

namespace ModLoaderExt
{
    /// <summary>
    /// Centralized exception capture & logging.
    /// Call once at startup: ExceptionTap.Arm(cfg.ExceptionMode).
    ///
    /// Modes:
    ///  - Off         : No extra hooks.
    ///  - CaughtOnly  : Logs what the game's Program/Main would catch/report.
    ///  - FirstChance : Logs every thrown exception (VERY noisy; throttled).
    ///
    /// Integration points:
    ///  - Harmony patch can check SuppressUpstreamCrashReporting to skip
    ///    Backtrace/telemetry (if your policy is to silence upstream reporting).
    /// </summary>
    internal static class ExceptionTap
    {
        #region State / Config

        /// <summary>
        /// When true, the Harmony patch should suppress the game's own crash reporter.
        /// Set automatically by Arm(mode != Off).
        /// </summary>
        public static bool SuppressUpstreamCrashReporting { get; private set; }

        /// <summary>Active capture mode (Off, CaughtOnly, FirstChance).</summary>
        public static ExceptionCaptureMode Mode           { get; private set; } = ExceptionCaptureMode.Off;

        // Internal guards to ensure we only hook handlers once.
        private static bool            _armed;
        private static bool            _firstChanceHooked;

        // Log file name (written via LogSystem).
        private static readonly string _exceptionsLogFile = "Caught_Exceptions.log";

        // First-chance flood throttle.
        private static DateTime        _lastFirstChanceUtc;
        private static int             _burstCount;

        #endregion

        #region Public API

        /// <summary>
        /// Idempotent arming:
        ///  - First call with Off:         No hooks.
        ///  - First call with CaughtOnly:  Hook global handlers.
        ///  - First call with FirstChance: Hook global + first-chance.
        ///  - Subsequent calls can escalate CaughtOnly -> FirstChance.
        /// </summary>
        public static void Arm(ExceptionCaptureMode mode)
        {
            // Used by the Harmony patches to disable built-in crash uploads.
            SuppressUpstreamCrashReporting = mode != ExceptionCaptureMode.Off;

            if (mode == ExceptionCaptureMode.Off)
            {
                Mode = ExceptionCaptureMode.Off;
                return;
            }

            // Hook once: AppDomain/TaskScheduler/WinForms thread handler.
            if (!_armed)
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
                TaskScheduler.UnobservedTaskException += OnUnobservedTask;

                // Optional; only available if WinForms is loaded.
                try { System.Windows.Forms.Application.ThreadException += OnThread; } catch { /* ignore */ }

                LogSystem.WriteLogSessionSeparator("Exception Logging Started", optionalLogName: _exceptionsLogFile);
                _armed = true;
                Mode = ExceptionCaptureMode.CaughtOnly;
            }

            // Escalate to FirstChance (very noisy; throttled below).
            if (mode == ExceptionCaptureMode.FirstChance && !_firstChanceHooked)
            {
                try
                {
                    AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
                    _firstChanceHooked = true;
                    Mode = ExceptionCaptureMode.FirstChance;
                }
                catch
                {
                    // FirstChance not available on some runtimes; remain at CaughtOnly.
                }
            }
        }

        /// <summary>
        /// Called by the Harmony "crash-report tap" when the game would have reported.
        /// We log it and (optionally) the patch suppresses the upstream upload.
        /// </summary>
        public static void LogReported(Exception ex) => SafeLog("[REPORTED]", ex);

        #endregion

        #region FirstChance Noise Filters

        /// <summary>
        /// FIRST_CHANCE can be extremely noisy, especially when content loading probes for multiple variants.
        /// Some missing-content probes are benign (caught internally) and do not affect gameplay.
        ///
        /// This filter suppresses ONLY known probe-miss patterns we consider "expected noise".
        /// Keep this list tight to avoid hiding real issues.
        /// </summary>
        private static bool ShouldSuppressFirstChance(Exception ex)
        {
            try
            {
                if (ex == null) return false;

                // Unwrap common wrapper exceptions (e.g., ContentLoadException -> FileNotFoundException).
                // We avoid referencing XNA ContentLoadException directly to keep this file lightweight.
                Exception cur = ex;
                for (int i = 0; i < 3 && cur != null; i++)
                {
                    if (cur.InnerException == null) break;
                    cur = cur.InnerException;
                }

                string msg = (cur.Message ?? ex.Message ?? string.Empty);

                #region Optional Guards / Early-Out Filters

                /// <summary>
                /// OPTIONAL GUARD (currently disabled / redundant):
                /// ------------------------------------------------------------------------
                /// If you ever add a generic fallback suppression (ex: "hide all benign file probes"),
                /// this gate can be re-enabled as a cheap filter so we only run filesystem-related
                /// suppression logic for exceptions that are actually file/path misses.
                ///
                /// Why both 'cur' and 'ex'?
                /// - Some pipelines throw FileNotFound/DirectoryNotFound directly.
                /// - Others wrap the real cause inside InnerException(s), so 'cur' may be the real type.
                /// </summary>

                /*
                if (!(cur is FileNotFoundException)      &&
                    !(cur is DirectoryNotFoundException) &&
                    !(ex is FileNotFoundException)       &&
                    !(ex is DirectoryNotFoundException)
                    )
                    return false; // Not a filesystem miss -> don't suppress (and can skip probe checks).
                */

                #endregion

                #region FirstChance Noise Suppressions

                #region Reflection / TargetException

                // Reflection invoke consistency check noise:
                // System.Reflection.TargetException: Non-static method requires a target.
                //   at System.Reflection.RuntimeMethodInfo.CheckConsistency(Object target)
                if (cur is TargetException || ex is TargetException)
                {
                    // Use ToString() because FIRST_CHANCE StackTrace/TargetSite are often null,
                    // but your logger prints {ex} which uses ToString() anyway.
                    string t = ex.ToString();

                    if (t.IndexOf("System.Reflection.RuntimeMethodInfo.CheckConsistency", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("RuntimeMethodInfo.CheckConsistency", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    // Optional: Last-resort message match (still specific to this noise).
                    string m = (cur.Message ?? ex.Message ?? string.Empty);
                    if (m.IndexOf("Non-static method requires a target", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                #endregion

                #region Networking / Buffer Noise

                // Net receive oversize buffer noise (we handle this safely elsewhere).
                if (msg.IndexOf("Data buffer is too small", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                #endregion

                #region Filesystem Probe Noise

                // Rocket model probe:
                if (msg.IndexOf(@"Content\Props\Weapons\Conventional\RPG\RPGGrenade.xnb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // Effect/shader probe variants:
                if (msg.IndexOf(@"Content\PropShader_hidef_0.xnb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (msg.IndexOf(@"Content\Specular_hidef_12.xnb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // ProfiledContentManager benign texture LOD probe misses:
                if (msg.IndexOf(@"Content\frame_0_L2.xnb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // Torch model probe:
                if (msg.IndexOf(@"Content\Props\Items\Torch\Model.xnb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // Benign per-world inventory cache probes (*.inv):
                if ((cur is FileNotFoundException || ex is FileNotFoundException))
                {
                    // Only suppress the CMZ per-world inventory cache misses:
                    // ...\Worlds\<worldGuid>\<hash>.inv
                    if (msg.IndexOf(@"\Worlds\", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        msg.IndexOf(".inv", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                // Benign per-world "world.info" probe misses:
                // ...\CastleMinerZ\<steamId>\Worlds\<worldGuid>\world.info
                if ((cur is FileNotFoundException || ex is FileNotFoundException))
                {
                    if (msg.IndexOf(@"\Worlds\", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        msg.IndexOf(@"\world.info", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                #endregion

                #endregion

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Dump details from ReflectionTypeLoadException (FIRST_CHANCE helper).
        ///
        /// Summary:
        /// - When Assembly.GetTypes()/Module.GetTypes() fails, the runtime throws ReflectionTypeLoadException.
        /// - The "real" cause is in LoaderExceptions (missing DLLs, type load failures, version mismatches).
        /// - FIRST_CHANCE logs often show the wrapper exception but hide the underlying LoaderExceptions.
        ///
        /// Notes:
        /// - This is best-effort and never throws.
        /// - Uses LogSystem.Log(...) so multiline alignment matches your exception log format.
        /// - Safe to call even if you later suppress upstream crash reporting.
        /// </summary>
        private static void DumpReflectionTypeLoad(Exception ex)
        {
            try
            {
                if (!(ex is ReflectionTypeLoadException rtle))
                    return;

                // Summary: Header line provides the high-level reason.
                LogSystem.Log(
                    message:           $"[TYPELOAD] {rtle.Message}\n",
                    optionalLogName:   _exceptionsLogFile,
                    skipTypeFullNames: new[] { typeof(ExceptionTap).FullName },
                    _alignColumn:      83
                );

                // Summary: Underlying loader failures are the actionable errors.
                var les = rtle.LoaderExceptions;
                if (les == null || les.Length == 0)
                {
                    LogSystem.Log(
                        message:           "[TYPELOAD] (no LoaderExceptions provided)\n",
                        optionalLogName:   _exceptionsLogFile,
                        skipTypeFullNames: new[] { typeof(ExceptionTap).FullName },
                        _alignColumn:      83
                    );
                    return;
                }

                for (int i = 0; i < les.Length; i++)
                {
                    var le = les[i];
                    if (le == null) continue;

                    LogSystem.Log(
                        message:           $"[TYPELOAD] -> {le.GetType().FullName}: {le.Message}\n",
                        optionalLogName:   _exceptionsLogFile,
                        skipTypeFullNames: new[] { typeof(ExceptionTap).FullName },
                        _alignColumn:      83
                    );
                }
            }
            catch
            {
                // Summary: Never allow diagnostics to interfere with runtime stability.
            }
        }
        #endregion

        #region Global Handlers

        // Fires when an exception escapes all the way out of a thread with no handler.
        private static void OnUnhandled(object s, UnhandledExceptionEventArgs e)
            => SafeLog("[UNHANDLED]", e.ExceptionObject as Exception);

        // Fires when a Task's exception was never observed (prevents default crash).
        private static void OnUnobservedTask(object s, UnobservedTaskExceptionEventArgs e)
        {
            SafeLog("[UNOBSERVED_TASK]", e.Exception);
            try { e.SetObserved(); } catch { /* best-effort */ }
        }

        // WinForms UI-thread exceptions (if WinForms is present).
        private static void OnThread(object s, ThreadExceptionEventArgs e)
            => SafeLog("[THREAD]", e.Exception);

        // First-chance: Every throw (even those that get caught later).
        private static void OnFirstChance(object s, FirstChanceExceptionEventArgs e)
        {
            if (e?.Exception == null) return;
            if (e.Exception is ThreadAbortException) return;

            // Suppress known benign probe-miss spam (keeps visuals unchanged).
            if (ShouldSuppressFirstChance(e.Exception))
                return;

            // Throttle: 1/10 lines inside 250ms bursts (keeps logs sane).
            var now = DateTime.UtcNow;
            bool throttled = (now - _lastFirstChanceUtc) < TimeSpan.FromMilliseconds(250);
            if (throttled)
            {
                if ((++_burstCount % 10) != 1) return;
            }
            else
            {
                _burstCount = 0;
                _lastFirstChanceUtc = now;
            }

            // If type enumeration fails (Assembly.GetTypes/Module.GetTypes), dump LoaderExceptions
            // so missing/incorrect dependency DLLs are visible (this is diagnostic-only and never throws).
            DumpReflectionTypeLoad(e.Exception);

            SafeLog("[FIRST_CHANCE]", e.Exception);
        }
        #endregion

        #region Core Logging

        /// <summary>
        /// Writes a single, aligned log entry using LogSystem.
        /// - Uses tag blocks (e.g., "[FIRST_CHANCE]") so the logger can align multiline output.
        /// - Skips common wrapper namespaces so the caller context looks useful.
        /// NOTE: This calls the LogSystem overload that supports multiline alignment.
        /// </summary>
        private static void SafeLog(string tag, Exception ex, bool sendFeedback = true)
        {
            if (ex == null) return;

            // Include type, message, and full stack via ToString().
            // The structured logger will prefix the first line and indent the rest.
            string logMessage      = $"{tag} {ex}\n";
            string feedbackMessage = $"{ex.Message}";

            // Skip this helper so caller namespace aligns with the real source.
            var skip = new[]
            {
                typeof(ExceptionTap).FullName,
            };

            // IMPORTANT: This relies on the LogSystem.Log(...) overload that supports
            // multiline alignment (named parameter "_alignColumn"). See the logger impl.
            LogSystem.Log(
                message:          logMessage,
                optionalLogName:  _exceptionsLogFile,
                skipTypeFullNames: skip,
                _alignColumn:     83                  // Keep in sync with the chosen column.
            );

            // Optional: Send in-game feedback.
            if (sendFeedback)
                LogSystem.SendFeedback($"[Exception] {feedbackMessage}", false);
        }
        #endregion
    }
}