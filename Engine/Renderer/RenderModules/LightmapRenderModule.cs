using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules
{
    /// <summary>
    /// Lightmap pass (Source 2 look, phase 4.2), right after the G-buffer and decals: redraws every Lightmap Static
    /// gameobject that has a baked lightmap with its lightmap UVs and writes its indirect light + AO and its static lights'
    /// shadow mask into two screen targets (Lightmap.fx). The environment pass then uses the lightmap instead of the probes
    /// and SSAO, and the light passes multiply their mask channel in.
    /// </summary>
    public sealed class LightmapRenderModule : IDisposable
    {
        private readonly Effect _effect;
        private readonly EffectPass _pass;
        private readonly EffectParameter _worldView, _worldViewProj, _farClip, _transposeView;
        private readonly EffectParameter _depthMap, _normalMap, _irradianceMap, _directionMap, _maskMap;
        private readonly VertexBufferBinding[] _bindings = new VertexBufferBinding[2];

        /// <summary>Gameobjects drawn with a lightmap last frame (stats/tests).</summary>
        public int DrawnInstances { get; private set; }

        public LightmapRenderModule(ContentManager content, string shaderPath)
        {
            _effect = content.Load<Effect>(shaderPath);
            _pass = _effect.Techniques["Lightmap"].Passes[0];
            _worldView = _effect.Parameters["WorldView"];
            _worldViewProj = _effect.Parameters["WorldViewProj"];
            _farClip = _effect.Parameters["FarClip"];
            _transposeView = _effect.Parameters["TransposeView"];
            _depthMap = _effect.Parameters["DepthMap"];
            _normalMap = _effect.Parameters["NormalMap"];
            _irradianceMap = _effect.Parameters["IrradianceMap"];
            _directionMap = _effect.Parameters["DirectionMap"];
            _maskMap = _effect.Parameters["MaskMap"];
        }

        public Texture2D DepthMap { set => _depthMap.SetValue(value); }
        public Texture2D NormalMap { set => _normalMap.SetValue(value); }

        /// <summary>
        /// Clears both targets (indirect: 0 = not lightmapped, mask: 1 = lit) and draws the lightmapped gameobjects in view.
        /// </summary>
        public void Draw(GraphicsDevice device, LightmapResources lightmaps, List<BasicEntity> entities, Matrix view,
            Matrix viewProjection, BoundingFrustum frustum, float farClip, RenderTarget2D indirectTarget, RenderTarget2D maskTarget)
        {
            DrawnInstances = 0;
            device.SetRenderTarget(maskTarget);
            device.Clear(Color.White);
            device.SetRenderTarget(indirectTarget);
            device.Clear(Color.Transparent);
            if (lightmaps == null || entities == null) return;

            device.SetRenderTargets(indirectTarget, maskTarget);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            //Two-sided like the materials that draw both faces; the depth test drops what the G-buffer didn't show
            device.RasterizerState = RasterizerState.CullNone;
            //Bilinear lightmaps whatever the previous pass left in the sampler slots
            for (int i = 0; i < 5; i++) device.SamplerStates[i] = SamplerState.LinearClamp;
            _farClip.SetValue(farClip);
            _transposeView.SetValue(Matrix.Transpose(view));

            int boundPage = -1;
            foreach (BasicEntity entity in entities)
            {
                if (entity == null || !entity.LightmapStatic) continue;
                TransformMatrix transform = entity.WorldTransform;
                // Same rules as MeshMaterialLibrary.Draw: hidden meshes and LOD levels above 0 don't show the baked model
                if (transform == null || transform.Hidden || transform.LodLevel != 0) continue;
                LightmapResources.InstanceGpu instance = lightmaps.Find(entity);
                if (instance == null) continue;

                Matrix world = transform.World;
                LightmapResources.MeshGpu mesh = lightmaps.Meshes[instance.Data.Mesh];
                float scale = Math.Max(Math.Abs(entity.Scale.X), Math.Max(Math.Abs(entity.Scale.Y), Math.Abs(entity.Scale.Z)));
                var sphere = new BoundingSphere(Vector3.Transform(mesh.Center, world), mesh.Radius * scale);
                if (!frustum.Intersects(sphere)) continue;

                int page = instance.Data.Page;
                if (page != boundPage)
                {
                    _irradianceMap.SetValue(lightmaps.Irradiance[page]);
                    _directionMap.SetValue(lightmaps.Direction[page]);
                    _maskMap.SetValue(lightmaps.Mask[page]);
                    boundPage = page;
                }
                _worldView.SetValue(world * view);
                _worldViewProj.SetValue(world * viewProjection);
                _pass.Apply();

                for (int p = 0; p < mesh.Parts.Length; p++)
                {
                    LightmapResources.PartGpu part = mesh.Parts[p];
                    VertexBuffer uv = p < instance.Uv.Length ? instance.Uv[p] : null;
                    if (part.Positions == null || uv == null) continue;
                    _bindings[0] = new VertexBufferBinding(part.Positions);
                    _bindings[1] = new VertexBufferBinding(uv);
                    device.SetVertexBuffers(_bindings);
                    device.Indices = part.Indices;
                    device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, part.PrimitiveCount);
                }
                DrawnInstances++;
            }
            device.SetVertexBuffer(null);
        }

        public void Dispose() => _effect?.Dispose();
    }
}
