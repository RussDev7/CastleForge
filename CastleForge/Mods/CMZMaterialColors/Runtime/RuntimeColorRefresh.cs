/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using HarmonyLib;
using System;

namespace CMZMaterialColors
{
    /// <summary>
    /// Applies configured material and laser colors back onto already-registered CastleMiner Z item classes.
    ///
    /// Purpose:
    /// - Refreshes cached runtime item colors after vanilla item registration has already occurred.
    /// - Ensures visible item colors reflect the current CMZMaterialColors config.
    /// - Rebuilds cached 2D inventory icon atlases so UI icons can regenerate with updated colors.
    ///
    /// Notes:
    /// - Many CastleMiner Z item classes cache their colors during initialization instead of querying
    ///   CMZColors every frame.
    /// - Because of that, patching the CMZColors lookup methods alone is not always enough for visible changes.
    /// - This helper reapplies the resolved colors directly onto the registered item class instances.
    /// </summary>
    internal static class RuntimeColorRefresh
    {
        #region Public Refresh Entry Point

        /// <summary>
        /// Walk all registered inventory item definitions and reapply any configured material/laser colors.
        ///
        /// Flow:
        /// - Enumerates every InventoryItemIDs value.
        /// - Resolves the registered InventoryItemClass for that ID.
        /// - Applies the appropriate cached color fields based on item type.
        /// - Invalidates cached 2D icon atlases so the UI can rebuild them with new colors.
        ///
        /// Notes:
        /// - Sabers only refresh the beam color and intentionally leave the hilt behavior alone.
        /// - Laser guns use material color for the tool body and laser color for tracer/emissive visuals.
        /// - Standard tools and conventional guns refresh their cached ToolColor/TracerColor fields directly.
        /// </summary>
        public static void ApplyRegisteredItemColors()
        {
            int touched = 0;

            #region Refresh Registered Item Classes

            foreach (InventoryItemIDs id in Enum.GetValues(typeof(InventoryItemIDs)))
            {
                var item = InventoryItem.GetClass(id);
                if (item == null)
                    continue;

                #region Saber

                // Saber: keep gray hilt, only change beam color.
                if (item is SaberInventoryItemClass saber)
                {
                    Color laser = GetLaserColor(saber.Material);

                    var beamField = AccessTools.Field(typeof(SaberInventoryItemClass), "BeamColor");
                    beamField?.SetValue(saber, laser);

                    touched++;
                    continue;
                }
                #endregion

                #region Laser Guns

                // Laser guns: body color still uses material color,
                // but tracer/emissive use laser color.
                if (item is LaserGunInventoryItemClass laserGun)
                {
                    Color material = GetMaterialColor(laserGun.Material);
                    Color laser = GetLaserColor(laserGun.Material);

                    laserGun.ToolColor = material;
                    laserGun.TracerColor = laser.ToVector4();
                    laserGun.EmissiveColor = laser;

                    touched++;
                    continue;
                }
                #endregion

                #region Conventional Guns

                // Conventional guns.
                if (item is GunInventoryItemClass gun)
                {
                    Color material = GetMaterialColor(gun.Material);

                    gun.ToolColor = material;
                    gun.TracerColor = material.ToVector4();

                    touched++;
                    continue;
                }
                #endregion

                #region Hand Tools

                // Picks.
                if (item is PickInventoryItemClass pick)
                {
                    pick.ToolColor = GetMaterialColor(pick.Material);
                    touched++;
                    continue;
                }

                // Axes.
                if (item is AxeInventoryClass axe)
                {
                    axe.ToolColor = GetMaterialColor(axe.Material);
                    touched++;
                    continue;
                }

                // Spades.
                if (item is SpadeInventoryClass spade)
                {
                    spade.ToolColor = GetMaterialColor(spade.Material);
                    touched++;
                    continue;
                }

                // Knives.
                if (item is KnifeInventoryItemClass knife)
                {
                    knife.ToolColor = GetMaterialColor(knife.Material);
                    touched++;
                    continue;
                }

                // Chainsaws.
                if (item is ChainsawInventoryItemClass chainsaw)
                {
                    chainsaw.ToolColor = GetMaterialColor(chainsaw.Material);
                    touched++;
                    continue;
                }
                #endregion
            }
            #endregion

            #region Invalidate Cached 2D Icon Atlases

            // Force UI item icons to rebuild using the new colors.
            try { InventoryItem._2DImages?.Dispose(); } catch { }
            try { InventoryItem._2DImagesLarge?.Dispose(); } catch { }
            InventoryItem._2DImages = null;
            InventoryItem._2DImagesLarge = null;

            #endregion

            #region Logging

            ModLoader.LogSystem.Log($"Refreshed cached colors on {touched} registered item classes.");

            #endregion
        }
        #endregion

        #region Color Resolution Helpers

        /// <summary>
        /// Resolve the material/body color for the given material type.
        ///
        /// Behavior:
        /// - Returns the configured override when one exists in CMZMaterialColors settings.
        /// - Falls back to the vanilla CastleMiner Z material color when no override is configured.
        /// </summary>
        private static Color GetMaterialColor(ToolMaterialTypes mat)
        {
            if (CMZMaterialColors_Settings.MaterialColors.TryGetValue(mat, out var c))
                return c;

            return ColorConfig.GetVanillaMaterialColor(mat);
        }

        /// <summary>
        /// Resolve the laser/emissive color for the given material type.
        ///
        /// Behavior:
        /// - Returns the configured laser override when one exists in CMZMaterialColors settings.
        /// - Falls back to the vanilla CastleMiner Z laser material color when no override is configured.
        /// </summary>
        private static Color GetLaserColor(ToolMaterialTypes mat)
        {
            if (CMZMaterialColors_Settings.LaserColors.TryGetValue(mat, out var c))
                return c;

            return ColorConfig.GetVanillaLaserColor(mat);
        }
        #endregion
    }
}