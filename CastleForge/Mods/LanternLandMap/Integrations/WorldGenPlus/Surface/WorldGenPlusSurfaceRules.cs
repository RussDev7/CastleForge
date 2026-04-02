/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System;

namespace LanternLandMap.Integrations.WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus surface resolution helpers (rings / square bands / single biome / random regions).
    /// Summary: Mirrors WorldGenPlus "surface rules" so LanternLandMap can preview biomes correctly.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - This file should change when WorldGenPlus surface logic changes.
    /// </remarks>
    internal static class WorldGenPlusSurfaceRules
    {
        #region Constants / Defaults

        /// <summary>Fallback bag used if cfg does not provide one.</summary>
        private static readonly List<string> DefaultVanillaBag = new List<string>
        {
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.LagoonBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DesertBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.MountainBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.ArcticBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.DecentBiome",
            "DNA.CastleMinerZ.Terrain.WorldBuilders.HellFloorBiome",
        };
        #endregion

        #region Bands (Rings / Square Bands)

        /// <summary>
        /// Builds the WorldGenPlus surface bands in effective-distance space (0..RingPeriod).
        /// Summary: Produces solid and blended segments that match the generator's ring logic.
        /// </summary>
        /// <remarks>
        /// Inputs:
        /// - patternIndex: Usually the "segment index" used for pipe patterns and optional vary-by-period randomization.
        /// - alpha:        Used for mapping to map colors (presentation layer).
        /// </remarks>
        public static bool TryBuildWorldGenPlusBands(
            object cfg,
            int seed,
            int patternIndex,
            byte alpha,
            out LanternLandMapScreen.BiomeBand[] bands) // <-- If BiomeBand is nested; otherwise change type.
        {
            bands = null;

            try
            {
                double period = WorldGenPlusMath.GetCfgDouble(cfg, "RingPeriod", 4400.0);
                if (period <= 0.0) period = 4400.0;

                double transitionWidth = Math.Max(0.0, WorldGenPlusMath.GetCfgDouble(cfg, "TransitionWidth", 0.0));

                int surfaceMode = WorldGenPlusMath.GetSurfaceMode(cfg);

                // Single-biome mode:
                // Build one full-period solid band so all existing 1D band rendering / readout code keeps working.
                if (WorldGenPlusMath.IsSingleSurfaceMode(surfaceMode))
                {
                    string biomeType = WorldGenPlusMath.GetSingleSurfaceBiome(
                        cfg,
                        "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome");

                    var c = WorldGenPlusBiomePalette.MapBiomeTypeToColor(biomeType, alpha, seed);

                    bands = new[]
                    {
                        new LanternLandMapScreen.BiomeBand(0.0, period, c, c, false, biomeType, biomeType)
                    };

                    return true;
                }

                // cfg.Rings is usually a list of items with { EndRadius, BiomeType }.
                var ringsObj = WorldGenPlusMath.GetCfgObject(cfg, "Rings");
                if (!(ringsObj is System.Collections.IEnumerable ringsEnum))
                    return false;

                // Random ring bag.
                var bagObj = WorldGenPlusMath.GetCfgObject(cfg, "RandomRingBiomeChoices");
                var bag = WorldGenPlusMath.ReadStringList(bagObj);
                if (bag == null || bag.Count == 0)
                    bag = DefaultVanillaBag;

                bool varyByPeriod = WorldGenPlusMath.GetCfgBool(cfg, "RandomRingsVaryByPeriod", false);

                // Resolve biome token (pipes + @Random).
                string ResolveToken(string raw, int ringIdx)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return raw;

                    string pick = raw;

                    if (raw.IndexOf('|') >= 0)
                    {
                        string[] parts = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts != null && parts.Length > 0)
                        {
                            int pi = WorldGenPlusMath.Mod(patternIndex, parts.Length);
                            pick = (parts[pi] ?? "").Trim();
                        }
                    }

                    // @Random (or "Random").
                    if (IsRingRandomToken(pick))
                    {
                        int periodKey = varyByPeriod ? patternIndex : 0;

                        unchecked
                        {
                            uint h = 0u;
                            h ^= (uint)seed * 0xCB1AB31Fu;
                            h ^= (uint)ringIdx * 0x8DA6B343u;
                            h ^= (uint)periodKey * 0xD8163841u;
                            h = WorldGenPlusMath.HashU(h ^ 0xA2C79D1Fu);

                            int idx = (int)(h % (uint)bag.Count);
                            pick = bag[idx];
                        }
                    }

                    return pick;
                }

                // Read ring list into local arrays.
                var ends = new List<double>();
                var types = new List<string>();

                int ringIdx2 = 0;
                foreach (var it in ringsEnum)
                {
                    if (it == null) continue;

                    double endR = WorldGenPlusMath.ReadDouble(it, "EndRadius", 0.0);
                    string rawT = WorldGenPlusMath.ReadString(it, "BiomeType", null);

                    if (endR <= 0.0) continue;

                    ends.Add(endR);
                    types.Add(ResolveToken(rawT, ringIdx2));
                    ringIdx2++;
                }

                if (ends.Count == 0)
                    return false;

                // Build effective bands (solid + transition bands), matching the builder rules.
                var list = new List<LanternLandMapScreen.BiomeBand>(ends.Count * 2);

                double coreStart = 0.0;

                for (int i = 0; i < ends.Count; i++)
                {
                    double coreEnd = ends[i];
                    if (coreEnd <= coreStart) continue;

                    string aType = types[i];

                    // Decent special case: no inserted transition.
                    if (IsDecentBiomeType(aType))
                    {
                        var c = WorldGenPlusBiomePalette.MapBiomeTypeToColor(aType, alpha, seed);
                        list.Add(new LanternLandMapScreen.BiomeBand(coreStart, coreEnd, c, c, false, aType, aType));
                        coreStart = coreEnd;
                        continue;
                    }

                    // Solid core.
                    var aC = WorldGenPlusBiomePalette.MapBiomeTypeToColor(aType, alpha, seed);
                    list.Add(new LanternLandMapScreen.BiomeBand(coreStart, coreEnd, aC, aC, false, aType, aType));

                    if (i + 1 < ends.Count)
                    {
                        string bType   = types[i + 1];
                        double nextEnd = ends[i + 1];

                        // Next is Decent: Promoted, skip normal transition.
                        if (IsDecentBiomeType(bType))
                        {
                            var dC = WorldGenPlusBiomePalette.MapBiomeTypeToColor(bType, alpha, seed);
                            list.Add(new LanternLandMapScreen.BiomeBand(coreEnd, nextEnd, dC, dC, false, bType, bType));
                            coreStart = nextEnd;
                            i++; // Consumed the next ring.
                            continue;
                        }

                        // Normal transition.
                        double wHere = Math.Min(transitionWidth, Math.Max(0.0, nextEnd - coreEnd));
                        if (wHere > 0.0)
                        {
                            var bC = WorldGenPlusBiomePalette.MapBiomeTypeToColor(bType, alpha, seed);
                            list.Add(new LanternLandMapScreen.BiomeBand(coreEnd, coreEnd + wHere, aC, bC, true, aType, bType));
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

                // Fill remainder to period with Hell.
                if (coreStart < period)
                {
                    var c = WorldGenPlusBiomePalette.MapBiomeTypeToColor("HellFloorBiome", alpha, seed);
                    list.Add(new LanternLandMapScreen.BiomeBand(coreStart, period, c, c, false, "HellFloorBiome", "HellFloorBiome"));
                }

                // Clamp.
                var finalList = new List<LanternLandMapScreen.BiomeBand>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    double s = Math.Max(0.0, b.Start);
                    double e = Math.Min(period, b.End);
                    if (e > s + 1e-9)
                        finalList.Add(new LanternLandMapScreen.BiomeBand(s, e, b.ColorA, b.ColorB, b.IsBlend, b.TypeA, b.TypeB));
                }

                bands = finalList.ToArray();
                return bands != null && bands.Length > 0;
            }
            catch
            {
                bands = null;
                return false;
            }
        }
        #endregion

        #region RandomRegions Sampling

        /// <summary>
        /// Samples the RandomRegions surface at a world X/Z.
        /// Summary: Returns primary biome type + optional secondary biome type and blend weight (0..1).
        /// </summary>
        public static bool TrySampleRandomRegions(object cfg, int seed, int worldX, int worldZ, out string aType, out string bType, out float bW)
        {
            aType = null;
            bType = null;
            bW    = 0f;

            try
            {
                int   cellSize   = Math.Max(32, WorldGenPlusMath.GetCfgInt(cfg, "RegionCellSize", 256));
                float blendWidth = Math.Max(0f, (float)WorldGenPlusMath.GetCfgDouble(cfg, "RegionBlendWidth", 0.0));
                float power      = Math.Max(0.10f, (float)WorldGenPlusMath.GetCfgDouble(cfg, "RegionBlendPower", 1.0));

                // Bag of biomes used for region features.
                var bagObj = WorldGenPlusMath.GetCfgObject(cfg, "RandomSurfaceBiomeChoices");
                var bag    = WorldGenPlusMath.ReadStringList(bagObj);
                if (bag == null || bag.Count == 0)
                    bag = DefaultVanillaBag;

                // Nearest 2 features.
                int cx = WorldGenPlusMath.FloorDiv(worldX, cellSize);
                int cz = WorldGenPlusMath.FloorDiv(worldZ, cellSize);

                WorldGenPlusMath.FeatureLite f0 = default;
                WorldGenPlusMath.FeatureLite f1 = default;
                f0.D2 = double.MaxValue;
                f1.D2 = double.MaxValue;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cxx = cx + dx;
                        int czz = cz + dz;

                        var f = WorldGenPlusMath.MakeFeatureLite(cxx, czz, cellSize, seed, bag);

                        double ddx = worldX - f.X;
                        double ddz = worldZ - f.Z;
                        f.D2 = ddx * ddx + ddz * ddz;

                        WorldGenPlusMath.InsertBest2(ref f0, ref f1, f);
                    }
                }

                if (string.IsNullOrWhiteSpace(f0.BiomeType))
                    return false;

                aType = f0.BiomeType;
                bType = f1.BiomeType;

                // Decent special-case.
                if (IsDecentBiomeType(aType))
                {
                    bType = null;
                    bW = 0f;
                    return true;
                }

                if (IsDecentBiomeType(bType))
                    bType = null;

                if (blendWidth <= 0f || string.IsNullOrWhiteSpace(bType))
                {
                    bType = null;
                    bW = 0f;
                    return true;
                }

                // Signed distance to bisector.
                if (WorldGenPlusMath.TryGetBisectorSignedDistance(worldX, worldZ, f0, f1, out double signedDist))
                {
                    float half = blendWidth * 0.5f;

                    if (Math.Abs(signedDist) >= half)
                    {
                        bool onASide = signedDist < 0.0;
                        string pick = onASide ? aType : bType;

                        aType = pick;
                        bType = null;
                        bW = 0f;
                        return true;
                    }

                    float t = (float)((signedDist + half) / blendWidth);
                    t = MathHelper.Clamp(t, 0f, 1f);

                    t = WorldGenPlusMath.ApplySymmetricPower(t, power);
                    t = WorldGenPlusMath.SmoothStep(t);

                    bW = t;
                    return true;
                }

                // Fallback: inverse-distance weighting.
                double dA = Math.Sqrt(f0.D2);
                double dB = Math.Sqrt(f1.D2);

                const double EPS = 1e-6;
                double wa = 1.0 / Math.Pow(dA + EPS, power);
                double wb = 1.0 / Math.Pow(dB + EPS, power);
                double sum = wa + wb;

                bW = (sum > 0.0) ? (float)(wb / sum) : 0f;
                bW = WorldGenPlusMath.SmoothStep(MathHelper.Clamp(bW, 0f, 1f));
                return true;
            }
            catch
            {
                aType = null;
                bType = null;
                bW = 0f;
                return false;
            }
        }
        #endregion

        #region Token / Biome Helpers

        /// <summary>
        /// Returns true if the token indicates "pick from random bag".
        /// Summary: Supports both "@Random" and "Random" for forgiving config input.
        /// </summary>
        public static bool IsRingRandomToken(string s)
        {
            return !string.IsNullOrWhiteSpace(s) &&
                   (string.Equals(s, "@Random", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, "Random", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns true if the biome token is the special Decent biome.
        /// Summary: Decent has special behavior in transitions and random-region blending.
        /// </summary>
        public static bool IsDecentBiomeType(string s)
        {
            return !string.IsNullOrWhiteSpace(s) &&
                   s.IndexOf("DecentBiome", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        #endregion
    }
}