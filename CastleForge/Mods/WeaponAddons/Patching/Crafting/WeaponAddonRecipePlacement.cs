/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using HarmonyLib;

namespace WeaponAddons
{
    /// <summary>
    /// Crafting UI "layout assist" for WeaponAddons recipes.
    ///
    /// Summary:
    /// - The vanilla crafting UI (Tier2Item) is not a real grid:
    ///   items are laid out vertically, and the UI only places an icon "to the right"
    ///   when it sees a SECOND recipe with the SAME result item ID.
    /// - WeaponAddons extends this behavior by allowing a recipe icon to be placed
    ///   "to the right of" ANY anchor item row (RIGHT_OF mode).
    ///
    /// How it's used:
    /// - WeaponAddonRecipeInjector adds a Recipe to Recipe.CookBook.
    /// - If RECIPE_INSERT_MODE == RIGHT_OF, it calls:
    ///     WeaponAddonRecipePlacement.SetRightOf(recipe, anchorId)
    /// - A Tier2Item constructor patch calls RelayoutTier2Item(...) to rebuild icon positions.
    ///
    /// Notes:
    /// - UI-only: Does not affect crafting logic, discovery, or recipe requirements.
    /// - Best-effort: If reflection targets aren't found, the method exits without breaking gameplay.
    /// </summary>
    internal static class WeaponAddonRecipePlacement
    {
        // =====================================================================================
        // STATE
        // =====================================================================================
        //
        // Summary:
        // - RIGHT_OF is tracked per Recipe instance (not by ID) to avoid touching vanilla recipes.
        // - Map: Recipe instance -> anchor InventoryItemIDs (row to place beside).
        //
        // =====================================================================================

        #region State

        // Recipe instance -> anchor item id to place "right of".
        private static readonly Dictionary<Recipe, InventoryItemIDs> _rightOf
            = new Dictionary<Recipe, InventoryItemIDs>();

        #endregion

        // =====================================================================================
        // PUBLIC API (REGISTER / UNREGISTER)
        // =====================================================================================

        #region Registration

        /// <summary>
        /// Register a "RIGHT_OF" rule for a specific recipe.
        /// Summary: During UI layout, this recipe will try to sit to the right of the anchor row.
        /// </summary>
        public static void SetRightOf(Recipe recipe, InventoryItemIDs anchorId)
        {
            if (recipe == null) return;
            _rightOf[recipe] = anchorId;
        }

        /// <summary>
        /// Remove any placement rule for a recipe.
        /// Summary: Called when a pack recipe is removed on reload/unload.
        /// </summary>
        public static void Remove(Recipe recipe)
        {
            if (recipe == null) return;
            _rightOf.Remove(recipe);
        }
        #endregion

        // =====================================================================================
        // UI RELAYOUT (TIER2ITEM)
        // =====================================================================================
        //
        // Summary:
        // - Rewrites Tier2Item's private _itemLocations[] array using:
        //   1) Custom RIGHT_OF anchors (Recipe -> anchor result ID), else
        //   2) Vanilla duplicate-result behavior (second recipe for same result ID goes right).
        //
        // Key behavior:
        // - "Right-of" entries do NOT advance the vertical cursor (they do not consume rows),
        //   matching vanilla's duplicate-result placement style.
        //
        // Notes:
        // - Reflection targets:
        //   • _items         : List<Recipe>
        //   • _itemLocations : Point[]
        // - Constants STEP and initial cursor match Tier2Item's ctor defaults (pixel coordinates).
        //
        // =====================================================================================

        #region Relayout

        public static void RelayoutTier2Item(object tier2)
        {
            if (tier2 == null) return;

            var t = tier2.GetType();

            var fiItems = AccessTools.Field(t, "_items");
            var fiLocs  = AccessTools.Field(t, "_itemLocations");
            if (fiItems == null || fiLocs == null) return;
            if (!(fiItems.GetValue(tier2) is List<Recipe> items) || !(fiLocs.GetValue(tier2) is Point[] locs)) return;
            if (locs.Length != items.Count) return;

            // Match vanilla constants from Tier2Item ctor.
            const int STEP = 65;
            Point cursor   = new Point(393, 40);

            // Occupancy and first occurrence map (for vanilla duplicate behavior).
            var used               = new HashSet<long>();
            var firstIdxByResultId = new Dictionary<InventoryItemIDs, int>();

            // Helper to encode point into a hash key.
            long Key(int x, int y) => ((long)x << 32) | (uint)y;

            // Helper to find next free X on same row.
            int NextFreeX(int startX, int y)
            {
                int x = startX;
                while (used.Contains(Key(x, y)))
                    x += STEP;
                return x;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var r     = items[i];
                var resId = r?.Result?.ItemClass?.ID ?? default;

                // Determine if this recipe should be "right-of" another item.
                bool placeRight = false;
                int anchorIndex;

                // Custom: RIGHT_OF anchor.
                if (r != null && _rightOf.TryGetValue(r, out var anchorId))
                {
                    if (firstIdxByResultId.TryGetValue(anchorId, out anchorIndex))
                        placeRight = true;
                }
                // Vanilla: Duplicate result id => place to the right of the first of that result.
                else if (firstIdxByResultId.TryGetValue(resId, out anchorIndex))
                {
                    placeRight = true;
                }

                if (placeRight && anchorIndex >= 0)
                {
                    var a = locs[anchorIndex];
                    int y = a.Y;
                    int x = NextFreeX(a.X + STEP, y);

                    locs[i] = new Point(x, y);
                    used.Add(Key(x, y));

                    // IMPORTANT:
                    // Right-of items DO NOT advance the cursor (so they don't consume a row).
                }
                else
                {
                    int x = cursor.X;
                    int y = cursor.Y;

                    // Rare safety: If occupied, shove right until free.
                    x = NextFreeX(x, y);

                    locs[i] = new Point(x, y);
                    used.Add(Key(x, y));

                    // Row consumer: Advance cursor.
                    cursor.Y += STEP;
                }

                // Always record first occurrence for this result id (even if custom right-of).
                if (!firstIdxByResultId.ContainsKey(resId))
                    firstIdxByResultId[resId] = i;
            }
        }
        #endregion
    }
}