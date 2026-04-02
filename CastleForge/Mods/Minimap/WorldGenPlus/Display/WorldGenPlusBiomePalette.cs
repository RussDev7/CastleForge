/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System;

namespace LanternLandMap.Integrations.WorldGenPlus
{
    /// <summary>
    /// Biome display helpers for LanternLandMap (colors + friendly names).
    /// Summary: Converts WorldGenPlus biome type tokens into map colors and readable names.
    /// </summary>
    /// <remarks>
    /// Notes:
    /// - Keep "known palette" in sync with whatever the map rendering uses.
    /// - Unknown/custom biomes get a deterministic random color derived from (seed + full type name).
    /// </remarks>
    internal static class WorldGenPlusBiomePalette
    {
        #region Names

        /// <summary>
        /// Returns a short friendly name for a biome type token.
        /// Summary: Used for hover/readout text (custom biomes are namespace-stripped).
        /// </summary>
        public static string ShortBiomeName(string biomeType)
        {
            if (string.IsNullOrWhiteSpace(biomeType)) return "Unknown";

            if (biomeType.IndexOf("ClassicBiome",  StringComparison.OrdinalIgnoreCase) >= 0) return "Classic";
            if (biomeType.IndexOf("LagoonBiome",   StringComparison.OrdinalIgnoreCase) >= 0) return "Lagoon";
            if (biomeType.IndexOf("DesertBiome",   StringComparison.OrdinalIgnoreCase) >= 0) return "Desert";
            if (biomeType.IndexOf("MountainBiome", StringComparison.OrdinalIgnoreCase) >= 0) return "Mountain";
            if (biomeType.IndexOf("ArcticBiome",   StringComparison.OrdinalIgnoreCase) >= 0) return "Arctic";
            if (biomeType.IndexOf("DecentBiome",   StringComparison.OrdinalIgnoreCase) >= 0) return "Decent";
            if (biomeType.IndexOf("Hell",          StringComparison.OrdinalIgnoreCase) >= 0) return "Hell";

            int dot = biomeType.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < biomeType.Length) return biomeType.Substring(dot + 1);
            return biomeType;
        }

        /// <summary>
        /// Maps known palette colors back to short names for display.
        /// Summary: Used when the readout only has colors (bands stored as colors instead of type tokens).
        /// </summary>
        public static string ShortBiomeNameFromColor(Color c)
        {
            if (c == LanternLandMapConfig.ClassicBiomeColor)  return "Classic";
            if (c == LanternLandMapConfig.LagoonBiomeColor)   return "Lagoon";
            if (c == LanternLandMapConfig.DesertBiomeColor)   return "Desert";
            if (c == LanternLandMapConfig.MountainBiomeColor) return "Mountain";
            if (c == LanternLandMapConfig.ArcticBiomeColor)   return "Arctic";
            if (c == LanternLandMapConfig.DecentBiomeColor)   return "Decent";
            if (c == LanternLandMapConfig.HellBiomeColor)     return "Hell";
            return "Custom";
        }
        #endregion

        #region Colors

        /// <summary>
        /// Maps a biome type token to a map color (with alpha applied).
        /// Summary: Known vanilla biomes use config palette; unknown/custom biomes get deterministic random colors.
        /// </summary>
        public static Color MapBiomeTypeToColor(string biomeType, byte alpha, int seed)
        {
            if (string.IsNullOrWhiteSpace(biomeType))
                return new Color(0, 0, 0, alpha);

            // Known palette (matches your map colors).
            if (biomeType.IndexOf("ClassicBiome",  StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.ClassicBiomeColor, alpha);

            if (biomeType.IndexOf("LagoonBiome",   StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.LagoonBiomeColor, alpha);

            if (biomeType.IndexOf("DesertBiome",   StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.DesertBiomeColor, alpha);

            if (biomeType.IndexOf("MountainBiome", StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.MountainBiomeColor, alpha);

            if (biomeType.IndexOf("ArcticBiome",   StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.ArcticBiomeColor, alpha);

            if (biomeType.IndexOf("DecentBiome",   StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.DecentBiomeColor, alpha);

            if (biomeType.IndexOf("Hell",          StringComparison.OrdinalIgnoreCase) >= 0)
                return WithA(LanternLandMapConfig.HellBiomeColor, alpha);

            // Unknown/custom biome: Deterministic color from (seed + type string).
            unchecked
            {
                uint h = 2166136261u;
                h ^= (uint)seed * 0x9E3779B9u;

                for (int i = 0; i < biomeType.Length; i++)
                    h = (h ^ biomeType[i]) * 16777619u;

                h = WorldGenPlusMath.HashU(h);

                float hue = ((h & 0xFFFFu) / 65535f);
                float sat = 0.55f + (((h >> 16) & 0xFFu) / 255f) * 0.35f;
                float val = 0.70f + (((h >> 24) & 0xFFu) / 255f) * 0.20f;

                Color c = HsvToColor(hue, sat, val);
                return new Color(c.R, c.G, c.B, alpha);
            }
        }

        /// <summary>
        /// Applies alpha to an RGB color with premultiplication.
        /// Summary: Keeps underlay brightness consistent when rendering with AlphaBlend (premultiplied).
        /// </summary>
        private static Color WithA(Color c, byte a)
        {
            return new Color(
                (byte)((c.R * a) / 255),
                (byte)((c.G * a) / 255),
                (byte)((c.B * a) / 255),
                a);
        }

        /// <summary>
        /// Simple HSV->RGB conversion (h in [0..1]).
        /// Summary: Used to generate nice-looking deterministic colors for custom biomes.
        /// </summary>
        private static Color HsvToColor(float h, float s, float v)
        {
            float hh = (h - (float)Math.Floor(h)) * 6f;
            int i = (int)Math.Floor(hh);
            float f = hh - i;

            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));

            float r, g, b;
            switch (i % 6)
            {
                default:
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }

            return new Color(
                (byte)MathHelper.Clamp(r * 255f, 0f, 255f),
                (byte)MathHelper.Clamp(g * 255f, 0f, 255f),
                (byte)MathHelper.Clamp(b * 255f, 0f, 255f),
                255);
        }
        #endregion
    }
}