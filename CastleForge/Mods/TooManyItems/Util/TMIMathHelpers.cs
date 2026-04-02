/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

namespace TooManyItems
{
    /// <summary>
    /// Tiny Clamp helpers for targets where System.Math.Clamp isn't available
    /// (older .NET Framework / XNA). These mirror the common semantics.
    /// </summary>
    internal static class TMIMathHelpers
    {
        public static int    Clamp(int v, int lo, int hi)          => (v < lo) ? lo : (v > hi ? hi : v);
        public static float  Clamp(float v, float lo, float hi)    => (v < lo) ? lo : (v > hi ? hi : v);
        public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);
    }
}