/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq.Expressions;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ.AI;
using System.Reflection;
using DNA.CastleMinerZ;
using HarmonyLib;
using System;

using static Minimap.Integrations.WorldGenPlus.WorldGenPlusBiomePalette;
using static Minimap.Integrations.WorldGenPlus.WorldGenPlusSurfaceRules;
using static Minimap.Integrations.WorldGenPlus.WorldGenPlusContext;
using static Minimap.Integrations.WorldGenPlus.WorldGenPlusMath;

namespace Minimap
{
    /// <summary>
    /// Biome minimap renderer: Draws a cached, low-resolution biome field around the local player
    /// into a small HUD rect.
    ///
    /// Summary:
    /// - Samples biomes into a low-res cell grid (cached / throttled).
    /// - Supports Square/Circle minimap shapes (circle uses software clipping).
    /// - Supports WorldGenPlus surface modes (rings, square bands, random regions).
    /// - Draws optional overlays: Chunk grid, player dot, other players, labels/readout text.
    /// </summary>
    internal static class MinimapRenderer
    {
        #region Cache / State

        /// <summary>1x1 white pixel texture used for all primitive drawing.</summary>
        private static Texture2D _px;

        /// <summary>Cached sampling grid dimensions.</summary>
        private static int _cols, _rows;

        /// <summary>Cached sampled colors for each cell in the grid.</summary>
        private static Color[] _cellColors;

        /// <summary>Cached dominant biome token per cell (used for edge drawing).</summary>
        private static string[] _cellDom; // dominant biome token per cell (for edge drawing)

        /// <summary>Next allowed cache update timestamp in ms (throttles sampling).</summary>
        private static double _nextUpdateMs;

        /// <summary>WorldGenPlus seed used for current cache; used to detect changes.</summary>
        private static int _lastSeed;

        /// <summary>WorldGenPlus cfg object reference used for current cache; used to detect changes.</summary>
        private static object _lastCfg;

        /// <summary>
        /// Cache for per-segment WorldGenPlus band tokens.
        /// Summary: Avoids rebuilding the band list every frame for the same segment index.
        /// </summary>
        private static readonly Dictionary<int, BandTokenCache> _wgpBandCache = new Dictionary<int, BandTokenCache>();

        /// <summary>
        /// Cached list of token bands for a given WGP "pattern index" / segment.
        /// </summary>
        private sealed class BandTokenCache
        {
            public double Period;
            public List<BandToken> Bands = new List<BandToken>();
        }

        /// <summary>
        /// Token band segment in effective distance space (0..period).
        /// </summary>
        private struct BandToken
        {
            public double Start;
            public double End;

            public string AType; // primary biome token
            public string BType; // optional secondary biome token
            public bool IsBlend;
        }
        #endregion

        #region Other Players: Color Cache

        /// <summary>
        /// Cache of stable colors for remote players when "random color per player" is enabled.
        /// Key: StableKeyForGamer(...)
        /// </summary>
        private static readonly Dictionary<int, Color> _otherPlayerColorCache = new Dictionary<int, Color>();

        #endregion

        #region Layout

        /// <summary>
        /// Computes minimap rectangle given a safe screen rect and anchor/margins.
        /// Summary: Used by both overlay modes to place the minimap in a corner.
        /// </summary>
        private static Rectangle ComputeMinimapRect(Rectangle safe, int sizePx, MinimapAnchor anchor, int marginPx)
        {
            int w = sizePx;
            int h = sizePx;

            int x = safe.Left + marginPx;
            int y = safe.Top + marginPx;

            if (anchor == MinimapAnchor.TopRight || anchor == MinimapAnchor.BottomRight)
                x = safe.Right - marginPx - w;

            if (anchor == MinimapAnchor.BottomLeft || anchor == MinimapAnchor.BottomRight)
                y = safe.Bottom - marginPx - h;

            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// Computes how many vertical pixels the "text under minimap" will occupy.
        /// Summary: Used to nudge bottom-anchored minimaps upward so the readout stays on-screen.
        /// </summary>
        private static int GetUnderTextReserve(SpriteFont font)
        {
            if (font == null) return 0;

            int lines = 0;
            if (MinimapConfig.MinimapCoordinates) lines++;
            if (MinimapConfig.ShowCurrentBiome) lines++;

            if (lines <= 0) return 0;

            float scale = MinimapConfig.FontScale;
            int lineH = (int)Math.Ceiling(font.LineSpacing * scale);

            int reserve = MinimapConfig.TextSpacingPx + (lineH * lines);

            // Optional safety for outlines (prevents outline from clipping at bottom).
            if (MinimapConfig.OutlineEnabled && MinimapConfig.OutlineThicknessPx > 0)
                reserve += MinimapConfig.OutlineThicknessPx;

            return reserve;
        }
        #endregion

        #region Sampling (Biome Cache)

        /// <summary>
        /// Populates the cached cell grid around the local player (throttled by _nextUpdateMs).
        /// Summary:
        /// - Converts screen-space cell centers into world XZ via current zoom.
        /// - Samples biome color (vanilla/WGP).
        /// - For Circle shape, marks cells outside the radius as Transparent.
        /// </summary>
        private static void UpdateCache(Rectangle mapRect, Vector3 playerLocalPos)
        {
            // Determine sample grid size from pixel step.
            int stepPx = Math.Max(2, MinimapConfig.SampleStepPx);

            int cols = Math.Max(8, mapRect.Width / stepPx);
            int rows = Math.Max(8, mapRect.Height / stepPx);

            // Clamp to avoid silly costs.
            cols = Math.Min(cols, 160);
            rows = Math.Min(rows, 160);

            if (_cols != cols || _rows != rows || _cellColors == null || _cellColors.Length != cols * rows)
            {
                _cols = cols;
                _rows = rows;
                _cellColors = new Color[cols * rows];
                _cellDom = new string[cols * rows];
            }

            // Grab WGP context if present (seed + cfg).
            bool wgpActive = TryGetWorldGenPlusContext(out object wgpCfg, out int wgpSeed);

            int wgpMode = 0; // 0=rings, 1=square, 2=single, 3=random
            bool wgpMirror = true;
            double wgpPeriod = 4400.0;

            if (wgpActive)
            {
                wgpMode = GetSurfaceMode(wgpCfg);
                wgpMirror = GetCfgBool(wgpCfg, "MirrorRepeat", true);
                wgpPeriod = GetCfgDouble(wgpCfg, "RingPeriod", 4400.0);
                if (wgpPeriod <= 0.0) wgpPeriod = 4400.0;
            }

            // If config object or seed changed, flush WGP per-segment caches.
            if (!ReferenceEquals(_lastCfg, wgpCfg) || _lastSeed != wgpSeed)
            {
                _lastCfg = wgpCfg;
                _lastSeed = wgpSeed;
                _wgpBandCache.Clear();
            }

            float zoom = MinimapState.ZoomPixelsPerBlock;
            if (zoom < MinimapConfig.ZoomMin) zoom = MinimapConfig.ZoomMin;
            if (zoom > MinimapConfig.ZoomMax) zoom = MinimapConfig.ZoomMax;

            // Center world (player).
            double cx = playerLocalPos.X;
            double cz = playerLocalPos.Z;

            // Circle mask for sampling.
            bool circle = (MinimapConfig.MinimapShape == MinimapShape.Circle);
            float radiusPx = mapRect.Width * 0.5f;
            float r2 = radiusPx * radiusPx;

            byte alpha = MinimapConfig.BiomeFillAlpha;

            // Sample each cell at its pixel-center mapped into world-space.
            for (int ry = 0; ry < _rows; ry++)
            {
                float y0 = mapRect.Top + (ry + 0.5f) * (mapRect.Height / (float)_rows);

                for (int rx = 0; rx < _cols; rx++)
                {
                    float x0 = mapRect.Left + (rx + 0.5f) * (mapRect.Width / (float)_cols);

                    // Circle mask: skip outside.
                    if (circle)
                    {
                        float dx = x0 - mapRect.Center.X;
                        float dy = y0 - mapRect.Center.Y;
                        if ((dx * dx + dy * dy) > r2)
                        {
                            _cellColors[ry * _cols + rx] = Color.Transparent;
                            _cellDom[ry * _cols + rx] = null;
                            continue;
                        }
                    }

                    double wx = cx + (x0 - mapRect.Center.X) / zoom;
                    double wz = cz + (y0 - mapRect.Center.Y) / zoom;

                    if (!TrySampleBiomeColor(wgpActive, wgpCfg, wgpSeed, wgpMode, wgpMirror, wgpPeriod,
                                            (int)Math.Round(wx), (int)Math.Round(wz), alpha,
                                            out Color c, out string dom))
                    {
                        c = Premul(new Color(0, 0, 0, alpha));
                        dom = null;
                    }

                    _cellColors[ry * _cols + rx] = c;
                    _cellDom[ry * _cols + rx] = dom;
                }
            }
        }

        /// <summary>
        /// Samples a biome color at world X/Z.
        /// Summary:
        /// - If WGP RandomRegions: samples two nearest features and blends.
        /// - Else: uses (vanilla or WGP) bands over an effective-distance metric, optionally mirrored per segment.
        /// - Single-biome mode is handled by the band pipeline as one full-period solid band.
        /// </summary>
        private static bool TrySampleBiomeColor(
            bool wgpActive,
            object wgpCfg,
            int seed,
            int wgpMode,
            bool wgpMirror,
            double wgpPeriod,
            int worldX,
            int worldZ,
            byte alpha,
            out Color color,
            out string dominantToken)
        {
            color = Color.Transparent;
            dominantToken = null;

            // WorldGenPlus random regions.
            // WorldGenPlus random regions.
            if (wgpActive && IsRandomRegionsSurfaceMode(wgpMode))
            {

                if (!TrySampleRandomRegions(wgpCfg, seed, worldX, worldZ, out string aType, out string bType, out float bW))
                    return false;

                Color ca = MapBiomeTypeToColor(aType, alpha, seed);
                Color c = ca;

                dominantToken = aType;

                if (!string.IsNullOrWhiteSpace(bType))
                {
                    Color cb = MapBiomeTypeToColor(bType, alpha, seed);
                    c = Color.Lerp(ca, cb, bW);
                    dominantToken = (bW >= 0.5f) ? bType : aType;
                }

                color = c;
                return true;
            }

            // Rings / square bands (vanilla or WGP).
            double period = wgpActive ? wgpPeriod : 4400.0;

            // distance metric
            double dx = worldX;
            double dz = worldZ;

            double d = Math.Sqrt(dx * dx + dz * dz);
            if (wgpActive && IsSquareSurfaceMode(wgpMode))
            {
                // Chebyshev distance for square bands (matches map screen logic).
                d = Math.Max(Math.Abs(dx), Math.Abs(dz));
            }

            int seg = (int)Math.Floor(d / period);
            double local = d - seg * period;

            bool flipped = (!wgpActive || wgpMirror) && ((seg & 1) != 0);
            double eff = flipped ? (period - local) : local;

            string aTok, bTok;
            float bW2;

            if (wgpActive)
            {
                if (!TrySampleWorldGenPlusBandTokens(wgpCfg, seed, seg, eff, out aTok, out bTok, out bW2))
                    return false;
            }
            else
            {
                TrySampleVanillaBandTokens(eff, out aTok, out bTok, out bW2);
            }

            Color aC = MapBiomeTypeToColor(aTok, alpha, seed);
            Color outC = aC;

            dominantToken = aTok;

            if (!string.IsNullOrWhiteSpace(bTok))
            {
                Color bC = MapBiomeTypeToColor(bTok, alpha, seed);
                outC = Color.Lerp(aC, bC, bW2);
                dominantToken = (bW2 >= 0.5f) ? bTok : aTok;
            }

            color = outC;
            return true;
        }

        /// <summary>
        /// Vanilla surface-band tokens (4400 effective distance mirrored pattern).
        /// Summary: Produces a primary token + optional blend token and weight.
        /// </summary>
        private static void TrySampleVanillaBandTokens(double eff, out string aType, out string bType, out float bW)
        {
            // Return type tokens that MapBiomeTypeToColor can understand via IndexOf("ClassicBiome") etc.
            bType = null;
            bW = 0f;

            if (eff < 200.0) { aType = "ClassicBiome"; return; }
            if (eff < 300.0) { aType = "ClassicBiome"; bType = "LagoonBiome"; bW = (float)((eff - 200.0) / 100.0); return; }

            if (eff < 900.0) { aType = "LagoonBiome"; return; }
            if (eff < 1000.0) { aType = "LagoonBiome"; bType = "DesertBiome"; bW = (float)((eff - 900.0) / 100.0); return; }

            if (eff < 1600.0) { aType = "DesertBiome"; return; }
            if (eff < 1700.0) { aType = "DesertBiome"; bType = "MountainBiome"; bW = (float)((eff - 1600.0) / 100.0); return; }

            if (eff < 2300.0) { aType = "MountainBiome"; return; }
            if (eff < 2400.0) { aType = "MountainBiome"; bType = "ArcticBiome"; bW = (float)((eff - 2300.0) / 100.0); return; }

            if (eff < 3000.0) { aType = "ArcticBiome"; return; }
            if (eff < 3600.0) { aType = "ArcticBiome"; bType = "DecentBiome"; bW = (float)((eff - 3000.0) / 600.0); return; }

            aType = "HellFloorBiome";
        }

        /// <summary>
        /// Samples WorldGenPlus token bands for the given pattern/segment index.
        /// Summary: Uses a per-index cache because tokens may depend on the segment index (pipes / @Random).
        /// </summary>
        private static bool TrySampleWorldGenPlusBandTokens(object cfg, int seed, int patternIndex, double eff, out string aType, out string bType, out float bW)
        {
            aType = null;
            bType = null;
            bW = 0f;

            // Cache per segment because pipes/@Random can vary per segment index.
            if (!_wgpBandCache.TryGetValue(patternIndex, out BandTokenCache cache))
            {
                cache = BuildWorldGenPlusTokenBands(cfg, seed, patternIndex);
                _wgpBandCache[patternIndex] = cache;
            }

            if (cache == null || cache.Bands == null || cache.Bands.Count == 0)
                return false;

            // Scan bands (small list).
            for (int i = 0; i < cache.Bands.Count; i++)
            {
                var b = cache.Bands[i];
                if (eff >= b.Start && eff < b.End)
                {
                    aType = b.AType;
                    bType = b.BType;
                    if (b.IsBlend && !string.IsNullOrWhiteSpace(bType))
                    {
                        double t = (eff - b.Start) / Math.Max(1e-9, (b.End - b.Start));
                        if (t < 0.0) t = 0.0;
                        if (t > 1.0) t = 1.0;
                        bW = (float)t;
                    }
                    return true;
                }
            }

            // If we hit exact period endpoint.
            var last = cache.Bands[cache.Bands.Count - 1];
            aType = last.AType;
            bType = last.BType;
            bW = last.IsBlend ? 1f : 0f;
            return true;
        }

        /// <summary>
        /// Builds WorldGenPlus effective-distance token bands (0..RingPeriod) for a given pattern index.
        /// Summary:
        /// - Reads cfg.Rings (EndRadius + BiomeType).
        /// - Supports pipe patterns (A|B|C) and @Random (stable seeded pick).
        /// - Inserts optional transition bands (blend) except when "Decent" special behavior applies.
        /// </summary>
        private static BandTokenCache BuildWorldGenPlusTokenBands(object cfg, int seed, int patternIndex)
        {
            var outCache = new BandTokenCache();

            try
            {
                double period = GetCfgDouble(cfg, "RingPeriod", 4400.0);
                if (period <= 0.0) period = 4400.0;
                outCache.Period = period;

                int surfaceMode = GetSurfaceMode(cfg);

                // Single-biome mode:
                // Build one full-period solid band so the existing token/color pipeline works everywhere.
                if (IsSingleSurfaceMode(surfaceMode))
                {
                    string biomeType = GetSingleSurfaceBiome(
                        cfg,
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome");

                    outCache.Bands.Add(new BandToken
                    {
                        Start = 0.0,
                        End = period,
                        AType = biomeType,
                        BType = null,
                        IsBlend = false
                    });

                    return outCache;
                }

                double transitionWidth = Math.Max(0.0, GetCfgDouble(cfg, "TransitionWidth", 0.0));

                var ringsObj = GetCfgObject(cfg, "Rings");
                if (!(ringsObj is System.Collections.IEnumerable ringsEnum)) return outCache;

                // Random ring bag + vary by period (mirror of your band builder).
                var bagObj = GetCfgObject(cfg, "RandomRingBiomeChoices");
                var bag = ReadStringList(bagObj);
                if (bag == null || bag.Count == 0)
                {
                    bag = new List<string>
                    {
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",
                    };
                }

                bool varyByPeriod = GetCfgBool(cfg, "RandomRingsVaryByPeriod", false);

                string resolveToken(string raw, int ringIdx)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return raw;

                    string pick = raw;

                    if (raw.IndexOf('|') >= 0)
                    {
                        string[] parts = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts != null && parts.Length > 0)
                        {
                            int pi = Mod(patternIndex, parts.Length);
                            pick = (parts[pi] ?? "").Trim();
                        }
                    }

                    if (IsRingRandomToken(pick))
                    {
                        int periodKey = varyByPeriod ? patternIndex : 0;

                        unchecked
                        {
                            uint h = 0u;
                            h ^= (uint)seed * 0xCB1AB31Fu;
                            h ^= (uint)ringIdx * 0x8DA6B343u;
                            h ^= (uint)periodKey * 0xD8163841u;
                            h = HashU(h ^ 0xA2C79D1Fu);

                            int idx = (int)(h % (uint)bag.Count);
                            pick = bag[idx];
                        }
                    }

                    return pick;
                }

                var ends = new List<double>();
                var types = new List<string>();

                int ringIdx2 = 0;
                foreach (var it in ringsEnum)
                {
                    if (it == null) continue;

                    double endR = ReadDouble(it, "EndRadius", 0.0);
                    string rawT = ReadString(it, "BiomeType", null);

                    if (endR <= 0.0) continue;

                    ends.Add(endR);
                    types.Add(resolveToken(rawT, ringIdx2));
                    ringIdx2++;
                }

                if (ends.Count == 0) return outCache;

                double coreStart = 0.0;

                for (int i = 0; i < ends.Count; i++)
                {
                    double coreEnd = ends[i];
                    if (coreEnd <= coreStart) continue;

                    string aType = types[i];

                    // Decent special-case: no transition insertion.
                    if (IsDecentBiomeType(aType))
                    {
                        outCache.Bands.Add(new BandToken { Start = coreStart, End = coreEnd, AType = aType, BType = null, IsBlend = false });
                        coreStart = coreEnd;
                        continue;
                    }

                    // Solid core.
                    outCache.Bands.Add(new BandToken { Start = coreStart, End = coreEnd, AType = aType, BType = null, IsBlend = false });

                    // Optional transition.
                    if (i + 1 < ends.Count)
                    {
                        string nextType = types[i + 1];
                        double nextEnd = ends[i + 1];

                        // Next is Decent: promoted, skip normal transition.
                        if (IsDecentBiomeType(nextType))
                        {
                            outCache.Bands.Add(new BandToken { Start = coreEnd, End = nextEnd, AType = nextType, BType = null, IsBlend = false });
                            coreStart = nextEnd;
                            i++; // consumed next
                            continue;
                        }

                        double wHere = Math.Min(transitionWidth, Math.Max(0.0, nextEnd - coreEnd));
                        if (wHere > 0.0)
                        {
                            outCache.Bands.Add(new BandToken { Start = coreEnd, End = coreEnd + wHere, AType = aType, BType = nextType, IsBlend = true });
                            coreStart = coreEnd + wHere;
                        }
                        else
                        {
                            coreStart = coreEnd;
                        }
                    }
                    else
                    {
                        coreStart = coreEnd;
                    }
                }

                // Fill remainder with Hell.
                if (coreStart < period)
                {
                    outCache.Bands.Add(new BandToken { Start = coreStart, End = period, AType = "HellFloorBiome", BType = null, IsBlend = false });
                }

                return outCache;
            }
            catch
            {
                return outCache;
            }
        }
        #endregion

        #region Other Players (Collection + Rendering)

        /// <summary>
        /// Gets a color for a remote player marker.
        /// Summary:
        /// - If fixed color is configured, uses that.
        /// - If "random per player" is enabled, uses a stable hash->HSV mapping and caches it.
        /// </summary>
        private static Color GetOtherPlayerColor(int key)
        {
            if (!MinimapConfig.OtherPlayerColorRandom)
                return Premul(MinimapConfig.OtherPlayerColor);

            if (_otherPlayerColorCache.TryGetValue(key, out var c))
                return c;

            // Stable "random" color from hash
            c = Premul(ColorFromHash(key, 255));
            _otherPlayerColorCache[key] = c;
            return c;
        }

        /// <summary>
        /// Builds a stable random-ish color from a hash using an HSV mapping.
        /// Summary: Keeps the color consistent across frames for the same key.
        /// </summary>
        private static Color ColorFromHash(int h, byte a)
        {
            unchecked
            {
                // HSV-ish: stable hue, high sat/value
                uint x = (uint)h;
                x ^= x >> 16; x *= 0x7FEB352Du;
                x ^= x >> 15; x *= 0x846CA68Bu;
                x ^= x >> 16;

                float hue = (x % 360u) / 360f; // 0..1
                float s = 0.85f;
                float v = 0.95f;

                // HSV -> RGB
                HSVToRGB(hue, s, v, out float r, out float g, out float b);
                return new Color((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), a);
            }
        }

        /// <summary>HSV -> RGB helper (0..1 range).</summary>
        private static void HSVToRGB(float h, float s, float v, out float r, out float g, out float b)
        {
            if (s <= 0f) { r = g = b = v; return; }

            h = (h - (float)Math.Floor(h)) * 6f;
            int i = (int)Math.Floor(h);
            float f = h - i;

            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));

            switch (i)
            {
                default:
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }
        }
        #endregion

        #region Drawing (Overlay Pipeline)

        #region Marker Payloads / Shared Scratch

        /// <summary>
        /// Remote player marker payload used by the HUD patch.
        /// Summary: A stable key + current world position for each remote player.
        /// </summary>
        public struct OtherPlayerMarker
        {
            public int Key;          // Stable per player (hash)
            public Vector3 Position; // World/local position
        }
        #endregion

        #region Players

        /// <summary>
        /// Scratch list (optional) used by callers to avoid per-frame allocations.
        /// </summary>
        public static readonly List<OtherPlayerMarker> _otherPlayers = new List<OtherPlayerMarker>(16);

        #endregion

        #region Hostiles (Enemies + Dragon)

        /// <summary>
        /// Scratch list of enemy world positions (X/Z used for minimap).
        /// Notes:
        /// - Filled by the HUD patch (GamePatches) each frame (best-effort).
        /// - Kept as raw Vector3 positions for cheap draw conversion.
        /// </summary>
        public static readonly List<Vector3> _enemyMarkers = new List<Vector3>(64);

        /// <summary>
        /// Dragon marker state.
        /// Summary: Single-instance hostile represented as a simple alive flag + position.
        /// </summary>
        public static bool    _dragonAlive;
        public static Vector3 _dragonPos;

        /// <summary>
        /// EnemyManager private enemies list.
        /// Summary: AccessTools FieldRef for EnemyManager._enemies.
        /// </summary>
        private static readonly AccessTools.FieldRef<EnemyManager, List<BaseZombie>> EnemiesRef =
            AccessTools.FieldRefAccess<EnemyManager, List<BaseZombie>>("_enemies");

        /// <summary>
        /// Cache: runtime-type -> fast Vector3 getter (no boxing per enemy).
        /// Notes:
        /// - We compile a delegate once per runtime type and reuse it for the session.
        /// - This avoids repeated reflection each frame when building markers.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, Func<object, Vector3>> _posGetterCache =
            new ConcurrentDictionary<Type, Func<object, Vector3>>();

        #endregion

        #region Marker Builders

        /// <summary>
        /// Attempts to derive a stable integer key for a NetworkGamer.
        /// Summary: Prefers Gamertag if available, otherwise falls back to GetHashCode().
        /// </summary>
        private static int StableKeyForGamer(NetworkGamer gamer)
        {
            // Prefer Gamertag if available (stable across session).
            // If your NetworkGamer type doesn't have Gamertag, switch to gamer.Id or gamer.ToString().
            string gt = null;
            try { gt = gamer.Gamertag; } catch { }

            if (!string.IsNullOrEmpty(gt))
                return gt.GetHashCode();

            return gamer.GetHashCode();
        }

        /// <summary>
        /// Populates a destination list with remote player markers from the active network session.
        /// Notes:
        /// - Skips local gamer.
        /// - Tag is expected to be a Player instance.
        /// </summary>
        public static void BuildOtherPlayers(List<OtherPlayerMarker> dst)
        {
            dst.Clear();

            var game = CastleMinerZGame.Instance;
            if (game == null) return;

            var sess = game.CurrentNetworkSession;
            if (sess == null) return;

            var localGamer = game.LocalPlayer?.Gamer;
            if (localGamer == null) return;

            foreach (NetworkGamer gamer in sess.AllGamers)
            {
                if (gamer == null) continue;
                if (gamer == localGamer) continue;

                // Tag -> Player (your working approach).
                if (!(gamer.Tag is Player p)) continue;

                dst.Add(new OtherPlayerMarker
                {
                    Key = StableKeyForGamer(gamer),
                    Position = p.LocalPosition
                });
            }
        }
        #endregion

        #region Draw Overlay (Entrypoint)

        /// <summary>
        /// Primary overlay entrypoint that manages its own SpriteBatch Begin/End.
        /// Summary:
        /// - Updates cached biome samples (throttled).
        /// - Draws Square maps directly.
        /// - Draws Circle maps using software clipping for fills/cells/grid.
        /// </summary>
        public static void DrawOverlay(
            GraphicsDevice device,
            SpriteBatch sb,
            SpriteFont font,
            Rectangle safeRect,
            Vector3 playerLocalPos,
            GameTime time,
            List<OtherPlayerMarker> otherPlayers)
        {
            EnsurePx(device);

            Rectangle mapRect = ComputeMinimapRect(
                safeRect,
                MinimapConfig.MinimapScale,
                MinimapConfig.MinimapLocation,
                MinimapConfig.MarginPx);

            // If we're anchored to the bottom, reserve space for the readout text.
            if (MinimapConfig.MinimapLocation == MinimapAnchor.BottomLeft ||
                MinimapConfig.MinimapLocation == MinimapAnchor.BottomRight)
            {
                int reserve = GetUnderTextReserve(font);
                if (reserve > 0)
                {
                    mapRect.Offset(0, -reserve);

                    // Clamp so we don't shove it off the top on tiny resolutions.
                    int minY = safeRect.Top + MinimapConfig.MarginPx;
                    if (mapRect.Y < minY) mapRect.Y = minY;
                }
            }

            // Update cache (your existing throttled update logic).
            double nowMs = time.TotalGameTime.TotalMilliseconds;
            if (nowMs >= _nextUpdateMs)
            {
                UpdateCache(mapRect, playerLocalPos);
                _nextUpdateMs = nowMs + MinimapConfig.UpdateIntervalMs;
            }

            // Square minimap: No masking needed.
            if (MinimapConfig.MinimapShape == MinimapShape.Square)
            {
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                         DepthStencilState.None, RasterizerState.CullNone);

                // Background.
                if (MinimapConfig.MinimapFillAlpha > 0)
                    DrawRect(sb, mapRect, Premul(new Color(0, 0, 0, MinimapConfig.MinimapFillAlpha)));

                DrawBiomeCells(sb, mapRect);

                if (MinimapState.ShowChunkGrid)
                    DrawChunkGrid(sb, mapRect, playerLocalPos);

                DrawFrame(sb, mapRect);
                DrawCompassNSEW(sb, font, mapRect);
                if (MinimapConfig.Player) DrawPlayerDot(sb, mapRect);
                DrawTextUnderMinimap(sb, font, mapRect, playerLocalPos);

                sb.End();
                return;
            }

            // Circle minimap: Stencil mask.
            device.ReferenceStencil = 1;

            // Circle minimap (NO STENCIL): Draw only inside the circle.
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                     DepthStencilState.None, RasterizerState.CullNone);

            if (MinimapConfig.MinimapFillAlpha > 0)
                DrawFilledCircle(sb, mapRect, Premul(new Color(0, 0, 0, MinimapConfig.MinimapFillAlpha)));

            DrawBiomeCellsCircleClipped(sb, mapRect);

            if (MinimapState.ShowChunkGrid)
                DrawChunkGridCircleClipped(sb, mapRect, playerLocalPos);

            if (MinimapConfig.ShowOtherPlayers)
                DrawOtherPlayers(sb, mapRect, playerLocalPos, otherPlayers);

            if (MinimapConfig.ShowEnemies)
                DrawWorldDots(sb, mapRect, playerLocalPos, _enemyMarkers, MinimapConfig.EnemyColor, MinimapConfig.EnemyDotSizePx);

            if (MinimapConfig.ShowDragon && _dragonAlive)
                DrawWorldDot(sb, mapRect, playerLocalPos, _dragonPos, MinimapConfig.DragonColor, MinimapConfig.DragonDotSizePx);

            if (MinimapConfig.Player)
                DrawPlayerDot(sb, mapRect);

            DrawFrame(sb, mapRect);
            DrawCompassNSEW(sb, font, mapRect);
            DrawTextUnderMinimap(sb, font, mapRect, playerLocalPos);

            sb.End();
        }
        #endregion

        #region Draw Markers (Players / World Dots)

        /// <summary>
        /// Draws remote player dots on the minimap.
        /// Notes:
        /// - Uses ZoomPixelsPerBlock to convert world deltas to minimap pixels.
        /// - Applies an extra circle clip check when minimap is circular.
        /// </summary>
        private static void DrawOtherPlayers(SpriteBatch sb, Rectangle mapRect, Vector3 localPos, List<OtherPlayerMarker> others)
        {
            if (others == null || others.Count == 0) return;

            float zoom = MinimapState.ZoomPixelsPerBlock;
            if (zoom <= 0.000001f) return;

            bool circle = (MinimapConfig.MinimapShape == MinimapShape.Circle);
            float radius = (mapRect.Width * 0.5f) - 0.5f;
            float r2 = radius * radius;

            int dot = MinimapConfig.OtherPlayerDotSizePx;
            int half = dot / 2;

            for (int i = 0; i < others.Count; i++)
            {
                var m = others[i];

                // World delta -> minimap pixels (X/Z).
                float dx = (float)(m.Position.X - localPos.X);
                float dz = (float)(m.Position.Z - localPos.Z);

                int px = mapRect.Center.X + (int)Math.Round(dx * zoom);
                int py = mapRect.Center.Y + (int)Math.Round(dz * zoom);

                // Clip to square rect.
                if (px < mapRect.Left || px > mapRect.Right || py < mapRect.Top || py > mapRect.Bottom)
                    continue;

                // Clip to circle.
                if (circle)
                {
                    float cx = (px + 0.5f) - mapRect.Center.X;
                    float cy = (py + 0.5f) - mapRect.Center.Y;
                    if ((cx * cx + cy * cy) > r2)
                        continue;
                }

                Color c = GetOtherPlayerColor(m.Key);
                DrawRect(sb, new Rectangle(px - half, py - half, dot, dot), c);
            }
        }

        /// <summary>
        /// Draw a set of world-space points onto the minimap as small square "dots".
        /// Notes:
        /// - Converts each (X/Z) world position into minimap pixel coords around the local player.
        /// - Applies current zoom, clamps to the map rectangle, and respects circle masking (if enabled).
        /// - Draws each marker as a tiny filled rectangle using the provided color + dot size.
        /// </summary>
        private static void DrawWorldDots(SpriteBatch sb, Rectangle mapRect, Vector3 localPos, List<Vector3> points, Color color, int dotSizePx)
        {
            if (points == null || points.Count == 0) return;

            float zoom = MinimapState.ZoomPixelsPerBlock;
            if (zoom <= 0.000001f) return;

            bool circle  = (MinimapConfig.MinimapShape == MinimapShape.Circle);
            float radius = (mapRect.Width * 0.5f) - 0.5f;
            float r2     = radius * radius;

            int dot = Math.Max(1, dotSizePx);
            int half = dot / 2;

            Color c = Premul(color);

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];

                float dx = (float)(p.X - localPos.X);
                float dz = (float)(p.Z - localPos.Z);

                int px = mapRect.Center.X + (int)Math.Round(dx * zoom);
                int py = mapRect.Center.Y + (int)Math.Round(dz * zoom);

                if (px < mapRect.Left || px > mapRect.Right || py < mapRect.Top || py > mapRect.Bottom)
                    continue;

                if (circle)
                {
                    float cx = (px + 0.5f) - mapRect.Center.X;
                    float cy = (py + 0.5f) - mapRect.Center.Y;
                    if ((cx * cx + cy * cy) > r2)
                        continue;
                }

                DrawRect(sb, new Rectangle(px - half, py - half, dot, dot), c);
            }
        }

        /// <summary>
        /// Tiny reusable list to avoid per-frame allocs for single markers.
        /// Summary: Reuses DrawWorldDots() for single-point drawing.
        /// </summary>
        private static readonly List<Vector3> _tmpOne = new List<Vector3>(1);

        /// <summary>
        /// Draw a single world-space point as a minimap dot.
        /// Notes:
        /// - Uses a shared 1-element list to reuse DrawWorldDots() without allocations.
        /// </summary>
        private static void DrawWorldDot(SpriteBatch sb, Rectangle mapRect, Vector3 localPos, Vector3 point, Color color, int dotSizePx)
        {
            // Trivial wrapper to reuse the list version without allocations:
            _tmpOne.Clear();
            _tmpOne.Add(point);
            DrawWorldDots(sb, mapRect, localPos, _tmpOne, color, dotSizePx);
        }
        #endregion

        #region Hostile Position Extraction (Reflection Cache)

        /// <summary>
        /// Extract a Vector3 world position from an arbitrary enemy instance.
        /// Notes:
        /// - Uses a per-type cached delegate (compiled once) to avoid reflection each frame.
        /// - Returns false if the type has no supported position member or if access fails.
        /// </summary>
        private static bool TryGetPos(object obj, out Vector3 pos)
        {
            pos = default;

            if (obj == null) return false;

            var t = obj.GetType();
            var getter = _posGetterCache.GetOrAdd(t, BuildPosGetter);
            if (getter == null) return false;

            try { pos = getter(obj); return true; }
            catch { return false; }
        }

        /// <summary>
        /// Builds a fast "object -> Vector3" getter for common position members.
        /// Notes:
        /// - Prefers properties named LocalPosition/Position/WorldPosition.
        /// - Falls back to fields with the same names.
        /// - Returns null when no compatible member exists.
        /// </summary>
        private static Func<object, Vector3> BuildPosGetter(Type t)
        {
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Try common names first
                var pi =
                    t.GetProperty("LocalPosition", F) ??
                    t.GetProperty("Position", F) ??
                    t.GetProperty("WorldPosition", F);

                if (pi != null && pi.PropertyType == typeof(Vector3) && pi.GetGetMethod(true) != null)
                {
                    var obj = Expression.Parameter(typeof(object), "o");
                    var cast = Expression.Convert(obj, t);
                    var prop = Expression.Property(cast, pi);
                    return Expression.Lambda<Func<object, Vector3>>(prop, obj).Compile();
                }

                var fi =
                    t.GetField("LocalPosition", F) ??
                    t.GetField("Position", F) ??
                    t.GetField("WorldPosition", F);

                if (fi != null && fi.FieldType == typeof(Vector3))
                {
                    var obj = Expression.Parameter(typeof(object), "o");
                    var cast = Expression.Convert(obj, t);
                    var field = Expression.Field(cast, fi);
                    return Expression.Lambda<Func<object, Vector3>>(field, obj).Compile();
                }
            }
            catch { }

            return null;
        }
        #endregion

        #region Hostile Marker Builder (Enemies + Dragon)

        /// <summary>
        /// Collects current enemy + dragon world positions for the minimap overlay.
        /// Notes:
        /// - Clears cached hostile state first to prevent stale markers.
        /// - If enabled and alive, records the dragon's position.
        /// - If enabled, snapshots enemy positions (best-effort) into _enemyMarkers.
        /// - Never throws: failures are swallowed so HUD/minimap drawing can't be broken.
        /// </summary>
        public static void BuildHostileMarkers()
        {
            // Clear first so stale markers never hang around.
            _enemyMarkers.Clear();
            _dragonAlive = false;
            _dragonPos   = default;

            try
            {
                var em = EnemyManager.Instance;
                if (em == null) return;

                // Dragon (use the public helpers you already referenced in your snippet).
                if (MinimapConfig.ShowDragon && em.DragonIsAlive)
                {
                    _dragonAlive = true;
                    _dragonPos = em.DragonPosition;
                }

                // Enemies.
                if (!MinimapConfig.ShowEnemies) return;

                var list = EnemiesRef(em);
                if (list == null || list.Count == 0) return;

                // Index loop stays safe if list grows/shrinks during iteration.
                for (int i = 0; i < list.Count; i++)
                {
                    var z = list[i];
                    if (z == null) continue;

                    if (TryGetPos(z, out var p))
                        _enemyMarkers.Add(p);
                }
            }
            catch
            {
                // Best-effort; never break HUD draw.
                _enemyMarkers.Clear();
                _dragonAlive = false;
            }
        }
        #endregion

        #endregion

        #region Circle Clipping (Software)

        /// <summary>
        /// Draws a filled circle inside the map rect using scanlines.
        /// Summary: Used as a "background fill" for circular minimap shapes.
        /// </summary>
        private static void DrawFilledCircle(SpriteBatch sb, Rectangle mapRect, Color c)
        {
            int r = mapRect.Width / 2;
            int cx = mapRect.Center.X;
            int cy = mapRect.Center.Y;

            float r2 = r * r;

            for (int y = -r; y <= r; y++)
            {
                float dy2 = y * y;
                float xHalf = (float)Math.Sqrt(r2 - dy2);

                int halfW = (int)xHalf;
                int w = halfW * 2;
                if (w <= 0) continue;

                DrawRect(sb, new Rectangle(cx - halfW, cy + y, w, 1), c);
            }
        }

        /// <summary>
        /// Draws a rectangle clipped to a circular mask.
        /// Summary:
        /// - Fast reject if the rect is fully outside the circle.
        /// - Fast accept if the rect is fully inside the circle.
        /// - Otherwise scanline clips 1px rows.
        /// </summary>
        private static void DrawRectClippedToCircle(SpriteBatch sb, Rectangle rect, Point center, float r2, Color c)
        {
            // Fast reject: distance from circle center to rect (closest point)
            float nearestX = MathHelper.Clamp(center.X, rect.Left, rect.Right);
            float nearestY = MathHelper.Clamp(center.Y, rect.Top, rect.Bottom);

            float ndx = nearestX - center.X;
            float ndy = nearestY - center.Y;

            if ((ndx * ndx + ndy * ndy) >= r2)
                return;

            // Fast accept: farthest corner inside
            float farX = Math.Max(Math.Abs(rect.Left - center.X), Math.Abs(rect.Right - center.X));
            float farY = Math.Max(Math.Abs(rect.Top - center.Y), Math.Abs(rect.Bottom - center.Y));

            if ((farX * farX + farY * farY) <= r2)
            {
                DrawRect(sb, rect, c);
                return;
            }

            // Partial: scanline clip (draw 1px rows)
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                float dy = (y + 0.5f) - center.Y;
                float dy2 = dy * dy;
                if (dy2 >= r2) continue;

                float half = (float)Math.Sqrt(r2 - dy2);
                float xmin = center.X - half;
                float xmax = center.X + half;

                int sx0 = Math.Max(rect.Left, (int)Math.Ceiling(xmin));
                int sx1 = Math.Min(rect.Right, (int)Math.Floor(xmax) + 1); // exclusive

                int w = sx1 - sx0;
                if (w > 0)
                    DrawRect(sb, new Rectangle(sx0, y, w, 1), c);
            }
        }

        /// <summary>
        /// Draws cached biome cells for circle shape using per-cell clipping.
        /// </summary>
        private static void DrawBiomeCellsCircleClipped(SpriteBatch sb, Rectangle mapRect)
        {
            if (_cellColors == null || _cellColors.Length == 0) return;

            Point center = mapRect.Center;
            float radius = (mapRect.Width * 0.5f) - 0.5f;
            float r2 = radius * radius;

            int idx = 0;
            float cellW = mapRect.Width / (float)_cols;
            float cellH = mapRect.Height / (float)_rows;

            for (int y = 0; y < _rows; y++)
            {
                int py = (int)(mapRect.Top + y * cellH);
                int ph = Math.Max(1, (int)Math.Ceiling(cellH));

                for (int x = 0; x < _cols; x++, idx++)
                {
                    Color c = _cellColors[idx];
                    if (c.A == 0) continue;

                    int px = (int)(mapRect.Left + x * cellW);
                    int pw = Math.Max(1, (int)Math.Ceiling(cellW));

                    var rect = new Rectangle(px, py, pw, ph);
                    DrawRectClippedToCircle(sb, rect, center, r2, c);
                }
            }

            // Optional biome edges: just clip those tiny line-rects too
            if (MinimapConfig.ShowBiomeEdges && _cellDom != null)
                DrawEdgesCircleClipped(sb, mapRect, cellW, cellH, center, r2);
        }

        /// <summary>
        /// Draws biome boundaries for circle shape, clipped to the circular mask.
        /// Summary: Uses dominant token changes between adjacent cells.
        /// </summary>
        private static void DrawEdgesCircleClipped(SpriteBatch sb, Rectangle mapRect, float cellW, float cellH, Point center, float r2)
        {
            Color edge = Premul(MinimapConfig.BiomeEdgeColor);

            // Vertical boundaries
            for (int y = 0; y < _rows; y++)
            {
                for (int x = 1; x < _cols; x++)
                {
                    string a = _cellDom[y * _cols + (x - 1)];
                    string b = _cellDom[y * _cols + x];
                    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;
                    if (string.Equals(a, b, StringComparison.Ordinal)) continue;

                    int px = (int)(mapRect.Left + x * cellW);
                    int py = (int)(mapRect.Top + y * cellH);
                    int ph = Math.Max(1, (int)Math.Ceiling(cellH));

                    DrawRectClippedToCircle(sb, new Rectangle(px, py, 1, ph), center, r2, edge);
                }
            }

            // Horizontal boundaries
            for (int y = 1; y < _rows; y++)
            {
                for (int x = 0; x < _cols; x++)
                {
                    string a = _cellDom[(y - 1) * _cols + x];
                    string b = _cellDom[y * _cols + x];
                    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;
                    if (string.Equals(a, b, StringComparison.Ordinal)) continue;

                    int px = (int)(mapRect.Left + x * cellW);
                    int py = (int)(mapRect.Top + y * cellH);
                    int pw = Math.Max(1, (int)Math.Ceiling(cellW));

                    DrawRectClippedToCircle(sb, new Rectangle(px, py, pw, 1), center, r2, edge);
                }
            }
        }
        #endregion

        #region Grid Overlay (Square + Circle)

        /// <summary>
        /// Draws a fixed-step chunk/grid overlay in circle mode (clipped to circle).
        /// Notes:
        /// - step is in world blocks.
        /// - circle clipping uses per-line circle intersection.
        /// </summary>
        private static void DrawChunkGridCircleClipped(SpriteBatch sb, Rectangle mapRect, Vector3 playerLocalPos)
        {
            float zoom = MinimapState.ZoomPixelsPerBlock;
            if (zoom <= 0.000001f) return;

            int step = Math.Max(1, MinimapConfig.GridStepBlocks);
            Color gridC = Premul(MinimapConfig.GridColor);

            Point center = mapRect.Center;
            float radius = (mapRect.Width * 0.5f) - 0.5f;
            float r2 = radius * radius;

            double cx = playerLocalPos.X;
            double cz = playerLocalPos.Z;

            double halfW = mapRect.Width * 0.5;
            double halfH = mapRect.Height * 0.5;

            double minX = cx - halfW / zoom;
            double maxX = cx + halfW / zoom;
            double minZ = cz - halfH / zoom;
            double maxZ = cz + halfH / zoom;

            long minBlockX = (long)Math.Floor(minX);
            long maxBlockX = (long)Math.Ceiling(maxX);
            long minBlockZ = (long)Math.Floor(minZ);
            long maxBlockZ = (long)Math.Ceiling(maxZ);

            long startX = FloorDiv(minBlockX, step) * step;
            long endX = CeilDiv(maxBlockX, step) * step;

            long startZ = FloorDiv(minBlockZ, step) * step;
            long endZ = CeilDiv(maxBlockZ, step) * step;

            // Vertical lines clipped to circle
            for (long x = startX; x <= endX; x += step)
            {
                int sx = mapRect.Center.X + (int)Math.Round((x - cx) * zoom);
                if (sx < mapRect.Left || sx >= mapRect.Right) continue;

                float dx = (sx + 0.5f) - center.X;
                float dx2 = dx * dx;
                if (dx2 >= r2) continue;

                float half = (float)Math.Sqrt(r2 - dx2);

                int y0 = (int)Math.Ceiling(center.Y - half);
                int y1 = (int)Math.Floor(center.Y + half) + 1; // exclusive

                y0 = Math.Max(y0, mapRect.Top);
                y1 = Math.Min(y1, mapRect.Bottom);

                int h = y1 - y0;
                if (h > 0)
                    DrawRect(sb, new Rectangle(sx, y0, 1, h), gridC);
            }

            // Horizontal lines clipped to circle
            for (long z = startZ; z <= endZ; z += step)
            {
                int sy = mapRect.Center.Y + (int)Math.Round((z - cz) * zoom);
                if (sy < mapRect.Top || sy >= mapRect.Bottom) continue;

                float dy = (sy + 0.5f) - center.Y;
                float dy2 = dy * dy;
                if (dy2 >= r2) continue;

                float half = (float)Math.Sqrt(r2 - dy2);

                int x0 = (int)Math.Ceiling(center.X - half);
                int x1 = (int)Math.Floor(center.X + half) + 1; // exclusive

                x0 = Math.Max(x0, mapRect.Left);
                x1 = Math.Min(x1, mapRect.Right);

                int w = x1 - x0;
                if (w > 0)
                    DrawRect(sb, new Rectangle(x0, sy, w, 1), gridC);
            }
        }

        /// <summary>
        /// Ensures the 1x1 pixel texture is available.
        /// </summary>
        private static void EnsurePx(GraphicsDevice device)
        {
            if (_px != null && !_px.IsDisposed) return;

            _px = new Texture2D(device, 1, 1);
            _px.SetData(new[] { Color.White });
        }

        /// <summary>
        /// Draws cached biome cells for square shape (no clipping).
        /// </summary>
        private static void DrawBiomeCells(SpriteBatch sb, Rectangle mapRect)
        {
            if (_cellColors == null || _cellColors.Length == 0) return;

            int idx = 0;
            float cellW = mapRect.Width / (float)_cols;
            float cellH = mapRect.Height / (float)_rows;

            for (int y = 0; y < _rows; y++)
            {
                int py = (int)(mapRect.Top + y * cellH);
                int ph = Math.Max(1, (int)Math.Ceiling(cellH));

                for (int x = 0; x < _cols; x++, idx++)
                {
                    Color c = _cellColors[idx];
                    if (c.A == 0) continue;

                    int px = (int)(mapRect.Left + x * cellW);
                    int pw = Math.Max(1, (int)Math.Ceiling(cellW));

                    DrawRect(sb, new Rectangle(px, py, pw, ph), c);
                }
            }

            // Optional biome edges (dominant token boundaries).
            if (MinimapConfig.ShowBiomeEdges && _cellDom != null)
                DrawEdges(sb, mapRect, cellW, cellH);
        }

        /// <summary>
        /// Draws biome boundaries for square shape using adjacent-cell dominant token differences.
        /// </summary>
        private static void DrawEdges(SpriteBatch sb, Rectangle mapRect, float cellW, float cellH)
        {
            Color edge = Premul(MinimapConfig.BiomeEdgeColor);

            // Vertical boundaries
            for (int y = 0; y < _rows; y++)
            {
                for (int x = 1; x < _cols; x++)
                {
                    string a = _cellDom[y * _cols + (x - 1)];
                    string b = _cellDom[y * _cols + x];
                    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;
                    if (string.Equals(a, b, StringComparison.Ordinal)) continue;

                    int px = (int)(mapRect.Left + x * cellW);
                    int py = (int)(mapRect.Top + y * cellH);
                    int ph = Math.Max(1, (int)Math.Ceiling(cellH));
                    DrawRect(sb, new Rectangle(px, py, 1, ph), edge);
                }
            }

            // Horizontal boundaries
            for (int y = 1; y < _rows; y++)
            {
                for (int x = 0; x < _cols; x++)
                {
                    string a = _cellDom[(y - 1) * _cols + x];
                    string b = _cellDom[y * _cols + x];
                    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;
                    if (string.Equals(a, b, StringComparison.Ordinal)) continue;

                    int px = (int)(mapRect.Left + x * cellW);
                    int py = (int)(mapRect.Top + y * cellH);
                    int pw = Math.Max(1, (int)Math.Ceiling(cellW));
                    DrawRect(sb, new Rectangle(px, py, pw, 1), edge);
                }
            }
        }

        /// <summary>
        /// Draws the fixed-step chunk grid (square shape).
        /// Notes:
        /// - step is in world blocks; zoom only changes pixel spacing.
        /// - uses FloorDiv/CeilDiv to align correctly even for negative coordinates.
        /// </summary>
        private static void DrawChunkGrid(SpriteBatch sb, Rectangle mapRect, Vector3 playerLocalPos)
        {
            float zoom = MinimapState.ZoomPixelsPerBlock;
            if (zoom <= 0.000001f) return;

            // Fixed chunk size in blocks (world space).
            int step = Math.Max(1, MinimapConfig.GridStepBlocks);
            Color gridC = Premul(MinimapConfig.GridColor);

            double cx = playerLocalPos.X;
            double cz = playerLocalPos.Z;

            double halfW = mapRect.Width * 0.5;
            double halfH = mapRect.Height * 0.5;

            double minX = cx - halfW / zoom;
            double maxX = cx + halfW / zoom;
            double minZ = cz - halfH / zoom;
            double maxZ = cz + halfH / zoom;

            long minBlockX = (long)Math.Floor(minX);
            long maxBlockX = (long)Math.Ceiling(maxX);
            long minBlockZ = (long)Math.Floor(minZ);
            long maxBlockZ = (long)Math.Ceiling(maxZ);

            long startX = FloorDiv(minBlockX, step) * step;
            long endX = CeilDiv(maxBlockX, step) * step;

            long startZ = FloorDiv(minBlockZ, step) * step;
            long endZ = CeilDiv(maxBlockZ, step) * step;

            // Vertical chunk lines.
            for (long x = startX; x <= endX; x += step)
            {
                int sx = mapRect.Center.X + (int)Math.Round((x - cx) * zoom);

                // Rectangle.Right/Bottom are exclusive in XNA, so use >=.
                if (sx < mapRect.Left || sx >= mapRect.Right) continue;

                DrawRect(sb, new Rectangle(sx, mapRect.Top, 1, mapRect.Height), gridC);
            }

            // Horizontal chunk lines.
            for (long z = startZ; z <= endZ; z += step)
            {
                int sy = mapRect.Center.Y + (int)Math.Round((z - cz) * zoom);

                if (sy < mapRect.Top || sy >= mapRect.Bottom) continue;

                DrawRect(sb, new Rectangle(mapRect.Left, sy, mapRect.Width, 1), gridC);
            }
        }
        #endregion

        #region Frame / Outline

        /// <summary>
        /// Draws the map frame for the selected minimap shape.
        /// Summary: Square uses rect borders; Circle uses polyline outline.
        /// </summary>
        private static void DrawFrame(SpriteBatch sb, Rectangle mapRect)
        {
            if (MinimapConfig.MinimapShape == MinimapShape.Square)
            {
                DrawSquareFrame(sb, mapRect);
                return;
            }

            DrawCircleFrame(sb, mapRect);
        }

        /// <summary>Draws a rectangular frame (with optional thick outline).</summary>
        private static void DrawSquareFrame(SpriteBatch sb, Rectangle mapRect)
        {
            Color frame = Premul(MinimapConfig.FrameColor);

            // Outer frame.
            DrawRect(sb, new Rectangle(mapRect.Left, mapRect.Top, mapRect.Width, 1), frame);
            DrawRect(sb, new Rectangle(mapRect.Left, mapRect.Bottom - 1, mapRect.Width, 1), frame);
            DrawRect(sb, new Rectangle(mapRect.Left, mapRect.Top, 1, mapRect.Height), frame);
            DrawRect(sb, new Rectangle(mapRect.Right - 1, mapRect.Top, 1, mapRect.Height), frame);

            // Optional outline thickness (outside-ish feel).
            if (MinimapConfig.OutlineEnabled && MinimapConfig.OutlineThicknessPx > 0)
            {
                Color oc = Premul(MinimapConfig.OutlineColor);
                int t = MinimapConfig.OutlineThicknessPx;

                // Expand rect by t and draw a simple thick border via bands.
                Rectangle r = new Rectangle(mapRect.Left - t, mapRect.Top - t, mapRect.Width + t * 2, mapRect.Height + t * 2);

                DrawRect(sb, new Rectangle(r.Left, r.Top, r.Width, t), oc);
                DrawRect(sb, new Rectangle(r.Left, r.Bottom - t, r.Width, t), oc);
                DrawRect(sb, new Rectangle(r.Left, r.Top, t, r.Height), oc);
                DrawRect(sb, new Rectangle(r.Right - t, r.Top, t, r.Height), oc);
            }
        }

        /// <summary>Draws a circular outline frame (with optional thick outline).</summary>
        private static void DrawCircleFrame(SpriteBatch sb, Rectangle mapRect)
        {
            Vector2 center = new Vector2(mapRect.Center.X, mapRect.Center.Y);

            // Inscribed circle radius inside the square rect.
            float radius = (mapRect.Width * 0.5f) - 0.5f;

            // Outline first (thicker), then frame on top (thin).
            if (MinimapConfig.OutlineEnabled && MinimapConfig.OutlineThicknessPx > 0)
            {
                int t = MinimapConfig.OutlineThicknessPx;

                // Keep outline inside the map rect (looks nicer + avoids cutting off).
                float rOutline = Math.Max(1f, radius - (t * 0.5f));

                DrawCircleOutline(sb, center, rOutline, Premul(MinimapConfig.OutlineColor), t);
            }

            DrawCircleOutline(sb, center, radius, Premul(MinimapConfig.FrameColor), 1);
        }

        /// <summary>Draws a polyline circle outline in screen-space.</summary>
        private static void DrawCircleOutline(SpriteBatch sb, Vector2 center, float radius, Color color, int thicknessPx)
        {
            thicknessPx = Math.Max(1, thicknessPx);

            // More segments = smoother circle. This scales with size.
            int segments = Math.Max(48, (int)(radius * 0.9f));
            float step = MathHelper.TwoPi / segments;

            Vector2 prev = center + new Vector2((float)Math.Cos(0f), (float)Math.Sin(0f)) * radius;

            for (int i = 1; i <= segments; i++)
            {
                float a = i * step;
                Vector2 next = center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius;

                DrawLine(sb, prev, next, color, thicknessPx);

                prev = next;
            }
        }

        /// <summary>Draws a line using the 1x1 pixel texture stretched and rotated.</summary>
        private static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, int thicknessPx)
        {
            Vector2 delta = b - a;
            float len = delta.Length();
            if (len <= 0.0001f) return;

            float rot = (float)Math.Atan2(delta.Y, delta.X);

            // Draw a 1x1 pixel stretched into a line:
            // origin (0,0.5) means thickness is centered around the line.
            sb.Draw(_px,
                position: a,
                sourceRectangle: null,
                color: color,
                rotation: rot,
                origin: new Vector2(0f, 0.5f),
                scale: new Vector2(len, thicknessPx),
                effects: SpriteEffects.None,
                layerDepth: 0f);
        }
        #endregion

        #region Markers / Text

        /// <summary>
        /// Draws the local player dot in the center of the minimap.
        /// Summary:
        /// - Always draws the player square at map center.
        /// - Optionally draws a small facing indicator (triangle arrow) pointing where the player is looking.
        /// </summary>
        private static void DrawPlayerDot(SpriteBatch sb, Rectangle mapRect)
        {
            Color pc = Premul(MinimapConfig.PlayerColor);

            int sz = 6;
            int cx = mapRect.Center.X;
            int cy = mapRect.Center.Y;

            // --------------------------------------------------------------------
            // Optional facing indicator (triangle arrow).
            // --------------------------------------------------------------------
            if (MinimapConfig.PlayerFacingIndicator)
            {
                try
                {
                    float yaw = GetLocalPlayerYawRadians();

                    // yaw=0 faces world -Z => "up" on minimap.
                    // Convert yaw to screen-space direction where up is -Y.
                    Vector2 dir = new Vector2((float)Math.Sin(yaw), -(float)Math.Cos(yaw));
                    if (dir.LengthSquared() > 1e-6f) dir.Normalize();
                    else dir = new Vector2(0f, -1f);

                    Vector2 center = new Vector2(cx, cy);

                    // Start the indicator just outside the player square.
                    float startOffset = (sz * 0.5f) + MinimapConfig.PlayerFacingStartOffsetPx;

                    int len = MinimapConfig.PlayerFacingLengthPx;
                    int thick = MinimapConfig.PlayerFacingThicknessPx;

                    // Style switch (triangle vs. fallback line).
                    if (MinimapConfig.PlayerFacingUseTriangle)
                    {
                        int baseW = MinimapConfig.PlayerFacingTriangleBaseWidthPx;

                        // Tip and base placement.
                        Vector2 tip = center + dir * (startOffset + len);

                        // Perpendicular to dir (screen space).
                        Vector2 perp = new Vector2(-dir.Y, dir.X);

                        // Base sits closer to center than the tip.
                        Vector2 baseCenter = center + dir * (startOffset + (len * 0.45f));
                        Vector2 baseL      = baseCenter - perp * (baseW * 0.5f);
                        Vector2 baseR      = baseCenter + perp * (baseW * 0.5f);

                        // Fill first (so outline sits on top).
                        if (MinimapConfig.PlayerFacingTriangleFilled)
                        {
                            int step = MinimapConfig.PlayerFacingTriangleFillStepPx;
                            if (step < 1) step = 1;

                            DrawTriangleFilled(sb, tip, baseL, baseR, Premul(MinimapConfig.PlayerFacingColor), step);
                        }

                        // Outline on top.
                        if (thick > 0)
                        {
                            DrawTriangleOutline(sb, tip, baseL, baseR, Premul(MinimapConfig.PlayerFacingOutlineColor), thick);
                        }
                    }
                    else
                    {
                        // Simple fallback: a line/tick in the facing direction.
                        Vector2 a = center + dir * startOffset;
                        Vector2 b = center + dir * (startOffset + len);
                        DrawLine(sb, a, b, Premul(MinimapConfig.PlayerFacingColor), Math.Max(1, thick));
                    }
                }
                catch
                {
                    // Best-effort: Never break minimap drawing if something goes wrong.
                }
            }

            // --------------------------------------------------------------------
            // Player square (existing behavior).
            // --------------------------------------------------------------------
            int x = cx - sz / 2;
            int y = cy - sz / 2;

            DrawRect(sb, new Rectangle(x, y, sz, sz), pc);
        }

        #region Player Facing Triangle Helpers

        /// <summary>
        /// Draws a triangular facing indicator outline using 3 lines.
        /// Summary: Tip -> baseL -> baseR -> tip.
        /// </summary>
        private static void DrawTriangleOutline(SpriteBatch sb, Vector2 tip, Vector2 baseL, Vector2 baseR, Color color, int thicknessPx)
        {
            DrawLine(sb, tip, baseL, color, thicknessPx);
            DrawLine(sb, baseL, baseR, color, thicknessPx);
            DrawLine(sb, baseR, tip, color, thicknessPx);
        }

        /// <summary>
        /// Approximates a filled triangle by drawing multiple lines from the tip to points across the base.
        /// Summary:
        /// - stepPx controls fill density (1 = solid, 2-3 = cheaper).
        /// - Uses DrawLine to keep everything in the SpriteBatch + 1x1-pixel pipeline.
        /// </summary>
        private static void DrawTriangleFilled(SpriteBatch sb, Vector2 tip, Vector2 baseL, Vector2 baseR, Color color, int stepPx)
        {
            float baseLen = (baseR - baseL).Length();
            if (baseLen <= 0.5f) return;

            int steps = Math.Max(2, (int)(baseLen / Math.Max(1, stepPx)));

            for (int i = 0; i <= steps; i++)
            {
                float t = (steps == 0) ? 0f : (i / (float)steps);
                Vector2 p = Vector2.Lerp(baseL, baseR, t);
                DrawLine(sb, tip, p, color, 1);
            }
        }
        #endregion

        /// <summary>
        /// Draws centered readout text below the minimap (coordinates and/or biome).
        /// Notes:
        /// - Uses Floor() for stable integer grid coordinates.
        /// - Uses TrySampleBiomeColor(...) at player position for current biome readout.
        /// </summary>
        private static void DrawTextUnderMinimap(SpriteBatch sb, SpriteFont font, Rectangle mapRect, Vector3 playerLocalPos)
        {
            if (font == null) return;

            int y = mapRect.Bottom + MinimapConfig.TextSpacingPx;

            float scale = MinimapConfig.FontScale;

            // Current biome at player (center).
            string biomeLine = null;
            if (MinimapConfig.ShowCurrentBiome)
            {
                // Grab WGP context if present (seed + cfg).
                bool wgpActive = TryGetWorldGenPlusContext(out object wgpCfg, out int wgpSeed);

                int wgpMode = 0; // 0=rings, 1=square, 2=single, 3=random.
                bool wgpMirror = true;
                double wgpPeriod = 4400.0;

                if (wgpActive)
                {
                    wgpMode = GetSurfaceMode(wgpCfg);
                    wgpMirror = GetCfgBool(wgpCfg, "MirrorRepeat", true);
                    wgpPeriod = GetCfgDouble(wgpCfg, "RingPeriod", 4400.0);
                    if (wgpPeriod <= 0.0) wgpPeriod = 4400.0;
                }

                if (TrySampleBiomeColor(wgpActive, wgpCfg, wgpSeed, wgpMode, wgpMirror, wgpPeriod,
                                        (int)Math.Floor(playerLocalPos.X), (int)Math.Floor(playerLocalPos.Z),
                                        255, out _, out string dom))
                {
                    biomeLine = $"{ShortBiomeName(dom)}";
                }
                else
                {
                    biomeLine = "Unknown";
                }
            }

            string coordLine = null;
            if (MinimapConfig.MinimapCoordinates)
            {
                coordLine = $"{(int)Math.Floor(playerLocalPos.X)}, {(int)Math.Floor(playerLocalPos.Y)}, {(int)Math.Floor(playerLocalPos.Z)}";
            }

            // Draw centered lines.
            if (!string.IsNullOrEmpty(coordLine))
            {
                DrawCenteredText(sb, font, coordLine, mapRect.Center.X, y, scale, MinimapConfig.CoordinatesColor);
                y += (int)Math.Ceiling(font.LineSpacing * scale);
            }

            if (!string.IsNullOrEmpty(biomeLine))
            {
                DrawCenteredText(sb, font, biomeLine, mapRect.Center.X, y, scale, MinimapConfig.CoordinatesColor);
            }
        }

        /// <summary>
        /// Draws text centered horizontally around centerX.
        /// Notes: Optional outline uses simple 4-direction offsets.
        /// </summary>
        private static void DrawCenteredText(SpriteBatch sb, SpriteFont font, string text, int centerX, int y, float scale, Color c)
        {
            Vector2 sz = font.MeasureString(text) * scale;
            int x = centerX - (int)Math.Round(sz.X * 0.5f);

            // Optional outline by drawing 4 offsets.
            if (MinimapConfig.OutlineEnabled && MinimapConfig.OutlineThicknessPx > 0)
            {
                int t = MinimapConfig.OutlineThicknessPx;
                Color oc = Premul(MinimapConfig.OutlineColor);

                sb.DrawString(font, text, new Vector2(x - t, y), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, text, new Vector2(x + t, y), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, text, new Vector2(x, y - t), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, text, new Vector2(x, y + t), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            sb.DrawString(font, text, new Vector2(x, y), Premul(c), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
        #endregion

        #region Compass (N/E/S/W)

        /// <summary>
        /// Returns the local player's yaw (heading) in radians based on the camera forward vector.
        /// Summary:
        /// - Projects the camera's forward vector onto the XZ plane and normalizes it.
        /// - Defines "North" as world -Z (up on the minimap), so yaw=0 when facing -Z.
        /// - Uses atan2 to produce a stable signed angle suitable for rotating the compass.
        /// </summary>
        private static float GetLocalPlayerYawRadians()
        {
            try
            {
                var p = CastleMinerZGame.Instance?.LocalPlayer;
                if (p == null) return 0f;

                // Camera forward projected onto XZ plane.
                Vector3 f = p.FPSCamera.LocalToWorld.Forward;
                f.Y = 0f;

                if (f.LengthSquared() < 1e-6f)
                    return 0f;

                f.Normalize();

                // Define "North" as world -Z (up on the minimap), so yaw=0 when facing -Z.
                // atan2(x, -z): facing East (+X) => +90deg.
                return (float)Math.Atan2(f.X, -f.Z);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Rotates a 2D vector by an angle in radians.
        /// Summary: Standard 2D rotation matrix used to spin N/E/S/W directions around the minimap center.
        /// </summary>
        private static Vector2 Rotate2D(Vector2 v, float radians)
        {
            float c = (float)Math.Cos(radians);
            float s = (float)Math.Sin(radians);
            return new Vector2((v.X * c) - (v.Y * s), (v.X * s) + (v.Y * c));
        }

        /// <summary>
        /// Returns a point on (or just inside) the minimap border in the given direction.
        /// Summary:
        /// - Circle: returns center + dir * radius (minus padding).
        /// - Square: raycasts from center to the rectangle edge and then nudges inward by padding.
        /// </summary>
        /// <remarks>
        /// Assumes <paramref name="dirUnit"/> is a normalized direction vector.
        /// </remarks>
        private static Vector2 GetBorderPoint(Rectangle rect, Vector2 dirUnit, float pad)
        {
            Vector2 center = new Vector2(rect.Center.X, rect.Center.Y);

            // Circle: easy.
            if (MinimapConfig.MinimapShape == MinimapShape.Circle)
            {
                float r = (rect.Width * 0.5f) - 0.5f - pad;
                if (r < 1f) r = 1f;
                return center + dirUnit * r;
            }

            // Square: intersect ray with rectangle border, then pull inward by pad.
            float dx = dirUnit.X;
            float dy = dirUnit.Y;

            float tMin = float.PositiveInfinity;

            // Right/Left edge
            if (Math.Abs(dx) > 1e-6f)
            {
                float xEdge = (dx > 0f) ? (rect.Right - 1) : rect.Left;
                float tx    = (xEdge - center.X) / dx;
                if (tx > 0f && tx < tMin) tMin = tx;
            }

            // Bottom/Top edge
            if (Math.Abs(dy) > 1e-6f)
            {
                float yEdge = (dy > 0f) ? (rect.Bottom - 1) : rect.Top;
                float ty    = (yEdge - center.Y) / dy;
                if (ty > 0f && ty < tMin) tMin = ty;
            }

            if (float.IsInfinity(tMin))
                tMin = 0f;

            Vector2 p = center + dirUnit * tMin;

            // Move slightly inward so the glyph doesn't sit half outside the frame.
            return p - (dirUnit * pad);
        }

        /// <summary>
        /// Draws a small piece of text centered at a given screen-space point.
        /// Summary:
        /// - Computes centered position using MeasureString.
        /// - Optionally draws a simple 4-direction outline based on compass outline settings.
        /// - Draws final fill color on top (premultiplied for AlphaBlend).
        /// </summary>
        private static void DrawTextCenteredAt(SpriteBatch sb, SpriteFont font, string text, Vector2 center, float scale, Color fill)
        {
            Vector2 sz  = font.MeasureString(text) * scale;
            Vector2 pos = center - (sz * 0.5f);

            int t = MinimapConfig.CompassOutlineThicknessPx;
            if (t > 0)
            {
                Color oc = Premul(MinimapConfig.CompassOutlineColor);

                sb.DrawString(font, text, pos + new Vector2(-t, 0), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, text, pos + new Vector2(+t, 0), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, text, pos + new Vector2(0, -t), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                sb.DrawString(font, text, pos + new Vector2(0, +t), oc, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }

            sb.DrawString(font, text, pos, Premul(fill), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draws a N/E/S/W compass around the minimap border.
        /// Summary:
        /// - Computes player yaw and rotates the cardinal directions around the minimap center.
        /// - Places each letter on the border (circle) or nearest edge intersection (square).
        /// - Uses configurable padding, font scale, and color.
        /// </summary>
        private static void DrawCompassNSEW(SpriteBatch sb, SpriteFont font, Rectangle mapRect)
        {
            if (!MinimapConfig.ShowCompass) return;
            if (font == null) return;

            float yaw = GetLocalPlayerYawRadians();

            // Rotate opposite the player yaw so the player-facing direction becomes "up".
            // Example: facing East => E at top.
            float rot = MinimapConfig.CompassRotatesWithPlayer ? -yaw : 0f;

            // Base screen directions (unrotated): N up, E right, S down, W left.
            Vector2 dirN = Rotate2D(new Vector2(0f, -1f), rot);
            Vector2 dirE = Rotate2D(new Vector2(1f, 0f), rot);
            Vector2 dirS = Rotate2D(new Vector2(0f, 1f), rot);
            Vector2 dirW = Rotate2D(new Vector2(-1f, 0f), rot);

            // Ensure unit length.
            dirN.Normalize(); dirE.Normalize(); dirS.Normalize(); dirW.Normalize();

            float pad   = MinimapConfig.CompassPaddingPx;
            float scale = MinimapConfig.CompassFontScale;
            Color c     = MinimapConfig.CompassColor;

            DrawTextCenteredAt(sb, font, "N", GetBorderPoint(mapRect, dirN, pad), scale, c);
            DrawTextCenteredAt(sb, font, "E", GetBorderPoint(mapRect, dirE, pad), scale, c);
            DrawTextCenteredAt(sb, font, "S", GetBorderPoint(mapRect, dirS, pad), scale, c);
            DrawTextCenteredAt(sb, font, "W", GetBorderPoint(mapRect, dirW, pad), scale, c);
        }
        #endregion

        #region Primitive Drawing Helpers

        /// <summary>Draws a filled rectangle using the 1x1 pixel texture.</summary>
        private static void DrawRect(SpriteBatch sb, Rectangle r, Color c)
        {
            sb.Draw(_px, r, c);
        }

        /// <summary>
        /// Converts a color to premultiplied-alpha.
        /// Summary: SpriteBatch AlphaBlend expects premultiplied colors for translucent primitives to look correct.
        /// </summary>
        private static Color Premul(Color c)
        {
            if (c.A == 255) return c;

            int a = c.A;
            int r = (c.R * a + 127) / 255;
            int g = (c.G * a + 127) / 255;
            int b = (c.B * a + 127) / 255;
            return new Color((byte)r, (byte)g, (byte)b, (byte)a);
        }
        #endregion

        #region Math Helpers (Grid Snapping)

        /// <summary>
        /// Floor division for integers (long) with correct behavior for negative values.
        /// Summary: Returns ⌊a / b⌋ for b > 0 (unlike C# '/' which truncates toward zero).
        /// Useful for snapping world coordinates to fixed-size grid/chunk boundaries.
        /// </summary>
        private static long FloorDiv(long a, long b)
        {
            // b must be > 0
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
            // b must be > 0
            if (a >= 0) return (a + b - 1) / b;
            return -((-a) / b);
        }
        #endregion
    }
}