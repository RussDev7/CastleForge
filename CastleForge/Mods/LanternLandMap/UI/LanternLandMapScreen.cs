/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using System.Globalization;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Input;
using System;

using static LanternLandMap.Integrations.WorldGenPlus.WorldGenPlusBiomePalette;
using static LanternLandMap.Integrations.WorldGenPlus.WorldGenPlusSurfaceRules;
using static LanternLandMap.Integrations.WorldGenPlus.WorldGenPlusContext;
using static LanternLandMap.Integrations.WorldGenPlus.WorldGenPlusMath;
using static ModLoader.LogSystem;

namespace LanternLandMap
{
    /// <summary>
    /// Fullscreen in-game map/overlay screen for Lantern Land.
    ///
    /// Draws a scrollable left-side options panel and a right-side map view that supports:
    /// - Panning/zooming around the world XZ plane (view-centered camera).
    /// - Lantern ring outlines and optional solid ring fills.
    /// - Optional vanilla biome underlay bands (with optional edge emphasis).
    /// - Chunk grid + axes + player marker + label overlays.
    /// - Cursor readout (world X/Z, radius, ring index, wall/gap, biome name).
    ///
    /// Handles all input while open (modal), including hotkeys, slider dragging, textbox edits,
    /// and optional right-click teleport-to-cursor.
    /// </summary>
    internal sealed class LanternLandMapScreen : Screen
    {
        #region Fields

        #region Fields: Layout

        /// <summary>Left settings panel bounds.</summary>
        private Rectangle _panelRect;

        /// <summary>Map view bounds.</summary>
        private Rectangle _mapRect;

        /// <summary>Close button bounds (top-right of panel).</summary>
        private Rectangle _closeRect;

        #endregion

        #region Fields: Runtime / Assets

        /// <summary>Owning game instance.</summary>
        private readonly CastleMinerZGame _game;

        /// <summary>Base UI font used for most measurements and text.</summary>
        private SpriteFont _font;

        /// <summary>Small UI font (preferred for clarity at smaller scales).</summary>
        private SpriteFont _smallFont;

        /// <summary>Large UI font (preferred for clarity at larger scales).</summary>
        private SpriteFont _largeFont;

        /// <summary>Cached font heights to help choose the least-blurry font+scale combination.</summary>
        private float _baseFontHeight, _smallFontHeight, _largeFontHeight;

        /// <summary>1x1 white pixel texture used to draw rectangles/lines.</summary>
        private Texture2D _px;

        /// <summary>One-time init guard.</summary>
        private bool _initialized;

        #endregion

        #region Fields: View / Interaction

        /// <summary>True while left mouse is dragging the map.</summary>
        private bool _panning;

        /// <summary>Mouse position at start of pan.</summary>
        private Point _panStartMouse;

        /// <summary>View center at start of pan.</summary>
        private double _panStartCenterX, _panStartCenterZ;

        /// <summary>Zoom factor in pixels per world block (XZ plane).</summary>
        private float _zoom;

        /// <summary>Scrollable panel clip region (everything below the header). Set in DrawPanel; used for input.</summary>
        private Rectangle _panelScrollClipRect;

        /// <summary>Current view center in world coordinates.</summary>
        private double _viewCenterX, _viewCenterZ;

        /// <summary>Last mouse position (cached each frame for hover UI).</summary>
        private Point _lastMousePos;

        #endregion

        #region Fields: Ring / Tower Cache

        /// <summary>Ring cache key: Starting ring index (n0).</summary>
        private long _cacheN0 = -1;

        /// <summary>Ring cache key: Number of rings (A).</summary>
        private int _cacheA   = -1;

        /// <summary>Cached ring geometry for current (n0, A).</summary>
        private List<RingData> _rings = new List<RingData>();

        /// <summary>Cached visible tower k-values for current (n0, A).</summary>
        private List<long> _towerKs = new List<long>();

        #endregion

        #region Fields: Slider Drag State + Persist-on-Release

        private bool _dragA;
        private bool _dragR;
        private bool _dragLabelFont;
        private bool _dragReadoutFont;
        private bool _dragPanelFont;
        private bool _dragBiomeAlpha;

        /// <summary>Marks that UI changes occurred during drag; flushed on mouse-up.</summary>
        private bool _pendingConfigSave;

        #endregion

        #region Fields: Panel UI Hitboxes + Scroll State

        /// <summary>Panel slider hit boxes (computed during DrawPanel so input matches layout).</summary>
        private Rectangle _panelFontSliderRect;
        private Rectangle _readoutFontSliderRect;
        private Rectangle _labelFontSliderRect;
        private Rectangle _biomeAlphaSliderRect;

        /// <summary>Slider hitboxes for A/R (computed during DrawPanel).</summary>
        private Rectangle _sliderARect;
        private Rectangle _sliderRRect;

        /// <summary>Editable numeric textbox for R (Start Radius).</summary>
        private Rectangle _radiusTextRect;
        private bool      _editRadiusText;
        private string    _radiusText = "";
        private double    _radiusBeforeEdit;

        /// <summary>Used for caret blink in textboxes.</summary>
        private double _uiTimeSeconds;

        /// <summary>Panel scroll (mouse wheel + optional scrollbar drag). Only applies to content below the header.</summary>
        private int       _panelScrollY;
        private int       _panelScrollMax;
        private int       _panelContentHeight;
        private Rectangle _panelScrollTrack;
        private Rectangle _panelScrollThumb;
        private bool      _dragPanelScroll;
        private int       _dragPanelScrollStartMouseY;
        private int       _dragPanelScrollStartScroll;

        /// <summary>Clickable rows (computed during DrawPanel).</summary>
        private Rectangle _rowFillRings;
        private Rectangle _rowLanternRings;
        private Rectangle _rowBiomes;
        private Rectangle _rowBiomeEdges;
        private Rectangle _rowChunkGrid;
        private Rectangle _rowOtherPlayers;
        private Rectangle _rowStart;
        private Rectangle _rowEnd;
        private Rectangle _rowIndex;
        private Rectangle _rowThick;
        private Rectangle _rowGap;
        private Rectangle _rowTowers;

        #endregion

        #region Fields: Primitive Fill (Annulus Triangle Strips)

        /// <summary>Rasterizer with scissor enabled for clipping panel/map passes.</summary>
        private RasterizerState _scissorRaster;

        /// <summary>Effect for drawing vertex-colored triangle strips in screen-space.</summary>
        private BasicEffect _fillEffect;

        /// <summary>Reusable vertex buffer for annulus strips.</summary>
        private VertexPositionColor[] _stripVerts;

        /// <summary>Reusable vertex buffer for random-region edge lines.</summary>
        private VertexPositionColor[] _edgeLineVerts;


        #endregion

        #endregion

        #region Ctor / Screen Lifecycle

        /// <summary>Create the Lantern Land Map screen (modal overlay).</summary>
        public LanternLandMapScreen(CastleMinerZGame game) : base(true, true)
        {
            _game           = game;

            // Let this screen own the mouse (prevents gameplay mouse-center forcing in many CMZ paths).
            ShowMouseCursor = true;
            CaptureMouse    = false;
        }

        /// <summary>Called when pushed onto the UI stack.</summary>
        public override void OnPushed()
        {
            base.OnPushed();
            LanternLandMapState.IsOpen = true;

            // Prevent "stuck movement" immediately.
            try { CastleMinerZGame.Instance?._controllerMapping?.ClearAllControls(); } catch { }

            // Ensure config + runtime defaults exist (don't re-load on every open; it resets live toggles).
            if (!LanternLandMapConfig.IsLoaded)
                LanternLandMapConfig.LoadApply();
            LanternLandMapState.EnsureInitFromConfig();

            // Start centered on the local player.
            CenterOnPlayer();

            // Reset panel scroll to the top each time the screen opens.
            _panelScrollY       = 0;
            _panelScrollMax     = 0;
            _panelContentHeight = 0;
            _panelScrollTrack   = Rectangle.Empty;
            _panelScrollThumb   = Rectangle.Empty;
            _dragPanelScroll    = false;

            _zoom = LanternLandMapConfig.InitialZoomPixelsPerBlock;
            _zoom = MathHelper.Clamp(_zoom, LanternLandMapConfig.ZoomMinPixelsPerBlock, LanternLandMapConfig.ZoomMaxPixelsPerBlock);
        }

        /// <summary>Called when popped off the UI stack.</summary>
        public override void OnPoped()
        {
            base.OnPoped();
            LanternLandMapState.IsOpen = false;

            // Persist any UI changes (sliders/toggles) back to LanternLandMap.ini.
            try { LanternLandMapConfig.SaveFromState(); } catch { }

            // Prevent "stuck movement" immediately.
            try { CastleMinerZGame.Instance?._controllerMapping?.ClearAllControls(); } catch { }

            _panning = false;
            _dragA = _dragR = _dragLabelFont = _dragReadoutFont = _dragPanelFont = _dragBiomeAlpha = false;
            _dragPanelScroll = false;
        }
        #endregion

        #region Draw / Input

        /// <summary>Primary draw entrypoint (panel + map + optional primitives).</summary>
        protected override void OnDraw(GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
        {
            EnsureInit(device);

            _uiTimeSeconds = gameTime.TotalGameTime.TotalSeconds;

            // Layout: Left panel + map view.
            var vp     = device.Viewport;
            int panelW = Math.Min(380, (int)(vp.Width * 0.36f));
            _panelRect = new Rectangle(0, 0, panelW, vp.Height);
            _mapRect   = new Rectangle(panelW, 0, vp.Width - panelW, vp.Height);
            _closeRect = new Rectangle(_panelRect.Right - 30, _panelRect.Top + 8, 22, 22);

            // PASS 1: Background + panel + map background (SpriteBatch).
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

            DrawRect(spriteBatch, new Rectangle(0, 0, vp.Width, vp.Height), LanternLandMapConfig.BackgroundColor);
            DrawRect(spriteBatch, _panelRect, LanternLandMapConfig.PanelColor);
            DrawRect(spriteBatch, _mapRect, new Color(0, 0, 0, 120));

            spriteBatch.End();

            // PASS 1b: Panel content (scissored, so it can scroll without bleeding into the map).
            {
                var prevScissor         = device.ScissorRectangle;
                var prevRaster          = device.RasterizerState;

                device.ScissorRectangle = _panelRect;
                device.RasterizerState  = _scissorRaster;

                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, _scissorRaster);

                DrawPanel(spriteBatch);

                spriteBatch.End();

                device.RasterizerState  = prevRaster;
                device.ScissorRectangle = prevScissor;
            }

            // PASS 2a: Biome underlay (filled bands; no outlines by default).
            if (LanternLandMapState.ShowBiomesUnderlay)
            {
                var oldRast             = device.RasterizerState;
                var oldScis             = device.ScissorRectangle;

                device.ScissorRectangle = _mapRect;
                device.RasterizerState  = _scissorRaster;

                DrawBiomeUnderlayPrimitives(device);

                device.ScissorRectangle = oldScis;
                device.RasterizerState  = oldRast;
            }

            // PASS 2b: Lantern rings filled (optional).
            if (LanternLandMapState.ShowLanternRings && LanternLandMapConfig.FillRingsSolid)
            {
                var prevScissor         = device.ScissorRectangle;
                var prevRaster          = device.RasterizerState;

                device.ScissorRectangle = _mapRect;
                device.RasterizerState  = _scissorRaster;

                DrawRingFillsPrimitives(device);

                device.RasterizerState  = prevRaster;
                device.ScissorRectangle = prevScissor;
            }

            // PASS 3: Map overlays (grid/axes/ring outlines/labels/readout).
            {
                var prevScissor         = device.ScissorRectangle;
                var prevRaster          = device.RasterizerState;

                device.ScissorRectangle = _mapRect;
                device.RasterizerState  = _scissorRaster;

                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, _scissorRaster);

                DrawMap(spriteBatch);

                spriteBatch.End();

                device.RasterizerState  = prevRaster;
                device.ScissorRectangle = prevScissor;
            }

            // PASS 4: Math Read Me modal overlay.
            if (_showMathReadMe)
            {
                DrawMathReadMeOverlayModal(device, spriteBatch);
            }
        }

        /// <summary>Handles panel UI, map panning/zoom, hotkeys, and teleport clicks.</summary>
        protected override bool OnInput(InputManager inputManager, GameTime gameTime)
        {
            // Freeze gameplay controls while the map is open (prevents "stuck W" etc.)
            CastleMinerZGame.Instance._controllerMapping.ClearAllControls();

            _lastMousePos = inputManager.Mouse.Position;

            // Math Read Me overlay is modal: ESC closes it, and it blocks the rest of the UI.
            if (_showMathReadMe)
            {
                HandleMathReadMeInput(inputManager);
                return true;
            }

            // Close: ESC or ToggleMap hotkey (unless we're typing into a textbox).
            if (_editRadiusText)
            {
                if (inputManager.Keyboard.WasKeyPressed(Keys.Escape))
                {
                    CancelRadiusTextEdit();
                    return true;
                }
                // While typing, ignore the map-toggle hotkey so you don't close the screen accidentally.
            }
            else if (inputManager.Keyboard.WasKeyPressed(Keys.Escape) || HotkeyPressed(inputManager, LanternLandMapConfig.ToggleMapKey))
            {
                PopMe();
                return false;
            }

            // Reload config (while map is open).
            if (HotkeyPressed(inputManager, LanternLandMapConfig.ReloadConfigKey))
            {
                LanternLandMapConfig.LoadApply();

                // APPLY to live runtime state so the open screen updates immediately.
                LanternLandMapState.RingsToShow        = LanternLandMapConfig.RingsToShow;
                LanternLandMapState.TargetRadius       = LanternLandMapConfig.TargetRadius;
                LanternLandMapState.ShowChunkGrid      = LanternLandMapConfig.DefaultShowChunkGrid;
                LanternLandMapState.ShowLanternRings   = LanternLandMapConfig.DefaultShowLanternRings;
                LanternLandMapState.ShowBiomesUnderlay = LanternLandMapConfig.DefaultShowBiomesUnderlay;
                LanternLandMapState.ShowBiomeEdges     = LanternLandMapConfig.DefaultShowBiomeEdges;

                // Clamp (in case min/max changed).
                LanternLandMapState.RingsToShow  = Clamp(LanternLandMapState.RingsToShow, LanternLandMapConfig.RingsMin, LanternLandMapConfig.RingsMax);
                LanternLandMapState.TargetRadius = Clamp(LanternLandMapState.TargetRadius, LanternLandMapConfig.RadiusMin, LanternLandMapConfig.RadiusMax);
                _zoom = MathHelper.Clamp(_zoom, LanternLandMapConfig.ZoomMinPixelsPerBlock, LanternLandMapConfig.ZoomMaxPixelsPerBlock);

                // Optional: Force ring cache rebuild next draw.
                _cacheA  = -1;
                _cacheN0 = -1;
            }

            // Hotkey toggles (labels are tri-state cycles).
            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleChunkGridKey))
                LanternLandMapState.ShowChunkGrid = !LanternLandMapState.ShowChunkGrid;

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ResetViewToPlayerKey))
                CenterOnPlayer();

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ResetViewToOriginKey))
            {
                _viewCenterX = 0;
                _viewCenterZ = 0;
            }

            bool hotkeyChanged = false;

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleStartLabelsKey))
            {
                LanternLandMapConfig.StartLabelMode = Next(LanternLandMapConfig.StartLabelMode);
                hotkeyChanged = true;
            }

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleEndLabelsKey))
            {
                LanternLandMapConfig.EndLabelMode = Next(LanternLandMapConfig.EndLabelMode);
                hotkeyChanged = true;
            }

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleIndexKey))
            {
                LanternLandMapConfig.IndexLabelMode = Next(LanternLandMapConfig.IndexLabelMode);
                hotkeyChanged = true;
            }

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleThicknessKey))
            {
                LanternLandMapConfig.ThicknessLabelMode = Next(LanternLandMapConfig.ThicknessLabelMode);
                hotkeyChanged = true;
            }

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleGapKey))
            {
                LanternLandMapConfig.GapLabelMode = Next(LanternLandMapConfig.GapLabelMode);
                hotkeyChanged = true;
            }

            if (HotkeyPressed(inputManager, LanternLandMapConfig.ToggleTowersKey))
            {
                LanternLandMapConfig.TowersLabelMode = Next(LanternLandMapConfig.TowersLabelMode);
                hotkeyChanged = true;
            }

            if (hotkeyChanged)
                SaveConfigSafe(force: true);

            // Close button click.
            if (inputManager.Mouse.LeftButtonPressed && _closeRect.Contains(inputManager.Mouse.Position))
            {
                PopMe();
                return false;
            }

            // Panel UI (sliders + checkboxes). If we're dragging a slider, don't start panning.
            HandlePanelUI(inputManager);

            // Panning / zoom / teleport (map-only).
            if (_mapRect.Contains(inputManager.Mouse.Position))
            {
                // Panning begins only if not dragging sliders.
                if (inputManager.Mouse.LeftButtonPressed && !_dragA && !_dragR && !_dragLabelFont && !_dragReadoutFont && !_dragPanelFont && !_dragBiomeAlpha)
                {
                    _panning         = true;
                    _panStartMouse   = inputManager.Mouse.Position;
                    _panStartCenterX = _viewCenterX;
                    _panStartCenterZ = _viewCenterZ;
                }

                if (_panning && inputManager.Mouse.LeftButtonDown)
                {
                    // Drag screen delta -> World delta.
                    var dx = inputManager.Mouse.Position.X - _panStartMouse.X;
                    var dz = inputManager.Mouse.Position.Y - _panStartMouse.Y;

                    _viewCenterX = _panStartCenterX - (dx / _zoom) * LanternLandMapConfig.PanSpeed;
                    _viewCenterZ = _panStartCenterZ - (dz / _zoom) * LanternLandMapConfig.PanSpeed;
                }

                if (_panning && !inputManager.Mouse.LeftButtonDown)
                    _panning = false;

                // Zoom.
                int dWheel = inputManager.Mouse.DeltaWheel;
                if (dWheel != 0)
                {
                    float zoomMul = dWheel > 0 ? 1.12f : (1f / 1.12f);

                    if (LanternLandMapConfig.ZoomAboutCursor)
                        ZoomAboutCursor(inputManager.Mouse.Position, zoomMul);
                    else
                        SetZoom(_zoom * zoomMul);
                }

                // Teleport: Right click.
                if (LanternLandMapConfig.RightClickTeleport && inputManager.Mouse.RightButtonPressed)
                {
                    if (!LanternLandMapConfig.TeleportRequireShift ||
                        inputManager.Keyboard.IsKeyDown(Keys.LeftShift) || inputManager.Keyboard.IsKeyDown(Keys.RightShift))
                    {
                        ScreenToWorld(inputManager.Mouse.Position, out double wx, out double wz);
                        TeleportTo(wx, wz);
                        return false;
                    }
                }
            }

            // Modal overlay: Block underlying screens.
            return base.OnInput(inputManager, gameTime); // Keep map modal.
        }
        #endregion

        #region Init / Navigation

        /// <summary>One-time asset/init setup for this screen.</summary>
        private void EnsureInit(GraphicsDevice device)
        {
            if (_initialized) return;
            _initialized = true;

            _smallFont = _game._smallFont;
            _largeFont = _game._largeFont;

            // Base font used for most spacing math.
            _font = _smallFont ?? _largeFont;

            // Cache heights for font selection (helps reduce blur when scaling).
            _baseFontHeight  = (_font != null)      ? _font.MeasureString("Ag").Y      : 0f;
            _smallFontHeight = (_smallFont != null) ? _smallFont.MeasureString("Ag").Y : 0f;
            _largeFontHeight = (_largeFont != null) ? _largeFont.MeasureString("Ag").Y : 0f;

            _px = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
            _px.SetData(new[] { Color.White });

            _scissorRaster = new RasterizerState
            {
                ScissorTestEnable = true,
                CullMode = CullMode.None
            };

            _fillEffect = new BasicEffect(device)
            {
                VertexColorEnabled = true,
                TextureEnabled     = false,
                LightingEnabled    = false
            };

            _stripVerts = new VertexPositionColor[0];
        }

        /// <summary>Best-effort pop that supports both UIGroup and ScreenManager paths.</summary>
        private new void PopMe()
        {
            try
            {
                _game?.GameScreen?._uiGroup?.PopScreen();
            }
            catch
            {
                try { _game?.ScreenManager?.PopScreen(); } catch { }
            }
        }

        /// <summary>Centers view on local player position (XZ plane).</summary>
        private void CenterOnPlayer()
        {
            try
            {
                var lp = CastleMinerZGame.Instance?.LocalPlayer;
                if (lp != null)
                {
                    _viewCenterX = lp.LocalPosition.X;
                    _viewCenterZ = lp.LocalPosition.Z;
                }
            }
            catch { }
        }
        #endregion

        #region Panel UI

        /// <summary>Draws the left panel, including a scrollable region and a fixed header.</summary>
        private void DrawPanel(SpriteBatch sb)
        {
            float panelScale  = LanternLandMapConfig.PanelFontScale;
            float headerScale = panelScale * 1.12f;

            int x = _panelRect.Left + 12;
            int y = _panelRect.Top  + 10;

            // -------------------------------------------------------------------------
            // 1) Measure header (we draw it LAST so it stays clean on top).
            // -------------------------------------------------------------------------
            int headerY0 = y;

            // Title line.
            y += LineH(panelScale) + 8;

            // Close / Reload lines.
            float hintScale = panelScale * 0.92f;
            y += LineH(hintScale) + 2;
            y += LineH(hintScale) + 4;

            int scrollTop = y;
            int visibleH  = Math.Max(0, _panelRect.Bottom - scrollTop - 10);

            // Clamp scroll.
            _panelScrollY = Clamp(_panelScrollY, 0, _panelScrollMax);

            // If the panel is too small to show content, just draw the header + close and bail.
            if (visibleH <= 0)
            {
                _panelContentHeight  = 0;
                _panelScrollMax      = 0;
                _panelScrollY        = 0;
                _panelScrollTrack    = Rectangle.Empty;
                _panelScrollThumb    = Rectangle.Empty;
                _panelScrollClipRect = Rectangle.Empty;

                // Dark header cap.
                Rectangle headerRectSmall = new Rectangle(_panelRect.Left, _panelRect.Top, _panelRect.Width, scrollTop - _panelRect.Top);
                DrawRect(sb, headerRectSmall, new Color(0, 0, 0, 200));

                // Draw header text.
                int hy = headerY0;
                DrawText(sb, "Lantern Land Map", x, hy, headerScale, LanternLandMapConfig.PanelTextColor);
                hy += LineH(panelScale) + 8;
                DrawText(sb, "Close: ESC or " + LanternLandMapConfig.ToggleMapKey, x, hy, hintScale, LanternLandMapConfig.PanelTextColor);
                hy += LineH(hintScale) + 2;
                DrawText(sb, "Reload: " + LanternLandMapConfig.ReloadConfigKey, x, hy, hintScale, LanternLandMapConfig.PanelTextColor);

                DrawCloseButton(sb);
                return;
            }

            // -------------------------------------------------------------------------
            // 2) Scissor clip for scrollable region (everything below header).
            // -------------------------------------------------------------------------
            var gd = sb.GraphicsDevice;

            Rectangle clip = new Rectangle(
                _panelRect.Left + 2,
                scrollTop,
                _panelRect.Width - 4,
                visibleH);

            // Must be inside viewport bounds or XNA will throw.
            clip = Rectangle.Intersect(clip, gd.Viewport.Bounds);

            _panelScrollClipRect  = clip;

            Rectangle prevScissor = gd.ScissorRectangle;
            gd.ScissorRectangle   = clip;

            // Start drawing content at scrollTop, then shift up by scroll amount.
            y = scrollTop - _panelScrollY;

            // -------------------------------------------------------------------------
            // 3) SCROLLED CONTENT.
            // -------------------------------------------------------------------------

            // Extra breathing room between header and scroll content.
            y += (int)(5 * panelScale);

            // Sliders (A, R) - Add a separator before the controls.
            // y = DrawSeparator(sb, y, 4, 6);

            // A (rings).
            DrawText(sb, "A (Rings): " + LanternLandMapState.RingsToShow, x, y, hintScale, LanternLandMapConfig.PanelTextColor);
            _sliderARect = new Rectangle(_panelRect.Left + 16, y + LineH(panelScale) + 2, _panelRect.Width - 32, 18);
            DrawSlider(sb, _sliderARect, LanternLandMapState.RingsToShow, LanternLandMapConfig.RingsMin, LanternLandMapConfig.RingsMax);
            y = _sliderARect.Bottom + 14;

            // R (radius).
            DrawText(sb, "R (Start Radius):", x, y, hintScale, LanternLandMapConfig.PanelTextColor);

            int rBoxW       = 110;
            int rBoxH       = Math.Max(18, LineH(hintScale) + 4);
            _radiusTextRect = new Rectangle(x + 170, y - 2, rBoxW, rBoxH);

            string rText    = _editRadiusText ? _radiusText : FormatBig(LanternLandMapState.TargetRadius);
            DrawNumberTextBox(sb, _radiusTextRect, rText, hintScale, _editRadiusText);

            DrawText(sb, "blocks", _radiusTextRect.Right + 8, y, hintScale, LanternLandMapConfig.PanelTextColor);
            _sliderRRect = new Rectangle(_panelRect.Left + 16, y + LineH(panelScale) + 2, _panelRect.Width - 32, 18);
            DrawSliderRadius(sb, _sliderRRect, LanternLandMapState.TargetRadius);
            y = _sliderRRect.Bottom + 12;
            y = DrawSeparator(sb, y, 6, 6);

            // Toggles section.
            DrawText(sb, "Options", x, y, headerScale, LanternLandMapConfig.PanelTextColor);
            y += LineH(panelScale) + 6;

            // Panel font scale.
            DrawText(sb, "Panel Font: " + LanternLandMapConfig.PanelFontScale.ToString("0.00") + "x", x, y, hintScale, LanternLandMapConfig.PanelTextColor);
            y += LineH(hintScale) + 2;

            _panelFontSliderRect = new Rectangle(_panelRect.Left + 16, y, _panelRect.Width - 32, 18);
            DrawSliderFloat(sb, _panelFontSliderRect, LanternLandMapConfig.PanelFontScale, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
            y += 26;

            _rowLanternRings      = DrawBoolRow(sb, x, y, "Lantern Rings", LanternLandMapState.ShowLanternRings, "");
            y += RowH(panelScale);

            _rowFillRings         = DrawBoolRow(sb, x, y, "Fill Rings", LanternLandMapConfig.FillRingsSolid, "");
            y += RowH(panelScale);

            _rowBiomes            = DrawBoolRow(sb, x, y, "Biomes (underlay)", LanternLandMapState.ShowBiomesUnderlay, "");
            y += RowH(panelScale);

            _rowBiomeEdges        = DrawBoolRow(sb, x, y, "Biomes (edges)", LanternLandMapState.ShowBiomeEdges, "");
            y += RowH(panelScale);

            _rowChunkGrid         = DrawBoolRow(sb, x, y, "Chunk Grid", LanternLandMapState.ShowChunkGrid, " (" + LanternLandMapConfig.ToggleChunkGridKey + ")");
            y += RowH(panelScale);

            _rowOtherPlayers      = DrawBoolRow(sb, x, y, "Show Players", LanternLandMapState.ShowOtherPlayers, "");
            y += RowH(panelScale);

            // Biome underlay alpha (applies to vanilla + WorldGenPlus biome underlays).
            int alpha = LanternLandMapConfig.BiomeUnderlayAlpha;
            int pct   = (int)Math.Round(alpha * 100.0 / 255.0);
            DrawText(sb, "Biome Alpha: " + alpha + " (" + pct + "%)", x, y, hintScale, LanternLandMapConfig.PanelTextColor);
            y += LineH(hintScale) + 2;

            _biomeAlphaSliderRect = new Rectangle(_panelRect.Left + 16, y, _panelRect.Width - 32, 18);
            DrawSlider(sb, _biomeAlphaSliderRect, alpha, 0, 255);
            y = _biomeAlphaSliderRect.Bottom + 10;

            y = DrawSeparator(sb, y, 4, 6);

            DrawText(sb, "Labels (tri-state)", x, y, headerScale, LanternLandMapConfig.PanelTextColor);
            y += LineH(panelScale) + 6;

            // Label font scale.
            DrawText(sb, "Label Font: " + LanternLandMapConfig.LabelFontScale.ToString("0.00") + "x", x, y, hintScale, LanternLandMapConfig.PanelTextColor);
            y += LineH(hintScale) + 2;

            _labelFontSliderRect = new Rectangle(_panelRect.Left + 16, y, _panelRect.Width - 32, 18);
            DrawSliderFloat(sb, _labelFontSliderRect, LanternLandMapConfig.LabelFontScale, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
            y += 26;

            // Readout font scale.
            DrawText(sb, "Readout Font: " + LanternLandMapConfig.ReadoutFontScale.ToString("0.00") + "x", x, y, hintScale, LanternLandMapConfig.PanelTextColor);
            y += LineH(hintScale) + 2;

            _readoutFontSliderRect = new Rectangle(_panelRect.Left + 16, y, _panelRect.Width - 32, 18);
            DrawSliderFloat(sb, _readoutFontSliderRect, LanternLandMapConfig.ReadoutFontScale, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
            y += 26;

            // Label prefixes.
            string prefixS = LanternLandMapConfig.ToggleStartLabelsKey.ToString().ToUpper();
            string prefixE = LanternLandMapConfig.ToggleEndLabelsKey.ToString().ToUpper();
            string prefixI = LanternLandMapConfig.ToggleIndexKey.ToString().ToUpper();
            string prefixP = LanternLandMapConfig.ToggleThicknessKey.ToString().ToUpper();
            string prefixG = LanternLandMapConfig.ToggleGapKey.ToString().ToUpper();
            string prefixT = LanternLandMapConfig.ToggleTowersKey.ToString().ToUpper();

            _rowStart  = DrawTriRowColored(sb, x, y, $"{prefixS} ", LanternLandMapConfig.StartLabelColor, "Start X", LanternLandMapConfig.StartLabelMode, "");
            y += RowH(panelScale);

            _rowEnd    = DrawTriRowColored(sb, x, y, $"{prefixE} ", LanternLandMapConfig.EndLabelColor, "End X", LanternLandMapConfig.EndLabelMode, "");
            y += RowH(panelScale);

            _rowIndex  = DrawTriRowColored(sb, x, y, $"{prefixI} ", LanternLandMapConfig.IndexLabelColor, "Index", LanternLandMapConfig.IndexLabelMode, "");
            y += RowH(panelScale);

            _rowThick  = DrawTriRowColored(sb, x, y, $"{prefixP} ", LanternLandMapConfig.ThicknessLabelColor, "Thickness", LanternLandMapConfig.ThicknessLabelMode, "");
            y += RowH(panelScale);

            _rowGap    = DrawTriRowColored(sb, x, y, $"{prefixG} ", LanternLandMapConfig.GapLabelColor, "Gap", LanternLandMapConfig.GapLabelMode, "");
            y += RowH(panelScale);

            _rowTowers = DrawTriRowColored(sb, x, y, $"{prefixT} ", LanternLandMapConfig.TowerLabelColor, "Towers", LanternLandMapConfig.TowersLabelMode, "");
            y += RowH(panelScale);
            y = DrawSeparator(sb, y, 6, 6);

            // Window / cursor info.
            var win = LanternLandMath.ComputeWindow(LanternLandMapState.TargetRadius, LanternLandMapState.RingsToShow);
            DrawText(sb, "n0 = " + win.N0, x, y, panelScale * 0.9f, LanternLandMapConfig.PanelTextColor);
            y += LineH(panelScale * 0.9f) + 2;

            DrawText(sb, "Center: X " + FormatBig(_viewCenterX) + "  Z " + FormatBig(_viewCenterZ), x, y, panelScale * 0.9f, LanternLandMapConfig.PanelTextColor);
            y += LineH(panelScale * 0.9f) + 2;

            y = DrawSeparator(sb, y, 8, 8);
            _rowMathReadMe = DrawButtonRow(sb, x, y, "Math & Q&A (Read Me)");
            y += RowH(panelScale);

            // -------------------------------------------------------------------------
            // 4) Update scroll metrics BEFORE we restore scissor.
            // -------------------------------------------------------------------------
            int unscrolledBottom = y + _panelScrollY;
            _panelContentHeight  = Math.Max(0, unscrolledBottom - scrollTop);
            _panelScrollMax      = Math.Max(0, _panelContentHeight - visibleH);
            _panelScrollY        = Clamp(_panelScrollY, 0, _panelScrollMax);

            // Restore scissor.
            gd.ScissorRectangle = prevScissor;

            // -------------------------------------------------------------------------
            // 5) Scrollbar (not clipped by header).
            // -------------------------------------------------------------------------
            if (_panelScrollMax > 0 && visibleH > 0)
            {
                int trackX = _panelRect.Right - 8;
                int trackY = scrollTop;
                int trackW = 6;
                int trackH = Math.Max(24, _panelRect.Bottom - scrollTop - 8);

                _panelScrollTrack = new Rectangle(trackX, trackY, trackW, trackH);

                float fracVisible = visibleH / (float)Math.Max(1, _panelContentHeight);
                int thumbH        = Math.Max(28, (int)(trackH * fracVisible));
                int maxThumbMove  = Math.Max(1, trackH - thumbH);

                float fracScroll  = _panelScrollY / (float)Math.Max(1, _panelScrollMax);
                int thumbY = trackY + (int)(maxThumbMove * fracScroll);

                _panelScrollThumb = new Rectangle(trackX, thumbY, trackW, thumbH);

                DrawRect(sb, _panelScrollTrack, new Color(255, 255, 255, 18));
                DrawRect(sb, _panelScrollThumb, new Color(255, 255, 255, 90));
            }
            else
            {
                _panelScrollTrack = Rectangle.Empty;
                _panelScrollThumb = Rectangle.Empty;
            }

            // -------------------------------------------------------------------------
            // 6) Header cap + header text + close button drawn LAST (covers any overlap).
            // -------------------------------------------------------------------------
            Rectangle headerRect = new Rectangle(_panelRect.Left, _panelRect.Top, _panelRect.Width, scrollTop - _panelRect.Top);
            DrawRect(sb, headerRect, new Color(0, 0, 0, 255));

            // 1px divider under header.
            DrawRect(sb, new Rectangle(_panelRect.Left, scrollTop - 1, _panelRect.Width, 1), new Color(255, 255, 255, 40));

            int hy2 = headerY0;
            DrawText(sb, "Lantern Land Map", x, hy2, headerScale, LanternLandMapConfig.PanelTextColor);
            hy2 += LineH(panelScale) + 8;

            DrawText(sb, "Close: ESC or " + LanternLandMapConfig.ToggleMapKey, x, hy2, hintScale, LanternLandMapConfig.PanelTextColor);
            hy2 += LineH(hintScale) + 2;

            DrawText(sb, "Reload: " + LanternLandMapConfig.ReloadConfigKey, x, hy2, hintScale, LanternLandMapConfig.PanelTextColor);

            DrawCloseButton(sb); // Drawn last so it stays clean.
        }

        /// <summary>Draws the panel close button (hover-highlighted) with an "X" glyph.</summary>
        private void DrawCloseButton(SpriteBatch sb)
        {
            // subtle background, with hover highlight
            var mp     = _lastMousePos;
            bool hover = _closeRect.Contains(mp);

            DrawRect(sb, _closeRect, hover ? new Color(255, 255, 255, 35) : new Color(255, 255, 255, 18));

            // Draw an "X" using lines so it isn't a giant white square
            Vector2 a1 = new Vector2(_closeRect.Left + 5, _closeRect.Top + 5);
            Vector2 b1 = new Vector2(_closeRect.Right - 5, _closeRect.Bottom - 5);
            Vector2 a2 = new Vector2(_closeRect.Left + 5, _closeRect.Bottom - 5);
            Vector2 b2 = new Vector2(_closeRect.Right - 5, _closeRect.Top + 5);

            DrawLine(sb, a1, b1, new Color(255, 255, 255, 180), 2f);
            DrawLine(sb, a2, b2, new Color(255, 255, 255, 180), 2f);
        }

        /// <summary>Returns line-height for the selected font+scale.</summary>
        private int LineH(float scale)
        {
            if (_font == null) return (int)(14 * scale);
            return (int)Math.Ceiling(MeasureText("Ag", scale).Y);
        }

        /// <summary>Returns row-height for checkbox rows (line-height + padding).</summary>
        private int RowH(float scale)
        {
            return LineH(scale) + 6;
        }

        /// <summary>Draws a boolean checkbox row and returns its clickable rectangle.</summary>
        private Rectangle DrawBoolRow(SpriteBatch sb, int x, int y, string label, bool value, string suffix)
        {
            float scale = LanternLandMapConfig.PanelFontScale * 0.92f;
            int rowH    = RowH(scale);

            var row     = new Rectangle(_panelRect.Left + 10, y - 2, _panelRect.Width - 20, rowH);
            bool hover  = row.Contains(_lastMousePos);

            DrawRect(sb, row, hover ? new Color(255, 255, 255, 16) : new Color(0, 0, 0, 0));

            // Scale checkbox with panel font.
            int boxSize = (int)Math.Round(14f * LanternLandMapConfig.PanelFontScale);
            if (boxSize < 10) boxSize = 10;
            if (boxSize > rowH - 6) boxSize = Math.Max(10, rowH - 6);

            int boxY = row.Top + (row.Height - boxSize) / 2;
            var box  = new Rectangle(x, boxY, boxSize, boxSize);
            DrawCheckBox(sb, box, value);

            int textY = row.Top + (row.Height - LineH(scale)) / 2;
            DrawText(sb, label + suffix, x + boxSize + 8, textY, scale, LanternLandMapConfig.PanelTextColor);

            // Click handled in HandlePanelUI using these rects.
            return row;
        }

        /// <summary>Draws a tri-state checkbox row and returns its clickable rectangle.</summary>
        private Rectangle DrawTriRow(SpriteBatch sb, int x, int y, string label, TriStateLabelMode mode, string suffix)
        {
            float scale = LanternLandMapConfig.PanelFontScale * 0.92f;
            int rowH    = RowH(scale);

            var row     = new Rectangle(_panelRect.Left + 10, y - 2, _panelRect.Width - 20, rowH);
            bool hover  = row.Contains(_lastMousePos);

            DrawRect(sb, row, hover ? new Color(255, 255, 255, 16) : new Color(0, 0, 0, 0));

            // Scale checkbox with panel font.
            int boxSize = (int)Math.Round(14f * LanternLandMapConfig.PanelFontScale);
            if (boxSize < 10) boxSize = 10;
            if (boxSize > rowH - 6) boxSize = Math.Max(10, rowH - 6);

            int boxY = row.Top + (row.Height - boxSize) / 2;
            var box  = new Rectangle(x, boxY, boxSize, boxSize);
            DrawTriCheckBox(sb, box, mode);

            int textY = row.Top + (row.Height - LineH(scale)) / 2;
            DrawText(sb, label + suffix, x + boxSize + 8, textY, scale, LanternLandMapConfig.PanelTextColor);

            return row;
        }

        /// <summary>
        /// Draws a clickable "button-like" row in the right panel and returns its hit-rect.
        /// </summary>
        private Rectangle DrawButtonRow(SpriteBatch sb, int x, int y, string label)
        {
            float scale = LanternLandMapConfig.PanelFontScale * 0.92f;
            int rowH = RowH(scale);

            var row = new Rectangle(_panelRect.Left + 10, y - 2, _panelRect.Width - 20, rowH);
            bool hover = row.Contains(_lastMousePos);

            DrawRect(sb, row, hover ? new Color(255, 255, 255, 22) : new Color(255, 255, 255, 12));

            int textY = row.Top + (row.Height - LineH(scale)) / 2;
            DrawText(sb, label, x, textY, scale, LanternLandMapConfig.PanelTextColor);

            return row;
        }

        /// <summary>Draws a tri-state checkbox row with a colored prefix (used for label hotkey prefixes).</summary>
        private Rectangle DrawTriRowColored(SpriteBatch sb, int x, int y, string prefix, Color prefixColor, string label, TriStateLabelMode mode, string suffix)
        {
            float scale = LanternLandMapConfig.PanelFontScale * 0.92f;
            int rowH    = RowH(scale);

            var row     = new Rectangle(_panelRect.Left + 10, y - 2, _panelRect.Width - 20, rowH);
            bool hover  = row.Contains(_lastMousePos);

            DrawRect(sb, row, hover ? new Color(255, 255, 255, 16) : new Color(0, 0, 0, 0));

            // Scale checkbox with panel font.
            int boxSize = (int)Math.Round(14f * LanternLandMapConfig.PanelFontScale);
            if (boxSize < 10) boxSize = 10;
            if (boxSize > rowH - 6) boxSize = Math.Max(10, rowH - 6);

            int boxY = row.Top + (row.Height - boxSize) / 2;
            var box  = new Rectangle(x, boxY, boxSize, boxSize);
            DrawTriCheckBox(sb, box, mode);

            int textY = row.Top + (row.Height - LineH(scale)) / 2;
            int textX = x + boxSize + 8;

            // Prefix in label color, with a tiny outline so dark colors (ex: black) stay visible on the panel.
            Color outline = (Luma(prefixColor) < 0.45f) ? new Color(255, 255, 255, 200) : new Color(0, 0, 0, 200);
            DrawTextOutlined(sb, prefix, textX, textY, scale, prefixColor, outline, 1);

            int prefixW = (int)Math.Round(MeasureText(prefix, scale).X);
            DrawText(sb, label + suffix, textX + prefixW, textY, scale, LanternLandMapConfig.PanelTextColor);

            return row;
        }

        /// <summary>Quick luminance estimate for choosing light/dark outlines.</summary>
        private static float Luma(Color c)
        {
            return (0.2126f * (c.R / 255f)) + (0.7152f * (c.G / 255f)) + (0.0722f * (c.B / 255f));
        }

        /// <summary>Draws a standard checkbox.</summary>
        private void DrawCheckBox(SpriteBatch sb, Rectangle box, bool on)
        {
            DrawRect(sb, box, new Color(255, 255, 255, 22));

            // Border.
            DrawRect(sb, new Rectangle(box.Left, box.Top, box.Width, 1), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(box.Left, box.Bottom - 1, box.Width, 1), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(box.Left, box.Top, 1, box.Height), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(box.Right - 1, box.Top, 1, box.Height), new Color(255, 255, 255, 60));

            if (!on) return;

            // Simple check mark (scaled with the box).
            int pad   = Math.Max(2, box.Width / 5);

            float p1x = box.Left + pad;
            float p1y = box.Top + box.Height * 0.55f;

            float p2x = box.Left + box.Width * 0.45f;
            float p2y = box.Bottom - pad;

            float p3x = box.Right - pad;
            float p3y = box.Top + pad;

            Vector2 p1 = new Vector2((int)Math.Round(p1x), (int)Math.Round(p1y));
            Vector2 p2 = new Vector2((int)Math.Round(p2x), (int)Math.Round(p2y));
            Vector2 p3 = new Vector2((int)Math.Round(p3x), (int)Math.Round(p3y));

            float thick = MathHelper.Clamp(box.Width / 7f, 1.5f, 3.25f);

            DrawLine(sb, p1, p2, new Color(255, 255, 255, 200), thick);
            DrawLine(sb, p2, p3, new Color(255, 255, 255, 200), thick);
        }

        /// <summary>Draws a tri-state checkbox (Off / DotOnly / DotAndLabel).</summary>
        private void DrawTriCheckBox(SpriteBatch sb, Rectangle box, TriStateLabelMode mode)
        {
            DrawRect(sb, box, new Color(255, 255, 255, 22));

            // border
            DrawRect(sb, new Rectangle(box.Left, box.Top, box.Width, 1), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(box.Left, box.Bottom - 1, box.Width, 1), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(box.Left, box.Top, 1, box.Height), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(box.Right - 1, box.Top, 1, box.Height), new Color(255, 255, 255, 60));

            if (mode == TriStateLabelMode.Off)
                return;

            if (mode == TriStateLabelMode.DotOnly)
            {
                // Centered dot (scaled with the box).
                int cx = box.Center.X;
                int cy = box.Center.Y;

                int dot = Math.Max(3, box.Width / 4);
                if ((dot & 1) == 0) dot += 1; // Prefer odd so it centers nicely.

                DrawRect(sb, new Rectangle(cx - dot / 2, cy - dot / 2, dot, dot), new Color(255, 255, 255, 200));
                return;
            }

            // DotAndLabel: Check mark (scaled with the box).
            int pad   = Math.Max(2, box.Width / 5);

            float p1x = box.Left + pad;
            float p1y = box.Top + box.Height * 0.55f;

            float p2x = box.Left + box.Width * 0.45f;
            float p2y = box.Bottom - pad;

            float p3x = box.Right - pad;
            float p3y = box.Top + pad;

            Vector2 p1 = new Vector2((int)Math.Round(p1x), (int)Math.Round(p1y));
            Vector2 p2 = new Vector2((int)Math.Round(p2x), (int)Math.Round(p2y));
            Vector2 p3 = new Vector2((int)Math.Round(p3x), (int)Math.Round(p3y));

            float thick = MathHelper.Clamp(box.Width / 6.25f, 1.75f, 3.75f);

            DrawLine(sb, p1, p2, new Color(255, 255, 255, 220), thick);
            DrawLine(sb, p2, p3, new Color(255, 255, 255, 220), thick);
        }

        /// <summary>Draws an integer slider with a simple knob.</summary>
        private void DrawSlider(SpriteBatch sb, Rectangle r, int value, int min, int max)
        {
            DrawRect(sb, r, new Color(255, 255, 255, 25));
            float t = (max <= min) ? 0f : (value - min) / (float)(max - min);
            int knobX = r.Left + (int)(t * (r.Width - 10));
            var knob = new Rectangle(knobX, r.Top - 2, 10, r.Height + 4);
            DrawRect(sb, knob, new Color(255, 255, 255, 85));
        }

        /// <summary>Draws a float slider with a simple knob.</summary>
        private void DrawSliderFloat(SpriteBatch sb, Rectangle r, float value, float min, float max)
        {
            DrawRect(sb, r, new Color(255, 255, 255, 25));
            float t = (max <= min) ? 0f : (value - min) / (max - min);
            t = MathHelper.Clamp(t, 0f, 1f);
            int knobX = r.Left + (int)(t * (r.Width - 10));
            var knob  = new Rectangle(knobX, r.Top - 2, 10, r.Height + 4);
            DrawRect(sb, knob, new Color(255, 255, 255, 85));
        }

        /// <summary>Draws the radius slider, optionally using log scaling from config.</summary>
        private void DrawSliderRadius(SpriteBatch sb, Rectangle r, double radius)
        {
            DrawRect(sb, r, new Color(255, 255, 255, 25));

            double min = LanternLandMapConfig.RadiusMin;
            double max = LanternLandMapConfig.RadiusMax;

            double t;
            if (LanternLandMapConfig.RadiusLogScale)
            {
                double lo = Math.Log(Math.Max(1d, min));
                double hi = Math.Log(Math.Max(min + 1d, max));
                double v  = Math.Log(Math.Max(1d, radius));
                t = (hi <= lo) ? 0d : (v - lo) / (hi - lo);
            }
            else
            {
                t = (max <= min) ? 0d : (radius - min) / (max - min);
            }

            if (t < 0d) t = 0d;
            if (t > 1d) t = 1d;

            int knobX = r.Left + (int)(t * (r.Width - 10));
            var knob  = new Rectangle(knobX, r.Top - 2, 10, r.Height + 4);
            DrawRect(sb, knob, new Color(255, 255, 255, 85));
        }

        /// <summary>Handles panel clicks, slider drags, tri-state toggles, and panel scrolling.</summary>
        private void HandlePanelUI(InputManager input)
        {
            var mp = input.Mouse.Position;

            // Editable textbox for Start Radius (R).
            // Click the number box to type an exact value; Enter/Tab to apply; click away to apply.
            if (_editRadiusText)
            {
                // Typing
                if (input.Keyboard.WasKeyPressed(Keys.Enter) || input.Keyboard.WasKeyPressed(Keys.Tab))
                {
                    CommitRadiusTextEdit();
                }
                else
                {
                    HandleRadiusTextTyping(input);
                }

                // Keep focus if you click inside the box (don't fall through and toggle other widgets).
                if (input.Mouse.LeftButtonPressed && _radiusTextRect.Contains(mp))
                    return;

                // Click-away commits.
                if (input.Mouse.LeftButtonPressed && !_radiusTextRect.Contains(mp))
                {
                    CommitRadiusTextEdit();
                    // Fall through so the click can also interact with whatever you clicked.
                }
            }
            else
            {
                if (input.Mouse.LeftButtonPressed && _radiusTextRect.Contains(mp))
                {
                    BeginRadiusTextEdit();
                    return;
                }
            }

            // Scrollbar drag: Keeps the panel usable even when the options list is taller than the screen.
            if (_dragPanelScroll)
            {
                if (!input.Mouse.LeftButtonDown)
                {
                    _dragPanelScroll = false;
                }
                else if (_panelScrollMax > 0 && _panelScrollTrack.Height > 0 && _panelScrollThumb.Height > 0)
                {
                    int maxThumbMove = Math.Max(1, _panelScrollTrack.Height - _panelScrollThumb.Height);
                    int dy = mp.Y - _dragPanelScrollStartMouseY;

                    float t = dy / (float)maxThumbMove;
                    int newScroll = _dragPanelScrollStartScroll + (int)Math.Round(t * _panelScrollMax);
                    _panelScrollY = Clamp(newScroll, 0, _panelScrollMax);
                }

                return;
            }

            // Start drag on sliders.
            if (input.Mouse.LeftButtonPressed)
            {
                // Scrollbar click/drag (only when we actually have scrollable content).
                if (_panelScrollMax > 0 && _panelScrollThumb.Contains(mp))
                {
                    _dragPanelScroll = true;
                    _dragPanelScrollStartMouseY = mp.Y;
                    _dragPanelScrollStartScroll = _panelScrollY;
                    return;
                }

                if (_panelScrollMax > 0 && _panelScrollTrack.Contains(mp))
                {
                    // Jump so the thumb centers roughly on the click position.
                    int trackY = _panelScrollTrack.Top;
                    int trackH = _panelScrollTrack.Height;
                    int thumbH = Math.Max(28, _panelScrollThumb.Height > 0 ? _panelScrollThumb.Height : (trackH / 4));
                    int maxThumbMove = Math.Max(1, trackH - thumbH);

                    float t = (mp.Y - trackY - (thumbH * 0.5f)) / (float)maxThumbMove;
                    t = MathHelper.Clamp(t, 0f, 1f);

                    _panelScrollY = (int)Math.Round(t * _panelScrollMax);

                    _dragPanelScroll            = true;
                    _dragPanelScrollStartMouseY = mp.Y;
                    _dragPanelScrollStartScroll = _panelScrollY;
                    return;
                }

                if (_rowMathReadMe.Contains(mp))
                {
                    _showMathReadMe = true;
                    _mathScrollY = 0;
                    _dragMathScroll = false;
                    return;
                }

                if (_sliderARect.Contains(mp))
                    _dragA = true;
                else if (_sliderRRect.Contains(mp))
                    _dragR = true;
                else if (_panelFontSliderRect.Contains(mp))
                    _dragPanelFont = true;
                else if (_biomeAlphaSliderRect.Contains(mp))
                    _dragBiomeAlpha = true;
                else if (_labelFontSliderRect.Contains(mp))
                    _dragLabelFont = true;
                else if (_readoutFontSliderRect.Contains(mp))
                    _dragReadoutFont = true;

                // Clickable rows.
                bool changed = false;

                if (_rowLanternRings.Contains(mp))
                {
                    LanternLandMapState.ShowLanternRings = !LanternLandMapState.ShowLanternRings;
                    changed = true;
                }

                if (_rowFillRings.Contains(mp))
                {
                    LanternLandMapConfig.FillRingsSolid = !LanternLandMapConfig.FillRingsSolid;
                    changed = true;
                }

                if (_rowBiomes.Contains(mp))
                {
                    LanternLandMapState.ShowBiomesUnderlay = !LanternLandMapState.ShowBiomesUnderlay;
                    changed = true;
                }

                if (_rowBiomeEdges.Contains(mp))
                {
                    LanternLandMapState.ShowBiomeEdges = !LanternLandMapState.ShowBiomeEdges;
                    changed = true;
                }

                if (_rowChunkGrid.Contains(mp))
                {
                    LanternLandMapState.ShowChunkGrid = !LanternLandMapState.ShowChunkGrid;
                    changed = true;
                }

                if (_rowOtherPlayers.Contains(mp))
                {
                    LanternLandMapState.ShowOtherPlayers = !LanternLandMapState.ShowOtherPlayers;
                    changed = true;
                }

                if (_rowStart.Contains(mp))
                {
                    LanternLandMapConfig.StartLabelMode = Next(LanternLandMapConfig.StartLabelMode);
                    changed = true;
                }

                if (_rowEnd.Contains(mp))
                {
                    LanternLandMapConfig.EndLabelMode = Next(LanternLandMapConfig.EndLabelMode);
                    changed = true;
                }

                if (_rowIndex.Contains(mp))
                {
                    LanternLandMapConfig.IndexLabelMode = Next(LanternLandMapConfig.IndexLabelMode);
                    changed = true;
                }

                if (_rowThick.Contains(mp))
                {
                    LanternLandMapConfig.ThicknessLabelMode = Next(LanternLandMapConfig.ThicknessLabelMode);
                    changed = true;
                }

                if (_rowGap.Contains(mp))
                {
                    LanternLandMapConfig.GapLabelMode = Next(LanternLandMapConfig.GapLabelMode);
                    changed = true;
                }

                if (_rowTowers.Contains(mp))
                {
                    LanternLandMapConfig.TowersLabelMode = Next(LanternLandMapConfig.TowersLabelMode);
                    changed = true;
                }

                if (changed)
                    SaveConfigSafe(force: true);
            }

            // Mouse wheel behavior:
            //  - Default:   Wheel scrolls the panel if the mouse is over the scrollable panel region.
            //  - Hold Ctrl: Wheel-adjust sliders under the cursor (A/R/fonts) instead of scrolling.
            int dw = input.Mouse.DeltaWheel;
            if (dw != 0)
            {
                bool ctrl = input.Keyboard.IsKeyDown(Keys.LeftControl) || input.Keyboard.IsKeyDown(Keys.RightControl);

                bool overWheelWidget =
                    ctrl && (
                        _sliderRRect.Contains(mp) ||
                        _sliderARect.Contains(mp) ||
                        _labelFontSliderRect.Contains(mp) ||
                        _panelFontSliderRect.Contains(mp) ||
                        _biomeAlphaSliderRect.Contains(mp) ||
                        _readoutFontSliderRect.Contains(mp)
                    );

                // 1) Panel scroll has priority (unless Ctrl+hovering a wheel-widget).
                if (_panelScrollMax > 0 && _panelScrollClipRect.Contains(mp) && !overWheelWidget)
                {
                    int clicks = dw / 120;
                    if (clicks == 0) clicks = (dw > 0) ? 1 : -1;

                    int step = Math.Max(18, LineH(LanternLandMapConfig.PanelFontScale * 0.92f) + 8);
                    _panelScrollY = Clamp(_panelScrollY - (clicks * step), 0, _panelScrollMax);

                    return; // Consume wheel so sliders never also react this frame.
                }

                // 2) Optional: Ctrl+wheel adjusts sliders.
                if (ctrl)
                {
                    if (_sliderRRect.Contains(mp))
                    {
                        double mul = LanternLandMapConfig.RadiusWheelMul;
                        LanternLandMapState.TargetRadius *= (dw > 0) ? mul : (1d / mul);
                        LanternLandMapState.TargetRadius = Clamp(LanternLandMapState.TargetRadius, LanternLandMapConfig.RadiusMin, LanternLandMapConfig.RadiusMax);
                        SaveConfigSafe(force: true);
                    }

                    if (_sliderARect.Contains(mp))
                    {
                        LanternLandMapState.RingsToShow += (dw > 0) ? 1 : -1;
                        LanternLandMapState.RingsToShow = Clamp(LanternLandMapState.RingsToShow, LanternLandMapConfig.RingsMin, LanternLandMapConfig.RingsMax);
                        SaveConfigSafe(force: true);
                    }

                    if (_labelFontSliderRect.Contains(mp))
                    {
                        const float step = 0.05f;
                        LanternLandMapConfig.LabelFontScale += (dw > 0) ? step : -step;
                        LanternLandMapConfig.LabelFontScale = Clamp(LanternLandMapConfig.LabelFontScale, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
                        SaveConfigSafe(force: true);
                    }

                    if (_panelFontSliderRect.Contains(mp))
                    {
                        const float step = 0.05f;
                        LanternLandMapConfig.PanelFontScale += (dw > 0) ? step : -step;
                        LanternLandMapConfig.PanelFontScale = Clamp(LanternLandMapConfig.PanelFontScale, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
                        SaveConfigSafe(force: true);
                    }

                                        if (_biomeAlphaSliderRect.Contains(mp))
                    {
                        const int step = 5; // coarse wheel step; drag for fine control
                        int v = (int)LanternLandMapConfig.BiomeUnderlayAlpha + ((dw > 0) ? step : -step);
                        if (v < 0) v = 0;
                        if (v > 255) v = 255;
                        LanternLandMapConfig.BiomeUnderlayAlpha = (byte)v;
                        SaveConfigSafe(force: true);
                    }

                    if (_readoutFontSliderRect.Contains(mp))
                    {
                        const float step = 0.05f;
                        LanternLandMapConfig.ReadoutFontScale += (dw > 0) ? step : -step;
                        LanternLandMapConfig.ReadoutFontScale = Clamp(LanternLandMapConfig.ReadoutFontScale, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
                        SaveConfigSafe(force: true);
                    }
                }
            }

            // Release.
            if (!input.Mouse.LeftButtonDown)
            {
                // If we were dragging a persisted slider, flush a single INI write on mouse-up.
                if (_pendingConfigSave && (_dragA || _dragR || _dragPanelFont || _dragLabelFont || _dragReadoutFont || _dragBiomeAlpha))
                    SaveConfigSafe(force: true);

                _dragA = _dragR = _dragLabelFont = _dragReadoutFont = _dragPanelFont = _dragBiomeAlpha = false;
                _dragPanelScroll = false;
                return;
            }

            // Drag A.
            if (_dragA)
            {
                var r   = _sliderARect;
                float t = (mp.X - r.Left) / (float)Math.Max(1, r.Width);
                t = MathHelper.Clamp(t, 0f, 1f);
                int v = LanternLandMapConfig.RingsMin + (int)Math.Round(t * (LanternLandMapConfig.RingsMax - LanternLandMapConfig.RingsMin));
                LanternLandMapState.RingsToShow = Clamp(v, LanternLandMapConfig.RingsMin, LanternLandMapConfig.RingsMax);
                MarkConfigDirty();
            }

            // Drag R.
            if (_dragR)
            {
                var r    = _sliderRRect;
                double t = (mp.X - r.Left) / (double)Math.Max(1, r.Width);
                if (t < 0d) t = 0d;
                if (t > 1d) t = 1d;

                double min = LanternLandMapConfig.RadiusMin;
                double max = LanternLandMapConfig.RadiusMax;

                double v;
                if (LanternLandMapConfig.RadiusLogScale)
                {
                    double lo = Math.Log(Math.Max(1d, min));
                    double hi = Math.Log(Math.Max(min + 1d, max));
                    double x = lo + t * (hi - lo);
                    v = Math.Exp(x);
                }
                else
                {
                    v = min + t * (max - min);
                }

                LanternLandMapState.TargetRadius = Clamp(v, LanternLandMapConfig.RadiusMin, LanternLandMapConfig.RadiusMax);
                MarkConfigDirty();
            }

            // Drag panel font scale.
            if (_dragPanelFont)
            {
                var r   = _panelFontSliderRect;
                float t = (mp.X - r.Left) / (float)Math.Max(1, r.Width);
                t = MathHelper.Clamp(t, 0f, 1f);
                float v = LanternLandMapConfig.LabelFontScaleMin + t * (LanternLandMapConfig.LabelFontScaleMax - LanternLandMapConfig.LabelFontScaleMin);

                const float step = 0.05f;
                v = (float)Math.Round(v / step) * step;
                LanternLandMapConfig.PanelFontScale = Clamp(v, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
                MarkConfigDirty();
            }


            // Drag biome underlay alpha.
            if (_dragBiomeAlpha)
            {
                var r   = _biomeAlphaSliderRect;
                float t = (mp.X - r.Left) / (float)Math.Max(1, r.Width);
                t = MathHelper.Clamp(t, 0f, 1f);

                int v = (int)Math.Round(t * 255f);
                if (v < 0) v = 0;
                if (v > 255) v = 255;

                LanternLandMapConfig.BiomeUnderlayAlpha = (byte)v;
                MarkConfigDirty();
            }

            // Drag label font scale.
            if (_dragLabelFont)
            {
                var r   = _labelFontSliderRect;
                float t = (mp.X - r.Left) / (float)Math.Max(1, r.Width);
                t = MathHelper.Clamp(t, 0f, 1f);
                float v = LanternLandMapConfig.LabelFontScaleMin + t * (LanternLandMapConfig.LabelFontScaleMax - LanternLandMapConfig.LabelFontScaleMin);

                // Snap to a small step so the font doesn't jitter on fractional scales.
                const float step = 0.05f;
                v = (float)Math.Round(v / step) * step;
                LanternLandMapConfig.LabelFontScale = Clamp(v, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
                MarkConfigDirty();
            }

            // Drag readout font scale.
            if (_dragReadoutFont)
            {
                var r   = _readoutFontSliderRect;
                float t = (mp.X - r.Left) / (float)Math.Max(1, r.Width);
                t = MathHelper.Clamp(t, 0f, 1f);
                float v = LanternLandMapConfig.LabelFontScaleMin + t * (LanternLandMapConfig.LabelFontScaleMax - LanternLandMapConfig.LabelFontScaleMin);

                const float step = 0.05f;
                v = (float)Math.Round(v / step) * step;
                LanternLandMapConfig.ReadoutFontScale = Clamp(v, LanternLandMapConfig.LabelFontScaleMin, LanternLandMapConfig.LabelFontScaleMax);
                MarkConfigDirty();
            }
        }
        #endregion

        #region Math Read Me (Modal Overlay)

        #region State / Layout Fields

        /// <summary>
        /// True while the Math Read Me modal is open.
        /// </summary>
        private bool _showMathReadMe;

        /// <summary>
        /// Right-panel button row for "Math (Read Me)" (computed in DrawPanel).
        /// </summary>
        private Rectangle _rowMathReadMe;

        /// <summary>
        /// Full modal bounds (panel rect).
        /// </summary>
        private Rectangle _mathRect;

        /// <summary>
        /// X button hit-rect inside the modal header.
        /// </summary>
        private Rectangle _mathCloseRect;

        /// <summary>
        /// Scrollable text viewport inside the modal (the scissored area).
        /// </summary>
        private Rectangle _mathContentRect;

        /// <summary>
        /// Scrollbar track rect (drawn at the right of the content rect).
        /// </summary>
        private Rectangle _mathScrollTrack;

        /// <summary>
        /// Scrollbar thumb rect (computed from scroll position + content size).
        /// </summary>
        private Rectangle _mathScrollThumb;

        /// <summary>
        /// Current vertical scroll offset (pixels).
        /// </summary>
        private int _mathScrollY;

        /// <summary>
        /// Max allowed vertical scroll offset (pixels).
        /// </summary>
        private int _mathScrollMax;

        /// <summary>
        /// Total height of the wrapped text content (pixels).
        /// </summary>
        private int _mathContentHeight;

        /// <summary>
        /// True while the user is dragging the scrollbar thumb.
        /// </summary>
        private bool _dragMathScroll;

        /// <summary>
        /// Mouse Y at the start of a scrollbar drag.
        /// </summary>
        private int _dragMathScrollStartMouseY;

        /// <summary>
        /// Scroll value at the start of a scrollbar drag.
        /// </summary>
        private int _dragMathScrollStartScroll;

        #endregion

        #region Wrapped Text Cache

        /// <summary>
        /// Cached wrapped lines for the current content width + scale.
        /// </summary>
        private List<string> _mathLines = new List<string>();

        /// <summary>
        /// Last wrap width used to build <see cref="_mathLines"/>.
        /// </summary>
        private int _mathWrapWidthPx = -1;

        /// <summary>
        /// Last wrap scale used to build <see cref="_mathLines"/>.
        /// </summary>
        private float _mathWrapScale = -1f;

        #endregion

        #region Read Me Content

        private static readonly string[] MathReadMeText = new[]
        {
            "Lantern Land Map - What This Mod Does:",
            "-----------------------------------------------------------------------------------------------------",
            "This screen visualizes CastleMiner Z's Lantern Land \"ring walls\" as a 2D overlay.",
            "It helps you see where rings start/end, how thick the walls are, how big the gaps are,",
            "and where spawn towers land (square-index rings).",
            "",
            "Key idea:",
            "  • The ring math is continuous (real numbers), but the game places blocks on an integer grid.",
            "  • Because the engine floors coordinates, very thin walls/gaps can disappear when snapped to blocks.",
            "",
            "SLIDERS:",
            "  [A] - Amount of rings to draw (large values can lag).",
            "  [R] - Target radius (in blocks) used to choose which ring index window to display.",
            "",
            "LABELS: (Floor is what the game uses.)",
            "  [S] - Start X of a ring (inner edge at the +X axis).",
            "  [E] - End   X of a ring (outer edge at the +X axis).",
            "  [P] - Ring thickness (often shown as a value/marker; read the displayed Y/value).",
            "  [G] - Gap to next wall (often shown as a value/marker; read the displayed Y/value).",
            "  [I]  - Wall index (n).",
            "  [T] - Spawn tower marker (only on square-index rings).",
            "",
            "Radial Wall Math - How the Rings Work:",
            "-----------------------------------------------------------------------------------------------------",
            "Let K = 2^31. Rings are centered at the world origin on the XZ plane.",
            "",
            "For wall n (1-based index):",
            "  r_start(n) = sqrt((2n - 1) · K)   (inner edge radius)",
            "  r_end(n)   = sqrt((2n) · K)       (outer edge radius)",
            "",
            "So each wall is the annulus (ring band) between:",
            "  (2n - 1) · K <= x^2 + z^2 <= 2n · K",
            "",
            "Derived measures (continuous math):",
            "  T(n) = r_end(n)     - r_start(n)    (wall thickness)",
            "  G(n) = r_start(n+1) - r_end(n)    (gap to next wall)",
            "",
            "Facts:",
            "  • T(n) > 0 and G(n) > 0 for all n.",
            "  • Both shrink roughly like 1/sqrt(n), so rings get thinner and closer together forever.",
            "  • Mathematically, walls never truly merge; there is always a real gap.",
            "",
            "Choosing the first visible wall index (n0) from radius R:",
            "-----------------------------------------------------------------------------------------------------",
            "Outer edge is r_end(n) = sqrt(2nK). The first wall whose outer edge reaches R is:",
            "  n0 = max(1, ceil(R^2 / (2K)))",
            "Then the drawn window is typically:",
            "  n = n0, n0+1, ..., n0 + A - 1",
            "",
            "What Happens on the Block Grid (Floored X, Y, Z):",
            "-----------------------------------------------------------------------------------------------------",
            "In the game:",
            "  • Blocks are placed at floored coordinates.",
            "    Example: If a computed X is 5.950, the block goes at X = 5.",
            "  • Any continuous thickness/gap < 1.0 blocks can vanish after floor/snap-to-int.",
            "",
            "Empirical regimes along the +X axis (approx):",
            "  • Around radius ~= 537,000,000 blocks:",
            "      - Wall thickness : Wall gap ~= 1 : 1 (after flooring).",
            "      - T(n) and G(n) are in [1, 2) -> floor to 1.",
            "      - You still see an alternating 1-block wall, 1-block gap pattern.",
            "",
            "  • Around radius ~= 1,080,000,000 blocks:",
            "      - Wall thickness : Wall gap ~= 0 : 0 (after flooring).",
            "      - T(n) and G(n) are < 1 -> floor to 0.",
            "      - The integer grid \"wins\" and walls/gaps can visually collapse into one shell.",
            "",
            "Spawn Towers - Square-Index Rings:",
            "-----------------------------------------------------------------------------------------------------",
            "From manual checks: Spawn towers occur on ring indices that are perfect squares:",
            "  n = 1, 4, 9, 16, 25, 36, 49, 64, 81, 100, ...  (n = k^2)",
            "",
            "Using K = 2^31:",
            "  r_end(n) = sqrt(2nK) = sqrt(2n · 2^31) = sqrt(n · 2^32) = 2^16 · sqrt(n)",
            "",
            "If n = k^2, then:",
            "  r_end(k^2) = 2^16 · k = 65,536 · k",
            "",
            "So along the +X (or ±XZ axis directions), the tower end radius lands at:",
            "  Tower ring index:  n = k^2",
            "  Tower X position:  X = 65,536 · k",
            "  World-space (axis example): (X, 0, 0)",
            "",
            "Visible tower markers (in a window [n0 .. n0 + A - 1]) clamp k to:",
            "  minK = ceil(sqrt(n0))",
            "  maxK = floor(sqrt(n0 + A - 1))",
            "  k    = [minK .. maxK]",
            "",
            "So... Does The Wall Ever Become \"One Giant Wall\"?",
            "-----------------------------------------------------------------------------------------------------",
            "  • Mathematically: No. Thickness and gaps stay > 0 forever.",
            "  • On the integer block grid: Once both drop below 1 block, the engine can no longer",
            "    represent them separately, and they can behave like one connected shell in practice.",
            "",
            "RING MATH (symbol notes):",
            "-----------------------------------------------------------------------------------------------------",
            "  [K]   - Scale constant for radius^2, fixed at 2^31.",
            "  [n0]  - First wall index whose outer edge reaches radius R.",
            "  [n]   - List of A wall indices: n0, n0+1, ..., n0 + A - 1.",
            "  [x,z] - World-plane axes (centered at origin).",
            "",
            "LABEL MATH (helpful derived lists):",
            "-----------------------------------------------------------------------------------------------------",
            "  [k] - Indices used for gaps: n0, n0+1, ..., n0 + A - 2.",
            "  [g] - Gap sizes between wall k and wall k+1.",
            "  [t] - Wall thicknesses for each ring in n.",
            "  [m] - Mid-gap radii (middle of the gap band).",
            "  [u] - Mid-ring radii (middle of the wall band).",
            "",
            "Extra - What Is The Furthest Wall You Can Reach (Vanilla)?",
            "-----------------------------------------------------------------------------------------------------",
            "Further distances become harder due to player bounding-box / movement behavior.",
            "Approx \"furthest\" distances before progress becomes impossible:",
            "  • 2,095,152  - Walking",
            "  • 4,194,304  - Running",
            "  • 8,388,608  - Flying",
            "  • 16,777,216 - Flying + Running",
            "",
            "Given those points, the furthest lantern land the player can reach without mods is:",
            "  • n ~= 65,535",
            "",
            "Extra - How Much Of The World Is Lantern-Land?",
            "-----------------------------------------------------------------------------------------------------",
            "  • Each ring band and each gap band have equal area (~= PIE·K).",
            "  • Inside the big circle, walls cover ~50% of the circular area and gaps the other ~50%.",
            "  • If you treat the whole world as a 32-bit square (x,z E [−2^31, 2^31]), then walls",
            "    cover about PIE/8 ~= 39.27% of all possible block positions (by area comparison).",
            "",
            "Credits:",
            "-----------------------------------------------------------------------------------------------------",
            "Made by RussDev7 (dannyruss)",
        };
        #endregion

        #region Draw Pipeline (3-Pass Modal)

        /// <summary>
        /// Draws the Math Read Me modal using 3 passes:
        /// 1) frame (dim + panel), 2) scissored body text, 3) header drawn last to cover anything underneath.
        /// </summary>
        private void DrawMathReadMeOverlayModal(GraphicsDevice device, SpriteBatch sb)
        {
            // PASS A: Dim + panel + layout (no body text here).
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);

            DrawMathReadMeOverlay_FrameOnly(sb);

            sb.End();

            // PASS B: BODY (scissored) - Scissor MUST be set BEFORE Begin().
            var prevScissor         = device.ScissorRectangle;
            var prevRaster          = device.RasterizerState;

            device.RasterizerState  = _scissorRaster;
            device.ScissorRectangle = Rectangle.Intersect(_mathContentRect, device.Viewport.Bounds);

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, _scissorRaster);

            DrawMathReadMeOverlay_BodyOnly(sb);

            sb.End();

            device.ScissorRectangle = prevScissor;
            device.RasterizerState  = prevRaster;

            // PASS C: HEADER + close + scrollbar drawn LAST (cannot be overdrawn by body).
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);

            DrawMathReadMeOverlay_HeaderOnly(sb);

            sb.End();
        }
        #endregion

        #region Draw Passes

        /// <summary>
        /// Draws the dim background + modal frame and computes all rects/scroll values.
        /// (No header text and no body text in this pass.)
        /// </summary>
        private void DrawMathReadMeOverlay_FrameOnly(SpriteBatch sb)
        {
            var gd = sb.GraphicsDevice;
            var vp = gd.Viewport.Bounds;

            // Dim background.
            DrawRect(sb, vp, new Color(0, 0, 0, 170));

            // Modal rect.
            int w = Clamp((int)(vp.Width * 0.78f), 420, vp.Width - 40);
            int h = Clamp((int)(vp.Height * 0.78f), 260, vp.Height - 40);
            int x = vp.Left + (vp.Width - w) / 2;
            int y = vp.Top + (vp.Height - h) / 2;

            _mathRect = new Rectangle(x, y, w, h);

            // Body (slightly lighter / slightly transparent).
            DrawRect(sb, _mathRect, LanternLandMapConfig.PanelColor);

            // Border.
            DrawRect(sb, new Rectangle(_mathRect.Left, _mathRect.Top, _mathRect.Width, 1),        new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(_mathRect.Left, _mathRect.Bottom - 1, _mathRect.Width, 1), new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(_mathRect.Left, _mathRect.Top, 1, _mathRect.Height),       new Color(255, 255, 255, 60));
            DrawRect(sb, new Rectangle(_mathRect.Right - 1, _mathRect.Top, 1, _mathRect.Height),  new Color(255, 255, 255, 60));

            int pad           = 14;
            float panelScale  = LanternLandMapConfig.PanelFontScale;
            float headerScale = panelScale * 1.12f;
            float bodyScale   = panelScale * 0.92f;

            int titleY        = _mathRect.Top + 10;
            int escY          = titleY + LineH(headerScale) + 2;
            const int bodyStartPad = 12;
            int headerBottom  = escY + LineH(bodyScale) + 10 + bodyStartPad;

            _mathCloseRect    = new Rectangle(_mathRect.Right - 30, _mathRect.Top + 8, 22, 22);

            // Content rect + scrollbar track.
            int scrollBarW    = 8;

            int contentW      = _mathRect.Width - pad * 2 - scrollBarW - 8;
            int contentH      = _mathRect.Bottom - pad - headerBottom;

            if (contentW < 20) contentW = 20;
            if (contentH < 20) contentH = 20;

            _mathContentRect = new Rectangle(
                _mathRect.Left + pad,
                headerBottom,
                contentW,
                contentH);

            _mathScrollTrack = new Rectangle(_mathContentRect.Right + 8 + bodyStartPad, _mathContentRect.Top, scrollBarW, _mathContentRect.Height);

            // Wrap + compute scroll limits.
            EnsureMathReadMeWrapped(bodyScale, _mathContentRect.Width);

            int lh             = LineH(bodyScale) + 2;
            _mathContentHeight = _mathLines.Count * lh;
            _mathScrollMax     = Math.Max(0, _mathContentHeight - _mathContentRect.Height);
            _mathScrollY       = Clamp(_mathScrollY, 0, _mathScrollMax);

            // Compute thumb rect now (header pass will draw it).
            if (_mathScrollMax > 0 && _mathScrollTrack.Height > 0)
            {
                int trackH        = _mathScrollTrack.Height;
                float fracVisible = _mathContentRect.Height / (float)Math.Max(1, _mathContentHeight);

                int thumbH        = Math.Max(28, (int)(trackH * fracVisible));
                int maxThumbMove  = Math.Max(1, trackH - thumbH);

                float fracScroll  = _mathScrollY / (float)Math.Max(1, _mathScrollMax);
                int thumbY        = _mathScrollTrack.Top + (int)(maxThumbMove * fracScroll);

                _mathScrollThumb  = new Rectangle(_mathScrollTrack.X, thumbY, _mathScrollTrack.Width, thumbH);
            }
            else
            {
                _mathScrollThumb  = Rectangle.Empty;
            }
        }

        /// <summary>
        /// Draws ONLY the scrolled body text (the scissor is applied by the caller before Begin()).
        /// </summary>
        private void DrawMathReadMeOverlay_BodyOnly(SpriteBatch sb)
        {
            float bodyScale = LanternLandMapConfig.PanelFontScale * 0.92f;
            int lh          = LineH(bodyScale) + 2;

            int ty          = _mathContentRect.Top - _mathScrollY;

            for (int i = 0; i < _mathLines.Count; i++)
            {
                DrawText(sb, _mathLines[i], _mathContentRect.Left, ty, bodyScale, LanternLandMapConfig.PanelTextColor);
                ty += lh;
            }
        }

        /// <summary>
        /// Draws the header cap + title + "ESC to close" + X button + scrollbar (drawn last so it covers body).
        /// </summary>
        private void DrawMathReadMeOverlay_HeaderOnly(SpriteBatch sb)
        {
            int pad           = 14;
            float panelScale  = LanternLandMapConfig.PanelFontScale;
            float headerScale = panelScale * 1.12f;
            float bodyScale   = panelScale * 0.92f;

            int titleY        = _mathRect.Top + 10;
            int escY          = titleY + LineH(headerScale) + 2;
            int headerBottom  = escY + LineH(bodyScale) + 10;

            // Opaque header cap so body can never appear "under" the title.
            Rectangle headerRect = new Rectangle(_mathRect.Left, _mathRect.Top, _mathRect.Width, headerBottom - _mathRect.Top);
            DrawRect(sb, headerRect, new Color(0, 0, 0, 255)); // Header cap (darker / fully opaque).

            // 1px divider under header.
            DrawRect(sb, new Rectangle(_mathRect.Left, headerBottom - 1, _mathRect.Width, 1), new Color(255, 255, 255, 40));

            DrawText(sb, "Math & Q&A",   _mathRect.Left + pad, titleY, headerScale, LanternLandMapConfig.PanelTextColor);
            DrawText(sb, "ESC to close", _mathRect.Left + pad, escY, bodyScale, new Color(255, 255, 255, 160));

            DrawXButton(sb, _mathCloseRect);

            // Scrollbar.
            if (_mathScrollMax > 0 && _mathScrollTrack != Rectangle.Empty)
            {
                DrawRect(sb, _mathScrollTrack, new Color(255, 255, 255, 18));
                if (_mathScrollThumb != Rectangle.Empty)
                    DrawRect(sb, _mathScrollThumb, new Color(255, 255, 255, 90));
            }
        }
        #endregion

        #region Input / Interaction

        /// <summary>
        /// Handles scroll + close for the Math Read Me modal.
        /// </summary>
        private void HandleMathReadMeInput(InputManager input)
        {
            var mp = input.Mouse.Position;

            if (input.Keyboard.WasKeyPressed(Keys.Escape))
            {
                _showMathReadMe = false;
                _dragMathScroll = false;
                return;
            }

            if (input.Mouse.LeftButtonPressed && _mathCloseRect.Contains(mp))
            {
                _showMathReadMe = false;
                _dragMathScroll = false;
                return;
            }

            // Wheel scroll (only when over the overlay).
            int dw = input.Mouse.DeltaWheel;
            if (dw != 0 && _mathRect.Contains(mp))
            {
                int clicks = dw / 120;
                if (clicks == 0) clicks = (dw > 0) ? 1 : -1;

                float bodyScale = LanternLandMapConfig.PanelFontScale * 0.92f;
                int step = Math.Max(18, LineH(bodyScale) + 8);

                _mathScrollY = Clamp(_mathScrollY - (clicks * step), 0, _mathScrollMax);
            }

            // Drag scrollbar.
            if (_dragMathScroll)
            {
                if (!input.Mouse.LeftButtonDown)
                {
                    _dragMathScroll = false;
                    return;
                }

                if (_mathScrollMax > 0 && _mathScrollTrack.Height > 0 && _mathScrollThumb.Height > 0)
                {
                    int maxThumbMove = Math.Max(1, _mathScrollTrack.Height - _mathScrollThumb.Height);
                    int dy = mp.Y - _dragMathScrollStartMouseY;

                    float t       = dy / (float)maxThumbMove;
                    int newScroll = _dragMathScrollStartScroll + (int)Math.Round(t * _mathScrollMax);
                    _mathScrollY  = Clamp(newScroll, 0, _mathScrollMax);
                }

                return;
            }

            if (input.Mouse.LeftButtonPressed && _mathScrollMax > 0)
            {
                if (_mathScrollThumb.Contains(mp))
                {
                    _dragMathScroll            = true;
                    _dragMathScrollStartMouseY = mp.Y;
                    _dragMathScrollStartScroll = _mathScrollY;
                    return;
                }

                if (_mathScrollTrack.Contains(mp))
                {
                    // Jump thumb roughly to click position, then drag.
                    int trackY       = _mathScrollTrack.Top;
                    int trackH       = _mathScrollTrack.Height;

                    int thumbH       = Math.Max(28, _mathScrollThumb.Height > 0 ? _mathScrollThumb.Height : (trackH / 4));
                    int maxThumbMove = Math.Max(1, trackH - thumbH);

                    float t = (mp.Y - trackY - (thumbH * 0.5f)) / (float)maxThumbMove;
                    t = MathHelper.Clamp(t, 0f, 1f);

                    _mathScrollY = (int)Math.Round(t * _mathScrollMax);

                    _dragMathScroll            = true;
                    _dragMathScrollStartMouseY = mp.Y;
                    _dragMathScrollStartScroll = _mathScrollY;
                }
            }
        }
        #endregion

        #region UI Helpers

        /// <summary>
        /// Simple X close button for overlays.
        /// </summary>
        private void DrawXButton(SpriteBatch sb, Rectangle rect)
        {
            bool hover = rect.Contains(_lastMousePos);

            DrawRect(sb, rect, hover ? new Color(255, 255, 255, 35) : new Color(255, 255, 255, 18));

            Vector2 a1 = new Vector2(rect.Left + 5,  rect.Top + 5);
            Vector2 b1 = new Vector2(rect.Right - 5, rect.Bottom - 5);
            Vector2 a2 = new Vector2(rect.Left + 5,  rect.Bottom - 5);
            Vector2 b2 = new Vector2(rect.Right - 5, rect.Top + 5);

            DrawLine(sb, a1, b1, new Color(255, 255, 255, 180), 2f);
            DrawLine(sb, a2, b2, new Color(255, 255, 255, 180), 2f);
        }
        #endregion

        #region Wrapping

        /// <summary>
        /// Ensures the Math Read Me text is wrapped for the current content width + scale.
        /// Re-wraps only when width/scale changed (avoids doing expensive wrapping every frame).
        /// </summary>
        private void EnsureMathReadMeWrapped(float scale, int widthPx)
        {
            widthPx = Math.Max(50, widthPx);

            if (_mathWrapWidthPx == widthPx && Math.Abs(_mathWrapScale - scale) < 0.0001f && _mathLines != null && _mathLines.Count > 0)
                return;

            _mathWrapWidthPx = widthPx;
            _mathWrapScale   = scale;
            _mathLines       = WrapTextToWidth(MathReadMeText, scale, widthPx);
        }

        /// <summary>
        /// Word-wraps a pre-split array of lines into renderable lines that fit within maxWidthPx.
        /// Preserves blank lines and leading-space indentation (including a simple hanging indent for wraps),
        /// and will hard-break extremely long words if they exceed the available width.
        /// </summary>
        private List<string> WrapTextToWidth(string[] lines, float scale, int maxWidthPx)
        {
            var result = new List<string>();
            if (lines == null || lines.Length == 0 || maxWidthPx <= 8) return result;

            for (int li = 0; li < lines.Length; li++)
            {
                string raw = lines[li] ?? string.Empty;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    result.Add(string.Empty);
                    continue;
                }

                // Preserve leading spaces (indent).
                int indentCount = 0;
                while (indentCount < raw.Length && raw[indentCount] == ' ')
                    indentCount++;

                string indent = (indentCount > 0) ? new string(' ', indentCount) : string.Empty;

                string lineFull = raw.TrimEnd();

                // Fits as-is
                if (MeasureText(lineFull, scale).X <= maxWidthPx)
                {
                    result.Add(lineFull);
                    continue;
                }

                // Wrap words, keeping a hanging indent
                string trimmed = lineFull.TrimStart();
                string[] words = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                string cur     = indent;
                string hang    = indent + "  ";

                for (int i = 0; i < words.Length; i++)
                {
                    string w = words[i];
                    string test = (cur.Length == indent.Length) ? (cur + w) : (cur + " " + w);

                    if (MeasureText(test, scale).X <= maxWidthPx)
                    {
                        cur = test;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(cur))
                        result.Add(cur);

                    cur = hang + w;
                }

                if (!string.IsNullOrWhiteSpace(cur))
                    result.Add(cur);
            }

            return result;
        }
        #endregion

        #endregion

        #region Start Radius Textbox (R)

        /// <summary>Begins editing the Start Radius textbox.</summary>
        private void BeginRadiusTextEdit()
        {
            _editRadiusText = true;
            _radiusBeforeEdit = LanternLandMapState.TargetRadius;

            // Use a simple unformatted number while editing (easier to tweak precisely).
            double v = Math.Round(LanternLandMapState.TargetRadius);
            _radiusText = ((long)v).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Commits the typed radius text (or cancels if invalid/empty).</summary>
        private void CommitRadiusTextEdit()
        {
            if (!_editRadiusText)
                return;

            string raw = (_radiusText ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(raw))
            {
                // Empty -> treat as cancel.
                LanternLandMapState.TargetRadius = _radiusBeforeEdit;
                _editRadiusText = false;
                return;
            }

            // Accept commas/spaces/underscores for readability.
            string cleaned = raw.Replace(",", "").Replace("_", "").Replace(" ", "");

            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) &&
                !double.IsNaN(v) && !double.IsInfinity(v))
            {
                // Radius is in blocks; snap to whole blocks.
                v = Math.Round(v);
                LanternLandMapState.TargetRadius = Clamp(v, LanternLandMapConfig.RadiusMin, LanternLandMapConfig.RadiusMax);
                SaveConfigSafe(force: true);
                MarkConfigDirty();
            }
            else
            {
                LanternLandMapState.TargetRadius = _radiusBeforeEdit;
            }

            _editRadiusText = false;
        }

        /// <summary>Cancels editing and restores the pre-edit radius.</summary>
        private void CancelRadiusTextEdit()
        {
            LanternLandMapState.TargetRadius = _radiusBeforeEdit;
            _editRadiusText = false;
        }

        /// <summary>Consumes keystrokes to build the radius text string.</summary>
        private void HandleRadiusTextTyping(InputManager input)
        {
            if (!_editRadiusText)
                return;

            // Backspace.
            if (input.Keyboard.WasKeyPressed(Keys.Back))
            {
                if (!string.IsNullOrEmpty(_radiusText))
                    _radiusText = _radiusText.Substring(0, _radiusText.Length - 1);
                return;
            }

            // Delete clears.
            if (input.Keyboard.WasKeyPressed(Keys.Delete))
            {
                _radiusText = string.Empty;
                return;
            }

            // Digits (top row).
            for (int d = 0; d <= 9; d++)
            {
                Keys k = Keys.D0 + d;
                if (input.Keyboard.WasKeyPressed(k))
                {
                    AppendRadiusChar((char)('0' + d));
                    return;
                }
            }

            // Digits (numpad).
            for (int d = 0; d <= 9; d++)
            {
                Keys k = Keys.NumPad0 + d;
                if (input.Keyboard.WasKeyPressed(k))
                {
                    AppendRadiusChar((char)('0' + d));
                    return;
                }
            }

            // Optional separators / decimal.
            if (input.Keyboard.WasKeyPressed(Keys.OemComma))
            {
                AppendRadiusChar(',');
                return;
            }

            if (input.Keyboard.WasKeyPressed(Keys.OemPeriod) || input.Keyboard.WasKeyPressed(Keys.Decimal))
            {
                // Only allow one decimal point.
                if (_radiusText != null && _radiusText.IndexOf('.') < 0)
                    AppendRadiusChar('.');
                return;
            }

            // Optional leading minus (mostly useless here, but harmless if you want to clear it quickly).
            if (input.Keyboard.WasKeyPressed(Keys.OemMinus) || input.Keyboard.WasKeyPressed(Keys.Subtract))
            {
                if (string.IsNullOrEmpty(_radiusText))
                    AppendRadiusChar('-');
                return;
            }
        }

        /// <summary>Appends a character to the radius textbox with a safety length cap.</summary>
        private void AppendRadiusChar(char c)
        {
            if (_radiusText == null)
                _radiusText = string.Empty;

            // Keep it bounded so it never becomes absurd to render.
            if (_radiusText.Length >= 32)
                return;

            _radiusText += c;
        }
        #endregion

        #region Map Drawing (SpriteBatch Overlays)

        /// <summary>Draws the map overlays: grid/axes/player/rings/labels/readout.</summary>
        private void DrawMap(SpriteBatch sb)
        {
            // Update ring cache only when the window changes.
            var win = LanternLandMath.ComputeWindow(LanternLandMapState.TargetRadius, LanternLandMapState.RingsToShow);
            if (win.N0 != _cacheN0 || LanternLandMapState.RingsToShow != _cacheA)
            {
                _cacheN0 = win.N0;
                _cacheA  = LanternLandMapState.RingsToShow;
                _rings   = LanternLandMath.BuildRings(_cacheN0, _cacheA);
                _towerKs = LanternLandMath.BuildVisibleTowerKs(_cacheN0, _cacheA);
            }

            // Grid & axes.
            if (LanternLandMapState.ShowChunkGrid)
                DrawChunkGrid(sb);

            DrawAxes(sb);

            // Player marker.
            DrawPlayerMarker(sb);

            // Other player markers.
            if (LanternLandMapState.ShowOtherPlayers)
                DrawOtherPlayersMarkers(sb);

            // Rings (outlines).
            if (LanternLandMapState.ShowLanternRings)
                DrawRingsOutlines(sb);

            // Labels / markers.
            DrawLabels(sb);

            // Cursor readout (like "Desmos hover").
            DrawCursorReadout(sb);
        }

        /// <summary>Draws X/Z axes and origin indicator when visible.</summary>
        private void DrawAxes(SpriteBatch sb)
        {
            // Visible world bounds from the map rect.
            ScreenToWorld(new Point(_mapRect.Left, _mapRect.Top), out double wx0, out double wz0);
            ScreenToWorld(new Point(_mapRect.Right, _mapRect.Bottom), out double wx1, out double wz1);

            double minX = Math.Min(wx0, wx1);
            double maxX = Math.Max(wx0, wx1);
            double minZ = Math.Min(wz0, wz1);
            double maxZ = Math.Max(wz0, wz1);

            // Small pad (in world units) so we don't miss edges due to rounding.
            double pad = 2.0 / Math.Max(_zoom, 1e-6f);
            minX -= pad; maxX += pad;
            minZ -= pad; maxZ += pad;

            // X-axis (Z = 0) only if it can intersect the visible Z range.
            if (0 >= minZ && 0 <= maxZ)
                DrawLine(sb,
                    WorldToScreen(minX, 0),
                    WorldToScreen(maxX, 0),
                    LanternLandMapConfig.AxisColor,
                    LanternLandMapConfig.AxisThickness);

            // Z-axis (X = 0) only if it can intersect the visible X range.
            if (0 >= minX && 0 <= maxX)
                DrawLine(sb,
                    WorldToScreen(0, minZ),
                    WorldToScreen(0, maxZ),
                    LanternLandMapConfig.AxisColor,
                    LanternLandMapConfig.AxisThickness);

            // Origin dot only if visible.
            var o = WorldToScreen(0, 0);
            if (_mapRect.Contains((int)o.X, (int)o.Y))
                DrawRect(sb, new Rectangle((int)o.X - 2, (int)o.Y - 2, 4, 4), LanternLandMapConfig.AxisColor);
        }

        /// <summary>Draws the chunk grid overlay in world-space.</summary>
        private void DrawChunkGrid(SpriteBatch sb)
        {
            // True chunk size in blocks.
            const int step = 16;

            // World bounds visible (screen corners -> world).
            ScreenToWorld(new Point(_mapRect.Left, _mapRect.Top),     out double wx0, out double wz0);
            ScreenToWorld(new Point(_mapRect.Right, _mapRect.Bottom), out double wx1, out double wz1);

            // Correct min/max bounds (your snippet had a Z bug here).
            double minX = Math.Min(wx0, wx1);
            double maxX = Math.Max(wx0, wx1);
            double minZ = Math.Min(wz0, wz1);
            double maxZ = Math.Max(wz0, wz1);

            // Convert to block-space ints for stable snapping.
            long minBlockX = (long)Math.Floor(minX);
            long maxBlockX = (long)Math.Ceiling(maxX);
            long minBlockZ = (long)Math.Floor(minZ);
            long maxBlockZ = (long)Math.Ceiling(maxZ);

            // Snap to chunk boundaries (handles negatives correctly).
            long startX = FloorDiv(minBlockX, step) * step;
            long endX   = CeilDiv(maxBlockX, step) * step;

            long startZ = FloorDiv(minBlockZ, step) * step;
            long endZ   = CeilDiv(maxBlockZ, step) * step;

            // Safety clamp: If you zoom way out, drawing every 16 blocks can be thousands of lines.
            // This DOES NOT change step; it just prevents catastrophic perf.
            const int maxLines = 2000;

            long xCount = (endX - startX) / step;
            long zCount = (endZ - startZ) / step;

            if (xCount > maxLines || zCount > maxLines)
            {
                // Optional: Fade out instead of skipping. If you prefer, remove this early-return.
                // At extreme zoom-out the grid becomes visual noise anyway.
                return;
            }

            // Draw vertical chunk lines.
            for (long i = 0; i <= xCount; i++)
            {
                double x = startX + i * step;
                var p0 = WorldToScreen(x, minZ);
                var p1 = WorldToScreen(x, maxZ);
                DrawLine(sb, p0, p1, LanternLandMapConfig.GridColor, LanternLandMapConfig.GridThickness);
            }

            // Draw horizontal chunk lines.
            for (long i = 0; i <= zCount; i++)
            {
                double z = startZ + i * step;
                var p0 = WorldToScreen(minX, z);
                var p1 = WorldToScreen(maxX, z);
                DrawLine(sb, p0, p1, LanternLandMapConfig.GridColor, LanternLandMapConfig.GridThickness);
            }
        }

        /// <summary>Draws the local player marker in the map view.</summary>
        private void DrawPlayerMarker(SpriteBatch sb)
        {
            try
            {
                var lp = CastleMinerZGame.Instance?.LocalPlayer;
                if (lp == null) return;

                var p = WorldToScreen(lp.LocalPosition.X, lp.LocalPosition.Z);
                if (!_mapRect.Contains((int)p.X, (int)p.Y)) return;

                DrawRect(sb, new Rectangle((int)p.X - 3, (int)p.Y - 3, 6, 6), LanternLandMapConfig.PlayerColor);
            }
            catch { }
        }

        /// <summary>Draws lantern ring outline circles (inner/outer radii) with view culling.</summary>
        private void DrawRingsOutlines(SpriteBatch sb)
        {
            // Cull rings that cannot intersect the current view.
            double viewDist = Math.Sqrt(_viewCenterX * _viewCenterX + _viewCenterZ * _viewCenterZ);

            // Approx visible radius in world units (half-diagonal).
            double halfW = _mapRect.Width / 2d;
            double halfH = _mapRect.Height / 2d;
            double extWorld = Math.Sqrt((halfW / _zoom) * (halfW / _zoom) + (halfH / _zoom) * (halfH / _zoom));

            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                var color = LanternLandMapConfig.RingColors.Length > 0
                    ? LanternLandMapConfig.RingColors[i % LanternLandMapConfig.RingColors.Length]
                    : new Color(255, 255, 255, 120);

                // Intersect test (thin band; use outer radius).
                if (Math.Abs(viewDist - ring.REnd) > (extWorld + 5))
                    continue;

                DrawCircleWorld(sb, ring.RStart, color, LanternLandMapConfig.RingLineThickness * 0.8f);
                DrawCircleWorld(sb, ring.REnd, color, LanternLandMapConfig.RingLineThickness);
            }
        }

        #region Overlay: Other Player Markers

        /// <summary>
        /// Stable cache of remote player colors when "random" mode is enabled.
        /// Summary: Color is derived from Gamertag hash so it stays consistent per player.
        /// </summary>
        private static readonly Dictionary<string, Color> _otherPlayerColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Draws markers for all remote players in the current network session.
        /// Summary:
        /// - Skips the local player.
        /// - Uses Gamertag-stable random colors when enabled, otherwise uses a fixed color.
        /// - Uses a separate dot size setting from the local player marker.
        /// </summary>
        private void DrawOtherPlayersMarkers(SpriteBatch sb)
        {
            try
            {
                var game = CastleMinerZGame.Instance;
                var sess = game?.CurrentNetworkSession;
                if (sess == null) return;

                var localGamer = game?.LocalPlayer?.Gamer;

                int size = LanternLandMapConfig.OtherPlayerDotSizePx;
                if (size < 2) size = 2;

                foreach (NetworkGamer gamer in sess.AllGamers)
                {
                    if (gamer == null) continue;

                    // Skip local.
                    if (localGamer != null && ReferenceEquals(gamer, localGamer))
                        continue;

                    // Tag is typically the Player.
                    if (!(gamer.Tag is Player p)) continue;

                    Vector3 pos = p.LocalPosition;

                    Vector2 scr = WorldToScreen(pos.X, pos.Z);

                    // Quick cull (avoid drawing far off-screen).
                    if (scr.X < _mapRect.Left || scr.X > _mapRect.Right ||
                        scr.Y < _mapRect.Top || scr.Y > _mapRect.Bottom)
                        continue;

                    Color c;
                    if (LanternLandMapConfig.OtherPlayerColorRandom)
                        c = GetStableOtherPlayerColor(gamer.Gamertag);
                    else
                        c = LanternLandMapConfig.OtherPlayerColor;

                    int half = size / 2;

                    var r = new Rectangle(
                        (int)(scr.X - half),
                        (int)(scr.Y - half),
                        size,
                        size);

                    sb.Draw(_px, r, Premul(c));
                }
            }
            catch
            {
                // Best-effort; never break map draw.
            }
        }

        /// <summary>
        /// Returns a stable per-player color for the given Gamertag.
        /// Summary: Uses a deterministic hash so colors remain consistent across frames/sessions.
        /// </summary>
        private static Color GetStableOtherPlayerColor(string gamertag)
        {
            if (string.IsNullOrEmpty(gamertag))
                gamertag = "unknown";

            if (_otherPlayerColors.TryGetValue(gamertag, out var c))
                return c;

            uint h = Fnv1a32(gamertag);

            // Bias toward brighter colors (avoid very dark markers).
            byte r = (byte)(80 + (h & 0x7F));
            byte g = (byte)(80 + ((h >> 8) & 0x7F));
            byte b = (byte)(80 + ((h >> 16) & 0x7F));

            c = new Color(r, g, b, 255);
            _otherPlayerColors[gamertag] = c;
            return c;
        }

        /// <summary>
        /// Simple 32-bit FNV-1a hash for strings.
        /// Summary: Fast, deterministic, and good enough for stable marker coloring.
        /// </summary>
        private static uint Fnv1a32(string s)
        {
            unchecked
            {
                const uint FNV_OFFSET = 2166136261u;
                const uint FNV_PRIME  = 16777619u;

                uint h = FNV_OFFSET;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= FNV_PRIME;
                }
                return h;
            }
        }
        #endregion

        #endregion

        #region Map Fills (Primitives)

        /// <summary>Draws filled rings using triangle strips (scissored to the map rect).</summary>
        private void DrawRingFillsPrimitives(GraphicsDevice device)
        {
            if (_rings == null || _rings.Count == 0) return;

            // Update ring cache if needed (so fill uses the same window as outlines).
            var win = LanternLandMath.ComputeWindow(LanternLandMapState.TargetRadius, LanternLandMapState.RingsToShow);
            if (win.N0 != _cacheN0 || LanternLandMapState.RingsToShow != _cacheA)
            {
                _cacheN0 = win.N0;
                _cacheA  = LanternLandMapState.RingsToShow;
                _rings   = LanternLandMath.BuildRings(_cacheN0, _cacheA);
                _towerKs = LanternLandMath.BuildVisibleTowerKs(_cacheN0, _cacheA);
            }

            if (_zoom <= 0.000001f) return;

            // Ortho projection in screen coordinates.
            var vp                 = device.Viewport;
            _fillEffect.World      = Matrix.Identity;
            _fillEffect.View       = Matrix.Identity;
            _fillEffect.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);

            // Save device states we will touch.
            var oldBlend  = device.BlendState;
            var oldDepth  = device.DepthStencilState;
            var oldRaster = device.RasterizerState;

            // IMPORTANT:
            // - AlphaBlend matches SpriteBatch premultiplied alpha.
            // - _scissorRaster should be ScissorTestEnable=true + CullMode=None (recommended).
            device.BlendState        = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState   = _scissorRaster;

            try
            {
                // ------------------------------------------------------------
                // Stable segmentation for the entire pass (prevents "cracks").
                // ------------------------------------------------------------
                float maxOuterPx = 0f;
                for (int i = 0; i < _rings.Count; i++)
                {
                    float px = (float)(_rings[i].REnd * _zoom);
                    if (px > maxOuterPx) maxOuterPx = px;
                }

                int seg = ComputeSegments(Math.Max(maxOuterPx, 1f));

                // ------------------------------------------------------------
                // Cull rings that cannot intersect the current view.
                // ------------------------------------------------------------
                double viewDist = Math.Sqrt(_viewCenterX * _viewCenterX + _viewCenterZ * _viewCenterZ);

                double halfW    = _mapRect.Width / 2d;
                double halfH    = _mapRect.Height / 2d;
                double extWorld = Math.Sqrt((halfW / _zoom) * (halfW / _zoom) + (halfH / _zoom) * (halfH / _zoom));

                byte a = (byte)Clamp(LanternLandMapConfig.RingFillAlpha, 0, 255);

                for (int i = 0; i < _rings.Count; i++)
                {
                    var ring = _rings[i];

                    // Quick intersect test.
                    if (Math.Abs(viewDist - ring.REnd) > (extWorld + 5))
                        continue;

                    // Pick ring color.
                    Color baseC = (LanternLandMapConfig.RingColors != null && LanternLandMapConfig.RingColors.Length > 0)
                        ? LanternLandMapConfig.RingColors[i % LanternLandMapConfig.RingColors.Length]
                        : new Color(255, 255, 255, 120);

                    // Apply fill alpha.
                    Color fill = new Color(baseC.R, baseC.G, baseC.B, a);

                    // Draw solid annulus (gradient overload, but same color on both edges).
                    DrawAnnulusTriangleStrip(device, ring.RStart, ring.REnd, fill, fill, seg, true, true);
                }
            }
            finally
            {
                // Restore states.
                device.BlendState        = oldBlend;
                device.DepthStencilState = oldDepth;
                device.RasterizerState   = oldRaster;
            }
        }

        /// <summary>Snaps a world radius to the nearest pixel boundary in screen space.</summary>
        private double SnapRadiusToPixel(double r)
        {
            double z         = Math.Max(_zoom, 1e-6f);
            double px        = r * z;
            double snappedPx = Math.Round(px, MidpointRounding.AwayFromZero); // Avoid bankers rounding.
            return snappedPx / z;
        }

        /// <summary>
        /// Draws a filled annulus using a triangle strip (screen-space projection).
        /// Overlap flags can intentionally double-draw edges to emphasize boundaries.
        /// </summary>
        private void DrawAnnulusTriangleStrip(
            GraphicsDevice device,
            double         innerR,
            double         outerR,
            Color          innerColor,
            Color          outerColor,
            int            seg,
            bool           overlapInnerEdge,
            bool           overlapOuterEdge)
        {
            if (outerR <= innerR || outerR <= 0d) return;

            // Control per-edge overlap. Overlap creates the "1px edge ring" look via double-draw;
            // snapping non-overlapped edges keeps boundaries stable without introducing cracks.
            double eps = 0.75 / Math.Max(_zoom, 1e-6f);

            if (overlapInnerEdge)
                innerR = Math.Max(0.0, innerR - eps);
            else
                innerR = SnapRadiusToPixel(innerR);

            if (overlapOuterEdge)
                outerR += eps;
            else
                outerR = SnapRadiusToPixel(outerR);

            if (outerR <= innerR) return;

            Color pmInner = Premul(innerColor);
            Color pmOuter = Premul(outerColor);

            float outerPx = (float)(outerR * _zoom);
            float innerPx = (float)(innerR * _zoom);
            if (outerPx - innerPx < 1.0f) return;

            int vCount = (seg + 1) * 2;
            if (_stripVerts == null || _stripVerts.Length < vCount)
                _stripVerts = new VertexPositionColor[vCount];

            // Fill 0..seg-1, then hard-close by copying the first pair.
            // (Avoids a visible seam from sin(2π) not being exactly 0.)
            for (int i = 0; i < seg; i++)
            {
                double t = (i / (double)seg) * (Math.PI * 2.0);
                double c = Math.Cos(t);
                double s = Math.Sin(t);

                Vector2 o   = WorldToScreen(c * outerR, s * outerR);
                Vector2 inn = WorldToScreen(c * innerR, s * innerR);

                _stripVerts[i * 2 + 0] = new VertexPositionColor(new Vector3(o.X, o.Y, 0), pmOuter);
                _stripVerts[i * 2 + 1] = new VertexPositionColor(new Vector3(inn.X, inn.Y, 0), pmInner);
            }

            _stripVerts[seg * 2 + 0] = _stripVerts[0];
            _stripVerts[seg * 2 + 1] = _stripVerts[1];

            foreach (var pass in _fillEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, _stripVerts, 0, vCount - 2);
            }
        }

        /// <summary>
        /// Draws a filled "square ring" (Chebyshev-distance band) as a triangle strip between an inner and outer square.
        /// Used for WorldGenPlus square-band surface mode underlay rendering.
        /// </summary>
        void DrawSquareRingTriangleStrip(
            GraphicsDevice device,
            double         innerD,
            double         outerD,
            Color          innerColor,
            Color          outerColor,
            int            sideSeg,
            bool           overlapInnerEdge,
            bool           overlapOuterEdge)
        {
            if (outerD <= innerD || outerD <= 0d) return;

            // Overlap controls edge emphasis similar to annulus strips.
            double eps = 0.75 / Math.Max(_zoom, 1e-6f);

            double inner = innerD + (overlapInnerEdge ? -eps : 0.0);
            double outer = outerD + (overlapOuterEdge ? +eps : 0.0);
            if (inner < 0d) inner = 0d;

            // Build a perimeter polyline for each square:
            // We trace clockwise starting at (+D, -D) to match typical screen orientation.
            int perSide = Math.Max(1, sideSeg);
            int pCount  = perSide * 4;

            int vCount = (pCount * 2) + 2;
            if (_stripVerts == null || _stripVerts.Length < vCount)
                _stripVerts = new VertexPositionColor[Math.Max(vCount, 64)];

            // Premultiply alpha.
            var pmInner = new Color(innerColor.R, innerColor.G, innerColor.B, innerColor.A);
            var pmOuter = new Color(outerColor.R, outerColor.G, outerColor.B, outerColor.A);

            // Helper: Perimeter point at index k for distance D.
            Vector2 PerimeterPoint(double d, int k)
            {
                int side = k / perSide;                                        // 0..3.
                int i    = k - side * perSide;
                double t = (perSide == 1) ? 0.0 : (i / (double)(perSide - 1)); // 0..1.

                // side 0: (+d, -d) -> (+d, +d).
                // side 1: (+d, +d) -> (-d, +d).
                // side 2: (-d, +d) -> (-d, -d).
                // side 3: (-d, -d) -> (+d, -d).
                double x, z;
                switch (side)
                {
                    default:
                    case 0: x = +d; z = (-d) + (2.0 * d) * t;  break;
                    case 1: x = (+d) + (-2.0 * d) * t; z = +d; break;
                    case 2: x = -d; z = (+d) + (-2.0 * d) * t; break;
                    case 3: x = (-d) + (2.0 * d) * t; z = -d;  break;
                }

                return WorldToScreen(x, z);
            }

            for (int k = 0; k < pCount; k++)
            {
                Vector2 o = PerimeterPoint(outer, k);
                Vector2 inn = PerimeterPoint(inner, k);

                _stripVerts[k * 2 + 0] = new VertexPositionColor(new Vector3(o.X, o.Y, 0), pmOuter);
                _stripVerts[k * 2 + 1] = new VertexPositionColor(new Vector3(inn.X, inn.Y, 0), pmInner);
            }

            _stripVerts[pCount * 2 + 0] = _stripVerts[0];
            _stripVerts[pCount * 2 + 1] = _stripVerts[1];

            foreach (var pass in _fillEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, _stripVerts, 0, vCount - 2);
            }
        }
        #endregion

        #region Labels / Cursor Readout

        /// <summary>Draws ring labels and tower markers based on tri-state settings.</summary>
        private void DrawLabels(SpriteBatch sb)
        {
            float scale = LanternLandMapConfig.LabelFontScale;

            // Label prefixes.
            string prefixS = LanternLandMapConfig.ToggleStartLabelsKey.ToString().ToLower();
            string prefixE = LanternLandMapConfig.ToggleEndLabelsKey.ToString().ToLower();
            string prefixI = LanternLandMapConfig.ToggleIndexKey.ToString().ToLower();
            string prefixP = LanternLandMapConfig.ToggleThicknessKey.ToString().ToLower();
            string prefixG = LanternLandMapConfig.ToggleGapKey.ToString().ToLower();
            string prefixT = LanternLandMapConfig.ToggleTowersKey.ToString().ToLower();

            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];

                // Start / end X labels lie on +X axis: (floor(r_*), 0).
                if (LanternLandMapConfig.StartLabelMode != TriStateLabelMode.Off)
                    DrawAxisLabel(sb, ring.RStart, 0,  $"{prefixS}={ring.StartX}", LanternLandMapConfig.StartLabelColor, LanternLandMapConfig.StartLabelMode, LanternLandMapConfig.StartLabelYOffsetPx, scale);

                if (LanternLandMapConfig.EndLabelMode != TriStateLabelMode.Off)
                    DrawAxisLabel(sb, ring.REnd, 0,    $"{prefixE}={ring.EndX}", LanternLandMapConfig.EndLabelColor, LanternLandMapConfig.EndLabelMode, LanternLandMapConfig.EndLabelYOffsetPx, scale);

                if (LanternLandMapConfig.IndexLabelMode != TriStateLabelMode.Off)
                    DrawAxisLabel(sb, ring.MidRing, 0, $"{prefixI}={ring.N}", LanternLandMapConfig.IndexLabelColor, LanternLandMapConfig.IndexLabelMode, LanternLandMapConfig.IndexLabelYOffsetPx, scale);

                if (LanternLandMapConfig.ThicknessLabelMode != TriStateLabelMode.Off)
                    DrawAxisLabel(sb, ring.MidRing, 0, $"{prefixP}={ring.ThicknessFloor}", LanternLandMapConfig.ThicknessLabelColor, LanternLandMapConfig.ThicknessLabelMode, LanternLandMapConfig.ThicknessLabelYOffsetPx, scale);

                if (LanternLandMapConfig.GapLabelMode != TriStateLabelMode.Off)
                    DrawAxisLabel(sb, ring.MidGap, 0,  $"{prefixG}={ring.GapFloor}", LanternLandMapConfig.GapLabelColor, LanternLandMapConfig.GapLabelMode, LanternLandMapConfig.GapLabelYOffsetPx, scale);
            }

            // Towers: X = 2^16 * k, where ring index n = k^2 is visible.
            if (LanternLandMapConfig.TowersLabelMode != TriStateLabelMode.Off)
            {
                for (int i = 0; i < _towerKs.Count; i++)
                {
                    long k = _towerKs[i];
                    double x = LanternLandMath.TowerScale * k;

                    var p = WorldToScreen(x, 0);
                    if (!_mapRect.Contains((int)p.X, (int)p.Y)) continue;

                    // marker (always for DotOnly / DotAndLabel).
                    int yOffset = -LanternLandMapConfig.TowerLabelYOffsetPx;
                    int centerX = (int)p.X;
                    int centerY = (int)p.Y + yOffset;

                    // HALF-SIZE multiplier.
                    const float towerPlusScale = 0.5f;

                    int halfLen = Clamp((int)Math.Round(8f * scale * towerPlusScale), 3, 40);
                    int thick = Clamp((int)Math.Round(4f * scale * towerPlusScale), 1, 20);

                    DrawRect(sb,
                        new Rectangle(centerX - (thick / 2), centerY - halfLen, thick, halfLen * 2),
                        LanternLandMapConfig.TowerColor);

                    DrawRect(sb,
                        new Rectangle(centerX - halfLen, centerY - (thick / 2), halfLen * 2, thick),
                        LanternLandMapConfig.TowerColor);

                    if (LanternLandMapConfig.TowersLabelMode == TriStateLabelMode.DotAndLabel)
                    {
                        DrawMapLabelText(sb,
                            $"{prefixT}={k}", // (n={k * k}) - wall index.
                            (int)p.X + 8,
                            (int)p.Y - 18 - LanternLandMapConfig.TowerLabelYOffsetPx,
                            scale,
                            LanternLandMapConfig.TowerLabelColor);
                    }
                }
            }
        }

        /// <summary>Draws a dot at (wx,wz) and optionally text depending on tri-state mode.</summary>
        private void DrawAxisLabel(SpriteBatch sb, double wx, double wz, string text, Color labelColor, TriStateLabelMode mode, int yOffsetPx, float scale)
        {
            var p = WorldToScreen(wx, wz);
            if (!_mapRect.Contains((int)p.X, (int)p.Y)) return;

            // dot always for DotOnly / DotAndLabel.
            int dotSize = Clamp((int)Math.Round(4f * scale), 2, 18);

            DrawRect(sb, new Rectangle((int)p.X - dotSize / 2, (int)p.Y - dotSize / 2 - yOffsetPx, dotSize, dotSize), labelColor);

            if (mode == TriStateLabelMode.DotAndLabel)
                DrawMapLabelText(sb, text, (int)p.X + 6, (int)p.Y - 8 - yOffsetPx, scale, labelColor);
        }

        /// <summary>Draws the bottom "cursor readout" bar (world coords, radius, ring index, biome).</summary>
        private void DrawCursorReadout(SpriteBatch sb)
        {
            var mp = _lastMousePos;
            if (!_mapRect.Contains(mp)) return;

            ScreenToWorld(mp, out double wx, out double wz);

            double r    = Math.Sqrt(wx * wx + wz * wz);
            long n      = LanternLandMath.RingIndexAtRadius(r);
            bool inside = LanternLandMath.IsInsideWall(n, r);

            string biome = LanternLandMapState.ShowBiomesUnderlay
                ? GetSurfaceBiomeReadoutAtWorld(wx, wz, r)
                : null;

            string txt = string.Format(
                "Cursor X {0}  Z {1}  r {2}  n {3}  {4}{5}",
                FormatBig(wx), FormatBig(wz), FormatBig(r), n,
                inside ? "[WALL]" : "[gap]",
                (biome != null) ? ("  |  Biome: " + biome) : ""
            );

            float scale = LanternLandMapConfig.ReadoutFontScale;
            int pad = LanternLandMapConfig.ReadoutPaddingPx;

            Vector2 size = MeasureText(txt, scale);
            if (size.X <= 0f || size.Y <= 0f)
                size = new Vector2(400, 18);
            int barH = (int)Math.Ceiling(size.Y) + pad * 2;
            if (barH > _mapRect.Height) barH = _mapRect.Height;
            int barY = _mapRect.Bottom - barH;

            // Clamp if font got huge.
            if (barY < _mapRect.Top)
                barY = _mapRect.Top;

            DrawRect(sb, new Rectangle(_mapRect.Left, barY, _mapRect.Width, barH), new Color(0, 0, 0, 140));
            DrawText(sb, txt, _mapRect.Left + 10, barY + pad, scale, LanternLandMapConfig.MapTextColor);
        }
        #endregion

        #region Biomes Underlay (Primitives)

        /// <summary>Biomes underlay (matches CastleMinerZBuilder distance bands)</summary>
        private void DrawBiomeUnderlayPrimitives(GraphicsDevice device)
        {
            // Need our annulus strip helper + zoom + ScreenToWorld.
            // If you don't have rings/labels enabled, this still works: it's pure distance bands.
            if (_zoom <= 0.001f) return;

            // Compute visible world-radius range from the 4 map corners.
            ScreenToWorld(new Point(_mapRect.Left, _mapRect.Top), out double ax, out double az);
            ScreenToWorld(new Point(_mapRect.Right, _mapRect.Top), out double bx, out double bz);
            ScreenToWorld(new Point(_mapRect.Left, _mapRect.Bottom), out double cx, out double cz);
            ScreenToWorld(new Point(_mapRect.Right, _mapRect.Bottom), out double dx, out double dz);

            // World corners already computed: ax/az bx/bz cx/cz dx/dz.
            double minX = Math.Min(Math.Min(ax, bx), Math.Min(cx, dx));
            double maxX = Math.Max(Math.Max(ax, bx), Math.Max(cx, dx));
            double minZ = Math.Min(Math.Min(az, bz), Math.Min(cz, dz));
            double maxZ = Math.Max(Math.Max(az, bz), Math.Max(cz, dz));

            // rMax is fine as "max corner radius".
            double rA   = Math.Sqrt(ax * ax + az * az);
            double rB   = Math.Sqrt(bx * bx + bz * bz);
            double rC   = Math.Sqrt(cx * cx + cz * cz);
            double rD   = Math.Sqrt(dx * dx + dz * dz);
            double rMax = Math.Max(Math.Max(rA, rB), Math.Max(rC, rD));

            // rMin should be radius of the closest point in the world-rect to the origin (0,0).
            double closestX = (0.0 < minX) ? minX : (0.0 > maxX) ? maxX : 0.0;
            double closestZ = (0.0 < minZ) ? minZ : (0.0 > maxZ) ? maxZ : 0.0;
            double rMin     = Math.Sqrt(closestX * closestX + closestZ * closestZ);

            // Pad.
            double pad   = 8.0 / Math.Max(_zoom, 0.000001f);
            rMin         = Math.Max(0.0, rMin - pad);
            rMax         = Math.Max(rMin, rMax + pad);

            // ============================================================
            // WorldGenPlus support:
            // - Rings (radial).
            // - Square bands (Chebyshev distance).
            // - Single biome (full-period 1D band).
            // - Random regions (2D sampling).
            // ============================================================
            bool   wgpActive = false;
            int    wgpMode   = 0;      // 0=rings, 1=square, 2=single, 3=random.
            bool   wgpMirror = true;
            double wgpPeriod = 4400.0;

            if (TryGetWorldGenPlusContext(out object wgpCfg, out int wgpSeed))
            {
                wgpActive = true;
                wgpMode   = GetSurfaceMode(wgpCfg);
                wgpMirror = GetCfgBool(wgpCfg, "MirrorRepeat", true);
                wgpPeriod = GetCfgDouble(wgpCfg, "RingPeriod", 4400.0);
                if (wgpPeriod <= 0.0) wgpPeriod = 4400.0;
            }

            // Choose distance metric range based on mode.
            double dMin = rMin;
            double dMax = rMax;

            if (wgpActive && IsSquareSurfaceMode(wgpMode))
            {
                // Square bands => Chebyshev distance.
                double minAbsX = (0.0 < minX) ? minX : (0.0 > maxX) ? maxX : 0.0;
                double minAbsZ = (0.0 < minZ) ? minZ : (0.0 > maxZ) ? maxZ : 0.0;
                dMin = Math.Max(Math.Abs(minAbsX), Math.Abs(minAbsZ));

                double maxAbsX = Math.Max(Math.Abs(minX), Math.Abs(maxX));
                double maxAbsZ = Math.Max(Math.Abs(minZ), Math.Abs(maxZ));
                dMax = Math.Max(maxAbsX, maxAbsZ);

                // Pad in distance space.
                double padD = 8.0 / Math.Max(_zoom, 0.000001f);
                dMin = Math.Max(0.0, dMin - padD);
                dMax = Math.Max(dMin, dMax + padD);
            }

            int segCount = ComputeSegments((float)(dMax * _zoom));

            // Alpha from config.
            byte a = LanternLandMapConfig.BiomeUnderlayAlpha;
            if (a == 0) return;

            // Local helper: Apply the global alpha to the RGB config colors.
            Color WithA(Color rgb) => new Color(rgb.R, rgb.G, rgb.B, a);

            // Biome colors (from config; alpha applied here).
            Color C_CLASSIC  = WithA(LanternLandMapConfig.ClassicBiomeColor);
            Color C_LAGOON   = WithA(LanternLandMapConfig.LagoonBiomeColor);
            Color C_DESERT   = WithA(LanternLandMapConfig.DesertBiomeColor);
            Color C_MOUNTAIN = WithA(LanternLandMapConfig.MountainBiomeColor);
            Color C_ARCTIC   = WithA(LanternLandMapConfig.ArcticBiomeColor);
            Color C_DECENT   = WithA(LanternLandMapConfig.DecentBiomeColor);
            Color C_HELL     = WithA(LanternLandMapConfig.HellBiomeColor);

            // Ortho projection in screen coordinates (same setup as DrawRingFillsPrimitives).
            var vp                 = device.Viewport;
            _fillEffect.World      = Matrix.Identity;
            _fillEffect.View       = Matrix.Identity;
            _fillEffect.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);

            // Match SpriteBatch premultiplied alpha states for primitives.
            var oldBlend             = device.BlendState;
            var oldDepth             = device.DepthStencilState;
            var oldRaster            = device.RasterizerState;

            device.BlendState        = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState   = _scissorRaster;

            try
            {
                double PERIOD = 4400.0;

                // Vanilla "effective distance" breakpoints (0..4400)
                // (These match your CastleMinerZBuilder BuildWorldChunk dist checks)
                // Includes blend zones (we draw those with a small gradient).
                var bands = new[]
                {
                    new BiomeBand(   0, 200, C_CLASSIC,  C_CLASSIC,  false),
                    new BiomeBand( 200, 300, C_CLASSIC,  C_LAGOON,   true),  // Blend.
                    new BiomeBand( 300, 900, C_LAGOON,   C_LAGOON,   false),
                    new BiomeBand( 900,1000, C_LAGOON,   C_DESERT,   true),  // Blend.
                    new BiomeBand(1000,1600, C_DESERT,   C_DESERT,   false),
                    new BiomeBand(1600,1700, C_DESERT,   C_MOUNTAIN, true),  // Blend.
                    new BiomeBand(1700,2300, C_MOUNTAIN, C_MOUNTAIN, false),
                    new BiomeBand(2300,2400, C_MOUNTAIN, C_ARCTIC,   true),  // Blend.
                    new BiomeBand(2400,3000, C_ARCTIC,   C_ARCTIC,   false),
                    new BiomeBand(3000,3600, C_ARCTIC,   C_DECENT,   true),  // Blend (builder sets hellBlender here).
                    new BiomeBand(3600,4400, C_HELL,     C_HELL,     false),
                };
                // WorldGenPlus: RandomRegions draws a 2D underlay (no 1D bands / segments).
                if (wgpActive && IsRandomRegionsSurfaceMode(wgpMode))
                {
                    // Build a lightweight screen-space grid (recomputed every draw; low resolution by design).
                    // If you want higher quality, bump the sample resolution caps below.
                    int cols = Math.Min(160, Math.Max(32, _mapRect.Width / 10));
                    int rows = Math.Min(160, Math.Max(32, _mapRect.Height / 10));

                    bool showEdges = LanternLandMapState.ShowBiomeEdges;

                    // Store a dominant-biome token per cell so we can draw edges between neighboring cells.
                    // (Edge detection is intentionally approximate; it visually matches generated regions at this sampling resolution.)
                    string[,] dom = showEdges ? new string[rows, cols] : null;

                    // 2 triangles per cell (6 vertices).
                    int vNeeded = cols * rows * 6;
                    if (_stripVerts == null || _stripVerts.Length < vNeeded)
                        _stripVerts = new VertexPositionColor[Math.Max(vNeeded, 1024)];

                    int v = 0;

                    for (int ry = 0; ry < rows; ry++)
                    {
                        float y0 = _mapRect.Top + (ry * (_mapRect.Height / (float)rows));
                        float y1 = _mapRect.Top + ((ry + 1) * (_mapRect.Height / (float)rows));
                        float cys = (y0 + y1) * 0.5f;

                        for (int rx = 0; rx < cols; rx++)
                        {
                            float x0 = _mapRect.Left + (rx * (_mapRect.Width / (float)cols));
                            float x1 = _mapRect.Left + ((rx + 1) * (_mapRect.Width / (float)cols));
                            float cxs = (x0 + x1) * 0.5f;

                            // Sample at cell center.
                            ScreenToWorld(new Point((int)cxs, (int)cys), out double wx2, out double wz2);

                            if (!TrySampleRandomRegions(wgpCfg, wgpSeed, (int)Math.Round(wx2), (int)Math.Round(wz2),
                                    out string aType, out string bType, out float bW))
                                continue;

                            byte aUnder = LanternLandMapConfig.BiomeUnderlayAlpha;

                            // NOTE: RandomRegions uses premultiplied alpha colors so it behaves correctly with AlphaBlend.
                            Color ca = MapBiomeTypeToColor(aType, aUnder, wgpSeed);
                            Color c = ca;

                            if (!string.IsNullOrWhiteSpace(bType))
                            {
                                Color cb = MapBiomeTypeToColor(bType, aUnder, wgpSeed);
                                c = Color.Lerp(ca, cb, bW);
                            }

                            if (showEdges)
                            {
                                // Dominant token for boundary checks.
                                string domType = (!string.IsNullOrWhiteSpace(bType) && bW >= 0.5f) ? bType : aType;
                                dom[ry, rx] = domType ?? aType;
                            }

                            // Screen-space quad.
                            _stripVerts[v++] = new VertexPositionColor(new Vector3(x0, y0, 0), c);
                            _stripVerts[v++] = new VertexPositionColor(new Vector3(x1, y0, 0), c);
                            _stripVerts[v++] = new VertexPositionColor(new Vector3(x1, y1, 0), c);

                            _stripVerts[v++] = new VertexPositionColor(new Vector3(x0, y0, 0), c);
                            _stripVerts[v++] = new VertexPositionColor(new Vector3(x1, y1, 0), c);
                            _stripVerts[v++] = new VertexPositionColor(new Vector3(x0, y1, 0), c);
                        }
                    }

                    foreach (var pass in _fillEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        if (v > 0) device.DrawUserPrimitives(PrimitiveType.TriangleList, _stripVerts, 0, v / 3);
                    }

                    // Optional edges between cells with different dominant biome.
                    // This visually matches the existing "biome edges" look for rings/square rings.
                    if (showEdges && dom != null)
                    {
                        // Black with underlay alpha will "darken" the boundary with premultiplied AlphaBlend.
                        Color edgeC = new Color(0, 0, 0, LanternLandMapConfig.BiomeUnderlayAlpha);

                        int maxLines = ((cols - 1) * rows) + ((rows - 1) * cols);
                        int lvNeeded = maxLines * 2;
                        if (_edgeLineVerts == null || _edgeLineVerts.Length < lvNeeded)
                            _edgeLineVerts = new VertexPositionColor[Math.Max(lvNeeded, 1024)];

                        int lv = 0;

                        // Vertical boundaries.
                        for (int ry = 0; ry < rows; ry++)
                        {
                            float y0 = _mapRect.Top + (ry * (_mapRect.Height / (float)rows));
                            float y1 = _mapRect.Top + ((ry + 1) * (_mapRect.Height / (float)rows));

                            for (int rx = 0; rx < cols - 1; rx++)
                            {
                                string aTok = dom[ry, rx];
                                string bTok = dom[ry, rx + 1];

                                if (aTok == null || bTok == null) continue;
                                if (string.Equals(aTok, bTok, StringComparison.Ordinal)) continue;

                                float x = _mapRect.Left + ((rx + 1) * (_mapRect.Width / (float)cols));

                                _edgeLineVerts[lv++] = new VertexPositionColor(new Vector3(x, y0, 0), edgeC);
                                _edgeLineVerts[lv++] = new VertexPositionColor(new Vector3(x, y1, 0), edgeC);
                            }
                        }

                        // Horizontal boundaries.
                        for (int ry = 0; ry < rows - 1; ry++)
                        {
                            float y = _mapRect.Top + ((ry + 1) * (_mapRect.Height / (float)rows));

                            for (int rx = 0; rx < cols; rx++)
                            {
                                string aTok = dom[ry, rx];
                                string bTok = dom[ry + 1, rx];

                                if (aTok == null || bTok == null) continue;
                                if (string.Equals(aTok, bTok, StringComparison.Ordinal)) continue;

                                float x0 = _mapRect.Left + (rx * (_mapRect.Width / (float)cols));
                                float x1 = _mapRect.Left + ((rx + 1) * (_mapRect.Width / (float)cols));

                                _edgeLineVerts[lv++] = new VertexPositionColor(new Vector3(x0, y, 0), edgeC);
                                _edgeLineVerts[lv++] = new VertexPositionColor(new Vector3(x1, y, 0), edgeC);
                            }
                        }

                        if (lv >= 2)
                        {
                            foreach (var pass in _fillEffect.CurrentTechnique.Passes)
                            {
                                pass.Apply();
                                device.DrawUserPrimitives(PrimitiveType.LineList, _edgeLineVerts, 0, lv / 2);
                            }
                        }
                    }

                    return;
                }
                int segStart = (int)Math.Floor(dMin / PERIOD);
                int segEnd   = (int)Math.Floor(dMax / PERIOD);

                BiomeBand[] segBands = bands;
                for (int seg = segStart; seg <= segEnd; seg++)
                {
                    double baseR = seg * PERIOD;

                    // Matches builder:
                    // while (dist > 4400) { dist -= 4400; flips++; }
                    // if ((flips & 1) != 0) dist = 4400 - dist;
                    bool flipped = (!wgpActive || wgpMirror) && ((seg & 1) != 0);
                    if (wgpActive && UsesBandSurfaceSampling(wgpMode))
                    {
                        byte aUnder = LanternLandMapConfig.BiomeUnderlayAlpha;

                        // WorldGenPlus bands may vary by segment (pipe patterns / @Random).
                        if (TryBuildWorldGenPlusBands(wgpCfg, wgpSeed, seg, aUnder, out BiomeBand[] wBands) && wBands != null && wBands.Length > 0)
                            segBands = wBands;

                        // Use WorldGenPlus period for band mapping.
                        PERIOD = wgpPeriod;
                    }
                    for (int i = 0; i < segBands.Length; i++)
                    {
                        var b = segBands[i];

                        // Map effective interval -> local interval depending on flip.
                        double local0 = flipped ? (PERIOD - b.End) : b.Start;
                        double local1 = flipped ? (PERIOD - b.Start) : b.End;

                        double inner = baseR + local0;
                        double outer = baseR + local1;

                        // Clip to visible range.
                        if (outer <= dMin || inner >= dMax) continue;
                        inner = Math.Max(inner, dMin);
                        outer = Math.Min(outer, dMax);
                        if (outer <= inner) continue;

                        bool showEdges = LanternLandMapState.ShowBiomeEdges;

                        // Biome is continuous across segment boundaries (classic at even boundaries, hell at odd),
                        // so don't let the "edge overlap" create a fake ring at those radii.
                        bool startsAtSegBoundary = Math.Abs(local0) < 1e-9;          // Inner edge == baseR.
                        bool endsAtSegBoundary   = Math.Abs(local1 - PERIOD) < 1e-9; // Outer edge == baseR + PERIOD.

                        bool overlapInner = showEdges && !(startsAtSegBoundary && seg > segStart);
                        bool overlapOuter = showEdges && !(endsAtSegBoundary && seg < segEnd);

                        if (!b.IsBlend)
                        {
                            if (wgpActive && IsSquareSurfaceMode(wgpMode))
                                DrawSquareRingTriangleStrip(device, inner, outer, Premul(b.ColorA), Premul(b.ColorA), Math.Max(4, segCount / 4), overlapInner, overlapOuter);
                        else
                            DrawAnnulusTriangleStrip(device, inner, outer, b.ColorA, b.ColorA, segCount, overlapInner, overlapOuter);
                        }
                        else
                        {
                            // In flipped segments, effective distance decreases as radius increases,
                            // so the gradient direction must reverse.
                            Color innerC = flipped ? b.ColorB : b.ColorA; // Inner radius maps to End when flipped.
                            Color outerC = flipped ? b.ColorA : b.ColorB;

                            if (wgpActive && IsSquareSurfaceMode(wgpMode))
                                DrawSquareRingTriangleStrip(device, inner, outer, Premul(innerC), Premul(outerC), Math.Max(4, segCount / 4), overlapInner, overlapOuter);
                        else
                            DrawAnnulusTriangleStrip(device, inner, outer, innerC, outerC, segCount, overlapInner, overlapOuter);
                        }
                    }
                }
            }
            finally
            {
                device.BlendState        = oldBlend;
                device.DepthStencilState = oldDepth;
                device.RasterizerState   = oldRaster;
            }
        }

        /// <summary>Represents a single biome band in "effective distance" space, optionally blended.</summary>
        public readonly struct BiomeBand
        {
            public readonly double Start;
            public readonly double End;
            public readonly Color  ColorA;
            public readonly Color  ColorB;
            public readonly bool   IsBlend;

            // Preserve source biome tokens for readout text.
            public readonly string TypeA;
            public readonly string TypeB;

            public BiomeBand(double start, double end, Color a, Color b, bool blend)
                : this(start, end, a, b, blend, null, null)
            {
            }

            public BiomeBand(double start, double end, Color a, Color b, bool blend, string typeA, string typeB)
            {
                Start   = start;
                End     = end;
                ColorA  = a;
                ColorB  = b;
                IsBlend = blend;
                TypeA   = typeA;
                TypeB   = typeB;
            }
        }
        #endregion

        #region Teleport

        /// <summary>Teleports the player to the requested world XZ coordinate (fixed Y from config).</summary>
        private void TeleportTo(double wx, double wz)
        {
            try
            {
                var pos = new Vector3((float)wx, LanternLandMapConfig.TeleportY, (float)wz);

                // Used by vanilla in multiple places (InGameHUD etc).
                _game.GameScreen.TeleportToLocation(pos, true);
                SendFeedback($"LLMap: Teleported To Location {pos}.");
            }
            catch (Exception ex)
            {
                SendFeedback($"LLMap: Teleport failed - {ex.Message}.");
            }
        }
        #endregion

        #region Helpers

        #region Biome Helpers

        /// <summary>
        /// Returns the biome name (or blend progress text) at the given world radius,
        /// matching CastleMinerZBuilder's 4400-unit mirrored distance pattern.
        /// </summary>
        private static string GetBiomeAtRadius(double r)
        {
            double PERIOD = 4400.0;
            if (r < 0) r = -r;

            int seg      = (int)Math.Floor(r / PERIOD);
            double local = r - seg * PERIOD;            // 0..PERIOD.
            bool forward = (seg & 1) == 0;              // Even = Normal direction, Odd = Reverse direction.

            // "effective dist" used for thresholds.
            double d = forward ? local : (PERIOD - local);

            // Helper for blend text that flips direction on reverse segments.
            string Blend(string a, string b, double start, double end)
            {
                double t = (d - start) / (end - start); // 0..1 in forward space.
                if (!forward) t = 1.0 - t;              // Invert progress when traveling backwards.
                if (t < 0)    t = 0;
                if (t > 1)    t = 1;

                string from = forward ? a : b;
                string to   = forward ? b : a;

                return $"Blend {from} -> {to} {(int)Math.Round(t * 100.0)}%";
            }

            // Bands (same thresholds as your underlay)
            if (d < 200)  return "Classic";
            if (d < 300)  return Blend("Classic", "Lagoon", 200, 300);

            if (d < 900)  return "Lagoon";
            if (d < 1000) return Blend("Lagoon", "Desert", 900, 1000);

            if (d < 1600) return "Desert";
            if (d < 1700) return Blend("Desert", "Mountain", 1600, 1700);

            if (d < 2300) return "Mountain";
            if (d < 2400) return Blend("Mountain", "Arctic", 2300, 2400);

            if (d < 3000) return "Arctic";
            if (d < 3600) return Blend("Arctic", "Decent", 3000, 3600);

            return "Hell";
        }
        #endregion

        #region Coordinate Helpers

        #region Grid Snapping (Chunk/Cell Alignment)

        /// <summary>
        /// Floor division for integers (long) with correct behavior for negative values.
        /// Summary: Returns ⌊a / b⌋ for b > 0 (unlike C# '/' which truncates toward zero).
        /// Useful for snapping world coordinates to fixed-size grid/chunk boundaries.
        /// </summary>
        private static long FloorDiv(long a, long b)
        {
            // b > 0.
            if (a >= 0) return a / b;
            return -(((-a) + b - 1) / b);
        }

        /// <summary>
        /// Ceiling division for integers (long) with correct behavior for negative values.
        /// Summary: Returns ⌈a / b⌉ for b > 0 (unlike C# '/' which truncates toward zero).
        /// Useful for expanding a visible world range to the next grid/chunk boundary.
        /// </summary>
        private static long CeilDiv(long a, long b)
        {
            // b > 0.
            if (a >= 0) return (a + b - 1) / b;
            return -((-a) / b);
        }
        #endregion

        /// <summary>Converts world XZ to screen-space pixel coordinates inside the map rect.</summary>
        private Vector2 WorldToScreen(double wx, double wz)
        {
            float sx = _mapRect.Center.X + (float)((wx - _viewCenterX) * _zoom);
            float sy = _mapRect.Center.Y + (float)((wz - _viewCenterZ) * _zoom);
            return new Vector2(sx, sy);
        }

        /// <summary>Converts a screen-space pixel to world XZ coordinates.</summary>
        private void ScreenToWorld(Point p, out double wx, out double wz)
        {
            wx = _viewCenterX + (p.X - _mapRect.Center.X) / _zoom;
            wz = _viewCenterZ + (p.Y - _mapRect.Center.Y) / _zoom;
        }

        /// <summary>Sets zoom clamped to config min/max.</summary>
        private void SetZoom(float newZoom)
        {
            _zoom = MathHelper.Clamp(newZoom, LanternLandMapConfig.ZoomMinPixelsPerBlock, LanternLandMapConfig.ZoomMaxPixelsPerBlock);
        }

        /// <summary>Zooms while keeping the world point under the cursor stable.</summary>
        private void ZoomAboutCursor(Point mousePos, float zoomMul)
        {
            ScreenToWorld(mousePos, out double beforeX, out double beforeZ);

            SetZoom(_zoom * zoomMul);

            ScreenToWorld(mousePos, out double afterX, out double afterZ);

            // Keep the world point under cursor stable: shift center by the delta.
            _viewCenterX += (beforeX - afterX);
            _viewCenterZ += (beforeZ - afterZ);
        }

        /// <summary>
        /// Returns a biome readout string for the current world position.
        /// If WorldGenPlus is active (and has already patched the builder), uses its dynamic surface settings.
        /// Otherwise falls back to the vanilla (hardcoded) 4400-period rings.
        /// </summary>
        private string GetSurfaceBiomeReadoutAtWorld(double wx, double wz, double rFallback)
        {
            try
            {
                if (TryGetWorldGenPlusContext(out object cfg, out int seed))
                {
                    int mode = GetSurfaceMode(cfg);

                    // Random regions: Sample directly from X/Z.
                    if (IsRandomRegionsSurfaceMode(mode))
                    {
                        if (TrySampleRandomRegions(cfg, seed, (int)Math.Round(wx), (int)Math.Round(wz),
                                out string aType, out string bType, out float bW))
                        {
                            if (string.IsNullOrWhiteSpace(bType)) return ShortBiomeName(aType);

                            int pct = (int)Math.Round(bW * 100f);
                            return $"Blend {ShortBiomeName(aType)} -> {ShortBiomeName(bType)} {pct}%";
                        }

                        return null;
                    }

                    // Rings / square bands / single biome: Use a 1D distance metric with repeat/mirror.
                    double period = GetCfgDouble(cfg, "RingPeriod", 4400.0);
                    if (period <= 0.0) period = 4400.0;

                    bool mirror = GetCfgBool(cfg, "MirrorRepeat", true);

                    // mode: 0=Rings, 1=SquareBands, 2=SingleBiome, 3=RandomRegions.
                    double dist = IsSquareSurfaceMode(mode)
                        ? Math.Max(Math.Abs(wx), Math.Abs(wz)) // Chebyshev distance.
                        : rFallback;                           // Euclidean radius.

                    // Replicate builder's: while (tmp > period) { tmp -= period; flips++; } then optional mirror.
                    double tmp = Math.Abs(dist);
                    int flips = 0;
                    while (tmp > period)
                    {
                        tmp -= period;
                        flips++;
                    }

                    if (mirror && ((flips & 1) == 1))
                        tmp = period - tmp;

                    // Which band contains tmp?
                    if (TryBuildWorldGenPlusBands(cfg, seed, flips, (byte)255, out BiomeBand[] bands))
                    {
                        for (int i = 0; i < bands.Length; i++)
                        {
                            var b = bands[i];
                            if (tmp >= b.Start && tmp <= b.End + 1e-9)
                            {
                                string aName = !string.IsNullOrWhiteSpace(b.TypeA)
                                    ? ShortBiomeName(b.TypeA)
                                    : ShortBiomeNameFromColor(b.ColorA);

                                string bName = !string.IsNullOrWhiteSpace(b.TypeB)
                                    ? ShortBiomeName(b.TypeB)
                                    : ShortBiomeNameFromColor(b.ColorB);

                                if (!b.IsBlend)
                                    return aName;

                                // Blend percent through band (in effective space).
                                double t = (tmp - b.Start) / Math.Max(1e-9, (b.End - b.Start));
                                int pct = (int)Math.Round(t * 100.0);

                                return $"Blend {aName} -> {bName} {pct}%";
                            }
                        }
                    }

                    return "Hell";
                }
            }
            catch { /* Swallow. */ }

            // Vanilla fallback.
            return GetBiomeAtRadius(rFallback);
        }
        #endregion

        #region Drawing Helpers

        /// <summary>Draws a filled rectangle using the 1x1 pixel texture.</summary>
        private void DrawRect(SpriteBatch sb, Rectangle r, Color c)
        {
            sb.Draw(_px, r, Premul(c));
        }

        /// <summary>Draws a subtle horizontal divider and returns the next Y position.</summary>
        private int DrawSeparator(SpriteBatch sb, int y, int padTop, int padBottom)
        {
            // Subtle horizontal divider to visually group controls without requiring big vertical gaps.
            y += padTop;

            int left  = _panelRect.Left + 12;
            int width = _panelRect.Width - 24;

            // Use a 1px (or 2px on very large UI scales) line for clarity.
            int thickness = LanternLandMapConfig.PanelFontScale >= 2.25f ? 2 : 1;

            DrawRect(sb, new Rectangle(left, y, width, thickness), new Color(255, 255, 255, 35));
            y += thickness + padBottom;
            return y;
        }

        /// <summary>Draws an editable numeric textbox with a blinking caret while active.</summary>
        private void DrawNumberTextBox(SpriteBatch sb, Rectangle r, string text, float desiredScale, bool active)
        {
            // Background + outline (keeps it visually consistent with the sliders).
            DrawRect(sb, r, active ? new Color(255, 255, 255, 35) : new Color(255, 255, 255, 22));

            Color outline = active ? new Color(255, 255, 255, 110) : new Color(255, 255, 255, 70);
            DrawRect(sb, new Rectangle(r.Left, r.Top, r.Width, 1), outline);
            DrawRect(sb, new Rectangle(r.Left, r.Bottom - 1, r.Width, 1), outline);
            DrawRect(sb, new Rectangle(r.Left, r.Top, 1, r.Height), outline);
            DrawRect(sb, new Rectangle(r.Right - 1, r.Top, 1, r.Height), outline);

            string s = text ?? string.Empty;

            const int pad = 6;

            // Vertically center against our computed line height.
            int ty = r.Top + (r.Height - LineH(desiredScale)) / 2;

            Vector2 ms = MeasureText(s, desiredScale);

            // If the string is too wide, right-align it (numbers behave nicely this way).
            int tx = r.Left + pad;
            if (ms.X > r.Width - pad * 2)
                tx = r.Right - pad - (int)ms.X;

            DrawText(sb, s, tx, ty, desiredScale, LanternLandMapConfig.PanelTextColor);

            // Caret blink when active.
            if (active)
            {
                bool caretOn = (((int)(_uiTimeSeconds * 2.0)) & 1) == 0; // ~2 Hz
                if (caretOn)
                {
                    int cx = tx + (int)ms.X + 1;
                    cx = Math.Min(cx, r.Right - pad);

                    int ch = Math.Max(8, r.Height - 6);
                    DrawRect(sb, new Rectangle(cx, r.Top + 3, 1, ch), new Color(255, 255, 255, 180));
                }
            }
        }

        /// <summary>Measures text using the chosen font and snapped scale.</summary>
        private Vector2 MeasureText(string text, float desiredScale)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return Vector2.Zero;

            SpriteFont f = PickFontForScale(desiredScale, out float actualScale);
            return f.MeasureString(text) * actualScale;
        }

        /// <summary>Selects the best font (small/large) and a snapped scale for least blur.</summary>
        private SpriteFont PickFontForScale(float desiredScale, out float actualScale)
        {
            // Snap to reduce shimmer while dragging sliders (fractional scales cause the font texture
            // to re-sample from frame to frame depending on camera/pixel rounding).
            desiredScale = SnapScale(desiredScale);

            // If we only have one font, just use it.
            if (_font == null || _smallFont == null || _largeFont == null ||
                _baseFontHeight <= 0f || _smallFontHeight <= 0f || _largeFontHeight <= 0f)
            {
                actualScale = desiredScale;
                return _font ?? _smallFont ?? _largeFont;
            }

            // Interpret desiredScale relative to our base font.
            float desiredPx = _baseFontHeight * desiredScale;

            float sSmall    = desiredPx / _smallFontHeight;
            float sLarge    = desiredPx / _largeFontHeight;

            float costSmall = ScaleCost(sSmall);
            float costLarge = ScaleCost(sLarge);

            if (costLarge < costSmall)
            {
                actualScale = SnapScale(sLarge);
                return _largeFont;
            }

            actualScale = SnapScale(sSmall);
            return _smallFont;
        }

        /// <summary>
        /// Returns a simple "cost" score for a scale value (lower is better / sharper).
        /// </summary>
        private static float ScaleCost(float s)
        {
            if (s <= 0f || float.IsNaN(s) || float.IsInfinity(s)) return float.MaxValue;
            float nearestInt = (float)Math.Round(s);
            float intDist    = Math.Abs(s - nearestInt);

            // Integer-ish scales tend to look the sharpest with SpriteFont + PointClamp.
            return (intDist * 2f) + Math.Abs(s - 1f);
        }

        /// <summary>
        /// Snaps a scale value to 1/8th increments to reduce shimmer and sampling jitter.
        /// </summary>
        private static float SnapScale(float s)
        {
            if (s <= 0f || float.IsNaN(s) || float.IsInfinity(s)) return 0.01f;
            return (float)Math.Round(s * 8f) / 8f; // 1/8 increments.
        }

        /// <summary>Draws text using the best font choice and snapped scale.</summary>
        private void DrawText(SpriteBatch sb, string text, int x, int y, float desiredScale, Color c)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            SpriteFont f = PickFontForScale(desiredScale, out float actualScale);

            // Pixel-snap position to reduce blur/shimmer.
            float fx = x;
            float fy = y;

            sb.DrawString(f, text, new Vector2(fx, fy), Premul(c), 0f, Vector2.Zero, actualScale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draws map label text, optionally using an outline based on config settings.
        /// </summary>
        private void DrawMapLabelText(SpriteBatch sb, string text, int x, int y, float desiredScale, Color fill)
        {
            if (!LanternLandMapConfig.MapLabelOutlineEnabled || LanternLandMapConfig.MapLabelOutlineThicknessPx <= 0)
            {
                DrawText(sb, text, x, y, desiredScale, fill);
                return;
            }

            DrawTextOutlined(sb, text, x, y, desiredScale, fill, LanternLandMapConfig.MapLabelOutlineColor, LanternLandMapConfig.MapLabelOutlineThicknessPx);
        }

        /// <summary>
        /// Draws text with a simple pixel-outline (drawn behind), then draws the fill on top.
        /// </summary>
        private void DrawTextOutlined(SpriteBatch sb, string text, int x, int y, float desiredScale, Color fill, Color outline, int thicknessPx)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            SpriteFont f = PickFontForScale(desiredScale, out float actualScale);

            Vector2 basePos = new Vector2(x, y);

            // Outline (draw behind). Thickness is in screen pixels.
            int t = Math.Min(12, Math.Max(0, thicknessPx));
            if (t > 0)
            {
                for (int dy = -t; dy <= t; dy++)
                {
                    for (int dx = -t; dx <= t; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (dx * dx + dy * dy > t * t) continue; // Circular-ish outline.
                        sb.DrawString(f, text, basePos + new Vector2(dx, dy), Premul(outline), 0f, Vector2.Zero, actualScale, SpriteEffects.None, 0f);
                    }
                }
            }

            // Fill.
            sb.DrawString(f, text, basePos, Premul(fill), 0f, Vector2.Zero, actualScale, SpriteEffects.None, 0f);
        }

        /// <summary>Draws a line segment using a rotated/scaled pixel.</summary>
        private void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 d = b - a;
            float len = d.Length();
            if (len <= 0.001f) return;

            float rot = (float)Math.Atan2(d.Y, d.X);
            sb.Draw(_px, a, null, Premul(color), rot, Vector2.Zero, new Vector2(len, thickness), SpriteEffects.None, 0f);
        }

        /// <summary>Draws a world-space circle by polyline approximation.</summary>
        private void DrawCircleWorld(SpriteBatch sb, double radiusWorld, Color color, float thickness)
        {
            if (radiusWorld <= 0d) return;

            float radiusPx = (float)(radiusWorld * _zoom);

            // If the circle is smaller than a pixel, draw a dot at the origin.
            if (radiusPx < 1f)
            {
                var p = WorldToScreen(0, 0);
                if (_mapRect.Contains((int)p.X, (int)p.Y))
                    DrawRect(sb, new Rectangle((int)p.X, (int)p.Y, 1, 1), color);
                return;
            }

            int seg = ComputeSegments(radiusPx);

            Vector2 prev = Vector2.Zero;
            bool hasPrev = false;

            for (int i = 0; i <= seg; i++)
            {
                double t  = (i / (double)seg) * (Math.PI * 2.0);
                double wx = Math.Cos(t) * radiusWorld;
                double wz = Math.Sin(t) * radiusWorld;

                Vector2 cur = WorldToScreen(wx, wz);

                if (hasPrev)
                    DrawLine(sb, prev, cur, color, thickness);

                prev    = cur;
                hasPrev = true;
            }
        }

        /// <summary>Computes circle segment count based on pixel radius with min/max clamps.</summary>
        private int ComputeSegments(float radiusPx)
        {
            int seg = (int)(radiusPx * 0.15f);
            if (seg < LanternLandMapConfig.CircleSegmentsMin) seg = LanternLandMapConfig.CircleSegmentsMin;
            if (seg > LanternLandMapConfig.CircleSegmentsMax) seg = LanternLandMapConfig.CircleSegmentsMax;
            return seg;
        }
        #endregion

        #region Hotkeys / Config Persistence

        /// <summary>Evaluates a HotkeyBinding against the current input state.</summary>
        private static bool HotkeyPressed(InputManager input, HotkeyBinding hk)
        {
            if (hk.IsEmpty) return false;

            if (!input.Keyboard.WasKeyPressed(hk.Key))
                return false;

            if (hk.Ctrl  && !(input.Keyboard.IsKeyDown(Keys.LeftControl) || input.Keyboard.IsKeyDown(Keys.RightControl))) return false;
            if (hk.Shift && !(input.Keyboard.IsKeyDown(Keys.LeftShift)   || input.Keyboard.IsKeyDown(Keys.RightShift)))   return false;
            if (hk.Alt   && !(input.Keyboard.IsKeyDown(Keys.LeftAlt)     || input.Keyboard.IsKeyDown(Keys.RightAlt)))     return false;

            return true;
        }

        /// <summary>Marks config as dirty so we can save once when drag ends.</summary>
        private void MarkConfigDirty()
        {
            _pendingConfigSave = true;
        }

        /// <summary>Best-effort config save that can be forced or triggered when dirty.</summary>
        private void SaveConfigSafe(bool force = false)
        {
            if (!force && !_pendingConfigSave)
                return;

            try { LanternLandMapConfig.SaveFromState(); }
            catch { /* Best-effort. */ }

            _pendingConfigSave = false;
        }

        /// <summary>Cycles label tri-state: Off -> Check -> Tri.</summary>
        private static TriStateLabelMode Next(TriStateLabelMode mode)
        {
            // Click cycle: Off -> Check -> Tri.
            switch (mode)
            {
                case TriStateLabelMode.Off:         return TriStateLabelMode.DotAndLabel; // Check.
                case TriStateLabelMode.DotAndLabel: return TriStateLabelMode.DotOnly;     // Tri (dot).
                default:                            return TriStateLabelMode.Off;
            }
        }
        #endregion

        #endregion

        #region Misc

        /// <summary>
        /// Clamps an int to the given min/max range.
        /// </summary>
        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>
        /// Clamps a float to the given min/max range.
        /// </summary>
        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>
        /// Clamps a double to the given min/max range.
        /// </summary>
        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>
        /// Converts a color to premultiplied-alpha (SpriteBatch AlphaBlend expects this).
        /// </summary>
        private static Color Premul(Color c)
        {
            if (c.A == 255) return c;

            // XNA SpriteBatch defaults to premultiplied alpha (BlendState.AlphaBlend).
            // If we pass an unpremultiplied color like (255,255,255,25), it looks like a solid white box.
            // Premultiply RGB by A so translucent UI primitives render correctly.
            int a = c.A;
            int r = (c.R * a + 127) / 255;
            int g = (c.G * a + 127) / 255;
            int b = (c.B * a + 127) / 255;
            return new Color((byte)r, (byte)g, (byte)b, (byte)a);
        }

        /// <summary>
        /// Formats large numbers nicely (adds separators and reduces decimal noise).
        /// </summary>
        private static string FormatBig(double v)
        {
            double av = Math.Abs(v);
            if (av >= 1000000d)
                return ((long)Math.Round(v)).ToString("N0");
            if (av >= 1000d)
                return v.ToString("N0");
            return v.ToString("0.###");
        }
        #endregion
    }
}