/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System;

namespace TooManyItems
{
    #region Public Types

    /// <summary>
    /// Logical "bands" in the icon atlas. Each band consumes TWO rows:
    /// row N = OFF state, row N+1 = ON state.
    /// </summary>
    internal enum IconBand { Indicator = 0, Toolbar = 1, Mini = 2 }

    #endregion

    /// <summary>
    /// Sprite-sheet helper for TMI based icons (e.g. toolbar).
    ///
    /// Layout contract:
    /// • Atlas is a grid of fixed-size tiles (Tile x Tile) separated by optional padding (Pad).
    /// • Each <see cref="IconBand"/> uses two consecutive rows:
    ///     baseRow (OFF) and baseRow+1 (ON). If the ON row is blank, we gracefully
    ///     fall back to the OFF artwork.
    ///
    /// Usage:
    ///  1) Call <see cref="Bind(Texture2D)"/> once when the texture loads (or changes).
    ///  2) Use <see cref="Src(IconBand, int, bool)"/> to fetch the full 24x24 tile.
    ///  3) Use <see cref="ContentSrc(IconBand, int, bool)"/> to fetch a tight (alpha-trimmed)
    ///     rectangle of the tile's opaque pixels (handy for indicators).
    ///
    /// Notes:
    /// • <see cref="ContentSrc(IconBand, int, bool)"/> performs a CPU readback via
    ///   <see cref="Texture2D.GetData{T}"/> the first time a tile is requested; results are cached.
    /// • Call <see cref="Bind(Texture2D)"/> whenever the atlas changes; this resets caches.
    /// </summary>
    internal static class TMIIconSheet
    {
        #region Constants & Layout

        public const int Tile = 24; // Tile size in pixels (square, width == height).
        public const int Pad  = 0;  // Padding (in pixels) between tiles (both X and Y) in the atlas.

        #endregion

        #region Runtime State

        private static Texture2D _tex;
        private static int       _cols;
        private static int       _rows;

        // Caches.
        //  _hasOn : Whether a given (band,col) actually has any non-transparent pixels in its ON row.
        //  _tight : Alpha-trimmed rectangles (band,col,onState) -> tight Rectangle in atlas coords.
        private static readonly Dictionary<(IconBand, int), bool>            _hasOn = new Dictionary<(IconBand, int), bool>();
        private static readonly Dictionary<(IconBand, int, bool), Rectangle> _tight = new Dictionary<(IconBand, int, bool), Rectangle>();

        #endregion

        #region Binding / Reset

        /// <summary>
        /// Binds (or re-binds) the atlas texture and resets caches.
        /// Must be called before <see cref="Src"/> / <see cref="ContentSrc"/>.
        /// Pass null to unbind and reset state.
        /// </summary>
        public static void Bind(Texture2D tex)
        {
            _tex = tex;
            if (_tex == null)
            {
                _cols = _rows = 0;
                _hasOn.Clear();
                _tight.Clear();
                return;
            }

            // Compute grid dimensions based on tile size and padding.
            _cols = Math.Max(1, _tex.Width  / (Tile + Pad));
            _rows = Math.Max(1, _tex.Height / (Tile + Pad));

            _hasOn.Clear();
            _tight.Clear();
        }
        #endregion

        #region Public API

        /// <summary>
        /// Gets the full tile source rectangle for a given band/column/state.
        /// Falls back to the OFF row if the ON art is blank.
        /// Indices are clamped to atlas bounds.
        /// </summary>
        public static Rectangle Src(IconBand band, int col, bool on)
        {
            int baseRow = ((int)band) * 2;
            int row     = on && HasOn(band, col) ? baseRow + 1 : baseRow;

            col = TMIMathHelpers.Clamp(col, 0, Math.Max(0, _cols - 1));
            row = TMIMathHelpers.Clamp(row, 0, Math.Max(0, _rows - 1));

            int sx = col * (Tile + Pad);
            int sy = row * (Tile + Pad);
            return new Rectangle(sx, sy, Tile, Tile);
        }

        /// <summary>
        /// Returns a tight rectangle around opaque pixels (alpha >; 0) within the tile.
        /// Useful for pixel-perfect indicator alignment. Falls back to full tile if fully transparent
        /// or if the readback fails. Results are cached per (band,col,on).
        /// </summary>
        public static Rectangle ContentSrc(IconBand band, int col, bool on)
        {
            var key = (band, col, on);
            if (_tight.TryGetValue(key, out var rect))
                return rect;

            var tile = Src(band, col, on); // 24x24 base tile.
            if (_tex == null || tile == Rectangle.Empty)
            {
                _tight[key] = tile;
                return tile;
            }

            // Readback once and cache. Note: GetData is relatively expensive; caching avoids repeats.
            var buf = new Color[Tile * Tile];
            try
            {
                _tex.GetData(0, tile, buf, 0, buf.Length);
            }
            catch
            {
                // If not readable on this thread/device, fall back to full tile.
                _tight[key] = tile;
                return tile;
            }

            int minX = Tile, minY = Tile, maxX = -1, maxY = -1;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    if (buf[y * Tile + x].A == 0) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            // Fully transparent -> Use the full tile to avoid zero-size rects downstream.
            if (maxX < minX || maxY < minY)
            {
                _tight[key] = tile;
                return tile;
            }

            // Offset the trimmed rect back into atlas space.
            var tight = new Rectangle(tile.X + minX, tile.Y + minY, maxX - minX + 1, maxY - minY + 1);
            _tight[key] = tight;
            return tight;
        }
        #endregion

        #region Private Helpers

        /// <summary>
        /// Returns true if the ON row for (band,col) has any opaque pixel.
        /// Uses a cached probe of the ON tile to choose between ON and OFF artwork.
        /// </summary>
        private static bool HasOn(IconBand band, int col)
        {
            var key = (band, col);
            if (_hasOn.TryGetValue(key, out var v))
                return v;

            if (_tex == null)
            {
                _hasOn[key] = false;
                return false;
            }

            int baseRow = ((int)band) * 2;
            int onRow   = baseRow + 1;
            if (onRow >= _rows)
            {
                _hasOn[key] = false;
                return false;
            }

            int sx   = col * (Tile + Pad);
            int sy   = onRow * (Tile + Pad);
            var rect = new Rectangle(sx, sy, Tile, Tile);

            var buf = new Color[Tile * Tile];
            try
            {
                _tex.GetData(0, rect, buf, 0, buf.Length);
            }
            catch
            {
                _hasOn[key] = false;
                return false;
            }

            bool any = false;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].A != 0) { any = true; break; }
            }

            _hasOn[key] = any;
            return any;
        }
        #endregion
    }
}