/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System;

/// <summary>
/// NOTES / INTENT
/// - This runtime resolves a biome name and corresponding vertical water band at runtime
///   using classic CastleMinerZ ring-distance biome math.
/// - It is intentionally lightweight and config-driven:
///   - Biome names are resolved from world X/Z position.
///   - Per-biome MinY / MaxY bands are supplied externally through config.
///   - A global fallback band can be used when no enabled biome band is found.
/// - The goal is to keep water-band lookup simple and deterministic without requiring
///   full worldgen ownership or invasive biome-generation hooks.
/// </summary>
namespace Restore360Water
{
    /// <summary>
    /// Runtime biome + water-band resolver.
    ///
    /// Summary:
    /// - Resolves a classic CMZ biome name from world position.
    /// - Maps that biome name to a configured water band.
    /// - Optionally falls back to a global water band when no biome band is enabled.
    ///
    /// Notes:
    /// - Uses classic CMZ ring-biome distance math derived from CastleMinerZBuilder.
    /// - Intentionally keeps lookup simple and configurable rather than forcing full worldgen hooks.
    /// </summary>
    internal static class R360WBiomeWaterRuntime
    {
        #region Runtime Band Store

        /// <summary>
        /// Live per-biome water-band table keyed by biome name.
        /// Names are matched case-insensitively.
        /// </summary>
        private static readonly Dictionary<string, R360WBiomeBandConfig> _bands =
            new Dictionary<string, R360WBiomeBandConfig>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Band Registration

        /// <summary>
        /// Registers or replaces the water band for a biome name.
        /// If a null band is supplied, a default band config is stored instead.
        /// </summary>
        public static void SetBand(string biomeName, R360WBiomeBandConfig band)
        {
            if (string.IsNullOrWhiteSpace(biomeName))
                return;

            _bands[biomeName] = band ?? new R360WBiomeBandConfig();
        }
        #endregion

        #region Water Band Resolution

        /// <summary>
        /// Resolves the active biome water band from a full world position.
        /// Uses only X/Z for biome lookup; Y is not needed for biome identity.
        /// </summary>
        public static bool TryResolveBand(Vector3 worldPos, out string biomeName, out float minY, out float maxY, out bool useNativeWaterValues)
        {
            return TryResolveBand(worldPos.X, worldPos.Z, out biomeName, out minY, out maxY, out useNativeWaterValues);
        }

        /// <summary>
        /// Resolves the active biome water band from world X/Z coordinates.
        ///
        /// Resolution order:
        /// 1) If WorldGenPlus integration is enabled, try resolving the biome from the active WorldGenPlus surface layout.
        /// 2) If a matching enabled band is found for that biome, return its configured band values and native-water flag.
        /// 3) Otherwise resolve the biome from classic CMZ ring-distance math.
        /// 4) If a matching enabled band is found for that biome, return its configured band values and native-water flag.
        /// 5) Otherwise return the global fallback band if enabled, with native-water disabled.
        /// 6) Otherwise report failure with zeroed outputs and native-water disabled.
        /// </summary>
        public static bool TryResolveBand(float worldX, float worldZ, out string biomeName, out float minY, out float maxY, out bool useNativeWaterValues)
        {
            if (R360W_Settings.EnableWorldGenPlusIntegration)
            {
                if (R360WWorldGenPlusSurfaceResolver.TryResolveBiomeName(
                        (int)Math.Round(worldX),
                        (int)Math.Round(worldZ),
                        out string wgpBiome))
                {
                    if (!string.IsNullOrEmpty(wgpBiome) &&
                        _bands.TryGetValue(wgpBiome, out var wgpBand) &&
                        wgpBand != null && wgpBand.Enabled)
                    {
                        biomeName = wgpBiome;
                        minY = wgpBand.MinY;
                        maxY = wgpBand.MaxY;
                        useNativeWaterValues = wgpBand.UseNativeWaterValues;
                        return true;
                    }
                }
            }

            biomeName = ResolveBiomeName(worldX, worldZ);
            if (!string.IsNullOrEmpty(biomeName) &&
                _bands.TryGetValue(biomeName, out var band) &&
                band != null && band.Enabled)
            {
                minY = band.MinY;
                maxY = band.MaxY;
                useNativeWaterValues = band.UseNativeWaterValues;
                return true;
            }

            if (R360W_Settings.GlobalFallbackEnabled)
            {
                biomeName = string.IsNullOrEmpty(biomeName) ? "GlobalFallback" : biomeName + " (Fallback)";
                minY = R360W_Settings.GlobalFallbackMinY;
                maxY = R360W_Settings.GlobalFallbackMaxY;
                useNativeWaterValues = false;
                return true;
            }

            biomeName = biomeName ?? "None";
            minY = 0f;
            maxY = 0f;
            useNativeWaterValues = false;
            return false;
        }
        #endregion

        #region Biome Name Resolution

        /// <summary>
        /// Resolves the dominant classic CMZ ring biome for a world X/Z location.
        ///
        /// Notes:
        /// - Transition bands are intentionally assigned to the nearest dominant ring.
        /// - When biome overrides are disabled, a synthetic "Global" biome is returned
        ///   so callers can route into a single shared fallback-style behavior.
        /// - RingPeriod controls the width of the repeating biome cycle.
        /// - MirrorRepeat mirrors alternating cycles to mimic the classic ring pattern
        ///   rather than restarting each period identically.
        /// </summary>
        public static string ResolveBiomeName(float worldX, float worldZ)
        {
            if (!R360W_Settings.UseBiomeOverrides)
                return "Global";

            double dist = Math.Sqrt((worldX * worldX) + (worldZ * worldZ));
            float period = (R360W_Settings.RingPeriod <= 0f) ? 4400f : R360W_Settings.RingPeriod;

            if (R360W_Settings.MirrorRepeat)
            {
                int flips = 0;
                while (dist > period)
                {
                    dist -= period;
                    flips++;
                }

                if ((flips & 1) != 0)
                    dist = period - dist;
            }
            else
            {
                dist %= period;
            }

            // Classic CMZ dominant ring thresholds.
            if (dist < 250.0) return "Classic";
            if (dist < 950.0) return "Lagoon";
            if (dist < 1650.0) return "Desert";
            if (dist < 2350.0) return "Mountain";
            if (dist < 3300.0) return "Arctic";
            return "Decent";
        }
        #endregion
    }
}