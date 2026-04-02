/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Reflection.Emit;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using System.Linq;
using HarmonyLib;                       // Harmony patching library.
using DNA.Audio;
using DNA.Input;
using System;
using DNA;

using static ModLoader.LogSystem;       // For Log(...).

namespace TooManyItems
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

        #region Patch Limited Atlas Size Limations

        /// <summary>
        /// Problem:
        ///   The game bakes hard-coded UI atlas/camera constants into InventoryItem.FinishInitialization(...) like:
        ///     1280, 2560,  640, -640  (both int and float forms).
        ///   With TMI exposing hundreds of items, the 1280px height limit clips / mis-centers the grid.
        ///
        /// Fix:
        ///   A Harmony transpiler replaces those constants with calls to TMIIconGrid.* helpers that
        ///   compute values dynamically (and optionally read our INI override).
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // Literal height.
        ///   ldc.i4 1280     ->  call int   TMIIconGrid.RequiredHeight()
        ///   ldc.r4 1280.0f  ->  call float TMIIconGrid.RequiredHeightF()
        ///
        ///   // Double height (camera limits).
        ///   ldc.i4 2560     ->  call int   TMIIconGrid.DoubleHeight()
        ///   ldc.r4 2560.0f  ->  call float TMIIconGrid.DoubleHeightF()
        ///
        ///   // Half and negative half (origin/center).
        ///   ldc.i4 640      ->  call int   TMIIconGrid.HalfHeight()
        ///   ldc.i4.s -640   ->  call int   TMIIconGrid.NegHalfHeight()
        ///   ldc.r4 640.0f   ->  call float TMIIconGrid.HalfHeightF()
        ///   ldc.r4 -640.0f  ->  call float TMIIconGrid.NegHalfHeightF()
        /// </summary>
        [HarmonyPatch(typeof(InventoryItem), "FinishInitialization")]
        static class Patch_FinishInitialization_Transpiler
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
            {
                var reqH_i  = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.RequiredHeight));
                var reqH_f  = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.RequiredHeightF));
                var dblH_i  = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.DoubleHeight));
                var dblH_f  = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.DoubleHeightF));
                var halfH_i = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.HalfHeight));
                var halfH_f = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.HalfHeightF));
                var nHalf_i = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.NegHalfHeight));
                var nHalf_f = AccessTools.Method(typeof(TMIIconGrid), nameof(TMIIconGrid.NegHalfHeightF));

                foreach (var ci in instr)
                {
                    // Heights (int & float).
                    if (ci.opcode  == OpCodes.Ldc_I4 && (int)ci.operand   == 1280)  { yield return new CodeInstruction(OpCodes.Call, reqH_i); continue; }
                    if (ci.opcode  == OpCodes.Ldc_I4 && (int)ci.operand   == 2560)  { yield return new CodeInstruction(OpCodes.Call, dblH_i); continue; }
                    if (ci.opcode  == OpCodes.Ldc_R4 && (float)ci.operand == 1280f) { yield return new CodeInstruction(OpCodes.Call, reqH_f); continue; }
                    if (ci.opcode  == OpCodes.Ldc_R4 && (float)ci.operand == 2560f) { yield return new CodeInstruction(OpCodes.Call, dblH_f); continue; }

                    // Half-height origin (int & float).
                    if (ci.opcode  == OpCodes.Ldc_I4 && (int)ci.operand   == 640)   { yield return new CodeInstruction(OpCodes.Call, halfH_i); continue; }
                    if ((ci.opcode == OpCodes.Ldc_I4 || ci.opcode         == OpCodes.Ldc_I4_S) && Convert.ToInt32(ci.operand) == -640)
                                                                                    { yield return new CodeInstruction(OpCodes.Call, nHalf_i); continue; }

                    if (ci.opcode  == OpCodes.Ldc_R4 && (float)ci.operand == 640f)  { yield return new CodeInstruction(OpCodes.Call, halfH_f); continue; }
                    if (ci.opcode  == OpCodes.Ldc_R4 && (float)ci.operand == -640f) { yield return new CodeInstruction(OpCodes.Call, nHalf_f); continue; }

                    yield return ci;
                }
            }
        }
        #endregion

        #region Build & Inject Item Atlas Early

        /// <summary>
        /// Problem:
        ///   Registering/building the atlas too early (e.g., at mod startup) can race with content/device init,
        ///   leading to invalid textures or null device.
        ///
        /// Fix:
        ///   After the first time the ScreenGroup draws (device is live, backbuffer bound),
        ///   register once and only once.
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // No reliable place to register safely.
        ///   (none)  ->  Postfix(ScreenGroup.Draw) { TMIItemInjector.Register() once; }
        /// </summary>
        [HarmonyPatch]
        internal static class TMIRegisterPatch
        {
            // Hook: public override void ScreenGroup.Draw(GraphicsDevice, SpriteBatch, GameTime).
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(ScreenGroup), "Draw",
                    new Type[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) });
            }

            // Run AFTER all screens have drawn -> gui will be truly top-most.
            static void Postfix(ScreenGroup __instance, GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
            {
                if (!TMIItemInjector.IsRegistered)
                {
                    // Build atlas & register icons/items.
                    TMIItemInjector.Register();
                }
            }
        }
        #endregion

        #region Draw GUI In Crafting Menu

        /// <summary>
        /// Problem:
        ///   Drawing the overlay without controlling GPU state can cause:
        ///     • Scissor clipping by prior UI,
        ///     • Depth test/writes hiding parts of overlay,
        ///     • Drawing into an offscreen RT accidentally.
        ///
        /// Fix:
        ///   Postfix CraftingScreen.Draw, bind backbuffer, disable depth & stencil & scissor,
        ///   use PointClamp sampler, and render overlay as truly top-most.
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // Implicit state from game UI.
        ///   spriteBatch.Begin(...defaults...) ->  device.SetRenderTarget(null);
        ///                                         spriteBatch.Begin(SpriteSortMode.Immediate,
        ///                                                           BlendState.AlphaBlend,
        ///                                                           SamplerState.PointClamp,
        ///                                                           NoDepthNoStencil,
        ///                                                           NoScissorCullNone);
        ///                                         TMIOverlay.Draw(...); TMIOverlay.DrawTopMost(...);
        ///                                         spriteBatch.End();
        /// </summary>
        static readonly DepthStencilState NoDepthNoStencil  = new DepthStencilState() { DepthBufferEnable = false, DepthBufferWriteEnable = false, StencilEnable = false };
        static readonly RasterizerState   NoScissorCullNone = new RasterizerState()   { CullMode = CullMode.None, ScissorTestEnable = false };

        // Patch the game's CraftingScreen "Draw" so we can render gui in the crafting screen only.
        [HarmonyPatch]
        internal static class TMIHudPatch
        {
            // Hook: public override void CraftingScreen.Draw(GraphicsDevice, SpriteBatch, GameTime)
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(CraftingScreen), "Draw",
                    new Type[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) });
            }

            // Run AFTER all screens have drawn -> gui will be truly top-most.
            static void Postfix(CraftingScreen __instance, GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
            {
                try
                {
                    // Remember which crafting screen is drawing so overlay code can query sizes/slots if needed.
                    TMIOverlayActions.SetActiveCraftingScreen(__instance);

                    // Ensure we render to the backbuffer, not an offscreen UI RT (render target).
                    device.SetRenderTarget(null);

                    // Eliminate depth/stencil/scissor artifacts and use point sampling for crisp icons.
                    spriteBatch.Begin(SpriteSortMode.Deferred,  // Render in the order we submit. NOTE: Use Deferred, NOT Immediate.
                                      BlendState.AlphaBlend,
                                      SamplerState.PointClamp,
                                      NoDepthNoStencil,         // No depth, no stencil.
                                      NoScissorCullNone);       // Disable scissor tests.


                    TMIOverlay.Draw(sb: spriteBatch, inventoryIsOpen: true, gt: gameTime);

                    // Draw any floating tips LAST so they appear above the panels.
                    TMIOverlay.DrawTopMost(spriteBatch, gameTime);

                    spriteBatch.End();
                }
                catch { /* Never let HUD drawing crash. */ }
            }
        }
        #endregion

        #region Screen Lifetime Hooks

        /// <summary>
        /// Problem:
        ///   TMIOverlayActions tracks the current CraftingScreen via
        ///   SetActiveCraftingScreen, but the reference was never cleared when
        ///   the screen closed. After exiting the inventory, GetOpenCraftingScreen()
        ///   could still report an "open" crafting UI, so DeleteMode and other
        ///   input guards behaved as if crafting were visible even when it wasn't.
        ///
        /// Fix:
        ///   Hook Screen.PopMe in a prefix and, when the popped screen is a
        ///   CraftingScreen, call TMIOverlayActions.SetActiveCraftingScreen(null)
        ///   to mark crafting as closed. This ties our "crafting open" flag to
        ///   the actual ScreenManager stack instead of object lifetime / GC.
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // Before:
        ///   CraftingScreen.PopMe()  ->  Screen stack no longer shows crafting,
        ///                                but GetOpenCraftingScreen() may still
        ///                                return non-null (stale reference).
        ///
        ///   // After:
        ///   CraftingScreen.PopMe()  ->  PopMe prefix clears TMIOverlayActions,
        ///                                so GetOpenCraftingScreen() reliably
        ///                                returns null once inventory is closed.
        /// </summary>
        [HarmonyPatch(typeof(Screen), "PopMe")]
        internal static class Patch_Screen_PopMe_TMIClearCraft
        {
            static void Prefix(Screen __instance)
            {
                if (__instance is CraftingScreen)
                {
                    // Mark crafting as closed.
                    TMIOverlayActions.SetActiveCraftingScreen(null);
                }
            }
        }
        #endregion

        #region Disable Item Pickup / Throw (Delete Mode / Over GUI)

        /// <summary>
        /// Problems fixed:
        ///   1) "Click-through" into world/inventory when the mouse is over the TMI overlay.
        ///   2) While delete-mode is on, inventory/world clicks should not pick up/place blocks.
        ///   3) When dropping items into the favorites tab, items should not be "thrown".
        ///   4) Scrollwheel stacking (ItemCountUp/Down) should not affect inventory slots "through" the overlay.
        ///   5) Scrollwheel hotbar switching (NextItem/PreviousItem) should not fire "through" the overlay.
        ///
        /// Fix:
        ///   - A small helper computes when to block world actions, inventory clicks, inventory wheel stacking,
        ///     and hotbar wheel switching.
        ///   - Two prefixes (CraftingScreen.OnPlayerInput and InGameHUD.OnPlayerInput) swallow input for that frame.
        ///   - Wheel guards are only active while the crafting screen is open (avoid impacting normal gameplay wheel use).
        ///   - A targeted CraftingScreen guard also blocks the vanilla "left-click outside while holding -> drop"
        ///     when the cursor is over the TMI favorites/right panel.
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // CraftingScreen.OnPlayerInput (slot click).
        ///   if (mouse.LeftPressed over slot) { pick/split }                ->  if (PointerOverUI || DeleteMode) { __result=true; return false; }
        ///
        ///   // CraftingScreen.OnPlayerInput (wheel stack).
        ///   if (Mouse.DeltaWheel +/- && HitTest>=0) { ItemCountUp/Down }   ->  if (PointerOverUI || DeleteMode) { __result=true; return false; }
        ///
        ///   // InGameHUD.OnPlayerInput (hotbar wheel).
        ///   if (NextItem/PrevoiusItem.Pressed) { SelectedInventoryIndex }  ->  if (PointerOverUI || DeleteMode) { __result=true; return false; }
        ///
        ///   // InGameHUD.OnPlayerInput (world).
        ///   controllerMapping.Use/Activate may fire                        ->  if (PointerOverUI || DeleteMode) { __result=true; return false; }
        /// </summary>
        internal static class TMIInputGuards
        {
            // Quick accessors.
            internal static bool DeleteMode    => TMIOverlayActions._deleteOn;
            internal static bool OverlayOn     => ConfigGlobals.Enabled;
            internal static bool PointerOverUI => OverlayOn && TMIOverlay.PointerOverUI;

            // Reflection cache for CraftingScreen private fields.
            static readonly FieldInfo FI_BG       = AccessTools.Field(typeof(CraftingScreen), "_backgroundRectangle");
            static readonly FieldInfo FI_Holding  = AccessTools.Field(typeof(CraftingScreen), "_holdingItem");

            // Reflection cache for CraftingScreen.HitTest(Point).
            static readonly MethodInfo MI_HitTest = AccessTools.Method(typeof(CraftingScreen), "HitTest", new[] { typeof(Point) });

            static int HitTest(CraftingScreen scr, Point p) =>
                (int)(MI_HitTest?.Invoke(scr, new object[] { p }) ?? -1);

            // Block world placement/mining/activate when pointer is over the TMI UI or delete-mode is on.
            internal static bool ShouldBlockWorldActions(InputManager input, CastleMinerZControllerMapping map)
            {
                bool pointerOverUI = PointerOverUI;
                bool deleteMode    = DeleteMode;

                // Only let DeleteMode block world input while crafting is actually open.
                if (deleteMode && TMIOverlayActions.GetOpenCraftingScreen() == null) deleteMode = false;
                if (!(pointerOverUI || deleteMode)) return false;

                bool mouseClick =
                    (input?.Mouse?.LeftButtonPressed  == true) ||
                    (input?.Mouse?.RightButtonPressed == true);

                bool useOrActivate =
                    (map?.Use.Pressed      ?? false) || (map?.Use.Held      ?? false) ||
                    (map?.Activate.Pressed ?? false) || (map?.Activate.Held ?? false);

                return mouseClick || useOrActivate;
            }

            // Inventory cell pick-ups/splits should be blocked under the same conditions,
            // but only when actually over a valid inventory/crafting slot.
            internal static bool ShouldBlockInventoryClick(CraftingScreen screen, InputManager input)
            {
                if (!(PointerOverUI || DeleteMode)) return false;

                bool anyClick =
                    (input?.Mouse?.LeftButtonPressed  == true) ||
                    (input?.Mouse?.RightButtonPressed == true);

                if (!anyClick) return false;

                // Only swallow if we're actually over an inventory/crafting slot.
                int hit = HitTest(screen, input?.Mouse?.Position ?? Point.Zero);
                return hit >= 0;
            }

            // Inventory wheel stacking (ItemCountUp/Down) should be blocked under the same conditions,
            // but only when actually over a valid inventory/crafting slot.
            internal static bool ShouldBlockInventoryWheel(CraftingScreen screen, InputManager input)
            {
                if (TMIOverlayActions.GetOpenCraftingScreen() == null || !(PointerOverUI || DeleteMode)) return false;

                int dw = (input?.Mouse?.DeltaWheel ?? 0);
                if (dw == 0) return false;

                // Only swallow if we're actually over an inventory/crafting slot.
                int hit = HitTest(screen, input.Mouse.Position);
                return hit >= 0;
            }

            // Hotbar scroll-wheel (NextItem/PreviousItem) should not fire "through" the TMI overlay.
            // We gate it on PointerOverUI so normal wheel switching still works elsewhere.
            internal static bool ShouldBlockHotbarWheelSwitch(InputManager input)
            {
                if (TMIOverlayActions.GetOpenCraftingScreen() == null || !(PointerOverUI || DeleteMode)) return false;

                int dw = (input?.Mouse?.DeltaWheel ?? 0);
                return dw != 0;
            }

            /// <summary>
            /// Returns true only when all of the following are true:
            /// • Cursor is over the TMI right items panel ('PointerOverRightPanel').
            /// • Left mouse was pressed this frame.
            /// • An item is currently held ('_holdingItem != null').
            /// • The click is outside the crafting background rectangle
            ///   (i.e., the vanilla "left-click outside while holding -> drop" branch would run).
            /// This does not affect right-click drops or the Q-to-drop path.
            /// </summary>
            internal static bool ShouldBlockItemDrop(CraftingScreen screen, InputManager input)
            {
                if (!TMIOverlay.PointerOverRightPanel)       return false;
                if (input?.Mouse?.LeftButtonPressed != true) return false;

                var holding = (InventoryItem)FI_Holding?.GetValue(screen);
                if (holding == null)                         return false;

                var bg      = (Rectangle)(FI_BG?.GetValue(screen) ?? Rectangle.Empty);
                var pos     = input.Mouse.Position;

                return !bg.Contains(pos);
            }
        }

        // Inventory: Swallow slot pick/split when over TMI or in delete-mode.
        [HarmonyPatch(typeof(CraftingScreen), "OnPlayerInput",
            new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        [HarmonyPriority(Priority.High)]
        internal static class Patch_CraftingScreen_BlockClickThroughTMI
        {
            static bool Prefix(CraftingScreen __instance,
                               InputManager inputManager,
                               GameController controller,
                               KeyboardInput chatPad,
                               GameTime gameTime,
                               ref bool __result)
            {
                if (TMIInputGuards.ShouldBlockInventoryClick(__instance, inputManager))
                {
                    __result = true; // Handled by TMI.
                    return false;    // Skip vanilla slot pick-up/split/merge.
                }
                return true;
            }
        }

        // World: Swallow interaction while cursor is over TMI or delete-mode is on.
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput",
            new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        [HarmonyPriority(Priority.High)]
        internal static class Patch_InGameHUD_BlockClickThroughTMI
        {
            static bool Prefix(InputManager inputManager,
                               GameController controller,
                               KeyboardInput chatPad,
                               GameTime gameTime,
                               ref bool __result)
            {
                var map = CastleMinerZGame.Instance?._controllerMapping;
                if (TMIInputGuards.ShouldBlockWorldActions(inputManager, map) ||
                    TMIInputGuards.ShouldBlockHotbarWheelSwitch(inputManager))
                {
                    __result = true; // Handled by TMI.
                    return false;    // Skip HUD/world handling this frame.
                }
                return true;
            }
        }

        // Player: Swallow item throwing when over TMI favorites overlay.
        [HarmonyPatch(typeof(CraftingScreen), "OnPlayerInput",
            new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        [HarmonyPriority(Priority.High)]
        internal static class Patch_CraftingScreen_BlockLeftDropOutsideWhenOverTMI
        {
            static bool Prefix(CraftingScreen __instance,
                               InputManager inputManager,
                               GameController controller,
                               KeyboardInput chatPad,
                               GameTime gameTime,
                               ref bool __result)
            {
                if (TMIInputGuards.ShouldBlockInventoryClick(__instance, inputManager) ||
                    TMIInputGuards.ShouldBlockInventoryWheel(__instance, inputManager) ||
                    TMIInputGuards.ShouldBlockItemDrop(__instance, inputManager))
                {
                    __result = true; // Handled by TMI.
                    return false;    // Skip vanilla (prevents drop while over overlay).
                }
                return true;
            }
        }
        #endregion

        #region Configure On Join

        /// <summary>
        /// Problems fixed:
        ///   - Delete-mode could persist across sessions/worlds, causing accidental deletion.
        ///   - Overlay mode/difficulty could be stale after joining a session.
        ///
        /// Fix:
        ///   Postfix OnGamerJoined: When the local player is identified, clear delete-mode
        ///   and push session's mode/difficulty into the overlay state.
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // No reset on join.
        ///   (none)  ->  TMIOverlayActions.SetDelete(false);
        ///              TMIOverlayActions.SetSelectedMode(gameMode);
        ///              TMIOverlayActions.SetSelectedDifficulty(difficulty);
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_OnGamerJoined_LogHello
        {
            // Target: protected override void CastleMinerZGame.OnGamerJoined(NetworkGamer gamer)
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(CastleMinerZGame), "OnGamerJoined", new[] { typeof(NetworkGamer) });

            // Run AFTER the original method.
            static void Postfix(CastleMinerZGame __instance, NetworkGamer gamer)
            {
                // Ensure the reported gamer is always us.
                if (gamer != CastleMinerZGame.Instance?.MyNetworkGamer) return;

                // Get the current game instance.
                var gameInstance   = CastleMinerZGame.Instance;
                var netInstance    = gameInstance?.CurrentNetworkSession;

                // Grab the session properties directly from the session.
                #pragma warning disable IDE0059 // Suppress unnecessary assignment of a value.
                var version              = (int)netInstance.SessionProperties[0].Value;
                var gameBegun            = (bool)(netInstance.SessionProperties[1].Value == 1);
                var gameMode             = (GameModeTypes)netInstance.SessionProperties[2].Value;
                var difficulty           = (GameDifficultyTypes)netInstance.SessionProperties[3].Value;
                var infiniteResourceMode = (bool)(netInstance.SessionProperties[4].Value == 1);
                var pvp                  = (CastleMinerZGame.PVPEnum)netInstance.SessionProperties[5].Value;
                // var unused_1          = netInstance.SessionProperties[6].Value;
                // var unused_2          = netInstance.SessionProperties[7].Value;
                #pragma warning restore IDE0059

                // Toggle off delete mode.
                TMIOverlayActions.SetDelete(false);

                // Set the current mode.
                TMIOverlayActions.SetSelectedMode(gameMode);

                // Set the current difficulty.
                TMIOverlayActions.SetSelectedDifficulty(difficulty);
            }
        }
        #endregion

        #region Dont Close GUI While In Searchbox

        /// <summary>
        /// Problem:
        ///   The BlockUI toggle (e.g., Escape/E) could close/open the inventory while the
        ///   user is typing in the TMI search box - very frustrating.
        ///
        /// Fix:
        ///   When SearchFocused is true, swallow the BlockUI.Pressed edge in both CraftingScreen
        ///   and InGameHUD input paths.
        ///
        /// Summary (BEFORE -> AFTER):
        ///   // CraftingScreen.OnPlayerInput.
        ///   if (BlockUI.Pressed) CloseMenu();       ->  if (SearchFocused && BlockUI.Pressed) { __result = true; return false; }
        ///
        ///   // InGameHUD.OnPlayerInput.
        ///   if (BlockUI.Pressed) ToggleInventory()  ->  if (SearchFocused && BlockUI.Pressed) { __result = true; return false; }
        /// </summary>

        // Crafting screen: Don't let the toggle close the UI when the search box is active.
        [HarmonyPatch(typeof(CraftingScreen), "OnPlayerInput",
            new Type[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        [HarmonyPriority(Priority.High)]
        internal static class Patch_CraftingScreen_SuppressToggleWhileSearch
        {
            static bool Prefix(InputManager inputManager,
                               GameController controller,
                               KeyboardInput chatPad,
                               GameTime gameTime,
                               ref bool __result)
            {
                if (!TMIOverlay.SearchFocused) return true;

                var map = CastleMinerZGame.Instance?._controllerMapping;
                if (map != null && map.BlockUI.Pressed)
                {
                    __result = true; // Handled.
                    return false;    // Skip the original (prevents close).
                }
                return true;
            }
        }

        // In-game HUD: Swallow the toggle edge while typing.
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput",
            new Type[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        [HarmonyPriority(Priority.High)]
        internal static class Patch_InGameHUD_SuppressToggleWhileSearch
        {
            static bool Prefix(InputManager inputManager,
                               GameController controller,
                               KeyboardInput chatPad,
                               GameTime gameTime,
                               ref bool __result)
            {
                if (!TMIOverlay.SearchFocused) return true;

                var map = CastleMinerZGame.Instance?._controllerMapping;
                if (map != null && map.BlockUI.Pressed)
                {
                    __result = true; // Handled.
                    return false;    // Skip original HUD input this frame.
                }
                return true;
            }
        }
        #endregion

        #region Hard Block Mining Tweaks (Tool Tier / Breakability)

        /// <summary>
        /// Problem:
        /// - TMI hard-block settings (allow mining bedrock / very-hard blocks, tool tier)
        ///   were only stored in config and UI, but never actually applied to the live
        ///   BlockType table after DNAGame finished loading content.
        ///
        /// Fix:
        /// - Postfix DNAGame.SecondaryLoad and call TMIOverlayActions.ApplyHardBlockSettings()
        ///   once content is ready, so the current config is pushed into BlockType instances.
        ///
        /// Summary (BEFORE -> AFTER):
        /// - BEFORE: TMI checkbox / slider appeared to work, but bedrock / specials could
        ///           remain unmineable until something else applied the settings manually.
        /// - AFTER:  As soon as DNAGame.SecondaryLoad completes, the persisted TMI hard-block
        ///           settings are applied automatically to all tracked BlockTypes.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        static class Patch_SecondaryLoad_MakeSpecialsBreakable
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                // Apply persisted settings at content-load time.
                TMIOverlayActions.ApplyHardBlockSettings();
            }
        }

        /// <summary>
        /// Problem:
        /// - Even when "Allow mining very-hard blocks" was enabled, bedrock / hardness-5
        ///   blocks (and special cases) still used their vanilla block tier, so pickaxes
        ///   could not mine them unless hard-coded per-block.
        ///
        /// Fix:
        /// - Postfix PickInventoryItem.GetPickaxeBlockTier and, for any block in the
        ///   "very hard" group, override the block tier with the user-configured tool
        ///   tier (0-12) from TMIState, but only when the feature is enabled.
        ///
        /// Summary (BEFORE -> AFTER):
        /// - BEFORE: Mining restrictions for bedrock / specials were fixed by the base
        ///           game and could not be tuned from TMI.
        /// - AFTER:  Any block flagged as "very hard" can be made mineable by setting a
        ///           required tool tier in the TMI Gameplay Settings panel.
        /// </summary>
        [HarmonyPatch(typeof(PickInventoryItem), "GetPickaxeBlockTier")]
        static class Patch_PickaxeBlockTier_MakeSpecialsMineable
        {
            static void Postfix(BlockTypeEnum blockType, ref int __result)
            {
                // Feature disabled? leave vanilla behavior alone.
                if (!TMIState.GetHardBlocksEnabled(defaultIfMissing: false))
                    return;

                // Only adjust if this block belonged to the "very hard" (Hardness>=5) group at startup.
                if (!TMIOverlayActions.IsVeryHardBlock(blockType))
                    return;

                // Use configured tool-tier requirement (0-12).
                int tier = TMIState.GetHardBlockTier(defaultIfMissing: 12);
                if (tier < 0)  tier = 0;
                if (tier > 12) tier = 12;

                __result = tier;
            }
        }
        #endregion

        #region Torch Drop Mode

        /// <summary>
        /// Problem:
        /// - TMI injects a synthetic block-backed item for torch blocks so torches can
        ///   exist in the item atlas like other placeable blocks.
        /// - As a side effect, BlockInventoryItemClass.CreateBlockItem(...) may return
        ///   the synthetic "Torch" block item when a torch is mined or popped, instead
        ///   of the vanilla torch inventory item.
        ///
        /// Fix:
        /// - Prefix BlockInventoryItemClass.CreateBlockItem(...).
        /// - For torch-family block types:
        ///   • If TorchesDropBlock is false, force the vanilla torch item drop.
        ///   • If TorchesDropBlock is true, return the synthetic torch block item.
        /// - For all non-torch block types, let vanilla run unchanged.
        ///
        /// Summary (BEFORE -> AFTER):
        /// - BEFORE: Torches always dropped the synthetic block-backed "Torch" item.
        /// - AFTER:  Torches drop the vanilla torch item by default, and only drop the
        ///           synthetic block-backed torch when TorchesDropBlock is enabled.
        /// </summary>
        [HarmonyPatch(typeof(BlockInventoryItemClass), nameof(BlockInventoryItemClass.CreateBlockItem))]
        internal static class Patch_BlockInventoryItemClass_CreateBlockItem_TorchDropMode
        {
            /// <summary>
            /// Intercepts torch block-item creation so torch drops can switch between:
            /// - vanilla torch item    (default), or
            /// - synthetic torch block (config-enabled).
            /// </summary>
            static bool Prefix(BlockTypeEnum blockType, int stackCount, IntVector3 location, ref InventoryItem __result)
            {
                var bt = BlockType.GetType(blockType);
                if (bt == null || bt.ParentBlockType != BlockTypeEnum.Torch)
                    return true;

                if (!ConfigGlobals.TorchesDropBlock)
                {
                    __result = InventoryItem.CreateItem(InventoryItemIDs.Torch, stackCount);
                    return false;
                }

                if (TMIItemInjector.TryGetSyntheticId(BlockTypeEnum.Torch, out var syntheticTorchId))
                {
                    __result = InventoryItem.CreateItem(syntheticTorchId, stackCount);
                    return false;
                }

                return true;
            }
        }

        #endregion

        #region Synthetic Pickup Network Filters (Local-Only Drops / Consumes)

        #region CreatePickupMessage - Local-Only Synthetic Drops

        /// <summary>
        /// Problem:
        /// - Synthetic (TMI-injected) InventoryItemIDs could be sent via
        ///   CreatePickupMessage.Send, exposing unknown IDs to the host / other clients
        ///   and risking crashes or undefined behavior.
        ///
        /// Fix:
        /// - Prefix CreatePickupMessage.Send and:
        ///   • Allow vanilla IDs to flow as normal.
        ///   • For synthetic IDs, spawn a local PickupEntity only and skip the original
        ///     Send, ensuring no CreatePickupMessage is broadcast with custom IDs.
        ///
        /// Summary (BEFORE -> AFTER):
        /// - BEFORE: Breaking blocks with synthetic drops could broadcast unknown item
        ///           IDs across the network.
        /// - AFTER:  Synthetic drops are spawned purely client-side; other players and
        ///           the host never see those CreatePickupMessage packets.
        /// </summary>
        [HarmonyPatch(typeof(CreatePickupMessage), nameof(CreatePickupMessage.Send))]
        internal static class Patch_CreatePickupMessage_LocalOnlyForSynthetic
        {
            /// <summary>
            /// Prefix:
            /// - If vanilla ID   -> Let the original Send run (broadcast to everyone).
            /// - If synthetic ID -> Spawn a local pickup and skip the network send.
            /// </summary>
            static bool Prefix(
                LocalNetworkGamer from,
                Vector3 pos,
                Vector3 vec,
                int pickupID,
                InventoryItem item,
                bool dropped,
                bool displayOnPickup)
            {
                var cls = item?.ItemClass;
                if (cls == null)
                {
                    // Nothing we can do, let vanilla handle it.
                    return true;
                }

                InventoryItemIDs id = cls.ID;

                // Only intercept our synthetic / out-of-range IDs.
                if (!TMIItemInjector.IsSynthetic(id))
                {
                    // Vanilla/legit item - go through the normal network path.
                    return true;
                }

                // Synthetic item -> local-only spawn.
                TrySpawnLocalPickup(from, pos, vec, pickupID, item, dropped, displayOnPickup);

                // Skip original CreatePickupMessage.Send, so nothing is sent over the wire.
                return false;
            }

            /// <summary>
            /// Replicates PickupManager.HandleCreatePickupMessage but without going
            /// through the message system, so this stays entirely client-side.
            /// </summary>
            private static void TrySpawnLocalPickup(
                LocalNetworkGamer from,
                Vector3 pos,
                Vector3 vec,
                int pickupID,
                InventoryItem item,
                bool dropped,
                bool displayOnPickup)
            {
                var pm = PickupManager.Instance;
                if (pm == null)
                    return;

                try
                {
                    // Vanilla uses msg.Sender.Id as the SpawnerID.
                    int spawnerID = (int)from.Id;

                    var entity = new PickupEntity(item, pickupID, spawnerID, dropped, pos)
                    {
                        // Match HandleCreatePickupMessage behaviour:
                        // DisplayOnPickup, velocity, and a +0.5f offset.
                        Item = { DisplayOnPickup = displayOnPickup }
                    };

                    entity.PlayerPhysics.LocalVelocity = vec;
                    entity.LocalPosition = pos + new Vector3(0.5f, 0.5f, 0.5f);

                    pm.Pickups.Add(entity);

                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        scene.Children.Add(entity);
                    }

                    if (TMILog.GetLoggingMode() != TMILog.LoggingType.None)
                    {
                        // Log($"[TMI] Spawned local-only pickup for synthetic item {item.ItemClass.ID} at {pos}.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[TMI] ERROR: Local-only pickup spawn failed: {ex}.");
                }
            }
        }
        #endregion

        #region ConsumePickupMessage - Host-Local Synthetic Consumes

        /// <summary>
        /// Problem:
        /// - When a synthetic pickup was consumed via ConsumePickupMessage.Send, the
        ///   host would broadcast a packet containing a custom InventoryItemID, causing
        ///   unmodded / remote clients to crash on deserialize.
        ///
        /// Fix:
        /// - Prefix ConsumePickupMessage.Send and:
        ///   • Let vanilla IDs send normally.
        ///   • For synthetic IDs, perform a host-local consume:
        ///     - Remove the pickup from the host world.
        ///     - Grant the item to the picking player (if local).
        ///     - Optionally spawn the flying pickup effect.
        ///     - Skip sending the original message so remote clients never see it.
        ///
        /// Summary (BEFORE -> AFTER):
        /// - BEFORE: Picking up a synthetic drop on the host could crash other players
        ///           when the consume message hit their client.
        /// - AFTER:  Synthetic consumes are resolved entirely on the host; no network
        ///           traffic is generated for those items, and other players stay stable.
        /// </summary>
        [HarmonyPatch(typeof(ConsumePickupMessage), nameof(ConsumePickupMessage.Send))]
        internal static class Patch_ConsumePickupMessage_Send_TMI
        {
            /// <summary>
            /// Intercept ConsumePickupMessage.Send:
            /// - Vanilla IDs -> send as normal (return true).
            /// - Synthetic IDs -> perform a host-local consume and skip the send.
            /// </summary>
            static bool Prefix(
                LocalNetworkGamer from,
                byte pickerupper,
                Vector3 pos,
                int spawnerID,
                int pickupID,
                InventoryItem item,
                bool displayOnPickup)
            {
                // No item / no class -> nothing special to do.
                if (item == null || item.ItemClass == null)
                    return true;

                var id = item.ItemClass.ID;

                // Not a synthetic ID? Let the vanilla message flow.
                if (!TMIItemInjector.IsSynthetic(id))
                    return true;

                // Synthetic item: Handle locally and block the network send.
                var pm = PickupManager.Instance;
                var game = CastleMinerZGame.Instance;
                if (pm == null || game == null)
                    return false; // Fail-safe: Don't send anything.

                // -----------------------------------------------------------------
                // 1) Find which Player picked this up (from pickerupper id).
                // -----------------------------------------------------------------
                Player player = null;
                var session = game.CurrentNetworkSession;
                if (session != null)
                {
                    foreach (NetworkGamer nwg in session.AllGamers)
                    {
                        if (nwg != null && nwg.Id == pickerupper)
                        {
                            player = nwg.Tag as Player;
                            break;
                        }
                    }
                }

                // -----------------------------------------------------------------
                // 2) Remove the pickup entity from the world on this host.
                // -----------------------------------------------------------------
                for (int i = 0; i < pm.Pickups.Count; i++)
                {
                    var candidate = pm.Pickups[i];
                    if (candidate.PickupID == pickupID && candidate.SpawnerID == spawnerID)
                    {
                        pm.Pickups.RemoveAt(i);
                        pm.PendingPickupList.Remove(candidate);

                        var scene = pm.Scene;
                        if (scene != null && scene.Children != null)
                        {
                            scene.Children.Remove(candidate);
                        }
                        break;
                    }
                }

                // -----------------------------------------------------------------
                // 3) If this machine owns the picking player, grant the item.
                // -----------------------------------------------------------------
                if (player != null && player == game.LocalPlayer &&
                    game.GameScreen != null && game.GameScreen.HUD != null)
                {
                    game.GameScreen.HUD.PlayerInventory.AddInventoryItem(item, displayOnPickup);
                    SoundManager.Instance.PlayInstance("pickupitem");
                }

                // -----------------------------------------------------------------
                // 4) Optional: Local flying pickup effect (purely cosmetic).
                // -----------------------------------------------------------------
                if (player != null)
                {
                    Vector3 spawnPos = pos; // Fallback to msg-pos.
                                            // (If you want, you could use pickupEntity position instead.)

                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        var fpe = new FlyingPickupEntity(item, player, spawnPos);
                        scene.Children.Add(fpe);
                    }
                }

                // Returning false = do NOT call the original Send -> no message
                // is broadcast, so other (vanilla) clients never see the custom ID.
                return false;
            }
        }
        #endregion

        #region RequestPickupMessage - Client-Local Synthetic Consumes

        /// <summary>
        /// Problem:
        /// - On non-host clients, synthetic pickups are spawned locally only.
        ///   When the player walks into one, RequestPickupMessage.Send would still
        ///   notify the (unaware) host, which has no matching pickup, so the item
        ///   could never be granted and the local drop became uncollectable.
        ///
        /// Fix:
        /// - Prefix RequestPickupMessage.Send and:
        ///   • If we're the host, let vanilla behavior proceed (host-side is already
        ///     guarded by the consume patch).
        ///   • If we're a client:
        ///       - Find the local pickup.
        ///       - If vanilla -> send request as normal.
        ///       - If synthetic -> grant the item locally, remove the pickup, and
        ///         skip the network send.
        ///
        /// Summary (BEFORE -> AFTER):
        /// - BEFORE: In someone else's game, synthetic drops were visible but could
        ///           not be picked up, since the host never knew about them.
        /// - AFTER:  Non-host clients pick up synthetic drops entirely client-side
        ///           without sending RequestPickupMessage to the host at all.
        /// </summary>
        [HarmonyPatch(typeof(RequestPickupMessage), nameof(RequestPickupMessage.Send))]
        internal static class Patch_RequestPickupMessage_Send_TMI
        {
            /// <summary>
            /// Prefix:
            /// - If we're the host -> do nothing, let vanilla networking + the
            ///   ConsumePickupMessage patch handle things.
            /// - If we're a client:
            ///   • Find the pickup locally.
            ///   • If it's vanilla   -> Send request as normal.
            ///   • If it's synthetic -> Grant it locally and skip the send.
            /// </summary>
            static bool Prefix(LocalNetworkGamer from, int spawnerID, int pickupID)
            {
                // No gamer? Just fall back to vanilla.
                if (from == null)
                    return true;

                // Host should keep the normal request/consume flow; host-side
                // we already patch ConsumePickupMessage.Send for synthetic items.
                if (from.IsHost)
                    return true;

                var pm = PickupManager.Instance;
                var game = CastleMinerZGame.Instance;
                if (pm == null || game == null)
                    return true;

                // Locate the pickup we just walked into.
                PickupEntity pickup = null;
                for (int i = 0; i < pm.Pickups.Count; i++)
                {
                    var cand = pm.Pickups[i];
                    if (cand.PickupID == pickupID && cand.SpawnerID == spawnerID)
                    {
                        pickup = cand;
                        break;
                    }
                }

                // If we can't find it, or it has no item, let vanilla try.
                if (pickup == null || pickup.Item == null || pickup.Item.ItemClass == null)
                    return true;

                var id = pickup.Item.ItemClass.ID;

                // Vanilla item? Use normal network pickup logic.
                if (!TMIItemInjector.IsSynthetic(id))
                    return true;

                // -----------------------------------------------------------------
                // Synthetic pickup on a NON-HOST client:
                //   - Grant item locally.
                //   - Remove the pickup entity.
                //   - (Optionally) play the flying pickup effect.
                //   - Do NOT send a RequestPickupMessage to the host.
                // -----------------------------------------------------------------
                Player localPlayer = game.LocalPlayer;
                if (localPlayer != null && game.GameScreen != null && game.GameScreen.HUD != null)
                {
                    // Add to local inventory, same as HandleConsumePickupMessage.
                    game.GameScreen.HUD.PlayerInventory.AddInventoryItem(
                        pickup.Item,
                        pickup.Item.DisplayOnPickup
                    );

                    SoundManager.Instance.PlayInstance("pickupitem");

                    // Cosmetic flying pickup effect for the local player.
                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        Vector3 pos = pickup.GetActualGraphicPos();
                        var fpe = new FlyingPickupEntity(pickup.Item, localPlayer, pos);
                        scene.Children.Add(fpe);
                    }
                }

                // Remove the pickup from the local world.
                pm.RemovePickup(pickup);

                // Returning false means we skip the original RequestPickupMessage.Send,
                // so the host never sees this synthetic pickup at all.
                return false;
            }
        }
        #endregion

        #endregion

        #endregion
    }
}