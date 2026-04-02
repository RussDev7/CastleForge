/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection.Emit;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using DNA.Timers;
using HarmonyLib;                       // Harmony patching library.
using DNA.Audio;
using DNA.Input;
using System.IO;
using NLayer;
using System;
using DNA;

using static MoreAchievements.AchievementUIPatches;
using static ModLoader.LogSystem;       // For Log(...).

namespace MoreAchievements
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
        /// WEHotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (WEHotkeys.ReloadPressedThisFrame()) { WEConfig.LoadApply(); ... }
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
        internal static class MAHotkeys
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
                Log($"[WEdit] Reload hotkey set to \"{s}\".");
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
        /// Keeps the body small; heavy lifting should be inside WEConfig.LoadApply().
        /// </summary>
        [HarmonyPatch]
        static class Patch_Hotkey_ReloadConfig_MoreAchievements
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI.
            /// </summary>
            static void Postfix(InGameHUD __instance)
            {
                if (!MAHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    MAConfig.LoadApply();

                    SendFeedback($"[MoAc] Config hot-reloaded from \"{PathShortener.ShortenForLog(MAConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[MoAc] Hot-reload failed: {ex.Message}.");
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

        #region Patches

        #region Manager Wiring & Tick

        /// <summary>
        /// After the game sets up a new gamer, swap the stock CastleMinerZAchievementManager
        /// for our ExtendedAchievementManager. This is the only patch you need for wiring.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "SetupNewGamer")]
        internal static class Patch_SetupNewGamer_UseExtendedAchievementManager
        {
            /// <summary>
            /// Postfix hook that assigns ExtendedAchievementManager into the game instance.
            /// </summary>
            private static void Postfix(CastleMinerZGame __instance)
            {
                try
                {
                    __instance.AcheivmentManager = new ExtendedAchievementManager(__instance);
                    Log("Swapped in ExtendedAchievementManager for achievements.");
                }
                catch (Exception ex)
                {
                    Log($"Failed to swap ExtendedAchievementManager: {ex.GetType().Name}: {ex.Message}.");
                }
            }
        }

        #region Achievement Manager Tick

        /// <summary>
        /// Make sure the CastleMinerZ achievement manager runs its Update()
        /// every frame, even on the front-end (main menu, etc.).
        ///
        /// This lets it see up-to-date Steam stats and mark previously
        /// completed achievements as Achieved before our UI reads them.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "Update")]
        private static class Patch_AchievementManager_GlobalUpdate
        {
            // DNAGame.Update(GameTime gameTime)
            private static void Postfix(DNAGame __instance)
            {
                try
                {
                    // Only care if we're actually running CMZ.
                    var gm = CastleMinerZGame.Instance;
                    if (gm == null)
                        return;

                    var mgr = gm.AcheivmentManager;
                    if (mgr == null || gm.PlayerStats == null)
                        return;

                    // Safe to call every frame. Each achievement only fires
                    // once because Achievement.Update() checks _acheived.
                    mgr.Update();
                }
                catch (Exception ex)
                {
                    Log($"Achievement tick patch error: {ex.Message}.");
                }
            }
        }
        #endregion

        #endregion

        #region Intercept Selection In Main Menu & In-Game Menu

        /// <summary>
        /// In-game: intercept any "*MenuItemSelected" handlers that take (object, SelectedMenuItemArgs)
        /// and, when the selection is our injected "Achievements" row, launch the achievements browser instead.
        /// This avoids enum casts / crashes in the stock menu handler.
        /// </summary>
        [HarmonyPatch(typeof(GameScreen))]
        private static class Patch_InGameMenu_Selection_Intercept
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(GameScreen)))
                {
                    if (m.IsStatic) continue;
                    if (m.ReturnType != typeof(void)) continue;
                    if (m.Name.IndexOf("MenuItemSelected", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    if (ps[0].ParameterType != typeof(object)) continue;

                    var p1 = ps[1].ParameterType;
                    var n = p1?.Name ?? "";
                    if (!(n.Equals("SelectedMenuItemArgs", StringComparison.OrdinalIgnoreCase) ||
                          n.EndsWith("SelectedMenuItemArgs", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    yield return m;
                }
            }

            private static bool Prefix(object __instance, object sender, object e)
            {
                try
                {
                    if (MenuSelectUtil.IsOurSelection(sender, e))
                    {
                        if (!BrowserLauncher.LaunchInGame())
                            Log("Failed to launch browser from in-game menu.");
                        return false; // Consume.
                    }
                }
                catch
                {
                    // Never let UI input break due to our interception.
                }
                return true;
            }
        }

        /// <summary>
        /// Front-end: intercept FrontEndScreen "*MenuItemSelected" handlers. Same idea as in-game:
        /// swallow our "Achievements" row and launch the browser on the front-end UI stack instead.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen))]
        private static class Patch_MainMenu_Selection_Intercept
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(FrontEndScreen)))
                {
                    if (m.IsStatic) continue;
                    if (m.ReturnType != typeof(void)) continue;
                    if (m.Name.IndexOf("MenuItemSelected", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    if (ps[0].ParameterType != typeof(object)) continue;

                    var p1 = ps[1].ParameterType;
                    var n = p1?.Name ?? "";
                    if (!(n.Equals("SelectedMenuItemArgs", StringComparison.OrdinalIgnoreCase) ||
                          n.EndsWith("SelectedMenuItemArgs", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    yield return m;
                }
            }

            private static bool Prefix(object __instance, object sender, object e)
            {
                try
                {
                    if (MenuSelectUtil.IsOurSelection(sender, e))
                    {
                        if (!BrowserLauncher.LaunchFromFrontEnd())
                            Log("Failed to launch browser from main menu.");
                        return false; // Consume.
                    }
                }
                catch
                {
                    // Ignore and let stock handler proceed.
                }
                return true;
            }
        }
        #endregion

        #region Inject "Achievements" Menu Row (Main Menu + In-Game)

        /// <summary>
        /// Main menu: add the "Achievements" row during MainMenu construction
        /// and remember its MenuItemElement instance in AchMenuItemRegistry.
        /// </summary>
        [HarmonyPatch(typeof(MainMenu), MethodType.Constructor, new[] { typeof(CastleMinerZGame) })]
        private static class Patch_MainMenu_AddItem_Ctor
        {
            private static void Postfix(MainMenu __instance)
            {
                var label = "Achievements" ?? GetStringsText("Awards"); // Localized "Awards"/"Achievements".
                var item  = __instance.AddMenuItem(label, AchMenuItemRegistry.Tag);
                AchMenuItemRegistry.Remember(__instance, item);
                MenuOrderHelper.MainMenu_PlaceAbove(__instance, "Options");
            }
        }

        /// <summary>
        /// Safety net: If the main menu is rebuilt or for some reason our row goes missing,
        /// ensure the "Achievements" row is present again on each OnUpdate().
        /// </summary>
        [HarmonyPatch(typeof(MainMenu), "OnUpdate")]
        private static class Patch_MainMenu_Ensure_Achievements_Row
        {
            private static void Postfix(MainMenu __instance)
            {
                if (AchMenuItemRegistry.Get(__instance) == null)
                {
                    var label = "Achievements" ?? GetStringsText("Awards");
                    var item  = __instance.AddMenuItem(label, AchMenuItemRegistry.Tag);
                    AchMenuItemRegistry.Remember(__instance, item);
                    MenuOrderHelper.MainMenu_PlaceAbove(__instance, "Options");
                }
            }
        }

        /// <summary>
        /// In-game menu: add the "Achievements" row during InGameMenu construction
        /// and order it relative to the "Inventory" entry.
        /// </summary>
        [HarmonyPatch(typeof(InGameMenu), MethodType.Constructor, new[] { typeof(CastleMinerZGame) })]
        private static class Patch_InGameMenu_AddItem_Ctor
        {
            private static void Postfix(InGameMenu __instance)
            {
                var label = "Achievements" ?? GetStringsText("Awards");
                var item  = __instance.AddMenuItem(label, AchMenuItemRegistry.Tag);
                AchMenuItemRegistry.Remember(__instance, item);
                MenuOrderHelper.InGameMenu_PlaceAbove(__instance, "Inventory");
            }
        }

        /// <summary>
        /// Safety net for in-game menu: If our row disappears (menu rebuilt, etc.),
        /// re-add it on OnPushed().
        /// </summary>
        [HarmonyPatch(typeof(InGameMenu), "OnPushed")]
        private static class Patch_InGameMenu_Ensure_Achievements_Row
        {
            private static void Postfix(InGameMenu __instance)
            {
                if (AchMenuItemRegistry.Get(__instance) == null)
                {
                    var label = "Achievements" ?? GetStringsText("Awards");
                    var item  = __instance.AddMenuItem(label, AchMenuItemRegistry.Tag);
                    AchMenuItemRegistry.Remember(__instance, item);
                    MenuOrderHelper.InGameMenu_PlaceAbove(__instance, "Inventory");
                }
            }
        }
        #endregion

        #region Hook Achievement Unlock Event To Drive Toast Overlay

        /// <summary>
        /// After a new gamer is set up, hook the achievement manager's Achieved
        /// event so we can show bottom-right toasts when any (stock or custom)
        /// achievement is unlocked.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "SetupNewGamer")]
        private static class Patch_SetupNewGamer_HookAchievements
        {
            private static void Postfix(CastleMinerZGame __instance)
            {
                try
                {
                    var mgr = __instance?.AcheivmentManager;
                    if (mgr == null) return;

                    // Avoid duplicate subscriptions if something calls SetupNewGamer more than once.
                    mgr.Achieved -= OnAchievementUnlocked;
                    mgr.Achieved += OnAchievementUnlocked;
                }
                catch (Exception ex)
                {
                    Log($"Failed to hook Achieved event: {ex.Message}.");
                }
            }

            /// <summary>
            /// Event handler invoked any time an achievement transitions to Achieved.
            /// We forward the achievement object into the toast queue.
            /// </summary>
            private static void OnAchievementUnlocked(
                object sender,
                AchievementManager<CastleMinerZPlayerStats>.AcheimentEventArgs e)
            {
                try
                {
                    if (e?.Achievement != null)
                        AchievementToastOverlay.Enqueue(e.Achievement);
                }
                catch (Exception ex)
                {
                    Log($"Toast enqueue failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Globalization Strings Helper

        /// <summary>
        /// Helper to read a string from DNA.CastleMinerZ.Globalization.Strings via reflection,
        /// e.g. GetStringsText("Awards"). Returns null on failure.
        /// </summary>
        private static string GetStringsText(string stringName)
        {
            try
            {
                var p = AccessTools.Property(
                    AccessTools.TypeByName("DNA.CastleMinerZ.Globalization.Strings"),
                    stringName);

                if (p != null)
                    return p.GetValue(null, null) as string;
            }
            catch
            {
                // Swallow and fall through to null.
            }
            return null;
        }
        #endregion

        // === //

        #region Achievement UI & HUD Toast

        /// <summary>
        /// UI patches for achievement popups (Steam-style toast).
        /// </summary>
        internal static class AchievementUIPatches
        {
            #region FieldRefs Into InGameHUD

            // private CastleMinerZGame _game;
            private static readonly AccessTools.FieldRef<InGameHUD, CastleMinerZGame>
                HUD_GameRef = AccessTools.FieldRefAccess<InGameHUD, CastleMinerZGame>("_game");

            // private string _achievementText1;
            private static readonly AccessTools.FieldRef<InGameHUD, string>
                HUD_AchText1Ref = AccessTools.FieldRefAccess<InGameHUD, string>("_achievementText1");

            // private string _achievementText2;
            private static readonly AccessTools.FieldRef<InGameHUD, string>
                HUD_AchText2Ref = AccessTools.FieldRefAccess<InGameHUD, string>("_achievementText2");

            // private OneShotTimer acheivementDisplayTimer;
            private static readonly AccessTools.FieldRef<InGameHUD, OneShotTimer>
                HUD_AchTimerRef = AccessTools.FieldRefAccess<InGameHUD, OneShotTimer>("acheivementDisplayTimer");

            // private Queue<AchievementManager<CastleMinerZPlayerStats>.Achievement> AcheivementsToDraw;
            // Notice: The game misspells 'Achievements' as 'Acheivements'.
            private static readonly AccessTools.FieldRef<
                InGameHUD,
                Queue<AchievementManager<CastleMinerZPlayerStats>.Achievement>>
                HUD_AchQueueRef =
                    AccessTools.FieldRefAccess<
                        InGameHUD,
                        Queue<AchievementManager<CastleMinerZPlayerStats>.Achievement>>("AcheivementsToDraw");

            // private AchievementManager<CastleMinerZPlayerStats>.Achievement displayedAcheivement;
            private static readonly AccessTools.FieldRef<
                InGameHUD,
                AchievementManager<CastleMinerZPlayerStats>.Achievement>
                HUD_DisplayedAchRef =
                    AccessTools.FieldRefAccess<
                        InGameHUD,
                        AchievementManager<CastleMinerZPlayerStats>.Achievement>("displayedAcheivement");

            #endregion

            #region Reflection Helper For Strings.Has_earned

            // Cache so we don't reflect every time.
            private static string s_hasEarnedString;
            private static bool s_hasEarnedResolved;

            /// <summary>
            /// Get the localized "Has_earned" string from
            /// DNA.CastleMinerZ.Globalization.Strings via reflection.
            /// Falls back to "has earned" on failure.
            /// </summary>
            private static string GetHasEarnedString()
            {
                if (s_hasEarnedResolved)
                    return s_hasEarnedString ?? "has earned";

                s_hasEarnedResolved = true;

                try
                {
                    // Grab the CastleMinerZ assembly from a known type.
                    var asm = typeof(CastleMinerZGame).Assembly;

                    // Internal type: DNA.CastleMinerZ.Globalization.Strings
                    var stringsType = asm.GetType(
                        "DNA.CastleMinerZ.Globalization.Strings",
                        throwOnError: false);

                    if (stringsType != null)
                    {
                        // Property: internal static string Has_earned { get; }
                        var prop = stringsType.GetProperty(
                            "Has_earned",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                        if (prop != null && prop.PropertyType == typeof(string))
                        {
                            var value = prop.GetValue(null, null) as string;
                            if (!string.IsNullOrEmpty(value))
                            {
                                s_hasEarnedString = value;
                                return s_hasEarnedString;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to reflect Strings.Has_earned: {ex.Message}.");
                }

                // Absolute last resort: hard-coded English fallback.
                s_hasEarnedString = "has earned";
                return s_hasEarnedString;
            }

            #endregion

            #region Custom Award Sound (!Mods\MoreAchievements\CustomSounds)

            // Supported Award.* extensions.
            private static readonly string[] s_awardSoundExts = { ".mp3", ".wav" };

            // Cached sound effect (decoded once).
            private static SoundEffect s_customAwardSound;
            private static bool s_triedLoadCustomAwardSound;

            /// <summary>
            /// !Mods\MoreAchievements\CustomSounds root.
            /// </summary>
            private static string GetCustomSoundsRoot()
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "!Mods",
                    typeof(GamePatches).Namespace,
                    "CustomSounds");
            }

            /// <summary>
            /// Find Award.(mp3|wav) in !Mods\MoreAchievements\CustomSounds.
            /// </summary>
            private static string FindCustomAwardSoundPath()
            {
                try
                {
                    var root = GetCustomSoundsRoot();
                    if (!Directory.Exists(root))
                        return null;

                    foreach (var ext in s_awardSoundExts)
                    {
                        var path = Path.Combine(root, "Award" + ext);
                        if (File.Exists(path))
                            return path;
                    }
                }
                catch
                {
                    // Fail soft: No custom sound.
                }

                return null;
            }

            /// <summary>
            /// Ensure s_customAwardSound is loaded (if config + file present).
            /// </summary>
            private static bool EnsureCustomAwardSoundLoaded(GraphicsDevice device)
            {
                if (s_triedLoadCustomAwardSound)
                    return s_customAwardSound != null;

                s_triedLoadCustomAwardSound = true;

                if (!AchievementUIConfig.UseCustomAwardSound || device == null)
                    return false;

                var path = FindCustomAwardSoundPath();
                if (string.IsNullOrEmpty(path))
                    return false;

                try
                {
                    var ext = Path.GetExtension(path);
                    if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        s_customAwardSound = LoadMp3SoundEffect(path);
                    }
                    else
                    {
                        // Assume XNA-compatible WAV.
                        using (var stream = File.OpenRead(path))
                        {
                            s_customAwardSound = SoundEffect.FromStream(stream);
                        }
                    }

                    if (s_customAwardSound != null)
                    {
                        // Log($"Loaded custom Award sound: '{path}'.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to load custom Award sound: {ex.Message}.");
                    s_customAwardSound = null;
                }

                return s_customAwardSound != null;
            }

            /// <summary>
            /// Decode an MP3 into a SoundEffect using NLayer.
            /// </summary>
            private static SoundEffect LoadMp3SoundEffect(string path)
            {
                // NOTE:
                //   This is a simple "decode whole file" path intended for short
                //   Award jingles. For long tracks you probably want a streaming
                //   approach instead.
                using (var mpeg = new MpegFile(path))
                {
                    int sampleRate = mpeg.SampleRate;
                    int channels = mpeg.Channels;

                    const int chunk = 4096;
                    var samples = new List<float>(chunk * 8);
                    var buffer = new float[chunk];

                    int read;
                    while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                            samples.Add(buffer[i]);
                    }

                    // Convert float [-1,1] samples to 16-bit PCM.
                    var pcm = new byte[samples.Count * 2];
                    for (int i = 0; i < samples.Count; i++)
                    {
                        float sample = MathHelper.Clamp(samples[i], -1f, 1f);
                        short s = (short)(sample * short.MaxValue);
                        pcm[i * 2 + 0] = (byte)(s & 0xff);
                        pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                    }

                    return new SoundEffect(
                        pcm,
                        sampleRate,
                        channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                }
            }

            /// <summary>
            /// Play Award sound: custom Award.(mp3|wav) if available, otherwise the
            /// original SoundManager "Award" cue.
            /// </summary>
            private static void PlayAwardSound(CastleMinerZGame game)
            {
                try
                {
                    var gd = game.GraphicsDevice;
                    if (EnsureCustomAwardSoundLoaded(gd) && s_customAwardSound != null)
                    {
                        s_customAwardSound.Play();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Custom Award sound playback failed: {ex.Message}.");
                }

                // Fallback: Original behavior.
                SoundManager.Instance.PlayInstance("Award");
            }
            #endregion

            #region Optional: Access To _uiSprites["AwardCircle"] (Fallback Icon)

            private static readonly FieldInfo Game_UiSpritesField =
                AccessTools.Field(typeof(CastleMinerZGame), "_uiSprites"); // Works even if private.

            private static readonly PropertyInfo Game_UiSpritesIndexer =
                Game_UiSpritesField?.FieldType.GetProperty("Item", new[] { typeof(string) });

            private static Sprite TryGetAwardCircleSprite(CastleMinerZGame game)
            {
                try
                {
                    if (Game_UiSpritesField == null || Game_UiSpritesIndexer == null)
                        return null;

                    var lib = Game_UiSpritesField.GetValue(game);
                    if (lib == null)
                        return null;

                    var spriteObj = Game_UiSpritesIndexer.GetValue(lib, new object[] { "AwardCircle" });
                    return spriteObj as Sprite;
                }
                catch
                {
                    // Fail soft: Just don't draw the icon.
                    return null;
                }
            }
            #endregion

            #region HUD Achievement + Icon State (CustomIcons\Steam + Custom)

            // Last achievement passed into InGameHUD.DisplayAcheivement; used to resolve
            // the correct icon from CustomIcons\Steam or CustomIcons\Custom.
            private static AchievementManager<CastleMinerZPlayerStats>.Achievement s_lastHudAchievement;

            // Simple per-APIName texture cache for the HUD toast icon.
            // Key encodes which folder we loaded it from: "C:<api>" or "S:<api>".
            private static readonly Dictionary<string, Texture2D> s_hudIconCache =
                new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

            // Supported icon file extensions.
            private static readonly string[] s_iconExts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

            /// <summary>
            /// Root folder for achievement icons:
            ///   <CMZ>\!Mods\MoreAchievements\CustomIcons
            /// With two subfolders:
            ///   - Steam\  (vanilla achievements)
            ///   - Custom\ (MoreAchievements custom achievements)
            /// </summary>
            private static string GetIconsRootBase()
            {
                var modRoot = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "!Mods",
                    typeof(GamePatches).Namespace);
                return Path.Combine(modRoot, "CustomIcons");
            }

            /// <summary>
            /// True if the given achievement is one of our custom achievements.
            /// </summary>
            private static bool IsCustomAchievement(
                AchievementManager<CastleMinerZPlayerStats>.Achievement achievement)
            {
                var gm = CastleMinerZGame.Instance;
                if (gm?.AcheivmentManager is ExtendedAchievementManager ext && achievement != null)
                {
                    return ext.CustomAchievements.Contains(achievement);
                }
                return false;
            }

            /// <summary>
            /// Find a HUD icon in either CustomIcons\Custom or CustomIcons\Steam
            /// matching the given API name (e.g. "ACH_DAYS_730.png").
            /// </summary>
            private static string FindHudIconPath(string apiName, bool isCustom)
            {
                if (string.IsNullOrWhiteSpace(apiName))
                    return null;

                var baseRoot = GetIconsRootBase();
                var subFolder = isCustom ? "Custom" : "Steam";
                var folder = Path.Combine(baseRoot, subFolder);

                if (!Directory.Exists(folder))
                    return null;

                foreach (var ext in s_iconExts)
                {
                    var p = Path.Combine(folder, apiName + ext);
                    if (File.Exists(p))
                        return p;
                }

                return null;
            }

            /// <summary>
            /// Load (or retrieve cached) HUD icon texture for the given API name from the
            /// appropriate CustomIcons\Steam or CustomIcons\Custom subfolder.
            /// </summary>
            private static Texture2D GetOrLoadHudIcon(GraphicsDevice gd, string apiName, bool isCustom)
            {
                if (gd == null || string.IsNullOrEmpty(apiName))
                    return null;

                string cacheKey = (isCustom ? "C:" : "S:") + apiName;

                if (s_hudIconCache.TryGetValue(cacheKey, out var tex) &&
                    tex != null &&
                    !tex.IsDisposed)
                {
                    return tex;
                }

                var path = FindHudIconPath(apiName, isCustom);
                if (path == null)
                    return null;

                try
                {
                    using (var s = File.OpenRead(path))
                    {
                        tex = Texture2D.FromStream(gd, s);
                    }

                    s_hudIconCache[cacheKey] = tex;
                    return tex;
                }
                catch
                {
                    // Bad or unreadable icon; ignore and fall back to AwardCircle.
                    return null;
                }
            }
            #endregion

            #region Capture Last Achievement For HUD

            /// <summary>
            /// Track the last achievement shown via InGameHUD.DisplayAcheivement
            /// so DrawAcheivement can resolve the correct CustomIcons\Custom icon.
            /// </summary>
            [HarmonyPatch(typeof(InGameHUD), "DisplayAcheivement")]
            private static class Patch_InGameHUD_DisplayAcheivement_Capture
            {
                private static void Prefix(
                    AchievementManager<CastleMinerZPlayerStats>.Achievement acheivement)
                {
                    s_lastHudAchievement = acheivement;
                }
            }
            #endregion

            #region Patch - InGameHUD.UpdateAcheivements (Queue + Sound + Chat)

            /// <summary>
            /// Replace InGameHUD.UpdateAcheivements to:
            ///   - Keep the original queue / timer logic,
            ///   - Swap the "Award" sound for a custom MP3/WAV if available,
            ///   - Gate chat announcements via config.
            /// </summary>
            // Notice: The game misspells 'Achievements' as 'Acheivements'.
            [HarmonyPatch(typeof(InGameHUD), "UpdateAcheivements")]
            private static class Patch_InGameHUD_UpdateAcheivements
            {
                [HarmonyPrefix]
                private static bool Prefix(InGameHUD __instance, GameTime gameTime)
                {
                    var game = HUD_GameRef(__instance);
                    if (game == null)
                    {
                        // Something is very wrong - fall back to vanilla.
                        return true;
                    }

                    // 1) Keep manager logic identical to vanilla.
                    game.AcheivmentManager.Update();

                    var queue     = HUD_AchQueueRef(__instance);
                    var timer     = HUD_AchTimerRef(__instance);
                    var displayed = HUD_DisplayedAchRef(__instance);

                    if (displayed == null)
                    {
                        if (queue != null && queue.Count > 0)
                        {
                            var next = queue.Dequeue();

                            // 2) Custom Award sound (or vanilla fallback).
                            PlayAwardSound(game);

                            // 3) Reset timer + bind HUD state.
                            timer?.Reset();
                            HUD_DisplayedAchRef(__instance) = next;

                            // Keep icon source in sync with displayed achievement.
                            s_lastHudAchievement = next;

                            HUD_AchText1Ref(__instance) = next.Name;
                            HUD_AchText2Ref(__instance) = next.HowToUnlock;

                            // 4) Optional chat announcement (config-controlled).
                            var hasEarned = GetHasEarnedString();
                            if (AchievementUIConfig.AnnounceChat)
                            {
                                try
                                {
                                    var gamer = game.MyNetworkGamer;
                                    if (gamer != null)
                                    {
                                        BroadcastTextMessage.Send(
                                            gamer,
                                            string.Concat(
                                                __instance.LocalPlayer.Gamer.Gamertag,
                                                " ",
                                                hasEarned, // Strings.Has_earned.
                                                " '",
                                                next.Name,
                                                "'"));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"BroadcastTextMessage.Send failed: {ex.Message}.");
                                }
                            }

                            // We fully handled this call.
                            return false;
                        }
                    }
                    else
                    {
                        if (timer != null)
                        {
                            timer.Update(gameTime.ElapsedGameTime);
                            if (timer.Expired)
                            {
                                HUD_DisplayedAchRef(__instance) = null;
                            }
                        }

                        // Timer update handled - skip original.
                        return false;
                    }

                    // No queue, nothing displayed; we've already run the manager update.
                    return false;
                }
            }
            #endregion

            #region Patch - InGameHUD.DrawAcheivement (Steam-Style Toast)

            /// <summary>
            /// Replace InGameHUD.DrawAcheivement with a Steam-style toast.
            /// Slides up from the bottom-right, sits there, then
            /// slides back down while fading.
            /// Uses icons from !Mods\MoreAchievements\CustomIcons\Custom where available.
            /// </summary>
            [HarmonyPatch(typeof(InGameHUD), "DrawAcheivement")]
            private static class Patch_InGameHUD_DrawAcheivement
            {
                [HarmonyPrefix]
                private static bool Prefix(InGameHUD __instance, GraphicsDevice device, SpriteBatch spriteBatch)
                {
                    var game = HUD_GameRef(__instance);
                    if (game == null || game.DummyTexture == null)
                    {
                        // If something is very wrong, fall back to original.
                        return true;
                    }

                    string title = HUD_AchText1Ref(__instance) ?? string.Empty;  // Achievement name.
                    string desc  = HUD_AchText2Ref(__instance) ?? string.Empty;  // How to unlock / description.

                    OneShotTimer timer = HUD_AchTimerRef(__instance);
                    float t = timer != null ? MathHelper.Clamp(timer.PercentComplete, 0f, 1f) : 0f;

                    // If timer is basically done, skip drawing.
                    if (t >= 1f)
                        return false;

                    Rectangle safe = Screen.Adjuster.ScreenRect;
                    float uiScale  = Screen.Adjuster.ScaleFactor.Y;

                    // "Steam-like" header text.
                    const string headerText = "Achievement unlocked!";

                    SpriteFont headerFont = game._systemFont;
                    SpriteFont titleFont  = game._medFont;
                    SpriteFont descFont   = game._systemFont;

                    // Measure text (unscaled).
                    Vector2 headerSize = headerFont.MeasureString(headerText);
                    Vector2 titleSize  = titleFont.MeasureString(title);
                    Vector2 descSize   = string.IsNullOrEmpty(desc) ? Vector2.Zero : descFont.MeasureString(desc);

                    // Convert to pixels with scale.
                    float headerWidth  = headerSize.X * uiScale;
                    float titleWidth   = titleSize.X  * uiScale;
                    float descWidth    = descSize.X   * uiScale;

                    float textWidth    = Math.Max(headerWidth, Math.Max(titleWidth, descWidth));

                    // Layout constants.
                    float padding      = 8f   * uiScale;
                    float iconSize     = 48f  * uiScale;
                    float stripeWidth  = 6f   * uiScale;
                    float minWidth     = 260f * uiScale;
                    float maxWidth     = 600f * uiScale;

                    float textHeight =
                        (headerSize.Y + titleSize.Y + (descSize.Y > 0 ? descSize.Y : 0f)) * uiScale +
                        padding * 0.5f; // Small extra gap.

                    float height     = Math.Max(iconSize + padding * 2f, textHeight + padding * 2f);
                    float extraWidth = 10f * uiScale; // Extra padding on the right.
                    float width      = MathHelper.Clamp(stripeWidth + padding + iconSize + padding + textWidth + padding + extraWidth,
                                                        minWidth,
                                                        maxWidth + extraWidth);

                    int margin   = (int)(20f * uiScale);

                    // Base (final) position: bottom-right, like Steam.
                    float baseX  = safe.Right - width - margin;
                    float baseY  = safe.Bottom - height - margin;

                    // Animation curve:
                    //  - 0.00-0.15: slide up from bottom + fade in.
                    //  - 0.15-0.75: fully visible, no slide.
                    //  - 0.75-1.00: fade out and slide back down.
                    float tIn  = MathHelper.Clamp(t / 0.15f, 0f, 1f);
                    float tOut = MathHelper.Clamp((1f - t) / 0.25f, 0f, 1f);

                    float slideOffset;
                    if (t < 0.15f)
                    {
                        // Slide up: 1 -> 0 (start off-screen below, end at baseY).
                        slideOffset = 1f - tIn;
                    }
                    else if (t > 0.75f)
                    {
                        // Slide back down: 0 -> 1 (baseY down to off-screen).
                        slideOffset = 1f - tOut;
                    }
                    else
                    {
                        // Fully settled.
                        slideOffset = 0f;
                    }

                    // Fade: Product of fade-in and fade-out ramps.
                    float alpha = MathHelper.Clamp(tIn * tOut, 0f, 1f);

                    // X stays locked to right edge; Y animates up/down from the bottom.
                    float x = baseX;
                    float y = baseY + height * slideOffset;

                    var panelRect = new Rectangle(
                        (int)Math.Floor(x),
                        (int)Math.Floor(y),
                        (int)Math.Ceiling(width),
                        (int)Math.Ceiling(height));

                    // Colors.
                    Color bgColor     = new Color(16, 16, 16) * alpha;    // Dark panel.
                    Color stripeColor = new Color(78, 198, 90) * alpha;   // Green accent.
                    Color titleColor  = Color.White * alpha;
                    Color descColor   = new Color(200, 200, 200) * alpha;
                    Color headerColor = new Color(180, 220, 180) * alpha;

                    // Draw background panel.
                    spriteBatch.Draw(game.DummyTexture, panelRect, bgColor);

                    // Accent stripe on the left.
                    var stripeRect = new Rectangle(panelRect.Left, panelRect.Top, (int)stripeWidth, panelRect.Height);
                    spriteBatch.Draw(game.DummyTexture, stripeRect, stripeColor);

                    // Icon area:
                    // Prefer !Mods\MoreAchievements\CustomIcons\Custom\<APIName>.*,
                    // then fall back to AwardCircle, then a flat colored box.
                    float iconX = stripeRect.Right + padding;
                    float iconY = panelRect.Top + (panelRect.Height - iconSize) / 2f;

                    var iconRect = new Rectangle(
                        (int)Math.Floor(iconX),
                        (int)Math.Floor(iconY),
                        (int)Math.Ceiling(iconSize),
                        (int)Math.Ceiling(iconSize));

                    // Prefer the achievement that is actually being displayed;
                    // fall back to the last one we captured if needed.
                    var currentAch = HUD_DisplayedAchRef(__instance) ?? s_lastHudAchievement;
                    string apiName = currentAch?.APIName             ?? string.Empty;
                    bool isCustom  = IsCustomAchievement(currentAch);

                    Texture2D customIcon = GetOrLoadHudIcon(device, apiName, isCustom);

                    if (customIcon != null && !customIcon.IsDisposed)
                    {
                        spriteBatch.Draw(customIcon, iconRect, Color.White * alpha);
                    }
                    else
                    {
                        Sprite awardCircle = TryGetAwardCircleSprite(game);
                        if (awardCircle != null)
                        {
                            awardCircle.Draw(spriteBatch, iconRect, Color.White * alpha);
                        }
                        else
                        {
                            // Final fallback: Simple square in accent color.
                            spriteBatch.Draw(game.DummyTexture, iconRect, stripeColor);
                        }
                    }

                    // Text block to the right of the icon.
                    float textX = iconRect.Right + padding;
                    float textY = panelRect.Top + padding;

                    // Header: "Achievement unlocked!".
                    spriteBatch.DrawString(
                        headerFont,
                        headerText,
                        new Vector2(textX, textY),
                        headerColor,
                        0f,
                        Vector2.Zero,
                        uiScale,
                        SpriteEffects.None,
                        0f);

                    textY += headerSize.Y * uiScale;

                    // Title (achievement name).
                    spriteBatch.DrawString(
                        titleFont,
                        title,
                        new Vector2(textX, textY),
                        titleColor,
                        0f,
                        Vector2.Zero,
                        uiScale,
                        SpriteEffects.None,
                        0f);

                    textY += titleSize.Y * uiScale;

                    // Optional description (how to unlock).
                    if (!string.IsNullOrEmpty(desc))
                    {
                        textY += 2f * uiScale; // Small gap.
                        spriteBatch.DrawString(
                            descFont,
                            desc,
                            new Vector2(textX, textY),
                            descColor,
                            0f,
                            Vector2.Zero,
                            uiScale,
                            SpriteEffects.None,
                            0f);
                    }

                    // We fully handled drawing; skip the original DrawAcheivement.
                    return false;
                }
            }
            #endregion
        }
        #endregion

        #region Extra Stat Persistence (Cache, Trailer & Runtime Re-Apply)

        #region Extra Stat Cache

        /// <summary>
        /// Simple cache for extra stats loaded from our trailer so we
        /// can re-apply them later (e.g. after Steam callbacks).
        /// </summary>
        internal static class ExtraStatsCache
        {
            public static float MaxDistance       = 0f;
            public static float MaxDepth          = 0f;
            public static int   GuidedKills       = 0;
            public static int   UndeadDragonKills = 0;
            public static bool  HasData           = false;
        }
        #endregion

        #region Extra Stat Persistence - SaveData/LoadData Patches

        /// <summary>
        /// Persists the Steam-backed stats that the vanilla game never writes to the local
        /// player stats file: MaxDistanceTraveled, MaxDepth, UndeadDragonKills, DragonsKilledWithGuidedMissile.
        ///
        /// We append them at the end of the existing SaveData blob and read them back
        /// only if the extra bytes are present.
        /// </summary>
        internal static class ExtraStatPersistencePatches
        {
            /// <summary>
            /// Adds a tiny extra trailer to the CastleMinerZPlayerStats save data that
            /// stores a few stats the vanilla game never persists:
            ///   • MaxDistanceTraveled            (float)
            ///   • MaxDepth                       (float)
            ///   • UndeadDragonKills              (int)
            ///   • DragonsKilledWithGuidedMissile (int)
            ///
            /// Backwards compatible: If the trailer is not present, LoadData just returns.
            /// </summary>
            internal static class PlayerStatsExtraPersistence
            {
                #region SaveData Postfix - Write Trailer

                /// <summary>
                /// Extends CastleMinerZPlayerStats.SaveData to append extra stats
                /// that vanilla never saves (distance, guided-dragon kills, etc.).
                /// </summary>
                [HarmonyPatch(typeof(CastleMinerZPlayerStats), "SaveData")]
                internal static class PlayerStats_SaveData_ExtPatch
                {
                    // Magic marker so we can detect our trailer on load.
                    private const string TrailerMagic = "MA_EXTRA_V1";

                    // Postfix so we append AFTER vanilla data.
                    private static void Postfix(CastleMinerZPlayerStats __instance, BinaryWriter writer)
                    {
                        try
                        {
                            // Append our trailer.
                            writer.Write(TrailerMagic);

                            // 1) MaxDistanceTraveled is a float.
                            writer.Write(__instance.MaxDistanceTraveled);

                            // 2) MaxDepth is a float.
                            writer.Write(__instance.MaxDepth);

                            // 3) DragonsKilledWithGuidedMissile is an int.
                            writer.Write(__instance.DragonsKilledWithGuidedMissile);

                            // 4) UndeadDragonKills is an int.
                            writer.Write(__instance.UndeadDragonKills);
                        }
                        catch (Exception ex)
                        {
                            ModLoader.LogSystem.Log($"Error writing extra player stats trailer: {ex}.");
                        }
                    }
                }
                #endregion

                #region LoadData Postfix - Read Trailer & Merge

                /// <summary>
                /// Extends CastleMinerZPlayerStats.LoadData to restore extra stats
                /// from the trailer appended by PlayerStats_SaveData_ExtPatch.
                /// </summary>
                [HarmonyPatch(typeof(CastleMinerZPlayerStats), "LoadData")]
                internal static class PlayerStats_LoadData_ExtPatch
                {
                    private const string TrailerMagic = "MA_EXTRA_V1";

                    private static void Postfix(CastleMinerZPlayerStats __instance, BinaryReader reader, int version)
                    {
                        try
                        {
                            // If there's no more data, nothing to do (old saves).
                            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                                return;

                            long posBefore = reader.BaseStream.Position;

                            // Try to read the magic marker.
                            string magic;
                            try
                            {
                                magic = reader.ReadString();
                            }
                            catch (EndOfStreamException)
                            {
                                // No trailer present.
                                reader.BaseStream.Position = posBefore;
                                return;
                            }

                            if (!string.Equals(magic, TrailerMagic, StringComparison.Ordinal))
                            {
                                // Not our trailer; rewind to let other mods read if they want.
                                reader.BaseStream.Position = posBefore;
                                return;
                            }

                            // Read exactly what we wrote, in the same order / types.
                            float maxDistance      = reader.ReadSingle();
                            float maxDepth         = reader.ReadSingle();
                            int guidedMissileKills = reader.ReadInt32();
                            int undeadDragonKills  = reader.ReadInt32();

                            // Debugging.
                            // Log($"{maxDistance},{undeadDragonKills},{guidedMissileKills}");

                            // Merge with whatever vanilla / Steam already had.
                            __instance.MaxDistanceTraveled =
                                Math.Max(__instance.MaxDistanceTraveled, maxDistance);

                            __instance.MaxDepth =
                                Math.Max(__instance.MaxDepth, maxDepth);

                            __instance.DragonsKilledWithGuidedMissile =
                                Math.Max(__instance.DragonsKilledWithGuidedMissile, guidedMissileKills);

                            __instance.UndeadDragonKills =
                                Math.Max(__instance.UndeadDragonKills, undeadDragonKills);

                            // Debugging.
                            /*
                            Log($"{__instance.MaxDistanceTraveled}," +
                                $"{__instance.MaxDepth}," +
                                $"{__instance.UndeadDragonKills}," +
                                $"{__instance.DragonsKilledWithGuidedMissile}");
                            */

                            // Cache for later re-application in Update().
                            ExtraStatsCache.MaxDistance       = maxDistance;
                            ExtraStatsCache.MaxDepth          = maxDepth;
                            ExtraStatsCache.GuidedKills       = guidedMissileKills;
                            ExtraStatsCache.UndeadDragonKills = undeadDragonKills;
                            ExtraStatsCache.HasData           = true;
                        }
                        catch (Exception ex)
                        {
                            Log($"Error reading extra player stats trailer: {ex}.");
                        }
                    }
                }
                #endregion
            }
        }
        #endregion

        #region Extra Stat Re-Application - CastleMinerZGame.Update Postfix

        /// <summary>
        /// After the game updates each frame, make sure our extra stats
        /// (distance, guided kills, undead dragons) are at least as high
        /// as the values we loaded from our own trailer.
        ///
        /// This sidesteps Steam's OnUserStatsReceived wiping them back to 0,
        /// because even if Steam resets them, we bump them back up on the
        /// next Update tick.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "Update")]
        internal static class CastleMinerZGame_Update_ExtraStatsPatch
        {
            private static bool _loggedOnce = false;

            private static void Postfix(CastleMinerZGame __instance)
            {
                if (!ExtraStatsCache.HasData)
                    return;

                if (!(__instance.PlayerStats is CastleMinerZPlayerStats stats))
                    return;

                bool changed = false;

                if (ExtraStatsCache.MaxDistance > 0f &&
                    stats.MaxDistanceTraveled < ExtraStatsCache.MaxDistance)
                {
                    stats.MaxDistanceTraveled = ExtraStatsCache.MaxDistance;
                    changed = true;
                }

                if (ExtraStatsCache.MaxDepth < 0f &&
                    stats.MaxDepth > ExtraStatsCache.MaxDepth)
                {
                    stats.MaxDepth = ExtraStatsCache.MaxDepth;
                    changed = true;
                }

                if (ExtraStatsCache.GuidedKills > 0 &&
                    stats.DragonsKilledWithGuidedMissile < ExtraStatsCache.GuidedKills)
                {
                    stats.DragonsKilledWithGuidedMissile = ExtraStatsCache.GuidedKills;
                    changed = true;
                }

                if (ExtraStatsCache.UndeadDragonKills > 0 &&
                    stats.UndeadDragonKills < ExtraStatsCache.UndeadDragonKills)
                {
                    stats.UndeadDragonKills = ExtraStatsCache.UndeadDragonKills;
                    changed = true;
                }

                if (changed && !_loggedOnce)
                {
                    Log($"Re-applied extra stats from trailer: " +
                        $"dist={stats.MaxDistanceTraveled}, "    +
                        $"depth={stats.MaxDepth}, "              +
                        $"undead={stats.UndeadDragonKills}, "    +
                        $"guided={stats.DragonsKilledWithGuidedMissile}.");
                    _loggedOnce = true;
                }
            }
        }
        #endregion

        #endregion

        #region Vanilla Stat Gating Via AchievementModeRules

        /// <summary>
        /// Shared helper for all vanilla stat patches. Keeps the decision in one place.
        /// </summary>
        internal static class VanillaStatGate
        {
            /// <summary>
            /// When > 0, stat patches always allow the update regardless of
            /// AchievementModeRules. Used by admin commands like /achievement.
            /// </summary>
            [ThreadStatic]
            private static int _bypassDepth;

            /// <summary>
            /// RAII-style scope for temporarily bypassing the gate.
            /// Usage:
            ///   using (VanillaStatGate.BeginBypass("GrantOne")) { ... }
            /// </summary>
            internal static IDisposable BeginBypass(string reason = null)
            {
                _bypassDepth++;
                return new BypassScope();
            }

            private readonly struct BypassScope : IDisposable
            {
                public void Dispose()
                {
                    if (_bypassDepth > 0)
                        _bypassDepth--;
                }
            }

            /// <summary>
            /// Returns true if vanilla stat updates should go through right now.
            /// If false, the Harmony Prefix will skip the original setter.
            /// </summary>
            internal static bool Allow(string statName)
            {
                // Admin /achievement commands, etc.
                if (_bypassDepth > 0)
                    return true;

                if (!AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    // Optional debug logging:
                    // Log($"[MoreAchievements] Blocked vanilla stat update: {statName}.");
                    return false;
                }

                return true;
            }
        }

        // Max distance from spawn (used by your distance achievements).
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_MaxDistanceTraveled")]
        internal static class Patch_PlayerStats_MaxDistanceTraveled
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.MaxDistanceTraveled));
            }
        }

        // Max depth reached.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_MaxDepth")]
        internal static class Patch_PlayerStats_MaxDepth
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.MaxDepth));
            }
        }

        // Max days survived (Endurance-only in vanilla).
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_MaxDaysSurvived")]
        internal static class Patch_PlayerStats_MaxDaysSurvived
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.MaxDaysSurvived));
            }
        }

        // Total items crafted (used by vanilla & your custom crafting achievements).
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_TotalItemsCrafted")]
        internal static class Patch_PlayerStats_TotalItemsCrafted
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.TotalItemsCrafted));
            }
        }

        // Undead dragon kills.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_UndeadDragonKills")]
        internal static class Patch_PlayerStats_UndeadDragonKills
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.UndeadDragonKills));
            }
        }

        // Guided rocket dragon kills.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_DragonsKilledWithGuidedMissile")]
        internal static class Patch_PlayerStats_DragonsGuidedKills
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.DragonsKilledWithGuidedMissile));
            }
        }

        // TNT kills.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_EnemiesKilledWithTNT")]
        internal static class Patch_PlayerStats_EnemiesKilledWithTNT
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.EnemiesKilledWithTNT));
            }
        }

        // Grenade kills.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_EnemiesKilledWithGrenade")]
        internal static class Patch_PlayerStats_EnemiesKilledWithGrenade
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.EnemiesKilledWithGrenade));
            }
        }

        // Laser-weapon kills.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_EnemiesKilledWithLaserWeapon")]
        internal static class Patch_PlayerStats_EnemiesKilledWithLaserWeapon
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.EnemiesKilledWithLaserWeapon));
            }
        }

        // Optional: TimeOnline, TotalKills, GamesPlayed, etc, if you want them gated too.
        [HarmonyPatch(typeof(CastleMinerZPlayerStats), "set_TimeOnline")]
        internal static class Patch_PlayerStats_TimeOnline
        {
            static bool Prefix()
            {
                return VanillaStatGate.Allow(nameof(CastleMinerZPlayerStats.TimeOnline));
            }
        }
        #endregion

        #region ItemStats Call-Site Gating (Crafted/Used/Hits/TimeHeld)

        /// <summary>
        /// Harmony patch that prevents per-item kill statistics
        /// (<see cref="CastleMinerZPlayerStats.ItemStats.KillsZombies"/>,
        /// <see cref="CastleMinerZPlayerStats.ItemStats.KillsSkeleton"/>,
        /// <see cref="CastleMinerZPlayerStats.ItemStats.KillsHell"/>)
        /// from being updated when custom achievement progress is not allowed by
        /// <see cref="AchievementModeRules.IsCustomAchievementAllowedNow"/>.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZPlayerStats.ItemStats), "AddStat")]
        internal static class StatGate_ItemStats_AddStat_Patch
        {
            /// <summary>
            /// Prefix: If achievement progress is disallowed, skip the original
            /// AddStat implementation so that the per-item kill counters are
            /// not incremented for this item.
            /// </summary>
            static bool Prefix()
            {
                // If we don't want any achievement-related stat progress right now,
                // just skip the vanilla increments.
                if (!AchievementModeRules.IsCustomAchievementAllowedNow())
                    return false; // Skip original.

                return true;      // Run original.
            }
        }

        /// <summary>
        /// Harmony patch that prevents time-held statistics (ItemStats.TimeHeld)
        /// from accumulating when custom achievements are not allowed by
        /// <see cref="AchievementModeRules.IsCustomAchievementAllowedNow"/>.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnUpdate")]
        internal static class StatGate_InGameHUD_OnUpdate_TimeHeld
        {
            /// <summary>
            /// Internal state used to capture the original TimeHeld value for
            /// the currently active item so we can restore it later.
            /// </summary>
            private struct State
            {
                public bool Block;
                public CastleMinerZPlayerStats.ItemStats Stats;
                public TimeSpan TimeHeldBefore;
            }

            /// <summary>
            /// Prefix: When progress is disallowed, record the current TimeHeld
            /// for the active inventory item so that the postfix can roll back
            /// any changes made by the vanilla update.
            /// </summary>
            static void Prefix(InGameHUD __instance,
                               DNAGame game,
                               GameTime gameTime,
                               out State __state)
            {
                __state = default;

                // If we ALLOW progress, do nothing special.
                if (AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    __state.Block = false;
                    return;
                }

                __state.Block = true;

                var currentItem = __instance.ActiveInventoryItem;
                if (currentItem == null || currentItem.ItemClass == null)
                    return;

                var stats = CastleMinerZGame.Instance.PlayerStats
                    .GetItemStats(currentItem.ItemClass.ID);

                __state.Stats = stats;
                if (stats != null)
                    __state.TimeHeldBefore = stats.TimeHeld;
            }
            /// <summary>
            /// Postfix: If TimeHeld was changed while progress was disallowed,
            /// revert it back to the value captured in the prefix.
            /// </summary>
            static void Postfix(State __state)
            {
                if (!__state.Block || __state.Stats == null)
                    return;

                // Revert any TimeHeld changes made during OnUpdate.
                __state.Stats.TimeHeld = __state.TimeHeldBefore;
            }
        }

        /// <summary>
        /// Harmony patch that prevents gun usage statistics (ItemStats.Used)
        /// from increasing when custom achievements are not allowed by
        /// <see cref="AchievementModeRules.IsCustomAchievementAllowedNow"/>.
        /// </summary>
        [HarmonyPatch(typeof(GunInventoryItem), "ProcessInput")]
        internal static class StatGate_GunInventoryItem_ProcessInput_Used
        {
            /// <summary>
            /// Internal state used to capture the original Used value so we can
            /// undo any increments if progress is disallowed.
            /// </summary>
            private struct State
            {
                public bool Block;
                public CastleMinerZPlayerStats.ItemStats Stats;
                public int UsedBefore;
            }

            /// <summary>
            /// Prefix: When progress is disallowed, capture the current Used
            /// count for this gun so the postfix can restore it if needed.
            /// </summary>
            static void Prefix(GunInventoryItem __instance,
                               InGameHUD hud,
                               CastleMinerZControllerMapping controller,
                               out State __state)
            {
                __state = default;

                if (AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    __state.Block = false;
                    return;
                }

                __state.Block = true;

                var stats = CastleMinerZGame.Instance.PlayerStats
                    .GetItemStats(__instance.ItemClass.ID);
                __state.Stats = stats;
                if (stats != null)
                    __state.UsedBefore = stats.Used;
            }

            /// <summary>
            /// Postfix: If we recorded the original Used value and it changed
            /// while progress was disallowed, reset it back to the original.
            /// </summary>
            static void Postfix(State __state)
            {
                if (!__state.Block || __state.Stats == null)
                    return;

                // If vanilla incremented Used, undo it.
                if (__state.Stats.Used > __state.UsedBefore)
                    __state.Stats.Used = __state.UsedBefore;
            }
        }

        /// <summary>
        /// Harmony patch that prevents dragon hit statistics (ItemStats.Hits)
        /// from updating when custom achievements are not allowed by
        /// <see cref="AchievementModeRules.IsCustomAchievementAllowedNow"/>.
        /// </summary>
        [HarmonyPatch(typeof(DragonClientEntity), "TakeDamage")]
        internal static class StatGate_DragonClientEntity_TakeDamage_Hits
        {
            private struct State
            {
                public bool Block;
                public CastleMinerZPlayerStats.ItemStats Stats;
                public int HitsBefore;
            }

            /// <summary>
            /// Prefix: If achievement progress is disallowed, capture the current
            /// Hits value for the weapon used and mark this call for rollback.
            /// </summary>
            static void Prefix(DragonClientEntity __instance,
                               Vector3 damagePosition,
                               Vector3 damageDirection,
                               InventoryItem.InventoryItemClass itemClass,
                               byte shooterID,
                               out State __state)
            {
                __state = default;

                if (AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    __state.Block = false;
                    return;
                }

                __state.Block = true;

                if (!CastleMinerZGame.Instance.IsLocalPlayerId(shooterID))
                    return;

                var stats = CastleMinerZGame.Instance.PlayerStats.GetItemStats(itemClass.ID);
                __state.Stats = stats;
                if (stats != null)
                    __state.HitsBefore = stats.Hits;
            }

            /// <summary>
            /// Postfix: If we previously marked this call for rollback and the
            /// Hits counter increased, restore it to the original value.
            /// </summary>
            static void Postfix(State __state)
            {
                if (!__state.Block || __state.Stats == null)
                    return;

                if (__state.Stats.Hits > __state.HitsBefore)
                    __state.Stats.Hits = __state.HitsBefore;
            }
        }

        /// <summary>
        /// Harmony patch that prevents per-item crafting statistics
        /// (<see cref="CastleMinerZPlayerStats.ItemStats.Crafted"/>) from
        /// increasing when custom achievements are not allowed by
        /// <see cref="AchievementModeRules.IsCustomAchievementAllowedNow"/>.
        /// </summary>
        [HarmonyPatch(typeof(Tier2Item), "CheckInput")]
        internal static class StatGate_Tier2Item_CheckInput_Crafted
        {
            private struct State
            {
                public bool Block;
                public CastleMinerZPlayerStats.ItemStats Stats;
                public int CraftedBefore;
            }

            /// <summary>
            /// Prefix: When achievement progress is disallowed, record the current
            /// Crafted value for the selected recipe's result item so that the
            /// postfix can restore it if it changes.
            /// </summary>
            static void Prefix(Tier2Item __instance,
                               InputManager inputManager,
                               out State __state)
            {
                __state = default;

                // If we ALLOW progress, we don't need to do anything special.
                if (AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    __state.Block = false;
                    return;
                }

                __state.Block = true;

                // Use the public SelectedItem property instead of private fields.
                InventoryItem selected = __instance.SelectedItem;
                if (selected == null || selected.ItemClass == null)
                    return;

                // Get the stats entry for this recipe's result item.
                var stats = CastleMinerZGame.Instance.PlayerStats
                    .GetItemStats(selected.ItemClass.ID);

                __state.Stats = stats;
                if (stats != null)
                {
                    __state.CraftedBefore = stats.Crafted;
                }
            }

            /// <summary>
            /// Postfix: If we were guarding this call and the Crafted count was
            /// incremented while progress was disallowed, reset it back to the
            /// value captured in the prefix.
            /// </summary>
            static void Postfix(State __state)
            {
                if (!__state.Block || __state.Stats == null)
                    return;

                // If vanilla incremented Crafted during this call,
                // and we're in a mode where progress is not allowed,
                // revert it back to the saved value.
                if (__state.Stats.Crafted > __state.CraftedBefore)
                {
                    __state.Stats.Crafted = __state.CraftedBefore;
                }
            }
        }

        /// <summary>
        /// Harmony patch that prevents gun usage statistics
        /// (ItemStats.Used) from increasing when custom achievement
        /// progress is not allowed by
        /// <see cref="AchievementModeRules.IsCustomAchievementAllowedNow"/>.
        /// </summary>
        [HarmonyPatch(typeof(GunInventoryItem), "ProcessInput")]
        internal static class Patch_GunInventoryItem_ProcessInput_AchievementModeGuard
        {
            /// <summary>
            /// Captures the pre-call Used value so we can restore it
            /// in the postfix when stat progress is disallowed.
            /// </summary>
            private struct State
            {
                public bool Block;
                public CastleMinerZPlayerStats.ItemStats Stats;
                public int UsedBefore;
            }

            /// <summary>
            /// Prefix: When achievement progress is disallowed, record
            /// the current Used count for this gun so that any increments
            /// done by the original method can be reverted later.
            /// </summary>
            static void Prefix(GunInventoryItem __instance,
                               InGameHUD hud,
                               CastleMinerZControllerMapping controller,
                               out State __state)
            {
                __state = default;

                // If we allow progress, don't do anything special.
                if (AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    __state.Block = false;
                    return;
                }

                __state.Block = true;

                if (!(CastleMinerZGame.Instance.PlayerStats is CastleMinerZPlayerStats gameStats))
                    return;

                var stats = gameStats.GetItemStats(__instance.ItemClass.ID);
                __state.Stats = stats;
                if (stats != null)
                    __state.UsedBefore = stats.Used;
            }

            /// <summary>
            /// Postfix: If we guarded this call and the Used counter
            /// increased while progress was disallowed, restore it to
            /// the value captured in the prefix.
            /// </summary>
            static void Postfix(GunInventoryItem __instance, State __state)
            {
                if (!__state.Block || __state.Stats == null)
                    return;

                // If vanilla bumped Used during this call, put it back.
                if (__state.Stats.Used > __state.UsedBefore)
                    __state.Stats.Used = __state.UsedBefore;
            }
        }
        #endregion

        #region Gate Block Mining / Placing By Config (InGameHUD)

        /// <summary>
        /// Dig() is where the game calls PlayerStats.DugBlock(blockType).
        /// We replace that call with a guarded helper so BlocksDug never increments
        /// when AchievementModeRules disallows custom achievement progress.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "Dig", new[] { typeof(InventoryItem), typeof(bool) })]
        internal static class Patch_InGameHUD_Dig_GateDugBlockCall
        {
            /// <summary>
            /// Replacement for the direct DugBlock call inside Dig().
            /// </summary>
            private static void DugBlock_IfAllowed(CastleMinerZPlayerStats stats, BlockTypeEnum type)
            {
                if (stats == null)
                    return;

                if (!AchievementModeRules.IsCustomAchievementAllowedNow())
                    return;

                stats.DugBlock(type);
            }

            /// <summary>
            /// Transpiler: Replace the IL call to CastleMinerZPlayerStats.DugBlock(BlockTypeEnum)
            /// with a call to DugBlock_IfAllowed(stats, type).
            /// </summary>
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var target = AccessTools.Method(typeof(CastleMinerZPlayerStats), "DugBlock", new[] { typeof(BlockTypeEnum) });
                var repl = AccessTools.Method(typeof(Patch_InGameHUD_Dig_GateDugBlockCall), nameof(DugBlock_IfAllowed));

                foreach (var ci in instructions)
                {
                    if (ci.Calls(target))
                    {
                        // Preserve any labels/blocks attached to this instruction.
                        var ni = new CodeInstruction(OpCodes.Call, repl);
                        ni.labels.AddRange(ci.labels);
                        ni.blocks.AddRange(ci.blocks);
                        yield return ni;
                    }
                    else
                    {
                        yield return ci;
                    }
                }
            }
        }

        /// <summary>
        /// Gates block placement usage stats (ItemStats.Used) which is what "blocks placed"
        /// achievements typically read from.
        ///
        /// Vanilla increments Used here:
        ///   CastleMinerZGame.Instance.PlayerStats.GetItemStats(ItemClass.ID).Used++;
        ///
        /// This patch rolls that increment back whenever AchievementModeRules disallows progress.
        /// </summary>
        [HarmonyPatch(typeof(BlockInventoryItem), "ProcessInput", new[] { typeof(InGameHUD), typeof(CastleMinerZControllerMapping) })]
        internal static class Patch_BlockInventoryItem_ProcessInput_AchievementModeGuard
        {
            private struct State
            {
                public bool Block;
                public CastleMinerZPlayerStats.ItemStats Stats;
                public int UsedBefore;
            }

            static void Prefix(BlockInventoryItem __instance,
                               InGameHUD hud,
                               CastleMinerZControllerMapping controller,
                               out State __state)
            {
                __state = default;

                // If progress is allowed, do nothing.
                if (AchievementModeRules.IsCustomAchievementAllowedNow())
                {
                    __state.Block = false;
                    return;
                }

                __state.Block = true;

                // Defensive: stats can be null during load/transitions.
                if (!(CastleMinerZGame.Instance?.PlayerStats is CastleMinerZPlayerStats gameStats))
                    return;

                if (__instance?.ItemClass == null)
                    return;

                var stats = gameStats.GetItemStats(__instance.ItemClass.ID);
                __state.Stats = stats;
                if (stats != null)
                    __state.UsedBefore = stats.Used;
            }

            static void Postfix(State __state)
            {
                if (!__state.Block || __state.Stats == null)
                    return;

                // If vanilla bumped Used during this call, undo it.
                if (__state.Stats.Used > __state.UsedBefore)
                    __state.Stats.Used = __state.UsedBefore;
            }
        }
        #endregion

        #endregion
    }
}