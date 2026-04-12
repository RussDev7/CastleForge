/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;

namespace CMZMaterialColors
{
    internal static class HotReloadIconRefresh
    {
        /// <summary>
        /// Rebuild the cached 2D inventory icon atlases after a config hot reload.
        ///
        /// Flow:
        /// - Disposes the existing small/large icon atlases.
        /// - Clears the cached references so CastleMiner Z knows they are stale.
        /// - Immediately rebuilds the atlases using the current GraphicsDevice.
        ///
        /// Notes:
        /// - This is what makes inventory icons pick up the new material colors.
        /// - RuntimeColorRefresh updates the cached item class colors first; this helper
        ///   makes the UI icon textures regenerate from those updated values.
        /// </summary>
        public static void RefreshItemIconsNow()
        {
            try { InventoryItem._2DImages?.Dispose();      } catch { }
            try { InventoryItem._2DImagesLarge?.Dispose(); } catch { }

            InventoryItem._2DImages      = null;
            InventoryItem._2DImagesLarge = null;
        }
    }
}