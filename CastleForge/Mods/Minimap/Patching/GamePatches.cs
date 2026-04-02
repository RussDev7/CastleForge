/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060      // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using System.Linq;
using HarmonyLib;                       // Harmony patching library.
using DNA.Input;
using System;

using static Minimap.MinimapRenderer;
using static Minimap.MinimapConfig;
using static ModLoader.LogSystem;       // For Log(...).

namespace Minimap
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

        #region FieldRefs / Reflection Handles

        /// <summary>
        /// FieldRef for the private _hideUI flag on <see cref="InGameHUD"/>.
        /// Summary: Allows us to respect the game's UI-hidden state and skip drawing.
        /// </summary>
        // Private HUD field: _hideUI.
        private static readonly AccessTools.FieldRef<InGameHUD, bool> HideUiRef =
            AccessTools.FieldRefAccess<InGameHUD, bool>("_hideUI");

        /// <summary>
        /// FieldRef for the game's small font (_smallFont), if present.
        /// Summary: Best-effort font retrieval for minimap readouts and compass text.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Some builds may not expose the font publicly; FieldRefAccess keeps this resilient.
        /// - If the font is missing, the overlay draw patch safely bails out.
        /// </remarks>
        // Fonts are often private fields on CastleMinerZGame; use reflection so this works even if they're not public.
        private static readonly AccessTools.FieldRef<CastleMinerZGame, SpriteFont> SmallFontRef =
            AccessTools.FieldRefAccess<CastleMinerZGame, SpriteFont>("_smallFont");

        #endregion

        #region Hotkey Helper

        /// <summary>
        /// Evaluates a <see cref="HotkeyBinding"/> against the current input state.
        /// Summary: Returns true only when the main key is pressed this frame and required modifiers are held.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Uses WasKeyPressed for edge detection (one-shot) rather than IsKeyDown.
        /// - Modifiers are treated as "required if set" (Ctrl/Shift/Alt).
        /// </remarks>
        private static bool HotkeyPressed(InputManager input, HotkeyBinding hk)
        {
            if (hk.IsEmpty) return false;
            if (!input.Keyboard.WasKeyPressed(hk.Key)) return false;

            if (hk.Ctrl && !(input.Keyboard.IsKeyDown(Keys.LeftControl) || input.Keyboard.IsKeyDown(Keys.RightControl))) return false;
            if (hk.Shift && !(input.Keyboard.IsKeyDown(Keys.LeftShift) || input.Keyboard.IsKeyDown(Keys.RightShift))) return false;
            if (hk.Alt && !(input.Keyboard.IsKeyDown(Keys.LeftAlt) || input.Keyboard.IsKeyDown(Keys.RightAlt))) return false;

            return true;
        }
        #endregion

        #region Patch: InGameHUD.OnPlayerInput (Hotkeys)

        /// <summary>
        /// Hotkey handler patch for <see cref="InGameHUD.OnPlayerInput"/>.
        /// Summary:
        /// - One-time loads config + initializes runtime state.
        /// - Handles reload, toggle minimap visibility, toggle chunk grid, and zoom in/out.
        /// - Persists changed values back into the INI so they survive restarts.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Skips hotkeys while chat is active to avoid fighting the typing UX.
        /// - All logic is best-effort; any exception is swallowed to avoid breaking HUD input.
        /// </remarks>
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput")]
        internal static class Patch_InGameHUD_MinimapHotkeys
        {
            /// <summary>One-time init guard for config/state setup.</summary>
            private static bool _loaded;

            private static void Postfix(InGameHUD __instance, InputManager inputManager)
            {
                try
                {
                    if (__instance == null || inputManager == null) return;

                    // Don't trigger hotkeys while chat is focused.
                    if (__instance.IsChatting) return;

                    // One-time boot: Load config and seed runtime defaults.
                    if (!_loaded)
                    {
                        _loaded = true;
                        MinimapConfig.LoadApply();
                        MinimapState.EnsureInitFromConfig();
                    }

                    // Reload config (keeps current visibility; clamps zoom for safety).
                    if (HotkeyPressed(inputManager, MinimapConfig.ReloadConfigKey))
                    {
                        MinimapConfig.LoadApply();

                        // Keep current Visible as-is (quality-of-life), but clamp zoom.
                        if (MinimapState.ZoomPixelsPerBlock < MinimapConfig.ZoomMin) MinimapState.ZoomPixelsPerBlock = MinimapConfig.ZoomMin;
                        if (MinimapState.ZoomPixelsPerBlock > MinimapConfig.ZoomMax) MinimapState.ZoomPixelsPerBlock = MinimapConfig.ZoomMax;

                        SendFeedback($"[MMap] Config hot-reloaded from \"{PathShortener.ShortenForLog(MinimapConfig.ConfigPath)}\".");
                    }

                    // Toggle minimap visibility (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.ToggleMapKey))
                    {
                        MinimapState.Visible = !MinimapState.Visible;
                        MinimapConfig.PersistEnabled(MinimapState.Visible);
                    }

                    // Toggle chunk grid overlay (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.ToggleChunkGridKey))
                    {
                        MinimapState.ShowChunkGrid = !MinimapState.ShowChunkGrid;
                        MinimapConfig.PersistChunkGrid(MinimapState.ShowChunkGrid);
                    }

                    // Cycle minimap corner (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.CycleLocationKey))
                    {
                        MinimapConfig.MinimapLocation = NextAnchor(MinimapConfig.MinimapLocation);
                        MinimapConfig.PersistLocation(MinimapConfig.MinimapLocation);
                        // SendFeedback($"[MMap] Location: {MinimapConfig.MinimapLocation}.");
                    }

                    // Zoom in (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.ZoomInHotkey))
                    {
                        MinimapState.ZoomPixelsPerBlock *= MinimapConfig.ZoomStepMul;
                        if (MinimapState.ZoomPixelsPerBlock > MinimapConfig.ZoomMax)
                            MinimapState.ZoomPixelsPerBlock = MinimapConfig.ZoomMax;

                        MinimapConfig.PersistZoom(MinimapState.ZoomPixelsPerBlock);
                    }

                    // Zoom out (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.ZoomOutHotkey))
                    {
                        MinimapState.ZoomPixelsPerBlock /= MinimapConfig.ZoomStepMul;
                        if (MinimapState.ZoomPixelsPerBlock < MinimapConfig.ZoomMin)
                            MinimapState.ZoomPixelsPerBlock = MinimapConfig.ZoomMin;

                        MinimapConfig.PersistZoom(MinimapState.ZoomPixelsPerBlock);
                    }

                    // Toggle facing indicator (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.ToggleIndicatorKey))
                    {
                        MinimapConfig.PlayerFacingIndicator = !MinimapConfig.PlayerFacingIndicator;
                        MinimapConfig.PersistFacingIndicator(MinimapConfig.PlayerFacingIndicator);
                    }

                    // Toggle enemies + dragon markers (persisted).
                    if (HotkeyPressed(inputManager, MinimapConfig.ToggleMobsKey))
                    {
                        // If either is currently shown, turn both OFF; otherwise turn both ON.
                        bool enable = !(MinimapConfig.ShowEnemies || MinimapConfig.ShowDragon);

                        MinimapConfig.ShowEnemies = enable;
                        MinimapConfig.ShowDragon  = enable;

                        MinimapConfig.PersistShowEnemies(enable);
                        MinimapConfig.PersistShowDragon(enable);
                    }
                }
                catch
                {
                    // Best-effort; never break HUD input.
                }
            }
        }

        /// <summary>
        /// Returns the next minimap anchor in the cycle:
        /// TopLeft -> TopRight -> BottomLeft -> BottomRight -> (wrap).
        /// </summary>
        private static MinimapAnchor NextAnchor(MinimapAnchor a)
        {
            int v = (int)a;
            if (v < 0 || v > 3) v = 0;       // safety
            v = (v + 1) % 4;
            return (MinimapAnchor)v;
        }
        #endregion

        #region Patch: InGameHUD.OnDraw (Overlay Render)

        /// <summary>
        /// Overlay render patch for <see cref="InGameHUD.OnDraw"/>.
        /// Summary:
        /// - Respects the HUD hidden flag and minimap visibility toggle.
        /// - Retrieves the font and safe-area rectangle.
        /// - Optionally builds other-player markers, then draws the minimap overlay.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - This patch does not assume a SpriteBatch Begin/End state; the renderer manages its own.
        /// - If anything goes wrong, we swallow exceptions to avoid crashing the HUD.
        /// </remarks>
        [HarmonyPatch(typeof(InGameHUD), "OnDraw")]
        internal static class Patch_InGameHUD_DrawMinimapOverlay
        {
            private static void Postfix(InGameHUD __instance, GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
            {
                try
                {
                    if (__instance == null || device == null || spriteBatch == null || gameTime == null) return;

                    // Respect HUD hidden flag.
                    bool hide = false;
                    try { hide = HideUiRef(__instance); } catch { }
                    if (hide) return;

                    // Ensure config/state are initialized.
                    if (!MinimapConfig.IsLoaded)
                        MinimapConfig.LoadApply();
                    MinimapState.EnsureInitFromConfig();

                    // Visibility toggle.
                    if (!MinimapState.Visible)
                        return;

                    var game = CastleMinerZGame.Instance;
                    if (game == null) return;

                    // Best-effort font fetch.
                    SpriteFont font = null;
                    try { font = SmallFontRef(game); } catch { }
                    if (font == null) return;

                    // Use title-safe/safe area like vanilla HUD does.
                    Rectangle safe = Screen.Adjuster.ScreenRect;

                    var lp = game.LocalPlayer;

                    if (lp != null)
                    {
                        // Optional: Include other players (pre-build marker list for renderer).
                        if (MinimapConfig.ShowOtherPlayers)
                            BuildOtherPlayers(_otherPlayers);
                        else
                            _otherPlayers.Clear();

                        // Hostiles (Enemies + Dragon).
                        // Note: Only build markers when at least one hostile layer is enabled.
                        //       When disabled, we hard-clear cached state so no stale dots/positions linger between toggles.
                        if (MinimapConfig.ShowEnemies || MinimapConfig.ShowDragon)
                            BuildHostileMarkers();
                        else
                        {
                            _enemyMarkers.Clear();
                            _dragonAlive = false;
                            _dragonPos   = default; // Optional: Keeps state tidy.
                        }

                        MinimapRenderer.DrawOverlay(device, spriteBatch, font, safe, lp.LocalPosition, gameTime, _otherPlayers);
                    }
                }
                catch
                {
                    // Never crash HUD render.
                    try { spriteBatch.End(); } catch { }
                }
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