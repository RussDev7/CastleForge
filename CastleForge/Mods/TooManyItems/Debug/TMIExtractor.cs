/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using HarmonyLib;
using System.IO;
using System;

namespace TooManyItems
{
    /// <summary>
    /// Exports every inventory icon from the game's item atlas to PNG files.
    ///
    /// Summary:
    /// - Finds the item atlas via reflection (supports multiple private field names).
    /// - Finds each InventoryItemClass's sprite SourceRectangle (if present), otherwise
    ///   falls back to a 64px grid by numeric item ID.
    /// - Extracts pixels with Texture2D.GetData; if the format/driver blocks that, falls
    ///   back to blitting into a RenderTarget2D and saving from there.
    /// - Writes outputs to:
    ///     - _2DImages      -> <Game>\!Mods\<Namespace>\Extracted\2DImages\{id}_{name}.png
    ///     - _2DImagesLarge -> <Game>\!Mods\<Namespace>\Extracted\2DImagesLarge\{id}_{name}.png
    ///
    /// Notes:
    /// - TILE_SMALL/TILE_LARGE are defaults for grid fallback; adjust if your build differs.
    /// - This uses reflection to be resilient across game builds; no public API required.
    /// </summary>
    internal static class TMIExtractor
    {
        #region Constants & Paths

        // Fallback grid cell size when no SourceRectangle exists.
        private const int TILE       = 64;
        private const int TILE_LARGE = 128;

        /// <summary>Output directory: <Game>\!Mods\<Namespace>\Extracted</summary>
        private static string RootOutDir    =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(TMIExtractor).Namespace, "Extracted");

        private static string OutSmallDir   => Path.Combine(RootOutDir, "2DImages");
        private static string OutLargeDir   => Path.Combine(RootOutDir, "2DImagesLarge");
        private static string AtlasSmallPng => Path.Combine(OutSmallDir, "items_atlas.png");
        private static string AtlasLargePng => Path.Combine(OutLargeDir, "items_atlas.png");

        #endregion

        #region Public API

        /// <summary>
        /// Export every item icon into individual PNG files and also dump the full atlas once.
        /// </summary>
        public static void ExportItemSprites(GraphicsDevice gd)
        {
            try
            {
                Directory.CreateDirectory(OutSmallDir);
                Directory.CreateDirectory(OutLargeDir);

                // Find the item atlas texture (supports small & large).
                var smallAtlas = FindAtlas("_2DImages");
                var largeAtlas = FindAtlas("_2DImagesLarge"); // May be null on older builds.

                if (smallAtlas == null && largeAtlas == null)
                {
                    ModLoader.LogSystem.SendFeedback("Export failed: Neither _2DImages nor _2DImagesLarge were found.");
                    return;
                }

                // Save each atlas for reference (if present).
                if (smallAtlas != null) SaveAtlasPng(smallAtlas, AtlasSmallPng);
                if (largeAtlas != null) SaveAtlasPng(largeAtlas, AtlasLargePng);

                // Items and sprite accessors.
                var items = GetAllItemClasses();
                if (items == null || items.Count == 0)
                {
                    ModLoader.LogSystem.SendFeedback("Export found no items.");
                    return;
                }

                // Discover sprite/rectangle reflection once.
                var (spriteField, srcRectProp, srcRectField) = DiscoverSpriteAccessors();

                // Build the authoritative small-rect map (sprite -> fallback grid).
                var smallRects = new Dictionary<InventoryItemIDs, Rectangle>();
                int smallCols  = smallAtlas != null ? Math.Max(1, smallAtlas.Width / TILE) : 1;

                foreach (var kv in items)
                {
                    var id  = kv.Key;
                    var cls = kv.Value;

                    Rectangle src = Rectangle.Empty;

                    // Prefer the sprite's own SourceRectangle when available.
                    if (spriteField != null && smallAtlas != null)
                        src = GetSourceRectangleFromSprite(cls, spriteField, srcRectProp, srcRectField);

                    // Fallback to grid if empty/invalid or no small atlas.
                    if (src.Width <= 0 || src.Height <= 0 || smallAtlas == null)
                    {
                        int idx = (int)id;
                        int col = idx % smallCols;
                        int row = idx / smallCols;
                        src = new Rectangle(col * TILE, row * TILE, TILE, TILE);
                    }

                    // Clamp to small atlas bounds if present (safety against bad metadata).
                    if (smallAtlas != null)
                        src = ClampToTexture(smallAtlas, src);

                    smallRects[id] = src;
                }

                // Export from the small atlas.
                int exportedSmall = 0;
                if (smallAtlas != null)
                {
                    // Save each region as PNG (GetData preferred; fallback to blit if needed).
                    foreach (var kv in items.OrderBy(k => (int)k.Key))
                    {
                        var id = (int)kv.Key;
                        var name = kv.Value?.Name ?? kv.Key.ToString();
                        var src = smallRects[kv.Key];

                        var path = Path.Combine(OutSmallDir, $"{id:0000}_{Sanitize(name)}.png");
                        if (!TrySaveRegionWithGetData(gd, smallAtlas, src, path))
                            SaveRegionWithBlit(gd, smallAtlas, src, path);

                        exportedSmall++;
                    }
                }

                // Export from the large atlas (scale small rects; grid fallback if scaling invalid).
                int exportedLarge = 0;
                if (largeAtlas != null)
                {
                    // Scale factors if we also have the small atlas.
                    float sx = 1f, sy = 1f;
                    if (smallAtlas != null)
                    {
                        sx = (float)largeAtlas.Width / Math.Max(1, smallAtlas.Width);
                        sy = (float)largeAtlas.Height / Math.Max(1, smallAtlas.Height);
                    }
                    int largeCols = Math.Max(1, largeAtlas.Width / TILE_LARGE);

                    foreach (var kv in items.OrderBy(k => (int)k.Key))
                    {
                        var id   = (int)kv.Key;
                        var name = kv.Value?.Name ?? kv.Key.ToString();

                        Rectangle srcLarge;

                        if (smallAtlas != null)
                        {
                            // Scale the authoritative small rect.
                            var s = smallRects[kv.Key];
                            srcLarge = new Rectangle(
                                x:      (int)Math.Round(s.X * sx),
                                y:      (int)Math.Round(s.Y * sy),
                                width:  (int)Math.Round(s.Width * sx),
                                height: (int)Math.Round(s.Height * sy)
                            );

                            // If scaling goes out of bounds (builds/layouts differ), fall back to grid.
                            if (!IsInside(largeAtlas, srcLarge))
                            {
                                int col  = id % largeCols, row = id / largeCols;
                                srcLarge = new Rectangle(col * TILE_LARGE, row * TILE_LARGE, TILE_LARGE, TILE_LARGE);
                            }
                        }
                        else
                        {
                            // No small atlas -> use grid directly.
                            int col  = id % largeCols, row = id / largeCols;
                            srcLarge = new Rectangle(col * TILE_LARGE, row * TILE_LARGE, TILE_LARGE, TILE_LARGE);
                        }

                        // Clamp then save.
                        srcLarge = ClampToTexture(largeAtlas, srcLarge);

                        var path = Path.Combine(OutLargeDir, $"{id:0000}_{Sanitize(name)}.png");
                        if (!TrySaveRegionWithGetData(gd, largeAtlas, srcLarge, path))
                            SaveRegionWithBlit(gd, largeAtlas, srcLarge, path);

                        exportedLarge++;
                    }
                }

                // Export summary.
                if (smallAtlas != null && largeAtlas != null)
                    ModLoader.LogSystem.SendFeedback($"Exported {exportedSmall} small + {exportedLarge} large sprites to '{RootOutDir}'.");
                else if (smallAtlas != null)
                    ModLoader.LogSystem.SendFeedback($"Exported {exportedSmall} small sprites to '{OutSmallDir}'.");
                else
                    ModLoader.LogSystem.SendFeedback($"Exported {exportedLarge} large sprites to '{OutLargeDir}'.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.SendFeedback($"Export failed: {ex.Message}.");
            }
        }
        #endregion

        #region Reflection Discovery

        /// <summary>
        /// Locate an item atlas Texture2D by a specific field name on InventoryItem.
        /// Examples: "_2DImages", "_2DImagesLarge".
        /// </summary>
        private static Texture2D FindAtlas(string fieldName)
        {
            var f = AccessTools.Field(typeof(InventoryItem), fieldName);
            return f?.GetValue(null) as Texture2D;
        }

        /// <summary>
        /// Returns the dictionary of all item classes indexed by InventoryItemIDs.
        /// </summary>
        private static Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass> GetAllItemClasses()
        {
            var f = AccessTools.Field(typeof(InventoryItem), "AllItems");
            return f?.GetValue(null) as Dictionary<InventoryItemIDs, InventoryItem.InventoryItemClass>;
        }

        /// <summary>
        /// Finds the nested InventoryItemClass type and the field/property used to access the sprite SourceRectangle.
        /// Different builds may store this differently (field/private prop/etc).
        /// </summary>
        private static (System.Reflection.FieldInfo spriteField,
                        System.Reflection.PropertyInfo srcRectProp,
                        System.Reflection.FieldInfo srcRectField)
            DiscoverSpriteAccessors()
        {
            var iicType = typeof(InventoryItem).GetNestedType("InventoryItemClass", AccessTools.all);

            // The sprite field usually has a type with "Sprite" in its name.
            var spriteField = AccessTools.GetDeclaredFields(iicType)
                                         .FirstOrDefault(f => f.FieldType.Name.IndexOf("Sprite", StringComparison.OrdinalIgnoreCase) >= 0);

            var spriteType = spriteField?.FieldType;
            var srcProp = spriteType != null ? AccessTools.Property(spriteType, "SourceRectangle") : null;
            var srcFld = spriteType != null ? AccessTools.Field(spriteType, "_sourceRectangle") : null;

            return (spriteField, srcProp, srcFld);
        }

        /// <summary>
        /// Attempts to read a SourceRectangle from the item's sprite via the discovered accessors.
        /// Returns Rectangle.Empty if not available.
        /// </summary>
        private static Rectangle GetSourceRectangleFromSprite(
            InventoryItem.InventoryItemClass cls,
            System.Reflection.FieldInfo spriteField,
            System.Reflection.PropertyInfo srcRectProp,
            System.Reflection.FieldInfo srcRectField)
        {
            try
            {
                if (cls == null || spriteField == null) return Rectangle.Empty;

                var sprite = spriteField.GetValue(cls);
                if (sprite == null) return Rectangle.Empty;

                if (srcRectProp != null)
                    return (Rectangle)srcRectProp.GetValue(sprite, null);

                if (srcRectField != null)
                    return (Rectangle)srcRectField.GetValue(sprite);

                return Rectangle.Empty;
            }
            catch
            {
                return Rectangle.Empty;
            }
        }
        #endregion

        #region Extraction Implementation

        /// <summary>
        /// Preferred path: Read pixels into a new Texture2D via GetData, then SaveAsPng.
        /// Some formats/drivers may throw on sub-rectangle GetData; we catch and signal fallback.
        /// </summary>
        private static bool TrySaveRegionWithGetData(GraphicsDevice gd, Texture2D atlas, Rectangle src, string path)
        {
            try
            {
                var data = new Color[src.Width * src.Height];
                atlas.GetData(0, src, data, 0, data.Length);

                using (var tex = new Texture2D(gd, src.Width, src.Height, false, atlas.Format))
                {
                    tex.SetData(data);
                    using (var fs = File.Create(path))
                        tex.SaveAsPng(fs, src.Width, src.Height);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Fallback: Draw the rectangle into a RenderTarget2D (blit) and save that as PNG.
        /// This path works even when rectangle GetData is disallowed.
        /// </summary>
        private static void SaveRegionWithBlit(GraphicsDevice gd, Texture2D atlas, Rectangle src, string path)
        {
            var rt = new RenderTarget2D(gd, src.Width, src.Height, false, SurfaceFormat.Color, DepthFormat.None);
            var sb = new SpriteBatch(gd);

            var prev = gd.GetRenderTargets();
            gd.SetRenderTarget(rt);
            gd.Clear(Color.Transparent);

            // NOTE: Use Deferred, NOT Immediate.
            sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, null, null);
            sb.Draw(atlas, new Rectangle(0, 0, src.Width, src.Height), src, Color.White);
            sb.End();

            gd.SetRenderTarget(null);
            if (prev != null && prev.Length > 0) gd.SetRenderTargets(prev);

            using (var fs = File.Create(path))
                rt.SaveAsPng(fs, src.Width, src.Height);

            sb.Dispose();
            rt.Dispose();
        }

        /// <summary>
        /// Saves the whole atlas to disk (useful for verifying coordinates).
        /// </summary>
        private static void SaveAtlasPng(Texture2D atlas, string path)
        {
            using (var fs = File.Create(path))
                atlas.SaveAsPng(fs, atlas.Width, atlas.Height);
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Ensure the rectangle fully resides inside the texture bounds.
        /// </summary>
        private static Rectangle ClampToTexture(Texture2D tex, Rectangle src)
        {
            int x = Math.Max(0, Math.Min(src.X, tex.Width - 1));
            int y = Math.Max(0, Math.Min(src.Y, tex.Height - 1));
            int w = Math.Max(1, Math.Min(src.Width, tex.Width - x));
            int h = Math.Max(1, Math.Min(src.Height, tex.Height - y));
            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// True if the rectangle has positive size and lies entirely within the texture bounds.
        /// </summary>
        private static bool IsInside(Texture2D tex, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return false;
            return r.X >= 0 && r.Y >= 0 && r.Right <= tex.Width && r.Bottom <= tex.Height;
        }

        /// <summary>
        /// Sanitize a filename component (replace invalid chars with '_').
        /// </summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "item";
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            return s.Trim();
        }
        #endregion
    }
}