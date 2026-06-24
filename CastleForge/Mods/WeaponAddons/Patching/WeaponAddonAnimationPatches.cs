/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using DNA.Drawing.Animation;
using DNA.CastleMinerZ;
using DNA.Avatars;
using HarmonyLib;

namespace WeaponAddons
{
    /// <summary>
    /// Harmony hooks for WeaponAddons custom avatar/weapon handling animations.
    ///
    /// Summary:
    /// - Track the final ItemId currently shown in each Player's hand.
    /// - Remap vanilla animation names at AvatarAnimationCollection.Play right before the clip is resolved.
    /// - Restore AnimationPlayer.Name to the vanilla name so Player.UpdateAnimation state checks remain stable.
    ///
    /// Notes:
    /// - This intentionally avoids patching Player.UpdateAnimation directly.
    /// - Synthetic local items work because the existing carried-item de-remap calls PutItemInHand(syntheticId).
    /// </summary>
    internal static class WeaponAddonAnimationPatches
    {
        /// <summary>
        /// Tracks the item currently placed in a player's hand.
        /// Summary: This gives the animation Play patch the final ItemId to route from.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.PutItemInHand))]
        private static class Patch_Player_PutItemInHand_TrackWeaponAddonAnimationItem
        {
            private static void Postfix(Player __instance, InventoryItemIDs itemID)
            {
                WeaponAddonAnimationRouter.TrackHeldItem(__instance, itemID);
            }
        }

        /// <summary>
        /// Remaps vanilla animation names to custom WeaponAddons animation names.
        /// Summary: The postfix restores the returned player's Name to the original vanilla name.
        /// </summary>
        [HarmonyPatch(typeof(AvatarAnimationCollection), nameof(AvatarAnimationCollection.Play))]
        private static class Patch_AvatarAnimationCollection_Play_WeaponAddonAnimations
        {
            private static void Prefix(AvatarAnimationCollection __instance, ref string id, out string __state)
            {
                __state = null;

                try
                {
                    string original = id;
                    if (WeaponAddonAnimationRouter.TryRemap(__instance, original, out var remapped))
                    {
                        __state = original;
                        id = remapped;
                    }
                }
                catch
                {
                    __state = null;
                }
            }

            private static void Postfix(string __state, AnimationPlayer __result)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(__state) && __result != null)
                        __result.Name = __state;
                }
                catch
                {
                    // Never break animation playback.
                }
            }
        }
    }
}