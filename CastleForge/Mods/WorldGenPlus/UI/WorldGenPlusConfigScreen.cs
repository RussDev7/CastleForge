/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using DNA.Input;
using System;

namespace WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus configuration UI screen.
    ///
    /// Notes:
    /// - This screen draws its own scrollable list (instead of relying on MenuScreen's built-in list drawing),
    ///   because it needs: pixel-based scrolling, mouse hover highlight, mouse wheel scroll, and a draggable scrollbar.
    /// - Rows can be selectable settings OR non-selectable headers/spacers.
    /// - Some rows support Left/Right adjustment (keyboard hold-to-repeat + controller taps + mouse arrow hit-test).
    /// </summary>
    internal sealed class WorldGenPlusConfigScreen : MenuScreen
    {
        #region Fields

        #region Fields (Core)

        /// <summary>Owning game instance (fonts, graphics device, etc.).</summary>
        private readonly CastleMinerZGame _game;

        /// <summary>1x1 white pixel texture used for drawing solid rectangles.</summary>
        private Texture2D _white;

        /// <summary>Description text shown in the bottom description panel for the currently focused row.</summary>
        private string _focusDesc = "";

        #endregion

        #region Fields (Seed Editing)

        /// <summary>True while the user is typing a seed override value.</summary>
        private bool _editingSeed;

        /// <summary>Text buffer for seed override editing (digits and optional '-').</summary>
        private string _seedEditBuffer = "";

        #endregion

        #region Fields (Rows / Menu Data)

        /// <summary>All logical rows (settings, headers, spacers) used to build MenuItems.</summary>
        private readonly List<Row> _rows = new List<Row>();

        #endregion

        #region Fields (Scrolling / Layout)

        // Scroll state (in pixels).
        private int _scrollPx;

        // Cached layout rects (updated every draw).
        private Rectangle _panelRect;
        private Rectangle _listViewportRect;
        private Rectangle _descRect;

        #endregion

        #region Fields (Scrollbar Interaction)

        // Scrollbar state
        private bool      _mouseLeftWasDown;
        private bool      _draggingThumb;
        private int       _dragThumbOffsetY; // MouseY - thumbTop when dragging.

        private Rectangle _scrollTrackRect;
        private Rectangle _scrollThumbRect;

        #endregion

        #region Fields (Mouse Edge Detect / Hover)

        // Mouse RMB edge-detect.
        private bool _mouseRightWasDown;

        private const string RingRandomToken = "@Random";

        // Visual hover highlight (mouse). -1 = nothing highlighted.
        private int _hoverIndex = -1;

        // True when mouse is over the list area this frame (used by DrawMenuList).
        private bool _mouseOverListForDraw;

        #endregion

        #region Fields (Hold-to-Repeat Left/Right)

        // --- Hold-to-repeat for Left/Right ---
        private int         _lrHoldDir;        // -1 / 0 / +1.
        private Row         _lrHoldRow;        // Which row is being repeated.
        private float       _lrHeldSec;        // How long held.
        private float       _lrNextRepeatSec;  // Countdown to next repeat.
        private const float LR_INITIAL_DELAY = 0.30f;

        #endregion

        #endregion

        #region Drawing (Screen)

        /// <summary>
        /// Draws the config panel, list, header, description, and scrollbar.
        /// Notes:
        /// - The list is drawn in its own pass so it can be effectively "clipped" by viewport checks.
        /// - Header + description + scrollbar are drawn last so they remain on top.
        /// </summary>
        protected override void OnDraw(GraphicsDevice device, SpriteBatch spriteBatch, GameTime gameTime)
        {
            ComputeLayout(out _panelRect, out _listViewportRect, out _descRect);

            int maxScroll = GetMaxScrollPx(_listViewportRect.Height);
            ClampScroll(maxScroll);

            UpdateScrollbarRects(_listViewportRect, maxScroll);

            // Shrink list draw width so it doesn't run under the scrollbar.
            int listDrawWidth = Math.Max(1, _listViewportRect.Width - _scrollTrackRect.Width - 6);

            float sy = Screen.Adjuster.ScaleFactor.Y;

            // Same header padding values as ComputeLayout.
            int headerPadTop = (int)(16f * sy);
            int headerGap = (int)(6f * sy);

            float titleY = _panelRect.Y + headerPadTop;
            float noteY = titleY + (_game._largeFont.LineSpacing * sy) + headerGap;

            // Panel background.
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            spriteBatch.Draw(_white, _panelRect, new Color(0, 0, 0, 180));
            spriteBatch.Draw(_white, new Rectangle(_panelRect.X, _panelRect.Y, _panelRect.Width, 2), new Color(255, 255, 255, 90));
            spriteBatch.Draw(_white, new Rectangle(_panelRect.X, _panelRect.Bottom - 2, _panelRect.Width, 2), new Color(255, 255, 255, 90));

            spriteBatch.End();

            // Draw the menu list ourselves (clipped by viewport checks).
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            DrawMenuList(spriteBatch, _listViewportRect, listDrawWidth);
            spriteBatch.End();

            // Header + description + scrollbar (draw last so it's always on top).
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            spriteBatch.DrawOutlinedText(
                _game._largeFont,
                "WorldGenPlus - Custom Biomes",
                new Vector2(_panelRect.X + 20, titleY),
                Color.White,
                Color.Black,
                2,
                sy,
                0f,
                Vector2.Zero);

            spriteBatch.DrawOutlinedText(
                _game._medFont,
                "NOTE: Changing worldgen settings after exploring can cause seams.",
                new Vector2(_panelRect.X + 20, noteY),
                Color.Yellow,
                Color.Black,
                2,
                sy,
                0f,
                Vector2.Zero);

            spriteBatch.Draw(_white, _descRect, new Color(0, 0, 0, 140));
            spriteBatch.DrawOutlinedText(
                _game._medFont,
                _focusDesc ?? "",
                new Vector2(_descRect.X + 10, _descRect.Y + 10),
                Color.White,
                Color.Black,
                2,
                sy,
                0f,
                Vector2.Zero);

            DrawScrollbar(spriteBatch /* , maxScroll */);

            spriteBatch.End();
        }
        #endregion

        #region Row Construction (BuildRows)

        /// <summary>
        /// Builds the list of rows (settings + headers/spacers) from WGConfig.
        /// Notes:
        /// - This method is intentionally "append-only" to preserve stable visual ordering.
        /// - Many sections are grouped via Header() + Spacer() rows.
        /// - Some nested #region blocks are present inside for author convenience (kept as-is).
        /// </summary>
        private void BuildRows()
        {
            _rows.Clear();
            WGConfig.Load(); // Ensure file-backed state exists.

            _rows.Add(Header("WorldGenPlus"));

            #region [WorldGenPlus]

            _rows.Add(new Row
            {
                Key      = "Enabled",
                Desc     = "Master toggle. When ON, the host swaps to the WorldGenPlus builder.",
                GetText  = () => "Enabled: " + (WGConfig.EnabledValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.EnabledValue = !WGConfig.EnabledValue;
                    WGConfig.Save();
                }
            });
            #endregion

            #region [Seed]

            _rows.Add(new Row
            {
                Key      = "SeedEnabled",
                Desc     = "Enable seed override (independent of WorldGenPlus Enabled).",
                GetText  = () => "Seed Override: " + (WGConfig.SeedOverrideEnabledValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.SeedOverrideEnabledValue = !WGConfig.SeedOverrideEnabledValue;
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key      = "SeedValue",
                Desc     = "Seed value used when override is ON. Press A/Enter to edit, Enter to commit.",
                GetText  = () =>
                {
                    if (_editingSeed) return "Seed Value: [" + _seedEditBuffer + "]";
                    return "Seed Value: " + WGConfig.SeedOverrideValue;
                },
                OnSelect = () =>
                {
                    _editingSeed = true;
                    _seedEditBuffer = WGConfig.SeedOverrideValue.ToString();
                }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Multiplayer"));

            #region [Multiplayer]

            _rows.Add(new Row
            {
                Key      = "Multiplayer_SyncFromHost",
                Desc     = "Client: When ON, use host-sent WorldGenPlus settings for this session (keeps everyone in sync).",
                GetText  = () => "Sync From Host: " + (WGConfig.Multiplayer_SyncFromHostValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.Multiplayer_SyncFromHostValue = !WGConfig.Multiplayer_SyncFromHostValue;
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key      = "Multiplayer_BroadcastToClients",
                Desc     = "Host: When ON, clients can request and receive the host's WorldGenPlus settings on join.",
                GetText  = () => "Broadcast To Clients: " + (WGConfig.Multiplayer_BroadcastToClientsValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.Multiplayer_BroadcastToClientsValue = !WGConfig.Multiplayer_BroadcastToClientsValue;
                    WGConfig.Save();
                }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Surface"));

            #region [Surface]

            _rows.Add(new Row
            {
                Key     = "SurfaceMode",
                Desc    = "Select surface layout: Vanilla Rings, Square Bands, Single, or Random Regions.",
                GetText = () =>
                {
                    var m = (WorldGenPlusBuilder.SurfaceGenMode)WGConfig.SurfaceModeValue;
                    return "Surface Mode: " + m.ToString();
                },
                OnLeftRight = dir =>
                {
                    int v = WGConfig.SurfaceModeValue + dir;
                    if (v < 0) v = 3;
                    if (v > 3) v = 0;
                    WGConfig.SurfaceModeValue = v;
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "SingleSurfaceBiome",
                Desc        = "Mode=2 only. Biome used everywhere in Single mode. Left/Right cycles biome type.",
                GetText     = () => "Single Biome: " + ShortBiome(WGConfig.SingleSurfaceBiomeValue),
                OnLeftRight = dir =>
                {
                    var choices = GetBiomeChoices();
                    if (choices == null || choices.Length == 0)
                        return;

                    int cur = IndexOf(choices, WGConfig.SingleSurfaceBiomeValue);
                    cur = (cur + dir) % choices.Length;
                    if (cur < 0) cur += choices.Length;

                    WGConfig.SingleSurfaceBiomeValue = choices[cur];
                    WGConfig.Save();
                }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Rings"));

            #region [Rings]

            _rows.Add(new Row
            {
                Key         = "RegionCellSize",
                Desc        = "Mode=3 only. Size of a region in blocks. Bigger = bigger biome blobs. Left/Right.",
                GetText     = () => "Region Cell Size: " + WGConfig.RegionCellSizeValue,
                OnLeftRight = dir =>
                {
                    // Step in bigger chunks so it's not painful to tune.
                    int next = WGConfig.RegionCellSizeValue + dir * 64;
                    WGConfig.RegionCellSizeValue = Clamp(next, 32, 16384);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "RegionBlendWidth",
                Desc        = "Mode=3 only. Blend width at region borders (0 = hard edges). Left/Right.",
                GetText     = () => "Region Blend Width: " + WGConfig.RegionBlendWidthValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.RegionBlendWidthValue + dir * 16;
                    WGConfig.RegionBlendWidthValue = Clamp(next, 0, 4096);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "RegionBlendPower",
                Desc        = "Mode=3 only. Border sharpness (2..4 typical). Higher = sharper borders. Left/Right.",
                GetText     = () =>
                    "Region Blend Power: " +
                    WGConfig.RegionBlendPowerValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.RegionBlendPowerValue + dir * 0.25f;
                    WGConfig.RegionBlendPowerValue = Clamp(next, 0.5f, 8f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key      = "RegionSmoothEdges",
                Desc     = "Mode=3 only. When ON, blends borders using Voronoi bisector distance (constant-width smooth edges).",
                GetText  = () => "Region Smooth Edges: " + (WGConfig.RegionSmoothEdgesValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.RegionSmoothEdgesValue = !WGConfig.RegionSmoothEdgesValue;
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "TransitionWidth",
                Desc        = "Width of the blend zone between rings (vanilla = 100). Use Left/Right.",
                GetText     = () => "Transition Width: " + WGConfig.TransitionWidthValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.TransitionWidthValue + dir * 10;
                    WGConfig.TransitionWidthValue = Clamp(next, 0, 5000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "RingPeriod",
                Desc        = "Repeat distance of the ring pattern. Left/Right.",
                GetText     = () => "Ring Period: " + WGConfig.RingPeriodValue,
                OnLeftRight = dir =>
                {
                    WGConfig.RingPeriodValue = Clamp(WGConfig.RingPeriodValue + dir * 100, 1, 2_000_000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key      = "MirrorRepeat",
                Desc     = "When ON, every other period mirrors back toward the origin.",
                GetText  = () => "Mirror Repeat: " + (WGConfig.MirrorRepeatValue ? "ON" : "OFF"),
                OnSelect = () => { WGConfig.MirrorRepeatValue = !WGConfig.MirrorRepeatValue; WGConfig.Save(); }
            });

            _rows.Add(new Row
            {
                Key         = "WorldBlendRadius",
                Desc        = "Controls how strongly the vanilla overlays fade in/out. Left/Right.",
                GetText     = () => "World Blend Radius: " + WGConfig.WorldBlendRadiusValue,
                OnLeftRight = dir =>
                {
                    WGConfig.WorldBlendRadiusValue = Clamp(WGConfig.WorldBlendRadiusValue + dir * 50, 1, 2_000_000);
                    WGConfig.Save();
                }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Ring Indexing"));

            #region [Ring Indexing]

            // Ring count editor.
            _rows.Add(new Row
            {
                Key         = "RingCount",
                Desc        = "Number of ring cores. Left/Right to add/remove.",
                GetText     = () => "Ring Cores: " + WGConfig.RingsValue.Count,
                OnLeftRight = dir =>
                {
                    var rings = WGConfig.RingsValue;
                    int next = Clamp(rings.Count + dir, 1, 64);

                    while (rings.Count < next)
                    {
                        int lastEnd = rings[rings.Count - 1].EndRadius;
                        rings.Add(new RingCore(lastEnd + 500, rings[rings.Count - 1].BiomeType));
                    }
                    while (rings.Count > next)
                        rings.RemoveAt(rings.Count - 1);

                    WGConfig.Save();
                    BuildRows();
                    RebuildMenuItems();
                }
            });

            // Per-ring editors (End + Biome).
            for (int i = 0; i < WGConfig.RingsValue.Count; i++)
            {
                int idx = i;

                _rows.Add(new Row
                {
                    Key     = "Ring" + idx + "End",
                    Desc    = "Edit this ring's EndRadius. Left/Right.",
                    GetText = () =>
                    {
                        var r = WGConfig.RingsValue[idx];
                        return $"Ring{idx} End: {r.EndRadius}";
                    },
                    OnLeftRight = dir =>
                    {
                        var rings = WGConfig.RingsValue;
                        var r = rings[idx];

                        int lo = (idx == 0) ? 1 : (rings[idx - 1].EndRadius + 1);
                        int hi = (idx == rings.Count - 1) ? 2_000_000 : (rings[idx + 1].EndRadius - 1);
                        if (hi < lo) hi = lo;

                        r.EndRadius = Clamp(r.EndRadius + dir * 50, lo, hi);
                        rings[idx] = r;

                        WGConfig.Save();
                    }
                });

                _rows.Add(new Row
                {
                    Key     = "Ring" + idx + "Biome",
                    Desc    = "Left/Right cycles biome type for this ring core.",
                    GetText = () =>
                    {
                        var r = WGConfig.RingsValue[idx];
                        return $"Ring{idx} Biome: {ShortBiome(r.BiomeType)}";
                    },
                    OnLeftRight = dir =>
                    {
                        var rings = WGConfig.RingsValue;
                        var rc = rings[idx];

                        var choices = GetRingBiomeChoices();
                        int cur = IndexOf(choices, rc.BiomeType);
                        cur = (cur + dir) % choices.Length;
                        if (cur < 0) cur += choices.Length;

                        rc.BiomeType = choices[cur];
                        rings[idx] = rc;

                        WGConfig.Save();
                    }
                });
            }
            #endregion

            #region [RingsRandom]

            _rows.Add(new Row
            {
                Key      = "RingsRandom_AutoIncludeCustomBiomes",
                Desc     = "Used ONLY when a ring core biome is set to '@Random'. When ON, discovered custom biomes are appended to the ring-random bag (weight=1).",
                GetText  = () => "Ring Random: Auto Include Custom Biomes: " + (WGConfig.AutoIncludeCustomBiomesForRandomRingsValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.AutoIncludeCustomBiomesForRandomRingsValue = !WGConfig.AutoIncludeCustomBiomesForRandomRingsValue;

                    // Apply immediately so the UI shows the new entries now.
                    if (WGConfig.AutoIncludeCustomBiomesForRandomRingsValue)
                    {
                        CustomBiomeRegistry.Refresh();
                        var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();
                        var bag = WGConfig.RandomRingBiomeChoicesValue;

                        for (int i = 0; i < custom.Length; i++)
                        {
                            string t = custom[i];
                            bool has = false;

                            for (int j = 0; j < bag.Count; j++)
                            {
                                if (string.Equals(bag[j], t, StringComparison.OrdinalIgnoreCase))
                                {
                                    has = true;
                                    break;
                                }
                            }

                            if (!has)
                                bag.Add(t);
                        }
                    }

                    WGConfig.Save();
                    BuildRows();
                    RebuildMenuItems();
                }
            });

            _rows.Add(new Row
            {
                Key      = "RandomRingsVaryByPeriod",
                Desc     = "If ON, each repeated ring period gets new random biomes. If OFF, random picks repeat every period.",
                GetText  = () => "Ring Random: Vary By Period: " + (WGConfig.RandomRingsVaryByPeriodValue ? "ON" : "OFF"),
                OnSelect = () => { WGConfig.RandomRingsVaryByPeriodValue = !WGConfig.RandomRingsVaryByPeriodValue; WGConfig.Save(); }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Random Region Indexing"));

            #region [RandomRegions]

            // Helpful note row (non-selectable).
            _rows.Add(new Row
            {
                Key        = "_rr_note",
                Selectable = false,
                Desc       = "",
                GetText    = () => ":: Duplicates are allowed and act as weights (more copies = more often)."
            });

            // BiomeCount editor (controls list length).
            _rows.Add(new Row
            {
                Key     = "RandomRegions_BiomeCount",
                Desc    = "Mode=3 only. Number of entries in the Random Regions biome bag. Duplicates are weights. Left/Right to add/remove.",
                GetText = () =>
                {
                    var cfg = WGConfig.Current;
                    return "Biome Count: " + cfg.RandomSurfaceBiomeChoices.Count;
                },
                OnLeftRight = dir =>
                {
                    var cfg = WGConfig.Current;
                    var bag = cfg.RandomSurfaceBiomeChoices;

                    // Keep at least 1 entry so the bag can't be empty.
                    int next = Clamp(bag.Count + dir, 1, 512);

                    // Choices used when we need to append new entries.
                    var choices = GetBiomeChoices();

                    while (bag.Count < next)
                    {
                        // Default: copy last entry (keeps "weight" workflows easy),
                        // fallback to first known biome if list is somehow empty.
                        string add = (bag.Count > 0) ? bag[bag.Count - 1] : (choices.Length > 0 ? choices[0] : "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome");
                        bag.Add(add);
                    }

                    while (bag.Count > next)
                        bag.RemoveAt(bag.Count - 1);

                    WGConfig.Save();
                    BuildRows();
                    RebuildMenuItems();
                }
            });

            // Per-entry editors (Biome0..BiomeN).
            for (int i = 0; i < WGConfig.Current.RandomSurfaceBiomeChoices.Count; i++)
            {
                int idx = i;

                _rows.Add(new Row
                {
                    Key     = "RandomRegions_Biome" + idx,
                    Desc    = "Mode=3 only. Bag entry used by Random Regions. Duplicates are allowed (weights). Left/Right cycles biome type.",
                    GetText = () =>
                    {
                        var cfg = WGConfig.Current;
                        string s = (idx >= 0 && idx < cfg.RandomSurfaceBiomeChoices.Count) ? cfg.RandomSurfaceBiomeChoices[idx] : "";
                        return $"Biome{idx}: {ShortBiome(s)}";
                    },
                    OnLeftRight = dir =>
                    {
                        var cfg = WGConfig.Current;
                        var bag = cfg.RandomSurfaceBiomeChoices;

                        if (idx < 0 || idx >= bag.Count)
                            return;

                        var choices = GetBiomeChoices(); // Vanilla + discovered custom.
                        if (choices == null || choices.Length == 0)
                            return;

                        int cur = IndexOf(choices, bag[idx]);
                        cur = (cur + dir) % choices.Length;
                        if (cur < 0) cur += choices.Length;

                        bag[idx] = choices[cur]; // IMPORTANT: Keep duplicates (weights).
                        WGConfig.Save();
                    }
                });
            }

            // Auto include custom biomes toggle.
            _rows.Add(new Row
            {
                Key     = "RandomRegions_AutoIncludeCustomBiomes",
                Desc    = "Mode=3 only. When ON, discovered custom biomes are auto-added to the Random Regions bag (weight=1 each).",
                GetText = () =>
                {
                    var cfg = WGConfig.Current;
                    return "Auto Include Custom Biomes: " + (cfg.AutoIncludeCustomBiomesForRandomRegions ? "ON" : "OFF");
                },
                OnSelect = () =>
                {
                    var cfg = WGConfig.Current;
                    cfg.AutoIncludeCustomBiomesForRandomRegions = !cfg.AutoIncludeCustomBiomesForRandomRegions;

                    // If turning ON, immediately append missing custom biomes once each.
                    if (cfg.AutoIncludeCustomBiomesForRandomRegions)
                    {
                        CustomBiomeRegistry.Refresh();
                        var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();
                        var bag = cfg.RandomSurfaceBiomeChoices;

                        for (int i = 0; i < custom.Length; i++)
                        {
                            string t = custom[i];
                            bool has = false;

                            for (int j = 0; j < bag.Count; j++)
                            {
                                if (string.Equals(bag[j], t, StringComparison.OrdinalIgnoreCase))
                                {
                                    has = true;
                                    break;
                                }
                            }

                            if (!has)
                                bag.Add(t); // Add once (weight=1).
                        }
                    }

                    WGConfig.Save();
                    BuildRows();
                    RebuildMenuItems();
                }
            });
            #endregion

            #region [Overlay]

            _rows.Add(Spacer());
            _rows.Add(Header("Overlays"));

            #region [Overlay Toggles]

            _rows.Add(new Row { Key = "Caves",       Desc = "Toggle caves overlay.",      GetText = () => "Caves: " + (WGConfig.EnableCavesValue ? "ON" : "OFF"),                  OnSelect = () => { WGConfig.EnableCavesValue = !WGConfig.EnableCavesValue; WGConfig.Save(); } });
            _rows.Add(new Row { Key = "HellCeiling", Desc = "Toggle hell ceiling fade.",  GetText = () => "Hell Ceiling: " + (WGConfig.EnableHellCeilingValue ? "ON" : "OFF"),     OnSelect = () => { WGConfig.EnableHellCeilingValue = !WGConfig.EnableHellCeilingValue; WGConfig.Save(); } });
            _rows.Add(new Row { Key = "Hell",        Desc = "Toggle hell floor overlay.", GetText = () => "Hell: " + (WGConfig.EnableHellValue ? "ON" : "OFF"),                    OnSelect = () => { WGConfig.EnableHellValue = !WGConfig.EnableHellValue; WGConfig.Save(); } });
            _rows.Add(new Row { Key = "Origin",      Desc = "Toggle origin overlay.",     GetText = () => "Origin (Lantern Land): " + (WGConfig.EnableOriginValue ? "ON" : "OFF"), OnSelect = () => { WGConfig.EnableOriginValue = !WGConfig.EnableOriginValue; WGConfig.Save(); } });
            _rows.Add(new Row { Key = "Water",       Desc = "Toggle setting WaterLevel.", GetText = () => "Water: " + (WGConfig.EnableWaterValue ? "ON" : "OFF"),                  OnSelect = () => { WGConfig.EnableWaterValue = !WGConfig.EnableWaterValue; WGConfig.Save(); } });
            _rows.Add(new Row
            {
                Key      = "BiomeGuards",
                Desc     = "When ON, prevents certain overlays (Hell/HellCeiling/Bedrock) from running on special/test/unusual biomes.",
                GetText  = () => "Biome Guards: " + (WGConfig.EnableBiomeOverlayGuardsValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.EnableBiomeOverlayGuardsValue = !WGConfig.EnableBiomeOverlayGuardsValue;
                    WGConfig.Save();
                }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Bedrock Tuning"));

            #region [Bedrock]

            _rows.Add(new Row { Key = "Bedrock", Desc = "Toggle bedrock overlay.", GetText = () => "Bedrock: " + (WGConfig.EnableBedrockValue ? "ON" : "OFF"), OnSelect = () => { WGConfig.EnableBedrockValue = !WGConfig.EnableBedrockValue; WGConfig.Save(); } });

            _rows.Add(new Row
            {
                Key         = "Bedrock_MinLevel",
                Desc        = "CustomBedrockDepositer: Minimum bedrock thickness in blocks. Left/Right.",
                GetText     = () => "  - Bedrock Min Level: " + WGConfig.Bedrock_MinLevelValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Bedrock_MinLevelValue + dir * 1;
                    WGConfig.Bedrock_MinLevelValue = Clamp(next, 0, 128);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Bedrock_Variance",
                Desc        = "CustomBedrockDepositer: Thickness variance (adds wobble). Vanilla-ish = 3. Left/Right.",
                GetText     = () => "  - Bedrock Variance: " + WGConfig.Bedrock_VarianceValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Bedrock_VarianceValue + dir * 1;
                    WGConfig.Bedrock_VarianceValue = Clamp(next, 0, 128);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Bedrock_CoordDiv",
                Desc        = "CustomBedrockDepositer: Noise frequency control. Higher = smoother/lower-frequency thickness changes. Left/Right.",
                GetText     = () => "  - Bedrock Coord Div: " + WGConfig.Bedrock_CoordDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Bedrock_CoordDivValue + dir * 1;
                    WGConfig.Bedrock_CoordDivValue = Clamp(next, 1, 4096);
                    WGConfig.Save();
                }
            });
            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Crash Site Tuning"));

            #region [CrashSites]

            _rows.Add(new Row { Key         = "CrashSites", Desc        = "Toggle crash site overlay.", GetText     = () => "Crash Sites: " + (WGConfig.EnableCrashSitesValue ? "ON" : "OFF"), OnSelect = () => { WGConfig.EnableCrashSitesValue = !WGConfig.EnableCrashSitesValue; WGConfig.Save(); } });

            _rows.Add(new Row
            {
                Key         = "Crash_WorldScale",
                Desc        = "CustomCrashSiteDepositer: Noise frequency for crash site placement. Lower = larger, rarer features. Left/Right.",
                GetText     = () => "  - Crash World Scale: " + WGConfig.Crash_WorldScaleValue.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    // Small steps because this value is tiny in vanilla.
                    float step = 0.00025f;
                    float next = WGConfig.Crash_WorldScaleValue + dir * step;
                    WGConfig.Crash_WorldScaleValue = Clamp(next, 0.000001f, 1f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_NoiseThreshold",
                Desc        = "CustomCrashSiteDepositer: Threshold for activating a crash site (higher = fewer). Left/Right.",
                GetText     = () => "  - Crash Noise Threshold: " + WGConfig.Crash_NoiseThresholdValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Crash_NoiseThresholdValue + dir * 0.01f;
                    WGConfig.Crash_NoiseThresholdValue = Clamp(next, -1f, 1f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_CraterDepthMul",
                Desc        = "CustomCrashSiteDepositer: Crater depth strength. Higher = deeper craters. Left/Right.",
                GetText     = () => "  - Crash Crater Depth Mul: " + WGConfig.Crash_CraterDepthMulValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Crash_CraterDepthMulValue + dir * 10f;
                    WGConfig.Crash_CraterDepthMulValue = Clamp(next, 0f, 100000f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_EnableMound",
                Desc        = "CustomCrashSiteDepositer: When ON, builds a mound around some craters.",
                GetText     = () => "  - Crash Mound: " + (WGConfig.Crash_EnableMoundValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.Crash_EnableMoundValue = !WGConfig.Crash_EnableMoundValue;
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_MoundThreshold",
                Desc        = "CustomCrashSiteDepositer: Noise threshold for mound activation (higher = fewer mounds). Left/Right.",
                GetText     = () => "  - Crash Mound Threshold: " + WGConfig.Crash_MoundThresholdValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Crash_MoundThresholdValue + dir * 0.01f;
                    WGConfig.Crash_MoundThresholdValue = Clamp(next, -1f, 1f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_MoundHeightMul",
                Desc        = "CustomCrashSiteDepositer: Mound height strength. Higher = taller mound. Left/Right.",
                GetText     = () => "  - Crash Mound Height Mul: " + WGConfig.Crash_MoundHeightMulValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Crash_MoundHeightMulValue + dir * 10f;
                    WGConfig.Crash_MoundHeightMulValue = Clamp(next, 0f, 100000f);
                    WGConfig.Save();
                }
            });

            #region Extra Settings

            _rows.Add(new Row
            {
                Key         = "Crash_GroundPlane",
                Desc        = "CustomCrashSiteDepositer: Base ground plane used in crater/mound math (vanilla=66). Left/Right.",
                GetText     = () => "  - Crash Ground Plane: " + WGConfig.Crash_GroundPlaneValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_GroundPlaneValue + dir * 1;
                    WGConfig.Crash_GroundPlaneValue = Clamp(next, 0, 255);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_StartY",
                Desc        = "CustomCrashSiteDepositer: Start Y (inclusive) for the carve loop (vanilla=20). Left/Right.",
                GetText     = () => "  - Crash Start Y: " + WGConfig.Crash_StartYValue,
                OnLeftRight = dir =>
                {
                    int end = WGConfig.Crash_EndYExclusiveValue;
                    int next = WGConfig.Crash_StartYValue + dir * 1;
                    WGConfig.Crash_StartYValue = Clamp(next, 0, Math.Max(0, end - 1));
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_EndYExclusive",
                Desc        = "CustomCrashSiteDepositer: End Y (exclusive) for the carve loop (vanilla=126). Left/Right.",
                GetText     = () => "  - Crash End Y (Exclusive): " + WGConfig.Crash_EndYExclusiveValue,
                OnLeftRight = dir =>
                {
                    int start = WGConfig.Crash_StartYValue;
                    int next = WGConfig.Crash_EndYExclusiveValue + dir * 1;
                    WGConfig.Crash_EndYExclusiveValue = Clamp(next, start + 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_CarvePadding",
                Desc        = "CustomCrashSiteDepositer: Extra depth padding on the deep carve limit (vanilla=10). Left/Right.",
                GetText     = () => "  - Crash Carve Padding: " + WGConfig.Crash_CarvePaddingValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_CarvePaddingValue + dir * 1;
                    WGConfig.Crash_CarvePaddingValue = Clamp(next, 0, 1000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_ProtectBloodStone",
                Desc        = "CustomCrashSiteDepositer: When ON, avoids carving through BloodStone (vanilla behavior).",
                GetText     = () => "  - Protect BloodStone: " + (WGConfig.Crash_ProtectBloodStoneValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.Crash_ProtectBloodStoneValue = !WGConfig.Crash_ProtectBloodStoneValue;
                    WGConfig.Save();
                }
            });

            // ---- Slime inside SpaceRock ----

            _rows.Add(new Row
            {
                Key         = "Crash_EnableSlime",
                Desc        = "CustomCrashSiteDepositer: When ON, spawns Slime blocks inside SpaceRock interior.",
                GetText     = () => "  - Slime Ore: " + (WGConfig.Crash_EnableSlimeValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.Crash_EnableSlimeValue = !WGConfig.Crash_EnableSlimeValue;
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimePosOffset",
                Desc        = "CustomCrashSiteDepositer: Slime noise position offset added to x/y/z (vanilla=777). Left/Right.",
                GetText     = () => "  - Slime Pos Offset: " + WGConfig.Crash_SlimePosOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_SlimePosOffsetValue + dir * 10;
                    WGConfig.Crash_SlimePosOffsetValue = Clamp(next, -200000, 200000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeCoarseDiv",
                Desc        = "CustomCrashSiteDepositer: Coarse noise divisor (vanilla=2). Higher = larger blobs. Left/Right.",
                GetText     = () => "  - Slime Coarse Div: " + WGConfig.Crash_SlimeCoarseDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_SlimeCoarseDivValue + dir * 1;
                    WGConfig.Crash_SlimeCoarseDivValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeAdjustCenter",
                Desc        = "CustomCrashSiteDepositer: Center used in (n2-center)/div adjust (vanilla=128). Left/Right.",
                GetText     = () => "  - Slime Adjust Center: " + WGConfig.Crash_SlimeAdjustCenterValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_SlimeAdjustCenterValue + dir * 1;
                    WGConfig.Crash_SlimeAdjustCenterValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeAdjustDiv",
                Desc        = "CustomCrashSiteDepositer: Divisor used in (n2-center)/div adjust (vanilla=8). Left/Right.",
                GetText     = () => "  - Slime Adjust Div: " + WGConfig.Crash_SlimeAdjustDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_SlimeAdjustDivValue + dir * 1;
                    WGConfig.Crash_SlimeAdjustDivValue = Clamp(next, 1, 1024);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeThresholdBase",
                Desc        = "CustomCrashSiteDepositer: Base threshold for slime (vanilla=265). Lower = more slime. Left/Right.",
                GetText     = () => "  - Slime Threshold Base: " + WGConfig.Crash_SlimeThresholdBaseValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_SlimeThresholdBaseValue + dir * 1;
                    WGConfig.Crash_SlimeThresholdBaseValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeBlendToIntBlendMul",
                Desc        = "CustomCrashSiteDepositer: intblend = (int)(blender * mul) (vanilla=10). Left/Right.",
                GetText     = () => "  - Slime BlendToIntBlend Mul: " +
                    WGConfig.Crash_SlimeBlendToIntBlendMulValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Crash_SlimeBlendToIntBlendMulValue + dir * 0.5f;
                    WGConfig.Crash_SlimeBlendToIntBlendMulValue = Clamp(next, 0f, 100f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeThresholdBlendMul",
                Desc        = "CustomCrashSiteDepositer: thresh = base - (intblend * mul) (vanilla=1). Left/Right.",
                GetText     = () => "  - Slime Threshold Blend Mul: " +
                    WGConfig.Crash_SlimeThresholdBlendMulValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Crash_SlimeThresholdBlendMulValue + dir * 0.1f;
                    WGConfig.Crash_SlimeThresholdBlendMulValue = Clamp(next, -10f, 10f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Crash_SlimeTopPadding",
                Desc        = "CustomCrashSiteDepositer: Prevent slime near mound top (vanilla=3). Left/Right.",
                GetText     = () => "  - Slime Top Padding: " + WGConfig.Crash_SlimeTopPaddingValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Crash_SlimeTopPaddingValue + dir * 1;
                    WGConfig.Crash_SlimeTopPaddingValue = Clamp(next, 0, 64);
                    WGConfig.Save();
                }
            });
            #endregion

            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Ore Tuning"));

            #region [Ore]

            _rows.Add(new Row { Key = "Ore", Desc = "Toggle ore overlay.", GetText = () => "Ore: " + (WGConfig.EnableOreValue ? "ON" : "OFF"), OnSelect = () => { WGConfig.EnableOreValue = !WGConfig.EnableOreValue; WGConfig.Save(); } });

            _rows.Add(new Row
            {
                Key         = "Ore_DensityMul",
                Desc        = "CustomOreDepositer: Global ore density multiplier via blender shaping. Higher = more ore overall. Left/Right.",
                GetText     = () => "  - Ore Density Mul: " + WGConfig.Ore_BlendMulValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Ore_BlendMulValue + dir * 0.10f;
                    WGConfig.Ore_BlendMulValue = Clamp(next, -1000f, 1000f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_MaxY",
                Desc        = "CustomOreDepositer: Max Y processed by the ore pass. Higher = ore can appear higher. Left/Right.",
                GetText     = () => "  - Ore Max Y: " + WGConfig.Ore_MaxYValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_MaxYValue + dir * 4;
                    WGConfig.Ore_MaxYValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_CoalThresholdBase",
                Desc        = "CustomOreDepositer: Coal threshold base (lower = more coal). Left/Right.",
                GetText     = () => "  - Coal Threshold Base: " + WGConfig.Ore_CoalThresholdBaseValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_CoalThresholdBaseValue + dir * 1;
                    WGConfig.Ore_CoalThresholdBaseValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_CopperThresholdOffset",
                Desc        = "CustomOreDepositer: Copper threshold offset (higher = more copper). Left/Right.",
                GetText     = () => "  - Copper Threshold Offset: " + WGConfig.Ore_CopperThresholdOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_CopperThresholdOffsetValue + dir * 1;
                    WGConfig.Ore_CopperThresholdOffsetValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_IronThresholdBase",
                Desc        = "CustomOreDepositer: Iron threshold base (lower = more iron). Left/Right.",
                GetText     = () => "  - Iron Threshold Base: " + WGConfig.Ore_IronThresholdBaseValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_IronThresholdBaseValue + dir * 1;
                    WGConfig.Ore_IronThresholdBaseValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_GoldMaxY",
                Desc        = "CustomOreDepositer: Gold appears only below this Y. Higher = gold allowed higher. Left/Right.",
                GetText     = () => "  - Gold Max Y: " + WGConfig.Ore_GoldMaxYValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_GoldMaxYValue + dir * 2;
                    WGConfig.Ore_GoldMaxYValue = Clamp(next, 0, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_DiamondMaxY",
                Desc        = "CustomOreDepositer: Diamonds appear only below this Y. Higher = diamonds allowed higher. Left/Right.",
                GetText     = () => "  - Diamond Max Y: " + WGConfig.Ore_DiamondMaxYValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_DiamondMaxYValue + dir * 2;
                    WGConfig.Ore_DiamondMaxYValue = Clamp(next, 0, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_LootEnabled",
                Desc        = "CustomOreDepositer: Toggle loot/lucky loot blocks generation.",
                GetText     = () => "  - Loot Blocks: " + (WGConfig.Ore_LootEnabledValue ? "ON" : "OFF"),
                OnSelect = () =>
                {
                    WGConfig.Ore_LootEnabledValue = !WGConfig.Ore_LootEnabledValue;
                    WGConfig.Save();
                }
            });

            #region Extra Settings

            _rows.Add(new Row
            {
                Key         = "Ore_BlendRadius",
                Desc        = "Builder-side ore blender radius override. 0 = use WorldBlendRadius. Left/Right.",
                GetText     = () => "  - Ore Blend Radius: " + WGConfig.Ore_BlendRadiusValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_BlendRadiusValue + dir * 100;
                    WGConfig.Ore_BlendRadiusValue = Clamp(next, 0, 2_000_000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_BlendAdd",
                Desc        = "Builder-side ore blender additive bias. Higher = more ore overall. Left/Right.",
                GetText     = () => "  - Ore Blend Add: " + WGConfig.Ore_BlendAddValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Ore_BlendAddValue + dir * 0.10f;
                    WGConfig.Ore_BlendAddValue = Clamp(next, -1000f, 1000f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_BlendToIntBlendMul",
                Desc        = "CustomOreDepositer: intblend = (int)(blender * mul) (vanilla=10). Left/Right.",
                GetText     = () => "  - BlendToIntBlend Mul: " + WGConfig.Ore_BlendToIntBlendMulValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Ore_BlendToIntBlendMulValue + dir * 0.5f;
                    WGConfig.Ore_BlendToIntBlendMulValue = Clamp(next, 0f, 100f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_NoiseAdjustCenter",
                Desc        = "CustomOreDepositer: Center used in (n2-center)/div adjust (vanilla=128). Left/Right.",
                GetText     = () => "  - Noise Adjust Center: " + WGConfig.Ore_NoiseAdjustCenterValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_NoiseAdjustCenterValue + dir * 1;
                    WGConfig.Ore_NoiseAdjustCenterValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_NoiseAdjustDiv",
                Desc        = "CustomOreDepositer: Divisor used in (n2-center)/div adjust (vanilla=8). Left/Right.",
                GetText     = () => "  - Noise Adjust Div: " + WGConfig.Ore_NoiseAdjustDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_NoiseAdjustDivValue + dir * 1;
                    WGConfig.Ore_NoiseAdjustDivValue = Clamp(next, 1, 1024);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_CoalCoarseDiv",
                Desc        = "CustomOreDepositer: Coal/Copper coarse divisor (vanilla=4). Left/Right.",
                GetText     = () => "  - Coal Coarse Div: " + WGConfig.Ore_CoalCoarseDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_CoalCoarseDivValue + dir * 1;
                    WGConfig.Ore_CoalCoarseDivValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_IronOffset",
                Desc        = "CustomOreDepositer: Iron/Gold position offset added to x/y/z (vanilla=1000). Left/Right.",
                GetText     = () => "  - Iron Offset: " + WGConfig.Ore_IronOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_IronOffsetValue + dir * 10;
                    WGConfig.Ore_IronOffsetValue = Clamp(next, -200000, 200000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_IronCoarseDiv",
                Desc        = "CustomOreDepositer: Iron/Gold coarse divisor (vanilla=3). Left/Right.",
                GetText     = () => "  - Iron Coarse Div: " + WGConfig.Ore_IronCoarseDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_IronCoarseDivValue + dir * 1;
                    WGConfig.Ore_IronCoarseDivValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_GoldThresholdOffset",
                Desc        = "CustomOreDepositer: Gold threshold offset (vanilla=-9). Higher => more gold. Left/Right.",
                GetText     = () => "  - Gold Threshold Offset: " + WGConfig.Ore_GoldThresholdOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_GoldThresholdOffsetValue + dir * 1;
                    WGConfig.Ore_GoldThresholdOffsetValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_DeepPassMaxY",
                Desc        = "CustomOreDepositer: Max Y for deep pass (lava/diamond) (vanilla=50). Left/Right.",
                GetText     = () => "  - Deep Pass Max Y: " + WGConfig.Ore_DeepPassMaxYValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_DeepPassMaxYValue + dir * 2;
                    WGConfig.Ore_DeepPassMaxYValue = Clamp(next, 0, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_DiamondOffset",
                Desc        = "CustomOreDepositer: Diamond/Lava position offset added to x/y/z (vanilla=777). Left/Right.",
                GetText     = () => "  - Diamond Offset: " + WGConfig.Ore_DiamondOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_DiamondOffsetValue + dir * 10;
                    WGConfig.Ore_DiamondOffsetValue = Clamp(next, -200000, 200000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_DiamondCoarseDiv",
                Desc        = "CustomOreDepositer: Diamond/Lava coarse divisor (vanilla=2). Left/Right.",
                GetText     = () => "  - Diamond Coarse Div: " + WGConfig.Ore_DiamondCoarseDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_DiamondCoarseDivValue + dir * 1;
                    WGConfig.Ore_DiamondCoarseDivValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_LavaThresholdBase",
                Desc        = "CustomOreDepositer: Lava threshold base (vanilla=266). Lower => more lava. Left/Right.",
                GetText     = () => "  - Lava Threshold Base: " + WGConfig.Ore_LavaThresholdBaseValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_LavaThresholdBaseValue + dir * 1;
                    WGConfig.Ore_LavaThresholdBaseValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_DiamondThresholdOffset",
                Desc        = "CustomOreDepositer: Diamond threshold offset (vanilla=-11). Higher => more diamonds. Left/Right.",
                GetText     = () => "  - Diamond Threshold Offset: " + WGConfig.Ore_DiamondThresholdOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_DiamondThresholdOffsetValue + dir * 1;
                    WGConfig.Ore_DiamondThresholdOffsetValue = Clamp(next, -100000, 100000);
                    WGConfig.Save();
                }
            });

            // ---- Loot extras ----

            _rows.Add(new Row
            {
                Key      = "Ore_LootOnNonRockBlocks",
                Desc     = "CustomOreDepositer: When ON, also generates loot on sand/snow/bloodstone (vanilla behavior).",
                GetText  = () => "  - Loot On Non-Rock: " + (WGConfig.Ore_LootOnNonRockBlocksValue ? "ON" : "OFF"),
                OnSelect = () => { WGConfig.Ore_LootOnNonRockBlocksValue = !WGConfig.Ore_LootOnNonRockBlocksValue; WGConfig.Save(); }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_LootSandSnowMaxY",
                Desc        = "CustomOreDepositer: Above this Y, sand/snow will NOT get loot (vanilla=60). Left/Right.",
                GetText     = () => "  - Loot Sand/Snow Max Y: " + WGConfig.Ore_LootSandSnowMaxYValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_LootSandSnowMaxYValue + dir * 2;
                    WGConfig.Ore_LootSandSnowMaxYValue = Clamp(next, 0, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_LootOffset",
                Desc        = "CustomOreDepositer: Loot position offset added to x/y/z (vanilla=333). Left/Right.",
                GetText     = () => "  - Loot Offset: " + WGConfig.Ore_LootOffsetValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_LootOffsetValue + dir * 10;
                    WGConfig.Ore_LootOffsetValue = Clamp(next, -200000, 200000);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_LootCoarseDiv",
                Desc        = "CustomOreDepositer: Loot coarse divisor (vanilla=5). Left/Right.",
                GetText     = () => "  - Loot Coarse Div: " + WGConfig.Ore_LootCoarseDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_LootCoarseDivValue + dir * 1;
                    WGConfig.Ore_LootCoarseDivValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Ore_LootFineDiv",
                Desc        = "CustomOreDepositer: Loot fine divisor (vanilla=2). Left/Right.",
                GetText     = () => "  - Loot Fine Div: " + WGConfig.Ore_LootFineDivValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Ore_LootFineDivValue + dir * 1;
                    WGConfig.Ore_LootFineDivValue = Clamp(next, 1, 256);
                    WGConfig.Save();
                }
            });

            // Survival thresholds
            _rows.Add(new Row
            {
                Key         = "Ore_LootSurvivalMainThreshold",
                Desc        = "Survival: main loot threshold (vanilla=268). Lower => more loot. Left/Right.",
                GetText     = () => "  - Survival Main Thresh: " + WGConfig.Ore_LootSurvivalMainThresholdValue,
                OnLeftRight = dir => { WGConfig.Ore_LootSurvivalMainThresholdValue = Clamp(WGConfig.Ore_LootSurvivalMainThresholdValue + dir * 1, -100000, 100000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootSurvivalLuckyThreshold",
                Desc        = "Survival: lucky threshold (vanilla=249). Lower => more lucky loot. Left/Right.",
                GetText     = () => "  - Survival Lucky Thresh: " + WGConfig.Ore_LootSurvivalLuckyThresholdValue,
                OnLeftRight = dir => { WGConfig.Ore_LootSurvivalLuckyThresholdValue = Clamp(WGConfig.Ore_LootSurvivalLuckyThresholdValue + dir * 1, -100000, 100000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootSurvivalRegularThreshold",
                Desc        = "Survival: regular loot threshold (vanilla=145). Lower => more loot. Left/Right.",
                GetText     = () => "  - Survival Regular Thresh: " + WGConfig.Ore_LootSurvivalRegularThresholdValue,
                OnLeftRight = dir => { WGConfig.Ore_LootSurvivalRegularThresholdValue = Clamp(WGConfig.Ore_LootSurvivalRegularThresholdValue + dir * 1, -100000, 100000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootLuckyBandMinY",
                Desc        = "Survival: lucky band min Y (vanilla=55). Lucky allowed if y < min OR y >= maxStart. Left/Right.",
                GetText     = () => "  - Lucky Band Min Y: " + WGConfig.Ore_LootLuckyBandMinYValue,
                OnLeftRight = dir => { WGConfig.Ore_LootLuckyBandMinYValue = Clamp(WGConfig.Ore_LootLuckyBandMinYValue + dir * 1, 0, 256); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootLuckyBandMaxYStart",
                Desc        = "Survival: lucky band max-start Y (vanilla=100). Left/Right.",
                GetText     = () => "  - Lucky Band Max Start: " + WGConfig.Ore_LootLuckyBandMaxYStartValue,
                OnLeftRight = dir => { WGConfig.Ore_LootLuckyBandMaxYStartValue = Clamp(WGConfig.Ore_LootLuckyBandMaxYStartValue + dir * 1, 0, 256); WGConfig.Save(); }
            });

            // Scavenger thresholds
            _rows.Add(new Row
            {
                Key         = "Ore_LootScavengerTargetMod",
                Desc        = "Scavenger: extra mod applied for sand/snow above LootSandSnowMaxY (vanilla=1). Left/Right.",
                GetText     = () => "  - Scavenger Target Mod: " + WGConfig.Ore_LootScavengerTargetModValue,
                OnLeftRight = dir => { WGConfig.Ore_LootScavengerTargetModValue = Clamp(WGConfig.Ore_LootScavengerTargetModValue + dir * 1, -1000, 1000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootScavengerMainThreshold",
                Desc        = "Scavenger: main loot threshold (vanilla=267). Lower => more loot. Left/Right.",
                GetText     = () => "  - Scavenger Main Thresh: " + WGConfig.Ore_LootScavengerMainThresholdValue,
                OnLeftRight = dir => { WGConfig.Ore_LootScavengerMainThresholdValue = Clamp(WGConfig.Ore_LootScavengerMainThresholdValue + dir * 1, -100000, 100000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootScavengerLuckyThreshold",
                Desc        = "Scavenger: lucky threshold (vanilla=250). Lower => more lucky. Left/Right.",
                GetText     = () => "  - Scavenger Lucky Thresh: " + WGConfig.Ore_LootScavengerLuckyThresholdValue,
                OnLeftRight = dir => { WGConfig.Ore_LootScavengerLuckyThresholdValue = Clamp(WGConfig.Ore_LootScavengerLuckyThresholdValue + dir * 1, -100000, 100000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootScavengerLuckyExtraPerMod",
                Desc        = "Scavenger: lucky threshold increases by mod*extra (vanilla=3). Left/Right.",
                GetText     = () => "  - Lucky Extra Per Mod: " + WGConfig.Ore_LootScavengerLuckyExtraPerModValue,
                OnLeftRight = dir => { WGConfig.Ore_LootScavengerLuckyExtraPerModValue = Clamp(WGConfig.Ore_LootScavengerLuckyExtraPerModValue + dir * 1, -1000, 1000); WGConfig.Save(); }
            });
            _rows.Add(new Row
            {
                Key         = "Ore_LootScavengerRegularThreshold",
                Desc        = "Scavenger: regular loot threshold (vanilla=165). Lower => more loot. Left/Right.",
                GetText     = () => "  - Scavenger Regular Thresh: " + WGConfig.Ore_LootScavengerRegularThresholdValue,
                OnLeftRight = dir => { WGConfig.Ore_LootScavengerRegularThresholdValue = Clamp(WGConfig.Ore_LootScavengerRegularThresholdValue + dir * 1, -100000, 100000); WGConfig.Save(); }
            });
            #endregion

            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Trees Tuning"));

            #region [Trees]

            _rows.Add(new Row
            {
                Key         = "Trees",
                Desc        = "Toggle the vanilla tree placement pass (runs after chunk generation).",
                GetText     = () => "Trees: " + (WGConfig.EnableTreesValue ? "ON" : "OFF"),
                OnSelect = () => { WGConfig.EnableTreesValue = !WGConfig.EnableTreesValue; WGConfig.Save(); }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_Scale",
                Desc        = "CustomTreeDepositer: Noise frequency (0.01..4). Higher = more variation and more frequent changes. Left/Right.",
                GetText     = () => "  - Tree Scale: " + WGConfig.Tree_ScaleValue.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Tree_ScaleValue + dir * 0.02f;
                    WGConfig.Tree_ScaleValue = Clamp(next, 0.01f, 4f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_Threshold",
                Desc        = "CustomTreeDepositer: Noise threshold (0..1). Higher = fewer trees. Left/Right.",
                GetText     = () => "  - Tree Threshold: " + WGConfig.Tree_ThresholdValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Tree_ThresholdValue + dir * 0.02f;
                    WGConfig.Tree_ThresholdValue = Clamp(next, 0f, 1f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_BaseTrunkHeight",
                Desc        = "CustomTreeDepositer: Base trunk height in blocks (1..64). Left/Right.",
                GetText     = () => "  - Tree Base Trunk Height: " + WGConfig.Tree_BaseTrunkHeightValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Tree_BaseTrunkHeightValue + dir * 1;
                    WGConfig.Tree_BaseTrunkHeightValue = Clamp(next, 1, 64);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_HeightVarMul",
                Desc        = "CustomTreeDepositer: How strongly (noise - threshold) adds trunk height (0..64). Higher = taller trees. Left/Right.",
                GetText     = () => "  - Tree Height Var Mul: " + WGConfig.Tree_HeightVarMulValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Tree_HeightVarMulValue + dir * 0.5f;
                    WGConfig.Tree_HeightVarMulValue = Clamp(next, 0f, 64f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_LeafRadius",
                Desc        = "CustomTreeDepositer: Leaf radius (0..7). Also shrinks the seam-safe area inside each 16x16 chunk. Left/Right.",
                GetText     = () => "  - Tree Leaf Radius: " + WGConfig.Tree_LeafRadiusValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Tree_LeafRadiusValue + dir * 1;
                    WGConfig.Tree_LeafRadiusValue = Clamp(next, 0, 17);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_LeafNoiseScale",
                Desc        = "CustomTreeDepositer: LeafNoiseScale (0.01..4). Multiplies leaf sample position before Perlin noise. Left/Right.",
                GetText     = () => "  - Tree Leaf Noise Scale: " + WGConfig.Tree_LeafNoiseScaleValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Tree_LeafNoiseScaleValue + dir * 0.05f;
                    WGConfig.Tree_LeafNoiseScaleValue = Clamp(next, 0.01f, 4f);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_LeafCutoff",
                Desc        = "CustomTreeDepositer: LeafCutoff (-1..2). Leaves placed when (noise + distBlender) > cutoff. Higher = fewer leaves. Left/Right.",
                GetText     = () => "  - Tree Leaf Cutoff: " + WGConfig.Tree_LeafCutoffValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                OnLeftRight = dir =>
                {
                    float next = WGConfig.Tree_LeafCutoffValue + dir * 0.05f;
                    WGConfig.Tree_LeafCutoffValue = Clamp(next, -1f, 2f);
                    WGConfig.Save();
                }
            });

            #region Extra Settings

            _rows.Add(new Row
            {
                Key         = "Tree_GroundScanStartY",
                Desc        = "CustomTreeDepositer: Start Y for downward grass scan (vanilla=124). Left/Right.",
                GetText     = () => "  - Ground Scan Start Y: " + WGConfig.Tree_GroundScanStartYValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Tree_GroundScanStartYValue + dir * 1;
                    WGConfig.Tree_GroundScanStartYValue = Clamp(next, 0, 127);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_GroundScanMinY",
                Desc        = "CustomTreeDepositer: Min Y for grass scan loop (vanilla=0). Left/Right.",
                GetText     = () => "  - Ground Scan Min Y: " + WGConfig.Tree_GroundScanMinYValue,
                OnLeftRight = dir =>
                {
                    int start = WGConfig.Tree_GroundScanStartYValue;
                    int next = WGConfig.Tree_GroundScanMinYValue + dir * 1;
                    WGConfig.Tree_GroundScanMinYValue = Clamp(next, 0, start);
                    WGConfig.Save();
                }
            });

            _rows.Add(new Row
            {
                Key         = "Tree_MinGroundHeight",
                Desc        = "CustomTreeDepositer: If found ground height <= this, tree aborts (vanilla=1). Left/Right.",
                GetText     = () => "  - Min Ground Height: " + WGConfig.Tree_MinGroundHeightValue,
                OnLeftRight = dir =>
                {
                    int next = WGConfig.Tree_MinGroundHeightValue + dir * 1;
                    WGConfig.Tree_MinGroundHeightValue = Clamp(next, 0, 127);
                    WGConfig.Save();
                }
            });
            #endregion

            #endregion

            #endregion

            _rows.Add(Spacer());
            _rows.Add(Header("Menu Actions"));

            #region WorldGenPlus Menu Options

            _rows.Add(new Row
            {
                Key      = "Save",
                Desc     = "Write INI to disk.",
                GetText  = () => "Save",
                OnSelect = () => WGConfig.Save()
            });

            _rows.Add(new Row
            {
                Key      = "Reset",
                Desc     = "Reset INI values back to vanilla defaults.",
                GetText  = () => "Reset to Vanilla Defaults",
                OnSelect = () =>
                {
                    WGConfig.ResetToVanilla();
                    _editingSeed = false;
                    _seedEditBuffer = "";
                    BuildRows();
                    RebuildMenuItems();
                }
            });

            _rows.Add(new Row
            {
                Key      = "Back",
                Desc     = "Return to 'Choose A Server' Screen.",
                GetText  = () => "Back",
                OnSelect = () => PopMe()
            });
            #endregion
        }
        #endregion

        #region UI Metrics / Hit Testing

        /// <summary>
        /// Returns the width (pixels) of the per-row arrow buttons, scaled to resolution.
        /// </summary>
        private int GetArrowButtonW()
        {
            float sy = Screen.Adjuster.ScaleFactor.Y;
            return Math.Max(14, (int)(22f * sy));
        }

        /// <summary>
        /// Determines whether a click falls within the left/right arrow button zones for an adjustable row.
        /// </summary>
        private bool TryGetArrowDirForClick(Rectangle rowRect, Point mp, out int dir)
        {
            dir = 0;

            int w = GetArrowButtonW();
            // Right arrow button is the last w pixels.
            Rectangle rightBtn = new Rectangle(rowRect.Right - w, rowRect.Y, w, rowRect.Height);
            // Left arrow button is the w pixels right before that.
            Rectangle leftBtn = new Rectangle(rowRect.Right - (w * 2), rowRect.Y, w, rowRect.Height);

            if (rightBtn.Contains(mp)) { dir = +1; return true; }
            if (leftBtn.Contains(mp))  { dir = -1; return true; }

            return false;
        }

        /// <summary>
        /// Returns the vertical step (pixels) for each row in the list, matching MenuScreen's font sizing + LineSpacing.
        /// </summary>
        private int GetRowStepPx()
        {
            float sy = Screen.Adjuster.ScaleFactor.Y;

            // MenuScreen row layout uses the font passed to base(...). Pass _smallFont.
            int baseH = (int)(_game._medFont.LineSpacing * sy);

            int extra = (int)(LineSpacing.GetValueOrDefault(0) * sy);

            return Math.Max(1, baseH + extra);
        }
        #endregion

        #region Layout / Scrolling Calculations

        /// <summary>
        /// Computes the panel, list viewport, and description box rectangles for the current resolution.
        /// </summary>
        private void ComputeLayout(out Rectangle panel, out Rectangle listViewport, out Rectangle descRect)
        {
            Rectangle sr = Screen.Adjuster.ScreenRect;
            float sy     = Screen.Adjuster.ScaleFactor.Y;

            int margin = (int)(20f * sy);
            int pad    = (int)(20f * sy);

            // Header space for title + warning line.
            // Header sizing based on actual fonts.
            int headerPadTop = (int)(16f * sy);
            int headerGap    = (int)(6f * sy);
            int headerPadBot = (int)(12f * sy);

            int titleH  = (int)(_game._largeFont.LineSpacing * sy);
            int noteH   = (int)(_game._medFont.LineSpacing * sy);

            int headerH = headerPadTop + titleH + headerGap + noteH + headerPadBot;

            // Description box sizing (like your original -84 / 64 but scaled).
            int descGap = (int)(20f * sy);
            int descH   = (int)(64f * sy);

            // Fit panel to screen with margins.
            panel = new Rectangle(
                sr.X + margin,
                sr.Y + margin,
                Math.Max(1, sr.Width - margin * 2),
                Math.Max(1, sr.Height - margin * 2));

            // Desc box at bottom.
            descRect = new Rectangle(
                panel.X + pad,
                panel.Bottom - descGap - descH,
                Math.Max(1, panel.Width - pad * 2),
                Math.Max(1, descH));

            // List viewport between header and desc.
            int listTop = panel.Y + headerH;
            int listBottom = descRect.Y - pad;

            listViewport = new Rectangle(
                panel.X + pad,
                listTop,
                Math.Max(1, panel.Width - pad * 2),
                Math.Max(1, listBottom - listTop));
        }

        /// <summary>
        /// Computes the maximum scroll offset for the current list content vs. viewport height.
        /// </summary>
        private int GetMaxScrollPx(int listViewportHeight)
        {
            int step     = GetRowStepPx();
            int count    = (MenuItems != null) ? MenuItems.Count : 0;
            int contentH = count * step;
            return Math.Max(0, contentH - listViewportHeight);
        }

        /// <summary>Clamps current scroll offset to [0..maxScroll].</summary>
        private void ClampScroll(int maxScroll)
        {
            if (_scrollPx < 0) _scrollPx = 0;
            if (_scrollPx > maxScroll) _scrollPx = maxScroll;
        }

        /// <summary>
        /// Ensures the current SelectedIndex row is within the visible viewport (used for keyboard/controller navigation).
        /// </summary>
        private void EnsureSelectedVisible(int listViewportHeight)
        {
            int step      = GetRowStepPx();
            int maxScroll = GetMaxScrollPx(listViewportHeight);
            ClampScroll(maxScroll);

            int idx = SelectedIndex;
            if (idx < 0) return;

            int selTop  = idx * step;
            int selBot  = selTop + step;

            int viewTop = _scrollPx;
            int viewBot = _scrollPx + listViewportHeight;

            if (selTop < viewTop)
                _scrollPx = selTop;
            else if (selBot > viewBot)
                _scrollPx = selBot - listViewportHeight;

            ClampScroll(maxScroll);
        }
        #endregion

        #region Drawing (Scrollbar / List)

        /// <summary>
        /// Draws the scrollbar track + thumb using _white texture.
        /// </summary>
        private void DrawScrollbar(SpriteBatch sb /* int maxScrollPx */)
        {
            // Draw even if maxScroll==0 (shows "disabled" bar).
            // Track: Darker so it recedes.
            var trackCol = new Color(0, 0, 0, 140);

            // Thumb: bright, with hover + dragging boost.
            bool hoveringThumb = _scrollThumbRect.Contains(GetMousePoint());

            var thumbCol =
                _draggingThumb ? new Color(255, 255, 255, 230) :
                hoveringThumb  ? new Color(255, 255, 255, 190) :
                                 new Color(255, 255, 255, 150);

            sb.Draw(_white, _scrollTrackRect, trackCol);
            sb.Draw(_white, _scrollThumbRect, thumbCol);

            // Small border hint.
            sb.Draw(_white, new Rectangle(_scrollTrackRect.X, _scrollTrackRect.Y, _scrollTrackRect.Width, 2), new Color(0, 0, 0, 80));
            sb.Draw(_white, new Rectangle(_scrollTrackRect.X, _scrollTrackRect.Bottom - 2, _scrollTrackRect.Width, 2), new Color(0, 0, 0, 80));
        }

        /// <summary>
        /// Draws the scrollable menu list inside the viewport.
        /// Notes:
        /// - This is a custom renderer (not base MenuScreen drawing).
        /// - Uses _mouseOverListForDraw + _hoverIndex to show mouse hover highlight without changing selection.
        /// - Draws optional < > arrow glyphs for adjustable rows.
        /// </summary>
        private void DrawMenuList(SpriteBatch sb, Rectangle listViewport, int listDrawWidth)
        {
            float sy     = Screen.Adjuster.ScaleFactor.Y;

            int step     = GetRowStepPx();
            int xPad     = (int)(8f * sy);
            int yPad     = (int)(2f * sy);

            int count    = MenuItems?.Count ?? 0;

            int startY   = listViewport.Y - _scrollPx;

            int arrowW   = GetArrowButtonW();
            int arrowPad = (int)(6f * sy);

            for (int i = 0; i < count; i++)
            {
                int y  = startY + i * step;
                int y2 = y + step;

                if (y2 < listViewport.Y) continue;
                if (y > listViewport.Bottom) break;

                var rowRect = new Rectangle(listViewport.X, y, listDrawWidth, step);

                bool selected = (i == SelectedIndex);
                bool hovered = (_mouseOverListForDraw && i == _hoverIndex);

                if (hovered)
                    sb.Draw(_white, rowRect, new Color(255, 255, 255, 15));   // subtle hover
                if (selected)
                    sb.Draw(_white, rowRect, new Color(255, 255, 255, 25));   // actual selection

                string text = MenuItems[i]?.Text ?? "";

                sb.DrawOutlinedText(
                    _game._medFont,
                    text,
                    new Vector2(rowRect.X + xPad, rowRect.Y + yPad),
                    selected ? Color.Yellow : Color.White,
                    Color.Black,
                    2,
                    sy,
                    0f,
                    Vector2.Zero);

                // Draw < > for adjustable rows.
                if (MenuItems[i]?.Tag is Row r && r.OnLeftRight != null)
                {
                    // Button rects at the far right of the row.
                    Rectangle rightBtn = new Rectangle(rowRect.Right - arrowW, rowRect.Y, arrowW, rowRect.Height);
                    Rectangle leftBtn = new Rectangle(rowRect.Right - (arrowW * 2), rowRect.Y, arrowW, rowRect.Height);

                    // subtle hover highlight.
                    Point mp = GetMousePoint();
                    if (leftBtn.Contains(mp)) sb.Draw(_white, leftBtn, new Color(255, 255, 255, 25));
                    if (rightBtn.Contains(mp)) sb.Draw(_white, rightBtn, new Color(255, 255, 255, 25));

                    // Glyphs.
                    sb.DrawOutlinedText(_game._medFont, "<",
                        new Vector2(leftBtn.X + arrowPad, rowRect.Y + yPad),
                        Color.White, Color.Black, 2, sy, 0f, Vector2.Zero);

                    sb.DrawOutlinedText(_game._medFont, ">",
                        new Vector2(rightBtn.X + arrowPad, rowRect.Y + yPad),
                        Color.White, Color.Black, 2, sy, 0f, Vector2.Zero);
                }
            }
        }
        #endregion

        #region Row Model (Menu Row Abstraction)

        /// <summary>
        /// Represents a single menu row:
        /// - Key:         Stable identifier.
        /// - Desc:        Description shown in bottom panel.
        /// - OnSelect:    Action when pressed/activated.
        /// - OnLeftRight: Action when adjusted (mouse/keyboard/controller).
        /// - GetText:     Live text supplier (allows value refresh without rebuilding rows).
        /// - Selectable:  False for headers/spacers.
        /// </summary>
        private sealed class Row
        {
            public string       Key;
            public string       Desc;
            public Action       OnSelect;
            public Action<int>  OnLeftRight;       // -1 left, +1 right.
            public Func<string> GetText;
            public bool         Selectable = true; // Allows headers/spacers to exist but never receive focus/selection.
        }

        /// <summary>Creates a non-selectable blank spacer row.</summary>
        private static Row Spacer(string key = null)
        {
            return new Row
            {
                Key         = key ?? ("_spacer_" + Guid.NewGuid().ToString("N")),
                Desc        = "",
                GetText     = () => "",
                OnSelect    = null,
                OnLeftRight = null,
                Selectable  = false
            };
        }

        /// <summary>Creates a non-selectable header row with a consistent "== Title ==" style.</summary>
        private static Row Header(string title, string key = null)
        {
            string text = "== " + title + " ==";

            return new Row
            {
                Key         = key ?? ("_header_" + title),
                Desc        = "",
                GetText     = () => text,
                OnSelect    = null,
                OnLeftRight = null,
                Selectable  = false
            };
        }
        #endregion

        #region Selection Rules (Skip Headers/Spacers)

        /// <summary>Returns true if a MenuItemElement is selectable (headers/spacers are not).</summary>
        private static bool IsSelectable(MenuItemElement mi)
        {
            if (mi?.Tag is Row r) return r.Selectable;
            return true;
        }

        /// <summary>
        /// Finds the next selectable index starting from <paramref name="start"/> walking by <paramref name="dir"/>.
        /// </summary>
        private int FindNextSelectableIndex(int start, int dir)
        {
            int count = MenuItems?.Count ?? 0;
            if (count <= 0) return -1;

            if (start < 0) start = 0;
            if (start >= count) start = count - 1;

            for (int i = start; i >= 0 && i < count; i += dir)
            {
                if (IsSelectable(MenuItems[i]))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Ensures SelectedIndex points to a selectable row (skips headers/spacers).
        /// </summary>
        private void SnapSelectionToSelectable(int preferredDir)
        {
            int count = MenuItems?.Count ?? 0;
            if (count <= 0)
            {
                SelectedIndex = -1;
                return;
            }

            if (SelectedIndex < 0) SelectedIndex = 0;
            if (SelectedIndex >= count) SelectedIndex = count - 1;

            if (IsSelectable(SelectedMenuItem))
                return;

            // Try continuing in the direction we were moving first
            int dir = (preferredDir == 0) ? +1 : Math.Sign(preferredDir);

            int idx          = FindNextSelectableIndex(SelectedIndex, dir);
            if (idx < 0) idx = FindNextSelectableIndex(SelectedIndex, -dir);
            if (idx < 0) idx = FindNextSelectableIndex(0, +1);

            if (idx >= 0)
            {
                SelectedIndex = idx;
                OnMenuItemFocus(MenuItems[idx]);
            }
        }
        #endregion

        #region Construction / Lifecycle

        /// <summary>
        /// Creates the config screen, loads WGConfig, and builds the initial row list.
        /// </summary>
        public WorldGenPlusConfigScreen(CastleMinerZGame game)
            : base(game._smallFont, false)
        {
            _game = game;

            HorizontalAlignment = HorizontalAlignmentTypes.Left;
            VerticalAlignment = VerticalAlignmentTypes.Top;
            LineSpacing = 8;

            // Layout is computed dynamically per-frame in OnDraw.
            DrawArea = null;

            WGConfig.Load(); // Safe repeatedly.

            BuildRows();
            RebuildMenuItems();
        }

        /// <summary>
        /// Called when the screen is pushed onto the screen stack.
        /// Ensures our 1x1 white texture exists for primitive drawing.
        /// </summary>
        public override void OnPushed()
        {
            base.OnPushed();

            if (_white == null)
            {
                _white = new Texture2D(_game.GraphicsDevice, 1, 1);
                _white.SetData(new[] { Color.White });
            }
        }
        #endregion

        #region Focus / Selection Overrides

        /// <summary>
        /// Updates the description panel text when focus changes.
        /// </summary>
        protected override void OnMenuItemFocus(MenuItemElement selectedControl)
        {
            base.OnMenuItemFocus(selectedControl);

            if (selectedControl?.Tag is Row r)
                _focusDesc = r.Desc ?? "";
            else
                _focusDesc = "";
        }

        /// <summary>
        /// Invokes row selection action (OnSelect) and refreshes live texts.
        /// </summary>
        protected override void OnMenuItemSelected(MenuItemElement selectedControl)
        {
            base.OnMenuItemSelected(selectedControl);

            if (selectedControl?.Tag is Row r && r.OnSelect != null)
            {
                r.OnSelect();
                RefreshTexts();
            }
        }
        #endregion

        #region Character Input (Seed Edit)

        /// <summary>
        /// Intercepts character input while editing seed override.
        /// </summary>
        protected override bool OnChar(GameTime gameTime, char c)
        {
            if (!_editingSeed) return base.OnChar(gameTime, c);

            // Allow digits and '-' only.
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                _seedEditBuffer += c;
                RefreshTexts();
            }

            // Swallow.
            return false;
        }
        #endregion

        #region Player Input (Mouse + Keyboard + Controller)

        /// <summary>
        /// Main input loop.
        /// Notes:
        /// - When editing seed: consumes Back/Enter/Escape and prevents menu navigation.
        /// - Custom mouse handling for: scrollbar drag/click, mouse wheel scroll, hover highlight, per-row arrow clicking.
        /// - Calls base.OnPlayerInput once for standard up/down navigation + accept/back behaviors.
        /// </summary>
        protected override bool OnPlayerInput(InputManager input, GameController controller, KeyboardInput chatpad, GameTime gameTime)
        {
            // If the game window has no focus return no input.
            if (!CastleMinerZGame.Instance.IsActive) return false;

            // If editing seed, hijack a few keys.
            if (_editingSeed)
            {
                if (input.Keyboard.WasKeyPressed(Keys.Back) && _seedEditBuffer.Length > 0)
                {
                    _seedEditBuffer = _seedEditBuffer.Substring(0, _seedEditBuffer.Length - 1);
                    RefreshTexts();
                }

                if (input.Keyboard.WasKeyPressed(Keys.Enter))
                {
                    CommitSeedBuffer();
                    _editingSeed = false;
                    RefreshTexts();
                }

                if (input.Keyboard.WasKeyPressed(Keys.Escape))
                {
                    _editingSeed = false;
                    RefreshTexts();
                }

                return false;
            }

            // Compute layout + scroll limits.
            ComputeLayout(out _, out var listVp, out _);
            int maxScroll = GetMaxScrollPx(listVp.Height);
            ClampScroll(maxScroll);

            // Update scrollbar rects for hit-testing this frame.
            UpdateScrollbarRects(listVp, maxScroll);

            // Mouse state.
            Point mp = GetMousePoint();

            var ms = Mouse.GetState();

            bool leftDown        = ms.LeftButton == ButtonState.Pressed;
            bool leftPressed     = leftDown && !_mouseLeftWasDown;
            bool leftReleased    = !leftDown && _mouseLeftWasDown;
            _mouseLeftWasDown    = leftDown;

            bool rightDown       = ms.RightButton == ButtonState.Pressed;
            bool rightPressed    = rightDown && !_mouseRightWasDown;
            _mouseRightWasDown   = rightDown;

            bool mouseOverList   = listVp.Contains(mp);
            bool mouseOverScroll = _scrollTrackRect.Contains(mp);

            // --- Scrollbar drag ---
            if (_draggingThumb)
            {
                if (leftDown)
                {
                    SetScrollFromThumbTop(mp.Y - _dragThumbOffsetY, maxScroll);
                    ClampScroll(maxScroll);
                    return false; // Consume while dragging.
                }

                if (leftReleased)
                {
                    _draggingThumb = false;
                    return false;
                }
            }

            // --- Scrollbar click (thumb/track) ---
            if (leftPressed && mouseOverScroll)
            {
                if (_scrollThumbRect.Contains(mp))
                {
                    _draggingThumb = true;
                    _dragThumbOffsetY = mp.Y - _scrollThumbRect.Y;
                    return false;
                }

                // Click track: page up/down.
                if (mp.Y < _scrollThumbRect.Y)
                    _scrollPx -= listVp.Height;
                else if (mp.Y > _scrollThumbRect.Bottom)
                    _scrollPx += listVp.Height;

                ClampScroll(maxScroll);
                return false;
            }

            // --- Mouse wheel scroll (only when over list or scrollbar) ---
            int dWheel = input.Mouse.DeltaWheel;
            if (dWheel != 0 && (mouseOverList || mouseOverScroll))
            {
                int step = GetRowStepPx();

                // tune speed: 3 rows per notch feels better on long menus.
                int delta = Math.Sign(dWheel) * step * 3;

                // Wheel up should scroll up (toward earlier items).
                _scrollPx -= delta;

                ClampScroll(maxScroll);
                return false;
            }

            // --- List click selection/activation (we draw the list ourselves, so we must click-test ourselves) ---
            int listDrawWidth = Math.Max(1, listVp.Width - _scrollTrackRect.Width - 6);
            Rectangle listClickRect = new Rectangle(listVp.X, listVp.Y, listDrawWidth, listVp.Height);

            _mouseOverListForDraw = listClickRect.Contains(mp);

            if (_mouseOverListForDraw)
            {
                int step = GetRowStepPx();
                int count = MenuItems?.Count ?? 0;

                if (count > 0)
                {
                    int idx = (_scrollPx + (mp.Y - listVp.Y)) / step;
                    if (idx < 0) idx = 0;
                    if (idx >= count) idx = count - 1;

                    var mi = MenuItems[idx];

                    // ---- HOVER (no snapping) ----
                    if (mi?.Tag is Row hr && hr.Selectable)
                    {
                        _hoverIndex = idx;
                        _focusDesc = hr.Desc ?? "";
                    }
                    else
                    {
                        _hoverIndex = -1; // Hovering header/spacer => nothing highlighted.
                        _focusDesc = "";
                    }

                    // ---- CLICK (commit selection only if selectable) ----
                    if ((leftPressed || rightPressed) && mi?.Tag is Row r)
                    {
                        if (!r.Selectable)
                            return false; // Click on header/spacer does nothing.

                        if (SelectedIndex != idx)
                        {
                            SelectedIndex = idx;
                            OnMenuItemFocus(mi);
                        }

                        if (r.OnLeftRight != null)
                        {
                            var rowRect = new Rectangle(
                                listVp.X,
                                listVp.Y - _scrollPx + idx * step,
                                listDrawWidth,
                                step);

                            if (leftPressed && TryGetArrowDirForClick(rowRect, mp, out int dir))
                            {
                                r.OnLeftRight(dir);
                                RefreshTexts();
                                return false;
                            }

                            if (rightPressed)
                            {
                                r.OnLeftRight(-1);
                                RefreshTexts();
                                return false;
                            }

                            if (leftPressed)
                            {
                                r.OnLeftRight(+1);
                                RefreshTexts();
                                return false;
                            }
                        }

                        if (leftPressed)
                        {
                            OnMenuItemSelected(mi);
                            return false;
                        }
                    }
                }
            }
            else
            {
                // Mouse left the list -> no hover highlight, restore desc from actual selection.
                _hoverIndex = -1;
                _focusDesc = (SelectedMenuItem?.Tag as Row)?.Desc ?? "";
            }

            // Left/Right adjust current row (keyboard/controller)
            // Hold-to-repeat Left/Right (keyboard), plus controller single-tap.
            if (HandleLeftRightHoldRepeat(gameTime, controller))
                return false;

            // Track whether user navigated via keys (so we only auto-scroll in those cases).
            bool navKeys =
                controller.PressedDPad.Up || controller.PressedDPad.Down ||
                input.Keyboard.WasKeyPressed(Keys.Up) || input.Keyboard.WasKeyPressed(Keys.Down) ||
                input.Keyboard.WasKeyPressed(Keys.PageUp) || input.Keyboard.WasKeyPressed(Keys.PageDown);

            int oldIndex = SelectedIndex;

            // Let base handle selection movement / accept button, etc. (call ONCE).
            base.OnPlayerInput(input, controller, chatpad, gameTime);

            int dir2;
            if (SelectedIndex > oldIndex) dir2 = +1;
            else if (SelectedIndex < oldIndex) dir2 = -1;
            else dir2 = +1;

            SnapSelectionToSelectable(dir2);

            // Auto-scroll ONLY for keyboard/controller navigation (prevents hover treadmill).
            if (navKeys && SelectedIndex != oldIndex)
            {
                EnsureSelectedVisible(listVp.Height);
            }

            // Always consume so nothing under this screen can react.
            return false;
        }
        #endregion

        #region Biome Choice Cache (Vanilla + Custom)

        // Cache merged choices so we don't rebuild every click.
        private static int      _mergedChoicesVer = -1;
        private static string[] _mergedChoices;

        /// <summary>
        /// Returns vanilla biome choices merged with discovered custom biome type names.
        /// Notes:
        /// - Uses CustomBiomeRegistry.Version to avoid rebuilding when nothing changed.
        /// </summary>
        private static string[] GetBiomeChoices()
        {
            // Ensure we've scanned custom biome folder.
            CustomBiomeRegistry.Refresh();

            int ver = CustomBiomeRegistry.Version;
            if (_mergedChoices != null && _mergedChoicesVer == ver)
                return _mergedChoices;

            var list = new List<string>(VanillaBiomeChoices.Length + 16);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < VanillaBiomeChoices.Length; i++)
                if (seen.Add(VanillaBiomeChoices[i]))
                    list.Add(VanillaBiomeChoices[i]);

            var custom = CustomBiomeRegistry.GetCustomBiomeTypeNames();
            for (int i = 0; i < custom.Length; i++)
                if (seen.Add(custom[i]))
                    list.Add(custom[i]);

            _mergedChoices = list.ToArray();
            _mergedChoicesVer = ver;
            return _mergedChoices;
        }

        /// <summary>
        /// Built-in vanilla biome type names (plus a few special/test biomes).
        /// </summary>
        private static readonly string[] VanillaBiomeChoices = new[]
        {
            // Normal biomes.
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.CaveBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",

            // Hell biomes.
            "DNA.CastleMinerZ.Terrain.WorldBuilders.HellCeilingBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",

            // Unused special/test/unusual biomes.
            "DNA.CastleMinerZ.Terrain.WorldBuilders.OriginBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.CostalBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.FlatLandBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.TestBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.TreeTestBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.OceanBiome",

            // Depositors.
            // "DNA.CastleMinerZ.Terrain.WorldBuilders.OreDepositer",
            // "DNA.CastleMinerZ.Terrain.WorldBuilders.BedrockDepositer",
            // "DNA.CastleMinerZ.Terrain.WorldBuilders.CrashSiteDepositer",
            // "DNA.CastleMinerZ.Terrain.WorldBuilders.TreeDepositer",
        };
        #endregion

        #region Helpers

        #region Hold-To-Repeat Helpers

        /// <summary>Resets the current Left/Right hold-to-repeat state.</summary>
        private void ResetLeftRightHold()
        {
            _lrHoldDir       = 0;
            _lrHoldRow       = null;
            _lrHeldSec       = 0f;
            _lrNextRepeatSec = 0f;
        }

        /// <summary>
        /// Returns the repeat interval (seconds) based on how long the key has been held.
        /// Notes:
        /// - Starts slower, then accelerates to allow fast tuning on large numeric ranges.
        /// </summary>
        private float GetLeftRightRepeatInterval(float heldSeconds)
        {
            // Tune to taste
            if (heldSeconds < 0.75f) return 0.12f;
            if (heldSeconds < 1.50f) return 0.06f;
            return 0.03f;
        }

        /// <summary>
        /// Handles Left/Right adjustments for the currently selected row.
        ///
        /// Behavior:
        /// - Keyboard: true "hold" repeat using Keyboard.GetState() (not just WasKeyPressed).
        /// - Controller: still allows DPad single-tap behavior when keyboard is idle.
        /// - Only applies to rows that expose OnLeftRight.
        /// </summary>
        private bool HandleLeftRightHoldRepeat(GameTime gameTime, GameController controller)
        {
            // Only repeat for the currently selected item.
            var item = SelectedMenuItem;
            if (!(item?.Tag is Row r) || r.OnLeftRight == null)
            {
                ResetLeftRightHold();
                return false;
            }

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Keyboard hold state (real hold, not WasKeyPressed).
            var ks         = Keyboard.GetState();
            bool leftDown  = ks.IsKeyDown(Keys.Left);
            bool rightDown = ks.IsKeyDown(Keys.Right);

            // (Optional) still allow controller single-tap behavior:
            if (!leftDown && !rightDown)
            {
                if (controller.PressedDPad.Left)
                {
                    r.OnLeftRight(-1);
                    RefreshTexts();
                    ResetLeftRightHold();
                    return true;
                }
                if (controller.PressedDPad.Right)
                {
                    r.OnLeftRight(+1);
                    RefreshTexts();
                    ResetLeftRightHold();
                    return true;
                }
            }

            int dir = rightDown ? +1 : (leftDown ? -1 : 0);

            if (dir == 0)
            {
                ResetLeftRightHold();
                return false;
            }

            // New hold start (new dir or new row).
            if (_lrHoldDir != dir || _lrHoldRow != r)
            {
                _lrHoldDir       = dir;
                _lrHoldRow       = r;
                _lrHeldSec       = 0f;
                _lrNextRepeatSec = LR_INITIAL_DELAY;

                // Fire once immediately.
                r.OnLeftRight(dir);
                RefreshTexts();
                return true;
            }

            // Continue hold.
            _lrHeldSec       += dt;
            _lrNextRepeatSec -= dt;

            while (_lrNextRepeatSec <= 0f)
            {
                r.OnLeftRight(dir);
                RefreshTexts();

                _lrNextRepeatSec += GetLeftRightRepeatInterval(_lrHeldSec);
            }

            return true;
        }
        #endregion

        #region Ring/Biome Choice Helpers

        /// <summary>
        /// Returns ring biome choices: "@Random" + (vanilla + discovered custom biomes).
        /// </summary>
        private static string[] GetRingBiomeChoices()
        {
            var baseChoices = GetBiomeChoices(); // vanilla+custom
            var list = new List<string>(baseChoices.Length + 1)
            {
                RingRandomToken
            };
            list.AddRange(baseChoices);
            return list.ToArray();
        }
        #endregion

        #region Mouse / Scrollbar Helpers

        /// <summary>Returns current mouse position in screen coordinates.</summary>
        private static Point GetMousePoint()
        {
            var ms = Mouse.GetState();
            return new Point(ms.X, ms.Y);
        }

        /// <summary>
        /// Updates scrollbar track/thumb rectangles for the current viewport and maxScroll.
        /// </summary>
        private void UpdateScrollbarRects(Rectangle listViewport, int maxScrollPx)
        {
            float sy = Screen.Adjuster.ScaleFactor.Y;

            int barW = Math.Max(10, (int)(14f * sy));

            // Track sits on the right edge of the list viewport.
            _scrollTrackRect = new Rectangle(
                listViewport.Right - barW,
                listViewport.Y,
                barW,
                listViewport.Height);

            if (maxScrollPx <= 0)
            {
                _scrollThumbRect = _scrollTrackRect;
                return;
            }

            int trackH = _scrollTrackRect.Height;

            // contentH = viewportH + maxScroll.
            int contentH = listViewport.Height + maxScrollPx;

            int thumbMinH = Math.Max(12, (int)(24f * sy));
            int thumbH = (int)Math.Round(trackH * (listViewport.Height / (float)contentH));
            if (thumbH < thumbMinH) thumbH = thumbMinH;
            if (thumbH > trackH) thumbH = trackH;

            int travel = Math.Max(1, trackH - thumbH);
            int thumbY = _scrollTrackRect.Y + (int)Math.Round(travel * (_scrollPx / (float)maxScrollPx));

            _scrollThumbRect = new Rectangle(_scrollTrackRect.X, thumbY, _scrollTrackRect.Width, thumbH);
        }

        /// <summary>
        /// Converts a thumb top position (within track travel) to a scroll offset in pixels.
        /// </summary>
        private void SetScrollFromThumbTop(int thumbTopY, int maxScrollPx)
        {
            if (maxScrollPx <= 0)
            {
                _scrollPx = 0;
                return;
            }

            int trackY = _scrollTrackRect.Y;
            int trackH = _scrollTrackRect.Height;
            int thumbH = _scrollThumbRect.Height;

            int travel = Math.Max(1, trackH - thumbH);

            int minY = trackY;
            int maxY = trackY + travel;

            if (thumbTopY < minY) thumbTopY = minY;
            if (thumbTopY > maxY) thumbTopY = maxY;

            float t   = (thumbTopY - minY) / (float)travel;
            _scrollPx = (int)Math.Round(t * maxScrollPx);
        }
        #endregion

        #region Text / Utility Helpers

        /// <summary>Case-insensitive index lookup for a string array. Returns 0 if not found.</summary>
        private static int IndexOf(string[] arr, string value)
        {
            for (int i = 0; i < arr.Length; i++)
                if (string.Equals(arr[i], value, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        /// <summary>
        /// Shortens a biome type name for UI display:
        /// - "@Random"/"Random" => "Random"
        /// - otherwise => the final type name segment after the last '.'
        /// </summary>
        private static string ShortBiome(string full)
        {
            if (string.IsNullOrWhiteSpace(full)) return "(none)";
            if (string.Equals(full, RingRandomToken, StringComparison.OrdinalIgnoreCase)) return "Random";
            if (string.Equals(full, "Random", StringComparison.OrdinalIgnoreCase)) return "Random";
            int dot = full.LastIndexOf('.');
            return dot >= 0 ? full.Substring(dot + 1) : full;
        }
        #endregion

        #region Menu Item Wiring (MenuItems <-> Rows)

        /// <summary>
        /// Rebuilds MenuItems from _rows, then refreshes text and snaps selection to the first selectable row.
        /// </summary>
        private void RebuildMenuItems()
        {
            MenuItems.Clear();

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                AddMenuItem(r.GetText(), r.Desc, r);
            }

            RefreshTexts();
            SelectedIndex = 0;
            SnapSelectionToSelectable(+1);
        }

        /// <summary>
        /// Re-evaluates the live text for every row and applies it to the existing MenuItems.
        /// </summary>
        private void RefreshTexts()
        {
            for (int i = 0; i < MenuItems.Count; i++)
            {
                if (MenuItems[i]?.Tag is Row r && r.GetText != null)
                    MenuItems[i].Text = r.GetText();
            }
        }
        #endregion

        #region Seed Commit / Ring Sizing Helpers

        /// <summary>
        /// Commits the current seed edit buffer to WGConfig.SeedOverrideValue (if parse succeeds).
        /// </summary>
        private void CommitSeedBuffer()
        {
            if (int.TryParse(_seedEditBuffer, out var val))
            {
                WGConfig.SeedOverrideValue = val;
                WGConfig.Save();
            }
        }

        /// <summary>
        /// Ensures WGConfig.RingsValue contains at least <paramref name="count"/> entries.
        /// </summary>
        private void EnsureAtLeastRings(int count)
        {
            var rings = WGConfig.RingsValue;

            while (rings.Count < count)
            {
                rings.Add(new RingCore(
                    1000 + rings.Count * 500,
                    "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome"));
            }
        }
        #endregion

        #region Clamp Helpers

        /// <summary>Clamp helper (int).</summary>
        public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>Clamp helper (float).</summary>
        public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>Clamp helper (double).</summary>
        public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);

        #endregion

        #region Legacy: Ring Row Helper

        /// <summary>
        /// Creates a single "RingX" row that edits a ring core's biome by Left/Right.
        /// Notes:
        /// - This helper is currently unused by BuildRows (you build per-ring end/biome rows instead).
        /// - Kept here as a convenience method for alternate UI layouts.
        /// </summary>
        private Row MakeRingRow(int i, string label)
        {
            return new Row
            {
                Key     = "Ring" + i,
                Desc    = "Left/Right cycles biome type for this ring core.",
                GetText = () =>
                {
                    EnsureAtLeastRings(i + 1);
                    var r = WGConfig.RingsValue[i];
                    return $"{label}: {ShortBiome(r.BiomeType)} (End={r.EndRadius})";
                },
                OnLeftRight = dir =>
                {
                    EnsureAtLeastRings(i + 1);

                    var rings = WGConfig.RingsValue;
                    var rc    = rings[i];

                    var choices = GetRingBiomeChoices();
                    int cur     = IndexOf(choices, rc.BiomeType);
                    cur = (cur + dir) % choices.Length;
                    if (cur < 0) cur += choices.Length;

                    rc.BiomeType = choices[cur];
                    rings[i] = rc;

                    WGConfig.Save();
                }
            };
        }
        #endregion

        #endregion
    }
}