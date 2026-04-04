/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

/*
This mod was created for connecting to dedicated lidgren servers.
Main Project: https://github.com/RussDev7/CMZDedicatedServer.
*/

#pragma warning disable IDE0060              // Silence IDE0060.
using DNA.Net.GamerServices.LidgrenProvider;
using DNA.CastleMinerZ.Utils.Threading;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using DNA.Drawing.UI.Controls;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.UI;
using System.Diagnostics;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Threading;
using DNA.Drawing.UI;
using DNA.Drawing;
using System.Linq;
using HarmonyLib;                            // Harmony patching library.
using DNA.Input;
using System.IO;
using System;
using DNA;

using static ModLoader.LogSystem;            // For Log(...).

namespace DirectConnect
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

        #region Buttons: Bottom-Right - Launch Dedicated / Direct Connect

        /// <summary>
        /// Injects two custom bottom-right buttons into <see cref="ChooseOnlineGameScreen"/>:
        /// - Launch Dedicated
        /// - Direct Connect
        ///
        /// Purpose:
        /// - Allows launching the standalone dedicated host executable directly from the UI.
        /// - Allows connecting to a server by manually entering an address instead of relying on Steam browsing.
        ///
        /// Notes:
        /// - This patch intentionally mirrors the stock button styling and placement behavior.
        /// - Discovery objects are tracked per screen instance so they can be updated and disposed safely.
        /// - The original <see cref="NetworkSession.StaticProvider"/> is preserved and restored when returning to menu.
        /// </summary>
        [HarmonyPatch(typeof(ChooseOnlineGameScreen))]
        public static class ChooseOnlineGameScreen_BottomRightButtonsPatch
        {
            #region UI Fields

            /// <summary>
            /// Custom button used to launch the external dedicated server executable.
            /// </summary>
            private static FrameButtonControl _launchDedicatedButton;

            /// <summary>
            /// Custom button used to open the direct-connect flow.
            /// </summary>
            private static FrameButtonControl _directConnectButton;

            /// <summary>
            /// Tracks the most recently active <see cref="ChooseOnlineGameScreen"/>.
            /// Used by button callbacks that need screen context.
            /// </summary>
            private static ChooseOnlineGameScreen _lastScreen;

            /// <summary>
            /// Base size applied to both custom buttons before DPI / screen scaling.
            /// </summary>
            private static readonly Size BaseButtonSize = new Size(300, 40);

            /// <summary>
            /// Cached reflection handle for the private "_game" field on <see cref="ChooseOnlineGameScreen"/>.
            /// </summary>
            private static readonly FieldInfo _fiGame =
                AccessTools.Field(typeof(ChooseOnlineGameScreen), "_game");

            /// <summary>
            /// Cached reflection handle for the private "_refreshButton" field on <see cref="ChooseOnlineGameScreen"/>.
            /// Used to clone stock styling.
            /// </summary>
            private static readonly FieldInfo _fiRefresh =
                AccessTools.Field(typeof(ChooseOnlineGameScreen), "_refreshButton");

            /// <summary>
            /// Cached reflection handle for the private "_selectButton" field on <see cref="ScrollingListScreen"/>.
            /// Used to clone stock styling.
            /// </summary>
            private static readonly FieldInfo _fiSelect =
                AccessTools.Field(typeof(ScrollingListScreen), "_selectButton");

            /// <summary>
            /// Cached reflection handle for the private FrontEndScreen.JoinGame(AvailableNetworkSession, string) method.
            /// Used to enter the game's normal join pipeline after discovery succeeds.
            /// </summary>
            private static readonly MethodInfo _miJoinGame =
                AccessTools.Method(typeof(FrontEndScreen), "JoinGame",
                    new Type[] { typeof(AvailableNetworkSession), typeof(string) });

            /// <summary>
            /// Per-screen keyboard dialog used for entering an IP / host[:port].
            /// </summary>
            private static readonly Dictionary<ChooseOnlineGameScreen, PCKeyboardInputScreen> _ipScreens =
                new Dictionary<ChooseOnlineGameScreen, PCKeyboardInputScreen>();

            /// <summary>
            /// Per-screen keyboard dialog used for entering a server password when required.
            /// </summary>
            private static readonly Dictionary<ChooseOnlineGameScreen, PCKeyboardInputScreen> _passwordScreens =
                new Dictionary<ChooseOnlineGameScreen, PCKeyboardInputScreen>();

            /// <summary>
            /// Per-screen active discovery objects used during direct-connect host lookup.
            /// </summary>
            private static readonly Dictionary<ChooseOnlineGameScreen, HostDiscovery> _discoveries =
                new Dictionary<ChooseOnlineGameScreen, HostDiscovery>();

            /// <summary>
            /// Remembers the last manually entered address for convenience.
            /// </summary>
            private static string _lastAddress = "";

            /// <summary>
            /// Folder used to persist Direct Connect data.
            /// </summary>
            ///
            private static readonly string _directConnectFolder =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(DirectConnect).Namespace);

            /// <summary>
            /// Text file that stores the last used direct-connect address.
            /// </summary>
            private static readonly string _lastAddressFile =
                Path.Combine(_directConnectFolder, "LastServerAddress.txt");

            /// <summary>
            /// Prevents repeated disk reads every time the menu is opened.
            /// </summary>
            private static bool _lastAddressLoaded = false;

            /// <summary>
            /// Original provider captured before switching to the Lidgren-based direct-connect provider.
            /// Restored when returning to normal menu browsing.
            /// </summary>
            private static NetworkSessionStaticProvider _originalProvider;

            #endregion

            #region Layout Helpers

            /// <summary>
            /// Scaled vertical spacing between the two custom buttons.
            /// </summary>
            private static int ButtonGapPX()
            {
                return (int)(8f * Screen.Adjuster.ScaleFactor.Y);
            }

            /// <summary>
            /// Scaled right-side margin from the screen edge.
            /// </summary>
            private static int MarginXPX()
            {
                return (int)(20f * Screen.Adjuster.ScaleFactor.X);
            }

            /// <summary>
            /// Scaled bottom-side margin from the screen edge.
            /// </summary>
            private static int MarginYPX()
            {
                return (int)(20f * Screen.Adjuster.ScaleFactor.Y);
            }

            /// <summary>
            /// Applies stock menu styling to a custom button by borrowing appearance settings
            /// from the screen's existing Select / Refresh controls.
            ///
            /// Notes:
            /// - Uses stock frame, color, scale, and font so the buttons blend into the vanilla UI.
            /// - Falls back between Select and Refresh resources when needed.
            /// </summary>
            private static void StyleLikeStock(FrameButtonControl target, FrameButtonControl select, FrameButtonControl refresh)
            {
                if (target == null || select == null || refresh == null)
                    return;

                target.Size        = BaseButtonSize;
                target.Scale       = select.Scale;
                target.Frame       = select.Frame ?? refresh.Frame;
                target.ButtonColor = refresh.ButtonColor;
                target.Font        = CastleMinerZGame.Instance._medFont;
            }

            /// <summary>
            /// Parses either:
            /// - IP
            /// - IP:Port
            ///
            /// Notes:
            /// - Defaults to port 61903 when only an IP is provided.
            /// - Only numeric IP input is accepted here; host name resolution is not performed by this parser.
            /// </summary>
            private static bool TryParseAddress(string text, out string ip, out int port)
            {
                ip = null;
                port = 61903;

                if (string.IsNullOrWhiteSpace(text))
                    return false;

                string[] parts = text.Trim().Split(':');
                if (parts.Length == 1)
                {
                    if (!System.Net.IPAddress.TryParse(parts[0], out _))
                        return false;

                    ip = parts[0];
                    return true;
                }

                if (parts.Length == 2)
                {
                    if (!System.Net.IPAddress.TryParse(parts[0], out _))
                        return false;

                    if (!int.TryParse(parts[1], out port) || port <= 0 || port > 65535)
                        return false;

                    ip = parts[0];
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Anchors both custom buttons at the bottom-right of the current screen.
            ///
            /// Layout:
            /// - Launch Dedicated (top)
            /// - Direct Connect  (bottom)
            ///
            /// Notes:
            /// - Runs repeatedly during update to survive screen scaling / resolution changes.
            /// - Uses the game's current UI scale factor.
            /// </summary>
            private static void PlaceButtons(ChooseOnlineGameScreen screen)
            {
                if (screen == null || _launchDedicatedButton == null || _directConnectButton == null)
                    return;

                if (!(_fiSelect?.GetValue(screen) is FrameButtonControl select) ||
                    !(_fiRefresh?.GetValue(screen) is FrameButtonControl refresh))
                    return;

                StyleLikeStock(_launchDedicatedButton, select, refresh);
                StyleLikeStock(_directConnectButton, select, refresh);

                Rectangle r = Screen.Adjuster.ScreenRect;

                int gap = ButtonGapPX();
                int marginX = MarginXPX();
                int marginY = MarginYPX();

                // FrameButtonControl.Size is already scaled.
                int width = _directConnectButton.Size.Width;
                int height = _directConnectButton.Size.Height;

                int x = r.Right - width - marginX;

                // Bottom button = Direct Connect
                int yDirect = r.Bottom - height - marginY;
                _directConnectButton.LocalPosition = new Point(x, yDirect);

                // Top button = Launch Dedicated
                int yLaunch = yDirect - height - gap;
                _launchDedicatedButton.LocalPosition = new Point(x, yLaunch);
            }
            #endregion

            #region Lifecycle Hooks

            #region OnPushed / OnUpdate / OnPoped

            /// <summary>
            /// Injects buttons, creates per-screen input dialogs, captures the original provider,
            /// and performs an immediate initial layout pass.
            ///
            /// Notes:
            /// - Buttons are created once and then reused.
            /// - Keyboard input screens are stored per ChooseOnlineGameScreen instance.
            /// - Any exception here is logged instead of breaking the menu flow.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("OnPushed")]
            public static void OnPushed_Postfix(ChooseOnlineGameScreen __instance)
            {
                try
                {
                    if (_launchDedicatedButton == null)
                    {
                        _launchDedicatedButton = new FrameButtonControl
                        {
                            Text = "Launch Dedicated",
                            Font = CastleMinerZGame.Instance._medFont
                        };
                        _launchDedicatedButton.Pressed += LaunchDedicatedButton_Pressed;
                    }

                    if (_directConnectButton == null)
                    {
                        _directConnectButton = new FrameButtonControl
                        {
                            Text = "Direct Connect",
                            Font = CastleMinerZGame.Instance._medFont
                        };
                        _directConnectButton.Pressed += DirectConnectButton_Pressed;
                    }

                    if (!__instance.Controls.Contains(_launchDedicatedButton))
                        __instance.Controls.Add(_launchDedicatedButton);

                    if (!__instance.Controls.Contains(_directConnectButton))
                        __instance.Controls.Add(_directConnectButton);

                    if (!_ipScreens.ContainsKey(__instance))
                    {
                        CastleMinerZGame game = (CastleMinerZGame)_fiGame.GetValue(__instance);

                        PCKeyboardInputScreen ipScreen = new PCKeyboardInputScreen(
                            game,
                            "Direct Connect",
                            "Enter IP or host[:port]:",
                            game.DialogScreenImage,
                            game._myriadMed,
                            true,
                            game.ButtonFrame)
                        {
                            ClickSound = "Click",
                            OpenSound = "Popup"
                        };
                        _ipScreens[__instance] = ipScreen;

                        PCKeyboardInputScreen passwordScreen = new PCKeyboardInputScreen(
                            game,
                            "Server Password",
                            "Enter server password:",
                            game.DialogScreenImage,
                            game._myriadMed,
                            true,
                            game.ButtonFrame)
                        {
                            ClickSound = "Click",
                            OpenSound = "Popup"
                        };
                        _passwordScreens[__instance] = passwordScreen;
                    }

                    LoadLastAddressFromDisk();

                    if (_originalProvider == null)
                        _originalProvider = NetworkSession.StaticProvider;

                    _lastScreen = __instance;

                    // Avoid a one-frame layout mismatch.
                    PlaceButtons(__instance);

                    // Log("Bottom-right buttons injected.");
                }
                catch (Exception ex)
                {
                    Log($"Error adding bottom-right buttons: {ex.Message}.");
                }
            }

            /// <summary>
            /// Re-applies anchored placement every frame and updates any active discovery object.
            ///
            /// Notes:
            /// - Wrapped in a fail-safe try/catch so UI update flow is never interrupted.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("OnUpdate")]
            public static void OnUpdate_Postfix(ChooseOnlineGameScreen __instance)
            {
                try
                {
                    PlaceButtons(__instance);

                    if (_discoveries.TryGetValue(__instance, out var hd) && hd != null)
                        hd.Update();
                }
                catch
                {
                    // Never break the UI loop.
                }
            }

            /// <summary>
            /// Cleans up any active discovery object when the screen is popped.
            ///
            /// Notes:
            /// - The dialog screens themselves are cached per screen instance.
            /// - Discovery shutdown is best-effort and intentionally silent on failure.
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("OnPoped")]
            public static void OnPoped_Postfix(ChooseOnlineGameScreen __instance)
            {
                try
                {
                    if (_discoveries.TryGetValue(__instance, out var hd) && hd != null)
                    {
                        try { hd.Shutdown(); } catch { }
                        _discoveries.Remove(__instance);
                    }
                }
                catch
                {
                }
            }
            #endregion

            #endregion

            #region Button Handlers

            /// <summary>
            /// Handles clicks for the "Launch Dedicated" button.
            ///
            /// Purpose:
            /// - Locates the external dedicated server EXE.
            /// - Launches it using the EXE's own working directory.
            ///
            /// Notes:
            /// - If the EXE is not found, a user-facing dialog explains where it is expected.
            /// </summary>
            private static void LaunchDedicatedButton_Pressed(object sender, EventArgs e)
            {
                try
                {
                    string exePath = FindDedicatedServerExe();

                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    {
                        CastleMinerZGame.Instance.FrontEnd.ShowUIDialog(
                            "Launch Dedicated",
                            "CMZServerHost.exe was not found.\n\n" +
                            "To use Launch Dedicated, place the server executable in one of these locations:\n" +
                            "- Next to CastleMinerZ.exe\n" +
                            "- !Mods\\CMZServerHost\\CMZServerHost.exe\n" +
                            "- !Mods\\DirectConnect\\CMZServerHost.exe\n\n" +
                            "Please also make sure the file is named exactly:\n" +
                            "CMZServerHost.exe",
                            false);
                        return;
                    }

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName         = exePath,
                        WorkingDirectory = Path.GetDirectoryName(exePath),
                        UseShellExecute  = false
                    };

                    Process.Start(psi);

                    Log($"Launched dedicated server host: {exePath}.");
                }
                catch (Exception ex)
                {
                    CastleMinerZGame.Instance.FrontEnd.ShowUIDialog(
                        "Launch Dedicated",
                        ex.Message,
                        false);
                }
            }

            /// <summary>
            /// Handles clicks for the "Direct Connect" button.
            ///
            /// Flow:
            /// - Prompts for IP or IP:Port.
            /// - Parses and validates input.
            /// - Creates a synthetic <see cref="AvailableNetworkSession"/>.
            /// - Forwards that session into the game's existing join path.
            ///
            /// Notes:
            /// - This path currently joins immediately through <see cref="JoinViaLidgren"/>.
            /// - The older discovery-based path remains below as reusable helper logic.
            /// </summary>
            private static void DirectConnectButton_Pressed(object sender, EventArgs e)
            {
                try
                {
                    ChooseOnlineGameScreen screen = _lastScreen;
                    if (screen == null)
                        return;

                    if (!_ipScreens.TryGetValue(screen, out var ipScreen))
                        return;

                    LoadLastAddressFromDisk();
                    ipScreen.DefaultText = _lastAddress ?? "";

                    CastleMinerZGame.Instance.FrontEnd.ShowPCDialogScreen(ipScreen, delegate
                    {
                        if (ipScreen.OptionSelected == -1)
                            return;

                        string text = ipScreen.TextInput != null
                            ? ipScreen.TextInput.Trim()
                            : "";

                        if (string.IsNullOrWhiteSpace(text))
                            return;

                        if (!TryParseAddress(text, out string ip, out int port))
                        {
                            CastleMinerZGame.Instance.FrontEnd.ShowUIDialog(
                                "Connection Error",
                                "Invalid address. Use IP or IP:Port",
                                false);
                            return;
                        }

                        _lastAddress = text;
                        SaveLastAddressToDisk(_lastAddress);

                        Log($"Parsed -> {ip}:{port}.");

                        var session = CreateDirectAvailableSession(ip, port);
                        if (session == null)
                        {
                            CastleMinerZGame.Instance.FrontEnd.ShowUIDialog(
                                "Connection Error",
                                "Failed to create direct-connect session.",
                                false);
                            return;
                        }

                        JoinViaLidgren(session, "");
                    });
                }
                catch (Exception ex)
                {
                    Log($"Button click error: {ex}.");
                    CastleMinerZGame.Instance.FrontEnd.ShowUIDialog("Connection Error", ex.Message, false);
                }
            }

            /// <summary>
            /// Constructs a synthetic <see cref="AvailableNetworkSession"/> instance for a manual IP / port target.
            ///
            /// Purpose:
            /// - Allows the vanilla JoinGame pipeline to be reused without depending on Steam server browsing.
            ///
            /// Notes:
            /// - Uses a non-public constructor discovered via reflection.
            /// - Session properties are populated with safe defaults expected by the game.
            /// - This does not prove the server is reachable; it only prepares the object needed for join.
            /// </summary>
            private static AvailableNetworkSession CreateDirectAvailableSession(string ip, int port)
            {
                try
                {
                    var sessionType = typeof(AvailableNetworkSession);
                    var propsType   = typeof(NetworkSessionProperties);

                    var ipAddr   = System.Net.IPAddress.Parse(ip);
                    var endPoint = new System.Net.IPEndPoint(ipAddr, port);

                    var props = new NetworkSessionProperties
                    {
                        // Match what the game expects.
                        [0] = CastleMinerZGame.NetworkVersion, // Version.
                        [1] = 0,                               // Joinable.
                        [2] = (int)GameModeTypes.Survival,     // Safe default.
                        [3] = (int)GameDifficultyTypes.EASY,   // Safe default.
                        [4] = 0,                               // Infinite resources off.
                        [5] = 0                                // Pvp off.
                    };

                    // Non-public ctor used by your friend's patch path.
                    var ctor = sessionType.GetConstructor(
                        BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new[]
                        {
                            typeof(System.Net.IPEndPoint),
                            typeof(string),
                            typeof(string),
                            typeof(int),
                            propsType,
                            typeof(int),
                            typeof(int),
                            typeof(bool)
                        },
                        null);

                    if (ctor == null)
                    {
                        Log("AvailableNetworkSession internal ctor not found.");
                        return null;
                    }

                    object obj = ctor.Invoke(new object[]
                    {
                        endPoint,
                        "Direct Connect", // Host gamertag / display.
                        "Direct Connect", // Server message.
                        0,                // Current players.
                        props,
                        16,               // Max players.
                        0,                // Friends.
                        false             // Password protected.
                    });

                    var ans = obj as AvailableNetworkSession;
                    Log($"Created direct session for {ip}:{port}.");
                    return ans;
                }
                catch (Exception ex)
                {
                    Log($"CreateDirectAvailableSession error: {ex}.");
                    return null;
                }
            }
            #endregion

            #region Dedicated EXE Finder

            /// <summary>
            /// Finds the standalone dedicated server executable.
            ///
            /// Search order:
            /// - Next to CastleMinerZ.exe
            /// - !Mods\CMZServerHost\CMZServerHost.exe
            /// - !Mods\DirectConnect\CMZServerHost.exe
            ///
            /// Notes:
            /// - Update this list if your deployment layout changes.
            /// </summary>
            private static string FindDedicatedServerExe()
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                string[] candidates = new string[]
                {
                    Path.Combine(baseDir, "CMZServerHost.exe"),
                    Path.Combine(baseDir, "!Mods", "CMZServerHost", "CMZServerHost.exe"),
                    Path.Combine(baseDir, "!Mods", "DirectConnect", "CMZServerHost.exe"),
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(candidates[i]))
                        return candidates[i];
                }

                return null;
            }
            #endregion

            #region Discovery / Join Helpers

            /// <summary>
            /// Starts a Lidgren-backed host discovery request for the supplied address.
            ///
            /// Purpose:
            /// - Swaps the static provider to Lidgren.
            /// - Creates a <see cref="HostDiscovery"/> instance.
            /// - Begins asynchronous host lookup.
            ///
            /// Notes:
            /// - Any existing discovery attached to the same screen is first shut down.
            /// - Results are marshalled back to the main thread before UI handling.
            /// </summary>
            private static void BeginDiscovery(ChooseOnlineGameScreen screen, string address)
            {
                try
                {
                    if (_discoveries.TryGetValue(screen, out var oldHd) && oldHd != null)
                    {
                        try { oldHd.Shutdown(); } catch { }
                    }

                    if (_originalProvider == null)
                        _originalProvider = NetworkSession.StaticProvider;

                    // Switch the game to Lidgren BEFORE creating discovery.
                    NetworkSession.StaticProvider = new LidgrenNetworkSessionStaticProvider();

                    HostDiscovery hd = NetworkSession.GetHostDiscoveryObject(
                        CastleMinerZGame.NetworkGameName,
                        CastleMinerZGame.NetworkVersion,
                        DNAGame.GetLocalID());

                    _discoveries[screen] = hd;

                    Log($"BeginDiscovery -> {address}.");

                    hd.GetHostInfo(address, delegate (HostDiscovery.ResultCode result, AvailableNetworkSession session, object context)
                    {
                        TaskDispatcher.Instance.AddTaskForMainThread((ThreadStart)delegate
                        {
                            Log($"Discovery callback -> result={result}, {session}={(session != null ? "ok" : "null")}.");

                            HandleDiscoveryResult(screen, result, session);
                        });
                    }, null);
                }
                catch (Exception ex)
                {
                    Log($"BeginDiscovery exception: {ex}.");
                    CastleMinerZGame.Instance.FrontEnd.ShowUIDialog("Connection Error", ex.Message, false);
                }
            }

            /// <summary>
            /// Handles the asynchronous discovery result.
            ///
            /// Flow:
            /// - Fails with a dialog when discovery was unsuccessful.
            /// - Prompts for password when the discovered session is protected.
            /// - Otherwise joins immediately through the Lidgren provider.
            /// </summary>
            private static void HandleDiscoveryResult(ChooseOnlineGameScreen screen, HostDiscovery.ResultCode result, AvailableNetworkSession session)
            {
                try
                {
                    CastleMinerZGame game = CastleMinerZGame.Instance;

                    if (result != HostDiscovery.ResultCode.Success || session == null)
                    {
                        game.FrontEnd.ShowUIDialog("Connection Error", $"Direct connect failed: {result}", false);
                        return;
                    }

                    if (session.PasswordProtected)
                    {
                        if (!_passwordScreens.TryGetValue(screen, out var passwordScreen))
                            return;

                        passwordScreen.DefaultText = "";

                        game.FrontEnd.ShowPCDialogScreen(passwordScreen, delegate
                        {
                            if (passwordScreen.OptionSelected == -1)
                                return;

                            JoinViaLidgren(session, passwordScreen.TextInput ?? "");
                        });

                        return;
                    }

                    JoinViaLidgren(session, "");
                }
                catch (Exception ex)
                {
                    CastleMinerZGame.Instance.FrontEnd.ShowUIDialog("Connection Error", ex.Message, false);
                }
            }

            /// <summary>
            /// Performs the actual join call using the Lidgren-based provider.
            ///
            /// Notes:
            /// - This method intentionally reuses the game's private FrontEnd.JoinGame(...) path.
            /// - The original static provider is preserved for restoration later.
            /// </summary>
            private static void JoinViaLidgren(AvailableNetworkSession session, string password)
            {
                try
                {
                    if (session == null)
                    {
                        CastleMinerZGame.Instance.FrontEnd.ShowUIDialog(
                            "Connection Error",
                            "Direct-connect session was null.",
                            false);
                        return;
                    }

                    if (_originalProvider == null)
                        _originalProvider = NetworkSession.StaticProvider;

                    Log($"JoinViaLidgren -> {session.ServerMessage}.");

                    NetworkSession.StaticProvider = new LidgrenNetworkSessionStaticProvider();

                    _miJoinGame.Invoke(CastleMinerZGame.Instance.FrontEnd, new object[] { session, password });

                    Log("FrontEnd.JoinGame invoked.");
                }
                catch (Exception ex)
                {
                    Log($"JoinViaLidgren error: {ex}.");
                    CastleMinerZGame.Instance.FrontEnd.ShowUIDialog("Connection Error", ex.Message, false);
                }
            }
            #endregion

            #region Last Address Persistence

            /// <summary>
            /// Loads the last saved direct-connect address from disk once per session.
            /// </summary>
            private static void LoadLastAddressFromDisk()
            {
                try
                {
                    if (_lastAddressLoaded)
                        return;

                    _lastAddressLoaded = true;

                    if (!Directory.Exists(_directConnectFolder))
                        Directory.CreateDirectory(_directConnectFolder);

                    if (!File.Exists(_lastAddressFile))
                    {
                        _lastAddress = "";
                        return;
                    }

                    _lastAddress = (File.ReadAllText(_lastAddressFile) ?? "").Trim();

                    // Log($"Loaded last direct-connect address: '{_lastAddress}'.");
                }
                catch (Exception ex)
                {
                    _lastAddress = "";
                    Log($"LoadLastAddressFromDisk error: {ex}.");
                }
            }

            /// <summary>
            /// Saves the last successfully accepted direct-connect address to disk.
            /// </summary>
            private static void SaveLastAddressToDisk(string address)
            {
                try
                {
                    if (!Directory.Exists(_directConnectFolder))
                        Directory.CreateDirectory(_directConnectFolder);

                    File.WriteAllText(_lastAddressFile, address ?? "");

                    Log($"Saved last direct-connect address: '{address}'.");
                }
                catch (Exception ex)
                {
                    Log($"SaveLastAddressToDisk error: {ex}.");
                }
            }
            #endregion

            #region Restore Provider When Returning To Menu

            /// <summary>
            /// Restores the original provider when returning to the main menu so Steam browsing works again.
            ///
            /// Notes:
            /// - Restoration only occurs when there is no active network session.
            /// - This prevents lingering use of the Lidgren direct-connect provider after leaving the flow.
            /// </summary>
            [HarmonyPatch(typeof(FrontEndScreen), "PopToMainMenu",
                new Type[] { typeof(SignedInGamer), typeof(SuccessCallback) })]
            internal static class Patch_FrontEndScreen_PopToMainMenu_RestoreProvider
            {
                /// <summary>
                /// Restores the original provider captured before direct-connect provider swapping.
                /// </summary>
                private static void Postfix()
                {
                    try
                    {
                        if (_originalProvider != null && NetworkSession.CurrentNetworkSession == null)
                            NetworkSession.StaticProvider = _originalProvider;
                    }
                    catch
                    {
                    }
                }
            }
            #endregion
        }
        #endregion

        #region Buttons: Bottom-Right - Cancel

        /// <summary>
        /// Adds a vanilla-styled Cancel button to the frontend connecting screen.
        ///
        /// Behavior:
        /// - Visible only for actual join flows.
        /// - Clicking Cancel returns to the main menu.
        /// - Esc / controller B / Back also cancel.
        /// - Late JoinCallback results are swallowed after cancel.
        ///
        /// Notes:
        /// - This intentionally avoids DNAGame.LeaveGame() because that can trigger
        ///   the normal session-ended flow and fight the frontend unwind.
        /// - Instead, it silently detaches/disposes any partially joined session.
        /// </summary>
        [HarmonyPatch]
        internal static class Patch_FrontEnd_JoinCancel
        {
            #region Cached Reflection / Accessors

            /// <summary>
            /// Cached accessor for the private FrontEndScreen._connectingScreen field.
            /// Used to identify and target the actual connecting screen instance.
            /// </summary>
            private static readonly AccessTools.FieldRef<FrontEndScreen, Screen> _connectingScreenRef =
                AccessTools.FieldRefAccess<FrontEndScreen, Screen>("_connectingScreen");

            /// <summary>
            /// Cached accessor for DNAGame.processMessages.
            /// Re-enabled when canceling so frontend/network processing resumes normally.
            /// </summary>
            private static readonly AccessTools.FieldRef<DNAGame, bool> _processMessagesRef =
                AccessTools.FieldRefAccess<DNAGame, bool>("processMessages");

            /// <summary>
            /// Cached accessor for DNAGame._networkSession.
            /// Used to tear down a pending or partial join session safely.
            /// </summary>
            private static readonly AccessTools.FieldRef<DNAGame, NetworkSession> _networkSessionRef =
                AccessTools.FieldRefAccess<DNAGame, NetworkSession>("_networkSession");

            /// <summary>
            /// Cached reflection handle for CastleMinerZGame._waitForWorldInfo.
            /// Cleared on cancel to prevent continuation into the world-loading path.
            /// </summary>
            private static readonly FieldInfo _waitForWorldInfoField =
                AccessTools.Field(typeof(CastleMinerZGame), "_waitForWorldInfo");

            /// <summary>
            /// Cached method handle for DNAGame's SessionEnded event handler.
            /// Detached before disposal so shutdown does not route back through normal session-ended UI flow.
            /// </summary>
            private static readonly MethodInfo _miSessionEnded =
                AccessTools.Method(typeof(DNAGame), "_networkSession_SessionEnded");

            /// <summary>
            /// Cached method handle for DNAGame's HostChanged event handler.
            /// Detached before disposal as part of the silent session teardown.
            /// </summary>
            private static readonly MethodInfo _miHostChanged =
                AccessTools.Method(typeof(DNAGame), "_networkSession_HostChanged");

            /// <summary>
            /// Cached method handle for DNAGame's GameStarted event handler.
            /// Detached before disposal as part of the silent session teardown.
            /// </summary>
            private static readonly MethodInfo _miGameStarted =
                AccessTools.Method(typeof(DNAGame), "_networkSession_GameStarted");

            /// <summary>
            /// Cached method handle for DNAGame's GameEnded event handler.
            /// Detached before disposal as part of the silent session teardown.
            /// </summary>
            private static readonly MethodInfo _miGameEnded =
                AccessTools.Method(typeof(DNAGame), "_networkSession_GameEnded");

            /// <summary>
            /// Cached method handle for DNAGame's GamerJoined event handler.
            /// Detached before disposal as part of the silent session teardown.
            /// </summary>
            private static readonly MethodInfo _miGamerJoined =
                AccessTools.Method(typeof(DNAGame), "_networkSession_GamerJoined");

            /// <summary>
            /// Cached method handle for DNAGame's GamerLeft event handler.
            /// Detached before disposal as part of the silent session teardown.
            /// </summary>
            private static readonly MethodInfo _miGamerLeft =
                AccessTools.Method(typeof(DNAGame), "_networkSession_GamerLeft");

            #endregion

            #region Patch State

            /// <summary>
            /// Tracks the active frontend screen that initiated the join flow.
            /// </summary>
            private static FrontEndScreen _activeFrontEnd;

            /// <summary>
            /// True while the current join flow should show and accept cancel input.
            /// </summary>
            private static bool _joinCancelable;

            /// <summary>
            /// True once cancel has been requested, preventing duplicate cancellation
            /// and allowing late callbacks to be swallowed.
            /// </summary>
            private static bool _cancelRequested;

            /// <summary>
            /// Current on-screen rectangle used for the Cancel button hitbox and draw area.
            /// </summary>
            private static Rectangle _buttonRect;

            /// <summary>
            /// True while the mouse is currently hovering the Cancel button.
            /// </summary>
            private static bool _buttonHover;

            /// <summary>
            /// True while the left mouse button is captured on the Cancel button,
            /// allowing press-then-release behavior similar to vanilla buttons.
            /// </summary>
            private static bool _buttonCapture;

            #endregion

            #region Join Flow Arming

            /// <summary>
            /// Arms cancel support when the normal JoinGame flow begins.
            /// </summary>
            [HarmonyPatch(typeof(FrontEndScreen), "JoinGame", new[] { typeof(AvailableNetworkSession), typeof(string) })]
            [HarmonyPrefix]
            private static void JoinGame_Prefix(FrontEndScreen __instance)
            {
                Arm(__instance);
            }

            /// <summary>
            /// Arms cancel support when the invited join flow begins.
            /// </summary>
            [HarmonyPatch(typeof(FrontEndScreen), "JoinInvitedGame", new[] { typeof(ulong) })]
            [HarmonyPrefix]
            private static void JoinInvitedGame_Prefix(FrontEndScreen __instance)
            {
                Arm(__instance);
            }

            /// <summary>
            /// Resets state for a new cancelable join flow.
            ///
            /// Notes:
            /// - Clears any stale hover / capture / rect data from prior join attempts.
            /// - Records the active frontend so later patches know which connecting screen belongs to us.
            /// </summary>
            private static void Arm(FrontEndScreen frontEnd)
            {
                _activeFrontEnd  = frontEnd;
                _joinCancelable  = true;
                _cancelRequested = false;
                _buttonRect      = Rectangle.Empty;
                _buttonHover     = false;
                _buttonCapture   = false;
            }
            #endregion

            #region Draw Hook

            /// <summary>
            /// Draws the Cancel button directly onto the frontend connecting screen.
            ///
            /// Notes:
            /// - Uses manual rectangle drawing rather than a live UI control.
            /// - Applies simple vanilla-like idle / hover / pressed coloring.
            /// - Recomputes the button rectangle every draw so it stays anchored to the screen edge.
            /// </summary>
            [HarmonyPatch(typeof(FrontEndScreen), "_connectingScreen_BeforeDraw", new[] { typeof(object), typeof(DrawEventArgs) })]
            [HarmonyPostfix]
            private static void ConnectingScreen_BeforeDraw_Postfix(FrontEndScreen __instance, DrawEventArgs e)
            {
                if (!_joinCancelable || _cancelRequested)
                {
                    return;
                }

                SpriteBatch spriteBatch = e.SpriteBatch;
                SpriteFont  font        = __instance._game._medFont;

                int width  = 300;
                int height = 40;

                int marginX = (int)(20f * Screen.Adjuster.ScaleFactor.X);
                int marginY = (int)(20f * Screen.Adjuster.ScaleFactor.Y);

                int scaledWidth  = (int)(width  * Screen.Adjuster.ScaleFactor.X);
                int scaledHeight = (int)(height * Screen.Adjuster.ScaleFactor.Y);

                int x = Screen.Adjuster.ScreenRect.Right  - scaledWidth  - marginX;
                int y = Screen.Adjuster.ScreenRect.Bottom - scaledHeight - marginY;

                _buttonRect = new Rectangle(x, y, scaledWidth, scaledHeight);

                MouseState ms = Mouse.GetState();
                _buttonHover  = _buttonRect.Contains(ms.X, ms.Y);

                Color MenuGreen   = new Color(78, 177, 61);
                Color baseColor   = new Color(MenuGreen.ToVector4() * 0.8f);
                Color buttonColor = baseColor;
                Color textColor   = Color.Black;

                if (_buttonCapture)
                {
                    buttonColor = Color.Black;
                    textColor   = Color.White;
                }
                else if (_buttonHover)
                {
                    buttonColor = Color.Gray;
                    textColor   = Color.Black;
                }

                Vector2 textSize = font.MeasureString("Cancel");
                Vector2 textPos  = new Vector2(
                    _buttonRect.Center.X - textSize.X / 2f,
                    _buttonRect.Center.Y - ((float)font.LineSpacing / 2f));

                spriteBatch.Begin();

                __instance._game.ButtonFrame.Draw(spriteBatch, _buttonRect, buttonColor);
                spriteBatch.DrawString(font, "Cancel", textPos, textColor);

                spriteBatch.End();
            }
            #endregion

            #region Input Hook

            /// <summary>
            /// Intercepts input while the connecting screen is active and handles
            /// mouse / keyboard / controller cancellation.
            ///
            /// Behavior:
            /// - Mouse: capture on press, activate on release while still hovering.
            /// - Keyboard: Esc cancels immediately.
            /// - Controller: B / Back cancels immediately.
            ///
            /// Notes:
            /// - Input is consumed while on the connecting screen so underlying screens do not react.
            /// - Only runs for the specific connecting screen attached to the currently armed frontend.
            /// </summary>
            [HarmonyPatch(typeof(Screen), "ProcessInput", new[] { typeof(InputManager), typeof(GameTime) })]
            [HarmonyPrefix]
            private static bool Screen_ProcessInput_Prefix(Screen __instance, InputManager inputManager, GameTime gameTime, ref bool __result)
            {
                if (!_joinCancelable || _activeFrontEnd == null)
                {
                    return true;
                }

                Screen connectingScreen = _connectingScreenRef(_activeFrontEnd);
                if (!ReferenceEquals(__instance, connectingScreen))
                {
                    return true;
                }

                Point mousePos = inputManager.Mouse.Position;
                _buttonHover   = _buttonRect.Contains(mousePos);

                bool cancelByKeyboard = inputManager.Keyboard.WasKeyPressed(Keys.Escape);

                bool cancelByController = false;
                if (Screen.SelectedPlayerIndex != null)
                {
                    GameController controller = inputManager.Controllers[(int)Screen.SelectedPlayerIndex.Value];
                    cancelByController = controller.PressedButtons.B || controller.PressedButtons.Back;
                }

                // Vanilla-like mouse behavior: capture on press, activate on release while still hovering.
                if (_buttonHover && inputManager.Mouse.LeftButtonPressed)
                {
                    _buttonCapture = true;
                }

                if (_buttonCapture && !inputManager.Mouse.LeftButtonDown)
                {
                    bool releasedOverButton = _buttonHover && inputManager.Mouse.LeftButtonReleased;
                    _buttonCapture = false;

                    if (releasedOverButton)
                    {
                        CancelPendingJoin();
                        __result = false;
                        return false;
                    }
                }

                if (cancelByKeyboard || cancelByController)
                {
                    CancelPendingJoin();
                    __result = false;
                    return false;
                }

                // Consume input while on the connecting screen so nothing underneath reacts.
                __result = false;
                return false;
            }
            #endregion

            #region Cancel / Cleanup

            /// <summary>
            /// Performs the actual cancel flow for a pending join.
            ///
            /// Behavior:
            /// - Prevents duplicate cancellation.
            /// - Clears UI interaction state.
            /// - Re-enables normal message processing.
            /// - Clears any pending world-info continuation.
            /// - Silently disposes any partial network session.
            /// - Returns the player cleanly to the main menu.
            /// </summary>
            private static void CancelPendingJoin()
            {
                if (_activeFrontEnd == null || _cancelRequested)
                {
                    return;
                }

                _cancelRequested = true;
                _joinCancelable  = false;
                _buttonCapture   = false;
                _buttonHover     = false;
                _buttonRect      = Rectangle.Empty;

                CastleMinerZGame game = _activeFrontEnd._game;

                // Re-enable normal network message processing.
                _processMessagesRef(game) = true;

                // Clear any pending world-info continuation.
                _waitForWorldInfoField?.SetValue(game, null);

                // Silently tear down any partially joined session without triggering the normal
                // "session ended" frontend flow.
                DisposePendingSessionSilently(game);

                // Return cleanly to the main menu.
                if (Screen.CurrentGamer != null)
                {
                    _activeFrontEnd.PopToMainMenu(Screen.CurrentGamer, delegate (bool success) { });
                }
            }

            /// <summary>
            /// Disposes a pending or partial network session without allowing DNAGame's
            /// normal session-ended handlers to drive additional frontend transitions.
            ///
            /// Notes:
            /// - Event handlers are detached first on a best-effort basis.
            /// - Disposal is intentionally quiet; all failures are swallowed.
            /// - The cached network-session field is nulled afterward.
            /// </summary>
            private static void DisposePendingSessionSilently(DNAGame game)
            {
                NetworkSession session = _networkSessionRef(game);
                if (session == null)
                {
                    return;
                }

                try
                {
                    // Detach DNAGame callbacks so dispose does not bounce through normal session-ended flow.
                    session.SessionEnded -= (EventHandler<NetworkSessionEndedEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<NetworkSessionEndedEventArgs>), game, _miSessionEnded);

                    session.HostChanged  -= (EventHandler<HostChangedEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<HostChangedEventArgs>), game, _miHostChanged);

                    session.GameStarted  -= (EventHandler<GameStartedEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<GameStartedEventArgs>), game, _miGameStarted);

                    session.GameEnded    -= (EventHandler<GameEndedEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<GameEndedEventArgs>), game, _miGameEnded);

                    session.GamerJoined  -= (EventHandler<GamerJoinedEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<GamerJoinedEventArgs>), game, _miGamerJoined);

                    session.GamerLeft    -= (EventHandler<GamerLeftEventArgs>)Delegate.CreateDelegate(
                        typeof(EventHandler<GamerLeftEventArgs>), game, _miGamerLeft);
                }
                catch
                {
                }

                try
                {
                    session.Dispose();
                }
                catch
                {
                }

                _networkSessionRef(game) = null;
            }
            #endregion

            #region Join Callback Handling

            /// <summary>
            /// Swallows a late JoinCallback after the player has already canceled.
            ///
            /// Behavior:
            /// - Silently disposes any remaining session.
            /// - Clears pending world-info continuation.
            /// - Resets cancel/button state.
            /// - Prevents the original callback path from running.
            /// </summary>
            [HarmonyPatch(typeof(FrontEndScreen), "JoinCallback", new[] { typeof(bool), typeof(string) })]
            [HarmonyPrefix]
            private static bool JoinCallback_Prefix(FrontEndScreen __instance)
            {
                if (!_cancelRequested)
                {
                    return true;
                }

                DisposePendingSessionSilently(__instance._game);
                _waitForWorldInfoField?.SetValue(__instance._game, null);

                _cancelRequested = false;
                _joinCancelable  = false;
                _buttonCapture   = false;
                _buttonHover     = false;
                _buttonRect      = Rectangle.Empty;

                return false;
            }

            /// <summary>
            /// Resets cancel-related state after a failed join callback.
            ///
            /// Notes:
            /// - Successful joins intentionally do not clear the state here,
            ///   because success transitions to the normal game flow.
            /// </summary>
            [HarmonyPatch(typeof(FrontEndScreen), "JoinCallback", new[] { typeof(bool), typeof(string) })]
            [HarmonyPostfix]
            private static void JoinCallback_Postfix(bool success)
            {
                if (!success)
                {
                    _joinCancelable  = false;
                    _cancelRequested = false;
                    _buttonCapture   = false;
                    _buttonHover     = false;
                    _buttonRect      = Rectangle.Empty;
                }
            }
            #endregion
        }
        #endregion

        #region Join Callback Spy

        /// <summary>
        /// Logs the game's final join verdict.
        ///
        /// Purpose:
        /// - Confirms whether FrontEndScreen.JoinGame(...)
        ///   is actually reaching the built-in join callback.
        /// - Provides a hook for async join wait helpers.
        ///
        /// Notes:
        /// - Useful for debugging silent failures in the direct-connect path.
        /// </summary>
        [HarmonyPatch(typeof(FrontEndScreen), "JoinCallback")]
        internal static class FrontEnd_JoinCallback_Spy
        {
            /// <summary>
            /// Records the final join result and completes any pending join signal waiter.
            /// </summary>
            [HarmonyPostfix]
            private static void Postfix(bool success, string message)
            {
                // Log($"JoinCallback -> success={success}, msg={message}.");

                // Only keep this line if you also add the JoinSignal helper below.
                JoinSignal.Complete(success, message);
            }
        }

        #region Join Signal Helper

        /// <summary>
        /// Small async helper used to wait for the game's join callback.
        ///
        /// Purpose:
        /// - Allows external / async code to "arm" a join operation and await the callback result.
        ///
        /// Notes:
        /// - This helper is optional, but useful when debugging or sequencing join flows.
        /// - Uses RunContinuationsAsynchronously to avoid inline continuation surprises.
        /// </summary>
        internal static class JoinSignal
        {
            /// <summary>
            /// Current completion source for the armed join operation.
            /// </summary>
            private static System.Threading.Tasks.TaskCompletionSource<(bool ok, string msg)> _tcs;

            /// <summary>
            /// Arms a new join wait operation.
            /// </summary>
            public static void Arm()
            {
                _tcs = new System.Threading.Tasks.TaskCompletionSource<(bool, string)>(
                    System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            }

            /// <summary>
            /// Completes the currently armed join wait operation.
            /// </summary>
            public static void Complete(bool ok, string message)
            {
                _tcs?.TrySetResult((ok, message ?? string.Empty));
            }

            /// <summary>
            /// Waits asynchronously for join completion, timeout, or cancellation.
            ///
            /// Returns:
            /// - Final join result on success.
            /// - A failure tuple when timed out or cancelled.
            /// </summary>
            public static async System.Threading.Tasks.Task<(bool ok, string msg)> WaitAsync(
                TimeSpan timeout,
                CancellationToken cancel)
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    cts.CancelAfter(timeout);
                    var t = _tcs?.Task ?? System.Threading.Tasks.Task.FromResult((false, "Join not armed."));

                    using (cts.Token.Register(() => _tcs?.TrySetCanceled()))
                    {
                        try { return await t.ConfigureAwait(false); }
                        catch { return (false, "Join wait cancelled/timeout."); }
                    }
                }
            }
        }
        #endregion

        #endregion

        #endregion
    }
}