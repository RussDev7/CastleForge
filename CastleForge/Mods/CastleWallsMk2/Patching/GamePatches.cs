/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060         // Silence IDE0060.
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Input;
using DNA.CastleMinerZ.Utils.Trace;
using DNA.CastleMinerZ.Inventory;
using DNA.CastleMinerZ.Net.Steam;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using DNA.Drawing.UI.Controls;
using Microsoft.Xna.Framework;
using DNA.Distribution.Steam;
using System.Reflection.Emit;
using System.Threading.Tasks;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.Net;
using DNA.CastleMinerZ.AI;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using ModLoaderExt;
using DNA.Drawing;
using System.Linq;
using DNA.Timers;
using HarmonyLib;                       // Harmony patching library.
using DNA.Input;
using System.IO;
using DNA.Net;
using System;
using DNA;

using static CastleWallsMk2.FeedbackRouter;
using static CastleWallsMk2.ServerCommands;
using static CastleWallsMk2.CryptoRng;
using static ModLoader.LogSystem;       // For Log(...).

namespace CastleWallsMk2
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

            // Network-Calls (SendData/ReceiveData logger):
            // Build dynamic target lists BEFORE Harmony scans patch classes (TargetMethods uses these lists).
            try
            {
                NetCallsMessagePatches.DiscoverTargets();
                Log($"[NetCalls] Discovered {NetCallsMessagePatches.TargetCountSummary()} message hooks.");
            }
            catch (Exception ex)
            {
                Log($"[NetCalls] DiscoverTargets failed: {ex.GetType().Name}: {ex.Message}.");
            }

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

        #region ImGui Patches

        #region Uppermost "Screen" Tester

        /*
        [HarmonyPatch(typeof(Screen), "OnDraw")]
        static class Debug_DrawOrder
        {
            static void Prefix(Screen __instance)
                => ModLoader.LogSystem.SendLog("[Draw-Begin] " + __instance.GetType().Name);

            static void Postfix(Screen __instance)
                => ModLoader.LogSystem.SendLog("[Draw-End]   " + __instance.GetType().Name);
        }
        */
        #endregion

        #region Draw Hook

        /// <summary>
        /// Initializes post-draw overlay settings once (idempotent).
        /// Notes:
        /// • Keep this lightweight and exception-safe - it runs during render setup.
        /// • Reads the toggle key from mod config (if available); otherwise falls back to <see cref="ToggleKey"/>'s default.
        /// </summary>
        private static Keys ToggleKey = Keys.OemTilde;          // Global toggle for showing/hiding the ImGui overlay.
        private static bool _imguiPostDrawSettingsBootstrapped; // One-time guard so settings are initialized exactly once per process.
        public static void ImGuiPostDrawSettings_InitOnce()
        {
            if (_imguiPostDrawSettingsBootstrapped) return; // Already done.
            _imguiPostDrawSettingsBootstrapped = true;      // Prevent repeated init.

            // Load configuration with safe fallback.
            var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();
            try { ToggleKey = cfg.ToggleKey; } catch { }
        }

        /// <summary>
        /// ImGui overlay: Draws once at the very end of ScreenGroup.Draw so it's truly top-most.
        /// Notes:
        /// • Must never render onto transient render targets (RTs) - only the backbuffer.
        /// • Uses the CURRENT viewport size (not the backbuffer directly) to avoid invalid scissor/viewport.
        /// • Fails closed: Any invalid device state ⇒ skip this frame (don't crash the game).
        /// </summary>
        [HarmonyPatch]
        [HarmonyPriority(Priority.First)]
        internal static class ScreenGroup_ImGuiPostDraw
        {
            /// <summary>
            /// Target the virtual ScreenGroup.Draw(GraphicsDevice, SpriteBatch, GameTime).
            /// Using AccessTools keeps it robust across minor sig changes.
            /// </summary>
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(ScreenGroup),
                    "Draw",
                    new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) }
                );
            }

            /// <summary>
            /// Postfix: Executes after the game/UI finish drawing.
            /// Ordering is controlled by HarmonyPriority + HarmonyAfter (by Harmony ID).
            /// </summary>
            [HarmonyPostfix] // Required so Harmony registers this as the postfix.
            static void Postfix(GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
            {
                try
                {
                    // Basic sanity - if device is gone or draw context is invalid, do nothing.
                    if (device == null || spriteBatch == null || gameTime == null || device.IsDisposed) return;
                    if (CastleMinerZGame.Instance?.ScreenManager?.CurrentScreen == null) return; // Optional: Wait to draw until a valid game instance.
                                                                                                 // This improves compatibility with other heavy startup mods.

                    // Never draw to transient RTs (e.g., fades, off-screen passes, atlas updates).
                    // Rendering overlay here causes scissor/viewport mismatch and exceptions.
                    var rts = device.GetRenderTargets();
                    if (rts != null && rts.Length > 0) { ImGuiXnaRenderer.CancelFrame(); return; }

                    // Guard early boot / resize churn:
                    // Viewport must be valid AND match the backbuffer (we only draw to the backbuffer).
                    var vp = device.Viewport;
                    var pp = device.PresentationParameters;
                    if (vp.Width <= 8 || vp.Height <= 8)                                    { ImGuiXnaRenderer.CancelFrame(); return; }
                    if (vp.Width != pp.BackBufferWidth || vp.Height != pp.BackBufferHeight) { ImGuiXnaRenderer.CancelFrame(); return; }

                    // One-time init (creates context, font atlas, buffers). Returns false until ready.
                    if (!ImGuiXnaRenderer.EnsureInit(device)) return;

                    // Drive ImGui with the CURRENT viewport size (not pp.BackBuffer*).
                    // Keeps clip/scissor/coords aligned with what's actually bound.
                    var io = ImGuiNET.ImGui.GetIO();
                    io.DisplaySize = new System.Numerics.Vector2(vp.Width, vp.Height);
                    io.DisplayFramebufferScale = System.Numerics.Vector2.One; // XNA is pixel-backed.

                    // Build & render the overlay.
                    ImGuiPostDrawSettings_InitOnce();         // One-time overlay configuration.
                    ImGuiXnaRenderer.SetToggleKey(ToggleKey); // Set a custom toggle key. // '~' key.
                    // ImGuiXnaRenderer.UpdateInput();        // Feed mouse/keyboard to ImGui and handle _toggleKey show/hide toggle.
                    ImGuiXnaRenderer.BeforeLayout(gameTime);  // Begin a new ImGui frame (sets sizes, delta time, etc).
                    ImGuiXnaRenderer.DrawUi();                // Build the actual UI widgets here.
                    ImGuiXnaRenderer.AfterLayout();           // Finalize and render the ImGui draw data to the GraphicsDevice.
                }
                catch (Exception ex)
                {
                    // Fail closed: Never crash the game from the overlay.
                    // Also ends any half-open ImGui frame so the next frame starts clean.
                    ModLoader.LogSystem.Log($"[ImGui] ScreenGroup.Draw postfix error: {ex.Message}.");
                    ImGuiXnaRenderer.CancelFrame();
                }
            }
        }
        #endregion

        #region Input Hook

        // Patch the game's input pipeline so we can (a) toggle the overlay and
        // (b) optionally capture input when the ImGui UI is focused.
        [HarmonyPatch(typeof(ScreenGroup))]
        internal static class ScreenGroup_ImGuiHooks
        {
            // Run BEFORE ScreenGroup.ProcessInput(...).
            [HarmonyPrefix]
            [HarmonyPatch(nameof(ScreenGroup.ProcessInput))]
            static bool ProcessInput_Prefix(InputManager inputManager, GameTime gameTime, ref bool __result)
            {
                // Update our overlay toggle (_toggleKey) and feed latest key state into the renderer's logic.
                // NOTE: This does not draw anything-just updates visibility and edge states.
                ImGuiXnaRenderer.ToggleImGui();

                // If the overlay is visible, we "eat" the input for this frame.
                if (ImGuiXnaRenderer.Visible)
                {
                    __result = true; // Tell Harmony: The original method is considered "processed/successful".
                    return false;    // Skip calling the game's ProcessInput this frame.
                }

                // Otherwise, let the game handle input normally.
                return true;
            }
        }
        #endregion

        #region Overlay Hook

        // Patch runs before the GameScreen.Update() method.
        // It's used to sync ImGui's input and visibility state with the game's UI/input system.
        [HarmonyPatch(typeof(GameScreen), nameof(GameScreen.Update))]
        static class GameScreen_Update_Patch
        {
            // Prefix runs before GameScreen.Update() every frame.
            // We use it to ensure OverlayController.Update() is called once per frame,
            // allowing it to redirect input and mouse behavior based on overlay visibility.
            static void Prefix() => OverlayController.Update();
        }
        #endregion

        #endregion

        #region Network Sniffer

        /// <summary>
        /// Session ended / disconnected:
        /// - Disable the sniffer.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), nameof(DNAGame.LeaveGame), new Type[] { })]
        internal static class Patch_DNAGame_LeaveGame_NetworkSniffer
        {
            [HarmonyPostfix]
            private static void Postfix(DNAGame __instance)
            {
                try
                {
                    IGMainUI.NetSniffer_Stop(
                        clearRows:     false, // Set true if you want to wipe captured rows on disconnect.
                        clearGeoCache: false  // Set true if you want to wipe geo cache too.
                    );
                    // SendLog("[Net] Sniffer stopped on LeaveGame.");
                }
                catch (Exception ex)
                {
                    Log($"[Net] Sniffer stop failed: {ex}.");
                }
            }
        }
        #endregion

        #region Server History (Host Tracking)

        /// <summary>
        /// ======================================================================================
        /// Server History Hooks (Harmony)
        /// --------------------------------------------------------------------------------------
        /// SUMMARY:
        ///   Hooks the game's join/host/session lifecycle to keep ServerHistory accurate:
        ///   - Record successful joins (+ last-known password only when provided).
        ///   - Late-fill host identity (Gamertag / AlternateAddress) once in-session.
        ///   - Clear current-host marker on leave/disconnect to prevent stale IP/password attachments.
        ///
        /// NOTES:
        ///   - Password is recorded ONLY after a successful join AND only if non-empty.
        ///   - LateFillCurrentHostIdentity is designed to migrate name-keyed entries into alt-keyed ones.
        ///   - All hooks must be safe: swallow exceptions and always forward original callbacks.
        /// ======================================================================================
        /// </summary>

        #region Hook: DNAGame.JoinGame - Remember Password After Success

        /// <summary>
        /// Remembers the last-known password ONLY after a SUCCESSFUL join where a password was provided.
        /// Also ensures we record/update the host entry using AvailableNetworkSession (HostSteamID/Gamertag).
        /// </summary>
        [HarmonyPatch(typeof(DNAGame))]
        internal static class Patch_DNAGame_JoinGame_RememberPassword
        {
            // DNAGame.JoinGame(
            //    AvailableNetworkSession session,
            //    IList<SignedInGamer> gamers,
            //    DNAGame.SuccessCallbackWithMessage callback,
            //    string gameName,
            //    int version,
            //    string password)
            [HarmonyPrefix]
            [HarmonyPatch(nameof(DNAGame.JoinGame), new Type[]
            {
                typeof(AvailableNetworkSession),
                typeof(IList<SignedInGamer>),
                typeof(SuccessCallbackWithMessage),
                typeof(string),
                typeof(int),
                typeof(string)
            })]
            private static void Prefix(
                AvailableNetworkSession        session,
                IList<SignedInGamer>           gamers,
                ref SuccessCallbackWithMessage callback,
                string                         gameName,
                int                            version,
                string                         password)
            {
                // Capture the values now (safe even if session mutates later).
                ulong  hostAlt  = 0;
                string hostName = "";

                try { if (session != null) hostAlt  = session.HostSteamID;        } catch { }
                try { if (session != null) hostName = session.HostGamertag ?? ""; } catch { }

                // Only store password when actually provided.
                string pw = string.IsNullOrWhiteSpace(password) ? "" : password.Trim();

                // Capture "me" so we can ignore accidental self-identification.
                string myName = "";
                try { myName = CastleMinerZGame.Instance?.MyNetworkGamer?.Gamertag ?? ""; } catch { }

                var orig = callback;
                callback = (ok, message) =>
                {
                    bool shouldRecord = false;

                    try
                    {
                        if (ok)
                        {
                            // Decide only (do NOT return).
                            shouldRecord = true;

                            bool nameBad =
                                string.IsNullOrWhiteSpace(hostName) ||
                                string.Equals(hostName, "[unknown]", StringComparison.OrdinalIgnoreCase);

                            if (hostAlt == 0 && nameBad)
                                shouldRecord = false;

                            // If we still can't identify the host, do NOT create a junk row.
                            if (hostAlt == 0 && string.IsNullOrWhiteSpace(hostName))
                                shouldRecord = false;

                            // If the "hostName" somehow resolves to our own name and alt is unknown, skip.
                            if (shouldRecord && hostAlt == 0 && !string.IsNullOrEmpty(myName) &&
                                string.Equals(hostName, myName, StringComparison.OrdinalIgnoreCase))
                                shouldRecord = false;

                            // Record host connection (also sets current-host marker).
                            if (shouldRecord)
                                ServerHistory.NoteJoinSuccess(hostName, hostAlt, pw);
                        }
                    }
                    catch
                    {
                        // Swallow.
                    }
                    finally
                    {
                        // CRITICAL: Always forward so the game can finish its join flow.
                        try { orig?.Invoke(ok, message); } catch { }
                    }
                };
            }
        }
        #endregion

        #region Hook: CastleMinerZGame.OnHostChanged - Host Migration

        /// <summary>
        /// Host migration: When a session host changes, record the new host as well.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "OnHostChanged")]
        internal static class Patch_CastleMinerZGame_OnHostChanged_ServerHistory
        {
            [HarmonyPostfix]
            private static void Postfix(CastleMinerZGame __instance, NetworkGamer oldHost, NetworkGamer newHost)
            {
                try
                {
                    if (newHost == null)
                    {
                        ServerHistory.ClearCurrentHost();
                        return;
                    }

                    var session = __instance?.CurrentNetworkSession;
                    if (session == null) return;

                    // If we're now host, there is no remote host to track.
                    if (session.LocalGamers != null && session.LocalGamers.Count > 0 && session.LocalGamers[0] != null && session.LocalGamers[0].IsHost)
                    {
                        ServerHistory.ClearCurrentHost();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ServerHistory] OnHostChanged hook failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Hook: CastleMinerZGame.OnGamerJoined - Late Fill Host Identity

        /// <summary>
        /// Late-fill host identity once we're actually in-session as a client.
        /// This is used when the HostSteamID/AlternateAddress wasn't known/filled at join time.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "OnGamerJoined")]
        internal static class Patch_CastleMinerZGame_OnGamerJoined_ServerHistory_LateFill
        {
            [HarmonyPostfix]
            private static void Postfix(CastleMinerZGame __instance, NetworkGamer gamer)
            {
                try
                {
                    var session = __instance?.CurrentNetworkSession;
                    if (session == null) return;

                    // Only care once we're actually in-session as a client.
                    if (session.LocalGamers == null || session.LocalGamers.Count == 0) return;
                    var me = session.LocalGamers[0];
                    if (me == null || me.IsHost) return; // Host doesn't need a "remote host" entry.

                    // Find current host gamer.
                    NetworkGamer host = null;
                    foreach (NetworkGamer g in session.AllGamers)
                    {
                        if (g != null && g.IsHost) { host = g; break; }
                    }
                    if (host == null) return;

                    string hostName = null;
                    ulong hostAlt = 0;

                    try { hostName = host.Gamertag; } catch { }
                    try { hostAlt = host.AlternateAddress; } catch { }

                    // Only update if we learned something real.
                    if (string.IsNullOrWhiteSpace(hostName) && hostAlt == 0)
                        return;

                    // This method should:
                    // - if we already have current host key, update its Name if missing/[unknown]
                    // - if we have a junk "name:" entry and now have alt, migrate/merge to "alt:" entry
                    ServerHistory.LateFillCurrentHostIdentity(hostName, hostAlt);

                    // (Optional) if you used password during join, this is also a safe place
                    // to call TryUpdateCurrentHostPassword(...) AFTER you know current host key.
                }
                catch { }
            }
        }
        #endregion

        #region Hook: DNAGame.LeaveGame - Clear Current Host Marker

        /// <summary>
        /// Session ended / disconnected: Clear current-host marker so future IP guesses don't attach to old host.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), nameof(DNAGame.LeaveGame), new Type[] { })]
        internal static class Patch_CastleMinerZGame_OnSessionEnded_ServerHistory
        {
            [HarmonyPostfix]
            private static void Postfix(DNAGame __instance)
            {
                try
                {
                    try { ServerHistory.ClearCurrentHost(); }
                    catch { }
                }
                catch (Exception ex)
                {
                    Log($"[SH] Clear host on stop failed: {ex}.");
                }
            }
        }
        #endregion

        #endregion

        #region Player Enforcement

        #region Host-Side Steam Deny Enforcement

        /// <summary>
        /// Host-side Steam message gate for Player Enforcement.
        ///
        /// Summary:
        /// - Intercepts host-side Steam session messages before normal handling.
        /// - Detects hard-banned / runtime-blocked Steam senders.
        /// - Sends a deny reason back to the banned peer when appropriate.
        /// - Silently swallows blocked traffic so the peer does not continue joining.
        ///
        /// Notes:
        /// - This patch only performs network-side deny enforcement when running as the host.
        /// - Non-host enforcement is handled separately in the CastleMinerZGame.Update patch using
        ///   local Gamertag matching plus private KickMessage sends.
        /// - A short deny cooldown is used to avoid repeatedly spamming the same sender.
        /// </summary>
        [HarmonyPatch(typeof(SteamNetworkSessionProvider), "HandleHostMessages")]
        internal static class Patch_SteamNetworkSessionProvider_HandleHostMessages_EnforceBans
        {
            #region Reflection / Runtime State

            /// <summary>
            /// Cached reflection handle for SteamNetworkSessionProvider._steamAPI.
            /// </summary>
            private static readonly FieldInfo FI_SteamProvider_SteamApi =
                AccessTools.Field(typeof(SteamNetworkSessionProvider), "_steamAPI");

            /// <summary>
            /// Synchronizes deny-cooldown writes.
            /// </summary>
            private static readonly object _denySync = new object();

            /// <summary>
            /// Tracks the last time a deny was sent to a specific SteamID.
            ///
            /// Notes:
            /// - Used to prevent repeated deny sends within a very small time window.
            /// </summary>
            private static readonly Dictionary<ulong, DateTime> _lastDenyUtc =
                new Dictionary<ulong, DateTime>();

            #endregion

            #region Prefix

            /// <summary>
            /// Intercepts host-side Steam messages and blocks hard-banned senders.
            ///
            /// Summary:
            /// - Loads enforcement config.
            /// - Exits immediately for non-host clients.
            /// - Checks whether the sender is runtime-blocked or persistently hard-banned.
            /// - Sends the configured deny reason back to the peer.
            /// - Swallows the message when enforcement should deny the sender.
            /// </summary>
            private static bool Prefix(SteamNetworkSessionProvider __instance, SteamNetBuffer msg, ref bool __result)
            {
                if (msg == null)
                    return true;

                PlayerEnforcementConfig.LoadOnce();

                CastleMinerZGame game = CastleMinerZGame.Instance;
                bool isHost = game?.MyNetworkGamer != null && game.MyNetworkGamer.IsHost;

                // Non-host enforcement is handled in Update by gamertag + private kick.
                if (!isHost)
                    return true;

                ulong senderSteamId = msg.SenderId;

                bool shouldDeny =
                    ForcedDisconnectRuntime.IsBlockedSteamId(senderSteamId) ||
                    (PersistentBanStore.TryGetEntry(senderSteamId, out var entry) &&
                     string.Equals(entry.Mode, "HardBan", StringComparison.OrdinalIgnoreCase));

                if (!shouldDeny)
                    return true;

                string reason = string.IsNullOrWhiteSpace(PlayerEnforcementConfig.HardBanDenyMessage)
                    ? "Host Kicked Us"
                    : PlayerEnforcementConfig.HardBanDenyMessage;

                TryDenyJoin(__instance, senderSteamId, reason);

                __result = true;
                return false;
            }
            #endregion

            #region Helpers

            /// <summary>
            /// Attempts to send a Steam deny message back to a blocked sender.
            ///
            /// Summary:
            /// - Enforces a small per-SteamID cooldown.
            /// - Uses the provider's SteamWorks instance to send the deny reason.
            ///
            /// Notes:
            /// - SteamID 0 is ignored.
            /// - Failures are intentionally swallowed so enforcement remains non-disruptive.
            /// </summary>
            private static void TryDenyJoin(
                SteamNetworkSessionProvider provider,
                ulong steamId,
                string reason)
            {
                if (provider == null || steamId == 0UL)
                    return;

                bool allowSend = false;
                DateTime now = DateTime.UtcNow;

                lock (_denySync)
                {
                    if (!_lastDenyUtc.TryGetValue(steamId, out DateTime last) ||
                        (now - last).TotalSeconds >= 2.0)
                    {
                        _lastDenyUtc[steamId] = now;
                        allowSend = true;
                    }
                }

                if (!allowSend)
                    return;

                try
                {
                    if (FI_SteamProvider_SteamApi?.GetValue(provider) is SteamWorks steamApi)
                        steamApi.Deny(steamId, reason);
                }
                catch
                {
                }
            }
            #endregion
        }
        #endregion

        #region Periodic Ban Enforcement Sweep

        /// <summary>
        /// Periodic enforcement pass for persistent ban entries.
        ///
        /// Summary:
        /// - Runs during CastleMinerZGame.Update.
        /// - Loads config and persistent bans.
        /// - Performs host-only SteamID enforcement when hosting.
        /// - Performs off-host local Gamertag/private-kick enforcement when enabled.
        /// - Saves dirty config/store state after enforcement.
        ///
        /// Notes:
        /// - The sweep is throttled to run at most every 0.50 seconds.
        /// - A snapshot of current gamers is taken before enforcement to avoid modifying the live
        ///   collection while iterating it.
        /// - Off-host enforcement intentionally skips the session host.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "Update")]
        internal static class Patch_CastleMinerZGame_Update_EnforcePersistentBans
        {
            #region Runtime State

            /// <summary>
            /// Next UTC second threshold for the next sweep.
            ///
            /// Notes:
            /// - Stored as seconds-from-ticks double for lightweight interval checks.
            /// </summary>
            private static double _nextSweepUtc;

            #endregion

            #region Postfix

            /// <summary>
            /// Runs the periodic enforcement sweep.
            ///
            /// Summary:
            /// - Host path:
            ///   - Resolves the current gamer against the persistent store.
            ///   - Applies vanilla BanList state for VanillaBan entries.
            ///   - Force-disconnects hard-banned players not yet runtime-blocked.
            ///
            /// - Off-host path:
            ///   - Skips the session host.
            ///   - Matches players by normalized Gamertag.
            ///   - Re-sends private ban/kick messages based on locally stored entries.
            ///   - Applies runtime Gamertag block behavior for local hard-kick mode.
            ///
            /// Notes:
            /// - All exceptions are intentionally swallowed to keep the game update loop resilient.
            /// </summary>
            private static void Postfix()
            {
                try
                {
                    PlayerEnforcementConfig.LoadOnce();
                    PersistentBanStore.LoadOnce();

                    CastleMinerZGame game = CastleMinerZGame.Instance;
                    NetworkSession session = game?.CurrentNetworkSession;

                    if (game == null || session == null || !(game?.MyNetworkGamer is LocalNetworkGamer local))
                        return;

                    bool isHost = local.IsHost;
                    bool useGamertagOffHost = PlayerEnforcementConfig.UseGamertagPrivateKickWhenNotHost;

                    if (!isHost && !useGamertagOffHost)
                        return;

                    double now = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
                    if (now < _nextSweepUtc)
                        return;

                    _nextSweepUtc = now + 0.50;

                    List<NetworkGamer> gamers = new List<NetworkGamer>();
                    foreach (NetworkGamer gamer in session.AllGamers)
                    {
                        if (gamer != null)
                            gamers.Add(gamer);
                    }

                    foreach (NetworkGamer gamer in gamers)
                    {
                        if (gamer == null || gamer.IsLocal)
                            continue;

                        #region Host Enforcement Path

                        if (isHost)
                        {
                            ulong steamId = gamer.AlternateAddress;
                            if (steamId == 0UL)
                                continue;

                            if (!PersistentBanStore.TryGetEntryForCurrentGamer(gamer, out var entry))
                                continue;

                            if (string.Equals(entry.Mode, "VanillaBan", StringComparison.OrdinalIgnoreCase))
                            {
                                ForcedDisconnectRuntime.AddVanillaBan(steamId);
                                continue;
                            }

                            if (string.Equals(entry.Mode, "HardBan", StringComparison.OrdinalIgnoreCase) &&
                                !ForcedDisconnectRuntime.IsBlockedSteamId(steamId))
                            {
                                ForcedDisconnectRuntime.ForceDisconnectRemote(gamer, ban: true);
                            }

                            continue;
                        }
                        #endregion

                        #region Off-Host Local Enforcement Path

                        // Off-host: Never private-kick the session host.
                        if (gamer == session.Host || gamer.IsHost)
                            continue;

                        // Off-host mode: local Gamertag + private kick only.
                        string tag = ForcedDisconnectRuntime.NormalizeGamertag(gamer.Gamertag);
                        if (string.IsNullOrWhiteSpace(tag))
                            continue;

                        bool runtimeHardKick = ForcedDisconnectRuntime.IsBlockedGamertag(tag);

                        if (PersistentBanStore.TryGetEntryForOffHostGamertag(tag, out var tagEntry))
                        {
                            if (string.Equals(tagEntry.Mode, "VanillaBan", StringComparison.OrdinalIgnoreCase))
                            {
                                ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: true);
                                continue;
                            }

                            if (string.Equals(tagEntry.Mode, "HardBan", StringComparison.OrdinalIgnoreCase))
                            {
                                ForcedDisconnectRuntime.BlockGamertag(tag);
                                ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: true);
                                continue;
                            }
                        }

                        if (runtimeHardKick)
                        {
                            ForcedDisconnectRuntime.TryPrivateKick(local, gamer, banned: false);
                        }
                        #endregion
                    }

                    PlayerEnforcementConfig.SaveIfDirty();
                    PersistentBanStore.SaveIfDirty();
                }
                catch
                {
                }
            }
            #endregion
        }
        #endregion

        #endregion

        #region Log Patches

        #region Get Message

        /// <summary>
        /// Logs inbound/outbound chat-like messages parsed by DNA.Net.Message.GetMessage.
        /// We do this in a Postfix so the message is already fully deserialiMob.
        /// </summary>
        [HarmonyPatch(typeof(Message), nameof(Message.GetMessage))]
        static class Message_GetMessage_Log
        {
            static void Postfix(Message __result)
            {
                // Safety: If no message was read this frame, nothing to do.
                if (__result == null) return;

                // Heuristic extractor that pulls the user-visible text payload
                // from chat-ish message types (BroadcastTextMessage, etc).
                if (MessageTextExtractor.TryGetChatText(__result, out var text))
                {
                    // Sender can be null in some edge cases; treat null+local carefully.
                    var sender = __result.Sender as NetworkGamer;
                    var isLocal = sender != null && sender.IsLocal;

                    // If we have a gamer, use their tag; otherwise show "You" (local)
                    // or "?" (unknown) so the log is never blank.
                    var who = sender?.Gamertag ?? (isLocal ? "You" : "?");

                    // Direction tagging for the log (<< inbound / >> outbound).
                    if (isLocal) ChatLog.AddOutbound(who, text);
                    else ChatLog.AddInbound(who, text);

                    // IMPORTANT: Many builds echo chat to Console.WriteLine.
                    // Tell the Tee to ignore the same line for a short TTL so
                    // we don't double-log it when the console writer fires.
                    ConsoleCapture.MarkChatEcho(text);
                }
            }
        }
        #endregion

        #region Grab Console

        /// <summary>
        /// When the game grabs/redirects the console (ConsoleElement.GrabConsole),
        /// immediately re-wrap stdout/stderr with our Tee so we keep capturing lines.
        /// Postfix is used so the game's own writer stays intact under our wrapper.
        /// </summary>
        [HarmonyPatch(typeof(ConsoleElement), nameof(ConsoleElement.GrabConsole))]
        internal static class ConsoleElement_GrabConsole_Rewrap
        {
            static void Postfix()
            {
                try
                {
                    // Idempotent wrappers: Safe to call multiple times.
                    ConsoleCapture.TryWrapConsoleOut();
                    ConsoleCapture.TryWrapConsoleErr();
                }
                catch
                {
                    // Last-ditch capture: Never crash rendering/logging paths.
                }
            }
        }
        #endregion

        #endregion

        #region QoL Patches

        #region Remove Character Limitations + Paste Handling

        // Removed: Moved to QoL mod.
        /*
        [HarmonyPatch(typeof(TextEditControl), "OnChar")]
        internal static class TextEditControl_OnChar_AllowAnyCharPlusPaste
        {
            /// <summary>
            /// Restores Ctrl+V paste support for TextEditControl while still blocking
            /// raw control characters from being inserted into the text box.
            /// </summary>
            [HarmonyPrefix]
            static bool Prefix(TextEditControl __instance, char c)
            {
                // Ctrl+V arrives through the game's char pipeline as 0x16.
                if (c != '\u0016')
                    return true;

                try
                {
                    if (!System.Windows.Forms.Clipboard.ContainsText())
                        return false; // Consume Ctrl+V even if clipboard is empty.

                    string clipboard = System.Windows.Forms.Clipboard.GetText();
                    if (string.IsNullOrEmpty(clipboard))
                        return false;

                    // Keep it single-line and remove control junk.
                    clipboard = clipboard.Replace("\r\n", " ")
                                         .Replace('\r', ' ')
                                         .Replace('\n', ' ')
                                         .Replace('\t', ' ');

                    clipboard = new string(clipboard.Where(ch => !char.IsControl(ch)).ToArray());

                    if (clipboard.Length == 0)
                        return false;

                    var textBuilder = new StringBuilder(__instance.Text ?? string.Empty);
                    int curPos      = Math.Max(0, Math.Min(__instance.CursorPos, textBuilder.Length));

                    Rectangle textBounds = __instance.Frame.CenterRegion(__instance.ScreenBounds);

                    foreach (char ch in clipboard)
                    {
                        if (__instance.MaxChars >= 0 && textBuilder.Length >= __instance.MaxChars)
                            break;

                        // Match vanilla behavior: only insert if the rendered text still fits.
                        var test = new StringBuilder(textBuilder.ToString());
                        test.Insert(curPos, ch);

                        Vector2 newSize = __instance.Font.MeasureString(test) * __instance.Scale;
                        if (newSize.X >= textBounds.Width)
                            break;

                        textBuilder.Insert(curPos, ch);
                        curPos++;
                    }

                    __instance.Text      = textBuilder.ToString();
                    __instance.CursorPos = curPos;
                }
                catch
                {
                    // Never let paste crash the UI.
                }

                return false; // We handled Ctrl+V ourselves.
            }

            /// <summary>
            /// Broadens TextEditControl input acceptance from the vanilla
            /// letter/digit/punctuation/whitespace gate to:
            ///     !char.IsControl(c)
            /// so printable symbols and wider Unicode text can be entered
            /// without allowing escape/control characters into the buffer.
            /// </summary>
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var isLetterOrDigit = AccessTools.Method(typeof(char), nameof(char.IsLetterOrDigit), new[] { typeof(char) });
                var isPunctuation   = AccessTools.Method(typeof(char), nameof(char.IsPunctuation),   new[] { typeof(char) });
                var isWhiteSpace    = AccessTools.Method(typeof(char), nameof(char.IsWhiteSpace),    new[] { typeof(char) });
                var isControl       = AccessTools.Method(typeof(char), nameof(char.IsControl),       new[] { typeof(char) });

                foreach (var ins in instructions)
                {
                    if (ins.Calls(isLetterOrDigit) || ins.Calls(isPunctuation) || ins.Calls(isWhiteSpace))
                    {
                        // Replace each call with: !char.IsControl(c)
                        yield return new CodeInstruction(OpCodes.Call, isControl);
                        yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                        yield return new CodeInstruction(OpCodes.Ceq);
                        continue;
                    }

                    yield return ins;
                }
            }
        }
        */
        #endregion

        #region Remove Max World Height

        // Removed: Moved to QoL mod.
        /*
        [HarmonyPatch(typeof(Player), "OnUpdate")]
        internal static class Patch_Player_OnUpdate_RemoveMaxWorldHeight
        {
            #region Cached Config (Init Once)

            private static bool _removeMaxWorldHeightBootstrapped; // One-time guard.
            private static bool _removeMaxWorldHeightEnabled;      // Cached flag.

            /// <summary>
            /// Initialize cached config once per process (or until you manually refresh it).
            /// </summary>
            private static void RemoveMaxWorldHeight_InitOnce()
            {
                if (_removeMaxWorldHeightBootstrapped) return;
                _removeMaxWorldHeightBootstrapped = true;

                try
                {
                    _removeMaxWorldHeightEnabled = ModConfig.LoadOrCreateDefaults().RemoveMaxWorldHeight;
                }
                catch
                {
                    // Safe fallback: Behave like vanilla.
                    _removeMaxWorldHeightEnabled = false;
                }
            }

            /// <summary>
            /// Optional: Call this from your reload hotkey / config reload path to refresh the cached flag.
            /// </summary>
            public static void RemoveMaxWorldHeight_RefreshFromConfig()
            {
                _removeMaxWorldHeightBootstrapped = false; // Force re-init next time.
                RemoveMaxWorldHeight_InitOnce();
            }
            #endregion

            /// <summary>
            /// Returns either the vanilla ceiling (74/64) or "no ceiling" depending on cached config.
            /// Keeps vanilla behavior when disabled, even if the game uses 64f instead of 74f.
            /// </summary>
            private static float GetCeilingY(float vanillaCeiling)
            {
                RemoveMaxWorldHeight_InitOnce();

                if (_removeMaxWorldHeightEnabled)
                    return float.MaxValue;

                return vanillaCeiling;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var miGetCeiling = AccessTools.Method(
                    typeof(Patch_Player_OnUpdate_RemoveMaxWorldHeight),
                    nameof(GetCeilingY)
                );

                foreach (var ins in instructions)
                {
                    // Look for the ceiling constant used by Math.Min(CEILING, newpos.Y).
                    if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f &&
                        (Math.Abs(f - 74f) < 0.0001f || Math.Abs(f - 64f) < 0.0001f))
                    {
                        // Keep the original ceiling literal on the stack...
                        yield return ins;

                        // ...but route it through config:
                        // stack: [vanillaCeiling] -> [ceiling].
                        yield return new CodeInstruction(OpCodes.Call, miGetCeiling);

                        continue;
                    }

                    yield return ins;
                }
            }
        }
        */
        #endregion

        #region In-Game Menu: Keep Teleport Visible In Vanilla Slot

        /// <summary>
        /// Harmony patch for <see cref="InGameMenu"/> constructor logic that keeps the
        /// Teleport menu entry visible even when vanilla would normally hide it for:
        /// - Endurance mode
        /// - Hardcore difficulty
        ///
        /// Instead of injecting a new menu item afterward (which would place it at the
        /// bottom), this patch temporarily spoofs the values checked by vanilla during
        /// menu construction so the original Teleport button is created in its normal
        /// position directly under Inventory.
        /// </summary>
        [HarmonyPatch(typeof(InGameMenu), MethodType.Constructor, typeof(CastleMinerZGame))]
        internal static class Patch_InGameMenu_KeepTeleportInPlace
        {
            /// <summary>
            /// Small constructor-scoped state bag used to remember the original values
            /// so they can be restored after the menu finishes building.
            /// </summary>
            private struct MenuCtorState
            {
                public bool                Applied;
                public GameModeTypes       OriginalGameMode;
                public GameDifficultyTypes OriginalDifficulty;
            }

            [HarmonyPrefix]
            private static void Prefix(CastleMinerZGame game, ref MenuCtorState __state)
            {
                try
                {
                    if (game == null)
                        return;

                    if (!TryGetMember(game, "GameMode", out GameModeTypes currentMode))
                        return;

                    if (!TryGetMember(game, "Difficulty", out GameDifficultyTypes currentDifficulty))
                        return;

                    __state.OriginalGameMode   = currentMode;
                    __state.OriginalDifficulty = currentDifficulty;

                    bool blockedByMode       = currentMode       == GameModeTypes.Endurance;
                    bool blockedByDifficulty = currentDifficulty == GameDifficultyTypes.HARDCORE;

                    // Vanilla already allows Teleport, no need to spoof anything.
                    if (!blockedByMode && !blockedByDifficulty)
                        return;

                    bool ok = true;

                    if (blockedByMode)
                    {
                        var safeMode = GetAnyOtherEnumValue(currentMode);
                        ok &= TrySetMember(game, "GameMode", safeMode);
                    }

                    if (blockedByDifficulty)
                    {
                        var safeDifficulty = GetAnyOtherEnumValue(currentDifficulty);
                        ok &= TrySetMember(game, "Difficulty", safeDifficulty);
                    }

                    __state.Applied = ok;
                }
                catch
                {
                    // Swallow to avoid breaking menu creation.
                }
            }

            [HarmonyPostfix]
            private static void Postfix(CastleMinerZGame game, MenuCtorState __state)
            {
                try
                {
                    if (!__state.Applied || game == null)
                        return;

                    TrySetMember(game, "GameMode",   __state.OriginalGameMode);
                    TrySetMember(game, "Difficulty", __state.OriginalDifficulty);
                }
                catch
                {
                    // Swallow to avoid breaking menu creation.
                }
            }

            /// <summary>
            /// Returns any enum value other than the disallowed one so the vanilla menu
            /// visibility check evaluates as allowed during construction.
            /// </summary>
            private static TEnum GetAnyOtherEnumValue<TEnum>(TEnum disallowed) where TEnum : struct, Enum
            {
                foreach (var value in Enum.GetValues(typeof(TEnum)).Cast<TEnum>())
                {
                    if (!value.Equals(disallowed))
                        return value;
                }

                return disallowed;
            }

            /// <summary>
            /// Tries to read a member by property name first, then by a few common field
            /// naming conventions used by decompiled / game-side code.
            /// </summary>
            private static bool TryGetMember<T>(object obj, string memberName, out T value)
            {
                value = default;
                if (obj == null)
                    return false;

                Type type = obj.GetType();

                // Property first.
                PropertyInfo prop = AccessTools.Property(type, memberName);
                if (prop != null && prop.CanRead)
                {
                    object raw = prop.GetValue(obj, null);
                    if (raw is T typed)
                    {
                        value = typed;
                        return true;
                    }
                }

                // Then likely field names.
                foreach (string fieldName in GetCandidateFieldNames(memberName))
                {
                    FieldInfo field = AccessTools.Field(type, fieldName);
                    if (field == null)
                        continue;

                    object raw = field.GetValue(obj);
                    if (raw is T typed)
                    {
                        value = typed;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Tries to write a member by property name first, then by a few common field
            /// naming conventions used by decompiled / game-side code.
            /// </summary>
            private static bool TrySetMember<T>(object obj, string memberName, T value)
            {
                if (obj == null)
                    return false;

                Type type = obj.GetType();

                // Property first.
                PropertyInfo prop = AccessTools.Property(type, memberName);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(obj, value, null);
                    return true;
                }

                // Then likely field names.
                foreach (string fieldName in GetCandidateFieldNames(memberName))
                {
                    FieldInfo field = AccessTools.Field(type, fieldName);
                    if (field == null)
                        continue;

                    field.SetValue(obj, value);
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Generates a small set of likely backing-member names for decompiled types.
            /// </summary>
            private static string[] GetCandidateFieldNames(string memberName)
            {
                string camel = char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);

                return new[]
                {
                    memberName,
                    "_" + camel,
                    "m_" + camel,
                    "<" + memberName + ">k__BackingField"
                };
            }
        }
        #endregion

        #region Teleport Menu: Remove PvP Restriction From "Teleport To Player"

        /// <summary>
        /// Removes the PvP visibility restriction from TeleportMenu's
        /// "Teleport To Player" entry while still respecting online/offline state.
        /// </summary>
        [HarmonyPatch(typeof(TeleportMenu), "OnUpdate")]
        internal static class Patch_TeleportMenu_RemovePvpRestriction
        {
            // NOTE: Field name is exactly "toPLayer" in the decompiled code.
            private static readonly AccessTools.FieldRef<TeleportMenu, MenuItemElement> _toPlayerRef =
                AccessTools.FieldRefAccess<TeleportMenu, MenuItemElement>("toPLayer");

            private static readonly AccessTools.FieldRef<TeleportMenu, CastleMinerZGame> _gameRef =
                AccessTools.FieldRefAccess<TeleportMenu, CastleMinerZGame>("_game");

            [HarmonyPostfix]
            private static void Postfix(TeleportMenu __instance)
            {
                try
                {
                    var toPlayer = _toPlayerRef(__instance);
                    var game     = _gameRef(__instance);

                    if (toPlayer == null || game == null)
                        return;

                    // Keep the online requirement, remove only the PvP restriction.
                    toPlayer.Visible = game.IsOnlineGame;
                }
                catch
                {
                    // Never break menu updates.
                }
            }
        }
        #endregion

        #endregion

        #region Crash Patches (Not Handeled By MLE)

        #region UNUSED: Net Safety - Client SendData Uses Direct-Send (Avoid Steam BroadcastRemoteData NRE)

        /// <summary>
        /// SUMMARY
        /// -------
        /// Fixes a Steam join/handshake race where CLIENTS (non-host) calling
        /// Session.BroadcastRemoteData(...) can throw a NullReferenceException inside
        /// SteamNetworkSessionProvider.BroadcastRemoteData(...) and spam FIRST_CHANCE logs.
        ///
        /// Instead of broadcasting from a client, we:
        ///   1) Preserve vanilla local delivery (AppendNewDataPacket to LocalGamers)
        ///   2) Send ONLY to the host via Session.SendRemoteData(...)
        ///
        /// This avoids the Steam provider's BroadcastRemoteData path entirely for clients.
        ///
        /// NOTES / GOTCHAS
        /// ---------------
        /// - Host behavior is untouched: if (__instance.IsHost) we return true and run vanilla code.
        /// - Client behavior: we return false to skip vanilla, after doing local delivery + host direct-send.
        /// - If host is not yet assigned (or Host is local), we fallback to the first RemoteGamer as "target".
        ///   This is usually the host during the initial connection window.
        /// - If no valid target is found yet, we still deliver locally and silently skip the remote send.
        ///   (This is intentional: better to drop a packet than crash/spam logs during join.)
        /// - We patch BOTH overloads:
        ///     SendData(byte[] data, SendDataOptions options)
        ///     SendData(byte[] data, int offset, int count, SendDataOptions options)
        /// - Payload validation is stricter on the offset/count overload to avoid bounds errors.
        ///
        /// WHY THIS HELPS
        /// --------------
        /// FIRST_CHANCE exceptions are logged when the exception is THROWN, even if caught later.
        /// To stop the spam, we must avoid executing the throwing call path - not just catch it.
        ///
        /// This patch prevents clients from reaching:
        ///   BroadcastRemoteData -> SteamNetworkSessionProvider.BroadcastRemoteData (NRE site)
        /// by using:
        ///   SendRemoteData -> SteamNetworkSessionProvider.SendRemoteData (safe path)
        /// </summary>

        /*
        [HarmonyPatch(typeof(LocalNetworkGamer))]
        internal static class Patch_LocalNetworkGamer_SendData_ClientDirectToHost
        {
            [HarmonyPatch("SendData", new[] { typeof(byte[]), typeof(SendDataOptions) })]
            [HarmonyPrefix]
            private static bool Prefix_SendData(LocalNetworkGamer __instance, byte[] data, SendDataOptions options)
            {
                if (__instance == null) return true;

                // Host behavior stays vanilla.
                if (__instance.IsHost) return true;

                var session = __instance.Session;
                if (session == null || session.IsDisposed) return false;

                // 1) Deliver to locals (vanilla behavior).
                var locals = session.LocalGamers;
                for (int i = 0; i < locals.Count; i++)
                {
                    var lg = locals[i];
                    if (lg != null && !lg.HasLeftSession)
                        lg.AppendNewDataPacket(data, __instance);
                }

                // 2) Send ONLY to host (avoid BroadcastRemoteData).
                var host = session.Host;
                NetworkGamer target = null;

                // Prefer session.Host if it is a remote gamer.
                if (host != null && !(host is LocalNetworkGamer) && !host.HasLeftSession)
                {
                    target = host;
                }
                else
                {
                    // Fallback: pick first remote gamer (usually the host).
                    var remotes = session.RemoteGamers;
                    for (int j = 0; j < remotes.Count; j++)
                    {
                        var rg = remotes[j];
                        if (rg != null && !rg.HasLeftSession)
                        {
                            target = rg;
                            break;
                        }
                    }
                }

                if (target != null)
                {
                    // Offset/length overload exists; this will call provider.SendRemoteData instead of provider.BroadcastRemoteData.
                    session.SendRemoteData(data, options, target);
                }

                // Skip original (which calls BroadcastRemoteData on clients).
                return false;
            }

            [HarmonyPatch("SendData", new[] { typeof(byte[]), typeof(int), typeof(int), typeof(SendDataOptions) })]
            [HarmonyPrefix]
            private static bool Prefix_SendData_Offset(LocalNetworkGamer __instance, byte[] data, int offset, int count, SendDataOptions options)
            {
                if (__instance == null) return true;

                // Host behavior stays vanilla.
                if (__instance.IsHost) return true;

                var session = __instance.Session;
                if (session == null || session.IsDisposed) return false;

                if (data == null) return false;
                if (offset < 0 || count <= 0) return false;
                if (offset + count > data.Length) return false;

                // 1) Deliver to locals (copy slice, same semantics as vanilla AppendNewDataPacket overload).
                var locals = session.LocalGamers;
                for (int i = 0; i < locals.Count; i++)
                {
                    var lg = locals[i];
                    if (lg != null && !lg.HasLeftSession)
                        lg.AppendNewDataPacket(data, offset, count, __instance);
                }

                // 2) Send ONLY to host (avoid BroadcastRemoteData).
                var host = session.Host;
                NetworkGamer target = null;

                if (host != null && !(host is LocalNetworkGamer) && !host.HasLeftSession)
                {
                    target = host;
                }
                else
                {
                    var remotes = session.RemoteGamers;
                    for (int j = 0; j < remotes.Count; j++)
                    {
                        var rg = remotes[j];
                        if (rg != null && !rg.HasLeftSession)
                        {
                            target = rg;
                            break;
                        }
                    }
                }

                if (target != null)
                {
                    session.SendRemoteData(data, offset, count, options, target);
                }

                return false;
            }
        }
        */
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
        /// CWMK2Hotkeys.SetReloadBinding("Ctrl+Shift+F3");
        /// // ... each frame (via Harmony patch) -> if (CWMK2Hotkeys.ReloadPressedThisFrame()) { ModConfig.LoadApply(); ... }
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
        internal static class CWHotkeys
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
                    Log($"[CWMK2] Reload hotkey set to \"{s}\".");
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
        /// Keeps the body small; heavy lifting should be inside ModConfig.LoadApply().
        /// </summary>
        [HarmonyPatch]
        static class Patch_Hotkey_ReloadConfig_CWMK2
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(InGameHUD), "OnPlayerInput",
                    new[] { typeof(InputManager), typeof(GameController), typeof(KeyboardInput), typeof(GameTime) });

            /// <summary>
            /// On rising-edge press: Reload INI.
            /// </summary>
            static void Postfix(InGameHUD __instance)
            {
                if (!CWHotkeys.ReloadPressedThisFrame()) return;

                try
                {
                    // Reload INI and apply runtime statics.
                    ModConfig.LoadOrCreateDefaults();
                    // Patch_Player_OnUpdate_RemoveMaxWorldHeight.RemoveMaxWorldHeight_RefreshFromConfig();

                    SendLog($"[CWMK2] Config hot-reloaded from \"{PathShortener.ShortenForLog(ModConfig.ConfigPath)}\".");
                }
                catch (Exception ex)
                {
                    SendLog($"[CWMK2] Hot-reload failed: {ex.Message}.");
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

        #region Mod Patches

        #region === Patch Helpers ===

        #region Chunk Geometry

        /// <summary>
        /// Chunk Geometry Helper
        /// ---------------------
        /// Geometry helpers for visual toggles (e.g. - xray / fullbright):
        /// - <see cref="RebuildVisible(bool)"/> queues geometry rebuild work in the engine's
        ///   _computeGeometryPool for either the visible ring of chunks (fast path) or all
        ///   24x24 loaded chunks (heavy path), then drains the queue and finalizes vertex buffers.
        /// - Each queued chunk increments its _numUsers latch so the later geometry/vb build
        ///   can safely decrement it - preventing "stuck busy" chunks that hide mined/placed updates.
        ///
        /// Notes & Safety:
        /// - Balances per-chunk usage latches (_numUsers.Increment) before enqueuing, matching the
        ///   engine's later Decrement during BuildPendingVertexBuffers().
        /// - Skips chunks that are still WAITING_TO_LOAD.
        /// - Defaults to rebuilding only the drawable ring around the player to avoid big hitches.
        /// </summary>
        public static class ChunkGeometryRefresher
        {
            // Reflect BlockTerrain._computeGeometryPool and its Add/Drain methods once.
            private static readonly FieldInfo  _geomPoolFI  = AccessTools.Field(typeof(BlockTerrain), "_computeGeometryPool");
            private static readonly MethodInfo _poolAddMI;
            private static readonly MethodInfo _poolDrainMI;

            static ChunkGeometryRefresher()
            {
                // Resolve Add(int) / Drain() once from the pool's concrete type.
                var poolType   = _geomPoolFI.FieldType;
                _poolAddMI     = AccessTools.Method(poolType, "Add",   new[] { typeof(int) });
                _poolDrainMI   = AccessTools.Method(poolType, "Drain", Type.EmptyTypes);
            }

            /// <summary>
            /// Rebuild geometry for the currently loaded; visible ring of chunks.
            /// Pass <paramref name="all"/> = true to force all 24x24 indices (heavier, may hitch).
            ///
            /// Implementation notes:
            /// - Uses _computeGeometryPool.Add(index) to schedule jobs.
            /// - Increments chunk._numUsers before enqueuing so the engine's later decrement stays balanced.
            /// - Skips WAITING_TO_LOAD chunks.
            /// - After queuing, calls Drain() and BuildPendingVertexBuffers() to apply immediately.
            /// </summary>
            public static void RebuildVisible(bool all = false)
            {
                var bt = BlockTerrain.Instance;
                if (bt == null || !bt.IsReady) return;

                var pool = _geomPoolFI.GetValue(bt);
                if (pool == null || _poolAddMI == null || _poolDrainMI == null) return;

                if (all)
                {
                    // Full 24x24 ring (576). Expect a noticeable hitch.
                    for (int idx = 0; idx < 576; idx++)
                        QueueOne(bt, pool, idx);
                }
                else
                {
                    // Only the drawable ring around the current eye chunk: Cheaper + avoids load spikes.
                    int eye = bt._currentEyeChunkIndex;
                    var baseIv = new IntVector3(eye % 24, 0, eye / 24);
                    var offs   = bt._radiusOrderOffsets; // Already computed by BlockTerrain.

                    for (int k = 0; k < offs.Length; k++)
                    {
                        var iv = new IntVector3(baseIv.X + offs[k].X, 0, baseIv.Z + offs[k].Z);
                        if (iv.X < 0 || iv.X >= 24 || iv.Z < 0 || iv.Z >= 24) continue;
                        int idx = iv.X + iv.Z * 24;
                        QueueOne(bt, pool, idx);
                    }
                }

                // Kick the jobs and then finish vertex buffers now (same path main loop uses).
                _poolDrainMI.Invoke(pool, null);
                bt.BuildPendingVertexBuffers();
            }

            /// <summary>
            /// Queue one chunk for geometry rebuild:
            /// - Skip if chunk is not yet loaded.
            /// - Increment _numUsers to balance later decrements in the geometry/VB pipeline.
            /// - Enqueue via _computeGeometryPool.Add(index).
            /// </summary>
            private static void QueueOne(BlockTerrain bt, object pool, int idx)
            {
                // Skip chunks still waiting to load.
                if (bt._chunks[idx]._action == BlockTerrain.NextChunkAction.WAITING_TO_LOAD)
                    return;

                // IMPORTANT: Balance the later Decrement() performed during VB build.
                bt._chunks[idx]._numUsers.Increment();

                // Mark as needs-geometry and enqueue the work.
                _poolAddMI.Invoke(pool, new object[] { idx });
            }
        }
        #endregion

        #endregion

        #region === Core Patches ===

        #region Change UUID At SteamWorks Lvl

        /// <summary>
        /// NOTE:
        /// Patching SteamPlayerID or modifying _steamId does NOT allow you to
        /// bypass bans or change your identity in multiplayer.
        ///
        /// Reason:
        /// The game does NOT use this managed field or property to identify players.
        /// Instead, CastleMiner Z receives the authenticated SteamID directly from
        /// steam_api.dll via SteamNetworking. That Steam-provided ID is cryptographically
        /// verified and becomes SenderId, which is what the host checks against its
        /// BanList.
        ///
        /// Therefore:
        /// Overriding SteamPlayerID only changes what your client displays. It does
        /// NOT change the actual Steam identity seen by other players or used for ban
        /// enforcement, and cannot be used to avoid bans.
        /// </summary>
        /*
        [HarmonyPatch(typeof(SteamWorks), "get_SteamPlayerID")]
        static class SteamWorks_SteamPlayerID_Patch
        {
            static void Postfix(ref ulong __result)
            {
                __result = 76561198296840001;
            }
        }
        */
        #endregion

        #endregion

        #region === Server Commands ===

        #region Server Commands

        /// <summary>
        /// Client-side intercept for public slash/bang commands typed by other players in chat.
        /// Runs before mute logic so handled commands can be consumed without leaking to public chat.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processBroadcastTextMessage")]
        internal static class Patch_CastleMinerZGame_ProcessBroadcastTextMessage_ServerCommands
        {
            [HarmonyPriority(Priority.First)]
            [HarmonyPrefix]
            private static bool Prefix(Message message)
            {
                try
                {
                    if (!(message is BroadcastTextMessage btm))
                        return true;

                    if (ServerCommandRegistry.TryHandleIncomingChatCommand(btm, message.Sender, out bool suppressOriginal))
                        return !suppressOriginal;

                    return true;
                }
                catch
                {
                    return true;
                }
            }
        }
        #endregion

        #endregion

        #region GodMode (Damage/Stamina/Oxygen)

        // Blocks damage handling when godmode is enabled.
        [HarmonyPatch(typeof(InGameHUD), nameof(InGameHUD.ApplyDamage))]
        internal static class InGameHUD_ApplyDamage_GodMode
        {
            // A Harmony Prefix runs before the original method.
            // Return false -> Skip the original entirely.
            // Return true  -> Let the original run as usual.
            static bool Prefix(InGameHUD __instance, ref float damageAmount, Vector3 damageSource)
            {
                // When godmode flag is on, skip ApplyDamage entirely:
                //  - No recoil.
                //  - No hit sound.
                //  - No damage indicator.
                //  - No health change / death.
                if (CastleWallsMk2._godEnabled)
                    return false;

                // Otherwise, run the original ApplyDamage normally.
                return true;
            }
        }

        // Blocks stamina handling when godmode is enabled.
        [HarmonyPatch(typeof(InGameHUD), nameof(InGameHUD.UseStamina))]
        internal static class InGameHUD_ApplyStamina_GodMode
        {
            // A Harmony Prefix runs before the original method.
            // Return false -> Skip the original entirely.
            // Return true  -> Let the original run as usual.
            static bool Prefix(InGameHUD __instance, ref float amount)
            {
                // When godmode flag is on, skip UseStamina entirely.
                if (CastleWallsMk2._godEnabled)
                    return false;

                // Otherwise, run the original UseStamina normally.
                return true;
            }
        }

        // Prevents oxygen drain and drowning damage when godmode is enabled.
        [HarmonyPatch]
        internal static class InGameHUD_OnUpdate_Drowning_GodMode
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(InGameHUD),
                    "OnUpdate",
                    new[] { typeof(DNAGame), typeof(GameTime) });
            }

            // Run before vanilla OnUpdate so an already-empty oxygen bar does not
            // deal one more drowning tick on the frame godmode is enabled.
            static void Prefix(InGameHUD __instance)
            {
                if (!CastleWallsMk2._godEnabled || __instance == null)
                    return;

                __instance.PlayerOxygen = 1f;
            }

            // Run after vanilla OnUpdate so the HUD stays topped off and the oxygen
            // bubbles do not visually drain while underwater.
            static void Postfix(InGameHUD __instance)
            {
                if (!CastleWallsMk2._godEnabled || __instance == null)
                    return;

                __instance.PlayerOxygen = 1f;

                // Optional defensive clamp in case health was already pushed below zero
                // before this patch was added or enabled.
                if (__instance.PlayerHealth < 0f)
                    __instance.PlayerHealth = 0f;
            }
        }

        // Hides oxygen bubbles while godmode is enabled.
        [HarmonyPatch]
        internal static class InGameHUD_OnDraw_GodModeHideOxygenBubbles
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(InGameHUD),
                    "OnDraw",
                    new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) });
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo getUnderwater = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Underwater));
                MethodInfo shouldDrawBubbles = AccessTools.Method(
                    typeof(InGameHUD_OnDraw_GodModeHideOxygenBubbles),
                    nameof(ShouldDrawBubbles));

                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(getUnderwater))
                    {
                        yield return instruction;
                        yield return new CodeInstruction(OpCodes.Call, shouldDrawBubbles);
                        continue;
                    }

                    yield return instruction;
                }
            }

            static bool ShouldDrawBubbles(bool underwater)
            {
                return underwater && !CastleWallsMk2._godEnabled;
            }
        }
        #endregion

        #region No Kick

        /// <summary>
        /// If CastleWallsMk2._noKickEnabled is true, we:
        ///   1) Read and discard the payload (bool + byte) to keep the BinaryReader aligned.
        ///   2) (Optionally) neutralize the fields.
        ///   3) Return false to skip KickMessage.RecieveData's original body.
        ///
        /// Important: We still MUST consume the bytes, otherwise the network stream
        /// will be misaligned and later reads will explode.
        /// </summary>
        [HarmonyPatch(typeof(KickMessage), "RecieveData")] // Note the game's spelling: "Recieve".
        static class KickMessage_RecieveData_SkipWhenHost
        {
            // Grab fast (safe) field refs once:
            static readonly AccessTools.FieldRef<KickMessage, bool> _bannedRef   = AccessTools.FieldRefAccess<KickMessage, bool>("Banned");
            static readonly AccessTools.FieldRef<KickMessage, byte> _playerIdRef = AccessTools.FieldRefAccess<KickMessage, byte>("PlayerID");

            static bool Prefix(KickMessage __instance, BinaryReader reader)
            {
                if (!CastleWallsMk2._noKickEnabled || CastleWallsMk2._corruptOnKickEnabled)
                    return true; // Run the original RecieveData normally.

                // Drain payload to keep the stream aligned (the original reads 1 bool + 1 byte).
                // If anything goes wrong, swallow to avoid disconnects.
                try
                {
                    _ = reader.ReadBoolean();
                    _ = reader.ReadByte();
                }
                catch { /* ignore */ }

                // (Optional) Neutralize fields so any downstream handler sees a harmless message.
                try
                {
                    _bannedRef(__instance)   = false;
                    _playerIdRef(__instance) = 0;
                }
                catch { /* Ignore if fields are properties in the build. */ }

                return false; // Skip the original method body.
            }
        }
        #endregion

        #region Infinite Items

        /// <summary>
        /// Makes every call to PlayerInventory.Consume(...) succeed while the toggle is on.
        /// - We deliberately pick only the bool-returning Consume overloads so our Prefix
        ///   can set __result and skip the original body safely.
        /// - Returning false from the Prefix means "do NOT run the original" (Harmony).
        /// </summary>
        [HarmonyPatch(typeof(PlayerInventory))]
        static class PlayerInventory_Consume_InfiniteItems_All
        {
            // Pick the precise overloads to avoid Harmony's "Ambiguous match" on methods
            // with the same name. We filter to:
            //   bool Consume(InventoryItem,int).
            //   bool Consume(InventoryItem,int,bool).
            static IEnumerable<MethodBase> TargetMethods()
            {
                var tInv = typeof(PlayerInventory);
                var tItem = typeof(InventoryItem);

                foreach (var m in AccessTools.GetDeclaredMethods(tInv))
                {
                    if (m.Name != "Consume") continue;
                    if (m.ReturnType != typeof(bool)) continue;

                    var p = m.GetParameters();

                    // Match: Consume(InventoryItem item, int amount).
                    if (p.Length == 2 &&
                        p[0].ParameterType == tItem &&
                        p[1].ParameterType == typeof(int))
                    {
                        yield return m;
                    }

                    // Match: Consume(InventoryItem item, int amount, bool ignoreInfiniteResources).
                    if (p.Length == 3 &&
                        p[0].ParameterType == tItem &&
                        p[1].ParameterType == typeof(int) &&
                        p[2].ParameterType == typeof(bool))
                    {
                        yield return m;
                    }
                }
            }

            // One Prefix handles both overloads: if the feature is enabled, force success.
            // __result=true signals the call "succeeded"; returning false skips the original code.
            static bool Prefix(ref bool __result)
            {
                if (!CastleWallsMk2._infiniteItemsEnabled)
                    return true; // Let the original method run.

                __result = true; // Pretend consumption succeeded.
                return false;    // Skip original Consume(...) body entirely.
            }
        }
        #endregion

        #region Infinite Durability

        /// </summary>
        /// Prevents durability loss by short-circuiting InventoryItem.InflictDamage().
        /// - We target the base virtual AND every override so item-specific subclasses
        ///   can't bypass the patch.
        /// - When enabled: set ItemHealthLevel to a small positive value and report
        ///   "not broken" (false). This prevents the item from being removed.
        /// </summary>
        [HarmonyPatch]
        internal static class InventoryItem_InflictDamage_InfiniteDurability
        {
            /// <summary>
            /// Target the base method and all concrete overrides in derived types.
            /// This is safer across versions than patching only the base virtual.
            /// </summary>
            static IEnumerable<MethodBase> TargetMethods()
            {
                var baseType = typeof(InventoryItem);

                // Scan the assembly once; include base + any class that overrides the method.
                foreach (var t in AccessTools.GetTypesFromAssembly(baseType.Assembly))
                {
                    if (!baseType.IsAssignableFrom(t)) continue;

                    // Find InflictDamage() with no parameters on this type.
                    var m = AccessTools.Method(t, nameof(InventoryItem.InflictDamage), Type.EmptyTypes);
                    if (m != null && m.DeclaringType == t) // Only include if this type actually declares/overrides it.
                        yield return m;
                }
            }

            /// <summary>
            /// When enabled: Prevent durability from dropping and block "break".
            /// Note: We set ItemHealthLevel to 1f (a tiny positive) so any "<= 0" checks
            /// in other code paths won't treat the item as destroyed.
            /// </summary>
            static bool Prefix(InventoryItem __instance, ref bool __result)
            {
                if (!CastleWallsMk2._infiniteDurabilityEnabled)
                    return true; // Run original normally.

                // Keep the item alive at a minimal positive health.
                __instance.ItemHealthLevel = 1f;

                // Returning false from InflictDamage means "not broken".
                __result = false;

                // Skip the original method so no durability is subtracted.
                return false;
            }
        }
        #endregion

        #region Infinite Ammo (Grenades)

        /// <summary>
        /// Prevents local grenade stack consumption when one of the mod's infinite-ammo/item toggles is enabled.
        ///
        /// Why patch here:
        /// - Vanilla grenade consumption does NOT use the gun "LocalPlayerFiredGun" message path.
        /// - Grenades are consumed inside Player.ProcessGrenadeMessage after the projectile is spawned locally.
        /// - The vanilla code only respects CastleMinerZGame.Instance.InfiniteResourceMode, not this mod's custom toggles.
        ///
        /// Effect:
        /// - Normal throws still consume grenades.
        /// - Infinite Resource Mode still works.
        /// - CastleWallsMk2 infinite ammo / infinite items can also suppress grenade consumption.
        /// - Works for normal throws, rapid fire, and aimbot grenades because they all end up here.
        /// </summary>
        [HarmonyPatch(typeof(Player), "ProcessGrenadeMessage")]
        internal static class Patch_Player_ProcessGrenadeMessage_InfiniteAmmo
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, Message message)
            {
                if (__instance == null)
                    return false;

                if (__instance.Scene != null)
                {
                    GrenadeMessage frm = (GrenadeMessage)message;

                    GrenadeProjectile re = GrenadeProjectile.Create(
                        frm.Position,
                        frm.Direction * 15f,
                        frm.SecondsLeft,
                        frm.GrenadeType,
                        __instance.IsLocal);

                    __instance.Scene.Children.Add(re);
                    __instance.Avatar.Animations.Play("Grenade_Reset", 3, TimeSpan.Zero);

                    bool shouldConsume =
                        __instance.IsLocal &&
                        !CastleMinerZGame.Instance.InfiniteResourceMode &&
                        !CastleWallsMk2._noConsumeAmmo &&
                        !CastleWallsMk2._infiniteItemsEnabled &&
                        CastleMinerZGame.Instance.GameScreen?.HUD?.ActiveInventoryItem != null &&
                        CastleMinerZGame.Instance.GameScreen.HUD.ActiveInventoryItem.ItemClass is GrenadeInventoryItemClass;

                    if (shouldConsume)
                        CastleMinerZGame.Instance.GameScreen.HUD.ActiveInventoryItem.PopOneItem();
                }

                // Skip the original method because this prefix fully reproduces it.
                return false;
            }
        }
        #endregion

        #region Infinite Clips

        /// <summary>
        /// Supplies guns with a fake 9999 reserve ammo count while infinite clips is enabled.
        /// This makes reload checks, HUD ammo checks, and auto-reload logic behave as if ammo exists.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - This patch only changes the reported ammo count.
        /// - Real inventory consumption is bypassed by the Reload patch below.
        /// </remarks>
        [HarmonyPatch(typeof(GunInventoryItemClass), "AmmoCount")]
        internal static class Patch_GunInventoryItemClass_AmmoCount_InfiClips
        {
            private static void Postfix(ref int __result)
            {
                if (!InfiniteClipsHelper.Enabled)
                    return;

                __result = 9999;
            }
        }

        /// <summary>
        /// Summary:
        /// Replaces GunInventoryItem.Reload(...) while infinite clips is enabled so reloads
        /// no longer consume real ammo from PlayerInventory.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Preserves ClipCapacity and RoundsPerReload behavior.
        /// - Keeps the gun's internal ammo display pinned to 9999.
        /// - Falls back to vanilla reload logic when disabled.
        /// </remarks>
        [HarmonyPatch(typeof(GunInventoryItem), nameof(GunInventoryItem.Reload))]
        internal static class Patch_GunInventoryItem_Reload_InfiClips
        {
            #region Private Fields

            private static readonly FieldInfo _ammoCountField =
                AccessTools.Field(typeof(GunInventoryItem), "_ammoCount");

            #endregion

            #region Reload Override

            /// <summary>
            /// Reloads from a fake reserve instead of consuming actual inventory ammo.
            /// </summary>
            private static bool Prefix(GunInventoryItem __instance, InGameHUD hud, ref bool __result)
            {
                if (!InfiniteClipsHelper.Enabled)
                    return true; // Run vanilla.

                int ammoNeeded = __instance.GunClass.ClipCapacity - __instance.RoundsInClip;

                if (ammoNeeded > __instance.GunClass.RoundsPerReload)
                    ammoNeeded = __instance.GunClass.RoundsPerReload;

                if (ammoNeeded <= 0)
                {
                    SetAmmoCount(__instance, 9999);
                    __result = false;
                    return false;
                }

                __instance.RoundsInClip += ammoNeeded;
                SetAmmoCount(__instance, 9999);

                // Match vanilla meaning:
                // true  = Keep reloading.
                // false = Clip is full / reload done.
                __result = __instance.RoundsInClip < __instance.GunClass.ClipCapacity;
                return false;
            }

            private static void SetAmmoCount(GunInventoryItem item, int value)
            {
                _ammoCountField?.SetValue(item, value);
            }
            #endregion
        }

        /// <summary>
        /// Central helper for reading CastleWallsMk2._infiClipsEnabled safely.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Uses static-field reflection because _infiClipsEnabled is a static field.
        /// - Do not use FieldRefAccess<CastleWallsMk2, bool> for this if the field is static.
        /// </remarks>
        internal static class InfiniteClipsHelper
        {
            private static readonly FieldInfo _enabledField =
                AccessTools.Field(typeof(CastleWallsMk2), "_infiClipsEnabled");

            public static bool Enabled
            {
                get
                {
                    try
                    {
                        return _enabledField != null && (bool)_enabledField.GetValue(null);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }
        #endregion

        #region FullBright

        #region Runtime Helper

        /// <summary>
        /// Turn fullbright on/off now:
        /// - Flips the global flag,
        /// - Updates every BlockType singleton's DrawFullBright,
        /// - Requeues geometry for all loaded chunks so visuals update immediately.
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
                ChunkGeometryRefresher.RebuildVisible(all: true);
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

        #region Tracers / Hitboxes / Nametags / Block ESP

        /// <summary>
        /// Injects Tracers, Hotboxes, & Hitboxes into GameScreen.gameScreen_AfterDraw.
        /// Design notes:
        /// - We draw 3D lines first (device-space), then render text via SpriteBatch.
        ///   This avoids state conflicts that can happen if SpriteBatch is active while drawing lines.
        /// - If anything goes wrong, we let the vanilla method run (return true) so name tags still render.
        /// - Reflection fields are optional: if an update renames/moves them, we degrade gracefully.
        /// </summary>
        [HarmonyPatch(typeof(GameScreen), "gameScreen_AfterDraw",
            new Type[] { typeof(object), typeof(DrawEventArgs) })]
        internal static class GameScreen_AfterDraw_TracersHitboxes
        {
            #region Reflection Cache (Optional Fields)

            // These may be null if the game updates internals. We check for null at use sites.
            static readonly FieldInfo _fiSpriteBatch = AccessTools.Field(typeof(GameScreen), "spriteBatch");
            static readonly FieldInfo _fiMainView    = AccessTools.Field(typeof(GameScreen), "mainView");
            static readonly FieldInfo _fiGame        = AccessTools.Field(typeof(GameScreen), "_game");
            static readonly FieldInfo _fiNameTagFont = AccessTools.Field(typeof(CastleMinerZGame), "_nameTagFont");

            #endregion

            /// <returns>
            /// false => We fully handled drawing (skip original).
            /// true  => Let original method run this frame (fallback / safety).
            /// </returns>
            static bool Prefix(GameScreen __instance, object sender, DrawEventArgs e)
            {
                bool drewPatch = false; // Bookkeeping; if we drew labels we'll skip vanilla.
                try
                {
                    /// <summary>
                    /// Injects Block ESP into GameScreen.gameScreen_AfterDraw.
                    /// We draw our 3D overlays, then let vanilla continue normally.
                    /// If Block ESP fails on any frame, we swallow the error so base rendering keeps running.
                    /// </summary>
                    if (CastleWallsMk2._blockEspEnabled)
                        try { BlockEspRenderer.Draw(__instance, sender, e); } catch { }

                    // Pull private state via reflection (may be null after an update).
                    var game = (CastleMinerZGame)_fiGame?.GetValue(__instance);
                    var mainView = (CameraView)_fiMainView?.GetValue(__instance);
                    if (game == null || mainView == null || mainView.Camera == null)
                        return true; // Not safe to draw-let vanilla run.

                    var session = game.CurrentNetworkSession;
                    if (session == null)
                        return true; // No players to annotate, keep vanilla behavior.

                    // Build common matrices once.
                    var device = e.Device;
                    Matrix view = mainView.Camera.View;
                    Matrix proj = mainView.Camera.GetProjection(device);
                    Matrix vp = view * proj;

                    // Read feature toggles once per frame.
                    bool drawTracers = CastleWallsMk2._tracersEnabled;
                    bool drawHitBoxes = CastleWallsMk2._hitboxesEnabled;
                    bool drawNametags = CastleWallsMk2._nametagsEnabled;

                    #region Phase 1: 3D Lines BEFORE SpriteBatch

                    // Important: draw device-space primitives before starting any SpriteBatch to avoid state clashes.
                    if (drawTracers || drawHitBoxes)
                    {
                        for (int i = 0; i < session.AllGamers.Count; i++)
                        {
                            NetworkGamer g = session.AllGamers[i];
                            if (g == null || g.IsLocal || !(g.Tag is Player player) || !player.Visible) continue;

                            // Tracer: From local player to target.
                            if (drawTracers)
                            {
                                // Generate tracers at the cursor origin.
                                var cam = game.LocalPlayer.FPSCamera;
                                Vector3 tracerStart =
                                    cam != null
                                        ? cam.WorldPosition + cam.LocalToWorld.Forward * 0.05f
                                        : game.LocalPlayer.WorldPosition + new Vector3(0f, FPSRig.EyePointHeight, 0f);

                                var line = new LineF3D(tracerStart, player.LocalPosition);
                                device.DrawLineFade(view, proj, line, CastleWallsMk2._espColor);
                            }

                            // Hitbox: Simple 1x1x2 wireframe around the player.
                            if (drawHitBoxes)
                            {
                                DrawPlayerHitBox(device, view, proj, player, CastleWallsMk2._espColor, Color.White);
                            }
                        }
                    }
                    #endregion

                    #region Phase 2: Name Labels AFTER Lines (SpriteBatch)

                    // Prefer the event's SpriteBatch (matches vanilla). If missing, create one and (optionally) cache it back.
                    var spriteBatch = e.SpriteBatch ?? new SpriteBatch(device);
                    if (e.SpriteBatch == null && _fiSpriteBatch != null)
                        _fiSpriteBatch.SetValue(__instance, spriteBatch);

                    // Font is private on the game; skip labels if we can't find it.
                    var nameFont = (SpriteFont)_fiNameTagFont?.GetValue(game);
                    if (nameFont == null)
                    {
                        // We still drew lines-consider this handled to avoid double nametags.
                        drewPatch = true;
                        return false;
                    }

                    spriteBatch.Begin();
                    try
                    {
                        for (int i = 0; i < session.AllGamers.Count; i++)
                        {
                            NetworkGamer g = session.AllGamers[i];
                            if (g == null || g.IsLocal || !(g.Tag is Player player) || !player.Visible) continue;

                            // Project a point slightly above the head into clip space and reject behind camera.
                            Vector4 clip = Vector4.Transform(player.LocalPosition + new Vector3(0f, 2f, 0f), vp);
                            if (clip.Z <= 0f || clip.W <= 0f) continue;

                            // NDC -> Screen pixels.
                            Vector3 ndc = new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
                            ndc = ndc * new Vector3(0.5f, -0.5f, 1f) + new Vector3(0.5f, 0.5f, 0f);
                            ndc *= new Vector3(Screen.Adjuster.ScreenRect.Width,
                                               Screen.Adjuster.ScreenRect.Height, 1f);

                            // Label style: Cyan w/ distance when overlays are on, vanilla white otherwise.
                            bool anyOverlay = drawNametags; //  drawTracers || drawHitBoxes;
                            string label;
                            Color color;
                            if (anyOverlay)
                            {
                                int dist = (int)Vector3.Distance(game.LocalPlayer.LocalPosition, player.LocalPosition);
                                label = $"{g.Gamertag} ({dist})";
                                color = CastleWallsMk2._espColor;
                            }
                            else
                            {
                                label = g.Gamertag;
                                color = Color.White;
                            }

                            Vector2 size = nameFont.MeasureString(label);
                            spriteBatch.DrawOutlinedText(nameFont, label,
                                new Vector2(ndc.X, ndc.Y) - size / 2f, color, Color.Black, 1);
                        }
                    }
                    finally
                    {
                        // Always end the batch-match vanilla robustness.
                        try { spriteBatch.End(); } catch { }
                    }
                    #endregion

                    // We drew overlays + labels successfully; skip original to avoid double draw.
                    drewPatch = true;
                    return false;
                }
                catch
                {
                    // Keep rendering resilient like the base game.
                    Log("WARNING: Tracers/Hitboxes AfterDraw patch crashed a frame.");
                    return true; // Let vanilla draw this frame.
                }
                finally
                {
                    // If something prevented us from drawing but no exception occurred,
                    // we already returned true/false above-no action needed here.
                    _ = drewPatch;
                }
            }

            /// <summary>
            /// Draw a wireframe around the entity's (axis-aligned) bounding box.
            /// Uses Player.PlayerAABB translated by the entity's position,
            /// then renders the 12 edges via device-space line helpers.
            /// </summary>
            private static void DrawPlayerHitBox(GraphicsDevice device, Matrix view, Matrix proj, Entity entity,
                                                 Color? color = null, Color? gradient = null)
            {
                if (device == null || entity == null) return;

                Color lineColor = color ?? Color.Lime;
                Color lineGradient = gradient ?? lineColor;

                // Get the entity-local AABB.
                BoundingBox aabb = entity.GetAABB();
                if      (entity is Player player)             aabb = player.PlayerAABB;
                else if (entity is BaseZombie baseZombie)     aabb = baseZombie.PlayerAABB;
                else if (entity is DragonEntity dragonEntity) aabb = dragonEntity.GetAABB();

                // Translate to world/local space by the entity's position.
                // NOTE: In CMZ, the render helpers are typically fed LocalPosition.
                // If lines show offset, switch to WorldPosition here.
                Vector3 origin = entity.LocalPosition;
                Vector3 min = aabb.Min + origin;
                Vector3 max = aabb.Max + origin;

                // Compute the 8 corners of the box.
                Vector3 c000 = new Vector3(min.X, min.Y, min.Z);
                Vector3 c100 = new Vector3(max.X, min.Y, min.Z);
                Vector3 c010 = new Vector3(min.X, max.Y, min.Z);
                Vector3 c110 = new Vector3(max.X, max.Y, min.Z);
                Vector3 c001 = new Vector3(min.X, min.Y, max.Z);
                Vector3 c101 = new Vector3(max.X, min.Y, max.Z);
                Vector3 c011 = new Vector3(min.X, max.Y, max.Z);
                Vector3 c111 = new Vector3(max.X, max.Y, max.Z);

                // Helper to draw one edge.
                void Edge(Vector3 a, Vector3 b) =>
                    device.DrawLine(view, proj, new LineF3D(a, b), lineColor, lineGradient);

                Edge(c000, c100); Edge(c100, c101); Edge(c101, c001); Edge(c001, c000); // Bottom face.
                Edge(c010, c110); Edge(c110, c111); Edge(c111, c011); Edge(c011, c010); // Top face.
                Edge(c000, c010); Edge(c100, c110); Edge(c101, c111); Edge(c001, c011); // Vertical edges.
            }
        }
        #endregion

        #region Rapid Fire

        /// <summary>
        /// Harmony transpiler that injects the rapid-fire flag into two boolean checks
        /// inside GunInventoryItem.ProcessInput:
        ///   • CoolDownTimer.Expired  -> (CoolDownTimer.Expired || WeaponExtensions._rapidFireOn)
        ///   • GunClass.Automatic     -> (GunClass.Automatic    || WeaponExtensions._rapidFireOn)
        /// The rest of the method remains untouched; we only OR the flag onto those spots.
        /// </summary>
        [HarmonyPatch(typeof(GunInventoryItem), nameof(GunInventoryItem.ProcessInput))]
        internal static class GunInventoryItem_ProcessInput_RapidFireTranspiler
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalInstructions)
            {
                var instructionsList = new List<CodeInstruction>(originalInstructions);

                // Targets to detect in the IL stream.
                var expiredGetterMethod = AccessTools.PropertyGetter(typeof(OneShotTimer), nameof(OneShotTimer.Expired));
                var automaticField      = AccessTools.Field(typeof(GunInventoryItemClass), "Automatic");
                var rapidFireFlagField  = AccessTools.Field(typeof(WeaponExtensions),      nameof(WeaponExtensions._rapidFireOn));

                for (int index = 0; index < instructionsList.Count; index++)
                {
                    var instruction = instructionsList[index];
                    yield return instruction;

                    // Patch: CoolDownTimer.Expired  => CoolDownTimer.Expired || _rapidFireOn.
                    if (instruction.Calls(expiredGetterMethod))
                    {
                        // Stack: [..., bool expired]
                        yield return new CodeInstruction(OpCodes.Ldsfld, rapidFireFlagField); // [..., expired, rfOn].
                        yield return new CodeInstruction(OpCodes.Or);                         // [..., expired || rfOn].
                    }

                    // Patch: GunClass.Automatic  => GunClass.Automatic || _rapidFireOn
                    if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, automaticField))
                    {
                        // Stack: [..., bool automatic]
                        yield return new CodeInstruction(OpCodes.Ldsfld, rapidFireFlagField); // [..., automatic, rfOn].
                        yield return new CodeInstruction(OpCodes.Or);                         // [..., automatic || rfOn].
                    }
                }
            }
        }

        /// <summary>
        /// Postfix on InGameHUD.OnPlayerInput that enables rapid-fire grenade throws
        /// when WeaponExtensions._rapidFireOn is true:
        ///   • Requires USE to be held.
        ///   • Sends GrenadeMessage immediately (bypassing the cook/throw gate).
        ///   • Clears/rekicks the animation so visuals still move.
        /// A simple 16 ms limiter keeps it around ~60 FPS cadence (tweak as desired).
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput")]
        internal static class InGameHUD_OnPlayerInput_RapidGrenades
        {
            // Reflection handles for required fields.
            static readonly FieldInfo _fieldGame               = AccessTools.Field(typeof(InGameHUD), "_game");
            static readonly FieldInfo _fieldControllerMapping  = AccessTools.Field(typeof(CastleMinerZGame), "_controllerMapping");

            // Simple cadence limiter so we don't spam multiple sends in the same frame.
            static DateTime _nextSendUtc = DateTime.MinValue;

            static void Postfix(InGameHUD __instance)
            {
                if (!WeaponExtensions._rapidFireOn) return;

                var inventory   = __instance?.PlayerInventory;
                var activeItem  = inventory?.ActiveInventoryItem;
                var player      = __instance?.LocalPlayer;

                if (player == null || !(activeItem?.ItemClass is GrenadeInventoryItemClass grenadeItemClass))
                    return;

                // Require USE to be held for continuous throwing.
                var game               = (CastleMinerZGame)_fieldGame.GetValue(__instance);
                var controllerMapping  = (CastleMinerZControllerMapping)_fieldControllerMapping.GetValue(game);
                if (controllerMapping?.Use.Held != true)
                    return;

                // ~60 FPS limiter (set to 0 ms if you truly want "every tick").
                var nowUtc = DateTime.UtcNow;
                if (nowUtc < _nextSendUtc) return;
                _nextSendUtc = nowUtc.AddMilliseconds(16);

                // Orientation to use for this toss: follow the current camera basis.
                if (!(player.FPSCamera is PerspectiveCamera camera)) return;
                Matrix localToWorld = camera.LocalToWorld;

                // Send immediately (skips the Cook -> Throw state machine).
                GrenadeMessage.Send((LocalNetworkGamer)player.Gamer, localToWorld, grenadeItemClass.GrenadeType, 5f /* vanilla full fuse */);

                // Prevent the animation branch from also sending a throw this frame.
                player.PlayGrenadeAnim     = false;
                player.ReadyToThrowGrenade = false;
                player.Avatar?.Animations?.ClearAnimation(3, TimeSpan.Zero);

                // Kick a very short visual so the arm still moves (purely cosmetic).
                player.PlayGrenadeAnim     = true;
                player.ReadyToThrowGrenade = true;
                // (If you also fast-forward track 3 elsewhere, that still applies.)
            }
        }
        #endregion

        #region No Recoil

        /// <summary>
        /// Harmony prefix: If no-recoil is enabled, skip Player.ApplyRecoil(Angle),
        /// preventing any camera/aim kick regardless of the caller.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ApplyRecoil), new[] { typeof(Angle) })]
        internal static class Player_ApplyRecoil_NoRecoil
        {
            [HarmonyPrefix]
            private static bool Prefix() => !CastleWallsMk2._noGunRecoilEnabled;
        }
        #endregion

        #region Aimbot

        #region Aimbot - Ballistic Aim Override

        /// <summary>
        /// Forces the local camera/player to face the current aimbot target every input tick.
        /// Flow:
        ///  1) Bail if "face toward enemy" is off or there's no target.
        ///  2) Get the local PerspectiveCamera.
        ///  3) Prefer ballistic aim (if enabled + supported); else fall back to flat aim.
        ///  4) Convert the chosen orientation into yaw/pitch and apply to the local player.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), "OnPlayerInput")]
        internal static class InGameHUD_OnPlayerInput_ForceAim
        {
            static void Postfix()
            {
                // Gate: Only act when the feature is on and a target exists.
                if (!CastleWallsMk2.AnyAimbotEnabled() || !AimbotController.FaceTorwardsEnemy || AimbotController.Target == null) return;

                var game  = CastleMinerZGame.Instance;
                var local = game?.LocalPlayer;
                if (!(local?.FPSCamera is PerspectiveCamera cam)) return;

                // Prefer ballistic correction if: Active item is a gun AND the toggle allows drop
                // correction AND we can build a ballistic aim matrix. Otherwise, use flat aim math.
                if (CastleMinerZGame.Instance?.LocalPlayer?.PlayerInventory?.ActiveInventoryItem is GunInventoryItem gi && AimbotBallistics.ShouldApplyBulletDrop(gi, CastleWallsMk2._aimbotBulletDropEnabled) &&
                    AimbotBallistics.TryMakeAimL2W_WithBallistics(cam, AimbotController.Target, AimbotController.Offset, gi.GunClass, out Matrix aimL2W))
                { /* ok - using ballistic orientation */ }
                else
                {
                    if (!AimMath.TryMakeAimL2W(cam, AimbotController.Target,
                                               AimbotController.Offset, AimbotController.LeadSpeed,
                                               AimbotController.Roll, out aimL2W))
                        return;
                }

                // Convert the aim matrix to a facing: CreateWorld stores camera "look" on Backward.
                Vector3 dir = Vector3.Normalize(aimL2W.Backward);
                float yaw = (float)Math.Atan2(dir.X, dir.Z);
                float pitch = -(float)Math.Asin(MathHelper.Clamp(dir.Y, -1f, 1f));

                // Apply to the local player (clamp pitch to engine-safe range).
                local.LocalRotation = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f);
                local.TorsoPitch = Angle.FromRadians(MathHelper.Clamp(pitch,
                                         MathHelper.ToRadians(-89f), MathHelper.ToRadians(89f)));
            }
        }
        #endregion

        #region Aimbot - Grenade Aim Override

        /// <summary>
        /// Intercepts local grenade throws right before the network message is sent and
        /// swaps the orientation if AimbotGrenades prepared one. The parameter name must
        /// match the game's method signature.
        /// </summary>
        [HarmonyPatch(typeof(GrenadeMessage), nameof(GrenadeMessage.Send))]
        static class GrenadeMessage_Send_AimOverride
        {
            // Parameter name must match the real method ("orientation").
            static void Prefix(ref Matrix orientation)
            {
                // One-shot: If the aimbot stashed an orientation this frame, use it.
                if (AimbotGrenades.TryConsumePending(out var aimed))
                    orientation = aimed;
            }
        }
        #endregion

        #region Aimbot - HUD Crosshair Overlay

        /// <summary>
        /// Draws a simple "+" marker at the target's projected head/offset position after the game draws.
        /// Notes:
        ///  - Uses reflection to grab the SpriteBatch, font, and view.
        ///  - Transforms world -> clip -> NDC -> screen coordinates.
        ///  - Draws an outlined cyan "+" centered on the screen position.
        /// </summary>
        [HarmonyPatch(typeof(GameScreen), "gameScreen_AfterDraw", new[] { typeof(object), typeof(DrawEventArgs) })]
        internal static class GameScreen_AfterDraw_AimMarker
        {
            // Cached refs
            static readonly FieldInfo _fiMainView    = AccessTools.Field(typeof(GameScreen),       "mainView");
            static readonly FieldInfo _fiGame        = AccessTools.Field(typeof(GameScreen),       "_game");
            static readonly FieldInfo _fiNameTagFont = AccessTools.Field(typeof(CastleMinerZGame), "_nameTagFont");

            static void Postfix(GameScreen __instance, object sender, DrawEventArgs e)
            {
                try
                {
                    // Gate: Only act when the feature is on.
                    if (!CastleWallsMk2.AnyAimbotEnabled()) return;

                    // Snapshot state up-front (Target can change mid-frame).
                    if (!(AimbotController.Target is Entity target) || !AimbotController.ShowTargetCrosshair)
                        return;

                    var game = (CastleMinerZGame)_fiGame.GetValue(__instance);
                    if (game?.CurrentNetworkSession == null)
                        return;

                    var mainView = (CameraView)_fiMainView.GetValue(__instance);
                    var cam = mainView?.Camera;
                    if (cam == null)
                        return;

                    // Prefer a robust aim point (works for Player/Zombie/Dragon).
                    if (!AimTargeting.TryGetAimPointAndAABB(target, AimbotController.Offset, out var world, out _))
                        return;

                    // Build view-projection (device can be null only on device-lost; guard anyway)
                    var dev = e?.Device;
                    if (dev == null) return;

                    Matrix view = cam.View;
                    Matrix proj = cam.GetProjection(dev);
                    Matrix vp = view * proj;

                    // World -> clip
                    Vector4 clip = Vector4.Transform(new Vector4(world, 1f), vp);
                    if (clip.W <= 0f || clip.Z <= 0f) return; // behind camera or invalid

                    // Clip -> NDC -> screen
                    Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
                    ndc = ndc * new Vector3(0.5f, -0.5f, 1f) + new Vector3(0.5f, 0.5f, 0f);

                    float sx = ndc.X * Screen.Adjuster.ScreenRect.Width;
                    float sy = ndc.Y * Screen.Adjuster.ScreenRect.Height;
                    var screen = new Vector2(sx, sy);

                    // Try to draw with a font cross ("+"). If font isn't ready yet, just skip drawing.
                    var font = (SpriteFont)_fiNameTagFont.GetValue(game);
                    var sb = e.SpriteBatch;
                    if (font == null || sb == null) return;

                    sb.Begin();
                    try
                    {
                        const string glyph = "+";
                        var size = font.MeasureString(glyph);
                        sb.DrawOutlinedText(font, glyph, screen - size / 2f, Color.Cyan, Color.Black, 1);
                    }
                    finally { try { sb.End(); } catch { /* device state raced; ignore once */ } }
                }
                catch (Exception ex)
                {
                    // Keep it resilient; log and carry on.
                    ModLoader.LogSystem.Log("[AimMarker.Postfix] " + ex);
                }
            }
        }
        #endregion

        #endregion

        #region No Gun Cooldown

        /// <summary>
        /// Target: GunInventoryItem.ProcessInput(...).
        /// Guns override ProcessInput, so we have to patch this type separately.
        /// We inject: 'CoolDownTimer.Expired || CastleWallsMk2._noGunCooldownEnabled'.
        ///
        /// Notes:
        /// - We match the property getter MethodInfo of OneShotTimer.Expired.
        /// - In IL, the getter leaves a bool on the stack. We push our flag (ldsfld) and OR it.
        /// - This is "surgical": Only affects the exact spots that read .Expired.
        /// </summary>
        [HarmonyPatch(typeof(GunInventoryItem), nameof(GunInventoryItem.ProcessInput))]
        internal static class GunInventoryItem_ProcessInput_NoCooldown
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> T(IEnumerable<CodeInstruction> original)
            {
                var list = new List<CodeInstruction>(original);

                var expiredGetter = AccessTools.PropertyGetter(typeof(OneShotTimer), nameof(OneShotTimer.Expired));
                var flagField = AccessTools.Field(typeof(CastleWallsMk2), nameof(CastleWallsMk2._noGunCooldownEnabled));

                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i];
                    yield return ins; // Emit original.

                    // Turn: if (CoolDownTimer.Expired) ...
                    // into: if (CoolDownTimer.Expired || CastleWallsMk2._noGunCooldownEnabled) ...
                    if (ins.Calls(expiredGetter))
                    {
                        // Stack: [..., bool expired]
                        yield return new CodeInstruction(OpCodes.Ldsfld, flagField); // [..., expired, flag].
                        yield return new CodeInstruction(OpCodes.Or);                // [..., expired || flag].
                    }
                }
            }
        }
        #endregion

        #region Vanish (Handels Vanish, Corrupt, & Ghost)

        #region Fire-Rocket Message

        /// <summary>
        /// FIRE ROCKET - Vanish behavior.
        /// Goal: When vanish is ON, keep the local effects intact (sound, recoil, HUD),
        ///       but do NOT replicate the rocket to other players.
        /// Strategy:
        ///   • Build a normal FireRocketMessage so our own ReceiveData path still runs.
        ///   • Unicast it only to yourself (echo).
        ///   • Skip the original broadcast (prevents replication to peers).
        /// Fallback: If anything fails (session null, reflection hiccup), let vanilla run.
        /// </summary>
        [HarmonyPatch(typeof(FireRocketMessage), nameof(FireRocketMessage.Send))]
        internal static class FireRocketMessage_Send_VanishSpoof
        {
            static bool Prefix(LocalNetworkGamer from, Matrix orientation, InventoryItemIDs weaponType, bool guided)
            {
                if (!CastleWallsMk2._vanishEnabled)
                    return true; // Run original.

                var game = CastleMinerZGame.Instance;
                var session = game?.CurrentNetworkSession;
                if (session == null || from == null)
                    return true; // Safe fallback.

                try
                {
                    // Build message like vanilla (so local systems consuming ReceiveData stay happy).
                    var msg = MessageBridge.Get<FireRocketMessage>();
                    var dir = orientation.Forward;
                    msg.Direction  = dir;
                    msg.Position   = orientation.Translation + dir;
                    msg.WeaponType = weaponType;
                    msg.Guided     = guided;

                    // Echo ONLY to yourself so our client runs its normal effects.
                    // (This avoids desync in local state machines that listen for the message.)
                    MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, (NetworkGamer)from });

                    // IMPORTANT: Do NOT send to other players - that's the "vanish" effect.
                    return false; // Skip original broadcast.
                }
                catch
                {
                    // If anything goes wrong, fall back to vanilla so you don't break firing.
                    return true;
                }
            }
        }
        #endregion

        #region Grenade Message

        /// <summary>
        /// GRENADE - Vanish behavior.
        /// Goal: Keep local throw/animation/trajectory behaviors; block replication to others.
        /// Notes are identical to FireRocketMessage: echo self, skip broadcast.
        /// </summary>
        [HarmonyPatch(typeof(GrenadeMessage), nameof(GrenadeMessage.Send))]
        internal static class GrenadeMessage_Send_VanishSpoof
        {
            static bool Prefix(LocalNetworkGamer from, Matrix orientation, GrenadeTypeEnum grenadeType, float secondsLeft)
            {
                if (!CastleWallsMk2._vanishEnabled)
                    return true; // Run original.

                var game = CastleMinerZGame.Instance;
                var session = game?.CurrentNetworkSession;
                if (session == null || from == null)
                    return true; // Safe fallback.

                try
                {
                    // Build message like vanilla (so local systems consuming ReceiveData stay happy).
                    var msg = MessageBridge.Get<GrenadeMessage>();
                    var dir = orientation.Forward;
                    msg.Direction   = dir;
                    msg.Position    = orientation.Translation + dir;
                    msg.GrenadeType = grenadeType;
                    msg.SecondsLeft = secondsLeft;

                    // Echo ONLY to yourself so our client runs its normal effects.
                    // (This avoids desync in local state machines that listen for the message.)
                    MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, (NetworkGamer)from });

                    // IMPORTANT: Do NOT send to other players - that's the "vanish" effect.
                    return false; // Skip original broadcast.
                }
                catch
                {
                    // If anything goes wrong, fall back to vanilla so you don't break firing.
                    return true;
                }
            }
        }
        #endregion

        #region Gunshot Message

        /// <summary>
        /// GUNSHOT - Vanish behavior.
        /// Goal: Preserve local gunfire effects; block replication so others don't see/hear the shot.
        /// Implementation mirrors vanilla direction jitter + normalization, then self-echo.
        /// </summary>
        [HarmonyPatch(typeof(GunshotMessage), nameof(GunshotMessage.Send))]
        internal static class GunshotMessage_Send_VanishSpoof
        {
            static bool Prefix(LocalNetworkGamer from, Matrix m, Angle innacuracy, InventoryItemIDs item, bool addDropCompensation)
            {
                // Only intervene when vanish is enabled; otherwise let the game do its normal broadcast.
                if (!CastleWallsMk2._vanishEnabled)
                    return true; // run original

                var game = CastleMinerZGame.Instance;
                var session = game?.CurrentNetworkSession;
                if (session == null || from == null)
                    return true; // safe fallback: run original if anything is missing

                try
                {
                    // Build a pooled message just like the game does.
                    var msg = MessageBridge.Get<GunshotMessage>();

                    // Reproduce vanilla direction math.
                    Vector3 shot = m.Forward;
                    if (addDropCompensation)
                        shot += m.Up * 0.015f;

                    Matrix jitter = Matrix.CreateRotationX(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians)) *
                                    Matrix.CreateRotationY(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));

                    shot          = Vector3.TransformNormal(shot, jitter);
                    msg.Direction = Vector3.Normalize(shot);
                    msg.ItemID    = item;

                    // Echo ONLY to yourself so our client runs its normal effects.
                    // (This avoids desync in local state machines that listen for the message.)
                    MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, (NetworkGamer)from });

                    // IMPORTANT: Do NOT send to other players - that's the "vanish" effect.
                    return false; // Skip original broadcast.
                }
                catch
                {
                    // If anything goes wrong, fall back to vanilla so you don't break firing.
                    return true;
                }
            }
        }
        #endregion

        #region Change-Carried-Item Message

        /// <summary>
        /// CHANGECARRIEDITEM - Vanish behavior.
        /// Goal: Others should see you with bare hands; you still see your real item locally.
        /// Strategy:
        ///   • Build one pooled ChangeCarriedItemMessage and unicast it to EVERYONE:
        ///       - To self (IsLocal): send the REAL item (keeps local HUD/animations aligned).
        ///       - To others:         send BareHands (hides your weapon while vanished).
        ///   • Skip the original broadcast.
        /// Tradeoffs:
        ///   • This actively tells peers "your item changed to BareHands" instead of staying silent.
        ///     If you prefer *no* network noise, you could mirror the gunshot/rocket approach:
        ///     echo only to self and send nothing to peers (they'd keep your older item).
        /// </summary>
        [HarmonyPatch(typeof(ChangeCarriedItemMessage), nameof(ChangeCarriedItemMessage.Send))]
        internal static class ChangeCarriedItemMessage_Send_VanishSpoof
        {
            static bool Prefix(LocalNetworkGamer from, InventoryItemIDs id)
            {
                // Only intervene when vanish is enabled; otherwise let the game do its normal broadcast.
                if (!CastleWallsMk2._vanishEnabled)
                    return true; // Run original.

                var game = CastleMinerZGame.Instance;
                var session = game?.CurrentNetworkSession;
                if (session == null || from == null)
                    return true; // Safe fallback: Run original if anything's missing.

                // Build a spoof message instance (same type the game would send)
                var msg = MessageBridge.Get<ChangeCarriedItemMessage>();

                // Unicast to everyone. Self gets REAL item; others get BareHands.
                // (DoSend respects the per-recipient overload and the message's Reliable SendDataOptions.)
                for (int i = 0; i < session.AllGamers.Count; i++)
                {
                    var g = session.AllGamers[i];
                    if (g == null || g.HasLeftSession) continue;

                    if (g.IsLocal) // Local keeps real item so our UI/animations stay correct.
                        msg.ItemID = game.LocalPlayer.PlayerInventory.ActiveInventoryItem.ItemClass.ID;
                    else           // Hide weapon from others.
                        msg.ItemID = InventoryItemIDs.BareHands;

                    MessageBridge.DoSendDirect.Invoke(msg, new object[] { from, g });
                }

                // Skip the original static Send(..) broadcast entirely.
                // This prevents the real item id from leaking to peers and also avoids echoing back to us.
                return false;
            }
        }
        #endregion

        #region Player-Update Message

        /// <summary>
        /// PLAYERUPDATE - Vanish spoof.
        /// Goal: On the moment vanish turns ON, send spoofed position update that places
        ///       your replicated puppet far away (or at a benign spawn spot), then let
        ///       vanilla updates continue (or be suppressed by your other vanish rules).
        ///
        /// Behavior:
        ///   • Spoof goes to the normal broadcast (so everyone consumes it the same).
        ///   • Subsequent PlayerUpdateMessage.Send calls after that frame return false (no-op).
        ///
        /// Host nuance:
        ///   • Hosts tend to spawn at WorldInfo.DefaultStartLocation; non-hosts at 3,Y,3.
        /// </summary>
        [HarmonyPatch(typeof(PlayerUpdateMessage), nameof(PlayerUpdateMessage.Send))]
        internal static class PlayerUpdateMessage_Send_Vanish
        {
            static bool Prefix(LocalNetworkGamer from, Player player, CastleMinerZControllerMapping input)
            {
                // Vanish is OFF, use vanilla path.
                if (!CastleWallsMk2._vanishEnabled      &&
                    !CastleWallsMk2._corruptWorldActive &&
                    !CastleWallsMk2._ghostModeEnabled)
                    return true;

                try
                {
                    // Build and send a spoofed update. If we don't have a cached spoof yet,
                    // fall back to current position (still prevents running GetSpoofPosition()).
                    var msg = MessageBridge.Get<PlayerUpdateMessage>();

                    // Resolve spoof location from helper.
                    Vector3 spoofLocation = VanishHelpers.ResolveVanishSpoofPosition();

                    // Fill fields (same as original) BUT override LocalPosition.
                    msg.LocalPosition       = spoofLocation;
                    msg.WorldVelocity       = default;
                    msg.LocalRotation       = default;
                    msg.Movement            = default;
                    msg.TorsoPitch          = default;
                    msg.Using               = false;
                    msg.Shouldering         = false;
                    msg.Reloading           = false;
                    msg.PlayerMode          = default;
                    msg.Dead                = VanishHelpers.ShouldVanishPlayerBeDead(); // Fully "ghosted" (inert) to others.
                    msg.ThrowingGrenade     = false;
                    msg.ReadyToThrowGrenade = false;

                    // Send (protected) via reflection.
                    MessageBridge.DoSend.Invoke(msg, new object[] { from });

                    // We handled it - skip the original method.
                    return false;
                }
                catch
                {
                    // If anything goes wrong, fall back to vanilla path (better to degrade gracefully).
                    return true;
                }
            }
        }
        #endregion

        #region Vanish Helpers

        internal static class VanishHelpers
        {
            /// <summary>
            /// Resolves the replicated position others should see while vanish is enabled.
            /// Priority:
            /// 1) Zero:    Override when _ghostModeEnabled is active.
            /// 2) InPlace: Override when _corruptWorldActive is active.
            /// 3) InPlace: Use the local player's current position.
            /// 4) Spawn:   Use the vanilla-style host start location or non-host spawn-ish fallback.
            /// 5) Zero:    Use the absolute zero position.
            /// 6) Default: Use the vanilla-style non-host spawn-ish fallback.
            /// </summary>
            public static Vector3 ResolveVanishSpoofPosition()
            {
                if (CastleWallsMk2._ghostModeEnabled)
                    return Vector3.Zero;

                if (CastleWallsMk2._corruptWorldActive)
                    return CastleWallsMk2._vanishLocation;

                switch (CastleWallsMk2._vanishScope)
                {
                    case CastleWallsMk2.VanishSelectScope.InPlace:
                        return CastleWallsMk2._vanishLocation;

                    case CastleWallsMk2.VanishSelectScope.Spawn:
                        if (CastleMinerZGame.Instance?.MyNetworkGamer?.IsHost == true)
                            return BlockTerrain.Instance.FindTopmostGroundLocation(WorldInfo.DefaultStartLocation); // The host will never spawn at 3,Y,3, only 8,Y,8.
                        else
                            return BlockTerrain.Instance.FindTopmostGroundLocation(new Vector3(3f, 128f, 3f));      // Everyone else uses the initial first join location.

                    case CastleWallsMk2.VanishSelectScope.Distant:
                        return new Vector3((float)-1.661535E+35);  // [^.-]...

                    case CastleWallsMk2.VanishSelectScope.Zero:
                        return Vector3.Zero;

                    default:
                        return BlockTerrain.Instance.FindTopmostGroundLocation(new Vector3(3f, 128f, 3f));
                }
            }

            /// <summary>
            /// Resolves whether the replicated vanished player should appear dead to others.
            /// Priority:
            /// 1) Ghost Mode:    Always dead.
            /// 2) Corrupt World: Always alive.
            /// 3) Vanish Dead:   Follows the vanish dead toggle.
            /// </summary>
            public static bool ShouldVanishPlayerBeDead()
            {
                if (CastleWallsMk2._ghostModeEnabled)
                    return true;

                if (CastleWallsMk2._corruptWorldActive)
                    return false;

                return CastleWallsMk2._vanishIsDeadEnabled;
            }
        }
        #endregion

        #endregion

        #region Player Position

        /// <summary>
        /// Adds a third line under the distance HUD showing the player's
        /// world position (when enabled). Lives independently from other
        /// OnDraw transpiler mods that changes this blocks label.
        /// </summary>
        [HarmonyPatch(typeof(InGameHUD), nameof(InGameHUD.DrawDistanceStr))]
        internal static class InGameHUD_DrawDistanceStr_AddCoords
        {
            [HarmonyPostfix]
            private static void Postfix(InGameHUD __instance, SpriteBatch spriteBatch)
            {
                try
                {
                    if (!CastleWallsMk2._playerPositionEnabled) return;

                    var game  = CastleMinerZGame.Instance;
                    var local = game?.LocalPlayer;
                    if (game?.CurrentNetworkSession == null || local == null /* || local.Dead */)
                        return; // Not in-game yet or no local player - skip.

                    // Position (in blocks). Use invariant formatting for consistent decimals.
                    Vector3 w      = local.WorldPosition;
                    string posText = FormattableString.Invariant(
                        $"({Math.Floor(w.X)}, {Math.Floor(w.Y)}, {Math.Floor(w.Z)})");

                    // Match the font/scale/placement used by DrawDistanceStr.
                    var font    = (SpriteFont)AccessTools.Field(typeof(CastleMinerZGame), "_medFont").GetValue(game);
                    float scale = Screen.Adjuster.ScaleFactor.Y;
                    var screen  = Screen.Adjuster.ScreenRect;

                    // Y is one more line below the "max distance" line:
                    //   DrawDistanceStr draws:
                    //   - Line 0: "Distance"            at Top + 0*LineSpacing*scale.
                    //   - Line 1: "<current>-<max> Max" at Top + 1*LineSpacing*scale.
                    // We draw:
                    //   - Line 2: "Pos: (x, y, z)"      at Top + 2*LineSpacing*scale.
                    float y = screen.Top + 2f * font.LineSpacing * scale;

                    // Right-align with 10px padding (just like DrawDistanceStr).
                    float textW = font.MeasureString(posText).X * scale;
                    float x     = screen.Right - (textW + 10f * scale);

                    // Use the same tint used by the HUD (MenuAqua * 0.75f).
                    Color MenuAqua = new Color(53, 170, 253);
                    var tint = MenuAqua * 0.75f;

                    // IMPORTANT: We're inside the caller's active Begin/End.
                    // Do NOT call spriteBatch.Begin/End here.
                    spriteBatch.DrawString(
                        font,
                        posText,
                        new Vector2(x, y),
                        tint,
                        0f,
                        Vector2.Zero,
                        scale,
                        SpriteEffects.None,
                        0f
                    );
                }
                catch { /* Never let HUD drawing crash. */ }
            }
        }
        #endregion

        #region Pickup Range

        /// <summary>
        /// Extends pickup radius without altering vanilla logic.
        /// - Vanilla still uses DistanceSquared(pp, LocalPosition) < 4f (≈2-unit radius).
        /// - When _pickupRangeEnabled is true, this Postfix additionally grants pickup
        ///   if the player (eye at +1f Y) is within _extendedPickupRange (squared).
        /// - Respects vanilla timing (_readyForPickup) and bails if already picked up.
        /// - Uses squared distance for performance; no sqrt.
        /// - Safe: Adds behavior only; disabling the feature restores vanilla behavior.
        /// </summary>
        [HarmonyPatch(typeof(PickupEntity), "OnUpdate")]
        internal static class PickupEntity_OnUpdate_ExtendedPickup_Postfix
        {
            // Harmony can read/write private fields by parameter name pattern:
            //   ___<fieldName>  (3 underscores + original name)
            // Since the game fields already start with '_' you see 4 underscores here.
            static void Postfix(
                PickupEntity __instance,     // The instance being updated (for its position).
                ref bool ____readyForPickup, // Mirrors private field _readyForPickup.
                ref bool ____pickedUp)       // Mirrors private field _pickedUp.
            {
                // Feature gate + early out: don't interfere if vanilla has already picked it up.
                if (____pickedUp || !CastleWallsMk2._pickupRangeEnabled)
                    return;

                // Vanilla precondition: Only when we have a valid local player.
                var lp = CastleMinerZGame.Instance?.LocalPlayer;
                if (lp == null || !lp.ValidLivingGamer)
                {
                    // Vanilla sets _readyForPickup = true in this case, but also short-circuits.
                    // We follow the same spirit and do nothing here.
                    return;
                }

                // Respect vanilla timing: Only try extended range once the item became "ready".
                if (!____readyForPickup)
                    return;

                // Match vanilla measurement point: Player's position with +1f Y offset (eye height).
                Vector3 pp = lp.LocalPosition;
                pp.Y += 1f;

                // Configured radius (units, not squared). Ignore non-positive values.
                // Note: Vanilla compares DistanceSquared(...) < 4f (i.e., radius = 2 units).
                float r = CastleWallsMk2._extendedPickupRange;
                if (r <= 0f) return;

                float r2 = r * r; // Compare squared distance to avoid sqrt.

                // If within extended radius, trigger the same pickup side-effects as vanilla.
                if (Vector3.DistanceSquared(pp, __instance.LocalPosition) < r2)
                {
                    ____pickedUp = true;
                    PickupManager.Instance.PlayerTouchedPickup(__instance);
                }
            }
        }
        #endregion

        #region Xray

        #region Block - Enum Helper

        /// <summary>
        /// Fast mapping from <see cref="BlockType"/> singletons to their <see cref="BlockTypeEnum"/>.
        /// Built lazily the first time <see cref="GetEnum(BlockType)"/> is called.
        /// </summary>
        internal static class BlockTypeUtil
        {
            private static Dictionary<BlockType, BlockTypeEnum> _map;

            /// <summary>
            /// Returns the <see cref="BlockTypeEnum"/> for a given <see cref="BlockType"/> instance,
            /// or <see cref="BlockTypeEnum.Empty"/> if unknown.
            /// </summary>
            public static BlockTypeEnum GetEnum(BlockType bt)
            {
                if (bt == null) return BlockTypeEnum.Empty;
                if (_map == null) BuildMap();
                return _map.TryGetValue(bt, out var e) ? e : BlockTypeEnum.Empty;
            }

            /// <summary>
            /// Enumerates all enum values and builds a dictionary from each singleton instance
            /// returned by <see cref="BlockType.GetType(BlockTypeEnum)"/> to its enum.
            /// Some enum values may alias or throw; those are safely ignored.
            /// </summary>
            private static void BuildMap()
            {
                _map = new Dictionary<BlockType, BlockTypeEnum>(capacity: 128);

                foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
                {
                    try
                    {
                        var inst = BlockType.GetType(e);
                        if (inst != null && !_map.ContainsKey(inst))
                            _map[inst] = e;
                    }
                    catch
                    {
                        // Some enum entries may be aliases or throw in certain builds; ignore.
                    }
                }
            }
        }
        #endregion

        #region X-Ray Configuration

        /// <summary>
        /// Simple feature flag and visible block allowlist for the x-ray effect.
        /// When enabled, everything not in <see cref="KeepVisible"/> is treated as air *during meshing*.
        /// </summary>
        internal static class XRayConfig
        {
            public static bool Enabled;

            /// <summary>
            /// Only these blocks remain "solid" to the geometry builder; everything else is treated as Air.
            /// Extend this allowlist with any blocks you want to see while x-ray is on.
            /// </summary>
            public static readonly HashSet<BlockTypeEnum> KeepVisible = new HashSet<BlockTypeEnum>
            {
                // Ores.
                BlockTypeEnum.CopperOre,
                BlockTypeEnum.IronOre,
                BlockTypeEnum.GoldOre,
                BlockTypeEnum.DiamondOre,
                BlockTypeEnum.CoalOre,

                // Optional: Useful markers / containers / light sources. //

                // Storages.
                BlockTypeEnum.Crate,
                BlockTypeEnum.CrateStone,
                BlockTypeEnum.CrateCopper,
                BlockTypeEnum.CrateIron,
                BlockTypeEnum.CrateGold,
                BlockTypeEnum.CrateDiamond,
                BlockTypeEnum.CrateBloodstone,

                // Player made walls.
                BlockTypeEnum.CopperWall,
                BlockTypeEnum.IronWall,
                BlockTypeEnum.GoldenWall,
                BlockTypeEnum.DiamondWall,

                // Crafting stations.
                BlockTypeEnum.TeleportStation,
                BlockTypeEnum.SpawnPointBasic,

                // Light sources.
                BlockTypeEnum.Torch,
                BlockTypeEnum.Lantern,
                BlockTypeEnum.LanternFancy,
            };

            /// <summary>
            /// Returns true if x-ray is enabled and the provided type should be hidden (treated as air).
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool ShouldHide(BlockTypeEnum t) => Enabled && !KeepVisible.Contains(t);
        }
        #endregion

        #region Thread-Local Guard Around Terrain Meshing

        /// <summary>
        /// Limits x-ray behavior to the terrain mesher threads only.
        /// We set a thread-static flag before meshing and clear it after, so regular gameplay
        /// reads (physics, gameplay logic, etc.) are unaffected.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), "DoThreadedComputeGeometry")]
        internal static class XRayTLS
        {
            [ThreadStatic] public static bool InComputeGeometry;

            static void Prefix()  => InComputeGeometry = true;
            static void Postfix() => InComputeGeometry = false;
        }
        #endregion

        #region Hide Blocks During Mesher Reads

        /// <summary>
        /// The ONLY place we actually hide blocks: hijack the mesher's GetBlockAt(index) reads.
        /// IMPORTANT: The packed block int contains type + flags + light; we must extract type via
        /// <see cref="Block.GetTypeIndex(int)"/> and, when hiding, synthesize "air" while preserving
        /// light so exposed faces aren't pitch-black underground.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), "GetBlockAt", new[] { typeof(int) })]
        internal static class BlockTerrain_GetBlockAt_XRay
        {
            // Fast field-ref for BlockTerrain._blocks (private int[]).
            private static readonly AccessTools.FieldRef<BlockTerrain, int[]>
                _blocksRef = AccessTools.FieldRefAccess<BlockTerrain, int[]>("_blocks");

            // Optional brightness floors so faces don't render pitch-black underground.
            private const int MinSun   = 0;  // 0 keeps it realistic; try 8..15 if you want it brighter.
            private const int MinTorch = 10; // 0 = leave as-is; 8..12 = gentle glow.

            static bool Prefix(BlockTerrain __instance, int index, ref int __result)
            {
                // Only intercept during mesh builds and when x-ray is enabled.
                if (!(XRayTLS.InComputeGeometry && XRayConfig.Enabled))
                    return true;

                var blocks = _blocksRef(__instance);
                int raw    = blocks[index];

                // Correctly extract the type from the packed int.
                BlockTypeEnum type = Block.GetTypeIndex(raw);

                if (XRayConfig.ShouldHide(type))
                {
                    // Build "synthetic air" from the real packed value so we preserve/adjust lighting.
                    int air = Block.SetType(raw, BlockTypeEnum.Empty);
                    air     = Block.IsOpaque(air, false); // Must not occlude-
                    air     = Block.HasAlpha(air, false); // And not go to the alpha pass.

                    // Preserve (and optionally boost) light so neighbor faces aren't black.
                    int sun   = Block.GetSunLightLevel(raw);
                    int torch = Block.GetTorchLightLevel(raw);
                    air       = Block.SetSunLightLevel(air,   Math.Max(sun,   MinSun));
                    air       = Block.SetTorchLightLevel(air, Math.Max(torch, MinTorch));

                    __result = air;
                    return false; // Skip original.
                }

                // Allowed type: Return the real packed data unchanged.
                __result = raw;
                return false;
            }
        }
        #endregion

        #region Runtime Toggle Helper

        /// <summary>
        /// Convenience entry point to flip x-ray on/off and force a geometry refresh.
        /// Uses the safer refresher to rebuild visible (or all) chunks immediately.
        /// </summary>
        internal static class XRayRuntime
        {
            public static void SetEnabled(bool enabled)
            {
                XRayConfig.Enabled = enabled;

                // Rebuild now so the visual state matches immediately.
                ChunkGeometryRefresher.RebuildVisible(all: true);
            }
        }
        #endregion

        #endregion

        #region Instant Mine

        /// <summary>
        /// Target: InventoryItem.ProcessInput(...)
        /// Goal : When CastleWallsMk2._instantMineEnabled is true, make any call to
        ///        this.TimeToDig(BlockTypeEnum) return TimeSpan.Zero at runtime.
        /// </summary>
        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.ProcessInput))]
        internal static class InventoryItem_ProcessInput_InstantDig_WhenEnabled
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                // IL helpers to recognize/emit the right calls/fields.
                var list           = new List<CodeInstruction>(instructions);
                var miTimeToDig    = AccessTools.Method(typeof(InventoryItem), nameof(InventoryItem.TimeToDig), new[] { typeof(BlockTypeEnum) });
                var fiEnabled      = AccessTools.Field(typeof(CastleWallsMk2), nameof(CastleWallsMk2._instantMineEnabled)); // Static toggle.
                var fiTimeSpanZero = AccessTools.Field(typeof(TimeSpan), nameof(TimeSpan.Zero));                            // Static readonly TimeSpan.Zero.

                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i];

                    // Find the virtual call: this.TimeToDig(BlockTypeEnum)
                    if (ins.Calls(miTimeToDig))
                    {
                        // Stack just before the callvirt looks like: [ this, blockTypeEnum ]

                        // Branch around the original call:
                        // if (!_instantMineEnabled) { callvirt TimeToDig(...); } else { return TimeSpan.Zero; }
                        var lblDoCall = il.DefineLabel();                                 // Where to jump if feature is OFF.
                        var lblEnd    = il.DefineLabel();                                 // Common exit after either path.

                        // Branch test (leaves [this, bt] intact).
                        // Push the feature flag, then jump to original call if false.
                        yield return new CodeInstruction(OpCodes.Ldsfld, fiEnabled);      // [ this, bt, enabled ].
                        yield return new CodeInstruction(OpCodes.Brfalse_S, lblDoCall);   // (pops enabled).

                        // Enabled path: Discard args and push TimeSpan.Zero.
                        yield return new CodeInstruction(OpCodes.Pop);                    // Drop bt.
                        yield return new CodeInstruction(OpCodes.Pop);                    // Drop this.
                        yield return new CodeInstruction(OpCodes.Ldsfld, fiTimeSpanZero); // [ TimeSpan.Zero ].
                        yield return new CodeInstruction(OpCodes.Br_S, lblEnd);           // Jump to end.

                        // Original call path (feature OFF).
                        var doCall = new CodeInstruction(OpCodes.Callvirt, miTimeToDig);  // [ TimeSpan result ].
                        doCall.labels.Add(lblDoCall);                                     // Label falls through to call.
                        yield return doCall;

                        // End label (both paths meet here).
                        var end = new CodeInstruction(OpCodes.Nop);
                        end.labels.Add(lblEnd);
                        yield return end;

                        continue; // Fully handled this spot, move on.
                    }

                    // Anything that isn't the TimeToDig call is forwarded unchanged.
                    yield return ins;
                }
            }
        }
        #endregion

        #region Rapid Tools

        /// <summary>
        /// Target: InventoryItem.ProcessInput(...).
        /// This is the generic digging/using-tools pipeline (spades, picks, etc).
        /// We inject: 'CoolDownTimer.Expired || CastleWallsMk2._rapidToolsEnabled'.
        ///
        /// Notes:
        /// - We match the property getter MethodInfo of OneShotTimer.Expired.
        /// - In IL, the getter leaves a bool on the stack. We push our flag (ldsfld) and OR it.
        /// - This is "surgical": Only affects the exact spots that read .Expired.
        /// </summary>
        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.ProcessInput))]
        internal static class InventoryItem_ProcessInput_NoCooldown
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> T(IEnumerable<CodeInstruction> instr)
            {
                var list = new List<CodeInstruction>(instr);

                // MethodInfo for "bool OneShotTimer.get_Expired()".
                var expiredGetter = AccessTools.PropertyGetter(typeof(OneShotTimer), nameof(OneShotTimer.Expired));

                // Static field that enables rapid tools (set this from the UI/command).
                var flagField = AccessTools.Field(typeof(CastleWallsMk2), nameof(CastleWallsMk2._rapidToolsEnabled));

                for (int i = 0; i < list.Count; i++)
                {
                    var ci = list[i];
                    yield return ci; // Emit original instruction.

                    // IL stack at this point (right after the getter call) is: [..., bool expired].
                    // We want: expired || _rapidToolsEnabled.
                    if (ci.Calls(expiredGetter))
                    {
                        yield return new CodeInstruction(OpCodes.Ldsfld, flagField); // Push the toggle onto the stack -> [..., expired, bool flag].
                        yield return new CodeInstruction(OpCodes.Or);                // OR them -> [..., bool (expired || flag)].
                    }
                }
            }
        }
        #endregion

        #region No Mob Blocking

        /// <summary>
        /// Neutralize enemy "blocking" slowdown without touching EnemyManager.AttentuateVelocity or heavy IL surgery.
        /// We leave the original call intact and post-process its return value to 1f when our toggle is on.
        ///
        /// Notes:
        /// - Stack shape stays identical: call -> (float) -> ClampToOne(float) -> (float) -> stloc.
        /// - No labels/exception blocks are moved or removed.
        /// - Works even if the "attenuation" local changes index, because we inject just before the stloc.* that follows the call.
        /// </summary>

        #region Patch

        /// <summary>
        /// Transpiler that finds the call to EnemyManager.AttentuateVelocity(...) inside
        /// Player.ResolveCollsion(...) and injects a call to AttenuationHelper.ClampToOne(float)
        /// immediately before the attenuation is stored to its local.
        ///
        /// We do NOT remove or replace instructions-just insert one Call-so labels/try blocks remain valid.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ResolveCollsion))] // note: ResolveCollsion is misspelled in the game
        internal static class Patch_Player_ResolveCollsion_Attenuation
        {
            // The target "attenuation" provider we don't replace.
            private static readonly MethodInfo MI_Attenuate =
                AccessTools.Method(typeof(EnemyManager), "AttentuateVelocity",
                                   new[] { typeof(Player), typeof(Vector3), typeof(Vector3) });

            // Our post-processing helper (returns 1f when toggle is on).
            private static readonly MethodInfo MI_Clamp =
                AccessTools.Method(typeof(AttenuationHelper), nameof(AttenuationHelper.ClampToOne));

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
            {
                var list = instructions.ToList();
                int hits = 0;

                for (int i = 0; i < list.Count - 1; i++)
                {
                    // Find the call to EnemyManager.AttentuateVelocity(...).
                    if (list[i].Calls(MI_Attenuate))
                    {
                        // Scan forward to the next stloc.* that stores the attenuation local.
                        // We insert our ClampToOne call immediately before that stloc.
                        int j = i + 1;
                        while (j < list.Count && !IsStloc(list[j])) j++;

                        if (j < list.Count)
                        {
                            // Stack before:  [..., float].
                            // Insert call:   [..., float] -> ClampToOne(float) -> [..., float].
                            list.Insert(j, new CodeInstruction(OpCodes.Call, MI_Clamp));
                            hits++;
                            i = j; // Skip past the spot we just modified.
                        }
                    }
                }

                if (hits == 0)
                    Log("[NoBlock] WARN: Did not find AttentuateVelocity pattern in Player.ResolveCollsion.");

                return list;
            }

            // Small helper to match any stloc.* opcode.
            private static bool IsStloc(CodeInstruction ci)
            {
                var op = ci.opcode;
                return op == OpCodes.Stloc || op == OpCodes.Stloc_S ||
                       op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1 ||
                       op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3;
            }
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Tiny helper used by the transpiler: forces attenuation to 1f when the feature is enabled,
        /// otherwise passes through the original value.
        /// </summary>
        internal static class AttenuationHelper
        {
            public static float ClampToOne(float att) => CastleWallsMk2._noMobBlockingEnabled ? 1f : att;
        }
        #endregion

        #endregion

        #region Multi-Color Ammo

        #region MaterialCycler - Weapon/Tool Material Cycling

        /// <summary>
        /// MaterialCycler
        /// Summary:
        /// • Cycles a weapon/tool through its material variants (e.g., Pistol -> GoldPistol -> DiamondPistol ...).
        /// • Family detection is dynamic: Built from ToolMaterialTypes enum + InventoryItemIDs names (no hard-coding).
        /// • Two modes: Round-robin (default) or crypto-random (set randomize:true).
        /// • Keeps "normal" families separate from "Space" families, etc. ("Pistol" family ≠ "SpacePistol" family).
        /// • Caches families and per-family rotor index for performance.
        /// </summary>
        internal static class MaterialCycler
        {
            #region Fields & Caches

            // Material order comes straight from the enum (Wood, Stone, Copper, Iron, Gold, Diamond, BloodStone).
            private static readonly string[] _materials = Enum.GetNames(typeof(ToolMaterialTypes));

            // Cache: BaseName -> ordered variants (e.g., "Pistol" -> [Pistol, CopperPistol, IronPistol, ...] if present).
            private static readonly Dictionary<string, InventoryItemIDs[]> _familyByBase
                = new Dictionary<string, InventoryItemIDs[]>(StringComparer.Ordinal);

            // Per-family rotor for round-robin (baseName -> next index).
            private static readonly Dictionary<string, int> _rotor
                = new Dictionary<string, int>(StringComparer.Ordinal);

            #endregion

            #region Public API

            /// <summary>
            /// Return the next variant within the same item family.
            /// - Randomize=false (default): Round-robin per family.
            /// - Randomize=true: Crypto-strong random pick from that family.
            ///
            /// Example:
            ///   Instance.ItemID = MaterialCycler.NextFor(item);                 // Round-robin.
            ///   Instance.ItemID = MaterialCycler.NextFor(item, randomize:true); // Random color.
            /// </summary>
            public static InventoryItemIDs NextFor(InventoryItemIDs requested, bool randomize = false)
            {
                var fam = GetFamilyFor(requested, out var baseKey);
                if (fam == null || fam.Length == 0)
                    return requested; // No known family -> leave unchanged.

                if (randomize)
                {
                    // Uniform 0..N-1 using rejection sampling (no modulo bias).
                    int ix = GenerateRandomNumberInclusive(0, fam.Length);
                    return fam[ix];
                }
                else
                {
                    // Round-robin: Advance per-family rotor.
                    _rotor.TryGetValue(baseKey, out int idx);
                    var next = fam[idx % fam.Length];
                    _rotor[baseKey] = (idx + 1) % fam.Length;
                    return next;
                }
            }

            /// <summary>Convenience wrapper equal to NextFor(requested, true).</summary>
            public static InventoryItemIDs NextForRandom(InventoryItemIDs requested)
                => NextFor(requested, randomize: true);

            #endregion

            #region Internals: Family Detection

            /// <summary>
            /// Remove a known material prefix if present and return the base name.
            /// E.g., "GoldPistol" -> base="Pistol" (material="Gold").
            /// Only strips when followed by an Uppercase start (PascalCase boundary) to avoid false positives.
            /// </summary>
            private static string StripMaterialPrefix(string idName, out string material)
            {
                foreach (var token in _materials.OrderByDescending(s => s.Length))
                {
                    if (idName.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        int n = token.Length;
                        if (idName.Length > n && char.IsUpper(idName[n])) // boundary check
                        {
                            material = token;
                            return idName.Substring(n);
                        }
                    }
                }
                material = null;
                return idName; // no material prefix detected
            }

            /// <summary>
            /// Build or fetch the family for this item and return it along with its base key.
            /// The base key is the material-stripped name (e.g., "Pistol", "SpacePistol").
            /// </summary>
            private static InventoryItemIDs[] GetFamilyFor(InventoryItemIDs requested, out string baseKey)
            {
                var name = requested.ToString();
                var baseName = StripMaterialPrefix(name, out _); // keeps "SpacePistol" intact as a distinct family
                baseKey = baseName;

                if (_familyByBase.TryGetValue(baseKey, out var fam))
                    return fam;

                fam = BuildFamily(baseName, includeBare: true);
                _familyByBase[baseKey] = fam;
                return fam;
            }

            /// <summary>
            /// Construct the family list: [Bare?, material+base in ToolMaterialTypes order] if they exist in InventoryItemIDs.
            /// </summary>
            private static InventoryItemIDs[] BuildFamily(string baseName, bool includeBare)
            {
                var allIds = (InventoryItemIDs[])Enum.GetValues(typeof(InventoryItemIDs));
                var allNames = new HashSet<string>(allIds.Select(e => e.ToString()), StringComparer.Ordinal);

                var list = new List<(int order, InventoryItemIDs id)>();

                // Optional bare (unprefixed) variant first.
                if (includeBare && allNames.Contains(baseName))
                {
                    var bare = (InventoryItemIDs)Enum.Parse(typeof(InventoryItemIDs), baseName, ignoreCase: false);
                    list.Add((-1, bare)); // sort before materials
                }

                // Then each materialized variant in the enum's order (only if it exists)
                for (int i = 0; i < _materials.Length; i++)
                {
                    string candidate = _materials[i] + baseName;   // e.g., "Gold" + "Pistol"
                    if (allNames.Contains(candidate))
                    {
                        var id = (InventoryItemIDs)Enum.Parse(typeof(InventoryItemIDs), candidate, ignoreCase: false);
                        list.Add((i, id));
                    }
                }

                // Stable order: bare first (order=-1), then materials in enum order
                return list.OrderBy(t => t.order).Select(t => t.id).ToArray();
            }
            #endregion
        }
        #endregion

        #region Patches

        /// <summary>
        /// Rotates projectile color/material by rewriting the 'item' parameter
        /// before the game's Send(...) method assigns it to the network message.
        /// </summary>
        [HarmonyPatch(typeof(GunshotMessage))]
        internal static class Patch_GunshotMessage_Send_UseCycler
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(GunshotMessage.Send),
                new[] { typeof(LocalNetworkGamer), typeof(Matrix), typeof(Angle), typeof(InventoryItemIDs), typeof(bool) })]
            private static void Prefix(ref InventoryItemIDs item)
            {
                if (CastleWallsMk2._multiColorAmmoEnabled)
                    item = MaterialCycler.NextFor(item, randomize: CastleWallsMk2._multiColorRNGEnabled);
            }
        }

        /// <summary>
        /// Same idea for the shotgun (buckshot variant).
        /// </summary>
        [HarmonyPatch(typeof(ShotgunShotMessage))]
        internal static class Patch_ShotgunShotMessage_Send_UseCycler
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(GunshotMessage.Send),
                new[] { typeof(LocalNetworkGamer), typeof(Matrix), typeof(Angle), typeof(InventoryItemIDs), typeof(bool) })]
            private static void Prefix(ref InventoryItemIDs item)
            {
                if (CastleWallsMk2._multiColorAmmoEnabled)
                    item = MaterialCycler.NextFor(item, randomize: CastleWallsMk2._multiColorRNGEnabled);
            }
        }
        #endregion

        #endregion

        #region Shoot Blocks

        /// <summary>
        /// -----------------------------------------------------------------------------
        /// SHOOT-BLOCKS PIPELINE (high-level)
        /// 1) Tracer.Update calls TraceProbe.Init(Tail,Head). We inject a call right
        ///    after that to place a block at the tracer head and mark that cell.
        /// 2) Any BlockTerrain.Trace(tp) that hits a just-placed cell has its collision
        ///    cleared by our postfix (so the bullet won't "hit" the block it just made).
        /// 3) The ignore marker is short-lived so normal terrain collisions resume soon.
        /// -----------------------------------------------------------------------------
        /// </summary>

        #region Ignore Cache (Short-Lived Cells To Skip In Terrain Traces)

        /// <summary>
        /// Short-lived set of cells we've just placed so terrain traces can ignore them
        /// for the next frame(s). This prevents immediate self-collision with spawned blocks.
        /// </summary>
        internal static class ShootBlockIgnore
        {
            #region Constants & State

            /// <summary>How long to ignore a freshly placed cell (ms). Keep small.</summary>
            private const long TTL_MS = 400;

            /// <summary>Map: world cell -> expiration timestamp (Environment.TickCount).</summary>
            /// <remarks>
            /// TickCount is a 32-bit millisecond counter that wraps ~every 24.9 days.
            /// With such a short TTL we're safe from wrap-around comparisons here.
            /// </remarks>
            private static readonly Dictionary<IntVector3, long> _recent =
                new Dictionary<IntVector3, long>();

            #endregion

            #region API

            /// <summary>Add/refresh a cell's ignore window.</summary>
            public static void Mark(IntVector3 cell)
            {
                long now = Environment.TickCount;
                _recent[cell] = now + TTL_MS;

                // Opportunistic cleanup to keep the dictionary small.
                if (_recent.Count > 256) Cleanup(now);
            }

            /// <summary>True if the given cell is still under its ignore window.</summary>
            public static bool ShouldIgnore(in IntVector3 cell)
            {
                long now = Environment.TickCount;
                if (_recent.TryGetValue(cell, out var until))
                {
                    if (now <= until) return true;
                    _recent.Remove(cell);
                }
                return false;
            }
            #endregion

            #region Maintenance

            /// <summary>Remove expired entries.</summary>
            private static void Cleanup(long now)
            {
                var dead = new List<IntVector3>(8);
                foreach (var kv in _recent)
                    if (now > kv.Value) dead.Add(kv.Key);
                foreach (var k in dead) _recent.Remove(k);
            }
            #endregion
        }
        #endregion

        #region Placement Helper (Called Immediately After Tp.Init)

        /// <summary>
        /// Helper methods used by the transpiler-injected call in <see cref="Tracer_Update_Transpiler"/>.
        /// Places a block at the tracer head (if allowed) and marks that cell to be ignored by terrain traces.
        /// </summary>
        internal static class ShootBlocksImpl
        {
            #region Private Field Accessors (Tracer)

            // Access private fields on Tracer via Harmony's FieldRef; these names must match your build.
            private static readonly AccessTools.FieldRef<TracerManager.Tracer, byte> _shooterIdRef =
                AccessTools.FieldRefAccess<TracerManager.Tracer, byte>("ShooterID");

            private static readonly AccessTools.FieldRef<TracerManager.Tracer, Vector3> _headRef =
                AccessTools.FieldRefAccess<TracerManager.Tracer, Vector3>("Head");

            private static readonly AccessTools.FieldRef<BlasterShot, byte> _blasterShooterRef =
                AccessTools.FieldRefAccess<BlasterShot, byte>("_shooter");

            #endregion

            #region Injected Entry Points (Tracer + BlasterShot)

            /// <summary>
            /// Called right after TraceProbe.Init(Tail, Head) inside Tracer.Update.
            /// If the shoot-blocks feature is enabled and scope allows, places a block at the
            /// current head cell and registers it in the ignore cache so terrain trace won't hit it.
            /// </summary>
            public static void AfterTpInit_PlaceAndMark(TracerManager.Tracer tracer)
            {
                // Master toggle.
                if (!CastleWallsMk2._shootBlocksEnabled) return;

                var game = CastleMinerZGame.Instance;
                if (!(game?.LocalPlayer?.Gamer is LocalNetworkGamer me)) return;

                // Scope: Everyone vs. Personal (only my tracers).
                bool everyone  = (CastleWallsMk2._shootBlockScope == CastleWallsMk2.PlayerSelectScope.Everyone);
                byte shooterId = _shooterIdRef(tracer);
                if (!everyone && shooterId != me.Id) return;

                // Cell at tracer head.
                var cell = (IntVector3)_headRef(tracer);

                // Choose a safe/placeable block (swap with your UI/config selection if desired).
                BlockTypeEnum block = PickPlaceableBlock();

                // Apply if different (host will echo to others; fine for this feature).
                if (InGameHUD.GetBlock(cell) != block)
                {
                    AlterBlockMessage.Send(me, cell, block);
                }

                // Prevent immediate self-collision.
                ShootBlockIgnore.Mark(cell);
            }

            /// <summary>
            /// Called right after BlasterShot.tp.Init(lastPos, worldPos) inside BlasterShot.OnUpdate.
            /// Places a block at the shot head cell and marks it for short-lived ignore to prevent self-collision.
            /// </summary>
            public static void AfterBlasterTpInit_PlaceAndMark(BlasterShot shot)
            {
                if (!CastleWallsMk2._shootBlocksEnabled) return;

                var game = CastleMinerZGame.Instance;
                if (!(game?.LocalPlayer?.Gamer is LocalNetworkGamer me)) return;

                // Scope: Everyone vs. Personal (only my shots).
                bool everyone = (CastleWallsMk2._shootBlockScope == CastleWallsMk2.PlayerSelectScope.Everyone);
                byte shooterId = _blasterShooterRef(shot);
                if (!everyone && shooterId != me.Id) return;

                // Use the shot's current world position as the "head".
                Vector3 headPos = shot.WorldPosition;
                var cell = (IntVector3)headPos;

                BlockTypeEnum block = PickPlaceableBlock();

                if (InGameHUD.GetBlock(cell) != block)
                    AlterBlockMessage.Send(me, cell, block);

                ShootBlockIgnore.Mark(cell);
            }
            #endregion

            #region Block Picker (simple, safe defaults)

            /// <summary>All block types (cached once).</summary>
            static readonly BlockTypeEnum[] _allBlocks =
                (Enum.GetValues(typeof(BlockTypeEnum)) as BlockTypeEnum[]) ?? Array.Empty<BlockTypeEnum>();

            /// <summary>Filtered list of placeable blocks (lazy-built).</summary>
            static BlockTypeEnum[] _safeFiltered;

            /// <summary>
            /// Random but safe block set (excludes Torch/Empty/NumberOfBlocks).
            /// </summary>
            static BlockTypeEnum PickPlaceableBlock()
            {
                // build once (first call)
                if (_safeFiltered == null)
                {
                    var list = new List<BlockTypeEnum>(_allBlocks.Length);
                    foreach (var b in _allBlocks)
                        if (!IsBlacklisted(b))
                            list.Add(b);

                    _safeFiltered = list.ToArray();
                }

                return _safeFiltered.Length == 0
                    ? BlockTypeEnum.Dirt
                    : _safeFiltered[GenerateRandomNumberInclusive(0, _safeFiltered.Length)];
            }

            /// <summary>
            /// Hard-coded block blacklist for Shoot-Blocks RNG picks.
            /// </summary>
            private static bool IsBlacklisted(BlockTypeEnum b)
            {
                switch (b)
                {
                    // Always exclude these:
                    case BlockTypeEnum.Empty:
                    case BlockTypeEnum.NumberOfBlocks:
                    case BlockTypeEnum.Torch:

                    // Add more blocks here:
                    case BlockTypeEnum.BombBlock: // This is being used in another mod as a "Nuke".
                        return true;

                    default:
                        return false;
                }
            }
            #endregion
        }
        #endregion

        #region Tracer.Update Transpiler (Inject Right After Tp.Init)

        /// <summary>
        /// Transpiler that injects a call to <see cref="ShootBlocksImpl.AfterTpInit_PlaceAndMark"/>
        /// immediately after the in-method call to TraceProbe.Init(Tail, Head).
        /// This ensures our block is placed and marked *before* any terrain/player traces run,
        /// preventing the bullet from colliding with the block it just spawned.
        /// </summary>
        [HarmonyPatch(typeof(TracerManager.Tracer), "Update")]
        internal static class Tracer_Update_Transpiler
        {
            #region Targets

            private static readonly MethodInfo MI_TpInit =
                AccessTools.Method(typeof(TraceProbe), "Init", new[] { typeof(Vector3), typeof(Vector3) });

            private static readonly MethodInfo MI_AfterInit =
                AccessTools.Method(typeof(ShootBlocksImpl), nameof(ShootBlocksImpl.AfterTpInit_PlaceAndMark));

            #endregion

            #region IL Rewriter

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins, ILGenerator il)
            {
                foreach (var ci in ins)
                {
                    yield return ci;

                    // Inject right after "... tp.Init(Tail, Head)".
                    if (ci.opcode == OpCodes.Callvirt && ci.operand as MethodInfo == MI_TpInit)
                    {
                        // Push "this" (Tracer) and call our helper.
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, MI_AfterInit);
                    }
                }
            }
            #endregion
        }
        #endregion

        #region Terrain Trace Filter (Cancel Collisions With Fresh Blocks)

        /// <summary>
        /// Postfix for <see cref="BlockTerrain.Trace(TraceProbe)"/>.
        /// If the reported hit cell equals a just-placed cell in our ignore cache,
        /// clear the collision so the caller treats it as a miss.
        /// </summary>
        [HarmonyPatch(typeof(BlockTerrain), nameof(BlockTerrain.Trace))]
        internal static class BlockTerrain_Trace_Postfix
        {
            #region Private Field Access (TraceProbe)

            /// <summary>
            /// The exact cell index that the trace collided with.
            /// Using this is more robust than reprojecting the intersection point.
            /// </summary>
            private static readonly FieldInfo FI_WorldIndex =
                AccessTools.Field(typeof(TraceProbe), "_worldIndex");

            #endregion

            #region Postfix

            static void Postfix(TraceProbe tp)
            {
                if (!CastleWallsMk2._shootBlocksEnabled) return;
                if (!tp._collides) return;

                // Exact hit cell from the probe.
                var cell = (IntVector3)FI_WorldIndex.GetValue(tp);

                // Disarm collision if it's our freshly placed cell.
                if (ShootBlockIgnore.ShouldIgnore(cell))
                {
                    tp._collides = false;
                    // Optional: Push the hit far away.
                    // tp._inT = float.MaxValue;
                }
            }
            #endregion
        }
        #endregion

        #region Shoot Blocks - Laser Support (BlasterShot)

        /// <summary>
        /// Extends Shoot-Blocks to laser weapons by injecting the same "place + mark ignore" call
        /// right after BlasterShot.tp.Init(lastPos, WorldPos) inside BlasterShot.OnUpdate.
        /// </summary>
        [HarmonyPatch(typeof(BlasterShot), "OnUpdate")]
        internal static class BlasterShot_OnUpdate_Transpiler
        {
            #region Targets

            // We match by signature, not declaring type, because TracerProbe may inherit/forward Init().
            private static readonly Type[] TpInitSig = { typeof(Vector3), typeof(Vector3) };

            private static readonly MethodInfo MI_AfterInit =
                AccessTools.Method(typeof(ShootBlocksImpl), nameof(ShootBlocksImpl.AfterBlasterTpInit_PlaceAndMark));

            #endregion

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
            {
                foreach (var ci in ins)
                {
                    yield return ci;

                    // Inject right after: BlasterShot.tp.Init(this._lastPosition, base.WorldPosition)
                    if (ci.opcode == OpCodes.Callvirt && ci.operand is MethodInfo mi &&
                        mi.Name == "Init" &&
                        mi.GetParameters().Length == 2 &&
                        mi.GetParameters()[0].ParameterType == TpInitSig[0] &&
                        mi.GetParameters()[1].ParameterType == TpInitSig[1])
                    {
                        // Push "this" (BlasterShot) and call our helper.
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, MI_AfterInit);
                    }
                }
            }
        }

        #endregion

        #endregion

        #region Custom Ammo (Host, Grenade, Rocket)

        /// <summary>
        /// Intercepts bullet tracer creation and:
        /// - HostAmmo: Suppresses the local client-side tracer for the local shooter.
        /// - GrenadeAmmo / GrenadeAmmoAll: Sends a grenade fire message instead of a bullet.
        /// - RocketAmmo  / RocketAmmoAll : Sends a rocket fire message instead of a bullet.
        ///
        /// Return false when we fully handled the shot; true to let the game spawn the normal tracer.
        /// </summary>

        #region Main Patch

        [HarmonyPatch(typeof(TracerManager),
                      nameof(TracerManager.AddTracer),
                      new Type[] { typeof(Vector3), typeof(Vector3), typeof(InventoryItemIDs), typeof(byte) })]
        internal static class Patch_TracerManager_AddTracer_CustomAmmo
        {
            // NOTE: No __state needed; either handle and skip, or fall through to original.
            static bool Prefix(TracerManager __instance,
                               Vector3 position,
                               Vector3 velocity,
                               InventoryItemIDs item,
                               byte shooterID)
            {
                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game == null || game.LocalPlayer == null || game.LocalPlayer.Gamer == null)
                        return true; // Let vanilla handle if we can't reason about the shooter.

                    // Is the shooter this client?
                    byte localId = game.LocalPlayer.Gamer.Id;
                    bool isLocal = shooterID == localId;

                    // Track which custom path fired (any true => skip vanilla).
                    bool hostAmmoFired = false, grenadeAmmoFired = false, rocketAmmoFired = false;

                    // 1) "Host ammo": Don't spawn local client tracer (host/peers will still process the shot normally).
                    if (CastleWallsMk2._shootHostAmmoEnabled && isLocal)
                        hostAmmoFired = true; // Swallow the tracer for local shooter.

                    // 2) Grenade ammo (local only or for everyone).
                    if ((CastleWallsMk2._shootGrenadeAmmoEnabled && isLocal) ||
                        CastleWallsMk2._shootGrenadeAmmoEnabled && CastleWallsMk2._shootAmmoScope == CastleWallsMk2.PlayerSelectScope.Everyone)
                    {
                        Send_ShootGrenadeRounds(CastleMinerZGame.Instance.MyNetworkGamer, shooterID, position, GrenadeTypeEnum.HE, 5f);
                        grenadeAmmoFired = true;
                    }

                    // 3) Rocket ammo (local only or for everyone).
                    if ((CastleWallsMk2._shootRocketAmmoEnabled && isLocal) ||
                        CastleWallsMk2._shootRocketAmmoEnabled && CastleWallsMk2._shootAmmoScope == CastleWallsMk2.PlayerSelectScope.Everyone)
                    {
                        Send_ShootRocketRounds(CastleMinerZGame.Instance.MyNetworkGamer, shooterID, position, InventoryItemIDs.RocketLauncher, false);
                        rocketAmmoFired = true;
                    }

                    // If any custom path ran, stop the original tracer logic.
                    if (hostAmmoFired || grenadeAmmoFired || rocketAmmoFired)
                        return false;

                    // Otherwise, fall through to vanilla tracer creation + EnemyManager.RegisterGunShot(...).
                    return true;
                }
                catch (Exception ex)
                {
                    // Fail-open: Never break firing if the mod throws.
                    try { Log($"[CustomAmmo] Prefix error: {ex.Message}."); } catch { }
                    return true;
                }
            }
        }
        #endregion

        #region Private Network Senders

        /// <summary>Send a custom grenade shot payload.</summary>
        static void Send_ShootGrenadeRounds(LocalNetworkGamer from, byte shooterID, Vector3 startPosition, GrenadeTypeEnum grenadeType, float secondsLeft)
        {
            // Define the send instance message type.
            var sendInstance = MessageBridge.Get<GrenadeMessage>();

            // Custom payload data.
            Vector3 direction = Matrix.Invert(((Player)CastleMinerZGame.Instance.GetGamerFromID(shooterID).Tag).FPSCamera.View).Forward;
            Vector3 position  = startPosition + direction;

            // Packet fields.
            sendInstance.Direction   = direction;
            sendInstance.Position    = position;
            sendInstance.GrenadeType = grenadeType;
            sendInstance.SecondsLeft = secondsLeft;

            // Broadcast to all players.
            MessageBridge.DoSend.Invoke(sendInstance, new object[] { from });
        }

        /// <summary>Send a custom rocket shot payload.</summary>
        static void Send_ShootRocketRounds(LocalNetworkGamer from, byte shooterID, Vector3 startPosition, InventoryItemIDs rocketType, bool guided)
        {
            // Define the send instance message type.
            var sendInstance = MessageBridge.Get<FireRocketMessage>();

            // Custom payload data.
            Vector3 direction = Matrix.Invert(((Player)CastleMinerZGame.Instance.GetGamerFromID(shooterID).Tag).FPSCamera.View).Forward;
            Vector3 position  = startPosition + direction;

            // Packet fields.
            sendInstance.Direction  = direction;
            sendInstance.Position   = position;
            sendInstance.WeaponType = rocketType;
            sendInstance.Guided     = guided;

            // Broadcast to all players.
            MessageBridge.DoSend.Invoke(sendInstance, new object[] { from });
        }
        #endregion

        #endregion

        #region Custom Laser Behavior (Freeze, ExtendLifetime, InfiPath, InfiBounce, Explosive)

        /// <summary>
        /// Harmony patches for <see cref="BlasterShot"/> that control:
        /// - Freezing lasers in place.
        /// - Explosive laser behavior (C4 detonations along the tracer path).
        /// - Path behavior (straight-through vs infinite bounces on blocks).
        ///
        /// All behaviors are driven by independent CastleWallsMk2 static flags.
        /// </summary>
        [HarmonyPatch(typeof(BlasterShot))]
        internal static class BlasterShotPatches
        {
            #region FieldRefs

            /// <summary>
            /// FieldRef for the private _shooter byte on <see cref="BlasterShot"/>.
            /// Used to check which player fired a specific shot.
            /// </summary>
            private static readonly AccessTools.FieldRef<BlasterShot, byte>
                ShooterRef = AccessTools.FieldRefAccess<BlasterShot, byte>("_shooter");

            /// <summary>
            /// FieldRef for the public CollisionsRemaining counter on <see cref="BlasterShot"/>.
            /// Used to grant effectively infinite bounce budget when desired.
            /// </summary>
            private static readonly AccessTools.FieldRef<BlasterShot, int>
                CollisionsRemainingRef = AccessTools.FieldRefAccess<BlasterShot, int>("CollisionsRemaining");

            /// <summary>
            /// Reflection access to the static tracer probe (BlasterShot.tp), which
            /// holds collision data for the current frame (hit flag and world index).
            /// </summary>
            private static readonly FieldInfo TpField =
                AccessTools.Field(typeof(BlasterShot), "tp");

            /// <summary>
            /// Reflection handle for tp._collides (did the tracer hit something).
            /// </summary>
            private static readonly FieldInfo TpCollidesField =
                TpField != null ? AccessTools.Field(TpField.FieldType, "_collides") : null;

            /// <summary>
            /// Reflection handle for tp._worldIndex (world coordinates of the hit).
            /// </summary>
            private static readonly FieldInfo TpWorldIndexField =
                TpField != null ? AccessTools.Field(TpField.FieldType, "_worldIndex") : null;

            /// <summary>
            /// FieldRef for the private _lifeTime countdown on <see cref="BlasterShot"/>.
            /// Vanilla initializes this to TotalLifeTime (usually ~3 seconds) and
            /// decrements it each update until the shot is removed.
            /// </summary>
            private static readonly AccessTools.FieldRef<BlasterShot, TimeSpan> LifeTimeRef =
                AccessTools.FieldRefAccess<BlasterShot, TimeSpan>("_lifeTime");

            #endregion

            #region Freeze Lasers - OnUpdate Prefix

            /// <summary>
            /// Freeze-laser prefix.
            ///
            /// When CastleWallsMk2._freezeLasersEnabled is true, this prefix returns
            /// false to skip the original BlasterShot.OnUpdate(GameTime)
            /// implementation. That means:
            /// - Lifetime does not tick down.
            /// - Position is not advanced.
            /// - No collision or damage processing occurs.
            ///
            /// The shot is effectively "paused" in mid-air.
            /// </summary>
            [HarmonyPrefix]
            [HarmonyPatch("OnUpdate")]
            private static bool OnUpdate_FreezePrefix()
            {
                if (CastleWallsMk2._freezeLasersEnabled)
                    return false; // Skip original.

                return true;      // Run original.
            }
            #endregion

            #region Extend Laser Lifetime (Create Postfixes)

            /// <summary>
            /// Returns the desired BlasterShot lifetime when
            /// <see cref="LifetimeFeatureEnabled"/> is active.
            ///
            /// Notes:
            /// - This is applied immediately after BlasterShot.Create(...) returns (postfix),
            ///   overriding vanilla's default short lifetime.
            /// - Keep this reasonable if many shots can exist at once (perf/visual spam).
            /// </summary>
            private static TimeSpan GetDesiredLife()
            {
                // Pick what you want:
                // return TimeSpan.FromSeconds(30);
                // return TimeSpan.FromMinutes(5);
                return TimeSpan.FromMinutes(1);
            }

            /// <summary>
            /// Postfix for the "enemy targeted" Create overload:
            /// Create(Vector3 origin, Vector3 velocity, int enemyID, InventoryItemIDs weaponID).
            ///
            /// If enabled, forces the newly-created shot's private lifetime timer
            /// to <see cref="GetDesiredLife"/> so it persists longer than vanilla.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(nameof(BlasterShot.Create), typeof(Vector3), typeof(Vector3), typeof(int), typeof(InventoryItemIDs))]
            private static void Create_Enemy_Postfix(BlasterShot __result)
            {
                if (__result == null || !CastleWallsMk2._extendLaserTimeEnabled) return;
                LifeTimeRef(__result) = GetDesiredLife();
            }

            /// <summary>
            /// Postfix for the "player fired" Create overload:
            /// Create(Vector3 origin, Vector3 velocity, InventoryItemIDs weaponID, byte shooter).
            ///
            /// If enabled, forces the newly-created shot's private lifetime timer
            /// to <see cref="GetDesiredLife"/> so it persists longer than vanilla.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(nameof(BlasterShot.Create), typeof(Vector3), typeof(Vector3), typeof(InventoryItemIDs), typeof(byte))]
            private static void Create_Player_Postfix(BlasterShot __result)
            {
                if (__result == null || !CastleWallsMk2._extendLaserTimeEnabled) return;
                LifeTimeRef(__result) = GetDesiredLife();
            }
            #endregion

            #region Explosive Lasers - OnUpdate Postfix

            #region Helpers - Explosive Laser Filtering

            /// <summary>
            /// Returns true if the block at <paramref name="worldIndex"/> should NOT be
            /// affected by explosive lasers (e.g., bedrock, fixed lanterns, etc.).
            /// Used to prevent "unbreakable" or decorative blocks from being destroyed
            /// by C4 spawned from BlasterShot collisions.
            /// </summary>
            private static bool IsBlockProtectedFromExplosiveLasers(IntVector3 worldIndex)
            {
                // Read the current block at the impact position.
                var blockId         = BlockTerrain.Instance.GetBlockWithChanges(worldIndex);
                BlockType blockType = BlockType.GetType(blockId);

                // Generic rule: Anything that isn't diggable is treated as protected.
                if (!blockType.CanBeDug)
                    return true;

                return false;
            }
            #endregion

            /// <summary>
            /// Explosive-laser postfix.
            ///
            /// When CastleWallsMk2._explosiveLasersEnabled is true, this postfix
            /// inspects the shared tracer probe (BlasterShot.tp) after the vanilla
            /// update logic has run. If the tracer collided (_collides == true),
            /// it sends a <see cref="DetonateExplosiveMessage"/>
            /// to spawn a C4 explosion at tp._worldIndex.
            ///
            /// Scope is controlled by _explosiveLasersScope:
            /// - Personal -> Only detonates for shots fired by the local player.
            /// - Everyone -> Det.
            [HarmonyPostfix]
            [HarmonyPatch("OnUpdate")]
            private static void OnUpdate_ExplosivePostfix(BlasterShot __instance)
            {
                // Global explosive toggle off -> do nothing.
                if (!CastleWallsMk2._explosiveLasersEnabled)
                    return;

                var game = CastleMinerZGame.Instance;
                if (game == null)
                    return;

                var myGamer = game.MyNetworkGamer;
                if (myGamer == null)
                    return;

                // Ensure we can see tp and its fields.
                if (TpField == null || TpCollidesField == null || TpWorldIndexField == null)
                    return;

                var tp = TpField.GetValue(null); // Static field.
                if (tp == null)
                    return;

                bool collides = (bool)TpCollidesField.GetValue(tp);
                if (!collides)
                    return;

                IntVector3 worldIndex = (IntVector3)TpWorldIndexField.GetValue(tp);

                // Skip explosions on "protected" blocks (bedrock, fixed lanterns, etc.).
                if (IsBlockProtectedFromExplosiveLasers(worldIndex))
                    return;

                switch (CastleWallsMk2._explosiveLasersScope)
                {
                    case CastleWallsMk2.PlayerSelectScope.Personal:
                        {
                            // Only detonate for shots fired by the local player.
                            byte shooterId = ShooterRef(__instance);
                            if (shooterId == myGamer.Id)
                            {
                                DetonateExplosiveMessage.Send(
                                    myGamer,
                                    worldIndex,
                                    true,
                                    ExplosiveTypes.C4);
                            }
                            break;
                        }

                    case CastleWallsMk2.PlayerSelectScope.Everyone:
                        {
                            // Detonate for all shots, regardless of shooter.
                            DetonateExplosiveMessage.Send(
                                myGamer,
                                worldIndex,
                                true,
                                ExplosiveTypes.C4);
                            break;
                        }

                    default:
                        // Other scopes (if added later) are treated as no-op here.
                        break;
                }
            }
            #endregion

            #region Path Behaviour - HandleCollision Prefix

            /// <summary>
            /// Path-behavior prefix for BlasterShot.HandleCollision.
            ///
            /// This single hook handles two independent toggles:
            /// - _infiLaserPathEnabled
            ///     -> Treats block hits as "straight-through": Skip the original
            ///        HandleCollision for blocks so lasers do not bounce or
            ///        dig and simply continue on their path.
            ///
            /// - _infiLaserBounceEnabled
            ///     -> Grants lasers an effectively infinite bounce budget on blocks
            ///        by setting CollisionsRemaining to int.MaxValue
            ///        and forcing bounce = true for those hits.
            ///
            /// Both behaviors are independent of the explosive-laser toggle and can
            /// be combined or used separately.
            /// </summary>
            [HarmonyPrefix]
            [HarmonyPatch(
                "HandleCollision",
                typeof(Vector3),   // CollisionNormal.
                typeof(Vector3),   // CollisionLocation.
                typeof(bool),      // Bounce.
                typeof(bool),      // DestroyBlock.
                typeof(IntVector3) // BlockToDestroy.
            )]
            private static bool HandleCollision_PathPrefix(
                BlasterShot __instance,
                Vector3 collisionNormal,
                Vector3 collisionLocation,
                ref bool bounce,
                ref bool destroyBlock,
                IntVector3 blockToDestroy)
            {
                // Per-feature flags (independent from explosives).
                bool straightThrough = CastleWallsMk2._infiLaserPathEnabled;
                bool infinitePath    = CastleWallsMk2._infiLaserBounceEnabled;

                // Non-zero block index -> environment (block) hit.
                bool hitBlock = blockToDestroy != IntVector3.Zero; // Env vs enemy.

                // 1) Straight-through mode:
                //    If enabled and this is a block hit, completely skip the vanilla
                //    HandleCollision. That means:
                //      - No bounce.
                //      - No block destruction.
                //      - No removal due to collision.
                //    The shot just keeps going, governed only by lifetime and velocity.
                if (straightThrough && hitBlock)
                {
                    // Optional: Still grant a huge collision budget so they never
                    // run out of "bounces" even if other code touches this field.
                    if (infinitePath && CollisionsRemainingRef(__instance) < int.MaxValue)
                        CollisionsRemainingRef(__instance) = int.MaxValue;

                    return false; // Skip original HandleCollision.
                }

                // 2) Infinite-path mode:
                //    If enabled (and not straight-through), let lasers bounce
                //    effectively forever off blocks.
                if (infinitePath && hitBlock)
                {
                    if (CollisionsRemainingRef(__instance) < int.MaxValue)
                        CollisionsRemainingRef(__instance) = int.MaxValue;

                    // Force bounce so the vanilla code follows the "doBounce" branch,
                    // not the "RemoveFromParent()" branch.
                    bounce = true;
                }

                // Enemy hits (blockToDestroy == Zero) still go through vanilla:
                // they will kill the shot as usual, unless you later decide to
                // special-case that too.
                return true; // Run original HandleCollision.
            }
            #endregion
        }
        #endregion

        #region Ignore Restart Level Message

        /// <summary>
        /// Patch: CastleMinerZGame._processRestartLevelMessage(Message)
        ///
        /// Goal:
        ///   When CastleWallsMk2._noTPOnServerRestartEnabled is true, ignore the entire
        ///   restart-level message and do nothing (no teleport, no HUD reset, no day reset, etc.).
        ///
        /// Behavior:
        ///   - If flag is false => run original method.
        ///   - If flag is true  => skip original method body completely.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processRestartLevelMessage")]
        internal static class CastleMinerZGame__processRestartLevelMessage_Ignore
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                // Gate: if enabled, ignore the message completely.
                if (CastleWallsMk2._noTPOnServerRestartEnabled)
                    return false;

                return true;
            }
        }
        #endregion

        #region Corrupt On Kick (No Kick + Log)

        /// <summary>
        /// Patch: CastleMinerZGame._processKickMessage(Message, LocalNetworkGamer)
        ///
        /// Goal:
        ///   When CastleWallsMk2._corruptOnKickEnabled is true and the KickMessage targets you,
        ///   log that the host tried to kick/ban you and corrupt the players world.
        ///
        /// Behavior:
        ///   - If _corruptOnKickEnabled is false => run the original method.
        ///   - If _corruptOnKickEnabled is true  =>
        ///       * If message is not a KickMessage or not for you => run original.
        ///       * If it is for you => write a log line, corrupt, and skip EndGame/dialog.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processKickMessage")]
        internal static class CastleMinerZGame__processKickMessage_SuppressWhenLogEnabled
        {
            [HarmonyPrefix]
            private static bool Prefix(CastleMinerZGame __instance, Message message, LocalNetworkGamer localGamer)
            {
                // If logging isn't enabled, keep vanilla behavior.
                if (!CastleWallsMk2._corruptOnKickEnabled)
                    return true;

                try
                {
                    // Safety: Ensure we actually have a KickMessage.
                    if (!(message is KickMessage km))
                        return true; // Not our expected type; fall back to original.

                    // Determine the "local" player ID the same way the game does.
                    byte myId = 255;
                    if (__instance != null && __instance.MyNetworkGamer != null)
                        myId = __instance.MyNetworkGamer.Id;
                    else if (localGamer != null)
                        myId = localGamer.Id;

                    // If this KickMessage is not for us, let the game handle it normally.
                    if (km.PlayerID != myId)
                        return true;

                    // At this point, host is trying to kick/ban us.
                    string reason = km.Banned ? "banned" : "kicked";

                    // Log & corrupt instead of actually kicking.
                    Log($"Host attempted to {reason} you (PlayerID={km.PlayerID}) | corruptOnKick -> Enabled, corrupting world. ");

                    // Run this function async.
                    Task.Run(async () =>
                    {
                        try
                        {
                            // == Corrupt the world ==

                            // Corrupt all peers first -> host last.
                            try { CastleWallsMk2.CorruptAllPlayers(); } catch { }
                        }
                        catch (Exception ex)
                        {
                            // Last-resort guard so background task never crashes the process.
                            Log($"Corrupt task failed: {ex}.");
                        }
                    });

                    // Return false to skip:
                    //   this.EndGame(true);
                    //   this._waitForWorldInfo = null;
                    //   FrontEnd.ShowUIDialog("Session Ended", ...);
                    return false;
                }
                catch (Exception ex)
                {
                    // Fail open: If anything goes wrong, don't break the game's kick logic.
                    Log($"[CastleWallsMk2] _processKickMessage suppress-prefix failed: {ex}.");
                    return true;
                }
            }
        }
        #endregion

        #region Projectile Output Tuning

        /// <summary>
        /// Multiplies projectile/tracer output per "shot event" while leaving cooldown/shoot-speed unchanged.
        ///
        /// Hooks:
        /// - Player.ProcessGunshotMessage     (bullets + lasers).
        /// - Player.ProcessShotgunShotMessage (shotgun pellets).
        /// - Player.ProcessFireRocketMessage  (RPG rockets).
        /// - Player.ProcessGrenadeMessage     (thrown grenades).
        /// </summary>
        internal static class ProjectileOutputTuning
        {
            #region User Settings (Edit / Wire To Config / Commands)

            public static bool Enabled => CastleWallsMk2._projectileTuningEnabled;

            // Multipliers (>= 1). 1 = vanilla behavior.
            public static int BulletShotsPerFire => CastleWallsMk2._projectileTuningValue; // conventional bullets (TracerManager).
            public static int LaserShotsPerFire  => CastleWallsMk2._projectileTuningValue; // LaserGunInventoryItemClass (BlasterShot).
            public static int ShotgunSetsPerFire => CastleWallsMk2._projectileTuningValue; // Each set = 5 pellets (ShotgunShotMessage).
            public static int RocketsPerFire     => CastleWallsMk2._projectileTuningValue; // FireRocketMessage.
            public static int GrenadesPerThrow   => CastleWallsMk2._projectileTuningValue; // GrenadeMessage.

            // Optional extra spread (degrees). 0 = duplicates go exactly same direction.
            public static float BulletSpreadDeg  = 0f;
            public static float LaserSpreadDeg   = 0f;
            public static float RocketSpreadDeg  = 0f;
            public static float GrenadeSpreadDeg = 0f;

            // If true, we try to consume extra ammo for the extra shots (LOCAL player only, best-effort).
            // If false, the extras are "free" (still multiplied damage/particles if your sim is authoritative).
            public static bool ConsumeExtraAmmoForLocalPlayer = false;

            // Safety clamp to avoid someone setting 999 and nuking the frame time.
            public static int MaxMultiplierClamp = 256;

            #endregion

            #region Helpers

            public static int ClampMult(int v)
            {
                if (v < 1) return 1;
                if (v > MaxMultiplierClamp) return MaxMultiplierClamp;
                return v;
            }

            /// <summary>
            /// Apply a small random cone around the base direction.
            /// If deg <= 0, returns baseDir normalized.
            /// </summary>
            public static Vector3 ApplySpread(Vector3 baseDir, float deg)
            {
                if (deg <= 0f)
                    return Vector3.Normalize(baseDir);

                Vector3 dir = Vector3.Normalize(baseDir);

                // Build a stable orthonormal basis around dir.
                Vector3 right = Vector3.Cross(dir, Vector3.Up);
                if (right.LengthSquared() < 1e-6f)
                    right = Vector3.Cross(dir, Vector3.Forward);
                right.Normalize();

                Vector3 up = Vector3.Normalize(Vector3.Cross(right, dir));

                // Tiny yaw/pitch inside [-deg, +deg].
                float yaw = MathHelper.ToRadians(RandomSigned(deg));
                float pitch = MathHelper.ToRadians(RandomSigned(deg));

                Quaternion qYaw = Quaternion.CreateFromAxisAngle(up, yaw);
                Quaternion qPitch = Quaternion.CreateFromAxisAngle(right, pitch);

                Vector3 outDir = Vector3.Transform(dir, qPitch * qYaw);
                return Vector3.Normalize(outDir);
            }

            private static readonly Random _rng = new Random();

            private static float RandomSigned(float maxAbs)
                => (float)((_rng.NextDouble() * 2.0 - 1.0) * maxAbs);

            /// <summary>
            /// Best-effort: consume 1 more "shot worth" of ammo from the local active gun.
            /// Returns true if we should proceed with spawning an extra shot.
            /// </summary>
            public static bool TryConsumeOneExtraGunShot()
            {
                if (!ConsumeExtraAmmoForLocalPlayer) return true;

                var game = CastleMinerZGame.Instance;
                if (game == null) return true;

                if (game.InfiniteResourceMode) return true;

                var inv = game.LocalPlayer?.PlayerInventory;
                if (inv == null) return true;

                if (!(inv.ActiveInventoryItem is GunInventoryItem gi)) return true;

                // Rocket launchers in this codebase commonly use StackCount as their "ammo" counter.
                if (gi.GunClass is RocketLauncherInventoryItemClass || gi.GunClass is RocketLauncherGuidedInventoryItemClass)
                {
                    if (gi.StackCount > 0) { gi.StackCount--; return true; }
                    return false;
                }

                // Normal guns: use RoundsInClip.
                if (gi.RoundsInClip > 0) { gi.RoundsInClip--; return true; }
                return false;
            }

            /// <summary>
            /// Returns the final grenade orientation after upstream systems have had a chance
            /// to replace the aim (aimbot pending throw), and after Chaotic Aim has optionally
            /// scrambled that orientation.
            ///
            /// Order matters:
            /// 1) Consume one-shot aimbot/queued orientation if present.
            /// 2) Apply Chaotic Aim last so it wins over vanilla/aimbot aim.
            /// </summary>
            public static Matrix ResolveGrenadeOrientation(Matrix orientation)
            {
                if (AimbotGrenades.TryConsumePending(out Matrix pending))
                    orientation = pending;

                if (CastleWallsMk2._chaoticAimEnabled)
                    orientation = ChaoticAimRuntime.MakeRandomAimMatrix(orientation);

                return orientation;
            }

            /// <summary>
            /// Best-effort: Consume one more grenade from the local active item stack.
            /// Returns true if we should proceed with spawning an extra grenade.
            /// </summary>
            public static bool TryConsumeOneExtraGrenade()
            {
                if (!ConsumeExtraAmmoForLocalPlayer) return true;

                var game = CastleMinerZGame.Instance;
                if (game == null) return true;

                if (game.InfiniteResourceMode) return true;

                var hudItem = game.GameScreen?.HUD?.ActiveInventoryItem;
                if (hudItem == null) return true;

                // Only consume if the active item is actually a grenade stack.
                if (!(hudItem.ItemClass is GrenadeInventoryItemClass)) return true;

                if (hudItem.StackCount > 0)
                {
                    hudItem.PopOneItem(); // matches vanilla behavior in Player.ProcessGrenadeMessage
                    return true;
                }
                return false;
            }
            #endregion
        }

        // =========================================================================================
        // Patches
        // =========================================================================================

        #region GunshotMessage (Bullets + Lasers)

        [HarmonyPatch(typeof(GunshotMessage), nameof(GunshotMessage.Send))]
        internal static class Patch_GunshotMessage_Send
        {
            [ThreadStatic] private static bool _reenter;

            static bool Prefix(LocalNetworkGamer from, Matrix m, Angle innacuracy, InventoryItemIDs item, bool addDropCompensation)
            {
                if (_reenter)
                    return true;

                // Decide multiplier per item (laser vs bullet).
                var cls = InventoryItem.GetClass(item);
                bool isLaser = cls is LaserGunInventoryItemClass;

                int mult = ProjectileOutputTuning.Enabled
                    ? (isLaser
                        ? ProjectileOutputTuning.ClampMult(ProjectileOutputTuning.LaserShotsPerFire)
                        : ProjectileOutputTuning.ClampMult(ProjectileOutputTuning.BulletShotsPerFire))
                    : 1;

                bool chaos = CastleWallsMk2._chaoticAimEnabled;

                // Vanilla path only when neither system is doing anything.
                if (mult <= 1 && !chaos)
                    return true;

                _reenter = true;
                try
                {
                    Matrix baseMatrix = m;

                    for (int i = 0; i < mult; i++)
                    {
                        // Optional: consume extra ammo for duplicates only.
                        if (i > 0 && from.IsLocal && !ProjectileOutputTuning.TryConsumeOneExtraGunShot())
                            break;

                        var msg = MessageBridge.Get<GunshotMessage>();

                        // Every duplicate gets its OWN random chaotic direction.
                        Matrix fireMatrix = baseMatrix;
                        if (chaos)
                            fireMatrix = ChaoticAimRuntime.MakeRandomAimMatrix(baseMatrix);

                        Vector3 shot = fireMatrix.Forward;
                        if (addDropCompensation)
                            shot += fireMatrix.Up * 0.015f;

                        // Vanilla inaccuracy around this duplicate's current forward.
                        Matrix mat = Matrix.CreateRotationX(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));
                        mat *= Matrix.CreateRotationY(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));

                        // Optional extra spread (deg -> rad).
                        float extraRad = MathHelper.ToRadians(
                            isLaser
                                ? ProjectileOutputTuning.LaserSpreadDeg
                                : ProjectileOutputTuning.BulletSpreadDeg);

                        if (extraRad > 0f)
                        {
                            mat *= Matrix.CreateRotationX(MathTools.RandomFloat(-extraRad, extraRad));
                            mat *= Matrix.CreateRotationY(MathTools.RandomFloat(-extraRad, extraRad));
                        }

                        shot = Vector3.TransformNormal(shot, mat);

                        msg.Direction = Vector3.Normalize(shot);
                        msg.ItemID = item;
                        MessageBridge.DoSend.Invoke(msg, new object[] { from });
                    }
                }
                finally
                {
                    _reenter = false;
                }

                return false; // Skip original send (we already sent).
            }
        }
        #endregion

        #region ShotgunShotMessage (Pellets)

        [HarmonyPatch(typeof(ShotgunShotMessage), nameof(ShotgunShotMessage.Send))]
        internal static class Patch_ShotgunShotMessage_Send
        {
            [ThreadStatic] private static bool _reenter;

            static bool Prefix(LocalNetworkGamer from, Matrix m, Angle innacuracy, InventoryItemIDs item, bool addDropCompensation)
            {
                if (_reenter) return true;

                int sets = ProjectileOutputTuning.Enabled
                    ? ProjectileOutputTuning.ClampMult(ProjectileOutputTuning.ShotgunSetsPerFire)
                    : 1;

                bool chaos = CastleWallsMk2._chaoticAimEnabled;

                if (sets <= 1 && !chaos)
                    return true;

                _reenter = true;
                try
                {
                    Matrix baseMatrix = m;

                    for (int set = 0; set < sets; set++)
                    {
                        if (set > 0 && from.IsLocal && !ProjectileOutputTuning.TryConsumeOneExtraGunShot())
                            break;

                        var msg = MessageBridge.Get<ShotgunShotMessage>();

                        Matrix fireMatrix = baseMatrix;
                        if (chaos)
                            fireMatrix = ChaoticAimRuntime.MakeRandomAimMatrix(baseMatrix);

                        for (int i = 0; i < 5; i++)
                        {
                            Vector3 shot = fireMatrix.Forward;
                            if (addDropCompensation)
                                shot += fireMatrix.Up * 0.015f;

                            Matrix mat = Matrix.CreateRotationX(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));
                            mat *= Matrix.CreateRotationY(MathTools.RandomFloat(-innacuracy.Radians, innacuracy.Radians));

                            float extraRad = MathHelper.ToRadians(ProjectileOutputTuning.BulletSpreadDeg);
                            if (extraRad > 0f)
                            {
                                mat *= Matrix.CreateRotationX(MathTools.RandomFloat(-extraRad, extraRad));
                                mat *= Matrix.CreateRotationY(MathTools.RandomFloat(-extraRad, extraRad));
                            }

                            shot = Vector3.TransformNormal(shot, mat);
                            msg.Directions[i] = Vector3.Normalize(shot);
                        }

                        msg.ItemID = item;
                        MessageBridge.DoSend.Invoke(msg, new object[] { from });
                    }
                }
                finally { _reenter = false; }

                return false;
            }
        }
        #endregion

        #region FireRocketMessage (RPG)

        [HarmonyPatch(typeof(FireRocketMessage), nameof(FireRocketMessage.Send))]
        internal static class Patch_FireRocketMessage_Send
        {
            [ThreadStatic] private static bool _reenter;

            static bool Prefix(LocalNetworkGamer from, Matrix orientation, InventoryItemIDs weaponType, bool guided)
            {
                if (_reenter)
                    return true;

                int mult = ProjectileOutputTuning.Enabled
                    ? ProjectileOutputTuning.ClampMult(ProjectileOutputTuning.RocketsPerFire)
                    : 1;

                bool chaos = CastleWallsMk2._chaoticAimEnabled;

                // Vanilla path only when neither system is doing anything.
                if (mult <= 1 && !chaos)
                    return true;

                _reenter = true;
                try
                {
                    Matrix baseOrientation = orientation;

                    for (int i = 0; i < mult; i++)
                    {
                        if (i > 0 && from.IsLocal && !ProjectileOutputTuning.TryConsumeOneExtraGunShot())
                            break;

                        var msg = MessageBridge.Get<FireRocketMessage>();

                        // Every duplicate gets its OWN random chaotic direction.
                        Matrix fireOrientation = baseOrientation;
                        if (chaos)
                            fireOrientation = ChaoticAimRuntime.MakeRandomAimMatrix(baseOrientation);

                        Vector3 dir = fireOrientation.Forward;

                        float extraRad = MathHelper.ToRadians(ProjectileOutputTuning.RocketSpreadDeg);
                        if (extraRad > 0f)
                        {
                            Matrix mat = Matrix.CreateRotationX(MathTools.RandomFloat(-extraRad, extraRad));
                            mat *= Matrix.CreateRotationY(MathTools.RandomFloat(-extraRad, extraRad));
                            dir = Vector3.Normalize(Vector3.TransformNormal(dir, mat));
                        }
                        else
                        {
                            dir = Vector3.Normalize(dir);
                        }

                        msg.Direction = dir;
                        msg.Position = fireOrientation.Translation + dir;
                        msg.WeaponType = weaponType;
                        msg.Guided = guided;

                        MessageBridge.DoSend.Invoke(msg, new object[] { from });
                    }
                }
                finally
                {
                    _reenter = false;
                }

                return false;
            }
        }
        #endregion

        #region GrenadeMessage (Thrown Grenades)

        [HarmonyPatch(typeof(GrenadeMessage), nameof(GrenadeMessage.Send))]
        internal static class Patch_GrenadeMessage_Send
        {
            [ThreadStatic] private static bool _reenter;

            static bool Prefix(LocalNetworkGamer from, Matrix orientation, GrenadeTypeEnum grenadeType, float secondsLeft)
            {
                if (_reenter)
                    return true;

                int mult = ProjectileOutputTuning.Enabled
                    ? ProjectileOutputTuning.ClampMult(ProjectileOutputTuning.GrenadesPerThrow)
                    : 1;

                bool chaos = CastleWallsMk2._chaoticAimEnabled;

                // Vanilla only when neither system is doing anything.
                if (mult <= 1 && !chaos)
                    return true;

                _reenter = true;
                try
                {
                    // Resolve queued aimbot/vanilla throw orientation first.
                    Matrix baseOrientation = ProjectileOutputTuning.ResolveGrenadeOrientation(orientation);

                    for (int i = 0; i < mult; i++)
                    {
                        if (i > 0 && from.IsLocal && !ProjectileOutputTuning.TryConsumeOneExtraGrenade())
                            break;

                        var msg = MessageBridge.Get<GrenadeMessage>();

                        // Every duplicate gets its OWN random chaotic direction.
                        Matrix throwOrientation = baseOrientation;
                        if (chaos)
                            throwOrientation = ChaoticAimRuntime.MakeRandomAimMatrix(baseOrientation);

                        Vector3 dir = throwOrientation.Forward;

                        float extraRad = MathHelper.ToRadians(ProjectileOutputTuning.GrenadeSpreadDeg);
                        if (extraRad > 0f)
                        {
                            Matrix mat = Matrix.CreateRotationX(MathTools.RandomFloat(-extraRad, extraRad));
                            mat *= Matrix.CreateRotationY(MathTools.RandomFloat(-extraRad, extraRad));
                            dir = Vector3.Normalize(Vector3.TransformNormal(dir, mat));
                        }
                        else
                        {
                            dir = Vector3.Normalize(dir);
                        }

                        msg.Direction = dir;
                        msg.Position = throwOrientation.Translation + dir;
                        msg.GrenadeType = grenadeType;
                        msg.SecondsLeft = secondsLeft;

                        MessageBridge.DoSend.Invoke(msg, new object[] { from });
                    }
                }
                finally
                {
                    _reenter = false;
                }

                return false;
            }
        }
        #endregion

        #endregion

        #region Free Fly Camera

        internal static class FreeFlyCamera
        {
            #region Free Cam Settings - Init Once

            private static Keys ToggleKey = Keys.F6;             // Global toggle for showing/hiding the free fly camera.
            private static bool _freeCameraSettingsBootstrapped; // One-time guard so settings are initialized exactly once per process.

            public static void FreeCamSettings_InitOnce()
            {
                if (_freeCameraSettingsBootstrapped) return; // Already done.
                _freeCameraSettingsBootstrapped = true;      // Prevent repeated init.

                // Load configuration with safe fallback.
                var cfg = ModConfig.Current ?? ModConfig.LoadOrCreateDefaults();
                try { ToggleKey = cfg.FreeFlyToggleKey; } catch { }
            }
            #endregion

            #region Public Toggle

            /// <summary>
            /// Master on/off toggle for spectator camera mode.
            /// Set this from config/UI, or use the default hotkey below.
            /// </summary>
            public static bool Enabled
            {
                get => CastleWallsMk2._freeFlyCameraEnabled;
                set => CastleWallsMk2._freeFlyCameraEnabled = value;
            }
            #endregion

            #region Settings

            // private const Keys ToggleKey = Keys.F6;

            private const float MouseSensitivity = 0.0025f; // Radians per pixel-ish.
            private const float BaseSpeed        = 25f;     // Units per second.
            private const float BoostMultiplier  = 4.0f;    // Shift.
            private const float SlowMultiplier   = 0.25f;   // Alt.

            #endregion

            #region Runtime State

            private static PerspectiveCamera _cam;
            private static Vector3           _pos;
            private static float             _yaw;
            private static float             _pitch;

            private static bool  _initializedForScreen;

            // Cached references we restore on disable.
            private static object _mainView;           // CameraView.
            private static object _fpsView;            // CameraView.
            private static Scene  _mainScene;
            private static Camera _restoreMainCamera;  // Usually localPlayer.FPSCamera.
            private static bool   _restoreFpsViewEnabled;

            // Input edge detection.
            private static KeyboardState _prevKb;

            // Mouse lock.
            private static bool  _mouseLocked;
            private static Point _restoreMousePos;

            #endregion

            #region Entry Points (Called By Harmony)

            /// <summary>
            /// Called every frame from CastleMinerZGame.Update prefix.
            /// This runs early enough to keep the camera state ready for the frame.
            /// </summary>
            public static void Tick(GameTime gameTime)
            {
                var game = CastleMinerZGame.Instance;
                if (game == null) return;

                // Load the config settings once.
                FreeCamSettings_InitOnce();

                // If ImGui is opened, disable free-cam.
                if (ImGuiXnaRenderer.Visible)
                {
                    CastleWallsMk2._freeFlyCameraEnabled = false;
                    Enabled = false;
                }

                // Hotkey toggle (optional; remove this if you only want config/UI control)
                HandleToggleHotkey(game);

                if (!Enabled)
                {
                    // If we were active last frame, ensure we restore properly.
                    if (_initializedForScreen)
                        TryDisableSpectator();
                    return;
                }

                // We only initialize once we have an active GameScreen.
                if (game.GameScreen == null)
                    return;

                if (!_initializedForScreen)
                    TryEnableSpectator(game.GameScreen);

                if (_cam == null)
                    return;

                UpdateCameraFromInput(game, gameTime);

                // Keep terrain streaming / eye position aligned to the spectator camera.
                // (Optional but highly recommended, otherwise chunks may load around the player instead.)
                TryApplyTerrainCameraOverrides();
            }
            #endregion

            #region Toggle / Init / Teardown

            private static void HandleToggleHotkey(CastleMinerZGame game)
            {
                // Keyboard toggle for when enabled.
                KeyboardState kb = Keyboard.GetState();

                // Use existing global config instance.
                Keys toggleKey = ToggleKey;

                bool pressed = kb.IsKeyDown(toggleKey) && !_prevKb.IsKeyDown(toggleKey);
                _prevKb = kb;

                if (!pressed) return;

                Enabled = !Enabled;

                // If toggling off, restore immediately.
                if (!Enabled)
                    TryDisableSpectator();
            }

            private static void TryEnableSpectator(GameScreen screen)
            {
                try
                {
                    // --- Grab mainScene, mainView, fpsView via reflection (private fields) ---
                    _mainScene = GetFieldValue<Scene>(screen, "mainScene") ?? FindFirstFieldByType<Scene>(screen);
                    _mainView  = GetFieldValue<object>(screen, "mainView");
                    _fpsView   = GetFieldValue<object>(screen, "_fpsView");

                    if (_mainScene == null || _mainView == null)
                        return;

                    // mainView's current camera is what we restore later.
                    _restoreMainCamera = GetCameraFromCameraView(_mainView);

                    // Create spectator camera and attach to scene (CameraView requires camera.Scene != null)
                    _cam = new PerspectiveCamera();
                    _mainScene.Children.Add(_cam);

                    // Start camera at current main camera position/orientation if available
                    if (_restoreMainCamera != null)
                    {
                        _pos = _restoreMainCamera.WorldPosition;

                        // Derive yaw/pitch from the forward vector
                        Vector3 fwd = _restoreMainCamera.LocalToWorld.Forward;
                        fwd.Normalize();
                        _yaw   = (float)Math.Atan2(fwd.X, -fwd.Z);
                        _pitch = (float)Math.Asin(MathHelper.Clamp(fwd.Y, -0.999f, 0.999f));

                        // FOV match (if the restore cam is a PerspectiveCamera)
                        if (_restoreMainCamera is PerspectiveCamera pc)
                            _cam.FieldOfView = pc.FieldOfView;
                    }
                    else
                    {
                        _pos   = Vector3.Zero;
                        _yaw   = 0;
                        _pitch = 0;
                    }

                    ApplyCamTransform();

                    // Swap main view camera -> spectator cam
                    SetCameraOnCameraView(_mainView, _cam);

                    // Disable FPS overlay (hands/weapons) while spectating
                    if (_fpsView != null)
                    {
                        _restoreFpsViewEnabled = GetViewEnabled(_fpsView);
                        SetViewEnabled(_fpsView, false);
                    }

                    // Lock mouse (optional)
                    LockMouseToCenter(game: CastleMinerZGame.Instance);

                    _initializedForScreen = true;
                }
                catch
                {
                    // If anything fails, keep it non-fatal
                    _initializedForScreen = false;
                }
            }

            private static void TryDisableSpectator()
            {
                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game?.GameScreen != null && _mainView != null)
                    {
                        // Restore main camera
                        if (_restoreMainCamera != null)
                            SetCameraOnCameraView(_mainView, _restoreMainCamera);

                        // Restore FPS overlay view
                        if (_fpsView != null)
                            SetViewEnabled(_fpsView, _restoreFpsViewEnabled);
                    }

                    // Unlock mouse if we locked it
                    UnlockMouse();

                    // You can optionally remove camera from scene; leaving it is fine.
                    // (Removing is cleaner but depends on Scene/Tree remove semantics.)
                    // _cam?.RemoveFromParent();

                    _cam                  = null;
                    _mainView             = null;
                    _fpsView              = null;
                    _mainScene            = null;
                    _restoreMainCamera    = null;
                    _initializedForScreen = false;
                }
                catch
                {
                    // Non-fatal.
                }
            }
            #endregion

            #region Movement + Look

            private static void UpdateCameraFromInput(CastleMinerZGame game, GameTime gameTime)
            {
                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (dt <= 0f) return;

                KeyboardState kb = Keyboard.GetState();

                float speed = BaseSpeed;
                if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))
                    speed *= BoostMultiplier;
                if (kb.IsKeyDown(Keys.LeftAlt) || kb.IsKeyDown(Keys.RightAlt))
                    speed *= SlowMultiplier;

                // Mouse look (locked to center).
                var vp = game.GraphicsDevice.Viewport;
                int cx = vp.Width / 2;
                int cy = vp.Height / 2;

                MouseState ms = Mouse.GetState();
                float dx = ms.X - cx;
                float dy = ms.Y - cy;

                _yaw   -= dx * MouseSensitivity;
                _pitch -= dy * MouseSensitivity;
                _pitch = MathHelper.Clamp(_pitch, -1.5533f, 1.5533f); // ~ +/- 89 degrees.

                // Build basis from yaw/pitch.
                Matrix rot = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
                Vector3 forward = rot.Forward;
                Vector3 right   = rot.Right;

                Vector3 move = Vector3.Zero;

                if (kb.IsKeyDown(Keys.W)) move += forward;
                if (kb.IsKeyDown(Keys.S)) move -= forward;
                if (kb.IsKeyDown(Keys.D)) move += right;
                if (kb.IsKeyDown(Keys.A)) move -= right;

                // Vertical.
                if (kb.IsKeyDown(Keys.Space)) move += Vector3.Up;
                if (kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl)) move -= Vector3.Up;

                if (move.LengthSquared() > 0f)
                {
                    move.Normalize();
                    _pos += move * speed * dt;
                }

                ApplyCamTransform();

                // Keep mouse centered if locked.
                if (_mouseLocked)
                    Mouse.SetPosition(cx, cy);
            }

            private static void ApplyCamTransform()
            {
                Matrix m           = Matrix.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
                m.Translation      = _pos;
                _cam.LocalToParent = m;
            }
            #endregion

            #region Terrain / Eye Overrides

            private static void TryApplyTerrainCameraOverrides()
            {
                try
                {
                    if (_cam == null) return;

                    // These are used in Player.OnUpdate in your Player.cs snippet.
                    BlockTerrain.Instance.CenterOn(_cam.WorldPosition, true);
                    BlockTerrain.Instance.EyePos = _cam.WorldPosition;
                    BlockTerrain.Instance.ViewVector = _cam.LocalToWorld.Forward;
                }
                catch
                {
                    // non-fatal
                }
            }
            #endregion

            #region Mouse Lock Helpers

            private static void LockMouseToCenter(CastleMinerZGame game)
            {
                if (_mouseLocked || game == null) return;

                // Save current cursor pos so you can restore it later
                var ms = Mouse.GetState();
                _restoreMousePos = new Point(ms.X, ms.Y);

                var vp = game.GraphicsDevice.Viewport;
                Mouse.SetPosition(vp.Width / 2, vp.Height / 2);
                _mouseLocked = true;
            }

            private static void UnlockMouse()
            {
                if (!_mouseLocked) return;

                try
                {
                    Mouse.SetPosition(_restoreMousePos.X, _restoreMousePos.Y);
                }
                catch { }

                _mouseLocked = false;
            }
            #endregion

            #region Reflection Utilities (GameScreen.mainView, CameraView.Camera, View.Enabled)

            private static T GetFieldValue<T>(object obj, string fieldName) where T : class
            {
                if (obj == null) return null;
                var f = AccessTools.Field(obj.GetType(), fieldName);
                return f?.GetValue(obj) as T;
            }

            private static T FindFirstFieldByType<T>(object obj) where T : class
            {
                if (obj == null) return null;

                var t = obj.GetType();
                var f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .FirstOrDefault(x => typeof(T).IsAssignableFrom(x.FieldType));

                return f?.GetValue(obj) as T;
            }

            private static Camera GetCameraFromCameraView(object cameraView)
            {
                if (cameraView == null) return null;

                // Try property first
                var p = AccessTools.Property(cameraView.GetType(), "Camera");
                if (p != null)
                    return p.GetValue(cameraView, null) as Camera;

                // Fallback to private field
                var f = AccessTools.Field(cameraView.GetType(), "_camera");
                return f?.GetValue(cameraView) as Camera;
            }

            private static void SetCameraOnCameraView(object cameraView, Camera cam)
            {
                if (cameraView == null) return;

                // Try property setter first
                var p = AccessTools.Property(cameraView.GetType(), "Camera");
                if (p != null && p.CanWrite)
                {
                    p.SetValue(cameraView, cam, null);
                    return;
                }

                // Fallback: set private field
                var f = AccessTools.Field(cameraView.GetType(), "_camera");
                f?.SetValue(cameraView, cam);
            }

            private static bool GetViewEnabled(object view)
            {
                if (view == null) return true;

                var f = AccessTools.Field(view.GetType(), "Enabled");
                if (f != null && f.FieldType == typeof(bool))
                    return (bool)f.GetValue(view);

                return true;
            }

            private static void SetViewEnabled(object view, bool enabled)
            {
                if (view == null) return;

                var f = AccessTools.Field(view.GetType(), "Enabled");
                if (f != null && f.FieldType == typeof(bool))
                    f.SetValue(view, enabled);
            }
            #endregion
        }

        #region Patches

        /// <summary>
        /// Run our camera tick every frame.
        /// Prefix is nice because the camera is updated before most of the game frame runs.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "Update")]
        internal static class Patch_CastleMinerZGame_Update
        {
            private static void Prefix(GameTime gameTime)
            {
                FreeFlyCamera.Tick(gameTime);
            }
        }

        /// <summary>
        /// Optional: Prevent the local player from processing gameplay input while spectating.
        /// This keeps you from firing/tools/etc while the camera is detached.
        /// </summary>
        [HarmonyPatch(typeof(Player), "ProcessInput")]
        internal static class Patch_Player_ProcessInput
        {
            private static bool Prefix(Player __instance, GameTime gameTime)
            {
                if (!FreeFlyCamera.Enabled)
                    return true;

                // Only block the local player (avoid affecting remote players).
                if (__instance == null || !__instance.IsLocal)
                    return true;

                return false; // Skip original input processing.
            }
        }
        #endregion

        #endregion

        #region No Clip (Terrain)

        // NOTE:
        // - This bypasses ONLY terrain collision (BlockTerrain).
        // - Other entity collisions still run (enemies, pickups, etc).

        [HarmonyPatch(typeof(Player), nameof(Player.ResolveCollsion))]
        internal static class Patch_Player_ResolveCollsion_NoClipTerrain
        {
            private static bool Prefix(
                Player    __instance,
                Entity    e,
                ref Plane collsionPlane,
                GameTime  dt,
                ref bool  __result)
            {
                // Read from cached bool.
                if (!CastleWallsMk2._noClipEnabled)
                    return true;

                // Only bypass terrain collisions.
                if (e != BlockTerrain.Instance)
                    return true;

                // LOCAL-ONLY: Do NOT touch remote players.
                // Prefer IsLocal if available (covers splitscreen too).
                try
                {
                    if (!__instance.IsLocal)
                        return true;
                }
                catch { }

                // Optional: avoid client-side desync in online games (host-only).
                // if (CastleMinerZGame.Instance.IsOnlineGame && !CastleMinerZGame.Instance.MyNetworkGamer.IsHost)
                //     return true;

                float t = (float)dt.ElapsedGameTime.TotalSeconds;

                // Move freely: nextPos = current + velocity * dt
                Vector3 worldPos = __instance.WorldPosition;
                Vector3 vel      = __instance.PlayerPhysics.WorldVelocity;
                Vector3 nextPos  = worldPos + vel * t;

                __instance.LocalPosition = nextPos;

                // Keep consistent outputs: "no collision happened".
                collsionPlane = default;
                __result      = false;
                return false; // Skip original ResolveCollsion.
            }
        }
        #endregion

        #region Shoot Bow Ammo

        /// <summary>
        /// Replaces gun tracers with "arrow" tracers by routing AddTracer(...) into AddArrow(...)
        /// (which uses Tracer.Init(pos, vel, Player target, Color color)).
        ///
        /// IMPORTANT:
        /// - AddArrow requires a Player target (NOT a direction vector).
        /// - This only makes sense when we can pick a valid player target (PVP).
        /// </summary>
        [HarmonyPatch(typeof(TracerManager), nameof(TracerManager.AddTracer))]
        internal static class Patch_TracerManager_AddTracer_Arrowify
        {
            // ~10 degrees cone in front of the shot.
            private const float AIM_DOT_THRESHOLD = 0.985f;

            private static bool Prefix(
                TracerManager __instance,
                Vector3 position,
                Vector3 velocity,
                InventoryItemIDs item,
                byte shooterID)
            {
                // Gate.
                if (!CastleWallsMk2._shootBowAmmoEnabled)
                    return true;

                // Your original "&& shooterID" should be something like:
                // - shooterID != byte.MaxValue (engine uses MaxValue for non-player shots), or
                // - shooterID == local gamer id (only affect our shots)
                var myGamer = CastleMinerZGame.Instance?.MyNetworkGamer;
                if (myGamer == null || shooterID != myGamer.Id)
                    return true;

                // Change the starting position to the muzzle.
                var mi = AccessTools.Method(typeof(Player), "GetGunTipPosition");
                position = (Vector3)mi.Invoke(CastleMinerZGame.Instance.LocalPlayer, null);

                // AddTracer's "velocity" is commonly a DIRECTION vector for bullets/tracers.
                Vector3 dir = velocity;

                if (dir.LengthSquared() < 1e-6f)
                    dir = CastleMinerZGame.Instance.LocalPlayer?.FPSCamera?.LocalToWorld.Forward ?? Vector3.Forward;
                else
                    dir.Normalize();

                // Convert direction-ish input into an actual projectile velocity.
                Vector3 arrowVel = ComputeProjectileVelocity(velocity, dir, item);

                // Use the game's arrow path (calls Tracer.Init(pos, vel, target, Color.Pink))
                __instance.AddArrow(position, arrowVel, (Player)CastleMinerZGame.Instance.MyNetworkGamer.Tag);

                // Optional: if you want arrows to alert AI like gunshots, uncomment:
                EnemyManager.Instance?.RegisterGunShot(position);

                return false; // skip original AddTracer
            }

            private static Vector3 ComputeProjectileVelocity(Vector3 velocityArg, Vector3 dirNorm, InventoryItemIDs item)
            {
                // If the incoming vector already looks like a speed vector, keep it.
                // If it looks like a unit direction, scale to the weapon velocity.
                if (velocityArg.LengthSquared() > 9f) // > ~3 units/sec
                    return velocityArg;

                float speed = 60f; // fallback
                if (InventoryItem.GetClass(item) is GunInventoryItemClass gun)
                    speed = gun.Velocity;

                return dirNorm * speed;
            }
        }
        #endregion

        #region Exploding Ores

        /// <summary>
        /// If ExplodingOres is enabled, ore pickups detonate instantly at their current position
        /// and are removed from the world.
        /// </summary>
        [HarmonyPatch(typeof(PickupEntity), "OnUpdate")]
        internal static class Patch_PickupEntity_OnUpdate_ExplodingOres
        {
            private static readonly HashSet<string> ExplodingOreNames =
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "Coal",
                    "Copper Ore",
                    "Diamond",
                    "Gold Ore",
                    "Iron Ore",
                    "Space Goo",
                    "BloodStone",
                    "Space Rock",
                };

            private static bool Prefix(PickupEntity __instance, GameTime gameTime)
            {
                try
                {
                    // Gate on our flag.
                    if (!CastleWallsMk2._explodingOresEnabled)
                        return true;

                    // Item / name guard.
                    var item = __instance.Item;
                    if (item == null || string.IsNullOrEmpty(item.Name))
                        return true;

                    if (!ExplodingOreNames.Contains(item.Name))
                        return true;

                    // Need a LocalNetworkGamer to send the net message.
                    if (!(CastleMinerZGame.Instance.LocalPlayer?.Gamer is LocalNetworkGamer local))
                        return true;

                    // Detonate at the pickup position, then remove the pickup.
                    DetonateExplosiveMessage.Send(
                        local,
                        (IntVector3)__instance.LocalPosition,
                        true,
                        ExplosiveTypes.C4);

                    PickupManager.Instance.RemovePickup(__instance);

                    // Skip vanilla update (entity is removed).
                    return false;
                }
                catch
                {
                    // If anything goes wrong, fall back to vanilla behavior.
                    return true;
                }
            }
        }
        #endregion

        #region Shoot Fireballs

        /// <summary>
        /// Shoot Fireballs (Gun Tracers -> Fireballs)
        /// ==========================================
        ///
        /// Overview:
        /// - Hooks TracerManager.AddTracer(...) for our own gunshots and spawns a FireballEntity instead.
        /// - Uses the private Player.GetGunTipPosition() to start at the muzzle.
        /// - Pushes the spawn forward so it doesn't clip into the player.
        /// - Registers the fireball in EnemyManager so it can be found by FireballIndex.
        ///
        /// Notes:
        /// - CLIENT_FB_MARK is used to tag "our" fireballs so we can intercept detonation logic safely.
        /// - KEEP_TRACER_TOO controls whether the vanilla tracer still renders.
        /// - spawnedLocally is set to true so detonation flows through EnemyManager.DetonateFireball(...),
        ///   which is where we do custom handling (visual detonate + local crater + optional damage broadcast).
        ///
        /// Multiplayer:
        /// - BroadcastFireballDamageOnly(...) sends a DetonateFireballMessage with NumBlocks=0.
        ///   This is "damage-only" (no terrain edits), and relies on vanilla clients applying fireball damage.
        /// </summary>

        #region Patch: TracerManager.AddTracer -> Spawn FireballEntity

        /// <summary>
        /// Client-only: Replace gun tracers with a locally-spawned dragon fireball.
        /// - No network messages are sent (except optional damage-only broadcast at detonation time).
        /// - Only YOU will see the fireball visuals unless you also broadcast detonation/damage.
        /// - Uses muzzle tip + current aim direction.
        /// </summary>
        [HarmonyPatch(typeof(TracerManager), nameof(TracerManager.AddTracer))]
        internal static class Patch_TracerManager_AddTracer_ClientFireball
        {
            #region Constants / State

            private const int CLIENT_FB_MARK = unchecked((int)0x40000000); // bit30 marker.
            private static int _clientFbCounter = 1;

            // If true, keep the normal tracer and ALSO spawn a fireball (visual overlay).
            // If false, fully replace the tracer (skip original AddTracer).
            private const bool KEEP_TRACER_TOO = false;

            #endregion

            #region Cached Reflection

            // Cache the private muzzle helper so we don't reflect every shot.
            private static readonly Func<Player, Vector3> _getGunTip =
                AccessTools.MethodDelegate<Func<Player, Vector3>>(
                    AccessTools.Method(typeof(Player), "GetGunTipPosition"));

            #endregion

            private static bool Prefix(
                TracerManager    __instance,
                ref Vector3      position,
                Vector3          velocity,
                InventoryItemIDs item,
                byte             shooterID)
            {
                #region Gates / Ownership

                // Gate.
                if (!CastleWallsMk2._shootFireballAmmoEnabled)
                    return true;

                // Only affect OUR shots.
                var myGamer = CastleMinerZGame.Instance?.MyNetworkGamer;
                if (myGamer == null || shooterID != myGamer.Id)
                    return true;

                var lp = CastleMinerZGame.Instance.LocalPlayer;
                if (lp == null)
                    return true;

                #endregion

                #region Muzzle Position

                // Start from muzzle tip (matches gun barrel tip).
                try { position = _getGunTip(lp); } catch { /* Fallback to given position. */ }

                #endregion

                #region Aim Direction

                // Derive aim direction from tracer velocity (often a direction vector).
                Vector3 dir = velocity;
                if (dir.LengthSquared() < 1e-6f)
                {
                    dir = lp.FPSCamera != null ? lp.FPSCamera.LocalToWorld.Forward : Vector3.Forward;
                }
                else
                {
                    dir.Normalize();
                }
                #endregion

                #region Spawn Offset

                // Move slightly in front of the muzzle.
                const float FIREBALL_SPAWN_FORWARD = 2.00f;
                const float FIREBALL_SPAWN_UP      = 0.00f;
                position += dir        * FIREBALL_SPAWN_FORWARD;
                position += Vector3.Up * FIREBALL_SPAWN_UP;

                #endregion

                #region Target Selection

                // Pick a target point.
                // Use forward location.
                Vector3 target = position + dir * 200f;

                #endregion

                #region Fireball Construction

                // Pick a dragon type for visuals.
                DragonType type = DragonType.Types[(int)CastleWallsMk2._shootFireballType];

                int idx = CLIENT_FB_MARK | (_clientFbCounter++ & 0x3FFFFFFF);

                // IMPORTANT: "spawnedLocally" should be FALSE for strict client-only visuals.
                // If TRUE, FireballEntity may participate in host-style detonation messaging.
                bool spawnedLocally = true;

                var fb = new FireballEntity(position, target, idx, type, spawnedLocally);

                #endregion

                #region Register + Attach

                // Add to scene.
                var scene = DNA.CastleMinerZ.Terrain.BlockTerrain.Instance.Scene;
                if (scene != null && scene.Children != null)
                    scene.Children.Add(fb);

                // Optional: If you want it to be discoverable for local detonation lookup paths,
                // you can also register it in EnemyManager (still client-only).
                EnemyManager.Instance.AddFireball(fb);

                #endregion

                // Keep or replace the original tracer.
                return KEEP_TRACER_TOO;
            }
        }
        #endregion

        #region Patch: EnemyManager.DetonateFireball -> Custom Detonation (Marked Fireballs)

        private const int CLIENT_FB_MARK = unchecked((int)0x40000000);

        [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.DetonateFireball))]
        internal static class Patch_EnemyManager_DetonateFireball_ClientOnly
        {
            #region Reflection Handles

            private static readonly FieldInfo FiFireballs =
                AccessTools.Field(typeof(EnemyManager), "_fireballs");

            #endregion

            private static bool Prefix(EnemyManager __instance, Vector3 position, int index, DragonType dragonType)
            {
                // Only intercept our marked "gun fireball" detonations.
                if ((index & CLIENT_FB_MARK) == 0)
                    return true;

                // Impact point (position == hit location).
                BroadcastFireballDamageOnly(position, index, dragonType.EType);

                #region 1) Detonate the Visual Fireball Entity (No Net)

                try
                {
                    if (FiFireballs.GetValue(__instance) is List<FireballEntity> list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] != null && list[i].FireballIndex == index)
                            {
                                list[i].Detonate(position);
                                break;
                            }
                        }
                    }
                }
                catch { /* Ignore. */ }

                #endregion

                #region 2) Apply Terrain Crater Locally

                // Vanilla only breaks terrain in Endurance/DragonEndurance or HARD/HARDCORE.
                // If you want it ALWAYS, comment out this block.
                if (!(CastleMinerZGame.Instance.GameMode == GameModeTypes.Endurance
                   || CastleMinerZGame.Instance.GameMode == GameModeTypes.DragonEndurance
                   || CastleMinerZGame.Instance.Difficulty == GameDifficultyTypes.HARD
                   || CastleMinerZGame.Instance.Difficulty == GameDifficultyTypes.HARDCORE))
                {
                    return false; // Skip original (and importantly: skip sending net messages).
                }

                BlockTypeEnum newType = (dragonType.DamageType == DragonDamageType.ICE)
                    ? BlockTypeEnum.Ice
                    : BlockTypeEnum.Empty;

                // Match vanilla rounding to center of voxel.
                Vector3 basePos = new Vector3(
                    (float)Math.Floor(position.X) + 0.5f,
                    (float)Math.Floor(position.Y) + 0.5f,
                    (float)Math.Floor(position.Z) + 0.5f);

                // Same radius/shape as vanilla (sphere radius 3).
                for (float dx = -3f; dx <= 3f; dx += 1f)
                    for (float dy = -3f; dy <= 3f; dy += 1f)
                        for (float dz = -3f; dz <= 3f; dz += 1f)
                        {
                            Vector3 test = new Vector3(basePos.X + dx, basePos.Y + dy, basePos.Z + dz);

                            if (Vector3.DistanceSquared(test, position) > 9f)
                                continue;

                            IntVector3 world = (IntVector3)test;
                            IntVector3 local = BlockTerrain.Instance.GetLocalIndex(world);

                            if (!BlockTerrain.Instance.IsIndexValid(local))
                                continue;

                            BlockTypeEnum bt = Block.GetTypeIndex(BlockTerrain.Instance.GetBlockAt(local));

                            // Keep vanilla "unbreakable by this dragon type" rules.
                            if (DragonType.BreakLookup[(int)dragonType.EType, (int)bt])
                                continue;

                            // Vanilla special-case: don't treat upper doors normally.
                            if (IsUpperDoor(bt))
                                continue;

                            // BlockTerrain.Instance.SetBlock(world, newType);
                            AlterBlockMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, world, newType);

                            // Vanilla: if we hit certain lower-door variants, also remove upper.
                            if (bt == BlockTypeEnum.NormalLowerDoorOpenX   || bt == BlockTypeEnum.NormalLowerDoorOpenZ   ||
                                bt == BlockTypeEnum.NormalLowerDoorClosedX || bt == BlockTypeEnum.NormalLowerDoorClosedZ ||
                                bt == BlockTypeEnum.StrongLowerDoorOpenX   || bt == BlockTypeEnum.StrongLowerDoorOpenZ   ||
                                bt == BlockTypeEnum.StrongLowerDoorClosedX || bt == BlockTypeEnum.StrongLowerDoorClosedZ)
                            {
                                IntVector3 upper = world + new IntVector3(0, 1, 0);
                                // BlockTerrain.Instance.SetBlock(upper, newType);
                                AlterBlockMessage.Send(CastleMinerZGame.Instance.MyNetworkGamer, upper, newType);
                            }
                        }
                #endregion

                // We handled detonation locally. Skip original to prevent net sends.
                return false;
            }

            #region Helpers

            static bool IsUpperDoor(BlockTypeEnum blockType)
            {
                return blockType == BlockTypeEnum.NormalUpperDoorClosed || blockType == BlockTypeEnum.NormalUpperDoorOpen || blockType == BlockTypeEnum.StrongUpperDoorClosed || blockType == BlockTypeEnum.StrongUpperDoorOpen;
            }
            #endregion
        }
        #endregion

        #region Net Helper: Damage-Only Detonation Broadcast

        /// <summary>
        /// Broadcast a "damage-only" fireball detonation.
        ///
        /// Summary:
        /// - Sends DetonateFireballMessage with NumBlocks=0, so no terrain edits are applied by recipients.
        /// - Relies on vanilla clients handling HandleDetonateFireballMessage(...) to apply:
        ///   - Radius + LOS probe damage to their own local player.
        ///   - (Visual detonate only if a matching FireballEntity exists with the same index.)
        ///
        /// Notes:
        /// - This is not strictly client-only because it emits a net message.
        /// - If you want strict local-only behavior, do NOT call this.
        /// </summary>
        private static void BroadcastFireballDamageOnly(Vector3 hitPos, int index, DragonTypeEnum eType)
        {
            if (!(CastleMinerZGame.Instance.LocalPlayer?.Gamer is LocalNetworkGamer local))
                return;

            // No block edits.
            byte numBlocks = 0;
            IntVector3[] blocks = Array.Empty<IntVector3>();

            // This will make EVERY client run HandleDetonateFireballMessage:
            // - Detonate visuals only if they have a matching FireballEntity (usually they won't).
            // - BUT damage to their local player WILL still occur if within 5m and LOS allows.
            // - No terrain change because numBlocks == 0.
            DetonateFireballMessage.Send(local, hitPos, index, numBlocks, blocks, eType);
        }
        #endregion

        #endregion

        #region Rocket Speed

        /// <summary>
        /// Overrides RocketEntity._maxSpeed after construction.
        /// Notes:
        /// - This affects rocket simulation on this client (and/or host if you run it there).
        /// - Guided rockets are weaponType == RocketLauncherGuided.
        /// </summary>
        [HarmonyPatch(typeof(RocketEntity))]
        internal static class Patch_RocketEntity_MaxSpeed
        {
            // Fast private-field access (Harmony FieldRef).
            private static readonly AccessTools.FieldRef<RocketEntity, float> FrMaxSpeed =
                AccessTools.FieldRefAccess<RocketEntity, float>("_maxSpeed");

            [HarmonyPostfix]
            [HarmonyPatch(MethodType.Constructor, new[]
            {
                typeof(Vector3),
                typeof(Vector3),
                typeof(InventoryItemIDs),
                typeof(bool),
                typeof(bool)
            })]
            private static void Ctor_Postfix(RocketEntity __instance, InventoryItemIDs weaponType)
            {
                // Vanilla values:
                // Rocket: 25f.
                // Guided: 50f.

                // Gate.
                if (!CastleWallsMk2._rocketSpeedEnabled)
                    return;

                // Option A: absolute override.
                if (weaponType == InventoryItemIDs.RocketLauncherGuided)
                    FrMaxSpeed(__instance) = CastleWallsMk2._rocketSpeedGuidedValue;
                else
                    FrMaxSpeed(__instance) = CastleWallsMk2._rocketSpeedValue;

                // Option B: multiplier (keeps the vanilla guided/unguided ratio)
                // FrMaxSpeed(__instance) *= CastleWallsMk2._rocketSpeedMultiplier; // e.g. 1.5f
            }
        }
        #endregion

        #region Disable Inventory Retrieval

        /// <summary>
        /// Prevents the server-sent inventory payload from being applied to the player.
        ///
        /// Why patch here?
        /// - InventoryRetrieveFromServerMessage is internal, so you can't reference it directly.
        /// - This method runs AFTER the message is decoded, so skipping it won't break the reader/stream.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processInventoryRetrieveFromServerMessage")]
        internal static class Patch_CastleMinerZGame_ProcessInventoryRetrieveFromServerMessage_Block
        {
            [HarmonyPrefix]
            private static bool Prefix(CastleMinerZGame __instance, Message message, bool isHost)
            {
                if (!CastleWallsMk2._disableInvRetrievalEnabled)
                    return true; // Run vanilla.

                if (message == null)
                    return true;

                // Only block this specific message type (without referencing the internal type).
                var mt = message.GetType();
                if (mt.FullName != "DNA.CastleMinerZ.Net.InventoryRetrieveFromServerMessage")
                    return true;

                // Only block when it targets our local player id.
                var fPlayerId = AccessTools.Field(mt, "playerID");
                if (fPlayerId != null)
                {
                    byte id = (byte)fPlayerId.GetValue(message);
                    if (!__instance.IsLocalPlayerId(id))
                        return true; // Let other players update normally.
                }

                // Skip vanilla apply (prevents: player.PlayerInventory = pism.Inventory).
                return false;
            }
        }
        #endregion

        #region Lava Visuals Toggle

        /// <summary>
        /// Patch: DNA.CastleMinerZ.Player.InLava (getter)
        /// When CastleWallsMk2._lavaVisualsEnabled is enabled, force InLava = false.
        ///
        /// This will affect any logic that relies on Player.InLava (damage, audio, HUD, etc.)
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.InLava), MethodType.Getter)]
        internal static class Patch_Player_get_InLava_LavaVisuals
        {
            [HarmonyPrefix]
            private static bool Prefix(ref bool __result)
            {
                // Only override when the feature is enabled.
                if (!CastleWallsMk2._noLavaVisualsEnabled)
                    return true; // Run original getter (PercentSubmergedLava > 0).

                __result = false; // Force "not in lava".
                return false;     // Skip original getter.
            }
        }
        #endregion

        #region Hug

        /// <summary>
        /// PLAYERUPDATE - Hug one-shot spoof.
        /// Goal: When Hug turns ON, send exactly one spoofed PlayerUpdate that teleports the replicated model
        ///       (what other players see) to the current hug destination (CastleWallsMk2._hugLocation).
        ///       After that one send, suppress further PlayerUpdate broadcasts while Hug remains enabled,
        ///       so the remote puppet stays "parked" at the last hug location until Hug is turned OFF again.
        ///
        /// Why one-shot?
        ///   • A single spoofed update is enough to reposition the remote puppet.
        ///   • Spamming every tick wastes bandwidth and can fight the game's smoothing/lerp.
        ///
        /// Behavior:
        ///   • Rising-edge latch detects OFF -> ON and arms a one-shot send.
        ///   • On the first frame Hug is enabled, a PlayerUpdateMessage is built with LocalPosition overridden
        ///     to CastleWallsMk2._hugLocation and broadcast normally.
        ///   • Subsequent PlayerUpdateMessage.Send calls while Hug remains enabled return false (no further updates).
        ///   • When Hug turns OFF, vanilla updates resume automatically.
        /// </summary>
        [HarmonyPatch(typeof(PlayerUpdateMessage), nameof(PlayerUpdateMessage.Send))]
        internal static class PlayerUpdateMessage_Send_Hug
        {
            static bool Prefix(LocalNetworkGamer from, Player player, CastleMinerZControllerMapping input)
            {
                // If hug is not enabled, run vanilla.
                if (!CastleWallsMk2._hugEnabled) return true;

                try
                {
                    // Build and send a spoofed update. If we don't have a cached spoof yet,
                    // fall back to current position (still prevents running GetSpoofPosition()).
                    var msg = MessageBridge.Get<PlayerUpdateMessage>();

                    // Get the vanish location based on who is host.
                    Vector3 spoofLocation = CastleWallsMk2._hugLocation;

                    var existingPlayer = CastleMinerZGame.Instance.LocalPlayer;

                    // Fill fields (same as original) BUT override LocalPosition.
                    msg.LocalPosition       = spoofLocation;
                    msg.WorldVelocity       = Vector3.Zero;
                    msg.LocalRotation       = existingPlayer.LocalRotation;
                    msg.Movement            = Vector2.Zero;
                    msg.TorsoPitch          = existingPlayer.TorsoPitch;
                    msg.Using               = existingPlayer.UsingTool;
                    msg.Shouldering         = existingPlayer.Shouldering;
                    msg.Reloading           = existingPlayer.Reloading;
                    msg.PlayerMode          = existingPlayer._playerMode;
                    msg.Dead                = false;
                    msg.ThrowingGrenade     = false;
                    msg.ReadyToThrowGrenade = existingPlayer.ReadyToThrowGrenade;

                    // Send (protected) via reflection.
                    MessageBridge.DoSend.Invoke(msg, new object[] { from });

                    // We handled it - skip the original method.
                    return false;
                }
                catch
                {
                    // If anything goes wrong, fall back to vanilla path (better to degrade gracefully).
                    return true;
                }
            }
        }
        #endregion

        #region Mute

        #region In-Game Chat: Mute Selected Players

        /// <summary>
        /// Chat mute patch for BroadcastTextMessage.
        ///
        /// Purpose:
        /// - Prevent selected remote players' chat lines from being printed to this client's
        ///   in-game chat / console output.
        ///
        /// Behavior:
        /// - If sender is not muted: Run vanilla normally.
        /// - If sender is muted:     Skip the original _processBroadcastTextMessage(...) method,
        ///                           so the line is never shown locally. Broadcast newlines to
        ///                           clear the chat so no users see the original message.
        ///
        /// Notes:
        /// - This is the cleanest place to implement a personal mute list because vanilla's
        ///   handler only does:
        ///       BroadcastTextMessage btm = (BroadcastTextMessage)message;
        ///       Console.WriteLine(btm.Message);
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "_processBroadcastTextMessage")]
        internal static class Patch_CastleMinerZGame_ProcessBroadcastTextMessage_MutePlayers
        {
            [HarmonyPrefix]
            private static bool Prefix(Message message)
            {
                try
                {
                    if (!(message is BroadcastTextMessage btm))
                        return true; // Not expected, let vanilla handle it.

                    // Ensure the sender and game instance is valid.
                    var game = CastleMinerZGame.Instance;
                    NetworkGamer sender = message.Sender;
                    if (sender == null || game == null)
                        return true;

                    // If this sender is muted, block the local display.
                    if (IsMutedChatSender(sender))
                    {
                        // Clear the global chat.
                        BroadcastTextMessage.Send(game.MyNetworkGamer, "\n\n\n\n\n\n\n\n\n\n");

                        // Warn the offender they're muted (after clear chat).
                        if (CastleWallsMk2._muteWarnOffenderEnabled)
                        {
                            SendPrivateChatMessage(game.MyNetworkGamer, sender, $"[CAPTURED] --> '{btm.Message}'.");
                            SendPrivateChatMessage(game.MyNetworkGamer, sender, $"[ERROR] :: Your messasge was not sent! You have been muted!");
                        }

                        // Display their message to us.
                        if (CastleWallsMk2._muteShowMessageEnabled)
                            SendPrivateChatMessage(game.MyNetworkGamer, game.MyNetworkGamer, $"[Muted]: '{btm.Message}'");

                        return false; // Skip vanilla Console.WriteLine(...)
                    }

                    return true; // Not muted -> Allow normal chat display.
                }
                catch
                {
                    // Never break chat flow because of mute logic.
                    return true;
                }
            }
        }
        #endregion

        #region Chat Mute Helpers

        /// <summary>
        /// Returns true if the given sender should be muted locally based on the current
        /// mute targeting mode / selected net IDs.
        /// </summary>
        private static bool IsMutedChatSender(NetworkGamer sender)
        {
            if (sender == null)
                return false;

            // Never mute ourselves.
            if (ReferenceEquals(sender, CastleMinerZGame.Instance?.MyNetworkGamer))
                return false;

            switch (CastleWallsMk2._muteTargetMode)
            {
                case CastleWallsMk2.PlayerTargetMode.None:
                    return false;

                case CastleWallsMk2.PlayerTargetMode.AllPlayers:
                    return true;

                case CastleWallsMk2.PlayerTargetMode.Player:
                    return CastleWallsMk2._muteTargetNetids != null &&
                           CastleWallsMk2.IdMatchUtils.ContainsId(CastleWallsMk2._muteTargetNetids, sender.Id);

                default:
                    return false;
            }
        }

        // Sends a single-recipient chat packet (direct/unicast).
        static void SendPrivateChatMessage(LocalNetworkGamer from, NetworkGamer to, string message)
        {
            // Define the send instance message type.
            var sendInstance = MessageBridge.Get<BroadcastTextMessage>();

            // Packet fields.
            sendInstance.Message = message;

            // Send only to the targeted player.
            MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
        }
        #endregion

        #endregion

        #region Dragon Counter

        internal static class DragonEnduranceKillAnnouncer
        {
            #region Private State

            private static bool _pendingDragonKillAnnounce;
            private static int  _pendingDragonKillCount    = -1;

            // Simple dedupe so the same kill does not get queued twice.
            private static int  _lastQueuedDragonKillCount = -1;

            #endregion

            #region Private Field Access

            private static readonly AccessTools.FieldRef<EnemyManager, DragonClientEntity> _dragonClientRef =
                AccessTools.FieldRefAccess<EnemyManager, DragonClientEntity>("_dragonClient");

            #endregion

            #region Public Tick Consumer

            /// <summary>
            /// Summary:
            /// Consumes a queued Dragon Endurance kill announcement once.
            /// Call this from your fast tick/update loop.
            /// </summary>
            public static void TickDragonKillAnnouncement()
            {
                if (!_pendingDragonKillAnnounce)
                    return;

                int defeatedCount = _pendingDragonKillCount;

                _pendingDragonKillAnnounce = false;
                _pendingDragonKillCount    = -1;

                SendLog($"[Server] Dragon #{defeatedCount} defeated.", false);
            }
            #endregion

            #region Kill Hook

            /// <summary>
            /// Summary:
            /// Queues the Dragon Endurance dragon count before the client marks/removes the dragon.
            ///
            /// Notes:
            /// - Prefix is used so _dragonClient is still valid and alive.
            /// - DE_DragonsDefeated() returns the count encoded into the current live dragon HP,
            ///   so the just-killed dragon is +1 beyond that value.
            /// </summary>
            [HarmonyPatch(typeof(EnemyManager), nameof(EnemyManager.HandleKillDragonMessage))]
            internal static class Patch_EnemyManager_HandleKillDragonMessage_QueueAnnouncement
            {
                private static void Prefix(EnemyManager __instance, KillDragonMessage msg)
                {
                    if (CastleMinerZGame.Instance == null)
                        return;

                    if (CastleMinerZGame.Instance.IsGameHost &&
                        CastleMinerZGame.Instance.GameMode != GameModeTypes.DragonEndurance)
                        return;

                    DragonClientEntity dragon = _dragonClientRef(__instance);
                    if (dragon == null || dragon.Dead)
                        return;

                    // Optional:
                    // Only announce when the local player got the kill.
                    // Remove this block if you want all dragon kills announced.
                    /*
                    if (!CastleMinerZGame.Instance.IsLocalPlayerId(msg.KillerID))
                        return;
                    */

                    int defeatedAfterThisKill = DE_DragonsDefeated() + 1;
                    if (defeatedAfterThisKill <= 0)
                        return;

                    // Dedupe in case the hook fires twice for the same event/session edge case.
                    if (defeatedAfterThisKill == _lastQueuedDragonKillCount)
                        return;

                    _lastQueuedDragonKillCount = defeatedAfterThisKill;
                    _pendingDragonKillCount    = defeatedAfterThisKill;
                    _pendingDragonKillAnnounce = true;
                }
            }
            #endregion

            #region Helpers

            /// <summary>
            /// Derives the current dragon defeated count from the active Dragon Endurance dragon HP.
            /// </summary>
            private static int DE_DragonsDefeated()
            {
                DragonClientEntity dragon = _dragonClientRef(EnemyManager.Instance);
                if (dragon == null)
                    return 0;

                int existingDragonHP = (int)dragon.Health;

                if (existingDragonHP == 25)
                    return 0;

                return (existingDragonHP - 25) / 85;
            }
            #endregion
        }
        #endregion

        #region Chaotic Aim

        #region Runtime Helper

        /// <summary>
        /// Shared runtime helper for Chaotic Aim.
        ///
        /// Purpose:
        /// - Builds a random world-orientation matrix while preserving the original translation.
        /// - Used by projectile send patches that want to randomize outgoing projectile aim.
        ///
        /// Notes:
        /// - This intentionally affects the final fire direction only.
        /// - The actual usage now lives in the projectile send patches:
        ///   - GunshotMessage.Send(...)
        ///   - ShotgunShotMessage.Send(...)   // once/if you merge shotgun too
        ///   - FireRocketMessage.Send(...)
        ///   - GrenadeMessage.Send(...)
        /// - Standalone Chaotic Aim Harmony patches should be removed once their logic is merged
        ///   into the corresponding projectile send patch.
        /// </summary>
        internal static class ChaoticAimRuntime
        {
            private static readonly object _rngLock = new object();
            private static readonly Random _rng = new Random();

            /// <summary>
            /// Creates a new aim matrix using the original translation and a fully random forward vector.
            /// </summary>
            public static Matrix MakeRandomAimMatrix(Matrix original)
            {
                Vector3 dir = RandomUnitVector();

                // Pick a stable "up" that is not parallel to dir so Matrix.CreateWorld remains valid.
                Vector3 up = Math.Abs(Vector3.Dot(dir, Vector3.Up)) > 0.98f
                    ? Vector3.Right
                    : Vector3.Up;

                return Matrix.CreateWorld(original.Translation, dir, up);
            }

            /// <summary>
            /// Uniform random direction on the unit sphere.
            /// </summary>
            public static Vector3 RandomUnitVector()
            {
                lock (_rngLock)
                {
                    float z = (float)(_rng.NextDouble() * 2d - 1d);
                    float azimuth = (float)(_rng.NextDouble() * Math.PI * 2d);
                    float planar = (float)Math.Sqrt(Math.Max(0d, 1d - (z * z)));

                    return Vector3.Normalize(new Vector3(
                        planar * (float)Math.Cos(azimuth),
                        z,
                        planar * (float)Math.Sin(azimuth)));
                }
            }
        }
        #endregion

        #endregion

        #region Item Vortex (Disable Item Stacking)

        /// <summary>
        /// Replaces PickupManager.HandleConsumePickupMessage(...) with the original logic,
        /// except the local-player inventory grant + pickup sound block is removed.
        ///
        /// Removed block:
        /// if (player == CastleMinerZGame.Instance.LocalPlayer)
        /// {
        ///     CastleMinerZGame.Instance.GameScreen.HUD.PlayerInventory.AddInventoryItem(msg.Item, msg.DisplayOnPickup);
        ///     SoundManager.Instance.PlayInstance("pickupitem");
        /// }
        ///
        /// Notes:
        /// - Pickup removal still happens.
        /// - FlyingPickupEntity still spawns.
        /// - Local inventory is NOT granted here.
        /// - Pickup sound is NOT played here.
        /// </summary>
        [HarmonyPatch(typeof(PickupManager), "HandleConsumePickupMessage")]
        internal static class Patch_PickupManager_HandleConsumePickupMessage_RemoveLocalAdd
        {
            private static bool Prefix(PickupManager __instance, ConsumePickupMessage msg)
            {
                if (!CastleWallsMk2._itemVortexTargetsUs)                    return true;
                if (msg.Item.ItemClass.ID != CastleWallsMk2._itemVortexType) return true;

                Vector3      position;
                PickupEntity pe     = null;
                Player       player = null;

                if (CastleMinerZGame.Instance.CurrentNetworkSession != null)
                {
                    for (int i = 0; i < CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers.Count; i++)
                    {
                        NetworkGamer nwg = CastleMinerZGame.Instance.CurrentNetworkSession.AllGamers[i];
                        if (nwg != null && nwg.Id == msg.PickerUpper)
                        {
                            Player p = (Player)nwg.Tag;
                            if (p != null)
                            {
                                player = p;
                            }
                        }
                    }
                }

                for (int j = 0; j < __instance.Pickups.Count; j++)
                {
                    if (__instance.Pickups[j].PickupID == msg.PickupID &&
                        __instance.Pickups[j].SpawnerID == msg.SpawnerID)
                    {
                        pe = __instance.Pickups[j];
                        __instance.RemovePickup(pe);
                        break;
                    }
                }

                if (pe != null)
                {
                    position = pe.GetActualGraphicPos();
                }
                else
                {
                    position = msg.PickupPosition;
                }

                if (player != null)
                {
                    // Intentionally removed:
                    // if (player == CastleMinerZGame.Instance.LocalPlayer)
                    // {
                    //     CastleMinerZGame.Instance.GameScreen.HUD.PlayerInventory.AddInventoryItem(msg.Item, msg.DisplayOnPickup);
                    //     SoundManager.Instance.PlayInstance("pickupitem");
                    // }

                    FlyingPickupEntity fpe = new FlyingPickupEntity(msg.Item, player, position);
                    Scene scene = __instance.Scene;
                    if (scene != null && scene.Children != null)
                    {
                        scene.Children.Add(fpe);
                    }
                }

                // Skip original method.
                return false;
            }
        }
        #endregion

        #region Disable Item Pickups

        [HarmonyPatch]
        internal static class Patch_BlockAllRemoteCreatePickup
        {
            private static MethodBase TargetMethod()
                => AccessTools.Method(typeof(PickupManager), "HandleConsumePickupMessage");

            [HarmonyPrefix]
            private static bool Prefix(ConsumePickupMessage msg)
            {
                if (!CastleWallsMk2._disableItemPickupEnabled) return true;

                var game = CastleMinerZGame.Instance;
                if (game?.MyNetworkGamer == null || msg?.Sender == null)
                    return true;

                if (msg.PickerUpper != (game?.MyNetworkGamer?.Id ?? 0))
                    return true;

                return false;
            }
        }
        #endregion

        #region Ghost Mode

        /// <summary>
        /// GHOST MODE: SELF-ONLY STARTGAME PLAYEREXISTS
        /// Replaces the normal StartGame() PlayerExists broadcast with a runtime-gated helper.
        ///
        /// Behavior:
        /// - When ghost mode is disabled, preserves vanilla behavior.
        /// - When ghost mode is enabled, sends PlayerExists only back to the local gamer.
        ///
        /// Rationale:
        /// - Prevents the normal public player announce during initial join.
        /// - Allows the local echo/setup path to still occur for the ghosted client.
        /// </summary>
        #region Ghost Mode - StartGame Self PlayerExists

        [HarmonyPatch(typeof(CastleMinerZGame), "StartGame")]
        internal static class Patch_CastleMinerZGame_StartGame_PlayerExistsPrivate
        {
            /// <summary>
            /// Cached MethodInfo for the original PlayerExistsMessage.Send(...) call.
            /// </summary>
            private static readonly MethodInfo _miOriginalSend =
                AccessTools.Method(
                    typeof(PlayerExistsMessage),
                    nameof(PlayerExistsMessage.Send),
                    new[] { typeof(LocalNetworkGamer), typeof(AvatarDescription), typeof(bool) });

            /// <summary>
            /// Cached MethodInfo for the runtime-gated replacement helper.
            /// </summary>
            private static readonly MethodInfo _miReplacementSend =
                AccessTools.Method(
                    typeof(Patch_CastleMinerZGame_StartGame_PlayerExistsPrivate),
                    nameof(MaybeSendPlayerExistsStart));

            /// <summary>
            /// Sends PlayerExists to self only while ghost mode is enabled.
            /// Otherwise falls back to the normal vanilla send path.
            /// </summary>
            private static void MaybeSendPlayerExistsStart(
                LocalNetworkGamer gamer,
                AvatarDescription description,
                bool requestResponse)
            {
                if (CastleWallsMk2._ghostModeEnabled)
                {
                    var msg = MessageBridge.Get<PlayerExistsMessage>();

                    msg.AvatarDescriptionData = description.Description;
                    msg.RequestResponse = requestResponse;
                    msg.Gamer.Gamertag = gamer.Gamertag;
                    msg.Gamer.PlayerID = gamer.PlayerID;

                    // Self-only send while ghost mode is enabled.
                    MessageBridge.DoSendDirect.Invoke(msg, new object[] { gamer, gamer });
                    return;
                }

                // Normal vanilla behavior when ghost mode is disabled.
                PlayerExistsMessage.Send(gamer, description, requestResponse);
            }

            /// <summary>
            /// Rewrites the StartGame() PlayerExists send call to the runtime-gated helper.
            /// </summary>
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (var code in instructions)
                {
                    if (code.Calls(_miOriginalSend))
                    {
                        yield return new CodeInstruction(OpCodes.Call, _miReplacementSend);
                        continue;
                    }

                    yield return code;
                }
            }
        }
        #endregion

        /// <summary>
        /// GHOST MODE: SUPPRESS PLAYEREXISTS REPLY
        /// Replaces the reply PlayerExists send inside _processPlayerExistsMessage(...)
        /// with a runtime-gated helper.
        ///
        /// Behavior:
        /// - When ghost mode is disabled, preserves vanilla reply behavior.
        /// - When ghost mode is enabled, suppresses the reply entirely.
        ///
        /// Rationale:
        /// - Prevents the ghosted client from announcing itself back to newly discovered players.
        /// - Still allows incoming PlayerExists processing to continue normally.
        /// </summary>
        #region Ghost Mode - Suppress PlayerExists Reply

        [HarmonyPatch(typeof(CastleMinerZGame), "_processPlayerExistsMessage")]
        internal static class Patch_CastleMinerZGame_ProcessPlayerExistsMessage_RemoveSend
        {
            /// <summary>
            /// Cached MethodInfo for the original PlayerExistsMessage.Send(...) call.
            /// </summary>
            private static readonly MethodInfo _miPlayerExistsSend =
                AccessTools.Method(
                    typeof(PlayerExistsMessage),
                    nameof(PlayerExistsMessage.Send),
                    new[]
                    {
                        typeof(LocalNetworkGamer),
                        typeof(AvatarDescription),
                        typeof(bool)
                    });

            /// <summary>
            /// Cached MethodInfo for the runtime-gated reply helper.
            /// </summary>
            private static readonly MethodInfo _miReplacement =
                AccessTools.Method(
                    typeof(Patch_CastleMinerZGame_ProcessPlayerExistsMessage_RemoveSend),
                    nameof(MaybeReplyPlayerExists));

            /// <summary>
            /// Sends the normal PlayerExists reply only when ghost mode is disabled.
            /// Suppresses the reply entirely while ghost mode is enabled.
            /// </summary>
            private static void MaybeReplyPlayerExists(
                LocalNetworkGamer gamer,
                AvatarDescription description,
                bool requestResponse)
            {
                if (CastleWallsMk2._ghostModeEnabled)
                {
                    // Suppress the reply while ghost mode is enabled.
                    return;
                }

                // Vanilla behavior when ghost mode is disabled.
                PlayerExistsMessage.Send(gamer, description, requestResponse);
            }

            /// <summary>
            /// Rewrites the PlayerExists reply call to the runtime-gated helper.
            /// </summary>
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (var code in instructions)
                {
                    if (code.Calls(_miPlayerExistsSend))
                    {
                        yield return new CodeInstruction(OpCodes.Call, _miReplacement)
                        {
                            labels = code.labels,
                            blocks = code.blocks
                        };
                        continue;
                    }

                    yield return code;
                }
            }
        }
        #endregion

        /// <summary>
        /// GHOST MODE: ENSURE REMOTE PLAYERS REMAIN VISIBLE AFTER JOIN
        /// Wraps DNAGame.JoinGame(...) callback so that, after a successful join,
        /// ghost mode can restore the local name and manually ensure remote players
        /// appear on the local client.
        ///
        /// Behavior:
        /// - Preserves the original callback.
        /// - Runs only after successful joins.
        /// - Applies only while ghost mode is enabled.
        /// - Restores the local non-persistent gamertag when needed.
        /// - Attempts to create/add remote Player objects locally for visibility.
        ///
        /// Notes:
        /// - holdingGround is accessed through a private FieldRef for pre-GameScreen cases.
        /// - AvatarDescription(new byte[10]) may not represent valid avatar data in all cases.
        /// </summary>
        #region Ghost Mode - Ensure Remote Players Visible After Join

        [HarmonyPatch(typeof(DNAGame), "JoinGame",
            new[]
            {
                typeof(AvailableNetworkSession),
                typeof(IList<SignedInGamer>),
                typeof(SuccessCallbackWithMessage),
                typeof(string),
                typeof(int),
                typeof(string)
            })]
        internal static class Patch_DNAGame_JoinGame_EnsureAllRemotePlayersVisible
        {
            /// <summary>
            /// Private FieldRef accessor for CastleMinerZGame.holdingGround.
            /// Used when GameScreen is not yet available.
            /// </summary>
            private static readonly AccessTools.FieldRef<CastleMinerZGame, Entity> _holdingGroundRef =
                AccessTools.FieldRefAccess<CastleMinerZGame, Entity>("holdingGround");

            /// <summary>
            /// Wraps the original join callback so ghost-mode post-join visibility logic
            /// can run after a successful join completes.
            /// </summary>
            private static void Prefix(ref SuccessCallbackWithMessage callback)
            {
                SuccessCallbackWithMessage originalCallback = callback;

                callback = (success, failureMessage) =>
                {
                    try
                    {
                        originalCallback?.Invoke(success, failureMessage);
                    }
                    catch
                    {
                        // Keep original behavior isolated.
                    }

                    try
                    {
                        if (!success)
                            return;

                        if (!CastleWallsMk2._ghostModeEnabled)
                            return;

                        if (CastleMinerZGame.Instance.MyNetworkGamer.Gamertag != CastleWallsMk2._ghostModeNameBackup)
                        {
                            // Restore name (non-persistent).
                            string name = CastleWallsMk2._ghostModeNameBackup;
                            try { CastleMinerZGame.Instance.MyNetworkGamer.Gamertag = name; } catch (Exception) { }
                        }

                        EnsureAllRemotePlayersVisible();
                    }
                    catch
                    {
                    }
                };
            }

            /// <summary>
            /// Iterates all network gamers in the current session and ensures remote
            /// players are represented locally when possible.
            /// </summary>
            private static void EnsureAllRemotePlayersVisible()
            {
                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game?.CurrentNetworkSession == null)
                        return;

                    foreach (NetworkGamer networkGamer in game.CurrentNetworkSession.AllGamers)
                        EnsureRemotePlayerVisible(networkGamer);
                }
                catch
                {
                }
            }

            /// <summary>
            /// Creates and locally adds a Player object for a remote gamer if one does
            /// not already exist on this client.
            /// </summary>
            private static void EnsureRemotePlayerVisible(NetworkGamer gamer)
            {
                try
                {
                    var game = CastleMinerZGame.Instance;
                    if (game == null || gamer == null)
                        return;

                    // Skip self if desired.
                    if (ReferenceEquals(gamer, game.MyNetworkGamer))
                        return;

                    // Already has a player object.
                    if (gamer.Tag is Player)
                        return;

                    // WARNING:
                    // new byte[10] may not be a valid AvatarDescription blob.
                    // This may silently fail if your catch hides the exception.
                    var netPlayer = new Player(gamer, new AvatarDescription(new byte[10]));
                    gamer.Tag = netPlayer;

                    if (game.GameScreen == null)
                    {
                        Entity holdingGround = _holdingGroundRef(game);

                        lock (holdingGround)
                        {
                            holdingGround.Children.Add(netPlayer);
                        }

                        return;
                    }

                    game.GameScreen.AddPlayer(netPlayer);
                }
                catch
                {
                }
            }
        }
        #endregion

        /// <summary>
        /// GHOST MODE: BLOCK INVENTORY STORE SEND
        /// Prevents DNA.CastleMinerZ.Net.InventoryStoreOnServerMessage.Send(...)
        /// from sending the client's inventory-save packet to the host while
        /// ghost mode is enabled.
        ///
        /// Behavior:
        /// - When ghost mode is disabled, vanilla behavior is preserved.
        /// - When ghost mode is enabled, the outbound inventory-store send is suppressed.
        ///
        /// Notes:
        /// - InventoryStoreOnServerMessage is internal, so this patch resolves the
        ///   type and method by name at runtime instead of using typeof(...).
        /// - This blocks only the normal static Send(...) path.
        /// </summary>
        #region Ghost Mode - Block Inventory Store Send

        [HarmonyPatch]
        internal static class Patch_InventoryStoreOnServerMessage_Send_BlockWhenGhosted
        {
            /// <summary>
            /// Resolves the internal static
            /// InventoryStoreOnServerMessage.Send(LocalNetworkGamer, PlayerInventory, bool)
            /// method by reflection so Harmony can patch it.
            /// </summary>
            private static MethodBase TargetMethod()
            {
                var messageType = AccessTools.TypeByName("DNA.CastleMinerZ.Net.InventoryStoreOnServerMessage");
                if (messageType == null)
                    return null;

                return AccessTools.Method(
                    messageType,
                    "Send",
                    new[]
                    {
                        typeof(LocalNetworkGamer),
                        typeof(PlayerInventory),
                        typeof(bool)
                    });
            }

            /// <summary>
            /// Suppresses the outbound inventory-store send while ghost mode is active.
            /// </summary>
            private static bool Prefix(
                LocalNetworkGamer from,
                PlayerInventory playerInventory,
                bool final)
            {
                if (CastleWallsMk2._ghostModeEnabled)
                    return false;

                return true;
            }
        }
        #endregion

        /// <summary>
        /// GHOST MODE: TAB PLAYER LIST INDICATOR
        /// Rewrites the Tab player-list name lookup so the local ghosted player can show
        /// a visual marker in the roster without changing the real network gamertag.
        ///
        /// Behavior:
        /// - Only affects InGameHUD.DrawPlayerList(...).
        /// - Leaves the actual gamer/session identity untouched.
        /// - When ghost mode is disabled, preserves the normal roster name.
        /// - When ghost mode is enabled, appends " [GHOST]" to the local player's
        ///   displayed roster name only.
        /// - When hide-name mode is enabled, prefers the backed-up real name before
        ///   appending the roster indicator.
        ///
        /// Notes:
        /// - The Gamertag property used by DrawPlayerList resolves from the base
        ///   DNA.Net.GamerServices.Gamer type, not NetworkGamer directly.
        /// - This patch rewrites only the in-method getter call and does not modify
        ///   any saved or networked player identity.
        /// </summary>
        #region Ghost Mode - Tab Player List Indicator

        [HarmonyPatch]
        internal static class InGameHUD_DrawPlayerList_GhostIndicator
        {
            /// <summary>
            /// Resolves the protected InGameHUD.DrawPlayerList(...) method explicitly
            /// so Harmony can patch the correct roster draw path.
            /// </summary>
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(InGameHUD),
                    "DrawPlayerList",
                    new[] { typeof(GraphicsDevice), typeof(SpriteBatch), typeof(GameTime) });
            }

            /// <summary>
            /// Validates that the target method was found before patching.
            /// </summary>
            private static bool Prepare()
            {
                MethodBase target = TargetMethod();
                return target != null;
            }

            /// <summary>
            /// Rewrites the DrawPlayerList(...) Gamertag getter call to a runtime-gated
            /// helper that returns a roster-only display name for the local player.
            /// </summary>
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo getGamertag = AccessTools.PropertyGetter(
                    typeof(DNA.Net.GamerServices.Gamer),
                    "Gamertag");

                MethodInfo helper = AccessTools.Method(
                    typeof(InGameHUD_DrawPlayerList_GhostIndicator),
                    nameof(GetRosterDisplayName));

                var codes = new List<CodeInstruction>(instructions);

                for (int i = 0; i < codes.Count; i++)
                {
                    CodeInstruction ci = codes[i];

                    if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) &&
                        Equals(ci.operand, getGamertag))
                    {
                        ci.opcode = OpCodes.Call;
                        ci.operand = helper;
                    }
                }

                return codes;
            }

            /// <summary>
            /// Returns the roster-only display name for the supplied gamer.
            ///
            /// Behavior:
            /// - Non-network gamers are returned unchanged.
            /// - Remote/networked non-local players are returned unchanged.
            /// - The local player gains a " [GHOST]" suffix while ghost mode is enabled.
            /// - When hide-name mode is enabled, the backed-up original name is used
            ///   before adding the roster indicator.
            /// </summary>
            private static string GetRosterDisplayName(Gamer gamer)
            {
                string name = gamer?.Gamertag ?? string.Empty;

                try
                {
                    if (!(gamer is NetworkGamer ng))
                        return name;

                    // Only decorate the local player's roster entry.
                    if (!ng.IsLocal)
                        return name;

                    // Preserve vanilla roster text when ghost mode is disabled.
                    if (!CastleWallsMk2._ghostModeEnabled)
                        return name;

                    // If hide-name mode spoofed the visible name elsewhere, prefer the
                    // backed-up clean local name for the roster display.
                    if (CastleWallsMk2._ghostModeHideNameEnabled &&
                        !string.IsNullOrWhiteSpace(CastleWallsMk2._ghostModeNameBackup))
                    {
                        name = CastleWallsMk2._ghostModeNameBackup;
                    }

                    return name + " [GHOST]";
                }
                catch
                {
                    // Fail safe: Never break the player list over a cosmetic label.
                    return name;
                }
            }
        }
        #endregion

        #endregion

        #region Rapid Place

        [HarmonyPatch(typeof(BlockInventoryItem), nameof(BlockInventoryItem.ProcessInput))]
        internal static class Patch_BlockInventoryItem_ProcessInput_RapidPlace
        {
            private static readonly AccessTools.FieldRef<InventoryItem, OneShotTimer> _coolDownTimerRef =
                AccessTools.FieldRefAccess<InventoryItem, OneShotTimer>("_coolDownTimer");

            static bool Prefix(BlockInventoryItem __instance, InGameHUD hud, CastleMinerZControllerMapping controller)
            {
                // Only override when the mod is enabled.
                if (!CastleWallsMk2._rapidPlaceEnabled)
                    return true;

                // Keep vanilla behavior for null safety / bad state.
                if (__instance == null || hud == null || controller == null)
                    return true;

                var timer = _coolDownTimerRef(__instance);
                if (timer == null)
                    return true;

                // Shrink the cooldown so Held placement can happen much faster.
                timer.MaxTime = CastleWallsMk2._rapidPlaceEnabled
                    ? TimeSpan.FromMilliseconds(1)
                    : __instance.ItemClass.CoolDownTime;

                // Changed from controller.Use.Pressed to controller.Use.Held.
                if (timer.Expired && controller.Use.Held && __instance.StackCount > 0)
                {
                    timer.Reset();

                    var location = hud.Build(__instance, true);
                    if (location != IntVector3.Zero)
                    {
                        bool buildNow = true;

                        if (BlockType.IsDoor(__instance.BlockTypeID))
                        {
                            CastleMinerZGame.Instance.CurrentWorld.SetDoor(
                                location,
                                DoorEntity.GetModelNameFromInventoryId(__instance.ItemClass.ID));
                        }

                        if (__instance.BlockTypeID == BlockTypeEnum.SpawnPointBasic)
                        {
                            __instance.PlaceLocator(location);
                            CastleMinerZGame.Instance.LocalPlayer.PlayerInventory.InventorySpawnPointTeleport = __instance;
                            hud.PlayerInventory.Consume(__instance, 1, false);
                        }
                        else if (__instance.BlockTypeID == BlockTypeEnum.TeleportStation)
                        {
                            // Let vanilla handle teleport naming flow normally.
                            return true;
                        }
                        else
                        {
                            hud.PlayerInventory.Consume(__instance, 1, false);
                        }

                        if (buildNow)
                            hud.Build(__instance, false);
                    }

                    hud.LocalPlayer.UsingTool = true;

                    var itemStats = CastleMinerZGame.Instance.PlayerStats.GetItemStats(__instance.ItemClass.ID);
                    itemStats.Used++;
                    return false;
                }

                hud.LocalPlayer.UsingTool = false;
                return false;
            }
        }
        #endregion

        #region Sudo Player

        [HarmonyPatch(typeof(BroadcastTextMessage), nameof(BroadcastTextMessage.Send))]
        internal static class Patch_BroadcastTextMessage_Send_SudoName
        {
            /// <summary>
            /// Rewrites only normal local chat lines from "MyName: body" to
            /// "SpoofedName: Body" when Sudo Player is enabled.
            /// </summary>
            static void Prefix(LocalNetworkGamer from, ref string message)
            {
                if (from == null || string.IsNullOrWhiteSpace(message))
                    return;

                if (!CastleWallsMk2._sudoPlayerEnabled)
                    return;

                string myName =
                    from.SignedInGamer?.Gamertag ??
                    from.Gamertag;

                if (string.IsNullOrWhiteSpace(myName))
                    return;

                // Only intercept normal chat lines that vanilla builds as:
                // "MyGamertag: hello"
                string myPrefix = myName + ": ";
                if (!message.StartsWith(myPrefix, StringComparison.Ordinal))
                    return;

                // Do not touch empty-body messages.
                string body = message.Substring(myPrefix.Length);
                if (string.IsNullOrWhiteSpace(body))
                    return;

                string spoofName = ResolveSudoOutgoingDisplayName();
                if (string.IsNullOrWhiteSpace(spoofName))
                    return;

                message = spoofName + ": " + body;
            }
        }

        #region Chat Sudo Helpers

        /// <summary>
        /// Cleans a display name so it is safe to embed into "Name: Message" chat lines.
        /// </summary>
        private static string SanitizeChatDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            name = name.Trim();

            // Prevent malformed "Name: Message" formatting.
            name = name.Replace("\r", "")
                       .Replace("\n", "")
                       .Replace(":", "");

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>
        /// Resolves the outgoing sudo display name.
        /// Priority:
        /// 1) Custom override textbox.
        /// 2) Selected player's Gamertag.
        /// 3) null = Do not spoof.
        /// </summary>
        private static string ResolveSudoOutgoingDisplayName()
        {
            if (!CastleWallsMk2._sudoPlayerEnabled)
                return null;

            // Highest priority: Manual override text.
            string custom = SanitizeChatDisplayName(CastleWallsMk2._sudoNameMessage);
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            // Fallback: Selected player.
            if (CastleWallsMk2._sudoPlayerTargetMode != CastleWallsMk2.PlayerTargetMode.Player || CastleWallsMk2._sudoPlayerTargetNetid == 0)
                return null;

            var session = CastleMinerZGame.Instance?.CurrentNetworkSession;
            if (session == null)
                return null;

            for (int i = 0; i < session.AllGamers.Count; i++)
            {
                var g = session.AllGamers[i];
                if (g == null)
                    continue;

                if (g.Id == CastleWallsMk2._sudoPlayerTargetNetid)
                    return SanitizeChatDisplayName(g.Gamertag);
            }

            return null;
        }
        #endregion

        #endregion

        #region All Guns Harvest

        /// <summary>
        /// Makes all laser guns report as harvest weapons when the config toggle is enabled.
        /// </summary>
        [HarmonyPatch(typeof(LaserGunInventoryItemClass), "IsHarvestWeapon")]
        internal static class Patch_LaserGunInventoryItemClass_IsHarvestWeapon_AllHarvest
        {
            static bool Prefix(ref bool __result)
            {
                // Feature disabled:
                // Let vanilla decide normally.
                if (!CastleWallsMk2._allGunsHarvestEnabled)
                    return true;

                // Feature enabled:
                // Force all laser guns to behave as harvest weapons.
                __result = true;
                return false;
            }
        }
        #endregion

        #region PvP Thorns

        /// <summary>
        /// Injects PvP thorns reflection after BlasterShot applies local damage.
        /// </summary>
        [HarmonyPatch(typeof(BlasterShot), "OnUpdate")]
        internal static class Patch_BlasterShot_OnUpdate_PvPThorns
        {
            private static readonly MethodInfo _afterDamage =
                AccessTools.Method(typeof(ThornsCompat), nameof(ThornsCompat.AfterBlasterLocalDamage));

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ThornsTranspilerUtil.InjectAfterApplyDamage(instructions, _afterDamage);
            }
        }

        /// <summary>
        /// Injects PvP thorns reflection after TracerManager.Tracer applies local damage.
        /// </summary>
        [HarmonyPatch(typeof(TracerManager.Tracer), "Update")]
        internal static class Patch_TracerManager_Tracer_Update_PvPThorns
        {
            private static readonly MethodInfo _afterDamage =
                AccessTools.Method(typeof(ThornsCompat), nameof(ThornsCompat.AfterTracerLocalDamage));

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ThornsTranspilerUtil.InjectAfterApplyDamage(instructions, _afterDamage);
            }
        }

        /// <summary>
        /// Reflects melee damage back to the sender after the melee message is received.
        /// </summary>
        [HarmonyPatch(typeof(MeleePlayerMessage), "RecieveData", new[] { typeof(BinaryReader) })]
        internal static class Patch_MeleePlayerMessage_RecieveData_PvPThorns
        {
            static void Postfix(MeleePlayerMessage __instance)
            {
                ThornsCompat.AfterMeleeLocalDamage(__instance);
            }
        }

        #region Thorns Helper

        internal static class ThornsTranspilerUtil
        {
            private static readonly MethodInfo _applyDamage =
                AccessTools.Method(
                    typeof(InGameHUD),
                    nameof(InGameHUD.ApplyDamage),
                    new[] { typeof(float), typeof(Vector3) });

            internal static IEnumerable<CodeInstruction> InjectAfterApplyDamage(
                IEnumerable<CodeInstruction> instructions,
                MethodInfo helperMethod)
            {
                foreach (var code in instructions)
                {
                    yield return code;

                    if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) &&
                        code.operand is MethodInfo mi &&
                        mi == _applyDamage)
                    {
                        // Load the target method's "this" and call our helper.
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, helperMethod);
                    }
                }
            }
        }

        /// <summary>
        /// Shared helper logic for PvP thorns reflection.
        /// </summary>
        internal static class ThornsCompat
        {
            [ThreadStatic]
            private static bool _reflecting;

            private static readonly FieldInfo _messageSenderField =
                AccessTools.Field(typeof(Message), "_sender");

            private static readonly FieldInfo _blasterShooterField =
                AccessTools.Field(typeof(BlasterShot), "_shooter");

            private static readonly FieldInfo _blasterEnemyIdField =
                AccessTools.Field(typeof(BlasterShot), "_enemyID");

            private static readonly FieldInfo _tracerShooterField =
                AccessTools.Field(typeof(TracerManager.Tracer), "ShooterID") ??
                AccessTools.Field(typeof(TracerManager.Tracer), "_shooterID");

            private static readonly PropertyInfo _tracerShooterProperty =
                AccessTools.Property(typeof(TracerManager.Tracer), "ShooterID");

            /// <summary>
            /// Called immediately after BlasterShot damages the local player.
            /// Reflects only player-vs-player hits, not enemy laser hits.
            /// </summary>
            public static void AfterBlasterLocalDamage(BlasterShot shot)
            {
                if (!CanReflect())
                    return;

                if (shot == null)
                    return;

                try
                {
                    // Ignore enemy-caused damage.
                    if (_blasterEnemyIdField != null)
                    {
                        int enemyId = Convert.ToInt32(_blasterEnemyIdField.GetValue(shot));
                        if (enemyId > 0)
                            return;
                    }

                    if (_blasterShooterField == null)
                        return;

                    int shooterId = Convert.ToInt32(_blasterShooterField.GetValue(shot));
                    ReflectByShooterId(shooterId);
                }
                catch { }
            }

            /// <summary>
            /// Called immediately after TracerManager.Tracer damages the local player.
            /// Reflects if the shooter is a remote gamer in the current session.
            /// </summary>
            public static void AfterTracerLocalDamage(TracerManager.Tracer tracer)
            {
                if (!CanReflect())
                    return;

                if (tracer == null)
                    return;

                try
                {
                    int shooterId;

                    if (_tracerShooterField != null)
                        shooterId = Convert.ToInt32(_tracerShooterField.GetValue(tracer));
                    else if (_tracerShooterProperty != null)
                        shooterId = Convert.ToInt32(_tracerShooterProperty.GetValue(tracer, null));
                    else
                        return;

                    ReflectByShooterId(shooterId);
                }
                catch { }
            }

            /// <summary>
            /// Called after a melee player message is received locally.
            /// Uses the message sender as the attacker.
            /// </summary>
            public static void AfterMeleeLocalDamage(MeleePlayerMessage msg)
            {
                if (!CanReflect())
                    return;

                if (msg == null || _messageSenderField == null)
                    return;

                try
                {
                    var sender = _messageSenderField.GetValue(msg) as NetworkGamer;
                    ReflectToAttacker(sender);
                }
                catch { }
            }

            private static bool CanReflect()
            {
                var game = CastleMinerZGame.Instance;

                if (!CastleWallsMk2._pvpThornsEnabled)
                    return false;

                if (_reflecting)
                    return false;

                if (game == null || game.CurrentNetworkSession == null)
                    return false;

                // Only allow thorns when PvP is enabled.
                // if (game.PVPState == CastleMinerZGame.PVPEnum.Off) return false;

                return true;
            }

            private static void ReflectByShooterId(int shooterId)
            {
                var game = CastleMinerZGame.Instance;
                if (game == null || game.CurrentNetworkSession == null)
                    return;

                foreach (NetworkGamer gamer in game.CurrentNetworkSession.AllGamers)
                {
                    if (gamer != null && gamer.Id == shooterId)
                    {
                        ReflectToAttacker(gamer);
                        return;
                    }
                }
            }

            private static void ReflectToAttacker(NetworkGamer attacker)
            {
                var game = CastleMinerZGame.Instance;

                if (game == null || attacker == null)
                    return;

                if (attacker == game.MyNetworkGamer)
                    return;

                var targetPlayer = attacker.Tag as Player;
                if (targetPlayer?.Gamer == null)
                    return;

                int reflectCount = 1; // CastleWallsMk2._pvpThornsInstakillEnabled ? 30 : 1;

                try
                {
                    _reflecting = true;

                    for (int i = 0; i < reflectCount; i++)
                        SendFireballDamagePrivate(game.MyNetworkGamer, targetPlayer.Gamer, DragonTypeEnum.SKELETON);
                }
                catch { }
                finally
                {
                    _reflecting = false;
                }
            }

            /// <summary>
            /// Sends private reflected fireball damage to a specific gamer.
            /// </summary>
            public static void SendFireballDamagePrivate(
                LocalNetworkGamer from,
                NetworkGamer      to,
                DragonTypeEnum    dragonType = DragonTypeEnum.SKELETON)
            {
                if (from == null || to == null)
                    return;

                var sendInstance            = MessageBridge.Get<DetonateFireballMessage>();
                sendInstance.Location       = ((Player)to.Tag)?.LocalPosition ?? Vector3.Zero;
                sendInstance.Index          = -1;
                sendInstance.NumBlocks      = 0;
                sendInstance.BlocksToRemove = new IntVector3[] { };
                sendInstance.EType          = dragonType;

                MessageBridge.DoSendDirect.Invoke(sendInstance, new object[] { from, to });
            }
        }
        #endregion

        #endregion

        #region Trial Mode Reimplementation

        /// <summary>
        /// Shared helper logic for re-implemented trial mode behavior.
        /// </summary>
        internal static class TrialModeCompat
        {
            private static bool _endingFromTrial;

            /// <summary>
            /// Syncs the game's trial flag with the mod's toggle.
            /// </summary>
            public static void SyncGuideFlag()
            {
                Guide.IsTrialMode = CastleWallsMk2._trialModeEnabled;
            }

            /// <summary>
            /// Returns true when the selected game mode should be blocked in trial mode.
            ///
            /// Current policy:
            /// - Allowed: Endurance, Survival
            /// - Blocked: DragonEndurance, Creative, Exploration, Scavenger
            ///
            /// Adjust this list to make trial mode stricter or looser.
            /// </summary>
            public static bool IsLockedGameMode(GameModeTypes mode)
            {
                switch (mode)
                {
                    case GameModeTypes.DragonEndurance:
                    case GameModeTypes.Creative:
                    case GameModeTypes.Exploration:
                    case GameModeTypes.Scavenger:
                        return true;

                    default:
                        return false;
                }
            }

            /// <summary>
            /// Updates the trial timer only while actually in-game.
            /// Resets it while in menus.
            /// </summary>
            public static void UpdateTrialTimer(CastleMinerZGame game, GameTime gameTime)
            {
                if (game == null)
                    return;

                SyncGuideFlag();

                if (!CastleWallsMk2._trialModeEnabled)
                {
                    game.TrialModeTimer.Reset();
                    return;
                }

                // Only count down while actually in-game.
                if (game.GameScreen == null)
                {
                    game.TrialModeTimer.Reset();
                    return;
                }

                game.TrialModeTimer.Update(gameTime.ElapsedGameTime);

                if (game.PlayerStats != null)
                    game.PlayerStats.TimeInTrial += gameTime.ElapsedGameTime;

                if (_endingFromTrial || !game.TrialModeTimer.Expired)
                    return;

                try
                {
                    _endingFromTrial = true;
                    game.TrialModeTimer.Reset();

                    game.EndGame(true);

                    game.FrontEnd?.ShowUIDialog(
                            CMZStrings.Get("Trial_Mode"),
                            CMZStrings.Get("You_must_purchase_the_game_to_travel_further"),
                            false);
                }
                catch { }
                finally
                {
                    _endingFromTrial = false;
                }
            }

            /// <summary>
            /// Resets the trial timer when a new game starts.
            /// </summary>
            public static void ResetTrialTimer(CastleMinerZGame game)
            {
                if (game == null)
                    return;

                SyncGuideFlag();

                if (CastleWallsMk2._trialModeEnabled)
                    game.TrialModeTimer.Reset();
            }

            /// <summary>
            /// Shows the "trial mode / online locked" message.
            /// </summary>
            public static void ShowOnlineLockedDialog(FrontEndScreen frontEnd)
            {
                if (frontEnd == null)
                    return;

                frontEnd.ShowUIDialog(
                    CMZStrings.Get("Trial_Mode"),
                    CMZStrings.Get("You_must_purchase_the_game_before_you_can_play_online_"),
                    true);
            }

            /// <summary>
            /// Shows the "trial mode / game mode locked" message.
            /// </summary>
            public static void ShowModeLockedDialog(FrontEndScreen frontEnd)
            {
                if (frontEnd == null)
                    return;

                frontEnd.ShowUIDialog(
                    CMZStrings.Get("Trial_Mode"),
                    CMZStrings.Get("You_must_purchase_the_game_before_you_can_play_in_this_game_mode_"),
                    true);
            }
        }

        /// <summary>
        /// Keeps Guide.IsTrialMode synced and enforces the 8-minute gameplay timer.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "Update")]
        internal static class Patch_CastleMinerZGame_Update_TrialMode
        {
            static void Postfix(CastleMinerZGame __instance, GameTime gameTime)
            {
                TrialModeCompat.UpdateTrialTimer(__instance, gameTime);
            }
        }

        /// <summary>
        /// Resets the trial timer whenever a game starts.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "StartGame")]
        internal static class Patch_CastleMinerZGame_StartGame_TrialMode
        {
            static void Postfix(CastleMinerZGame __instance)
            {
                TrialModeCompat.ResetTrialTimer(__instance);
            }
        }

        /// <summary>
        /// Re-implements the original invite behavior:
        /// invited games open marketplace instead of joining while in trial mode.
        /// </summary>
        [HarmonyPatch(typeof(CastleMinerZGame), "NetworkSession_InviteAccepted")]
        internal static class Patch_CastleMinerZGame_InviteAccepted_TrialMode
        {
            static bool Prefix(CastleMinerZGame __instance, object sender, InviteAcceptedEventArgs e)
            {
                TrialModeCompat.SyncGuideFlag();

                if (!CastleWallsMk2._trialModeEnabled)
                    return true;

                try
                {
                    __instance.ShowMarketPlace(e.Gamer.PlayerIndex);
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Blocks Join Online immediately from the main menu while in trial mode.
        /// Play Offline remains allowed.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "_mainMenu_MenuItemSelected")]
        internal static class Patch_FrontEndScreen_MainMenu_TrialMode
        {
            static bool Prefix(FrontEndScreen __instance, SelectedMenuItemArgs e)
            {
                TrialModeCompat.SyncGuideFlag();

                if (!CastleWallsMk2._trialModeEnabled)
                    return true;

                if (e?.MenuItem?.Tag == null)
                    return true;

                MainMenuItems item = (MainMenuItems)e.MenuItem.Tag;

                switch (item)
                {
                    case MainMenuItems.HostOnline:
                    case MainMenuItems.JoinOnline:
                        TrialModeCompat.ShowOnlineLockedDialog(__instance);
                        return false;

                    default:
                        return true;
                }
            }
        }

        /// <summary>
        /// Blocks online hosting as a second safety net.
        /// This catches paths that bypass the main menu selection handler.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "HostGame")]
        internal static class Patch_FrontEndScreen_HostGame_TrialMode
        {
            static bool Prefix(FrontEndScreen __instance, bool local)
            {
                TrialModeCompat.SyncGuideFlag();

                if (!CastleWallsMk2._trialModeEnabled)
                    return true;

                // Local/offline hosting is allowed in this reimplementation.
                if (local)
                    return true;

                TrialModeCompat.ShowOnlineLockedDialog(__instance);
                return false;
            }
        }

        /// <summary>
        /// Blocks joining online as a safety net for paths that bypass the front-end menu.
        /// Calls the callback so the caller does not get stuck waiting.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "JoinGame",
            new[] { typeof(AvailableNetworkSession), typeof(IList<SignedInGamer>), typeof(SuccessCallbackWithMessage), typeof(string), typeof(int), typeof(string) })]
        internal static class Patch_DNAGame_JoinGame_WithMessage_TrialMode
        {
            static bool Prefix(SuccessCallbackWithMessage callback)
            {
                if (!CastleWallsMk2._trialModeEnabled)
                    return true;

                Guide.IsTrialMode = true;

                try
                {
                    callback?.Invoke(false, CMZStrings.Get("You_must_purchase_the_game_before_you_can_play_online_"));
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Blocks joining online for the callback-only overload as well.
        /// </summary>
        [HarmonyPatch(typeof(DNAGame), "JoinGame",
            new[] { typeof(AvailableNetworkSession), typeof(IList<SignedInGamer>), typeof(SuccessCallback), typeof(string), typeof(int), typeof(string) })]
        internal static class Patch_DNAGame_JoinGame_NoMessage_TrialMode
        {
            static bool Prefix(SuccessCallback callback)
            {
                if (!CastleWallsMk2._trialModeEnabled)
                    return true;

                Guide.IsTrialMode = true;

                try
                {
                    callback?.Invoke(false);
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Blocks selected game modes in trial mode.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "_gameModeMenu_MenuItemSelected")]
        internal static class Patch_FrontEndScreen_GameModeMenu_TrialMode
        {
            static bool Prefix(FrontEndScreen __instance, SelectedMenuItemArgs e)
            {
                TrialModeCompat.SyncGuideFlag();

                if (!CastleWallsMk2._trialModeEnabled)
                    return true;

                if (e?.MenuItem?.Tag == null)
                    return true;

                GameModeTypes mode = (GameModeTypes)e.MenuItem.Tag;

                if (!TrialModeCompat.IsLockedGameMode(mode))
                    return true;

                TrialModeCompat.ShowModeLockedDialog(__instance);
                return false;
            }
        }
        #endregion

        #endregion
    }
}