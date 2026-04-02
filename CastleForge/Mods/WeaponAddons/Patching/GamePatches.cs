/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using HarmonyLib;                       // Harmony patching library.
using DNA.Audio;
using DNA.Input;
using System;
using DNA;

using static ModLoader.LogSystem;       // For Log(...).

namespace WeaponAddons
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
        /// WAHotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (WAHotkeys.ReloadPressedThisFrame()) { WEConfig.LoadApply(); ... }
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
        internal static class WAHotkeys
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
                Log($"[WAddns] Reload hotkey set to \"{s}\".");
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
        static class Patch_Hotkey_ReloadConfig_WeaponAddons
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI.
            /// </summary>
            static void Postfix(InGameHUD __instance)
            {
                if (!WAHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // 1) Reload config + binding.
                    WeaponAddonConfig.LoadApply();

                    // 2) Re-scan packs + re-apply runtime overrides (stats/models/new-ids).
                    WeaponAddonManager.LoadApply();

                    // 3) Ensure model swapper is installed (safe/idempotent).
                    WeaponAddonEntitySwapper.ApplyAfterContent();

                    SendFeedback($"[WAddns] Config hot-reloaded from \"{PathShortener.ShortenForLog(WeaponAddonConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendFeedback($"[WAddns] Hot-reload failed: {ex.Message}.");
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

        #region Startup / Content Load

        /// <summary>
        /// Applies WeaponAddons once the game finishes content loading.
        /// Summary:
        /// - LoadApply() parses packs + registers synthetic ids + applies stats/models/icons
        /// - EntitySwapper patches CreateEntity() so held/pickup models can be swapped at runtime
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "SecondaryLoad")]
        private static class Patch_LoadWeaponAddons_AfterSecondaryLoad
        {
            [HarmonyPostfix]
            static void Postfix()
            {
                WeaponAddonManager.LoadApply();
                WeaponAddonEntitySwapper.ApplyAfterContent();
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// SYNTHETIC NETWORK SAFETY (PICKUPS + CARRIED ITEM VISIBILITY)
        /// =====================================================================================
        ///
        /// Summary:
        /// - Synthetic items (runtime IDs) must NEVER be broadcast to vanilla clients.
        /// - For pickups:
        ///   • Intercept CreatePickup/ConsumePickup/RequestPickup so synthetic items are local-only.
        ///   • Replicate the visible/cosmetic behavior locally (PickupEntity + FlyingPickupEntity).
        /// - For held item visibility:
        ///   • Broadcast the base SLOT_ID to peers (so they see a real item in-hand).
        ///   • Locally "de-remap" back to the synthetic ID so the local player sees the custom weapon.
        ///
        /// Notes:
        /// - These hooks intentionally fail-open for vanilla items.
        /// - All paths are best-effort (null-checked) to avoid impacting gameplay.
        /// =====================================================================================
        /// </summary>

        #region Synthetic Pickup Network Filters (Local-Only)

        /// <summary>
        /// CreatePickupMessage.Send interception for synthetic items.
        /// Summary:
        /// - If the item ID is synthetic, suppress the network message.
        /// - Spawn an equivalent PickupEntity locally (no replication).
        /// </summary>
        [HarmonyPatch(typeof(CreatePickupMessage), nameof(CreatePickupMessage.Send))]
        private static class Patch_CreatePickup_Send_Synthetic
        {
            private static bool Prefix(
                LocalNetworkGamer from,
                Vector3 pos,
                Vector3 vec,
                int pickupID,
                InventoryItem item,
                bool dropped,
                bool displayOnPickup)
            {
                var cls = item?.ItemClass;
                if (cls == null) return true;

                // Only intercept synthetic IDs.
                if (!WeaponAddonItemInjector.IsSynthetic(cls.ID))
                    return true;

                // Spawn locally only (no network replication).
                TrySpawnLocalPickup(from, pos, vec, pickupID, item, dropped, displayOnPickup);
                return false;
            }

            /// <summary>
            /// Replicates PickupManager.HandleCreatePickupMessage without going through the message system.
            /// Summary:
            /// - Creates a PickupEntity and inserts it into PickupManager + Scene.
            /// - Mimics vanilla position/velocity offsets used by CreatePickup.
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
                if (pm == null || from == null) return;

                // Vanilla uses msg.Sender.Id as SpawnerID.
                int spawnerID = (int)from.Id;

                var entity = new PickupEntity(item, pickupID, spawnerID, dropped, pos);
                entity.Item.DisplayOnPickup = displayOnPickup;
                entity.PlayerPhysics.LocalVelocity = vec;
                entity.LocalPosition = pos + new Vector3(0.5f, 0.5f, 0.5f);

                pm.Pickups.Add(entity);

                var scene = pm.Scene;
                if (scene != null && scene.Children != null)
                    scene.Children.Add(entity);
            }
        }

        /// <summary>
        /// ConsumePickupMessage.Send interception for synthetic items.
        /// Summary:
        /// - Host-only: resolves consume locally and does NOT broadcast the consume message.
        /// - Removes pickup entity, grants item to the local owner, and spawns a FlyingPickupEntity for cosmetics.
        /// </summary>
        [HarmonyPatch(typeof(ConsumePickupMessage), nameof(ConsumePickupMessage.Send))]
        private static class Patch_ConsumePickup_Send_Synthetic
        {
            private static bool Prefix(
                LocalNetworkGamer from,
                byte pickerupper,
                Vector3 pos,
                int spawnerID,
                int pickupID,
                InventoryItem item,
                bool displayOnPickup)
            {
                var cls = item?.ItemClass;
                if (cls == null) return true;

                if (!WeaponAddonItemInjector.IsSynthetic(cls.ID))
                    return true;

                // Synthetic consume: Resolve host-local, do NOT broadcast.
                var pm = PickupManager.Instance;
                var game = CastleMinerZGame.Instance;
                if (pm == null || game == null)
                    return false;

                // If we're not host, we shouldn't be broadcasting consumes anyway.
                if (!game.IsGameHost)
                    return false;

                // 1) Find the Player who picked it up (from pickerupper id).
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

                // 2) Remove pickup entity from world (and capture best position).
                PickupEntity pe = null;
                for (int i = 0; i < pm.Pickups.Count; i++)
                {
                    var cand = pm.Pickups[i];
                    if (cand.PickupID == pickupID && cand.SpawnerID == spawnerID)
                    {
                        pe = cand;
                        break;
                    }
                }

                Vector3 fxPos = (pe != null) ? pe.GetActualGraphicPos() : pos;
                if (pe != null)
                    pm.RemovePickup(pe);

                // 3) If this machine owns the picking player, grant the item.
                if (player != null && player == game.LocalPlayer && game.GameScreen?.HUD != null)
                {
                    game.GameScreen.HUD.PlayerInventory.AddInventoryItem(item, displayOnPickup);
                    SoundManager.Instance.PlayInstance("pickupitem");
                }

                // 4) Optional cosmetic flying pickup.
                if (player != null)
                {
                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                        scene.Children.Add(new FlyingPickupEntity(item, player, fxPos));
                }

                return false; // Block original Send().
            }
        }

        /// <summary>
        /// RequestPickupMessage.Send interception for synthetic items (client-side).
        /// Summary:
        /// - For NON-host clients: if the pickup is synthetic, grant locally and remove pickup locally.
        /// - Skips sending the pickup request to host (prevents synthetic IDs from being involved in net flow).
        /// </summary>
        [HarmonyPatch(typeof(RequestPickupMessage), nameof(RequestPickupMessage.Send))]
        private static class Patch_RequestPickup_Send_Synthetic
        {
            private static bool Prefix(LocalNetworkGamer from, int spawnerID, int pickupID)
            {
                if (from == null) return true;

                // Host keeps normal request -> consume flow (host-side consume patch handles synthetic).
                if (from.IsHost) return true;

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

                if (pickup?.Item?.ItemClass == null)
                    return true;

                var id = pickup.Item.ItemClass.ID;

                // Vanilla item? Let normal networking happen.
                if (!WeaponAddonItemInjector.IsSynthetic(id))
                    return true;

                // Synthetic pickup on NON-HOST client:
                // - Grant locally.
                // - Remove pickup.
                // - Skip sending request to host.
                if (game.LocalPlayer != null && game.GameScreen?.HUD != null)
                {
                    game.GameScreen.HUD.PlayerInventory.AddInventoryItem(
                        pickup.Item,
                        pickup.Item.DisplayOnPickup);

                    SoundManager.Instance.PlayInstance("pickupitem");

                    var scene = pm.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        Vector3 fxPos = pickup.GetActualGraphicPos();
                        scene.Children.Add(new FlyingPickupEntity(pickup.Item, game.LocalPlayer, fxPos));
                    }
                }

                pm.RemovePickup(pickup);
                return false;
            }
        }
        #endregion

        #region Carried Item Network Remap (Show SLOT_ID on Peers)

        /// <summary>
        /// ChangeCarriedItemMessage.Send remap for synthetic items.
        /// Summary:
        /// - When networked, broadcast the base SLOT_ID for synthetic items.
        /// - Ensures peers can resolve InventoryItem.GetClass(id) and render a held item.
        /// </summary>
        [HarmonyPatch(typeof(ChangeCarriedItemMessage), nameof(ChangeCarriedItemMessage.Send))]
        private static class Patch_ChangeCarriedItem_Send_SyntheticRemap
        {
            private static void Prefix(LocalNetworkGamer from, ref InventoryItemIDs id)
            {
                if (!IsNetworked(from)) return;

                // If we're holding a synthetic ID, broadcast the base SLOT_ID instead.
                if (WeaponAddonItemInjector.TryGetBaseId(id, out var baseId))
                    id = baseId;
            }
        }

        /// <summary>
        /// Local-only de-remap for carried item visuals.
        /// Summary:
        /// - If the local player is actually holding a synthetic item whose base matches the net message,
        ///   force PutItemInHand(synthetic) so local visuals show the custom weapon.
        /// - Peers still see the base SLOT_ID (vanilla-safe).
        /// </summary>
        [HarmonyPatch(typeof(Player), "ProcessChangeCarriedItemMessage")]
        private static class Patch_Player_ProcessChangeCarriedItem_LocalUseSynthetic
        {
            private static bool Prefix(Player __instance, object message)
            {
                try
                {
                    if (__instance == null || message == null) return true;
                    if (!__instance.IsLocal) return true;

                    if (!(message is ChangeCarriedItemMessage ccim)) return true;

                    // Local player's actually equipped item (synthetic if applicable).
                    var held = CastleMinerZGame.Instance?.GameScreen?.HUD?.ActiveInventoryItem;
                    var heldId = held?.ItemClass?.ID;
                    if (heldId == null) return true;

                    // If heldId is synthetic and maps to the base ID in the message, show synthetic locally.
                    if (WeaponAddonItemInjector.TryGetBaseId(heldId.Value, out var baseId) && baseId == ccim.ItemID)
                    {
                        __instance.PutItemInHand(heldId.Value);
                        return false; // Skip vanilla handler.
                    }

                    return true;
                }
                catch { return true; }
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// VANILLA-SAFE NETWORK REMAPS (FIRE MESSAGES)
        /// =====================================================================================
        ///
        /// Summary:
        /// - When a session is "networked" (more than 1 gamer), synthetic WeaponAddons item IDs
        ///   must not be sent over the wire to vanilla clients.
        /// - These prefixes remap the outgoing ItemID/WeaponType back to the base SLOT_ID.
        ///
        /// Result:
        /// - Other players always receive a vanilla-safe ID.
        /// - Client-side visuals for the local shooter can be restored separately via local-only
        ///   de-remap patches (so modded clients still see their custom ammo/FX).
        ///
        /// Notes:
        /// - Harmony argument binding can be sensitive to parameter names.
        ///   These prefixes intentionally match the target method param names:
        ///     • GunshotMessage.Send(..., InventoryItemIDs item, ...)
        ///     • ShotgunShotMessage.Send(..., InventoryItemIDs item, ...)
        ///     • FireRocketMessage.Send(..., InventoryItemIDs weaponType, ...)
        /// =====================================================================================
        /// </summary>

        #region Vanilla-Safe Fire Messages (Synthetic ItemID -> Base ItemID)

        /// <summary>
        /// Returns true if this send is occurring in a multiplayer session.
        /// Summary: Used to avoid unnecessary remaps in solo play.
        /// </summary>
        private static bool IsNetworked(LocalNetworkGamer g)
        {
            try
            {
                var s = g?.Session;
                if (s == null) return false;
                return s.AllGamers != null && s.AllGamers.Count > 1;
            }
            catch { return false; }
        }

        /// <summary>
        /// GunshotMessage.Send remap:
        /// Summary: Synthetic ItemID -> base SLOT_ID for vanilla-safe networking.
        /// </summary>
        [HarmonyPatch(typeof(GunshotMessage), nameof(GunshotMessage.Send))]
        private static class Patch_Gunshot_Send_SyntheticRemap
        {
            private static void Prefix(LocalNetworkGamer from, ref InventoryItemIDs item)
            {
                if (!IsNetworked(from)) return;
                if (WeaponAddonItemInjector.TryGetBaseId(item, out var baseId))
                    item = baseId;
            }
        }

        /// <summary>
        /// ShotgunShotMessage.Send remap:
        /// Summary: Synthetic ItemID -> base SLOT_ID for vanilla-safe networking.
        /// </summary>
        [HarmonyPatch(typeof(ShotgunShotMessage), nameof(ShotgunShotMessage.Send))]
        private static class Patch_ShotgunShot_Send_SyntheticRemap
        {
            private static void Prefix(LocalNetworkGamer from, ref InventoryItemIDs item)
            {
                if (!IsNetworked(from)) return;
                if (WeaponAddonItemInjector.TryGetBaseId(item, out var baseId))
                    item = baseId;
            }
        }

        /// <summary>
        /// FireRocketMessage.Send remap:
        /// Summary: Synthetic WeaponType -> base SLOT_ID for vanilla-safe networking.
        /// </summary>
        [HarmonyPatch(typeof(FireRocketMessage), nameof(FireRocketMessage.Send))]
        private static class Patch_FireRocket_Send_SyntheticRemap
        {
            private static void Prefix(LocalNetworkGamer from, ref InventoryItemIDs weaponType)
            {
                if (!IsNetworked(from)) return;
                if (WeaponAddonItemInjector.TryGetBaseId(weaponType, out var baseId))
                    weaponType = baseId;
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// LOCAL VISUAL DE-REMAP (USE SYNTHETIC ID ON LOCAL SHOOTER ONLY)
        /// =====================================================================================
        ///
        /// Summary:
        /// - In multiplayer, outgoing fire messages are remapped to base SLOT_ID to keep vanilla peers safe.
        /// - That means the local client ALSO receives/handles messages containing the base ID.
        /// - These patches "de-remap" ONLY for the local shooter so their visuals/sounds use the
        ///   real equipped synthetic ID (custom ammo, tracer/laser behavior, custom UseSound, etc.).
        ///
        /// Design:
        /// - Peers: stay vanilla-safe (base ID only).
        /// - Local player: if current equipped item is synthetic AND maps to the base ID in the message,
        ///   run the original effect/spawn logic using the synthetic ID instead.
        ///
        /// Notes:
        /// - These patches are best-effort and fail-open to vanilla behavior.
        /// - We intentionally do not change network payloads or game-state authority.
        /// =====================================================================================
        /// </summary>

        #region Local Visual De-Remap (Use Synthetic ID on Local Shooter Only)

        /// <summary>
        /// Player.ProcessGunshotMessage de-remap (local-only).
        ///
        /// Summary:
        /// - If the local player is holding a synthetic weapon whose base matches the message ItemID:
        ///     • Spawn laser/tracer visuals using the synthetic ID.
        ///     • Play UseSound (or fallback GunShot3) using the synthetic class.
        ///     • Trigger muzzle flash.
        /// - Otherwise, let vanilla run unchanged.
        ///
        /// Notes:
        /// - Uses GetGunTipPosition via reflection (best-effort) to match vanilla shot origin.
        /// - Peers are not affected: only runs for __instance.IsLocal.
        /// </summary>
        [HarmonyPatch]
        private static class Patch_Player_ProcessGunshotMessage_LocalUseSynthetic
        {
            private static readonly MethodInfo _miGetGunTipPos =
                AccessTools.Method(typeof(Player), "GetGunTipPosition");

            static MethodBase TargetMethod()
                => AccessTools.Method(typeof(Player), "ProcessGunshotMessage");

            static bool Prefix(Player __instance, object message)
            {
                try
                {
                    if (__instance == null || message == null) return true;

                    // Only for the local shooter. Remote players should stay vanilla-safe.
                    if (!__instance.IsLocal) return true;

                    if (!(message is GunshotMessage gsm)) return true;

                    // Message contains the network-safe (base) ItemID.
                    var baseIdFromMsg = gsm.ItemID;

                    // Current equipped item on this client (synthetic if applicable).
                    var heldId = CastleMinerZGame.Instance?.GameScreen?.HUD?.ActiveInventoryItem?.ItemClass?.ID;
                    if (heldId == null) return true;

                    // Only override if heldId is synthetic and maps back to the baseId in the message.
                    if (!WeaponAddonItemInjector.TryGetBaseId(heldId.Value, out var mappedBase) || mappedBase != baseIdFromMsg)
                        return true;

                    // -----------------------------------------
                    // Re-run the original logic, but use heldId
                    // for visuals/sounds (local-only).
                    // -----------------------------------------
                    var effectiveId = heldId.Value;
                    var invClass = InventoryItem.GetClass(effectiveId);

                    if (invClass is LaserGunInventoryItemClass)
                    {
                        var scene = __instance.Scene;
                        if (scene != null)
                        {
                            Vector3 tip = GetGunTip(__instance);
                            var shot = BlasterShot.Create(tip, gsm.Direction, effectiveId, __instance.Gamer.Id);
                            scene.Children.Add(shot);
                        }
                    }
                    else
                    {
                        TracerManager.Instance?.AddTracer(__instance.FPSCamera.WorldPosition, gsm.Direction, effectiveId, __instance.Gamer.Id);
                    }

                    if (SoundManager.Instance != null)
                    {
                        if (invClass == null || invClass.UseSound == null)
                            SoundManager.Instance.PlayInstance("GunShot3", __instance.SoundEmitter);
                        else
                            SoundManager.Instance.PlayInstance(invClass.UseSound, __instance.SoundEmitter);
                    }

                    if (__instance.RightHand.Children.Count > 0 && __instance.RightHand.Children[0] is GunEntity entity)
                    {
                        entity.ShowMuzzleFlash();
                    }

                    return false; // Handled (skip vanilla).
                }
                catch
                {
                    return true; // Fail open.
                }
            }

            /// <summary>
            /// Best-effort shot origin resolver used for local laser/tracer spawn.
            /// Summary: Calls Player.GetGunTipPosition() when available, otherwise falls back to camera position.
            /// </summary>
            private static Vector3 GetGunTip(Player p)
            {
                try
                {
                    if (_miGetGunTipPos != null)
                        return (Vector3)_miGetGunTipPos.Invoke(p, null);
                }
                catch { }
                return p.FPSCamera.WorldPosition;
            }
        }

        /// <summary>
        /// Player.ProcessShotgunShotMessage de-remap (local-only).
        ///
        /// Summary:
        /// - Same idea as ProcessGunshotMessage, but for shotgun/laser-shotgun.
        /// - If equipped synthetic weapon maps to the message base ItemID:
        ///     • Spawn 5x laser shots OR 5x tracers using the synthetic ID.
        ///     • Play UseSound (or fallback GunShot3).
        ///     • Trigger muzzle flash.
        /// - Otherwise, let vanilla run.
        ///
        /// Notes:
        /// - Only affects __instance.IsLocal.
        /// - Uses GetGunTipPosition via reflection (best-effort).
        /// </summary>
        [HarmonyPatch]
        private static class Patch_Player_ProcessShotgunShotMessage_LocalUseSynthetic
        {
            private static readonly MethodInfo _miGetGunTipPos =
                AccessTools.Method(typeof(Player), "GetGunTipPosition");

            static MethodBase TargetMethod()
                => AccessTools.Method(typeof(Player), "ProcessShotgunShotMessage");

            static bool Prefix(Player __instance, object message)
            {
                try
                {
                    if (__instance == null || message == null) return true;
                    if (!__instance.IsLocal) return true;

                    if (!(message is ShotgunShotMessage gsm)) return true;

                    var baseIdFromMsg = gsm.ItemID;

                    var heldId = CastleMinerZGame.Instance?.GameScreen?.HUD?.ActiveInventoryItem?.ItemClass?.ID;
                    if (heldId == null) return true;

                    if (!WeaponAddonItemInjector.TryGetBaseId(heldId.Value, out var mappedBase) || mappedBase != baseIdFromMsg)
                        return true;

                    var effectiveId = heldId.Value;
                    var invClass = InventoryItem.GetClass(effectiveId);

                    if (invClass is LaserGunInventoryItemClass)
                    {
                        var scene = __instance.Scene;
                        if (scene != null)
                        {
                            Vector3 tip = GetGunTip(__instance);
                            for (int i = 0; i < 5; i++)
                            {
                                var shot = BlasterShot.Create(tip, gsm.Directions[i], effectiveId, __instance.Gamer.Id);
                                scene.Children.Add(shot);
                            }
                        }
                    }
                    else if (TracerManager.Instance != null)
                    {
                        for (int i = 0; i < 5; i++)
                            TracerManager.Instance.AddTracer(__instance.FPSCamera.WorldPosition, gsm.Directions[i], effectiveId, __instance.Gamer.Id);
                    }

                    if (SoundManager.Instance != null)
                    {
                        if (invClass == null || invClass.UseSound == null)
                            SoundManager.Instance.PlayInstance("GunShot3", __instance.SoundEmitter);
                        else
                            SoundManager.Instance.PlayInstance(invClass.UseSound, __instance.SoundEmitter);
                    }

                    if (__instance.RightHand.Children.Count > 0 && __instance.RightHand.Children[0] is GunEntity entity)
                    {
                        entity.ShowMuzzleFlash();
                    }

                    return false; // Handled (skip vanilla).
                }
                catch
                {
                    return true; // Fail open.
                }
            }

            /// <summary>
            /// Best-effort shot origin resolver used for local laser/tracer spawn.
            /// Summary: Calls Player.GetGunTipPosition() when available, otherwise falls back to camera position.
            /// </summary>
            private static Vector3 GetGunTip(Player p)
            {
                try
                {
                    if (_miGetGunTipPos != null)
                        return (Vector3)_miGetGunTipPos.Invoke(p, null);
                }
                catch { }
                return p.FPSCamera.WorldPosition;
            }
        }

        /// <summary>
        /// Player.ProcessFireRocketMessage de-remap (local-only).
        ///
        /// Summary:
        /// - Keeps rocket behavior network-safe by spawning RocketEntity using the BASE WeaponType from the message.
        /// - Improves local feel by choosing a launch sound based on the equipped synthetic weapon:
        ///     • If held synthetic maps to frm.WeaponType, use that class's UseSound if present.
        ///     • Otherwise, fall back to vanilla "RPGLaunch".
        ///
        /// Notes:
        /// - This avoids changing rocket simulation/explosion authority; it is primarily a local audio upgrade.
        /// - Only affects __instance.IsLocal.
        /// </summary>
        [HarmonyPatch(typeof(Player), "ProcessFireRocketMessage")]
        private static class Patch_Player_ProcessFireRocketMessage_LocalUseSynthetic
        {
            private static bool Prefix(Player __instance, object message)
            {
                try
                {
                    if (__instance == null || message == null) return true;

                    // Only override for the local shooter instance.
                    // Remote players on this machine should stay vanilla.
                    if (!__instance.IsLocal) return true;

                    // Match vanilla behavior: no scene -> nothing to do.
                    if (__instance.Scene == null) return false;

                    if (!(message is FireRocketMessage frm)) return true;

                    // Spawn the rocket using the BASE (network-safe) weapon type.
                    var re = new RocketEntity(frm.Position, frm.Direction, frm.WeaponType, frm.Guided, __instance.IsLocal);
                    __instance.Scene.Children.Add(re);

                    // Choose launch sound:
                    // - If our equipped item is a synthetic clone of frm.WeaponType, use that class's UseSound.
                    // - Otherwise, fall back to vanilla "RPGLaunch".
                    string launch = "RPGLaunch";

                    var held = CastleMinerZGame.Instance?.GameScreen?.HUD?.ActiveInventoryItem; // HUD accessor.
                    var heldId = held?.ItemClass?.ID;

                    if (heldId != null && WeaponAddonItemInjector.TryGetBaseId(heldId.Value, out var baseId) && baseId == frm.WeaponType)
                    {
                        var cls = InventoryItem.GetClass(heldId.Value);
                        if (cls != null && !string.IsNullOrWhiteSpace(cls.UseSound))
                            launch = cls.UseSound;
                    }

                    SoundManager.Instance?.PlayInstance(launch, __instance.SoundEmitter);

                    // We fully handled it (otherwise vanilla would also play RPGLaunch).
                    return false;
                }
                catch
                {
                    return true; // Fail open.
                }
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// UI ICON HANDLING
        /// =====================================================================================
        ///
        /// Summary:
        /// - If WeaponAddonManager has a cached icon for the current item id, draw it and skip vanilla.
        /// - Otherwise, for synthetic items, temporarily remap ID -> base ID so vanilla Draw2D uses
        ///   the base (slot) icon cell.
        ///
        /// Notes:
        /// - TargetMethod() is defensive: some builds expose InventoryItem+InventoryItemClass,
        ///   others expose InventoryItemClass.
        /// - We use reflection accessors for ID so we don't care which type is chosen.
        /// =====================================================================================
        /// </summary>

        #region Item Icon Override (PNG / Render) + Synthetic Fallback

        [HarmonyPatch]
        private static class Patch_ItemClass_Draw2D_IconOverride_ThenSyntheticFallback
        {
            static MethodBase TargetMethod()
            {
                // Some builds have a nested type:
                //   DNA.CastleMinerZ.Inventory.InventoryItem+InventoryItemClass
                // Others may expose a flat InventoryItemClass.
                var t =
                    AccessTools.TypeByName("DNA.CastleMinerZ.Inventory.InventoryItem+InventoryItemClass") ??
                    AccessTools.TypeByName("DNA.CastleMinerZ.Inventory.InventoryItemClass");

                return t == null
                    ? null
                    : AccessTools.Method(t, "Draw2D", new[] { typeof(SpriteBatch), typeof(Rectangle), typeof(Color) });
            }

            private static bool Prefix(object __instance, SpriteBatch batch, Rectangle destRect, Color color, ref int __state)
            {
                __state = int.MinValue;
                if (__instance == null || batch == null)
                    return true;

                if (!TryGetId(__instance, out var id))
                    return true;

                // 1) Optional icon override (PNG or rendered model icon).
                if (WeaponAddonManager.TryGetIconForItem(id, out var tex) && tex != null)
                {
                    DrawFit(batch, tex, destRect, color);
                    return false; // Skip vanilla Draw2D.
                }

                // 2) Existing behavior: Synthetic -> base ID so vanilla uses SLOT_ID icon cell.
                if (WeaponAddonItemInjector.TryGetBaseId(id, out var baseId))
                {
                    __state = (int)id;
                    TrySetId(__instance, baseId);
                }

                return true;
            }

            private static void Postfix(object __instance, int __state)
            {
                if (__instance == null) return;

                if (__state != int.MinValue)
                    TrySetId(__instance, (InventoryItemIDs)__state);
            }

            // ------------------------------------------------------------
            // Helpers (reflection-based so we don't care which type name won).
            // ------------------------------------------------------------
            private static bool TryGetId(object inst, out InventoryItemIDs id)
            {
                id = default;
                try
                {
                    var t = inst.GetType();

                    var p = AccessTools.Property(t, "ID");
                    if (p != null && p.PropertyType == typeof(InventoryItemIDs))
                    {
                        id = (InventoryItemIDs)p.GetValue(inst, null);
                        return true;
                    }

                    var f = AccessTools.Field(t, "ID");
                    if (f != null && f.FieldType == typeof(InventoryItemIDs))
                    {
                        id = (InventoryItemIDs)f.GetValue(inst);
                        return true;
                    }
                }
                catch { }
                return false;
            }

            private static void TrySetId(object inst, InventoryItemIDs id)
            {
                try
                {
                    var t = inst.GetType();

                    var p = AccessTools.Property(t, "ID");
                    if (p != null && p.CanWrite && p.PropertyType == typeof(InventoryItemIDs))
                    {
                        p.SetValue(inst, id, null);
                        return;
                    }

                    var f = AccessTools.Field(t, "ID");
                    if (f != null && f.FieldType == typeof(InventoryItemIDs))
                    {
                        f.SetValue(inst, id);
                        return;
                    }
                }
                catch { }
            }

            /// <summary>
            /// Draws the provided texture into the destination rect while preserving aspect ratio.
            /// Used for PNG icons and RenderTarget icons (model renders).
            /// </summary>
            private static void DrawFit(SpriteBatch sb, Texture2D tex, Rectangle dst, Color tint)
            {
                int tw = tex.Width;
                int th = tex.Height;
                if (tw <= 0 || th <= 0)
                {
                    sb.Draw(tex, dst, tint);
                    return;
                }

                float sx = dst.Width / (float)tw;
                float sy = dst.Height / (float)th;
                float s = Math.Min(sx, sy);

                int w = Math.Max(1, (int)(tw * s));
                int h = Math.Max(1, (int)(th * s));

                int x = dst.X + (dst.Width - w) / 2;
                int y = dst.Y + (dst.Height - h) / 2;

                sb.Draw(tex, new Rectangle(x, y, w, h), null, tint);
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// CRAFTING UI (RECIPE LAYOUT PATCH)
        /// =====================================================================================
        ///
        /// Summary:
        /// - The vanilla Tier2Item constructor builds a vertical list of recipe icon locations.
        /// - Vanilla only places items "to the right" when multiple recipes share the same Result ID
        ///   (duplicate-result "alternate recipe lane" behavior).
        /// - WeaponAddons extends this by allowing arbitrary RIGHT_OF placement rules for injected recipes.
        ///
        /// How it works:
        /// - WeaponAddonRecipeInjector registers placement metadata per Recipe instance:
        ///     WeaponAddonRecipePlacement.SetRightOf(recipe, anchorId)
        /// - This patch runs after Tier2Item finishes constructing its internal recipe list.
        /// - We then rebuild the private _itemLocations[] array via WeaponAddonRecipePlacement.RelayoutTier2Item(...).
        ///
        /// Notes:
        /// - This does NOT change recipe logic, crafting requirements, or discovery.
        /// - This is UI-only placement; safe if it fails (best-effort, try/catch).
        /// - TargetMethod() is reflection-based so it works even if the UI types are in a different assembly build.
        /// =====================================================================================
        /// </summary>

        #region Crafting UI (Recipe Layout)

        [HarmonyPatch]
        private static class Patch_Tier2Item_Ctor_CustomRecipeLayout
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("DNA.CastleMinerZ.UI.Tier2Item");
                if (t == null) return null;

                // .ctor(string title, Recipe.RecipeTypes recipeType, Tier1Item tier1Item, CraftingScreen craftingScreen).
                var tier1  = AccessTools.TypeByName("DNA.CastleMinerZ.UI.Tier1Item");
                var screen = AccessTools.TypeByName("DNA.CastleMinerZ.UI.CraftingScreen");
                return (tier1 == null || screen == null)
                    ? null
                    : AccessTools.Constructor(t, new[] { typeof(string), typeof(Recipe.RecipeTypes), tier1, screen });
            }

            static void Postfix(object __instance)
            {
                // Post-construction relayout:
                // - Rewrites Tier2Item's private _itemLocations[] to apply RIGHT_OF rules.
                // - Preserves vanilla duplicate-result layout semantics.
                try { WeaponAddonRecipePlacement.RelayoutTier2Item(__instance); }
                catch { /* best-effort */ }
            }
        }
        #endregion

        /// <summary>
        /// =====================================================================================
        /// AUDIO ROUTING (WEAPONADDONAUDIO TOKENS)
        /// =====================================================================================
        ///
        /// Summary:
        /// - WeaponAddons supports custom file-based SFX by storing token strings into the weapon class:
        ///     • WASFX_SHOOT:<ItemId>
        ///     • WASFX_RELOAD:<ItemId>
        /// - The engine normally treats those fields as XACT cue names and calls SoundManager.PlayInstance(...).
        /// - These patches intercept SoundManager.PlayInstance(...) and, when a token is detected,
        ///   redirect playback to WeaponAddonAudio (SoundEffect/SoundEffectInstance) instead of XACT.
        ///
        /// Behavior:
        /// - If name is NOT a WeaponAddonAudio token:
        ///     • Return true -> engine plays the normal XACT cue (vanilla behavior).
        /// - If name IS a token:
        ///     • Play our file-backed sound (2D or 3D) and return false to suppress engine playback.
        ///     • Set __result = null (safe for callers that ignore the returned handle).
        ///
        /// Notes:
        /// - Harmony arg binding can be sensitive to parameter names; these prefixes match "name"/"emitter".
        /// - The returned Cue/SoundCue3D handle is intentionally null for tokens:
        ///     • Many call sites ignore it.
        ///     • Reload call paths often null-check before Stop().
        /// - WeaponAddonAudio.Update() prunes finished SoundEffectInstances and disposes them to avoid leaks.
        /// =====================================================================================
        /// </summary>

        #region Audio Routing (WeaponAddonAudio Tokens)

        // 2D one-shots (returns Cue).
        [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PlayInstance), new[] { typeof(string) })]
        private static class Patch_SoundManager_PlayInstance_2D_WeaponAddonAudio
        {
            // IMPORTANT: Match param name "name".
            private static bool Prefix(string name, ref Microsoft.Xna.Framework.Audio.Cue __result)
            {
                // Token route: Play our SoundEffectInstance and suppress XACT cue playback.
                if (WeaponAddonAudio.TryPlay2D(name))
                {
                    __result = null; // Callers usually ignore this; safe for our tokens.
                    return false;    // Skip engine cue playback.
                }
                return true; // Vanilla cue path.
            }
        }

        // 3D one-shots (returns SoundCue3D).
        [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PlayInstance), new[] { typeof(string), typeof(Microsoft.Xna.Framework.Audio.AudioEmitter) })]
        private static class Patch_SoundManager_PlayInstance_3D_WeaponAddonAudio
        {
            // IMPORTANT: Match param names "name", "emitter".
            private static bool Prefix(string name, Microsoft.Xna.Framework.Audio.AudioEmitter emitter, ref SoundCue3D __result)
            {
                // Token route: Play our SoundEffectInstance (Apply3D) and suppress XACT cue playback.
                if (WeaponAddonAudio.TryPlay3D(name, emitter))
                {
                    __result = null; // Reload path checks for null before stopping.
                    return false;
                }
                return true; // Vanilla cue path.
            }
        }

        // Housekeeping so SoundEffectInstances get disposed.
        [HarmonyPatch(typeof(SoundManager), nameof(SoundManager.Update))]
        private static class Patch_SoundManager_Update_WeaponAddonAudio
        {
            // Summary:
            // - Prune stopped SoundEffectInstances and dispose them.
            // - Keeps long sessions + frequent reloads from leaking instance handles.
            private static void Postfix()
                => WeaponAddonAudio.Update();
        }
        #endregion

        #endregion
    }
}