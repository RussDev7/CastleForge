/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060   // Silence IDE0060.
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using HarmonyLib;                 // Harmony patching library.
using DNA.Input;
using DNA.Net;
using System;
using DNA;

using static ModLoader.LogSystem; // For Log(...).

namespace RegionProtect
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
        /// // ... each frame (via Harmony patch) -> if (WEHotkeys.ReloadPressedThisFrame()) { RegionProtectConfig.LoadApply(); ... }
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
        internal static class RPHotkeys
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
                Log($"[RProt] Reload hotkey set to \"{s}\".");
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
        /// Keeps the body small; heavy lifting should be inside RegionProtectConfig.LoadApply().
        /// </summary>
        [HarmonyPatch]
        static class Patch_Hotkey_ReloadConfig_RegionProtect
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI.
            /// </summary>
            static void Postfix(InGameHUD __instance)
            {
                if (!RPHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    RPConfig.LoadApply();
                    RegionProtectStore.LoadApply();

                    SendFeedback($"[RProt] Config hot-reloaded from \"{PathShortener.ShortenForLog(RPConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[RProt] Hot-reload failed: {ex.Message}.");
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

        #region Patch: AlterBlockMessage (Mine / Place)

        /// <summary>
        /// Patch: CastleMinerZGame._processAlterBlocksMessage(Message message)
        /// Summary: This is where AlterBlockMessage is applied to terrain via BlockTerrain.SetBlock(...).
        /// We intercept it so we can deny based on region whitelist, and broadcast a correction when denied.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processAlterBlocksMessage")]
        internal static class Patch_AlterBlockMessage_RegionProtect
        {
            [HarmonyPrefix]
            private static bool Prefix(CastleMinerZGame __instance, Message message)
            {
                // Basic guards.
                if (__instance == null || message == null) return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled) return true;

                // Recommend enforcing on host only (authoritative).
                if (cfg.EnforceHostOnly && (__instance.MyNetworkGamer == null || !__instance.MyNetworkGamer.IsHost))
                    return true;

                if (!(message is AlterBlockMessage abm))
                    return true;

                if (BlockTerrain.Instance == null)
                    return true;

                // Determine old/new.
                BlockTypeEnum oldBlock = BlockTerrain.Instance.GetBlockWithChanges(abm.BlockLocation);
                BlockTypeEnum newBlock = abm.BlockType;

                // No-op: allow the game to handle.
                if (oldBlock == newBlock)
                    return true;

                // Identify "dig" as block -> Empty.
                bool isDig = (newBlock == BlockTypeEnum.Empty && oldBlock != BlockTypeEnum.Empty);

                // If it's neither a dig nor a place/replace we care about, let it through.
                if (!isDig && !cfg.ProtectPlacing)
                    return true;

                string who = message.Sender?.Gamertag ?? "";

                var action = isDig ? RegionProtectCore.ProtectAction.Mine : RegionProtectCore.ProtectAction.Build;

                if (RegionProtectCore.ShouldDeny(who, abm.BlockLocation, action, out var reason))
                {
                    RegionProtectCore.HandleDenied(__instance, message, abm, oldBlock, reason, action);
                    return false; // Skip original SetBlock.
                }

                return true; // Allow original SetBlock.
            }
        }
        #endregion

        #region Patch: Explosives (RemoveBlocksMessage)

        /// <summary>
        /// Patch: Explosive.HandleRemoveBlocksMessage(RemoveBlocksMessage msg)
        /// Summary: Filters explosion block-removals against RegionProtect rules (host-authoritative).
        /// </summary>
        [HarmonyPatch(typeof(Explosive), "HandleRemoveBlocksMessage")]
        internal static class Explosive_HandleRemoveBlocksMessage_RegionProtect
        {
            [HarmonyPrefix]
            private static bool Prefix(RemoveBlocksMessage msg)
            {
                var g = CastleMinerZGame.Instance;
                if (g == null || msg == null) return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled) return true;

                // Explosions are "mining" (block -> Empty). If mining protection is off, do nothing.
                if (!cfg.ProtectMining) return true;

                // Host-authoritative enforcement (recommended).
                if (cfg.EnforceHostOnly && (g.MyNetworkGamer == null || !g.MyNetworkGamer.IsHost))
                    return true;

                if (BlockTerrain.Instance == null) return true;

                // Who caused this removal (RemoveBlocksMessage inherits from DNA.Net.Message).
                string who = msg.Sender?.Gamertag ?? "";

                bool notified = false;

                // We handle the whole message ourselves (allowed blocks removed, denied blocks restored).
                for (int i = 0; i < msg.NumBlocks; i++)
                {
                    IntVector3 p = msg.BlocksToRemove[i];

                    // Deny if this block lies inside spawn-protect or a protected region and player isn't whitelisted.
                    if (RegionProtectCore.ShouldDeny(who, p, RegionProtectCore.ProtectAction.Mine, out var reason))
                    {
                        // Capture the current block type BEFORE any removal (host is authoritative here).
                        BlockTypeEnum oldBlock = BlockTerrain.Instance.GetBlockWithChanges(p);

                        // Optional: Notify offender once per explosion packet.
                        if (!notified)
                        {
                            RegionProtectCore.NotifyDenied(g, msg.Sender, reason, RegionProtectCore.ProtectAction.Mine);
                            notified = true;
                        }

                        // Broadcast a correction so everyone restores it (clients may already have removed it).
                        if (g.MyNetworkGamer != null)
                            AlterBlockMessage.Send(g.MyNetworkGamer, p, oldBlock);

                        continue;
                    }

                    // Allowed removal.
                    BlockTerrain.Instance.SetBlock(p, BlockTypeEnum.Empty);

                    if (msg.DoDigEffects)
                    {
                        // Explosive.AddDigEffects expects world-space center.
                        Explosive.AddDigEffects(IntVector3.ToVector3(p) + new Vector3(0.5f));
                    }
                }

                return false; // Skip original HandleRemoveBlocksMessage
            }
        }
        #endregion

        #region Patch: Gun-Triggered TNT/C4 Shooter Attribution

        /// <summary>
        /// Patch: TracerManager.Tracer.Update(float dt)
        /// Summary:
        /// - Remembers which shooter actually hit a TNT/C4 block with a bullet/laser.
        /// - Vanilla later sends DetonateExplosiveMessage using MyNetworkGamer, which can be the host
        ///   instead of the actual shooter, so RegionProtect needs this hint to notify the correct player.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_TracerManager_Tracer_Update_TrackExplosiveShooter
        {
            private static MethodBase TargetMethod()
            {
                var tracerType = AccessTools.TypeByName("DNA.CastleMinerZ.TracerManager+Tracer");
                return AccessTools.Method(tracerType, "Update");
            }

            [HarmonyPostfix]
            private static void Postfix(object __instance)
            {
                try
                {
                    if (__instance == null || BlockTerrain.Instance == null)
                        return;

                    Type tracerType = __instance.GetType();

                    var shooterField = AccessTools.Field(tracerType, "ShooterID");
                    var tpField = AccessTools.Field(tracerType, "tp");

                    if (shooterField == null || tpField == null)
                        return;

                    byte shooterId = (byte)shooterField.GetValue(__instance);

                    if (!(tpField.GetValue(null) is DNA.CastleMinerZ.Utils.Trace.TraceProbe tp) || !tp._collides)
                        return;

                    BlockTypeEnum blockType = BlockTerrain.Instance.GetBlockWithChanges(tp._worldIndex);
                    if (blockType != BlockTypeEnum.TNT && blockType != BlockTypeEnum.C4)
                        return;

                    RememberExplosiveShooter(tp._worldIndex, shooterId);
                }
                catch (Exception ex)
                {
                    Log($"[RProt] Failed to track explosive shooter: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Patch: Explosives (DetonateExplosiveMessage)

        /// <summary>
        /// Patch: Explosive.HandleDetonateExplosiveMessage(DetonateExplosiveMessage msg)
        /// Summary: Prevents TNT/C4 from being consumed (set to Empty) inside protected regions when detonation is denied.
        /// </summary>
        [HarmonyPatch(typeof(Explosive), "HandleDetonateExplosiveMessage")]
        internal static class Explosive_HandleDetonateExplosiveMessage_RegionProtect
        {
            [HarmonyPrefix]
            private static bool Prefix(DetonateExplosiveMessage msg)
            {
                var g = CastleMinerZGame.Instance;
                if (g == null || msg == null) return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled) return true;

                // Detonation consumes the explosive block (TNT/C4) -> treat as "mining".
                if (!cfg.ProtectMining) return true;

                // Host-authoritative enforcement (recommended).
                if (cfg.EnforceHostOnly && (g.MyNetworkGamer == null || !g.MyNetworkGamer.IsHost))
                    return true;

                if (BlockTerrain.Instance == null) return true;

                NetworkGamer offender = ResolveExplosiveOffender(g, msg, out string who);
                IntVector3 p = msg.Location;

                if (RegionProtectCore.ShouldDeny(who, p, RegionProtectCore.ProtectAction.Mine, out var reason))
                {
                    // Optional notify (rate-limited), consistent with other explosion filtering.
                    RegionProtectCore.NotifyDenied(g, offender, reason, RegionProtectCore.ProtectAction.Mine);

                    if (cfg.LogDenied)
                        Log($"DENY {who} detonate at {p} type={msg.ExplosiveType} reason={reason.Kind} {reason.RegionName}.");

                    // Restore TNT/C4 for everyone (clients may already have removed it locally).
                    try
                    {
                        if (g.MyNetworkGamer != null)
                        {
                            BlockTypeEnum explosiveBlock =
                                (msg.ExplosiveType == ExplosiveTypes.TNT) ? BlockTypeEnum.TNT : BlockTypeEnum.C4;

                            AlterBlockMessage.Send(g.MyNetworkGamer, p, explosiveBlock);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[RProt] Failed to send detonate correction at {p}: {ex.Message}.");
                    }

                    return false; // Skip original handler (prevents removal + stops daisy-chain start here).
                }

                return true;
            }
        }
        #endregion

        #region Patch: Crate - Item Edits (ItemCrateMessage)

        /// <summary>
        /// Patch: CastleMinerZGame._processItemCrateMessage(Message message)
        /// Summary:
        /// - Intercepts per-slot crate inventory edits (put/take/clear).
        /// - Uses RegionProtectCore.ShouldDeny(...) to decide if the edit is allowed at that crate location.
        /// - If denied: Notify offender, keep host's authoritative slot value, and broadcast a corrective
        ///   ItemCrateMessage so clients revert any predicted/illegal change.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_Crate_ItemCrateMessage_RegionProtect
        {
            /// <summary>
            /// Prefix:
            /// - Snapshot the current slot BEFORE vanilla mutates it.
            /// - If denied, restore that snapshot and push it back out to clients.
            /// </summary>
            [HarmonyPatch(typeof(CastleMinerZGame), "_processItemCrateMessage")]
            [HarmonyPrefix]
            private static bool Prefix(CastleMinerZGame __instance, Message message)
            {
                // Guard: Patch must be safe even if something is null/missing.
                if (__instance == null || message == null) return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled) return true;

                // Per-feature toggle.
                if (!cfg.ProtectCrateItems) return true;

                // Host-authoritative enforcement (recommended).
                // If we're not the host, let vanilla run (we don't want clients fighting authority).
                if (cfg.EnforceHostOnly)
                {
                    if (__instance.MyNetworkGamer == null || !__instance.MyNetworkGamer.IsHost)
                        return true;
                }

                // Only handle crate item edits.
                if (!(message is ItemCrateMessage icm)) return true;
                if (__instance.CurrentWorld == null) return true;

                // Grab the crate (createIfMissing=true) so we can snapshot/restore slot state.
                // NOTE: Creating missing crates can be useful for resync, but also means "crate" is never null here.
                Crate crate = __instance.CurrentWorld.GetCrate(icm.Location, true);

                // Snapshot old slot BEFORE vanilla Apply() mutates it.
                // Safety: Crate inventory is fixed 32, but guard anyway.
                InventoryItem oldItem = null;
                if (crate != null && crate.Inventory != null &&
                    icm.Index >= 0 && icm.Index < crate.Inventory.Length)
                {
                    oldItem = crate.Inventory[icm.Index];
                }

                // Offender identity (may be empty if sender is missing).
                string who = message.Sender?.Gamertag ?? "";

                // Core policy check: Deny if inside spawn protection or a protected region and not whitelisted.
                if (RegionProtectCore.ShouldDeny(who, icm.Location, RegionProtectCore.ProtectAction.UseCrate, out var reason))
                {
                    // Notify offender (throttled by core).
                    RegionProtectCore.NotifyDenied(__instance, message.Sender, reason, RegionProtectCore.ProtectAction.UseCrate);

                    // Host state stays authoritative: keep the old slot.
                    // IMPORTANT: This prevents the server-side crate data from changing.
                    if (crate != null && crate.Inventory != null &&
                        icm.Index >= 0 && icm.Index < crate.Inventory.Length)
                    {
                        crate.Inventory[icm.Index] = oldItem;
                    }

                    // Broadcast correction: Push the authoritative item back into that slot.
                    // This is what forces clients to "undo" the illegal edit visually/state-wise.
                    ItemCrateMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, oldItem, crate, icm.Index);

                    return false; // Skip vanilla Apply()+forward of the illegal change.
                }

                // Allowed: Let vanilla apply and forward normally.
                return true;
            }
        }
        #endregion

        #region Patch: Crate - Removal (DestroyCrateMessage)

        /// <summary>
        /// Patch: CastleMinerZGame._processDestroyCrateMessage(Message message)
        /// Summary:
        /// - Intercepts crate destruction (mined/exploded crate removal).
        /// - If denied by RegionProtect rules, prevents the crate from being removed from CurrentWorld.Crates.
        /// - Optionally re-broadcasts the crate's authoritative contents (per-slot ItemCrateMessage) to resync clients.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_Crate_DestroyCrateMessage_RegionProtect
        {
            /// <summary>
            /// Prefix:
            /// - Decide whether crate destruction is allowed at this location for this sender.
            /// - If denied, keep crate + broadcast inventory state so clients recreate/restore it.
            /// </summary>
            [HarmonyPatch(typeof(CastleMinerZGame), "_processDestroyCrateMessage")]
            [HarmonyPrefix]
            private static bool Prefix(CastleMinerZGame __instance, Message message)
            {
                // Guard: Patch must be safe even if something is null/missing.
                if (__instance == null || message == null) return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled) return true;

                // Per-feature toggle.
                // if (!cfg.ProtectCrateMining) return true;

                // Host-authoritative enforcement (recommended).
                // If we're not the host, don't try to fight authoritative state.
                if (cfg.EnforceHostOnly)
                {
                    if (__instance.MyNetworkGamer == null || !__instance.MyNetworkGamer.IsHost)
                        return true;
                }

                // Only handle crate destroy messages.
                if (!(message is DestroyCrateMessage dcm)) return true;
                if (__instance.CurrentWorld == null)       return true;

                // Offender identity (may be empty if sender is missing).
                string who = message.Sender?.Gamertag ?? "";

                // Allowed -> Let vanilla remove crate.
                if (!RegionProtectCore.ShouldDeny(who, dcm.Location, RegionProtectCore.ProtectAction.BreakCrate, out var reason))
                    return true; // Allowed -> Let vanilla remove crate.

                // Denied -> Do NOT remove the crate object + resync contents.
                RegionProtectCore.NotifyDenied(__instance, message.Sender, reason, RegionProtectCore.ProtectAction.BreakCrate);

                // If the offender's client predicted "crate destroyed", you can force-recreate its contents by
                // broadcasting per-slot ItemCrateMessage state (this also re-creates the crate on clients).
                // Snapshot current host contents first (authoritative).
                Crate crate = __instance.CurrentWorld.GetCrate(dcm.Location, true);
                InventoryItem[] snap = (crate?.Inventory != null) ? (InventoryItem[])crate.Inventory.Clone() : null;

                // Re-broadcast each slot as authoritative truth.
                // This is a "resync hammer": It tells clients what each slot should contain right now.
                if (snap != null)
                {
                    for (int i = 0; i < snap.Length; i++)
                    {
                        ItemCrateMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, snap[i], crate, i);
                    }
                }

                return false; // Skip vanilla crate removal.
            }
        }
        #endregion

        #region Patch: Local Player Pre-Checks (Make Host Obey Region Rules)

        /// <summary>
        /// Patch: CrateScreen.OnPushed()
        /// Summary:
        /// - Prevents the local player from even opening a protected crate if they are not allowed.
        /// - This fixes the local/host bypass where crate UI actions never hit _processItemCrateMessage
        ///   because ItemCrateMessage has Echo=false.
        /// </summary>
        [HarmonyPatch(typeof(CrateScreen), nameof(CrateScreen.OnPushed))]
        internal static class Patch_CrateScreen_OnPushed_RegionProtect
        {
            [HarmonyPrefix]
            private static bool Prefix(CrateScreen __instance)
            {
                if (__instance == null || __instance.CurrentCrate == null)
                    return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled || !cfg.ProtectCrateItems)
                    return true;

                var game = CastleMinerZGame.Instance;
                var local = game?.MyNetworkGamer;

                // Match host-authoritative design.
                if (cfg.EnforceHostOnly && (local == null || !local.IsHost))
                    return true;

                string who = local?.Gamertag ?? "";

                if (!RegionProtectCore.ShouldDeny(
                        who,
                        __instance.CurrentCrate.Location,
                        RegionProtectCore.ProtectAction.UseCrate,
                        out var reason))
                {
                    return true;
                }

                RegionProtectCore.NotifyDenied(game, local, reason, RegionProtectCore.ProtectAction.UseCrate);

                // The screen was already pushed by vanilla. Pop it immediately.
                game?.GameScreen?._uiGroup?.PopScreen();

                return false;
            }
        }

        /// <summary>
        /// Patch: InGameHUD.Dig(InventoryItem tool, bool effective)
        /// Summary:
        /// - Stops the local player before vanilla runs its local dig side-effects.
        /// - This prevents local crate mining from ejecting contents, creating pickups, granting stats,
        ///   and then relying on an after-the-fact terrain restore.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "Dig")]
        internal static class Patch_InGameHUD_Dig_LocalPrecheck_RegionProtect
        {
            [HarmonyPrefix]
            private static bool Prefix(InGameHUD __instance, InventoryItem tool, bool effective)
            {
                if (__instance == null || __instance.LocalPlayer == null || __instance.ConstructionProbe == null)
                    return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled || !cfg.ProtectMining)
                    return true;

                var game = CastleMinerZGame.Instance;
                var local = game?.MyNetworkGamer;

                // Match host-authoritative design.
                if (cfg.EnforceHostOnly && (local == null || !local.IsHost))
                    return true;

                if (!__instance.ConstructionProbe._collides)
                    return true;

                IntVector3 p = __instance.ConstructionProbe._worldIndex;
                BlockTypeEnum blockType = InGameHUD.GetBlock(p);

                if (!BlockType.GetType(blockType).CanBeDug)
                    return true;

                // Mirrors vanilla IsValidDigTarget(...)
                if (blockType == BlockTypeEnum.TeleportStation &&
                    __instance.LocalPlayer.PlayerInventory.GetTeleportAtWorldIndex(p + Vector3.Zero) == null)
                {
                    return true;
                }

                string who = local?.Gamertag ?? "";

                if (!RegionProtectCore.ShouldDeny(
                        who,
                        p,
                        RegionProtectCore.ProtectAction.Mine,
                        out var reason))
                {
                    return true;
                }

                RegionProtectCore.NotifyDenied(game, local, reason, RegionProtectCore.ProtectAction.Mine);
                return false;
            }
        }

        /// <summary>
        /// Patch: Crate.EjectContents()
        /// Summary:
        /// - Prevents local crate contents from being dumped on the ground before the authoritative
        ///   destroy-crate deny/restore path runs.
        /// - Covers local dig/explosive/fireball code paths that call EjectContents() directly.
        /// </summary>
        [HarmonyPatch(typeof(Crate), nameof(Crate.EjectContents))]
        internal static class Patch_Crate_EjectContents_LocalPrecheck_RegionProtect
        {
            [HarmonyPrefix]
            private static bool Prefix(Crate __instance)
            {
                if (__instance == null)
                    return true;

                var cfg = RPConfig.Active;
                if (cfg == null || !cfg.Enabled || !cfg.ProtectCrateMining)
                    return true;

                var game = CastleMinerZGame.Instance;
                var local = game?.MyNetworkGamer;

                // Match host-authoritative design.
                if (cfg.EnforceHostOnly && (local == null || !local.IsHost))
                    return true;

                string who = local?.Gamertag ?? "";

                if (!RegionProtectCore.ShouldDeny(
                        who,
                        __instance.Location,
                        RegionProtectCore.ProtectAction.BreakCrate,
                        out var reason))
                {
                    return true;
                }

                RegionProtectCore.NotifyDenied(game, local, reason, RegionProtectCore.ProtectAction.BreakCrate);
                return false;
            }
        }
        #endregion

        #endregion

        #region Helpers

        #region Explosive Shooter Attribution Helpers

        /// <summary>
        /// Small hint record used to remember who actually shot a TNT/C4 block.
        /// This fixes vanilla's detonation sender attribution for gun-triggered explosives.
        /// </summary>
        private struct ExplosiveShooterHint
        {
            public byte ShooterId;
            public long TickMs;
        }

        private static readonly object _explosiveShooterHintsLock = new object();
        private static readonly Dictionary<IntVector3, ExplosiveShooterHint> _explosiveShooterHints =
            new Dictionary<IntVector3, ExplosiveShooterHint>();

        private const long ExplosiveShooterHintLifetimeMs = 3000;

        /// <summary>
        /// Remembers the real shooter for a recently hit TNT/C4 block.
        /// </summary>
        private static void RememberExplosiveShooter(IntVector3 location, byte shooterId)
        {
            long now = Environment.TickCount;

            lock (_explosiveShooterHintsLock)
            {
                _explosiveShooterHints[location] = new ExplosiveShooterHint
                {
                    ShooterId = shooterId,
                    TickMs    = now
                };

                // Cheap stale cleanup.
                if (_explosiveShooterHints.Count > 64)
                {
                    var stale = _explosiveShooterHints
                        .Where(kvp => now - kvp.Value.TickMs > ExplosiveShooterHintLifetimeMs)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in stale)
                        _explosiveShooterHints.Remove(key);
                }
            }
        }

        /// <summary>
        /// Resolves the actual offender for TNT/C4 detonation:
        /// - For gun-triggered hits, prefer the cached tracer shooter.
        /// - Otherwise fall back to the message sender (works for normal fuse lighting).
        /// </summary>
        private static NetworkGamer ResolveExplosiveOffender(CastleMinerZGame game, DetonateExplosiveMessage msg, out string who)
        {
            NetworkGamer offender = msg?.Sender;
            who = offender?.Gamertag ?? "";

            if (game == null || msg == null || !msg.OriginalExplosion)
                return offender;

            long now       = Environment.TickCount;
            byte shooterId = 0;
            bool foundHint = false;

            lock (_explosiveShooterHintsLock)
            {
                if (_explosiveShooterHints.TryGetValue(msg.Location, out var hint))
                {
                    if (now - hint.TickMs <= ExplosiveShooterHintLifetimeMs)
                    {
                        shooterId = hint.ShooterId;
                        foundHint = true;
                    }

                    // One-shot consume so we do not reuse stale attribution.
                    _explosiveShooterHints.Remove(msg.Location);
                }
            }

            if (foundHint)
            {
                var resolved = game.GetGamerFromID(shooterId);
                if (resolved != null)
                {
                    offender = resolved;
                    who = resolved.Gamertag ?? who;
                }
            }

            return offender;
        }
        #endregion

        #endregion
    }
}