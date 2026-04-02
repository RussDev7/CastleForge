/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Restore360Water.Integrations.WorldGenPlus;
using System.Collections.Generic;
using System.Collections;
using System;

namespace Restore360Water
{
    /// <summary>
    /// Restore360Water-specific WorldGenPlus biome resolver.
    /// Summary:
    /// - Detects the dominant WorldGenPlus biome at a world X/Z.
    /// - Supports ring-based worlds, square-band worlds, single-biome worlds, and RandomRegions worlds.
    /// - Returns normalized Restore360Water biome names (Classic/Lagoon/etc).
    ///
    /// Notes:
    /// - For water-band purposes, blended areas resolve to the dominant primary biome.
    /// - If WGP is not active, callers should fall back to classic CMZ ring math.
    /// </summary>
    internal static class R360WWorldGenPlusSurfaceResolver
    {
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

        public static bool TryResolveBiomeName(int worldX, int worldZ, out string biomeName)
        {
            biomeName = null;

            if (!WorldGenPlusContext.TryGetWorldGenPlusContext(out object cfg, out int seed) || cfg == null)
                return false;

            string mode = (R360W_Settings.WorldGenPlusSurfaceMode ?? "Auto").Trim();
            int surfaceMode = WorldGenPlusMath.GetSurfaceMode(cfg);

            if (string.Equals(mode, "RandomRegions", StringComparison.OrdinalIgnoreCase))
                return TryResolveRandomRegionsBiomeName(cfg, seed, worldX, worldZ, out biomeName);

            if (string.Equals(mode, "Rings", StringComparison.OrdinalIgnoreCase))
                return TryResolveRingBiomeName(cfg, seed, worldX, worldZ, out biomeName);

            if (string.Equals(mode, "Single", StringComparison.OrdinalIgnoreCase))
                return TryResolveSingleBiomeName(cfg, out biomeName);

            // Auto:
            if (WorldGenPlusMath.IsSingleSurfaceMode(surfaceMode))
                return TryResolveSingleBiomeName(cfg, out biomeName);

            if (WorldGenPlusMath.IsRandomRegionsSurfaceMode(surfaceMode))
            {
                if (TryResolveRandomRegionsBiomeName(cfg, seed, worldX, worldZ, out biomeName))
                    return true;
            }

            return TryResolveRingBiomeName(cfg, seed, worldX, worldZ, out biomeName);
        }

        private static bool TryResolveSingleBiomeName(object cfg, out string biomeName)
        {
            try
            {
                string rawType = WorldGenPlusMath.GetSingleSurfaceBiome(
                    cfg,
                    "DNA.CastleMinerZ.Terrain.WorldBuilders.ClassicBiome");

                biomeName = NormalizeBiomeName(rawType);
                return !string.IsNullOrWhiteSpace(biomeName);
            }
            catch
            {
                biomeName = null;
                return false;
            }
        }

        private static bool TryResolveRingBiomeName(object cfg, int seed, int worldX, int worldZ, out string biomeName)
        {
            biomeName = null;

            try
            {
                double period = WorldGenPlusMath.GetCfgDouble(cfg, "RingPeriod", 4400.0);
                if (period <= 0.0)
                    period = 4400.0;

                int surfaceMode = WorldGenPlusMath.GetSurfaceMode(cfg);

                var ringsObj = WorldGenPlusMath.GetCfgObject(cfg, "Rings");
                if (!(ringsObj is IEnumerable ringsEnum))
                    return false;

                var bagObj = WorldGenPlusMath.GetCfgObject(cfg, "RandomRingBiomeChoices");
                List<string> bag = WorldGenPlusMath.ReadStringList(bagObj);
                if (bag == null || bag.Count == 0)
                    bag = DefaultVanillaBag;

                bool varyByPeriod = WorldGenPlusMath.GetCfgBool(cfg, "RandomRingsVaryByPeriod", false);

                double dist = WorldGenPlusMath.IsSquareSurfaceMode(surfaceMode)
                    ? Math.Max(Math.Abs((double)worldX), Math.Abs((double)worldZ))
                    : Math.Sqrt((double)worldX * worldX + (double)worldZ * worldZ);

                int patternIndex = 0;
                while (dist > period)
                {
                    dist -= period;
                    patternIndex++;
                }

                int ringIdx = 0;
                foreach (object it in ringsEnum)
                {
                    if (it == null)
                    {
                        ringIdx++;
                        continue;
                    }

                    double endRadius = WorldGenPlusMath.ReadDouble(it, "EndRadius", 0.0);
                    string rawType = WorldGenPlusMath.ReadString(it, "BiomeType", null);

                    if (endRadius > 0.0 && dist <= endRadius)
                    {
                        string resolved = ResolveRingToken(rawType, ringIdx, patternIndex, seed, varyByPeriod, bag);
                        biomeName = NormalizeBiomeName(resolved);
                        return !string.IsNullOrWhiteSpace(biomeName);
                    }

                    ringIdx++;
                }

                biomeName = null;
                return false;
            }
            catch
            {
                biomeName = null;
                return false;
            }
        }

        private static bool TryResolveRandomRegionsBiomeName(object cfg, int seed, int worldX, int worldZ, out string biomeName)
        {
            biomeName = null;

            try
            {
                int cellSize = Math.Max(32, WorldGenPlusMath.GetCfgInt(cfg, "RegionCellSize", 256));

                var bagObj = WorldGenPlusMath.GetCfgObject(cfg, "RandomSurfaceBiomeChoices");
                List<string> bag = WorldGenPlusMath.ReadStringList(bagObj);
                if (bag == null || bag.Count == 0)
                    return false;

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

                biomeName = NormalizeBiomeName(f0.BiomeType);
                return !string.IsNullOrWhiteSpace(biomeName);
            }
            catch
            {
                biomeName = null;
                return false;
            }
        }

        private static string ResolveRingToken(string raw, int ringIdx, int patternIndex, int seed, bool varyByPeriod, List<string> bag)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

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

            if (IsRandomToken(pick))
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

        private static bool IsRandomToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            return string.Equals(s, "@Random", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "Random", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBiomeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string s = raw.Trim();

            if (s.IndexOf("ClassicBiome", StringComparison.OrdinalIgnoreCase) >= 0)   return "Classic";
            if (s.IndexOf("LagoonBiome", StringComparison.OrdinalIgnoreCase) >= 0)    return "Lagoon";
            if (s.IndexOf("DesertBiome", StringComparison.OrdinalIgnoreCase) >= 0)    return "Desert";
            if (s.IndexOf("MountainBiome", StringComparison.OrdinalIgnoreCase) >= 0)  return "Mountain";
            if (s.IndexOf("ArcticBiome", StringComparison.OrdinalIgnoreCase) >= 0)    return "Arctic";
            if (s.IndexOf("DecentBiome", StringComparison.OrdinalIgnoreCase) >= 0)    return "Decent";
            if (s.IndexOf("HellFloorBiome", StringComparison.OrdinalIgnoreCase) >= 0) return "Hell";
            if (s.IndexOf("HellBiome", StringComparison.OrdinalIgnoreCase) >= 0)      return "Hell";
            if (s.IndexOf("CoastalBiome", StringComparison.OrdinalIgnoreCase) >= 0)   return "Coastal";
            if (s.IndexOf("CostalBiome", StringComparison.OrdinalIgnoreCase) >= 0)    return "Coastal";
            if (s.IndexOf("OceanBiome", StringComparison.OrdinalIgnoreCase) >= 0)     return "Ocean";

            return s;
        }
    }
}