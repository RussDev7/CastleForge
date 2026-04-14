/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorldEditPixelart;
using DNA.CastleMinerZ;
using System.Drawing;
using System;

/// <summary>
/// Purpose:
/// - Host the WinForms editor inside the game process.
/// - Provide a simple API: Toggle / Show / Hide / Dispose.
/// - Freeze / unfreeze game input via CaptureInput (used by Harmony gates).
/// - Optionally embed as a real child window (WS_CHILD) of the game.
///
/// Notes:
/// - ConfigGlobals are used across the whole mod (e.g., ToggleKey, EmbedAsChild).
/// - Prefer gating game input off 'CaptureInput', not 'IsOpen'.
/// </summary>
internal static class WinFormsOverlay
{
    #region P/Invoke & Win32

    [DllImport("user32.dll")] private static extern bool   SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] private static extern int    GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int    SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool   GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool   ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool   SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                                                        int X, int Y, int cx, int cy, uint uFlags);

    private const int  GWL_STYLE   = -16;
    private const int  WS_CHILD    = 0x40000000;
    private const int  WS_CAPTION  = 0x00C00000;
    private const int  WS_THICK    = 0x00040000;
    private const int  WS_MINBOX   = 0x00020000;
    private const int  WS_MAXBOX   = 0x00010000;
    private const int  WS_OVERLAPPEDWINDOW = WS_CAPTION | WS_THICK | WS_MINBOX | WS_MAXBOX;

    private const uint SWP_NOSIZE      = 0x0001;
    private const uint SWP_NOZORDER    = 0x0004;
    private const uint SWP_NOACTIVATE  = 0x0010;
    private const uint SWP_FRAMECHANGED= 0x0020;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    #endregion

    #region State & Flags

    private static WE_ImageToPixelart.MainForm _form;
    private static IntPtr _gameHwnd;

    /// <summary>Authoritative "freeze game input" flag. Harmony patches should read this.</summary>
    public static bool CaptureInput { get; private set; }

    /// <summary>UI state only (derived from form visibility).</summary>
    public static bool IsOpen => _form != null && _form.Visible;

    /// <summary>Owner wrapper for Show(owner) path.</summary>
    private sealed class WindowWrapper : IWin32Window
    {
        public WindowWrapper(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }

    // Default client size for the tool when not maximized.
    private static readonly Size DefaultClientSize = new Size(1774, 881);

    /// <summary>
    /// Remembers whether we temporarily forced the game out of fullscreen
    /// when opening the WinForms editor, so Hide() can restore it later.
    /// </summary>
    private static bool _restoreFullscreenOnHide;

    #endregion

    #region Creation & Setup

    /// <summary>
    /// Ensure the form is constructed and wired. Idempotent.
    /// </summary>
    private static void EnsureCreated()
    {
        if (_form != null) return;

        _gameHwnd = CastleMinerZGame.Instance?.Window?.Handle ?? IntPtr.Zero;
        if (_gameHwnd == IntPtr.Zero) return;

        _form = new WE_ImageToPixelart.MainForm
        {
            // If embedding as child, we'll tweak style later; keep this non-tool window by default.
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox     = false,
            ClientSize      = DefaultClientSize,
            ShowInTaskbar   = false,
            StartPosition   = FormStartPosition.CenterScreen,
            KeyPreview      = true,
            TopMost         = true, // Top-most is harmless; child embedding will disable it.
            ShowIcon        = true
        };

        // Convert user "X" close into Hide() so we keep one instance and hotkey behavior.
        _form.FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        // Close via the configured key while the form has focus.
        _form.KeyDown += (s, e) =>
        {
            if (ConfigGlobals.ToggleKey.HasValue && e.KeyCode == ConfigGlobals.ToggleKey.Value)
            {
                e.Handled = true;
                Hide();
            }
        };

        // Optional: Embed as a real child of the game window (no taskbar, follows parent z-order).
        if (ConfigGlobals.EmbedAsChild)
            MakeChild(_form, _gameHwnd);
    }

    /// <summary>
    /// Turn the form into a genuine child (WS_CHILD) of the game window.
    /// </summary>
    private static void MakeChild(Form f, IntPtr parent)
    {
        // Force handle creation before mutating styles.
        _ = f.Handle;

        SetParent(f.Handle, parent);

        int style = GetWindowLong(f.Handle, GWL_STYLE);
        style &= ~WS_OVERLAPPEDWINDOW; // Remove top-level chrome.
        style |= WS_CHILD;             // Make it a child window.
        SetWindowLong(f.Handle, GWL_STYLE, style);

        // Child windows don't need TopMost/taskbar; parent controls Z-order.
        f.TopMost       = false;
        f.ShowInTaskbar = false;
        f.MinimizeBox   = false;
        f.FormBorderStyle = FormBorderStyle.Sizable;

        // Size can be overridden later; this is just an initial value.
        f.ClientSize = DefaultClientSize;
    }
    #endregion

    #region Public API (Toggle / Show / Hide / Dispose)

    /// <summary>Two-state toggle: Open+freeze on first press, close+unfreeze on second.</summary>
    public static void Toggle()
    {
        if (!IsOpen) Show();
        else Hide();
    }

    /// <summary>
    /// Show the overlay and freeze game input immediately.
    /// </summary>
    public static void Show()
    {
        // Optional: Only allow showing while actually in a session.
        var gameInstance = CastleMinerZGame.Instance;
        var netInstance  = gameInstance?.CurrentNetworkSession;
        if (netInstance == null) return;

        // WinForms overlays commonly disappear behind true fullscreen swap-chain output.
        // Move to windowed first so the editor remains visible and interactive.
        if (gameInstance.IsFullScreen)
        {
            _restoreFullscreenOnHide  = true;
            gameInstance.IsFullScreen = false;
        }
        else
        {
            _restoreFullscreenOnHide = false;
        }

        EnsureCreated();
        if (_form == null) return;

        // Freeze game input FIRST so Harmony gates apply on this very frame.
        CaptureInput = true;

        // Choose owner path if not a real child; child path uses client coords.
        var owner = new WindowWrapper(_gameHwnd);

        if (_form.WindowState != FormWindowState.Maximized)
        {
            _form.ClientSize = DefaultClientSize;
            CenterOverGame(_form, asChild: ConfigGlobals.EmbedAsChild);
        }

        _form.Show(owner);
        _form.Activate(); // Take focus now.
    }

    /// <summary>
    /// Hide the overlay, unfreeze input, and proactively refocus the game.
    /// </summary>
    public static void Hide()
    {
        if (_form == null) return;

        _form.Hide();

        // Release Harmony gates.
        CaptureInput = false;

        // Restore fullscreen only if Show() temporarily changed it.
        var gameInstance = CastleMinerZGame.Instance;
        if (_restoreFullscreenOnHide && gameInstance != null)
        {
            try
            {
                gameInstance.IsFullScreen = true;
            }
            catch
            {
                // Swallow restore failures; focus handoff below is still safe/useful.
            }

            _restoreFullscreenOnHide = false;
        }

        // Hand focus back to the game immediately (don't rely on Windows heuristics).
        if (_gameHwnd != IntPtr.Zero)
            SetForegroundWindow(_gameHwnd);
    }

    /// <summary>
    /// Rebuild the overlay form so config-driven host changes apply cleanly.
    /// </summary>
    public static void RebuildFromConfig()
    {
        bool wasOpen = IsOpen;
        Dispose();

        if (wasOpen)
            Show();
    }

    /// <summary>Close and dispose the overlay. Safe to call multiple times.</summary>
    public static void Dispose()
    {
        try { _form?.Close(); _form?.Dispose(); }
        catch { /* swallow on shutdown */ }
        finally { _form = null; CaptureInput = false; }
    }
    #endregion

    #region Positioning & Layout Helpers

    /// <summary>
    /// Center the overlay over the game's client area (owned top-level vs child).
    /// </summary>
    private static void CenterOverGame(Form f, bool asChild)
    {
        if (_gameHwnd == IntPtr.Zero || f == null || f.IsDisposed) return;

        // Get the game client rect (size in client coords).
        if (!GetClientRect(_gameHwnd, out var rc)) return;
        int gw = rc.Right - rc.Left;
        int gh = rc.Bottom - rc.Top;

        // Defensive: ensure non-zero form size.
        int fw = Math.Max(f.Width, 1);
        int fh = Math.Max(f.Height, 1);

        // Compute target position.
        int x = Math.Max(0, (gw - fw) / 2);
        int y = Math.Max(0, (gh - fh) / 2);

        if (asChild)
        {
            // Child coordinates are already in the parent's client space.
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(x, y);
        }
        else
        {
            // Owned top-level: Convert game client origin to screen coordinates.
            var tl = new POINT { X = rc.Left, Y = rc.Top };
            ClientToScreen(_gameHwnd, ref tl);

            int sx = tl.X + x;
            int sy = tl.Y + y;

            // Move without resizing or activating.
            SetWindowPos(f.Handle, IntPtr.Zero, sx, sy, 0, 0,
                         SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }
    #endregion
}