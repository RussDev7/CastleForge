/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System;

namespace TooManyItems
{
    /// <summary>
    /// Computes sprite-atlas grid metrics the same way vanilla packing expects:
    /// tiles of 64px, N columns (defaults to 8 before the atlas exists), and
    /// rows based on the largest numeric <see cref="InventoryItemIDs"/> value.
    ///
    /// Why "max id" and not "count"?
    ///   Because the game's item enum isn't guaranteed to be contiguous-mods may
    ///   add gaps. Using the max numeric value ensures the grid is tall enough.
    ///
    /// Safety notes:
    ///   • All lookups are defensive: if the texture/dictionary isn't ready yet,
    ///     we fall back to sane defaults (8 columns, height >= 64).
    ///   • Reflection fields are cached so repeated calls don't re-scan metadata.
    /// </summary>
    internal static class TMIIconGrid
    {
        #region Constants & Defaults

        /// <summary> Width/height of each icon tile within the atlas (pixels). </summary>
        public const int Tile = 64;

        /// <summary> Fallback column count before the atlas exists. </summary>
        private const int DefaultColumns = 8;

        #endregion

        #region Reflection Cache

        // InventoryItem._2DImages : Texture2D (the atlas) - source of width/columns.
        private static readonly FieldInfo F_2DImages =
            AccessTools.Field(typeof(InventoryItem), "_2DImages");

        // InventoryItem.AllItems : Dictionary<InventoryItemIDs, InventoryItemClass> - source of ids/count.
        private static readonly FieldInfo F_AllItems =
            AccessTools.Field(typeof(InventoryItem), "AllItems");

        #endregion

        #region Internal Helpers

        /// <summary> Try to fetch the current item atlas texture; returns null if not created yet. </summary>
        private static Texture2D TryGetAtlas()
            => (Texture2D)F_2DImages?.GetValue(null);

        /// <summary> Try to fetch the item-class dictionary; returns null if uninitialized. </summary>
        private static Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> TryGetDict()
            => (Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>)F_AllItems?.GetValue(null);

        /// <summary> Integer ceil-divide helper: ceil(a / b) for positive integers. </summary>
        private static int CeilDiv(int a, int b)
        {
            if (b <= 0) return a; // Avoid div-by-zero; caller ensures sensible b.
            return (a + b - 1) / b;
        }
        #endregion

        #region Grid Metrics

        /// <summary>
        /// Column count: Derived from the created atlas width when available,
        /// otherwise falls back to <see cref="DefaultColumns"/>.
        /// </summary>
        public static int Columns
        {
            get
            {
                var atlas = TryGetAtlas();
                if (atlas != null && atlas.Width > 0)
                {
                    // Ensure at least one column if width < Tile (defensive).
                    return Math.Max(1, atlas.Width / Tile);
                }
                return DefaultColumns;
            }
        }

        /// <summary>
        /// Maximum numeric value present in <see cref="InventoryItemIDs"/> currently registered.
        /// Used to compute how many slots (and therefore rows) the grid must cover.
        /// </summary>
        public static int MaxId()
        {
            var dict = TryGetDict();
            if (dict == null || dict.Count == 0)
                return 0;

            int max = 0;
            foreach (var key in dict.Keys)
            {
                int v = (int)key;
                if (v > max) max = v;
            }
            return max;
        }

        /// <summary> Total number of registered items after the injector has run. </summary>
        public static int AllItemCount()
            => TryGetDict()?.Count ?? 0;

        /// <summary>
        /// Number of rows required to contain all IDs from 0..MaxId inclusive,
        /// laid out across <see cref="Columns"/> columns.
        /// </summary>
        public static int RequiredRows()
        {
            // +1 because IDs are zero-based.
            int slots = MaxId() + 1;
            return CeilDiv(slots, Math.Max(1, Columns));
        }
        #endregion

        #region Height Utilities

        public static int   RequiredHeight()  => Math.Max(Tile, RequiredRows() * Tile); // Atlas height (pixels) required to hold all rows
        public static int   HalfHeight()      => RequiredHeight() / 2;                  // Half the required height (integer).
        public static int   DoubleHeight()    => RequiredHeight() * 2;                  // Double the required height (integer).
        public static float RequiredHeightF() => RequiredHeight();                      // Atlas height (float).
        public static float HalfHeightF()     => RequiredHeight() * 0.5f;               // Half the required height (float).
        public static float DoubleHeightF()   => RequiredHeight() * 2f;                 // Double the required height (float).
        public static int   NegHalfHeight()   => -HalfHeight();                         // Negative half height (integer) - Handy for centering math.
        public static float NegHalfHeightF()  => -HalfHeightF();                        // Negative half height (float) - Handy for centering math.

        #endregion
    }
}
