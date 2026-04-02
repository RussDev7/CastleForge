/*
Copyright (c) 2025 RussDev7

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

/*
Sections of this class was taken from 'WorldEdit-CSharp' by RussDev7.
Main Project: https://github.com/RussDev7/WorldEdit-CSharp.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using DNA.Drawing;
using System;

using static WorldEdit.WorldEditCore;

namespace WorldEditCUI
{
    /// <summary>
    /// =================================================================================================
    /// WorldEditCUI - Overlay Renderer (CUIOverlayRenderer)
    /// -------------------------------------------------------------------------------------------------
    /// Overview:
    ///   Central renderer for the WorldEditCUI selection overlay. This class is responsible for drawing:
    ///     • The base selection outline (either edges-only OR edges + 1-block face grid).
    ///     • Optional 16x16 chunk boundary outlines inside the selection.
    ///     • Optional 24x24 "mega-grid" (every 384 blocks) outlines inside the selection.
    ///
    /// Config / Hot-Reload:
    ///   Most settings are config-backed (WorldEditCUI.Config.ini) and can be hot-reloaded:
    ///     • Toggles:   Chunk outlines, mega-grid, base outline mode (grid vs outline).
    ///     • Colors:    CUI outline, chunk outline, mega-grid outline.
    ///     • Thickness: Outline/grid/chunk/mega-grid line thickness (world units).
    ///
    /// Rendering Notes:
    ///   Drawing is done using BasicEffect + VertexPositionColor thick-line quads (no SpriteBatch needed).
    ///   This makes the overlay safe to render inside the game's 3D draw pipeline without interfering
    ///   with the HUD/UI SpriteBatch state.
    /// =================================================================================================
    /// </summary>
    internal class CUIOverlayRenderer
    {
        #region Config-Backed Settings (WorldEditCUI.Config.ini)

        /// <summary>Last-loaded config instance (used for saving toggles/colors/thickness).</summary>
        internal static CUIConfig _config;

        /// <summary>Hotkey string (ex: Ctrl+Shift+R) used by the reload-hotkey patch.</summary>
        internal static string    ReloadHotkey;

        #endregion

        #region Overlay Toggles

        /// <summary>Draw 16x16 chunk boundaries within the selection.</summary>
        internal static volatile bool ShowChunkOutlines  = false;

        /// <summary>Draw the 24x24-chunk mega-grid (every 384 blocks) within the selection.</summary>
        internal static volatile bool ShowChunkGrid      = false;

        /// <summary>
        /// Base selection outline mode:
        /// true  -> OutlineSelectionWithGrid (default).
        /// false -> OutlineSelection (edges only).
        /// </summary>
        internal static volatile bool UseGridBaseOutline = true;

        #endregion

        #region Overlay Colors

        /// <summary>Selection outline color.</summary>
        internal static Color CUIOutlineColor       = Color.LightCoral;

        /// <summary>16x16 chunk boundary color.</summary>
        internal static Color ChunkOutlineColor     = Color.Yellow;

        /// <summary>24x24 chunk mega-grid boundary color.</summary>
        internal static Color ChunkGridOutlineColor = Color.Lime;

        #endregion

        #region Line Thickness (World Units)

        /// <summary>Main selection outline thickness (default 0.06f).</summary>
        internal static volatile float CUIOutlineThickness       = 0.06f;

        /// <summary>Interior grid line thickness used by OutlineSelectionWithGrid (default 0.02f).</summary>
        internal static volatile float CUIGridLineThickness      = 0.02f;

        /// <summary>Thickness for 16x16 chunk boundaries (default 0.025f).</summary>
        internal static volatile float ChunkOutlineThickness     = 0.025f;

        /// <summary>Thickness for the 24x24 mega-grid boundaries (default 0.03f).</summary>
        internal static volatile float ChunkGridOutlineThickness = 0.03f;

        #endregion

        #region Chunk Constants

        // ---------------------------------------------------------------------------------
        // Chunk facts:
        //   - World chunks are 16x16 blocks.
        //   - The game's cached chunk grid is 24x24 chunks (384x384 blocks).
        // ---------------------------------------------------------------------------------

        internal const int ChunkSizeBlocks     = 16;
        internal const int ChunkGridChunks     = 24;
        internal const int ChunkGridSizeBlocks = ChunkSizeBlocks * ChunkGridChunks; // 384.

        #endregion

        #region Selection Overlay Drawing (Base + Chunk Overlays)

        #region Base Outline (Edges Only)

        /// <summary>
        /// Draws only the outer 3D selection box (12 edges).
        /// </summary>
        public static void OutlineSelection(
            GraphicsDevice dev,
            Matrix         view,
            Matrix         projection,
            Vector3        corner1,
            Vector3        corner2,
            Color?         color = null)
        {
            // If the caller didn't specify a color, use LightCoral.
            var outlineColor = color ?? Color.LightCoral;

            // Draw no outlines if; The CLU is disabled, or if either of the points are invalid.
            if (!_enableCLU || corner1 == null || corner2 == null) return;

            // Find min/max on each axis.
            var min = Vector3.Min(corner1, corner2);
            var max = Vector3.Max(corner1, corner2);

            // Expand max by one whole block so outline is on the outside faces.
            max += Vector3.One; // This +1 pushes it to the far face.

            // Build the 8 corners of the box.
            var corners = new Vector3[8]
            {
                new Vector3(min.X, min.Y, min.Z), // 0: Bottom-near-left.
                new Vector3(max.X, min.Y, min.Z), // 1: Bottom-near-right.
                new Vector3(max.X, min.Y, max.Z), // 2: Bottom-far-right.
                new Vector3(min.X, min.Y, max.Z), // 3: Bottom-far-left.

                new Vector3(min.X, max.Y, min.Z), // 4: Top-near-left.
                new Vector3(max.X, max.Y, min.Z), // 5: Top-near-right.
                new Vector3(max.X, max.Y, max.Z), // 6: Top-far-right.
                new Vector3(min.X, max.Y, max.Z), // 7: Top-far-left.
            };

            // List the 12 edges as pairs of indices into the corners array.
            var edges = new (int, int)[]
            {
                // Bottom rectangle.
                (0,1), (1,2), (2,3), (3,0),
                // Top rectangle.
                (4,5), (5,6), (6,7), (7,4),
                // Vertical pillars.
                (0,4), (1,5), (2,6), (3,7)
            };

            // Draw the outline around all edges (config-driven thickness).
            float thickLine = Math.Max(0.0001f, CUIOutlineThickness);
            foreach (var (a, b) in edges)
            {
                var line = new LineF3D(corners[a], corners[b]);
                SolidLineRenderer.DrawSolidLine(dev, view, projection, line, outlineColor, thickLine);
            }
        }
        #endregion

        #region Overlay Entrypoint

        /// <summary>
        /// Draws the WorldEdit selection outline plus optional chunk overlays (config-driven).
        /// </summary>
        public static void DrawSelectionOverlay(
            GraphicsDevice dev,
            Matrix         view,
            Matrix         projection,
            Vector3        corner1,
            Vector3        corner2)
        {
            if (!_enableCLU) return;

            // Base selection outline (mode configurable).
            if (UseGridBaseOutline)
                OutlineSelectionWithGrid(dev, view, projection, corner1, corner2, CUIOutlineColor);
            else
                OutlineSelection(dev, view, projection, corner1, corner2, CUIOutlineColor);

            // 16x16 chunk boundaries (world chunk size).
            if (ShowChunkOutlines)
                DrawChunkBoundariesInSelection(dev, view, projection, corner1, corner2, ChunkSizeBlocks, ChunkOutlineColor, ChunkOutlineThickness);

            // 24x24 "mega grid" (every 384 blocks). Optional debugging overlay.
            if (ShowChunkGrid)
                DrawChunkBoundariesInSelection(dev, view, projection, corner1, corner2, ChunkGridSizeBlocks, ChunkGridOutlineColor, ChunkGridOutlineThickness);
        }
        #endregion

        #region Chunk Boundary Overlays (Selection Surfaces)

        /// <summary>
        /// Draws X/Z-aligned grid lines at a given block step on the selection's surfaces.
        /// The grid is "within" the selection (outer edges are skipped to avoid double-drawing the outline).
        /// </summary>
        private static void DrawChunkBoundariesInSelection(
            GraphicsDevice dev,
            Matrix         view,
            Matrix         projection,
            Vector3        corner1,
            Vector3        corner2,
            int            stepBlocks,
            Color          color,
            float          thickness)
        {
            if (stepBlocks <= 0) return;

            // Match OutlineSelection's faces by expanding max by +1.
            var min = Vector3.Min(corner1, corner2);
            var max = Vector3.Max(corner1, corner2) + Vector3.One;

            int x0 = (int)Math.Floor(min.X);
            int y0 = (int)Math.Floor(min.Y);
            int z0 = (int)Math.Floor(min.Z);

            int x1 = (int)Math.Floor(max.X);
            int y1 = (int)Math.Floor(max.Y);
            int z1 = (int)Math.Floor(max.Z);

            if (x1 <= x0 || y1 <= y0 || z1 <= z0) return;

            int firstX = NextBoundaryAtOrAfter(x0, stepBlocks);
            int firstZ = NextBoundaryAtOrAfter(z0, stepBlocks);

            // Safety valve: Don't spam thousands of lines if someone selects half the planet.
            // (Chunk boundaries are much cheaper than the old per-block grid, but still not free.)
            int approxX = (firstX < x1) ? ((x1 - firstX) / stepBlocks + 1) : 0;
            int approxZ = (firstZ < z1) ? ((z1 - firstZ) / stepBlocks + 1) : 0;
            int approxLines = (approxX + approxZ) * 8;
            if (approxLines > 8000) return;

            // X boundaries: Lines parallel to Z.
            for (int x = firstX; x < x1; x += stepBlocks)
            {
                if (x <= x0 || x >= x1) continue;

                // Top / Bottom faces.
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x, y0, z0), new Vector3(x, y0, z1)), color, thickness);
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x, y1, z0), new Vector3(x, y1, z1)), color, thickness);

                // Front / Back faces (vertical).
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x, y0, z0), new Vector3(x, y1, z0)), color, thickness);
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x, y0, z1), new Vector3(x, y1, z1)), color, thickness);
            }

            // Z boundaries: Lines parallel to X.
            for (int z = firstZ; z < z1; z += stepBlocks)
            {
                if (z <= z0 || z >= z1) continue;

                // Top / Bottom faces.
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x0, y0, z), new Vector3(x1, y0, z)), color, thickness);
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x0, y1, z), new Vector3(x1, y1, z)), color, thickness);

                // Left / Right faces (vertical).
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x0, y0, z), new Vector3(x0, y1, z)), color, thickness);
                SolidLineRenderer.DrawSolidLine(dev, view, projection, new LineF3D(new Vector3(x1, y0, z), new Vector3(x1, y1, z)), color, thickness);
            }
        }
        #endregion

        #region Base Outline (With 1-Block Face Grid)

        /// <summary>
        /// Draws the selection outline + a thin 1-block grid on each face.
        /// </summary>
        public static void OutlineSelectionWithGrid(
            GraphicsDevice dev,
            Matrix         view,
            Matrix         projection,
            Vector3        corner1,
            Vector3        corner2,
            Color?         color = null)
        {
            // If the caller didn't specify a color, use LightCoral.
            var outlineColor = color ?? Color.LightCoral;

            // Draw no outlines If; The CLU is disabled, or if either of the points are invalid.
            if (!_enableCLU || corner1 == null || corner2 == null) return;

            // Find min/max on each axis.
            var min = Vector3.Min(corner1, corner2);
            var max = Vector3.Max(corner1, corner2);

            // Expand max by one whole block so outline is on the outside faces.
            max += Vector3.One; // This +1 pushes it to the far face.

            // Build the 8 corners of the box.
            var corners = new Vector3[8]
            {
                new Vector3(min.X, min.Y, min.Z), // 0: Bottom-near-left.
                new Vector3(max.X, min.Y, min.Z), // 1: Bottom-near-right.
                new Vector3(max.X, min.Y, max.Z), // 2: Bottom-far-right.
                new Vector3(min.X, min.Y, max.Z), // 3: Bottom-far-left.

                new Vector3(min.X, max.Y, min.Z), // 4: Top-near-left.
                new Vector3(max.X, max.Y, min.Z), // 5: Top-near-right.
                new Vector3(max.X, max.Y, max.Z), // 6: Top-far-right.
                new Vector3(min.X, max.Y, max.Z), // 7: Top-far-left.
            };

            // List the 12 edges as pairs of indices into the corners array.
            var edges = new (int, int)[]
            {
                // Bottom rectangle.
                (0,1), (1,2), (2,3), (3,0),
                // Top rectangle.
                (4,5), (5,6), (6,7), (7,4),
                // Vertical pillars.
                (0,4), (1,5), (2,6), (3,7)
            };

            // Main outline (config-driven thickness).
            float thickLine = Math.Max(0.0001f, CUIOutlineThickness);
            foreach (var (a, b) in edges)
            {
                var line = new LineF3D(corners[a], corners[b]);
                SolidLineRenderer.DrawSolidLine(dev, view, projection, line, outlineColor, thickLine);
            }

            // Thin interior grid lines (config-driven thickness).
            float thinLine = Math.Max(0.0001f, CUIGridLineThickness);

            int x0 = (int)min.X, x1 = (int)max.X;
            int y0 = (int)min.Y, y1 = (int)max.Y;
            int z0 = (int)min.Z, z1 = (int)max.Z;

            // Front/Back faces.
            for (int x = x0; x <= x1; x++)
            {
                // Vertical grid lines.
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x, y0, z0), new Vector3(x, y1, z0)), outlineColor, thinLine);
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x, y0, z1), new Vector3(x, y1, z1)), outlineColor, thinLine);
            }
            for (int y = y0; y <= y1; y++)
            {
                // Horizontal grid lines.
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x0, y, z0), new Vector3(x1, y, z0)), outlineColor, thinLine);
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x0, y, z1), new Vector3(x1, y, z1)), outlineColor, thinLine);
            }

            // Left/Right faces.
            for (int z = z0; z <= z1; z++)
            {
                // Vertical grid lines.
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x0, y0, z), new Vector3(x0, y1, z)), outlineColor, thinLine);
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x1, y0, z), new Vector3(x1, y1, z)), outlineColor, thinLine);
            }
            for (int y = y0; y <= y1; y++)
            {
                // Horizontal grid lines.
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x0, y, z0), new Vector3(x0, y, z1)), outlineColor, thinLine);
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                     new LineF3D(new Vector3(x1, y, z0), new Vector3(x1, y, z1)), outlineColor, thinLine);
            }

            // Top/Bottom faces.
            for (int x = x0; x <= x1; x++)
            {
                // Vertical grid lines.
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                    new LineF3D(new Vector3(x, y0, z0), new Vector3(x, y0, z1)), outlineColor, thinLine);
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                    new LineF3D(new Vector3(x, y1, z0), new Vector3(x, y1, z1)), outlineColor, thinLine);
            }
            for (int z = z0; z <= z1; z++)
            {
                // Horizontal grid lines.
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                    new LineF3D(new Vector3(x0, y0, z), new Vector3(x1, y0, z)), outlineColor, thinLine);
                SolidLineRenderer.DrawSolidLine(dev, view, projection,
                    new LineF3D(new Vector3(x0, y1, z), new Vector3(x1, y1, z)), outlineColor, thinLine);
            }
        }
        #endregion

        #region Math Helpers

        /// <summary>
        /// Returns the smallest multiple of <paramref name="step"/> that is >= <paramref name="value"/>.
        /// Uses floor-division so negative coordinates behave correctly.
        /// </summary>
        private static int NextBoundaryAtOrAfter(int value, int step)
        {
            int k = (int)Math.Floor(value / (double)step);
            int b = k * step;
            if (b < value) b += step;
            return b;
        }
        #endregion

        #endregion

        #region SolidLineRenderer (Thick 3D Line Quads)

        /// <summary>
        /// A drop-in replacement for DNA.Drawing.DrawLine,
        /// but forces both verts to your color.
        /// </summary>
        public static class SolidLineRenderer
        {
            // Reused across calls to avoid reallocating.
            private static readonly VertexPositionColor[] _quadVerts = new VertexPositionColor[6];
            private static BasicEffect                    _effect;

            /// <summary>
            /// Draws a 3D line with the same color at both endpoints.
            /// </summary>
            public static void DrawSolidLine(
                GraphicsDevice graphicsDevice,
                Matrix         view,
                Matrix         projection,
                LineF3D        line,
                Color          color,
                float          thickness = 0.06f)
            {
                // Lazy-init effect.
                if (_effect == null)
                {
                    _effect = new BasicEffect(graphicsDevice)
                    {
                        LightingEnabled = false,
                        TextureEnabled = false,
                        VertexColorEnabled = true
                    };
                }

                // Compute camera position from inverse view.
                Matrix invView = Matrix.Invert(view);
                Vector3 camPos = invView.Translation;

                // Find a perp vector: Direction of line, and eye-to-line vector.
                Vector3 dir = line.End - line.Start;
                Vector3 toCamera = Vector3.Normalize(camPos - line.Start);
                Vector3 perp = Vector3.Normalize(Vector3.Cross(dir, toCamera));

                // Scale perp by half-thickness.
                Vector3 offset = perp * (thickness * 0.5f);

                // Build quad corner positions.
                Vector3 v0 = line.Start + offset;
                Vector3 v1 = line.Start - offset;
                Vector3 v2 = line.End + offset;
                Vector3 v3 = line.End - offset;

                // Fill out two triangles (6 verts).
                _quadVerts[0] = new VertexPositionColor(v0, color);
                _quadVerts[1] = new VertexPositionColor(v1, color);
                _quadVerts[2] = new VertexPositionColor(v2, color);
                _quadVerts[3] = new VertexPositionColor(v2, color);
                _quadVerts[4] = new VertexPositionColor(v1, color);
                _quadVerts[5] = new VertexPositionColor(v3, color);

                // Set matrices.
                _effect.World = Matrix.Identity;
                _effect.View = view;
                _effect.Projection = projection;

                // Draw.
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    graphicsDevice.DrawUserPrimitives(
                        PrimitiveType.TriangleList,
                        _quadVerts, 0, 2 // Two triangles.
                    );
                }
            }
        }
        #endregion
    }
}