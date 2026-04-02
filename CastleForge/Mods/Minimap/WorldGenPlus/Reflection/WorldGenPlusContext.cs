/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Concurrent;
using System.Reflection;
using System;

namespace LanternLandMap.Integrations.WorldGenPlus
{
    /// <summary>
    /// Reflection helpers for discovering the active WorldGenPlus builder and its config.
    /// Summary: Uses direct BlockTerrain/WorldInfo access when available, reflecting only to read WGP's private _cfg.
    /// </summary>
    internal static class WorldGenPlusContext
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Cache "how to get cfg" per builder runtime type (avoids repeated reflection).
        private static readonly ConcurrentDictionary<Type, Func<object, object>> _cfgGetterCache
            = new ConcurrentDictionary<Type, Func<object, object>>();

        /// <summary>
        /// Attempts to locate the WorldGenPlus config object currently driving world generation.
        /// Summary: Returns false if WorldGenPlus isn't installed/active or the builder hasn't been swapped yet.
        /// </summary>
        public static bool TryGetWorldGenPlusContext(out object cfg, out int seed)
        {
            cfg  = null;
            seed = 0;

            try
            {
                var terrain = DNA.CastleMinerZ.Terrain.BlockTerrain.Instance;
                if (terrain == null) return false;

                // Direct access for seed (no reflection / no guessing).
                seed = terrain.WorldInfo != null ? terrain.WorldInfo.Seed : 0;

                object builder = terrain._worldBuilder;
                if (builder == null) return false;

                // Identify WGP builder without referencing its assembly.
                string fullName = builder.GetType().FullName ?? builder.GetType().Name;
                if (fullName.IndexOf("WorldGenPlusBuilder", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

                cfg = GetCfg(builder);
                return cfg != null;
            }
            catch
            {
                cfg = null;
                seed = 0;
                return false;
            }
        }

        /// <summary>
        /// Gets the active WGP settings instance from the builder.
        /// Summary: Prefers the confirmed private field name "_cfg" to avoid probing/guesswork.
        /// </summary>
        private static object GetCfg(object builder)
        {
            var t = builder.GetType();

            var getter = _cfgGetterCache.GetOrAdd(t, type =>
            {
                // Confirmed by runtime logs:
                //   Has _cfg field: True
                //   _cfg FieldType: WorldGenPlus.WorldGenPlusSettings
                var fCfg = type.GetField("_cfg", F);
                if (fCfg != null)
                    return (object o) => fCfg.GetValue(o);

                return (object o) => null;
            });

            return getter(builder);
        }
    }
}