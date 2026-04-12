/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Input;
using DNA.CastleMinerZ.Inventory;
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
using HarmonyLib;                       // Harmony patching library.
using System;
using DNA;

using static TexturePacks.TexturePackManager;
using static ModLoader.LogSystem;       // For Log(...).

namespace TexturePacks
{
    /// <summary>
    /// Harmony entry-point + patch registry.
    /// Call <see cref="ApplyAllPatches"/> once at startup. It:
    ///  - Builds a unique Harmony ID for this mod.
    ///  - Scans and applies all nested [HarmonyPatch] containers in this assembly.
    ///  - Logs per-patch-class results and a final summary of methods we actually patched.
    /// Use <see cref="DisableAll"/> to safely unpatch only this mod's changes.
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
        /// - Classes/methods additionally marked with [HarmonySilent] are omitted from patch-report spam.
        /// - Patches each class independently inside a try/catch (one bad target won't kill the rest).
        /// - Summarizes methods actually patched (by Owner == our Harmony ID).
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
        /// Tag a patch container or patch method to silence it in the patch-report logs.
        /// Useful for very chatty or trivially-applied patches you don't want to list.
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
        /// Finds all types that are Harmony patch containers in the given assembly.
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

        #endregion // Patcher Initiation

        #region Texture Pack Button / UI

        internal static partial class TexturePackPickerPatches
        {
            // ============================================================
            // UTIL: Robust way to detect "our" selection even though the
            // engine passes SelectedMenuItemArgs (unknown to us).
            // ============================================================
            private static class PickerSelectUtil
            {
                // Try to pull any "tag-like" value off the event args via reflection.
                private static object TryGetAnyTag(object e)
                {
                    if (e == null) return null;

                    // Fast path: Common property names.
                    foreach (var name in new[] { "Item", "Tag", "Value", "SelectedItem", "Payload" })
                    {
                        var p = AccessTools.Property(e.GetType(), name);
                        if (p != null)
                        {
                            try { return p.GetValue(e, null); } catch { }
                        }
                        var f = AccessTools.Field(e.GetType(), name);
                        if (f != null)
                        {
                            try { return f.GetValue(e); } catch { }
                        }
                    }

                    // Fallback: Scan all object-typed props/fields and see if one is our tag.
                    foreach (var p in e.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (p.PropertyType != typeof(object)) continue;
                        try
                        {
                            var v = p.GetValue(e, null);
                            if (ReferenceEquals(v, TPMenuItemRegistry.Tag)) return v;
                        }
                        catch { }
                    }
                    foreach (var f in e.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (f.FieldType != typeof(object)) continue;
                        try
                        {
                            var v = f.GetValue(e);
                            if (ReferenceEquals(v, TPMenuItemRegistry.Tag)) return v;
                        }
                        catch { }
                    }

                    return null;
                }

                /// <summary>Returns true if the selection belongs to our injected "Texture Packs" row.</summary>
                public static bool IsOurSelection(object sender, object e)
                {
                    // Most reliable: the tag we passed to AddMenuItem
                    var tag = TryGetAnyTag(e);
                    if (ReferenceEquals(tag, TPMenuItemRegistry.Tag))
                        return true;

                    // Secondary heuristic: If sender is a MenuScreen, match the currently "selected" row instance
                    // with the row we injected/remembered for that MenuScreen.
                    if (sender is MenuScreen ms)
                    {
                        var ours = TPMenuItemRegistry.Get(ms);
                        if (ours != null)
                        {
                            // SelectedMenuItemArgs nearly always also carries the selected MenuItemElement somewhere.
                            // Check any MenuItemElement-looking property/field for reference equality.
                            foreach (var p in e.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (typeof(MenuItemElement).IsAssignableFrom(p.PropertyType))
                                {
                                    try { if (ReferenceEquals(p.GetValue(e, null), ours)) return true; } catch { }
                                }
                            }
                            foreach (var f in e.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (typeof(MenuItemElement).IsAssignableFrom(f.FieldType))
                                {
                                    try { if (ReferenceEquals(f.GetValue(e), ours)) return true; } catch { }
                                }
                            }
                        }
                    }

                    return false;
                }
            }

            // ============================================================
            // LAUNCHER for the EXISTING picker.
            // ============================================================
            internal static class OldPickerLauncher
            {
                // Try these names first.
                private static readonly string[] CANDIDATE_TYPES =
                {
                    "TexturePacks.TexturePackPickerScreen",
                    "TexturePacks.PackPickerScreen",
                    "TexturePacks.UI.TexturePackPicker"
                };

                private static Screen TryCreateOldPicker()
                {
                    var gm = CastleMinerZGame.Instance;
                    if (gm == null) return null;

                    // A) Explicit candidates.
                    foreach (var name in CANDIDATE_TYPES)
                    {
                        var t = AccessTools.TypeByName(name);
                        var s = TryConstruct(t, gm);
                        if (s != null) return s;
                    }

                    // B) Broad scan: Any Screen-derived type with "Texture" + "Pack" in the name.
                    try
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            foreach (var t in AccessTools.GetTypesFromAssembly(asm))
                            {
                                if (t == null || t.IsAbstract) continue;
                                if (!typeof(Screen).IsAssignableFrom(t)) continue;

                                var n = t.FullName ?? t.Name ?? "";
                                if (n.IndexOf("Texture", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                if (n.IndexOf("Pack", StringComparison.OrdinalIgnoreCase) < 0) continue;

                                var s = TryConstruct(t, gm);
                                if (s != null) return s;
                            }
                        }
                    }
                    catch { /* Safe. */ }

                    // C) FINAL FALLBACK -> use the modern picker so click still does something.
                    try
                    {
                        return new TexturePackPickerScreen(gm);
                    }
                    catch
                    {
                        Log("OldPickerLauncher: Could not locate or create a picker screen. (No old UI; fallback failed.)");
                        return null;
                    }
                }

                private static Screen TryConstruct(Type t, CastleMinerZGame gm)
                {
                    if (t == null) return null;
                    try
                    {
                        var ctor1 = AccessTools.Constructor(t, new[] { typeof(CastleMinerZGame) });
                        if (ctor1 != null) return (Screen)ctor1.Invoke(new object[] { gm });

                        var ctor0 = AccessTools.Constructor(t, Type.EmptyTypes);
                        if (ctor0 != null) return (Screen)ctor0.Invoke(null);
                    }
                    catch { }
                    return null;
                }

                public static bool LaunchFromFrontEnd()
                {
                    var gm = CastleMinerZGame.Instance;
                    if (gm?.FrontEnd == null) return false;

                    var s = TryCreateOldPicker();
                    if (s == null) return false;

                    gm.FrontEnd.PushScreen(s);
                    return true;
                }

                public static bool LaunchInGame()
                {
                    var gm = CastleMinerZGame.Instance;
                    var gs = gm?.GameScreen;
                    if (gs == null) return false;

                    var uiGroupFI = AccessTools.Field(gs.GetType(), "_uiGroup");
                    if (!(uiGroupFI?.GetValue(gs) is ScreenGroup uiGroup))
                    {
                        Log("OldPickerLauncher: _uiGroup not found on GameScreen; cannot push picker.");
                        return false;
                    }

                    var s = TryCreateOldPicker();
                    if (s == null) return false;

                    uiGroup.PushScreen(s);
                    return true;
                }
            }

            // ============================================================
            // INTERCEPT the handlers that cast to enums and crash.
            // We swallow only our row and launch the old picker UI.
            // ============================================================

            // A) In-game menu handler: GameScreen._inGameMenu_MenuItemSelected(object, SelectedMenuItemArgs).
            [HarmonyPatch(typeof(GameScreen))]
            private static class Patch_InGameMenu_Selection_Intercept
            {
                static IEnumerable<MethodBase> TargetMethods()
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

                        yield return m; // This is a real handler, not a b__ lambda.
                    }
                }

                static bool Prefix(object __instance, object sender, object e)
                {
                    try
                    {
                        if (PickerSelectUtil.IsOurSelection(sender, e))
                        {
                            if (!OldPickerLauncher.LaunchInGame())
                                Log("Failed to launch picker from in-game menu.");
                            return false; // Consume to avoid engine enum cast.
                        }
                    }
                    catch { /* Never break input. */ }
                    return true;
                }
            }

            // B) Front-end handler(s): FrontEndScreen.*MenuItemSelected(object, SelectedMenuItemArgs).
            [HarmonyPatch(typeof(FrontEndScreen))]
            private static class Patch_MainMenu_Selection_Intercept
            {
                static IEnumerable<MethodBase> TargetMethods()
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

                static bool Prefix(object __instance, object sender, object e)
                {
                    try
                    {
                        if (PickerSelectUtil.IsOurSelection(sender, e))
                        {
                            if (!OldPickerLauncher.LaunchFromFrontEnd())
                                Log("Failed to launch picker from main menu.");
                            return false; // Consume.
                        }
                    }
                    catch { }
                    return true;
                }
            }

            // === MainMenu ordering helper: Place our item just above "Exit" ===
            private static class MenuOrderHelper
            {
                static string menuText = "options";

                // Try to get the menu items list: look for a List<MenuItemElement> on MenuScreen or its bases.
                private static IList<MenuItemElement> GetItemList(MenuScreen screen)
                {
                    if (screen == null) return null;

                    var t = screen.GetType();
                    while (t != null)
                    {
                        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                        {
                            var ft = f.FieldType;
                            // Look for List<MenuItemElement> or IList<MenuItemElement>.
                            if (typeof(IList<MenuItemElement>).IsAssignableFrom(ft))
                            {
                                try { return (IList<MenuItemElement>)f.GetValue(screen); } catch { }
                            }
                            if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                            {
                                var arg = ft.GetGenericArguments()[0];
                                if (typeof(MenuItemElement).IsAssignableFrom(arg))
                                {
                                    if (f.GetValue(screen) is System.Collections.IList obj)
                                    {
                                        // Wrap with a typed list view.
                                        var typed = new List<MenuItemElement>(obj.Cast<MenuItemElement>());
                                        // If it's the actual underlying List<>, cast will already be typed.
                                        if (obj is IList<MenuItemElement> direct) return direct;
                                        // If not, we can't mutate; bail.
                                    }
                                }
                            }
                        }
                        t = t.BaseType;
                    }
                    return null;
                }

                // Pull the localized "Exit" text if accessible.
                private static string GetMenuText()
                {
                    try
                    {
                        var p = AccessTools.Property(AccessTools.TypeByName("DNA.CastleMinerZ.Globalization.Strings"), menuText);
                        if (p != null) return p.GetValue(null, null) as string;
                    }
                    catch { }
                    return null;
                }

                // Try to read the visible text of a menu item.
                private static string GetItemText(MenuItemElement mi)
                {
                    if (mi == null) return null;
                    foreach (var name in new[] { "Text", "Caption", "Title", "Label", "Content" })
                    {
                        var p = AccessTools.Property(mi.GetType(), name);
                        if (p != null && p.PropertyType == typeof(string))
                        {
                            try { return (string)p.GetValue(mi, null); } catch { }
                        }
                        var f = AccessTools.Field(mi.GetType(), name);
                        if (f != null && f.FieldType == typeof(string))
                        {
                            try { return (string)f.GetValue(mi); } catch { }
                        }
                    }
                    return null;
                }

                private static int FindAnchorIndex(IList<MenuItemElement> list)
                {
                    if (list == null || list.Count == 0) return -1;

                    var mainMenuText = GetMenuText();
                    if (!string.IsNullOrEmpty(mainMenuText))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var t = GetItemText(list[i]);
                            if (!string.IsNullOrEmpty(t) &&
                                string.Equals(t, mainMenuText, StringComparison.OrdinalIgnoreCase))
                            {
                                return i;
                            }
                        }
                    }
                    // Fallback: Assume last item is the "quit"/Main Menu action.
                    return list.Count - 1;
                }

                public static void MainMenu_PlaceAbove(MainMenu menu, string placeAbove)
                {
                    if (menu == null) return;
                    if (menuText != null) menuText = placeAbove;

                    var ours = TPMenuItemRegistry.Get(menu);
                    if (ours == null) return;

                    var list = GetItemList(menu);
                    if (list == null) return;

                    int ourIdx = list.IndexOf(ours);
                    if (ourIdx < 0) return;

                    // Compute target position = index of Exit (or last item).
                    int exitIdx = FindAnchorIndex(list);
                    if (exitIdx < 0) return;

                    // We want to insert directly above Exit.
                    int targetIdx = Math.Max(0, exitIdx);

                    // If we're already right above Exit, nothing to do.
                    if (ourIdx == targetIdx) return;

                    // Remove and reinsert before Exit.
                    // Note: Removing before computing final index is safer; recompute exit after removal.
                    list.RemoveAt(ourIdx);

                    // After removal, indices may have shifted; recompute exitIdx.
                    exitIdx = FindAnchorIndex(list);
                    if (exitIdx < 0) exitIdx = list.Count; // Safety.

                    targetIdx = Math.Max(0, exitIdx);
                    if (targetIdx > list.Count) targetIdx = list.Count;

                    list.Insert(targetIdx, ours);
                }

                public static void InGameMenu_PlaceAbove(InGameMenu menu, string placeAbove)
                {
                    if (menu == null) return;
                    if (menuText != null) menuText = placeAbove;

                    var ours = TPMenuItemRegistry.Get(menu);
                    if (ours == null) return;

                    var list = GetItemList(menu);
                    if (list == null) return;

                    int ourIdx = list.IndexOf(ours);
                    if (ourIdx < 0) return;

                    int anchorIdx = FindAnchorIndex(list);
                    if (anchorIdx < 0) return;

                    // We want our item directly above the "Main Menu" row.
                    int targetIdx = Math.Max(0, anchorIdx);

                    if (ourIdx == targetIdx) return;

                    list.RemoveAt(ourIdx);

                    // Recompute anchor after removal (indices may shift).
                    anchorIdx = FindAnchorIndex(list);
                    if (anchorIdx < 0) anchorIdx = list.Count;

                    targetIdx = Math.Max(0, anchorIdx);
                    if (targetIdx > list.Count) targetIdx = list.Count;

                    list.Insert(targetIdx, ours);
                }
            }

            // ============================================================
            // SAFETY NET: If a MainMenu/InGameMenu instance is rebuilt,
            // make sure our "Texture Packs" row is present again.
            // (Keeps it from "disappearing" on re-enter.)
            // ============================================================

            [HarmonyPatch(typeof(MainMenu), MethodType.Constructor, new[] { typeof(CastleMinerZGame) })]
            private static class Patch_MainMenu_AddItem_Ctor
            {
                static void Postfix(MainMenu __instance)
                {
                    var item = __instance.AddMenuItem("Texture Packs", TPMenuItemRegistry.Tag);
                    TPMenuItemRegistry.Remember(__instance, item);
                    MenuOrderHelper.MainMenu_PlaceAbove(__instance, "Options");
                }
            }

            [HarmonyPatch(typeof(MainMenu), "OnUpdate")]
            private static class Patch_MainMenu_EnsureTP_OnUpdate
            {
                static void Postfix(MainMenu __instance)
                {
                    if (TPMenuItemRegistry.Get(__instance) == null)
                    {
                        var item = __instance.AddMenuItem("Texture Packs", TPMenuItemRegistry.Tag);
                        TPMenuItemRegistry.Remember(__instance, item);
                        MenuOrderHelper.MainMenu_PlaceAbove(__instance, "Options");
                    }
                }
            }

            [HarmonyPatch(typeof(InGameMenu), MethodType.Constructor, new[] { typeof(CastleMinerZGame) })]
            private static class Patch_InGame_AddItem_Ctor
            {
                static void Postfix(InGameMenu __instance)
                {
                    var item = __instance.AddMenuItem("Texture Packs", TPMenuItemRegistry.Tag);
                    TPMenuItemRegistry.Remember(__instance, item);
                    MenuOrderHelper.InGameMenu_PlaceAbove(__instance, "Inventory");
                }
            }

            [HarmonyPatch(typeof(InGameMenu), "OnPushed")]
            private static class Patch_InGame_AddItem_OnPushed
            {
                static void Postfix(InGameMenu __instance)
                {
                    if (TPMenuItemRegistry.Get(__instance) == null)
                    {
                        var item = __instance.AddMenuItem("Texture Packs", TPMenuItemRegistry.Tag);
                        TPMenuItemRegistry.Remember(__instance, item);
                        MenuOrderHelper.InGameMenu_PlaceAbove(__instance, "Inventory");
                    }
                }
            }

            // ===== Deferred Apply (next Update) =====
            public static class DeferredApply
            {
                public static volatile bool Pending;
                public static string PackName;

                public static void Request(string pack)
                {
                    PackName = pack;
                    Pending = true;
                }
            }

            // One-shot runner: Executes the queued reload on the next Update tick.
            [HarmonyPatch(typeof(DNAGame), "Update")]
            private static class Patch_RunDeferredApply_NextUpdate
            {
                static void Postfix(DNAGame __instance)
                {
                    if (!DeferredApply.Pending) return;
                    DeferredApply.Pending = false;

                    try
                    {
                        // 1) Apply content reload.
                        TexturePackManager.DoReloadCore();

                        // 2) Make sure fonts are rebound & menus retarget the new fonts.
                        var gm = CastleMinerZGame.Instance;
                        var gd = gm?.GraphicsDevice;
                        if (gd != null)
                        {
                            FontPacks.ApplyAll(gd);
                            // Rebind everything currently on the front-end (so MainMenu immediately uses the new fonts).
                            try { MenuFontRebinder.RebindAllMenusOn(gm?.FrontEnd, gm); } catch { }
                        }

                        // 3) Nudge the adjuster after reload to avoid any weird scaling residue.
                        // try { Screen.Adjuster.Update(gm?.GraphicsDevice); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Log($"[Picker] Deferred reload error: {ex.Message}.");
                    }
                }
            }

            // Registry (unchanged).
            internal static class TPMenuItemRegistry
            {
                internal sealed class TexturePacksTag
                {
                    public static readonly TexturePacksTag Instance = new TexturePacksTag();
                    private TexturePacksTag() { }
                }

                private static readonly ConditionalWeakTable<MenuScreen, MenuItemElement> _items
                    = new ConditionalWeakTable<MenuScreen, MenuItemElement>();

                public static void Remember(MenuScreen screen, MenuItemElement item)
                {
                    try { _items.Remove(screen); } catch { }
                    if (item != null) _items.Add(screen, item);
                }

                public static MenuItemElement Get(MenuScreen screen)
                    => _items.TryGetValue(screen, out var mi) ? mi : null;

                public static object Tag => TexturePacksTag.Instance;
            }
        }
        #endregion

        #region Defer Draw / Retirement Tick

        /// <summary>
        /// After all SpriteBatch.End() calls, retire any GPU resources we swapped out this frame.
        /// </summary>
        [HarmonyPatch(typeof(ScreenGroup), "Draw")]
        internal static class Patch_Draw_FlushRetireQueue
        {
            [HarmonyPostfix]
            static void Postfix() => GpuRetireQueue.FlushAfterDraw();
        }
        #endregion

        #region Deferred Runners (Queued Work On Update)

        /// <summary>
        /// Runs queued icon swaps/uploads outside of Draw and outside of the reset window.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "Update")]
        static class Patch_RunQueuedIconSwap
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                // Only run when not resetting; re-arm the queue if anything throws (try again next tick).
                if (_itemSwapQueued && !_gdResetting)
                {
                    _itemSwapQueued = false;
                    try
                    {
                        var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                        if (gd != null) SwapIconAtlasesFromCpu(gd);
                    }
                    catch
                    {
                        _itemSwapQueued = true; // Try again next tick if something hiccups.
                    }
                }
            }
        }
        #endregion

        #region Patches

        #region Items: Small/Large Icon Atlases

        /// <summary>
        /// Ensures item icon PNGs are re-applied after the engine rebuilds the atlases.
        /// </summary>
        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.FinishInitialization))]
        internal static class Patch_TP_Items_ReapplyAfterBuild
        {
            [HarmonyPostfix]
            static void Postfix(GraphicsDevice device)
            {
                // Re-apply our PNGs after the atlases are (re)built.
                Log("FinishInitialization (InventoryItem) fired.");
                TexturePackManager.CaptureItemsBaselineIfNeeded();   // FIRST capture.
                TexturePackManager.ApplyItemIconReplacementsIfAny();

            }
        }
        #endregion

        #region Blocks & Item Models (Entity Skins)

        #region Blocks (Terrain Etc.) Entry

        /// <summary>
        /// Runs at content load; also triggers the item-entity skinner to wire per-entity CreateEntity hooks.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        internal static class Patch_TP_Apply_OnSecondaryLoad_DNA
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                // Debugging: Log("SecondaryLoad (DNA) fired.");
                // Use the safer deferred reload path instead of directly patching atlases here.
                TexturePackManager.BeginStartupLaunchGate();

                // Only now that content exists, patch all item CreateEntity overrides.
                ItemEntitySkinner.ApplyAfterContent();

                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                TexturePackManager.AttachDeviceResetHandlers(gd);
            }
        }
        #endregion

        #region Item Model Skins (Per-Entity)

        /// <summary>
        /// Harmony helper that finds every declared
        /// InventoryItem.InventoryItemClass.CreateEntity(ItemUse,bool)
        /// implementation and attaches a shared postfix. The postfix registers a
        /// per-entity model skin (if available) so the render patch can swap textures
        /// on a per-entity basis instead of globally.
        /// </summary>
        internal static class ItemEntitySkinner
        {
            private static bool _applied;
            private static Harmony _h;

            /// <summary>
            /// One-time setup that scans the CastleMinerZ item assembly for concrete
            /// CreateEntity(ItemUse,bool) overrides and patches them with a
            /// shared postfix. Must be called after content has loaded so that:
            ///
            ///   • All item types are already constructed / discoverable.
            ///   • Harmony can resolve method bodies (no "implemented method" issues).
            ///
            /// The patching is defensive:
            ///   • Only DECLARED overrides are patched (inherited implementations are
            ///     counted as "inherited(no-op)" and skipped).
            ///   • Abstract methods are counted separately and never patched.
            ///   • Failures are logged per-method instead of aborting the loop.
            /// </summary>
            public static void ApplyAfterContent()
            {
                if (_applied) return;
                _applied = true;

                _h = new Harmony($"castleminerz.mods.{typeof(GamePatches).Namespace}.itementities.patches");

                var baseType = typeof(InventoryItem).GetNestedType("InventoryItemClass",
                               BindingFlags.Public | BindingFlags.NonPublic);
                if (baseType == null)
                {
                    Log("ItemEntitySkinner: Base type not found.");
                    return;
                }

                var asm = baseType.Assembly;
                var postfix = new HarmonyMethod(typeof(ItemEntitySkinner), nameof(PostfixShim));

                var methods = new HashSet<MethodBase>();
                int candidates = 0, abstractCount = 0, inheritedCount = 0;

                foreach (var t in AccessTools.GetTypesFromAssembly(asm))
                {
                    if (t == null || !t.IsClass) continue;
                    if (!t.IsSubclassOf(baseType)) continue;

                    // Only methods DECLARED on this type (skip inherited implementations).
                    // This ensures m.DeclaringType == t so we don't double-patch bases.
                    var m = AccessTools.DeclaredMethod(t, "CreateEntity", new[] { typeof(ItemUse), typeof(bool) });
                    if (m == null) { inheritedCount++; continue; }
                    if (m.IsAbstract) { abstractCount++; continue; }

                    candidates++;
                    methods.Add(m);
                }

                int patched = 0;
                foreach (var m in methods)
                {
                    try
                    {
                        _h.Patch(m, postfix: postfix);
                        patched++;
                        // Log($"ItemEntitySkinner: patched {m.DeclaringType.FullName}.CreateEntity(ItemUse,bool).");
                    }
                    catch (Exception ex)
                    {
                        Log($"ItemEntitySkinner: Failed to patch {m.DeclaringType.FullName}.CreateEntity: {ex.GetType().Name}: {ex.Message}.");
                    }
                }

                Log($"ItemEntitySkinner: declared={candidates}, patched={patched}, abstract={abstractCount}, inherited(no-op)={inheritedCount}.");
            }

            /// <summary>
            /// Shared postfix for every patched CreateEntity(ItemUse,bool) override.
            /// Responsible for:
            ///
            ///   • Ignoring UI-only "entities" (inventory icons, etc.).
            ///   • Ensuring that each spawned entity is only skinned once
            ///     (<see cref="EntitySkinRegistry.MarkFirstTime"/> gate).
            ///   • Delegating to <see cref="TexturePackManager.TryApplyItemModelSkin"/>
            ///     so the registry can link this concrete entity to a cached model skin.
            ///
            /// Draw-time binding (effect texture swap) is performed separately by
            /// <see cref="Patch_PerEntityModelSkin_AtSetEffectParams"/>.
            /// </summary>
            public static void PostfixShim(InventoryItem.InventoryItemClass __instance,
                               ItemUse use,
                               bool attachedToLocalPlayer,
                               ref Entity __result)
            {
                // Nothing to do if the item did not spawn an entity or is only being used
                // in UI space (e.g., menus, inventory thumbnails).
                if (__result == null || use == ItemUse.UI) return;

                // Only apply skin the first time we see this entity instance.
                // This prevents double-registration when multiple CreateEntity overrides
                // in the inheritance chain are patched (e.g., gun base + concrete gun).
                if (!TexturePackManager.EntitySkinRegistry.MarkFirstTime(__result))
                    return;

                // NOTE: Pass both ID and the class Name so we can disambiguate variants
                // (different items may share IDs in different builds/branches).
                TexturePackManager.TryApplyItemModelSkin(__result, __instance.ID, __instance.Name);

                // NOTE: Geometry overrides MUST go through the tracking system (ItemModelGeometryOverrides)
                // so the original vanilla Model is captured per-entity and restored correctly on pack switch.
                // If you load/swap the Model without registering it, the custom geometry can "stick" across packs
                // (e.g., pack B applies only a skin but ends up reusing pack C's model).
                TexturePackManager.ItemModelGeometryOverrides.TryApplyItemModelGeometry(__result, __instance.ID, __instance.Name);
            }
        }

        #region Door Entities (Model Sheet Skin Apply)

        /// <summary>
        /// Applies full-sheet door model texture overrides as soon as a DoorEntity is constructed.
        /// </summary>
        /// <remarks>
        /// NOTE:
        /// - DoorEntity is model-driven, not terrain-atlas-driven.
        /// - TexturePackManager now loads door replacements from:
        ///     Packs\...\Models\Doors\NormalDoor.png
        ///     Packs\...\Models\Doors\IronDoor.png
        ///     Packs\...\Models\Doors\DiamondDoor.png
        ///     Packs\...\Models\Doors\TechDoor.png
        /// - Applying at construction time ensures newly created world doors, hand-held doors,
        ///   and pickup doors receive the correct active override before the next draw.
        /// </remarks>
        [HarmonyPatch(typeof(DoorEntity), MethodType.Constructor, new[] { typeof(DoorEntity.ModelNameEnum), typeof(BlockTypeEnum) })]
        internal static class Patch_DoorEntityCtor_TexturePackSkin
        {
            static void Postfix(DoorEntity __instance, DoorEntity.ModelNameEnum modelName)
            {
                // Apply geometry first so any PNG sheet override can target the final live model entity.
                TexturePackManager.DoorModelGeometryOverrides.TryApplyDoorModelGeometry(__instance, modelName);
                TexturePackManager.TryApplyDoorModelSkin(__instance, modelName);
            }
        }
        #endregion

        /// <summary>
        /// Draw-time Harmony patch that performs a per-entity texture swap just before
        /// a model's effect is used. It:
        ///
        ///   • Captures the first texture ever seen on each <see cref="Effect"/>
        ///     instance as that Effect's "baseline" texture.
        ///   • Looks up a per-entity skin in <see cref="EntitySkinRegistry"/>.
        ///   • If a skin exists for the current entity, binds that texture to the Effect.
        ///   • If no skin exists, restores the baseline texture so skins never bleed
        ///     between entities or survive texture-pack switches.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_PerEntityModelSkin_AtSetEffectParams
        {
            /// <summary>
            /// Target both <see cref="DNA.Drawing.ModelEntity"/> and, if present,
            /// DNA.Drawing.SkinnedModelEntity implementations of
            /// SetEffectParams(ModelMesh,Effect,GameTime,Matrix,Matrix,Matrix).
            ///
            /// This ensures per-entity skinning applies consistently to:
            ///   • Static item/tool models (e.g., pickaxes, guns, blocks).
            ///   • Skinned models (if CastleMinerZ uses them for any held items).
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods()
            {
                var sig = new[] {
                    typeof(Microsoft.Xna.Framework.Graphics.ModelMesh),
                    typeof(Microsoft.Xna.Framework.Graphics.Effect),
                    typeof(Microsoft.Xna.Framework.GameTime),
                    typeof(Microsoft.Xna.Framework.Matrix),
                    typeof(Microsoft.Xna.Framework.Matrix),
                    typeof(Microsoft.Xna.Framework.Matrix)
                };

                // Base.
                var baseMeth = AccessTools.Method(typeof(DNA.Drawing.ModelEntity), "SetEffectParams", sig);
                if (baseMeth != null) yield return baseMeth;

                // Skinned (if the game has it and it overrides SetEffectParams).
                var skinnedT = AccessTools.TypeByName("DNA.Drawing.SkinnedModelEntity");
                if (skinnedT != null)
                {
                    var skinnedMeth = AccessTools.DeclaredMethod(skinnedT, "SetEffectParams", sig);
                    if (skinnedMeth != null) yield return skinnedMeth;
                }
            }

            // --- Per-Effect default cache ----------------------------------------------------
            //
            // Idea:
            //   • For each Effect instance, remember the first texture we ever see on it.
            //   • That first texture is treated as the baseline/default.
            //   • When an entity does NOT have a skin, we restore that default.
            //
            // This avoids epoch tracking and texture ownership heuristics and guarantees
            // that even if a Gun's DNAEffect never switches back on its own, we always
            // know what its original texture was and can revert safely.
            //
            private sealed class TexHolder
            {
                public Texture2D Tex;
            }

            // Weak table so Effects can still be GC'd; we only retain the default texture
            // reference for as long as the Effect is alive.
            private static readonly ConditionalWeakTable<Effect, TexHolder> _defaults =
                new ConditionalWeakTable<Effect, TexHolder>();

            // Parameter names we'll try when the Effect isn't BasicEffect.
            private static readonly string[] _paramNames =
            {
                "DiffuseTexture", "Texture", "g_DiffuseTexture", "diffuseTexture",
                "BaseTexture", "Albedo", "albedoMap", "tex0", "Texture0", "DiffuseMap"
            };

            [HarmonyPostfix]
            private static void Postfix(
                object __instance,
                Microsoft.Xna.Framework.Graphics.ModelMesh mesh,
                Microsoft.Xna.Framework.Graphics.Effect effect)
            {
                if (effect == null)
                    return;

                if (!(__instance is DNA.Drawing.Entity ent))
                    return;

                // Do we have a live per-entity skin for this entity (and the texture
                // is still valid / not disposed)?
                bool hasSkin = TexturePackManager.EntitySkinRegistry.TryGetSkin(ent, out var skin)
                               && skin != null && !skin.IsDisposed;

                // Always make sure we've captured a baseline for this Effect before
                // we ever apply our skin. First-ever non-null texture wins.
                EnsureDefaultCaptured(effect);

                if (!hasSkin)
                {
                    // No skin for this entity: Restore the first-seen texture, if any.
                    var def = GetDefault(effect);
                    if (def != null)
                    {
                        TrySetTexture(effect, def);
                    }

                    return;
                }

                // Skin is active for this entity: Override the Effect's texture for this draw.
                TrySetTexture(effect, skin);
            }

            /// <summary>
            /// Capture the first non-null texture we ever see on this Effect instance.
            /// Subsequent calls are no-ops once a baseline is stored.
            /// </summary>
            private static void EnsureDefaultCaptured(Effect fx)
            {
                if (fx == null)
                    return;

                if (_defaults.TryGetValue(fx, out var holder) && holder?.Tex != null)
                    return; // Already have a baseline.

                var cur = GetCurrentTexture(fx);
                if (cur == null)
                    return; // Nothing useful to store yet.

                // First non-null wins for this Effect instance.
                holder = _defaults.GetOrCreateValue(fx);
                holder.Tex = cur;
            }

            /// <summary>
            /// Retrieve the previously-captured baseline texture for the given Effect,
            /// if one exists. Returns null if we never saw a non-null texture
            /// for this Effect.
            /// </summary>
            private static Texture2D GetDefault(Effect fx)
            {
                if (fx == null)
                    return null;

                return _defaults.TryGetValue(fx, out var holder) ? holder?.Tex : null;
            }

            /// <summary>
            /// Assign the given texture to the Effect, handling both <see cref="BasicEffect"/>
            /// and custom Effects with a known diffuse/texture parameter.
            /// </summary>
            private static void TrySetTexture(Effect fx, Texture2D tex)
            {
                if (fx is BasicEffect be)
                {
                    be.TextureEnabled = true;
                    be.Texture = tex;
                    return;
                }

                var p = FindTextureParam(fx);
                if (p != null)
                {
                    try { p.SetValue(tex); } catch { }
                }
            }

            /// <summary>
            /// Attempt to find a plausible texture parameter on a custom Effect using
            /// a small set of common diffuse/texture parameter names.
            /// </summary>
            private static EffectParameter FindTextureParam(Effect fx)
            {
                var coll = fx.Parameters;
                if (coll == null)
                    return null;

                foreach (var name in _paramNames)
                {
                    var p = coll[name];
                    if (p != null)
                        return p;
                }
                return null;
            }

            /// <summary>
            /// Get the texture currently bound to the Effect:
            ///   • For <see cref="BasicEffect"/>, returns <see cref="BasicEffect.Texture"/>.
            ///   • For custom Effects, attempts to read the value of the first matching
            ///     texture parameter discovered by <see cref="FindTextureParam"/>.
            /// </summary>
            private static Texture2D GetCurrentTexture(Effect fx)
            {
                try
                {
                    if (fx is BasicEffect be)
                        return be.Texture;

                    var p = FindTextureParam(fx);
                    if (p != null)
                    {
                        try { return p.GetValueTexture2D(); } catch { }
                    }
                }
                catch
                {
                    // Swallow exceptions: failing to introspect Effect state should never
                    // break the frame; worst case we just don't capture a baseline here.
                }
                return null;
            }
        }
        #endregion

        #endregion

        #region Sounds (Music/Ambience/One-Shots)

        /// <summary>
        /// Front-end ambience muter: ensures menu ambient categories are quiet and stops our loops.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "OnPushed")]
        static class Patch_Menu_AmbienceOff
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                var g = CastleMinerZGame.Instance;
                if (g != null)
                {
                    try
                    {
                        g.DaySounds.SetVolume(0f);
                        g.NightSounds.SetVolume(0f);
                        g.CaveSounds.SetVolume(0f);
                        g.HellSounds.SetVolume(0f);
                    }
                    catch { }
                }

                // Stop your own ambience loops too (if any were left playing).
                ReplacementAudio.Ambience.StopAll();
            }
        }

        /// <summary>
        /// Starts replacement music theme on menu boot; mutes engine category when shadowing.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "OnPushed")]
        static class Patch_MenuTheme_OnBoot
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                var g = CastleMinerZGame.Instance;
                if (g == null) return;

                // Shadow the engine cue with our replacement "Theme".
                // (If you expose this as a config, read the name from config instead of the literal.)
                MusicShadow.Start("Theme", g.MusicCue);

                // Keep the engine's music category silent while we're shadowing.
                if (MusicShadow.IsActive)
                {
                    try { g.MusicSounds.SetVolume(0f); } catch { }
                }
            }
        }

        /// <summary>
        /// Mirrors calls to PlayMusic into the replacement system; toggles engine category volume.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "PlayMusic")]
        static class Patch_PlayMusic_Postfix
        {
            [HarmonyPostfix]
            static void Postfix(CastleMinerZGame __instance, string cueName)
            {
                MusicShadow.Start(cueName, __instance.MusicCue);
                bool haveReplacement = MusicShadow.IsActive;

                // If we're shadowing, keep the engine category silent. Otherwise restore it.
                try { __instance.MusicSounds.SetVolume(haveReplacement ? 0f : __instance.PlayerStats.musicVolume); } catch { }

                if (!haveReplacement)
                    MusicShadow.Stop();
            }
        }

        /// <summary>
        /// Music update mirror: Keeps engine music category muted while replacement is active.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "UpdateMusic")]
        static class Patch_UpdateMusic_Postfix
        {
            [HarmonyPostfix]
            static void Postfix(CastleMinerZGame __instance, GameTime time)
            {
                // Drive your replacement's volume and stop/start logic.
                MusicShadow.Tick(__instance);

                // Mute the engine category if we're providing a replacement.
                if (MusicShadow.IsActive)
                {
                    try { __instance.MusicSounds.SetVolume(0f); } catch { }
                }
            }
        }

        /// <summary>
        /// Front-end transition: Ensure replacement music is stopped before entering the menu.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "OnPushed")]
        static class Patch_StopMusic_OnFrontEndEnter
        {
            [HarmonyPrefix] static void Prefix() => MusicShadow.Stop();
        }

        /// <summary>
        /// Ambience mirroring: Mute engine categories only when a replacement is present; mirror volumes to our loops.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), nameof(CastleMinerZGame.SetAudio))]
        static class Patch_SoundReplace_Ambience
        {
            static void Postfix(CastleMinerZGame __instance, float day, float night, float cave, float hell)
            {
                try
                {
                    // Menu: Keep everything silent and stop your loops.
                    if (!IsInGame(__instance))
                    {
                        __instance.DaySounds.SetVolume(0f);
                        __instance.NightSounds.SetVolume(0f);
                        __instance.CaveSounds.SetVolume(0f);
                        __instance.HellSounds.SetVolume(0f);
                        ReplacementAudio.Ambience.StopAll();
                        return;
                    }

                    AmbiencePresence.EnsureScanned();

                    // If underwater, force silence in both systems.
                    if (__instance.LocalPlayer != null && __instance.LocalPlayer.Underwater)
                        day = night = cave = hell = 0f;

                    // Per-category control: Mute engine only if we have a replacement for that category.
                    // DAY -> Birds.
                    if (AmbiencePresence.HasBirds)
                    {
                        __instance.DaySounds.SetVolume(0f);
                        ReplacementAudio.Ambience.Apply(day, 0f, 0f, 0f);
                    }
                    else
                    {
                        // No replacement -> let vanilla day ambience play.
                        __instance.DaySounds.SetVolume(MathHelper.Clamp(day, 0f, 1f));
                    }

                    // NIGHT -> Crickets.
                    if (AmbiencePresence.HasCrickets)
                    {
                        __instance.NightSounds.SetVolume(0f);
                        ReplacementAudio.Ambience.Apply(0f, night, 0f, 0f);
                    }
                    else
                    {
                        __instance.NightSounds.SetVolume(MathHelper.Clamp(night, 0f, 1f));
                    }

                    // CAVE -> Drips.
                    if (AmbiencePresence.HasDrips)
                    {
                        __instance.CaveSounds.SetVolume(0f);
                        ReplacementAudio.Ambience.Apply(0f, 0f, cave, 0f);
                    }
                    else
                    {
                        __instance.CaveSounds.SetVolume(MathHelper.Clamp(cave, 0f, 1f));
                    }

                    // HELL -> lostSouls.
                    if (AmbiencePresence.HasLostSouls)
                    {
                        __instance.HellSounds.SetVolume(0f);
                        ReplacementAudio.Ambience.Apply(0f, 0f, 0f, hell);
                    }
                    else
                    {
                        __instance.HellSounds.SetVolume(MathHelper.Clamp(hell, 0f, 1f));
                    }
                }
                catch { }
            }

            static bool IsInGame(CastleMinerZGame instance)
            {
                var g = instance;
                return g != null && g.GameScreen != null && g.CurrentNetworkSession != null;
            }
        }

        /// <summary>
        /// Preload replacement SFX when entering the front-end for snappy UI sounds.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "OnPushed")]
        static class Patch_PreloadSfx_OnMenuEnter
        {
            [HarmonyPrefix]
            static void Prefix()
            {
                ReplacementAudio.PreloadSfx(); // loads Click.wav etc. if present
            }
        }

        // Cues the engine stores and later manipulates (must never be null).
        private static readonly HashSet<string> _noSkipCues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Music.
                "Theme","SpaceTheme","song1","song2","song3","song4","song5","song6",

                // Ambience loops (engine stores these too).
                "Birds","Crickets","Drips","lostSouls"
            };

        /// <summary>
        /// One-shot 2D sound replacement. If a replacement exists, skip creating engine cue.
        /// </summary>
        [HarmonyPatch(typeof(DNA.Audio.SoundManager), "PlayInstance", new[] { typeof(string) })]
        static class Patch_SoundReplace_2D_Prefix
        {
            [HarmonyPrefix]
            static bool Prefix(string name, ref Microsoft.Xna.Framework.Audio.Cue __result)
            {
                // IMPORTANT:
                // Never skip cues that vanilla stores (MusicCue / DayCue / NightCue / etc).
                if (_noSkipCues.Contains(name))
                    return true;

                if (ReplacementAudio.TryPlay2D_IfAvailable(name))
                {
                    __result = null; // Prevent engine cue creation/playback.
                    return false;    // SKIP original.
                }
                return true;         // No replacement -> let vanilla play.
            }
        }

        /// <summary>
        /// One-shot 3D sound replacement. If a replacement exists, skip engine 3D cue.
        /// </summary>
        [HarmonyPatch(typeof(DNA.Audio.SoundManager), "PlayInstance", new[] { typeof(string), typeof(Microsoft.Xna.Framework.Audio.AudioEmitter) })]
        static class Patch_SoundReplace_3D_Prefix
        {
            [HarmonyPrefix]
            static bool Prefix(string name, Microsoft.Xna.Framework.Audio.AudioEmitter emitter,
                               ref DNA.Audio.SoundCue3D __result)
            {
                if (ReplacementAudio.TryPlay3D_IfAvailable(name, emitter))
                {
                    __result = null; // Prevent engine 3D cue.
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// ReplacementAudio per-frame maintenance tick (lifetime, fades, cleanup).
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "Update")]
        static class Patch_SoundReplace_Update
        {
            static void Postfix() => ReplacementAudio.Update();
        }
        #endregion

        #region Fonts (SpriteFont Swaps / UI Rebinders)

        /// <summary>
        /// Applies font packs to front-end screens and rebinds UI trees.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "OnPushed")]
        static class Patch_FontPacks_Apply_ToAllMenus
        {
            [HarmonyPostfix]
            static void Postfix(FrontEndScreen __instance)
            {
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null) return;

                // FrontEndFontRebinder.Rebind(__instance, gm);

                FontPacks.CaptureBaselineIfNeeded();

                // First swap all SpriteFont fields on CastleMinerZGame (your existing runtime font loader).
                FontPacks.ApplyAll(gd);

                // Then rebind every FrontEndScreen menu (Main Menu, "Choose Game Mode", etc.)
                MenuFontRebinder.RebindAllMenusOn(__instance, gm);
            }
        }

        /// <summary>
        /// Applies fonts to Options screen when pushed and rebinds the subtree.
        /// </summary>
        [HarmonyPatch(typeof(OptionsScreen), "OnPushed")]
        static class Patch_FontPacks_Apply_ToOptions
        {
            [HarmonyPostfix]
            static void Postfix(OptionsScreen __instance)
            {
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null) return;

                // Rebind ONLY the OptionsScreen subtree, using safe traversal.
                UIOptionsFontRebinder.RebindTree(__instance, gm);
            }
        }

        #region Replacements

        /// <summary>
        /// Connecting/loading sub-screens: ensure fonts are rebound before draw.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "_connectingScreen_BeforeDraw")]
        static class Patch_FE_Connecting_BeforeDraw_Font
        {
            [HarmonyPrefix]
            static void Prefix(FrontEndScreen __instance)
            {
                var gm = CastleMinerZGame.Instance;
                FrontEndFontRebinder.Rebind(__instance, gm);
            }
        }

        [HarmonyPatch(typeof(FrontEndScreen), "_loadingScreen_BeforeDraw")]
        static class Patch_FE_Loading_BeforeDraw_Font
        {
            [HarmonyPrefix]
            static void Prefix(FrontEndScreen __instance)
            {
                var gm = CastleMinerZGame.Instance;
                FrontEndFontRebinder.Rebind(__instance, gm);
            }
        }

        /// <summary>
        /// Console: Apply fonts to chat input screen at push-time.
        /// </summary>
        [HarmonyPatch(typeof(PlainChatInputScreen), "OnPushed")]
        static class Patch_FontPacks_Apply_ToConsole
        {
            [HarmonyPostfix]
            static void Postfix(PlainChatInputScreen __instance)
            {
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null) return;

                // Rebind ONLY the OptionsScreen subtree, using safe traversal.
                UIOptionsFontRebinder.RebindTree(__instance, gm);
            }
        }

        /// <summary>
        /// In-game menu: Rebind menu and options subtree to current fonts.
        /// </summary>
        [HarmonyPatch(typeof(InGameMenu), "OnPushed")]
        static class Patch_FontPacks_Apply_ToInGameMenu
        {
            [HarmonyPostfix]
            static void Postfix(InGameMenu __instance)
            {
                var gm = CastleMinerZGame.Instance;
                if (gm?.GraphicsDevice == null) return;

                MenuFontRebinder.RebindMenu(__instance, gm);

                // Rebind ONLY the OptionsScreen subtree, using safe traversal.
                UIOptionsFontRebinder.RebindTree(__instance, gm);
            }
        }

        /// <summary>
        /// Main menu item draw: Rebind subtree if dirty (lazy).
        /// </summary>
        [HarmonyPatch(typeof(MainMenu), "OnDraw")]
        static class Patch_FontPacks_Apply_ToMainMenuItems
        {
            [HarmonyPostfix]
            static void Postfix(MainMenu __instance)
            {
                var gm = CastleMinerZGame.Instance;
                if (gm?.GraphicsDevice == null) return;

                // Rebind ONLY the OptionsScreen subtree, using safe traversal.
                UIOptionsFontRebinder.RebindTreeIfDirty(__instance, gm);
            }
        }

        /// <summary>
        /// Game mode menu (internal type) draw: late font rebind.
        /// </summary>
        [HarmonyPatch]
        static class Patch_FontPacks_Apply_ToGameModeMenu_OnDraw
        {
            // Dynamically resolve: Internal class DNA.CastleMinerZ.UI.GameModeMenu
            // protected override void OnDraw(GraphicsDevice, SpriteBatch, GameTime)
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("DNA.CastleMinerZ.UI.GameModeMenu");
                return AccessTools.Method(t, "OnDraw",
                    new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) });
            }

            // Use object __instance to avoid referencing the internal type at compile time.
            static void Postfix(object __instance)
            {
                var gm = CastleMinerZGame.Instance;
                if (gm?.GraphicsDevice == null) return;

                // Rebind the GameModeMenu subtree to the current fonts.
                UIOptionsFontRebinder.RebindTreeIfDirty(__instance, gm);
            }
        }

        /// <summary>
        /// Console element draw: Lazy font rebind for scrolling/output text.
        /// </summary>
        [HarmonyPatch(typeof(ConsoleElement), "OnDraw")]
        static class Patch_FontPacks_Apply_ToConsoleText
        {
            [HarmonyPostfix]
            static void Postfix(ConsoleElement __instance)
            {
                var gm = CastleMinerZGame.Instance;
                if (gm?.GraphicsDevice == null) return;

                // Rebind ONLY the OptionsScreen subtree, using safe traversal.
                UIOptionsFontRebinder.RebindTreeIfDirty(__instance, gm);
            }
        }
        #endregion

        /// <summary>
        /// ChooseOnlineGameScreen: Apply medium/small fonts to specific buttons/headers.
        /// </summary>
        [HarmonyPatch(typeof(ChooseOnlineGameScreen), "OnPushed")]
        static class Patch_ChooseOnlineGameScreen_ButtonFonts
        {
            [HarmonyPostfix]
            static void Postfix(ChooseOnlineGameScreen __instance)
            {
                var gm = CastleMinerZGame.Instance;

                // Back button.
                UIFontUtil.SetFontByField(__instance, "_backButton",    gm?._medFont);

                // Right column buttons.
                UIFontUtil.SetFontByField(__instance, "_selectButton", gm?._medFont);
                UIFontUtil.SetFontByField(__instance, "_refreshButton", gm?._medFont); // "Search Again".

                // Column headers.
                var small = gm?._smallFont;
                foreach (var f in new[]{
                    "_nameButton","_dateButton","_numPLayersButton",
                    "_MaxPlayersButton","_modeButton","_numberFriendsButton"
                })
                {
                    UIFontUtil.SetFontByField(__instance, f, small);
                }
            }
        }
        #endregion

        #region Screen Splashes (Load/Menu/Logo/DialogBack)

        /// <summary>
        /// LoadScreen texture swap with vanilla baseline capture (safe fallback).
        /// </summary>
        [HarmonyPatch(typeof(LoadScreen), "OnDraw")]
        public static class Patch_LoadScreen_ImageFromPack
        {
            static bool loadScreenSplash_swapped;
            static readonly FieldInfo LoadScreen_Image = AccessTools.Field(typeof(LoadScreen), "_image");

            [HarmonyPrefix]
            static void Prefix(LoadScreen __instance)
            {
                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null || LoadScreen_Image == null) return;

                // Capture vanilla before touching _image.
                SplashPacks.CaptureLoadScreenBaseline(__instance);

                if (!loadScreenSplash_swapped)
                {
                    // Try pack image; else fall back to vanilla baseline.
                    if (!SplashTextures.TryLoadExact(gd, "Screens", "LoadScreen", out Texture2D next))
                        next = SplashPacks.BaseLoadImage;

                    if (next != null)
                        SplashPacks.ReplaceLoadScreenImage(__instance, next);

                    loadScreenSplash_swapped = true;
                }
            }
        }

        /// <summary>
        /// Menu backdrop swap with vanilla capture.
        /// </summary>
        [HarmonyPatch(typeof(MenuBackdropScreen), "OnDraw")]
        public static class Patch_MenuBackdrop_FromPack
        {
            static bool menuBack_swapped;

            [HarmonyPrefix]
            static void Prefix()
            {
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null) return;

                // Capture vanilla before any change.
                SplashPacks.CaptureBaselineFromGame(gm);

                if (!menuBack_swapped)
                {
                    if (SplashTextures.TryLoadExact(gd, "Screens", "MenuBack", out var custom))
                        SplashPacks.ReplaceMenuBackdrop(gm, custom);
                    // else leave vanilla captured above.
                    menuBack_swapped = true;
                }
            }
        }

        /// <summary>
        /// Logo + DialogBack swap at SecondaryLoad; validates baseline capture.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        public static class Patch_Logo_FromPack
        {
            static bool logo_swapped, dialogBack_swapped;

            [HarmonyPrefix]
            static void Prefix()
            {
                var gm = CastleMinerZGame.Instance;
                var gd = gm?.GraphicsDevice;
                if (gd == null) return;

                // Capture vanilla before any change.
                SplashPacks.CaptureBaselineFromGame(gm);

                if (!logo_swapped)
                {
                    if (SplashTextures.TryLoadExact(gd, "Screens", "Logo", out var customLogo))
                        SplashPacks.ReplaceLogo(gm, customLogo);
                    logo_swapped = true;
                }
                if (!dialogBack_swapped)
                {
                    if (SplashTextures.TryLoadExact(gd, "Screens", "DialogBack", out var customDlg))
                        SplashPacks.ReplaceDialogBack(gm, customDlg);
                    dialogBack_swapped = true;
                }
            }
        }
        #endregion

        #region Inventory Sprites (Crafting/Crate)

        /// <summary>
        /// Applies/Restores inventory sprites for CraftingScreen on push.
        /// </summary>
        [HarmonyPatch(typeof(CraftingScreen), "OnPushed")]
        static class Patch_CraftingScreen_ImageFromPack
        {
            static readonly FieldInfo F_Background = AccessTools.Field(typeof(CraftingScreen), "_background");
            static readonly FieldInfo F_Selector = AccessTools.Field(typeof(CraftingScreen), "_gridSelector");
            static readonly FieldInfo F_GridSquare = AccessTools.Field(typeof(CraftingScreen), "_gridSquare");
            static readonly FieldInfo F_Tier2Back = AccessTools.Field(typeof(CraftingScreen), "_tier2Back");
            static readonly FieldInfo F_HudGrid = AccessTools.Field(typeof(CraftingScreen), "_gridSprite");
            static readonly FieldInfo F_CraftSelect = AccessTools.Field(typeof(CraftingScreen), "_craftSelector");

            const string Folder = "Inventory";

            [HarmonyPostfix]
            static void Postfix(CraftingScreen __instance)
            {
                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null) return;

                UISpritePacks.ApplyOrRestore(gd, __instance, F_Background, Folder, "BlockUIBack");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_Selector, Folder, "Selector");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_GridSquare, Folder, "SingleGrid");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_Tier2Back, Folder, "Tier2Back");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_HudGrid, Folder, "HudGrid");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_CraftSelect, Folder, "CraftSelector");
            }
        }

        /// <summary>
        /// Applies/Restores inventory sprites for CrateScreen on push.
        /// </summary>
        [HarmonyPatch(typeof(CrateScreen), "OnPushed")]
        static class Patch_CrateScreen_ImageFromPack
        {
            static readonly FieldInfo F_Selector = AccessTools.Field(typeof(CrateScreen), "_gridSelector");
            static readonly FieldInfo F_InventoryGrid = AccessTools.Field(typeof(CrateScreen), "_grid");
            static readonly FieldInfo F_HudGrid = AccessTools.Field(typeof(CrateScreen), "_gridSprite");

            const string Folder = "Inventory";

            [HarmonyPostfix]
            static void Postfix(CrateScreen __instance)
            {
                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null) return;

                UISpritePacks.ApplyOrRestore(gd, __instance, F_Selector, Folder, "Selector");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_InventoryGrid, Folder, "InventoryGrid");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_HudGrid, Folder, "HudGrid");
            }
        }
        #endregion

        #region HUD Sprites

        /// <summary>
        /// Applies/Restores HUD sprites each draw (epoch-based); automatically handles pack switches.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnDraw")]
        static class Patch_InGameHUD_ImageFromPack
        {
            static readonly FieldInfo F_damageArrow    = AccessTools.Field(typeof(InGameHUD), "_damageArrow");
            static readonly FieldInfo F_gridSprite     = AccessTools.Field(typeof(InGameHUD), "_gridSprite");
            static readonly FieldInfo F_selectorSprite = AccessTools.Field(typeof(InGameHUD), "_selectorSprite");
            static readonly FieldInfo F_crosshair      = AccessTools.Field(typeof(InGameHUD), "_crosshair");
            static readonly FieldInfo F_crosshairTick  = AccessTools.Field(typeof(InGameHUD), "_crosshairTick");
            static readonly FieldInfo F_emptyStamina   = AccessTools.Field(typeof(InGameHUD), "_emptyStaminaBar");
            static readonly FieldInfo F_emptyHealth    = AccessTools.Field(typeof(InGameHUD), "_emptyHealthBar");
            static readonly FieldInfo F_fullHealth     = AccessTools.Field(typeof(InGameHUD), "_fullHealthBar");
            static readonly FieldInfo F_bubbleBar      = AccessTools.Field(typeof(InGameHUD), "_bubbleBar");
            static readonly FieldInfo F_sniperScope    = AccessTools.Field(typeof(InGameHUD), "_sniperScope");
            static readonly FieldInfo F_missileLocking = AccessTools.Field(typeof(InGameHUD), "_missileLocking");
            static readonly FieldInfo F_missileLock    = AccessTools.Field(typeof(InGameHUD), "_missileLock");

            const string Folder = "HUD";

            [HarmonyPostfix]
            static void Postfix(InGameHUD __instance)
            {
                var gd = CastleMinerZGame.Instance?.GraphicsDevice;
                if (gd == null) return;

                UISpritePacks.ApplyOrRestore(gd, __instance, F_damageArrow,    Folder, "DamageArrow");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_gridSprite,     Folder, "HudGrid");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_selectorSprite, Folder, "Selector");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_crosshair,      Folder, "CrossHair");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_crosshairTick,  Folder, "CrossHairTick");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_emptyStamina,   Folder, "StaminaBarEmpty");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_emptyHealth,    Folder, "HealthBarEmpty");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_fullHealth,     Folder, "HealthBarFull");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_bubbleBar,      Folder, "BubbleBar");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_sniperScope,    Folder, "SniperScope");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_missileLocking, Folder, "MissileLocking");
                UISpritePacks.ApplyOrRestore(gd, __instance, F_missileLock,    Folder, "MissileLock");
            }
        }
        #endregion

        #region Player, Enemy, Dragon, + Extra Skins

        /// <summary>
        /// Applies player model skin to the proxy model at SecondaryLoad (safe timing).
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        internal static class Patch_TP_Apply_ModelTextures
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                var proxyModelField = AccessTools.Field(typeof(DNA.CastleMinerZ.Player), "ProxyModel");
                if (proxyModelField?.GetValue(null) is Model proxyModel)
                {
                    ModelSkinManager.TryApplyPlayerSkin(proxyModel, "Player");
                }
            }
        }

        /// <summary>
        /// One-time application of active model skins + model geometry (after a valid draw screen appears).
        /// Captures vanilla baselines first for safe fallback.
        /// </summary>
        [HarmonyPatch(typeof(LoadScreen), "OnDraw")]
        static class Patch_LoadScreen_ApplyModelSkins
        {
            static bool modelSkins_swapped;

            [HarmonyPostfix]
            static void Postfix(LoadScreen __instance)
            {
                if (!modelSkins_swapped)
                {
                    // 1) Capture true vanilla baselines (skins + geometry).
                    ModelSkinManager.CaptureBaselinesIfNeeded();

                    // 2) Apply active pack skins once we have a real draw screen.
                    ModelSkinManager.ApplyActiveModelSkinsNow();

                    // 3) Apply active pack model GEOMETRY once (enemies/player/dragons are global overrides).
                    //    This mirrors what DoReloadCore() does on pack switches.
                    TexturePackManager.ModelGeometryManager.OnPackSwitched();
                    TexturePackManager.ModelGeometryManager.ApplyActiveModelGeometryNow();

                    modelSkins_swapped = true;
                }
            }
        }
        #endregion

        #region Terrain Shaders (BlockEffect)

        /// <summary>
        /// BlockTerrain Shader Load Intercept (BlockEffect).
        ///
        /// Purpose:
        ///   - Intercept the single terrain shader load inside BlockTerrain(ContentManager),
        ///     replacing cm.Load<Effect>("Shaders\\BlockEffect") with our helper:
        ///       ShaderOverride.LoadBlockEffectOrVanilla(...)
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain))]
        internal static class Patch_BlockTerrain_Ctor_BlockEffectOverride
        {
            /// <summary>
            /// Target: BlockTerrain(ContentManager cm) constructor.
            /// </summary>
            static MethodBase TargetMethod()
                => AccessTools.Constructor(typeof(BlockTerrain), new[] { typeof(ContentManager) });

            /// <summary>
            /// Transpiler:
            ///   - Finds the callvirt to ContentManager.Load<Effect>(string).
            ///   - Replaces it with a static call to ShaderOverride.LoadBlockEffectOrVanilla(cm, assetName).
            /// </summary>
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);

                try
                {
                    var loadGeneric = AccessTools.Method(typeof(ContentManager), "Load", new[] { typeof(string) });
                    var loadEffect = loadGeneric?.MakeGenericMethod(typeof(Effect));

                    var helper = AccessTools.Method(typeof(ShaderOverride), nameof(ShaderOverride.LoadBlockEffectOrVanilla));

                    if (loadEffect == null || helper == null)
                    {
                        Log("[Shaders] Transpiler: Missing Load<Effect> or helper method; no changes applied.");
                        return code;
                    }

                    int replaced = 0;

                    for (int i = 0; i < code.Count; i++)
                    {
                        // Replace: callvirt instance Effect ContentManager::Load<Effect>(string)
                        // With:    call              Effect ShaderOverride::LoadBlockEffectOrVanilla(ContentManager,string)
                        if (code[i].Calls(loadEffect))
                        {
                            code[i] = new CodeInstruction(OpCodes.Call, helper);
                            replaced++;
                            // Stack remains correct: (cm, assetName) -> Effect.
                        }
                    }

                    Log($"[Shaders] Transpiler: BlockTerrain(.ctor) replaced {replaced} Load<Effect> call(s).");
                    if (replaced == 0)
                        Log("[Shaders] Transpiler WARNING: Did not find the expected cm.Load<Effect>(...) call.");

                    return code;
                }
                catch (Exception ex)
                {
                    Log($"[Shaders] Transpiler FAILED: {ex}.");
                    return code; // best-effort: never break patching
                }
            }
        }
        #endregion

        #region Skys (ClearSky, NightSky, SunSet, DawnSky, TextureSky)

        /// <summary>
        /// Sky Startup Load + Apply.
        ///
        /// Purpose:
        ///   - Runs once after the game's SecondaryLoad.
        ///   - Ensures the vanilla sky textures are loaded (guarded).
        ///   - Immediately applies SkyPacks so the active pack's sky overrides are live at startup
        ///     (no need to /tpreload just to see pack skies).
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        internal static class Patch_Skys_StartupLoadAndApply
        {
            private static bool _did;

            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_did) return;
                _did = true;

                try
                {
                    var gm = CastleMinerZGame.Instance;
                    var gd = gm?.GraphicsDevice;
                    if (gm == null || gd == null) return;

                    // Guard: If something ever prevented the vanilla load, ensure it.
                    if (!SkyLoaded())
                    {
                        ModLoader.LogSystem.Log("[Skys] Startup: sky textures not loaded yet; calling CastleMinerSky.LoadTextures().");
                        try { CastleMinerSky.LoadTextures(); }
                        catch (Exception ex)
                        {
                            ModLoader.LogSystem.Log($"[Skys] Startup: CastleMinerSky.LoadTextures FAILED: {ex}.");
                        }
                    }
                    else
                    {
                        // ModLoader.LogSystem.Log("[Skys] Startup: Vanilla sky textures already loaded.");
                    }

                    // Apply active pack sky overrides immediately (partial overrides allowed if you use the baseline-style SkyPacks).
                    try
                    {
                        SkyPacks.OnPackSwitched(gd, gm);
                        ModLoader.LogSystem.Log("[Skys] Startup: SkyPacks applied.");
                    }
                    catch (Exception ex)
                    {
                        ModLoader.LogSystem.Log($"[Skys] Startup: SkyPacks apply FAILED: {ex}.");
                    }
                }
                catch (Exception ex)
                {
                    ModLoader.LogSystem.Log($"[Skys] Startup patch FAILED: {ex}.");
                }
            }

            /// <summary>
            /// Returns true if the minimum sky assets are present and not disposed.
            /// (We only need a subset here to consider skies "loaded enough".)
            /// </summary>
            private static bool SkyLoaded()
            {
                try
                {
                    // If any of these are null/disposed, we consider skies "not loaded".
                    if (!(AccessTools.Field(typeof(CastleMinerSky), "_dayTexture")?.GetValue(null)   is TextureCube day)   ||
                        !(AccessTools.Field(typeof(CastleMinerSky), "_nightTexture")?.GetValue(null) is TextureCube night) ||
                        !(AccessTools.Field(typeof(CastleMinerSky), "_blendEffect")?.GetValue(null)  is Effect fx))
                        return false;

                    if (day.IsDisposed || night.IsDisposed || fx.IsDisposed) return false;

                    return true;
                }
                catch { return false; }
            }
        }
        #endregion

        #region Startup Apply (Models: Skins + Geometry)

        /// <summary>
        /// One-time startup application of active model skins + model geometry during SecondaryLoad.
        /// Rationale:
        /// - OnDraw can be too late/too inconsistent depending on screen flow.
        /// - SecondaryLoad guarantees core content and device are available early.
        /// - Captures vanilla baselines first for safe fallback.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        internal static class Patch_Models_StartupLoadAndApply
        {
            private static bool _did;

            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_did) return;
                _did = true;

                try
                {
                    var gm = CastleMinerZGame.Instance;
                    var gd = gm?.GraphicsDevice;
                    if (gm == null || gd == null) return;

                    // 1) Capture true vanilla baselines (skins + geometry).
                    try
                    {
                        TexturePackManager.ModelSkinManager.CaptureBaselinesIfNeeded();
                    }
                    catch (Exception ex)
                    {
                        ModLoader.LogSystem.Log($"[Models] Startup: CaptureBaselinesIfNeeded FAILED: {ex}.");
                    }

                    // 2) Apply active pack skins immediately.
                    try
                    {
                        TexturePackManager.ModelSkinManager.ApplyActiveModelSkinsNow();
                        ModLoader.LogSystem.Log("[Models] Startup: Model skins applied.");
                    }
                    catch (Exception ex)
                    {
                        ModLoader.LogSystem.Log($"[Models] Startup: ApplyActiveModelSkinsNow FAILED: {ex}.");
                    }

                    // 3) Apply active pack model GEOMETRY immediately (enemies/player/dragons are global overrides).
                    try
                    {
                        TexturePackManager.ModelGeometryManager.OnPackSwitched();
                        TexturePackManager.ModelGeometryManager.ApplyActiveModelGeometryNow();
                        ModLoader.LogSystem.Log("[Models] Startup: Model geometry applied.");
                    }
                    catch (Exception ex)
                    {
                        ModLoader.LogSystem.Log($"[Models] Startup: Model geometry apply FAILED: {ex}.");
                    }
                }
                catch (Exception ex)
                {
                    ModLoader.LogSystem.Log($"[Models] Startup patch FAILED: {ex}.");
                }
            }
        }
        #endregion

        #region Startup Launch Gate - New World

        /// <summary>
        /// Blocks starting a new world until startup texture-pack work is fully complete.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "startWorld")]
        internal static class Patch_TP_BlockStartWorldUntilReady
        {
            [HarmonyPrefix]
            static bool Prefix(FrontEndScreen __instance)
            {
                if (TexturePackManager.CanLaunchWorlds)
                    return true;

                __instance.ShowUIDialog("Texture Packs", "Please wait a moment for texture packs to finish loading.", false);
                return false;
            }
        }
        #endregion

        #region Debug / Export

        /// <summary>
        /// Optional hotkey-driven texture exporter (guarded by config).
        /// </summary>
        static class DebugExportHotkey
        {
            static KeyboardState    _prevKb;
            static bool             _busy;
            static bool             _initialized;
            static KeyboardListener _exportHK = KeyboardListener.Disabled;

            static void EnsureInit()
            {
                if (_initialized) return;
                _initialized = true;
                try
                {
                    var cfg   = TPConfig.LoadOrCreate();
                    _exportHK = KeyboardListener.Parse(cfg.ExportHotkey);
                    Log($"[Hotkeys] Export = \"{cfg.ExportHotkey}\" => Key={_exportHK.Key}.");
                }
                catch { _exportHK = KeyboardListener.Disabled; }
            }

            [HarmonyPatch(typeof(DNAGame), "Update")]
            static class Hook
            {
                [HarmonyPostfix]
                static void Postfix(GameTime gameTime)
                {
                    try
                    {
                        EnsureInit();

                        var kb    = Keyboard.GetState();
                        bool edge = _exportHK.IsEdge(kb, _prevKb);
                        _prevKb = kb;

                        if (edge && !_busy)
                        {
                            _busy = true;
                            try { TexturePackExtractor.ExportAll(); }
                            finally { _busy = false; }
                        }
                    }
                    catch { /* never break the loop */ }
                }
            }
        }

        #region Screens - Loading Screen

        /// <summary>
        /// LoadScreen texture capture (cloned copy) for export/debug; avoids per-frame cloning.
        /// </summary>
        private static SpriteBatch _sb;
        private static Texture2D _lastRawPtr; // To avoid cloning every frame.

        [HarmonyPatch(typeof(LoadScreen), "OnDraw")]
        static class Patch_LoadScreen_OnDraw_Capture
        {
            [HarmonyPostfix]
            static void Postfix(LoadScreen __instance)
            {
                TryCapture(__instance);
            }
        }

        public static void TryCapture(LoadScreen ls)
        {
            if (ls == null) return;

            // Get either a raw Texture2D (_image) or a Sprite-like (Texture + SourceRectangle).
            var tex = GetLoadTextureOrSprite(ls, out Rectangle region);
            if (tex == null) return;

            // Only clone when the underlying texture pointer changes.
            if (ReferenceEquals(_lastRawPtr, tex)) return;

            var clone = CloneRegionToColorTexture(tex, region);
            if (clone == null) return;

            // Dispose old clone to avoid leaks.
            try { ScreenExporter.LastSeenLoadTexture?.Dispose(); } catch { }

            ScreenExporter.LastSeenLoadTexture = clone;
            _lastRawPtr = tex;
            Log("[Export] Captured LoadScreen texture (cloned).");
        }

        // --- helpers ---

        private static Texture2D GetLoadTextureOrSprite(LoadScreen ls, out Rectangle region)
        {
            region = Rectangle.Empty;

            // 1) private Texture2D _image
            var fi = AccessTools.Field(typeof(LoadScreen), "_image");
            if (fi?.GetValue(ls) is Texture2D t1)
            {
                region = new Rectangle(0, 0, t1.Width, t1.Height);
                return t1;
            }
            return null;
        }

        private static Texture2D CloneRegionToColorTexture(Texture2D src, Rectangle region)
        {
            if (src == null || src.IsDisposed) return null;
            var gd = src.GraphicsDevice;
            if (gd == null) return null;

            RenderTargetBinding[] saved = null;
            try
            {
                saved = gd.GetRenderTargets();
                using (var rt = new RenderTarget2D(gd, region.Width, region.Height, false, SurfaceFormat.Color, DepthFormat.None))
                {
                    if (_sb == null || _sb.GraphicsDevice != gd) _sb = new SpriteBatch(gd);

                    gd.SetRenderTarget(rt);
                    gd.Clear(Color.Transparent);

                    _sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                    _sb.Draw(src, new Rectangle(0, 0, region.Width, region.Height), region, Color.White);
                    _sb.End();

                    gd.SetRenderTarget(null);

                    // Copy pixels into a standalone Texture2D (no mips, SurfaceFormat.Color).
                    var data = new Color[region.Width * region.Height];
                    rt.GetData(data);
                    var copy = new Texture2D(gd, region.Width, region.Height, false, SurfaceFormat.Color);
                    copy.SetData(data);
                    return copy;
                }
            }
            catch { return null; }
            finally
            {
                if (saved != null && saved.Length > 0) gd.SetRenderTargets(saved);
                else gd.SetRenderTarget(null);
            }
        }
        #endregion

        #endregion

        #endregion
    }
}