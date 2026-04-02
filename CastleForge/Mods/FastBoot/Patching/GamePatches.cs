/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060      // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.Drawing.UI;
using System.Linq;
using HarmonyLib;                       // Harmony patching library.
using System;
using DNA;

using static ModLoader.LogSystem;       // For Log(...).

namespace FastBoot
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

        /// <summary>
        /// FastBoot: Neuter the splash/loading screen without touching SecondaryLoad().
        ///
        /// Design:
        ///  - We do NOT stop the screen from being pushed (that can stall loops that wait on the instance).
        ///  - Instead, we mark the LoadScreen instance "Finished" as soon as it exists and every frame thereafter,
        ///    and we skip its draw/update so no timers or fades run and nothing flashes on screen.
        ///
        /// Safety:
        ///  - This targets where LoadScreen exposes 'public bool Finished;' (a FIELD, not a property).
        ///  - NOTE: If this mod later ships with a toggle (config), wrap assignments with 'if (DisableLoadingScreens) { ... }'.
        ///
        /// Order independence:
        ///  - #1 (PushScreen Prefix) hits any time a LoadScreen is pushed into a ScreenGroup.
        ///  - #2 (ctor Postfix) catches instances created but not yet pushed.
        ///  - #3 (OnUpdate Prefix) reasserts Finished and prevents timers from running.
        ///  - #4 (OnDraw Prefix) prevents any visual flicker/black frames.
        /// </summary>

        #region Patches

        /// <summary>
        /// 1) If the game pushes a LoadScreen, flag it finished immediately.
        ///    Why Prefix? We want to set Finished BEFORE the first frame after push, so any loops polling
        ///    this instance (e.g., 'while (!screen.Finished)') can break immediately after the push.
        /// </summary>
        [HarmonyPatch(typeof(ScreenGroup), "PushScreen", new[] { typeof(Screen) })]
        internal static class Patch_ScreenGroup_Push_MarkFinished
        {
            static void Prefix(Screen screen)
            {
                // We do NOT cancel the push. Keeping the instance in the stack preserves expected state,
                // avoids nulls, and keeps downstream code happy. We just make it inert.
                if (screen is LoadScreen ls)
                    ls.Finished = true; // Make the wait-loop condition true right away.
            }
        }

        /// <summary>
        /// 2) Keep the ctor patch (belt & suspenders - helps callers that construct a LoadScreen
        ///    and inspect it before pushing). Postfix runs AFTER the object has been fully constructed.
        /// </summary>
        [HarmonyPatch(typeof(LoadScreen))]
        [HarmonyPatch(MethodType.Constructor)]
        [HarmonyPatch(new[] { typeof(Texture2D), typeof(TimeSpan) })]
        internal static class Patch_LoadScreen_Ctor_FinishNow
        {
            static void Postfix(LoadScreen __instance)
            {
                // Even if someone stashes the instance or checks it pre-push, it's already "done".
                __instance.Finished = true;
            }
        }

        /// <summary>
        /// 3) Short-circuit Update so the screen never "un-finishes" or runs internal timers.
        ///    Why Prefix returning false? It completely skips the original OnUpdate, so none of the
        ///    OneShotTimers (preBlackness, fadeIn, display, fadeOut, postBlackness) advance, and the
        ///    original code never toggles Finished back and forth. We also reassert Finished just in case.
        /// </summary>
        [HarmonyPatch(typeof(LoadScreen), "OnUpdate",
            new[] { typeof(DNAGame), typeof(GameTime) })]
        internal static class Patch_LoadScreen_OnUpdate_ShortCircuit
        {
            static bool Prefix(LoadScreen __instance)
            {
                __instance.Finished = true; // Defensive reassert each frame.
                return false;               // Skip original timer/fade logic entirely.
            }
        }

        /// <summary>
        /// 4) Skip drawing to avoid any flicker/black overlay.
        ///    Why Prefix returning false? Prevents the image/black fades from rendering at all.
        ///    Note: This also skips the original method's 'base.OnDraw(...)' call; that's fine here
        ///    because we want a truly inert screen. ScreenGroup will continue to render others normally.
        /// </summary>
        [HarmonyPatch(typeof(LoadScreen), "OnDraw",
            new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) })]
        internal static class Patch_LoadScreen_OnDraw_Skip
        {
            static bool Prefix()
            {
                return false; // Do not run original draw; nothing from LoadScreen is rendered.
            }
        }
        #endregion
    }
}