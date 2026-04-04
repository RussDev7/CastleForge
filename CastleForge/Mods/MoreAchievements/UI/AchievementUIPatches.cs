/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;
using DNA.CastleMinerZ.Achievements;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ.UI;
using System.Reflection;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using System.Linq;
using HarmonyLib;
using DNA.Audio;
using DNA.Input;
using System;
using DNA;

namespace MoreAchievements
{
    /// <summary>
    /// Achievement UI:
    /// ---------------
    /// • Adds an "Achievements" button to:
    ///     - Main menu (FrontEnd main menu)
    ///     - In-game menu
    ///
    /// • Intercepts selection to show a custom Achievement browser screen:
    ///     - Shows vanilla (Steam) + custom achievements
    ///     - Displays ID, icon, name, description, progress
    ///     - Supports difficulty tiers (Vanilla/Easy/Normal/Hard/Brutal/Insane)
    ///       with colour-coded rows and an on-screen legend
    ///     - Sorts within difficulty by name or numeric ID (config-driven)
    ///
    /// • Hooks CastleMinerZ achievement unlocks to show a Steam-style
    ///   bottom-right toast via the overlayScreenGroup, honouring
    ///   the popup/sound options from AchievementUIConfig/MAConfig.
    /// </summary>
    #region Achievement Menu Button / Browser / Toast - Patches

    /// <summary>
    /// Harmony patches that:
    ///  - Inject an "Achievements" row into MainMenu + InGameMenu.
    ///  - Intercept SelectedMenuItemArgs on those menus to open
    ///    the AchievementBrowserScreen instead of enum-casting.
    ///  - Hook CastleMinerZAchievementManager.Achieved to drive
    ///    the AchievementToastOverlay.
    /// </summary>
    internal static class AchievementUIPatches
    {
        #region Registry: Tag + MenuItem Mapping

        /// <summary>
        /// Unique tag object used to flag our injected "Achievements"
        /// menu rows inside the main and in-game menus.
        /// </summary>
        internal sealed class AchievementsTag
        {
            /// <summary>
            /// Singleton instance used as the tag value on our custom menu items.
            /// </summary>
            public static readonly AchievementsTag Instance = new AchievementsTag();

            /// <summary>
            /// Private ctor to enforce singleton usage via <see cref="Instance"/>.
            /// </summary>
            private AchievementsTag()
            {
            }
        }

        /// <summary>
        /// Registry that tracks the "Achievements" menu item we inject into
        /// each <see cref="MenuScreen"/> (e.g., MainMenu, InGameMenu), so
        /// we can later update or remove it safely.
        /// </summary>
        internal static class AchMenuItemRegistry
        {
            /// <summary>
            /// Maps each menu screen instance to its injected "Achievements"
            /// menu item element. Uses a <see cref="ConditionalWeakTable{TKey,TValue}"/>
            /// so entries are automatically discarded when a screen is GC'd.
            /// </summary>
            private static readonly ConditionalWeakTable<MenuScreen, MenuItemElement> _items =
                new ConditionalWeakTable<MenuScreen, MenuItemElement>();

            /// <summary>
            /// Tag value applied to our custom menu items so they can be
            /// identified later without relying on string comparisons.
            /// </summary>
            public static object Tag => AchievementsTag.Instance;

            /// <summary>
            /// Stores (or replaces) the injected "Achievements" menu item
            /// associated with a specific <paramref name="screen"/>.
            /// </summary>
            public static void Remember(MenuScreen screen, MenuItemElement item)
            {
                if (screen == null) return;

                // Remove any existing mapping for this screen, ignoring failures.
                try { _items.Remove(screen); } catch { /* ignore */ }
                if (item != null)
                    _items.Add(screen, item);
            }

            /// <summary>
            /// Retrieves the injected "Achievements" menu item for the given
            /// <paramref name="screen"/>, or null if none is registered.
            /// </summary>
            public static MenuItemElement Get(MenuScreen screen)
                => screen != null && _items.TryGetValue(screen, out var mi) ? mi : null;
        }
        #endregion

        #region PickerSelectUtil Clone - But For Achievement Tag

        /// <summary>
        /// Helper to detect whether a SelectedMenuItemArgs event represents
        /// selection of *our* injected "Achievements" row, without knowing the
        /// concrete SelectedMenuItemArgs type.
        /// </summary>
        public static class MenuSelectUtil
        {
            /// <summary>
            /// Tries to extract any "tag-like" object from the selection event
            /// argument. This method:
            ///   • First probes common property/field names (Item, Tag, Value, etc.).
            ///   • Then falls back to scanning object-typed properties/fields.
            /// It is used to locate the marker object attached to our Achievements menu
            /// row so we can reliably recognize its selection across different menu
            /// event types.
            /// </summary>
            private static object TryGetAnyTag(object e)
            {
                if (e == null) return null;

                // Fast path: Common property/field names.
                foreach (var name in new[] { "Item", "Tag", "Value", "SelectedItem", "Payload", "MenuItem" })
                {
                    var p = AccessTools.Property(e.GetType(), name);
                    if (p != null)
                    {
                        try { return p.GetValue(e, null); } catch { }
                    }
                    var f = AccessTools.Field(e.GetType(), name);
                    if (f != null)
                    {
                        try { return f.GetValue(e); } catch { }
                    }
                }

                // Fallback: Scan object-typed props/fields for our tag.
                foreach (var p in e.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (p.PropertyType != typeof(object)) continue;
                    try
                    {
                        var v = p.GetValue(e, null);
                        if (ReferenceEquals(v, AchMenuItemRegistry.Tag)) return v;
                    }
                    catch { }
                }
                foreach (var f in e.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType != typeof(object)) continue;
                    try
                    {
                        var v = f.GetValue(e);
                        if (ReferenceEquals(v, AchMenuItemRegistry.Tag)) return v;
                    }
                    catch { }
                }

                return null;
            }

            /// <summary>Returns true if this selection belongs to our "Achievements" row.</summary>
            public static bool IsOurSelection(object sender, object e)
            {
                // Most reliable: The tag we passed to AddMenuItem.
                var tag = TryGetAnyTag(e);
                if (ReferenceEquals(tag, AchMenuItemRegistry.Tag))
                    return true;

                // Secondary heuristic: If sender is a MenuScreen, match the selected row instance.
                if (sender is MenuScreen ms)
                {
                    var ours = AchMenuItemRegistry.Get(ms);
                    if (ours != null)
                    {
                        foreach (var p in e.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (typeof(MenuItemElement).IsAssignableFrom(p.PropertyType))
                            {
                                try { if (ReferenceEquals(p.GetValue(e, null), ours)) return true; } catch { }
                            }
                        }
                        foreach (var f in e.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (typeof(MenuItemElement).IsAssignableFrom(f.FieldType))
                            {
                                try { if (ReferenceEquals(f.GetValue(e), ours)) return true; } catch { }
                            }
                        }
                    }
                }

                return false;
            }
        }
        #endregion

        #region MenuOrderHelper Clone - Place Row Above Anchor By Localized String

        /// <summary>
        /// Utility for reordering menu items so our custom achievement entry is placed
        /// directly above a given "anchor" item (e.g. Options / Inventory), using the
        /// localized text from the Strings resource.
        /// </summary>
        public static class MenuOrderHelper
        {
            /// <summary>
            /// Name of the property on Strings used as anchor. Default "Options"
            /// for main menu and "Inventory" for in-game menu.
            /// </summary>
            private static string _anchorProperty = "Options";

            /// <summary>
            /// Uses reflection to locate the first field on the given screen that is
            /// an <see cref="IList{T}"/> of <see cref="MenuItemElement"/> and returns it.
            /// Falls back through the inheritance chain until a match is found or null.
            /// </summary>
            private static IList<MenuItemElement> GetItemList(MenuScreen screen)
            {
                if (screen == null) return null;

                var t = screen.GetType();
                while (t != null)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        var ft = f.FieldType;

                        if (typeof(IList<MenuItemElement>).IsAssignableFrom(ft))
                        {
                            try { return (IList<MenuItemElement>)f.GetValue(screen); } catch { }
                        }

                        if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            var arg = ft.GetGenericArguments()[0];
                            if (typeof(MenuItemElement).IsAssignableFrom(arg))
                            {
                                if (f.GetValue(screen) is System.Collections.IList obj)
                                {
                                    if (obj is IList<MenuItemElement> direct)
                                        return direct;
                                }
                            }
                        }
                    }
                    t = t.BaseType;
                }
                return null;
            }

            /// <summary>
            /// Resolves the anchor text by reading the configured <see cref="_anchorProperty"/>
            /// from DNA.CastleMinerZ.Globalization.Strings via reflection.
            /// </summary>
            private static string GetAnchorText()
            {
                try
                {
                    var stringsType = AccessTools.TypeByName("DNA.CastleMinerZ.Globalization.Strings");
                    var p = AccessTools.Property(stringsType, _anchorProperty);
                    if (p != null)
                        return p.GetValue(null, null) as string;
                }
                catch { }
                return null;
            }

            /// <summary>
            /// Attempts to read a display text from the menu item using common property
            /// / field names such as Text, Caption, Title, Label, or Content.
            /// </summary>
            private static string GetItemText(MenuItemElement mi)
            {
                if (mi == null) return null;

                foreach (var name in new[] { "Text", "Caption", "Title", "Label", "Content" })
                {
                    var p = AccessTools.Property(mi.GetType(), name);
                    if (p != null && p.PropertyType == typeof(string))
                    {
                        try { return (string)p.GetValue(mi, null); } catch { }
                    }

                    var f = AccessTools.Field(mi.GetType(), name);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        try { return (string)f.GetValue(mi); } catch { }
                    }
                }
                return null;
            }

            /// <summary>
            /// Finds the index of the anchor item in the given list by comparing its
            /// text with the localized anchor text. If not found, falls back to the
            /// last item in the list.
            /// </summary>
            private static int FindAnchorIndex(IList<MenuItemElement> list)
            {
                if (list == null || list.Count == 0) return -1;

                var anchorText = GetAnchorText();
                if (!string.IsNullOrEmpty(anchorText))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = GetItemText(list[i]);
                        if (!string.IsNullOrEmpty(t) &&
                            string.Equals(t, anchorText, StringComparison.OrdinalIgnoreCase))
                        {
                            return i;
                        }
                    }
                }

                return list.Count - 1; // Fallback: Last item.
            }

            /// <summary>
            /// Moves our custom main-menu item to sit directly above the anchor item
            /// (e.g. Options), using <paramref name="anchorProperty"/> on Strings
            /// to find the correct localized entry.
            /// </summary>
            public static void MainMenu_PlaceAbove(MainMenu menu, string anchorProperty)
            {
                if (menu == null) return;
                _anchorProperty = anchorProperty;

                var ours = AchMenuItemRegistry.Get(menu);
                if (ours == null) return;

                var list = GetItemList(menu);
                if (list == null) return;

                int ourIdx = list.IndexOf(ours);
                if (ourIdx < 0) return;

                int anchorIdx = FindAnchorIndex(list);
                if (anchorIdx < 0) return;

                int targetIdx = Math.Max(0, anchorIdx);

                if (ourIdx == targetIdx) return;

                // Remove, re-resolve anchor (in case indices shifted), then insert
                // our item at the correct position.
                list.RemoveAt(ourIdx);

                anchorIdx = FindAnchorIndex(list);
                if (anchorIdx < 0) anchorIdx = list.Count;

                targetIdx = Math.Max(0, anchorIdx);
                if (targetIdx > list.Count) targetIdx = list.Count;

                list.Insert(targetIdx, ours);
            }

            /// <summary>
            /// Moves our custom in-game menu item to sit directly above the anchor item
            /// (e.g. Inventory), using <paramref name="anchorProperty"/> on Strings
            /// to find the correct localized entry.
            /// </summary>
            public static void InGameMenu_PlaceAbove(InGameMenu menu, string anchorProperty)
            {
                if (menu == null) return;
                _anchorProperty = anchorProperty;

                var ours = AchMenuItemRegistry.Get(menu);
                if (ours == null) return;

                var list = GetItemList(menu);
                if (list == null) return;

                int ourIdx = list.IndexOf(ours);
                if (ourIdx < 0) return;

                int anchorIdx = FindAnchorIndex(list);
                if (anchorIdx < 0) return;

                int targetIdx = Math.Max(0, anchorIdx);

                if (ourIdx == targetIdx) return;

                // Remove, re-resolve anchor (in case indices shifted), then insert
                // our item at the correct position.
                list.RemoveAt(ourIdx);

                anchorIdx = FindAnchorIndex(list);
                if (anchorIdx < 0) anchorIdx = list.Count;

                targetIdx = Math.Max(0, anchorIdx);
                if (targetIdx > list.Count) targetIdx = list.Count;

                list.Insert(targetIdx, ours);
            }
        }
        #endregion

        #region Launcher Helpers

        /// <summary>
        /// Helper methods for opening the achievement browser from either
        /// the main menu front-end or the in-game UI.
        /// </summary>
        public static class BrowserLauncher
        {
            /// <summary>
            /// Attempts to open the achievement browser from the front-end
            /// (main menu) UI. Returns false if the game or front-end
            /// UI is not available.
            /// </summary>
            public static bool LaunchFromFrontEnd()
            {
                var gm = CastleMinerZGame.Instance;
                var fe = gm?.FrontEnd;
                var ui = fe?._uiGroup;
                if (gm == null || fe == null || ui == null)
                    return false;

                ui.PushScreen(new AchievementBrowserScreen(gm));
                return true;
            }

            /// <summary>
            /// Attempts to open the achievement browser from the in-game HUD
            /// layer. Returns false if the game or in-game UI group
            /// is not available.
            /// </summary>
            public static bool LaunchInGame()
            {
                var gm = CastleMinerZGame.Instance;
                var gs = gm?.GameScreen;
                var uiGroup = gs?._uiGroup;
                if (gm == null || gs == null || uiGroup == null)
                    return false;

                uiGroup.PushScreen(new AchievementBrowserScreen(gm));
                return true;
            }
        }
        #endregion
    }
    #endregion

    #region Achievement Browser Screen

    /// <summary>
    /// Non-modal overlay that lists all achievements (vanilla + custom)
    /// with ID, icon (from !Mods\MoreAchievements\CustomIcons), title,
    /// description, and progress.
    ///
    /// ESC or CLOSE closes the overlay. Mouse wheel scroll supported.
    /// </summary>
    internal sealed class AchievementBrowserScreen : Screen
    {
        #region Fields & Layout

        /// <summary>
        /// Back-reference to the running CastleMinerZ game instance.
        /// Used for fonts, content, and achievement manager access.
        /// </summary>
        private readonly CastleMinerZGame _game;

        /// <summary>
        /// Fonts used for the browser UI:
        /// title text, row text, and small legend / metadata text.
        /// </summary>
        private SpriteFont _titleFont, _itemFont, _smallFont;

        /// <summary>
        /// 1x1 white texture used as a generic rectangle fill / border
        /// for panels, rows, and legend boxes.
        /// </summary>
        private Texture2D _white;

        /// <summary>
        /// Main panel rectangle that contains the achievement list and buttons.
        /// </summary>
        private Rectangle _panelRect;

        /// <summary>
        /// Sub-rectangle inside the panel where the scrollable achievement list is drawn.
        /// </summary>
        private Rectangle _listRect;

        /// <summary>
        /// Rectangle for the bottom-right CLOSE button.
        /// </summary>
        private Rectangle _closeRect;

        /// <summary>
        /// Last viewport used to lay out rectangles; allows detecting
        /// when the window size changes so layout can be recomputed.
        /// </summary>
        private Rectangle _lastVp;

        /// <summary>Row height for each achievement entry (from config).</summary>
        private int RowH => AchievementUIConfig.RowH;

        /// <summary>Extra vertical spacing between rows (from config).</summary>
        private int RowPad => AchievementUIConfig.RowPad;

        /// <summary>Padding inside the outer panel (from config).</summary>
        private int PanelPad => AchievementUIConfig.PanelPad;

        /// <summary>Gap between the panel title and the top of the list (from config).</summary>
        private int TitleGap => AchievementUIConfig.TitleGap;

        /// <summary>Gap between the list and the bottom button row (from config).</summary>
        private int ButtonsGap => AchievementUIConfig.ButtonsGap;

        /// <summary>Padding inside the per-row icon box (from config).</summary>
        private int IconPad => AchievementUIConfig.IconPad;

        /// <summary>Horizontal gap between the row icon and its text (from config).</summary>
        private int IconGap => AchievementUIConfig.IconGap;

        /// <summary>
        /// Current vertical scroll offset in pixels for the achievement list.
        /// </summary>
        private int _scroll;

        /// <summary>
        /// Cached rectangles for a simple vertical scrollbar to the right of the
        /// achievement list.
        /// </summary>
        private Rectangle _scrollbarTrackRect;

        /// <summary>
        /// Thumb rectangle that indicates the visible portion of the list and
        /// can be dragged with the mouse.
        /// </summary>
        private Rectangle _scrollbarThumbRect;

        /// <summary>
        /// True if the scrollbar should be drawn (content taller than list).
        /// </summary>
        private bool _scrollbarVisible;

        /// <summary>
        /// True while the user is dragging the scrollbar thumb.
        /// </summary>
        private bool _draggingScrollbar;

        /// <summary>
        /// Mouse Y position at the start of a drag gesture.
        /// </summary>
        private int _dragStartMouseY;

        /// <summary>
        /// Scroll offset at the start of a drag gesture.
        /// </summary>
        private int _dragStartScroll;

        /// <summary>
        /// Base green colour used for menu accents (e.g. selected states).
        /// </summary>
        private static readonly Color MenuGreen = new Color(78, 177, 61);

        /// <summary>
        /// Flattened, pre-built list of achievement rows (vanilla + custom)
        /// used for drawing and sorting in the browser.
        /// </summary>
        private readonly List<AchievementRow> _rows = new List<AchievementRow>();

        /// <summary>
        /// Cache of loaded achievement icon textures, keyed by API name or
        /// file stem, so they only need to be loaded once.
        /// </summary>
        private readonly Dictionary<string, Texture2D> _icons =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Image file extensions that will be probed when loading custom icons
        /// from disk (per achievement).
        /// </summary>
        private static readonly string[] IconExts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

        /// <summary>
        /// Data model for a single row in the achievement browser.
        /// Holds both vanilla and custom achievements plus UI metadata.
        /// </summary>
        private sealed class AchievementRow
        {
            public int    Index;           // Index into the underlying achievement manager list.
            public bool   IsCustom;        // True if this achievement was registered by MoreAchievements (as opposed to coming from the vanilla Steam list).
            public string IdText;          // Human-readable ID text shown in the UI (e.g. numeric or short code).
            public string ApiName;         // Achievement API name used for lookups and icon mapping.
            public string Name;            // Display name of the achievement (title).
            public string Description;     // Description text explaining how to earn the achievement.
            public bool   Achieved;        // True if the achievement has been earned/unlocked.
            public float  Progress;        // Normalized progress value in the range [0..1].
            public string ProgressMessage; // Human-readable progress text (e.g. "5 / 10", "50% complete").
            public AchievementManager<CastleMinerZPlayerStats>.Achievement Ach; // Back-reference to the underlying achievement object.
            public int    IdSortKey;       // Numeric key used to enforce a stable sort by ID (vanilla first, then custom) when requested.

            /// <summary>
            /// Difficulty bucket used for grouping and colour-coding.
            /// Vanilla achievements default to <see cref="ExtendedAchievementManager.AchievementDifficulty.Vanilla"/>.
            /// </summary>
            public ExtendedAchievementManager.AchievementDifficulty Difficulty;
        }

        /// <summary>
        /// Size of the per-row icon square, derived from row height and padding.
        /// </summary>
        private int IconSize => RowH - IconPad * 2;

        #endregion

        #region Ctor / Lifecycle

        /// <summary>
        /// Constructs the achievement browser screen, enabling input
        /// and allowing the game to render behind the UI.
        /// </summary>
        public AchievementBrowserScreen(CastleMinerZGame game)
            : base(acceptInput: true, drawBehind: true)
        {
            _game = game ?? CastleMinerZGame.Instance;
        }

        /// <summary>
        /// Called when the achievement browser is pushed onto the screen stack.
        /// Initializes fonts, creates helper textures, refreshes achievements,
        /// builds row data, loads icons, and performs the initial layout.
        /// </summary>
        public override void OnPushed()
        {
            base.OnPushed();

            var gm = _game ?? CastleMinerZGame.Instance;

            _titleFont = gm?._largeFont ?? gm?._myriadMed ?? gm?._consoleFont;
            _itemFont = gm?._medFont ?? gm?._consoleFont ?? gm?._myriadMed;
            _smallFont = gm?._myriadSmall ?? gm?._smallFont ?? _itemFont;

            var gd = gm?.GraphicsDevice;
            var mgr = gm?.AcheivmentManager;
            if (gd != null)
            {
                _white = new Texture2D(gd, 1, 1);
                _white.SetData(new[] { Color.White });
                mgr?.Update();
            }

            AchievementArt.EnsureLoaded();

            BuildRows();
            LoadIcons(gd);
            LayoutToScreen();
        }

        /// <summary>
        /// Called when the achievement browser is popped from the screen stack.
        /// Disposes GPU-backed resources (icons, helper textures) and clears
        /// internal caches.
        /// </summary>
        public override void OnPoped()
        {
            try { _white?.Dispose(); } catch { }
            _white = null;

            foreach (var t in _icons.Values)
            {
                try { t?.Dispose(); } catch { }
            }
            _icons.Clear();

            base.OnPoped();
        }

        /// <summary>
        /// Ensures that the UI layout matches the current viewport size.
        /// If the graphics device viewport has changed, recalculates the
        /// panel, list, and button positions to fit the new resolution.
        /// </summary>
        private void EnsureLayoutForCurrentViewport(GraphicsDevice gd)
        {
            if (gd == null) return;
            var vp = gd.Viewport.Bounds;
            if (vp != _lastVp)
            {
                _lastVp = vp;
                LayoutToScreen();
            }
        }

        /// <summary>
        /// Build achievement rows from CastleMinerZAchievementManager:
        /// - Map vanilla ones to AcheivementID.
        /// - Append any custom achievements.
        /// </summary>
        private void BuildRows()
        {
            _rows.Clear();

            var gm = _game ?? CastleMinerZGame.Instance;
            var mgr = gm?.AcheivmentManager;
            if (mgr == null)
                return;

            // Map vanilla achievements to AcheivementID enum.
            var idMap = new Dictionary<AchievementManager<CastleMinerZPlayerStats>.Achievement, AcheivementID>();
            try
            {
                var arr = mgr.Achievements;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var ach = arr[i];
                        if (ach == null) continue;
                        if (Enum.IsDefined(typeof(AcheivementID), i))
                        {
                            idMap[ach] = (AcheivementID)i;
                        }
                    }
                }
            }
            catch { }

            // Now go through the manager's full list to include customs.
            for (int i = 0; i < mgr.Count; i++)
            {
                var ach = mgr[i];
                if (ach == null) continue;

                var row = new AchievementRow
                {
                    Index           = i,
                    Ach             = ach,
                    ApiName         = ach.APIName ?? "",
                    Name            = ach.Name ?? "",
                    Description     = ach.HowToUnlock ?? "",
                    Achieved        = ach.Acheived,
                    Progress        = 0f,
                    ProgressMessage = ""
                };

                // Difficulty: Pull from ExtendedAchievementManager when available,
                // otherwise treat as Vanilla.
                if (mgr is ExtendedAchievementManager extMgr)
                {
                    row.Difficulty = extMgr.GetDifficulty(ach);
                }
                else
                {
                    row.Difficulty = ExtendedAchievementManager.AchievementDifficulty.Vanilla;
                }

                if (idMap.TryGetValue(ach, out var id))
                {
                    row.IsCustom  = false;
                    row.IdText    = $"#{(int)id}"; // $"{(int)id} ({id})";.
                    row.IdSortKey = (int)id;       // numeric ID for sorting
                }
                else
                {
                    row.IsCustom  = true;
                    row.IdText    = $"#{i}";       // $"Custom #{i}";

                    row.IdSortKey = i;             // Put customs after all vanilla IDs, still ordered by their index.
                }

                // Progress & message.
                try
                {
                    row.Progress = MathHelper.Clamp(ach.ProgressTowardsUnlock, 0f, 1f);
                }
                catch { row.Progress = 0f; }

                try
                {
                    row.ProgressMessage = ach.ProgressTowardsUnlockMessage ?? string.Empty;
                }
                catch { row.ProgressMessage = string.Empty; }

                _rows.Add(row);
            }

            // Sort: Unlocked first, then by difficulty bucket, then Name or numeric Id
            // depending on config.
            _rows.Sort((a, b) =>
            {
                // 1) Unlocked first (true > false).
                int cmp = b.Achieved.CompareTo(a.Achieved);
                if (cmp != 0) return cmp;

                // 2) Difficulty grouping: Vanilla, Easy, Normal, Hard, Brutal, Insane.
                cmp = ((int)a.Difficulty).CompareTo((int)b.Difficulty);
                if (cmp != 0) return cmp;

                // 3-4) Within difficulty: Either name-first or id-first.
                if (AchievementUIConfig.SortMode == AchievementSortMode.ByName)
                {
                    // Name -> Id.
                    cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;

                    cmp = a.IdSortKey.CompareTo(b.IdSortKey);
                    if (cmp != 0) return cmp;
                }
                else // AchievementSortMode.ById.
                {
                    // Id -> Name.
                    cmp = a.IdSortKey.CompareTo(b.IdSortKey);
                    if (cmp != 0) return cmp;

                    cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }

                // 5) Final stable tie-breaker by name.
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Recalculate scrollbar rectangles based on current list rect, content
        /// height, and scroll position.
        /// </summary>
        private void UpdateScrollbarRects()
        {
            _scrollbarVisible = false;

            if (_rows == null || _rows.Count == 0)
                return;

            int contentH = _rows.Count * (RowH + RowPad);
            if (contentH <= _listRect.Height || _listRect.Height <= 0)
                return;

            _scrollbarVisible = true;

            // Width of the scrollbar track (scaled with resolution).
            int trackWidth = (int)(18f * Screen.Adjuster.ScaleFactor.Y);

            _scrollbarTrackRect = new Rectangle(
                _listRect.Right - trackWidth,
                _listRect.Top,
                trackWidth,
                _listRect.Height);

            // Clamp scroll to valid range.
            int maxScroll = Math.Max(0, contentH - _listRect.Height);
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;

            // Thumb height proportional to visible fraction, with a minimum.
            int minThumb = (int)(20f * Screen.Adjuster.ScaleFactor.Y);
            int thumbHeight = Math.Max(
                minThumb,
                (int)((float)_listRect.Height * _listRect.Height / contentH));

            float t = (maxScroll > 0) ? (float)_scroll / maxScroll : 0f;
            int thumbY = _scrollbarTrackRect.Top +
                         (int)(t * (_scrollbarTrackRect.Height - thumbHeight));

            _scrollbarThumbRect = new Rectangle(
                _scrollbarTrackRect.X + 2,
                thumbY,
                _scrollbarTrackRect.Width - 4,
                thumbHeight);
        }

        /// <summary>
        /// Lays out the achievements UI to the current screen size:
        /// computes the main panel, title area, scrollable list, and
        /// bottom-right CLOSE button rectangles.
        /// </summary>
        private void LayoutToScreen()
        {
            var r = Screen.Adjuster.ScreenRect;

            int W = Math.Max((int)(r.Width * 0.60f), 720);
            int H = Math.Max((int)(r.Height * 0.70f), 480);
            _panelRect = new Rectangle(r.Center.X - W / 2, r.Center.Y - H / 2, W, H);

            // Title
            var titleText = "Achievements" ?? GetStringsText("Awards");
            int titleH = _titleFont != null
                ? (int)Math.Ceiling(Math.Max(_titleFont.MeasureString(titleText).Y, _titleFont.LineSpacing))
                : 48;

            int btnH = Math.Max(38, _itemFont != null ? _itemFont.LineSpacing + 10 : 38);
            int btnW = 160;

            int titleTop = _panelRect.Top + PanelPad;
            int listTop  = titleTop + titleH + TitleGap;

            int btnY     = _panelRect.Bottom - PanelPad - btnH;

            int listLeft = _panelRect.Left + PanelPad;
            int listW    = _panelRect.Width - PanelPad * 2;
            int listH    = Math.Max(64, btnY - ButtonsGap - listTop);
            _listRect    = new Rectangle(listLeft, listTop, listW, listH);

            _closeRect = new Rectangle(
                _panelRect.Right  - btnW - PanelPad,
                _panelRect.Bottom - btnH - PanelPad,
                btnW,
                btnH);

            // Initialize scrollbar layout.
            UpdateScrollbarRects();
        }
        #endregion

        #region Drawing Helpers

        /// <summary>
        /// Snaps a floating-point position to the nearest whole pixel to avoid
        /// sub-pixel blurring when drawing text or sprites.
        /// </summary>
        private static Vector2 PixelSnap(Vector2 p)
            => new Vector2((float)Math.Round(p.X), (float)Math.Round(p.Y));

        /// <summary>
        /// Draws a string centered within the given bounds, with optional scale
        /// and a simple 1px shadow behind the text.
        /// </summary>
        private static void DrawStringCentered(SpriteBatch sb, SpriteFont f, string text,
                                               Rectangle bounds, Color color,
                                               float scale = 1f, bool shadow = true)
        {
            if (f == null || string.IsNullOrEmpty(text)) return;

            Vector2 size = f.MeasureString(text) * scale;
            Vector2 pos = new Vector2(bounds.Center.X - size.X * 0.5f,
                                      bounds.Center.Y - size.Y * 0.5f);
            pos = PixelSnap(pos);

            if (shadow)
                sb.DrawString(f, text, pos + new Vector2(1, 1), Color.Black);
            sb.DrawString(f, text, pos, color);
        }

        /// <summary>
        /// Returns the base colour associated with a given achievement difficulty
        /// tier, used for tinting rows and legend boxes in the UI.
        /// </summary>
        private static Color GetDifficultyColor(ExtendedAchievementManager.AchievementDifficulty difficulty)
        {
            switch (difficulty)
            {
                case ExtendedAchievementManager.AchievementDifficulty.Easy:
                    return new Color(60, 140, 80, 255);  // Soft green.

                case ExtendedAchievementManager.AchievementDifficulty.Normal:
                    return new Color(70, 110, 160, 255); // Blue-ish.

                case ExtendedAchievementManager.AchievementDifficulty.Hard:
                    return new Color(170, 130, 60, 255); // Amber.

                case ExtendedAchievementManager.AchievementDifficulty.Brutal:
                    return new Color(160, 70, 70, 255);  // Red.

                case ExtendedAchievementManager.AchievementDifficulty.Insane:
                    return new Color(130, 70, 150, 255); // Purple.

                case ExtendedAchievementManager.AchievementDifficulty.Vanilla:
                default:
                    return new Color(40, 40, 40, 255);   // Neutral dark gray.
            }
        }
        #endregion

        #region Render

        /// <summary>
        /// Renders the achievement browser overlay, including the dimmed background,
        /// panel frame, title, scrollable list, difficulty legend, and close button.
        /// </summary>
        protected override void OnDraw(GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
        {
            EnsureLayoutForCurrentViewport(device);

            // Dim the background a bit.
            spriteBatch.Begin();
            spriteBatch.Draw(_white, Screen.Adjuster.ScreenRect, new Color(0, 0, 0, 180));
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
            DrawPanel(spriteBatch);
            DrawTitle(spriteBatch);
            DrawList(spriteBatch);
            DrawDifficultyKey(spriteBatch);
            DrawButtons(spriteBatch);
            spriteBatch.End();

            base.OnDraw(device, spriteBatch, gameTime);
        }

        /// <summary>
        /// Renders the achievement browser overlay, including the dimmed background,
        /// panel frame, title, scrollable list, difficulty legend, and close button.
        /// </summary>
        private void DrawPanel(SpriteBatch sb)
        {
            var bg = new Color(18, 18, 18, 230);
            var border = new Color(60, 60, 60, 255);
            sb.Draw(_white, _panelRect, bg);
            sb.Draw(_white, new Rectangle(_panelRect.Left, _panelRect.Top, _panelRect.Width, 1), border);
            sb.Draw(_white, new Rectangle(_panelRect.Left, _panelRect.Bottom - 1, _panelRect.Width, 1), border);
            sb.Draw(_white, new Rectangle(_panelRect.Left, _panelRect.Top, 1, _panelRect.Height), border);
            sb.Draw(_white, new Rectangle(_panelRect.Right - 1, _panelRect.Top, 1, _panelRect.Height), border);
        }

        /// <summary>
        /// Draws the main panel background and border that contain the achievement UI.
        /// </summary>
        private void DrawTitle(SpriteBatch sb)
        {
            var title = "Achievements" ?? GetStringsText("Awards");
            DrawStringCentered(sb, _titleFont, title,
                               new Rectangle(_panelRect.Left, _panelRect.Top + PanelPad, _panelRect.Width, 56),
                               MenuGreen, 1f, shadow: true);

            // Small stats: unlocked / total
            int unlocked = _rows.Count(r => r.Achieved);
            int total = _rows.Count;
            string stat = $"{unlocked}/{total} unlocked";
            if (_smallFont != null)
            {
                Vector2 size = _smallFont.MeasureString(stat);
                Vector2 pos = PixelSnap(new Vector2(_panelRect.Left + PanelPad,
                                                    _panelRect.Top + PanelPad + 40));
                sb.DrawString(_smallFont, stat, pos + new Vector2(1, 1), Color.Black);
                sb.DrawString(_smallFont, stat, pos, Color.White);
            }
        }

        /// <summary>
        /// Draws the bottom button row (currently just the CLOSE button).
        /// </summary>
        private void DrawButtons(SpriteBatch sb)
        {
            DrawButton(sb, _closeRect, "CLOSE");
        }

        /// <summary>
        /// Draws the bottom button row (currently just the CLOSE button).
        /// </summary>
        private void DrawButton(SpriteBatch sb, Rectangle r, string text)
        {
            var fill = new Color(30, 30, 30, 255);
            var outline = new Color(80, 80, 80, 255);

            sb.Draw(_white, r, fill);
            sb.Draw(_white, new Rectangle(r.Left, r.Top, r.Width, 1), outline);
            sb.Draw(_white, new Rectangle(r.Left, r.Bottom - 1, r.Width, 1), outline);
            sb.Draw(_white, new Rectangle(r.Left, r.Top, 1, r.Height), outline);
            sb.Draw(_white, new Rectangle(r.Right - 1, r.Top, 1, r.Height), outline);

            DrawStringCentered(sb, _itemFont, text, r, Color.White, 1f, shadow: true);
        }

        /// <summary>
        /// Draws the scrollable list of achievement rows, including row background,
        /// icon, name, ID/description line, and progress line, clipped to the list area.
        /// </summary>
        private void DrawList(SpriteBatch sb)
        {
            var gd = sb.GraphicsDevice;

            // Save/restore GD state.
            var oldVP  = gd.Viewport;
            var oldSR  = gd.ScissorRectangle;
            var oldRS  = gd.RasterizerState;
            var oldBS  = gd.BlendState;
            var oldSS0 = gd.SamplerStates[0];
            var oldDS  = gd.DepthStencilState;

            sb.End();
            try
            {
                var raster = new RasterizerState { ScissorTestEnable = true };
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, raster);

                gd.Viewport = oldVP;
                gd.ScissorRectangle = ClampToViewport(_listRect, gd);

                // Recompute scrollbar geometry for this frame.
                UpdateScrollbarRects();

                sb.Draw(_white, _listRect, new Color(22, 22, 22, 255));

                int y = _listRect.Top - _scroll;

                int contentWidth = _listRect.Width;
                if (_scrollbarVisible) contentWidth -= _scrollbarTrackRect.Width;

                for (int i = 0; i < _rows.Count; i++)
                {
                    var rowData = _rows[i];
                    var rowRect = new Rectangle(_listRect.Left, y, contentWidth, RowH);

                    bool visible = rowRect.Bottom >= _listRect.Top && rowRect.Top <= _listRect.Bottom;
                    if (visible)
                    {
                        // Base alternating row colour.
                        var baseFill = (i % 2 == 0)
                            ? new Color(30, 30, 30, 255)
                            : new Color(28, 28, 28, 255);

                        // Difficulty tint (Vanilla stays close to base).
                        var diffColor = Color.Lerp(baseFill, GetDifficultyColor(rowData.Difficulty), 0.4f);
                        var fill      = Color.Lerp(baseFill, diffColor,                              0.4f);

                        // Achieved rows get an extra green-ish tint on top.
                        if (rowData.Achieved)
                        {
                            var baseColor    = new Color(40, 40, 40, 255);  // Neutral dark gray.
                            var achievedTint = new Color(80, 180, 90, 255); // 36, 42, 30, 255.
                            fill = Color.Lerp(baseColor, achievedTint, 0.3f);
                        }

                        sb.Draw(_white, rowRect, fill);

                        // Icon slot.
                        var iconRect = new Rectangle(
                            rowRect.Left + 12,
                            rowRect.Top + (rowRect.Height - IconSize) / 2,
                            IconSize,
                            IconSize);

                        Texture2D frameTex = AchievementArt.Frame;

                        // If we have a frame, draw the icon slightly inset and then the frame on top.
                        // If we don't have it (failed to load), fall back to old behavior.
                        if (frameTex != null && !frameTex.IsDisposed)
                        {
                            // Background under icon (dark).
                            sb.Draw(_white, iconRect, new Color(20, 20, 20, 255));

                            // Inner rect where the actual icon sits.
                            int inset = 4;
                            var innerRect = new Rectangle(
                                iconRect.X + inset,
                                iconRect.Y + inset,
                                iconRect.Width - inset * 2,
                                iconRect.Height - inset * 2);

                            Texture2D iconTex = null;
                            if (!string.IsNullOrEmpty(rowData.ApiName))
                                _icons.TryGetValue(rowData.ApiName, out iconTex);

                            if (iconTex != null && !iconTex.IsDisposed)
                                sb.Draw(iconTex, innerRect, Color.White);
                            else
                                sb.Draw(_white, innerRect, new Color(45, 45, 45, 255));

                            // Finally, the decorative frame scaled to the same rect.
                            sb.Draw(frameTex, iconRect, Color.White);
                        }
                        else
                        {
                            // Fallback: Old simple box + outline.
                            Texture2D iconTex = null;
                            if (!string.IsNullOrEmpty(rowData.ApiName))
                                _icons.TryGetValue(rowData.ApiName, out iconTex);

                            if (iconTex != null && !iconTex.IsDisposed)
                                sb.Draw(iconTex, iconRect, Color.White);
                            else
                                sb.Draw(_white, iconRect, new Color(45, 45, 45, 255));

                            var outline = new Color(20, 20, 20, 160);
                            sb.Draw(_white, new Rectangle(iconRect.Left, iconRect.Top, iconRect.Width, 1), outline);
                            sb.Draw(_white, new Rectangle(iconRect.Left, iconRect.Bottom - 1, iconRect.Width, 1), outline);
                            sb.Draw(_white, new Rectangle(iconRect.Left, iconRect.Top, 1, iconRect.Height), outline);
                            sb.Draw(_white, new Rectangle(iconRect.Right - 1, iconRect.Top, 1, iconRect.Height), outline);
                        }

                        // Text area.
                        int textLeft = iconRect.Right + IconGap;
                        var textRect = new Rectangle(textLeft, rowRect.Top, rowRect.Right - textLeft - 8, rowRect.Height);

                        // First line: name.
                        string title = string.IsNullOrEmpty(rowData.Name) ? "(unnamed)" : rowData.Name;
                        var titlePos = PixelSnap(new Vector2(textRect.Left, textRect.Top + 4));
                        var titleColor = rowData.Achieved ? MenuGreen : Color.White;
                        sb.DrawString(_itemFont, title, titlePos + new Vector2(1, 1), Color.Black * 0.85f);
                        sb.DrawString(_itemFont, title, titlePos, titleColor);

                        // Second line: ID + API.
                        string idLine = $"[{rowData.IdText}]";
                        if (!string.IsNullOrEmpty(rowData.ApiName))
                            idLine += $" {rowData.Description}";
                        var idPos = PixelSnap(titlePos + new Vector2(0, _itemFont.LineSpacing));
                        sb.DrawString(_smallFont, idLine, idPos + new Vector2(1, 1), Color.Black * 0.85f);
                        sb.DrawString(_smallFont, idLine, idPos, new Color(190, 190, 190));

                        // Third line: progress and message.
                        string diffLabel = rowData.Difficulty == ExtendedAchievementManager.AchievementDifficulty.Vanilla
                            ? "Vanilla"
                            : rowData.Difficulty.ToString();
                        float pct = rowData.Progress * 100f;
                        string progText = rowData.Achieved
                            ? $"{diffLabel} | Completed"
                            : $"{diffLabel} | {pct:0}% complete";

                        if (!string.IsNullOrEmpty(rowData.ProgressMessage))
                            progText += $" | {rowData.ProgressMessage}";

                        var progPos = PixelSnap(idPos + new Vector2(0, _smallFont.LineSpacing));
                        sb.DrawString(_smallFont, progText, progPos + new Vector2(1, 1), Color.Black * 0.85f);
                        sb.DrawString(_smallFont, progText, progPos, Color.White);
                    }

                    y += RowH + RowPad;
                }

                // Scrollbar on top.
                if (_scrollbarVisible)
                {
                    // Track.
                    var trackColor  = new Color(10, 10, 10, 220);
                    var borderColor = new Color(0, 0, 0, 220);
                    var thumbColor  = new Color(170, 170, 170, 255);

                    sb.Draw(_white, _scrollbarTrackRect, trackColor);

                    // Simple border around track.
                    sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Left, _scrollbarTrackRect.Top, _scrollbarTrackRect.Width, 1), borderColor);
                    sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Left, _scrollbarTrackRect.Bottom - 1, _scrollbarTrackRect.Width, 1), borderColor);
                    sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Left, _scrollbarTrackRect.Top, 1, _scrollbarTrackRect.Height), borderColor);
                    sb.Draw(_white, new Rectangle(_scrollbarTrackRect.Right - 1, _scrollbarTrackRect.Top, 1, _scrollbarTrackRect.Height), borderColor);

                    // Thumb.
                    sb.Draw(_white, _scrollbarThumbRect, thumbColor);
                }
            }
            finally
            {
                sb.End();
                try
                {
                    gd.Viewport          = oldVP;
                    gd.ScissorRectangle  = oldSR;
                    gd.RasterizerState   = oldRS  ?? RasterizerState.CullCounterClockwise;
                    gd.BlendState        = oldBS  ?? BlendState.AlphaBlend;
                    gd.SamplerStates[0]  = oldSS0 ?? SamplerState.LinearClamp;
                    gd.DepthStencilState = oldDS  ?? DepthStencilState.None;
                }
                catch { }

                sb.Begin();
            }
        }

        /// <summary>
        /// Draws the colour legend at the bottom-left of the panel, showing
        /// the "Completed" key and difficulty colour squares.
        /// </summary>
        private void DrawDifficultyKey(SpriteBatch sb)
        {
            if (_white == null || _smallFont == null)
                return;

            var levels = new[]
            {
                ExtendedAchievementManager.AchievementDifficulty.Vanilla,
                ExtendedAchievementManager.AchievementDifficulty.Easy,
                ExtendedAchievementManager.AchievementDifficulty.Normal,
                ExtendedAchievementManager.AchievementDifficulty.Hard,
                ExtendedAchievementManager.AchievementDifficulty.Brutal,
                ExtendedAchievementManager.AchievementDifficulty.Insane
            };

            float scale      = Screen.Adjuster.ScaleFactor.Y;
            int   boxSize    = (int)(14f * scale);
            int   boxTextGap = (int)(4f  * scale);
            int   groupGap   = (int)(14f * scale);
            int   rowGap     = (int)(8f  * scale); // Vertical gap between rows.

            // Center the whole 2-row block inside the CLOSE button height.
            int totalLegendHeight = boxSize * 2 + rowGap;

            // Top of the legend block, vertically centered within the close button.
            int legendTopY = _closeRect.Y + (_closeRect.Height - totalLegendHeight) / 2;

            // Top row (Completed).
            int row1Y = legendTopY;

            // Bottom row (difficulties).
            int row2Y = legendTopY + boxSize + rowGap;
            // -----------------------------------------------------------------------

            int xStart = _panelRect.Left + PanelPad;

            // ---------------------------------------------------------------------
            // Row 1: "Completed" key (green square).
            // ---------------------------------------------------------------------
            int x1 = xStart;
            if (x1 + boxSize <= _closeRect.Left - 8)
            {
                var boxRect = new Rectangle(x1, row1Y, boxSize, boxSize);

                Color fill = new Color(80, 180, 90, 255);
                sb.Draw(_white, boxRect, fill);

                var border = new Color(0, 0, 0, 200);
                sb.Draw(_white, new Rectangle(boxRect.Left, boxRect.Top, boxRect.Width, 1), border);
                sb.Draw(_white, new Rectangle(boxRect.Left, boxRect.Bottom - 1, boxRect.Width, 1), border);
                sb.Draw(_white, new Rectangle(boxRect.Left, boxRect.Top, 1, boxRect.Height), border);
                sb.Draw(_white, new Rectangle(boxRect.Right - 1, boxRect.Top, 1, boxRect.Height), border);

                const string completedLabel = "Completed";
                Vector2 labelSize = _smallFont.MeasureString(completedLabel);
                Vector2 labelPos = new Vector2(
                    boxRect.Right + boxTextGap,
                    boxRect.Top + (boxRect.Height - labelSize.Y) / 2f
                );

                sb.DrawString(_smallFont, completedLabel, PixelSnap(labelPos + new Vector2(1, 1)), Color.Black);
                sb.DrawString(_smallFont, completedLabel, PixelSnap(labelPos), Color.White);
            }

            // ---------------------------------------------------------------------
            // Row 2: Difficulty keys (Vanilla/Easy/Normal/Hard/Brutal/Insane).
            // ---------------------------------------------------------------------
            int x2 = xStart;

            foreach (var diff in levels)
            {
                if (x2 + boxSize > _closeRect.Left - 8)
                    break;

                var boxRect = new Rectangle(x2, row2Y, boxSize, boxSize);

                Color fill = GetDifficultyColor(diff);
                sb.Draw(_white, boxRect, fill);

                var border = new Color(0, 0, 0, 200);
                sb.Draw(_white, new Rectangle(boxRect.Left, boxRect.Top, boxRect.Width, 1), border);
                sb.Draw(_white, new Rectangle(boxRect.Left, boxRect.Bottom - 1, boxRect.Width, 1), border);
                sb.Draw(_white, new Rectangle(boxRect.Left, boxRect.Top, 1, boxRect.Height), border);
                sb.Draw(_white, new Rectangle(boxRect.Right - 1, boxRect.Top, 1, boxRect.Height), border);

                string label = diff == ExtendedAchievementManager.AchievementDifficulty.Vanilla
                    ? "Vanilla"
                    : diff.ToString();

                Vector2 labelSize = _smallFont.MeasureString(label);
                Vector2 labelPos = new Vector2(
                    boxRect.Right + boxTextGap,
                    boxRect.Top + (boxRect.Height - labelSize.Y) / 2f
                );

                sb.DrawString(_smallFont, label, PixelSnap(labelPos + new Vector2(1, 1)), Color.Black);
                sb.DrawString(_smallFont, label, PixelSnap(labelPos), Color.White);

                x2 = (int)(labelPos.X + labelSize.X + groupGap);
            }
        }

        /// <summary>
        /// Clamps a rectangle to the current graphics device viewport bounds so
        /// scissor rectangles remain valid.
        /// </summary>
        private static Rectangle ClampToViewport(Rectangle r, GraphicsDevice gd)
        {
            var vp = gd.Viewport.Bounds;
            int x = Math.Max(vp.Left, Math.Min(r.Left, vp.Right));
            int y = Math.Max(vp.Top, Math.Min(r.Top, vp.Bottom));
            int w = Math.Max(0, Math.Min(r.Right, vp.Right) - x);
            int h = Math.Max(0, Math.Min(r.Bottom, vp.Bottom) - y);
            return new Rectangle(x, y, w, h);
        }
        #endregion

        #region Input

        /// <summary>
        /// Handles input while the achievement browser is open:
        ///   - Esc closes the screen.
        ///   - Mouse wheel scrolls the list.
        ///   - Dragging the scrollbar thumb scrolls the list.
        ///   - Clicking the scrollbar track jumps to that approximate position.
        ///   - Left-click on the CLOSE button closes the screen.
        /// Unhandled input is passed to the base implementation.
        /// </summary>
        protected override bool OnPlayerInput(InputManager input, GameController controller, KeyboardInput chatpad, GameTime gameTime)
        {
            if (input.Keyboard.WasKeyPressed(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                this.PopMe();
                return false;
            }

            int contentH  = _rows.Count * (RowH + RowPad);
            int maxScroll = Math.Max(0, contentH - _listRect.Height);

            // Mouse wheel scrolling.
            int dWheel = input.Mouse.DeltaWheel;
            if (dWheel != 0)
            {
                _scroll -= Math.Sign(dWheel) * (RowH + RowPad);
                if (_scroll < 0) _scroll = 0;
                if (_scroll > maxScroll) _scroll = maxScroll;
            }

            // Update scrollbar layout after wheel scroll.
            UpdateScrollbarRects();

            var mouse = input.Mouse;
            var mpos = mouse.Position;

            // Scrollbar interactions (track + thumb).
            if (_scrollbarVisible)
            {
                // Start drag when clicking on the thumb.
                if (mouse.LeftButtonPressed && _scrollbarThumbRect.Contains(mpos))
                {
                    _draggingScrollbar = true;
                    _dragStartMouseY = mpos.Y;
                    _dragStartScroll = _scroll;
                }
                // Click on track (but not thumb) -> jump.
                else if (mouse.LeftButtonPressed &&
                         _scrollbarTrackRect.Contains(mpos) &&
                         !_scrollbarThumbRect.Contains(mpos))
                {
                    if (maxScroll > 0)
                    {
                        float tClick = (float)(mpos.Y - _scrollbarTrackRect.Top) /
                                       Math.Max(1, _scrollbarTrackRect.Height);
                        _scroll = (int)(tClick * maxScroll);
                        if (_scroll < 0) _scroll = 0;
                        if (_scroll > maxScroll) _scroll = maxScroll;
                        UpdateScrollbarRects();
                    }
                }

                // Drag update while held.
                if (_draggingScrollbar && mouse.LeftButtonDown)
                {
                    if (maxScroll > 0)
                    {
                        float trackSpan = Math.Max(1, _scrollbarTrackRect.Height - _scrollbarThumbRect.Height);
                        int dy = mpos.Y - _dragStartMouseY;
                        float deltaT = dy / trackSpan;

                        _scroll = _dragStartScroll + (int)(deltaT * maxScroll);
                        if (_scroll < 0) _scroll = 0;
                        if (_scroll > maxScroll) _scroll = maxScroll;

                        UpdateScrollbarRects();
                    }
                }
                else if (!mouse.LeftButtonDown)
                {
                    _draggingScrollbar = false;
                }
            }

            // Close button.
            if (input.Mouse.LeftButtonPressed)
            {
                if (_closeRect.Contains(mpos))
                {
                    if (AchievementUIConfig.PlaySounds) SoundManager.Instance.PlayInstance("Click");
                    this.PopMe();
                    return false;
                }
            }

            return base.OnPlayerInput(input, controller, chatpad, gameTime);
        }
        #endregion

        #region Icon Loading

        /// <summary>
        /// Handles input while the achievement browser is open:
        ///   - Esc closes the screen.
        ///   - Mouse wheel scrolls the list.
        ///   - Left-click on the CLOSE button closes the screen.
        /// Unhandled input is passed to the base implementation.
        /// </summary>
        private static string GetIconsRoot()
        {
            // !Mods\MoreAchievements\CustomIcons
            var ns = typeof(AchievementBrowserScreen).Namespace ?? "MoreAchievements";
            var modRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            return System.IO.Path.Combine(modRoot, "CustomIcons");
        }

        /// <summary>
        /// Returns the base icon root folder used when resolving Steam/Custom
        /// icon paths. Currently identical to <see cref="GetIconsRoot"/> but
        /// kept separate in case the resolution logic needs to vary later.
        /// </summary>
        private static string GetIconsRootBase()
        {
            // !Mods\MoreAchievements\CustomIcons
            var ns = typeof(AchievementBrowserScreen).Namespace ?? "MoreAchievements";
            var modRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            return System.IO.Path.Combine(modRoot, "CustomIcons");
        }

        /// <summary>
        /// Resolve an icon path for the given API name.
        /// If isCustom == false: Prefer ...\Steam\API.* then ...\Custom\API.*
        /// If isCustom == true:  Prefer ...\Custom\API.* then ...\Steam\API.*
        /// Supports icons in the root of those folders *and* any sub-directories.
        /// </summary>
        private static string FindIconPath(string apiName, bool isCustom)
        {
            if (string.IsNullOrWhiteSpace(apiName))
                return null;

            var baseRoot        = GetIconsRootBase();
            var primaryFolder   = System.IO.Path.Combine(baseRoot, isCustom ? "Custom" : "Steam");
            var secondaryFolder = System.IO.Path.Combine(baseRoot, isCustom ? "Steam"  : "Custom");

            foreach (var folder in new[] { primaryFolder, secondaryFolder })
            {
                if (!System.IO.Directory.Exists(folder))
                    continue;

                // 1) First: Look in the root of the folder (old behaviour).
                foreach (var ext in IconExts)
                {
                    var directPath = System.IO.Path.Combine(folder, apiName + ext);
                    if (System.IO.File.Exists(directPath))
                        return directPath;
                }

                // 2) Then: Search all sub-directories for a matching file.
                //    This allows organizing icons under Custom\Easy, Custom\Hard, etc.
                foreach (var ext in IconExts)
                {
                    try
                    {
                        var pattern = apiName + ext;
                        var matches = System.IO.Directory.EnumerateFiles(
                            folder,
                            pattern,
                            System.IO.SearchOption.AllDirectories);

                        var first = matches.FirstOrDefault();
                        if (!string.IsNullOrEmpty(first))
                            return first;
                    }
                    catch
                    {
                        // Ignore IO issues; just fall back to "no icon".
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Loads icon textures for all achievement rows using their API names and
        /// custom/vanilla flag. Any previously loaded icons are disposed before
        /// reloading. Missing or invalid icon files are silently ignored.
        /// </summary>
        private void LoadIcons(GraphicsDevice gd)
        {
            foreach (var t in _icons.Values) { try { t.Dispose(); } catch { } }
            _icons.Clear();
            if (gd == null) return;

            // For every row, try to find an icon based on apiName + IsCustom.
            foreach (var row in _rows)
            {
                if (string.IsNullOrEmpty(row.ApiName)) continue;
                if (_icons.ContainsKey(row.ApiName)) continue;

                string path = FindIconPath(row.ApiName, row.IsCustom);
                if (path == null) continue;

                try
                {
                    using (var s = System.IO.File.OpenRead(path))
                        _icons[row.ApiName] = Texture2D.FromStream(gd, s);
                }
                catch
                {
                    // Ignore bad icons; row will just show the placeholder frame.
                }
            }
        }
        #endregion

        #region Globalization Strings Helper

        /// <summary>
        /// Safely retrieves a localized string from the CastleMinerZ
        /// DNA.CastleMinerZ.Globalization.Strings class by property name.
        /// </summary>
        private static string GetStringsText(string stringName)
        {
            try
            {
                var p = AccessTools.Property(AccessTools.TypeByName("DNA.CastleMinerZ.Globalization.Strings"), stringName);
                if (p != null) return p.GetValue(null, null) as string;
            }
            catch { }
            return null;
        }
        #endregion
    }
    #endregion

    #region Steam-Like Bottom-Right Toast Overlay

    /// <summary>
    /// Always-on overlay screen (in <see cref="CastleMinerZGame.overlayScreenGroup"/>)
    /// that renders a queue of bottom-right "Achievement Unlocked" style toasts.
    ///
    /// Call <see cref="Enqueue"/> to push a new toast into the overlay.
    /// </summary>
    internal sealed class AchievementToastOverlay : Screen
    {
        #region Nested Types & Static Queue / Entry Point

        /// <summary>
        /// Simple data container for one toast entry in the overlay queue.
        /// </summary>
        private struct Toast
        {
            public string Title;       // Title line, typically the achievement name.
            public string Description; // Description or "how to unlock" text.
            public string ApiName;     // API/Steam name, used for icon lookups.
            public bool   IsCustom;    // True when this toast came from one of our custom achievements.
            public float  Age;         // Current age of this toast in seconds.
            public float  Lifetime;    // Total lifetime of this toast in seconds before it expires.
        }

        /// <summary>
        /// Singleton overlay instance that lives in the overlay screen group.
        /// </summary>
        private static AchievementToastOverlay _instance;

        /// <summary>
        /// Global toast queue; newest entries are appended to the end.
        /// </summary>
        private static readonly List<Toast> _toasts = new List<Toast>();

        /// <summary>
        /// Lock used to synchronize access to <see cref="_toasts"/> and <see cref="_instance"/>.
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// True if the given achievement belongs to the extended manager's custom set.
        /// This is used by the toast overlay so it does not have to guess custom-vs-steam
        /// from the API name prefix.
        /// </summary>
        private static bool IsCustomAchievement(AchievementManager<CastleMinerZPlayerStats>.Achievement achievement)
        {
            var gm = CastleMinerZGame.Instance;
            if (gm?.AcheivmentManager is ExtendedAchievementManager ext && achievement != null)
                return ext.CustomAchievements.Contains(achievement);

            return false;
        }

        /// <summary>
        /// Enqueue a toast for the given achievement. Creates/pushes the
        /// overlay screen into overlayScreenGroup if needed.
        /// </summary>
        public static void Enqueue(AchievementManager<CastleMinerZPlayerStats>.Achievement ach)
        {
            if (ach == null) return;

            var gm = CastleMinerZGame.Instance;
            if (gm == null) return;

            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new AchievementToastOverlay(gm);
                    gm.overlayScreenGroup.PushScreen(_instance);
                }

                _toasts.Add(new Toast
                {
                    Title = ach.Name ?? "", // fallback
                    Description = ach.HowToUnlock ?? "",
                    ApiName = ach.APIName ?? "",
                    IsCustom = IsCustomAchievement(ach),
                    Age = 0f,
                    Lifetime = 5.0f
                });
            }
        }

        #endregion

        #region Instance Fields

        /// <summary>Owning game instance used for fonts, devices and resources.</summary>
        private readonly CastleMinerZGame _game;

        /// <summary>Font used for the toast title line.</summary>
        private SpriteFont _titleFont;

        /// <summary>Font used for the description line.</summary>
        private SpriteFont _smallFont;

        /// <summary>Single-pixel white texture used for panels, borders and fills.</summary>
        private Texture2D _white;

        /// <summary>Cached viewport bounds from last layout; used to detect resolution changes.</summary>
        private Rectangle _lastVp;

        /// <summary>
        /// Cache of loaded icon textures keyed by "C:" / "S:" + API name so custom
        /// and steam lookups with the same API name cannot collide.
        /// </summary>
        private readonly Dictionary<string, Texture2D> _icons =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Image file extensions that will be probed when loading custom icons.
        /// </summary>
        private static readonly string[] IconExts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

        #endregion

        #region Construction & Lifetime

        /// <summary>
        /// Creates a new toast overlay bound to the given game instance.
        /// </summary>
        public AchievementToastOverlay(CastleMinerZGame game)
            : base(acceptInput: false, drawBehind: true)
        {
            _game = game ?? CastleMinerZGame.Instance;
        }

        /// <summary>
        /// Called when the overlay is pushed onto the screen stack; initializes
        /// fonts, the 1x1 white texture, and shared art resources.
        /// </summary>
        public override void OnPushed()
        {
            base.OnPushed();

            var gm = _game ?? CastleMinerZGame.Instance;
            _titleFont = gm?._medFont ?? gm?._myriadMed ?? gm?._consoleFont;
            _smallFont = gm?._myriadSmall ?? gm?._smallFont ?? _titleFont;

            var gd = gm?.GraphicsDevice;
            if (gd != null)
            {
                _white = new Texture2D(gd, 1, 1);
                _white.SetData(new[] { Color.White });
            }

            AchievementArt.EnsureLoaded();
        }

        /// <summary>
        /// Called when the overlay is removed; disposes local textures and clears
        /// the global toast queue / singleton instance.
        /// </summary>
        public override void OnPoped()
        {
            try { _white?.Dispose(); } catch { }
            _white = null;

            foreach (var t in _icons.Values)
            {
                try { t?.Dispose(); } catch { }
            }
            _icons.Clear();

            lock (_lock)
            {
                _instance = null;
                _toasts.Clear();
            }

            base.OnPoped();
        }
        #endregion

        #region Layout & Update

        /// <summary>
        /// Ensures cached layout-related data (e.g. viewport bounds) is updated
        /// when the graphics device changes resolution or viewport.
        /// </summary>
        private void EnsureLayoutForCurrentViewport(GraphicsDevice gd)
        {
            if (gd == null) return;
            var vp = gd.Viewport.Bounds;
            if (vp != _lastVp)
            {
                _lastVp = vp;
                // (Currently no additional layout data to recompute.)
            }
        }

        /// <summary>
        /// Advances the age of all queued toasts, removes any that have
        /// exceeded their lifetime, and then updates the base screen.
        /// </summary>
        public override void Update(DNAGame game, GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            lock (_lock)
            {
                for (int i = 0; i < _toasts.Count; i++)
                {
                    var t = _toasts[i];
                    t.Age += dt;
                    _toasts[i] = t;
                }

                _toasts.RemoveAll(t => t.Age >= t.Lifetime);
            }

            base.Update(game, gameTime);
        }
        #endregion

        #region Drawing

        /// <summary>
        /// Draws the current snapshot of toasts as a vertical stack of
        /// bottom-right panels, with fade-in/out and achievement icons.
        /// </summary>
        protected override void OnDraw(GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
        {
            EnsureLayoutForCurrentViewport(device);

            List<Toast> snapshot;
            lock (_lock)
            {
                if (_toasts.Count == 0)
                {
                    base.OnDraw(device, spriteBatch, gameTime);
                    return;
                }
                snapshot = new List<Toast>(_toasts);
            }

            int width = 360;
            int height = 80;
            int margin = 12;
            var screenRect = Screen.Adjuster.ScreenRect;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

            for (int i = 0; i < snapshot.Count; i++)
            {
                var t = snapshot[i];

                // Newest at bottom.
                int idxFromBottom = snapshot.Count - 1 - i;

                int x = screenRect.Right - width - margin;
                int y = screenRect.Bottom - (height + margin) * (idxFromBottom + 1);

                var panel = new Rectangle(x, y, width, height);

                // Simple fade in/out.
                float alpha = 1f;
                const float fadeTime = 0.3f;
                if (t.Age < fadeTime)
                    alpha = t.Age / fadeTime;
                else if (t.Lifetime - t.Age < fadeTime)
                    alpha = MathHelper.Clamp((t.Lifetime - t.Age) / fadeTime, 0f, 1f);

                var bg = Color.Black * (0.80f * alpha);
                var border = new Color(70, 70, 70) * alpha;

                spriteBatch.Draw(_white, panel, bg);
                spriteBatch.Draw(_white, new Rectangle(panel.Left, panel.Top, panel.Width, 1), border);
                spriteBatch.Draw(_white, new Rectangle(panel.Left, panel.Bottom - 1, panel.Width, 1), border);
                spriteBatch.Draw(_white, new Rectangle(panel.Left, panel.Top, 1, panel.Height), border);
                spriteBatch.Draw(_white, new Rectangle(panel.Right - 1, panel.Top, 1, panel.Height), border);

                // Icon
                int iconSize = height - 20;
                var iconRect = new Rectangle(panel.Left + 10, panel.Top + (panel.Height - iconSize) / 2, iconSize, iconSize);

                // Use the actual achievement membership captured at enqueue time.
                // Custom achievements in this mod still use ACH_* API names, so prefix-based
                // guessing can misclassify them and send lookups to the wrong folder first.
                bool isCustomHint = t.IsCustom;

                Texture2D iconTex = null;
                if (!string.IsNullOrEmpty(t.ApiName))
                    iconTex = GetOrLoadIcon(device, t.ApiName, isCustomHint);

                Texture2D frameTex = AchievementArt.Frame;

                if (frameTex != null && !frameTex.IsDisposed)
                {
                    // Underlay
                    spriteBatch.Draw(_white, iconRect, new Color(20, 20, 20) * alpha);

                    int inset = 3;
                    var innerRect = new Rectangle(
                        iconRect.X + inset,
                        iconRect.Y + inset,
                        iconRect.Width - inset * 2,
                        iconRect.Height - inset * 2);

                    if (iconTex != null && !iconTex.IsDisposed)
                        spriteBatch.Draw(iconTex, innerRect, Color.White * alpha);
                    else
                        spriteBatch.Draw(_white, innerRect, new Color(45, 45, 45) * alpha);

                    spriteBatch.Draw(frameTex, iconRect, Color.White * alpha);
                }
                else
                {
                    // Fallback to old behavior
                    if (iconTex != null && !iconTex.IsDisposed)
                        spriteBatch.Draw(iconTex, iconRect, Color.White * alpha);
                    else
                        spriteBatch.Draw(_white, iconRect, new Color(45, 45, 45) * alpha);
                }

                // Text
                int textLeft = iconRect.Right + 12;
                var titlePos = new Vector2(textLeft, panel.Top + 8);
                var descPos = new Vector2(textLeft, panel.Top + 8 + (_titleFont?.LineSpacing ?? 18));

                var titleColor = new Color(200, 230, 200) * alpha;
                var descColor = Color.White * alpha;

                if (_titleFont != null)
                {
                    spriteBatch.DrawString(_titleFont, t.Title, titlePos + new Vector2(1, 1), Color.Black * alpha);
                    spriteBatch.DrawString(_titleFont, t.Title, titlePos, titleColor);
                }

                if (_smallFont != null && !string.IsNullOrEmpty(t.Description))
                {
                    spriteBatch.DrawString(_smallFont, t.Description, descPos + new Vector2(1, 1), Color.Black * alpha);
                    spriteBatch.DrawString(_smallFont, t.Description, descPos, descColor);
                }
            }

            spriteBatch.End();

            base.OnDraw(device, spriteBatch, gameTime);
        }
        #endregion

        #region Icon Loading

        #region Path Helpers

        /// <summary>
        /// Returns the root folder for this mod under !Mods\{Namespace}.
        /// Currently used as the base for custom achievement icon lookups.
        /// </summary>
        /// <remarks>
        /// Example:
        ///   <GameRoot>\!Mods\MoreAchievements
        /// </remarks>
        private static string GetIconsRoot()
        {
            var ns = typeof(AchievementToastOverlay).Namespace ?? "MoreAchievements";
            var modRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            return System.IO.Path.Combine(modRoot, "CustomIcons");
        }

        /// <summary>
        /// Returns the base icon root folder:
        ///   !Mods\MoreAchievements\CustomIcons
        /// for the current assembly namespace.
        /// </summary>
        /// <remarks>
        /// This is the parent for the Custom and Steam icon subfolders.
        /// </remarks>
        private static string GetIconsRootBase()
        {
            // !Mods\MoreAchievements\CustomIcons
            var ns = typeof(AchievementToastOverlay).Namespace ?? "MoreAchievements";
            var modRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", ns);
            return System.IO.Path.Combine(modRoot, "CustomIcons");
        }
        #endregion

        #region Icon Lookup & Caching

        /// <summary>
        /// Attempts to resolve an icon file path for the given achievement API name.
        /// Checks primary (Custom/Steam) first, then the secondary folder.
        /// Supports icons both in the root and in any sub-directories.
        /// </summary>
        private static string FindIconPath(string apiName, bool isCustomHint)
        {
            if (string.IsNullOrWhiteSpace(apiName))
                return null;

            var baseRoot        = GetIconsRootBase();
            var primaryFolder   = System.IO.Path.Combine(baseRoot, isCustomHint ? "Custom" : "Steam");
            var secondaryFolder = System.IO.Path.Combine(baseRoot, isCustomHint ? "Steam"  : "Custom");

            foreach (var folder in new[] { primaryFolder, secondaryFolder })
            {
                if (!System.IO.Directory.Exists(folder))
                    continue;

                // 1) Root-only check (preserve existing behaviour).
                foreach (var ext in IconExts)
                {
                    var directPath = System.IO.Path.Combine(folder, apiName + ext);
                    if (System.IO.File.Exists(directPath))
                        return directPath;
                }

                // 2) Recursive search under this folder.
                foreach (var ext in IconExts)
                {
                    try
                    {
                        var pattern = apiName + ext;
                        var matches = System.IO.Directory.EnumerateFiles(
                            folder,
                            pattern,
                            System.IO.SearchOption.AllDirectories);

                        var first = matches.FirstOrDefault();
                        if (!string.IsNullOrEmpty(first))
                            return first;
                    }
                    catch
                    {
                        // Ignore IO issues and keep looking in other folders/extensions.
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a cached icon texture for the given achievement API name,
        /// loading it from disk on first use if necessary.
        /// </summary>
        private Texture2D GetOrLoadIcon(GraphicsDevice gd, string apiName, bool isCustomHint)
        {
            if (string.IsNullOrEmpty(apiName) || gd == null)
                return null;

            string cacheKey = (isCustomHint ? "C:" : "S:") + apiName;

            if (_icons.TryGetValue(cacheKey, out var cached) && cached != null && !cached.IsDisposed)
                return cached;

            string path = FindIconPath(apiName, isCustomHint);
            if (path == null)
                return null;

            try
            {
                using (var s = System.IO.File.OpenRead(path))
                {
                    var tex = Texture2D.FromStream(gd, s);
                    _icons[cacheKey] = tex;
                    return tex;
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #endregion
    }
    #endregion
}