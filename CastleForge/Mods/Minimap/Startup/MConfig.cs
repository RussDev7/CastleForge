/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Globalization;
using System.IO;
using System;

namespace Minimap
{
    #region Runtime State

    /// <summary>
    /// Runtime state for the minimap (persists while the game is running).
    /// Summary:
    /// - Holds live toggles and zoom that can differ from defaults in <see cref="MinimapConfig"/>.
    /// - Initialized once per session via <see cref="EnsureInitFromConfig"/>.
    /// </summary>
    internal static class MinimapState
    {
        public static bool  Initialized;

        public static bool  Visible;
        public static bool  ShowChunkGrid;

        public static float ZoomPixelsPerBlock;

        /// <summary>
        /// One-time initialization from <see cref="MinimapConfig"/> defaults.
        /// Summary: Seeds runtime state and clamps zoom to configured limits.
        /// </summary>
        public static void EnsureInitFromConfig()
        {
            if (Initialized) return;

            Visible            = MinimapConfig.EnabledByDefault;
            ShowChunkGrid      = MinimapConfig.ToggleChunkGrid;
            ZoomPixelsPerBlock = MinimapConfig.InitialZoom;

            if (ZoomPixelsPerBlock < MinimapConfig.ZoomMin) ZoomPixelsPerBlock = MinimapConfig.ZoomMin;
            if (ZoomPixelsPerBlock > MinimapConfig.ZoomMax) ZoomPixelsPerBlock = MinimapConfig.ZoomMax;

            Initialized = true;
        }
    }
    #endregion

    #region Enums (Shape / Anchor)

    /// <summary>
    /// Minimap shape modes.
    /// Summary: Controls whether the map renders as a square or a clipped circle.
    /// </summary>
    internal enum MinimapShape : byte
    {
        Square = 0,
        Circle = 1,
    }

    /// <summary>
    /// Screen anchor locations for placing the minimap.
    /// Summary: Used with MarginPx to position the map in a corner.
    /// </summary>
    internal enum MinimapAnchor : byte
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3,
    }
    #endregion

    /// <summary>
    /// Biome Minimap - lightweight HUD overlay config.
    ///
    /// Summary:
    /// - Loads values from a SimpleIni file and applies them to static config fields.
    /// - Defines all hotkeys, visual settings, and performance knobs for the minimap overlay.
    /// - Includes small persistence helpers to write specific runtime-toggled values back to disk.
    ///
    /// Notes:
    /// - This class uses the same minimal "SimpleIni" and "HotkeyBinding" patterns as your other mods.
    /// - IO operations are best-effort and must never crash the HUD pipeline.
    /// </summary>
    internal static class MinimapConfig
    {
        #region Paths / Status

        /// <summary>Folder for this mod under the game directory.</summary>
        public static readonly string ModFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "Minimap");

        /// <summary>Primary config file path.</summary>
        public static readonly string ConfigPath =
            Path.Combine(ModFolder, "Minimap.Config.ini");

        /// <summary>True once <see cref="LoadApply"/> has successfully parsed the config.</summary>
        public static bool IsLoaded { get; private set; }

        #endregion

        #region Settings (Loaded From INI)

        #region Hotkeys

        /// <summary>
        /// Toggle minimap overlay visibility.
        /// Summary: Bound to [Hotkeys] ToggleMap.
        /// </summary>
        public static HotkeyBinding ToggleMapKey       = HotkeyBinding.Parse("N");

        /// <summary>
        /// Reload config from disk.
        /// Summary: Bound to [Hotkeys] ReloadConfig.
        /// </summary>
        public static HotkeyBinding ReloadConfigKey    = HotkeyBinding.Parse("Ctrl+Shift+R");

        /// <summary>
        /// Toggle chunk grid overlay.
        /// Summary: Bound to [Hotkeys] ToggleChunkGrid.
        /// </summary>
        public static HotkeyBinding ToggleChunkGridKey = HotkeyBinding.Parse("C");

        /// <summary>
        /// Cycle minimap screen corner (TopLeft -> TopRight -> BottomLeft -> BottomRight).
        /// Summary: Bound to [Hotkeys] CycleLocation.
        /// </summary>
        public static HotkeyBinding CycleLocationKey   = HotkeyBinding.Parse("OemPipe");

        /// <summary>Zoom in hotkey (increases pixels-per-block).</summary>
        public static HotkeyBinding ZoomInHotkey       = HotkeyBinding.Parse("OemPlus");

        /// <summary>Zoom out hotkey (decreases pixels-per-block).</summary>
        public static HotkeyBinding ZoomOutHotkey      = HotkeyBinding.Parse("OemMinus");

        /// <summary>
        /// Toggle the local-player facing indicator on/off.
        /// Summary: Bound to [Hotkeys] ToggleIndicator.
        /// </summary>
        public static HotkeyBinding ToggleIndicatorKey = HotkeyBinding.Parse("I");

        /// <summary>
        /// Toggle enemies + dragon markers together.
        /// Summary: Bound to [Hotkeys] ToggleMobs.
        /// </summary>
        public static HotkeyBinding ToggleMobsKey = HotkeyBinding.Parse("B");

        #endregion

        #region Other Players

        /// <summary>Enables drawing remote player markers.</summary>
        public static bool ShowOtherPlayers = true;

        /// <summary>
        /// Whether to use stable random per-player colors.
        /// Summary:
        /// - When true, renderer assigns each remote player a stable random color.
        /// - When false, renderer uses <see cref="OtherPlayerColor"/> for all remote players.
        /// </summary>
        public static bool OtherPlayerColorRandom = true;

        /// <summary>
        /// Fixed remote player color when <see cref="OtherPlayerColorRandom"/> is false.
        /// </summary>
        public static Color OtherPlayerColor = new Color(0, 240, 255, 255); // #00F0FFFF.

        /// <summary>Dot size for remote players (optional).</summary>
        public static int OtherPlayerDotSizePx = 5;

        #endregion

        #region Mobs (Enemies + Dragon)

        /// <summary>Enables drawing enemy markers on the minimap.</summary>
        public static bool ShowEnemies = true;

        /// <summary>Enemy marker color.</summary>
        public static Color EnemyColor = new Color(255, 0, 0, 255); // red

        /// <summary>Enemy marker size in pixels.</summary>
        public static int EnemyDotSizePx = 4;

        /// <summary>Enables drawing the dragon marker on the minimap.</summary>
        public static bool ShowDragon = true;

        /// <summary>Dragon marker color.</summary>
        public static Color DragonColor = new Color(0, 255, 0, 255); // green

        /// <summary>Dragon marker size in pixels.</summary>
        public static int DragonDotSizePx = 7;

        #endregion

        #region Player Facing Indicator

        /// <summary>
        /// Enables a small facing indicator on the local player marker.
        /// Summary: When true, DrawPlayerDot renders a tiny triangle arrow showing the player's yaw.
        /// </summary>
        public static bool PlayerFacingIndicator = true;

        /// <summary>
        /// Facing indicator style.
        /// Summary:
        /// - When true, draws a triangle "arrowhead".
        /// - When false, you can fall back to a simple line/tick (optional).
        /// </summary>
        public static bool PlayerFacingUseTriangle = true;

        /// <summary>
        /// Length of the facing indicator in pixels.
        /// Summary: Controls how far the tip extends from the player center.
        /// </summary>
        public static int PlayerFacingLengthPx = 10;

        /// <summary>
        /// Width of the triangle base in pixels (only used when <see cref="PlayerFacingUseTriangle"/> is true).
        /// Summary: Larger = wider arrowhead.
        /// </summary>
        public static int PlayerFacingTriangleBaseWidthPx = 8;

        /// <summary>
        /// Thickness of the facing indicator outline in pixels.
        /// Summary:
        /// - Triangle: outline thickness.
        /// - Line: line thickness.
        /// </summary>
        public static int PlayerFacingThicknessPx = 1;

        /// <summary>
        /// Whether to fill the triangle arrow.
        /// Summary: If true, DrawPlayerDot will approximate a filled triangle via line fan.
        /// </summary>
        public static bool PlayerFacingTriangleFilled = true;

        /// <summary>
        /// Fill density for the triangle (lower = more solid, higher = cheaper).
        /// Summary: 1 looks solid; 2-3 is usually fine at minimap scale.
        /// </summary>
        public static int PlayerFacingTriangleFillStepPx = 1;

        /// <summary>
        /// Additional pixel offset applied to where the facing indicator starts relative to the player square edge.
        /// Summary:
        /// - startOffset = (playerDotSize/2) + PlayerFacingStartOffsetPx
        /// - Smaller values move the indicator closer; negative values overlap the square.
        /// </summary>
        public static float PlayerFacingStartOffsetPx = 0f;

        /// <summary>
        /// Color of the facing indicator.
        /// Summary: Used for the triangle/line fill (and line mode).
        /// </summary>
        public static Color PlayerFacingColor = new Color(191, 255, 0, 220);

        /// <summary>
        /// Outline color for the facing indicator (triangle only).
        /// Summary: Drawn on top so the arrow stays readable against bright biomes.
        /// </summary>
        public static Color PlayerFacingOutlineColor = new Color(0, 0, 0, 200);

        #endregion

        #region Compass (N/E/S/W)

        /// <summary>Enables compass label drawing around the minimap border.</summary>
        public static bool ShowCompass = true;

        /// <summary>Rotates the compass with player yaw (player-facing direction becomes "up").</summary>
        public static bool CompassRotatesWithPlayer = true;

        /// <summary>Inset padding from the border for compass letters.</summary>
        public static int CompassPaddingPx = 6;

        /// <summary>Scale multiplier for compass letter rendering.</summary>
        public static float CompassFontScale = 0.90f;

        /// <summary>Main compass letter color.</summary>
        public static Color CompassColor = new Color(255, 255, 255, 220);

        /// <summary>Compass letter outline color.</summary>
        public static Color CompassOutlineColor = new Color(0, 0, 0, 200);

        /// <summary>Compass outline thickness (in pixels).</summary>
        public static int CompassOutlineThicknessPx = 2;

        #endregion

        #region Layout / Size

        /// <summary>Which corner to anchor the minimap to.</summary>
        public static MinimapAnchor MinimapLocation = MinimapAnchor.TopLeft;

        /// <summary>Square size in pixels. (Circle uses this as diameter.)</summary>
        public static int MinimapScale = 180;

        /// <summary>Padding from the chosen screen corner.</summary>
        public static int MarginPx = 12;

        /// <summary>Vertical gap between minimap and the centered text rows below.</summary>
        public static int TextSpacingPx = 4;

        #endregion

        #region Visibility Toggles

        /// <summary>Default minimap enabled state (also persisted by ToggleMap).</summary>
        public static bool EnabledByDefault = true;

        /// <summary>Draw player dot at center.</summary>
        public static bool Player = true;

        /// <summary>Default chunk grid toggle state (also persisted by ToggleChunkGrid).</summary>
        public static bool ToggleChunkGrid = false;

        /// <summary>Draw biome edges (dominant token boundary overlay).</summary>
        public static bool ShowBiomeEdges  = false;

        /// <summary>Show coordinate text under minimap.</summary>
        public static bool MinimapCoordinates = true;

        /// <summary>Show current biome text under minimap.</summary>
        public static bool ShowCurrentBiome = true;

        #endregion

        #region Zoom

        /// <summary>Initial pixels-per-block zoom (persisted by zoom hotkeys).</summary>
        public static float InitialZoom = 0.25f; // pixels per block

        /// <summary>Minimum allowed zoom (pixels-per-block).</summary>
        public static float ZoomMin     = 0.05f;

        /// <summary>Maximum allowed zoom (pixels-per-block).</summary>
        public static float ZoomMax     = 2.00f;

        /// <summary>Per key press zoom multiplier (ZoomIn/ZoomOut hotkeys).</summary>
        public static float ZoomStepMul = 1.12f;

        #endregion

        #region Shape / Fill

        /// <summary>Minimap render shape.</summary>
        public static MinimapShape MinimapShape = MinimapShape.Circle;

        /// <summary>Background fill alpha under the biomes.</summary>
        public static byte MinimapFillAlpha = 90;

        /// <summary>Biome cell alpha (passed into biome palette mapping).</summary>
        public static byte BiomeFillAlpha = 110;

        #endregion

        #region Text / Outline

        /// <summary>Scale multiplier for the coordinate/biome readout text.</summary>
        public static float FontScale    = 1.00f;

        /// <summary>Minimum allowed font scale.</summary>
        public static float FontScaleMin = 0.35f;

        /// <summary>Maximum allowed font scale.</summary>
        public static float FontScaleMax = 2.50f;

        /// <summary>Enables text outline drawing (used by centered text).</summary>
        public static bool OutlineEnabled     = true;

        /// <summary>Outline thickness in pixels (used by centered text).</summary>
        public static int  OutlineThicknessPx = 2;

        #endregion

        #region Perf

        /// <summary>
        /// Minimap samples are cached and only recomputed at this interval.
        /// Lower = smoother updates, higher = less CPU.
        /// </summary>
        public static int UpdateIntervalMs = 150;

        /// <summary>
        /// Sampling density control: bigger = fewer cells (faster).
        /// Summary: Pixel step used to compute the sample grid resolution.
        /// </summary>
        public static int SampleStepPx = 6;

        #endregion

        #region Colors / Palette

        public static Color FrameColor        = new Color(255, 255, 255, 140);
        public static Color CoordinatesColor  = new Color(255, 255, 255, 255);
        public static Color OutlineColor      = new Color(0, 0, 0, 200);

        public static Color PlayerColor       = new Color(255, 80, 80, 255);

        public static Color GridColor         = new Color(255, 255, 255, 40);
        public static int   GridStepBlocks    = 16;
        public static int   MinGridPixels     = 24;

        public static Color BiomeEdgeColor    = new Color(0, 0, 0, 140);

        // Palette (these feed WorldGenPlusBiomePalette via BiomeMinimapConfig.* colors).
        public static Color ClassicBiomeColor  = new Color(70, 160, 70);
        public static Color LagoonBiomeColor   = new Color(60, 170, 170);
        public static Color DesertBiomeColor   = new Color(200, 180, 60);
        public static Color MountainBiomeColor = new Color(150, 150, 150);
        public static Color ArcticBiomeColor   = new Color(140, 190, 255);
        public static Color DecentBiomeColor   = new Color(190, 120, 220);
        public static Color HellBiomeColor     = new Color(190, 70, 70);

        #endregion

        #endregion

        #region Persist Helpers (Write Back To INI)

        /// <summary>
        /// Saves the current zoom back into the config so it persists across restarts/reloads.
        /// Uses the existing "InitialZoom" key in [Minimap].
        /// </summary>
        public static void PersistZoom(float zoom)
        {
            // Clamp and store in config field.
            zoom = Clamp(zoom, ZoomMin, ZoomMax);
            InitialZoom = zoom;

            // Write to disk.
            TryUpsertIniValue(ConfigPath, "Minimap", "InitialZoom",
                zoom.ToString("0.#####", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Updates or inserts a key=value line within [section] in-place (preserves other lines/comments).
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - Preserves comments and unknown keys by editing the file in-place.
        /// - Best-effort IO; any exception is swallowed so the HUD never crashes.
        /// </remarks>
        public static void TryUpsertIniValue(string path, string section, string key, string value)
        {
            try
            {
                Directory.CreateDirectory(ModFolder);

                // If file missing, create defaults first.
                if (!File.Exists(path))
                    WriteDefaults(path);

                var lines = new List<string>(File.ReadAllLines(path));

                int sectionStart = -1;
                int sectionEnd = lines.Count;

                // Find section header
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.Length == 0) continue;

                    if (t.StartsWith("[") && t.EndsWith("]"))
                    {
                        string name = t.Substring(1, t.Length - 2).Trim();
                        if (sectionStart < 0)
                        {
                            if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                                sectionStart = i;
                        }
                        else
                        {
                            // first section after the target section
                            sectionEnd = i;
                            break;
                        }
                    }
                }

                // If section doesn't exist, append it.
                if (sectionStart < 0)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length != 0)
                        lines.Add("");

                    lines.Add("[" + section + "]");
                    lines.Add(key + " = " + value);

                    File.WriteAllLines(path, lines);
                    return;
                }

                // Search key within section.
                int keyLine = -1;
                for (int i = sectionStart + 1; i < sectionEnd; i++)
                {
                    string raw = lines[i];
                    string t = raw.Trim();

                    if (t.Length == 0) continue;
                    if (t.StartsWith(";") || t.StartsWith("#")) continue;

                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;

                    string k = t.Substring(0, eq).Trim();
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        keyLine = i;
                        break;
                    }
                }

                string newLine = key + " = " + value;

                if (keyLine >= 0)
                {
                    lines[keyLine] = newLine;
                }
                else
                {
                    // Insert before next section header (or end of file).
                    lines.Insert(sectionEnd, newLine);
                }

                File.WriteAllLines(path, lines);
            }
            catch
            {
                // best-effort; don't crash the HUD for IO issues
            }
        }

        /// <summary>
        /// Persists the minimap enabled/visible default to disk.
        /// Summary: Updates <see cref="EnabledByDefault"/> and writes [Minimap] Enabled=true/false to the INI.
        /// </summary>
        public static void PersistEnabled(bool enabled)
        {
            EnabledByDefault = enabled;

            TryUpsertIniValue(
                ConfigPath,
                "Minimap",
                "Enabled",
                enabled ? "true" : "false");
        }

        /// <summary>
        /// Persists the chunk-grid toggle to disk.
        /// Summary: Updates <see cref="ToggleChunkGrid"/> and writes [Minimap] ToggleChunkGrid=true/false to the INI.
        /// </summary>
        public static void PersistChunkGrid(bool showGrid)
        {
            ToggleChunkGrid = showGrid;

            TryUpsertIniValue(
                ConfigPath,
                "Minimap",
                "ToggleChunkGrid",
                showGrid ? "true" : "false");
        }

        /// <summary>
        /// Persists the minimap anchor/corner to disk.
        /// Summary: Updates <see cref="MinimapLocation"/> and writes [Minimap] MinimapLocation=... to the INI.
        /// </summary>
        public static void PersistLocation(MinimapAnchor loc)
        {
            MinimapLocation = loc;

            TryUpsertIniValue(
                ConfigPath,
                "Minimap",
                "MinimapLocation",
                loc.ToString());
        }

        /// <summary>
        /// Persists the facing-indicator toggle to disk.
        /// Summary: Updates <see cref="PlayerFacingIndicator"/> and writes [Minimap] PlayerFacingIndicator=true/false.
        /// </summary>
        public static void PersistFacingIndicator(bool enabled)
        {
            PlayerFacingIndicator = enabled;

            TryUpsertIniValue(
                ConfigPath,
                "Minimap",
                "PlayerFacingIndicator",
                enabled ? "true" : "false");
        }

        /// <summary>
        /// Persist enemies toggle to the INI.
        /// Summary: Updates runtime flag + writes [Mobs] ShowEnemies.
        /// </summary>
        public static void PersistShowEnemies(bool enabled)
        {
            ShowEnemies = enabled;
            TryUpsertIniValue(ConfigPath, "Mobs", "ShowEnemies", enabled ? "true" : "false");
        }

        /// <summary>
        /// Persist dragon toggle to the INI.
        /// Summary: Updates runtime flag + writes [Mobs] ShowDragon.
        /// </summary>
        public static void PersistShowDragon(bool enabled)
        {
            ShowDragon = enabled;
            TryUpsertIniValue(ConfigPath, "Mobs", "ShowDragon", enabled ? "true" : "false");
        }
        #endregion

        #region Load / Save

        /// <summary>
        /// Loads config from disk and applies values to the static fields.
        /// Summary:
        /// - Creates the mod folder and default INI on first run.
        /// - Reads Hotkeys, Minimap, Players, Compass, Text, Outline, Perf, Colors, Palette sections.
        /// - Clamps values to safe ranges.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                Directory.CreateDirectory(ModFolder);

                if (!File.Exists(ConfigPath))
                    WriteDefaults(ConfigPath);

                var ini = SimpleIni.Load(ConfigPath);

                // Hotkeys.
                ToggleMapKey       = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleMap", "N"));
                ReloadConfigKey    = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"));
                ToggleChunkGridKey = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleChunkGrid", "C"));
                CycleLocationKey   = HotkeyBinding.Parse(ini.GetString("Hotkeys", "CycleLocation", "OemPipe"));
                ZoomInHotkey       = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ZoomInHotkey", "OemPlus"));
                ZoomOutHotkey      = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ZoomOutHotkey", "OemMinus"));
                ToggleIndicatorKey = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleIndicator", "I"));
                ToggleMobsKey      = HotkeyBinding.Parse(ini.GetString("Hotkeys", "ToggleMobs", "B"));

                // Players.
                ShowOtherPlayers = ini.GetBool("Players", "ShowOtherPlayers", true);

                // Mobs (Enemies + Dragon).
                ShowEnemies     = ini.GetBool("Mobs", "ShowEnemies", true);
                EnemyColor      = ParseColor(ini.GetString("Mobs", "EnemyColor", "255,0,0,255"));
                EnemyDotSizePx  = Clamp(ini.GetInt("Mobs", "EnemyDotSizePx", 4), 1, 12);

                ShowDragon      = ini.GetBool("Mobs", "ShowDragon", true);
                DragonColor     = ParseColor(ini.GetString("Mobs", "DragonColor", "0,255,0,255"));
                DragonDotSizePx = Clamp(ini.GetInt("Mobs", "DragonDotSizePx", 7), 2, 20);

                string opc = ini.GetString("Players", "OtherPlayerColor", "random");
                if (!string.IsNullOrWhiteSpace(opc) &&
                    string.Equals(opc.Trim(), "random", StringComparison.OrdinalIgnoreCase))
                {
                    OtherPlayerColorRandom = true;
                    OtherPlayerColor = new Color(0, 240, 255, 255); // Unused when random. // #00F0FFFF.
                }
                else
                {
                    OtherPlayerColorRandom = false;
                    OtherPlayerColor = ParseColor(opc);
                }
                OtherPlayerDotSizePx = Clamp(ini.GetInt("Players", "OtherPlayerDotSizePx", 5), 2, 12);

                // Compass.
                ShowCompass               = ini.GetBool("Compass", "ShowCompass", true);
                CompassRotatesWithPlayer  = ini.GetBool("Compass", "CompassRotatesWithPlayer", true);
                CompassPaddingPx          = Clamp(ini.GetInt("Compass", "CompassPaddingPx", 6), 0, 64);
                CompassFontScale          = Clamp(ini.GetFloat("Compass", "CompassFontScale", 0.90f), 0.20f, 4.0f);
                CompassColor              = ParseColor(ini.GetString("Compass", "CompassColor", "255,255,255,220"));
                CompassOutlineColor       = ParseColor(ini.GetString("Compass", "CompassOutlineColor", "0,0,0,200"));
                CompassOutlineThicknessPx = Clamp(ini.GetInt("Compass", "CompassOutlineThicknessPx", 2), 0, 12);

                // Layout / toggles.
                EnabledByDefault                = ini.GetBool("Minimap", "Enabled", true);
                Player                          = ini.GetBool("Minimap", "Player", true);
                ToggleChunkGrid                 = ini.GetBool("Minimap", "ToggleChunkGrid", false);
                ShowBiomeEdges                  = ini.GetBool("Minimap", "ShowBiomeEdges", false);
                MinimapCoordinates              = ini.GetBool("Minimap", "MinimapCoordinates", true);
                ShowCurrentBiome                = ini.GetBool("Minimap", "ShowCurrentBiome", true);

                MinimapScale                    = Clamp(ini.GetInt("Minimap", "MinimapScale", 180), 80, 420);
                MarginPx                        = Clamp(ini.GetInt("Minimap", "MarginPx", 12), 0, 200);
                TextSpacingPx                   = Clamp(ini.GetInt("Minimap", "TextSpacingPx", 4), 0, 40);

                MinimapFillAlpha                = (byte)Clamp(ini.GetInt("Minimap", "MinimapFillAlpha", 90), 0, 255);
                BiomeFillAlpha                  = (byte)Clamp(ini.GetInt("Minimap", "BiomeFillAlpha", 110), 0, 255);

                InitialZoom                     = Clamp(ini.GetFloat("Minimap", "InitialZoom", 0.25f), 0.001f, 10f);
                ZoomMin                         = Clamp(ini.GetFloat("Minimap", "ZoomMin", 0.05f), 0.001f, 10f);
                ZoomMax                         = Clamp(ini.GetFloat("Minimap", "ZoomMax", 2.00f), 0.001f, 10f);
                if (ZoomMax < ZoomMin) ZoomMax = ZoomMin;

                ZoomStepMul                     = Clamp(ini.GetFloat("Minimap", "ZoomStepMul", 1.12f), 1.01f, 3.0f);

                PlayerFacingIndicator           = ini.GetBool("Minimap", "PlayerFacingIndicator", true);
                PlayerFacingUseTriangle         = ini.GetBool("Minimap", "PlayerFacingUseTriangle", true);
                PlayerFacingLengthPx            = Clamp(ini.GetInt("Minimap", "PlayerFacingLengthPx", 10), 4, 64);
                PlayerFacingTriangleBaseWidthPx = Clamp(ini.GetInt("Minimap", "PlayerFacingTriangleBaseWidthPx", 8), 4, 64);
                PlayerFacingThicknessPx         = Clamp(ini.GetInt("Minimap", "PlayerFacingThicknessPx", 1), 1, 8);
                PlayerFacingTriangleFilled      = ini.GetBool("Minimap", "PlayerFacingTriangleFilled", true);
                PlayerFacingTriangleFillStepPx  = Clamp(ini.GetInt("Minimap", "PlayerFacingTriangleFillStepPx", 1), 1, 8);
                PlayerFacingStartOffsetPx       = Clamp(ini.GetFloat("Minimap", "PlayerFacingStartOffsetPx", 0f), -16, 16);
                PlayerFacingColor               = ParseColor(ini.GetString("Minimap", "PlayerFacingColor", "191,255,0,220"));
                PlayerFacingOutlineColor        = ParseColor(ini.GetString("Minimap", "PlayerFacingOutlineColor", "0,0,0,200"));

                FontScaleMin                    = Clamp(ini.GetFloat("Text", "FontScaleMin", 0.35f), 0.1f, 5f);
                FontScaleMax                    = Clamp(ini.GetFloat("Text", "FontScaleMax", 2.50f), 0.1f, 5f);
                FontScale                       = Clamp(ini.GetFloat("Text", "FontScale", 1.00f), FontScaleMin, FontScaleMax);

                OutlineEnabled                  = ini.GetBool("Outline", "OutlineEnabled", true);
                OutlineThicknessPx              = Clamp(ini.GetInt("Outline", "OutlineThicknessPx", 2), 0, 12);

                UpdateIntervalMs                = Clamp(ini.GetInt("Perf", "UpdateIntervalMs", 150), 25, 2000);
                SampleStepPx                    = Clamp(ini.GetInt("Perf", "SampleStepPx", 6), 2, 32);

                // Enums.
                MinimapShape     = ParseEnum(ini.GetString("Minimap", "MinimapShape", "Circle"), MinimapShape.Circle);
                MinimapLocation  = ParseEnum(ini.GetString("Minimap", "MinimapLocation", "TopLeft"), MinimapAnchor.TopLeft);

                // Colors
                FrameColor       = ParseColor(ini.GetString("Colors", "FrameColor", "255,255,255,140"));
                CoordinatesColor = ParseColor(ini.GetString("Colors", "CoordinatesColor", "255,255,255,255"));
                OutlineColor     = ParseColor(ini.GetString("Colors", "OutlineColor", "0,0,0,200"));

                PlayerColor      = ParseColor(ini.GetString("Colors", "PlayerColor", "255,80,80,255"));

                GridColor        = ParseColor(ini.GetString("Colors", "GridColor", "255,255,255,40"));
                GridStepBlocks   = Clamp(ini.GetInt("Colors", "GridStepBlocks", 16), 1, 256);
                MinGridPixels    = Clamp(ini.GetInt("Colors", "MinGridPixels", 24), 1, 200);

                BiomeEdgeColor   = ParseColor(ini.GetString("Colors", "BiomeEdgeColor", "0,0,0,140"));

                ClassicBiomeColor  = ParseColor(ini.GetString("Palette", "ClassicBiomeColor",  "70,160,70,255"));
                LagoonBiomeColor   = ParseColor(ini.GetString("Palette", "LagoonBiomeColor",   "60,170,170,255"));
                DesertBiomeColor   = ParseColor(ini.GetString("Palette", "DesertBiomeColor",   "200,180,60,255"));
                MountainBiomeColor = ParseColor(ini.GetString("Palette", "MountainBiomeColor", "150,150,150,255"));
                ArcticBiomeColor   = ParseColor(ini.GetString("Palette", "ArcticBiomeColor",   "140,190,255,255"));
                DecentBiomeColor   = ParseColor(ini.GetString("Palette", "DecentBiomeColor",   "190,120,220,255"));
                HellBiomeColor     = ParseColor(ini.GetString("Palette", "HellBiomeColor",     "190,70,70,255"));

                // IMPORTANT: Feed palette into the existing shared config the WGP palette already uses.
                MinimapConfig.ClassicBiomeColor  = new Color(ClassicBiomeColor.R, ClassicBiomeColor.G, ClassicBiomeColor.B);
                MinimapConfig.LagoonBiomeColor   = new Color(LagoonBiomeColor.R, LagoonBiomeColor.G, LagoonBiomeColor.B);
                MinimapConfig.DesertBiomeColor   = new Color(DesertBiomeColor.R, DesertBiomeColor.G, DesertBiomeColor.B);
                MinimapConfig.MountainBiomeColor = new Color(MountainBiomeColor.R, MountainBiomeColor.G, MountainBiomeColor.B);
                MinimapConfig.ArcticBiomeColor   = new Color(ArcticBiomeColor.R, ArcticBiomeColor.G, ArcticBiomeColor.B);
                MinimapConfig.DecentBiomeColor   = new Color(DecentBiomeColor.R, DecentBiomeColor.G, DecentBiomeColor.B);
                MinimapConfig.HellBiomeColor     = new Color(HellBiomeColor.R, HellBiomeColor.G, HellBiomeColor.B);

                IsLoaded = true;
            }
            catch
            {
                IsLoaded = false; // Best-effort.
            }
        }

        /// <summary>
        /// Writes a default INI file for first-run setup.
        /// Notes:
        /// - This file is intentionally simple (no escaping/multi-line).
        /// - Values are chosen to be safe and readable on most HUD layouts.
        /// </summary>
        private static void WriteDefaults(string path)
        {
            var lines = new[]
            {
                "; -------------------------------------------------------------------------------------------------",
                "; Minimap HUD Overlay",
                "; -------------------------------------------------------------------------------------------------",
                "",
                "[Hotkeys]",
                "ToggleMap       = N",
                "ReloadConfig    = Ctrl+Shift+R",
                "ToggleChunkGrid = C",
                "CycleLocation   = OemPipe", // The '\' key name in XNA.
                "ZoomInHotkey    = OemPlus",
                "ZoomOutHotkey   = OemMinus",
                "ToggleIndicator = I",
                "ToggleMobs      = B",
                "",
                "[Minimap]",
                "Enabled                         = true",
                "Player                          = true",
                "ToggleChunkGrid                 = false",
                "ShowBiomeEdges                  = false",
                "MinimapShape                    = Circle",
                "MinimapScale                    = 180",
                "MinimapCoordinates              = true",
                "ShowCurrentBiome                = true",
                "MinimapLocation                 = TopLeft",
                "MarginPx                        = 12",
                "TextSpacingPx                   = 4",
                "InitialZoom                     = 0.25",
                "ZoomMin                         = 0.05",
                "ZoomMax                         = 2.0",
                "ZoomStepMul                     = 1.12",
                "MinimapFillAlpha                = 90",
                "BiomeFillAlpha                  = 110",
                "PlayerFacingIndicator           = true",
                "PlayerFacingUseTriangle         = true",
                "PlayerFacingLengthPx            = 10",
                "PlayerFacingTriangleBaseWidthPx = 8",
                "PlayerFacingThicknessPx         = 1",
                "PlayerFacingTriangleFilled      = true",
                "PlayerFacingTriangleFillStepPx  = 1",
                "PlayerFacingStartOffsetPx       = 1",
                "PlayerFacingColor               = 191,255,0,220",
                "PlayerFacingOutlineColor        = 0,0,0,200",
                "",
                "[Players]",
                "ShowOtherPlayers     = true",
                "OtherPlayerColor     = #00F0FFFF",
                "OtherPlayerDotSizePx = 5",
                "",
                "[Mobs]",
                "ShowEnemies     = true",
                "EnemyColor      = 255,0,0,255",
                "EnemyDotSizePx  = 4",
                "ShowDragon      = true",
                "DragonColor     = 0,255,0,255",
                "DragonDotSizePx = 7",
                "",
                "[Compass]",
                "ShowCompass               = true",
                "CompassPaddingPx          = 6",
                "CompassFontScale          = 0.90",
                "CompassColor              = 255,255,255,220",
                "CompassOutlineEnabled     = true",
                "CompassOutlineThicknessPx = 2",
                "CompassOutlineColor       = 0,0,0,200",
                "",
                "[Text]",
                "FontScaleMin = 0.35",
                "FontScaleMax = 2.50",
                "FontScale    = 1.00",
                "",
                "[Outline]",
                "OutlineEnabled     = true",
                "OutlineThicknessPx = 2",
                "",
                "[Perf]",
                "UpdateIntervalMs = 150",
                "SampleStepPx     = 6",
                "",
                "[Colors]",
                "FrameColor       = 255,255,255,140",
                "CoordinatesColor = 255,255,255,255",
                "OutlineColor     = 0,0,0,200",
                "PlayerColor      = 255,80,80,255",
                "GridColor        = 255,255,255,40",
                "GridStepBlocks   = 16",
                "MinGridPixels    = 24",
                "BiomeEdgeColor   = 0,0,0,140",
                "",
                "[Palette]",
                "ClassicBiomeColor  = 70,160,70,255",
                "LagoonBiomeColor   = 60,170,170,255",
                "DesertBiomeColor   = 200,180,60,255",
                "MountainBiomeColor = 150,150,150,255",
                "ArcticBiomeColor   = 140,190,255,255",
                "DecentBiomeColor   = 190,120,220,255",
                "HellBiomeColor     = 190,70,70,255",
                "",
            };

            File.WriteAllLines(path, lines);
        }
        #endregion

        #region Parse Helpers

        /// <summary>
        /// Parses an enum value from a string (case-insensitive), returning a fallback on failure.
        /// </summary>
        private static TEnum ParseEnum<TEnum>(string s, TEnum fallback) where TEnum : struct
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (Enum.TryParse(s.Trim(), true, out TEnum v))
                    return v;
            }
            return fallback;
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

        /// <summary>
        /// Parses an integer using invariant culture, returning a fallback on failure.
        /// </summary>
        private static int ParseInt(string s, int fallback)
        {
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;
            return fallback;
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

        #region Hotkey Binding

        /// <summary>
        /// Hotkey binding helper (Ctrl/Shift/Alt + key).
        /// Summary:
        /// - Parses human-friendly strings ("Ctrl+Shift+R", "OemPlus", "M").
        /// - Provides <see cref="IsDown"/> for modifier-aware state checks.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - This struct is used by your HUD patch to check edge presses (WasKeyPressed + modifier gates).
        /// - Keep parsing forgiving; unknown tokens are ignored.
        /// </remarks>
        internal struct HotkeyBinding
        {
            public Keys Key;
            public bool Ctrl;
            public bool Shift;
            public bool Alt;

            public bool IsEmpty => Key == Keys.None;

            public override string ToString()
            {
                string s = "";
                if (Ctrl) s += "Ctrl+";
                if (Shift) s += "Shift+";
                if (Alt) s += "Alt+";
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
        /// <remarks>
        /// Notes:
        /// - This intentionally avoids dependencies and keeps parsing rules simple and predictable.
        /// - It is read-only; persistence is handled by TryUpsertIniValue(...) above.
        /// </remarks>
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
            private static bool _ticked;

            /// <summary>
            /// Call once per input tick (our HUD patch does this).
            /// </summary>
            public static void Tick()
            {
                _now = Keyboard.GetState();
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

                bool downNow = hk.IsDown(_now);
                bool downPrev = hk.IsDown(_prev);

                return downNow && !downPrev;
            }

            /// <summary>
            /// Call after you've checked all bindings for this tick.
            /// </summary>
            public static void EndTick()
            {
                if (!_ticked) return;
                _prev = _now;
                _ticked = false;
            }
        }
        #endregion
    }
}