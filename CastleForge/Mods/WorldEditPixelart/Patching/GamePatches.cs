/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060 // Silence IDE0060.
using System.Collections.Generic;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using System.Linq;
using HarmonyLib;                  // Harmony patching library.
using DNA.Input;
using System;

using static ModLoader.LogSystem;  // For Log(...).

namespace WorldEditPixelart
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

        #region Patches

        #region Overlay Input Gate

        /// <summary>
        /// Overlay input gate for CastleMiner Z (ScreenGroup-level).
        ///
        /// Goals:
        ///  1) If your overlay (WinForms/ImGui/etc.) is open, STOP the game's input pipeline for that frame.
        ///     -> Prevents any Screen on the stack (including InGameHUD) from consuming input.
        ///  2) While overlay is open, FORCE CaptureMouse = false so InputManager does NOT recenter the mouse.
        ///     -> Prevents artificial mouse deltas that cause camera drift.
        ///  3) While overlay is open, FORCE ShowMouseCursor = true so the OS cursor is visible.
        /// </summary>
        [HarmonyPatch(typeof(ScreenGroup))]
        static class Patch_ScreenGroup_InputGate
        {
            /// <summary>
            /// Kill the input pipeline for this frame when overlay is open.
            /// Prefix fires BEFORE ScreenGroup.ProcessInput(...) would walk the screen stack.
            /// We set __result=true so the caller believes input was handled, then skip the original.
            /// </summary>
            [HarmonyPrefix]
            [HarmonyPatch(nameof(ScreenGroup.ProcessInput))]
            static bool ProcessInput_Prefix(ref bool __result)
            {
                if (WinFormsOverlay.IsOpen)
                {
                    __result = true; // "handled" => Don't continue input processing for this frame.
                    return false;    // Skip the original ProcessInput.
                }
                return true;         // Run original when overlay is closed.
            }

            /// <summary>
            /// While overlay is open, force NO mouse capture.
            /// Postfix runs AFTER the group computed its CaptureMouse value from active screens.
            /// Overriding here prevents InputManager.ResetMouse(...) -> no recenter => no fake deltas/camera drift.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("get_CaptureMouse")]
            static void CaptureMouse_Postfix(ref bool __result)
            {
                if (WinFormsOverlay.IsOpen)
                    __result = false;
            }

            /// <summary>
            /// While overlay is open, ensure OS cursor is visible (useful when capture is off).
            /// Postfix here guarantees a visible cursor even if the active screen wanted it hidden.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("get_ShowMouseCursor")]
            static void ShowMouseCursor_Postfix(ref bool __result)
            {
                if (WinFormsOverlay.IsOpen)
                    __result = true;
            }
        }

        /// <summary>
        /// Prevent controller mapping from producing actions while overlay is open.
        /// This closes any remaining path that could move camera/character even if the ScreenGroup is gated.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZControllerMapping),
                      nameof(CastleMinerZControllerMapping.ProcessInput),
                      new[] { typeof(KeyboardInput), typeof(MouseInput), typeof(GameController) })]
        static class Patch_ControllerMapping_BlockWhenOverlay
        {
            static bool Prefix() => !WinFormsOverlay.IsOpen; // False => Skip original when overlay is open.
        }
        #endregion

        #endregion
    }
}