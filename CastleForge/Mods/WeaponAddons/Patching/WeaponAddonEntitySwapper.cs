/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

#pragma warning disable IDE0060   // Silence IDE0060.
using DNA.CastleMinerZ.Inventory;
using System.Reflection;
using DNA.Drawing;
using HarmonyLib;

using static ModLoader.LogSystem;

namespace WeaponAddons
{
    /// <summary>
    /// Runtime model swapper for WeaponAddons.
    ///
    /// Purpose:
    /// - Patch every InventoryItemClass-derived CreateEntity(ItemUse,bool) override.
    /// - After each entity is created, replace the ModelEntity's model with the addon-provided model (if any).
    ///
    /// Notes:
    /// - UI entities are skipped to avoid touching inventory/menu previews.
    /// - The replacement uses ModelEntity.Model's setter (calls SetupModel internally).
    /// </summary>
    internal static class WeaponAddonEntitySwapper
    {
        #region State

        private static bool    _applied;
        private static Harmony _h;

        #endregion

        #region Public API

        /// <summary>
        /// Applies the entity-swap patch pass once content/types are available.
        ///
        /// Summary:
        /// - Finds the InventoryItem.InventoryItemClass nested type.
        /// - Scans all derived types in its assembly.
        /// - Patches each declared CreateEntity(ItemUse,bool) with a postfix (PostfixShim).
        /// </summary>
        public static void ApplyAfterContent()
        {
            if (_applied) return;
            _applied = true;

            _h = new Harmony($"castleminerz.mods.{typeof(WeaponAddonEntitySwapper).Namespace}.entityswap");

            var baseType = typeof(InventoryItem).GetNestedType("InventoryItemClass",
                BindingFlags.Public | BindingFlags.NonPublic);

            if (baseType == null)
            {
                Log("InventoryItemClass nested type not found.");
                return;
            }

            var asm = baseType.Assembly;
            var postfix = new HarmonyMethod(typeof(WeaponAddonEntitySwapper), nameof(PostfixShim));

            int patched = 0, failed = 0;

            foreach (var t in AccessTools.GetTypesFromAssembly(asm))
            {
                if (t == null || t.IsAbstract) continue;
                if (!baseType.IsAssignableFrom(t)) continue;

                // Only patch DECLARED CreateEntity(ItemUse,bool).
                var m = AccessTools.DeclaredMethod(t, "CreateEntity", new[] { typeof(ItemUse), typeof(bool) });
                if (m == null) continue;

                try
                {
                    _h.Patch(m, postfix: postfix);
                    patched++;
                }
                catch
                {
                    failed++;
                }
            }

            Log($"EntitySwapper patched CreateEntity overrides: ok={patched}, fail={failed}.");
        }
        #endregion

        #region Harmony Postfix

        /// <summary>
        /// Postfix applied to CreateEntity(ItemUse,bool) for InventoryItemClass-derived types.
        ///
        /// Summary:
        /// - Ignore null inputs and UI entities.
        /// - If WeaponAddonManager has a model override for the current item ID:
        ///     - If the created entity is a ModelEntity, set its Model property to the addon model.
        ///
        /// Notes:
        /// - Using ModelEntity.Model setter triggers internal setup (skeleton/effects).
        /// - Any errors are swallowed to avoid impacting gameplay.
        /// </summary>
        public static void PostfixShim(InventoryItem.InventoryItemClass __instance, ItemUse use, bool attachedToLocalPlayer, ref Entity __result)
        {
            try
            {
                if (__instance == null || __result == null) return;
                if (use == ItemUse.UI) return;

                // If this slot has an addon model, swap it.
                if (WeaponAddonManager.TryGetModelForSlot(__instance.ID, out var model) && model != null)
                {
                    if (__result is ModelEntity)
                    {
                        // Protected Model Model { set => SetupModel(value); }.
                        var p = AccessTools.Property(typeof(ModelEntity), "Model");
                        p?.SetValue(__result, model, null);
                    }
                }
            }
            catch
            {
                // Never break gameplay.
            }
        }
        #endregion
    }
}