/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection.Emit;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;
using DNA.Input;
using System;
using DNA;

using static ModLoader.LogSystem;

/// <summary>
/// NOTES / INTENT
/// - This patch container restores dormant 360-style water scene behavior while keeping
///   the PC branch stable and biome-aware.
/// - The file does three major jobs:
///   1) Applies and removes Harmony patches.
///   2) Maintains live biome-driven water state (water world, level, depth, plane, reflection).
///   3) Rewrites / clamps Player water queries so gameplay, HUD, audio, and tint logic
///      all respect the active biome water band instead of a single global water plane.
/// 
/// DESIGN NOTES
/// - Runtime water state is applied centrally through UpdateEffectiveWaterState(...).
/// - Getter call-site rewrites are used for broad behavior compatibility.
/// - Getter postfix clamps are retained as a defensive fallback for missed call-sites.
/// - Reflection support is optional and removed when disabled.
/// - All fragile game-engine interaction paths are wrapped in try/catch to avoid hard crashes.
/// </summary>
namespace Restore360Water
{
    /// <summary>
    /// Central Harmony patch container for restoring dormant 360-era water scene wiring
    /// and driving biome-aware water-band overrides.
    /// </summary>
    internal static class GamePatches
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
        /// R360WHotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (R360WHotkeys.ReloadPressedThisFrame()) { R360WConfig.LoadApply(); ... }
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
        internal static class R360WHotkeys
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
                Log($"[R360W] Reload hotkey set to \"{s}\".");
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
        /// Keeps the body small; heavy lifting should be inside R360WConfig.LoadApply().
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
                if (!R360WHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    R360WConfig.LoadApply();
                    GamePatches.ApplyLiveState();

                    SendFeedback($"[R360W] Config hot-reloaded from \"{PathShortener.ShortenForLog(R360WConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[R360W] Hot-reload failed: {ex.Message}.");
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

        #region Patch Runtime State / Patcher Lifecycle

        // Tracks the last applied live state so runtime updates only touch the engine
        // when something materially changes.
        private static bool _lastWaterWorldState;
        private static string _lastBiomeLogged;
        private static bool _runtimeStateInitialized;
        private static bool _appliedBiomeEnabled;
        private static string _appliedBiomeName;
        private static float _appliedMinY;
        private static float _appliedMaxY;
        private static float _appliedWaterLevel;
        private static bool _appliedReflectionEnabled;
        private static bool _appliedVanillaUnderwaterEnabled;
        private static bool _waterSceneAttached;
        private static bool _appliedUseNativeBiomeWaterValues;
        private static GameScreen _lastSceneScreen;


        // Small epsilon for live state comparisons so harmless float jitter does not
        // constantly reapply engine state.
        private const float WaterStateEpsilon = 0.05f;

        // Reserved headroom constant. Kept as-is for future tuning / readability.
        private const float WaterActivationHeadroom = 6f;

        #endregion

        #region Live Water Runtime: Core State Application

        /// <summary>
        /// Safely retrieves the active BlockTerrain instance.
        /// </summary>
        public static BlockTerrain TryGetTerrain()
        {
            try { return BlockTerrain.Instance; } catch { return null; }
        }

        /// <summary>
        /// Forces a full reapplication of the current effective water state.
        /// Intended for config reloads / world state refreshes.
        /// </summary>
        public static void ApplyLiveState()
        {
            try
            {
                UpdateEffectiveWaterState(force: true);
            }
            catch (Exception ex)
            {
                Log($"ApplyLiveState failed: {ex.Message}.");
            }
        }

        // When no biome water is active, push the terrain water plane far enough away
        // that dormant vanilla systems do not act like the whole world is flooded.
        private const float HiddenNoWaterLevel = -256f;

        /// <summary>
        /// Applies the effective biome-aware water state to terrain, water plane, and
        /// reflection scene wiring. This is the central runtime authority for water state.
        /// </summary>
        public static void UpdateEffectiveWaterState(bool force = false)
        {
            try
            {
                var terrain = TryGetTerrain();
                var game = CastleMinerZGame.Instance;
                var gs = game?.GameScreen;
                var player = game?.LocalPlayer;
                Vector3 pos = player?.WorldPosition ?? terrain?.EyePos ?? Vector3.Zero;

                string biomeName = "";
                float minY = 0f;
                float maxY = 0f;
                bool useNativeWaterValues = false;
                bool biomeEnabled = R360W_Settings.Enabled &&
                                   R360WBiomeWaterRuntime.TryResolveBand(pos, out biomeName, out minY, out maxY, out useNativeWaterValues);

                if (biomeEnabled && useNativeWaterValues)
                {
                    if (TryGetNativeBiomeWaterBand(biomeName, out float nativeMinY, out float nativeMaxY))
                    {
                        minY = nativeMinY;
                        maxY = nativeMaxY;
                    }
                    else
                    {
                        // No native water definition exists for this biome.
                        // Fall back to the configured band.
                        useNativeWaterValues = false;
                    }
                }

                R360W_Settings.CurrentBiomeName = biomeName ?? "None";
                R360W_Settings.CurrentBiomeEnabled = biomeEnabled;
                R360W_Settings.CurrentWaterMinY = biomeEnabled ? minY : R360W_Settings.GlobalFallbackMinY;
                R360W_Settings.CurrentWaterMaxY = biomeEnabled ? maxY : R360W_Settings.GlobalFallbackMaxY;
                R360W_Settings.CurrentUseNativeBiomeWaterValues = biomeEnabled && useNativeWaterValues;

                bool biomeHasWater = biomeEnabled;

                bool waterWorldActive =
                    R360W_Settings.Enabled &&
                    R360W_Settings.UseVanillaUnderwaterEngine &&
                    biomeHasWater;

                // WaterLevel must stay biome-centric, not player-centric.
                float desiredWaterLevel =
                    biomeHasWater
                        ? maxY
                        : HiddenNoWaterLevel;

                float desiredWaterDepth = biomeHasWater
                    ? Math.Max(0.5f, maxY - minY)
                    : 0.5f;

                bool sceneChanged = !ReferenceEquals(_lastSceneScreen, gs);
                bool stateChanged =
                    force ||
                    !_runtimeStateInitialized ||
                    _lastWaterWorldState != waterWorldActive ||
                    _appliedBiomeEnabled != biomeEnabled ||
                    _appliedReflectionEnabled != R360W_Settings.EnableReflection ||
                    _appliedVanillaUnderwaterEnabled != R360W_Settings.UseVanillaUnderwaterEngine ||
                    _appliedUseNativeBiomeWaterValues != R360W_Settings.CurrentUseNativeBiomeWaterValues ||
                    !SameText(_appliedBiomeName, R360W_Settings.CurrentBiomeName) ||
                    !FloatsNear(_appliedMinY, R360W_Settings.CurrentWaterMinY) ||
                    !FloatsNear(_appliedMaxY, R360W_Settings.CurrentWaterMaxY) ||
                    !FloatsNear(_appliedWaterLevel, desiredWaterLevel) ||
                    sceneChanged;

                if (!stateChanged)
                    return;

                // Terrain state.
                if (terrain != null)
                {
                    if (terrain.IsWaterWorld != waterWorldActive)
                        terrain.IsWaterWorld = waterWorldActive;

                    if (!R360W_Settings.CurrentUseNativeBiomeWaterValues)
                    {
                        if (!FloatsNear(terrain.WaterLevel, desiredWaterLevel))
                            terrain.WaterLevel = desiredWaterLevel;

                        if (terrain._worldBuilder != null &&
                            !FloatsNear(terrain._worldBuilder.WaterDepth, desiredWaterDepth))
                        {
                            terrain._worldBuilder.WaterDepth = desiredWaterDepth;
                        }
                    }
                }

                // Water plane scene attachment.
                if (biomeHasWater && R360W_Settings.AttachWaterPlane)
                {
                    EnsureWaterPlaneExists();

                    if (gs != null)
                    {
                        bool needsAttach =
                            force ||
                            sceneChanged ||
                            !_waterSceneAttached ||
                            R360WWaterPlane.Instance == null ||
                            !ReferenceEquals(R360WWaterPlane.Instance.Parent, gs.mainScene);

                        if (needsAttach)
                            EnsureWaterSceneAttached(gs);

                        _waterSceneAttached =
                            R360WWaterPlane.Instance != null &&
                            ReferenceEquals(R360WWaterPlane.Instance.Parent, gs.mainScene);
                    }
                    else
                    {
                        _waterSceneAttached = false;
                    }
                }
                else
                {
                    bool planeAttachedSomewhere =
                        R360WWaterPlane.Instance != null &&
                        R360WWaterPlane.Instance.Parent != null;

                    if (force || _waterSceneAttached || planeAttachedSomewhere)
                        DetachWaterPlane();

                    _waterSceneAttached = false;
                }

                // Reflection scene cleanup when reflections are disabled.
                if (gs != null)
                {
                    if (!R360W_Settings.EnableReflection)
                    {
                        if (force || sceneChanged || _appliedReflectionEnabled)
                            RemoveReflectionSceneAttached(gs);
                    }
                }

                // Cache the last applied state.
                _runtimeStateInitialized = true;
                _lastSceneScreen = gs;
                _lastWaterWorldState = waterWorldActive;
                _appliedBiomeEnabled = biomeEnabled;
                _appliedBiomeName = R360W_Settings.CurrentBiomeName;
                _appliedMinY = R360W_Settings.CurrentWaterMinY;
                _appliedMaxY = R360W_Settings.CurrentWaterMaxY;
                _appliedWaterLevel = desiredWaterLevel;
                _appliedReflectionEnabled = R360W_Settings.EnableReflection;
                _appliedVanillaUnderwaterEnabled = R360W_Settings.UseVanillaUnderwaterEngine;
                _appliedUseNativeBiomeWaterValues = R360W_Settings.CurrentUseNativeBiomeWaterValues;

                if (R360W_Settings.DoLogging)
                {
                    Log($"Biome={R360W_Settings.CurrentBiomeName}, Enabled={R360W_Settings.CurrentBiomeEnabled}, MinY={R360W_Settings.CurrentWaterMinY:0.##}, MaxY={R360W_Settings.CurrentWaterMaxY:0.##}, PlaneVisible={biomeHasWater}, LiveWaterWorld={waterWorldActive}, VanillaUnderwater={R360W_Settings.UseVanillaUnderwaterEngine}, Force={force}, SceneChanged={sceneChanged}.");
                    _lastBiomeLogged = R360W_Settings.CurrentBiomeName;
                }
            }
            catch (Exception ex)
            {
                Log($"UpdateEffectiveWaterState failed: {ex.Message}.");
            }
        }

        /// <summary>
        /// Float comparison helper for runtime state change detection.
        /// </summary>
        private static bool FloatsNear(float a, float b)
        {
            return Math.Abs(a - b) <= WaterStateEpsilon;
        }

        /// <summary>
        /// Case-insensitive text comparison helper.
        /// </summary>
        private static bool SameText(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves hard native vanilla water bands for known vanilla biomes.
        /// Returns false when the biome has no defined native water band.
        /// </summary>
        private static bool TryGetNativeBiomeWaterBand(string biomeName, out float minY, out float maxY)
        {
            minY = 0f;
            maxY = 0f;

            switch ((biomeName ?? string.Empty).Trim())
            {
                case "Coastal":
                case "CoastalBiome":
                case "CostalBiome":
                    maxY = -31.5f;
                    minY = maxY - 36f; // vanilla CostalBiome.WaterDepth = 36f
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Live Water Runtime: Plane / Scene Wiring

        /// <summary>
        /// Creates the shared water plane instance on demand.
        /// Safe to call repeatedly.
        /// </summary>
        public static void EnsureWaterPlaneExists()
        {
            try
            {
                if (!R360W_Settings.Enabled || !R360W_Settings.AttachWaterPlane)
                    return;

                if (R360WWaterPlane.Instance != null)
                    return;

                var game = CastleMinerZGame.Instance;
                if (game == null || game.GraphicsDevice == null || game.Content == null)
                    return;

                new R360WWaterPlane(game.GraphicsDevice, game.Content);
                if (R360W_Settings.DoLogging)
                    Log("WaterPlane created.");
            }
            catch (Exception ex)
            {
                Log($"Failed creating WaterPlane: {ex.Message}.");
            }
        }

        /// <summary>
        /// Detaches the shared water plane from whatever parent scene currently owns it.
        /// </summary>
        public static void DetachWaterPlane()
        {
            try
            {
                if (R360WWaterPlane.Instance != null && R360WWaterPlane.Instance.Parent != null)
                    R360WWaterPlane.Instance.RemoveFromParent();
            }
            catch (Exception ex)
            {
                Log($"Failed detaching WaterPlane: {ex.Message}.");
            }
        }

        /// <summary>
        /// Ensures the water plane and optional reflection camera/view are attached
        /// to the current GameScreen scene graph.
        /// </summary>
        public static void EnsureWaterSceneAttached(GameScreen gameScreen)
        {
            try
            {
                if (!R360W_Settings.Enabled || gameScreen == null || gameScreen.mainScene == null)
                    return;

                EnsureWaterPlaneExists();
                if (R360WWaterPlane.Instance == null)
                    return;

                if (R360WWaterPlane.Instance.Parent != null && R360WWaterPlane.Instance.Parent != gameScreen.mainScene)
                    R360WWaterPlane.Instance.RemoveFromParent();

                if (!gameScreen.mainScene.Children.Contains(R360WWaterPlane.Instance))
                    gameScreen.mainScene.Children.Add(R360WWaterPlane.Instance);

                if (!R360W_Settings.EnableReflection)
                {
                    RemoveReflectionSceneAttached(gameScreen);
                    return;
                }

                ReflectionCamera reflectionCamera = null;
                for (int i = 0; i < gameScreen.mainScene.Children.Count; i++)
                {
                    reflectionCamera = gameScreen.mainScene.Children[i] as ReflectionCamera;
                    if (reflectionCamera != null)
                        break;
                }

                if (reflectionCamera == null)
                {
                    reflectionCamera = new ReflectionCamera();
                    gameScreen.mainScene.Children.Add(reflectionCamera);
                }

                SceneScreen sceneScreen = GetSceneScreen(gameScreen);
                if (sceneScreen == null)
                    return;

                bool alreadyAdded = false;
                for (int i = 0; i < sceneScreen.Views.Count; i++)
                {
                    if (!(sceneScreen.Views[i] is CameraView cv)) continue;
                    if (object.ReferenceEquals(cv.Camera, reflectionCamera))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                {
                    CameraView reflectionView = new CameraView(CastleMinerZGame.Instance,
                                                        R360WWaterPlane.Instance.ReflectionTexture,
                                                        reflectionCamera,
                                                        GameScreen.FilterWorldGeo);
                    AttachPreDrawReflection(gameScreen, reflectionView);
                    sceneScreen.Views.Insert(0, reflectionView);
                }
            }
            catch (Exception ex)
            {
                Log($"Failed attaching water scene: {ex.Message}.");
            }
        }

        #endregion

        #region Reflection / Screen Helpers

        /// <summary>
        /// Retrieves the SceneScreen owned by the given GameScreen through the internal screen list.
        /// </summary>
        private static SceneScreen GetSceneScreen(GameScreen gameScreen)
        {
            try
            {
                var field = typeof(ScreenGroup).GetField("screensList", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(field?.GetValue(gameScreen) is Screen[] list)) return null;

                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] is SceneScreen sceneScreen)
                        return sceneScreen;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Wires the GameScreen.PreDrawReflection handler into the generated reflection view.
        /// </summary>
        private static void AttachPreDrawReflection(GameScreen gameScreen, View reflectionView)
        {
            try
            {
                var mi = typeof(GameScreen).GetMethod("PreDrawReflection", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi == null) return;

                if (Delegate.CreateDelegate(typeof(EventHandler<DrawEventArgs>), gameScreen, mi, false) is EventHandler<DrawEventArgs> del)
                    reflectionView.BeforeDraw += del;
            }
            catch (Exception ex)
            {
                Log($"Failed wiring PreDrawReflection: {ex.Message}.");
            }
        }

        /// <summary>
        /// Removes reflection-related scene objects previously attached by this runtime.
        /// Note: this removes ReflectionCamera children/views broadly from the active scene.
        /// </summary>
        private static void RemoveReflectionSceneAttached(GameScreen gameScreen)
        {
            try
            {
                if (gameScreen?.mainScene == null)
                    return;

                for (int i = gameScreen.mainScene.Children.Count - 1; i >= 0; i--)
                {
                    if (gameScreen.mainScene.Children[i] is ReflectionCamera rc)
                        gameScreen.mainScene.Children.RemoveAt(i);
                }

                SceneScreen sceneScreen = GetSceneScreen(gameScreen);
                if (sceneScreen == null)
                    return;

                for (int i = sceneScreen.Views.Count - 1; i >= 0; i--)
                {
                    if (!(sceneScreen.Views[i] is CameraView cv)) continue;
                    if (cv.Camera is ReflectionCamera)
                        sceneScreen.Views.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                Log($"Failed removing reflection scene state: {ex.Message}.");
            }
        }

        #endregion

        #region Mod Content Helpers

        /// <summary>
        /// Lightweight mod-local content loader for stream-based texture loading.
        /// </summary>
        internal static class ModContent
        {
            public static string ModRoot => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "Restore360Water");

            /// <summary>
            /// Loads a texture from the mod folder if it exists; otherwise returns null.
            /// </summary>
            public static Texture2D LoadTexture(GraphicsDevice gd, string relativePath)
            {
                try
                {
                    string path = System.IO.Path.Combine(ModRoot, relativePath.Replace('/', '\\'));
                    if (!System.IO.File.Exists(path))
                        return null;

                    using (var fs = System.IO.File.OpenRead(path))
                        return Texture2D.FromStream(gd, fs);
                }
                catch
                {
                    return null;
                }
            }
        }
        #endregion

        #region Water Runtime Boot / Scene Entry Patches

        /// <summary>
        /// Ensures water resources exist after the game's secondary load phase.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "SecondaryLoad")]
        internal static class Patch_CastleMinerZGame_SecondaryLoad_RestoreWaterResources
        {
            private static void Postfix()
            {
                if (R360W_Settings.Enabled)
                    EnsureWaterPlaneExists();
            }
        }

        /// <summary>
        /// Initializes terrain water-world state to a safe dormant baseline,
        /// then applies the active biome-aware runtime state.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), nameof(BlockTerrain.Init))]
        internal static class Patch_BlockTerrain_Init_EnableWaterWorld
        {
            private static void Postfix(BlockTerrain __instance)
            {
                if (__instance == null) return;

                __instance.IsWaterWorld = false;

                __instance.WaterLevel = HiddenNoWaterLevel;

                if (__instance._worldBuilder != null)
                    __instance._worldBuilder.WaterDepth = 0.5f;

                UpdateEffectiveWaterState(force: true);
            }
        }

        /// <summary>
        /// Refreshes runtime water state and scene wiring when the GameScreen initializes.
        /// Note: target name is intentionally kept exactly as supplied.
        /// </summary>
        [HarmonyPatch(typeof(GameScreen), "Inialize")]
        internal static class Patch_GameScreen_Inialize_RestoreWaterScene
        {
            private static void Postfix(GameScreen __instance)
            {
                UpdateEffectiveWaterState();
                if (R360W_Settings.Enabled)
                    EnsureWaterSceneAttached(__instance);
            }
        }

        #endregion

        #region HUD Runtime Patch: Live State Refresh / Drowning Compatibility

        /// <summary>
        /// Refreshes water state each HUD update, rewrites water getter call-sites used by the HUD,
        /// and preserves drowning-death behavior when the local player is underwater in a valid band.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnUpdate", new Type[] { typeof(DNAGame), typeof(GameTime) })]
        internal static class Patch_InGameHUD_OnUpdate_WaterRuntime
        {
            private static void Prefix()
            {
                UpdateEffectiveWaterState();
            }

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }

            private static void Postfix(InGameHUD __instance)
            {
                try
                {
                    if (!R360W_Settings.Enabled || !R360W_Settings.UseVanillaUnderwaterEngine || __instance == null)
                        return;

                    var terrain = BlockTerrain.Instance;
                    if (terrain == null || !terrain.IsWaterWorld)
                        return;

                    var localPlayer = __instance.LocalPlayer;
                    if (localPlayer == null || localPlayer.Dead)
                        return;

                    if (!GetBandLimitedUnderwater(localPlayer))
                        return;

                    if (__instance.PlayerOxygen > 0f || __instance.PlayerHealth > 0f)
                        return;

                    __instance.PlayerOxygen = 0f;
                    __instance.PlayerHealth = 0f;
                    __instance.KillPlayer();
                }
                catch (Exception ex)
                {
                    Log($"Drowning death compatibility patch failed: {ex.Message}.");
                }
            }
        }

        #endregion

        #region HUD Safety: Clamp Drowning Health / Oxygen

        /// <summary>
        /// Prevents underwater death from pushing HUD health below zero.
        ///
        /// Why this patch exists:
        /// - Vanilla subtracts oxygen health penalty inside InGameHUD.OnUpdate(...).
        /// - That drowning block runs outside the later "!LocalPlayer.Dead" guard.
        /// - If the player dies underwater, health can continue drifting below 0 on later ticks.
        ///
        /// What this patch does:
        /// - Runs after the vanilla HUD update.
        /// - Clamps health to a minimum of 0.
        /// - Clamps oxygen to the valid 0..1 range.
        /// - Optionally refills oxygen when dead so corpse/respawn state does not keep looking "out of breath".
        ///
        /// Notes:
        /// - This is a safe, minimal fix.
        /// - It does not change the rest of the vanilla death / respawn flow.
        /// - If you want, you can remove the dead-player oxygen refill line and only keep the clamps.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnUpdate")]
        internal static class InGameHUD_OnUpdate_ClampDrowningVitals
        {
            static void Postfix(InGameHUD __instance)
            {
                if (__instance == null)
                    return;

                try
                {
                    // Hard clamp health so underwater death cannot drive HP negative.
                    if (__instance.PlayerHealth < 0f)
                        __instance.PlayerHealth = 0f;
                    else if (__instance.PlayerHealth > 1f)
                        __instance.PlayerHealth = 1f;

                    // Keep oxygen in a sane range too.
                    if (__instance.PlayerOxygen < 0f)
                        __instance.PlayerOxygen = 0f;
                    else if (__instance.PlayerOxygen > 1f)
                        __instance.PlayerOxygen = 1f;

                    // Optional:
                    // Once dead, stop the HUD from sitting at negative/empty drowning state.
                    if (__instance.LocalPlayer != null && __instance.LocalPlayer.Dead)
                    {
                        __instance.PlayerHealth = 0f;
                        __instance.PlayerOxygen = 1f;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Clamp drowning vitals patch failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Player Water Helper Replacements

        /// <summary>
        /// Biome-aware replacement for Player.PercentSubmergedWater.
        /// Returns 0 when no valid water band is active.
        /// </summary>
        public static float GetBandLimitedPercentSubmergedWater(Player player)
        {
            try
            {
                if (player == null)
                    return 0f;

                if (!R360W_Settings.Enabled || !R360W_Settings.UseVanillaUnderwaterEngine)
                {
                    try { return player.PercentSubmergedWater; }
                    catch { return 0f; }
                }

                var terrain = TryGetTerrain();
                if (terrain == null || !terrain.IsWaterWorld)
                    return 0f;

                if (!R360W_Settings.CurrentBiomeEnabled)
                    return 0f;

                float y = player.WorldPosition.Y;
                float minY = R360W_Settings.CurrentWaterMinY;
                float maxY = R360W_Settings.CurrentWaterMaxY;

                if (minY > maxY)
                {
                    (maxY, minY) = (minY, maxY);
                }

                if (y > maxY || y < minY)
                    return 0f;

                return MathHelper.Clamp(maxY - y, 0f, 1f);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Biome-aware replacement for Player.InWater.
        /// </summary>
        public static bool GetBandLimitedInWater(Player player)
        {
            try
            {
                if (player == null)
                    return false;

                if (!R360W_Settings.Enabled || !R360W_Settings.UseVanillaUnderwaterEngine)
                {
                    try { return player.InWater; }
                    catch { return false; }
                }

                var terrain = TryGetTerrain();
                if (terrain == null || !terrain.IsWaterWorld)
                    return false;

                if (!R360W_Settings.CurrentBiomeEnabled)
                    return false;

                float y = player.WorldPosition.Y;
                float minY = R360W_Settings.CurrentWaterMinY;
                float maxY = R360W_Settings.CurrentWaterMaxY;

                if (minY > maxY)
                {
                    (maxY, minY) = (minY, maxY);
                }

                return y <= maxY && y >= minY;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Biome-aware replacement for Player.Underwater.
        /// Uses a minimum depth threshold inside the active water band.
        /// </summary>
        public static bool GetBandLimitedUnderwater(Player player)
        {
            try
            {
                if (player == null)
                    return false;

                if (!R360W_Settings.Enabled || !R360W_Settings.UseVanillaUnderwaterEngine)
                {
                    try { return player.Underwater; }
                    catch { return false; }
                }

                var terrain = TryGetTerrain();
                if (terrain == null || !terrain.IsWaterWorld)
                    return false;

                if (!R360W_Settings.CurrentBiomeEnabled)
                    return false;

                float y = player.WorldPosition.Y;
                float minY = R360W_Settings.CurrentWaterMinY;
                float maxY = R360W_Settings.CurrentWaterMaxY;

                if (minY > maxY)
                {
                    (maxY, minY) = (minY, maxY);
                }

                if (y > maxY || y < minY)
                    return false;

                return (maxY - y) >= 1.5f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rewrites direct calls to Player water getters so patched methods consume the
        /// biome-aware helper versions instead of the original global-water implementations.
        /// </summary>
        private static IEnumerable<CodeInstruction> ReplacePlayerWaterGetterCalls(IEnumerable<CodeInstruction> instructions)
        {
            var percentGetter = AccessTools.PropertyGetter(typeof(Player), "PercentSubmergedWater");
            var inWaterGetter = AccessTools.PropertyGetter(typeof(Player), "InWater");
            var underwaterGetter = AccessTools.PropertyGetter(typeof(Player), "Underwater");

            var percentHelper = AccessTools.Method(typeof(GamePatches), nameof(GetBandLimitedPercentSubmergedWater));
            var inWaterHelper = AccessTools.Method(typeof(GamePatches), nameof(GetBandLimitedInWater));
            var underwaterHelper = AccessTools.Method(typeof(GamePatches), nameof(GetBandLimitedUnderwater));

            foreach (var code in instructions)
            {
                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo mi)
                {
                    if (mi == percentGetter)
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = percentHelper;
                    }
                    else if (mi == inWaterGetter)
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = inWaterHelper;
                    }
                    else if (mi == underwaterGetter)
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = underwaterHelper;
                    }
                }

                yield return code;
            }
        }

        #endregion

        #region Player / HUD / Audio Call-Site Rewrites

        /// <summary>
        /// Rewrites water getter usage inside Player.OnUpdate.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnUpdate", new Type[] { typeof(GameTime) })]
        internal static class Patch_Player_OnUpdate_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        /// <summary>
        /// Rewrites water getter usage inside Player.ProcessInput.
        /// </summary>
        [HarmonyPatch(typeof(Player), "ProcessInput", new Type[] { typeof(FPSControllerMapping), typeof(GameTime) })]
        internal static class Patch_Player_ProcessInput_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        /// <summary>
        /// Rewrites water getter usage inside Player.UpdateAnimation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateAnimation", new Type[] { typeof(float), typeof(float), typeof(Angle), typeof(PlayerMode), typeof(bool) })]
        internal static class Patch_Player_UpdateAnimation_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        /// <summary>
        /// Rewrites water getter usage inside Player.UpdateAudio.
        /// Important for splash / underwater / water-adjacent audio behavior.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateAudio", new Type[] { typeof(GameTime) })]
        internal static class Patch_Player_UpdateAudio_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        /// <summary>
        /// Rewrites water getter usage inside Player.CanJump.
        /// </summary>
        [HarmonyPatch(typeof(Player), "CanJump", MethodType.Getter)]
        internal static class Patch_Player_get_CanJump_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        /// <summary>
        /// Rewrites water getter usage inside CastleMinerZGame.SetAudio.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "SetAudio", new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) })]
        internal static class Patch_CastleMinerZGame_SetAudio_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        /// <summary>
        /// Rewrites water getter usage inside InGameHUD.OnDraw.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnDraw", new Type[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) })]
        internal static class Patch_InGameHUD_OnDraw_BandAwareCalls
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplacePlayerWaterGetterCalls(instructions);
            }
        }

        #endregion

        #region Terrain Visual Compatibility Patches

        /// <summary>
        /// Recomputes underwater sky tint against the currently active biome water band
        /// instead of a single world-wide water plane.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), "GetUnderwaterSkyTint")]
        internal static class Patch_BlockTerrain_GetUnderwaterSkyTint_CurrentBand
        {
            private static bool Prefix(BlockTerrain __instance, ref Vector3 color, ref float __result)
            {
                if (!R360W_Settings.Enabled || !R360W_Settings.UseVanillaUnderwaterEngine)
                    return true;

                if (__instance == null || !__instance.IsWaterWorld || !R360W_Settings.CurrentBiomeEnabled)
                {
                    color = Vector3.Zero;
                    __result = 0f;
                    return false;
                }

                float minY = R360W_Settings.CurrentWaterMinY;
                float maxY = R360W_Settings.CurrentWaterMaxY;
                float eyeY = __instance.EyePos.Y;

                if (eyeY > maxY || eyeY < minY)
                {
                    color = Vector3.Zero;
                    __result = 0f;
                    return false;
                }

                float bandDepth = Math.Max(0.001f, maxY - minY);
                float depth = MathHelper.Clamp(maxY - eyeY, 0f, bandDepth);

                float tint = Math.Min(depth / 12f, 1f - __instance.SunlightColor.ToVector3().X * __instance.SunlightColor.ToVector3().X);

                color = __instance.GetActualWaterColor();
                __result = tint;
                return false;
            }
        }

        #endregion

        #region Player Water State: Band Clamp (Safe Postfixes)

        /// <summary>
        /// Safely gathers the current player's active biome water band.
        /// Used by getter postfixes as a defensive fallback for any remaining unrewritten calls.
        /// </summary>
        private static bool TryGetSafePlayerWaterBand(Player player, out float y, out float minY, out float maxY)
        {
            y = 0f;
            minY = 0f;
            maxY = 0f;

            try
            {
                if (!R360W_Settings.Enabled || !R360W_Settings.UseVanillaUnderwaterEngine)
                    return false;

                var terrain = BlockTerrain.Instance;
                if (terrain == null || !terrain.IsWaterWorld)
                    return false;

                if (!R360W_Settings.CurrentBiomeEnabled)
                    return false;

                if (player == null)
                    return false;

                y = player.WorldPosition.Y;
                minY = R360W_Settings.CurrentWaterMinY;
                maxY = R360W_Settings.CurrentWaterMaxY;

                if (minY > maxY)
                {
                    (maxY, minY) = (minY, maxY);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Getter-level safety clamp for Player.InWater.
        /// This acts as a fallback in case any direct getter usages escape transpiler replacement.
        /// </summary>
        [HarmonyPatch(typeof(Player), "InWater", MethodType.Getter)]
        internal static class Patch_Player_get_InWater_BandClamp
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance, ref bool __result)
            {
                try
                {
                    if (!TryGetSafePlayerWaterBand(__instance, out float y, out float minY, out float maxY))
                        return;

                    if (y > maxY || y < minY)
                        __result = false;
                }
                catch { }
            }
        }

        /// <summary>
        /// Getter-level safety clamp for Player.Underwater.
        /// </summary>
        [HarmonyPatch(typeof(Player), "Underwater", MethodType.Getter)]
        internal static class Patch_Player_get_Underwater_BandClamp
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance, ref bool __result)
            {
                try
                {
                    if (!TryGetSafePlayerWaterBand(__instance, out float y, out float minY, out float maxY))
                        return;

                    if (y > maxY || y < minY)
                        __result = false;
                }
                catch { }
            }
        }

        #endregion

        #region Reflection Compatibility Overrides

        /// <summary>
        /// Replaces CastleMinerSky reflection drawing with the SkySphere base draw path
        /// as a compatibility fallback.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerSky), nameof(CastleMinerSky.DrawReflection))]
        internal static class Patch_CastleMinerSky_DrawReflection_Compat
        {
            private static bool Prefix(CastleMinerSky __instance, GraphicsDevice device, GameTime gameTime, Matrix view, Matrix projection)
            {
                try
                {
                    SkySphere_Draw_Base(__instance, device, gameTime, view, projection);
                }
                catch (Exception ex)
                {
                    Log($"CastleMinerSky reflection fallback failed: {ex.Message}.");
                }

                return false;
            }

            /// <summary>
            /// Harmony reverse-patch stub for invoking the SkySphere base draw implementation.
            /// </summary>
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(SkySphere), nameof(SkySphere.Draw))]
            private static void SkySphere_Draw_Base(SkySphere __instance, GraphicsDevice device, GameTime gameTime, Matrix view, Matrix projection)
            {
                throw new NotImplementedException("Stub for Harmony reverse patch.");
            }
        }

        /// <summary>
        /// Suppresses BlockTerrain.DrawReflection to avoid conflicting / duplicate reflection rendering.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), nameof(BlockTerrain.DrawReflection))]
        internal static class Patch_BlockTerrain_DrawReflection_Compat
        {
            private static bool Prefix()
            {
                return false;
            }
        }

        #endregion
    }
}