/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Input;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ.Net.Steam;
using System.Collections.Generic;
using DNA.Drawing.UI.Controls;
using Microsoft.Xna.Framework;
using DNA.Distribution.Steam;
using System.Reflection.Emit;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Net.Lidgren;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using System.Text;
using DNA.Timers;
using HarmonyLib;                       // Harmony patching library.
using DNA.Input;
using ModLoader;
using System.IO;
using DNA.Net;
using DNA.IO;
using System;
using DNA;

using static ModLoaderExt.GamePatches.ImpersonationGuard;
using static ModLoaderExt.GamePatches.GamerTagFixer;
using static ModLoader.LogSystem;       // For Log(...).

namespace ModLoaderExt
{
    /// <summary>
    /// All Harmony patches in one place. Using ApplyAllPatches()
    /// will scan this assembly for nested [HarmonyPatch] classes
    /// and apply them, then log exactly what got patched.
    /// </summary>
    class GamePatches
    {
        #region Patcher Initiation

        // Keep a handle to this Harmony instance so we can unpatch later.
        private static Harmony _harmony;
        private static string  _harmonyId;

        /// <summary>
        /// Best-effort Harmony bootstrap:
        /// - Scans this assembly for all classes marked with [HarmonyPatch].
        /// - All classes marked with the additional [HarmonySilent] attribute will have logging silenced.
        /// - Patches each class independently inside a try/catch (one bad target won't kill the rest).
        /// - Logs a per-class result and a final summary of methods actually patched by our Harmony ID.
        /// - Leaves your UI wiring call in place after patching.
        /// </summary>
        public static void ApplyAllPatches()
        {
            Log("[Harmony] Starting game patching.");

            // Create a stable, unique Harmony ID for this mod. Using the namespace helps avoid collisions.
            _harmonyId = $"castleminerz.mods.{typeof(GamePatches).Namespace}.patches"; // Unique ID based on namespace.
            _harmony   = new Harmony(_harmonyId);                                      // Create & store the Harmony instance.

            // Choose which assembly to scan for patch classes.
            // If you split patches across multiple assemblies, call this routine for each assembly.
            Assembly asm = typeof(GamePatches).Assembly;

            int successCount = 0;
            int failCount    = 0;

            // Enumerate every class that has at least one [HarmonyPatch] attribute,
            // and patch it independently (best-effort).
            foreach (var patchType in EnumeratePatchTypes(asm))
            {
                try
                {
                    // Create a processor for this patch class and apply all of its prefixes/postfixes/transpilers.
                    var proc    = _harmony.CreateClassProcessor(patchType);
                    var targets = proc?.Patch(); // List<MethodBase> of target methods Harmony hooked (may be null).
                    successCount++;

                    /*
                    // NOTE: Don't show silent patch containers.
                    if (!IsSilent(patchType))
                    {
                        int targetCount = targets?.Count ?? 0;
                        Log($"[Harmony] Patched {patchType.FullName} ({targetCount} target(s)).");
                    }
                    */

                    int targetCount = targets?.Count ?? 0;
                    Log($"[Harmony] Patched {patchType.FullName} ({targetCount} target(s)).");
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"[Harmony] FAILED patching {patchType.FullName}: {ex.GetType().Name}: {ex.Message}.");
                    // Log($"[Harmony] FAILED patching {patchType.FullName}: {ex.GetType().Name}: {ex}."); // More detailed logging.
                }
            }

            // Summarize what we actually patched (filter by Owner == our Harmony ID).
            var ours = _harmony.GetPatchedMethods()
                               .Where(m =>
                               {
                                   var info = Harmony.GetPatchInfo(m);
                                   return info != null && (info.Owners?.Contains(_harmonyId) ?? false);
                               })
                               .ToList();

            // Print per-method details, but filter out any silent patches FIRST.
            foreach (var m in ours)
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) continue;

                // Filter out silent patches before printing anything.
                var prefixes    = Filter(info.Prefixes).ToList();
                var postfixes   = Filter(info.Postfixes).ToList();
                var transpilers = Filter(info.Transpilers).ToList();

                // If nothing remains (all were silent), don't log this method at all.
                if (prefixes.Count == 0 && postfixes.Count == 0 && transpilers.Count == 0) continue;

                // Show filtered counts (not the raw/total counts).
                Log($"[Harmony] Patched method: {Describe(m)} | " +
                    $"[Prefixes={prefixes.Count}] [Postfixes={postfixes.Count}] [Transpilers={transpilers.Count}].");

                foreach (var p in prefixes)    Log($"  • Prefix    : {Describe(p.PatchMethod)}.");
                foreach (var p in postfixes)   Log($"  • Postfix   : {Describe(p.PatchMethod)}.");
                foreach (var p in transpilers) Log($"  • Transpiler: {Describe(p.PatchMethod)}.");
            }

            Log($"[Harmony] Patching complete. Success={successCount}, Failed={failCount}, MethodsPatchedByUs={ours.Count}.");
        }

        /// <summary>
        /// Unpatch everything applied by this mod's Harmony ID only
        /// (restores original game methods without touching other mods).
        /// </summary>
        public static void DisableAll()
        {
            if (_harmony != null)
            {
                Log($"[Harmony] Unpatching all ({_harmonyId}).");
                _harmony.UnpatchAll(_harmonyId);
            }
        }

        #region Silent Attribute

        /// <summary>
        /// Lets you tag a whole patch class or a single method so the patch-reporting logger will ignore it.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
        internal sealed class HarmonySilentAttribute : Attribute { };

        #endregion

        #region Patcher Helpers

        /// <summary>
        /// Return true if the method or its declaring type is marked with [HarmonySilent].
        /// </summary>
        static bool IsSilent(MemberInfo mi)
        {
            if (mi == null) return false;

            // Respect [HarmonySilent] on the member itself.
            if (mi.IsDefined(typeof(HarmonySilentAttribute), inherit: false))
                return true;

            // Respect [HarmonySilent] on declaring type.
            var dt = (mi as MethodBase)?.DeclaringType ?? mi as Type;
            if (dt != null && dt.IsDefined(typeof(HarmonySilentAttribute), inherit: false))
                return true;

            return false;
        }

        /// <summary>
        /// Filters out patches whose patch method (or its declaring type) is marked "silent".
        /// </summary>
        static IEnumerable<Patch> Filter(IEnumerable<Patch> src)
            => (src ?? Enumerable.Empty<Patch>()).Where(p => !IsSilent(p.PatchMethod));

        /// <summary>
        /// Finds all types that are Harmony patch containers in the given assembly
        /// (i.e., classes marked with [HarmonyPatch]). Using an attribute scan keeps us
        /// from trying to patch non-patch helper classes accidentally.
        /// </summary>
        private static IEnumerable<Type> EnumeratePatchTypes(Assembly asm)
        {
            // AccessTools.GetTypesFromAssembly is defensive (skips type-load failures).
            foreach (var t in AccessTools.GetTypesFromAssembly(asm))
            {
                if (t == null || !t.IsClass) continue;

                // Harmony 2.x attribute name is "HarmonyLib.HarmonyPatch".
                // Compare by FullName or simple Name to stay robust across versions/builds.
                bool hasPatchAttr = t.GetCustomAttributes(inherit: true)
                                    .Any(a => a != null &&
                                              (a.GetType().FullName == "HarmonyLib.HarmonyPatch" ||
                                               a.GetType().Name     == "HarmonyPatch"));
                if (hasPatchAttr)
                    yield return t;
            }
        }

        /// <summary>
        /// Nice method formatter for log output: TypeName.MethodName(T0, T1, ...).
        /// </summary>
        private static string Describe(MethodBase m)
        {
            if (m == null) return "(null)";
            try
            {
                string type = m.DeclaringType != null ? m.DeclaringType.FullName : "(global)";
                string pars = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
                return $"{type}.{m.Name}({pars})";
            }
            catch
            {
                // Fallback if reflection blows up for any reason.
                return m.ToString();
            }
        }
        #endregion

        #endregion

        #region Hotkey: Reload Config (Configurable)

        /// <summary>
        /// SUMMARY
        /// -------
        /// Adds a configurable hotkey (Ctrl/Alt/Shift/Win + 1 main key) to hot-reload the
        /// mod's config at runtime. We hook inside InGameHUD.OnPlayerInput so it runs on
        /// the main game thread (safe for content ops and Harmony-driven skin updates).
        ///
        /// DESIGN NOTES
        /// ------------
        /// • Parsing: Forgiving tokenizer; accepts "Ctrl+Shift+F3", "ctrl f3", "Control+F3",
        ///   "Win+R", "Alt+0", "A", "F12", etc. Case-insensitive. Unknown tokens are ignored.
        /// • Binding: Keys.None disables the hotkey.
        /// • Detection: Rising-edge detector (fires once when keys go from "not pressed" -> "pressed").
        /// • Input source: XNA KeyboardState (polling). The Windows key is checked via
        ///   LeftWindows/RightWindows-be aware some OS/game overlays swallow Win keys.
        /// • Threading: Runs in the HUD input tick (game thread). Keep work lightweight.
        ///
        /// USAGE
        /// -----
        /// MLEHotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (MLEHotkeys.ReloadPressedThisFrame()) { MLEConfig.LoadApply(); ... }
        ///
        /// EXAMPLES
        /// --------
        /// "F9"                 -> F9.
        /// "Ctrl+F3"            -> Ctrl + F3.
        /// "Control Shift F12"  -> Ctrl + Shift + F12.
        /// "Win+R"              -> Windows + R.
        /// "Alt+0"              -> Alt + D0 (top-row zero).
        /// "" or null           -> Disabled (Keys.None).
        /// </summary>

        #region Hotkey Binding Model

        /// <summary>
        /// Minimal (Ctrl/Alt/Shift/Win) + one main key binding.
        /// <para>Use <see cref="Parse(string)"/> to create from strings like: "Ctrl+Shift+F3".</para>
        /// </summary>
        internal struct HotkeyBinding
        {
            /// <summary>Modifier flags. Plain fields on purpose (no recursion in property setters).</summary>
            public bool Ctrl, Alt, Shift, Win;

            /// <summary>Main key; Keys.None disables the binding.</summary>
            public Microsoft.Xna.Framework.Input.Keys Key;

            /// <summary>
            /// Parses a human-friendly hotkey like "Ctrl+Shift+F3", "Alt+0", "Win+R".
            /// Unknown tokens are ignored; if no main key is recognized -> Keys.None.
            /// </summary>
            /// <remarks>
            /// Accepts: "ctrl/control", "alt", "shift", "win/windows", F1..F24, A..Z, 0..9, or any <see cref="Microsoft.Xna.Framework.Input.Keys"/> name.
            /// </remarks>
            public static HotkeyBinding Parse(string s)
            {
                var hk = new HotkeyBinding { Key = Microsoft.Xna.Framework.Input.Keys.None };
                if (string.IsNullOrWhiteSpace(s)) return hk;

                var tokens = s.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in tokens)
                {
                    var t = raw.Trim().ToLowerInvariant();
                    switch (t)
                    {
                        case "ctrl":
                        case "control": hk.Ctrl = true; break;
                        case "alt":     hk.Alt = true; break;
                        case "shift":   hk.Shift = true; break;
                        case "win":
                        case "windows": hk.Win = true; break;

                        default:
                            // F-keys (F1..F24).
                            if (t.Length >= 2 && t[0] == 'f' && int.TryParse(t.Substring(1), out var f) && f >= 1 && f <= 24)
                            {
                                hk.Key = (Microsoft.Xna.Framework.Input.Keys)((int)Microsoft.Xna.Framework.Input.Keys.F1 + (f - 1));
                            }
                            // A..Z.
                            else if (t.Length == 1 && t[0] >= 'a' && t[0] <= 'z')
                            {
                                hk.Key = (Microsoft.Xna.Framework.Input.Keys)((int)Microsoft.Xna.Framework.Input.Keys.A + (t[0] - 'a'));
                            }
                            // 0..9 (top row).
                            else if (t.Length == 1 && t[0] >= '0' && t[0] <= '9')
                            {
                                hk.Key = (Microsoft.Xna.Framework.Input.Keys)((int)Microsoft.Xna.Framework.Input.Keys.D0 + (t[0] - '0'));
                            }
                            // Any XNA Keys enum name (e.g., "PageUp", "Insert").
                            else if (Enum.TryParse(raw, ignoreCase: true, out Microsoft.Xna.Framework.Input.Keys k))
                            {
                                hk.Key = k;
                            }
                            break;
                    }
                }
                return hk;
            }

            /// <summary>
            /// Returns true while the binding is currently depressed in the given <see cref="KeyboardState"/>.
            /// Checks both left/right modifier variants (e.g., LeftControl/RightControl).
            /// </summary>
            public bool IsDown(Microsoft.Xna.Framework.Input.KeyboardState ks)
            {
                if (Key == Microsoft.Xna.Framework.Input.Keys.None) return false;

                bool ctrl  = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl) || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl);
                bool alt   = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftAlt)     || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightAlt);
                bool shift = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)   || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
                bool win   = ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftWindows) || ks.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightWindows);

                if (Ctrl  && !ctrl)  return false;
                if (Alt   && !alt)   return false;
                if (Shift && !shift) return false;
                if (Win   && !win)   return false;

                return ks.IsKeyDown(Key);
            }
        }
        #endregion

        #region Hotkey Utility (Edge Detection + Binding)

        /// <summary>
        /// Runtime hotkey manager for "reload config".
        /// <para>Call <see cref="SetReloadBinding(string)"/> after reading INI, then poll <see cref="ReloadPressedThisFrame"/> each HUD tick.</para>
        /// </summary>
        internal static class MLEHotkeys
        {
            private static HotkeyBinding _reload;
            private static bool _hasPrev;
            private static Microsoft.Xna.Framework.Input.KeyboardState _prev;

            /// <summary>
            /// Sets (or disables) the reload binding. Resets the edge detector to avoid a spurious trigger right after change.
            /// </summary>
            public static void SetReloadBinding(string s)
            {
                _reload = HotkeyBinding.Parse(s);
                _hasPrev = false; // Reset edge detector so we don't fire instantly after changing binding.
                Log($"[ModLEx] Reload hotkey set to \"{s}\".");
            }

            /// <summary>
            /// Returns true exactly once when the binding transitions to pressed this frame.
            /// </summary>
            public static bool ReloadPressedThisFrame()
            {
                var now = Microsoft.Xna.Framework.Input.Keyboard.GetState();
                if (!_hasPrev) { _prev = now; _hasPrev = true; return false; }

                bool nowDown = _reload.IsDown(now);
                bool prevDown = _reload.IsDown(_prev);
                _prev = now;

                return nowDown && !prevDown; // Rising edge -> one-shot.
            }
        }
        #endregion

        #region Hotkey: Reload Config (Main-Thread)

        /// <summary>
        /// Listens for the reload hotkey inside InGameHUD.OnPlayerInput so all work executes on the main thread.
        /// Keeps the body small; heavy lifting should be inside MLEConfig.LoadApply().
        /// </summary>
        [HarmonyPatch]
        static class Patch_Hotkey_ReloadConfig_WorldEdit
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI.
            /// </summary>
            static void Postfix(InGameHUD __instance)
            {
                if (!MLEHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    MLEConfig.LoadApply();

                    SendFeedback($"[ModLEx] Config hot-reloaded from \"{PathShortener.ShortenForLog(MLEConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[ModLEx] Hot-reload failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Path Helper (Logs)

        /// <summary>
        /// Shortens absolute paths for logs (prefers trimming to \!Mods\... if present).
        /// </summary>
        internal static class PathShortener
        {
            public static string ShortenForLog(string fullPath)
            {
                if (string.IsNullOrEmpty(fullPath))
                    return string.Empty;

                // Normalize slashes.
                var p = fullPath.Replace('/', '\\');

                // Prefer showing from "\!Mods\..."
                int idx = p.IndexOf(@"\!Mods\", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return p.Substring(idx);

                // Fallback: Full path.
                return p;
            }
        }
        #endregion

        #endregion

        #region Exception Handeler Patches

        #region Backtrace Tap (Capture & Optionally Suppress Uploads)

        /// <summary>
        /// Harmony "tap" for any Backtrace reporter implementation in the game.
        /// - Dynamically discovers all ReportCrash(Exception ...) methods on any type whose
        ///   name matches "BacktraceIssueReporter" (namespace-agnostic).
        /// - Prefix logs the exception via your ExceptionTap and (optionally) suppresses the original call,
        ///   preventing uploads to the vendor crash service.
        /// </summary>
        [HarmonyPatch]
        internal static class BacktraceIssueReporter_ReportCrash_Tap
        {
            #region Target Discovery

            /// <summary>
            /// Multi-target patch: Find every matching ReportCrash overload that
            /// takes an <see cref="Exception"/> as its first parameter.
            ///
            /// Why the assembly scan?
            /// - Some CMZ builds place the class at DNA.Text.BacktraceIssueReporter,
            ///   others root it as BacktraceIssueReporter, and modded builds may embed it elsewhere.
            /// - We search all loaded assemblies once at patch time for robustness.
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // Try common qualified names first, then fall back to a type-name-only scan.
                    Type t = asm.GetType("DNA.Text.BacktraceIssueReporter")
                          ?? asm.GetType("BacktraceIssueReporter")
                          ?? SafeFindTypeByName(asm, "BacktraceIssueReporter");
                    if (t == null) continue;

                    // Pick only methods actually named ReportCrash where arg0 : Exception (covers overloads).
                    foreach (var m in AccessTools.GetDeclaredMethods(t))
                    {
                        if (m.Name != "ReportCrash") continue;
                        var ps = m.GetParameters();
                        if (ps.Length >= 1 && typeof(Exception).IsAssignableFrom(ps[0].ParameterType))
                            yield return m;
                    }
                }

                // Local helper: Avoid TypeLoadException in partially loaded/obfuscated assemblies.
                Type SafeFindTypeByName(Assembly asm, string simpleName)
                {
                    try { return asm.GetTypes().FirstOrDefault(x => x.Name == simpleName); }
                    catch { return null; }
                }
            }
            #endregion

            #region Prefix (Log & Optionally Skip Upload)

            /// <summary>
            /// Prefix receives the first argument as __0 (Harmony convention).
            /// Return:
            ///  - true  => let original ReportCrash run (upload proceeds).
            ///  - false => skip original (no upload).
            /// </summary>
            static bool Prefix(Exception __0 /* ex */)
            {
                try
                {
                    // Always log to the file pipeline so we still get the diagnostics.
                    ExceptionTap.LogReported(__0);
                }
                catch
                {
                    // Never let logging failures interfere with the game.
                }

                // Respect the config switch: If Off, allow upstream reporting to proceed.
                // (ExceptionTap.Arm(mode) sets this flag.)
                if (!ExceptionTap.SuppressUpstreamCrashReporting)
                    return true; // Run the original (upload).

                return false;    // Suppress original => no upload.
            }
            #endregion
        }
        #endregion

        #region Program.Main Patch

        /// <summary>
        /// Patches DNA.CastleMinerZ.Program.Main(string[]):
        /// - Transpiler removes calls to BacktraceIssueReporter.RegisterGlobalHandlers() and .ReportCrash(..).
        /// - Finalizer swallows any exception that still escapes Main (prevents hard crash dialog).
        /// </summary>
        [HarmonyPatch]
        internal static class Program_Main_Patch
        {
            // Resolve the internal Program type by name at runtime; no compile-time reference needed.
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("DNA.CastleMinerZ.Program");
                return AccessTools.Method(t, "Main", new[] { typeof(string[]) });
            }

            // If something still bubbles out of Main, swallow it here (prevents the app from rethrowing).
            [HarmonyFinalizer]
            static Exception Finalizer(Exception __exception)
            {
                if (__exception == null)
                    return null; // Nothing to do; original Main returned normally.

                // Always log one last time to your file logger. Never throw from logging.
                try { ExceptionTap.LogReported(__exception); } catch { }

                // Swallow to keep the process alive:
                return null;     // Swallow => Prevents the vanilla crash dialog.
            }
        }
        #endregion

        #endregion

        #region Exception Sink Patches (Prevents Game Crashes)

        /// <summary>
        /// SAFE TX SEND
        /// Hardens LocalNetworkGamer.SendData(...) against rare race-condition crashes during
        /// worldgen / session transitions (e.g., session not fully initialized or already torn down).
        ///
        /// Behavior:
        /// - Logs the exception (guarded).
        /// - Swallows only known "non-fatal" lifecycle cases:
        ///     • NullReferenceException
        ///     • ObjectDisposedException
        ///     • InvalidOperationException (only when message indicates a not-ready / disposed state)
        ///
        /// Rationale:
        /// - We've seen rare NREs inside LocalNetworkGamer.SendData triggered by worldgen code
        ///   attempting to broadcast block updates while the underlying network state is in flux.
        /// - This keeps the session/game alive by dropping the send attempt instead of hard-crashing.
        /// </summary>
        #region Safe TX Send

        [HarmonyPatch]
        internal static class LocalNetworkGamer_SendData_SafeFinalizer
        {
            // Tell Harmony EXACTLY which overload to patch:
            // Signature: void SendData(byte[] data, int offset, int count, SendDataOptions options)
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(LocalNetworkGamer),
                    "SendData",
                    new Type[]
                    {
                        typeof(byte[]),
                        typeof(int),
                        typeof(int),
                        typeof(SendDataOptions)
                    }
                );
            }

            // Finalizer runs after the original (even if it threw).
            // We can inspect __exception and optionally swallow it by setting it to null.
            static void Finalizer(
                LocalNetworkGamer __instance,
                byte[] data,
                int offset,
                int count,
                SendDataOptions options,
                ref Exception __exception)
            {
                // Fast path: Nothing to do if the original did not throw.
                if (__exception == null) return;

                // Log the exception type + message (guarded so logging itself can't crash).
                try
                {
                    var name = __exception?.GetType()?.Name ?? "<unknown>";
                    LogException(__exception, $"LocalNetworkGamer.SendData:{name}");
                }
                catch { /* Never throw from logging. */ }

                // Swallow only known benign "lifecycle" failures.
                bool isNullRef = __exception is NullReferenceException;
                bool isDisposed = __exception is ObjectDisposedException;

                // InvalidOperationException is sometimes thrown during teardown / not-ready states.
                // Keep this filter tighter so we don't hide real logic bugs.
                bool isInvalidLifecycle =
                    __exception is InvalidOperationException &&
                    IsLikelyLifecycleInvalidOperation(__exception.Message);

                if (!isNullRef && !isDisposed && !isInvalidLifecycle)
                    return; // Let anything else bubble; it's not in our "known benign" bucket.

                // Swallow => prevents crash.
                __exception = null;
            }

            /// <summary>
            /// Heuristic filter: only treat InvalidOperationException as "benign lifecycle" if the message
            /// suggests a not-ready / disposed / disconnected state.
            /// </summary>
            private static bool IsLikelyLifecycleInvalidOperation(string message)
            {
                if (string.IsNullOrEmpty(message))
                    return false;

                // Lowercase once for cheap substring checks (C# 7.3 friendly).
                string m = message.ToLowerInvariant();

                // Common-ish lifecycle indicators (keep conservative).
                if (m.Contains("disposed"))        return true;
                if (m.Contains("not initialized")) return true;
                if (m.Contains("not ready"))       return true;
                if (m.Contains("not connected"))   return true;
                if (m.Contains("no session"))      return true;
                if (m.Contains("session"))         return true; // Fallback (still somewhat conservative).
                if (m.Contains("sign-in") || m.Contains("signin") || m.Contains("signed")) return true;

                return false;
            }
        }
        #endregion

        /// <summary>
        /// SAFE RX RECEIVE
        /// Harmony finalizer that hardens LocalNetworkGamer.ReceiveData against crashy inputs.
        /// Behavior:
        /// - Logs any exception type + message raised by ReceiveData (safe-guarded).
        /// - Swallows only two known, non-fatal cases:
        ///     • ArgumentException("Data buffer is too small").
        ///     • NullReferenceException.
        /// - When swallowed, drops the head packet from the pending queue (calls Release via reflection),
        ///   clears sender, returns 0 bytes, and nulls the exception so the game keeps running.
        /// Rationale:
        /// - Attackers (or buggy peers) can enqueue malformed packets (e.g., null/undersized) that would
        ///   otherwise throw in the middle of your update loop. This keeps the session alive and drains the bad packet.
        /// </summary>
        #region Safe RX Receive

        [HarmonyPatch]
        static class LocalNetworkGamer_ReceiveData_SafeFinalizer
        {
            // Tell Harmony EXACTLY which overload to patch:
            // Signature: int ReceiveData(byte[] data, out NetworkGamer sender).
            // NOTE: 'out' is represented as ByRef in reflection (MakeByRefType).
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(LocalNetworkGamer),
                    "ReceiveData",
                    new Type[] { typeof(byte[]), typeof(NetworkGamer).MakeByRefType() } // 'out sender' => ByRef.
                );
            }

            // Finalizer runs after the original (even if it threw).
            // We can inspect __exception, optionally suppress it, and adjust return values.
            static void Finalizer(
                LocalNetworkGamer __instance,
                ref int __result,
                ref NetworkGamer sender,
                ref Exception __exception)
            {
                // Fast path: Nothing to do if the original did not throw.
                if (__exception == null) return;

                // Log the exception type + message (guarded so logging itself can't crash).
                try
                {
                    var name = __exception?.GetType()?.Name ?? "<unknown>";
                    LogException(__exception, name);
                }
                catch { /* Never throw from logging. */ }

                // Decide if we should swallow this exception.
                // Only intercept known benign cases that stem from malformed/hostile packets.
                bool isBufferTooSmall =
                     __exception is ArgumentException &&
                    (__exception.Message?.IndexOf("Data buffer is too small", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
                bool isNullRef = __exception is NullReferenceException;

                // Don't swallow the buffer-too-small case. Let it bubble normally.
                if (isBufferTooSmall)
                    return;

                // Only swallow the benign NRE (rare bad packet). Everything else bubbles.
                if (!isNullRef)
                    return;

                // Drop the offending head packet so we don't loop on it again next frame.
                // _pendingData is a private list; we access it via reflection and lock it just like the original.
                var listField = AccessTools.Field(typeof(LocalNetworkGamer), "_pendingData");
                var list = listField != null ? listField.GetValue(__instance) as System.Collections.IList : null;
                if (list != null)
                {
                    lock (list)
                    {
                        if (list.Count > 0)
                        {
                            var pkt = list[0];
                            list.RemoveAt(0);

                            // If the pending packet has a Release() method, call it to free any pooled buffers.
                            if (pkt != null)
                            {
                                var rel = AccessTools.Method(pkt.GetType(), "Release");
                                rel?.Invoke(pkt, null);
                            }
                        }
                    }
                }

                // Mimic a benign receive: No sender, 0 bytes, and no exception.
                sender      = null;
                __result    = 0;
                __exception = null; // Swallow (prevents the crash).
            }
        }
        #endregion

        /// <summary>
        /// SAFE RX DECODE
        /// Wraps every message RecieveData/ReceiveData(BinaryReader) in a guard:
        ///   - Prefix remembers the stream start position.
        ///   - Finalizer swallows any decode exception and fast-forwards to end
        ///     of the message buffer so the pipeline keeps ticking.
        /// Notes:
        ///   • Marked [HarmonySilent] to hide from patch-summary noise.
        ///   • We do NOT change message objects when a failure happens; we just
        ///     bail out early for this one payload and log the exception.
        /// </summary>
        #region Safe RX Decode

        [HarmonyPatch]
        [HarmonySilent] // Hide this whole patch container from the summary logs.
        internal static class SafeReceivePatch
        {
            /// <summary>
            /// Dynamically finds every instance method named RecieveData/ReceiveData(BinaryReader)
            /// across loaded assemblies (defensive: ignores abstract/generic).
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods()
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        if (!t.IsClass || t.IsAbstract) continue;

                        // Optional namespace filter:
                        // if (t.Namespace == null || !t.Namespace.StartsWith("DNA.CastleMinerZ.Net", StringComparison.Ordinal)) continue;

                        var m1 = t.GetMethod("RecieveData", flags, null, new[] { typeof(BinaryReader) }, null);
                        if (IsPatchable(m1)) yield return m1;

                        var m2 = t.GetMethod("ReceiveData", flags, null, new[] { typeof(BinaryReader) }, null);
                        if (IsPatchable(m2)) yield return m2;
                    }
                }
                bool IsPatchable(MethodInfo mi) => mi != null && !mi.IsAbstract && !mi.ContainsGenericParameters;
            }

            /// <summary>
            /// Capture the start position in the stream so we can skip the rest if decode fails.
            /// </summary>
            static void Prefix(BinaryReader __0, out (Stream s, long start) __state)
            {
                var s = __0?.BaseStream;
                __state = (s != null && s.CanSeek) ? (s, s.Position) : (null, -1);
            }

            /// <summary>
            /// Swallow any RecieveData/ReceiveData exception, seek to end of message,
            /// and log a structured entry (keeps networking loop alive).
            /// </summary>
            static Exception Finalizer(object __instance, BinaryReader __0, (Stream s, long start) __state, Exception __exception)
            {
                if (__exception == null) return null;
                try
                {
                    var s = __state.s ?? __0?.BaseStream;
                    if (s != null && s.CanSeek)
                        s.Position = s.Length; // Fast-forward to the end of the current buffer; this abandons the malformed payload.

                    var name = __instance?.GetType()?.Name ?? "<unknown>";
                    LogException(__exception, name);
                }
                catch { /* Never throw from a guard. */ }
                return null; // Returning null swallows the original exception.
            }
        }
        #endregion

        /// <summary>
        /// SAFE MESSAGE DISPATCH
        /// EnemyManager.HandleMessage is a hot junction for many messages.
        /// If a downstream handler throws, we turn it into a feedback line and keep going.
        /// </summary>
        #region Safe Message Dispatch

        // EnemyManager is the central router for many messages (see its HandleMessage(CastleMinerZMessage)).
        [HarmonyPatch(typeof(DNA.CastleMinerZ.AI.EnemyManager), "HandleMessage", new[] { typeof(DNA.CastleMinerZ.Net.CastleMinerZMessage) })]
        internal static class SafeHandleMessagePatch
        {
            /// <summary>
            /// Convert an exception during HandleMessage into in-game feedback and swallow it
            /// so the server/client loop continues.
            /// </summary>
            static Exception Finalizer(DNA.CastleMinerZ.Net.CastleMinerZMessage __0, Exception __exception)
            {
                if (__exception == null) return null;
                try
                {
                    var mt = __0?.GetType()?.Name ?? "<unknown>";
                    Log($"[Exception] HandleMessage {mt}: {__exception.Message}");
                }
                catch { /* Never throw from a guard. */ }
                return null; // Returning null swallows the original exception.
            }
        }
        #endregion

        /// <summary>
        ///  LAST-DITCH GAME LOOP GUARD
        ///  If anything still bubbles out of Update, log and swallow so the frame loop lives.
        /// </summary>
        #region Game Loop Guard

        // Catch anything that still slips out during normal Update/Draw ticks.
        // CastleMinerZGame.Update(GameTime).
        [HarmonyPatch(typeof(DNA.CastleMinerZ.CastleMinerZGame), "Update", new[] { typeof(GameTime) })]
        internal static class SafeGameUpdatePatch
        {
            /// <summary>
            /// Final guard for Update: Log & swallow unexpected exceptions to keep the game from crashing.
            /// </summary>
            static Exception Finalizer(Exception __exception)
            {
                if (__exception == null) return null;
                try { LogException(__exception); } catch { }
                return null; // Returning null swallows the original exception.
            }
        }

        // InGameHUD.OnDraw(GraphicsDevice, SpriteBatch, GameTime).
        [HarmonyPatch(
            typeof(DNA.CastleMinerZ.UI.InGameHUD),
            "OnDraw",
            new Type[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) }
        )]
        internal static class SafeGameOnDrawPatch_InGameHUD
        {
            /// <summary>
            /// Final guard for HUD draw: Log & swallow to keep the frame loop alive.
            /// </summary>
            static Exception Finalizer(Exception __exception)
            {
                if (__exception == null) return null;
                try { LogException(__exception); } catch { }
                return null; // Returning null swallows the original exception.
            }
        }

        // BlockTerrain.Draw(GraphicsDevice, GameTime, Matrix, Matrix).
        [HarmonyPatch(
            typeof(DNA.CastleMinerZ.Terrain.BlockTerrain),
            "Draw",
            new Type[] { typeof(GraphicsDevice), typeof(GameTime), typeof(Matrix), typeof(Matrix) }
        )]
        internal static class SafeGameDrawPatch_BlockTerrain
        {
            /// <summary>
            /// Final guard for terrain draw: Log & swallow to keep rendering stable.
            /// </summary>
            static Exception Finalizer(Exception __exception)
            {
                if (__exception == null) return null;
                try { LogException(__exception); } catch { }
                return null; // Returning null swallows the original exception.
            }
        }
        #endregion

        /// <summary>
        /// Shared Exception Logger
        /// - Writes a structured, multi-line-aligned entry to Caught_Exceptions.log
        /// - Keeps alignment consistent with your LogSystem (_alignColumn = 83 here)
        /// - Feedback sending is optional (currently disabled; FirstChance tap may already cover it)
        /// </summary>
        #region Shared Logging Helper

        internal static void LogException(Exception exception, string instanceName = null)
        {
            try
            {
                // Include type, message, and full stack via ToString().
                // The structured logger will prefix the first line and indent the rest.
                const string _exceptionsLogFile = "Caught_Exceptions.log";
                string name                     = !string.IsNullOrEmpty(instanceName) ? $"{instanceName}:" : string.Empty;
                string logMessage               = $"[NetRx]{name} {exception}\n";

                // Skip this helper so caller namespace aligns with the real source.
                var skip = new[]
                {
                    typeof(ExceptionTap).FullName,
                };

                // IMPORTANT: relies on your LogSystem.Log overload that supports multiline alignment.
                LogSystem.Log(
                    message:           logMessage,
                    optionalLogName:   _exceptionsLogFile,
                    skipTypeFullNames: skip,
                    _alignColumn:      83                  // Keep in sync with the chosen column.
                );

                // Optional: In-game feedback (disabled here; the exception tap already does this).
                // LogSystem.SendFeedback($"[Exception] {exception.Message}", alsoLogToFile: false);
            }
            catch { /* Never throw from logging. */ }
        }
        #endregion

        #endregion

        #region Fullscreen Alt-Tab Terrain Recovery

        /// <summary>
        /// SAFE FULLSCREEN ALT-TAB TERRAIN RECOVERY
        /// Vanilla BlockTerrain.GlobalUpdate() commits pending terrain vertex buffers while the
        /// game is inactive. In exclusive fullscreen, alt-tab / Win-key focus loss can leave the
        /// graphics device in a transient state while multiplayer block edits continue arriving.
        ///
        /// Failure mode:
        /// - Remote/local block edits queue chunk geometry rebuild work while the client is inactive.
        /// - Vanilla BuildPendingVertexBuffers() finalizes those chunks during the inactive window.
        /// - RenderChunk.FinishBuildingBuffers() ultimately builds GPU vertex buffers from pending
        ///   BlockBuildData; if the device is not fully usable, the chunk can end up committed with
        ///   no valid geometry.
        /// - Collision/data remain correct, but the chunk becomes visually invisible until a later
        ///   nearby world update forces another rebuild.
        ///
        /// Fix strategy:
        /// - Defer BuildPendingVertexBuffers() while the game is inactive in fullscreen.
        /// - On reactivation, perform one best-effort visible-ring terrain rebuild so any stale or
        ///   geometry-less chunks around the player are healed immediately.
        ///
        /// Notes:
        /// - This intentionally targets the vanilla fullscreen focus-loss bug you reproduced with
        ///   zero mods loaded.
        /// - Windowed / borderless flows are left alone.
        /// - The recovery pass is best-effort and only runs after we actually deferred work.
        /// </summary>

        #region Fullscreen Terrain Recovery Helper

        internal static class FullscreenTerrainRecovery
        {
            // Reflect BlockTerrain._computeGeometryPool and its Add/Drain methods once so we can
            // re-queue the visible chunk ring after regaining focus.
            private static readonly FieldInfo F_ComputeGeometryPool =
                AccessTools.Field(typeof(DNA.CastleMinerZ.Terrain.BlockTerrain), "_computeGeometryPool");

            private static readonly MethodInfo MI_ComputeGeometryPool_Add;
            private static readonly MethodInfo MI_ComputeGeometryPool_Drain;

            // Runtime focus / recovery state.
            private static bool _focusStateInitialized;
            private static bool _lastIsActive;
            private static bool _deferredInactiveVertexCommit;

            static FullscreenTerrainRecovery()
            {
                try
                {
                    Type poolType = F_ComputeGeometryPool?.FieldType;
                    MI_ComputeGeometryPool_Add   = poolType != null ? AccessTools.Method(poolType, "Add", new[] { typeof(int) }) : null;
                    MI_ComputeGeometryPool_Drain = poolType != null ? AccessTools.Method(poolType, "Drain", Type.EmptyTypes) : null;
                }
                catch
                {
                    MI_ComputeGeometryPool_Add   = null;
                    MI_ComputeGeometryPool_Drain = null;
                }
            }

            /// <summary>
            /// Returns true when pending terrain vertex-buffer commits should be deferred because
            /// the game is currently inactive in exclusive fullscreen.
            /// </summary>
            public static bool ShouldDeferPendingVertexCommit()
            {
                CastleMinerZGame game = CastleMinerZGame.Instance;
                if (game == null)
                    return false;

                // Keep the fix narrow: only touch the proven bad path.
                if (!game.IsFullScreen)
                    return false;

                if (game.IsActive)
                    return false;

                _deferredInactiveVertexCommit = true;
                return true;
            }

            /// <summary>
            /// Tracks focus transitions and, when needed, performs a one-shot visible-ring terrain
            /// rebuild after fullscreen focus is regained.
            /// </summary>
            public static void OnGameUpdate(CastleMinerZGame game)
            {
                if (game == null)
                    return;

                bool isActive = game.IsActive;

                if (!_focusStateInitialized)
                {
                    _focusStateInitialized = true;
                    _lastIsActive          = isActive;
                    return;
                }

                bool reactivated = !_lastIsActive && isActive;
                _lastIsActive    = isActive;

                if (!reactivated)
                    return;

                if (!_deferredInactiveVertexCommit)
                    return;

                try
                {
                    TryRecoverVisibleTerrain(game._terrain);
                }
                catch (Exception ex)
                {
                    LogException(ex, "FullscreenTerrainRecovery");
                }
                finally
                {
                    _deferredInactiveVertexCommit = false;
                }
            }

            /// <summary>
            /// Best-effort terrain healing pass after returning from fullscreen focus loss:
            /// - Flush any still-pending VB commits now that the game is active again.
            /// - Re-queue the currently visible ring of chunks for geometry rebuild.
            /// - Drain and finalize immediately so stale/invisible chunks recover without requiring
            ///   a nearby player interaction.
            /// </summary>
            private static void TryRecoverVisibleTerrain(DNA.CastleMinerZ.Terrain.BlockTerrain terrain)
            {
                if (terrain == null || !terrain.IsReady)
                    return;

                // First, commit anything that was intentionally deferred while inactive.
                terrain.BuildPendingVertexBuffers();

                object pool = F_ComputeGeometryPool?.GetValue(terrain);
                if (pool == null || MI_ComputeGeometryPool_Add == null || MI_ComputeGeometryPool_Drain == null)
                    return;

                IntVector3[] offsets = terrain._radiusOrderOffsets;
                if (offsets == null || offsets.Length == 0)
                    return;

                int eyeChunkIndex    = terrain._currentEyeChunkIndex;
                IntVector3 baseChunk = new IntVector3(eyeChunkIndex % 24, 0, eyeChunkIndex / 24);

                // Avoid duplicate queueing if offsets ever overlap.
                bool[] queued = new bool[576];

                for (int i = 0; i < offsets.Length; i++)
                {
                    IntVector3 iv = new IntVector3(baseChunk.X + offsets[i].X, 0, baseChunk.Z + offsets[i].Z);
                    if (iv.X < 0 || iv.X >= 24 || iv.Z < 0 || iv.Z >= 24)
                        continue;

                    int idx = iv.X + iv.Z * 24;
                    if (queued[idx])
                        continue;

                    queued[idx] = true;
                    QueueOne(terrain, pool, idx);
                }

                // Kick rebuild work now, then finalize the resulting VBs on the active device.
                MI_ComputeGeometryPool_Drain.Invoke(pool, null);
                terrain.BuildPendingVertexBuffers();
            }

            /// <summary>
            /// Queues one loaded chunk for a geometry rebuild, balancing the engine's later
            /// _numUsers decrement performed during VB finalization.
            /// </summary>
            private static void QueueOne(DNA.CastleMinerZ.Terrain.BlockTerrain terrain, object pool, int idx)
            {
                if (terrain._chunks[idx]._action == DNA.CastleMinerZ.Terrain.BlockTerrain.NextChunkAction.WAITING_TO_LOAD)
                    return;

                terrain._chunks[idx]._numUsers.Increment();
                MI_ComputeGeometryPool_Add.Invoke(pool, new object[] { idx });
            }
        }
        #endregion

        #region Defer BlockTerrain.BuildPendingVertexBuffers While Inactive Fullscreen

        [HarmonyPatch(typeof(DNA.CastleMinerZ.Terrain.BlockTerrain), nameof(DNA.CastleMinerZ.Terrain.BlockTerrain.BuildPendingVertexBuffers))]
        internal static class BlockTerrain_BuildPendingVertexBuffers_DeferDuringInactiveFullscreen
        {
            /// <summary>
            /// Prevents terrain vertex-buffer commits from running while the game is inactive in
            /// exclusive fullscreen. This keeps vanilla from swapping in geometry-less chunks during
            /// alt-tab / Win-key focus loss.
            /// </summary>
            [HarmonyPrefix]
            private static bool Prefix()
            {
                // Return false -> skip original BuildPendingVertexBuffers() for now.
                return !FullscreenTerrainRecovery.ShouldDeferPendingVertexCommit();
            }
        }
        #endregion

        #region CastleMinerZGame.Update - Recover Terrain On Focus Regain

        [HarmonyPatch(typeof(CastleMinerZGame), "Update", new[] { typeof(GameTime) })]
        internal static class CastleMinerZGame_Update_FullscreenTerrainRecovery
        {
            /// <summary>
            /// Watches for the transition from inactive -> active and triggers a one-shot terrain
            /// recovery pass when we previously deferred fullscreen VB commits.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(CastleMinerZGame __instance)
            {
                FullscreenTerrainRecovery.OnGameUpdate(__instance);
            }
        }
        #endregion

        #endregion

        #region Defensive Hardening & Noise Reduction (Non-Fatal)

        /// <summary>
        /// SAFE PLAYER PROFILE CALLBACK
        /// Player's ctor schedules an async BeginGetProfile callback that can run during session teardown
        /// or while graphics are not ready, causing FIRST_CHANCE NullReferenceException spam.
        ///
        /// Patch target:
        ///   Player.<.ctor>b__0(IAsyncResult)  (the anonymous delegate method)
        ///
        /// Behavior:
        /// - Replaces the callback body with a fully guarded version.
        /// - Avoids throwing (so FIRST_CHANCE noise stops).
        /// - Disposes the picture stream.
        /// </summary>
        #region Safe Player Profile Callback

        [HarmonyPatch]
        internal static class Player_ProfileCallback_SafePrefix
        {
            static MethodBase TargetMethod()
            {
                // Match the method shown in your stack trace exactly:
                // DNA.CastleMinerZ.Player.<.ctor>b__0(IAsyncResult result)
                return AccessTools.Method(
                    AccessTools.TypeByName("DNA.CastleMinerZ.Player"),
                    "<.ctor>b__0",
                    new[] { typeof(IAsyncResult) }
                );
            }

            [HarmonyPrefix]
            private static bool Prefix(object __instance, IAsyncResult result)
            {
                // __instance is Player (kept 'object' so we don't need a compile-time reference here).
                try
                {
                    if (__instance == null)
                        return false; // Skip original.

                    // Get fields via reflection (keeps it drop-in even if your patch project doesn't reference Player directly).
                    var tPlayer = __instance.GetType();

                    var gamerField = AccessTools.Field(tPlayer, "Gamer");
                    var profField  = AccessTools.Field(tPlayer, "Profile");
                    var picField   = AccessTools.Field(tPlayer, "GamerPicture");

                    var gamer = gamerField != null ? gamerField.GetValue(__instance) as DNA.Net.GamerServices.NetworkGamer : null;
                    if (gamer == null)
                        return false;

                    // Respect the NetworkGamer flags.
                    // These guards prevent EndGetProfile from running on torn-down gamers.
                    try
                    {
                        if (gamer.IsDisposed || gamer.HasLeftSession)
                            return false;
                    }
                    catch { /* If properties aren't present in some build, ignore. */ }

                    // EndGetProfile can throw if the async op was canceled/invalid.
                    DNA.Net.GamerServices.GamerProfile prof = null;
                    try
                    {
                        prof = gamer.EndGetProfile(result);
                    }
                    catch
                    {
                        return false;
                    }

                    if (prof == null)
                        return false;

                    // Need a valid graphics device to build the texture.
                    var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                    var gd   = game?.GraphicsDevice;
                    if (gd == null)
                        return false;

                    Stream s = null;
                    try
                    {
                        s = prof.GetGamerPicture();
                        if (s == null)
                            return false;

                        if (s.CanSeek)
                            s.Position = 0;

                        // Commit fields only after everything is valid.
                        profField?.SetValue(__instance, prof);
                        picField?.SetValue(__instance, Texture2D.FromStream(gd, s));
                    }
                    catch
                    {
                        // Swallow any image decode / device / stream issues.
                        return false;
                    }
                    finally
                    {
                        try { s?.Dispose(); } catch { }
                    }

                    // Skip original callback (we handled it).
                    return false;
                }
                catch
                {
                    // Never throw from patch; skip original.
                    return false;
                }
            }
        }
        #endregion

        /// <summary>
        /// SAFE PARTICLE DRAW
        /// Hardens ParticleEmitterCore.Draw(...) against rare draw-time NullReferenceException
        /// failures caused by transient / partially-torn-down particle state.
        ///
        /// Behavior:
        /// - Logs the exception (throttled).
        /// - Swallows only NullReferenceException.
        /// - Lets all other exception types bubble normally.
        /// </summary>
        #region Safe Particle Draw

        [HarmonyPatch]
        internal static class ParticleEmitterCore_Draw_SafeFinalizer
        {
            private static DateTime _lastLogUtc;

            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("DNA.Drawing.Particles.ParticleEmitter+ParticleEmitterCore");
                if (t == null)
                    return null;

                return AccessTools.Method(
                    t,
                    "Draw",
                    new[] { typeof(GraphicsDevice), typeof(GameTime), typeof(Matrix), typeof(Matrix) }
                );
            }

            /// <summary>
            /// Finalizer runs after the original method, even if it throws.
            /// Swallows only NullReferenceException so unexpected render failures still surface.
            /// </summary>
            [HarmonyFinalizer]
            static Exception Finalizer(Exception __exception)
            {
                if (__exception == null)
                    return null;

                if (!(__exception is NullReferenceException))
                    return __exception;

                try
                {
                    // Light throttle so a broken emitter does not spam every frame.
                    var now = DateTime.UtcNow;
                    if ((now - _lastLogUtc) > TimeSpan.FromSeconds(2))
                    {
                        _lastLogUtc = now;
                        LogException(__exception, "ParticleEmitterCore.Draw");
                    }
                }
                catch { }

                return null; // Swallow this specific draw-time null ref.
            }
        }
        #endregion

        /// <summary>
        /// SAFE INVENTORY ITEM ATLAS RECOVERY
        /// Hardens InventoryItem's static 2D icon atlas lifecycle against stale or disposed
        /// RenderTarget2D instances that can survive across reload paths and later fail when
        /// FinishInitialization(...) tries to bind them again.
        ///
        /// Behavior:
        /// - Detects disposed or otherwise invalid cached atlas render targets before vanilla reuse.
        /// - Clears stale atlas references so vanilla rebuilds them cleanly.
        /// - Prevents non-fatal atlas reuse crashes caused by dead RenderTarget2D instances.
        /// </summary>
        #region Safe Inventory Item Atlas Recovery

        #region Safe InventoryItem.FinishInitialization - Rebuild Disposed Atlases

        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.FinishInitialization))]
        internal static class InventoryItem_FinishInitialization_DisposedAtlasGuard
        {
            private static readonly FieldInfo F_2DImages =
                AccessTools.Field(typeof(InventoryItem), "_2DImages");

            private static readonly FieldInfo F_2DImagesLarge =
                AccessTools.Field(typeof(InventoryItem), "_2DImagesLarge");

            /// <summary>
            /// Runs before InventoryItem.FinishInitialization(...) and clears stale atlas
            /// render targets if they were already disposed, forcing vanilla to recreate them.
            /// </summary>
            [HarmonyPriority(HarmonyLib.Priority.First)]
            [HarmonyPrefix]
            private static void Prefix()
            {
                var small = F_2DImages?.GetValue(null) as RenderTarget2D;
                var large = F_2DImagesLarge?.GetValue(null) as RenderTarget2D;

                bool smallBad = IsDead(small);
                bool largeBad = IsDead(large);

                if (!smallBad && !largeBad)
                    return;

                try { if (small != null && !small.IsDisposed) small.Dispose(); } catch { }
                try { if (large != null && !large.IsDisposed) large.Dispose(); } catch { }

                try { F_2DImages?.SetValue(null, null); } catch { }
                try { F_2DImagesLarge?.SetValue(null, null); } catch { }
            }

            /// <summary>
            /// Returns true when the atlas render target exists but is no longer valid for reuse.
            /// </summary>
            private static bool IsDead(RenderTarget2D rt)
            {
                if (rt == null)
                    return false;

                try
                {
                    if (rt.IsDisposed)
                        return true;

                    var gd = rt.GraphicsDevice;
                    if (gd == null)
                        return true;

                    _ = rt.Width;
                    _ = rt.Height;

                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
                catch
                {
                    return true;
                }
            }
        }
        #endregion

        #endregion

        #endregion

        #region Defensive Networking Hardening (Anti-Spam + Stability)

        /// <summary>
        /// This patch prevents application-layer Denial-of-Service (DoS) attacks via
        /// the reliable message flooding / queue flooding.
        ///
        /// The key detail is that this abuses a high-cost gameplay message (Create/Consume pickup)
        /// at a rate that makes the main thread spend all its time draining the network loop,
        /// and with Reliable the backlog it can build until you hitch/freeze/crash.
        ///
        /// Also includes a local pickup-request throttle to reduce client-side burst lag:
        /// - Token-bucket caps how fast the local client emits RequestPickupMessage via PlayerTouchedPickup.
        /// - When throttled, it undoes the early _pickedUp flag so pickups can retry next frame (no "stuck" pickups).
        /// </summary>
        #region FloodGuard: LocalNetworkGamer Inbound Packet Rate Limiter (+ Purge Queued Backlog)

        /// <summary>
        /// NOTES / INTENT
        /// --------------------------------------------------------------------------------
        /// This patch sits on the lowest practical inbound path you can easily hook:
        ///     LocalNetworkGamer.AppendNewDataPacket(...)
        /// That's the point where decoded packets are about to be queued into _pendingData.
        ///
        /// Goal:
        /// - Prevent "reliable spam" (or any spam) from turning into:
        ///     1) an ever-growing pending queue, and/or
        ///     2) long main-thread stalls while the game drains/processes that queue.
        ///
        /// Key behaviors:
        /// - Per-sender 1-second window count (PER_SENDER_MAX_PACKETS_PER_SEC).
        /// - If a sender exceeds the threshold, we "blackhole" them for BLACKHOLE_MS.
        /// - On entering blackhole, we also purge already queued packets from that sender
        ///   so you don't spend 10-20 seconds "recovering" by processing backlog.
        ///
        /// Local stability add-on:
        /// - PlayerTouchedPickup is token-bucket throttled (Burst + RefillMs) to cap local pickup request spam
        ///   without permanently breaking pickups.
        ///
        /// Important tuning notes:
        /// - PER_SENDER_MAX_PACKETS_PER_SEC:
        ///     - Don't set this too low (like 20) unless you're OK with dropping legit bursts.
        ///     - Some legit systems can burst messages (pickups, chunk/terrain traffic, etc.).
        /// - DO_NOT_EXEMPT_HOST:
        ///     - If your attacker can be host (your test attacker is host), exempting host
        ///       means you'll never catch it. Keeping this true is safer for "host spam" scenarios.
        /// - Logging:
        ///     - This implementation logs when a sender trips into blackhole (throttled).
        ///     - While already blackholed, it returns false immediately and does NOT spam logs
        ///       (that's intentional to avoid making lag worse during an attack).
        /// --------------------------------------------------------------------------------
        /// </summary>

        [HarmonyPatch(typeof(LocalNetworkGamer))]
        internal static class Patch_LocalNetworkGamer_FloodGuard
        {
            #region FloodGuard - Runtime State + Timing Helpers (Config-Backed)

            // Config backed states.
            private static bool FLOOD_GUARD_ENABLED            => FloodGuard_Settings.FloodGuardEnabled;
            private static int  PER_SENDER_MAX_PACKETS_PER_SEC => FloodGuard_Settings.PerSenderMaxPacketsPerSec;
            private static int  BLACKHOLE_MS                   => FloodGuard_Settings.BlackholeMs;
            private static bool DO_NOT_EXEMPT_HOST             => FloodGuard_Settings.DoNotExemptHost;
            private static int  ALLOWLIST_MAX_PACKETS_PER_SEC  => FloodGuard_Settings.AllowlistMaxPacketsPerSec;
            private static bool PICKUP_THROTTLE_ENABLED        => FloodGuard_Settings.PickupThrottleEnabled;
            private static int  PICKUP_TOUCH_BURST             => FloodGuard_Settings.PickupTouchBurst;
            private static int  PICKUP_TOUCH_REFILL_MS         => FloodGuard_Settings.PickupTouchRefillMs;

            private sealed class SenderState
            {
                public long WindowStart;
                public int  Count;
                public long BlackholeUntil;
                public long LastLog;

                // Allowlist throttle window (separate from spam window).
                public long AllowWindowStart;
                public int  AllowCount;
            }

            private static readonly object                        _lock   = new object();
            private static readonly Dictionary<byte, SenderState> _sender = new Dictionary<byte, SenderState>(32);

            // Stopwatch ticks helpers (stable, high-res).
            private static long Now()                  => Stopwatch.GetTimestamp();
            private static long MsToTicks(int ms)      => (long)(ms * (Stopwatch.Frequency / 1000.0));
            private static long SecToTicks(double sec) => (long)(sec * Stopwatch.Frequency);

            /// <summary>
            /// Resets the allowlist lookup so it will be rebuilt on next use.
            /// </summary>
            internal static void InvalidateAllowlistCache()
            {
                try { _allowIds.Clear(); } catch { }
                _allowInit = false;
            }
            #endregion

            #region FloodGuard - MessageID allowlist (Exact, Using DNA.Net.Message._messageIDs)

            // NOTE: These are message types; we resolve their MessageID bytes via DNA.Net.Message._messageIDs.
            private static string[] ALLOW_MESSAGE_TYPES => FloodGuard_Settings.AllowMessageTypes;

            // DNA.Net.Message has: Private static Dictionary<Type, byte> _messageIDs;
            private static readonly FieldInfo F_messageIDs =
                AccessTools.Field(AccessTools.TypeByName("DNA.Net.Message"), "_messageIDs");

            private static readonly HashSet<byte> _allowIds = new HashSet<byte>();
            private static bool                   _allowInit;

            private static void EnsureAllowlistInit()
            {
                if (_allowInit) return;
                _allowInit = true;

                try
                {
                    // If Message hasn't been referenced yet, TypeByName triggers load and static ctor,
                    // which calls PopulateMessageTypes() (per your Message.cs).
                    if (!(F_messageIDs?.GetValue(null) is Dictionary<Type, byte> dict) || dict.Count == 0) return;

                    for (int i = 0; i < ALLOW_MESSAGE_TYPES.Length; i++)
                    {
                        var t = AccessTools.TypeByName(ALLOW_MESSAGE_TYPES[i]);
                        if (t == null) continue;

                        if (dict.TryGetValue(t, out byte id))
                            _allowIds.Add(id);
                    }
                }
                catch { /* Best-effort. */ }
            }

            /// <summary>
            /// CMZ message blob format (per DNA.Net.Message.cs):
            ///   [0]   = MessageID (byte)
            ///   [...] = message payload
            ///   [end] = XOR checksum (byte)
            ///
            /// So the exact MessageID is data[offset].
            /// </summary>
            private static bool IsAllowlistedPayload(byte[] data, int offset, int length)
            {
                if (data == null) return false;

                // Must be at least: [id][checksum].
                if (length < 2) return false;
                if (offset < 0 || offset >= data.Length) return false;
                if (offset + length > data.Length) return false;

                EnsureAllowlistInit();
                if (_allowIds.Count == 0) return false;

                byte id = data[offset];
                return _allowIds.Contains(id);
            }
            #endregion

            #region FloodGuard - Packet Gate (Rate Limit + Allowlist + Blackhole)

            // Hook both overloads: Packet enqueued as a full byte[] or as a slice.
            [HarmonyPrefix]
            [HarmonyPatch("AppendNewDataPacket", new[] { typeof(byte[]), typeof(NetworkGamer) })]
            private static bool Prefix_Append1(LocalNetworkGamer __instance, byte[] data, NetworkGamer sender)
                => ShouldEnqueue(__instance, data, 0, data?.Length ?? 0, sender);

            [HarmonyPrefix]
            [HarmonyPatch("AppendNewDataPacket", new[] { typeof(byte[]), typeof(int), typeof(int), typeof(NetworkGamer) })]
            private static bool Prefix_Append2(LocalNetworkGamer __instance, byte[] data, int offset, int length, NetworkGamer sender)
                => ShouldEnqueue(__instance, data, offset, length, sender);

            /// <summary>
            /// Returns false to drop the inbound packet BEFORE it is queued.
            /// </summary>
            private static bool ShouldEnqueue(LocalNetworkGamer local, byte[] data, int offset, int length, NetworkGamer sender)
            {
                if (!FLOOD_GUARD_ENABLED)
                    return true;

                if (local == null || sender == null)
                    return true;

                // Never block local loopback.
                if (sender.IsLocal)
                    return true;

                // If you exempt host, your test attacker (host) will bypass, and some real attacks can too.
                if (!DO_NOT_EXEMPT_HOST && sender.IsHost)
                    return true;

                bool allowlisted = IsAllowlistedPayload(data, offset, length);

                byte sid = sender.Id;
                long now = Now();

                bool blackholeNow = false;

                lock (_lock)
                {
                    if (!_sender.TryGetValue(sid, out var st))
                    {
                        st = new SenderState
                        {
                            WindowStart      = now,
                            Count            = 0,
                            BlackholeUntil   = 0,
                            LastLog          = 0,

                            AllowWindowStart = now,
                            AllowCount       = 0,
                        };
                        _sender[sid] = st;
                    }

                    // Already blackholed:
                    // - Allow allowlisted messages through (throttled).
                    // - Drop everything else silently (avoids log spam / extra overhead during attack).
                    if (st.BlackholeUntil > now)
                    {
                        if (!allowlisted)
                            return false;

                        // 1-second allow window.
                        if (now - st.AllowWindowStart >= SecToTicks(1.0))
                        {
                            st.AllowWindowStart = now;
                            st.AllowCount = 0;
                        }

                        st.AllowCount++;
                        if (st.AllowCount > ALLOWLIST_MAX_PACKETS_PER_SEC)
                            return false;

                        return true;
                    }

                    // If allowlisted, do not count toward flood threshold; just apply its own small throttle.
                    if (allowlisted)
                    {
                        if (now - st.AllowWindowStart >= SecToTicks(1.0))
                        {
                            st.AllowWindowStart = now;
                            st.AllowCount = 0;
                        }

                        st.AllowCount++;
                        if (st.AllowCount > ALLOWLIST_MAX_PACKETS_PER_SEC)
                            return false;

                        return true;
                    }

                    // 1-second window (non-allowlisted traffic).
                    if (now - st.WindowStart >= SecToTicks(1.0))
                    {
                        st.WindowStart = now;
                        st.Count = 0;
                    }

                    st.Count++;

                    if (st.Count > PER_SENDER_MAX_PACKETS_PER_SEC)
                    {
                        // Enter blackhole.
                        st.BlackholeUntil = now + MsToTicks(BLACKHOLE_MS);
                        blackholeNow = true;

                        // Throttled log (~2/sec max).
                        if (now - st.LastLog >= SecToTicks(0.5))
                        {
                            st.LastLog = now;
                            try { SendFeedback($"[FloodGuard] BLACKHOLE {sender.Gamertag} (id:{sid}) for {BLACKHOLE_MS}ms (>{PER_SENDER_MAX_PACKETS_PER_SEC}/s). Purging queued packets."); } catch { }
                        }
                    }
                }

                if (blackholeNow)
                {
                    // Purge already queued packets from this sender so you don't "recover for 20 seconds".
                    // Preserve allowlisted queued packets (chat, etc.) so they still arrive during blackhole.
                    PurgeSenderQueued(local, sid, preserveAllowlisted: true);
                    return false; // Also drop current one.
                }

                return true;
            }
            #endregion

            #region FloodGuard - Precise Purge Of LocalNetworkGamer._pendingData (No Heuristics)

            /// <summary>
            /// FloodGuard helper: Purge already-queued inbound packets so you don't "recover slowly" after a spam burst.
            ///
            /// Why this exists:
            /// - Dropping new packets helps, but a big backlog in LocalNetworkGamer._pendingData can still lag/freeze the game
            ///   while the main thread drains it.
            ///
            /// What it does:
            /// - Removes queued packets that belong to a specific sender (or clears everything as a last resort).
            /// - Calls Release() on removed packets when available so pooled buffers get returned (reduces GC / memory pressure).
            /// </summary>

            private static readonly FieldInfo  F_pendingData   =
                AccessTools.Field(typeof(LocalNetworkGamer), "_pendingData");

            // LocalNetworkGamer has: private class PendingDataPacket { public NetworkGamer Sender; public byte[] Data; public void Release(); ... }.
            private static readonly Type       T_pendingPacket =
                typeof(LocalNetworkGamer).GetNestedType("PendingDataPacket", BindingFlags.NonPublic);

            private static readonly FieldInfo  F_pktSender     =
                (T_pendingPacket != null) ? AccessTools.Field(T_pendingPacket, "Sender")   : null;

            private static readonly FieldInfo  F_pktData       =
                (T_pendingPacket != null) ? AccessTools.Field(T_pendingPacket, "Data")     : null;

            private static readonly MethodInfo M_pktRelease    =
                (T_pendingPacket != null) ? AccessTools.Method(T_pendingPacket, "Release") : null;

            /// <summary>
            /// Removes queued inbound packets belonging to a specific sender from LocalNetworkGamer._pendingData,
            /// calling Release() on each removed packet to return it to the pool (CMZ uses a free-list).
            ///
            /// If preserveAllowlisted=true, queued allowlisted packets are kept (best-effort).
            /// </summary>
            private static void PurgeSenderQueued(LocalNetworkGamer local, byte senderId, bool preserveAllowlisted)
            {
                if (local == null) return;

                // _pendingData is List<PendingDataPacket>, but we can treat it as IList safely.
                if (!(F_pendingData?.GetValue(local) is IList list) || list.Count == 0) return;

                // If these ever fail (different build), safest fallback is to clear all queued packets.
                // If we need to preserve allowlisted but can't reflect Data, also fall back to PurgeAll.
                if (F_pktSender == null || M_pktRelease == null || (preserveAllowlisted && F_pktData == null))
                {
                    PurgeAllQueued(local, list);
                    return;
                }

                lock (list) // LocalNetworkGamer.ReceiveData/AppendNewDataPacket locks on the list object.
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        object pkt = list[i];
                        if (pkt == null) { list.RemoveAt(i); continue; }

                        NetworkGamer sender = null;
                        try { sender = (NetworkGamer)F_pktSender.GetValue(pkt); } catch { }

                        if (sender != null && sender.Id == senderId)
                        {
                            if (preserveAllowlisted)
                            {
                                try
                                {
                                    if (F_pktData.GetValue(pkt) is byte[] bytes && IsAllowlistedPayload(bytes, 0, bytes.Length))
                                        continue; // Keep allowlisted packet queued.
                                }
                                catch { /* Best-effort. */ }
                            }

                            try { M_pktRelease.Invoke(pkt, null); } catch { /* Best-effort. */ }
                            list.RemoveAt(i);
                        }
                    }
                }
            }

            /// <summary>
            /// "Nuke it from orbit" option: Drop EVERYTHING queued and Release() packets.
            /// This is the most effective way to eliminate "recovery lag" from backlog.
            /// </summary>
            private static void PurgeAllQueued(LocalNetworkGamer local, IList list = null)
            {
                if (local == null) return;

                if (list == null)
                {
                    list = F_pendingData?.GetValue(local) as IList;
                }

                if (list == null || list.Count == 0) return;

                // If we can't reflect Release(), just Clear().
                bool canRelease = (M_pktRelease != null);

                lock (list)
                {
                    if (canRelease)
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            object pkt = list[i];
                            if (pkt == null) { list.RemoveAt(i); continue; }
                            try { M_pktRelease.Invoke(pkt, null); } catch { }
                            list.RemoveAt(i);
                        }
                    }
                    else
                    {
                        list.Clear();
                    }
                }
            }
            #endregion

            #region Throttle - Local Pickup Requests (Token Bucket, Config-Backed)

            /// <summary>
            /// Client-side throttle for pickup requests triggered by PickupEntity touch.
            ///
            /// Why this exists:
            /// - PickupEntity marks _pickedUp = true BEFORE the pickup request is sent/accepted.
            /// - If a pickup request is blocked without undoing that flag, the pickup can become stuck.
            ///
            /// Behavior:
            /// - Vanilla-range item IDs are token-bucket throttled.
            /// - Out-of-range / custom item IDs bypass the throttle entirely.
            /// - When throttled, the early _pickedUp flag is undone so the pickup can retry next frame.
            ///
            /// Implementation note:
            /// - We fully handle PlayerTouchedPickup inside this prefix and skip the original method.
            /// - This avoids relying on the original Harmony-wrapped method path.
            /// </summary>
            [HarmonyPatch(typeof(PickupManager), "PlayerTouchedPickup")]
            internal static class Patch_Throttle_PlayerTouchedPickup
            {
                #region State

                // Config-backed knobs (hot-reload friendly).
                private static int Burst    => PICKUP_TOUCH_BURST;
                private static int RefillMs => PICKUP_TOUCH_REFILL_MS;

                private static double _tokens;
                private static int    _lastTick;
                private static bool   _init;

                // PickupEntity private field: bool _pickedUp.
                private static readonly FieldInfo F_pickedUp =
                    AccessTools.Field(typeof(PickupEntity), "_pickedUp");

                /// <summary>
                /// Highest defined vanilla item ID from InventoryItemIDs.
                /// Anything above this is treated as non-vanilla and bypasses throttling.
                /// </summary>
                private static readonly int VANILLA_MAX_ITEM_ID = GetVanillaMaxItemId();

                #endregion

                #region Helpers

                private static int GetVanillaMaxItemId()
                {
                    int max = -1;

                    foreach (var value in Enum.GetValues(typeof(InventoryItemIDs)))
                    {
                        int id = Convert.ToInt32(value);
                        if (id > max)
                            max = id;
                    }

                    return max;
                }

                /// <summary>
                /// Returns the pickup's item ID, or -1 if unavailable.
                /// </summary>
                private static int GetPickupItemId(PickupEntity pickup)
                {
                    try
                    {
                        var id = pickup?.Item?.ItemClass?.ID;
                        return id.HasValue ? (int)id.Value : -1;
                    }
                    catch
                    {
                        return -1;
                    }
                }

                /// <summary>
                /// Returns true if the pickup should be throttled.
                /// Only vanilla-range item IDs are throttled.
                /// </summary>
                private static bool ShouldThrottlePickup(PickupEntity pickup)
                {
                    int id = GetPickupItemId(pickup);
                    return id >= 0 && id <= VANILLA_MAX_ITEM_ID;
                }

                /// <summary>
                /// Sends the pickup request using the local player gamer.
                /// Returns true if the send was attempted.
                /// </summary>
                private static bool TrySendPickupRequest(PickupEntity pickup)
                {
                    try
                    {
                        if (!(CastleMinerZGame.Instance?.LocalPlayer?.Gamer is LocalNetworkGamer local) || pickup == null)
                            return false;

                        RequestPickupMessage.Send(local, pickup.SpawnerID, pickup.PickupID);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                /// <summary>
                /// Undoes the early _pickedUp flag so the pickup can retry next frame.
                /// </summary>
                private static void ResetPickedUp(PickupEntity pickup)
                {
                    try { F_pickedUp?.SetValue(pickup, false); } catch { }
                }

                private static void EnsureInit()
                {
                    if (_init) return;
                    _init = true;

                    int burst = Burst;
                    if (burst < 1) burst = 1;

                    _tokens = burst;
                    _lastTick = Environment.TickCount;
                }

                /// <summary>
                /// Returns true if the token bucket allows a send this tick.
                /// </summary>
                private static bool ConsumeThrottleBudget()
                {
                    EnsureInit();

                    int burst    = Burst;
                    int refillMs = RefillMs;

                    if (burst < 1)    burst    = 1;
                    if (refillMs < 1) refillMs = 1;

                    // Refill token bucket.
                    int now = Environment.TickCount;
                    int dt  = unchecked(now - _lastTick);
                    if (dt < 0) dt = 0;
                    _lastTick = now;

                    if (dt > 0)
                    {
                        _tokens += (double)dt / refillMs;
                        if (_tokens > burst) _tokens = burst;
                    }
                    else
                    {
                        // Hot-reload safety: Clamp tokens if Burst was lowered.
                        if (_tokens > burst) _tokens = burst;
                    }

                    if (_tokens >= 1.0)
                    {
                        _tokens -= 1.0;
                        return true;
                    }

                    return false;
                }
                #endregion

                #region Prefix

                [HarmonyPrefix]
                private static bool Prefix(PickupEntity pickup)
                {
                    if (!PICKUP_THROTTLE_ENABLED)
                        return true; // Let vanilla PlayerTouchedPickup run untouched.

                    if (pickup == null)
                        return false; // Nothing to do; skip original.

                    // Non-vanilla / out-of-range items bypass the throttle completely.
                    if (!ShouldThrottlePickup(pickup))
                    {
                        if (!TrySendPickupRequest(pickup))
                            ResetPickedUp(pickup);

                        return false; // We handled it ourselves.
                    }

                    // Vanilla pickup: throttle via token bucket.
                    if (ConsumeThrottleBudget())
                    {
                        if (!TrySendPickupRequest(pickup))
                            ResetPickedUp(pickup);

                        return false; // We handled it ourselves.
                    }

                    // No budget this tick: undo the early pickup latch so it can retry next frame.
                    ResetPickedUp(pickup);
                    return false; // Skip original send this tick.
                }
                #endregion
            }
            #endregion
        }
        #endregion

        #region PlayerUpdateMessage: Reject Invalid Remote Transform Data (NaN / Infinity / Absurd Values)

        /// <summary>
        /// Validates inbound <see cref="PlayerUpdateMessage"/> state before it is applied to
        /// a remote <see cref="Player"/> instance.
        ///
        /// This prevents malformed or malicious player updates from writing invalid numeric
        /// values such as:
        /// - NaN
        /// - PositiveInfinity
        /// - NegativeInfinity
        /// - absurdly large finite coordinates / velocities
        ///
        /// The main goal is to stop remote clients from freezing or destabilizing the game
        /// by sending corrupt transform data that eventually reaches:
        /// - player.LocalPosition
        /// - player.PlayerPhysics.WorldVelocity
        /// - player.LocalRotation
        ///
        /// Behavior:
        /// - If the packet looks sane, the original Apply(...) runs normally.
        /// - If the packet is invalid, the update is dropped and the player's previous
        ///   valid state is preserved.
        /// </summary>
        /// <remarks>
        /// Why patch Apply(...):
        /// - This is the narrowest useful inbound choke point for PlayerUpdateMessage state.
        /// - It blocks the dangerous assignment before bad values touch live player state.
        /// - It is much less invasive than rewriting message receive logic.
        ///
        /// Notes:
        /// - This only validates inbound PlayerUpdateMessage application.
        /// - If you also want to prevent your own client/mod from ever sending invalid values,
        ///   add a second guard on PlayerUpdateMessage.Send(...).
        /// - The position / velocity limits below are intentionally generous and can be tuned
        ///   to match your world's real bounds.
        /// </remarks>
        [HarmonyPatch(typeof(PlayerUpdateMessage), nameof(PlayerUpdateMessage.Apply))]
        internal static class Patch_PlayerUpdateMessage_RejectInvalidTransforms
        {
            /// <summary>
            /// Very generous sanity caps to reject absurd finite values in addition to
            /// NaN / Infinity. Tune these if you want stricter world-bound enforcement.
            /// </summary>
            private const float MaxAbsPosition = int.MaxValue;
            private const float MaxAbsVelocity = 100000f;

            [HarmonyPrefix]
            private static bool Prefix(PlayerUpdateMessage __instance, Player player)
            {
                try
                {
                    if (__instance == null || player == null)
                        return false;

                    // Reject malformed / dangerous position data.
                    if (!IsSafeVector3(__instance.LocalPosition, MaxAbsPosition))
                    {
                        LogRejected(player, "LocalPosition", __instance.LocalPosition);
                        return false;
                    }

                    // Reject malformed / dangerous velocity data.
                    if (!IsSafeVector3(__instance.WorldVelocity, MaxAbsVelocity))
                    {
                        LogRejected(player, "WorldVelocity", __instance.WorldVelocity);
                        return false;
                    }

                    // Rotation from vanilla RecieveData() should already be finite, but
                    // validate it anyway for defense-in-depth.
                    if (!IsSafeQuaternion(__instance.LocalRotation))
                    {
                        LogRejected(player, "LocalRotation", __instance.LocalRotation);
                        return false;
                    }

                    // Optional: validate movement too, even though vanilla byte unpacking
                    // should keep it finite.
                    if (!IsSafeVector2(__instance.Movement, 2f))
                    {
                        LogRejected(player, "Movement", __instance.Movement);
                        return false;
                    }

                    return true;
                }
                catch
                {
                    // If validation itself fails for any reason, safest choice is to
                    // drop the update rather than risk applying corrupted state.
                    return false;
                }
            }

            /// <summary>
            /// Returns true only if all components are finite and within a generous absolute limit.
            /// </summary>
            private static bool IsSafeVector3(Vector3 v, float maxAbs)
            {
                return IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z) &&
                       Math.Abs(v.X) <= maxAbs &&
                       Math.Abs(v.Y) <= maxAbs &&
                       Math.Abs(v.Z) <= maxAbs;
            }

            /// <summary>
            /// Returns true only if all components are finite and within a generous absolute limit.
            /// </summary>
            private static bool IsSafeVector2(Vector2 v, float maxAbs)
            {
                return IsFinite(v.X) && IsFinite(v.Y) &&
                       Math.Abs(v.X) <= maxAbs &&
                       Math.Abs(v.Y) <= maxAbs;
            }

            /// <summary>
            /// Returns true only if the quaternion is finite and has a sane magnitude.
            /// </summary>
            private static bool IsSafeQuaternion(Quaternion q)
            {
                if (!IsFinite(q.X) || !IsFinite(q.Y) || !IsFinite(q.Z) || !IsFinite(q.W))
                    return false;

                float lenSq = q.LengthSquared();

                if (!IsFinite(lenSq))
                    return false;

                // Very loose sanity window. Vanilla quaternions should normally be near unit length.
                if (lenSq <= 0.0001f || lenSq > 4f)
                    return false;

                return true;
            }

            /// <summary>
            /// .NET Framework-friendly finite check.
            /// </summary>
            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            /// <summary>
            /// Minimal rejection logging hook. Use preferred logger / offender tracking.
            /// </summary>
            private static void LogRejected(Player player, string fieldName, object value)
            {
                try
                {
                    string name = player?.Gamer?.Gamertag ?? "<unknown>";

                    // Example:
                    // SendFeedback($"[NET][DROP] Rejected PlayerUpdateMessage from {name}: invalid {fieldName} = {value}.");
                }
                catch
                {
                }
            }
        }
        #endregion

        #endregion

        #region Defensive Networking Hardening (Dragon Host Migration Guardrails)

        /// <summary>
        /// Hardens dragon host-migration on both the outgoing handoff path and the incoming accept path.
        ///
        /// Goals:
        /// - Prevent NullReferenceException during dragon migration send/cleanup.
        /// - Reject obviously malformed / corrupt migration payloads before creating a new local dragon owner.
        /// - Reduce the chance of migration-state desync causing local crashes or invalid ownership transitions.
        ///
        /// Scope:
        /// - EnemyManager.MigrateDragon(...)
        /// - EnemyManager.HandleMigrateDragonMessage(...)
        ///
        /// Design:
        /// - Prefixes fully replace the original methods so the unsafe vanilla logic never runs.
        /// - Uses conservative validation (finite checks, enum checks, basic sanity bounds).
        /// - Fails closed on bad migration state rather than trying to "fix" unsafe payloads.
        /// </summary>
        [HarmonyPatch(typeof(EnemyManager))]
        internal static class Patch_EnemyManager_DragonMigrationHardening
        {
            #region Private Field Access

            /// <summary>
            /// Private field accessor for EnemyManager._dragon (locally controlled dragon entity).
            /// </summary>
            private static readonly AccessTools.FieldRef<EnemyManager, DragonEntity> _dragonRef =
                AccessTools.FieldRefAccess<EnemyManager, DragonEntity>("_dragon");

            /// <summary>
            /// Private field accessor for EnemyManager._dragonClient (replicated / client-visible dragon entity).
            /// </summary>
            private static readonly AccessTools.FieldRef<EnemyManager, DragonClientEntity> _dragonClientRef =
                AccessTools.FieldRefAccess<EnemyManager, DragonClientEntity>("_dragonClient");

            #endregion

            #region Outgoing Migration Hardening

            /// <summary>
            /// NOTES / INTENT
            /// --------------------------------------------------------------------------------
            /// This patch replaces EnemyManager.MigrateDragon(...) with a guarded version.
            ///
            /// Why:
            /// - Vanilla assumes target, target.Gamer, miginfo, and EnemyManager._dragon are always valid.
            /// - In real play / modded play / desync conditions, any of those can be null or stale.
            /// - A bad migration attempt should be cancelled safely instead of crashing or silently deleting ownership.
            ///
            /// Behavior:
            /// - Validates target, target.Gamer, migration info, and local player/gamer state.
            /// - Rejects invalid migration payloads before sending.
            /// - Cancels pending migration if the handoff state is unsafe.
            /// - Only removes local _dragon after a validated send path.
            ///
            /// Safety:
            /// - Returns false in all cases because this prefix fully replaces the original method.
            /// - Never throws intentionally; exceptions are logged and swallowed.
            /// --------------------------------------------------------------------------------
            /// </summary>
            [HarmonyPatch(nameof(EnemyManager.MigrateDragon))]
            [HarmonyPrefix]
            private static bool Prefix_MigrateDragon(EnemyManager __instance, Player target, DragonHostMigrationInfo miginfo)
            {
                try
                {
                    ref DragonEntity dragon = ref _dragonRef(__instance);

                    var game        = CastleMinerZGame.Instance;
                    var localPlayer = game?.LocalPlayer;

                    if (dragon == null)
                    {
                        // Log($"[DM][Send] Blocked: EnemyManager._dragon was already null.");
                        return false;
                    }

                    if (target == null || target.Gamer == null)
                    {
                        // Log($"[DM][Send] Blocked: Target or target.Gamer was null.");
                        CancelPendingMigration(dragon);
                        return false;
                    }

                    if (miginfo == null)
                    {
                        // Log($"[DM][Send] Blocked: Migration info was null.");
                        CancelPendingMigration(dragon);
                        return false;
                    }

                    if (!IsMigrationInfoValid(miginfo, out string invalidReason))
                    {
                        // Log($"[DM][Send] Blocked: Invalid migration info. Reason: {invalidReason}.");
                        CancelPendingMigration(dragon);
                        return false;
                    }

                    if (localPlayer == null || !localPlayer.ValidGamer || !(localPlayer?.Gamer is LocalNetworkGamer localGamer))
                    {
                        // Log("[DM][Send] Blocked: Local player/local gamer was not valid.");
                        CancelPendingMigration(dragon);
                        return false;
                    }

                    MigrateDragonMessage.Send(localGamer, target.Gamer.Id, miginfo);

                    // Only clean up local ownership after a validated send attempt.
                    dragon.RemoveFromParent();
                    dragon = null;

                    return false;
                }
                catch (Exception ex)
                {
                    Log($"[DM][Send] Exception while migrating dragon: {ex}.");
                    return false;
                }
            }
            #endregion

            #region Incoming Migration Hardening

            /// <summary>
            /// NOTES / INTENT
            /// --------------------------------------------------------------------------------
            /// This patch replaces EnemyManager.HandleMigrateDragonMessage(...) with a guarded receiver.
            ///
            /// Why:
            /// - Vanilla trusts the incoming migration message too much.
            /// - A malformed or malicious migration payload can cause bad local ownership state,
            ///   invalid dragon creation, or downstream crashes.
            ///
            /// Behavior:
            /// - Only considers migration messages targeted at the local player.
            /// - Requires an existing dragon client proxy (same expectation as vanilla).
            /// - Rejects null sender, null migration info, invalid enums, NaN/Infinity, absurd values,
            ///   and obvious type mismatches.
            /// - Avoids spawning a new local dragon if scene state is unavailable.
            ///
            /// Important note:
            /// - Some failure paths below currently log but do not early-return because your existing
            ///   draft had those 'return false;' lines commented out.
            /// - That means those paths are currently "warn only" instead of "hard reject."
            /// --------------------------------------------------------------------------------
            /// </summary>
            [HarmonyPatch("HandleMigrateDragonMessage")]
            [HarmonyPrefix]
            private static bool Prefix_HandleMigrateDragonMessage(EnemyManager __instance, MigrateDragonMessage msg)
            {
                try
                {
                    ref DragonEntity dragon             = ref _dragonRef(__instance);
                    ref DragonClientEntity dragonClient = ref _dragonClientRef(__instance);

                    var game        = CastleMinerZGame.Instance;
                    var localPlayer = game?.LocalPlayer;

                    if (msg == null)
                    {
                        // Log("[DM][Recv] Blocked: Message was null.");
                        return false;
                    }

                    // Preserve vanilla behavior: If we do not currently have a dragon client proxy,
                    // there is nothing sensible to migrate into local ownership.
                    if (dragonClient == null)
                    {
                        return false;
                    }

                    if (localPlayer == null || localPlayer.Gamer == null)
                    {
                        // Log("[DM][Recv] Blocked: Local player or local gamer was null.");
                        return false;
                    }

                    if (!game.IsLocalPlayerId(msg.TargetID))
                    {
                        // Not for us.
                        return false;
                    }

                    if (msg.Sender == null)
                    {
                        // Log("[DM][Recv] Blocked: Sender was null.");
                        return false;
                    }

                    if (msg.Sender.Id == localPlayer.Gamer.Id)
                    {
                        // Log("[DM][Recv] Blocked: Received self-targeted migration from local sender.");
                        // return false;
                    }

                    if (dragon != null)
                    {
                        // Log("[DM][Recv] Blocked: Local _dragon already exists.");
                        return false;
                    }

                    DragonHostMigrationInfo miginfo = msg.MigrationInfo;
                    if (miginfo == null)
                    {
                        // Log("[DM][Recv] Blocked: Migration info was null.");
                        return false;
                    }

                    if (!IsMigrationInfoValid(miginfo, out string invalidReason))
                    {
                        // Log($"[DM][Recv] Blocked: Invalid migration info. Reason: {invalidReason}.");
                        return false;
                    }

                    // Optional extra consistency check:
                    // the incoming migrated owner should match the type of the dragon proxy we already know about.
                    if (dragonClient.EType == null || dragonClient.EType.EType != miginfo.EType)
                    {
                        // Log("[DM][Recv] Blocked: Dragon type mismatch between client proxy and migration info.");
                        return false;
                    }

                    Scene scene = EnemyManager.Instance?.Scene;
                    if (scene == null || scene.Children == null)
                    {
                        // Log("[DM][Recv] Blocked: Scene or scene.Children was null.");
                        return false;
                    }

                    dragon = new DragonEntity(miginfo.EType, miginfo.ForBiome, miginfo);
                    scene.Children.Add(dragon);

                    return false;
                }
                catch (Exception ex)
                {
                    Log($"[DM][Recv] Exception while handling migration message: {ex}.");
                    return false;
                }
            }
            #endregion

            #region Validation Helpers

            /// <summary>
            /// Performs conservative validation on the migration payload.
            /// </summary>
            /// <remarks>
            /// Intent:
            /// - Reject malformed / nonsense / exploit-style state before it can become live local dragon state.
            ///
            /// This is NOT intended to prove true sender authority or perfect game correctness.
            /// It is a defensive sanity filter only.
            ///
            /// Current checks include:
            /// - null checks,
            /// - dragon type / animation enum validation,
            /// - NaN / Infinity rejection,
            /// - negative fireball index rejection,
            /// - loose velocity / rotation / world-position sanity caps.
            /// </remarks>
            private static bool IsMigrationInfoValid(DragonHostMigrationInfo miginfo, out string reason)
            {
                if (miginfo == null)
                {
                    reason = "MigrationInfo == null";
                    return false;
                }

                if (!IsValidDragonType(miginfo.EType))
                {
                    reason = $"Invalid dragon type enum: {miginfo.EType}.";
                    return false;
                }

                if (!Enum.IsDefined(typeof(DragonAnimEnum), miginfo.Animation))
                {
                    reason = $"Invalid dragon animation enum: {miginfo.Animation}.";
                    return false;
                }

                if (!IsFinite(miginfo.NextDragonTime) ||
                    !IsFinite(miginfo.Roll)           ||
                    !IsFinite(miginfo.TargetRoll)     ||
                    !IsFinite(miginfo.Pitch)          ||
                    !IsFinite(miginfo.TargetPitch)    ||
                    !IsFinite(miginfo.Yaw)            ||
                    !IsFinite(miginfo.TargetYaw)      ||
                    !IsFinite(miginfo.Velocity)       ||
                    !IsFinite(miginfo.TargetVelocity) ||
                    !IsFinite(miginfo.DefaultHeading) ||
                    !IsFinite(miginfo.FlapDebt)       ||
                    !IsFinite(miginfo.NextUpdateTime))
                {
                    reason = "One or more float fields were NaN/Infinity.";
                    return false;
                }

                if (!IsFinite(miginfo.Position) || !IsFinite(miginfo.Target))
                {
                    reason = "Position or Target contained NaN/Infinity.";
                    return false;
                }

                if (miginfo.NextFireballIndex < 0)
                {
                    reason = "NextFireballIndex was negative.";
                    return false;
                }

                // Conservative sanity limits to reject obviously corrupt payloads.
                if (Math.Abs(miginfo.Velocity) > 500f || Math.Abs(miginfo.TargetVelocity) > 500f)
                {
                    reason = "Velocity/TargetVelocity exceeded sanity bounds.";
                    return false;
                }

                if (Math.Abs(miginfo.Roll)        > 100f  ||
                    Math.Abs(miginfo.TargetRoll)  > 100f  ||
                    Math.Abs(miginfo.Pitch)       > 100f  ||
                    Math.Abs(miginfo.TargetPitch) > 100f  ||
                    Math.Abs(miginfo.Yaw)         > 1000f ||
                    Math.Abs(miginfo.TargetYaw)   > 1000f)
                {
                    reason = "Rotation values exceeded sanity bounds.";
                    return false;
                }

                if (!IsReasonableWorldVector(miginfo.Position) || !IsReasonableWorldVector(miginfo.Target))
                {
                    reason = "Position or Target exceeded world sanity bounds.";
                    return false;
                }

                reason = null;
                return true;
            }

            /// <summary>
            /// Returns true only for defined DragonTypeEnum values that represent real dragon types.
            /// </summary>
            private static bool IsValidDragonType(DragonTypeEnum type)
            {
                return Enum.IsDefined(typeof(DragonTypeEnum), type) &&
                       type != DragonTypeEnum.COUNT;
            }

            /// <summary>
            /// .NET Framework-friendly finite float check.
            /// </summary>
            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            /// <summary>
            /// Returns true only if all Vector3 components are finite.
            /// </summary>
            private static bool IsFinite(Vector3 value)
            {
                return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
            }

            /// <summary>
            /// Returns true only if the vector is within a very loose world-space sanity bound.
            /// </summary>
            /// <remarks>
            /// Notes:
            /// - Very loose bounds on purpose.
            /// - The goal is only to reject absurd / exploit / corrupt coordinates,
            ///   not normal far-world dragon state.
            /// </remarks>
            private static bool IsReasonableWorldVector(Vector3 value)
            {
                const float limit = 100000f;
                return Math.Abs(value.X) <= limit &&
                       Math.Abs(value.Y) <= limit &&
                       Math.Abs(value.Z) <= limit;
            }
            #endregion

            #region Utility Helpers

            /// <summary>
            /// Cancels a pending local migration request so the dragon does not keep retrying
            /// every frame after a bad target/payload state.
            /// </summary>
            /// <remarks>
            /// This is a local safety reset only.
            /// It does not attempt to rebuild or repair broader dragon ownership state.
            /// </remarks>
            private static void CancelPendingMigration(DragonEntity dragon)
            {
                if (dragon == null)
                    return;

                dragon.MigrateDragon   = false;
                dragon.MigrateDragonTo = null;
            }
            #endregion
        }
        #endregion

        #region Defensive Networking Hardening - Steam Host GID Repair / Compatibility Layer

        /*
        NOTES / PURPOSE
        - This block hardens the Steam networking join flow against malformed or modded hosts that do not
          follow vanilla CastleMiner Z assumptions about the host using gamer ID 0.
        - Vanilla logic assumes:
            1) the host appears as gamer ID 0 during client join,
            2) direct-to-host packets can safely target the host's visible gamer ID,
            3) fallback gameplay/server lookups can rely on ID 0 or TerrainServerID resolving normally.
        - When a hostile/modded host breaks those assumptions, clients can get stuck in:
            - infinite loading loops,
            - dropped bootstrap/world-info messages,
            - null host references during broadcast/direct-send paths.
        - These patches repair those assumptions at the compatibility layer instead of changing core game logic.
        */

        #region State / Session Tracking

        /// <summary>
        /// Remembers the expected host identity for the current Steam join attempt.
        /// Used to repair the host mapping if the remote host advertises a non-vanilla gamer ID.
        /// </summary>
        private sealed class PendingSteamHostJoinInfo
        {
            public ulong  HostSteamId;
            public string HostGamertag;
        }

        /// <summary>
        /// Per-provider storage of the expected host during the join handshake.
        /// The entry is populated in StartClient and consumed during client system-message handling.
        /// </summary>
        private static readonly ConditionalWeakTable<SteamNetworkSessionProvider, PendingSteamHostJoinInfo>
            _pendingSteamHostJoinInfo = new ConditionalWeakTable<SteamNetworkSessionProvider, PendingSteamHostJoinInfo>();

        #endregion

        #region Shared Reflection Handles / Helpers

        /// <summary>
        /// Backing field for the Steam API instance used by the provider.
        /// Required so we can build and send rewritten packets in the direct-send compatibility path.
        /// </summary>
        private static readonly FieldInfo SteamApiField =
            AccessTools.Field(typeof(SteamNetworkSessionProvider), "_steamAPI");

        /// <summary>
        /// Backing field for the provider's local player gamer ID.
        /// Needed when manually constructing direct packets so the sender ID remains correct.
        /// </summary>
        private static readonly FieldInfo LocalPlayerGidField =
            AccessTools.Field(typeof(NetworkSessionProvider), "_localPlayerGID");

        /// <summary>
        /// Maps XNA-style SendDataOptions to the underlying Steam/Lidgren delivery method.
        /// Used by the rewritten direct-send host compatibility path.
        /// </summary>
        private static NetDeliveryMethod GetSteamDeliveryMethod(SendDataOptions options)
        {
            switch (options)
            {
                case SendDataOptions.None:
                    return NetDeliveryMethod.Unreliable;

                case SendDataOptions.Reliable:
                    return NetDeliveryMethod.ReliableUnordered;

                case SendDataOptions.InOrder:
                    return NetDeliveryMethod.UnreliableSequenced;

                case SendDataOptions.ReliableInOrder:
                    return NetDeliveryMethod.ReliableOrdered;

                default:
                    return NetDeliveryMethod.Unknown;
            }
        }

        /// <summary>
        /// Returns true when a client-side direct send to the current host must be rewritten to wire ID 0.
        /// This is only needed for malformed hosts whose visible host gamer object is non-zero, but whose
        /// receiving logic still expects direct packets addressed to host ID 0.
        /// </summary>
        private static bool ShouldRewriteHostDirectSend(SteamNetworkSessionProvider provider, NetworkGamer recipient)
        {
            if (provider == null || recipient == null)
                return false;

            // Host itself does not need this.
            if (provider.IsHost)
                return false;

            var host = provider.Host;
            if (host == null)
                return false;

            // Only rewrite direct sends to the current host.
            if (!ReferenceEquals(recipient, host))
                return false;

            // If visible host ID is already 0, vanilla is fine.
            if (recipient.Id == 0)
                return false;

            // Must have a real Steam connection.
            if (recipient.AlternateAddress == 0UL)
                return false;

            return true;
        }
        #endregion

        #region Join Handshake - Remember Expected Host

        /// <summary>
        /// Captures the expected host SteamID / gamertag before the Steam client join completes.
        /// Vanilla later assumes the host peer arrives as gamer ID 0; this cached identity lets us
        /// recover the real host even if that assumption is violated.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), nameof(SteamNetworkSessionProvider.StartClient))]
        internal static class SteamNetworkSessionProvider_StartClient_RememberExpectedHost
        {
            static void Postfix(SteamNetworkSessionProvider __instance, NetworkSessionStaticProvider.BeginJoinSessionState sqs)
            {
                if (__instance == null || sqs?.AvailableSession == null)
                    return;

                try { _pendingSteamHostJoinInfo.Remove(__instance); }
                catch { }

                _pendingSteamHostJoinInfo.Add(__instance, new PendingSteamHostJoinInfo
                {
                    HostSteamId  = sqs.AvailableSession.HostSteamID,
                    HostGamertag = sqs.AvailableSession.HostGamertag ?? string.Empty
                });

                // Log($"[SteamHostIdGuard] Cached expected host: '{sqs.AvailableSession.HostGamertag}' ({sqs.AvailableSession.HostSteamID}).");
            }
        }
        #endregion

        #region Join Handshake - Repair Broken Host Mapping

        /// <summary>
        /// Repairs malformed Steam join responses where the actual host was not advertised as gamer ID 0.
        /// Vanilla creates the host as a proxy in that case, leaving _host null and causing later client
        /// broadcast / direct-send paths to fail or dereference a bad host.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), "HandleClientSystemMessages")]
        internal static class SteamNetworkSessionProvider_HandleClientSystemMessages_HostIdRepair
        {
            /// <summary>
            /// Backing field for the session provider's host gamer reference.
            /// </summary>
            private static readonly FieldInfo HostField =
                AccessTools.Field(typeof(NetworkSessionProvider), "_host");

            /// <summary>
            /// Backing field for a gamer's alternate network address (Steam ID in this case).
            /// </summary>
            private static readonly FieldInfo AlternateAddressField =
                AccessTools.Field(typeof(NetworkGamer), "_alternateAddress");

            /// <summary>
            /// Backing field for the gamer's host-status flag.
            /// </summary>
            private static readonly FieldInfo IsHostField =
                AccessTools.Field(typeof(NetworkGamer), "_isHost");

            static void Postfix(SteamNetworkSessionProvider __instance, SteamNetBuffer msg)
            {
                try
                {
                    if (__instance == null || __instance.IsHost)
                        return;

                    // Vanilla already worked; nothing to repair.
                    if (__instance.Host != null)
                        return;

                    if (!_pendingSteamHostJoinInfo.TryGetValue(__instance, out var expected) || expected == null)
                        return;

                    var remotes = __instance.RemoteGamers;
                    if (remotes == null || remotes.Count == 0)
                        return;

                    NetworkGamer hostProxy = null;

                    // First choice:
                    // Match the gamertag advertised by the session browser / available-session record.
                    if (!string.IsNullOrWhiteSpace(expected.HostGamertag))
                    {
                        for (int i = 0; i < remotes.Count; i++)
                        {
                            var ng = remotes[i];
                            if (ng == null)
                                continue;

                            if (string.Equals(ng.Gamertag, expected.HostGamertag, StringComparison.OrdinalIgnoreCase))
                            {
                                hostProxy = ng;
                                break;
                            }
                        }
                    }

                    // Fallback:
                    // If only one remote exists, it must be the host.
                    if (hostProxy == null && remotes.Count == 1)
                        hostProxy = remotes[0];

                    if (hostProxy == null)
                    {
                        Log($"[SteamHostIdGuard] Could not identify malformed host peer. Sender={msg?.SenderId ?? 0UL} Remotes={remotes.Count}.");
                        return;
                    }

                    // If the host came in as a proxy because their visible GID was not 0,
                    // convert that proxy into the real host in-place.
                    if (hostProxy.NetProxyObject || hostProxy.AlternateAddress == 0UL)
                    {
                        ulong hostSteamId = expected.HostSteamId != 0UL
                            ? expected.HostSteamId
                            : (msg?.SenderId ?? 0UL);

                        hostProxy.NetProxyObject      = false;
                        hostProxy.NetConnectionObject = null;

                        AlternateAddressField?.SetValue(hostProxy, hostSteamId);
                        IsHostField?.SetValue(hostProxy, true);
                        HostField?.SetValue(__instance, hostProxy);

                        Log($"[SteamHostIdGuard] Repaired host mapping. Host '{hostProxy.Gamertag}' used GID {hostProxy.Id} instead of 0; rebound to SteamID {hostSteamId}.");
                    }

                    try { _pendingSteamHostJoinInfo.Remove(__instance); }
                    catch { }
                }
                catch (Exception ex)
                {
                    LogException(ex, "SteamHostIdGuard");
                }
            }
        }
        #endregion

        #region Broadcast Safety Nets

        /// <summary>
        /// Final safety net for broadcast calls using the byte[] overload.
        /// If a malformed/broken session still leaves the client without a valid host mapping,
        /// drop the broadcast instead of entering an exception spam / infinite loading loop.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), nameof(SteamNetworkSessionProvider.BroadcastRemoteData),
            new[] { typeof(byte[]), typeof(SendDataOptions) })]
        internal static class SteamNetworkSessionProvider_BroadcastRemoteData_SafeGuard_A
        {
            static bool Prefix(SteamNetworkSessionProvider __instance)
            {
                var host = __instance?.Host;
                if (host != null && host.AlternateAddress != 0UL)
                    return true;

                Log("[SteamHostIdGuard] Dropped client broadcast (byte[]) because host mapping is invalid.");
                return false;
            }
        }

        /// <summary>
        /// Final safety net for broadcast calls using the offset/length overload.
        /// Mirrors the logic above so both broadcast entry points are protected.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), nameof(SteamNetworkSessionProvider.BroadcastRemoteData),
            new[] { typeof(byte[]), typeof(int), typeof(int), typeof(SendDataOptions) })]
        internal static class SteamNetworkSessionProvider_BroadcastRemoteData_SafeGuard_B
        {
            static bool Prefix(SteamNetworkSessionProvider __instance)
            {
                var host = __instance?.Host;
                if (host != null && host.AlternateAddress != 0UL)
                    return true;

                Log("[SteamHostIdGuard] Dropped client broadcast (byte[], offset, length) because host mapping is invalid.");
                return false;
            }
        }
        #endregion

        #region Direct Send Compatibility - Force Wire Recipient ID 0 For Broken Hosts

        /// <summary>
        /// Rewrites direct sends to the current host when the visible host object uses a non-zero GID,
        /// but the malformed remote still expects incoming direct packets addressed to wire recipient ID 0.
        /// This overload handles the basic byte[] send path.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), nameof(SteamNetworkSessionProvider.SendRemoteData),
            new[] { typeof(byte[]), typeof(SendDataOptions), typeof(NetworkGamer) })]
        internal static class SteamNetworkSessionProvider_SendRemoteData_HostWireIdFix_A
        {
            static bool Prefix(
                SteamNetworkSessionProvider __instance,
                byte[] data,
                SendDataOptions options,
                NetworkGamer recipient)
            {
                if (!ShouldRewriteHostDirectSend(__instance, recipient))
                    return true;

                try
                {
                    if (!(SteamApiField.GetValue(__instance) is SteamWorks steamApi))
                        return true;

                    ulong netConnection = recipient.AlternateAddress;
                    if (netConnection == 0UL)
                        return true;

                    byte localPlayerGid = (byte)LocalPlayerGidField.GetValue(__instance);

                    SteamNetBuffer msg = steamApi.AllocSteamNetBuffer();
                    NetDeliveryMethod flags = GetSteamDeliveryMethod(options);

                    // Direct packet format on channel 0:
                    // [recipientId][senderId][payload...]
                    //
                    // Force recipientId = 0 because the malformed host still expects
                    // its local host gamer on wire ID 0, even though the repaired
                    // host object is visible client-side as a non-zero GID.
                    msg.Write((byte)0);
                    msg.Write(localPlayerGid);
                    msg.WriteArray(data);

                    steamApi.SendPacket(msg, netConnection, flags, 0);

                    return false;
                }
                catch (Exception ex)
                {
                    LogException(ex, "SteamHostIdGuard.SendRemoteData.HostWireIdFix_A");
                    return true;
                }
            }
        }

        /// <summary>
        /// Same compatibility rewrite as the byte[] overload above, but for the offset/length send path.
        /// Keeps both direct-send overloads behaviorally aligned.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), nameof(SteamNetworkSessionProvider.SendRemoteData),
            new[] { typeof(byte[]), typeof(int), typeof(int), typeof(SendDataOptions), typeof(NetworkGamer) })]
        internal static class SteamNetworkSessionProvider_SendRemoteData_HostWireIdFix_B
        {
            static bool Prefix(
                SteamNetworkSessionProvider __instance,
                byte[] data,
                int offset,
                int length,
                SendDataOptions options,
                NetworkGamer recipient)
            {
                if (!ShouldRewriteHostDirectSend(__instance, recipient))
                    return true;

                try
                {
                    if (!(SteamApiField.GetValue(__instance) is SteamWorks steamApi))
                        return true;

                    ulong netConnection = recipient.AlternateAddress;
                    if (netConnection == 0UL)
                        return true;

                    byte localPlayerGid = (byte)LocalPlayerGidField.GetValue(__instance);

                    SteamNetBuffer msg = steamApi.AllocSteamNetBuffer();
                    NetDeliveryMethod flags = GetSteamDeliveryMethod(options);

                    msg.Write((byte)0);
                    msg.Write(localPlayerGid);
                    msg.WriteArray(data, offset, length);

                    steamApi.SendPacket(msg, netConnection, flags, 0);

                    return false;
                }
                catch (Exception ex)
                {
                    LogException(ex, "SteamHostIdGuard.SendRemoteData.HostWireIdFix_B");
                    return true;
                }
            }
        }
        #endregion

        #region Legacy Host ID 0 Compatibility

        /// <summary>
        /// Some malformed/modded hosts change their visible gamer ID away from 0,
        /// but still send packets stamped internally with sender ID 0.
        ///
        /// On the client, that causes incoming host packets to be dropped because
        /// FindGamerById(0) returns null even though CurrentNetworkSession.Host has
        /// already been repaired to the real host object.
        ///
        /// This patch aliases legacy ID 0 to the current session host whenever:
        /// - the original lookup for ID 0 failed,
        /// - the session already has a known host,
        /// - and that repaired host's real visible ID is not 0.
        /// </summary>
        [HarmonyPatch(typeof(NetworkSessionProvider), nameof(NetworkSessionProvider.FindGamerById))]
        internal static class NetworkSessionProvider_FindGamerById_HostZeroCompat
        {
            static void Postfix(NetworkSessionProvider __instance, byte gamerId, ref NetworkGamer __result)
            {
                // Vanilla lookup already worked.
                if (__result != null)
                    return;

                // We only alias the legacy "host == 0" case.
                if (gamerId != 0 || __instance == null)
                    return;

                var host = __instance.Host;
                if (host == null || host.HasLeftSession)
                    return;

                // If the host is already actually ID 0, nothing to fix.
                if (host.Id == 0)
                    return;

                __result = host;
            }
        }
        #endregion

        #region Gameplay Fallback - Resolve Host When Terrain / Server IDs Are Stale

        /// <summary>
        /// Gameplay code often routes server/terrain traffic through TerrainServerID.
        /// During malformed joins, that ID can remain 0 or temporarily stale even though the
        /// network host object has already been repaired.
        ///
        /// This patch falls back to CurrentNetworkSession.Host when a direct ID lookup fails,
        /// keeping bootstrap traffic like world-info, terrain-server, and startup messages
        /// from going nowhere.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), nameof(CastleMinerZGame.GetGamerFromID))]
        internal static class CastleMinerZGame_GetGamerFromID_HostFallback
        {
            static void Postfix(CastleMinerZGame __instance, byte id, ref NetworkGamer __result)
            {
                if (__result != null || __instance?.CurrentNetworkSession == null)
                    return;

                var host = __instance.CurrentNetworkSession.Host;
                if (host == null || host.HasLeftSession)
                    return;

                if (id == 0 || id == __instance.TerrainServerID)
                {
                    __result = host;
                }
            }
        }
        #endregion

        #endregion

        #region Gamertag Integrity (Anti-Spoof / Anti-Impersonation)

        /// <summary>
        /// =============================================================================
        /// Gamertag Integrity (Anti-Spoof / Anti-Impersonation)
        /// =============================================================================
        /// Goal:
        ///  - Keep player names + chat readable (strip control/newline spam).
        ///  - Prevent simple "Name: msg" spoofing via BroadcastTextMessage.
        ///
        /// Notes:
        ///  - GamertagSanitizerConfig.Enabled is the master kill-switch for sanitization.
        ///  - ImpersonationConfig.Enabled is separate so we can keep spoof-protection on/off
        ///    independently from general chat/name sanitizing.
        ///  - The BroadcastTextMessage path currently sanitizes the raw line first to make
        ///    spoof parsing stable (prevents "\n\n\n" tricks from defeating parsing).
        /// =============================================================================
        /// </summary>

        #region Impersonation Guard - BroadcastTextMessage "Name: msg" Spoof Detection

        /// <summary>
        /// Detects and rewrites spoof attempts where a sender manually includes a leading "Name: "
        /// prefix that matches another player's displayed name.
        /// </summary>
        /// <remarks>
        /// Detection strategy:
        ///  - Parse chat as "ClaimedName: msg"
        ///  - Resolve ClaimedName to a real gamer by comparing against the same visible/sanitized name
        ///  - If sender != resolved target, treat as impersonation
        ///
        /// Response strategy:
        ///  - Rewrite the incoming line locally so it cannot appear as "bob: ..."
        ///  - Optionally broadcast a warning/quote (or clear-chat spam) based on config
        ///
        /// Special case:
        ///  - For impersonation warnings only, sender name is shown with escaped control tokens
        ///    (ex: "\n\n\n") so "blank name" spam is still visible in the warning output.
        /// </remarks>
        internal static class ImpersonationGuard
        {
            /// <summary>
            /// Parses "Name: message" (allows whitespace around ':').
            /// Returns false if the format is not recognized or the "name" portion is unreasonable.
            /// </summary>
            internal static bool TryParseClaim(string line, out string claimedName, out string msg)
            {
                claimedName = null;
                msg = null;

                if (string.IsNullOrEmpty(line))
                    return false;

                int colon = line.IndexOf(':');
                if (colon <= 0)
                    return false;

                // Name portion must be "reasonable" (prevents most false positives).
                // (You can tune this if needed.)
                if (colon > GamertagSanitizerConfig.MaxNameLen + 4)
                    return false;

                string left = line.Substring(0, colon).Trim();
                if (left.Length == 0)
                    return false;

                string right = (colon + 1 < line.Length) ? line.Substring(colon + 1) : string.Empty;
                right = right.TrimStart();

                claimedName = left;
                msg = right;
                return true;
            }

            /// <summary>
            /// Resolves a displayed name back to a NetworkGamer by comparing against the same
            /// "shown" form used elsewhere (GamerTagFixer.Sanitize(...)).
            /// </summary>
            internal static NetworkGamer FindTargetByDisplayedName(
                NetworkSession sess,
                string claimedName)
            {
                if (sess == null || string.IsNullOrEmpty(claimedName))
                    return null;

                var all = sess.AllGamers;
                for (int i = 0; i < all.Count; i++)
                {
                    var g = all[i];
                    if (g == null || g.HasLeftSession)
                        continue;

                    // Compare against the SAME "visible" form you use everywhere
                    // (includes "[id]" fallback if their name is blank/spam).
                    string shown = GamerTagFixer.Sanitize(g, g.Gamertag ?? string.Empty);

                    if (string.Equals(shown, claimedName, StringComparison.OrdinalIgnoreCase))
                        return g;
                }

                return null;
            }

            /// <summary>
            /// Returns the sender's normal safe displayed name, clamped for chat.
            /// </summary>
            internal static string GetDisplayedName(NetworkGamer g)
            {
                if (g == null)
                    return "?";

                string shown = GamerTagFixer.Sanitize(g, g.Gamertag ?? string.Empty);
                shown = ChatNameClamp.ClampWithEllipsis(shown, ChatNameClamp.MaxNameChars);
                return shown;
            }

            /// <summary>
            /// Builds a local-only rewrite for cases where the sender's actual session name
            /// and the chat-prefix name do not match, but the claimed name is not another real player.
            /// Example:
            ///   sender display = "Player1"
            ///   incoming line  = "Player3: hello"
            /// becomes:
            ///   "Player1: [Player3]: hello"
            /// </summary>
            internal static string BuildAliasRewrite(NetworkGamer sender, string claimedName, string msg)
            {
                string safeSender = GetDisplayedName(sender);

                string safeClaimed = IncomingMessageSanitizer.SanitizeText(
                    claimedName ?? string.Empty,
                    GamertagSanitizerConfig.MaxNameLen);

                safeClaimed = safeClaimed.Trim();
                safeClaimed = ChatNameClamp.ClampWithEllipsis(safeClaimed, ChatNameClamp.MaxNameChars);

                string prefix = $"{safeSender}: [{safeClaimed}]";

                if (string.IsNullOrWhiteSpace(msg))
                    return prefix;

                int budget = GamertagSanitizerConfig.MaxChatLineLen - (prefix.Length + 2); // ": "
                if (budget < 0) budget = 0;

                string safeMsg = IncomingMessageSanitizer.SanitizeText(msg, budget);
                return $"{prefix}: {safeMsg}";
            }

            /// <summary>
            /// Returns true if the target is one of this client's LocalGamers (split-screen included).
            /// </summary>
            internal static bool IsLocalTarget(NetworkSession sess,
                                              NetworkGamer target)
            {
                if (sess == null || target == null) return false;

                var locals = sess.LocalGamers;
                for (int i = 0; i < locals.Count; i++)
                {
                    if (ReferenceEquals(locals[i], target))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// Safe accessor for NetworkGamer.Id as a string.
            /// </summary>
            internal static string GetIdSafe(NetworkGamer g)
            {
                try { return g != null ? g.Id.ToString() : "?"; }
                catch { return "?"; }
            }

            /// <summary>
            /// Main impersonation handler.
            /// Returns true if a spoof was detected and handled (rewritten and/or broadcast).
            /// </summary>
            /// <param name="cleanedLine">Already sanitized for control/newline spam.</param>
            internal static bool HandleIfImpersonation(
                LocalNetworkGamer localGamer,
                BroadcastTextMessage btm,
                string cleanedLine /* Already SanitizeText'd. */)
            {
                if (!ImpersonationConfig.NoImpersonationEnabled)
                    return false;

                if (btm == null)
                    return false;

                var sender = btm.Sender;
                if (sender == null)
                    return false;

                var sess = sender.Session;
                if (sess == null)
                    return false;

                // Attempt to parse "Name: msg".
                if (!TryParseClaim(cleanedLine.TrimStart(), out string claimed, out string msg))
                    return false;

                // Find which gamer that name belongs to (based on displayed/sanitized name).
                var target = FindTargetByDisplayedName(sess, claimed);

                // If the claimed name does NOT belong to another real player, treat it as an alias/self-rename
                // and rewrite the line locally instead of letting the normal prefixer produce:
                //   "Player1 NewPlayer3: msg"
                if (target == null)
                {
                    btm.Message = BuildAliasRewrite(sender, claimed, msg);
                    return true;
                }

                // If sender IS target, it's fine.
                if (ReferenceEquals(sender, target))
                    return false;

                // Scope check: Only protect locals unless ProtectEveryone is enabled.
                bool targetIsLocal = IsLocalTarget(sess, target);
                if (!ImpersonationConfig.ProtectEveryone && !targetIsLocal)
                    return false;

                // Build safe names + ids.
                // For impersonation only: Show original spam as "\n" tokens (clamped).
                string safeSenderName = GetNameForImpersonation(sender);

                // Keep target using your usual display (or also use GetNameForImpersonation(target)).
                string safeTargetName = GamerTagFixer.Sanitize(target, target.Gamertag ?? string.Empty);
                safeTargetName = ChatNameClamp.ClampWithEllipsis(safeTargetName, ChatNameClamp.MaxNameChars);

                string senderId = GetIdSafe(sender);
                string targetId = GetIdSafe(target);

                // Build the prefix first.
                string prefix =
                    $"['{safeSenderName}' (id:{senderId}) -> Impersonated -> '{safeTargetName}' (id:{targetId})]: ";

                // Figure out how much room remains for the message payload.
                int maxLine = GamertagSanitizerConfig.MaxChatLineLen;
                int budget = maxLine - prefix.Length;
                if (budget < 0) budget = 0;

                // Now sanitize message to FIT the remaining space.
                string safeMsg = IncomingMessageSanitizer.SanitizeText(msg ?? string.Empty, budget);

                // Final rewritten line (guaranteed <= MaxChatLineLen).
                string rewritten = prefix + safeMsg;

                // OPTIONAL: Show the original (but sanitized) line first.
                btm.Message = cleanedLine;

                // Optional response broadcast.
                bool shouldRespond;
                if (ImpersonationConfig.ProtectEveryone && ImpersonationConfig.HostOnlyRespondWhenProtectEveryone)
                {
                    // Only let host respond to "everyone" protections to avoid spam
                    shouldRespond = (localGamer != null && localGamer.IsHost) || targetIsLocal;
                }
                else
                {
                    // In self-only mode, it's fine to respond when we're the target
                    shouldRespond = targetIsLocal || ImpersonationConfig.ProtectEveryone;
                }

                if (shouldRespond && localGamer != null && !localGamer.HasLeftSession)
                {
                    if (!ImpersonationConfig.UseClearChat)
                    {
                        BroadcastTextMessage.Send(localGamer, rewritten);
                    }
                    else
                    {
                        // Clear chat (your own client will see it because you skip local-sender sanitizing).
                        BroadcastTextMessage.Send(localGamer,
                            "\n\n\n\n\n\n\n\n\n\n");
                    }
                }

                return true;
            }

            // Put near your other toggles/config.
            /// <summary>
            /// Display clamp for names in chat output (separate from MaxNameLen which is the sanitizer cap).
            /// </summary>
            internal static class ChatNameClamp
            {
                internal static int MaxNameChars = 20; // config (ex: 20)

                internal static string ClampWithEllipsis(string s, int maxChars)
                {
                    s = s ?? string.Empty;

                    if (maxChars <= 0)
                        return string.Empty;

                    if (s.Length <= maxChars)
                        return s;

                    // If max is tiny, don't try to add "...".
                    if (maxChars <= 3)
                        return s.Substring(0, maxChars);

                    // Trim end so we don't get "Bob ...".
                    return s.Substring(0, maxChars - 3).TrimEnd() + "...";
                }
            }

            /// <summary>
            /// Converts real control characters into visible tokens so name-spam is still displayed
            /// in impersonation warnings (ex: '\n' => "\\n").
            /// </summary>
            public static string EscapeControlsToLiterals(string raw)
            {
                raw = raw ?? string.Empty;

                var sb = new StringBuilder(raw.Length * 2);

                for (int i = 0; i < raw.Length; i++)
                {
                    char c = raw[i];

                    // Treat CRLF as one newline token.
                    if (c == '\r')
                    {
                        if (i + 1 < raw.Length && raw[i + 1] == '\n')
                            continue;

                        sb.Append("\\n");
                        continue;
                    }

                    if (c == '\n') { sb.Append("\\n"); continue; }
                    if (c == '\t') { sb.Append("\\t"); continue; }

                    // Other control chars: Show as hex so it's obvious spam.
                    if (char.IsControl(c))
                    {
                        sb.Append("\\x");
                        sb.Append(((int)c).ToString("X2"));
                        continue;
                    }

                    sb.Append(c);
                }

                return sb.ToString();
            }

            /// <summary>
            /// Prevents double-bracketing in warning output when a name is already "[id]"-style.
            /// </summary>
            private static string StripOuterBrackets(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                if (s.Length >= 2 && s[0] == '[' && s[s.Length - 1] == ']')
                    return s.Substring(1, s.Length - 2);
                return s;
            }

            /// <summary>
            /// Impersonation-only display name:
            ///  - Prefer original (pre-sanitized) raw gamertag snapshot (if captured).
            ///  - Escape controls into visible tokens (\\n, \\t, \\xNN).
            ///  - Fallback to the normal sanitized "[id]" display if still empty.
            ///  - Clamp for chat output.
            /// </summary>
            public static string GetNameForImpersonation(Gamer g)
            {
                // Prefer the ORIGINAL pre-sanitized tag if we captured it.
                string raw = GamerTagFixer.TryGetRaw(g) ?? (g?.Gamertag ?? string.Empty);

                // Convert real linebreak chars into visible "\n" tokens.
                string shown = EscapeControlsToLiterals(raw);

                // If still "empty-ish", fall back to your normal sanitized name (includes [id]).
                if (string.IsNullOrEmpty(shown))
                    shown = GamerTagFixer.Sanitize(g, g?.Gamertag ?? string.Empty);

                // Avoid the "[[1]" double-bracket issue in the warning prefix.
                shown = StripOuterBrackets(shown);

                // Clamp for display
                shown = ChatNameClamp.ClampWithEllipsis(shown, ChatNameClamp.MaxNameChars);

                return shown;
            }
        }
        #endregion

        #region GamerTag Fixer (MODIFIES Original Gamer.Gamertag)

        /// <summary>
        /// Sanitizes remote (non-local) gamertags in-place to prevent control/newline spam.
        /// </summary>
        /// <remarks>
        /// - Does NOT modify local gamertags (local players / split-screen).
        /// - Captures original raw gamertag once (before mutation) for impersonation display.
        /// - Controlled by GamertagSanitizerConfig.Enabled.
        /// </remarks>
        internal static class GamerTagFixer
        {
            internal static int MaxNameLen => GamertagSanitizerConfig.MaxNameLen;

            // If JoinCallback fires before AllGamers is ready, we apply later.
            private static volatile bool _pendingFullRescan;

            internal static void RequestFullRescan()
                => _pendingFullRescan = true;

            internal static void TryApplyPendingFullRescan()
            {
                if (!_pendingFullRescan) return;
                _pendingFullRescan = false;

                ApplyToAllGamers();
            }

            /// <summary>
            /// Full pass over session.AllGamers (locals + remotes). Locals are skipped inside ApplyToOne.
            /// </summary>
            internal static void ApplyToAllGamers()
            {
                if (!GamertagSanitizerConfig.GamertagSanitizerEnabled)
                    return;

                var game = CastleMinerZGame.Instance;
                if (game == null) return;

                var sess = game.CurrentNetworkSession;
                if (sess == null) return;

                // AllGamers includes locals + remotes.
                foreach (NetworkGamer ng in sess.AllGamers)
                    ApplyToOne(ng);
            }

            /// <summary>
            /// Applies sanitization to a single gamer (non-local only).
            /// </summary>
            internal static void ApplyToOne(Gamer gamer)
            {
                if (gamer == null) return;

                // Don't ever modify our own gamertag.
                if (IsLocalGamer(gamer))
                    return;

                string raw = gamer.Gamertag ?? string.Empty;

                // Cache the original BEFORE we sanitize/mutate it.
                RememberRaw(gamer, raw);

                // Master kill-switch: Do not sanitize/mutate anything.
                if (!GamertagSanitizerConfig.GamertagSanitizerEnabled)
                    return;

                string cleaned = Sanitize(gamer, raw);

                // Avoid churn: Only assign if it actually changed.
                if (!string.Equals(raw, cleaned, StringComparison.Ordinal))
                    gamer.Gamertag = cleaned; // <-- MODIFIES ORIGINAL.
            }

            /// <summary>
            /// Produces a safe visible name:
            ///  - Replaces newline/tab and "\n"/"/n" literals with spaces.
            ///  - Strips other control characters.
            ///  - Collapses whitespace.
            ///  - Clamps to MaxNameLen.
            ///  - Falls back to "[id]" if empty.
            /// </summary>
            internal static string Sanitize(Gamer gamer, string raw)
            {
                raw = raw ?? string.Empty;

                if (!NeedsCleaning(raw))
                    return raw;

                var sb = new StringBuilder(raw.Length);

                for (int i = 0; i < raw.Length; i++)
                {
                    char c = raw[i];

                    // Literal "/n", "\n", "/r", "\r", "/t", "\t" (case-insensitive).
                    /*
                    if ((c == '\\' || c == '/') && i + 1 < raw.Length)
                    {
                        char n = raw[i + 1];
                        if (n == 'n' || n == 'N' || n == 'r' || n == 'R' || n == 't' || n == 'T')
                        {
                            sb.Append(' ');
                            i++;
                            continue;
                        }
                    }
                    */

                    if (c == '\r' || c == '\n' || c == '\t')
                    {
                        sb.Append(' ');
                        continue;
                    }

                    if (char.IsControl(c))
                        continue;

                    sb.Append(c);
                }

                string cleaned = CollapseWhitespace(sb);

                if (cleaned.Length > MaxNameLen)
                    cleaned = cleaned.Substring(0, MaxNameLen).TrimEnd();

                if (cleaned.Length == 0) cleaned = "(blank)";

                return cleaned;
            }

            private static string GetBestId(Gamer gamer)
            {
                // Prefer NetworkGamer.Id if present.
                try
                {
                    if (gamer is NetworkGamer ng)
                        return ng.Id.ToString();
                }
                catch { }

                // Fallback PlayerID.
                try
                {
                    return gamer.PlayerID.ToString();
                }
                catch { }

                return "?";
            }

            private static bool NeedsCleaning(string s)
            {
                if (string.IsNullOrEmpty(s)) return true;
                if (s.Length > MaxNameLen) return true;

                if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]))
                    return true;

                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '\r' || c == '\n' || c == '\t' || char.IsControl(c))
                        return true;

                    if ((c == '/' || c == '\\') && i + 1 < s.Length)
                    {
                        char n = s[i + 1];
                        if (n == 'n' || n == 'N' || n == 'r' || n == 'R' || n == 't' || n == 'T')
                            return true;
                    }
                }

                return false;
            }

            private static string CollapseWhitespace(StringBuilder sb)
            {
                var outSb = new StringBuilder(sb.Length);
                bool inWs = true;

                for (int i = 0; i < sb.Length; i++)
                {
                    char c = sb[i];

                    if (char.IsWhiteSpace(c))
                    {
                        if (!inWs)
                        {
                            outSb.Append(' ');
                            inWs = true;
                        }
                        continue;
                    }

                    outSb.Append(c);
                    inWs = false;
                }

                int len = outSb.Length;
                if (len > 0 && outSb[len - 1] == ' ')
                    outSb.Length = len - 1;

                return outSb.ToString();
            }

            #region Session Cache Reset

            /// <summary>
            /// Clears session-scoped caches so join/leave announcements can re-fire when IDs are reused in a new session.
            /// Call this whenever you leave a session or before joining a new one.
            /// </summary>
            internal static void ResetSessionCaches()
            {
                // Reset pending rescan gate too (optional but clean).
                _pendingFullRescan = false;

                // Clear raw gamertag snapshots (prevents stale "raw" names carrying into a new session).
                lock (_rawLock)
                    _rawGamertagByKey.Clear();

                // Clear join/leave announcer dedupe set (prevents "JOIN:N:1" being suppressed in later sessions).
                JoinNameSanitizationAnnouncer.ResetSessionCaches();
            }
            #endregion

            #region Raw Gamertag Snapshot (For Impersonation Display)

            /// <summary>
            /// Stores the original (pre-sanitized) gamertag once per gamer key so impersonation warnings
            /// can show what was actually used, including newline/control spam.
            /// </summary>
            private static readonly Dictionary<string, string> _rawGamertagByKey = new Dictionary<string, string>();
            private static readonly object _rawLock = new object();

            private static string GetKey(Gamer g)
            {
                try
                {
                    if (g is NetworkGamer ng)
                        return "N:" + ng.Id.ToString();
                }
                catch { }

                try
                {
                    return "P:" + g.PlayerID.ToString();
                }
                catch { }

                return "?:?";
            }

            internal static void RememberRaw(Gamer g, string raw)
            {
                if (g == null) return;

                string key = GetKey(g);
                if (string.IsNullOrEmpty(key)) return;

                lock (_rawLock)
                {
                    // Only capture once: We want the ORIGINAL before we mutate.
                    if (!_rawGamertagByKey.ContainsKey(key))
                        _rawGamertagByKey[key] = raw ?? string.Empty;
                }
            }

            internal static string TryGetRaw(Gamer g)
            {
                if (g == null) return null;

                string key = GetKey(g);
                if (string.IsNullOrEmpty(key)) return null;

                lock (_rawLock)
                {
                    return _rawGamertagByKey.TryGetValue(key, out var v) ? v : null;
                }
            }
            #endregion

            #region Join/Leave Name Sanitization Announcer (Public Notice for Blank/Newline-Spam Tags)

            /// <summary>
            /// Broadcasts a public chat notice when a player JOINs or LEAVEs *and* their original (raw) gamertag
            /// was effectively blank / newline spam / control spam(the same scenario that forces a "[id]" fallback).
            ///
            /// WHY THIS EXISTS
            /// ---------------
            /// - Some players can join with gamertags made entirely from control characters / newlines.
            /// - The sanitizer fixes the name locally, but other players may not realize the name was "fixed".
            /// - This announcer optionally broadcasts a readable, sanitized public notice to alert everyone.
            /// </summary>
            internal static class JoinNameSanitizationAnnouncer
            {
                private static readonly HashSet<string> _announced = new HashSet<string>();
                private static readonly object _lock = new object();

                /// <summary>
                /// Builds a stable-ish key for a gamer to dedupe announcements across repeated events.
                /// Preference order:
                /// - NetworkGamer.Id
                /// - PlayerID
                /// - fallback placeholder
                /// </summary>
                private static string GetKey(Gamer g)
                {
                    try { if (g is NetworkGamer ng) return "N:" + ng.Id.ToString(); } catch { }
                    try { return "P:" + g.PlayerID.ToString(); } catch { }
                    return "?:?";
                }

                /// <summary>
                /// Returns true when the *raw* name is effectively empty after stripping newline/control spam.
                /// (This is the case that triggers the "[id]" fallback.)
                /// </summary>
                private static bool RawCleansToEmpty(string raw)
                {
                    raw = raw ?? string.Empty;

                    var sb = new StringBuilder(raw.Length);

                    for (int i = 0; i < raw.Length; i++)
                    {
                        char c = raw[i];

                        // Treat literal "/n" or "\n" etc as whitespace.
                        /*
                        if ((c == '\\' || c == '/') && i + 1 < raw.Length)
                        {
                            char n = raw[i + 1];
                            if (n == 'n' || n == 'N' || n == 'r' || n == 'R' || n == 't' || n == 'T')
                            {
                                sb.Append(' ');
                                i++;
                                continue;
                            }
                        }
                        */

                        if (c == '\r' || c == '\n' || c == '\t')
                        {
                            sb.Append(' ');
                            continue;
                        }

                        if (char.IsControl(c))
                            continue;

                        sb.Append(c);
                    }

                    // Collapse whitespace in the same style as your other helpers.
                    bool inWs = true;
                    int outLen = 0;
                    for (int i = 0; i < sb.Length; i++)
                    {
                        char c = sb[i];
                        if (char.IsWhiteSpace(c))
                        {
                            if (!inWs)
                            {
                                outLen++; // Would add one space.
                                inWs = true;
                            }
                            continue;
                        }

                        outLen++;
                        inWs = false;
                    }

                    // If we only produced whitespace (or nothing), it collapses to empty.
                    // Note: Leading whitespace doesn't count; trailing single-space is trimmed in the impl.
                    return outLen == 0;
                }

                /// <summary>
                /// Only announces if the joiners's RAW name was effectively blank/newline spam (=> forced "[id]").
                /// </summary>
                internal static void MaybeAnnounce(LocalNetworkGamer local, Gamer joined)
                {
                    if (!GamertagSanitizerConfig.AnnounceSanitizedJoinLeaveNames) return;
                    if (local == null || local.HasLeftSession) return;
                    if (joined == null) return;
                    if (IsLocalGamer(joined)) return;

                    // Only announce the "forced sanitize to [id]" case.
                    string raw = GamerTagFixer.TryGetRaw(joined) ?? joined.Gamertag ?? string.Empty;
                    if (!RawCleansToEmpty(raw)) return;

                    // Dedupe per player key (JOIN).
                    string key = "JOIN:" + GetKey(joined);
                    lock (_lock)
                    {
                        if (_announced.Contains(key)) return;
                        _announced.Add(key);
                    }

                    // Build a readable target display using existing helpers.
                    string safeTargetName = GetNameForImpersonation(joined);
                    string safeTargetId   = GetIdSafe(joined as NetworkGamer);

                    // Final message line.
                    string line = $"Player Joined: '{safeTargetName}' (id:{safeTargetId})";

                    // IMPORTANT: Do NOT pass through IncomingMessageSanitizer.SanitizeText (it eats "\n" literals).
                    BroadcastTextMessage.Send(local, line);
                }

                /// <summary>
                /// Only announces if the leaver's RAW name was effectively blank/newline spam (=> forced "[id]").
                /// </summary>
                internal static void MaybeAnnounceLeft(LocalNetworkGamer local, Gamer left)
                {
                    if (!GamertagSanitizerConfig.AnnounceSanitizedJoinLeaveNames) return;
                    if (local == null || local.HasLeftSession) return;
                    if (left == null) return;
                    if (IsLocalGamer(left)) return;

                    // Only announce the "forced sanitize to [id]" case.
                    string raw = GamerTagFixer.TryGetRaw(left) ?? left.Gamertag ?? string.Empty;
                    if (!RawCleansToEmpty(raw)) return;

                    // Dedupe per player key (LEFT).
                    string key = "LEFT:" + GetKey(left);
                    lock (_lock)
                    {
                        if (_announced.Contains(key))
                            return;
                        _announced.Add(key);
                    }

                    // Build a readable target display using existing helpers.
                    string safeTargetName = GetNameForImpersonation(left);
                    string safeTargetId   = GetIdSafe(left as NetworkGamer);

                    // Final message line.
                    string line = $"Player Left: '{safeTargetName}' (id:{safeTargetId})";

                    // IMPORTANT: Do NOT pass through IncomingMessageSanitizer.SanitizeText (it eats "\n" literals).
                    BroadcastTextMessage.Send(local, line);
                }

                #region Session Cache Reset

                /// <summary>
                /// Clears dedupe so announcements can occur again in a new session (IDs often reuse 0/1/2...).
                /// </summary>
                internal static void ResetSessionCaches()
                {
                    lock (_lock)
                        _announced.Clear();
                }
                #endregion
            }
            #endregion
        }

        /// <summary>
        /// Local gamer detection helper used by the sanitizer so we never mutate our own tag.
        /// </summary>
        internal static bool IsLocalGamer(Gamer g)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                var sess = game?.CurrentNetworkSession;
                if (sess == null || g == null) return false;

                // Most reliable: Reference-equality against LocalGamers.
                for (int i = 0; i < sess.LocalGamers.Count; i++)
                {
                    if (ReferenceEquals(sess.LocalGamers[i], g))
                        return true;
                }

                // Extra safety: If types exist, treat LocalNetworkGamer as local.
                if (g is LocalNetworkGamer)
                    return true;
            }
            catch { }

            return false;
        }
        #endregion

        #region Incoming Message Sanitizer (Newline/Control Spam + Max Length)

        /// <summary>
        /// Sanitizes incoming message text (especially BroadcastTextMessage lines) to remove newline/control spam,
        /// normalize whitespace, and enforce a max total line length.
        /// </summary>
        /// <remarks>
        /// - MaxChatLineLen is sourced from GamertagSanitizerConfig.
        /// - BroadcastTextMessage path also prefixes sender name to match gamertag formatting.
        /// </remarks>
        internal static class IncomingMessageSanitizer
        {
            internal static int MaxChatLineLen => GamertagSanitizerConfig.MaxChatLineLen; // includes name + message

            /// <summary>
            /// Ensures a BroadcastTextMessage line is safe and consistently formatted.
            /// 
            /// Behavior:
            /// - If NoImpersonationEnabled == false:
            ///     Preserve an incoming "ClaimedName: message" line exactly as the sender intended
            ///     (after sanitization), even if ClaimedName != sender's real/current gamertag.
            /// - If NoImpersonationEnabled == true:
            ///     Fall back to the normal sender-safe-name prefix behavior unless the impersonation
            ///     guard already handled the line earlier.
            /// </summary>
            internal static void SanitizeBroadcastLine(BroadcastTextMessage btm)
            {
                if (btm == null)
                    return;

                NetworkGamer sender = btm.Sender;
                string rawLine = btm.Message ?? string.Empty;

                // 1) Sanitize the incoming line first.
                string cleanedLine = SanitizeText(rawLine, MaxChatLineLen);

                if (sender != null)
                {
                    string safeSenderName = GamerTagFixer.Sanitize(sender, sender.Gamertag ?? string.Empty);
                    safeSenderName = ChatNameClamp.ClampWithEllipsis(safeSenderName, ChatNameClamp.MaxNameChars);

                    // If anti-impersonation is OFF, and the incoming line already looks like:
                    //   "SomeName: message"
                    // then preserve that claimed name instead of prepending the sender's real/current name.
                    //
                    // This allows:
                    //   Player3: hello
                    // to stay:
                    //   Player3: hello
                    //
                    // instead of becoming:
                    //   Player1 Player3: hello
                    if (!ImpersonationConfig.NoImpersonationEnabled)
                    {
                        if (ImpersonationGuard.TryParseClaim(cleanedLine.TrimStart(), out string claimed, out string msg))
                        {
                            // If the claimed name differs from the sender's real/current displayed name,
                            // preserve the claimed name exactly (sanitized/clamped).
                            if (!string.Equals(claimed, safeSenderName, StringComparison.OrdinalIgnoreCase))
                            {
                                string safeClaimed = SanitizeText(claimed ?? string.Empty, GamertagSanitizerConfig.MaxNameLen);
                                safeClaimed = safeClaimed.Trim();
                                safeClaimed = ChatNameClamp.ClampWithEllipsis(safeClaimed, ChatNameClamp.MaxNameChars);

                                int budget = MaxChatLineLen - (safeClaimed.Length + 2); // ": "
                                if (budget < 0) budget = 0;

                                string safeMsg = SanitizeText(msg ?? string.Empty, budget);

                                btm.Message = string.IsNullOrWhiteSpace(safeMsg)
                                    ? $"{safeClaimed}:"
                                    : $"{safeClaimed}: {safeMsg}";
                                return;
                            }
                        }
                    }

                    // Original behavior:
                    // If the line doesn't already start with the sender's safe/current name, prefix it.
                    if (!StartsWithName(cleanedLine, safeSenderName))
                    {
                        string tail = (cleanedLine ?? string.Empty).TrimStart();

                        bool noSpace =
                            tail.Length > 0 && (tail[0] == ':' || tail[0] == '>' || tail[0] == '-');

                        string sep = noSpace ? "" : " ";

                        int budget = MaxChatLineLen - (safeSenderName.Length + sep.Length);
                        if (budget < 0) budget = 0;

                        if (tail.Length > budget)
                            tail = tail.Substring(0, budget).TrimEnd();

                        cleanedLine = safeSenderName + sep + tail;
                    }
                }

                btm.Message = cleanedLine;
            }

            private static bool StartsWithName(string line, string name)
            {
                if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(name))
                    return false;

                if (!line.StartsWith(name, StringComparison.Ordinal))
                    return false;

                // Accept: "Name "  /  "Name:"  /  "Name -"
                if (line.Length == name.Length)
                    return true;

                char next = line[name.Length];
                return next == ' ' || next == ':' || next == '-' || next == '>';
            }

            // Only touch message types in this namespace (reduces unintended side-effects).
            private const string AllowedNamespacePrefix = "DNA.CastleMinerZ";

            // Common field/property names used by chat/system text messages.
            private static readonly string[] _textNames =
            {
                "Message", "Text", "Chat", "Body", "Reason"
            };

            private static readonly Dictionary<Type, MemberCache> _cache = new Dictionary<Type, MemberCache>();

            private sealed class MemberCache
            {
                public FieldInfo[] Fields;
                public PropertyInfo[] Properties;
            }

            /// <summary>
            /// Generic reflection-based sanitizer for CMZ message types that have string payload fields/properties.
            /// </summary>
            internal static void SanitizeInPlace(Message msg)
            {
                if (msg == null) return;

                Type t = msg.GetType();
                string ns = t.Namespace ?? string.Empty;

                // Avoid touching low-level DNA.Net types unless they are CMZ messages.
                if (!ns.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
                    return;

                MemberCache members = GetMembers(t);
                if ((members.Fields == null || members.Fields.Length == 0) &&
                    (members.Properties == null || members.Properties.Length == 0))
                    return;

                // Sanitize fields.
                if (members.Fields != null)
                {
                    for (int i = 0; i < members.Fields.Length; i++)
                    {
                        var f = members.Fields[i];
                        try
                        {
                            if (!(f.GetValue(msg) is string raw)) continue;

                            string cleaned = SanitizeText(raw, MaxChatLineLen);
                            if (!string.Equals(raw, cleaned, StringComparison.Ordinal))
                                f.SetValue(msg, cleaned);
                        }
                        catch { }
                    }
                }

                // Sanitize properties.
                if (members.Properties != null)
                {
                    for (int i = 0; i < members.Properties.Length; i++)
                    {
                        var p = members.Properties[i];
                        try
                        {
                            if (!(p.GetValue(msg, null) is string raw)) continue;

                            string cleaned = SanitizeText(raw, MaxChatLineLen);
                            if (!string.Equals(raw, cleaned, StringComparison.Ordinal))
                                p.SetValue(msg, cleaned, null);
                        }
                        catch { }
                    }
                }
            }

            private static MemberCache GetMembers(Type t)
            {
                lock (_cache)
                {
                    if (_cache.TryGetValue(t, out var hit))
                        return hit;

                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    var fields = new List<FieldInfo>();
                    var props = new List<PropertyInfo>();

                    // Fields.
                    foreach (var name in _textNames)
                    {
                        // Field names are often exact.
                        var f = t.GetField(name, flags);
                        if (f != null && f.FieldType == typeof(string))
                            fields.Add(f);
                    }

                    // Properties.
                    foreach (var name in _textNames)
                    {
                        var p = t.GetProperty(name, flags);
                        if (p != null &&
                            p.PropertyType == typeof(string) &&
                            p.CanRead && p.CanWrite &&
                            p.GetIndexParameters().Length == 0)
                        {
                            props.Add(p);
                        }
                    }

                    var created = new MemberCache
                    {
                        Fields = fields.Count > 0 ? fields.ToArray() : Array.Empty<FieldInfo>(),
                        Properties = props.Count > 0 ? props.ToArray() : Array.Empty<PropertyInfo>()
                    };

                    _cache[t] = created;
                    return created;
                }
            }

            /// <summary>
            /// Shared sanitizer used for chat lines and message payloads:
            ///  - Converts newline/tab and "\n"/"/n" literals into spaces.
            ///  - Strips other control characters.
            ///  - Collapses whitespace.
            ///  - Clamps to maxLen.
            ///  - Prevents empty spam (returns "(blank)").
            /// </summary>
            internal static string SanitizeText(string raw, int maxLen)
            {
                raw = raw ?? string.Empty;

                var sb = new StringBuilder(raw.Length);

                for (int i = 0; i < raw.Length; i++)
                {
                    char c = raw[i];

                    // Literal "/n", "\n", "/r", "\r", "/t", "\t" (case-insensitive).
                    /*
                    if ((c == '\\' || c == '/') && i + 1 < raw.Length)
                    {
                        char n = raw[i + 1];
                        if (n == 'n' || n == 'N' || n == 'r' || n == 'R' || n == 't' || n == 'T')
                        {
                            sb.Append(' ');
                            i++;
                            continue;
                        }
                    }
                    */

                    if (c == '\r' || c == '\n' || c == '\t')
                    {
                        sb.Append(' ');
                        continue;
                    }

                    if (char.IsControl(c))
                        continue;

                    sb.Append(c);
                }

                // Collapse whitespace.
                string cleaned = CollapseWhitespace(sb);

                // Cap.
                if (cleaned.Length > maxLen)
                    cleaned = cleaned.Substring(0, maxLen).TrimEnd();

                // Don't allow fully blank spam.
                if (cleaned.Length == 0) cleaned = "(blank)";

                return cleaned;
            }

            private static string CollapseWhitespace(StringBuilder sb)
            {
                var outSb = new StringBuilder(sb.Length);
                bool inWs = true;

                for (int i = 0; i < sb.Length; i++)
                {
                    char c = sb[i];

                    if (char.IsWhiteSpace(c))
                    {
                        if (!inWs)
                        {
                            outSb.Append(' ');
                            inWs = true;
                        }
                        continue;
                    }

                    outSb.Append(c);
                    inWs = false;
                }

                int len = outSb.Length;
                if (len > 0 && outSb[len - 1] == ' ')
                    outSb.Length = len - 1;

                return outSb.ToString();
            }

            internal static bool ShouldDropRaw(string raw)
            {
                raw = raw ?? string.Empty;

                // Empty/whitespace => drop.
                if (string.IsNullOrWhiteSpace(raw))
                    return true;

                // Any real newline => drop.
                if (raw.IndexOf('\n') >= 0 || raw.IndexOf('\r') >= 0)
                    return true;

                // OPTIONAL: Also treat literal "/n" or "\n" sequences as newline-spam.
                // if (raw.IndexOf("/n", StringComparison.OrdinalIgnoreCase) >= 0)  return true;
                // if (raw.IndexOf("\\n", StringComparison.OrdinalIgnoreCase) >= 0) return true;

                return false;
            }

            #region Session Cache Reset

            /// <summary>
            /// Clears cached reflection members for message types (optional; avoids cache growth across sessions).
            /// </summary>
            internal static void ResetSessionCaches()
            {
                lock (_cache)
                    _cache.Clear();
            }
            #endregion
        }
        #endregion

        #region Patch - Sanitize Incoming Message Text (Skip Local Sender)

        /// <summary>
        /// Intercepts Message.GetMessage() results and sanitizes incoming text.
        /// </summary>
        /// <remarks>
        /// - Skips local echo (ReferenceEquals(sender, localGamer)).
        /// - BroadcastTextMessage:
        ///    1) Sanitize raw line.
        ///    2) Impersonation guard (may rewrite + optionally broadcast).
        ///    3) Normal broadcast line formatting (prefix name etc.).
        /// - Other messages: sanitized only when GamertagSanitizerConfig.Enabled is true.
        /// </remarks>
        [HarmonyPatch(typeof(Message), "GetMessage")]
        internal static class Patch_Message_GetMessage_SanitizeIncomingText
        {
            private static void Postfix(LocalNetworkGamer localGamer,
                               ref Message __result)
            {
                if (__result == null) return;

                var sender = __result.Sender;
                if (sender == null) return;

                // Don't touch our own echoed messages.
                if (ReferenceEquals(sender, localGamer))
                    return;

                if (__result is BroadcastTextMessage btm)
                {
                    // RAW text (before the sanitizer converts newlines into spaces).
                    string raw = btm.Message ?? string.Empty;

                    // ADDON: Drop empty/newline spam EARLY so nothing downstream sees it.
                    if (NewlineChatConfig.IgnoreChatNewlines)
                    {
                        if (IncomingMessageSanitizer.ShouldDropRaw(raw))
                        {
                            // __result = null;
                            return;
                        }
                    }

                    // Step 1: Sanitize raw line (remove \n/control spam etc.).
                    string cleaned = IncomingMessageSanitizer.SanitizeText(raw, IncomingMessageSanitizer.MaxChatLineLen);

                    // Step 2: Impersonation check BEFORE you do any sender prefixing.
                    if (ImpersonationGuard.HandleIfImpersonation(localGamer, btm, cleaned))
                        return;

                    // Step 3: Normal "make chat match gamertags" formatting/prefixing.
                    // (the existing SanitizeBroadcastLine can take 'cleaned' as input if you want).
                    btm.Message = cleaned;
                    IncomingMessageSanitizer.SanitizeBroadcastLine(btm); // The existing prefixing method.
                    return;
                }

                // Everything else.
                if (!GamertagSanitizerConfig.GamertagSanitizerEnabled)
                    return;

                // Everything else: generic sanitize.
                IncomingMessageSanitizer.SanitizeInPlace(__result);
            }
        }

        #region Ignore Newlines Addon

        /// <summary>
        /// Filters incoming BroadcastTextMessage chat lines to block newline/blank spam.
        /// </summary>
        /// <remarks>
        /// - Gate:   Only runs when CastleWallsMk2._ignoreNewlinesEnabled is true.
        /// - Scope:  Only affects BroadcastTextMessage (regular chat broadcast).
        /// - Safety: Allows your own outgoing/echoed messages (sender == MyNetworkGamer).
        /// - Action: If the message is empty/whitespace OR contains ANY '\n' or '\r',
        ///   return false to skip the vanilla handler (drops the line entirely).
        /// </remarks>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processBroadcastTextMessage")]
        internal static class Patch_CastleMinerZGame_ProcessBroadcastTextMessage_DropNewlines
        {
            [HarmonyPrefix]
            private static bool Prefix(Message message)
            {
                if (!NewlineChatConfig.IgnoreChatNewlines)
                    return true; // Run vanilla.

                if (!(message is BroadcastTextMessage btm))
                    return true; // Not our message type, let vanilla handle it.

                if (message.Sender == CastleMinerZGame.Instance.MyNetworkGamer)
                    return true; // Our message, let it send.

                string text = btm.Message;

                // Drop empty/blank.
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                // Drop any newline at all.
                if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                    return false;

                // OPTIONAL: Also treat literal "/n" and "\n" sequences as newline-spam.
                // if (text.Contains("/n") || text.Contains("\\n"))
                //     return false;

                return true; // Allow vanilla Console.WriteLine(...).
            }
        }
        #endregion

        #endregion

        #region Patch - Reset Session-Scoped Caches On Join/Leave (Prevents Cross-Session Dedupe / Stale Raw Tags)

        /// <summary>
        /// Clears session-sticky caches whenever we JOIN or LEAVE a network session.
        ///
        /// Why:
        /// - NetworkGamer.Id values are often reused across sessions (0/1/2...), so a static dedupe set can
        ///   incorrectly suppress join/leave announcements in later sessions.
        /// - Raw gamertag snapshot caches can also "stick" between sessions and show stale data.
        ///
        /// What gets cleared:
        /// - GamerTagFixer session caches (raw-tag snapshots + any pending flags).
        /// - IncomingMessageSanitizer reflection/member cache (optional, but prevents cache growth).
        ///
        /// NOTES
        /// -----
        /// - These are Prefix hooks so we reset BEFORE async BeginJoin/BeginJoinInvited kicks off and before
        ///   LeaveGame disposes the session (best chance to avoid stale state affecting immediate callbacks).
        /// </summary>
        [HarmonyPatch(typeof(DNAGame))]
        internal static class Patch_DNAGame_JoinLeave_ResetSessionCaches
        {
            // Clear BEFORE leaving (safe + ensures no "last-second" events use stale dedupe).
            [HarmonyPrefix]
            [HarmonyPatch(nameof(DNAGame.LeaveGame), new Type[] { })]
            private static void Pre_LeaveGame(DNAGame __instance)
            {
                GamerTagFixer.ResetSessionCaches();
                IncomingMessageSanitizer.ResetSessionCaches();
            }

            // Clear BEFORE joining so OnGamerJoined announcements aren't suppressed by old session keys.
            [HarmonyPrefix]
            [HarmonyPatch(nameof(DNAGame.JoinGame), new[] { typeof(AvailableNetworkSession), typeof(IList<SignedInGamer>), typeof(SuccessCallbackWithMessage), typeof(string), typeof(int), typeof(string) })]
            private static void Pre_JoinGame_WithMessage(DNAGame __instance)
            {
                GamerTagFixer.ResetSessionCaches();
                IncomingMessageSanitizer.ResetSessionCaches();
            }

            // Clear BEFORE joining invited games (Steam/lobby invites).
            [HarmonyPrefix]
            [HarmonyPatch(nameof(DNAGame.JoinInvitedGame), new[] { typeof(ulong), typeof(int), typeof(string), typeof(IList<SignedInGamer>), typeof(SuccessCallbackWithMessage), typeof(GetPasswordForInvitedGameCallback) })]
            private static void Pre_JoinInvitedGame(DNAGame __instance)
            {
                GamerTagFixer.ResetSessionCaches();
                IncomingMessageSanitizer.ResetSessionCaches();
            }
        }
        #endregion

        #region Patch - Apply Gamertag Sanitization At Safe Session Lifecycle Points

        /// <summary>
        /// Applies gamertag sanitization after join/start, and on late joiners.
        /// </summary>
        /// <remarks>
        /// - JoinCallback:  Requests a rescan and optionally tries immediately.
        /// - StartGame:     Best "we're in" point; performs the pending rescan.
        /// - OnGamerJoined: Sanitizes the joining gamer after base logic.
        /// </remarks>
        [HarmonyPatch]
        internal static class Patch_Gamertag_MutateSafely
        {
            // 1) JoinCallback: Request a rescan (don't assume AllGamers is fully ready here).
            [HarmonyPostfix]
            [HarmonyPatch(typeof(FrontEndScreen), "JoinCallback")]
            private static void FrontEndScreen_JoinCallback_Postfix(bool success, string message)
            {
                if (!success) return;

                // Mark that we want a full pass once StartGame happens.
                GamerTagFixer.RequestFullRescan();

                // OPTIONAL: Try once right now if session already exists (harmless if null).
                // If this ever causes join weirdness, delete these 2 lines and rely on StartGame.
                GamerTagFixer.ApplyToAllGamers();
            }

            // 2) StartGame: safest "we're in" point -> sanitize existing AllGamers now
            [HarmonyPostfix]
            [HarmonyPatch(typeof(CastleMinerZGame), "StartGame")]
            private static void CastleMinerZGame_StartGame_Postfix()
            {
                GamerTagFixer.TryApplyPendingFullRescan();
                // If you want this to always run on StartGame (even without JoinCallback), do:
                // GamerTagFixer.ApplyToAllGamers();
            }

            // 3) OnGamerJoined: Sanitize late joiners AFTER base logic runs with raw value.
            [HarmonyPostfix]
            [HarmonyPatch(typeof(CastleMinerZGame), "OnGamerJoined")]
            private static void CastleMinerZGame_OnGamerJoined_Postfix(NetworkGamer gamer)
            {
                GamerTagFixer.ApplyToOne(gamer);

                // Broadcast a public join notice if the name was blank/newline spam -> "[id]".
                var local = CastleMinerZGame.Instance?.MyNetworkGamer as LocalNetworkGamer;
                JoinNameSanitizationAnnouncer.MaybeAnnounce(local, gamer);
            }

            // 4) OnGamerLeft: Sanitize leavers raw-tag snapshots / IDs before cleanup.
            [HarmonyPatch(typeof(CastleMinerZGame), "OnGamerLeft")]
            internal static class Patch_CastleMinerZGame_OnGamerLeft_AnnounceSanitizedName
            {
                [HarmonyPrefix]
                private static void Prefix(CastleMinerZGame __instance, NetworkGamer gamer)
                {
                    var local = __instance?.MyNetworkGamer as LocalNetworkGamer;
                    JoinNameSanitizationAnnouncer.MaybeAnnounceLeft(local, gamer);
                }
            }
        }
        #endregion

        #region Patch - Sanitize ONLY The OnGamerJoined Console.WriteLine Gamertag

        /// <summary>
        /// Transpiler that replaces the specific Gamer.get_Gamertag() used by the OnGamerJoined
        /// Console.WriteLine(...) log with a sanitized version (including "[id]" fallback).
        /// </summary>
        /// <remarks>
        /// This does NOT change gameplay logic-only the log output is sanitized.
        /// </remarks>
        [HarmonyPatch(typeof(CastleMinerZGame), "OnGamerJoined")]
        internal static class Patch_CastleMinerZGame_OnGamerJoined_SanitizeJoinLog
        {
            private static readonly MethodInfo MI_GetGamertag =
                AccessTools.PropertyGetter(typeof(Gamer), "Gamertag");

            private static readonly MethodInfo MI_ConsoleWriteLine_String =
                AccessTools.Method(typeof(Console), "WriteLine", new[] { typeof(string) });

            private static readonly MethodInfo MI_GetSafeGamertagForLog =
                AccessTools.Method(typeof(Patch_CastleMinerZGame_OnGamerJoined_SanitizeJoinLog),
                                   nameof(GetSafeGamertagForLog));

            // Helper: Returns the same sanitized value as your gamertag sanitizer,
            // INCLUDING the "[id]" fallback for empty/whitespace-only names.
            private static string GetSafeGamertagForLog(Gamer g)
            {
                // Uses your existing logic; does NOT change any game flow besides the log output.
                return GamerTagFixer.Sanitize(g, g?.Gamertag ?? string.Empty);
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);

                bool replaced = false;

                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i];

                    // Find calls to Gamer.get_Gamertag().
                    if (!replaced && ins.Calls(MI_GetGamertag))
                    {
                        // Only replace the one that feeds the Console.WriteLine(...) call.
                        // Look ahead a small window for Console.WriteLine(string).
                        for (int j = i; j < Math.Min(i + 12, list.Count); j++)
                        {
                            if (list[j].Calls(MI_ConsoleWriteLine_String))
                            {
                                // Stack already has the Gamer instance for callvirt get_Gamertag().
                                // Replace it with a call to our helper (same stack usage).
                                ins.opcode = OpCodes.Call;
                                ins.operand = MI_GetSafeGamertagForLog;

                                replaced = true;
                                break;
                            }
                        }
                    }
                }

                return list;
            }
        }
        #endregion

        #endregion

        #region Net Message Hardening

        #region ShotgunShotMessage ("RecieveData")

        #region Valid Shotgun Helper

        internal static class ShotgunIDs
        {
            // All valid shotgun item IDs in CMZ.
            private static readonly HashSet<InventoryItemIDs> _all = new HashSet<InventoryItemIDs>
            {
                InventoryItemIDs.PumpShotgun,
                InventoryItemIDs.GoldPumpShotgun,
                InventoryItemIDs.DiamondPumpShotgun,
                InventoryItemIDs.BloodStonePumpShotgun,
                InventoryItemIDs.IronSpacePumpShotgun,
                InventoryItemIDs.CopperSpacePumpShotgun,
                InventoryItemIDs.GoldSpacePumpShotgun,
                InventoryItemIDs.DiamondSpacePumpShotgun,
            };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsValid(InventoryItemIDs id) => _all.Contains(id);
        }
        #endregion

        [HarmonyPatch(typeof(ShotgunShotMessage), "RecieveData")]
        internal static class ShotgunShotMessage_RecieveData_Prefix
        {
            /// <summary>
            /// Prefix fully replaces the original decode.
            /// - Reads 5 directions + the ItemID (exactly like vanilla).
            /// - Validates ItemID using ShotgunIDs.IsValid.
            /// - If invalid: Returns false (skip original) without modifying the message instance (effectively ignored).
            /// - If valid:   Writes the values into the message instance and returns false (since we already did the work).
            ///
            /// Returning false from a Harmony Prefix on a void method = do not call original.
            /// Do this even on the "valid" path to avoid double reads; this logic is byte-for-byte equivalent.
            /// </summary>
            /// <param name="__instance">The ShotgunShotMessage being filled.</param>
            /// <param name="reader">Network binary reader.</param>
            /// <returns>false to skip original; true would let original run.</returns>
            static bool Prefix(ShotgunShotMessage __instance, BinaryReader reader)
            {
                // Defensive: If the stream is truncated/corrupt, swallow and ignore this message rather than crash the net thread.
                try
                {
                    // Read exactly what vanilla reads: 5 vectors then a 16-bit item id.
                    // (Same order & types as the original RecieveData.)
                    Vector3[] Directions = new Vector3[5];
                    for (int i = 0; i < 5; i++)
                        Directions[i] = reader.ReadVector3();
                    var ItemID = (InventoryItemIDs)reader.ReadInt16();

                    // Validate against your allow-list of shotgun IDs.
                    if (!ShotgunIDs.IsValid(ItemID))
                    {
                        // Invalid payload: Ignore the message entirely.
                        // We've already advanced the stream so subsequent messages remain aligned.

                        // SendFeedback($"[NetGuard] Dropped ShotgunShotMessage with non-shotgun (ItemID={ItemID}).");
                        return false; // Skip original; message has no effect.
                    }

                    // Valid payload: Populate the instance exactly as vanilla would have done.
                    for (int i = 0; i < 5; i++)
                        __instance.Directions[i] = Directions[i];
                    __instance.ItemID = ItemID;

                    // We did all the work; skip the original to avoid a duplicate read.
                    return false;
                }
                catch (EndOfStreamException) { return false; /* Truncated / malformed packet, safely ignore.           */ }
                catch (IOException)          { return false; /* I/O failure on the stream, safely ignore this message. */ }
                catch (Exception)            { return false; /* Any unexpected decode issue, fail closed (ignore).     */ }
            }
        }
        #endregion

        #region Safe Inventory Store (Host)

        /// <summary>
        /// SAFE INVENTORY STORE (HOST)
        /// Hardens CastleMinerZGame._processInventoryStoreOnServerMessage(...) against
        /// host-side NullReferenceException when a remote sender has no valid Player
        /// attached to Sender.Tag.
        ///
        /// Behavior:
        /// - Lets the original method run normally when no exception occurs.
        /// - Swallows only NullReferenceException from this specific inventory-store path.
        /// - Prevents a bad / partially registered player state from crashing the host.
        ///
        /// Rationale:
        /// - In modded / ghost-mode / desynced states, Sender.Tag may be null even though
        ///   InventoryStoreOnServerMessage still arrives.
        /// - Vanilla assumes Sender.Tag is always a valid Player and will dereference it.
        /// - This patch keeps the session alive by suppressing that specific non-fatal crash.
        /// </summary>

        [HarmonyPatch(typeof(CastleMinerZGame), "_processInventoryStoreOnServerMessage")]
        internal static class Patch_CastleMinerZGame_ProcessInventoryStoreOnServerMessage_CatchNre
        {
            /// <summary>
            /// Finalizer runs after the original method, even if it throws.
            /// Swallows only NullReferenceException so the host loop can continue.
            /// </summary>
            private static Exception Finalizer(Exception __exception)
            {
                // Fast path: Original method completed successfully.
                if (__exception == null)
                    return null;

                // Suppress only the known host-side null dereference.
                if (__exception is NullReferenceException)
                    return null;

                // Let anything else propagate normally.
                return __exception;
            }
        }
        #endregion

        #endregion

        #region Game Patches

        #region MainMenu: Hide Menu Ad

        /// <summary>
        /// Draw-time ad hider: Temporarily swaps ad textures to a transparent 1x1 for this draw.
        /// </summary>
        [HarmonyPatch(typeof(MainMenu), "OnDraw")]
        static class Patch_MainMenu_HideAd_Draw
        {
            [HarmonyPrefix]
            static void Prefix()
            {
                if (!AdsConfig.HideMenuAd) return;

                var game = CastleMinerZGame.Instance;
                var gd   = game?.GraphicsDevice;
                if (gd == null) return;

                // Save originals and swap to a transparent 1x1.
                MainMenuAdSuppressor._prevAd    = MainMenuAdSuppressor.GetAdTex(game);
                MainMenuAdSuppressor._prevAdSel = MainMenuAdSuppressor.GetAdSelTex(game);

                if (MainMenuAdSuppressor._prevAd != null || MainMenuAdSuppressor._prevAdSel != null)
                {
                    var blank = MainMenuAdSuppressor.Transparent(gd);
                    MainMenuAdSuppressor.SetAdTex(game, blank);
                    MainMenuAdSuppressor.SetAdSelTex(game, blank);
                    MainMenuAdSuppressor._swappedThisDraw = true;
                }
            }

            [HarmonyPostfix]
            static void Postfix()
            {
                if (!AdsConfig.HideMenuAd) return;

                if (!MainMenuAdSuppressor._swappedThisDraw) return;
                MainMenuAdSuppressor._swappedThisDraw = false;

                var game = CastleMinerZGame.Instance;
                if (game == null) return;

                // Restore originals so we only affect MainMenu drawing.
                if (MainMenuAdSuppressor._prevAd != null)    MainMenuAdSuppressor.SetAdTex(game, MainMenuAdSuppressor._prevAd);
                if (MainMenuAdSuppressor._prevAdSel != null) MainMenuAdSuppressor.SetAdSelTex(game, MainMenuAdSuppressor._prevAdSel);
                MainMenuAdSuppressor._prevAd = MainMenuAdSuppressor._prevAdSel = null;
            }
        }

        /// <summary>
        /// Input-time ad disabler: Zeroes ad hitbox/flags before input logic to block clicks.
        /// </summary>
        [HarmonyPatch(typeof(MainMenu), "OnPlayerInput")]
        static class Patch_MainMenu_DisableAd_Input
        {
            [HarmonyPrefix]
            static void Prefix(object __instance)
            {
                if (!AdsConfig.HideMenuAd) return;

                MainMenuAdSuppressor.FI_AdRect?.SetValue(__instance, Rectangle.Empty);
                MainMenuAdSuppressor.FI_AdSel?.SetValue(__instance, false);
                MainMenuAdSuppressor.FI_AdClicked?.SetValue(__instance, false);
            }
        }

        #region MainMenu Ad Helpers

        /// <summary>
        /// Suppresses the main menu ad by temporarily swapping its textures to a
        /// transparent 1x1 and clearing its private hover/click state when needed.
        /// </summary>
        internal class MainMenuAdSuppressor
        {
            // Cache reflection for MainMenu private fields.
            public static readonly FieldInfo    FI_AdRect    = AccessTools.Field(typeof(MainMenu),            "adRect");
            public static readonly FieldInfo    FI_AdSel     = AccessTools.Field(typeof(MainMenu),            "adSel");
            public static readonly FieldInfo    FI_AdClicked = AccessTools.Field(typeof(MainMenu),            "adClicked");

            // Helpers to get/set CastleMinerZGame.CMZREAd / CMZREAdSel as field or property.
            public static readonly FieldInfo    FI_CmzAd     = AccessTools.Field(typeof(CastleMinerZGame),    "CMZREAd");
            public static readonly FieldInfo    FI_CmzAdSel  = AccessTools.Field(typeof(CastleMinerZGame),    "CMZREAdSel");
            public static readonly PropertyInfo PI_CmzAd     = AccessTools.Property(typeof(CastleMinerZGame), "CMZREAd");
            public static readonly PropertyInfo PI_CmzAdSel  = AccessTools.Property(typeof(CastleMinerZGame), "CMZREAdSel");

            public static Texture2D _prevAd, _prevAdSel;
            public static bool      _swappedThisDraw;
            public static Texture2D _transparent1x1;

            /// <summary>
            /// Lazily create and cache a fully transparent 1x1 texture used as the "no ad" replacement.
            /// </summary>
            public static Texture2D Transparent(GraphicsDevice gd)
            {
                if (_transparent1x1 == null || _transparent1x1.IsDisposed)
                {
                    _transparent1x1 = new Texture2D(gd, 1, 1, false, SurfaceFormat.Color);
                    _transparent1x1.SetData(new[] { new Color(0, 0, 0, 0) });
                }
                return _transparent1x1;
            }

            public static Texture2D GetAdTex(CastleMinerZGame g)
                => FI_CmzAd != null ? (Texture2D)FI_CmzAd.GetValue(g)
                 : PI_CmzAd != null ? (Texture2D)PI_CmzAd.GetValue(g, null)
                 : null;

            public static Texture2D GetAdSelTex(CastleMinerZGame g)
                => FI_CmzAdSel != null ? (Texture2D)FI_CmzAdSel.GetValue(g)
                 : PI_CmzAdSel != null ? (Texture2D)PI_CmzAdSel.GetValue(g, null)
                 : null;

            public static void SetAdTex(CastleMinerZGame g, Texture2D tex)
            {
                if (FI_CmzAd != null) FI_CmzAd.SetValue(g, tex);
                else PI_CmzAd?.SetValue(g, tex, null);
            }

            public static void SetAdSelTex(CastleMinerZGame g, Texture2D tex)
            {
                if (FI_CmzAdSel != null) FI_CmzAdSel.SetValue(g, tex);
                else PI_CmzAdSel?.SetValue(g, tex, null);
            }
        }
        #endregion

        #endregion

        #region MainMenu: Display Mods Loaded

        /// <summary>
        /// Harmony patch that draws a flashy "X mods loaded" line near the player's name
        /// while the Main Menu is visible. We hook MainMenu.OnDraw so our text renders
        /// in the same pass as the menu UI.
        /// </summary>
        [HarmonyPatch(typeof(MainMenu))]
        internal static class MainMenu_ModCountBannerPatch
        {
            // A private SpriteBatch just for this overlay. Keeping our own batch avoids
            // colliding with whatever the menu is doing with its SpriteBatch state.
            private static SpriteBatch _overlayBatch;

            [HarmonyPostfix]
            [HarmonyPatch("OnDraw")]
            private static void OnDraw_Postfix(MainMenu __instance, GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
            {
                try
                {
                    // Basic guards: Nothing to draw without a game or a valid device.
                    var game = CastleMinerZGame.Instance;
                    if (game == null || device == null)
                        return;

                    // Lazy-create our SpriteBatch (no C# 8 '??=' to keep 7.3 compatibility).
                    if (_overlayBatch == null)
                        _overlayBatch = new SpriteBatch(device);

                    // Fonts & scale: Use the same fonts the title screens use and respect the screen scaler.
                    var nameFont = game._medFont;     // Player name typically uses the medium font.
                    var infoFont = game._consoleFont; // Small, readable font for status lines.
                    float scale = Screen.Adjuster.ScaleFactor.Y;

                    // Player name (fallback keeps it obvious during development).
                    string playerName =
                        (Screen.CurrentGamer != null && !string.IsNullOrEmpty(Screen.CurrentGamer.Gamertag))
                        ? Screen.CurrentGamer.Gamertag
                        : "Player";

                    // Count loaded mods. This reflection fallback looks for ModLoader.ModBase-derived types.
                    // If your loader exposes a direct "GetLoadedMods()" API, swap it in here.
                    int modCount = GetLoadedModCount();

                    // === RAINBOW COLOR ===
                    // Rotate hue over time for a subtle rainbow effect. 'speed' controls how quickly it cycles.
                    double seconds = (gameTime != null) ? gameTime.TotalGameTime.TotalSeconds : 0.0;
                    const double speed = 0.20; // 0.20 ≈ one full cycle every ~5 seconds
                    float hue = (float)(seconds * speed - Math.Floor(seconds * speed)); // NormaliMob [0,1).

                    // Optional "breathing" brightness pulse:
                    // double pulse = 0.75 + 0.25 * Math.Sin(seconds * 2.0); // V in [0.5..1.0]
                    // Color flash = HsvToColor(hue, 1f, (float)pulse);

                    // Flat full-bright rainbow (simple and eye-catching).
                    Color flash = HsvToColor(hue, 1f, 1f);

                    // === POSITIONING ===
                    // Many CMZ screens draw the player name starting at (0, 0) with the medium font.
                    // We compute one line of vertical space using that font's LineSpacing and the current scaler.
                    // NOTE: As written, modsPos shares the same Y as namePos (i.e., same baseline).
                    // If you want it BELOW the name, add another 'nameH' (or a fraction) to Y.
                    float nameH = nameFont.LineSpacing * scale;
                    var namePos = new Vector2(0f, nameH);     // "typical" name baseline.
                    var modsPos = new Vector2(0f, namePos.Y); // Currently same baseline as name.
                                                              // Example to push it one line below: new Vector2(0f, namePos.Y + nameH * 1.05f).

                    // === RENDER ===
                    // We only draw the mods line. The menu itself draws the player name and other UI.
                    _overlayBatch.Begin();

                    string modsText = $"ModLoader: {modCount} mod{(modCount == 1 ? "" : "s")} loaded.";
                    _overlayBatch.DrawOutlinedText(
                        infoFont,
                        modsText,
                        modsPos,
                        flash,       // Foreground text color (rainbow).
                        Color.Black, // 1px black outline keeps it readable on any background.
                        1,           // Outline thickness.
                        scale,       // Respect global UI scaling.
                        0f,
                        Vector2.Zero);

                    _overlayBatch.End();
                }
                catch
                {
                    // Never surface exceptions during rendering; keep the menu flow resilient.
                }
            }

            /// <summary>
            /// Reflection-based fallback for counting loaded mods.
            /// Looks for non-abstract types assignable to ModLoader.ModBase across all loaded assemblies.
            /// Replace with your loader's direct API if available for better accuracy/perf.
            /// </summary>
            private static int GetLoadedModCount()
            {
                try
                {
                    var modBaseType = Type.GetType("ModLoader.ModBase, ModLoader", throwOnError: false);
                    if (modBaseType != null)
                    {
                        int count = 0;
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            Type[] types;
                            try { types = asm.GetTypes(); }
                            catch { continue; } // Ignore dynamic or reflection-only assemblies that throw here.

                            for (int i = 0; i < types.Length; i++)
                            {
                                var t = types[i];
                                if (t != null && !t.IsAbstract && modBaseType.IsAssignableFrom(t))
                                    count++;
                            }
                        }
                        return count;
                    }
                }
                catch
                {
                    // Fall through to 0; Not fatal, we just won't display a count.
                }
                return 0;
            }

            /// <summary>
            /// Tiny HSV->RGB helper (h in [0..1), s,v in [0..1]) that returns an XNA Color.
            /// Keeps code self-contained and compatible with C# 7.3 (no tuple returns, etc.).
            /// </summary>
            private static Color HsvToColor(float h, float s, float v)
            {
                // Achromatic: Return a shade of gray.
                if (s <= 0f)
                {
                    var g = (byte)(v * 255f);
                    return new Color(g, g, g);
                }

                // Map hue ring into 6 sectors.
                h = (h - (float)Math.Floor(h)) * 6f;
                int i = (int)Math.Floor(h);
                float f = h - i;

                float p = v * (1f - s);
                float q = v * (1f - s * f);
                float t = v * (1f - s * (1f - f));

                float r, g2, b;
                switch (i)
                {
                    case 0:  r = v; g2 = t; b = p; break;
                    case 1:  r = q; g2 = v; b = p; break;
                    case 2:  r = p; g2 = v; b = t; break;
                    case 3:  r = p; g2 = q; b = v; break;
                    case 4:  r = t; g2 = p; b = v; break;
                    default: r = v; g2 = p; b = q; break; // Sector 5.
                }
                return new Color(r, g2, b);
            }
        }
        #endregion

        #region In-Game Chat: History & Input

        #region Class: InGameChatHistory

        /// <summary>
        /// Simple in-memory history for the in-game chat/console input.
        /// Shared by the Harmony patches below to provide Up/Down history cycling.
        /// </summary>
        internal static class InGameChatHistory
        {
            // Stored entries.
            private static readonly List<string> _history = new List<string>();

            // -1 = "live typing" (not currently browsing history).
            private static int _pos = -1;

            // What the user was typing before they hit Up the first time.
            private static string _draft = string.Empty;

            /// <summary>
            /// Add a new line to history, skipping blanks and consecutive duplicates.
            /// </summary>
            public static void Push(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;

                line = line.Trim();

                // Optional: Only remember slash-commands.
                // if (!line.StartsWith("/")) return;

                if (_history.Count == 0 || _history[_history.Count - 1] != line)
                {
                    _history.Add(line);
                }

                // Reset browsing state for the next entry.
                _pos = -1;
                _draft = string.Empty;

                // Optional: Cap size (MC uses 100).
                const int MaxEntries = 256;
                if (_history.Count > MaxEntries)
                {
                    _history.RemoveAt(0);
                }
            }

            /// <summary>
            /// Move through history (Up = older, Down = newer).
            /// Returns true if <paramref name="text"/> was changed.
            /// </summary>
            public static bool Step(bool isUp, ref string text)
            {
                if (_history.Count == 0)
                    return false;

                if (isUp)
                {
                    // First time pressing Up: Capture draft and jump to newest.
                    if (_pos == -1)
                    {
                        _draft = text ?? string.Empty;
                        _pos = _history.Count - 1;
                    }
                    else if (_pos > 0)
                    {
                        // Move toward older entries.
                        _pos--;
                    }
                    // else: Already at oldest; stay put.
                }
                else // Down
                {
                    if (_pos == -1)
                    {
                        // Already in live typing; nothing to do.
                        return false;
                    }

                    if (_pos < _history.Count - 1)
                    {
                        // Move toward newer entries.
                        _pos++;
                    }
                    else
                    {
                        // Past the newest entry -> back to live draft.
                        _pos = -1;
                        text = _draft ?? string.Empty;
                        return true;
                    }
                }

                if (_pos >= 0 && _pos < _history.Count)
                {
                    text = _history[_pos];
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Clear only non-command entries (anything that doesn't start with "/").
            /// Call this when leaving a game so commands persist, but chat lines don't.
            /// </summary>
            public static void ClearNonCommands()
            {
                if (_history.Count == 0)
                {
                    _pos = -1;
                    _draft = string.Empty;
                    return;
                }

                // Remove entries whose trimmed text does NOT start with '/'.
                _history.RemoveAll(line =>
                {
                    var t = line?.TrimStart();
                    return string.IsNullOrEmpty(t) || t[0] != '/';
                });

                // Reset browsing state; history list now only contains commands.
                _pos = -1;
                _draft = string.Empty;
            }

            /// <summary>
            /// Clear the entire history (commands and non-commands) and reset navigation state.
            /// </summary>
            public static void ClearAll()
            {
                _history.Clear();
                _pos = -1;
                _draft = string.Empty;
            }
        }
        #endregion

        #region PlainChatInputScreen - Capture Enter

        /// <summary>
        /// Harmony prefix on PlainChatInputScreen._textEditControl_EnterPressed:
        /// Captures any non-empty line when the player presses Enter and pushes it into history.
        /// </summary>
        [HarmonyPatch(typeof(PlainChatInputScreen), "_textEditControl_EnterPressed")]
        internal static class PlainChatInputScreen_EnterPatch
        {
            /// <summary>
            /// Capture any non-empty line when the player presses Enter in the chat box.
            /// </summary>
            [HarmonyPrefix]
            private static void Prefix(PlainChatInputScreen __instance)
            {
                TextEditControl edit = __instance._textEditControl;
                if (edit == null)
                    return;

                string text = edit.Text;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                InGameChatHistory.Push(text);
            }
        }
        #endregion

        #region PlainChatInputScreen - Up/Down History

        /// <summary>
        /// Harmony postfix on PlainChatInputScreen.OnPlayerInput:
        /// Intercepts Up/Down arrow keys to step through chat/command history and
        /// replace the current textbox contents.
        /// </summary>
        [HarmonyPatch(typeof(PlainChatInputScreen), "OnPlayerInput")]
        internal static class PlainChatInputScreen_OnPlayerInputPatch
        {
            /// <summary>
            /// Allow Up/Down arrows in the in-game chat box to cycle through previous entries.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(PlainChatInputScreen __instance,
                                        InputManager inputManager,
                                        GameController controller,
                                        KeyboardInput chatPad,
                                        GameTime gameTime,
                                        ref bool __result)
            {
                TextEditControl edit = __instance._textEditControl;
                if (edit == null)
                    return;

                if (!edit.HasFocus)
                    return;

                var keyboard = inputManager.Keyboard;

                bool up   = keyboard.WasKeyPressed(Keys.Up);
                bool down = keyboard.WasKeyPressed(Keys.Down);

                if (!up && !down)
                    return;

                string text = edit.Text ?? string.Empty;
                if (!InGameChatHistory.Step(isUp: up, ref text))
                    return;

                edit.Text = text;

                // Mark as handled. Original already ran, but this tells the UI "we used this input".
                __result = true;
            }
        }
        #endregion

        #region DNAGame.LeaveGame - Trim History Between Sessions

        /// <summary>
        /// Harmony postfix on DNAGame.LeaveGame():
        /// Trims chat history to only command entries when leaving a game, so
        /// normal chat lines don't carry across worlds but your slash-commands do.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), nameof(DNAGame.LeaveGame))]
        internal static class DNAGame_LeaveGame_ClearChatHistory
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                InGameChatHistory.ClearNonCommands();
            }
        }
        #endregion

        #region TextEditControl - Centered Caret Rendering

        /// <summary>
        /// Harmony prefix on TextEditControl.OnDraw:
        /// Re-draws the text box and centers the blinking caret between characters.
        /// </summary>
        [HarmonyPatch(typeof(TextEditControl), "OnDraw")]
        internal static class TextEditControl_OnDraw_CenteredCaretPatch
        {
            // Cache private fields so we don't look them up every call.
            private static readonly FieldInfo _visibleTextField =
                typeof(TextEditControl).GetField("_visibleText",
                   BindingFlags.Instance | BindingFlags.NonPublic);

            private static readonly FieldInfo _cursorTimerField =
                typeof(TextEditControl).GetField("_cursorTimer",
                    BindingFlags.Instance | BindingFlags.NonPublic);

            /// <summary>
            /// Draw the frame, visible text, and a centered caret, then skip the original OnDraw.
            /// </summary>
            [HarmonyPrefix]
            private static bool Prefix(
                TextEditControl __instance,
                GraphicsDevice device,
                SpriteBatch spriteBatch,
                GameTime gameTime)
            {
                // 1) Frame / background.
                Rectangle screen = __instance.ScreenBounds; // Same as base.ScreenBounds.
                __instance.Frame.Draw(spriteBatch, screen, __instance.FrameColor);

                // 2) Text region + visible text (respects HideInput).
                Rectangle textBounds = __instance.Frame.CenterRegion(__instance.ScreenBounds);

                string vis;
                if (_visibleTextField != null)
                {
                    vis = _visibleTextField.GetValue(__instance) as string ?? string.Empty;
                }
                else
                {
                    // Fallback: Reconstruct from Text / HideInput if reflection fails.
                    string raw = __instance.Text ?? string.Empty;
                    vis = __instance.HideInput ? new string('*', raw.Length) : raw;
                }

                spriteBatch.DrawString(
                    __instance.Font,
                    vis,
                    new Vector2(textBounds.X, textBounds.Y),
                    __instance.TextColor,
                    0f,
                    Vector2.Zero,
                    __instance.Scale,
                    SpriteEffects.None,
                    0f);

                // 3) Blinking caret, but visually centered between characters.
                OneShotTimer timer = null;
                if (_cursorTimerField != null)
                {
                    timer = _cursorTimerField.GetValue(__instance) as OneShotTimer;
                }

                if (__instance.HasFocus && timer != null && timer.PercentComplete < 0.5f)
                {
                    int caretPos = Math.Min(Math.Max(__instance.CursorPos, 0), vis.Length);

                    // Total width of text up to the caret.
                    string left = caretPos > 0 ? vis.Substring(0, caretPos) : string.Empty;
                    Vector2 leftSize = __instance.Font.MeasureString(left) * __instance.Scale;

                    // Size of the "|" glyph, so we can center it on the boundary.
                    Vector2 caretSize = __instance.Font.MeasureString("|") * __instance.Scale;

                    Vector2 caretPosition = new Vector2(
                        textBounds.X + leftSize.X,
                        textBounds.Y);

                    // Origin.X = half glyph width -> visually centered on the boundary.
                    Vector2 caretOrigin = new Vector2(caretSize.X * 0.5f, 0f);

                    spriteBatch.DrawString(
                        __instance.Font,
                        "|",
                        caretPosition,
                        __instance.TextColor,
                        0f,
                        caretOrigin,
                        __instance.Scale,
                        SpriteEffects.None,
                        0f);
                }

                // Skip the original OnDraw - we've fully replaced it.
                return false;
            }
        }
        #endregion

        #region TextEditControl - Left/Right Caret Movement (Single-Step)

        /// <summary>
        /// Harmony postfix on TextEditControl.OnInput:
        /// Lets Left/Right arrow keys move the caret one character at a time when focused.
        /// </summary>
        [HarmonyPatch(typeof(TextEditControl), "OnInput")]
        internal static class TextEditControl_OnInput_LeftRightCaretPatch
        {
            // Global guard so we only handle Left/Right once per frame.
            private static long _lastHandledTicks;

            /// <summary>
            /// Handle Left/Right keys once per frame to nudge CursorPos without double-stepping.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(
                TextEditControl __instance,
                InputManager inputManager,
                GameController controller,
                KeyboardInput chatPad,
                GameTime gameTime)
            {
                // Only act if this textbox is active.
                if (!__instance.HasFocus)
                    return;

                // If OnInput is called multiple times in the same frame, GameTime.TotalGameTime
                // will be identical. In that case, ignore subsequent calls.
                long currentTicks = gameTime.TotalGameTime.Ticks;
                if (currentTicks == _lastHandledTicks)
                    return;

                _lastHandledTicks = currentTicks;

                var keyboard = inputManager.Keyboard;

                bool left  = keyboard.WasKeyPressed(Keys.Left);
                bool right = keyboard.WasKeyPressed(Keys.Right);

                if (!left && !right)
                    return;

                int pos = __instance.CursorPos;

                if (left)
                {
                    pos--;
                }
                else if (right)
                {
                    pos++;
                }

                // Property clamps to [0, Text.Length].
                __instance.CursorPos = pos;
            }
        }
        #endregion

        #region TextEditControl - Preserve Caret Position On Typing

        /// <summary>
        /// Harmony prefix/postfix on TextEditControl.OnChar:
        /// Keeps the caret at the logical edit position instead of snapping to the end.
        /// </summary>
        [HarmonyPatch(typeof(TextEditControl), "OnChar")]
        internal static class TextEditControl_OnChar_PreserveCaretPatch
        {
            // State we stash between Prefix and Postfix.
            internal struct CaretState
            {
                public int PosBefore;
                public int LengthBefore;
            }

            /// <summary>
            /// Capture caret position and text length before the character is processed.
            /// </summary>
            [HarmonyPrefix]
            private static void Prefix(TextEditControl __instance, char c, out CaretState __state)
            {
                string before = __instance.Text ?? string.Empty;

                __state = new CaretState
                {
                    PosBefore = __instance.CursorPos,
                    LengthBefore = before.Length
                };
            }

            /// <summary>
            /// After OnChar runs, adjust CursorPos by the text-length delta to keep it at the edit point.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(TextEditControl __instance, char c, CaretState __state)
            {
                string after = __instance.Text ?? string.Empty;
                int lengthAfter = after.Length;

                // Text changed by this many characters (-1 for backspace, +1 for insert, 0 otherwise).
                int delta = lengthAfter - __state.LengthBefore;

                int newPos = __state.PosBefore + delta;

                // Clamp to [0, lengthAfter].
                if (newPos < 0)
                    newPos = 0;
                if (newPos > lengthAfter)
                    newPos = lengthAfter;

                __instance.CursorPos = newPos;
            }
        }
        #endregion

        #endregion

        #endregion
    }
}