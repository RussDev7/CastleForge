/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Globalization;
using System.Text;
using System.IO;
using System;

namespace LanternLandMap
{
    /// <summary>
    /// -----------------------------------------------------------------------------
    /// Lantern Land Map - Config + State + Hotkeys (single-file module)
    /// -----------------------------------------------------------------------------
    /// This file contains the core, self-contained "settings layer" for the LanternLandMap mod:
    ///
    /// • LanternLandMapConfig
    ///   - Defines defaults for all mod options (hotkeys, sliders, view, drawing, labels, colors, teleport).
    ///   - Loads settings from !Mods\LanternLandMap\LanternLandMap.ini (creates the file if missing).
    ///   - Saves the current in-memory values back to the INI.
    ///   - Can persist runtime UI changes via SaveFromState().
    ///
    /// • LanternLandMapState
    ///   - Holds session/runtime state that should survive closing/reopening the map screen
    ///     (current slider values, visibility toggles, "IsOpen", etc.).
    ///   - Initializes once from config using EnsureInitFromConfig().
    ///
    /// • HotkeyBinding + LanternLandMapHotkeys
    ///   - HotkeyBinding parses strings like "Ctrl+Shift+R" into a key + modifiers.
    ///   - LanternLandMapHotkeys provides edge detection (PressedThisFrame) outside the normal Screen input flow.
    ///
    /// • SimpleIni
    ///   - Minimal INI reader used by the config loader (supports [Section], key=value, comments).
    ///
    /// Notes:
    /// • "LLMConfig" is a backwards-compatible alias wrapper for LanternLandMapConfig.
    /// • Config = Persisted disk settings, State = Live values while the game is running.
    /// -----------------------------------------------------------------------------
    /// </summary>

    #region Enums

    /// <summary>
    /// Tri-state label mode (Desmos-like):
    ///   Off         - Draw nothing.
    ///   DotOnly     - Draw the marker dot only.
    ///   DotAndLabel - Draw marker dot + text label.
    /// </summary>
    internal enum TriStateLabelMode : byte
    {
        Off         = 0,
        DotOnly     = 1,
        DotAndLabel = 2,
    }
    #endregion

    #region Config

    /// <summary>
    /// Central config store for Lantern Land Map.
    ///
    /// Responsibilities:
    /// • Defines default values for all options (hotkeys, drawing, colors, UI).
    /// • Loads LanternLandMap.ini and applies values (clamping where necessary).
    /// • Saves current in-memory values back to LanternLandMap.ini.
    /// • Provides "SaveFromState" to persist runtime state (LanternLandMapState) into the config file.
    /// </summary>
    internal static class LanternLandMapConfig
    {
        #region Paths / Status

        /// <summary>Folder containing LanternLandMap.ini under the CastleForge !Mods output tree.</summary>
        public static readonly string ModFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "LanternLandMap");

        /// <summary>Full path to LanternLandMap.ini.</summary>
        public static readonly string ConfigPath = Path.Combine(ModFolder, "LanternLandMap.Config.ini");

        /// <summary>True once LoadApply() has successfully completed at least once.</summary>
        public static bool IsLoaded { get; private set; } = false;

        #endregion

        #region Hotkeys

        /// <summary>Toggle the map screen on/off.</summary>
        public static HotkeyBinding ToggleMapKey         = HotkeyBinding.Parse("M");

        /// <summary>Reload the INI from disk and apply changes immediately.</summary>
        public static HotkeyBinding ReloadConfigKey      = HotkeyBinding.Parse("Ctrl+Shift+R");

        public static HotkeyBinding ToggleChunkGridKey   = HotkeyBinding.Parse("C");
        public static HotkeyBinding ResetViewToPlayerKey = HotkeyBinding.Parse("Home");
        public static HotkeyBinding ResetViewToOriginKey = HotkeyBinding.Parse("End");

        public static HotkeyBinding ToggleStartLabelsKey = HotkeyBinding.Parse("S");
        public static HotkeyBinding ToggleEndLabelsKey   = HotkeyBinding.Parse("E");
        public static HotkeyBinding ToggleThicknessKey   = HotkeyBinding.Parse("P");
        public static HotkeyBinding ToggleGapKey         = HotkeyBinding.Parse("G");
        public static HotkeyBinding ToggleIndexKey       = HotkeyBinding.Parse("I");
        public static HotkeyBinding ToggleTowersKey      = HotkeyBinding.Parse("T");

        #endregion

        #region Sliders (Desmos: A, R)

        /// <summary>How many rings to draw (A).</summary>
        public static int    RingsToShow    = 10;
        public static int    RingsMin       = 1;
        public static int    RingsMax       = 200;

        /// <summary>Radius where we start our visible window (R, in blocks).</summary>
        public static double TargetRadius   = 1;
        public static double RadiusMin      = 1;
        public static double RadiusMax      = 2_000_000_000d;

        /// <summary>Whether the radius slider uses logarithmic stepping.</summary>
        public static bool   RadiusLogScale = true;

        /// <summary>Scroll multiplier when scrolling over the slider (zoom-like behavior).</summary>
        public static double RadiusWheelMul = 1.15;

        #endregion

        #region View / Camera

        /// <summary>Initial zoom in pixels-per-block (very zoomed out by default).</summary>
        public static float InitialZoomPixelsPerBlock = 0.002f;

        public static float ZoomMinPixelsPerBlock     = 1e-9f;
        public static float ZoomMaxPixelsPerBlock     = 1.0f;

        public static float PanSpeed                  = 1.0f;

        /// <summary>When true, zoom centers around the mouse cursor instead of the screen center.</summary>
        public static bool  ZoomAboutCursor           = true;

        #endregion

        #region Drawing

        /// <summary>Grid step in blocks. (CMZ chunk = 16x16 blocks; region is 24x24 chunks.)</summary>
        public static int   ChunkGridStepBlocks = 16;

        /// <summary>Minimum grid spacing in pixels; step increases while zoomed out to keep the grid readable.</summary>
        public static int   MinGridPixels       = 30;

        public static int   CircleSegmentsMin   = 64;
        public static int   CircleSegmentsMax   = 512;

        public static float RingLineThickness   = 2f;
        public static float AxisThickness       = 2f;
        public static float GridThickness       = 1f;

        /// <summary>Solid ring fill (annulus) using GPU triangle strips (no spoke-pattern artifacts).</summary>
        public static bool  FillRingsSolid      = true;

        /// <summary>Overrides alpha for ring fill color (0..255).</summary>
        public static int   RingFillAlpha       = 80;

        #endregion

        #region UI (Panel / Readout)

        /// <summary>Left sidebar font scaling.</summary>
        public static float PanelFontScale   = 1.75f;

        /// <summary>Bottom cursor bar font scaling.</summary>
        public static float ReadoutFontScale = 1.60f;

        /// <summary>Padding around the bottom readout bar (pixels).</summary>
        public static int   ReadoutPaddingPx = 6;

        #endregion

        #region Labels (Font / Layout / Outline)

        public static float LabelFontScaleMin          = 0.35f;
        public static float LabelFontScaleMax          = 3.00f;
        public static float LabelFontScale             = 1.60f;

        // Screen-space offsets, in pixels (Desmos-style "raise Y so stacked labels don't overlap").
        public static int   StartLabelYOffsetPx        = 0;
        public static int   EndLabelYOffsetPx          = 0;
        public static int   IndexLabelYOffsetPx        = 0;
        public static int   ThicknessLabelYOffsetPx    = 35;
        public static int   GapLabelYOffsetPx          = 35;
        public static int   TowerLabelYOffsetPx        = -40;

        /// <summary>Optional outline for in-map labels (helps readability for dark label colors).</summary>
        public static bool  MapLabelOutlineEnabled     = true;

        /// <summary>Outline thickness in pixels (0 disables).</summary>
        public static int   MapLabelOutlineThicknessPx = 1;

        /// <summary>Outline color for labels.</summary>
        public static Color MapLabelOutlineColor       = ParseColor("255,255,255,255");

        /// <summary>Tri-state defaults (Desmos-like behavior).</summary>
        public static TriStateLabelMode StartLabelMode     = TriStateLabelMode.DotOnly;
        public static TriStateLabelMode EndLabelMode       = TriStateLabelMode.DotOnly;
        public static TriStateLabelMode IndexLabelMode     = TriStateLabelMode.DotOnly;
        public static TriStateLabelMode ThicknessLabelMode = TriStateLabelMode.DotOnly;
        public static TriStateLabelMode GapLabelMode       = TriStateLabelMode.DotOnly;
        public static TriStateLabelMode TowersLabelMode    = TriStateLabelMode.DotOnly;

        #endregion

        #region Colors

        public static Color BackgroundColor     = ParseColor("0,0,0,180");
        public static Color PanelColor          = ParseColor("10,10,10,200");
        public static Color GridColor           = ParseColor("255,255,255,30");
        public static Color AxisColor           = ParseColor("255,255,255,80");
        public static Color PlayerColor         = ParseColor("255,80,80,255");

        // Text colors.
        public static Color PanelTextColor      = ParseColor("255,255,255,255"); // Left panel text.
        public static Color MapTextColor        = ParseColor("255,255,255,255"); // Map overlays (cursor bar, etc.).

        // Label colors (map) - defaults requested:
        //   S = Red, E = Blue, P = Purple, G = Green, I = Black, T = Black
        public static Color StartLabelColor     = ParseColor("255,0,0,255");     // S.
        public static Color EndLabelColor       = ParseColor("0,0,255,255");     // E.
        public static Color ThicknessLabelColor = ParseColor("128,0,128,255");   // P.
        public static Color GapLabelColor       = ParseColor("0,255,0,255");     // G.
        public static Color IndexLabelColor     = ParseColor("0,0,0,255");       // I.
        public static Color TowerLabelColor     = ParseColor("0,0,0,255");       // T.

        /// <summary>Legacy alias (older configs / older code paths).</summary>
        public static Color TextColor           = ParseColor("255,255,255,255");

        /// <summary>Tower marker color (the '+' marker on the map).</summary>
        public static Color TowerColor          = ParseColor("255,255,0,255");

        public static Color[] RingColors = new[]
        {
            ParseColor("#ff5555aa"),
            ParseColor("#55ff55aa"),
            ParseColor("#5555ffaa"),
            ParseColor("#ffff55aa"),
            ParseColor("#ff55ffaa"),
            ParseColor("#55ffffaa"),
        };

        // Vanilla biome underlay colors (RGB; alpha comes from BiomeUnderlayAlpha).
        public static Color ClassicBiomeColor  = new Color(70, 160, 70);
        public static Color LagoonBiomeColor   = new Color(60, 170, 170);
        public static Color DesertBiomeColor   = new Color(200, 180, 60);
        public static Color MountainBiomeColor = new Color(150, 150, 150);
        public static Color ArcticBiomeColor   = new Color(140, 190, 255);
        public static Color DecentBiomeColor   = new Color(190, 120, 220);
        public static Color HellBiomeColor     = new Color(190, 70, 70);

        #endregion

        #region Teleport

        public static bool  RightClickTeleport   = true;
        public static bool  TeleportRequireShift = false;

        /// <summary>TeleportToLocation handles safety/loading; Y is mostly a hint.</summary>
        public static float TeleportY            = 0f;

        #endregion

        #region Default Toggle States (Initial Visibility)

        public static bool  DefaultShowLanternRings   = true;
        public static bool  DefaultShowChunkGrid      = false;
        public static bool  DefaultShowOtherPlayers   = false;

        public static bool  OtherPlayerColorRandom    = true;
        public static Color OtherPlayerColor          = new Color(0, 240, 255, 255); // #00F0FFFF
        public static int   OtherPlayerDotSizePx      = 5;

        public static bool  DefaultShowBiomesUnderlay = false;
        public static bool  DefaultShowBiomeEdges     = false;
        public static byte  BiomeUnderlayAlpha        = 60;

        // Legacy booleans (still written for backwards compatibility, even though tri-state exists).
        public static bool  DefaultShowStartLabels    = true;
        public static bool  DefaultShowEndLabels      = true;
        public static bool  DefaultShowThickness      = true;
        public static bool  DefaultShowGap            = true;
        public static bool  DefaultShowIndex          = true;
        public static bool  DefaultShowTowers         = true;

        #endregion

        #region Load / Save (Public API)

        /// <summary>
        /// Loads LanternLandMap.ini and applies settings to the static fields in this class.
        /// If the INI does not exist, it is created with default values first.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                Directory.CreateDirectory(ModFolder);

                // If missing, write defaults first.
                if (!File.Exists(ConfigPath))
                    WriteDefaults(ConfigPath);

                var ini = SimpleIni.Load(ConfigPath);

                #region Hotkeys

                ToggleMapKey         = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleMap",         "M"));
                ReloadConfigKey      = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ReloadConfig",      "Ctrl+Shift+R"));

                ToggleChunkGridKey   = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleChunkGrid",   "C"));
                ResetViewToPlayerKey = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ResetViewToPlayer", "Home"));
                ResetViewToOriginKey = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ResetViewToOrigin", "End"));

                ToggleStartLabelsKey = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleStartLabels", "S"));
                ToggleEndLabelsKey   = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleEndLabels",   "E"));
                ToggleThicknessKey   = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleThickness",   "P"));
                ToggleGapKey         = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleGap",         "G"));
                ToggleIndexKey       = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleIndex",       "I"));
                ToggleTowersKey      = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleTowers",      "T"));

                #endregion

                #region Sliders

                RingsMin       = Math.Max(1, ini.GetInt                ("Sliders", "RingsMin",       1));
                RingsMax       = Math.Max(RingsMin, ini.GetInt         ("Sliders", "RingsMax",       200));
                RingsToShow    = Clamp(ini.GetInt                      ("Sliders", "Rings",          10),   RingsMin,  RingsMax);
                RadiusMin      = Math.Max(0d, ini.GetDouble            ("Sliders", "RadiusMin",      1));
                RadiusMax      = Math.Max(RadiusMin + 1d, ini.GetDouble("Sliders", "RadiusMax",      2_000_000_000d));
                TargetRadius   = Clamp(ini.GetDouble                   ("Sliders", "TargetRadius",   1),    RadiusMin, RadiusMax);
                RadiusLogScale = ini.GetBool                           ("Sliders", "RadiusLogScale", true);
                RadiusWheelMul = Clamp(ini.GetDouble                   ("Sliders", "RadiusWheelMul", 1.15), 1.01,      5.0);

                #endregion

                #region View

                InitialZoomPixelsPerBlock = Clamp(ini.GetFloat("View", "InitialZoom",     0.002f), 1e-9f,  0.5f);
                ZoomMinPixelsPerBlock     = Clamp(ini.GetFloat("View", "ZoomMin",         1e-9f),  1e-12f, 0.1f);
                ZoomMaxPixelsPerBlock     = Clamp(ini.GetFloat("View", "ZoomMax",         1.0f),   1e-6f,  5f);
                PanSpeed                  = Clamp(ini.GetFloat("View", "PanSpeed",        1.0f),   0.05f,  20f);
                ZoomAboutCursor           = ini.GetBool       ("View", "ZoomAboutCursor", true);

                #endregion

                #region UI

                PanelFontScale   = Clamp(ini.GetFloat("UI", "PanelFontScale",   1.75f),          0.35f, 3f);
                ReadoutFontScale = Clamp(ini.GetFloat("UI", "ReadoutFontScale", LabelFontScale), 0.35f, 3f);
                ReadoutPaddingPx = Clamp(ini.GetInt  ("UI", "ReadoutPaddingPx", 6),              0,     50);

                #endregion

                #region Drawing

                ChunkGridStepBlocks = Math.Max(1, ini.GetInt("Drawing", "ChunkGridStepBlocks", 16));
                MinGridPixels       = Math.Max(5, ini.GetInt("Drawing", "MinGridPixels",       30));

                CircleSegmentsMin   = Clamp(ini.GetInt      ("Drawing", "CircleSegmentsMin",   64),  8,                 4096);
                CircleSegmentsMax   = Clamp(ini.GetInt      ("Drawing", "CircleSegmentsMax",   512), CircleSegmentsMin, 8192);

                RingLineThickness   = Clamp(ini.GetFloat    ("Drawing", "RingLineThickness",   2f),  1f, 8f);
                AxisThickness       = Clamp(ini.GetFloat    ("Drawing", "AxisThickness",       2f),  1f, 8f);
                GridThickness       = Clamp(ini.GetFloat    ("Drawing", "GridThickness",       1f),  1f, 4f);

                FillRingsSolid      = ini.GetBool           ("Drawing", "FillRingsSolid",      true);
                RingFillAlpha       = Clamp(ini.GetInt      ("Drawing", "RingFillAlpha",       80),  0,  255);

                #endregion

                #region Labels

                LabelFontScaleMin          = Clamp(ini.GetFloat("Labels", "FontScaleMin",      0.35f), 0.05f,                    10f);
                LabelFontScaleMax          = Clamp(ini.GetFloat("Labels", "FontScaleMax",      3.00f), LabelFontScaleMin + 0.05f, 10f);
                LabelFontScale             = Clamp(ini.GetFloat("Labels", "FontScale",         1.60f), LabelFontScaleMin,          LabelFontScaleMax);
                StartLabelYOffsetPx        = Clamp(ini.GetInt  ("Labels", "StartYOffsetPx",    0),     -1000,                     1000);
                EndLabelYOffsetPx          = Clamp(ini.GetInt  ("Labels", "EndYOffsetPx",      0),     -1000,                     1000);
                IndexLabelYOffsetPx        = Clamp(ini.GetInt  ("Labels", "IndexYOffsetPx",    0),     -1000,                     1000);
                ThicknessLabelYOffsetPx    = Clamp(ini.GetInt  ("Labels", "ThicknessYOffsetPx",35),    -1000,                     1000);
                GapLabelYOffsetPx          = Clamp(ini.GetInt  ("Labels", "GapYOffsetPx",      35),    -1000,                     1000);
                TowerLabelYOffsetPx        = Clamp(ini.GetInt  ("Labels", "TowerYOffsetPx",    -40),   -1000,                     1000);

                // Optional outline for in-map labels (improves readability, especially for dark label colors).
                MapLabelOutlineEnabled     = ini.GetBool             ("Labels", "OutlineEnabled",    true);
                MapLabelOutlineThicknessPx = Clamp(ini.GetInt        ("Labels", "OutlineThicknessPx",1),     0,                         12);
                MapLabelOutlineColor       = ParseColor(ini.GetString("Labels", "OutlineColor", "255,255,255,255"));

                // Tri-state label modes (fall back to old [Defaults] booleans if missing)
                StartLabelMode             = ParseTriState(ini.GetString("Labels", "StartMode",     null), ini.GetBool("Defaults", "ShowStartLabels", true));
                EndLabelMode               = ParseTriState(ini.GetString("Labels", "EndMode",       null), ini.GetBool("Defaults", "ShowEndLabels",   true));
                IndexLabelMode             = ParseTriState(ini.GetString("Labels", "IndexMode",     null), ini.GetBool("Defaults", "ShowIndex",       true));
                ThicknessLabelMode         = ParseTriState(ini.GetString("Labels", "ThicknessMode", null), ini.GetBool("Defaults", "ShowThickness",   true));
                GapLabelMode               = ParseTriState(ini.GetString("Labels", "GapMode",       null), ini.GetBool("Defaults", "ShowGap",         true));
                TowersLabelMode            = ParseTriState(ini.GetString("Labels", "TowersMode",    null), ini.GetBool("Defaults", "ShowTowers",      true));

                #endregion

                #region Colors

                BackgroundColor     = ParseColor(ini.GetString("Colors", "Background",        "0,0,0,180"));
                PanelColor          = ParseColor(ini.GetString("Colors", "Panel",             "10,10,10,200"));
                GridColor           = ParseColor(ini.GetString("Colors", "Grid",              "255,255,255,30"));
                AxisColor           = ParseColor(ini.GetString("Colors", "Axis",              "255,255,255,80"));
                PlayerColor         = ParseColor(ini.GetString("Colors", "Player",            "255,80,80,255"));

                // Back-compat: Older configs only had [Colors] Text, so default PanelText/MapText to that.
                Color legacyText    = ParseColor(ini.GetString("Colors", "Text",              "255,255,255,255"));
                PanelTextColor      = ParseColor(ini.GetString("Colors", "PanelText",         ColorToCsv(legacyText)));
                MapTextColor        = ParseColor(ini.GetString("Colors", "MapText",           ColorToCsv(legacyText)));

                // Per-label map colors.
                StartLabelColor     = ParseColor(ini.GetString("Colors", "StartLabel",        "255,0,0,255"));
                EndLabelColor       = ParseColor(ini.GetString("Colors", "EndLabel",          "0,0,255,255"));
                ThicknessLabelColor = ParseColor(ini.GetString("Colors", "ThicknessLabel",    "128,0,128,255"));
                GapLabelColor       = ParseColor(ini.GetString("Colors", "GapLabel",          "0,255,0,255"));
                IndexLabelColor     = ParseColor(ini.GetString("Colors", "IndexLabel",        "0,0,0,255"));

                // Towers: Marker + (optional) separate text color.
                TowerColor          = ParseColor(ini.GetString("Colors", "Tower",             "255,255,0,255"));
                TowerLabelColor     = ParseColor(ini.GetString("Colors", "TowerLabel",        ColorToCsv(TowerColor)));

                // Legacy alias.
                TextColor           = PanelTextColor;

                var ringColorsRaw   = ini.GetString("Colors", "RingColors",
                    "#ff5555aa,#55ff55aa,#5555ffaa,#ffff55aa,#ff55ffaa,#55ffffaa");
                RingColors          = ParseColorList(ringColorsRaw, RingColors);

                // Vanilla biome underlay colors.
                ClassicBiomeColor  = ParseColor(ini.GetString("Colors", "ClassicBiomeColor",  "70,160,70,255"));
                LagoonBiomeColor   = ParseColor(ini.GetString("Colors", "LagoonBiomeColor",   "60,170,170,255"));
                DesertBiomeColor   = ParseColor(ini.GetString("Colors", "DesertBiomeColor",   "200,180,60,255"));
                MountainBiomeColor = ParseColor(ini.GetString("Colors", "MountainBiomeColor", "150,150,150,255"));
                ArcticBiomeColor   = ParseColor(ini.GetString("Colors", "ArcticBiomeColor",   "140,190,255,255"));
                DecentBiomeColor   = ParseColor(ini.GetString("Colors", "DecentBiomeColor",   "190,120,220,255"));
                HellBiomeColor     = ParseColor(ini.GetString("Colors", "HellBiomeColor",     "190,70,70,255"));

                #endregion

                #region Teleport

                RightClickTeleport = ini.GetBool ("Teleport", "RightClickTeleport", true);
                TeleportRequireShift = ini.GetBool ("Teleport", "RequireShift",       false);
                TeleportY            = ini.GetFloat("Teleport", "Y",                  0f);

                #endregion

                #region Defaults / Toggles

                DefaultShowChunkGrid      = ini.GetBool("Defaults", "ShowChunkGrid",         DefaultShowChunkGrid);
                DefaultShowOtherPlayers   = ini.GetBool("Defaults", "ShowOtherPlayers",      DefaultShowOtherPlayers);
                DefaultShowLanternRings   = ini.GetBool("Defaults", "ShowLanternRings",      DefaultShowLanternRings);
                DefaultShowBiomesUnderlay = ini.GetBool("Defaults", "ShowBiomesUnderlay",    DefaultShowBiomesUnderlay);
                DefaultShowBiomeEdges     = ini.GetBool("Defaults", "ShowBiomeEdges",        DefaultShowBiomeEdges);
                BiomeUnderlayAlpha        = (byte)MathHelper.Clamp(ini.GetInt("Defaults", "BiomeUnderlayAlpha", BiomeUnderlayAlpha), 0, 255);
                DefaultShowStartLabels    = ini.GetBool("Defaults", "ShowStartLabels",       true);
                DefaultShowEndLabels      = ini.GetBool("Defaults", "ShowEndLabels",         true);
                DefaultShowThickness      = ini.GetBool("Defaults", "ShowThickness",         true);
                DefaultShowGap            = ini.GetBool("Defaults", "ShowGap",               true);
                DefaultShowIndex          = ini.GetBool("Defaults", "ShowIndex",             true);
                DefaultShowTowers         = ini.GetBool("Defaults", "ShowTowers",            true);

                #endregion

                #region Players

                // Players (remote markers).
                string opc = ini.GetString("Players", "OtherPlayerColor", "random");
                if (!string.IsNullOrWhiteSpace(opc) &&
                    string.Equals(opc.Trim(), "random", StringComparison.OrdinalIgnoreCase))
                {
                    OtherPlayerColorRandom = true;
                    OtherPlayerColor       = new Color(0, 240, 255, 255); // Unused when random.
                }
                else
                {
                    OtherPlayerColorRandom = false;
                    OtherPlayerColor       = ParseColor(opc);
                }
                OtherPlayerDotSizePx = Clamp(ini.GetInt("Players", "OtherPlayerDotSizePx", 5), 2, 16);

                #endregion

                // Mark as loaded so screens don't re-load and wipe runtime toggles on every open.
                IsLoaded = true;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config load failed: {ex}.");
            }
        }

        /// <summary>
        /// Writes the CURRENT in-memory config values back to LanternLandMap.ini.
        /// This is intentionally simple: It overwrites the file using our template.
        /// </summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ModFolder);
                File.WriteAllText(ConfigPath, BuildIniText());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config save failed: {ex}.");
            }
        }

        /// <summary>
        /// Persists runtime UI state (sliders/toggles) into the config file.
        /// Call this when the map screen closes or the game exits.
        /// </summary>
        public static void SaveFromState()
        {
            // Copy runtime values into the "defaults" we persist.
            // (Some controls update LanternLandMapState instead of LanternLandMapConfig.)
            if (LanternLandMapState.Initialized)
            {
                RingsToShow               = Clamp(LanternLandMapState.RingsToShow, RingsMin, RingsMax);
                TargetRadius              = Clamp(LanternLandMapState.TargetRadius, RadiusMin, RadiusMax);

                // Chunk grid is still stored as a runtime bool.
                DefaultShowChunkGrid      = LanternLandMapState.ShowChunkGrid;
                DefaultShowOtherPlayers   = LanternLandMapState.ShowOtherPlayers;
                DefaultShowLanternRings   = LanternLandMapState.ShowLanternRings;
                DefaultShowBiomesUnderlay = LanternLandMapState.ShowBiomesUnderlay;
                DefaultShowBiomeEdges     = LanternLandMapState.ShowBiomeEdges;
            }

            Save();
        }
        #endregion

        #region INI Build / Defaults

        /// <summary>
        /// Builds the INI text from the current in-memory values.
        /// </summary>
        private static string BuildIniText()
        {
            // Back-compat booleans for older builds that still read [Defaults].
            bool showStart     = StartLabelMode     != TriStateLabelMode.Off;
            bool showEnd       = EndLabelMode       != TriStateLabelMode.Off;
            bool showIndex     = IndexLabelMode     != TriStateLabelMode.Off;
            bool showThickness = ThicknessLabelMode != TriStateLabelMode.Off;
            bool showGap       = GapLabelMode       != TriStateLabelMode.Off;
            bool showTowers    = TowersLabelMode    != TriStateLabelMode.Off;

            var sb = new StringBuilder(4096);

            sb.AppendLine("; -------------------------------------------------------------------------------------------------");
            sb.AppendLine("; LanternLandMap.ini");
            sb.AppendLine("; -------------------------------------------------------------------------------------------------");
            sb.AppendLine("; Toggle map (default M). Pan with LMB-drag. Zoom with wheel. Right-click to teleport (optional).");
            sb.AppendLine(";");
            sb.AppendLine("; Sliders:");
            sb.AppendLine(";   Rings (A)        - How many rings to draw (big values will lag).");
            sb.AppendLine(";   TargetRadius (R) - Radius where we start our visible window (n0 = ceil(R^2 / (2*K))).");
            sb.AppendLine(";");
            sb.AppendLine("; Labels (toggle while the map is open):");
            sb.AppendLine(";   S = Start X for each ring (floor(r_start)).");
            sb.AppendLine(";   E = End   X for each ring (floor(r_end)).");
            sb.AppendLine(";   P = Ring thickness (floor(t)).");
            sb.AppendLine(";   G = Gap to next ring (floor(g)).");
            sb.AppendLine(";   I = Ring index.");
            sb.AppendLine(";   T = Spawn tower markers (square indices).");
            sb.AppendLine("; -------------------------------------------------------------------------------------------------");
            sb.AppendLine();

            sb.AppendLine("[Hotkeys]");
            sb.AppendLine("ToggleMap            = " + ToggleMapKey);
            sb.AppendLine("ReloadConfig         = " + ReloadConfigKey);
            sb.AppendLine("ToggleChunkGrid      = " + ToggleChunkGridKey);
            sb.AppendLine("ResetViewToPlayer    = " + ResetViewToPlayerKey);
            sb.AppendLine("ResetViewToOrigin    = " + ResetViewToOriginKey);
            sb.AppendLine("ToggleStartLabels    = " + ToggleStartLabelsKey);
            sb.AppendLine("ToggleEndLabels      = " + ToggleEndLabelsKey);
            sb.AppendLine("ToggleThickness      = " + ToggleThicknessKey);
            sb.AppendLine("ToggleGap            = " + ToggleGapKey);
            sb.AppendLine("ToggleIndex          = " + ToggleIndexKey);
            sb.AppendLine("ToggleTowers         = " + ToggleTowersKey);
            sb.AppendLine();

            sb.AppendLine("[Sliders]");
            sb.AppendLine("Rings                = " + RingsToShow.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RingsMin             = " + RingsMin.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RingsMax             = " + RingsMax.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("TargetRadius         = " + ToIniDouble(TargetRadius));
            sb.AppendLine("RadiusMin            = " + ToIniDouble(RadiusMin));
            sb.AppendLine("RadiusMax            = " + ToIniDouble(RadiusMax));
            sb.AppendLine("RadiusLogScale       = " + Bool(RadiusLogScale));
            sb.AppendLine("RadiusWheelMul       = " + ToIniDouble(RadiusWheelMul));
            sb.AppendLine();

            sb.AppendLine("[View]");
            sb.AppendLine("InitialZoom          = " + ToIniFloat(InitialZoomPixelsPerBlock));
            sb.AppendLine("ZoomMin              = " + ToIniFloat(ZoomMinPixelsPerBlock));
            sb.AppendLine("ZoomMax              = " + ToIniFloat(ZoomMaxPixelsPerBlock));
            sb.AppendLine("PanSpeed             = " + ToIniFloat(PanSpeed));
            sb.AppendLine("ZoomAboutCursor      = " + Bool(ZoomAboutCursor));
            sb.AppendLine();

            sb.AppendLine("[UI]");
            sb.AppendLine("PanelFontScale       = " + ToIniFloat(PanelFontScale));
            sb.AppendLine("ReadoutFontScale     = " + ToIniFloat(ReadoutFontScale));
            sb.AppendLine("ReadoutPaddingPx     = " + ReadoutPaddingPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[Drawing]");
            sb.AppendLine("ChunkGridStepBlocks  = " + ChunkGridStepBlocks.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("MinGridPixels        = " + MinGridPixels.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CircleSegmentsMin    = " + CircleSegmentsMin.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CircleSegmentsMax    = " + CircleSegmentsMax.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RingLineThickness    = " + ToIniFloat(RingLineThickness));
            sb.AppendLine("AxisThickness        = " + ToIniFloat(AxisThickness));
            sb.AppendLine("GridThickness        = " + ToIniFloat(GridThickness));
            sb.AppendLine("FillRingsSolid       = " + Bool(FillRingsSolid));
            sb.AppendLine("RingFillAlpha        = " + RingFillAlpha.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[Labels]");
            sb.AppendLine("; Font scale for ring labels (S/E/n/t/g), tower label, and cursor readout.");
            sb.AppendLine("FontScaleMin         = " + ToIniFloat(LabelFontScaleMin));
            sb.AppendLine("FontScaleMax         = " + ToIniFloat(LabelFontScaleMax));
            sb.AppendLine("FontScale            = " + ToIniFloat(LabelFontScale));
            sb.AppendLine("; Label modes (tri-state): off / dot / label");
            sb.AppendLine("StartMode            = " + TriToIni(StartLabelMode));
            sb.AppendLine("EndMode              = " + TriToIni(EndLabelMode));
            sb.AppendLine("IndexMode            = " + TriToIni(IndexLabelMode));
            sb.AppendLine("ThicknessMode        = " + TriToIni(ThicknessLabelMode));
            sb.AppendLine("GapMode              = " + TriToIni(GapLabelMode));
            sb.AppendLine("TowersMode           = " + TriToIni(TowersLabelMode));
            sb.AppendLine("; Additional screen-space Y offsets (pixels) so stacked labels don't overlap.");
            sb.AppendLine("StartYOffsetPx       = " + StartLabelYOffsetPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("EndYOffsetPx         = " + EndLabelYOffsetPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("IndexYOffsetPx       = " + IndexLabelYOffsetPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ThicknessYOffsetPx   = " + ThicknessLabelYOffsetPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("GapYOffsetPx         = " + GapLabelYOffsetPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("TowerYOffsetPx       = " + TowerLabelYOffsetPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("; Optional outline for map labels (helps readability for dark label colors).");
            sb.AppendLine("OutlineEnabled       = " + (MapLabelOutlineEnabled ? "true" : "false"));
            sb.AppendLine("OutlineThicknessPx   = " + MapLabelOutlineThicknessPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("OutlineColor         = " + ColorToCsv(MapLabelOutlineColor));
            sb.AppendLine();

            sb.AppendLine("[Colors]");
            sb.AppendLine("Background           = " + ColorToCsv(BackgroundColor));
            sb.AppendLine("Panel                = " + ColorToCsv(PanelColor));
            sb.AppendLine("Grid                 = " + ColorToCsv(GridColor));
            sb.AppendLine("Axis                 = " + ColorToCsv(AxisColor));
            sb.AppendLine("Player               = " + ColorToCsv(PlayerColor));
            sb.AppendLine("PanelText            = " + ColorToCsv(PanelTextColor));  // Panel/map text.
            sb.AppendLine("MapText              = " + ColorToCsv(MapTextColor));
            sb.AppendLine("StartLabel           = " + ColorToCsv(StartLabelColor)); // Per-label map colors.
            sb.AppendLine("EndLabel             = " + ColorToCsv(EndLabelColor));
            sb.AppendLine("ThicknessLabel       = " + ColorToCsv(ThicknessLabelColor));
            sb.AppendLine("GapLabel             = " + ColorToCsv(GapLabelColor));
            sb.AppendLine("IndexLabel           = " + ColorToCsv(IndexLabelColor));
            sb.AppendLine("TowerLabel           = " + ColorToCsv(TowerLabelColor));
            sb.AppendLine("Text                 = " + ColorToCsv(PanelTextColor));  // Legacy alias (older configs used [Colors] Text).
            sb.AppendLine("Tower                = " + ColorToCsv(TowerColor));      // Tower marker (the '+').
            sb.AppendLine("RingColors           = " + JoinHexColors(RingColors));
            sb.AppendLine("ClassicBiomeColor    = " + ColorToCsv(ClassicBiomeColor));
            sb.AppendLine("LagoonBiomeColor     = " + ColorToCsv(LagoonBiomeColor));
            sb.AppendLine("DesertBiomeColor     = " + ColorToCsv(DesertBiomeColor));
            sb.AppendLine("MountainBiomeColor   = " + ColorToCsv(MountainBiomeColor));
            sb.AppendLine("ArcticBiomeColor     = " + ColorToCsv(ArcticBiomeColor));
            sb.AppendLine("DecentBiomeColor     = " + ColorToCsv(DecentBiomeColor));
            sb.AppendLine("HellBiomeColor       = " + ColorToCsv(HellBiomeColor));
            sb.AppendLine();

            sb.AppendLine("[Teleport]");
            sb.AppendLine("RightClickTeleport   = " + Bool(RightClickTeleport));
            sb.AppendLine("RequireShift         = " + Bool(TeleportRequireShift));
            sb.AppendLine("Y                    = " + ToIniFloat(TeleportY));
            sb.AppendLine();

            sb.AppendLine("[Defaults]");
            sb.AppendLine("ShowChunkGrid        = " + Bool(DefaultShowChunkGrid));
            sb.AppendLine("ShowOtherPlayers     = " + Bool(DefaultShowOtherPlayers));
            sb.AppendLine("ShowLanternRings     = " + Bool(DefaultShowLanternRings));
            sb.AppendLine("ShowBiomesUnderlay   = " + Bool(DefaultShowBiomesUnderlay));
            sb.AppendLine("ShowBiomeEdges       = " + Bool(DefaultShowBiomeEdges));
            sb.AppendLine("BiomeUnderlayAlpha   = " + BiomeUnderlayAlpha.ToString());
            sb.AppendLine("ShowStartLabels      = " + Bool(showStart));
            sb.AppendLine("ShowEndLabels        = " + Bool(showEnd));
            sb.AppendLine("ShowThickness        = " + Bool(showThickness));
            sb.AppendLine("ShowGap              = " + Bool(showGap));
            sb.AppendLine("ShowIndex            = " + Bool(showIndex));
            sb.AppendLine("ShowTowers           = " + Bool(showTowers));
            sb.AppendLine();

            sb.AppendLine("[Players]");
            sb.AppendLine("; \"random\" => stable random per player (Gamertag hash)");
            sb.AppendLine("; or specify a fixed color (#RRGGBBAA or r,g,b,a)");
            sb.AppendLine("OtherPlayerColor     = " + (OtherPlayerColorRandom ? "random" : ColorToCsv(OtherPlayerColor)));
            sb.AppendLine("OtherPlayerDotSizePx = " + ToIniInt(OtherPlayerDotSizePx));
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Writes a default LanternLandMap.ini file to disk (used when no config exists yet).
        /// </summary>
        private static void WriteDefaults(string path)
        {
            File.WriteAllLines(path, new[]
            {
                "; -------------------------------------------------------------------------------------------------",
                "; LanternLandMap.ini",
                "; -------------------------------------------------------------------------------------------------",
                "; Toggle map (default M). Pan with LMB-drag. Zoom with wheel. Right-click to teleport (optional).",
                ";",
                "; Sliders:",
                ";   Rings (A)        - How many rings to draw (big values will lag).",
                ";   TargetRadius (R) - Radius where we start our visible window (n0 = ceil(R^2 / (2*K))).",
                ";",
                "; Labels (toggle while the map is open):",
                ";   S = Start X for each ring (floor(r_start)).",
                ";   E = End   X for each ring (floor(r_end)).",
                ";   P = Ring thickness (floor(t)).",
                ";   G = Gap to next ring (floor(g)).",
                ";   I = Ring index.",
                ";   T = Spawn tower markers (square indices).",
                "; -------------------------------------------------------------------------------------------------",
                "",
                "[Hotkeys]",
                "ToggleMap            = M",
                "ReloadConfig         = Ctrl+Shift+R",
                "ToggleChunkGrid      = C",
                "ResetViewToPlayer    = Home",
                "ResetViewToOrigin    = End",
                "ToggleStartLabels    = S",
                "ToggleEndLabels      = E",
                "ToggleThickness      = P",
                "ToggleGap            = G",
                "ToggleIndex          = I",
                "ToggleTowers         = T",
                "",
                "[Sliders]",
                "Rings                = 10",
                "RingsMin             = 1",
                "RingsMax             = 200",
                "TargetRadius         = 1",
                "RadiusMin            = 1",
                "RadiusMax            = 2000000000",
                "RadiusLogScale       = true",
                "RadiusWheelMul       = 1.15",
                "",
                "[View]",
                "InitialZoom          = 0.002",
                "ZoomMin              = 0.000000001",
                "ZoomMax              = 1.0",
                "PanSpeed             = 1.0",
                "ZoomAboutCursor      = true",
                "",
                "[UI]",
                "PanelFontScale       = 1.75",
                "ReadoutFontScale     = 1.60",
                "ReadoutPaddingPx     = 6",
                "",
                "[Drawing]",
                "ChunkGridStepBlocks  = 16",
                "MinGridPixels        = 30",
                "CircleSegmentsMin    = 64",
                "CircleSegmentsMax    = 512",
                "RingLineThickness    = 2",
                "AxisThickness        = 2",
                "GridThickness        = 1",
                "FillRingsSolid       = true",
                "RingFillAlpha        = 80",
                "",
                "[Labels]",
                "; Font scale for ring labels (S/E/n/t/g), tower label, and cursor readout.",
                "; You can also set a min/max range if you add the in-map slider.",
                "FontScaleMin         = 0.35",
                "FontScaleMax         = 3.00",
                "FontScale            = 1.60",
                "; Label modes (tri-state): off / dot / label",
                "StartMode            = dot",
                "EndMode              = dot",
                "IndexMode            = dot",
                "ThicknessMode        = dot",
                "GapMode              = dot",
                "TowersMode           = dot",
                "; Additional screen-space Y offsets (pixels) so stacked labels don't overlap",
                "; (Higher value pushes the text upward.)",
                "StartYOffsetPx       = 0",
                "EndYOffsetPx         = 0",
                "IndexYOffsetPx       = 0",
                "ThicknessYOffsetPx   = 35",
                "GapYOffsetPx         = 35",
                "TowerYOffsetPx       = -40",
                "; Optional outline for in-map labels (helps readability for dark label colors).",
                "OutlineEnabled       = true",
                "OutlineThicknessPx   = 1",
                "OutlineColor         = 255,255,255,255",
                "",
                "[Colors]",
                "Background           = 0,0,0,180",
                "Panel                = 10,10,10,200",
                "Grid                 = 255,255,255,30",
                "Axis                 = 255,255,255,80",
                "Player               = 255,80,80,255",
                "",
                "; Panel / map overlay text.",
                "PanelText            = 255,255,255,255",
                "MapText              = 255,255,255,255",
                "",
                "; Per-label map colors.",
                "StartLabel           = 255,0,0,255",
                "EndLabel             = 0,0,255,255",
                "ThicknessLabel       = 128,0,128,255",
                "GapLabel             = 0,255,0,255",
                "IndexLabel           = 0,0,0,255",
                "TowerLabel           = 0,0,0,255",
                "",
                "; Legacy alias (older configs only had 'Text').",
                "Text                 = 255,255,255,255",
                "",
                "; Tower marker (the '+').",
                "Tower                = 255,255,0,255",
                "",
                "RingColors           = #ff5555aa,#55ff55aa,#5555ffaa,#ffff55aa,#ff55ffaa,#55ffffaa",
                "",
                "; Vanilla biome underlay colors.",
                "ClassicBiomeColor    = 70,160,70,255",
                "LagoonBiomeColor     = 60,170,170,255",
                "DesertBiomeColor     = 200,180,60,255",
                "MountainBiomeColor   = 150,150,150,255",
                "ArcticBiomeColor     = 140,190,255,255",
                "DecentBiomeColor     = 190,120,220,255",
                "HellBiomeColor       = 190,70,70,255",
                "",
                "[Teleport]",
                "RightClickTeleport   = true",
                "RequireShift         = false",
                "Y                    = 0",
                "",
                "[Defaults]",
                "ShowChunkGrid        = false",
                "ShowLanternRings     = true",
                "ShowBiomesUnderlay   = false",
                "BiomeUnderlayAlpha   = 60",
                "ShowBiomeEdges       = false",
                "ShowStartLabels      = true",
                "ShowEndLabels        = true",
                "ShowThickness        = true",
                "ShowGap              = true",
                "ShowIndex            = true",
                "ShowTowers           = true",
                "",
                "[Players]",
                "; \"random\" => stable random per player (Gamertag hash)",
                "; or specify a fixed color (#RRGGBBAA or r,g,b,a)",
                "OtherPlayerColor     = 0,240,255,255",
                "OtherPlayerDotSizePx = 5",
            });
        }
        #endregion

        #region Formatting Helpers

        /// <summary>Converts a bool to INI-friendly "true"/"false".</summary>
        private static string Bool(bool v) => v ? "true" : "false";

        /// <summary>Formats a float for INI output using invariant culture (stable decimals).</summary>
        private static string ToIniFloat(float v) =>
            v.ToString("0.############################", CultureInfo.InvariantCulture);

        /// <summary>Formats a double for INI output using invariant culture (stable decimals).</summary>
        private static string ToIniDouble(double v) =>
            v.ToString("0.############################", CultureInfo.InvariantCulture);

        /// <summary>
        /// Formats an int for INI output using invariant culture.
        /// Summary: Ensures numbers are written without locale-specific separators.
        /// </summary>
        private static string ToIniInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Converts tri-state label mode to INI token: off/dot/label.</summary>
        private static string TriToIni(TriStateLabelMode m)
        {
            switch (m)
            {
                case TriStateLabelMode.Off: return "off";
                case TriStateLabelMode.DotOnly: return "dot";
                case TriStateLabelMode.DotAndLabel: return "label";
                default: return "dot";
            }
        }

        /// <summary>Serializes a Color as "r,g,b,a" for the INI.</summary>
        private static string ColorToCsv(Color c) =>
            c.R.ToString(CultureInfo.InvariantCulture) + "," +
            c.G.ToString(CultureInfo.InvariantCulture) + "," +
            c.B.ToString(CultureInfo.InvariantCulture) + "," +
            c.A.ToString(CultureInfo.InvariantCulture);

        /// <summary>Serializes a Color as hex "#rrggbbaa" for compact INI lists.</summary>
        private static string ColorToHex(Color c)
        {
            // #RRGGBBAA
            return ("#" +
                c.R.ToString("X2", CultureInfo.InvariantCulture) +
                c.G.ToString("X2", CultureInfo.InvariantCulture) +
                c.B.ToString("X2", CultureInfo.InvariantCulture) +
                c.A.ToString("X2", CultureInfo.InvariantCulture)).ToLowerInvariant();
        }

        /// <summary>Joins multiple colors into a comma-separated "#rrggbbaa" list.</summary>
        private static string JoinHexColors(Color[] colors)
        {
            if (colors == null || colors.Length == 0)
                return "";

            var sb = new StringBuilder();
            for (int i = 0; i < colors.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ColorToHex(colors[i]));
            }
            return sb.ToString();
        }
        #endregion

        #region Clamp Helpers

        /// <summary>Clamps an int to the inclusive [min,max] range.</summary>
        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>Clamps a float to the inclusive [min,max] range.</summary>
        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>Clamps a double to the inclusive [min,max] range.</summary>
        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
        #endregion

        #region Parse Helpers (Tri-state / Colors / Lists)

        /// <summary>
        /// Parses tri-state label mode from INI text; falls back to legacy boolean defaults if missing/unknown.
        /// </summary>
        private static TriStateLabelMode ParseTriState(string raw, bool legacyDefaultOn)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return legacyDefaultOn ? TriStateLabelMode.DotOnly : TriStateLabelMode.Off;

            string s = raw.Trim();

            // Accept names.
            if (s.Equals("off", StringComparison.OrdinalIgnoreCase) || s.Equals("0"))
                return TriStateLabelMode.Off;
            if (s.Equals("dot", StringComparison.OrdinalIgnoreCase) || s.Equals("dotonly", StringComparison.OrdinalIgnoreCase) || s.Equals("1") || s.Equals("-"))
                return TriStateLabelMode.DotOnly;
            if (s.Equals("label", StringComparison.OrdinalIgnoreCase) || s.Equals("dotandlabel", StringComparison.OrdinalIgnoreCase) || s.Equals("2") || s.Equals("x") || s.Equals("true", StringComparison.OrdinalIgnoreCase))
                return TriStateLabelMode.DotAndLabel;

            // Unknown -> Legacy default.
            return legacyDefaultOn ? TriStateLabelMode.DotOnly : TriStateLabelMode.Off;
        }

        /// <summary>
        /// Parses a color from either:
        /// • Hex: #RRGGBB or #RRGGBBAA
        /// • CSV: r,g,b or r,g,b,a
        /// </summary>
        public static Color ParseColor(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return Color.White;

            s = s.Trim();

            // Hex: #RRGGBB or #RRGGBBAA.
            if (s[0] == '#')
            {
                string hex = s.Substring(1);
                if (hex.Length == 6)
                    hex += "ff";

                if (hex.Length == 8)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    byte a = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                    return new Color(r, g, b, a);
                }
            }

            // CSV: r,g,b or r,g,b,a.
            var parts = s.Split(',');
            if (parts.Length >= 3)
            {
                byte r = (byte)Clamp(ParseInt(parts[0], 255), 0, 255);
                byte g = (byte)Clamp(ParseInt(parts[1], 255), 0, 255);
                byte b = (byte)Clamp(ParseInt(parts[2], 255), 0, 255);
                byte a = (byte)Clamp(parts.Length >= 4 ? ParseInt(parts[3], 255) : 255, 0, 255);
                return new Color(r, g, b, a);
            }

            return Color.White;
        }

        private static int ParseInt(string s, int fallback)
        {
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            return fallback;
        }

        private static Color[] ParseColorList(string s, Color[] fallback)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;

            // Prefer list separators that won't collide with CSV colors:
            //   RingColors = #ff0000aa;#00ff00aa;#0000ffaa.
            // (commas are allowed inside a single color for "r,g,b,a").
            char[] seps = new[] { ';', '|' };
            string[] items = s.IndexOf(';') >= 0 || s.IndexOf('|') >= 0
                ? s.Split(seps, StringSplitOptions.RemoveEmptyEntries)
                : s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var list = new List<Color>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i].Trim();
                if (it.Length == 0) continue;

                // If we split by commas (no ';' or '|'), hex items are still fine.
                // If the user uses CSV colors AND wants a list, they should use ';' between colors.
                list.Add(ParseColor(it));
            }

            return list.Count > 0 ? list.ToArray() : fallback;
        }
        #endregion
    }
    #endregion

    #region Back-Compat Alias (LLMConfig)

    /// <summary>
    /// Back-compat alias: some of your files use "LLMConfig" instead of "LanternLandMapConfig".
    /// Keep both names working so you can swap implementations without doing a massive search/replace.
    /// </summary>
    internal static class LLMConfig
    {
        #region Paths / Status

        public static string ModFolder  => LanternLandMapConfig.ModFolder;
        public static string ConfigPath => LanternLandMapConfig.ConfigPath;
        public static bool   IsLoaded   => LanternLandMapConfig.IsLoaded;

        #endregion

        #region Load / Save

        public static void LoadApply()     => LanternLandMapConfig.LoadApply();
        public static void Save()          => LanternLandMapConfig.Save();
        public static void SaveFromState() => LanternLandMapConfig.SaveFromState();

        #endregion

        #region Hotkeys

        public static HotkeyBinding ToggleMapKey         { get => LanternLandMapConfig.ToggleMapKey;         set => LanternLandMapConfig.ToggleMapKey = value;         }
        public static HotkeyBinding ReloadConfigKey      { get => LanternLandMapConfig.ReloadConfigKey;      set => LanternLandMapConfig.ReloadConfigKey = value;      }
        public static HotkeyBinding ToggleChunkGridKey   { get => LanternLandMapConfig.ToggleChunkGridKey;   set => LanternLandMapConfig.ToggleChunkGridKey = value;   }
        public static HotkeyBinding ResetViewToPlayerKey { get => LanternLandMapConfig.ResetViewToPlayerKey; set => LanternLandMapConfig.ResetViewToPlayerKey = value; }
        public static HotkeyBinding ResetViewToOriginKey { get => LanternLandMapConfig.ResetViewToOriginKey; set => LanternLandMapConfig.ResetViewToOriginKey = value; }

        public static HotkeyBinding ToggleStartLabelsKey { get => LanternLandMapConfig.ToggleStartLabelsKey; set => LanternLandMapConfig.ToggleStartLabelsKey = value; }
        public static HotkeyBinding ToggleEndLabelsKey   { get => LanternLandMapConfig.ToggleEndLabelsKey;   set => LanternLandMapConfig.ToggleEndLabelsKey = value;   }
        public static HotkeyBinding ToggleThicknessKey   { get => LanternLandMapConfig.ToggleThicknessKey;   set => LanternLandMapConfig.ToggleThicknessKey = value;   }
        public static HotkeyBinding ToggleGapKey         { get => LanternLandMapConfig.ToggleGapKey;         set => LanternLandMapConfig.ToggleGapKey = value;         }
        public static HotkeyBinding ToggleIndexKey       { get => LanternLandMapConfig.ToggleIndexKey;       set => LanternLandMapConfig.ToggleIndexKey = value;       }
        public static HotkeyBinding ToggleTowersKey      { get => LanternLandMapConfig.ToggleTowersKey;      set => LanternLandMapConfig.ToggleTowersKey = value;      }

        #endregion

        #region Sliders

        public static int    RingsToShow    { get => LanternLandMapConfig.RingsToShow;    set => LanternLandMapConfig.RingsToShow    = value; }
        public static int    RingsMin       { get => LanternLandMapConfig.RingsMin;       set => LanternLandMapConfig.RingsMin       = value; }
        public static int    RingsMax       { get => LanternLandMapConfig.RingsMax;       set => LanternLandMapConfig.RingsMax       = value; }

        public static double TargetRadius   { get => LanternLandMapConfig.TargetRadius;   set => LanternLandMapConfig.TargetRadius   = value; }
        public static double RadiusMin      { get => LanternLandMapConfig.RadiusMin;      set => LanternLandMapConfig.RadiusMin      = value; }
        public static double RadiusMax      { get => LanternLandMapConfig.RadiusMax;      set => LanternLandMapConfig.RadiusMax      = value; }
        public static bool   RadiusLogScale { get => LanternLandMapConfig.RadiusLogScale; set => LanternLandMapConfig.RadiusLogScale = value; }
        public static double RadiusWheelMul { get => LanternLandMapConfig.RadiusWheelMul; set => LanternLandMapConfig.RadiusWheelMul = value; }

        #endregion

        #region View

        public static float InitialZoomPixelsPerBlock { get => LanternLandMapConfig.InitialZoomPixelsPerBlock; set => LanternLandMapConfig.InitialZoomPixelsPerBlock = value; }
        public static float ZoomMinPixelsPerBlock     { get => LanternLandMapConfig.ZoomMinPixelsPerBlock;     set => LanternLandMapConfig.ZoomMinPixelsPerBlock     = value; }
        public static float ZoomMaxPixelsPerBlock     { get => LanternLandMapConfig.ZoomMaxPixelsPerBlock;     set => LanternLandMapConfig.ZoomMaxPixelsPerBlock     = value; }
        public static float PanSpeed                  { get => LanternLandMapConfig.PanSpeed;                  set => LanternLandMapConfig.PanSpeed                  = value; }
        public static bool  ZoomAboutCursor           { get => LanternLandMapConfig.ZoomAboutCursor;           set => LanternLandMapConfig.ZoomAboutCursor           = value; }

        #endregion

        #region Drawing

        public static int   ChunkGridStepBlocks { get => LanternLandMapConfig.ChunkGridStepBlocks; set => LanternLandMapConfig.ChunkGridStepBlocks = value; }
        public static int   MinGridPixels       { get => LanternLandMapConfig.MinGridPixels;       set => LanternLandMapConfig.MinGridPixels       = value; }
        public static int   CircleSegmentsMin   { get => LanternLandMapConfig.CircleSegmentsMin;   set => LanternLandMapConfig.CircleSegmentsMin   = value; }
        public static int   CircleSegmentsMax   { get => LanternLandMapConfig.CircleSegmentsMax;   set => LanternLandMapConfig.CircleSegmentsMax   = value; }
        public static float RingLineThickness   { get => LanternLandMapConfig.RingLineThickness;   set => LanternLandMapConfig.RingLineThickness   = value; }
        public static float AxisThickness       { get => LanternLandMapConfig.AxisThickness;       set => LanternLandMapConfig.AxisThickness       = value; }
        public static float GridThickness       { get => LanternLandMapConfig.GridThickness;       set => LanternLandMapConfig.GridThickness       = value; }

        #endregion

        #region Labels

        public static float LabelFontScale          { get => LanternLandMapConfig.LabelFontScale;          set => LanternLandMapConfig.LabelFontScale          = value; }
        public static int   StartLabelYOffsetPx     { get => LanternLandMapConfig.StartLabelYOffsetPx;     set => LanternLandMapConfig.StartLabelYOffsetPx     = value; }
        public static int   EndLabelYOffsetPx       { get => LanternLandMapConfig.EndLabelYOffsetPx;       set => LanternLandMapConfig.EndLabelYOffsetPx       = value; }
        public static int   IndexLabelYOffsetPx     { get => LanternLandMapConfig.IndexLabelYOffsetPx;     set => LanternLandMapConfig.IndexLabelYOffsetPx     = value; }
        public static int   ThicknessLabelYOffsetPx { get => LanternLandMapConfig.ThicknessLabelYOffsetPx; set => LanternLandMapConfig.ThicknessLabelYOffsetPx = value; }
        public static int   GapLabelYOffsetPx       { get => LanternLandMapConfig.GapLabelYOffsetPx;       set => LanternLandMapConfig.GapLabelYOffsetPx       = value; }
        public static int   TowerLabelYOffsetPx     { get => LanternLandMapConfig.TowerLabelYOffsetPx;     set => LanternLandMapConfig.TowerLabelYOffsetPx     = value; }

        #endregion

        #region Colors

        public static Color   BackgroundColor { get => LanternLandMapConfig.BackgroundColor; set => LanternLandMapConfig.BackgroundColor = value; }
        public static Color   PanelColor      { get => LanternLandMapConfig.PanelColor;      set => LanternLandMapConfig.PanelColor      = value; }
        public static Color   GridColor       { get => LanternLandMapConfig.GridColor;       set => LanternLandMapConfig.GridColor       = value; }
        public static Color   AxisColor       { get => LanternLandMapConfig.AxisColor;       set => LanternLandMapConfig.AxisColor       = value; }
        public static Color   PlayerColor     { get => LanternLandMapConfig.PlayerColor;     set => LanternLandMapConfig.PlayerColor     = value; }
        public static Color   TextColor       { get => LanternLandMapConfig.TextColor;       set => LanternLandMapConfig.TextColor       = value; }
        public static Color   TowerColor      { get => LanternLandMapConfig.TowerColor;      set => LanternLandMapConfig.TowerColor      = value; }
        public static Color[] RingColors      { get => LanternLandMapConfig.RingColors;      set => LanternLandMapConfig.RingColors      = value; }

        #endregion

        #region Teleport

        public static bool  RightClickTeleport   { get => LanternLandMapConfig.RightClickTeleport;   set => LanternLandMapConfig.RightClickTeleport = value;   }
        public static bool  TeleportRequireShift { get => LanternLandMapConfig.TeleportRequireShift; set => LanternLandMapConfig.TeleportRequireShift = value; }
        public static float TeleportY            { get => LanternLandMapConfig.TeleportY;            set => LanternLandMapConfig.TeleportY = value;            }

        #endregion

        #region Defaults (Visibility)

        public static bool DefaultShowChunkGrid   { get => LanternLandMapConfig.DefaultShowChunkGrid;   set => LanternLandMapConfig.DefaultShowChunkGrid    = value; }
        public static bool DefaultShowStartLabels { get => LanternLandMapConfig.DefaultShowStartLabels; set => LanternLandMapConfig.DefaultShowStartLabels  = value; }
        public static bool DefaultShowEndLabels   { get => LanternLandMapConfig.DefaultShowEndLabels;   set => LanternLandMapConfig.DefaultShowEndLabels    = value; }
        public static bool DefaultShowThickness   { get => LanternLandMapConfig.DefaultShowThickness;   set => LanternLandMapConfig.DefaultShowThickness    = value; }
        public static bool DefaultShowGap         { get => LanternLandMapConfig.DefaultShowGap;         set => LanternLandMapConfig.DefaultShowGap          = value; }
        public static bool DefaultShowIndex       { get => LanternLandMapConfig.DefaultShowIndex;       set => LanternLandMapConfig.DefaultShowIndex        = value; }
        public static bool DefaultShowTowers      { get => LanternLandMapConfig.DefaultShowTowers;      set => LanternLandMapConfig.DefaultShowTowers       = value; }

        #endregion
    }
    #endregion

    #region Runtime State

    /// <summary>
    /// Runtime state for the map (persists while the game is running, even if you close/reopen the map screen).
    ///
    /// Important:
    /// • Config = what gets persisted to disk.
    /// • State  = what the player is currently doing right now (toggles/sliders while map is open).
    /// </summary>
    internal static class LanternLandMapState
    {
        public static bool   Initialized;
        public static bool   IsOpen;

        public static int    RingsToShow;
        public static double TargetRadius;

        public static bool   ShowChunkGrid;
        public static bool   ShowOtherPlayers;
        public static bool   ShowLanternRings;
        public static bool   ShowBiomesUnderlay;
        public static bool   ShowBiomeEdges;
        public static bool   ShowStartLabels;
        public static bool   ShowEndLabels;
        public static bool   ShowThickness;
        public static bool   ShowGap;
        public static bool   ShowIndex;
        public static bool   ShowTowers;

        /// <summary>
        /// Initializes runtime values from config once per game session.
        /// Call this before using state fields (typically from your HUD patch).
        /// </summary>
        public static void EnsureInitFromConfig()
        {
            if (Initialized) return;

            RingsToShow        = LanternLandMapConfig.RingsToShow;
            TargetRadius       = LanternLandMapConfig.TargetRadius;

            ShowBiomesUnderlay = LanternLandMapConfig.DefaultShowBiomesUnderlay;
            ShowBiomeEdges     = LanternLandMapConfig.DefaultShowBiomeEdges;

            ShowChunkGrid      = LanternLandMapConfig.DefaultShowChunkGrid;
            ShowOtherPlayers   = LanternLandMapConfig.DefaultShowOtherPlayers;
            ShowLanternRings   = LanternLandMapConfig.DefaultShowLanternRings;
            ShowStartLabels    = LanternLandMapConfig.DefaultShowStartLabels;
            ShowEndLabels      = LanternLandMapConfig.DefaultShowEndLabels;
            ShowThickness      = LanternLandMapConfig.DefaultShowThickness;
            ShowGap            = LanternLandMapConfig.DefaultShowGap;
            ShowIndex          = LanternLandMapConfig.DefaultShowIndex;
            ShowTowers         = LanternLandMapConfig.DefaultShowTowers;

            Initialized = true;
        }
    }
    #endregion

    #region Hotkey Binding

    /// <summary>
    /// Hotkey binding helper (Ctrl/Shift/Alt + key).
    /// Used by LanternLandMapHotkeys for edge detection outside the Screen input pipeline.
    /// </summary>
    internal struct HotkeyBinding
    {
        public Keys Key;
        public bool Ctrl;
        public bool Shift;
        public bool Alt;

        public bool IsEmpty => Key == Keys.None;

        public override string ToString()
        {
            string     s = "";
            if (Ctrl)  s += "Ctrl+";
            if (Shift) s += "Shift+";
            if (Alt)   s += "Alt+";
            s += Key.ToString();
            return s;
        }

        /// <summary>
        /// Parses "Ctrl+Shift+R", "Alt F10", "M", etc.
        /// Unknown tokens are ignored.
        /// </summary>
        public static HotkeyBinding Parse(string text)
        {
            var hk = new HotkeyBinding { Key = Keys.None };

            if (string.IsNullOrWhiteSpace(text))
                return hk;

            var parts = text.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();

                if (p.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || p.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
                    hk.Ctrl = true;
                else if (p.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
                    hk.Shift = true;
                else if (p.Equals("ALT", StringComparison.OrdinalIgnoreCase))
                    hk.Alt = true;
                else
                {
                    if (Enum.TryParse(p, true, out Keys key))
                        hk.Key = key;
                }
            }

            return hk;
        }

        /// <summary>
        /// Returns true if the binding is currently held down (including required modifiers).
        /// NOTE: This is "level" state; use LanternLandMapHotkeys.PressedThisFrame for edge detection.
        /// </summary>
        public bool IsDown(KeyboardState ks)
        {
            if (Key == Keys.None) return false;

            bool keyDown = ks.IsKeyDown(Key);
            if (!keyDown) return false;

            if (Ctrl && !(ks.IsKeyDown(Keys.LeftControl) || ks.IsKeyDown(Keys.RightControl))) return false;
            if (Shift && !(ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift))) return false;
            if (Alt && !(ks.IsKeyDown(Keys.LeftAlt) || ks.IsKeyDown(Keys.RightAlt))) return false;

            // If modifier is NOT required, allow it either way (more forgiving).
            return true;
        }
    }
    #endregion

    #region SimpleIni

    /// <summary>
    /// Tiny, case-insensitive INI reader.
    /// Supports [Section], key=value, ';' or '#' comments. No escaping, no multi-line.
    /// </summary>
    internal sealed class SimpleIni
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads an INI file from disk into a simple nested dictionary:
        ///   section -> (key -> value).
        /// Unknown / malformed lines are ignored.
        /// </summary>
        public static SimpleIni Load(string path)
        {
            var ini = new SimpleIni();
            string section = "";

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                // Section header: [SectionName].
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!ini._data.ContainsKey(section))
                        ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                // Key/value pair: key = value.
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (!ini._data.TryGetValue(section, out var dict))
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ini._data[section] = dict;
                }
                dict[key] = val;
            }

            return ini;
        }

        /// <summary>
        /// Reads a string value from [section] key=... and returns <paramref name="def"/> if missing.
        /// </summary>
        public string GetString(string section, string key, string def)
            => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

        /// <summary>
        /// Reads an int value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public int GetInt(string section, string key, int def)
            => int.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a float value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public float GetFloat(string section, string key, float def)
            => float.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public double GetDouble(string section, string key, double def)
            => double.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a bool value from [section] key=... accepting:
        ///   "true/false" (case-insensitive) or "1/0".
        /// Returns <paramref name="def"/> on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i != 0;
            return def;
        }
    }
    #endregion

    #region Hotkey Edge Detection

    /// <summary>
    /// Hotkey edge detection used by patches (works outside Screen input pipeline).
    /// Call Tick() once per HUD input tick, query PressedThisFrame(...), then call EndTick().
    /// </summary>
    internal static class LanternLandMapHotkeys
    {
        private static KeyboardState _prev;
        private static KeyboardState _now;
        private static bool          _ticked;

        /// <summary>
        /// Call once per input tick (our HUD patch does this).
        /// </summary>
        public static void Tick()
        {
            _now    = Keyboard.GetState();
            _ticked = true;
        }

        /// <summary>
        /// Returns true only on the frame the binding becomes down.
        /// IMPORTANT: Requires Tick() to be called once beforehand.
        /// </summary>
        public static bool PressedThisFrame(HotkeyBinding hk)
        {
            if (hk.IsEmpty) return false;

            if (!_ticked)
                _now = Keyboard.GetState();

            bool downNow  = hk.IsDown(_now);
            bool downPrev = hk.IsDown(_prev);

            return downNow && !downPrev;
        }

        /// <summary>
        /// Call after you've checked all bindings for this tick.
        /// </summary>
        public static void EndTick()
        {
            if (!_ticked) return;
            _prev   = _now;
            _ticked = false;
        }
    }
    #endregion
}