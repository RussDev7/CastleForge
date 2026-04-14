/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Drawing.UI.Controls;
using System.Reflection.Emit;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using System.Text;
using HarmonyLib;                       // Harmony patching library.
using DNA.Input;
using System;

using static ModLoader.LogSystem;       // For Log(...).

namespace QoLTweaks
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
        /// QoLHotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (QoLHotkeys.ReloadPressedThisFrame()) { QoLConfig.LoadApply(); ... }
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
        internal static class QoLHotkeys
        {
            private static HotkeyBinding _reload;
            private static bool _hasPrev;
            private static Microsoft.Xna.Framework.Input.KeyboardState _prev;

            /// <summary>
            /// Sets (or disables) the reload binding. Resets the edge detector to avoid a spurious trigger right after change.
            /// </summary>
            private static string _reloadKeyRaw;
            public static void SetReloadBinding(string s)
            {
                _reload = HotkeyBinding.Parse(s);
                _hasPrev = false; // Reset edge detector so we don't fire instantly after changing binding.

                if (_reloadKeyRaw != s)
                {
                    _reloadKeyRaw = s;
                    LogIfEnabled($"[QoLT] Reload hotkey set to \"{s}\".");
                }
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
        /// Keeps the body small; heavy lifting should be inside QoLConfig.LoadApply().
        /// </summary>
        [HarmonyPatch]
        static class Patch_Hotkey_ReloadConfig_QoLTweaks
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI.
            /// </summary>
            static void Postfix(InGameHUD __instance)
            {
                if (!QoLHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    QoLConfig.LoadApply();

                    SendFeedback($"[QoLT] Config hot-reloaded from \"{PathShortener.ShortenForLog(QoLConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    LogIfEnabled($"[QoLT] Hot-reload failed: {ex.Message}.");
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

        #region Patch Helpers

        /// <summary>
        /// Returns true when the standalone QoL mod and the specified feature are both enabled.
        /// </summary>
        private static bool FeatureEnabled(bool featureEnabled)
        {
            return QoL_Settings.Enabled && featureEnabled;
        }

        /// <summary>
        /// Logs only when optional QoL diagnostics are enabled.
        /// </summary>
        private static void LogIfEnabled(string message)
        {
            if (QoL_Settings.DoLogging)
                Log(message);
        }
        #endregion

        #region Patches

        #region Increase Placement Range

        /// <summary>
        /// Replaces the hard-coded construction range with the configured standalone QoL range when enabled.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "DoConstructionModeUpdate")]
        internal static class InGameHUD_ConstructionRangePatch
        {
            /// <summary>
            /// Returns either the configured construction range or the original vanilla range.
            /// </summary>
            private static float GetConstructionRange(float vanillaRange)
            {
                return FeatureEnabled(QoL_Settings.EnableConstructionRangePatch)
                    ? QoL_Settings.ConstructionRange
                    : vanillaRange;
            }

            /// <summary>
            /// Routes the hard-coded placement range literal through the QoL config.
            /// </summary>
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var miGetConstructionRange = AccessTools.Method(typeof(InGameHUD_ConstructionRangePatch), nameof(GetConstructionRange));

                foreach (var instr in instructions)
                {
                    if (instr.opcode == OpCodes.Ldc_R4 && instr.operand is float f && Math.Abs(f - 5f) < 0.0001f)
                    {
                        yield return instr;
                        yield return new CodeInstruction(OpCodes.Call, miGetConstructionRange);
                        continue;
                    }

                    yield return instr;
                }
            }
        }
        #endregion

        #region Enable The Chat In Offline Games

        /// <summary>
        /// Opens the text chat screen in offline games when the standalone QoL toggle is enabled.
        /// </summary>
        [HarmonyPatch]
        internal static class InGameHUD_OfflineChatPatch
        {
            private static MethodBase TargetMethod()
                => AccessTools.Method(
                       typeof(InGameHUD), "OnPlayerInput",
                       new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// Replays the online chat-open logic for offline worlds.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(InGameHUD __instance)
            {
                if (!FeatureEnabled(QoL_Settings.EnableOfflineChat))
                    return;

                try
                {
                    var gameField = AccessTools.Field(typeof(InGameHUD), "_game");
                    var game = gameField?.GetValue(__instance);
                    if (game == null) return;

                    bool isOnline = false;
                    var isOnlineProp = AccessTools.Property(game.GetType(), "IsOnlineGame");
                    if (isOnlineProp != null)
                        isOnline = (bool)isOnlineProp.GetValue(game, null);
                    else
                    {
                        var isOnlineField = AccessTools.Field(game.GetType(), "IsOnlineGame");
                        if (isOnlineField != null)
                            isOnline = (bool)isOnlineField.GetValue(game);
                    }
                    if (isOnline)
                        return;

                    bool isChatting = false;
                    var chattingProp = AccessTools.Property(typeof(InGameHUD), "IsChatting");
                    if (chattingProp != null)
                        isChatting = (bool)chattingProp.GetValue(__instance, null);
                    else
                    {
                        var chattingField = AccessTools.Field(typeof(InGameHUD), "IsChatting");
                        if (chattingField != null)
                            isChatting = (bool)chattingField.GetValue(__instance);
                    }
                    if (isChatting)
                        return;

                    var controllerMappingField = AccessTools.Field(game.GetType(), "_controllerMapping");
                    if (controllerMappingField == null) return;
                    var controllerMapping = controllerMappingField.GetValue(game);
                    if (controllerMapping == null) return;

                    var textChatProp = AccessTools.Property(controllerMapping.GetType(), "TextChat");
                    var textChatField = AccessTools.Field(controllerMapping.GetType(), "TextChat");
                    object textChat = textChatProp != null ? textChatProp.GetValue(controllerMapping, null)
                                                             : (textChatField?.GetValue(controllerMapping));
                    if (textChat == null) return;

                    bool released = false;
                    var releasedProp = AccessTools.Property(textChat.GetType(), "Released");
                    if (releasedProp != null)
                        released = (bool)releasedProp.GetValue(textChat, null);
                    else
                    {
                        var releasedField = AccessTools.Field(textChat.GetType(), "Released");
                        if (releasedField != null)
                            released = (bool)releasedField.GetValue(textChat);
                    }
                    if (!released)
                        return;

                    var chatScreenField = AccessTools.Field(typeof(InGameHUD), "_chatScreen");
                    var chatScreen = chatScreenField?.GetValue(__instance);
                    if (chatScreen == null) return;

                    object gameScreen = null;
                    var gameProp = AccessTools.Property(game.GetType(), "GameScreen");
                    if (gameProp != null)
                        gameScreen = gameProp.GetValue(game, null);
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

                    var pushMethod = AccessTools.Method(uiGroup.GetType(), "PushScreen");
                    pushMethod?.Invoke(uiGroup, new[] { chatScreen });
                }
                catch (Exception ex)
                {
                    LogIfEnabled($"[OCP] Exception: {ex}.");
                }
            }
        }
        #endregion

        #region Display The Targeted Block Name And Id

        /// <summary>
        /// Replaces the default targeted block text with a custom "Name (ID)" label when enabled,
        /// while still respecting the same vanilla HUD visibility rules.
        /// </summary>
        [HarmonyPatch]
        internal static class InGameHUD_DrawPatch
        {
            private static readonly FieldInfo _fiConstructionProbe =
                AccessTools.Field(typeof(InGameHUD), "ConstructionProbe");

            private static readonly FieldInfo _fiHideUI =
                AccessTools.Field(typeof(InGameHUD), "_hideUI");

            private static readonly FieldInfo _fiShowPlayers =
                AccessTools.Field(typeof(InGameHUD), "showPlayers");

            private static readonly FieldInfo _fiMedFont =
                AccessTools.Field(typeof(CastleMinerZGame), "_medFont");

            private static MethodBase TargetMethod()
                => AccessTools.Method(typeof(InGameHUD), "OnDraw",
                       new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) });

            /// <summary>
            /// Forwards the original draw call when the tweak is disabled.
            /// When enabled, the vanilla targeted-block string draw is suppressed and replaced in Postfix.
            /// </summary>
            public static void ForwardOrSuppressDrawString(
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
                if (!FeatureEnabled(QoL_Settings.EnableTargetedBlockNameAndId))
                {
                    sb.DrawString(font, text, position, color, rotation, origin, scale, effects, layerDepth);
                    return;
                }
            }

            /// <summary>
            /// Swaps the vanilla targeted-block DrawString call for a helper that can suppress it.
            /// </summary>
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> DisableDefaultDraw(IEnumerable<CodeInstruction> instrs)
            {
                var drawString = AccessTools.Method(
                    typeof(SpriteBatch),
                    "DrawString",
                    new[]
                    {
                typeof(SpriteFont), typeof(string), typeof(Vector2), typeof(Color), typeof(float),
                typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float)
                    });

                var redirect = AccessTools.Method(typeof(InGameHUD_DrawPatch), nameof(ForwardOrSuppressDrawString));

                foreach (var ins in instrs)
                {
                    if (ins.Calls(drawString))
                    {
                        yield return new CodeInstruction(OpCodes.Call, redirect);
                    }
                    else
                    {
                        yield return ins;
                    }
                }
            }

            /// <summary>
            /// Returns true only when the vanilla top-right HUD text area is visible.
            /// This matches the same show/hide rules used by the distance/max label.
            /// </summary>
            private static bool ShouldDrawTopRightHud(InGameHUD hud)
            {
                if (hud == null)
                    return false;

                bool hideUI = _fiHideUI != null && (bool)_fiHideUI.GetValue(hud);
                if (hideUI)
                    return false;

                bool showPlayers = _fiShowPlayers != null && (bool)_fiShowPlayers.GetValue(hud);
                if (showPlayers)
                    return false;

                return true;
            }

            /// <summary>
            /// Draws the enhanced targeted block label after vanilla HUD rendering,
            /// but only when the same vanilla HUD visibility rules allow it.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(InGameHUD __instance, SpriteBatch spriteBatch)
            {
                if (!FeatureEnabled(QoL_Settings.EnableTargetedBlockNameAndId))
                    return;

                if (!ShouldDrawTopRightHud(__instance))
                    return;

                try
                {
                    var probe = _fiConstructionProbe?.GetValue(__instance);
                    if (probe == null)
                        return;

                    bool ableToBuild = (bool)AccessTools.Property(probe.GetType(), "AbleToBuild").GetValue(probe, null);
                    if (!ableToBuild)
                        return;

                    if (__instance.PlayerInventory?.ActiveInventoryItem == null)
                        return;

                    var worldIndex = (DNA.IntVector3)AccessTools.Field(probe.GetType(), "_worldIndex").GetValue(probe);
                    var blockEnum = InGameHUD.GetBlock(worldIndex);
                    var blockType = BlockType.GetType(blockEnum);

                    string text = $"{blockType.Name} ({(int)blockEnum})";

                    var game = CastleMinerZGame.Instance;
                    if (game == null)
                        return;

                    var medFont = (SpriteFont)_fiMedFont?.GetValue(game);
                    if (medFont == null)
                        return;

                    float scale = Screen.Adjuster.ScaleFactor.Y;
                    int screenW = Screen.Adjuster.ScreenRect.Width;
                    float textWidth = medFont.MeasureString(text).X * scale;
                    float x = screenW - (textWidth + 10f * scale);
                    float y = medFont.LineSpacing * 4 * scale;

                    Color menuAqua = new Color(53, 170, 253);

                    spriteBatch.Begin();
                    spriteBatch.DrawString(
                        medFont,
                        text,
                        new Vector2(x, y),
                        menuAqua * 0.75f,
                        0f,
                        Vector2.Zero,
                        scale,
                        SpriteEffects.None,
                        0f);
                    spriteBatch.End();
                }
                catch (Exception ex)
                {
                    LogIfEnabled($"[IGHUD_DP] Error: {ex}.");
                }
            }
        }
        #endregion

        #region Increase Chat Length

        /// <summary>
        /// Makes PlainChatInputScreen follow the game window width when enabled.
        /// </summary>
        internal static class PlainChatInputScreen_WidthPatch
        {
            [HarmonyPatch]
            private static class CtorPatch
            {
                private static MethodBase TargetMethod()
                    => AccessTools.Constructor(typeof(PlainChatInputScreen), new[] { typeof(float) });

                /// <summary>
                /// Applies the configured chat width immediately after construction.
                /// </summary>
                [HarmonyPostfix]
                private static void Postfix(PlainChatInputScreen __instance)
                {
                    TryApplyDynamicWidth(__instance);
                }
            }

            [HarmonyPatch(typeof(PlainChatInputScreen), "OnDraw",
                new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) })]
            private static class OnDrawPatch
            {
                /// <summary>
                /// Keeps the configured chat width in sync with window resizes.
                /// </summary>
                [HarmonyPostfix]
                private static void Postfix(PlainChatInputScreen __instance)
                {
                    TryApplyDynamicWidth(__instance);
                }
            }

            /// <summary>
            /// Computes and applies the desired chat width and text-box size.
            /// </summary>
            private static void TryApplyDynamicWidth(PlainChatInputScreen screen)
            {
                if (!FeatureEnabled(QoL_Settings.EnableWideChatInput))
                    return;

                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game?.Window == null || screen == null)
                        return;

                    int target = Math.Max(350, game.Window.ClientBounds.Width);
                    if (screen.Width != target)
                    {
                        screen.Width = target;

                        var tec = screen._textEditControl;
                        if (tec != null)
                            tec.Size = new Size(screen.Width - 20, tec.Size.Height);
                    }
                }
                catch (Exception ex)
                {
                    LogIfEnabled($"[PCIS_WP] Error: {ex}.");
                }
            }
        }
        #endregion

        #region HUD Console Opacity Floor (0.25 Opacity)

        /// <summary>
        /// Raises the console fade floor by a configurable bonus amount when enabled.
        /// </summary>
        [HarmonyPatch(typeof(ConsoleElement), "OnDraw",
            new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime), typeof(bool) })]
        internal static class ConsoleElement_OnDraw_VisibilityTranspiler
        {
            /// <summary>
            /// Adjusts console message visibility based on the standalone QoL config.
            /// </summary>
            private static float AdjustVisibility(float v)
            {
                if (!FeatureEnabled(QoL_Settings.EnableConsoleOpacityFloor))
                    return v;

                float bonus = QoL_Settings.ConsoleOpacityFloorBonus;
                return Math.Min(1f, v + bonus);
            }

            /// <summary>
            /// Routes visibility reads through a helper so the fade floor can be changed at runtime.
            /// </summary>
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);
                var messageType = AccessTools.Inner(typeof(ConsoleElement), "Message");
                var visGetter = AccessTools.PropertyGetter(messageType, "Visibility");
                var adjustMethod = AccessTools.Method(typeof(ConsoleElement_OnDraw_VisibilityTranspiler), nameof(AdjustVisibility));

                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i];
                    yield return ins;

                    if (ins.opcode == OpCodes.Callvirt && ins.operand is MethodInfo mi && mi == visGetter)
                        yield return new CodeInstruction(OpCodes.Call, adjustMethod);
                }
            }
        }
        #endregion

        #region Remove Character Limitations + Paste Handling

        /// <summary>
        /// Broadens text input acceptance and restores Ctrl+V paste support for TextEditControl when enabled.
        /// </summary>
        [HarmonyPatch(typeof(TextEditControl), "OnChar")]
        internal static class TextEditControl_OnChar_AllowAnyCharPlusPaste
        {
            /// <summary>
            /// Returns the widened input gate when enabled, or the original vanilla gate when disabled.
            /// </summary>
            private static bool AcceptInputCharacter(char c)
            {
                if (!FeatureEnabled(QoL_Settings.EnableAllowAnyCharPlusPaste))
                    return char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c);

                return !char.IsControl(c);
            }

            /// <summary>
            /// Handles Ctrl+V paste while filtering out raw control characters from the inserted text.
            /// </summary>
            [HarmonyPrefix]
            private static bool Prefix(TextEditControl __instance, char c)
            {
                if (!FeatureEnabled(QoL_Settings.EnableAllowAnyCharPlusPaste))
                    return true;

                if (c != '\u0016')
                    return true;

                try
                {
                    if (!System.Windows.Forms.Clipboard.ContainsText())
                        return false;

                    string clipboard = System.Windows.Forms.Clipboard.GetText();
                    if (string.IsNullOrEmpty(clipboard))
                        return false;

                    clipboard = clipboard.Replace("\r\n", " ")
                                         .Replace('\r', ' ')
                                         .Replace('\n', ' ')
                                         .Replace('\t', ' ');

                    clipboard = new string(clipboard.Where(ch => !char.IsControl(ch)).ToArray());
                    if (clipboard.Length == 0)
                        return false;

                    var textBuilder = new StringBuilder(__instance.Text ?? string.Empty);
                    int curPos = Math.Max(0, Math.Min(__instance.CursorPos, textBuilder.Length));

                    Rectangle textBounds = __instance.Frame.CenterRegion(__instance.ScreenBounds);

                    foreach (char ch in clipboard)
                    {
                        if (__instance.MaxChars >= 0 && textBuilder.Length >= __instance.MaxChars)
                            break;

                        var test = new StringBuilder(textBuilder.ToString());
                        test.Insert(curPos, ch);

                        Vector2 newSize = __instance.Font.MeasureString(test) * __instance.Scale;
                        if (newSize.X >= textBounds.Width)
                            break;

                        textBuilder.Insert(curPos, ch);
                        curPos++;
                    }

                    __instance.Text = textBuilder.ToString();
                    __instance.CursorPos = curPos;
                }
                catch (Exception ex)
                {
                    LogIfEnabled($"[TEC_OC_AACPP] Paste error: {ex}.");
                }

                return false;
            }

            /// <summary>
            /// Replaces the vanilla text-input character gate with a runtime helper.
            /// </summary>
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var isLetterOrDigit = AccessTools.Method(typeof(char), nameof(char.IsLetterOrDigit), new[] { typeof(char) });
                var isPunctuation = AccessTools.Method(typeof(char), nameof(char.IsPunctuation), new[] { typeof(char) });
                var isWhiteSpace = AccessTools.Method(typeof(char), nameof(char.IsWhiteSpace), new[] { typeof(char) });
                var acceptMethod = AccessTools.Method(typeof(TextEditControl_OnChar_AllowAnyCharPlusPaste), nameof(AcceptInputCharacter));

                foreach (var ins in instructions)
                {
                    if (ins.Calls(isLetterOrDigit) || ins.Calls(isPunctuation) || ins.Calls(isWhiteSpace))
                    {
                        yield return new CodeInstruction(OpCodes.Call, acceptMethod);
                        continue;
                    }

                    yield return ins;
                }
            }
        }
        #endregion

        #region Remove Max World Height

        /// <summary>
        /// Removes the vanilla upper world-height clamp from Player.OnUpdate when enabled.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnUpdate")]
        internal static class Patch_Player_OnUpdate_RemoveMaxWorldHeight
        {
            /// <summary>
            /// Returns either the vanilla ceiling or an effectively unlimited ceiling based on the QoL config.
            /// </summary>
            private static float GetCeilingY(float vanillaCeiling)
            {
                return FeatureEnabled(QoL_Settings.EnableRemoveMaxWorldHeight)
                    ? float.MaxValue
                    : vanillaCeiling;
            }

            /// <summary>
            /// Routes the player Y-ceiling literal through the standalone QoL config.
            /// </summary>
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var miGetCeiling = AccessTools.Method(typeof(Patch_Player_OnUpdate_RemoveMaxWorldHeight), nameof(GetCeilingY));

                foreach (var ins in instructions)
                {
                    if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f &&
                        (Math.Abs(f - 74f) < 0.0001f || Math.Abs(f - 64f) < 0.0001f))
                    {
                        yield return ins;
                        yield return new CodeInstruction(OpCodes.Call, miGetCeiling);
                        continue;
                    }

                    yield return ins;
                }
            }
        }
        #endregion

        #endregion
    }
}