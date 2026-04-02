/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ImGuiNET;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Minimal Dear ImGui renderer for XNA/MonoGame-era graphics (x86, .NET Framework 4 Client).
    ///
    /// CONTEXT / WHY THE EXTRA CARE:
    /// - We are embedding a modern Dear ImGui (ImGui.NET 1.91.x) into a legacy XNA pipeline.
    /// - Modern ImGui emits draw commands that rely on per-command base-vertex/index offsets.
    ///   => Our backend must advertise and handle those offsets or children/tables/listboxes will break.
    /// - XNA HiDef caps a single DrawIndexedPrimitives at 1,048,575 triangles, so we chunk large draws.
    /// - Texture binding in ImGui uses opaque TextureIds, not Texture2D.GetHashCode().
    ///   => We keep a small ID->Texture map and give ImGui the real id (SetTexID).
    ///
    /// REQUIRED INITIALIZATION (done outside this file, right after ImGui.CreateContext()):
    ///   ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
    ///   // (Optional but recommended for keyboard navigation)
    ///   // ImGui.GetIO().ConfigFlags  |= ImGuiConfigFlags.NavEnableKeyboard;
    ///
    /// COORDINATE/CLIP COMBO (follow this exact trio per frame):
    ///   1) dd.ScaleClipRects(io.DisplayFramebufferScale);                          // ImGui clip rects -> framebuffer space.
    ///   2) _gd.Viewport = (0,0,fbW,fbH);                                           // viewport in framebuffer pixels.
    ///   3) Projection = OrthoOffCenter(0, DisplaySize.X, DisplaySize.Y, 0, -1, 1); // ImGui space (origin: top-left).
    ///
    /// ALPHA:
    ///   BlendState.NonPremultiplied (ImGui font atlas is straight RGBA).
    ///
    /// SAMPLER:
    ///   LinearClamp (avoids wrap artifacts near scissor edges).
    ///
    /// INDICES:
    ///   We assume Dear ImGui is compiled with 16-bit indices (default).
    ///   If you ever ship a cimgui build with 32-bit indices, change DynamicIndexBuffer to ThirtyTwoBits
    ///   and the staging array to uint[] accordingly.
    ///
    /// INPUT:
    ///   - XNA (Framework) lacks a reliable GameWindow.TextInput event. We synthesize text from Keys
    ///     (US layout only) inside UpdateInput(). If you later target MonoGame/FNA, you can switch to
    ///     their TextInput events and remove the synth.
    ///   - Make sure only ONE place feeds ImGui input per frame (this class's UpdateInput()) to avoid
    ///     double events.
    ///
    /// ROBUSTNESS:
    ///   - Sanity-check every ImDrawCmd against its list buffers; skip invalid commands safely.
    ///   - Chunk ElemCount to satisfy HiDef primitive limits.
    ///   - Fallback path draws the whole list if every command looked invalid (debug visibility).
    ///
    /// TEXTURES:
    ///   - Use BindTexture(Texture2D) to obtain an IntPtr TextureId for ImGui.Image()/ImageButton().
    ///   - The font atlas is also bound this way (io.Fonts.SetTexID(_fontId)).
    /// </summary>
    internal sealed class ImGuiXnaRenderer : IDisposable
    {
        #region Fields & State

        // NOTE: Static fields allow this renderer to be driven from Harmony patches / global hooks
        // in older XNA-based games that don't give you a clean instance lifetime.
        private static GraphicsDevice      _gd;
        private static BasicEffect         _fx;
        private static RasterizerState     _rsScissorOn;
        private static RasterizerState     _rsScissorOff;

        private static Texture2D           _font;                         // ImGui font atlas Texture2D.
        private static DynamicVertexBuffer _vb;                           // Staging VB (grows as needed).
        private static DynamicIndexBuffer  _ib;                           // Staging IB (16-bit indices expected).
        private static int                 _vbCap, _ibCap;                // Current capacities for staging buffers.

        public static bool DisableScissor { get; set; }          = false; // Keep scissor ON by default; ImGui relies on clipping heavily.

        // --- TextureId map (modern ImGui requires real ids; DO NOT use GetHashCode) ---
        // ImGui will pass back TextureId in draw commands; we look up the Texture2D here.
        private static readonly Dictionary<IntPtr, Texture2D> _textures  = new Dictionary<IntPtr, Texture2D>();
        private int                        _nextTexId            = 1;     // 0 is reserved by ImGui.
        private IntPtr                     _fontId;                       // TextureId for the font atlas.

        // === Frame & Overlay state ===
        private static bool                _frameBegun;                   // Prevent multiple NewFrame() in a single tick.
        public static bool                 Visible { get; set; } = true;  // Toggle with _toggleKey edge inside UpdateInput.

        // === External UI visibility hooks ===
        public static Func<bool> HasForceVisiblePanels { get; set; }
            // Optional callback: Host can report if any "force-visible" ImGui panels are active.
            // Example use: Small pinned debug HUDs that should render even when Visible == false.

        // If you need to query "should the game eat input?": use WantsCapture.
        // It mirrors official backend behavior (only valid when Visible).
        public static bool WantsCapture
        {
            get
            {
                if (!_inited || !Visible) return false;
                var io = ImGui.GetIO();
                return io.WantCaptureKeyboard || io.WantCaptureMouse;
            }
        }

        // Mouse wheel accumulator (XNA gives absolute wheel value).
        private static int                 _scrollWheelValue;

        // Keyboard tracking for edge detection + text synth (for InputText).
        private static KeyboardState       _prevKeyboard;
        private static readonly Keys[]     _allKeys    = (Keys[])Enum.GetValues(typeof(Keys));

        // Simple edge tracker for _toggleKey overlay toggle (press -> toggle).
        public static  Keys                _toggleKey  = Keys.F10;
        public enum   Edge                 { Up, Down }
        public static Edge                 _toggleEdge = Edge.Up;
        public static void SetToggleKey(Keys key) =>
                                           _toggleKey = key;

        // Init guard and back-reference (useful if you later split responsibilities).
        private static bool                _inited;
        private static ImGuiXnaRenderer    _renderer;

        #endregion Fields & State

        #region Toggle UI

        public static void ToggleImGui()
        {
            // NOTE: Edge-trigger visibility toggle. If we're mid-frame and hiding, we end the frame
            // safely to avoid leaving ImGui in an inconsistent state (important when patched into
            // existing engines).
            if (!_inited) return;

            var ks = Keyboard.GetState();
            var now = ks.IsKeyDown(_toggleKey) ? Edge.Down : Edge.Up;
            if (_toggleEdge == Edge.Up && now == Edge.Down)
            {
                bool suppressToggle = false;
                try
                {
                    // Only suppress while overlay is visible and ImGui is actively using the keyboard.
                    if (Visible && ImGui.GetCurrentContext() != IntPtr.Zero)
                    {
                        var io = ImGui.GetIO();
                        if (io.WantCaptureKeyboard) suppressToggle = true;
                    }
                }
                catch { }

                if (!suppressToggle)
                {
                    // If we are about to HIDE and a frame was already begun this tick, end it.
                    if (Visible && _frameBegun)
                    {
                        try { ImGui.EndFrame(); } catch { /* ignore */ }
                        _frameBegun = false;
                    }
                    Visible = !Visible;
                    ModLoader.LogSystem.Log("[ImGui] Visible: " + (Visible ? "ON" : "OFF"));
                }
            }
            _toggleEdge = now;
        }
        #endregion Toggle UI

        #region ImGuiRenderer

        public ImGuiXnaRenderer(GraphicsDevice gd)
        {
            // NOTE: we expect EnsureInit() to be called externally to create the context,
            // set BackendFlags, and call RebuildFontAtlas().
            _gd = gd ?? throw new ArgumentNullException(nameof(gd));

            _fx = new BasicEffect(_gd)
            {
                VertexColorEnabled = true,
                TextureEnabled = true
            };

            // Two rasterizer states so we can toggle scissor cheaply.
            _rsScissorOn = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
            _rsScissorOff = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = false };
        }

        // Build font atlas and register a real TextureId for ImGui to use.
        // NOTE: This must be called after ImGui.CreateContext(). We use RGBA32 (straight alpha).
        public unsafe bool RebuildFontAtlas()
        {
            var io = ImGui.GetIO();
            io.Fonts.Clear();
            io.Fonts.AddFontDefault(); // Add your custom fonts here if desired.

            io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int w, out int h, out int bpp);
            if (pixels == null || w <= 0 || h <= 0 || bpp <= 0) return false;

            var data = new byte[w * h * bpp];
            Marshal.Copy(new IntPtr(pixels), data, 0, data.Length);

            _font?.Dispose();
            _font = new Texture2D(_gd, w, h, false, SurfaceFormat.Color);
            _font.SetData(data);

            // IMPORTANT: Give ImGui a real TextureId from our map (not a hash code).
            if (_fontId != IntPtr.Zero) UnbindTexture(_fontId);
            _fontId = BindTexture(_font);
            io.Fonts.SetTexID(_fontId);
            io.Fonts.ClearTexData(); // Release CPU-side font data.

            return true;
        }

        public IntPtr BindTexture(Texture2D tex)
        {
            // Provide a stable, opaque handle for ImGui's TextureId.
            // Use this for any Texture2D you wish to draw with ImGui.Image(), etc.
            var id = new IntPtr(_nextTexId++);
            _textures[id] = tex;
            return id;
        }
        public void UnbindTexture(IntPtr id) => _textures.Remove(id);

        /// <summary>
        /// Vertex format used for XNA: Position (float3) + Color (RGBA8) + TexCoord (float2)
        /// NOTE:
        /// - [StructLayout(Pack=1)] ensures the managed struct is 24 bytes to match the VertexDeclaration.
        /// - If you change fields, also update VertexDeclaration byte offsets.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Vpct
        {
            public Vector3 Pos; // 12.
            public Color   Col; //  4.
            public Vector2 Tex; //  8 -> 24 bytes total.

            public static readonly VertexDeclaration Decl = new VertexDeclaration(
                new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Color,   VertexElementUsage.Color,    0),
                new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
            );
        }

        /// <summary>
        /// Call at the start of your frame. This pushes XNA input into ImGui and calls ImGui.NewFrame().
        /// Mirrors what your ImGuiHud.NewFrame(...) used to do.
        /// </summary>
        public static void BeforeLayout(GameTime gameTime)
        {
            // NOTE: Only Begin a frame if any ImGui surface wants to draw and we haven't begun already this tick.
            bool wantsAnyUi = Visible || (HasForceVisiblePanels?.Invoke() == true);
            if (!wantsAnyUi || _frameBegun)
                return;

            var io = ImGui.GetIO();

            // Use the CURRENT VIEWPORT, not the backbuffer.
            // NOTE: The viewport always matches the currently bound render target.
            // This stays correct during off-screen passes, window resizes, and letterboxing.
            var vp = _gd.Viewport;
            io.DisplaySize = new System.Numerics.Vector2(vp.Width, vp.Height);
            io.DisplayFramebufferScale = System.Numerics.Vector2.One; // XNA is pixel-backed; leave at (1,1).

            /*
            // Legacy fallback - ONLY when drawing directly to the backbuffer.
            // NOTE: Guard it so it does not size ImGui against the backbuffer while an RT is bound.
            var rts = _gd.GetRenderTargets();
            if (rts == null || rts.Length == 0)
            {
                var pp = _gd.PresentationParameters;
                io.DisplaySize = new System.Numerics.Vector2(pp.BackBufferWidth, pp.BackBufferHeight);
                io.DisplayFramebufferScale = System.Numerics.Vector2.One;
            }
            */

            // Clamp DT here (e.g., after alt-tab, breakpoints, minimiMob, etc.).
            float dt = (float)(gameTime?.ElapsedGameTime.TotalSeconds ?? 1f / 60f);
            if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;   // clamp huge/zero dt
            io.DeltaTime = dt;

            UpdateInput(); // Mouse, keys, wheel, text synth, _toggleKey edge toggle.

            ImGui.NewFrame();
            _frameBegun = true;
        }

        public static void DrawUi()
        {
            // NOTE: Your app/game's UI build goes here. We mirror prior usage where
            // ImGuiMainUI draws all widgets and uses ImGuiDataFeed to refresh data.

            // If neither the main overlay nor any force-visible panels want drawing,
            // skip building widgets completely.
            bool wantsMainOverlay    = Visible;
            bool wantsForceVisibleUi = HasForceVisiblePanels?.Invoke() == true;

            if (!wantsMainOverlay && !wantsForceVisibleUi)
                return;

            // Optional: Keep the window open/closed in sync with the overlay.
            // IGMainUI.IsOpen = true;

            // Draw the full UI (uses the active frame begun in NewFrame).
            IGMainUI.SetPlayers(IGMainUI.ImGuiDataFeed.GetPlayers());
            IGMainUI.Draw();
        }

        /// <summary>
        /// Call after you've built all ImGui widgets. This calls ImGui.Render() and draws via RenderDrawData().
        /// </summary>
        public static unsafe void AfterLayout()
        {
            // NOTE: Guard to ensure balanced NewFrame/Render per tick and visibility.
            if (!_frameBegun /*|| !Visible*/) return;

            ImGui.Render();
            _frameBegun = false;

            var dd = ImGui.GetDrawData();
            if ((IntPtr)dd.NativePtr == IntPtr.Zero || dd.TotalVtxCount <= 0 || dd.TotalIdxCount <= 0)
                return;

            RenderDrawData(dd);
        }

        /// <summary>
        /// Safely terminates the current ImGui frame if one was started.
        /// Use when you early-out (e.g., overlay hidden/minimiMob) so ImGui isn't left mid-frame.
        /// No-ops if no frame is active; Exceptions from EndFrame() are swallowed to avoid UI crashes.
        /// </summary>
        public static void CancelFrame()
        {
            if (_frameBegun)
            {
                try { ImGui.EndFrame(); } catch { }
                _frameBegun = false;
            }
        }
        #endregion ImGuiRenderer

        #region Setup & Update

        /// <summary>
        /// Pushes XNA input into ImGui IO (mouse, wheel, buttons, keys, text).
        /// Also handles the _toggleKey edge toggle for overlay visibility.
        /// </summary>
        public static void UpdateInput()
        {
            // NOTE: This should be the ONLY place that posts input to ImGui each frame.
            // Avoid feeding input from multiple systems to prevent duplicate events.
            var io = ImGui.GetIO();

            // Slow the auto-repeat:
            // Default: KeyRepeatDelay ≈ 0.275f, KeyRepeatRate ≈ 0.050f (20/sec).
            io.KeyRepeatDelay               = 0.50f; // Wait 0.5s before repeating.
            io.KeyRepeatRate                = 0.50f; // One repeat every 500ms (~2/sec).

            // Smooth out event bursts at very high FPS:
            // Usually true by default in recent ImGui.
            io.ConfigInputTrickleEventQueue = true;

            // --- Mouse ---
            var ms = Mouse.GetState();
            io.AddMousePosEvent(ms.X, ms.Y);
            io.AddMouseButtonEvent(0, ms.LeftButton   == ButtonState.Pressed);
            io.AddMouseButtonEvent(1, ms.RightButton  == ButtonState.Pressed);
            io.AddMouseButtonEvent(2, ms.MiddleButton == ButtonState.Pressed);
            io.AddMouseButtonEvent(3, ms.XButton1     == ButtonState.Pressed);
            io.AddMouseButtonEvent(4, ms.XButton2     == ButtonState.Pressed);

            // Wheel: XNA provides absolute count; ImGui expects delta "lines".
            io.AddMouseWheelEvent(0f, (ms.ScrollWheelValue - _scrollWheelValue) / 120f);
            _scrollWheelValue = ms.ScrollWheelValue;

            // --- Keys (chords & nav) ---
            var ks = Keyboard.GetState();
            foreach (var key in _allKeys)
                if (TryMapKeys(key, out ImGuiKey ig)) io.AddKeyEvent(ig, ks.IsKeyDown(key));

            // Post the aggregate modifier states (makes Ctrl+V, Ctrl+C, etc. reliable).
            bool ctrl  = ks.IsKeyDown(Keys.LeftControl) || ks.IsKeyDown(Keys.RightControl);
            bool shift = ks.IsKeyDown(Keys.LeftShift)   || ks.IsKeyDown(Keys.RightShift);
            bool alt   = ks.IsKeyDown(Keys.LeftAlt)     || ks.IsKeyDown(Keys.RightAlt);
            bool super = ks.IsKeyDown(Keys.LeftWindows) || ks.IsKeyDown(Keys.RightWindows);

            io.AddKeyEvent(ImGuiKey.ModCtrl,  ctrl);
            io.AddKeyEvent(ImGuiKey.ModShift, shift);
            io.AddKeyEvent(ImGuiKey.ModAlt,   alt);
            io.AddKeyEvent(ImGuiKey.ModSuper, super);

            // --- Text input synth (US layout) ---
            // Because XNA lacks a reliable TextInput event, we synthesize ASCII chars on new key presses.
            var prev     = _prevKeyboard;
            var nowKeys  = ks.GetPressedKeys();
            var prevKeys = new HashSet<Keys>(prev.GetPressedKeys());

            for (int i = 0; i < nowKeys.Length; i++)
            {
                var k = nowKeys[i];

                // Only on "just pressed".
                if (prevKeys.Contains(k))
                    continue;

                // Ignore modifiers as text input.
                if (k == Keys.LeftControl || k == Keys.RightControl ||
                    k == Keys.LeftAlt     || k == Keys.RightAlt     ||
                    k == Keys.LeftWindows || k == Keys.RightWindows ||
                    k == Keys.LeftShift   || k == Keys.RightShift)
                    continue;

                // Only feed text when no shortcut modifier is down.
                if (!ctrl && !alt && !super && TryKeyToChar(k, shift, out char ch))
                    io.AddInputCharacter(ch);
            }

            _prevKeyboard = ks;
        }

        // XNA (legacy) has no reliable GameWindow.TextInput. We synthesize ASCII for US layout.
        // If you target MonoGame/FNA later, prefer their TextInput events (will support IME etc.).
        public static bool TryKeyToChar(Keys key, bool shift, out char ch)
        {
            ch = '\0';

            if (key >= Keys.A && key <= Keys.Z)
            { char baseChar = (char)('a' + (key - Keys.A)); ch = shift ? char.ToUpperInvariant(baseChar) : baseChar; return true; }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                int n = key - Keys.D0;
                if (!shift) { ch = (char)('0' + n); return true; }
                ch = ")!@#$%^&*("[n]; return true; // US row symbols.
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) { ch = (char)('0' + (key - Keys.NumPad0)); return true; }
            if (key == Keys.Decimal)  { ch = '.'; return true; }
            if (key == Keys.Add)      { ch = '+'; return true; }
            if (key == Keys.Subtract) { ch = '-'; return true; }
            if (key == Keys.Multiply) { ch = '*'; return true; }
            if (key == Keys.Divide)   { ch = '/'; return true; }

            switch (key)
            {
                case Keys.Space:            ch = ' ';                return true;
                case Keys.OemSemicolon:     ch = shift ? ':' : ';';  return true;
                case Keys.OemPlus:          ch = shift ? '+' : '=';  return true;
                case Keys.OemComma:         ch = shift ? '<' : ',';  return true;
                case Keys.OemMinus:         ch = shift ? '_' : '-';  return true;
                case Keys.OemPeriod:        ch = shift ? '>' : '.';  return true;
                case Keys.OemQuestion:      ch = shift ? '?' : '/';  return true;
                case Keys.OemTilde:         ch = shift ? '~' : '`';  return true;
                case Keys.OemOpenBrackets:  ch = shift ? '{' : '[';  return true;
                case Keys.OemCloseBrackets: ch = shift ? '}' : ']';  return true;
                case Keys.OemPipe:          ch = shift ? '|' : '\\'; return true;
                case Keys.OemQuotes:        ch = shift ? '"' : '\''; return true;
            }
            return false;
        }

        // Key -> ImGuiKey mapping for navigation/shortcuts. Note we also post Mod in UpdateInput().
        private static bool TryMapKeys(Keys key, out ImGuiKey imguikey)
        {
            if (key == Keys.None) { imguikey = ImGuiKey.None; return true; }

            if (key >= Keys.D0 && key <= Keys.D9)           { imguikey = ImGuiKey._0 + (key - Keys.D0); return true; }
            if (key >= Keys.A && key <= Keys.Z)             { imguikey = ImGuiKey.A + (key - Keys.A);   return true; }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) { imguikey = ImGuiKey.Keypad0 + (key - Keys.NumPad0); return true; }
            if (key >= Keys.F1 && key <= Keys.F24)          { imguikey = ImGuiKey.F1 + (key - Keys.F1); return true; }

            switch (key)
            {
                case Keys.Back:             imguikey = ImGuiKey.Backspace;      return true;
                case Keys.Tab:              imguikey = ImGuiKey.Tab;            return true;
                case Keys.Enter:            imguikey = ImGuiKey.Enter;          return true;
                case Keys.CapsLock:         imguikey = ImGuiKey.CapsLock;       return true;
                case Keys.Escape:           imguikey = ImGuiKey.Escape;         return true;
                case Keys.Space:            imguikey = ImGuiKey.Space;          return true;
                case Keys.PageUp:           imguikey = ImGuiKey.PageUp;         return true;
                case Keys.PageDown:         imguikey = ImGuiKey.PageDown;       return true;
                case Keys.End:              imguikey = ImGuiKey.End;            return true;
                case Keys.Home:             imguikey = ImGuiKey.Home;           return true;
                case Keys.Left:             imguikey = ImGuiKey.LeftArrow;      return true;
                case Keys.Right:            imguikey = ImGuiKey.RightArrow;     return true;
                case Keys.Up:               imguikey = ImGuiKey.UpArrow;        return true;
                case Keys.Down:             imguikey = ImGuiKey.DownArrow;      return true;
                case Keys.PrintScreen:      imguikey = ImGuiKey.PrintScreen;    return true;
                case Keys.Insert:           imguikey = ImGuiKey.Insert;         return true;
                case Keys.Delete:           imguikey = ImGuiKey.Delete;         return true;

                case Keys.Multiply:         imguikey = ImGuiKey.KeypadMultiply; return true;
                case Keys.Add:              imguikey = ImGuiKey.KeypadAdd;      return true;
                case Keys.Subtract:         imguikey = ImGuiKey.KeypadSubtract; return true;
                case Keys.Decimal:          imguikey = ImGuiKey.KeypadDecimal;  return true;
                case Keys.Divide:           imguikey = ImGuiKey.KeypadDivide;   return true;

                case Keys.NumLock:          imguikey = ImGuiKey.NumLock;        return true;
                case Keys.Scroll:           imguikey = ImGuiKey.ScrollLock;     return true;

                // Modifiers -> Mod (what ImGui expects for shortcut chord evaluation)
                // NOTE: We also post Left/Right variants (and aggregate Mod) in UpdateInput() if desired.
                case Keys.LeftShift:        imguikey = ImGuiKey.LeftShift;      return true;
                case Keys.RightShift:       imguikey = ImGuiKey.RightShift;     return true;
                case Keys.LeftControl:      imguikey = ImGuiKey.LeftCtrl;       return true;
                case Keys.RightControl:     imguikey = ImGuiKey.RightCtrl;      return true;
                case Keys.LeftAlt:          imguikey = ImGuiKey.LeftAlt;        return true;
                case Keys.RightAlt:         imguikey = ImGuiKey.RightAlt;       return true;

                case Keys.OemSemicolon:     imguikey = ImGuiKey.Semicolon;      return true;
                case Keys.OemPlus:          imguikey = ImGuiKey.Equal;          return true;
                case Keys.OemComma:         imguikey = ImGuiKey.Comma;          return true;
                case Keys.OemMinus:         imguikey = ImGuiKey.Minus;          return true;
                case Keys.OemPeriod:        imguikey = ImGuiKey.Period;         return true;
                case Keys.OemQuestion:      imguikey = ImGuiKey.Slash;          return true;
                case Keys.OemTilde:         imguikey = ImGuiKey.GraveAccent;    return true;
                case Keys.OemOpenBrackets:  imguikey = ImGuiKey.LeftBracket;    return true;
                case Keys.OemCloseBrackets: imguikey = ImGuiKey.RightBracket;   return true;
                case Keys.OemPipe:          imguikey = ImGuiKey.Backslash;      return true;
                case Keys.OemQuotes:        imguikey = ImGuiKey.Apostrophe;     return true;

                case Keys.BrowserBack:      imguikey = ImGuiKey.AppBack;        return true;
                case Keys.BrowserForward:   imguikey = ImGuiKey.AppForward;     return true;
            }

            imguikey = ImGuiKey.None;
            return false;
        }

        // One-time ImGui context init and font atlas creation. Call this before using the renderer.
        public static bool EnsureInit(GraphicsDevice gd)
        {
            if (_inited) return true;
            if (gd == null) return false;

            _gd = gd;
            ImGui.CreateContext(); // Create a new Dear ImGui context.

            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset; // Critical for modern ImGui draw lists.

            ImGui.StyleColorsDark(); // Default style; customize later if desired.

            _renderer = new ImGuiXnaRenderer(_gd);
            if (!_renderer.RebuildFontAtlas())
            {
                ModLoader.LogSystem.Log("[ImGui] Font atlas build failed; overlay disabled.");
                return false;
            }

            _inited = true;
            ModLoader.LogSystem.Log("[ImGui] Initialized (HUD).");
            return true;
        }
        #endregion

        #region Internals

        public static unsafe void RenderDrawData(ImDrawDataPtr dd)
        {
            // NOTE: This is the heart of the renderer: set state, upload consolidated VB/IB,
            // iterate draw lists, obey scissor & texture, chunk for HiDef caps, and restore state.

            var io = ImGui.GetIO();
            if (dd.CmdListsCount == 0 || dd.TotalVtxCount <= 0 || dd.TotalIdxCount <= 0) return;

            var rts = _gd.GetRenderTargets();
            if (rts != null && rts.Length > 0) return; // Never draw onto transient RTs.

            int fbW = (int)(io.DisplaySize.X * io.DisplayFramebufferScale.X);
            int fbH = (int)(io.DisplaySize.Y * io.DisplayFramebufferScale.Y);
            if (fbW <= 0 || fbH <= 0) return;

            // Ortho for ImGui coordinates (origin top-left).
            _fx.World      = Matrix.Identity;
            _fx.View       = Matrix.Identity;
            _fx.Projection = Matrix.CreateOrthographicOffCenter(
                0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);

            // --- Save state (XNA has lots of sticky global state; always restore) ---
            var prevBlend     = _gd.BlendState;
            var prevDepth     = _gd.DepthStencilState;
            var prevRaster    = _gd.RasterizerState;
            var prevScissor   = _gd.ScissorRectangle;
            var prevVP        = _gd.Viewport;
            var prevSampler0  = _gd.SamplerStates[0];

            // Clip rects are expressed in framebuffer coordinates; scale once (retina/DPI-safe).
            dd.ScaleClipRects(io.DisplayFramebufferScale);

            // --- Set render state for ImGui ---
            // _gd.Viewport       = new Viewport(0, 0, fbW, fbH);
            var vp                = _gd.Viewport;                // Current, valid for the bound target.
            if (vp.Width <= 0 || vp.Height <= 0) return;         // Protect projection/viewport.
            _gd.Viewport          = new Viewport(0, 0, vp.Width, vp.Height);
            _gd.BlendState        = BlendState.NonPremultiplied; // ImGui uses straight alpha.
            _gd.DepthStencilState = DepthStencilState.None;
            _gd.RasterizerState   = DisableScissor ? _rsScissorOff : _rsScissorOn;
            _gd.SamplerStates[0]  = SamplerState.LinearClamp;

            // Ensure buffers big enough (Dynamic* so we can Discard-update each frame).
            int vCount = dd.TotalVtxCount;
            int iCount = dd.TotalIdxCount;

            if (_vb == null || _vbCap < vCount)
            {
                _vb?.Dispose();
                _vbCap = vCount + 5000;  // Small growth slack.
                _vb = new DynamicVertexBuffer(_gd, Vpct.Decl, _vbCap, BufferUsage.WriteOnly);
            }
            if (_ib == null || _ibCap < iCount)
            {
                _ib?.Dispose();
                _ibCap = iCount + 10000; // Small growth slack.
                _ib = new DynamicIndexBuffer(_gd, IndexElementSize.SixteenBits, _ibCap, BufferUsage.WriteOnly);
            }

            // Stage arrays (convert ImDrawVert -> our Vpct layout).
            // We consolidate all lists into single VB/IB to minimize binds.
            var vtx = new Vpct[vCount];
            var idx = new ushort[iCount];

            int vOfs = 0, iOfs = 0;
            for (int n = 0; n < dd.CmdListsCount; n++)
            {
                var cl = dd.CmdLists[n];

                for (int i = 0; i < cl.VtxBuffer.Size; i++)
                {
                    var v = cl.VtxBuffer[i];
                    // ImGui packs color as ABGR in a uint; We unpack to XNA Color (RGBA8).
                    byte a = (byte)((v.col >> 24) & 0xFF);
                    byte b = (byte)((v.col >> 16) & 0xFF);
                    byte g = (byte)((v.col >> 8)  & 0xFF);
                    byte r = (byte)((v.col)       & 0xFF);

                    vtx[vOfs + i].Pos = new Vector3(v.pos.X, v.pos.Y, 0);
                    vtx[vOfs + i].Tex = new Vector2(v.uv.X, v.uv.Y);
                    vtx[vOfs + i].Col = new Color(r, g, b, a);
                }
                for (int i = 0; i < cl.IdxBuffer.Size; i++)
                    idx[iOfs + i] = cl.IdxBuffer[i];

                vOfs += cl.VtxBuffer.Size;
                iOfs += cl.IdxBuffer.Size;
            }

            // Upload to GPU.
            _gd.SetVertexBuffer(null);
            _gd.Indices = null;
            _vb.SetData(vtx, 0, vtx.Length, SetDataOptions.Discard);
            _ib.SetData(idx, 0, idx.Length, SetDataOptions.Discard);

            _gd.SetVertexBuffer(_vb);
            _gd.Indices = _ib;

            // Per-command rendering with offsets (modern ImGui draw path).
            int globalVtxOffset = 0;
            int globalIdxOffset = 0;

            const int MAX_PRIMS_PER_CALL = 1_048_575;              // XNA HiDef hard limit.
            const int MAX_ELEMS_PER_CALL = MAX_PRIMS_PER_CALL * 3; // Indices (3 per triangle).

            for (int n = 0; n < dd.CmdListsCount; n++)
            {
                var cl = dd.CmdLists[n];
                int listVtxCount = cl.VtxBuffer.Size;
                int listIdxCount = cl.IdxBuffer.Size;

                bool anyDrawn = false; // If nothing valid drew, we try a fallback for visibility.
                bool anyBad   = false;

                for (int ci = 0; ci < cl.CmdBuffer.Size; ci++)
                {
                    var cmd    = cl.CmdBuffer[ci];
                    uint elem  = cmd.ElemCount;
                    uint vtxOff= cmd.VtxOffset;
                    uint idxOff= cmd.IdxOffset;

                    // HARD SANITY: The command must reference ranges inside this list's buffers.
                    bool bad =
                        (elem == 0) ||
                        (vtxOff > (uint)listVtxCount) ||
                        (idxOff > (uint)listIdxCount) ||
                        (elem  > (uint)(listIdxCount - idxOff));

                    if (bad)
                    {
                        anyBad = true;
                        // Uncomment for troubleshooting:
                        // ModLoader.LogSystem.Log($"[ImGui] BAD CMD: elem={elem} vtxOff={vtxOff} idxOff={idxOff} listVtx={listVtxCount} listIdx={listIdxCount} n={n} ci={ci}");
                        continue;
                    }

                    // Scissor (ImGui gives framebuffer coords; we already scaled clip rects).
                    var cr = cmd.ClipRect;
                    var rect = new Rectangle((int)cr.X, (int)cr.Y, (int)(cr.Z - cr.X), (int)(cr.W - cr.Y));

                    // Clamp to the ACTUAL current viewport (handles letterboxing, RTs with offsets, etc.).
                    rect = ClampToViewport(_gd, rect);
                    if (rect.Width <= 0 || rect.Height <= 0) { anyDrawn = true; continue; }
                    _gd.ScissorRectangle = rect;

                    // Texture for this command (fallback to font if missing).
                    if (!_textures.TryGetValue(cmd.TextureId, out Texture2D tex)) tex = _font;
                    if (tex == null) { anyDrawn = true; continue; }
                    _fx.Texture = tex;

                    // Draw in chunks (satisfy XNA HiDef primitive cap).
                    int baseVertex = globalVtxOffset + (int)vtxOff;
                    int idxCursor  = globalIdxOffset + (int)idxOff;
                    int remaining  = (int)elem;

                    while (remaining > 0)
                    {
                        int drawElems = Math.Min(remaining, MAX_ELEMS_PER_CALL);
                        int primCount = drawElems / 3;
                        if (primCount <= 0) break;

                        foreach (var pass in _fx.CurrentTechnique.Passes)
                        {
                            try
                            {
                                pass.Apply();
                                _gd.DrawIndexedPrimitives(
                                    PrimitiveType.TriangleList,
                                    baseVertex,
                                    0,
                                    listVtxCount,
                                    idxCursor,
                                    primCount
                                );
                            } catch { }
                        }

                        idxCursor  += drawElems;
                        remaining  -= drawElems;
                        anyDrawn    = true;
                    }
                }

                // Fallback: if every command in this list was "bad", draw the whole list.
                // Not perfect (no per-cmd texture/scissor), but ensures visible UI during debugging.
                if (!anyDrawn && anyBad && listIdxCount >= 3)
                {
                    _gd.ScissorRectangle = ClampToViewport(_gd, new Rectangle(0, 0, fbW, fbH));
                    _fx.Texture = _font;
                    int primCount = listIdxCount / 3;

                    foreach (var pass in _fx.CurrentTechnique.Passes)
                    {
                        try
                        {
                            pass.Apply();
                            _gd.DrawIndexedPrimitives(
                                PrimitiveType.TriangleList,
                                globalVtxOffset,
                                0,
                                listVtxCount,
                                globalIdxOffset,
                                primCount
                            );
                        } catch { }
                    }

                    // Uncomment for troubleshooting:
                    // ModLoader.LogSystem.Log($"[ImGui] Fallback drew whole list n={n} prims={primCount}");
                }

                globalVtxOffset += listVtxCount;
                globalIdxOffset += listIdxCount;
            }

            // --- Restore previous XNA state (very important in engine-integrations) ---
            _gd.SetVertexBuffer(null);
            _gd.Indices           = null;

            _gd.Viewport          = prevVP;
            _gd.ScissorRectangle  = prevScissor;
            _gd.BlendState        = prevBlend;
            _gd.DepthStencilState = prevDepth;
            _gd.RasterizerState   = prevRaster;
            if (prevSampler0 != null) _gd.SamplerStates[0] = prevSampler0;
        }

        // NOTE: Clamp a rectangle to the CURRENT viewport bounds (including non-zero X/Y offsets).
        // Use this before setting GraphicsDevice.ScissorRectangle to avoid
        // "scissor rectangle is invalid" when letterboxing, RTs, or other mods change the viewport.
        private static Rectangle ClampToViewport(GraphicsDevice gd, Rectangle r)
        {
            var vp = gd.Viewport;
            int x = Math.Max(vp.X, Math.Min(r.X, vp.X + vp.Width));
            int y = Math.Max(vp.Y, Math.Min(r.Y, vp.Y + vp.Height));
            int maxW = (vp.X + vp.Width) - x;
            int maxH = (vp.Y + vp.Height) - y;
            int w = Math.Max(0, Math.Min(r.Width, maxW));
            int h = Math.Max(0, Math.Min(r.Height, maxH));
            return new Rectangle(x, y, w, h);
        }
        #endregion Internals

        #region Teardown (Dispose/Shutdown)

        // Instance Dispose required by IDisposable.
        public void Dispose()
        {
            Shutdown();                // Prefer centralizing cleanup in one place.
            GC.SuppressFinalize(this); // Standard pattern (even if no finalizer exists).
        }

        // Static cleanup for callers that don't have an instance handy.
        // NOTE: Call this on the same thread you used for ImGui.NewFrame/Render, typically the render thread.
        public static void Shutdown()
        {
            // If a frame was begun but not ended (e.g., shutting down mid-draw), this avoids debug asserts.
            // Safe to ignore failures if no frame is active.
            try { ImGui.EndFrame();         } catch { }

            // Unbind GPU state we may have set so the game's pipeline is left clean.
            if (_gd != null)
            {
                try { _gd.SetVertexBuffer(null); } catch { } // OK: Clears VB binding.
                try { _gd.Indices = null;        } catch { } // OK: Clears IB binding (behind null-check because _gd is static).
            }

            // Dispose GPU stuff individually, null them, and swallow exceptions.
            try { _font?.Dispose();         } catch { } finally { _font = null;           } // ImGui font atlas Texture2D.
            try { _vb?.Dispose();           } catch { } finally { _vb = null; _vbCap = 0; } // Dynamic vertex buffer (staging).
            try { _ib?.Dispose();           } catch { } finally { _ib = null; _ibCap = 0; } // Dynamic index buffer (staging).
            try { _fx?.Dispose();           } catch { } finally { _fx = null;             } // BasicEffect used to draw ImGui.
            try { _rsScissorOn?.Dispose();  } catch { } finally { _rsScissorOn = null;    } // Rasterizer state with scissor ON.
            try { _rsScissorOff?.Dispose(); } catch { } finally { _rsScissorOff = null;   } // Rasterizer state with scissor OFF.

            // Clear TextureId -> Texture2D bindings used by ImGui::Image/etc.
            try { _textures.Clear();        } catch { }

            // Reset runtime flags so a future re-init starts clean.
            _frameBegun                             = false;
            _inited                                 = false;

            // Tear down Dear ImGui context LAST (no more ImGui calls after this).
            try { if (ImGui.GetCurrentContext() != IntPtr.Zero) { ImGui.DestroyContext(); } } catch { }
        }
        #endregion Teardown (Dispose/Shutdown)
    }
}