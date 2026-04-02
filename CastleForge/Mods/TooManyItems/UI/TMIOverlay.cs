/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using System.Collections;
using System.Reflection;
using DNA.CastleMinerZ;
using System.Linq;
using HarmonyLib;
using DNA.Audio;
using System.IO;
using System;

using static TooManyItems.TMIOverlayActions; // Give/take actions, delete mode, etc.
using static TooManyItems.ConfigGlobals;     // NOTE: ConfigGlobals are used across the whole mod.
using static ModLoader.LogSystem;
using static TooManyItems.TMILog;

namespace TooManyItems
{
    /// <summary>
    /// Too Many Items overlay for the inventory screen.
    ///
    /// Highlights:
    /// • Focus-based search box with once-per-press input (no key-repeat spam).
    /// • Left & right toolbars with tooltips and "ON" highlights (supports dynamic tooltip text).
    /// • Item grid draws real inventory icons (safe fallback to a checker texture).
    /// • Separate "Favorites" panel (Alt+Click to add/remove) with paging and wheel-scroll.
    /// • Save slots with reliable per-slot clear [X] and persistence via TMIState.
    /// • Optional sort and "hide unusable" filters; quarantines bad IDs after first failure.
    /// • Page arrows and scroll-wheel paging; clean edge-detection for clicks/keys/wheel.
    /// • Hover tooltips with configurable delays; drawn top-most to avoid being occluded.
    /// • Footer shows mod name, version, and build date.
    ///
    /// Quality-of-life:
    /// • PointerOverUI: Fast hit-test so other patches can gate input when mouse is over TMI.
    /// • Search box handles Backspace/Escape/Space and 0-9/A-Z; hint text when empty.
    /// • Alt modifier adds/removes favorites without giving items; right-click gives 1 item.
    /// • Delete mode integration: Safe click-to-delete outside overlay chrome.
    ///
    /// Configuration:
    /// • Reads settings from ConfigGlobals/TMIConfig (shared across the mod).
    /// • Hot-reload support for the INI (recomputes column width/layout deterministically).
    /// • Fully themable colors, sizes, margins, and toolbar/missing textures.
    ///
    /// Performance & robustness:
    /// • Avoids per-frame allocations; lazy, one-time asset bootstrapping (font/px/atlases).
    /// • Caches InventoryItem icons and item name/description lookups.
    /// • Reflection hot-path minimized (font field cached; AllItems probed defensively).
    /// • Resolution-aware layout with cached rects for hit-testing across frames.
    ///
    /// Design notes:
    /// • Pure static surface (no instances); designed to be called from the draw/update loop.
    /// • All input paths are edge-driven to avoid accidental repeats while holding mouse/keys.
    /// • Fails safe: broken items are marked once and then skipped on future frames.
    /// • No thread affinity beyond the game thread; do not call from background tasks.
    /// </summary>
    internal static class TMIOverlay
    {
        #region Tooltip State (Item & Toolbar)

        public static double     _now;                   // Current time (seconds), copied from GameTime.
        static InventoryItemIDs? _hoverItem;             // Currently hovered item.
        static Rectangle         _hoverRect;             // The cell of the hovered item.
        static double            _hoverBegan = -1;       // When the hover started.

        static readonly Dictionary<InventoryItemIDs, string> _nameCache        = new Dictionary<InventoryItemIDs, string>();
        static readonly Dictionary<InventoryItemIDs, string> _descriptionCache = new Dictionary<InventoryItemIDs, string>();

        public static double      _toolHoverBegan = -1;  // When toolbar hover started.
        static Rectangle[]        _toolbarRects;         // Cached button rects.
        public static string      _toolHoverTip;         // Current tooltip text for toolbar.
        public static bool        _toolHoveredThisFrame; // Reset each draw.

        #endregion

        #region Panels & Pages / Runtime State (Items / Favorites / Settings)

        enum   RightPanel   { Items, Favorites, Settings }
        static RightPanel   _rightPanel = RightPanel.Items;

        enum   HoverContext { None, Items, Favorites }
        static HoverContext _hoverCtx = HoverContext.None;

        static int _favPage; // Page index for favorites.

        // Favorites (order + fast lookup).
        static readonly List<InventoryItemIDs>    _favoritesList = new List<InventoryItemIDs>();
        static readonly HashSet<InventoryItemIDs> _favoritesSet  = new HashSet<InventoryItemIDs>();

        // Settings dropdown state.
        static bool _settingsLoaded;
        static bool _hardBlocksEnabledUI;
        static int  _hardBlockTierUI = 12; // 0-12.

        static bool IsFavorite(InventoryItemIDs id)     => _favoritesSet.Contains(id);
        public static bool AddFavorite(InventoryItemIDs id)    { if (_favoritesSet.Add(id))    { _favoritesList.Add(id); TMIState.SaveFavorites(); return true;    } return false; }
        static bool RemoveFavorite(InventoryItemIDs id) { if (_favoritesSet.Remove(id)) { _favoritesList.Remove(id); TMIState.SaveFavorites(); return true; } return false; }

        static bool AltDown() => Input.Kb.IsKeyDown(Keys.LeftAlt) || Input.Kb.IsKeyDown(Keys.RightAlt);

        static void SetRightPanel(RightPanel mode)
        {
            if (_rightPanel == mode) return;
            _rightPanel      = mode;
            _page            = 0;
            _favPage         = 0;
            _searchFocused   = false; // Defocus search on panel swap.
        }
        #endregion

        #region Persistence Bridge (Favorites + Slot Names)

        internal static InventoryItemIDs[] ExportFavorites() => _favoritesList.ToArray();

        internal static void ImportFavorites(IEnumerable<InventoryItemIDs> seq, bool clearFirst)
        {
            if (clearFirst) { _favoritesSet.Clear(); _favoritesList.Clear(); }
            foreach (var id in seq ?? Enumerable.Empty<InventoryItemIDs>())
                if (_favoritesSet.Add(id)) _favoritesList.Add(id);
        }

        internal static string[] ExportSlotNames() => (_slotNames ?? Array.Empty<string>()).ToArray();

        internal static void ImportSlotNames(string[] names)
        {
            if (names == null) return;
            ResizeSlots(names.Length);
            for (int i = 0; i < names.Length; i++)
                _slotNames[i] = string.IsNullOrWhiteSpace(names[i]) ? null : names[i];
        }
        #endregion

        #region Runtime State & Public Surface

        static SpriteFont _font;
        static Texture2D  _px;

        static bool       _wasToggleDown;
        static bool       _wasReloadDown;
        static string     _search = string.Empty;
        static bool       _searchFocused;
        static int        _page;

        // Cached UI hit-test rectangles and the viewport size they were computed for.
        static Rectangle _lastTop, _lastLeft, _lastRight, _lastSearch, _lastGrid;
        static Point     _lastViewportSize; // (Width, Height) of GraphicsDevice.Viewport at last layout.
        static bool      _rectsValid;       // False until we've drawn at least once for this viewport.

        internal static bool SearchFocused => _searchFocused;

        /// <summary> True if mouse is over any TMI widget (top, left, right, search, grid). </summary>
        public static bool PointerOverUI
        {
            get
            {
                if (!Enabled)
                    return false;

                var game = CastleMinerZGame.Instance;
                var gd   = game?.GraphicsDevice;
                if (gd == null)
                    return false;

                var vp = gd.Viewport;

                // If the window size changed since we last drew the overlay, the cached
                // rects are in the wrong coordinate space. Treat them as invalid so we
                // don't accidentally swallow input after a maximize/resize.
                if (!_rectsValid ||
                    vp.Width  != _lastViewportSize.X ||
                    vp.Height != _lastViewportSize.Y)
                {
                    return false;
                }

                var ms = Mouse.GetState();
                var p  = new Point(ms.X, ms.Y);
                return _lastTop.Contains(p) || _lastLeft.Contains(p) || _lastRight.Contains(p)
                    || _lastSearch.Contains(p) || _lastGrid.Contains(p);
            }
        }

        /// <summary> True if overlay is on and the mouse is inside the right items/favorites panel. </summary>
        public static bool PointerOverRightPanel
        {
            get
            {
                if (!Enabled) return false;
                var ms = Mouse.GetState();
                var p = new Point(ms.X, ms.Y);
                return _lastRight.Contains(p);
            }
        }
        #endregion

        #region Input Edge-Detector (Keyboard/Mouse Snapshot)

        public static class Input
        {
            public static KeyboardState Kb,  PrevKb;
            public static MouseState    Ms,  PrevMs;

            public static void Update() { PrevKb = Kb; PrevMs = Ms; Kb = Keyboard.GetState(); Ms = Microsoft.Xna.Framework.Input.Mouse.GetState(); }
            public static bool KeyPressed(Keys k)   => Kb.IsKeyDown(k) && !PrevKb.IsKeyDown(k);
            public static bool LeftClicked          => Ms.LeftButton  == ButtonState.Pressed  && PrevMs.LeftButton  == ButtonState.Released;
            public static bool RightClicked         => Ms.RightButton == ButtonState.Pressed  && PrevMs.RightButton == ButtonState.Released;
            public static Point Mouse               => new Point(Ms.X, Ms.Y);
            public static int   ScrollDelta         => Ms.ScrollWheelValue - PrevMs.ScrollWheelValue;
        }
        #endregion

        #region Items & Icon Cache

        static InventoryItemIDs[] _allIds = Array.Empty<InventoryItemIDs>();                                                        // Known item IDs.
        static readonly Dictionary<InventoryItemIDs, InventoryItem> _iconCache = new Dictionary<InventoryItemIDs, InventoryItem>(); // InventoryItem instances for Draw2D.

        // Pull game font via reflection only once.
        private static readonly FieldInfo _fiMedFont = AccessTools.Field(typeof(CastleMinerZGame), "_medFont");

        #endregion

        #region Toolbar Definition

        // Spritesheet for toolbar icons (loaded once in EnsureAssets).
        // Expected layout is managed by TMIIconSheet: Columns = distinct icons; rows = OFF/ON states per band.
        static Texture2D _toolbarTex;

        /// <summary>
        /// Declarative description of a toolbar button.
        /// Describe what the button is (icon column + band), and how it behaves (IsOn/Action/Tip).
        /// The renderer takes care of drawing, hover, and tooltips.
        /// </summary>
        struct Tool
        {
            public int          Col;         // Column index in the spritesheet (0-based). Each column is one logical icon.
            public IconBand     Band;        // Which band (atlas row-set) to use: Toolbar, Mini, Indicator, etc.
            public Func<bool>   IsOn;        // Returns true to draw the "ON/active" variant (if the band has one).
            public Action       Action;      // Invoked on left-click (ignored for indicator-only tiles).
            public string       Tip;         // Static tooltip text (shown after delay).
            public Func<string> TipFunc;     // Dynamic tooltip text; if set, this overrides Tip on hover.
            public bool         IsIndicator; // If true, this is a narrow overlay tab drawn next to the previous button.

            // Convenience ctor: Static tooltip.
            public Tool(int col, IconBand band, Action action, string tip, Func<bool> isOn = null, bool isIndicator = false)
            { Col = col; Band = band; Action = action; Tip = tip; TipFunc = null; IsOn = isOn; IsIndicator = isIndicator; }

            // Convenience ctor: Dynamic tooltip (e.g., "Difficulty: X", "Delete mode ON/OFF").
            public Tool(int col, IconBand band, Action action, Func<string> tipFunc, Func<bool> isOn = null, bool isIndicator = false)
            { Col = col; Band = band; Action = action; Tip = null; TipFunc = tipFunc; IsOn = isOn; IsIndicator = isIndicator; }
        }

        // LEFT toolbar (trash, mode, time, difficulty, heal).
        // Notes:
        // • Trash has a paired Indicator tab (same column) that hugs the main button for a status flag UI.
        // • Game modes use IsOn to light the currently active mode.
        // • Time buttons are momentary actions (no ON state).
        // • Difficulty shows a dynamic tooltip so it always reflects the current value.
        // • "Col" is the icon column on the spritesheet; keep them in a consistent order for readability.
        static readonly Tool[] _toolbarLeft =
        {
            // Delete toggle (dynamic tooltip says "ON/OFF"; Shift modifies the meaning to "DELETE ALL").
            new Tool(col: 0, band: IconBand.Toolbar, action: ToggleDelete, tipFunc: () => ShiftHeld()
                ? "DELETE ALL ITEMS from current inventory screen" : $"Delete mode is {(_deleteOn ? "ON" : "OFF")}", isOn: ()=>_deleteOn),

            // Narrow indicator tab that draws beside the delete button (no action; just a visual flag).
            new Tool(col: 0, band: IconBand.Indicator, action: null, tip: "", isOn: ()=>_deleteOn, isIndicator:true),

            // Game mode selectors (ON highlight follows _curMode).
            new Tool(col: 1, band: IconBand.Toolbar, action: ()=>SetMode(GameMode.Endurance),       tip:"Endurance",       isOn: ()=>_curMode==GameMode.Endurance),
            new Tool(col: 2, band: IconBand.Toolbar, action: ()=>SetMode(GameMode.Survival),        tip:"Survival",        isOn: ()=>_curMode==GameMode.Survival),
            new Tool(col: 3, band: IconBand.Toolbar, action: ()=>SetMode(GameMode.DragonEndurance), tip:"DragonEndurance", isOn: ()=>_curMode==GameMode.DragonEndurance),
            new Tool(col: 4, band: IconBand.Toolbar, action: ()=>SetMode(GameMode.Creative),        tip:"Creative",        isOn: ()=>_curMode==GameMode.Creative),
            new Tool(col: 5, band: IconBand.Toolbar, action: ()=>SetMode(GameMode.Exploration),     tip:"Exploration",     isOn: ()=>_curMode==GameMode.Exploration),
            new Tool(col: 6, band: IconBand.Toolbar, action: ()=>SetMode(GameMode.Scavenger),       tip:"Scavenger",       isOn: ()=>_curMode==GameMode.Scavenger),

            // Time-of-day hot actions (no persistent ON state).
            new Tool(col: 7, band: IconBand.Toolbar, action: ()=>SetTime(Time.Sunrise),             tip:"Sunrise"),
            new Tool(col: 8, band: IconBand.Toolbar, action: ()=>SetTime(Time.Noon),                tip:"Noon"),
            new Tool(col: 9, band: IconBand.Toolbar, action: ()=>SetTime(Time.Sunset),              tip:"Sunset"),
            new Tool(col:10, band: IconBand.Toolbar, action: ()=>SetTime(Time.Midnight),            tip:"Midnight"),

            // Difficulty is a toggle/rotator with a live tooltip.
            new Tool(col:11, band: IconBand.Toolbar, action: ToggleDifficulty,                      tipFunc: ()=> $"Difficulty: {_curDifficulty}"),

            // Heal action (momentary).
            new Tool(col:12, band: IconBand.Toolbar, action: MaxHealthAndStamina,                   tip:"Heal")
        };

        // RIGHT toolbar (panel switches).
        // Notes:
        // • These are "mini" buttons (smaller band). IsOn highlights which panel is active.
        // • No indicator tabs here-just simple toggles between two panels.
        static readonly Tool[] _toolbarRight =
        {
            // Column 0: Items panel.
            // Column 1: Favorites panel.
            // Column 2: Settings panel.

            new Tool(col:0, band: IconBand.Mini, action: ()=>SetRightPanel(RightPanel.Items),     tip:"Items",     isOn: ()=> _rightPanel == RightPanel.Items),
            new Tool(col:1, band: IconBand.Mini, action: ()=>SetRightPanel(RightPanel.Favorites), tip:"Favorites", isOn: ()=> _rightPanel == RightPanel.Favorites),
            new Tool(col:2, band: IconBand.Mini, action: ()=>SetRightPanel(RightPanel.Settings),  tip:"Settings",  isOn: ()=> _rightPanel == RightPanel.Settings),
        };
        #endregion

        #region Draw Pipeline (Public Entry)

        /// <summary>
        /// Called from the inventory screen draw. Manages input, toolbars, left/right panels, and footer.
        ///
        /// Notes:
        /// • Early-outs keep work minimal when inventory isn't visible or the overlay is disabled.
        /// • EnsureAssets(...) lazily boots fonts/textures/config once, avoiding per-frame allocations.
        /// • Input.Update() provides clean edge-driven input; HandleToggle() may disable the overlay mid-frame.
        /// • Rects computed here (_lastTop/_lastLeft/_lastRight) are cached for hover tests in other code paths.
        /// • Delete-mode click only fires when the mouse is NOT over TMI chrome to avoid eating UI clicks.
        /// </summary>
        public static void Draw(SpriteBatch sb, bool inventoryIsOpen, GameTime gt)
        {
            // Early-out while Inactive.
            if (!CastleMinerZGame.Instance.IsActive) return;

            // Debug sprite export (once per key-down edge): Handy when developing icon atlas.
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(TMIConfig.LoadOrCreate().DebugKey))
            {
                if (!_exportLatch)
                {
                    TMIExtractor.ExportItemSprites(sb.GraphicsDevice);
                    _exportLatch = true; // Latch so we don't spam every frame while key is held.
                }
            }
            else _exportLatch = false;

            // Delete-mode hook: Only act if the click occurs outside our UI (don't steal UI interactions).
            if (_deleteOn && !MouseOverOurUI() && Clicked())
                TryDeleteUnderMouse();

            // Nothing to draw if the inventory screen isn't up.
            if (!inventoryIsOpen) return;

            // Keep a monotonic "now" (seconds) for hover/tooltip timers even when frames hiccup.
            _now = gt?.TotalGameTime.TotalSeconds ?? _now;

            // One-time bootstraps (config/state/fonts/textures/items) and cached resources.
            var gd = sb.GraphicsDevice;
            EnsureAssets(gd);

            // Pull current input snapshot + run overlay toggle logic (may flip Enabled).
            Input.Update();
            HandleToggle();
            if (!Enabled) return; // Overlay disabled -> bail out early.

            // Compute a screen-space layout grid once and reuse.
            var vp     = gd.Viewport;
            var screen = new Rectangle(0, 0, vp.Width, vp.Height);

            // Remember which resolution these rects are for.
            _lastViewportSize = new Point(vp.Width, vp.Height);

            // Top toolbars (left main + right tabs), with a horizontal separator.
            _toolHoveredThisFrame = false; // Reset per-frame hover bookkeeping.

            // Left toolbar stretches across; right toolbar is pinned to the item column width.
            var rTop      = new Rectangle(MARGIN, MARGIN, screen.Width - MARGIN * 2, TOPBAR_H);
            var rTopRight = new Rectangle(screen.Width - ITEMS_COL_W - MARGIN - CELL_PAD, MARGIN, ITEMS_COL_W + CELL_PAD, TOPBAR_H);

            DrawLeftToolbar(sb, rTop);
            DrawRightToolbar(sb, rTopRight);
            DrawHLine(sb, rTop.Bottom, rTop.X, rTop.Right, ColLine); // Visual divider.

            // If we never hovered a tool this frame, clear any stale tooltip timer/text.
            if (!_toolHoveredThisFrame) { _toolHoverTip = null; _toolHoverBegan = -1; }

            // Left column: Save/load slots panel.
            int leftTopY = rTop.Bottom + MARGIN;
            var rLeft    = new Rectangle(MARGIN, leftTopY, SAVE_BTN_W, screen.Height - leftTopY - MARGIN);
            DrawSaveSlots(sb, rLeft);

            // Right column: Either Items panel (search + pager + grid), Favorites panel, or Settings panel.
            var rRight = new Rectangle(screen.Width - ITEMS_COL_W - MARGIN, leftTopY, ITEMS_COL_W, screen.Height - leftTopY - MARGIN);
            switch (_rightPanel)
            {
                case RightPanel.Items:
                    DrawRightItemsPanel(sb, rRight);
                    break;

                case RightPanel.Favorites:
                    DrawFavoritesPanel(sb, rRight);
                    break;

                case RightPanel.Settings:
                    DrawSettingsPanelRight(sb, rRight);
                    break;
            }

            // Footer: Version/build info at bottom-left.
            DrawFooterInfo(sb, screen);

            // Cache rects for fast "mouse over TMI UI?" checks in input patches and delete-mode guard.
            _lastTop   = rTop;
            _lastLeft  = rLeft;
            _lastRight = rRight;

            // Mark rects as valid for the current viewport. The search/grid rects are
            // cached inside DrawRightItemsPanel, so we don't touch them here.
            _rectsValid = true;
        }
        #endregion

        #region Config & Hot-Reload Application

        static void ApplyConfig(TMIConfig c)
        {
            // These come from ConfigGlobals (shared across mod) or TMIConfig.
            Enabled               = c.Enabled;
            ToggleKey             = c.ToggleKey;
            ShowEnums             = c.ShowEnums;
            ShowBlockIds          = c.ShowBlockIds;
            ShowBlockInfo         = c.ShowBlockInfo;
            ShowItemInfo          = c.ShowItemInfo;

            // Behavior.
            ITEM_COLUMNS          = c.ItemColumns;
            ResizeSlots(c.SaveSlots);
            HideUnusable          = c.HideUnusable;
            SortItems             = c.SortItems;
            MidnightIsNewday      = c.MidnightIsNewday;
            TorchesDropBlock      = c.TorchesDropBlock;

            // Layout.
            MARGIN                = c.Margin;
            TOPBAR_H              = c.TopbarH;
            SEARCH_H              = c.SearchH;
            SAVE_BTN_W            = c.SaveBtnW;
            SAVE_BTN_H            = c.SaveBtnH;
            ITEMS_COL_W           = c.ItemsColW;
            CELL                  = c.Cell;
            CELL_PAD              = c.CellPad;

            // Colors.
            ColPanel              = c.PanelColor;
            ColPanelHi            = c.PanelHiColor;
            ColBtn                = c.ButtonColor;
            ColBtnHot             = c.ButtonHotColor;
            ColXBtn               = c.XButtonColor;
            ColXBtnHot            = c.XButtonHotColor;
            ColLine               = c.LineColor;
            ColText               = c.TextColor;

            // Textures.
            _toolbarTexturePath   = c.ToolbarTexture;
            _missingTexturePath   = c.MissingTexture;

            // Tooltips & sounds.
            USE_TOOLTIPS          = c.UseTooltips;
            TOOLTIP_DELAY_ITEM    = c.ItemDelay;
            TOOLTIP_DELAY_TOOLBAR = c.ToolbarDelay;
            USE_SOUNDS            = c.UseSounds;

            // Logging.
            TMILog.SetLoggingMode(c.LoggingType);
        }

        public static void ApplyConfig(SimpleIni ini)
        {
            // General.
            ShowEnums     = ini.GetBool("General", "ShowEnums",     ShowEnums);
            ShowBlockIds  = ini.GetBool("General", "ShowBlockIds",  ShowBlockIds);
            ShowBlockInfo = ini.GetBool("General", "ShowBlockInfo", ShowBlockInfo);
            ShowItemInfo  = ini.GetBool("General", "ShowItemInfo",  ShowItemInfo);

            // Behavior.
            int cols         = TMIMathHelpers.Clamp(ini.GetInt("Behavior", "ItemColumns", ITEM_COLUMNS ?? 6), 1, 12);
            ITEM_COLUMNS     = cols;
            int cfgSlots     = TMIMathHelpers.Clamp(ini.GetInt("Behavior", "SaveSlots", SAVE_SLOTS), 0, 32);
            ResizeSlots(cfgSlots);
            HideUnusable     = ini.GetBool("Behavior", "HideUnusable", HideUnusable);
            SortItems        = ini.GetBool("Behavior", "SortItems", SortItems);
            MidnightIsNewday = ini.GetBool("Behavior", "MidnightIsNewday", MidnightIsNewday);
            TorchesDropBlock = ini.GetBool("Behavior", "TorchesDropBlock", TorchesDropBlock);

            // Layout.
            MARGIN     = TMIMathHelpers.Clamp(ini.GetInt("Layout", "Margin", MARGIN), 0, 64);
            TOPBAR_H   = TMIMathHelpers.Clamp(ini.GetInt("Layout", "TopbarHeight", TOPBAR_H), 24, 96);
            SEARCH_H   = TMIMathHelpers.Clamp(ini.GetInt("Layout", "SearchHeight", SEARCH_H), 20, 64);
            SAVE_BTN_W = TMIMathHelpers.Clamp(ini.GetInt("Layout", "SaveBtnWidth", SAVE_BTN_W), 120, 320);
            SAVE_BTN_H = TMIMathHelpers.Clamp(ini.GetInt("Layout", "SaveBtnHeight", SAVE_BTN_H), 32, 96);
            CELL       = TMIMathHelpers.Clamp(ini.GetInt("Layout", "Cell", CELL), 32, 64);
            CELL_PAD   = TMIMathHelpers.Clamp(ini.GetInt("Layout", "CellPad", CELL_PAD), 0, 12);

            // Tooltips & sounds.
            USE_TOOLTIPS = ini.GetBool("Tooltips", "UseTooltips", USE_TOOLTIPS);
            USE_SOUNDS   = ini.GetBool("Sounds", "UseSounds", USE_SOUNDS);

            // Logging.
            var mode = ini.GetLoggingType("Logging", "LoggingType", LoggingType.SendFeedback);
            TMILog.SetLoggingMode(mode);

            // Recompute items column width from columns (don't accumulate)
            const int initialBuffer = 17;
            const int columnSpacing = 58;
            ITEMS_COL_W = initialBuffer + (columnSpacing * ITEM_COLUMNS.Value);

            // Reset hover/page to avoid OOB indexes after a layout change
            _page        = 0;
            _hoverItem   = null;
            _hoverBegan  = -1;
        }

        public static void OnHotReload()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "TooManyItems", "TooManyItems.Config.ini");
                if (File.Exists(path))
                {
                    var ini = SimpleIni.Load(path);
                    ApplyConfig(ini);
                    SendLog("Config hot-reloaded.");
                }
            }
            catch (Exception ex)
            {
                if (GetLoggingMode() != LoggingType.None)
                    SendLog($"Config hot-reload failed: {ex.Message}.");
            }
        }
        #endregion

        #region Asset Bootstrapping (Lazy)

        /// <summary>
        /// One-time/lazy asset bootstrap for the TMI overlay.
        /// Called from Draw; each section guards itself so we don't re-do work every frame.
        /// </summary>
        static void EnsureAssets(GraphicsDevice gd)
        {
            // Load INI config once, then push into live fields (colors, sizes, hotkeys, paths, etc.)
            if (!_configApplied)
            {
                var cfg = TMIConfig.LoadOrCreate();
                ApplyConfig(cfg);
                _configApplied = true;
            }

            // Load persisted overlay state once (favorites, slot names, etc.)
            if (!_stateApplied)
            {
                TMIState.LoadAllOnce();
                _stateApplied = true;
            }

            // Load persisted UI state once and apply it.
            if (!_uiInitApplied)
            {
                // If no prior value on disk, keep the current config default.
                Enabled = TMIState.GetOverlayEnabled(defaultIfMissing: Enabled);
                _uiInitApplied = true;
            }

            // Load persisted hard-block settings once.
            if (!_settingsLoaded)
            {
                _hardBlocksEnabledUI = TMIState.GetHardBlocksEnabled(defaultIfMissing: false);
                _hardBlockTierUI     = TMIState.GetHardBlockTier(defaultIfMissing: 12);
                _settingsLoaded      = true;
            }

            // Derive the items-column width once (from configured column count) if not explicitly set.
            // This keeps layout deterministic without recomputing every frame.
            if (!_itemWidthApplied)
            {
                if (ITEM_COLUMNS != null && ITEM_COLUMNS > 0 && (ITEMS_COL_W <= 0))
                {
                    const int initialBuffer = 17;   // left pad to align with other UI
                    const int columnSpacing = 58;   // tuned: CELL + inter-cell padding
                    ITEMS_COL_W = initialBuffer + (columnSpacing * ITEM_COLUMNS.Value);
                    _itemWidthApplied = true;
                }
            }

            // Create a 1x1 white pixel texture once (used for solid fills, lines, X-mark, etc.)
            if (_px == null)
            {
                _px = new Texture2D(gd, 1, 1);
                _px.SetData(new[] { Color.White });
            }

            // Grab the game's medium font via cached reflection (so our text matches CMZ UI).
            if (_font == null)
            {
                _font = (SpriteFont)_fiMedFont.GetValue(CastleMinerZGame.Instance);
            }

            // Populate the full item ID list once from the injector (fallback to empty if injector not ready).
            if (_allIds.Length == 0)
            {
                try { _allIds = TMIItemInjector.AllIds.ToArray(); }
                catch { _allIds = Array.Empty<InventoryItemIDs>(); }
            }

            // Load the toolbar sprite sheet (path can be absolute or relative to game root).
            // Also bind to the icon sheet helper and compute how many columns it contains.
            if (_toolbarTex == null)
            {
                try
                {
                    _toolbarTexturePath = _toolbarTexturePath ?? TMIConfig.LoadOrCreate().ToolbarTexture;
                    var full = Path.IsPathRooted(_toolbarTexturePath)
                        ? _toolbarTexturePath
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _toolbarTexturePath);

                    using (var s = File.OpenRead(full))
                        _toolbarTex = Texture2D.FromStream(gd, s);

                    TMIIconSheet.Bind(_toolbarTex);
                    _toolbarCols = Math.Max(1, _toolbarTex.Width / (TMIIconSheet.Tile + TMIIconSheet.Pad));
                }
                catch
                {
                    // Fail safe: Leave null -> Buttons will render labels instead of icons.
                    _toolbarTex = null;
                }
            }

            // Load the "missing" fallback texture (checkerboard) used when an item icon can't be drawn.
            if (_missingTex == null)
            {
                try
                {
                    _missingTexturePath = _missingTexturePath ?? TMIConfig.LoadOrCreate().MissingTexture;
                    var full = Path.IsPathRooted(_missingTexturePath)
                        ? _missingTexturePath
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _missingTexturePath);

                    using (var s = File.OpenRead(full))
                        _missingTex = Texture2D.FromStream(gd, s);
                }
                catch
                {
                    // Fail safe: Keep null -> Code will generate an in-memory checker texture on demand.
                    _missingTex = null;
                }
            }
        }
        #endregion

        #region Toolbars (left & right)

        // Draws the LEFT toolbar (main tools + optional slim indicator tabs).
        // Notes:
        // • Buttons come from a sprite atlas via TMIIconSheet (OFF/ON frames).
        // • "Indicator" entries are narrow overlay tabs that visually hug the previous button.
        // • Hover = hot state color; click triggers the tool's Action.
        // • Track which index was hovered this frame to drive delayed tooltips.
        static void DrawLeftToolbar(SpriteBatch sb, Rectangle r)
        {
            // Panel background.
            Fill(sb, r, ColPanelHi);

            // Layout: Square buttons sized to the bar height, with small padding.
            int x = r.X + 4, y = r.Y + 4, h = r.Height - 8, w = h;

            // Ensure we have one rect per tool (useful for future hit-tests/automation).
            if (_toolbarRects == null || _toolbarRects.Length != _toolbarLeft.Length)
                _toolbarRects = new Rectangle[_toolbarLeft.Length];

            int hoveredThisFrame = -1;

            for (int i = 0; i < _toolbarLeft.Length; i++)
            {
                var tool = _toolbarLeft[i];
                bool on  = tool.IsOn?.Invoke() == true; // "ON" state drives which row in the icon sheet is chosen.

                // Pick the source rect: Full tile for normal buttons, content-cropped tile for indicators.
                Rectangle src = tool.IsIndicator
                    ? TMIIconSheet.ContentSrc(tool.Band, tool.Col, on) // Preserves art's true width.
                    : TMIIconSheet.Src(tool.Band, tool.Col, on);

                // Scale target size:
                // • Normal:    Square (hxh).
                // • Indicator: Keep sprite aspect ratio (e.g., 16x24).
                int newH = r.Height - 8;
                int newW = tool.IsIndicator
                    ? Math.Max(1, (int)Math.Round(h * (src.Width / (float)src.Height)))
                    : h; // Square for normal buttons.

                // "Indicator" tags visually hug the previous button: small left shift + negative gap.
                int gap  = tool.IsIndicator ? -src.Width : 6;
                int newX = tool.IsIndicator ? x - 6 : x;

                var  rr  = new Rectangle(newX, y, newW, newH);
                bool hot = !tool.IsIndicator && rr.Contains(Input.Mouse); // Ensure indicators never become "hot".

                // Button chrome.
                Fill(sb, rr, hot ? ColBtnHot : ColBtn);

                // Icon (atlas) or fallback debug label if atlas missing.
                string fallbackTipText = _toolbarLeft[i].TipFunc?.Invoke() // Get a displayable string.
                                      ?? _toolbarLeft[i].Tip
                                      ?? string.Empty;
                if (_toolbarTex != null && _toolbarCols > 0) sb.Draw(_toolbarTex, rr, src, Color.White);
                else DrawStringCentered(sb, rr, $"{Abbrev(fallbackTipText)}", ColText);

                // Track hover index for tooltip logic at the end of the pass.
                if (hot) hoveredThisFrame = i;

                // Only "real" buttons are clickable (indicators are purely visual).
                if (hot && Input.LeftClicked && !tool.IsIndicator)
                {
                    _toolbarLeft[i].Action?.Invoke();
                    _toolHoverBegan = -1; // Reset hover timer to avoid stale tooltip flashing after click.
                }

                // Advance X for next button (indicator uses a negative gap to overlap slightly).
                x += w + gap;
            }

            // Tooltip bookkeeping: Update the current tip only when the hovered button actually changed.
            if (hoveredThisFrame >= 0)
            {
                var tip = _toolbarLeft[hoveredThisFrame].TipFunc?.Invoke()
                          ?? _toolbarLeft[hoveredThisFrame].Tip
                          ?? string.Empty;

                if (!string.Equals(_toolHoverTip, tip, StringComparison.Ordinal))
                {
                    _toolHoverTip   = tip;
                    _toolHoverBegan = _now; // Start tooltip delay timer.
                }
                _toolHoveredThisFrame = true;
            }
        }

        /// <summary>
        /// Draws the RIGHT toolbar (panel toggles, e.g., Items / Favorites).
        /// Notes:
        /// • Same rendering rules as the left bar.
        /// • Typically smaller set: Just view-mode toggles with "ON" highlight.
        /// </summary>
        static void DrawRightToolbar(SpriteBatch sb, Rectangle r)
        {
            // Same baseline geometry: pad in 4px, square buttons.
            int x = r.X + 4, y = r.Y + 4, h = r.Height - 8, w = h;
            int hoveredThisFrame = -1;

            for (int i = 0; i < _toolbarRight.Length; i++)
            {
                var tool = _toolbarRight[i];
                bool on  = tool.IsOn?.Invoke() == true;

                Rectangle src = tool.IsIndicator
                    ? TMIIconSheet.ContentSrc(tool.Band, tool.Col, on)
                    : TMIIconSheet.Src(tool.Band, tool.Col, on);

                int newH = r.Height - 8;
                int newW = tool.IsIndicator
                    ? Math.Max(1, (int)Math.Round(h * (src.Width / (float)src.Height)))
                    : h;

                int gap  = tool.IsIndicator ? -src.Width : 6;
                int newX = tool.IsIndicator ? x - 6 : x;

                var  rr  = new Rectangle(newX, y, newW, newH);
                bool hot = !tool.IsIndicator && rr.Contains(Input.Mouse);

                Fill(sb, rr, hot ? ColBtnHot : ColBtn);

                string fallbackTipText = _toolbarRight[i].TipFunc?.Invoke() // Get a displayable string.
                                      ?? _toolbarRight[i].Tip
                                      ?? string.Empty;
                if (_toolbarTex != null && _toolbarCols > 0) sb.Draw(_toolbarTex, rr, src, Color.White);
                else DrawStringCentered(sb, rr, $"{Abbrev(fallbackTipText)}", ColText);

                if (hot) hoveredThisFrame = i;

                if (hot && Input.LeftClicked && !tool.IsIndicator)
                {
                    _toolbarRight[i].Action?.Invoke();
                    _toolHoverBegan = -1; // Prevent stale tooltip flicker after click.
                }

                x += w + (tool.IsIndicator ? -src.Width : 6);
            }

            if (hoveredThisFrame >= 0)
            {
                var tip = _toolbarRight[hoveredThisFrame].TipFunc?.Invoke()
                          ?? _toolbarRight[hoveredThisFrame].Tip
                          ?? string.Empty;

                if (!string.Equals(_toolHoverTip, tip, StringComparison.Ordinal))
                {
                    _toolHoverTip   = tip;
                    _toolHoverBegan = _now;
                }
                _toolHoveredThisFrame = true;
            }
        }
        #endregion

        #region Left Panel: Save Slots

        /// <summary> Renders the left-side save-slot panel and handles clicks for Save/Load/Clear. </summary>
        static void DrawSaveSlots(SpriteBatch sb, Rectangle r)
        {
            // Compute how tall the panel wants to be for the current number of slots,
            // then clamp so it never draws past 'r' (prevents overlap at small resolutions).
            int slotCount     = SAVE_SLOTS;
            int desiredHeight = 8 + slotCount * (SAVE_BTN_H + 8); // Padding + N*(btn + gap).
            int usedHeight    = Math.Min(r.Height, desiredHeight);
            var panel         = new Rectangle(r.X, r.Y, r.Width, usedHeight);

            // Panel background (solid so mouse-hit tests are easy & predictable).
            Fill(sb, panel, ColPanel);

            // Walk each slot and draw either a "Save" button (empty) or "Load + [X]" (occupied).
            int y = r.Y + 8;
            for (int i = 0; i < slotCount; i++)
            {
                // If the next button would run past the panel, stop drawing (clamped height).
                if (y + SAVE_BTN_H > panel.Bottom) break;

                // Main button area (full row) and the trailing [X] clear button (only when occupied).
                var btnR = new Rectangle(r.X + 8, y, r.Width - 16, SAVE_BTN_H);
                var xBtn = new Rectangle(btnR.Right - 40, btnR.Y + 8, 32, btnR.Height - 16);

                // Hover states:
                // - overX:   mouse over the [X] clear button (only when a name exists).
                // - hotMain: mouse over the main button but not the [X] area.
                bool overX = _slotNames[i] != null && xBtn.Contains(Input.Mouse);
                bool hotMain = btnR.Contains(Input.Mouse) && !overX;

                // Main button: "Save N" if empty, "Load N" if occupied.
                Fill(sb, btnR, hotMain ? ColBtnHot : ColBtn);
                string label = _slotNames[i] == null ? $"Save {i + 1}" : $"Load {i + 1}";
                DrawStringCentered(sb, btnR, label, ColText);

                // Draw and handle the [X] clear button, but only for occupied slots.
                if (_slotNames[i] != null)
                {
                    Fill(sb, xBtn, overX ? ColXBtnHot : ColXBtn);
                    DrawCenteredX(sb, xBtn, ColText);

                    // Edge-triggered click: if user clicked the [X], clear the slot and
                    // continue to the next row (prevents the main button from also firing).
                    if (overX && Clicked())
                    {
                        ClearSlot(i);
                        y += SAVE_BTN_H + 8;
                        continue;
                    }
                }

                // Main button click:
                // - Empty slot: Save current inventory snapshot into slot i.
                // - Occupied:   Load slot i into the player's inventory.
                if (hotMain && Clicked())
                {
                    if (_slotNames[i] == null) SaveSlot(i);
                    else LoadSlot(i);
                }

                // Advance to the next row.
                y += SAVE_BTN_H + 8;
            }
        }

        /// <summary>
        /// Grows/shrinks the number of save slots (1..32), preserving existing names
        /// and persisting the new size+names via TMIState.
        /// </summary>
        static void ResizeSlots(int newCount)
        {
            // Clamp to sane bounds (UI is tuned for this range).
            newCount = Math.Max(1, Math.Min(32, newCount));

            // No work if unchanged.
            if (newCount == SAVE_SLOTS) return;

            // Create a resized array and copy as many existing names as will fit.
            var newArr = new string[newCount];
            if (_slotNames != null)
                Array.Copy(_slotNames, newArr, Math.Min(_slotNames.Length, newCount));

            // Swap in the new storage and remember the new logical size.
            _slotNames = newArr;
            SAVE_SLOTS = newCount;

            // Persist names and count so it survives restarts.
            TMIState.SaveNames();
        }
        #endregion

        #region Right Panel: Items (Search + Pager + Grid)

        /// <summary>
        /// Tracks how many pages the last computed grid had.
        /// Used to clamp _page and to render the "x / y" label.
        /// </summary>
        static int _lastPageCount = 1;

        /// <summary>
        /// Right column: Search bar + pager + items grid.
        /// Lays out widgets, clamps the current page, then draws each piece.
        /// Caches the last search/grid rectangles for outside hit-tests.
        /// </summary>
        static void DrawRightItemsPanel(SpriteBatch sb, Rectangle r)
        {
            Fill(sb, r, ColPanel);

            // Layout: Compute grid first (independent of search width).
            int innerY      = r.Y + 8;
            int gridTop     = innerY + SEARCH_H + 10;
            var gridR       = new Rectangle(r.X + 8, gridTop, r.Width - 16, r.Bottom - 8 - gridTop);

            // Page count depends on actual grid size; clamp current page into range.
            _lastPageCount  = ComputePageCount(gridR);
            _page           = TMIMathHelpers.Clamp(_page, 0, _lastPageCount - 1);

            // Pager widgets (<<  [x/y]).
            string pageText = $"{_page + 1}/{_lastPageCount}";
            int pageTextW   = (int)(_font?.MeasureString(pageText).X ?? 40) + 10;

            int gap         = 6;
            int arrowSize   = SEARCH_H - 4;
            int pagerW      = arrowSize * 2 + gap * 2 + pageTextW;
            int pagerX      = r.Right - 8 - pagerW;

            var leftArrow   = new Rectangle(pagerX, innerY, arrowSize, SEARCH_H);
            var rightArrow  = new Rectangle(leftArrow.Right + gap, innerY, arrowSize, SEARCH_H);
            var pageRect    = new Rectangle(rightArrow.Right + gap, innerY, pageTextW, SEARCH_H);

            // Search consumes remaining width to the left of the pager.
            var searchR     = new Rectangle(r.X + 8, innerY, Math.Max(64, leftArrow.X - (r.X + 8) - gap), SEARCH_H);

            // Draw in z-order (top -> bottom).
            bool isHoldingItem = IsHoldingCursorItem();               // Check if the cursor is holding an item this frame.
            DrawSearch(sb, searchR);                                  // Text box (handles focus + key input).
            DrawPager(sb, leftArrow, rightArrow, pageRect, pageText); // Arrows + "x/y".
            HandleDeleteDrop(r);                                      // Handle items being "Dropped" into the items panel.
            DrawItemsGrid(sb, gridR, isHoldingItem);                  // Grid (handles wheel paging + clicks).
                                                                      // Pass the holding item bool to skip this frame.

            // Cache rects so external code can gate input when mouse is over our UI.
            _lastSearch = searchR;
            _lastGrid   = gridR;
            _rectsValid = true; // Now all 5 rects are valid for the current viewport.
        }

        /// <summary>
        /// Computes how many pages the items grid will need for the given rectangle,
        /// honoring CELL and CELL_PAD. Returns at least 1.
        /// </summary>
        static int ComputePageCount(Rectangle gridR)
        {
            var items = FilteredItems();
            int cols  = Math.Max(1, (gridR.Width  + CELL_PAD) / (CELL + CELL_PAD));
            int rows  = Math.Max(1, (gridR.Height + CELL_PAD) / (CELL + CELL_PAD));
            int per   = Math.Max(1, cols * rows);
            return Math.Max(1, (items.Count + per - 1) / per); // ceil(count / per).
        }

        /// <summary>
        /// Draws left/right pager buttons and the "x / y" page label,
        /// with once-per-press navigation and safe clamping.
        /// </summary>
        static void DrawPager(SpriteBatch sb, Rectangle l, Rectangle r, Rectangle txtR, string pageText)
        {
            bool hotL = l.Contains(Input.Mouse), hotR = r.Contains(Input.Mouse);
            Fill(sb, l, hotL ? ColBtnHot : ColBtn);
            Fill(sb, r, hotR ? ColBtnHot : ColBtn);

            DrawStringCentered(sb, l, "<", ColText);
            DrawStringCentered(sb, r, ">", ColText);

            // Edge-triggered clicks; ClearHover() prevents stale item tooltip after paging.
            if (hotL && Input.LeftClicked) { _page = TMIMathHelpers.Clamp(_page - 1, 0, _lastPageCount - 1); ClearHover(); }
            if (hotR && Input.LeftClicked) { _page = TMIMathHelpers.Clamp(_page + 1, 0, _lastPageCount - 1); ClearHover(); }

            // Align the x/y text to the right edge of the label rect.
            DrawString(sb, txtR, pageText, ColText, alignRight: true);
        }

        /// <summary>
        /// Search textbox:
        /// • Click to focus/blur.
        /// • ESC clears/blur.
        /// • Backspace, Space, A-Z, 0-9 handled as once-per-press (no key-repeat flood).
        /// </summary>
        static void DrawSearch(SpriteBatch sb, Rectangle r)
        {
            // Focus/blur by click (consumes the edge so downstream widgets don't double-handle).
            if (ClickLatch.ConsumeLeft())
                _searchFocused = r.Contains(Mouse.GetState().X, Mouse.GetState().Y);

            Fill(sb, r, Color.Black * 0.55f);

            // ESC while focused -> blur (and optionally clear via branch below).
            var kbs = Keyboard.GetState();
            if (_searchFocused && kbs.IsKeyDown(Keys.Escape))
                _searchFocused = false;

            if (_searchFocused)
            {
                // Edge-driven keystrokes only (ignore held keys to avoid character spam).
                foreach (var key in Input.Kb.GetPressedKeys())
                {
                    if (Input.PrevKb.IsKeyDown(key)) continue;

                    if (key == Keys.Back)   { if (!string.IsNullOrEmpty(_search)) _search = _search.Substring(0, _search.Length - 1); continue; }
                    if (key == Keys.Escape) { _search = string.Empty; _searchFocused = false; continue; }
                    if (key == Keys.Space)  { _search += " "; continue; }

                    // Minimal map: A-Z, top-row 0-9, numpad 0-9 (lowercased).
                    if (key >= Keys.A && key <= Keys.Z)                     _search += key.ToString().ToLowerInvariant();
                    else if (key >= Keys.D0      && key <= Keys.D9)         _search += ((int)key - (int)Keys.D0).ToString();
                    else if (key >= Keys.NumPad0 && key <= Keys.NumPad9)    _search += ((int)key - (int)Keys.NumPad0).ToString();
                }
            }

            // Hint style when empty; same draw path as content (just dimmer color).
            var hint = string.IsNullOrEmpty(_search) ? "Search" : _search;
            var col  = string.IsNullOrEmpty(_search) ? ColText * 0.45f : ColText;
            DrawString(sb, r, hint, col, padX: 8, padY: 4);
        }

        /// <summary>
        /// Items grid:
        /// • Computes cols/rows from available pixels.
        /// • Supports mouse-wheel paging when pointer is inside the grid.
        /// • Alt+Click favorites; L/R click gives full stack / single.
        /// • Gold corner mark indicates a favorite.
        /// </summary>
        static void DrawItemsGrid(SpriteBatch sb, Rectangle r, bool isHoldingItem)
        {
            var items = FilteredItems();

            // Per-page capacity from geometry.
            int cols  = Math.Max(1, (r.Width  + CELL_PAD) / (CELL + CELL_PAD));
            int per   = Math.Max(1, (r.Height + CELL_PAD) / (CELL + CELL_PAD)) * cols;
            int pages = Math.Max(1, (items.Count + per - 1) / per);

            // Wheel paging only when mouse is inside the grid rect.
            if (r.Contains(Input.Mouse) && Input.ScrollDelta != 0)
            {
                if (Input.ScrollDelta < 0) _page++;
                if (Input.ScrollDelta > 0) _page--;
                ClearHover();
            }

            _page = TMIMathHelpers.Clamp(_page, 0, pages - 1);
            int start = _page * per, end = Math.Min(items.Count, start + per);

            int x = r.X, y = r.Y;
            bool hoveredThisFrame = false;

            for (int i = start; i < end; i++)
            {
                var id   = items[i];
                var cell = new Rectangle(x, y, CELL, CELL);

                // Track hover per-cell so tooltips can time-in after a short delay.
                TrackHover(cell, id, HoverContext.Items);

                bool hot = cell.Contains(Input.Mouse);
                if (hot) hoveredThisFrame = true;

                // Cell background (hot vs normal).
                Fill(sb, cell, hot ? new Color(60, 60, 80, 230) : new Color(35, 35, 50, 220));

                // Draw icon (uses game's icon when possible; safe fallback otherwise).
                try { DrawItemIcon(sb, new Rectangle(cell.X + 2, cell.Y + 2, cell.Width - 4, cell.Height - 4), id); } catch { }

                // Favorite mark (small gold square in bottom-right).
                if (IsFavorite(id))
                {
                    var mark = new Rectangle(cell.Right - 10, cell.Bottom - 10, 8, 8);
                    Fill(sb, mark, new Color(255, 215, 0, 220));
                }

                // Click interactions (edge-driven).
                if (hot && !isHoldingItem)
                {
                    if (Input.LeftClicked && AltDown())
                    {
                        // Add to favorites without giving the item.
                        if (AddFavorite(id))
                        {
                            SendLog($"Favorited '{GetItemName(id)}'.");
                            ClearHover(); // Avoid instant tooltip after state change.
                        }
                    }
                    else if (!AltDown() && (Input.LeftClicked || Input.RightClicked))
                    {
                        // Give item (full stack on LMB, single on RMB), if usable.
                        if (IsBad(id))
                        {
                            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Error");
                            SendLog($"'{id}' is not usable (skipping).");
                        }
                        else
                        {
                            try
                            {
                                var probe = SafeCreate(id, 1); // Validate once.
                                if (probe == null) SendLog($"'{id}' cannot be created (skipping).");
                                else GiveItem(id, fullStack: !Input.RightClicked);
                            }
                            catch (KeyNotFoundException knf) { MarkBad(id, knf); }
                            catch (Exception ex)             { MarkBad(id, ex);  }
                        }
                    }
                }

                // Advance grid position.
                x += CELL + CELL_PAD;
                if ((i - start + 1) % cols == 0) { x = r.X; y += CELL + CELL_PAD; }
            }

            // Right border line (visual separation).
            DrawVLine(sb, r.Right, r.Y - 6, r.Bottom + 6, ColLine);

            // If cursor left the grid or nothing is hovered, clear any pending tooltip timer.
            if (!hoveredThisFrame || !r.Contains(Input.Mouse))
                ClearHover();

            // These are set by DrawRightItemsPanel; safe to assign here too
            // when the grid is used standalone (keeps PointerOverUI accurate).
            _lastSearch = Rectangle.Empty;
            _lastGrid   = r;
        }

        /// <summary>
        /// Filters the universe of item IDs by search text and/or user options.
        /// When searching, always returns an alpha-sorted list for stability.
        /// Otherwise, optional insertion-order sort (via AllItems) and unusable filtering.
        /// </summary>
        static List<InventoryItemIDs> FilteredItems()
        {
            IEnumerable<InventoryItemIDs> ids = _allIds;

            // Human-friendly key for display + filtering (uses real item name when available).
            string Key(InventoryItemIDs id)
            {
                var n = GetItemName(id) ?? id.ToString();
                return n.Replace('_', ' ').Trim();
            }

            // Search mode: Filter + alphabetical sort (case-insensitive).
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string f = _search.Trim().ToLowerInvariant();
                ids = ids.Where(id => Key(id).ToLowerInvariant().Contains(f));
                return ids.OrderBy(id => Key(id), StringComparer.OrdinalIgnoreCase).ToList();
            }

            // Non-search mode: Optional insertion-order and/or unusable filtering.
            if (SortItems)    ids = GetAllIds_Insertion();
            if (HideUnusable) ids = ids.Where(id => !IsBad(id));

            return ids.ToList();
        }

        /// <summary>
        /// Returns items in the order they were inserted into InventoryItem.AllItems,
        /// then appends any remaining IDs that aren't present (e.g., unusable).
        /// Useful for a "game-native" sort without alpha reordering.
        /// </summary>
        static List<InventoryItemIDs> GetAllIds_Insertion()
        {
            var f = AccessTools.Field(typeof(InventoryItem), "AllItems");
            if (f?.GetValue(null) is IDictionary dict)
            {
                var list = new List<InventoryItemIDs>();

                // Preserve dictionary enumeration order (implementation-defined but stable in this game build).
                foreach (DictionaryEntry e in dict)
                    list.Add((InventoryItemIDs)e.Key);

                // Append any IDs not in the dictionary (e.g., blocked/unusable), preserving _allIds order.
                foreach (InventoryItemIDs e in _allIds)
                    if (!list.Contains(e))
                        list.Add(e);

                return list;
            }
            return null; // Caller will ignore and fall back.
        }
        #endregion

        #region Right Panel: Favorites Grid

        /// <summary> Draw the Favorites panel: Grid-only view with wheel paging and Alt-click remove. </summary>
        static void DrawFavoritesPanel(SpriteBatch sb, Rectangle r)
        {
            // Panel background.
            Fill(sb, r, ColPanel);

            // Inner content rect (padding on all sides).
            var gridR = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16);

            // Take a snapshot so we can modify the live list safely while drawing.
            var view = _favoritesList.ToArray();

            // Empty state -> show a short hint and bail.
            if (view.Length == 0)
            {
                const string msg = "Favorites:\nDrop items here or Alt-click them in the items panel.";
                DrawStringCenteredMultiline(sb, gridR, WrapByChars(msg, 20), ColText);

                // Cache rectangles for hit-testing from other systems; no search box in this panel.
                _lastSearch = Rectangle.Empty;
                _lastGrid   = gridR;
                return;
            }

            // Compute grid geometry (how many cells fit).
            int cols  = Math.Max(1, (gridR.Width  + CELL_PAD) / (CELL + CELL_PAD));
            int rows  = Math.Max(1, (gridR.Height + CELL_PAD) / (CELL + CELL_PAD));
            int per   = Math.Max(1, cols * rows);
            int pages = Math.Max(1, (view.Length + per - 1) / per);

            // Mouse wheel inside the grid pages left/right.
            if (gridR.Contains(Input.Mouse) && Input.ScrollDelta != 0)
            {
                if (Input.ScrollDelta < 0) _favPage++; // Wheel down -> next.
                if (Input.ScrollDelta > 0) _favPage--; // Wheel up   -> prev.
            }
            _favPage = TMIMathHelpers.Clamp(_favPage, 0, pages - 1);

            // Page slice.
            int start = _favPage * per;
            int end   = Math.Min(view.Length, start + per);

            // Iteration cursor.
            int x = gridR.X, y = gridR.Y;

            for (int i = start; i < end; i++)
            {
                var id   = view[i];
                var cell = new Rectangle(x, y, CELL, CELL);

                // Track hover and tooltip timing for this specific cell/context.
                TrackHover(cell, id, HoverContext.Favorites);

                // Paint the cell (hot vs normal).
                bool hot = cell.Contains(Input.Mouse);
                Fill(sb, cell, hot ? new Color(60, 60, 80, 230) : new Color(35, 35, 50, 220));

                // Draw the real item icon when available; fallback handled inside DrawItemIcon.
                try
                {
                    var icon = new Rectangle(cell.X + 2, cell.Y + 2, cell.Width - 4, cell.Height - 4);
                    DrawItemIcon(sb, icon, id);
                }
                catch { /* Fail-safe: Swallow and continue. */ }

                if (hot && !IsHoldingCursorItem())
                {
                    // Alt + LeftClick on favorites panel removes the favorite.
                    if (Input.LeftClicked && AltDown())
                    {
                        RemoveFavorite(id);
                        SendLog($"Removed '{GetItemName(id)}'.");
                        ClearHover(); // Drop tooltip immediately after removal.
                    }
                    // Normal click gives items (right-click = single; left-click = full stack).
                    else if (!AltDown() && (Input.LeftClicked || Input.RightClicked))
                    {
                        if (IsBad(id))
                        {
                            if (USE_SOUNDS) SoundManager.Instance.PlayInstance("Error");
                            SendLog($"'{id}' is not usable (skipping).");
                        }
                        else
                        {
                            try
                            {
                                // Probe once so we can mark bad IDs and avoid repeated exceptions.
                                var probe = SafeCreate(id, 1);
                                if (probe == null)
                                {
                                    SendLog($"'{id}' cannot be created (skipping).");
                                }
                                else
                                {
                                    // Give the item; right click = 1, left click = full stack.
                                    GiveItem(id, fullStack: !Input.RightClicked);
                                }
                            }
                            catch (KeyNotFoundException knf) { MarkBad(id, knf); }
                            catch (Exception ex)             { MarkBad(id, ex);  }
                        }
                    }
                }

                // Advance to next grid slot.
                x += CELL + CELL_PAD;
                if ((i - start + 1) % cols == 0)
                {
                    x = gridR.X;
                    y += CELL + CELL_PAD;
                }
            }

            // If the favorites list changed while drawing (e.g., user removed one),
            // clamp the active page so it never points past the end.
            if (_favoritesList.Count != view.Length)
            {
                int newPages = Math.Max(1, (_favoritesList.Count + per - 1) / per);
                _favPage     = TMIMathHelpers.Clamp(_favPage, 0, newPages - 1);
            }

            // Right-side separator line for the column.
            DrawVLine(sb, gridR.Right, gridR.Y - 6, gridR.Bottom + 6, ColLine);

            // Cache rects for external hit-tests (used by PointerOverUI, etc.).
            _lastSearch = Rectangle.Empty;
            _lastGrid   = gridR;
        }
        #endregion

        #region Right Panel: Settings

        /// <summary>
        /// Right-hand Settings panel: Uses the same panel chrome as Items/Favorites.
        /// </summary>
        static void DrawSettingsPanelRight(SpriteBatch sb, Rectangle r)
        {
            // Panel background.
            Fill(sb, r, ColPanel);

            int innerX = r.X + 8;
            int innerY = r.Y + 8;
            int innerW = r.Width - 16;

            // ---------------------------------------------------------------------
            // Header: centered "TMI Gameplay Settings"
            // ---------------------------------------------------------------------
            var headerRect = new Rectangle(r.X, innerY, r.Width, 24);
            DrawStringCentered(sb, headerRect, "TMI Gameplay Settings", ColText);

            // Space below header.
            innerY = headerRect.Bottom + 6;

            // Separator under header.
            DrawHLine(sb, innerY, r.X + 4, r.Right - 4, ColLine);

            // Extra space before the subsection content.
            innerY += 8;

            // ---------------------------------------------------------------------
            // Subsection: Hard Block Settings
            // ---------------------------------------------------------------------
            var titleRect = new Rectangle(innerX, innerY, innerW, 24);
            DrawString(sb, titleRect, "Hard Block Settings:", ColText);
            innerY = titleRect.Bottom + 8;

            // Row 1: Checkbox - allow mining very-hard (level 5 hardness) blocks.
            int rowH = 24;

            var row1 = new Rectangle(innerX, innerY, innerW, rowH);
            int boxSize = rowH - 6;
            var chkRect = new Rectangle(
                row1.X,
                row1.Y + (rowH - boxSize) / 2,
                boxSize,
                boxSize
            );

            var mouse = Input.Mouse;
            bool overChk = chkRect.Contains(mouse);

            Fill(sb, chkRect, overChk ? ColBtnHot : ColBtn);
            if (_hardBlocksEnabledUI)
            {
                DrawStringCentered(sb, chkRect, "X", ColText);
            }

            var chkLabelRect = new Rectangle(
                chkRect.Right + 6,
                row1.Y,
                row1.Width - (chkRect.Width + 6),
                row1.Height
            );
            DrawString(sb, chkLabelRect, "Allow mining level-5 blocks", ColText);

            if (overChk && Input.LeftClicked)
            {
                _hardBlocksEnabledUI = !_hardBlocksEnabledUI;

                // Persist + apply.
                TMIState.SetHardBlocksEnabled(_hardBlocksEnabledUI);
                TMIOverlayActions.ApplyHardBlockSettings();

                if (_hardBlocksEnabledUI)
                    SendLog($"Hard-block tweaks ENABLED for {_hardBlockDefaults.Count} hardness-5 blocks.");
                else
                    SendLog("Hard-block tweaks DISABLED; restored defaults for all hardness-5 blocks.");
            }

            // Row 2: Tool tier slider (0-12).
            innerY += rowH + 6;
            var row2 = new Rectangle(innerX, innerY, innerW, rowH);

            DrawSettingsSliderRow(
                sb,
                row2,
                label: $"Tool Tier: {_hardBlockTierUI}",
                min: 0,
                max: 12,
                ref _hardBlockTierUI,
                onChanged: val =>
                {
                    TMIState.SetHardBlockTier(val);
                    TMIOverlayActions.ApplyHardBlockSettings();
                });

            // ---------------------------------------------------------------------
            // Separator below the controls.
            // ---------------------------------------------------------------------
            int bottomSepY = row2.Bottom + 6;
            DrawHLine(sb, bottomSepY, r.X + 4, r.Right - 4, ColLine);
        }

        /// <summary>
        /// A single row: "Label    [-] [value] [+]" using TMI button colors.
        /// </summary>
        static void DrawSettingsSliderRow(
            SpriteBatch sb,
            Rectangle row,
            string label,
            int min,
            int max,
            ref int value,
            System.Action<int> onChanged)
        {
            int rowH = row.Height;

            // Label area on the left.
            int btnSize = rowH - 6;
            int buttonsW = btnSize * 3 + 8; // [-] [value] [+] + gaps
            var labelRect = new Rectangle(row.X, row.Y, row.Width - buttonsW - 6, rowH);

            DrawString(sb, labelRect, label, ColText);

            int x = labelRect.Right + 4;
            int y = row.Y + (rowH - btnSize) / 2;

            var minusRect = new Rectangle(x, y, btnSize, btnSize);
            x += btnSize + 4;
            var valueRect = new Rectangle(x, y, btnSize, btnSize);
            x += btnSize + 4;
            var plusRect = new Rectangle(x, y, btnSize, btnSize);

            var mouse = Input.Mouse;
            bool overMinus = minusRect.Contains(mouse);
            bool overPlus = plusRect.Contains(mouse);

            // Minus button.
            Fill(sb, minusRect, overMinus ? ColBtnHot : ColBtn);
            DrawStringCentered(sb, minusRect, "-", ColText);

            // Value box.
            Fill(sb, valueRect, ColPanelHi);
            DrawStringCentered(sb, valueRect, value.ToString(), ColText);

            // Plus button.
            Fill(sb, plusRect, overPlus ? ColBtnHot : ColBtn);
            DrawStringCentered(sb, plusRect, "+", ColText);

            // Click handling (same pattern as pager arrows).
            if (overMinus && Input.LeftClicked)
            {
                int newVal = System.Math.Max(min, value - 1);
                if (newVal != value)
                {
                    value = newVal;
                    onChanged?.Invoke(value);
                }
            }

            if (overPlus && Input.LeftClicked)
            {
                int newVal = System.Math.Min(max, value + 1);
                if (newVal != value)
                {
                    value = newVal;
                    onChanged?.Invoke(value);
                }
            }
        }
        #endregion

        #region Footer (Bottom-Left Signature)

        /// <summary>
        /// Draws a small footer (mod name, version, and build date) in the bottom-left corner.
        /// Called from the overlay's Draw() pass.
        /// </summary>
        static void DrawFooterInfo(SpriteBatch sb, Rectangle screen)
        {
            // If the game font hasn't been resolved yet, bail out quietly.
            if (_font == null) return;

            // Pull simple identity/version info from the assembly that defines TooManyItems.
            // Version.ToString(3) -> "Major.Minor.Build" (omit Revision for brevity).
            var asm     = typeof(TooManyItems).Assembly;
            string name = asm.GetName().Name;
            string ver  = asm.GetName().Version?.ToString(3);

            // Build date is taken from the file's last write time (dev-friendly, works for loose DLLs).
            // See GetBuildDate() for details and caveats.
            string date = GetBuildDate(asm).ToString("yyyy-MM-dd");

            // Final label: "TooManyItems v1.2.3 2025-10-06".
            string text = $"{name} v{ver} {date}";

            // Size the text once to place it flush to the lower edge with the configured margin.
            var size    = _font.MeasureString(text);
            var pos     = new Vector2(MARGIN, screen.Height - MARGIN - size.Y);

            // Simple 1px drop shadow to keep the text readable on bright backgrounds.
            sb.DrawString(_font, text, pos + new Vector2(0, 1), Color.Black * 0.6f);
            sb.DrawString(_font, text, pos, ColText * 0.85f);
        }

        /// <summary>
        /// Best-effort build date:
        ///  - Tries the assembly file's last write time (great during development / loose DLL deploys).
        ///  - Falls back to UtcNow if the path is unavailable (e.g., single-file publishing or sandboxed loaders).
        /// Note: This isn't a cryptographic build stamp-just a friendly "built on" hint.
        /// </summary>
        static DateTime GetBuildDate(Assembly asm)
        {
            try
            {
                var path = asm.Location;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return File.GetLastWriteTime(path);
            }
            catch
            {
                // Swallow and fall through to the fallback below.
            }

            // Fallback so the footer never breaks rendering; shows "today" if we can't read the file time.
            return DateTime.UtcNow;
        }
        #endregion

        #region Icon Drawing (Real Game Icons, Safe Fallback)

        /// <summary>
        /// Draw a single inventory icon into the given rectangle, falling back to a
        /// checker "missing texture" if the item can't be constructed/drawn.
        /// - Uses the game's own InventoryItem.Draw2D for authentic icons.
        /// - Defensively handles broken/missing entries and quarantines them via MarkBad.
        /// - Keeps reflection in one place and avoids per-frame allocations where possible.
        /// </summary>
        static void DrawItemIcon(SpriteBatch sb, Rectangle r, InventoryItemIDs id)
        {
            try
            {
                // Look up the game's master item registry:
                //   private static Dictionary<InventoryItemIDs, InventoryItemClass> InventoryItem.AllItems
                // We use AccessTools to avoid a hard compile-time dependency on internals.
                var f    = AccessTools.Field(typeof(InventoryItem), "AllItems");
                var dict = (Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>)f.GetValue(null);

                // If the registry is missing, or this id isn't registered (or the class is null),
                // draw a "missing" tile and quarantine this id so future frames skip it quickly.
                if (dict == null || !dict.TryGetValue(id, out var cls) || cls == null)
                {
                    MarkBad(id, new KeyNotFoundException());

                    // Use a custom missing texture if provided; otherwise synthesize a magenta/black checker.
                    var tex = _missingTex ?? MakeCheckerIcon(sb.GraphicsDevice, 64, 64, 8, Color.Magenta, Color.Black);
                    sb.Draw(tex, r, Color.White);
                    return;
                }

                // Happy path: Create a temporary 1-count item and let the game render its real icon.
                // drawAmt=false hides the quantity overlay (cleaner grid).
                var item = InventoryItem.CreateItem(id, 1);
                item.Draw2D(sb, r, Color.White, drawAmt: false);
                return;
            }
            catch
            {
                // Any unexpected failure falls back to the checker tile to keep UI resilient.
                var tex = _missingTex ?? MakeCheckerIcon(sb.GraphicsDevice, 64, 64, 8, Color.Magenta, Color.Black);
                sb.Draw(tex, r, Color.White);
            }
        }

        /// <summary> Build a simple checkerboard "missing texture" on the fly. </summary>
        static Texture2D MakeCheckerIcon(GraphicsDevice gd, int w, int h, int tile, Color a, Color b)
        {
            var tex = new Texture2D(gd, w, h);
            var data = new Color[w * h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    data[y * w + x] = (((x / tile) + (y / tile)) & 1) == 0 ? a : b;

            tex.SetData(data);
            return tex;
        }
        #endregion

        #region Top-Most Overlay: Tooltips

        #region Panel Hover: State + Helpers

        // Panel-level hover timing (for when no specific item cell is hot).
        static double _panelHoverBegan = -1;

        // Is the cursor inside the right column (items/favorites) panel?
        static bool PointerInRightPanel()
        {
            var ms = Mouse.GetState();
            return _lastRight.Contains(ms.X, ms.Y); // _lastRight is set each frame in Draw(...)
        }
        #endregion

        /// <summary>
        /// Draw after main UI for tooltips so they sit above everything.
        /// Notes:
        /// • Called near the end of the frame's UI pass (after Draw(...)) so tooltips render on top.
        /// • This method assumes the caller already has an active SpriteBatch.Begin(...). If not, either
        ///   wrap this call in its own Begin/End with matching states or move it inside an existing pass.
        /// • Uses cached hover state set by the main overlay draw; no extra hit-testing is done here.
        /// • Two tooltip types are supported:
        ///   (1) Item grid/favorites hover (with optional ALT modifier behavior)
        ///   (2) Toolbar hover (uses a separate hover timer/delay)
        /// </summary>
        #pragma warning disable IDE0060 // 'gt' is intentionally unused (time comes from cached _now).
        public static void DrawTopMost(SpriteBatch sb, GameTime gt)
        {
            // Global gates: Skip fast when overlay/tooltip system is disabled.
            if (!Enabled || !USE_TOOLTIPS) return;

            // Track panel-level hover (right column) so we can show a tooltip even when not on a cell.
            bool inRightPanel = PointerInRightPanel();
            if (inRightPanel)
            {
                if (_panelHoverBegan < 0) _panelHoverBegan = _now; // Start delay.
            }
            else
            {
                _panelHoverBegan = -1; // Reset when cursor leaves panel.
            }

            /// <summary>
            /// -------------------------------
            /// ITEM Tooltip
            /// -------------------------------
            /// Conditions:
            /// • We are NOT currently holding an item.
            /// • We are hovering a valid item cell (_hoverItem + _hoverBegan set by TrackHover).
            /// • The per-item tooltip delay has elapsed (TOOLTIP_DELAY_ITEM).
            /// </summary>
            if (!IsHoldingCursorItem() && _hoverItem.HasValue && _hoverBegan >= 0 && (_now - _hoverBegan) >= TOOLTIP_DELAY_ITEM)
            {
                // ALT modifies the intent text depending on which panel we're over:
                // • Items panel -> "Add to favorites".
                // • Favorites   -> "Remove from favorites".
                bool alt = Keyboard.GetState().IsKeyDown(Keys.LeftAlt) || Keyboard.GetState().IsKeyDown(Keys.RightAlt);

                if (alt)
                {
                    switch (_hoverCtx)
                    {
                        case HoverContext.Items:
                            DrawUITooltip(sb, $"Add {(int)_hoverItem.Value} to favorites.");
                            break;

                        case HoverContext.Favorites:
                            DrawUITooltip(sb, $"Remove {GetItemName(_hoverItem.Value)}.");
                            break;
                    }
                }
                else
                {
                    // Default item tooltip:
                    // • Shows numeric ID, display name and (if present) a wrapped description.
                    // Notes:
                    // - Block resolution is conditional because this code runs every frame.
                    // - If the hovered item isn't a block, TryGetBlockInfo returns false and the extra lines stay null.
                    string blockLine = null, blockInfoLine = null, itemInfoLine = null;

                    if (ShowBlockIds || ShowBlockInfo)
                        TryGetBlockInfo(_hoverItem.Value, out blockLine, out blockInfoLine);

                    if (ShowItemInfo)
                        TryGetItemInfo(_hoverItem.Value, out itemInfoLine);

                    var    hoverTip    = new System.Text.StringBuilder(128);
                    int    idNum       = (int)_hoverItem.Value;
                    string name        = GetItemName(_hoverItem.Value);
                    string description = GetItemDescription(_hoverItem.Value);
                    string wrapped     = WrapByChars(description, 30);
                    string enumName    = ShowEnums ? $"{(InventoryItemIDs)idNum}" : string.Empty;

                    // Notes:
                    // - blockLine/blockInfoLine may be null when the item isn't a placeable block.
                    // - itemInfoLine may be null if class lookup fails.
                    // - AppendSection ignores null/empty so we can pass nulls safely.
                    string blockId     = (ShowBlockIds  && !string.IsNullOrWhiteSpace(blockLine))     ? blockLine     : null;
                    string blockExtras = (ShowBlockInfo && !string.IsNullOrWhiteSpace(blockInfoLine)) ? blockInfoLine : null;
                    string itemExtras  = (ShowItemInfo  && !string.IsNullOrWhiteSpace(itemInfoLine))  ? itemInfoLine  : null;

                    hoverTip.Append($"{idNum}: {name}");
                    AppendSection(hoverTip, !string.IsNullOrWhiteSpace(description) ? wrapped             : null);
                    AppendSection(hoverTip, !string.IsNullOrWhiteSpace(enumName)    ? $"Enum: {enumName}" : null);
                    AppendSection(hoverTip, blockId,     doubleBreak: false);
                    AppendSection(hoverTip, blockExtras, doubleBreak: true);
                    AppendSection(hoverTip, itemExtras,  doubleBreak: true);

                    DrawUITooltip(sb, hoverTip.ToString());
                }
            }
            /// <summary>
            /// -------------------------------
            /// DELETE / FAVORITE ITEM Tooltip
            /// -------------------------------
            /// Conditions:
            /// • We are currently holding an item.
            /// • We are hovering within the right panel (_panelHoverBegan set by PointerInRightPanel).
            /// </summary>
            else if (inRightPanel && _panelHoverBegan >= 0 && (_now - _panelHoverBegan) >= TOOLTIP_DELAY_ITEM)
            {
                var heldItem   = GetHolding(GetOpenCraftingScreen());

                // If dragging an inventory item, show the delete/remove hint
                if (heldItem != null)
                {
                    // RightPanel is the enum you already have: Items vs Favorites
                    if      (_rightPanel == RightPanel.Items)
                        DrawUITooltip(sb, $"DELETE {heldItem.Name}");
                    else if (_rightPanel == RightPanel.Favorites)
                        DrawUITooltip(sb, $"Add {heldItem.Name}");
                }
            }
            /// <summary>
            /// -------------------------------
            /// FAVORITE ITEM Helper
            /// -------------------------------
            /// </summary>
            if (inRightPanel)
            {
                var heldItem = GetHolding(GetOpenCraftingScreen());

                // If dragging an inventory item, show the delete/remove hint.
                if (heldItem != null)
                {
                    if (_rightPanel == RightPanel.Favorites)
                        HandleFavoriteDrop(heldItem);
                }
            }

            /// <summary>
            /// -----------------------------
            /// TOOLBAR Tooltip
            /// -----------------------------
            /// Conditions:
            /// • A toolbar control was hovered this frame and provided text (_toolHoverTip).
            /// • The toolbar tooltip delay has elapsed (TOOLTIP_DELAY_TOOLBAR).
            /// </summary>
            if (!string.IsNullOrEmpty(_toolHoverTip) &&
                _toolHoverBegan >= 0 &&
                (_now - _toolHoverBegan) >= TOOLTIP_DELAY_TOOLBAR)
            {
                DrawUITooltip(sb, _toolHoverTip);
            }
        }
        #pragma warning restore IDE0060

        #endregion

        #region Draw Helpers (Text/Lines/Tooltips)

        // ------------------------------
        // Drawing helpers: text, lines, shapes, tooltips
        // Notes:
        // • All funcs assume you're inside an active SpriteBatch.Begin(...)/End().
        // • Uses a cached 1x1 white texture (_px) to draw solids/lines.
        // • For crisp text & pixel lines, prefer PointClamp and integer-aligned positions.
        // ------------------------------

        // Default to 2-character abbreviations (e.g., "DiamondPickAxe" -> "DP").
        static string Abbrev(string s) => Abbrev(s, 2);

        /// <summary>
        /// Builds a short label up to <paramref name="maxLength"/> chars.
        /// Heuristics:
        /// 1) If text already fits, return it (after replacing '_'/'-' with spaces).
        /// 2) Otherwise, take the first letter, then letters at "boundaries":
        ///    - Start of a word (after space/underscore/hyphen).
        ///    - CamelCase transitions (aA).
        ///    - Digit transitions (a1).
        /// 3) If more chars is needed, fill from the original (skipping spaces)
        ///    until reaching <paramref name="maxLength"/>.
        /// Notes:
        /// • Returns empty string if maxLength <= 0.
        /// • Preserves original letter casing during selection; final result is upper-cased.
        /// • Complexity: O(n) over the input string; no allocations beyond the small list.
        /// • Not a linguistics-aware acronym generator; purely mechanical heuristics.
        /// Examples:
        /// • "Diamond Pick Axe"  (max=2) -> "DP"
        /// • "diamondPickAxe"    (max=3) -> "DPA"
        /// • "laser-weapon v2"   (max=3) -> "LW2"
        /// • "__ ultra  tool __" (max=2) -> "UT"
        /// </summary>
        static string Abbrev(string s, int maxLength)
        {
            // Guard rails: Disabled or empty input -> trivial returns.
            if (maxLength <= 0) return string.Empty;
            if (string.IsNullOrEmpty(s)) return s;

            // Normalize common separators to spaces; keep original casing for boundary checks.
            s = s.Replace('_', ' ').Replace('-', ' ').Trim();

            // Early-out when already short enough.
            if (s.Length <= maxLength) return s;

            // Handle strings that trimmed down to nothing.
            if (s.Length == 0) return string.Empty;

            // Pre-size to a tiny capacity to avoid re-allocations (max few chars).
            var picks = new List<char>(Math.Min(maxLength, 8));

            // Always take the first non-space char (the "leading letter").
            int i = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length) picks.Add(s[i]);

            // Pass 1: Harvest boundary letters (word starts, camelCase bumps, digit switches).
            for (int j = i + 1; j < s.Length && picks.Count < maxLength; j++)
            {
                char cur  = s[j];
                char prev = s[j - 1];

                bool atWordStart   = !char.IsWhiteSpace(cur) && char.IsWhiteSpace(prev);
                bool camelBoundary = char.IsLetter(cur) && char.IsLower(prev) && char.IsUpper(cur);
                bool digitBoundary = char.IsDigit(cur) && !char.IsDigit(prev);

                if (atWordStart || camelBoundary || digitBoundary)
                {
                    picks.Add(cur);
                }
            }

            // Pass 2: If still short, fill forward with non-space chars in order.
            if (picks.Count < maxLength)
            {
                for (int j = i + 1; j < s.Length && picks.Count < maxLength; j++)
                {
                    if (!char.IsWhiteSpace(s[j]))
                        picks.Add(s[j]);
                }
            }

            // Safety: If nothing picked (extreme edge), hard-cut.
            if (picks.Count == 0)
                return s.Substring(0, Math.Min(maxLength, s.Length));

            // Final presentation: All-caps for compact UI labels.
            // (If you prefer original casing, drop the ToUpperInvariant().)
            return new string(picks.ToArray()).ToUpperInvariant();
        }

        #pragma warning disable IDE0060 // Some parameters are required by the SpriteBatch API shape, even if unused in a given call.
        /// <summary>
        /// Draws a single-line string vertically centered in 'r'.
        /// - Optional left padding (padX/padY).
        /// - If 'alignRight' is true, right-aligns within 'r'.
        /// - Renders a 1px drop shadow for legibility.
        /// </summary>
        static void DrawString(SpriteBatch sb, Rectangle r, string text, Color color, int padX = 0, int padY = 0, bool alignRight = false)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            // Measure first to compute alignment precisely.
            var size = _font.MeasureString(text);

            // Vertical center + optional horizontal right-alignment.
            Vector2 pos = alignRight
                ? new Vector2(r.Right - padX - size.X, r.Y + (r.Height - size.Y) * 0.5f)
                : new Vector2(r.X + padX,              r.Y + (r.Height - size.Y) * 0.5f);

            // Cheap 1px shadow. Adjust offset or alpha to taste.
            sb.DrawString(_font, text, pos + new Vector2(0, 1), Color.Black * 0.6f);
            sb.DrawString(_font, text, pos, color);
        }
        #pragma warning restore IDE0060

        /// <summary>
        /// Centers text both horizontally and vertically in 'r'.
        /// - Rounds to integers to keep SpriteFont pixels sharp (avoids blurry half-pixels).
        /// - Adds a 1px shadow for contrast on bright/dark backgrounds.
        /// </summary>
        static void DrawStringCentered(SpriteBatch sb, Rectangle r, string text, Color color)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            var size = _font.MeasureString(text);

            // Integer rounding = Crisp glyphs (especially with PointClamp).
            var pos = new Vector2(
                r.X + (int)Math.Round((r.Width  - size.X) * 0.5f),
                r.Y + (int)Math.Round((r.Height - size.Y) * 0.5f));

            sb.DrawString(_font, text, pos + new Vector2(0, 1), Color.Black * 0.6f);
            sb.DrawString(_font, text, pos, color);
        }

        /// <summary>
        /// Centers multi-line text: Each line is centered horizontally, and the whole block
        /// is centered vertically inside 'r'.
        /// - Rounds to integers to keep SpriteFont pixels sharp (avoids blurry half-pixels).
        /// - Adds a 1px shadow for contrast on bright/dark backgrounds.
        /// </summary>
        static void DrawStringCenteredMultiline(SpriteBatch sb, Rectangle r, string text, Color color)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            // Split and measure.
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int lineH = _font.LineSpacing;     // Consistent line height.
            int totalH = lineH * lines.Length;

            // Top Y so the block is vertically centered.
            int y = r.Y + (int)Math.Round((r.Height - totalH) * 0.5f);

            foreach (var line in lines)
            {
                var s = line ?? string.Empty;
                var size = _font.MeasureString(s);

                // Left X so this line is horizontally centered.
                int x = r.X + (int)Math.Round((r.Width - size.X) * 0.5f);

                // 1px shadow + text.
                var pos = new Vector2(x, y);
                sb.DrawString(_font, s, pos + new Vector2(0, 1), Color.Black * 0.6f);
                sb.DrawString(_font, s, pos, color);

                y += lineH;
            }
        }

        /// <summary>
        /// Draws a centered "X" inside 'r' using the 1x1 pixel texture, rotated to form the diagonals.
        /// - Length auto-fits the smaller side of 'r'.
        /// - 'thick' controls line thickness in pixels.
        /// </summary>
        static void DrawCenteredX(SpriteBatch sb, Rectangle r, Color c)
        {
            var center = new Vector2(r.X + r.Width * 0.5f, r.Y + r.Height * 0.5f);
            float len   = Math.Max(4, Math.Min(r.Width, r.Height) - 6);
            float thick = 2f;

            // Two diagonals at ±45°
            sb.Draw(_px, center, null, c, (float)(Math.PI / 4f),  new Vector2(0.5f, 0.5f), new Vector2(len, thick), SpriteEffects.None, 0);
            sb.Draw(_px, center, null, c, (float)(-Math.PI / 4f), new Vector2(0.5f, 0.5f), new Vector2(len, thick), SpriteEffects.None, 0);
        }

        /// <summary>
        /// Solid fill of 'r' using the cached 1x1 white pixel.
        /// </summary>
        static void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_px, r, c);

        /// <summary>
        /// 1px horizontal line from (x1, y) to (x2, y).
        /// - Order-agnostic: handles x1 > x2.
        /// </summary>
        static void DrawHLine(SpriteBatch sb, int y, int x1, int x2, Color c)
            => Fill(sb, new Rectangle(Math.Min(x1, x2), y, Math.Abs(x2 - x1), 1), c);

        /// <summary>
        /// 1px vertical line from (x, y1) to (x, y2).
        /// - Order-agnostic: handles y1 > y2.
        /// </summary>
        static void DrawVLine(SpriteBatch sb, int x, int y1, int y2, Color c)
            => Fill(sb, new Rectangle(x, Math.Min(y1, y2), 1, Math.Abs(y2 - y1)), c);

        /// <summary>
        /// Renders a tooltip near the mouse:
        /// - Auto-sizes based on measured string + padding.
        /// - Clamps to viewport so it never goes off-screen.
        /// - Semi-opaque background, 1px border, drop-shadowed text.
        /// Tip: Pre-wrap long text before calling for predictable sizing.
        /// </summary>
        static void DrawUITooltip(SpriteBatch sb, string tooltipText)
        {
            if (_font == null) return;

            var ms   = Mouse.GetState();
            var size = _font.MeasureString(tooltipText);
            int pad  = 6;

            // Place slightly offset from the cursor to avoid overlap/flicker.
            var bg = new Rectangle(ms.X + 18, ms.Y + 18, (int)size.X + pad * 2, (int)size.Y + pad * 2);

            // Keep fully on-screen.
            var vp = sb.GraphicsDevice.Viewport;
            if (bg.Right  > vp.Width)  bg.X = vp.Width  - bg.Width  - 4;
            if (bg.Bottom > vp.Height) bg.Y = vp.Height - bg.Height - 4;

            // Panel + border (drawn with 1px lines).
            Fill(sb, bg, new Color(20, 20, 28, 230));
            DrawHLine(sb, bg.Y,          bg.X, bg.Right,      Color.Black * 0.6f);
            DrawHLine(sb, bg.Bottom - 1, bg.X, bg.Right,      Color.Black * 0.6f);
            DrawVLine(sb, bg.X,          bg.Y, bg.Bottom,     Color.Black * 0.6f);
            DrawVLine(sb, bg.Right - 1,  bg.Y, bg.Bottom,     Color.Black * 0.6f);

            // Text (shadow + face).
            var textPos = new Vector2(bg.X + pad, bg.Y + pad);
            sb.DrawString(_font, tooltipText, textPos + new Vector2(0, 1), Color.Black * 0.6f);
            sb.DrawString(_font, tooltipText, textPos, Color.White);
        }
        #endregion

        #region Hover Tracking & Name/Description Helpers

        /// <summary>
        /// Tracks hover state for a single grid cell:
        /// - Starts (or resets) the hover timer when the mouse enters a new cell/item/context.
        /// - Clears the hover when the mouse leaves the specific cell that was active.
        ///
        /// Notes:
        /// • We store the last hovered <see cref="Rectangle"/> and context so tooltips don't flicker
        ///   when the mouse moves within the same cell.
        /// • Hover timing (_hoverBegan) is checked elsewhere to delay tooltips.
        /// </summary>
        static void TrackHover(Rectangle cell, InventoryItemIDs id, HoverContext ctx)
        {
            var ms  = Mouse.GetState();
            bool ov = cell.Contains(ms.X, ms.Y);

            if (ov)
            {
                // Only reset hover state if anything meaningfully changed:
                //  - different item.
                //  - different cell instance/rect.
                //  - different UI context (items vs favorites).
                if (!_hoverItem.HasValue || !_hoverItem.Value.Equals(id) || _hoverRect != cell || _hoverCtx != ctx)
                {
                    _hoverItem  = id;
                    _hoverRect  = cell;
                    _hoverBegan = _now; // Start/restart hover timer.
                    _hoverCtx   = ctx;
                }
            }
            else
            {
                // Clear only if THIS cell/context was the active hover.
                if (_hoverItem.HasValue && _hoverRect == cell && _hoverCtx == ctx)
                    ClearHover();
            }
        }

        /// <summary>
        /// Resets all hover-related state so no tooltip will render until a new hover begins.
        /// </summary>
        public static void ClearHover()
        {
            _hoverItem  = null;
            _hoverBegan = -1;
            _hoverRect  = Rectangle.Empty;
            _hoverCtx   = HoverContext.None;
        }

        /// <summary>
        /// Gets a user-facing item name with caching and fault tolerance:
        /// - Returns cached value if present.
        /// - Tries to construct an <see cref="InventoryItem"/> and use its Name.
        /// - Falls back to enum text (underscores -> spaces) if creation fails or item is quarantined.
        /// - On any exception, marks the ID as "bad" so future calls short-circuit.
        /// </summary>
        public static string GetItemName(InventoryItemIDs id)
        {
            if (_nameCache.TryGetValue(id, out var s)) return s;
            try
            {
                if (!IsBad(id)) { var it = InventoryItem.CreateItem(id, 1); s = it?.Name ?? id.ToString().Replace('_', ' '); }
                else            { s = id.ToString().Replace('_', ' '); }
            }
            catch (Exception ex) { MarkBad(id, ex); s = id.ToString().Replace('_', ' '); }
            _nameCache[id] = s; return s;
        }

        /// <summary>
        /// Gets a user-facing item description with caching and fault tolerance:
        /// - Returns cached value if present.
        /// - Uses InventoryItem.Description when available.
        /// - If description equals the item name, returns empty (avoids redundant tooltips).
        /// - Quarantines IDs that throw once, so we don't keep hitting exceptions while drawing.
        /// </summary>
        public static string GetItemDescription(InventoryItemIDs id)
        {
            if (_descriptionCache.TryGetValue(id, out var s)) return s;
            try
            {
                if (!IsBad(id))
                {
                    var it = InventoryItem.CreateItem(id, 1);
                    s = it?.Description ?? id.ToString().Replace('_', ' ');
                    if (string.Equals(s, it?.Name, StringComparison.Ordinal)) s = string.Empty;
                }
                else s = string.Empty;
            }
            catch (Exception ex) { MarkBad(id, ex); s = string.Empty; }
            _descriptionCache[id] = s; return s;
        }

        /// <summary>
        /// Attempts to resolve block-specific info for an inventory item.
        /// Summary: If <paramref name="itemId"/> is a <see cref="BlockInventoryItemClass"/>, returns a formatted BlockId line
        /// (numeric + enum name) and an optional multi-line details string (parent/hardness/light transmission).
        /// </summary>
        private static bool TryGetBlockInfo(
            InventoryItemIDs itemId,
            out string blockIdLine,
            out string blockInfoLine)
        {
            blockIdLine   = null;
            blockInfoLine = null;

            try
            {
                // Class lookup only (no item instance is created).
                var cls = InventoryItem.GetClass(itemId);

                // Only BlockInventoryItemClass instances map cleanly to a BlockType / BlockTypeEnum.
                if (cls is BlockInventoryItemClass bic && bic.BlockType != null)
                {
                    // Canonical block identity (better than raw _type for "what block is this?" display).
                    BlockTypeEnum bt = bic.BlockType.ParentBlockType;

                    // Primary line:
                    // Notes:
                    // - Shows both enum name and numeric value for quick copy/paste + debugging.
                    blockIdLine = $"BlockID: {bt} ({(int)bt})";

                    // Extended details:
                    // Notes:
                    // - Keep this human-readable; avoid ultra-long lines since the tooltip wraps poorly.
                    // - Prefer booleans / flags that help explain rendering, lighting, placement, and interactions.
                    blockInfoLine =
                        "Block Properties:\n"                                                                                                                                                                     +
                        $"· Name: {bic.BlockType.Name}\n"                                                                                                                                                         +
                        $"· Parent: {bic.BlockType.ParentBlockType}\n"                                                                                                                                            +
                        $"· Facing: {bic.BlockType.Facing}\n"                                                                                                                                                     +
                        $"· Hardness: {bic.BlockType.Hardness}\n"                                                                                                                                                 +
                        $"· Light: Tx={bic.BlockType.LightTransmission}, Self={bic.BlockType.SelfIllumination}, FullBright={bic.BlockType.DrawFullBright}\n"                                                      +
                        $"· Render: Alpha={bic.BlockType.HasAlpha}, FancyLight={bic.BlockType.NeedsFancyLighting}, Translucent={bic.BlockType.LightAsTranslucent}, InteriorFaces={bic.BlockType.InteriorFaces}\n" +
                        $"· Interaction: BlockPlayer={bic.BlockType.BlockPlayer}, CanDig={bic.BlockType.CanBeDug}, CanTouch={bic.BlockType.CanBeTouched}, CanBuildOn={bic.BlockType.CanBuildOn}\n"                +
                        $"· Entity: SpawnEntity={bic.BlockType.SpawnEntity}, ItemEntity={bic.BlockType.IsItemEntity}\n"                                                                                           +
                        $"· Combat: DamageMask=0x{bic.BlockType.DamageMask:X}, DmgTx={bic.BlockType.DamageTransmision}\n"                                                                                         +
                        $"· Physics: Slopes={bic.BlockType.AllowSlopes}, LaserBounce={bic.BlockType.BouncesLasers}, Rest={bic.BlockType.BounceRestitution}\n"                                                     +
                        $"· Tags: Door={BlockType.IsDoor(bt)}, Container={BlockType.IsContainer(bt)}, Structure={BlockType.IsStructure(bt)}";

                    return true;
                }
            }
            catch
            {
                // Swallow: Tooltip should never crash the draw loop.
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve generic item-class info for an inventory item.
        /// Summary: Returns a formatted multi-line details string for any valid item class
        /// (max stack, cooldown, combat values, animation mode, etc.).
        /// </summary>
        private static bool TryGetItemInfo(
            InventoryItemIDs itemId,
            out string itemInfoLine)
        {
            itemInfoLine = null;

            try
            {
                var cls = InventoryItem.GetClass(itemId);
                if (cls == null)
                    return false;

                var sb = new System.Text.StringBuilder(256);

                sb.Append("Item Properties:\n");
                sb.Append($"· Class: {cls.GetType().Name}\n");
                sb.Append($"· MaxStack: {cls.MaxStackCount}\n");
                sb.Append($"· Cooldown: {cls.CoolDownTime.TotalSeconds:0.###}s\n");
                sb.Append($"· Damage: {cls.EnemyDamage:0.###} ({cls.EnemyDamageType})\n");
                sb.Append($"· PlayerMode: {cls.PlayerAnimationMode}\n");
                sb.Append($"· Melee: {cls.IsMeleeWeapon}\n");
                sb.Append($"· PickupTimeout: {cls.PickupTimeoutLength:0.###}s");

                if (!string.IsNullOrWhiteSpace(cls.UseSound))
                    sb.Append($"\n· UseSound: {cls.UseSound}");

                itemInfoLine = sb.ToString();
                return true;
            }
            catch
            {
                // Swallow: Tooltip generation should never break draw.
            }

            return false;
        }

        /// <summary>
        /// Appends an optional tooltip section to the StringBuilder.
        /// Summary: Adds either a single or double newline before the text, but only if the text is non-empty.
        /// </summary>
        static void AppendSection(System.Text.StringBuilder sb, string text, bool doubleBreak = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            sb.Append(doubleBreak ? "\n\n" : "\n");
            sb.Append(text);
        }

        /// <summary>
        /// Simple fixed-width word wrapper:
        /// - Respects existing newlines first.
        /// - Tries to break on whitespace; if a single word exceeds <paramref name="maxChars"/>,
        ///   it performs a hard break to avoid an infinite loop.
        /// - Trims repeated spaces after a break (but not newlines).
        /// </summary>
        static string WrapByChars(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars < 1) return text ?? string.Empty;

            var sb = new System.Text.StringBuilder(text.Length + 16);
            string[] paras = text.Replace("\r\n", "\n").Split('\n');
            for (int p = 0; p < paras.Length; p++)
            {
                string para = paras[p];
                int i = 0;
                while (i < para.Length)
                {
                    int remaining = para.Length - i;
                    int take      = Math.Min(maxChars, remaining);
                    int end       = i + take;

                    // If slice ends mid-word, walk left to last whitespace.
                    if (end < para.Length && !char.IsWhiteSpace(para[end]))
                    {
                        int j = end;
                        while (j > i && !char.IsWhiteSpace(para[j - 1])) j--;
                        end = (j > i) ? j : i + take; // Hard break if no whitespace found.
                    }

                    sb.Append(para, i, end - i);

                    // Skip extra spaces after the break (but keep newlines intact).
                    while (end < para.Length && char.IsWhiteSpace(para[end]) && para[end] != '\n' && para[end] != '\r') end++;

                    i = end;
                    if (i < para.Length) sb.Append('\n');
                }
                if (p < paras.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
        #endregion

        #region Toggle Handling (Config-Driven Hotkeys; No UI When Typing In Search)

        /// <summary>
        /// Handles the TMI overlay toggle + config hot-reload.
        /// Uses edge-detection (press, not hold) and ignores the toggle while the search box is focused.
        /// </summary>
        static void HandleToggle()
        {
            var k = Keyboard.GetState();

            // Hot-reload shortcut:
            // When the configured ReloadKey is pressed (on the rising edge), reload INI and
            // clear texture refs so they get reloaded/rebound next draw.
            var  reloadKey  = TMIConfig.LoadOrCreate().ReloadKey; // Keys or your parsed key type.
            bool reloadDown = k.IsKeyDown(reloadKey);

            if (reloadDown && !_wasReloadDown)
            {
                OnHotReload();
                _toolbarTex    = null; // Force toolbar atlas to reload on next EnsureAssets().
                _missingTex    = null; // Force missing icon to reload on next EnsureAssets().
                _wasReloadDown = true; // Set the latch true so holding the key won't retrigger until released.
                _font          = (SpriteFont)_fiMedFont.GetValue(CastleMinerZGame.Instance); // Re-grab the games font.
            }

            // If the search box has focus, don't let the global toggle fire.
            // (Typing the toggle key while searching shouldn't close the overlay.)
            // We still update the latch so the next real press is recognized correctly.
            if (_searchFocused)
            {
                _wasToggleDown = k.IsKeyDown(ToggleKey); // Keep latch in sync.
                return;                                  // Swallow toggle while editing.
            }

            // Edge-detect the toggle key (press once to flip Enabled; holding does nothing).
            bool down = k.IsKeyDown(ToggleKey);
            if (down && !_wasToggleDown)
            {
                Enabled = !Enabled; // Flip overlay visibility.
                TMIState.SetOverlayEnabled(Enabled);
            }

            // Update latch for next frame (prevents repeat while the key is held).
            _wasToggleDown = down;
            _wasReloadDown = reloadDown; // Update reload latch last (independent from toggle).
        }
        #endregion

        #region Click Latch & Simple Input Helpers

        /// <summary>
        /// ClickLatch provides simple, once-per-press detection for mouse buttons.
        /// Call ConsumeLeft/ConsumeRight every frame; they return TRUE only on the
        /// transition from Released -> Pressed (i.e., a clean edge), then update
        /// the stored state so holding the button won't spam actions.
        /// </summary>
        static class ClickLatch
        {
            // Last-frame button states we compare against.
            static ButtonState _lastLeft = ButtonState.Released;
            static ButtonState _lastRight = ButtonState.Released;

            /// <summary>
            /// Returns true exactly once when the left mouse button goes down.
            /// Use this instead of checking "Pressed" directly to avoid repeat while held.
            /// </summary>
            public static bool ConsumeLeft()
            {
                var ms    = Mouse.GetState();
                bool just = (ms.LeftButton == ButtonState.Pressed && _lastLeft == ButtonState.Released);
                _lastLeft = ms.LeftButton; // Remember current state for next frame.
                return just;
            }

            /// <summary>
            /// Returns true exactly once when the right mouse button goes down.
            /// </summary>
            public static bool ConsumeRight()
            {
                var ms     = Mouse.GetState();
                bool just  = (ms.RightButton == ButtonState.Pressed && _lastRight == ButtonState.Released);
                _lastRight = ms.RightButton;
                return just;
            }
        }

        // Small convenience wrappers so the call sites read nicely.
        static bool Clicked()      => ClickLatch.ConsumeLeft();
        static bool RightClicked() => ClickLatch.ConsumeRight();

        /// <summary>
        /// True while either Shift key is held. Helpful for "modified" actions
        /// (e.g., Shift+Click = delete all).
        /// </summary>
        public static bool ShiftHeld()
        {
            var k = Keyboard.GetState();
            return k.IsKeyDown(Keys.LeftShift) || k.IsKeyDown(Keys.RightShift);
        }

        /// <summary>
        /// Hit-test for the overlay chrome. Returns true if the mouse is over any
        /// of our UI regions (top toolbar, left save slots, right items/favorites).
        /// Use this to gate game input (don't place blocks, don't rotate camera, etc.).
        /// </summary>
        static bool MouseOverOurUI()
        {
            // If the graphics device isn't ready, err on the safe side and say "not over UI".
            var gd = CastleMinerZGame.Instance?.GraphicsDevice;
            if (gd == null) return false;

            // Screen bounds from current viewport (handles any resolution).
            var vp       = gd.Viewport;
            var screen   = new Rectangle(0, 0, vp.Width, vp.Height);

            // UI regions we draw each frame //

            // Top toolbar spans horizontally with configured margin/height.
            var rTop     = new Rectangle(MARGIN, MARGIN, screen.Width - MARGIN * 2, TOPBAR_H);

            // Left save-slot column runs from below the top bar to the bottom.
            int leftTopY = rTop.Bottom + MARGIN;
            var rLeft    = new Rectangle(MARGIN, leftTopY, SAVE_BTN_W, screen.Height - leftTopY - MARGIN);

            // Right items/favorites column: use explicit width if provided,
            // otherwise derive from configured columns + cell size/padding.
            int itemsW   = (int)((ITEMS_COL_W > 0) ? ITEMS_COL_W
                                                 : (ITEM_COLUMNS * CELL) + (CELL_PAD * 2));
            var rRight   = new Rectangle(screen.Width - itemsW - MARGIN, leftTopY, itemsW, screen.Height - leftTopY - MARGIN);

            // Current mouse position (The Input wrapper exposes a Point).
            var pt       = Input.Mouse;

            // If the pointer is over any of our rectangles, we consider it "over UI".
            return rTop.Contains(pt) || rLeft.Contains(pt) || rRight.Contains(pt);
        }
        #endregion

        #region Bad-ID Quarantine (Don't Retry Broken Items Every Frame)

        /// <summary>
        /// We keep a local "do not touch" set for InventoryItemIDs that throw or fail to
        /// instantiate. This prevents spamming exceptions every frame and speeds up filtering.
        /// </summary>
        static readonly HashSet<InventoryItemIDs> _badIds = new HashSet<InventoryItemIDs>();

        /// <summary>
        /// Fast check: Has this ID previously failed (and been quarantined)?
        /// </summary>
        static bool IsBad(InventoryItemIDs id) => _badIds.Contains(id);

        /// <summary>
        /// Mark an item ID as unusable. First time we see a failure, add it to the
        /// quarantine set and (optionally) log the reason/type for diagnostics.
        /// </summary>
        static void MarkBad(InventoryItemIDs id, Exception ex = null)
        {
            // Add returns true only on first insert -> log once.
            if (_badIds.Add(id))
                if (GetLoggingMode() != LoggingType.None)
                    Log($"Marking unusable item '{id}' ({ex?.GetType().Name ?? "unknown"}).");
        }

        /// <summary>
        /// Safe item factory used by UI hover/draw/give paths:
        /// - Early-out if ID is already quarantined (no more exceptions).
        /// - Uses a small cache (_iconCache) so we don't re-create sprites/items each draw.
        /// - On any failure, marks the ID bad and returns null (callers should handle null).
        /// </summary>
        static InventoryItem SafeCreate(InventoryItemIDs id, int count = 1)
        {
            // Don't try again if we already know it breaks.
            if (IsBad(id)) return null;

            try
            {
                // Reuse a cached instance when possible (cheap icon draws, name/desc lookups, etc.).
                if (!_iconCache.TryGetValue(id, out var it) || it == null)
                    _iconCache[id] = it = InventoryItem.CreateItem(id, Math.Max(1, count));

                return it;
            }
            catch (Exception ex)
            {
                // One failure is enough -> quarantine so future frames skip fast.
                MarkBad(id, ex);
                return null;
            }
        }
        #endregion
    }
}