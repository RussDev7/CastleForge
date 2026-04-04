/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using DNA.CastleMinerZ.Terrain;
using Microsoft.Xna.Framework;
using DNA.CastleMinerZ;
using DNA.Drawing.UI;
using DNA.Drawing;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace Restore360Water
{
    /// <summary>
    /// Mod-owned replacement for the dormant vanilla WaterPlane path.
    ///
    /// Summary:
    /// - Builds and renders a custom water surface + surrounding "well" volume.
    /// - Uses the game's WaterEffect shader for above/below water rendering.
    /// - Scales vertically to match the currently active biome water band.
    ///
    /// Notes:
    /// - Uses the game's WaterEffect shader.
    /// - Loads the normal map from !Mods\Restore360Water\Textures\Terrain\water_normalmap.png.
    /// - Uses a configurable vertical water band depth (MaxY -> MinY).
    /// - Creates a fallback generated normal map if the mod texture is unavailable.
    /// - Maintains a reflection render target sized to the current screen.
    /// </summary>
    internal sealed class R360WWaterPlane : Entity
    {
        #region Singleton / Instance

        /// <summary>
        /// Global live instance for the active custom water plane.
        /// </summary>
        public static R360WWaterPlane Instance;

        #endregion

        #region GPU Geometry / Cached Resources

        /// <summary>
        /// Vertex buffer for the top visible water surface.
        /// </summary>
        private VertexBuffer _waterVerts;

        /// <summary>
        /// Vertex buffer for the vertical water volume / well walls.
        /// </summary>
        private VertexBuffer _wellVerts;

        /// <summary>
        /// Cached graphics device passed in during construction.
        /// </summary>
        private readonly GraphicsDevice _graphicsDevice;

        /// <summary>
        /// Cached content manager used to load the game's WaterEffect shader.
        /// </summary>
        private readonly ContentManager _gameContent;

        /// <summary>
        /// Reflection render target used by the water shader.
        /// </summary>
        private RenderTarget2D _reflectionTexture;

        /// <summary>
        /// Water shader effect instance.
        /// </summary>
        private Effect _effect;

        /// <summary>
        /// Water normal map texture used by the shader.
        /// </summary>
        private Texture2D _normalMap;

        #endregion

        #region Public Accessors

        /// <summary>
        /// Exposes the reflection texture for any caller that needs it.
        /// </summary>
        public RenderTarget2D ReflectionTexture => _reflectionTexture;

        #endregion

        #region Construction / Initialization

        /// <summary>
        /// Creates the custom water plane entity, builds its geometry, and ensures
        /// required shader/texture resources are available.
        /// </summary>
        public R360WWaterPlane(GraphicsDevice gd, ContentManager gameContent)
        {
            Instance        = this;
            _graphicsDevice = gd;
            _gameContent    = gameContent;

            BuildBuffers(gd);
            EnsureResources(gd, allowReflectionRecreate: true);
            Collidee     = false;
            DrawPriority = 1000;
        }
        #endregion

        #region Active Water Band Helpers

        /// <summary>
        /// Gets the active biome water band depth in world units.
        ///
        /// Notes:
        /// - The water well mesh is authored at a fixed height of 128 units.
        /// - This value is used to vertically scale that mesh to match the current biome band.
        /// - A tiny fallback depth is returned if the band is invalid or collapsed.
        /// </summary>
        private static float CurrentBandDepth
        {
            get
            {
                float depth = R360W_Settings.CurrentWaterMaxY - R360W_Settings.CurrentWaterMinY;
                return (depth <= 0.001f) ? 0.5f : depth;
            }
        }
        #endregion

        #region Geometry Creation

        /// <summary>
        /// Builds the static vertex buffers for:
        /// - the top water surface quad
        /// - the surrounding water "well" volume
        ///
        /// Notes:
        /// - Geometry is authored once and reused every frame.
        /// - The well height is authored at -128 Y and later scaled to match the active water band depth.
        /// </summary>
        private void BuildBuffers(GraphicsDevice gd)
        {
            PositionVX[] verts = new PositionVX[30];
            float width = 384f;
            float depth = 384f;
            float height = -128f;

            verts[0] = new PositionVX(new Vector3(width, 0f, 0f));
            verts[1] = new PositionVX(new Vector3(width, 0f, depth));
            verts[2] = new PositionVX(new Vector3(0f, 0f, 0f));
            verts[3] = new PositionVX(new Vector3(0f, 0f, 0f));
            verts[4] = new PositionVX(new Vector3(width, 0f, depth));
            verts[5] = new PositionVX(new Vector3(0f, 0f, depth));
            _waterVerts = new VertexBuffer(gd, typeof(PositionVX), 6, BufferUsage.WriteOnly);
            _waterVerts.SetData(verts, 0, 6);

            verts[0]  = new PositionVX(new Vector3(width, 0f, 0f));
            verts[1]  = new PositionVX(new Vector3(width, height, 0f));
            verts[2]  = new PositionVX(new Vector3(0f, 0f, 0f));
            verts[3]  = new PositionVX(new Vector3(0f, 0f, 0f));
            verts[4]  = new PositionVX(new Vector3(width, height, 0f));
            verts[5]  = new PositionVX(new Vector3(0f, height, 0f));
            verts[6]  = new PositionVX(new Vector3(0f, 0f, depth));
            verts[7]  = new PositionVX(new Vector3(0f, height, depth));
            verts[8]  = new PositionVX(new Vector3(width, 0f, depth));
            verts[9]  = new PositionVX(new Vector3(width, 0f, depth));
            verts[10] = new PositionVX(new Vector3(0f, height, depth));
            verts[11] = new PositionVX(new Vector3(width, height, depth));
            verts[12] = new PositionVX(new Vector3(width, 0f, depth));
            verts[13] = new PositionVX(new Vector3(width, height, depth));
            verts[14] = new PositionVX(new Vector3(width, 0f, 0f));
            verts[15] = new PositionVX(new Vector3(width, 0f, 0f));
            verts[16] = new PositionVX(new Vector3(width, height, depth));
            verts[17] = new PositionVX(new Vector3(width, height, 0f));
            verts[18] = new PositionVX(new Vector3(0f, 0f, 0f));
            verts[19] = new PositionVX(new Vector3(0f, height, 0f));
            verts[20] = new PositionVX(new Vector3(0f, 0f, depth));
            verts[21] = new PositionVX(new Vector3(0f, 0f, depth));
            verts[22] = new PositionVX(new Vector3(0f, height, 0f));
            verts[23] = new PositionVX(new Vector3(0f, height, depth));
            verts[24] = new PositionVX(new Vector3(width, height, 0f));
            verts[25] = new PositionVX(new Vector3(width, height, depth));
            verts[26] = new PositionVX(new Vector3(0f, height, 0f));
            verts[27] = new PositionVX(new Vector3(0f, height, 0f));
            verts[28] = new PositionVX(new Vector3(width, height, depth));
            verts[29] = new PositionVX(new Vector3(0f, height, depth));
            _wellVerts = new VertexBuffer(gd, typeof(PositionVX), verts.Length, BufferUsage.WriteOnly);
            _wellVerts.SetData(verts, 0, verts.Length);
        }
        #endregion

        #region Rendering

        /// <summary>
        /// Draws the custom water plane and water well using the game's WaterEffect shader.
        ///
        /// Flow:
        /// - Early out if terrain or biome water is inactive.
        /// - Ensure shader/texture/render-target resources exist.
        /// - Update effect parameters for view/projection/light/water color.
        /// - Scale and position the water geometry to the active water band.
        /// - Choose above-water or underwater technique based on eye position.
        /// - Draw the well first, then the water surface.
        ///
        /// Notes:
        /// - When the game is currently drawing a reflection pass, only the reflection matrix is updated.
        /// - The active water band is defined by CurrentWaterMaxY / CurrentWaterMinY.
        /// </summary>
        public override void Draw(GraphicsDevice device, GameTime gameTime, Matrix view, Matrix projection)
        {
            if (BlockTerrain.Instance == null || !R360W_Settings.CurrentBiomeEnabled)
                return;

            bool drawingReflection = CastleMinerZGame.Instance?.DrawingReflection == true;

            // Never recreate/dispose the reflection RT during the reflection pass itself.
            EnsureResources(device, allowReflectionRecreate: !drawingReflection);

            if (_effect == null || _reflectionTexture == null || _normalMap == null)
                return;

            if (drawingReflection)
            {
                _effect.Parameters["Reflection"]?.SetValue(view);
                return;
            }

            _effect.Parameters["Projection"].SetValue(projection);
            _effect.Parameters["View"].SetValue(view);
            _effect.Parameters["Time"].SetValue((float)gameTime.TotalGameTime.TotalSeconds);
            _effect.Parameters["LightDirection"].SetValue(BlockTerrain.Instance.VectorToSun);

            Vector2 pl = BlockTerrain.Instance.GetLightAtPoint(BlockTerrain.Instance.EyePos);
            _effect.Parameters["SunLightColor"].SetValue(BlockTerrain.Instance.SunSpecular.ToVector3() * (float)Math.Pow(pl.X, 10.0));
            _effect.Parameters["TorchLightColor"].SetValue(BlockTerrain.Instance.TorchColor.ToVector3() * pl.Y);

            float yScale  = CurrentBandDepth / 128f;
            Matrix world  = Matrix.CreateScale(1f, yScale, 1f);
            Vector3 basev = IntVector3.ToVector3(BlockTerrain.Instance._worldMin);
            basev.Y = R360W_Settings.CurrentWaterMaxY;
            world *= Matrix.CreateTranslation(basev);

            _effect.Parameters["World"].SetValue(world);
            _effect.Parameters["EyePos"].SetValue(BlockTerrain.Instance.EyePos);
            _effect.Parameters["ReflectionTexture"].SetValue(_reflectionTexture);
            _effect.Parameters["WaterColor"].SetValue(BlockTerrain.Instance.GetActualWaterColor());

            BlendState oldBlend       = device.BlendState;
            RasterizerState oldRaster = device.RasterizerState;

            bool useUnderwaterTechnique =
                R360W_Settings.UseVanillaUnderwaterEngine                         &&
                R360W_Settings.CurrentBiomeEnabled                                &&
                BlockTerrain.Instance.EyePos.Y <= R360W_Settings.CurrentWaterMaxY &&
                BlockTerrain.Instance.EyePos.Y >= R360W_Settings.CurrentWaterMinY;

            if (!useUnderwaterTechnique)
            {
                _effect.CurrentTechnique = _effect.Techniques[0];
                device.BlendState        = BlendState.AlphaBlend;
            }
            else
            {
                _effect.CurrentTechnique = _effect.Techniques[1];
                device.BlendState        = BlendState.Opaque;
            }

            device.DepthStencilState = DepthStencilState.DepthRead;
            device.SetVertexBuffer(_wellVerts);
            _effect.CurrentTechnique.Passes[1].Apply();
            device.DrawPrimitives(PrimitiveType.TriangleList, 0, 10);

            device.SetVertexBuffer(_waterVerts);
            _effect.CurrentTechnique.Passes[0].Apply();
            device.RasterizerState = RasterizerState.CullNone;
            device.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = oldBlend;
            device.RasterizerState = oldRaster;

            base.Draw(device, gameTime, view, projection);
        }
        #endregion

        #region Resource Setup / Recovery

        /// <summary>
        /// Ensures the shader effect, reflection target, and normal map are available.
        ///
        /// Notes:
        /// - Attempts to use the provided device first, then cached device, then game device.
        /// - Recreates the normal map if it became disposed.
        /// - Uses a generated fallback normal map if the mod texture cannot be loaded.
        /// </summary>
        private void EnsureResources(GraphicsDevice device, bool allowReflectionRecreate)
        {
            var gd = device ?? _graphicsDevice ?? CastleMinerZGame.Instance?.GraphicsDevice;
            if (gd == null)
                return;

            if (_effect == null)
            {
                try
                {
                    _effect = (_gameContent ?? CastleMinerZGame.Instance?.Content)?.Load<Effect>(@"Shaders\WaterEffect");
                }
                catch (Exception ex)
                {
                    Log($"Failed loading WaterEffect: {ex.Message}.");
                }
            }

            EnsureReflectionTarget(gd, allowReflectionRecreate);

            if (IsDisposed(_normalMap))
                _normalMap = null;

            if (_normalMap == null)
            {
                _normalMap = GamePatches.ModContent.LoadTexture(gd, @"Textures\Terrain\water_normalmap.png");
                if (_normalMap == null)
                {
                    _normalMap = CreateFallbackNormalMap(gd);
                    Log("Using generated fallback water normal map.");
                }
            }

            try
            {
                _effect?.Parameters["NormalTexture"]?.SetValue(_normalMap);
            }
            catch (ObjectDisposedException)
            {
                _normalMap = null;
            }
            catch { }
        }

        /// <summary>
        /// Ensures the reflection render target exists and matches the current screen size.
        ///
        /// Notes:
        /// - Recreates the render target when screen dimensions change.
        /// - Clears the target once after recreation.
        /// </summary>
        private void EnsureReflectionTarget(GraphicsDevice gd, bool allowReflectionRecreate)
        {
            int width  = Math.Max(1, gd.PresentationParameters.BackBufferWidth);
            int height = Math.Max(1, gd.PresentationParameters.BackBufferHeight);

            bool recreate =
                _reflectionTexture == null         ||
                IsDisposed(_reflectionTexture)     ||
                _reflectionTexture.Width  != width ||
                _reflectionTexture.Height != height;

            if (!recreate || !allowReflectionRecreate)
                return;

            RenderTarget2D oldTarget = _reflectionTexture;
            RenderTarget2D newTarget = null;

            try
            {
                newTarget = new RenderTarget2D(gd, width, height, true, SurfaceFormat.Color, DepthFormat.Depth16);

                gd.SetRenderTarget(newTarget);
                gd.Clear(Color.Black);
                gd.SetRenderTarget(null);

                _reflectionTexture = newTarget;

                // Retarget any existing reflection CameraView before the old RT is disposed.
                GamePatches.SyncReflectionViewTarget(_reflectionTexture);
            }
            catch
            {
                try { newTarget?.Dispose(); } catch { }
                throw;
            }
            finally
            {
                try { gd.SetRenderTarget(null); } catch { }
            }

            try { oldTarget?.Dispose(); } catch { }
        }

        /// <summary>
        /// Ensures the reflection render target exists and is recreated when needed.
        /// This is intended for safe setup/update paths outside the active reflection draw pass.
        /// </summary>
        public void EnsureReflectionTargetReady(GraphicsDevice gd)
        {
            EnsureReflectionTarget(gd, allowReflectionRecreate: true);
        }
        #endregion

        #region Disposal Guards

        /// <summary>
        /// Returns true when a Texture2D is null or already disposed.
        /// </summary>
        private static bool IsDisposed(Texture2D tex)
        {
            if (tex == null)
                return true;
            try { _ = tex.Width; return false; }
            catch (ObjectDisposedException) { return true; }
        }

        /// <summary>
        /// Returns true when a RenderTarget2D is null or already disposed.
        /// </summary>
        private static bool IsDisposed(RenderTarget2D tex)
        {
            if (tex == null)
                return true;
            try { _ = tex.Width; return false; }
            catch (ObjectDisposedException) { return true; }
        }
        #endregion

        #region Fallback Normal Map Generation

        /// <summary>
        /// Creates a small generated fallback normal map when the mod texture file is missing.
        ///
        /// Notes:
        /// - This keeps the water shader functional even without the external texture asset.
        /// - The pattern is procedurally generated from a simple wave-like height field.
        /// </summary>
        private static Texture2D CreateFallbackNormalMap(GraphicsDevice gd)
        {
            const int size = 64;
            Texture2D tex = new Texture2D(gd, size, size, false, SurfaceFormat.Color);
            Color[] data = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float fy = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float fx = (float)x / size;
                    float dhdx = (float)(Math.Cos((fx * Math.PI * 2.0) * 2.0) * 0.25 + Math.Cos((fx * Math.PI * 2.0 + fy * Math.PI * 2.0) * 1.5) * 0.15);
                    float dhdy = (float)(Math.Sin((fy * Math.PI * 2.0) * 2.0) * 0.25 + Math.Sin((fx * Math.PI * 2.0 - fy * Math.PI * 2.0) * 1.5) * 0.15);
                    Vector3 n = Vector3.Normalize(new Vector3(-dhdx, -dhdy, 1f));
                    byte r = (byte)(MathHelper.Clamp(n.X * 0.5f + 0.5f, 0f, 1f) * 255f);
                    byte g = (byte)(MathHelper.Clamp(n.Y * 0.5f + 0.5f, 0f, 1f) * 255f);
                    byte b = (byte)(MathHelper.Clamp(n.Z * 0.5f + 0.5f, 0f, 1f) * 255f);
                    data[y * size + x] = new Color(r, g, b, 255);
                }
            }

            tex.SetData(data);
            return tex;
        }
        #endregion

        #region Vertex Format

        /// <summary>
        /// Minimal position-only vertex used by the water surface and well geometry.
        /// </summary>
        private struct PositionVX : IVertexType
        {
            VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
            public PositionVX(Vector3 pos) { Position = pos; }
            public Vector3 Position;
            public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0));
        }
        #endregion
    }
}