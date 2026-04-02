/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060   // Silence IDE0060.
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Reflection.Emit;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using HarmonyLib;                 // Harmony patching library.
using DNA.Input;
using System;

using static ModLoader.LogSystem; // For Log(...).

namespace LanternLandMap
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

        #region Block Gameplay Input While The Map Is Open (Prevents Stuck Movement / Recenter)

        /// <summary>
        /// Blocks InGameHUD's player-input pipeline while LanternLandMapScreen is open.
        /// This prevents gameplay movement, camera recentering, and "stuck keys" while the map is modal.
        ///
        /// Why this exists:
        /// • CMZ's HUD input path can keep feeding/recentering input even when a custom screen is up.
        /// • Clearing the controller mapping each frame avoids "W stuck down" and similar issues.
        /// </summary>
        [HarmonyPatch]
        internal static class InGameHUD_BlockInputWhileLanternMapOpen
        {
            static MethodBase TargetMethod()
                => AccessTools.Method(
                    typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// If the map is open, clear controls and skip vanilla input handling entirely.
            /// Returns false so the game treats input as "handled" (consistent with many HUD screens).
            /// </summary>
            static bool Prefix(ref bool __result)
            {
                if (!LanternLandMapState.IsOpen)
                    return true; // Run vanilla.

                // Zero out controls so nothing "sticks".
                // NOTE: HUD uses this on focus loss too, so it's a safe "hard reset" method.
                CastleMinerZGame.Instance._controllerMapping.ClearAllControls();

                __result = false; // HUD returns false normally (AcceptInput screen), so keep it consistent.
                return false;     // Skip vanilla -> stops recenter + movement.
            }
        }
        #endregion

        #region Toggle Map Screen + Hot-Reload Config (HUD OnPlayerInput Postfix)

        /// <summary>
        /// Adds Lantern Land Map hotkeys into the main HUD input loop:
        /// • Loads config once (lazy-init) the first time the patch runs.
        /// • Supports config hot-reload (even when the map is closed).
        /// • Toggles the LanternLandMapScreen on/off via the game's UI group stack.
        ///
        /// Notes:
        /// • Postfix is used so we don't interfere with vanilla input processing when the map isn't open.
        /// • Guard avoids opening on top of the crafting/block picker (prevents UI overlap bugs).
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput")]
        internal static class Patch_InGameHUD_ToggleLanternLandMap
        {
            private static bool _loadedConfig;

            /// <summary>
            /// Runs after HUD input is processed; checks our hotkeys and pushes/pops the map screen.
            /// </summary>
            private static void Postfix(InGameHUD __instance, InputManager inputManager)
            {
                try
                {
                    // Lazy init: Don't load config at DLL load time; do it at first real input tick.
                    if (!_loadedConfig)
                    {
                        _loadedConfig = true;
                        LLMConfig.LoadApply();
                        LanternLandMapState.EnsureInitFromConfig();
                    }

                    // Tick hotkeys once (so multiple bindings can be checked safely).
                    LanternLandMapHotkeys.Tick();

                    // Hot reload config (works even if map not open).
                    if (LanternLandMapHotkeys.PressedThisFrame(LLMConfig.ReloadConfigKey))
                    {
                        LLMConfig.LoadApply();

                        // Apply config -> runtime state (so changes take effect immediately).
                        LanternLandMapState.RingsToShow   = LLMConfig.RingsToShow;
                        LanternLandMapState.TargetRadius  = LLMConfig.TargetRadius;
                        LanternLandMapState.ShowChunkGrid = LLMConfig.DefaultShowChunkGrid;

                        // Clamp to safe ranges (prevents broken UI/logic if INI is edited manually).
                        LanternLandMapState.RingsToShow   =
                            Math.Max(LLMConfig.RingsMin, Math.Min(LLMConfig.RingsMax, LanternLandMapState.RingsToShow));

                        LanternLandMapState.TargetRadius  =
                            Math.Max(LLMConfig.RadiusMin, Math.Min(LLMConfig.RadiusMax, LanternLandMapState.TargetRadius));

                        SendFeedback($"[LLMap] Config hot-reloaded from \"{PathShortener.ShortenForLog(LLMConfig.ConfigPath)}\".");
                    }

                    // Toggle map screen.
                    if (LanternLandMapHotkeys.PressedThisFrame(LLMConfig.ToggleMapKey))
                    {
                        var game = DNA.CastleMinerZ.CastleMinerZGame.Instance;
                        var gs   = game?.GameScreen;
                        var ui   = gs?._uiGroup;

                        if (ui != null)
                        {
                            // If already open, close it (always allowed).
                            if (ui.CurrentScreen is LanternLandMapScreen)
                            {
                                ui.PopScreen();
                            }
                            else
                            {
                                // Guard: Don't open while crafting/block picker is up.
                                // NOTE:  This prevents map input + picker input fighting each other.
                                if (gs.IsBlockPickerUp)
                                    return;

                                ui.PushScreen(new LanternLandMapScreen(game));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Toggle patch error: {ex.Message}.");
                }
                finally
                {
                    // Always advance hotkey state (prevents "PressedThisFrame" from sticking).
                    LanternLandMapHotkeys.EndTick();
                }
            }
        }
        #endregion

        #region Hide Crosshair While Map Is Open (HUD OnDraw Transpiler)

        /// <summary>
        /// Suppresses the HUD crosshair rendering while the Lantern Land Map is visible.
        ///
        /// How it works:
        /// • Finds the draw block that renders InGameHUD._crosshairTick (the texture used for the crosshair).
        /// • Injects:
        ///     if (ShouldHideCrosshair()) goto after_crosshair;
        ///   So all crosshair "bar" draws are skipped (covers multiple Draw overloads).
        ///
        /// Notes:
        /// • This is intentionally a "skip the whole crosshair block" approach (more robust than matching each Draw overload).
        /// • If a future CMZ version changes crosshair rendering, the matcher may fail gracefully (patch becomes a no-op).
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnDraw")]
        internal static class Patch_InGameHUD_HideCrosshairWhileLanternMapOpen
        {
            /// <summary>
            /// Returns true when the crosshair should be hidden.
            /// Prefer the explicit IsOpen flag; fall back to checking the current UI screen by name.
            /// </summary>
            private static bool ShouldHideCrosshair()
            {
                // Best option: State flag set in LanternLandMapScreen.OnPushed/OnPoped.
                if (LanternLandMapState.IsOpen)
                    return true;

                // Fallback: Check the active UI screen.
                var cur = CastleMinerZGame.Instance?.GameScreen?._uiGroup?.CurrentScreen;
                return cur != null && cur.GetType().Name == "LanternLandMapScreen";
            }

            private static readonly MethodInfo MI_ShouldHide =
                AccessTools.Method(typeof(Patch_InGameHUD_HideCrosshairWhileLanternMapOpen), nameof(ShouldHideCrosshair));

            /// <summary>
            /// Injects an early branch that skips the crosshair draw section when the map is open.
            /// </summary>
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                var codes = new List<CodeInstruction>(instructions);

                // Locate the field load(s) for _crosshairTick.
                var tickField = AccessTools.Field(typeof(InGameHUD), "_crosshairTick");
                if (tickField == null) return codes;

                var loadIdx = new List<int>();
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldfld && codes[i].operand is FieldInfo fi && fi == tickField)
                        loadIdx.Add(i);
                }

                // Expect multiple loads in vanilla (typically 4 bars), but don't hard-fail if it differs.
                if (loadIdx.Count == 0) return codes;

                int firstLoad = loadIdx[0];
                int lastLoad  = loadIdx[loadIdx.Count - 1];

                // Find the draw call that consumes the last _crosshairTick load.
                int lastDrawCall = -1;
                for (int i = lastLoad; i < codes.Count; i++)
                {
                    if ((codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt) &&
                        codes[i].operand is MethodInfo mi &&
                        mi.Name == "Draw")
                    {
                        lastDrawCall = i;
                        break;
                    }
                }
                if (lastDrawCall < 0) return codes;

                int afterIdx = Math.Min(lastDrawCall + 1, codes.Count - 1);

                // Create the skip label and attach it to the instruction after the last draw.
                Label skip = il.DefineLabel();
                codes[afterIdx].labels.Add(skip);

                // Insert before the first draw's argument setup.
                // Typical pattern near the first draw:
                //   ldarg.2   (spriteBatch)
                //   ldarg.0
                //   ldfld _crosshairTick
                // So firstLoad-2 is usually safe.
                int insertAt = Math.Max(0, firstLoad - 2);

                codes.Insert(insertAt + 0, new CodeInstruction(OpCodes.Call, MI_ShouldHide));
                codes.Insert(insertAt + 1, new CodeInstruction(OpCodes.Brtrue_S, skip));

                return codes;
            }
        }
        #endregion

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
    }
}