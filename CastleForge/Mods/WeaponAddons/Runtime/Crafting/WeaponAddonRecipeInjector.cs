/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System;

using static ModLoader.LogSystem;

namespace WeaponAddons
{
    /// <summary>
    /// Per-pack crafting recipe injection for WeaponAddons.
    ///
    /// Summary:
    /// - Reads recipe fields from each WeaponAddonDef (parsed from .clag).
    /// - Creates a Recipe instance (result + ingredients) and inserts it into Recipe.CookBook.
    /// - Only removes recipes that WE injected (object-identity removal).
    /// - Supports optional ordering + UI placement metadata:
    ///   • RECIPE_INSERT_AFTER: insert immediately after an anchor recipe in the same tab.
    ///   • RECIPE_INSERT_MODE = RIGHT_OF: also mark the recipe for UI "right-of" layout (Tier2Item patch).
    ///
    /// Notes:
    /// - We do not modify vanilla recipes.
    /// - Uses a signature cache per pack so no-op reloads don't churn the cookbook list.
    /// - Ingredient parsing is forgiving (supports multiple separators and name normalization).
    /// </summary>
    internal static class WeaponAddonRecipeInjector
    {
        // =====================================================================================
        // STATE (TRACK WHAT WE INJECTED)
        // =====================================================================================
        //
        // Summary:
        // - _recipeByPack stores the last injected Recipe instance per pack key.
        //   This allows safe removal without searching/guessing in the cookbook.
        // - _sigByPack stores a signature of the last applied recipe config per pack key.
        //   This prevents repeat work on no-op hot reloads.
        // - _idLookup is a forgiving lookup table for InventoryItemIDs (spaces/underscores ignored).
        //
        // =====================================================================================

        #region State

        // PackKey -> last injected Recipe instance (so we can remove only ours).
        private static readonly Dictionary<string, Recipe> _recipeByPack =
            new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);

        // PackKey -> signature to avoid redoing work on no-op reloads.
        private static readonly Dictionary<string, string> _sigByPack =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Forgiving item-name lookup (spaces/underscores ignored), same idea as TacticalNuke.
        private static Dictionary<string, InventoryItemIDs> _idLookup;

        #endregion

        // =====================================================================================
        // APPLY / UPDATE (PER PACK)
        // =====================================================================================
        //
        // Summary:
        // - ApplyOrUpdate():
        //   1) Validate recipe is enabled and has ingredients.
        //   2) Ensure output item class exists (synthetic IDs must be registered first).
        //   3) Compute signature and skip if unchanged.
        //   4) Remove prior injected recipe for this pack.
        //   5) Parse ingredient list (InventoryItem.CreateItem per entry).
        //   6) Create Recipe and insert into Recipe.CookBook (optionally after anchor).
        //   7) Optionally register RIGHT_OF placement metadata for UI layout patch.
        //
        // Notes:
        // - Object-identity removal: we remove ONLY the recipe instance we created.
        // - Errors are best-effort; failures are logged and do not crash gameplay.
        //
        // =====================================================================================

        #region Apply / Update

        public static void ApplyOrUpdate(WeaponAddonDef def)
        {
            if (def == null) return;

            // Always remove ours if feature is off (or incomplete).
            if (!def.RecipeEnabled || string.IsNullOrWhiteSpace(def.RecipeIngredients))
            {
                RemoveForPack(def.PackKey);
                return;
            }

            // Must be registered or CreateItem can throw (same guard TacticalNuke uses).
            if (InventoryItem.GetClass(def.ItemId) == null)
                return;

            // Parse recipe tab (defaults to Advanced if invalid/missing).
            if (!TryParseRecipeType(def.RecipeType, out var recipeType))
                recipeType = Recipe.RecipeTypes.Advanced;

            int outCount = Math.Max(1, def.RecipeOutputCount);

            // Build signature so repeated calls are cheap.
            string sig =
                ((int)def.ItemId).ToString()  + "|" +
                recipeType.ToString()         + "|" +
                outCount.ToString()           + "|" +
                (def.RecipeInsertAfter ?? "") + "|" +
                (def.RecipeInsertMode  ?? "") + "|" +
                (def.RecipeIngredients ?? "");

            if (_sigByPack.TryGetValue(def.PackKey, out var oldSig) && oldSig == sig)
                return;

            // Remove previous injected recipe for this pack (object-identity removal; won't touch vanilla).
            RemoveForPack(def.PackKey);

            // Parse ingredients.
            var ingredients = ParseIngredients(def.RecipeIngredients);
            if (ingredients.Count == 0)
            {
                Log($"[WAddns] Recipe not added for '{def.PackKey}': no valid ingredients.");
                _sigByPack[def.PackKey] = sig;
                return;
            }

            // Build recipe result.
            InventoryItem result;
            try
            {
                result = InventoryItem.CreateItem(def.ItemId, outCount);
            }
            catch (Exception ex)
            {
                Log($"[WAddns] Recipe not added for '{def.PackKey}': CreateItem({def.ItemId}) failed: {ex.Message}.");
                _sigByPack[def.PackKey] = sig;
                return;
            }

            var recipe = new Recipe(recipeType, result, ingredients.ToArray());

            var book = Recipe.CookBook;
            if (book == null)
            {
                Log("[WAddns] Recipe not added: Recipe.CookBook was null.");
                _sigByPack[def.PackKey] = sig;
                return;
            }

            // Optional ordering: Insert after a specific result item (within same tab).
            int insertAt = -1;

            // Register optional UI placement (Tier2Item layout patch uses this).
            // RIGHT_OF means "put this recipe icon to the right of RECIPE_INSERT_AFTER's item row".
            if (!string.IsNullOrWhiteSpace(def.RecipeInsertAfter) &&
                TryParseInventoryItemId(def.RecipeInsertAfter, out var afterId))
            {
                // Find the anchor within the SAME tab type.
                int afterIndex = -1;
                for (int i = 0; i < book.Count; i++)
                {
                    var r = book[i];
                    if (r == null || r.Type != recipeType) continue;

                    if (r.Result?.ItemClass?.ID == afterId)
                    {
                        afterIndex = i;
                        break;
                    }
                }

                if (afterIndex >= 0)
                {
                    // Insert immediately after anchor in the cookbook list.
                    insertAt = afterIndex + 1;

                    // Optional UI placement (RIGHT_OF).
                    string mode = (def.RecipeInsertMode ?? "").Trim();
                    if (mode.Equals("RIGHT_OF", StringComparison.OrdinalIgnoreCase))
                        WeaponAddonRecipePlacement.SetRightOf(recipe, afterId);
                }
            }

            // Fallback: If we didn't compute a position, append to end.
            if (insertAt >= 0 && insertAt <= Recipe.CookBook.Count)
                Recipe.CookBook.Insert(insertAt, recipe);
            else
                Recipe.CookBook.Add(recipe);

            _recipeByPack[def.PackKey] = recipe;
            _sigByPack[def.PackKey] = sig;

            Log($"[WAddns] Added/updated crafting recipe: Pack='{def.PackKey}', Tab={recipeType}, Out={def.ItemId}x{outCount}.");
        }
        #endregion

        // =====================================================================================
        // CLEANUP (STALE PACKS / REMOVALS)
        // =====================================================================================

        #region Cleanup

        /// <summary>
        /// Cleanup recipes for packs that are no longer present.
        /// Summary: Removes any injected recipes whose PackKey is not in the current defs list.
        /// </summary>
        public static void CleanupStale(List<WeaponAddonDef> defs)
        {
            // Remove recipes for packs that no longer exist on disk.
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
                if (d?.PackKey != null) live.Add(d.PackKey);

            var dead = new List<string>();
            foreach (var k in _recipeByPack.Keys)
                if (!live.Contains(k)) dead.Add(k);

            foreach (var k in dead)
            {
                RemoveForPack(k);
                _sigByPack.Remove(k);
            }
        }

        /// <summary>
        /// Removes the injected recipe for a given pack key (if present).
        /// Summary:
        /// - Removes only our known Recipe instance (does not touch vanilla).
        /// - Also clears RIGHT_OF layout metadata to prevent stale UI placement rules.
        /// </summary>
        private static void RemoveForPack(string packKey)
        {
            if (string.IsNullOrWhiteSpace(packKey)) return;

            try
            {
                if (_recipeByPack.TryGetValue(packKey, out var r) && r != null)
                {
                    Recipe.CookBook?.Remove(r);
                    WeaponAddonRecipePlacement.Remove(r);
                }
            }
            catch { /* best-effort */ }

            _recipeByPack.Remove(packKey);
            _sigByPack.Remove(packKey); // Optional: Prevents stale signature caching.
        }
        #endregion

        // =====================================================================================
        // PARSING HELPERS
        // =====================================================================================
        //
        // Summary:
        // - TryParseRecipeType: Supports numeric or enum name.
        // - ParseIngredients: Accepts "Name:Count" or "Name x Count" with commas/semicolons/newlines.
        // - TryParseInventoryItemId: Supports numeric, enum name, and normalized lookup.
        // - Normalize: Case-insensitive match ignoring spaces/underscores.
        //
        // =====================================================================================

        #region Parsing Helpers

        /// <summary>
        /// Parse a RecipeTypes value from config text.
        /// Summary: Accepts either a numeric value or an enum name.
        /// </summary>
        private static bool TryParseRecipeType(string raw, out Recipe.RecipeTypes type)
        {
            type = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var s = raw.Trim();

            // Numeric.
            if (int.TryParse(s, out var i))
            {
                type = (Recipe.RecipeTypes)i;
                return true;
            }

            return Enum.TryParse(s, ignoreCase: true, out type);
        }

        /// <summary>
        /// Parse an ingredients string into InventoryItem instances.
        /// Summary: Supports "Item:Count", "Item=Count", "Item*Count", or "Item x Count".
        /// </summary>
        private static List<InventoryItem> ParseIngredients(string text)
        {
            var list = new List<InventoryItem>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            foreach (var raw in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;

                string namePart;
                string countPart;

                int sep = token.IndexOf(':');
                if (sep < 0) sep = token.IndexOf('=');
                if (sep < 0) sep = token.IndexOf('*');

                if (sep >= 0)
                {
                    namePart = token.Substring(0, sep).Trim();
                    countPart = token.Substring(sep + 1).Trim();
                }
                else
                {
                    var parts = token.Split(new[] { 'x', 'X' }, 2);
                    if (parts.Length != 2) continue;
                    namePart = parts[0].Trim();
                    countPart = parts[1].Trim();
                }

                if (!int.TryParse(countPart, out var count) || count <= 0)
                    continue;

                if (!TryParseInventoryItemId(namePart, out var id))
                    continue;

                try
                {
                    list.Add(InventoryItem.CreateItem(id, count));
                }
                catch
                {
                    // CreateItem can throw if an ingredient isn't registered in this build.
                }
            }

            return list;
        }

        /// <summary>
        /// Parse an InventoryItemIDs from config text.
        /// Summary: Accepts numeric ID, enum name, or a normalized name (spaces/underscores ignored).
        /// </summary>
        private static bool TryParseInventoryItemId(string raw, out InventoryItemIDs id)
        {
            id = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string s = raw.Trim();

            // Numeric.
            if (int.TryParse(s, out var i))
            {
                id = (InventoryItemIDs)i;
                return true;
            }

            // Enum name.
            if (Enum.TryParse(s, true, out id))
                return true;

            EnsureLookup();
            return _idLookup.TryGetValue(Normalize(s), out id);
        }

        /// <summary>
        /// Build the normalized-name lookup table once.
        /// Summary: Maps "diamondpistol" / "diamond_pistol" -> InventoryItemIDs.DiamondPistol.
        /// </summary>
        private static void EnsureLookup()
        {
            if (_idLookup != null) return;

            _idLookup = new Dictionary<string, InventoryItemIDs>(StringComparer.OrdinalIgnoreCase);
            foreach (InventoryItemIDs e in Enum.GetValues(typeof(InventoryItemIDs)))
                _idLookup[Normalize(e.ToString())] = e;
        }

        /// <summary>
        /// Normalize an item name for fuzzy matching.
        /// Summary: Removes spaces/underscores and lowercases.
        /// </summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("_", "").Replace(" ", "");
            return s.Trim().ToLowerInvariant();
        }
        #endregion
    }
}
