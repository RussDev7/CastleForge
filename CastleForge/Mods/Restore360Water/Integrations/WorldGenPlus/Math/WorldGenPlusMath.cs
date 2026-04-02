/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Reflection;
using System;

namespace Restore360Water.Integrations.WorldGenPlus
{
    /// <summary>
    /// WorldGenPlus math helpers extracted for Restore360Water.
    /// Summary: Mirrors key hashing + region math from WorldGenPlus so the map overlay can match world-gen exactly.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - Keep these functions in sync with WorldGenPlusBuilder when you update that project.
    /// - The HashU/Hash2D logic must match byte-for-byte or RandomRegions will not line up with the world.
    /// - This class is intentionally self-contained and avoids referencing WorldGenPlus types directly (reflection only).
    /// </remarks>
    internal static class WorldGenPlusMath
    {
        #region Feature Structs

        /// <summary>
        /// Lightweight Voronoi "feature point" used for RandomRegions sampling.
        /// Summary: Stores a feature's location, squared distance (D2), and biome type token.
        /// </summary>
        public struct FeatureLite
        {
            /// <summary>Feature X in world space.</summary>
            public double X;

            /// <summary>Feature Z in world space.</summary>
            public double Z;

            /// <summary>Squared distance from query point to this feature (smaller = closer).</summary>
            public double D2;

            /// <summary>Biome type token for this feature (usually a full type name string).</summary>
            public string BiomeType;
        }
        #endregion

        #region Integer / Index Helpers

        /// <summary>
        /// Floor-division for integers, matching mathematical floor(a/b) even for negative values.
        /// Summary: Needed for stable cell addressing in grid-based world-gen math.
        /// </summary>
        public static int FloorDiv(int a, int b)
        {
            if (b == 0) return 0;
            int q = a / b;
            int r = a % b;
            if ((r != 0) && ((r < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>
        /// Positive modulo, returning a value in [0..m-1] for m > 0.
        /// Summary: Used for ring segment indexing and pipe-choice selection.
        /// </summary>
        public static int Mod(int a, int m)
        {
            if (m == 0) return 0;
            int r = a % m;
            return (r < 0) ? (r + m) : r;
        }
        #endregion

        #region RandomRegions Feature Generation (Voronoi)

        /// <summary>
        /// Creates a deterministic feature point for a given cell, using seed + cell coords.
        /// Summary: This is the core that makes RandomRegions stable and seed-correct.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - The "bag" acts as weighted choices: duplicates increase probability.
        /// - Offsets (ox/oz) are derived from hash output to scatter features inside the cell.
        /// </remarks>
        public static FeatureLite MakeFeatureLite(int cellX, int cellZ, int cellSize, int seed, List<string> bag)
        {
            uint h = Hash2D((uint)cellX, (uint)cellZ, (uint)seed);

            // Random offsets inside the cell.
            float ox = U01(h);
            h = HashU(h ^ 0xB5297A4Du);
            float oz = U01(h);

            // Final hash for biome pick.
            h = HashU(h ^ 0x68E31DA4u);

            int idx = (int)(h % (uint)bag.Count);
            string biomeType = bag[idx];

            double fx = (cellX * (double)cellSize) + ox * cellSize;
            double fz = (cellZ * (double)cellSize) + oz * cellSize;

            return new FeatureLite { X = fx, Z = fz, BiomeType = biomeType, D2 = double.MaxValue };
        }

        /// <summary>
        /// Inserts f into (a,b) as the best two (smallest D2), keeping order: a is best, b is second best.
        /// Summary: Helps find the closest and second-closest Voronoi features efficiently.
        /// </summary>
        public static void InsertBest2(ref FeatureLite a, ref FeatureLite b, FeatureLite f)
        {
            if (f.D2 < a.D2)
            {
                b = a;
                a = f;
            }
            else if (f.D2 < b.D2)
            {
                b = f;
            }
        }

        /// <summary>
        /// Computes the signed distance from a point to the perpendicular bisector between two features.
        /// Summary: Used to estimate blend amount between the closest two regions.
        /// </summary>
        /// <remarks>
        /// The sign indicates which side of the bisector the point lies on.
        /// When the features are nearly identical (len ~ 0), returns false.
        /// </remarks>
        public static bool TryGetBisectorSignedDistance(int worldX, int worldZ, FeatureLite a, FeatureLite b, out double signedDist)
        {
            double dx = b.X - a.X;
            double dz = b.Z - a.Z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6)
            {
                signedDist = 0.0;
                return false;
            }

            // Midpoint between features.
            double mx = (a.X + b.X) * 0.5;
            double mz = (a.Z + b.Z) * 0.5;

            // Unit direction from a -> b.
            double nx = dx / len;
            double nz = dz / len;

            signedDist = ((worldX - mx) * nx) + ((worldZ - mz) * nz);
            return true;
        }
        #endregion

        #region Blend Curves

        /// <summary>
        /// Applies a symmetric power curve around 0.5 (ease-in/out) without shifting endpoints.
        /// Summary: Useful for "sharper" or "softer" blends while keeping t=0 and t=1 fixed.
        /// </summary>
        public static float ApplySymmetricPower(float t, float power)
        {
            if (power <= 0f || Math.Abs(power - 1f) < 1e-6f) return t;

            if (t < 0.5f)
            {
                float u = t / 0.5f;
                u = (float)Math.Pow(u, power);
                return 0.5f * u;
            }
            else
            {
                float u = (1f - t) / 0.5f;
                u = (float)Math.Pow(u, power);
                return 1f - 0.5f * u;
            }
        }

        /// <summary>
        /// Smoothstep easing: t*t*(3-2t).
        /// Summary: Standard Hermite smoothing for blend factors.
        /// </summary>
        public static float SmoothStep(float t) => t * t * (3f - 2f * t);

        #endregion

        #region Hashing (Must Match WorldGenPlus)

        /// <summary>
        /// WorldGenPlus hash mixer (uint -> uint).
        /// Summary: MUST match WorldGenPlusBuilder.Hash() to keep RandomRegions layout identical.
        /// </summary>
        /// <remarks>
        /// Notes:
        /// - If RandomRegions looks "shifted" vs the real world, this is the first place to check.
        /// - Keep constants and shifts exactly the same.
        /// </remarks>
        public static uint HashU(uint x)
        {
            unchecked
            {
                // Match WorldGenPlusBuilder.Hash()
                x += 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>
        /// Mixes (x,z,seed) into a single hash.
        /// Summary: Deterministic per-cell/per-seed key used to derive offsets and biome picks.
        /// </summary>
        public static uint Hash2D(uint x, uint z, uint seed)
        {
            unchecked
            {
                uint h = 0u;
                h ^= x * 0x8DA6B343u;
                h ^= z * 0xD8163841u;
                h ^= seed * 0xCB1AB31Fu;
                return HashU(h);
            }
        }

        /// <summary>
        /// Converts a hash to a uniform float in [0..1).
        /// Summary: Uses 24-bit mantissa extraction for a stable fractional value.
        /// </summary>
        public static float U01(uint h)
        {
            // 24-bit mantissa -> [0..1).
            return (h & 0x00FFFFFFu) / 16777216f;
        }
        #endregion

        #region Reflection Config Access (WorldGenPlus Cfg)

        /// <summary>
        /// Reflects a config field/property by name (public or private).
        /// Summary: Lets Minimap read WorldGenPlus config without referencing its assembly directly.
        /// </summary>
        public static object GetCfgObject(object cfg, string name)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(name)) return null;

            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            return cfg.GetType().GetProperty(name, F)?.GetValue(cfg, null) ??
                   cfg.GetType().GetField(name, F)?.GetValue(cfg);
        }

        /// <summary>
        /// Reads an int config value via reflection with a safe fallback.
        /// Summary: Supports common numeric runtime types (int/long/float/double/etc).
        /// </summary>
        public static int GetCfgInt(object cfg, string name, int fallback)
        {
            try
            {
                object v = GetCfgObject(cfg, name);
                if (v == null) return fallback;
                if (v is int i) return i;
                if (v is long l) return (int)l;
                if (v is float f) return (int)f;
                if (v is double d) return (int)d;
                return Convert.ToInt32(v);
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Reads a bool config value via reflection with a safe fallback.
        /// Summary: Keeps Minimap resilient when WorldGenPlus changes types/fields.
        /// </summary>
        public static bool GetCfgBool(object cfg, string name, bool fallback)
        {
            try
            {
                object v = GetCfgObject(cfg, name);
                if (v == null) return fallback;
                if (v is bool b) return b;
                return Convert.ToBoolean(v);
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Reads a double config value via reflection with a safe fallback.
        /// Summary: Supports common numeric runtime types (float/double/int/long/etc).
        /// </summary>
        public static double GetCfgDouble(object cfg, string name, double fallback)
        {
            try
            {
                object v = GetCfgObject(cfg, name);
                if (v == null) return fallback;
                if (v is float f) return f;
                if (v is double d) return d;
                if (v is int i) return i;
                if (v is long l) return l;
                return Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Reads the WorldGenPlus surface mode with a safe fallback.
        /// Summary: Centralizes surface-mode lookups so callers don't hardcode raw numeric values.
        /// </summary>
        public static int GetSurfaceMode(object cfg)
        {
            return GetCfgInt(cfg, "SurfaceMode", 0);
        }

        /// <summary>
        /// Returns true when the active surface layout uses square-band distance.
        /// Summary: Keeps callers aligned with WorldGenPlus square-mode semantics.
        /// </summary>
        public static bool IsSquareSurfaceMode(int mode)
        {
            return mode == 1;
        }

        /// <summary>
        /// Returns true when the active surface layout is a single biome everywhere.
        /// Summary: Used by overlays/resolvers to bypass RandomRegions and 1D band assumptions.
        /// </summary>
        public static bool IsSingleSurfaceMode(int mode)
        {
            return mode == 2;
        }

        /// <summary>
        /// Returns true when the active surface layout uses RandomRegions sampling.
        /// Summary: Centralized helper for 2D Voronoi-based surface lookup.
        /// </summary>
        public static bool IsRandomRegionsSurfaceMode(int mode)
        {
            return mode == 3;
        }

        /// <summary>
        /// Returns true when the active surface layout should be sampled as 1D repeated bands.
        /// Summary: Rings, square bands, and single-biome mode all share the band pipeline.
        /// </summary>
        public static bool UsesBandSurfaceSampling(int mode)
        {
            return mode == 0 || mode == 1 || mode == 2;
        }

        /// <summary>
        /// Reads the configured single-biome token with a safe fallback.
        /// Summary: Lets each project resolve Single mode without duplicating reflection code.
        /// </summary>
        public static string GetSingleSurfaceBiome(object cfg, string fallback)
        {
            return ReadString(cfg, "SingleSurfaceBiome", fallback);
        }
        #endregion

        #region Reflection Readers (Generic)

        /// <summary>
        /// Reads a member (field/property) as string from an arbitrary object.
        /// Summary: Convenience wrapper used when iterating WorldGenPlus ring items via reflection.
        /// </summary>
        public static string ReadString(object obj, string name, string fallback)
        {
            try
            {
                if (obj == null) return fallback;

                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                object v = obj.GetType().GetProperty(name, F)?.GetValue(obj, null) ??
                           obj.GetType().GetField(name, F)?.GetValue(obj);

                return (v == null) ? fallback : v.ToString();
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Reads a member (field/property) as double from an arbitrary object.
        /// Summary: Supports common numeric types, falling back safely if conversion fails.
        /// </summary>
        public static double ReadDouble(object obj, string name, double fallback)
        {
            try
            {
                if (obj == null) return fallback;

                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                object v = obj.GetType().GetProperty(name, F)?.GetValue(obj, null) ??
                           obj.GetType().GetField(name, F)?.GetValue(obj);

                if (v == null) return fallback;
                if (v is float f) return f;
                if (v is double d) return d;
                if (v is int i) return i;
                if (v is long l) return l;
                return Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Attempts to read any enumerable into a List<string>.
        /// Summary: Used for bag/choices lists (duplicates allowed = weighting).
        /// </summary>
        public static List<string> ReadStringList(object v)
        {
            try
            {
                if (v == null) return null;

                List<string> list = new List<string>();

                if (v is IEnumerable<string> es)
                {
                    foreach (var s in es) if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    return list;
                }

                if (v is System.Collections.IEnumerable e)
                {
                    foreach (var it in e)
                    {
                        if (it == null) continue;
                        string s = it.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                    return list;
                }

                return null;
            }
            catch { return null; }
        }
        #endregion
    }
}