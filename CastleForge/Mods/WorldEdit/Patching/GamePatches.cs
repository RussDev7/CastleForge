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
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;                       // Harmony patching library.
using DNA.Input;
using System;
using DNA;

using static ModLoader.LogSystem;       // For Log(...).
using static WorldEdit.WorldEdit;

namespace WorldEdit
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

        #region Per-Frame Pumps

        [HarmonyPatch(typeof(DNAGame), "Update")]
        static class Patch_DNAGame_Update
        {
            static void Postfix(GameTime __0)
            {
                // 1) Resume any queued "await next frame" continuations (always safe / very cheap).
                AsyncFrameYield.Pump();

                // 2) Drain block edits (gated).
                if (AsyncBlockPlacer_Settings.Enabled)
                    AsyncBlockPlacer.Pump();
            }
        }
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
        internal static class WEHotkeys
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
                if (!WEHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    WEConfig.LoadApply();

                    SendFeedback($"[WEdit] Config hot-reloaded from \"{PathShortener.ShortenForLog(WEConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[WEdit] Hot-reload failed: {ex.Message}.");
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

        #region QoL Patches

        // Quality of Life Tweaks //

        #region Increase Placement Range

        // Removed: Moved to QoL mod.
        /*
        /// <summary>
        /// Replaces the hard-coded "5f" placement range in
        /// InGameHUD.DoConstructionModeUpdate() with a static field
        /// CustomRange so you can tune it (or disable the patch) at runtime.
        /// </summary>
        /// Usage:
        /// InGameHUD_ConstructionRangePatch.Enabled = true; // false to disable.
        /// InGameHUD_ConstructionRangePatch.CustomRange = 100f;
        [HarmonyPatch(typeof(InGameHUD), "DoConstructionModeUpdate")]
        public static class InGameHUD_ConstructionRangePatch
        {
            // New range to use instead of 5f.
            public static float CustomRange = 420f;

            // Toggle this to turn the patch on or off at runtime.
            // When false, the original 5f constant is left intact.
            public static bool Enabled = true;

            /// <summary>
            /// Transpiler walks the IL for Ldc_R4 (load constant float),
            /// looks for 5.0, and-if Enabled-replaces it with a ldsfld loading
            /// our CustomRange instead.  Otherwise it keeps the original.
            /// </summary>
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // Grab the FieldInfo once.
                var customField = AccessTools.Field(typeof(InGameHUD_ConstructionRangePatch), nameof(CustomRange));

                foreach (var instr in instructions)
                {
                    // Detect "load constant 5.0f".
                    if (instr.opcode == OpCodes.Ldc_R4
                        && instr.operand is float f
                        && Math.Abs(f - 5f) < 0.0001f)
                    {
                        if (Enabled)
                        {
                            // Patch enabled: Load from CustomRange instead.
                            yield return new CodeInstruction(OpCodes.Ldsfld, customField);
                        }
                        else
                        {
                            // Patch disabled: Emit the original instruction unchanged.
                            yield return instr;
                        }
                    }
                    else
                    {
                        // All other instructions pass through.
                        yield return instr;
                    }
                }
            }
        }
        */
        #endregion

        #region Enable The Chat In Offline Games

        // Removed: Moved to QoL mod.
        /*
        /// <summary>
        /// Adds a Postfix to OnPlayerInput so that pressing the chat key
        /// in offline mode opens the chat screen, removing the
        /// "this._game.IsOnlineGame &&" guard in the original.
        /// </summary>
        [HarmonyPatch]
        static class InGameHUD_OfflineChatPatch
        {
            // Target the protected override bool OnPlayerInput(...) method via reflection.
            static MethodBase TargetMethod()
                => AccessTools.Method(
                       typeof(InGameHUD), "OnPlayerInput",
                       new[] { typeof(InputManager), typeof(GameController),
                               typeof(KeyboardInput), typeof(GameTime) });

            // Postfix runs after the original. We replicate the chat-open logic for offline mode.
            static void Postfix(InGameHUD __instance)
            {
                try
                {
                    // Retrieve game instance (private field _game)
                    var gameField = AccessTools.Field(typeof(InGameHUD), "_game");
                    var game = gameField?.GetValue(__instance);
                    if (game == null) return;

                    // Check IsOnlineGame; skip if online (original logic already handles that)
                    bool isOnline = false;
                    var isOnlineProp = AccessTools.Property(game.GetType(), "IsOnlineGame");
                    if (isOnlineProp != null)
                        isOnline = (bool)isOnlineProp.GetValue(game);
                    else
                    {
                        var isOnlineField = AccessTools.Field(game.GetType(), "IsOnlineGame");
                        if (isOnlineField != null)
                            isOnline = (bool)isOnlineField.GetValue(game);
                    }
                    if (isOnline)
                        return; // online already handled in original method

                    // Check __instance.IsChatting
                    bool isChatting = false;
                    var chattingProp = AccessTools.Property(typeof(InGameHUD), "IsChatting");
                    if (chattingProp != null)
                        isChatting = (bool)chattingProp.GetValue(__instance);
                    else
                    {
                        var chattingField = AccessTools.Field(typeof(InGameHUD), "IsChatting");
                        if (chattingField != null)
                            isChatting = (bool)chattingField.GetValue(__instance);
                    }
                    if (isChatting)
                        return;

                    // Get controllerMapping from game (_controllerMapping)
                    var controllerMappingField = AccessTools.Field(game.GetType(), "_controllerMapping");
                    if (controllerMappingField == null) return;
                    var controllerMapping = controllerMappingField.GetValue(game);
                    if (controllerMapping == null) return;

                    // Get TextChat and its Released state
                    var textChatMember = AccessTools.Property(controllerMapping.GetType(), "TextChat")
                                        ?? (MemberInfo)AccessTools.Field(controllerMapping.GetType(), "TextChat");
                    if (textChatMember == null) return;
                    object textChat = null;
                    if (textChatMember is PropertyInfo pi)
                        textChat = pi.GetValue(controllerMapping);
                    else if (textChatMember is FieldInfo fi)
                        textChat = fi.GetValue(controllerMapping);
                    if (textChat == null) return;

                    bool released = false;
                    var releasedProp = AccessTools.Property(textChat.GetType(), "Released")
                                       ?? (MemberInfo)AccessTools.Field(textChat.GetType(), "Released") as PropertyInfo;
                    if (releasedProp is PropertyInfo rp)
                        released = (bool)rp.GetValue(textChat);
                    else
                    {
                        var releasedField = AccessTools.Field(textChat.GetType(), "Released");
                        if (releasedField != null)
                            released = (bool)releasedField.GetValue(textChat);
                    }
                    if (!released)
                        return;

                    // At this point: offline, not chatting, and TextChat.Released -> open chat screen

                    // Get the chat screen (_chatScreen)
                    var chatScreenField = AccessTools.Field(typeof(InGameHUD), "_chatScreen");
                    var chatScreen = chatScreenField?.GetValue(__instance);
                    if (chatScreen == null) return;

                    // Get game.GameScreen._uiGroup
                    object gameScreen = null;
                    var gameProp = AccessTools.Property(game.GetType(), "GameScreen");
                    if (gameProp != null)
                        gameScreen = gameProp.GetValue(game);
                    else
                    {
                        var gameField2 = AccessTools.Field(game.GetType(), "GameScreen");
                        if (gameField2 != null)
                            gameScreen = gameField2.GetValue(game);
                    }
                    if (gameScreen == null) return;

                    var uiGroupField = AccessTools.Field(gameScreen.GetType(), "_uiGroup");
                    if (uiGroupField == null) return;
                    var uiGroup = uiGroupField.GetValue(gameScreen);
                    if (uiGroup == null) return;

                    // Push the chat screen
                    var pushMethod = AccessTools.Method(uiGroup.GetType(), "PushScreen");
                    pushMethod?.Invoke(uiGroup, new object[] { chatScreen });
                }
                catch (Exception ex)
                {
                    Log($"[OfflineChatPatch] exception: {ex}.");
                }
            }
        }
        */
        #endregion

        #region Display The Targeted Block Name And Id

        // Removed: Moved to QoL mod.
        /*
        /// <summary>
        /// Completely strips out the default DrawString call in OnDraw,
        /// then draws "BlockName (ID)" instead.
        /// </summary>
        [HarmonyPatch]
        static class InGameHUD_DrawPatch
        {
            // Tell Harmony which method we're patching (the protected override OnDraw).
            static MethodBase TargetMethod()
                => AccessTools.Method(
                       typeof(InGameHUD), "OnDraw",
                       new[] { typeof(GraphicsDevice),
                               typeof(SpriteBatch),
                               typeof(GameTime) });

            // Transpiler: Swap out every DrawString call in that method with our NoOp stub.
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> DisableDefaultDraw(IEnumerable<CodeInstruction> instrs)
            {
                var drawString = AccessTools.Method(
                    typeof(SpriteBatch),
                    "DrawString",
                    new[]{
                    typeof(SpriteFont),
                    typeof(string),
                    typeof(Vector2),
                    typeof(Color),
                    typeof(float),
                    typeof(Vector2),
                    typeof(float),
                    typeof(SpriteEffects),
                    typeof(float)
                    });

                // Our stub must have the same signature plus the instance as first arg:
                var noOp = AccessTools.Method(typeof(InGameHUD_DrawPatch), nameof(NoOpDrawString));

                foreach (var ins in instrs)
                {
                    if (ins.Calls(drawString))
                    {
                        // Replace the callvirt with a static call to NoOpDrawString.
                        yield return new CodeInstruction(OpCodes.Call, noOp);
                    }
                    else
                    {
                        yield return ins;
                    }
                }
            }

            // Stub that does nothing (consumes all DrawString parameters).
            public static void NoOpDrawString(
                SpriteBatch sb,
                SpriteFont font,
                string text,
                Vector2 position,
                Color color,
                float rotation,
                Vector2 origin,
                float scale,
                SpriteEffects effects,
                float layerDepth)
            {
                // Intentionally left blank
            }

            // Postfix: Now draw your enhanced label.
            [HarmonyPostfix]
            static void Postfix(InGameHUD __instance, SpriteBatch spriteBatch)
            {
                try
                {
                    // If HideUI is on, skip the entire OnDraw (no default draw, no postfix).
                    if (HUDToggle._hideUI) return;

                    // Grab the ConstructionProbe (private field) via reflection.
                    var probeField = AccessTools.Field(typeof(InGameHUD), "ConstructionProbe");
                    var probe = probeField.GetValue(__instance);
                    // Check if we're actually targeting something buildable.
                    bool ableToBuild = (bool)AccessTools.Property(probe.GetType(), "AbleToBuild").GetValue(probe);
                    if (!ableToBuild) return;

                    // Get the block index we're pointing at.
                    var worldIndex = (DNA.IntVector3)AccessTools.Field(probe.GetType(), "_worldIndex").GetValue(probe);
                    // Translate to the enum and then to the BlockType.
                    var blockEnum = InGameHUD.GetBlock(worldIndex);
                    var blockType = BlockType.GetType(blockEnum);

                    // Build the display string: "Name (ID)".
                    string text = $"{blockType.Name} ({(int)blockEnum})";

                    // Get the median font and scale factor.
                    var game = CastleMinerZGame.Instance;
                    var medFont = (SpriteFont)AccessTools.Field(typeof(CastleMinerZGame), "_medFont").GetValue(game);
                    float scale = Screen.Adjuster.ScaleFactor.Y;

                    // Compute a position in the top‐right, matching the original.
                    int screenW = Screen.Adjuster.ScreenRect.Width;
                    float textWidth = medFont.MeasureString(text).X * scale;
                    // 10px padding + approximate extra for "(ID)".
                    float x = screenW - (textWidth + 10f * scale);
                    float y = medFont.LineSpacing * 4 * scale;

                    // Draw on top.
                    Color MenuAqua = new Color(53, 170, 253);
                    spriteBatch.Begin();
                    spriteBatch.DrawString(
                        medFont,
                        text,
                        new Vector2(x, y),
                        MenuAqua * 0.75f,
                        0f,
                        Vector2.Zero,
                        scale,
                        SpriteEffects.None,
                        0f
                    );
                    spriteBatch.End();
                }
                catch (Exception ex)
                {
                    Log($"[InGameHUD_DrawPatch] error: {ex}.");
                }
            }
        }
        */
        #endregion

        #region Increase Chat Length

        // Removed: Moved to QoL mod.
        /*
        /// <summary>
        /// Make PlainChatInputScreen use the game window width instead of the hard-coded 350.
        /// We:
        ///  - Postfix the .ctor(float) to override the default Width immediately.
        ///  - Postfix OnDraw(...) so width keeps tracking window resizes.
        /// </summary>
        internal static class PlainChatInputScreen_WidthPatch
        {
            // Patch the constructor via TargetMethod().
            [HarmonyPatch]
            private static class CtorPatch
            {
                // Explicitly return the (float) ctor.
                static MethodBase TargetMethod()
                    => AccessTools.Constructor(typeof(PlainChatInputScreen), new[] { typeof(float) });

                static void Postfix(PlainChatInputScreen __instance)
                {
                    TryApplyDynamicWidth(__instance);
                }
            }

            // Patch OnDraw(GraphicsDevice, SpriteBatch, GameTime) so we keep in sync with resizes.
            [HarmonyPatch(typeof(PlainChatInputScreen), "OnDraw",
                new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) })]
            private static class OnDrawPatch
            {
                static void Postfix(PlainChatInputScreen __instance)
                {
                    TryApplyDynamicWidth(__instance);
                }
            }

            // Shared logic to compute & apply the desired width + resize the edit control.
            private static void TryApplyDynamicWidth(PlainChatInputScreen screen)
            {
                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game?.Window == null || screen == null)
                        return;

                    // Use the current client area width. Guard against going smaller than original.
                    int target = Math.Max(350, game.Window.ClientBounds.Width);

                    if (screen.Width != target)
                    {
                        screen.Width = target;

                        // Resize the text box to match (the ctor uses (Width - 20, 200)).
                        var tec = screen._textEditControl;
                        if (tec != null)
                        {
                            tec.Size = new Size(screen.Width - 20, tec.Size.Height); // Keep current height.
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PlainChatInputScreen_WidthPatch] error: {ex}.");
                }
            }
        }
        */
        #endregion

        #region HUD Console Opacity Floor (0.25 Opacity)

        // Removed: Moved to QoL mod.
        /*
        /// <summary>
        /// Patch target:
        /// - Method: ConsoleElement.OnDraw(GraphicsDevice, SpriteBatch, GameTime, bool).
        /// Goal:
        /// - Wherever the code reads 'Message.Visibility', replace that float 'v' with
        ///   'min(1.0, v + 0.25)' by calling AdjustVisibility(v).
        /// Effect:
        /// - Fresh messages stay fully visible (clamped to 1.0).
        /// - Fading messages bottom out at 0.25 (i.e., emulate 1.25f - PercentComplete).
        /// - No need to touch the private nested ConsoleElement.Message type directly.
        /// </summary>
        [HarmonyPatch(typeof(ConsoleElement), "OnDraw",
            new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime), typeof(bool) })]
        internal static class ConsoleElement_OnDraw_VisibilityTranspiler
        {
            // Helper called from IL after each get_Visibility:
            //  - If v >= 0.75, return 1.0 (clamp).
            //  - Else return v + 0.25 (raise the floor).
            private static float AdjustVisibility(float v) =>
                v >= 0.75f ? 1f : (v + 0.25f); // Micro-branch ≈ Math.Min(1f, v + 0.25f).

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);

                // Reflect the nested type and its property getter: ConsoleElement.Message.get_Visibility.
                var messageType = AccessTools.Inner(typeof(ConsoleElement), "Message");
                var visGetter = AccessTools.PropertyGetter(messageType, "Visibility");
                var adjustMethod = AccessTools.Method(typeof(ConsoleElement_OnDraw_VisibilityTranspiler), nameof(AdjustVisibility));

                // Iterate IL and, for every callvirt get_Visibility,
                // push a call to AdjustVisibility to transform the value on the stack.
                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i];
                    yield return ins; // Emit the original instruction.

                    if (ins.opcode == OpCodes.Callvirt && ins.operand is MethodInfo mi && mi == visGetter)
                    {
                        // Stack before: [..., float visibility].
                        yield return new CodeInstruction(OpCodes.Call, adjustMethod);
                        // Stack after:  [..., float adjustedVisibility].
                    }
                }
            }
        }
        */
        #endregion

        #endregion

        #region Showcase Patches (Optional)

        // Showcasing Commands //

        #region Allow For Full Brightness

        /// <summary>
        /// Overrides InGameHUD.RefreshPlayer() to skip setting PlayerHealth,
        /// leaving health as-is instead of resetting to 1.0f every call.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "RefreshPlayer")]
        static class InGameHUD_RefreshPlayer_Prefix
        {
            public static bool Enabled = true;

            // Runs instead of the original RefreshPlayer().
            static bool Prefix(InGameHUD __instance)
            {
                // Patch disabled: Emit the original instruction unchanged.
                if (!Enabled) return true;

                // Replicate everything *except* the health reset.
                __instance.LocalPlayer.Dead = false;
                __instance.LocalPlayer.FPSMode = true;
                __instance.PlayerHealth = 1f; // Keep respawn behavior intact.

                // Return false to skip the original method completely.
                return false;
            }
        }
        #endregion

        #region Make Blocks Full Brightness

        #region Runtime Helper

        /// <summary>
        /// Turn fullbright on/off now:
        /// - flips the global flag,
        /// - updates every BlockType singleton's DrawFullBright,
        /// - requeues geometry for all loaded chunks so visuals update immediately.
        /// </summary>
        public static class FullBrightRuntime
        {
            /// <summary>
            /// Global toggle you can flip at runtime (e.g. a chat command).
            /// Call <see cref="FullBrightRuntime.SetEnabled(bool)"/> to apply immediately.
            /// </summary>
            public static bool UseFullBrightTiles { get; private set; } = false;

            /// <summary>Public entrypoint you can call from a UI / chat command.</summary>
            public static void SetEnabled(bool enabled)
            {
                UseFullBrightTiles = enabled;

                // 1) Touch all BlockType singletons so their flag matches right now.
                TouchAllBlockTypes(enabled);

                // 2) Force geometry rebuild on all loaded chunks.
                RebuildAllChunkGeometry();
            }

            /// <summary>
            /// Safely iterate the BlockType enum and set DrawFullBright
            /// on each real BlockType instance.
            /// </summary>
            private static void TouchAllBlockTypes(bool enabled)
            {
                try
                {
                    Array values = Enum.GetValues(typeof(BlockTypeEnum));
                    for (int i = 0; i < values.Length; i++)
                    {
                        var e = (BlockTypeEnum)values.GetValue(i);
                        // Some enum values can be aliases; just guard with try/catch.
                        try
                        {
                            BlockType bt = BlockType.GetType(e);
                            if (bt != null)
                                bt.DrawFullBright = enabled;
                        }
                        catch { /* ignore bad/unused enum entries */ }
                    }
                }
                catch
                {
                    // As a fallback, touching only common types is still fine;
                    // the postfix keeps future fetches consistent.
                }
            }

            /// <summary>
            /// Use BlockTerrain's internal ChunkActionPool to schedule a geometry rebuild
            /// for every loaded chunk (24 x 24 ring = 576 indices), then drain the queue.
            /// </summary>
            private static void RebuildAllChunkGeometry()
            {
                var terrain = BlockTerrain.Instance;
                if (terrain == null)
                    return;

                // Reflect private fields/methods:
                //   BlockTerrain._computeGeometryPool : ChunkActionPool
                //   ChunkActionPool.Add(int)          : void
                //   ChunkActionPool.Drain()           : void
                var poolField = AccessTools.Field(typeof(BlockTerrain), "_computeGeometryPool");
                if (poolField == null)
                    return;

                object pool = poolField.GetValue(terrain);
                if (pool == null)
                    return;

                MethodInfo miAdd = AccessTools.Method(pool.GetType(), "Add", new Type[] { typeof(int) });
                MethodInfo miDrain = AccessTools.Method(pool.GetType(), "Drain", Type.EmptyTypes);

                if (miAdd == null || miDrain == null)
                    return;

                // Queue all chunk indices (the ring is always 24x24 = 576).
                for (int i = 0; i < 576; i++)
                {
                    try { miAdd.Invoke(pool, new object[] { i }); }
                    catch { /* ignore */ }
                }

                // Drain the work so it applies right away.
                try { miDrain.Invoke(pool, null); } catch { }
            }
        }
        #endregion

        /// <summary>
        /// Postfix on BlockType.GetType(...) so that every BlockType
        /// instance fetched gets DrawFullBright = true when enabled.
        /// This is very cheap and keeps future fetches in-sync.
        /// </summary>
        [HarmonyPatch(typeof(BlockType))]
        static class BlockType_GetType_Patch
        {
            [HarmonyPatch(nameof(BlockType.GetType))]
            [HarmonyPostfix]
            static void Postfix(BlockType __result)
            {
                if (__result == null) return;
                if (!FullBrightRuntime.UseFullBrightTiles) return;

                __result.DrawFullBright = true;
            }
        }
        #endregion

        #region Allow For Toggle HUD And UI

        /// <summary>
        /// Global toggle you can flip at runtime (e.g. via a chat command).
        /// </summary>
        public static class HUDToggle
        {
            public static bool _hideUI = false;
        }

        /// <summary>
        /// If HUDToggle.HideUI == true, skip the entire OnDraw (so nothing is rendered).
        /// Otherwise run the original OnDraw as usual.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD))]
        [HarmonyPatch("OnDraw")]
        static class InGameHUD_OnDraw_Patch
        {
            static bool Prefix()
            {
                // Return false to skip original OnDraw.
                return !HUDToggle._hideUI;
            }
        }

        /// <summary>
        /// If HUDToggle.HideUI == true, skip the entire construction-mode update (so the box doesn't move).
        /// Otherwise run the original DoConstructionModeUpdate as usual.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD))]
        [HarmonyPatch("DoConstructionModeUpdate")]
        static class InGameHUD_DoConstructionModeUpdate_Patch
        {
            // Runs before the original. If it returns false, the original method is skipped entirely.
            static bool Prefix()
            {
                // If our toggle is on, skip the entire construction‐mode update.
                if (HUDToggle._hideUI)
                    return false;

                // Otherwise run the normal game logic.
                return true;
            }
        }

        // This was removed in recent updates, simply ignore patching it now. //
        /// <summary>
        /// After the original OnPlayerInput runs, override the ShowTitleSafeArea assignment
        /// so it follows our HideUI toggle rather than the private _hideUI field.
        /// </summary>
        /*
        [HarmonyPatch(typeof(InGameHUD))]
        [HarmonyPatch("OnPlayerInput",
            new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) })]
        static class InGameHUD_OnPlayerInput_Patch
        {
            static void Postfix()
            {
                // Force title-safe area on/off based on our static toggle
                CastleMinerZGame.Instance.ShowTitleSafeArea = !HUDToggle._hideUI;
            }
        }
        */
        #endregion

        #endregion

        #region InfiniteDurability (WorldEdit Wand Tool Items)

        /// <summary>
        /// Prevents durability loss for WorldEdit wand tools by short-circuiting InventoryItem.InflictDamage().
        /// Summary: Only affects items whose ItemClass.ID matches WandItemID or NavWandItemID.
        /// </summary>
        [HarmonyPatch]
        internal static class InventoryItem_InflictDamage_InfiniteDurability_WandsOnly
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var baseType = typeof(InventoryItem);

                foreach (var t in AccessTools.GetTypesFromAssembly(baseType.Assembly))
                {
                    if (!baseType.IsAssignableFrom(t)) continue;

                    var m = AccessTools.Method(t, nameof(InventoryItem.InflictDamage), Type.EmptyTypes);
                    if (m != null && m.DeclaringType == t)
                        yield return m;
                }
            }

            /// <summary>
            /// When enabled: Prevent durability from dropping and block "break" for wand items only.
            /// Note: Sets ItemHealthLevel to a tiny positive so other checks don't treat it as destroyed.
            /// </summary>
            static bool Prefix(InventoryItem __instance, ref bool __result)
            {
                // Only active when one of the WorldEdit wand systems is enabled.
                // Summary: Avoids globally affecting durability when WorldEdit tools are not in use.
                if (!IsAnyWandModeActive())
                    return true; // Run original normally.

                // Determine the item ID being damaged.
                int id;
                try
                {
                    id = (int)__instance.ItemClass.ID;
                }
                catch
                {
                    return true; // If we can't read the ID, don't interfere.
                }

                // Only protect the selection wand and the navwand (both are configurable).
                // NavWandItemID may be -1 when disabled, so guard that too.
                if (!IsProtectedWorldEditToolItemId(id))
                    return true; // Not our tools -> do normal durability.

                __instance.ItemHealthLevel = 1f; // Keep alive at minimal health.
                __result = false;                // "not broken".
                return false;                    // Skip original (no durability loss).
            }

            /// <summary>
            /// Returns true if the given item id is one of the currently active WorldEdit tool items.
            /// Summary: Keeps the block scoped to wand/tool/navwand/brush items only.
            /// </summary>
            private static bool IsProtectedWorldEditToolItemId(int id)
            {
                // 'BareHands' uses the ID 0, skip checking it.
                return (ActiveWandItemID    > 0 && id == ActiveWandItemID)    ||
                       (ActiveToolItemID    > 0 && id == ActiveToolItemID)    ||
                       (ActiveNavWandItemID > 0 && id == ActiveNavWandItemID) ||
                       (ActiveBrushItemID   > 0 && id == ActiveBrushItemID);
            }
        }
        #endregion

        #region DisableMiningAndMelee (WorldEdit Wand Tool Items)

        /// <summary>
        /// Prevents WorldEdit wand tool items from performing vanilla mining/melee.
        /// Summary: Short-circuits InventoryItem.ProcessInput() so left/right "use" doesn't dig or deal damage.
        /// </summary>
        [HarmonyPatch]
        internal static class InventoryItem_ProcessInput_DisableMiningAndMelee_WorldEditTools
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var baseType = typeof(InventoryItem);
                var sig = new Type[]
                {
                    typeof(InGameHUD),
                    typeof(CastleMinerZControllerMapping)
                };

                foreach (var t in AccessTools.GetTypesFromAssembly(baseType.Assembly))
                {
                    if (!baseType.IsAssignableFrom(t)) continue;

                    var m = AccessTools.Method(t, nameof(InventoryItem.ProcessInput), sig);
                    if (m != null && m.DeclaringType == t)
                        yield return m;
                }
            }

            static bool Prefix(InventoryItem __instance,
                               InGameHUD hud,
                               CastleMinerZControllerMapping controller)
            {
                // Only interfere when your wand systems are active.
                if (!IsAnyWandModeActive())
                    return true;

                // Identify the item being "used".
                int id;
                try { id = (int)__instance.ItemClass.ID; }
                catch { return true; }

                // Only block vanilla use for your configured tool items.
                if (!IsProtectedWorldEditToolItemId(id))
                    return true;

                // Clean up any visual/tool state that might be left over.
                try
                {
                    // ProcessInput normally writes crack progress + UsingTool.
                    // Clearing here avoids stuck crack overlays/tool state.
                    CastleMinerZGame.Instance.GameScreen.CrackBox.CrackAmount = 0f;
                    if (hud?.LocalPlayer != null)
                        hud.LocalPlayer.UsingTool = false;
                }
                catch
                {
                    // If we can't touch HUD state, still block the action.
                }

                // Skip original -> no Dig(), no Melee(), no fuse setting, etc.
                return false;
            }

            /// <summary>
            /// Returns true if the given item id is one of the currently active WorldEdit tool items.
            /// Summary: Keeps the block scoped to wand/tool/navwand/brush items only.
            /// </summary>
            private static bool IsProtectedWorldEditToolItemId(int id)
            {
                // 'BareHands' uses the ID 0, skip checking it.
                return (ActiveWandItemID    > 0 && id == ActiveWandItemID)    ||
                       (ActiveToolItemID    > 0 && id == ActiveToolItemID)    ||
                       (ActiveNavWandItemID > 0 && id == ActiveNavWandItemID) ||
                       (ActiveBrushItemID   > 0 && id == ActiveBrushItemID);
            }
        }
        #endregion

        #region DisableOpeningCrates (WorldEdit Wand Tool Items)

        /// <summary>
        /// Prevents opening the crate UI while holding a WorldEdit wand/tool/navwand/brush item.
        /// Summary: We intercept ScreenGroup.PushScreen() and ignore pushes of <see cref="CrateScreen"/> when
        /// the local player is currently holding one of our configured WorldEdit tool items.
        /// </summary>
        [HarmonyPatch]
        internal static class ScreenGroup_PushScreen_DisableOpeningCrates_WorldEditTools
        {
            /// <summary>
            /// Targets any ScreenGroup.PushScreen overload that takes exactly one argument assignable to Screen.
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var m in typeof(ScreenGroup).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(m.Name, "PushScreen", StringComparison.Ordinal))
                        continue;

                    var ps = m.GetParameters();
                    if (ps.Length != 1)
                        continue;

                    if (typeof(Screen).IsAssignableFrom(ps[0].ParameterType))
                        yield return m;
                }
            }

            /// <summary>
            /// If the pushed screen is a crate screen AND we're holding a WorldEdit tool item,
            /// return false to skip the push (prevents right-click opening crates while using tools).
            /// </summary>
            static bool Prefix(Screen screen)
            {
                try
                {
                    if (screen == null)
                        return true;

                    // Only affect crate UI pushes (leave chat, menus, block picker, etc. alone).
                    if (!(screen is CrateScreen))
                        return true;

                    // Only interfere when your wand systems are active.
                    // Summary: Avoid changing vanilla behavior when WorldEdit is disabled.
                    if (!IsAnyWandModeActive())
                        return true;

                    // Resolve the local HUD (for current held item).
                    InGameHUD hud = null;
                    try { hud = CastleMinerZGame.Instance?.GameScreen?.HUD; } catch { }
                    if (hud == null)
                        return true;

                    var active = hud.ActiveInventoryItem;
                    if (active?.ItemClass == null)
                        return true;

                    int id;
                    try { id = (int)active.ItemClass.ID; }
                    catch { return true; }

                    // Only block for your configured WorldEdit tool items.
                    if (!IsProtectedWorldEditToolItemId(id))
                        return true;

                    return false; // Skip PushScreen -> Crate won't open.
                }
                catch
                {
                    // Fail open: Never break UI flow if something unexpected happens.
                    return true;
                }
            }

            /// <summary>
            /// Returns true if the given item id is one of the currently active WorldEdit tool items.
            /// Summary: Keeps the block scoped to wand/tool/navwand/brush items only.
            /// </summary>
            private static bool IsProtectedWorldEditToolItemId(int id)
            {
                // 'BareHands' uses the ID 0, skip checking it.
                return (ActiveWandItemID    > 0 && id == ActiveWandItemID)    ||
                       (ActiveToolItemID    > 0 && id == ActiveToolItemID)    ||
                       (ActiveNavWandItemID > 0 && id == ActiveNavWandItemID) ||
                       (ActiveBrushItemID   > 0 && id == ActiveBrushItemID);
            }
        }
        #endregion

        #region DisableActivate_Spawners_Explosives (WorldEdit Tool Items)

        [HarmonyPatch]
        internal static class Patch_BlockType_IsSpawnerClickable_BlockWhenHoldingTool
        {
            // Patches internal static bool BlockType.IsSpawnerClickable(BlockTypeEnum blockType)
            static MethodBase TargetMethod()
                => AccessTools.Method(typeof(BlockType), "IsSpawnerClickable", new[] { typeof(BlockTypeEnum) });

            [HarmonyPrefix]
            private static bool Prefix(BlockTypeEnum blockType, ref bool __result)
            {
                // Resolve the local HUD (for current held item).
                InGameHUD hud = null;
                try { hud = CastleMinerZGame.Instance?.GameScreen?.HUD; } catch { }
                if (hud == null)
                    return true;

                var active = hud.ActiveInventoryItem;
                if (active?.ItemClass == null)
                    return true;

                int id;
                try { id = (int)active.ItemClass.ID; }
                catch { return true; }

                // Only block for your configured WorldEdit tool items.
                if (!IsProtectedWorldEditToolItemId(id))
                    return true;

                __result = false; // "not clickable" -> HUD won't go into spawner click path.
                return false;     // Skip original.
            }
        }

        [HarmonyPatch]
        internal static class Patch_InGameHUD_SetFuseForExplosive_BlockWhenHoldingTool
        {
            // Patches (usually private) void InGameHUD.SetFuseForExplosive(IntVector3 location, ExplosiveTypes explosiveType)
            static MethodBase TargetMethod()
                => AccessTools.Method(typeof(InGameHUD), "SetFuseForExplosive",
                    new[] { typeof(IntVector3), typeof(ExplosiveTypes) });

            [HarmonyPrefix]
            private static bool Prefix()
            {
                // Resolve the local HUD (for current held item).
                InGameHUD hud = null;
                try { hud = CastleMinerZGame.Instance?.GameScreen?.HUD; } catch { }
                if (hud == null)
                    return true;

                var active = hud.ActiveInventoryItem;
                if (active?.ItemClass == null)
                    return true;

                int id;
                try { id = (int)active.ItemClass.ID; }
                catch { return true; }

                // Only block for your configured WorldEdit tool items.
                if (!IsProtectedWorldEditToolItemId(id))
                    return true;

                return false; // Skip fuse trigger.
            }
        }

        /// <summary>
        /// Returns true if the given item id is one of the currently active WorldEdit tool items.
        /// Summary: Keeps the block scoped to wand/tool/navwand/brush items only.
        /// </summary>
        private static bool IsProtectedWorldEditToolItemId(int id)
        {
            // 'BareHands' uses the ID 0, skip checking it.
            return (ActiveWandItemID    > 0 && id == ActiveWandItemID)    ||
                   (ActiveToolItemID    > 0 && id == ActiveToolItemID)    ||
                   (ActiveNavWandItemID > 0 && id == ActiveNavWandItemID) ||
                   (ActiveBrushItemID   > 0 && id == ActiveBrushItemID);
        }
        #endregion

        #region Remove Hardcoded Item Ban (Bullets / SpaceRock)

        /// <summary>
        /// - Reimplements vanilla IsValid() stack validation logic, but removes the two hardcoded ID bans:
        ///     • InventoryItemIDs.BloodStoneBullets
        ///     • InventoryItemIDs.SpaceRock
        /// - Uses cached reflection for:
        ///     • InventoryItem._stackCount
        ///     • InventoryItem.ItemValidWithZeroStacks(id)
        /// - Returns false to skip the original method entirely (so the hard-ban clause never runs).
        /// </summary>
        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.IsValid))]
        internal static class Patch_InventoryItem_IsValid_RemoveHardBan
        {
            private static readonly FieldInfo F_StackCount =
                AccessTools.Field(typeof(InventoryItem), "_stackCount");

            private static readonly MethodInfo M_ItemValidWithZeroStacks =
                AccessTools.Method(typeof(InventoryItem), "ItemValidWithZeroStacks");

            [HarmonyPrefix]
            private static bool Prefix(InventoryItem __instance, ref bool __result)
            {
                if (__instance == null || __instance.ItemClass == null)
                {
                    __result = false;
                    return false;
                }

                int stack = (F_StackCount != null) ? (int)F_StackCount.GetValue(__instance) : 0;
                int max = __instance.MaxStackCount;
                var id = __instance.ItemClass.ID;

                bool validZero = true;
                if (stack <= 0 && M_ItemValidWithZeroStacks != null)
                    validZero = (bool)M_ItemValidWithZeroStacks.Invoke(__instance, new object[] { id });

                __result =
                    stack <= max
                    && (stack > 0 || validZero);

                return false; // Skip original.
            }
        }
        #endregion
    }
}