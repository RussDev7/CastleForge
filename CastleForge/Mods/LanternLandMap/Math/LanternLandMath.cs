/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System;

namespace LanternLandMap
{
    /// <summary>
    /// Lantern Land ring math helpers.
    ///
    /// Provides:
    ///  • Ring index selection from a target radius (n0).
    ///  • Ring start/end radii + floored X positions (what the block grid would see).
    ///  • Thickness / gap metrics (continuous and floored).
    ///  • Spawn-tower support via perfect-square ring indices.
    /// </summary>
    internal static class LanternLandMath
    {
        #region Constants

        /// <summary>
        /// K = 2^31. This is the fixed scale constant used by the game's ring formulas.
        /// </summary>
        public const double K = 2147483648d;

        /// <summary>
        /// 2^16 (used for tower X positions when n = k^2 => r_end = 2^16 * k).
        /// </summary>
        public const double TowerScale = 65536d;

        #endregion

        #region Window / Index Helpers

        /// <summary>
        /// Computes the first visible ring index (n0) for a target radius R:
        /// n0 = ceil(R^2 / (2*K)), clamped to at least 1.
        /// </summary>
        public static long FirstRingIndex(double targetRadius)
        {
            if (targetRadius <= 0d)
                return 1;

            double n0 = Math.Ceiling((targetRadius * targetRadius) / (2d * K));
            if (n0 < 1d) n0 = 1d;
            if (n0 > long.MaxValue) n0 = long.MaxValue;
            return (long)n0;
        }

        /// <summary>
        /// Builds a [n0..n1] inclusive ring window from a target radius and ring count.
        /// </summary>
        public static RingWindow ComputeWindow(double targetRadius, int ringCount)
        {
            ringCount = Math.Max(1, ringCount);

            long n0 = FirstRingIndex(targetRadius);
            long n1 = n0 + ringCount - 1;

            return new RingWindow(n0, n1);
        }

        /// <summary>
        /// Returns the ring index at a given radius (same shape as the n0 formula):
        /// n = ceil(r^2 / (2*K)), clamped to at least 1.
        /// </summary>
        public static long RingIndexAtRadius(double r)
        {
            if (r <= 0d) return 1;
            double n = Math.Ceiling((r * r) / (2d * K));
            if (n < 1d) n = 1d;
            if (n > long.MaxValue) n = long.MaxValue;
            return (long)n;
        }

        /// <summary>
        /// Checks whether a radius is inside the wall band for ring n:
        /// (2n - 1)K <= r^2 <= 2nK
        /// </summary>
        public static bool IsInsideWall(long n, double r)
        {
            if (n < 1) n = 1;
            double rr = r * r;
            double inner = ((2d * n) - 1d) * K;
            double outer = (2d * n) * K;
            return rr >= inner && rr <= outer;
        }
        #endregion

        #region Ring Builders

        /// <summary>
        /// Builds per-ring metrics for a window starting at n0 and spanning ringCount rings.
        ///
        /// For each ring:
        ///  • r_start(n) = sqrt((2n - 1)*K).
        ///  • r_end(n)   = sqrt((2n)*K).
        ///  • StartX/EndX are floored radii (what block placement "sees" on an axis).
        ///  • Thickness and Gap are computed in continuous space and also floored.
        ///  • HasTower is true when n is a perfect square.
        /// </summary>
        public static List<RingData> BuildRings(long n0, int ringCount)
        {
            ringCount = Math.Max(1, ringCount);

            var rings = new List<RingData>(ringCount);
            for (int i = 0; i < ringCount; i++)
            {
                long n = n0 + i;

                // r_start(n)      = sqrt((2n - 1)*K).
                // r_end(n)        = sqrt((2n)*K).
                double rStart      = Math.Sqrt(((2d * n) - 1d) * K);
                double rEnd        = Math.Sqrt((2d * n) * K);

                long startX        = (long)Math.Floor(rStart);
                long endX          = (long)Math.Floor(rEnd);

                double thickness   = rEnd - rStart;
                int thicknessFloor = (int)Math.Floor(thickness);

                // Gap to next wall: g(n) = r_start(n+1) - r_end(n).
                double rStartNext  = Math.Sqrt(((2d * (n + 1)) - 1d) * K);
                double gap         = rStartNext - rEnd;
                int gapFloor       = (int)Math.Floor(gap);

                double midRing     = (rStart + rEnd) * 0.5;
                double midGap      = (rEnd + rStartNext) * 0.5;

                bool hasTower      = IsPerfectSquare(n);

                rings.Add(new RingData(
                    n:              n,
                    rStart:         rStart,
                    rEnd:           rEnd,
                    startX:         startX,
                    endX:           endX,
                    thickness:      thickness,
                    thicknessFloor: thicknessFloor,
                    gap:            gap,
                    gapFloor:       gapFloor,
                    midRing:        midRing,
                    midGap:         midGap,
                    hasTower:       hasTower
                ));
            }

            return rings;
        }

        /// <summary>
        /// Builds the visible tower k values for the current ring window [n0..n1] where n = k^2.
        /// Returned list is [ceil(sqrt(n0)) .. floor(sqrt(n1))].
        /// </summary>
        public static List<long> BuildVisibleTowerKs(long n0, int ringCount)
        {
            long n1   = n0 + Math.Max(1, ringCount) - 1;

            long minK = (long)Math.Ceiling(Math.Sqrt(n0));
            long maxK = (long)Math.Floor(Math.Sqrt(n1));

            var ks    = new List<long>();
            if (maxK < minK) return ks;

            for (long k = minK; k <= maxK; k++)
                ks.Add(k);

            return ks;
        }
        #endregion

        #region Tower / Perfect-Square Tests

        /// <summary>
        /// Returns true if n is a perfect square (n = k^2).
        /// Used as the spawn-tower condition in the Lantern Land overlay.
        /// </summary>
        public static bool IsPerfectSquare(long n)
        {
            if (n <= 0) return false;
            long r = (long)Math.Round(Math.Sqrt(n));
            return r * r == n || (r - 1) * (r - 1) == n || (r + 1) * (r + 1) == n;
        }
        #endregion
    }

    #region Data Types

    /// <summary>
    /// Inclusive ring index window [N0..N1] used by the overlay.
    /// </summary>
    internal readonly struct RingWindow
    {
        /// <summary>First ring index in the window (inclusive).</summary>
        public readonly long N0;

        /// <summary>Last ring index in the window (inclusive).</summary>
        public readonly long N1;

        /// <summary>
        /// Creates a ring window [n0..n1] (inclusive).
        /// </summary>
        public RingWindow(long n0, long n1)
        {
            N0 = n0;
            N1 = n1;
        }
    }

    /// <summary>
    /// Computed metrics for a single ring index n:
    /// start/end radii, floored axis positions, thickness/gap (continuous + floored),
    /// midpoints, and whether the ring is a tower ring (perfect square).
    /// </summary>
    internal sealed class RingData
    {
        #region Identity / Radii

        /// <summary>Ring index (1-based).</summary>
        public readonly long N;

        /// <summary>Inner edge radius r_start(n).</summary>
        public readonly double RStart;

        /// <summary>Outer edge radius r_end(n).</summary>
        public readonly double REnd;

        #endregion

        #region Axis (Floored) Positions

        /// <summary>Floored inner edge on the +axis: floor(r_start).</summary>
        public readonly long StartX;

        /// <summary>Floored outer edge on the +axis: floor(r_end).</summary>
        public readonly long EndX;

        #endregion

        #region Thickness / Gap

        /// <summary>Continuous wall thickness T(n) = r_end - r_start.</summary>
        public readonly double Thickness;

        /// <summary>Floored wall thickness (useful for \"block-grid\" intuition).</summary>
        public readonly int ThicknessFloor;

        /// <summary>Continuous gap to next wall G(n) = r_start(n+1) - r_end(n).</summary>
        public readonly double Gap;

        /// <summary>Floored gap size (useful for \"block-grid\" intuition).</summary>
        public readonly int GapFloor;

        #endregion

        #region Midpoints

        /// <summary>Mid-ring radius: (r_start + r_end)/2.</summary>
        public readonly double MidRing;

        /// <summary>Mid-gap radius: (r_end + r_start_next)/2.</summary>
        public readonly double MidGap;

        #endregion

        #region Tower Flag

        /// <summary>True if this ring index is a perfect square (tower ring).</summary>
        public readonly bool HasTower;

        #endregion

        /// <summary>
        /// Creates a computed ring record for drawing/labeling.
        /// </summary>
        public RingData(
            long   n,
            double rStart,
            double rEnd,
            long   startX,
            long   endX,
            double thickness,
            int    thicknessFloor,
            double gap,
            int    gapFloor,
            double midRing,
            double midGap,
            bool   hasTower)
        {
            N              = n;
            RStart         = rStart;
            REnd           = rEnd;
            StartX         = startX;
            EndX           = endX;
            Thickness      = thickness;
            ThicknessFloor = thicknessFloor;
            Gap            = gap;
            GapFloor       = gapFloor;
            MidRing        = midRing;
            MidGap         = midGap;
            HasTower       = hasTower;
        }
    }
    #endregion
}