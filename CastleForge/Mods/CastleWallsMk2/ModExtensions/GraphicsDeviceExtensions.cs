/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using DNA.Drawing;

namespace CastleWallsMk2
{
    /// <summary>
    /// Minimal line-drawing helpers to replace the removed DNA.Drawing.DrawLine().
    /// Uses BasicEffect with vertex colors so gradients work (colors are interpolated).
    /// </summary>
    internal static class GraphicsDeviceExtensions
    {
        // Reusable effect instance (created per-GraphicsDevice).
        private static BasicEffect _lineEffect;

        // Two-vertex buffer reused for each line (start/end).
        private static readonly VertexPositionColor[] _lineVerts = new VertexPositionColor[2];

        /// <summary>
        /// Draw a 3D line with different colors at each end (gradient).
        /// </summary>
        public static void DrawLine(this GraphicsDevice gd, Matrix view, Matrix projection,
                                    LineF3D line, Color startColor, Color endColor)
            => DrawLine(gd, view, projection, line.Start, startColor, line.End, endColor);

        /// <summary>
        /// Draw a 3D line with different colors at each end (gradient).
        /// </summary>
        public static void DrawLine(this GraphicsDevice gd, Matrix view, Matrix projection,
                                    Vector3 start, Color startColor, Vector3 end, Color endColor)
        {
            // Lazily build (or rebuild) the effect when needed
            if (_lineEffect == null || _lineEffect.IsDisposed || _lineEffect.GraphicsDevice != gd)
            {
                _lineEffect = new BasicEffect(gd)
                {
                    VertexColorEnabled = true, // Enable per-vertex coloring (for gradient).
                    TextureEnabled     = false,
                    LightingEnabled    = false
                };
            }

            // Fill the 2 vertices for this line.
            _lineVerts[0] = new VertexPositionColor(start, startColor);
            _lineVerts[1] = new VertexPositionColor(end,   endColor);

            // Set transforms for this draw.
            _lineEffect.World      = Matrix.Identity;
            _lineEffect.View       = view;
            _lineEffect.Projection = projection;

            // Draw one LineList primitive (2 verts = 1 segment).
            foreach (var pass in _lineEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.LineList, _lineVerts, 0, 1);
            }
        }

        /// <summary>
        /// One-color convenience overload (no gradient).
        /// </summary>
        public static void DrawLine(this GraphicsDevice gd, Matrix view, Matrix projection,
                                    LineF3D line, Color color)
            => DrawLine(gd, view, projection, line, color, color);

        /// <summary>
        /// Draw a line that fades from startColor to transparent at the end.
        /// Blending is enabled just for this call.
        /// </summary>
        public static void DrawLineFade(this GraphicsDevice gd, Matrix view, Matrix projection,
                                        LineF3D line, Color startColor)
        {
            // Create a fully transparent version of startColor.
            var endColor = new Color(startColor.R, startColor.G, startColor.B, 0);

            // Temporarily enable alpha blending for the fade.
            var oldBlend = gd.BlendState;
            gd.BlendState = BlendState.AlphaBlend;

            DrawLine(gd, view, projection, line, startColor, endColor);

            // Restore previous blend state.
            gd.BlendState = oldBlend;
        }

        /// <summary>
        /// Draws a solid 3D tracer line between two world-space points.
        /// Uses the existing line helper with the same color at both ends (no gradient).
        /// </summary>
        public static void DrawTracer(this GraphicsDevice gd, Matrix view, Matrix projection,
                                      Vector3 start, Vector3 end, Color color)
        {
            gd.DrawLine(view, projection, start, color, end, color);
        }

        /// <summary>
        /// Draws a gradient 3D tracer line between two world-space points.
        /// </summary>
        public static void DrawTracer(this GraphicsDevice gd, Matrix view, Matrix projection,
                                      Vector3 start, Vector3 end, Color startColor, Color endColor)
        {
            gd.DrawLine(view, projection, start, startColor, end, endColor);
        }

        /// <summary>
        /// Draws a tracer that fades from the start color to transparent at the end.
        /// Useful for subtle ESP/tracer visuals.
        /// </summary>
        public static void DrawTracerFade(this GraphicsDevice gd, Matrix view, Matrix projection,
                                          Vector3 start, Vector3 end, Color startColor)
        {
            gd.DrawLineFade(view, projection, new LineF3D(start, end), startColor);
        }

        /// <summary>
        /// Draws a wireframe axis-aligned box using the provided min/max world-space corners.
        /// Renders all 12 edges through the existing device line helper.
        /// Pass the same color/gradient to get a solid non-gradient box.
        /// </summary>
        public static void DrawWireBox(GraphicsDevice device, Matrix view, Matrix proj,
                                        Vector3 min, Vector3 max,
                                        Color? color = null, Color? gradient = null)
        {
            if (device == null)
                return;

            Color lineColor = color ?? Color.Lime;
            Color lineGradient = gradient ?? lineColor;

            // Compute the 8 corners of the box.
            Vector3 c000 = new Vector3(min.X, min.Y, min.Z);
            Vector3 c100 = new Vector3(max.X, min.Y, min.Z);
            Vector3 c010 = new Vector3(min.X, max.Y, min.Z);
            Vector3 c110 = new Vector3(max.X, max.Y, min.Z);
            Vector3 c001 = new Vector3(min.X, min.Y, max.Z);
            Vector3 c101 = new Vector3(max.X, min.Y, max.Z);
            Vector3 c011 = new Vector3(min.X, max.Y, max.Z);
            Vector3 c111 = new Vector3(max.X, max.Y, max.Z);

            void Edge(Vector3 a, Vector3 b) =>
                device.DrawLine(view, proj, new LineF3D(a, b), lineColor, lineGradient);

            // Bottom face.
            Edge(c000, c100);
            Edge(c100, c101);
            Edge(c101, c001);
            Edge(c001, c000);

            // Top face.
            Edge(c010, c110);
            Edge(c110, c111);
            Edge(c111, c011);
            Edge(c011, c010);

            // Vertical edges.
            Edge(c000, c010);
            Edge(c100, c110);
            Edge(c101, c111);
            Edge(c001, c011);
        }
    }
}