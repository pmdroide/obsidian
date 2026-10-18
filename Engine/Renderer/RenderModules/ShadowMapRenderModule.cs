using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules.Default;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules
{
    public class ShadowMapRenderModule : IRenderModule, IFoliageWindModule
    {
        private Effect _shader;

        private EffectParameter _WorldViewProj;
        private EffectParameter _WorldView;
        private EffectParameter _World;
        private EffectParameter _View;
        private EffectParameter _ViewProjection;
        private EffectParameter _LightPositionWS;
        private EffectParameter _FarClip;
        private EffectParameter _SizeBias;
        private EffectParameter _MaskTexture;

        //Linear = VS Depth -> used for directional lights
        private EffectPass _linearPass;
        //Linear with a mask texture (leaves) -> cut-out shadows
        private EffectPass _linearAlphaPass;

        //Distance = distance(pixel, light) -> used for omnidirectional lights
        private EffectPass _distancePass;
        private EffectPass _distanceAlphaPass;

        private Passes _pass;

        private BoundingFrustum _boundingFrustumShadow;

        private enum Passes
        {
            Directional,
            DirectionalAlpha,
            Omnidirectional,
            OmnidirectionalAlpha
        };

        public ShadowMapRenderModule(ContentManager content, string shaderPath)
        {
            Load(content, shaderPath);
            Initialize();
        }

        public void Initialize()
        {
            _WorldViewProj = _shader.Parameters["WorldViewProj"];
            _WorldView = _shader.Parameters["WorldView"];
            _World = _shader.Parameters["World"];
            _View = _shader.Parameters["View"];
            _ViewProjection = _shader.Parameters["ViewProjection"];
            Wind = new FoliageWindParameters(_shader);
            _LightPositionWS = _shader.Parameters["LightPositionWS"];
            _FarClip = _shader.Parameters["FarClip"];
            _SizeBias = _shader.Parameters["SizeBias"];
            _MaskTexture = _shader.Parameters["MaskTexture"];

            _linearPass = _shader.Techniques["DrawLinearDepth"].Passes[0];
            _linearAlphaPass = _shader.Techniques["DrawLinearDepthAlpha"].Passes[0];
            _distancePass = _shader.Techniques["DrawDistanceDepth"].Passes[0];
            _distanceAlphaPass = _shader.Techniques["DrawDistanceDepthAlpha"].Passes[0];
        }

        /// <summary>Foliage Wind parameters of the shadow map shader.</summary>
        public FoliageWindParameters Wind { get; private set; }

        public void Load(ContentManager content, string shaderPath)
        {
            _shader = content.Load<Effect>(shaderPath);
        }

        public void Draw(GraphicsDevice graphicsDevice, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities, List<PointLight> pointLights, List<DirectionalLight> dirLights, Camera camera)
        {
            _pass = Passes.Omnidirectional;
            DrawLocalLightShadows(graphicsDevice, meshMaterialLibrary, entities, pointLights, camera);

            _pass = Passes.Directional;

            for (int index = 0; index < dirLights.Count; index++)
            {
                DirectionalLight light = dirLights[index];
                if (!light.IsEnabled || !light.CastShadows)
                {
                    light.ActiveCascades = 0;
                    continue;
                }

                GameStats.shadowMaps += light.CascadeCount;
                if (DrawCascades(graphicsDevice, light, camera, meshMaterialLibrary, entities))
                    camera.HasChanged = true;
                light.HasChanged = false;
            }
        }

        // ---- Local light shadows: one atlas for every point and spot light (Source 2 look, phase 2.2) ----

        private Lighting.ShadowAtlas _atlas;
        //Lights holding atlas tiles, so tiles of removed lights can be given back
        private readonly HashSet<PointLight> _tileOwners = new HashSet<PointLight>();
        private readonly List<(PointLight Light, float Priority, int Size)> _shadowCandidates = new List<(PointLight, float, int)>();
        private readonly HashSet<PointLight> _seen = new HashSet<PointLight>();

        /// <summary>The shared shadow atlas of point and spot lights (null before the first shadowed light).</summary>
        public Lighting.ShadowAtlas Atlas => _atlas;

        private static Matrix FaceView(Vector3 position, CubeMapFace face)
        {
            switch (face)
            {
                case CubeMapFace.PositiveX: return Matrix.CreateLookAt(position, position + Vector3.UnitX, Vector3.UnitZ);
                case CubeMapFace.NegativeX: return Matrix.CreateLookAt(position, position - Vector3.UnitX, Vector3.UnitZ);
                case CubeMapFace.PositiveY: return Matrix.CreateLookAt(position, position + Vector3.UnitY, Vector3.UnitZ);
                case CubeMapFace.NegativeY: return Matrix.CreateLookAt(position, position - Vector3.UnitY, Vector3.UnitZ);
                case CubeMapFace.PositiveZ: return Matrix.CreateLookAt(position, position + Vector3.UnitZ, Vector3.UnitX);
                default: return Matrix.CreateLookAt(position, position - Vector3.UnitZ, Vector3.UnitX);
            }
        }

        private void ReleaseTiles(PointLight light)
        {
            if (light.ShadowTileSize > 0 && _atlas != null)
                for (int f = 0; f < 6; f++)
                    if (light.ShadowTiles[f].W > 0)
                        _atlas.Free(new Point((int)light.ShadowTiles[f].X, (int)light.ShadowTiles[f].Y), light.ShadowTileSize);
            for (int f = 0; f < 6; f++) light.ShadowTiles[f] = Vector4.Zero;
            light.ShadowTileSize = 0;
            light.ShadowTilesDrawn = false;
            _tileOwners.Remove(light);
        }

        /// <summary>
        /// Shadows of point and spot lights. Lights are ranked by how big they look from the camera; each gets atlas tiles
        /// sized by that (up to its Shadow Resolution), lights that look tiny or are beyond the fade distance get none,
        /// and at most <see cref="GameSettings.g_shadowupdatesperframe"/> lights redraw per frame (time slicing), the
        /// biggest first. Lights whose tiles hold their current shadows keep them (the cache); Static Shadows never redraw.
        /// </summary>
        private void DrawLocalLightShadows(GraphicsDevice graphicsDevice, MeshMaterialLibrary meshMaterialLibrary,
            List<BasicEntity> entities, List<PointLight> pointLights, Camera camera)
        {
            int atlasSize = Math.Clamp(Lighting.ShadowAtlas.NextPowerOfTwo(GameSettings.g_shadowatlassize), 512, 8192);
            if (_atlas != null && _atlas.Size != atlasSize)
            {
                foreach (PointLight owner in new List<PointLight>(_tileOwners)) ReleaseTiles(owner);
                _atlas.Dispose();
                _atlas = null;
            }

            float fadeStart = Math.Max(GameSettings.g_shadowfadestart, 0);
            float fadeEnd = Math.Max(GameSettings.g_shadowfadeend, fadeStart + 0.01f);

            _shadowCandidates.Clear();
            _seen.Clear();
            for (int index = 0; index < pointLights.Count; index++)
            {
                PointLight light = pointLights[index];
                _seen.Add(light);
                light.ShadowStrength = 0;
                if (!light.IsEnabled || !light.CastShadows || light.Radius <= 0) continue;

                //Distance to the light's sphere: inside it the light fills the view
                float distance = Math.Max(Vector3.Distance(camera.Position, light.Position) - light.Radius, 0);
                float strength = 1 - MathHelper.Clamp((distance - fadeStart) / (fadeEnd - fadeStart), 0, 1);
                //Angular size of the light's sphere (r / d): Source 2's r_size_cull_threshold_shadow
                float coverage = light.Radius / Math.Max(Vector3.Distance(camera.Position, light.Position), 0.01f);
                if (strength <= 0 || coverage < GameSettings.g_shadowsizecull) continue;

                int size = Lighting.ShadowAtlas.NextPowerOfTwo(
                    (int)(light.ShadowResolution * MathHelper.Clamp(coverage, 0.125f, 1)));
                size = Math.Clamp(size, Lighting.ShadowAtlas.MinTileSize, Math.Min(light.ShadowResolution, atlasSize / 4));
                light.ShadowStrength = strength;
                _shadowCandidates.Add((light, coverage * (light.StaticShadows ? 1.5f : 1), size));
            }

            //Lights that left the scene or lost their shadows give their tiles back
            foreach (PointLight owner in new List<PointLight>(_tileOwners))
                if (!_seen.Contains(owner) || owner.ShadowStrength <= 0)
                    ReleaseTiles(owner);
            if (_shadowCandidates.Count == 0) return;

            _atlas ??= new Lighting.ShadowAtlas(graphicsDevice, atlasSize);
            _shadowCandidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            //Tiles: keep the ones of the right size, then hand out new ones, biggest lights first
            for (int i = 0; i < _shadowCandidates.Count; i++)
            {
                var (light, _, size) = _shadowCandidates[i];
                //A turned spot light may need other cube faces
                if (light.ShadowTileSize == size && !(light is SpotLight && light.HasChanged)) continue;
                ReleaseTiles(light);
            }
            for (int i = 0; i < _shadowCandidates.Count; i++)
            {
                var (light, _, size) = _shadowCandidates[i];
                if (light.ShadowTileSize > 0) continue;
                for (int tile = size; tile >= Lighting.ShadowAtlas.MinTileSize && light.ShadowTileSize == 0; tile /= 2)
                {
                    if (!AllocateFaces(light, tile)) continue;
                }
            }

            //Draw: new tiles and changed lights first, then lights something moved in front of, within the budget
            int budget = Math.Max(GameSettings.g_shadowupdatesperframe, 1);
            bool targetSet = false;
            for (int i = 0; i < _shadowCandidates.Count; i++)
            {
                PointLight light = _shadowCandidates[i].Light;
                if (light.ShadowTileSize == 0) continue;
                GameStats.shadowMaps += 6;

                bool redraw = !light.ShadowTilesDrawn || (light.HasChanged && !light.StaticShadows);
                if (!redraw && !light.StaticShadows)
                {
                    redraw = AnyObjectMovedFor(light, meshMaterialLibrary, entities);
                    //The check culled moved meshes against the light's faces: the view culls again
                    if (redraw) camera.HasChanged = true;
                }
                if (!redraw) continue;
                if (budget <= 0)
                {
                    //Out of budget this frame: lights without any shadows yet stay unshadowed, the rest keep their old ones
                    continue;
                }
                budget--;

                if (!targetSet)
                {
                    graphicsDevice.SetRenderTarget(_atlas.Target);
                    targetSet = true;
                }
                DrawLightTiles(graphicsDevice, light, meshMaterialLibrary, entities);
                light.ShadowTilesDrawn = true;
                light.HasChanged = false;
                camera.HasChanged = true;
            }
        }

        private bool AllocateFaces(PointLight light, int size)
        {
            var allocated = new List<(int Face, Point Origin)>(6);
            for (int f = 0; f < 6; f++)
            {
                if (!LightSeesFace(light, (CubeMapFace)f)) continue;
                if (!_atlas.TryAllocate(size, out Point origin))
                {
                    foreach (var (_, o) in allocated) _atlas.Free(o, size);
                    return false;
                }
                allocated.Add((f, origin));
            }
            for (int f = 0; f < 6; f++) light.ShadowTiles[f] = Vector4.Zero;
            foreach (var (face, origin) in allocated) light.ShadowTiles[face] = new Vector4(origin.X, origin.Y, size, 1);
            light.ShadowTileSize = size;
            light.ShadowTilesDrawn = false;
            _tileOwners.Add(light);
            return true;
        }

        private bool AnyObjectMovedFor(PointLight light, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities)
        {
            Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, 0.05f, light.Radius);
            for (int f = 0; f < 6; f++)
            {
                if (light.ShadowTiles[f].W <= 0) continue;
                Matrix viewProjection = FaceView(light.Position, (CubeMapFace)f) * projection;
                if (_boundingFrustumShadow != null) _boundingFrustumShadow.Matrix = viewProjection;
                else _boundingFrustumShadow = new BoundingFrustum(viewProjection);
                if (meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, false, light.Position)) return true;
            }
            return false;
        }

        private void DrawLightTiles(GraphicsDevice graphicsDevice, PointLight light, MeshMaterialLibrary meshMaterialLibrary,
            List<BasicEntity> entities)
        {
            Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, 0.05f, light.Radius);
            _FarClip.SetValue(light.Radius);
            _LightPositionWS.SetValue(light.Position);

            for (int f = 0; f < 6; f++)
            {
                Vector4 tile = light.ShadowTiles[f];
                if (tile.W <= 0) continue;
                CubeMapFace face = (CubeMapFace)f;
                Matrix viewProjection = FaceView(light.Position, face) * projection;
                switch (face)
                {
                    case CubeMapFace.PositiveX: light.LightViewProjectionPositiveX = viewProjection; break;
                    case CubeMapFace.NegativeX: light.LightViewProjectionNegativeX = viewProjection; break;
                    case CubeMapFace.PositiveY: light.LightViewProjectionPositiveY = viewProjection; break;
                    case CubeMapFace.NegativeY: light.LightViewProjectionNegativeY = viewProjection; break;
                    case CubeMapFace.PositiveZ: light.LightViewProjectionPositiveZ = viewProjection; break;
                    default: light.LightViewProjectionNegativeZ = viewProjection; break;
                }

                var viewport = new Viewport((int)tile.X, (int)tile.Y, (int)tile.Z, (int)tile.Z);
                graphicsDevice.Viewport = viewport;
                graphicsDevice.ScissorRectangle = viewport.Bounds;
                //The tile may hold another light's old shadows: clear it to far (0) even if nothing is drawn into it
                meshMaterialLibrary.ClearShadowViewport();

                if (_boundingFrustumShadow != null) _boundingFrustumShadow.Matrix = viewProjection;
                else _boundingFrustumShadow = new BoundingFrustum(viewProjection);
                meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, true, light.Position);

                meshMaterialLibrary.SkipShadowClear = true;
                try
                {
                    meshMaterialLibrary.Draw(renderType: MeshMaterialLibrary.RenderType.ShadowOmnidirectional,
                        viewProjection: viewProjection, lightViewPointChanged: true, hasAnyObjectMoved: true, renderModule: this);
                }
                finally
                {
                    meshMaterialLibrary.SkipShadowClear = false;
                }
            }
        }

        private static bool LightSeesFace(PointLight light, CubeMapFace face)
        {
            if (!(light is SpotLight spot)) return true;

            switch (face)
            {
                case CubeMapFace.PositiveX: return spot.ConeTouchesCubeFace(Vector3.UnitX);
                case CubeMapFace.NegativeX: return spot.ConeTouchesCubeFace(-Vector3.UnitX);
                case CubeMapFace.PositiveY: return spot.ConeTouchesCubeFace(Vector3.UnitY);
                case CubeMapFace.NegativeY: return spot.ConeTouchesCubeFace(-Vector3.UnitY);
                case CubeMapFace.PositiveZ: return spot.ConeTouchesCubeFace(Vector3.UnitZ);
                default: return spot.ConeTouchesCubeFace(-Vector3.UnitZ);
            }
        }

        private readonly Vector3[] _sliceCorners = new Vector3[8];

        /// <summary>
        /// Cascade split distances (practical scheme: <paramref name="lambda"/> 0 = uniform, 1 = logarithmic) from
        /// <paramref name="near"/> to <paramref name="far"/>. ends[i] = where cascade i ends.
        /// </summary>
        public static void SplitCascades(float near, float far, int count, float lambda, float[] ends)
        {
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                float logarithmic = near * MathF.Pow(far / near, t);
                float uniform = near + (far - near) * t;
                ends[i - 1] = MathHelper.Lerp(uniform, logarithmic, lambda);
            }
        }

        private readonly float[] _ends = new float[DirectionalLight.MaxCascades];

        /// <summary>
        /// Fits the light's cascades to the camera (stable: bounding spheres of each frustum slice, snapped to whole shadow
        /// texels so they don't shimmer when the camera moves or turns) and renders them into the atlas.
        /// Returns whether anything was drawn.
        /// </summary>
        private bool DrawCascades(GraphicsDevice graphicsDevice, DirectionalLight light, Camera camera,
            MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities)
        {
            int count = light.CascadeCount;
            int resolution = light.CascadeResolution;
            int columns = count > 1 ? 2 : 1;
            int rows = count > 2 ? 2 : 1;
            if (light.ShadowMap == null || light.ShadowMap.Width != resolution * columns || light.ShadowMap.Height != resolution * rows)
            {
                light.ShadowMap?.Dispose();
                light.ShadowMap = new RenderTarget2D(graphicsDevice, resolution * columns, resolution * rows, false,
                    SurfaceFormat.Single, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
                light.HasChanged = true;
            }

            Vector3 dir = Vector3.Normalize(light.Direction);
            Vector3 up = Math.Abs(dir.Y) > 0.99f ? Vector3.UnitZ : -Vector3.UnitY;
            Matrix lightRotation = Matrix.CreateLookAt(Vector3.Zero, dir, up);
            Matrix inverseRotation = Matrix.Invert(lightRotation);

            const float near = 0.1f;
            float far = Math.Min(light.ShadowDistance, GameSettings.g_farplane);
            SplitCascades(near, far, count, light.SplitLambda, _ends);

            float aspect = GameSettings.g_screenwidth / (float)Math.Max(GameSettings.g_screenheight, 1);
            float tanY = MathF.Tan(camera.FieldOfView * 0.5f);
            float tanX = tanY * aspect;
            Vector3 forward = Vector3.Normalize(camera.Forward);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
            Vector3 camUp = Vector3.Cross(right, forward);

            bool moved = light.HasChanged;
            float depthExtension = Math.Max(light.ShadowDepth, 10);
            for (int i = 0; i < count; i++)
            {
                float sliceNear = i == 0 ? near : _ends[i - 1];
                float sliceFar = _ends[i];
                //Corners of this slice of the camera frustum
                for (int c = 0; c < 8; c++)
                {
                    float d = c < 4 ? sliceNear : sliceFar;
                    float sx = (c & 1) == 0 ? -1 : 1;
                    float sy = (c & 2) == 0 ? -1 : 1;
                    _sliceCorners[c] = camera.Position + forward * d + right * (sx * tanX * d) + camUp * (sy * tanY * d);
                }
                Vector3 center = Vector3.Zero;
                foreach (Vector3 corner in _sliceCorners) center += corner;
                center /= 8;
                float radius = 0;
                foreach (Vector3 corner in _sliceCorners) radius = Math.Max(radius, Vector3.Distance(corner, center));
                //Quantized so the box size, and with it the texel size, only changes in steps
                radius = MathF.Ceiling(radius * 4) / 4;

                //Snap the centre to whole texels in light space
                float texel = 2 * radius / resolution;
                Vector3 lightSpaceCenter = Vector3.Transform(center, lightRotation);
                lightSpaceCenter.X = MathF.Floor(lightSpaceCenter.X / texel) * texel;
                lightSpaceCenter.Y = MathF.Floor(lightSpaceCenter.Y / texel) * texel;
                center = Vector3.Transform(lightSpaceCenter, inverseRotation);

                Vector3 eye = center - dir * (radius + depthExtension);
                Matrix view = Matrix.CreateLookAt(eye, center, up);
                float farClip = 2 * radius + depthExtension;
                Matrix projection = Matrix.CreateOrthographic(2 * radius, 2 * radius, 0, farClip);

                //Cascades are redrawn only when they moved or something inside them did
                Matrix viewProjection = view * projection;
                if (viewProjection != light.CascadeViewProjection[i] || light.ActiveCascades != count) moved = true;
                light.CascadeView[i] = view;
                light.CascadeViewProjection[i] = viewProjection;
                SetComponent(ref light.CascadeEnds, i, sliceFar);
                SetComponent(ref light.CascadeFarClip, i, farClip);
                SetComponent(ref light.CascadeTexelSize, i, texel);
            }
            for (int i = count; i < DirectionalLight.MaxCascades; i++)
            {
                SetComponent(ref light.CascadeEnds, i, float.MaxValue);
                SetComponent(ref light.CascadeFarClip, i, 1);
                SetComponent(ref light.CascadeTexelSize, i, 1);
            }
            light.ActiveCascades = count;
            light.LightView = light.CascadeView[count - 1];
            light.LightViewProjection = light.CascadeViewProjection[count - 1];

            //Something moved inside one of the cascades?
            if (!moved)
            {
                for (int i = 0; i < count && !moved; i++)
                {
                    _boundingFrustumShadow ??= new BoundingFrustum(light.CascadeViewProjection[i]);
                    _boundingFrustumShadow.Matrix = light.CascadeViewProjection[i];
                    moved = meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, false, light.Position);
                }
            }
            if (!moved) return false;

            graphicsDevice.SetRenderTarget(light.ShadowMap);
            //1 = the far end of every cascade: texels nothing covers are lit
            graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1, 0);

            for (int i = 0; i < count; i++)
            {
                _boundingFrustumShadow ??= new BoundingFrustum(light.CascadeViewProjection[i]);
                _boundingFrustumShadow.Matrix = light.CascadeViewProjection[i];
                meshMaterialLibrary.FrustumCulling(entities, _boundingFrustumShadow, true, light.Position);

                graphicsDevice.Viewport = new Viewport((i % 2) * resolution, (i / 2) * resolution, resolution, resolution);
                _FarClip.SetValue(GetComponent(light.CascadeFarClip, i));
                _SizeBias.SetValue(GameSettings.ShadowBias * 2048 / resolution);

                meshMaterialLibrary.SkipShadowClear = true;
                try
                {
                    meshMaterialLibrary.Draw(MeshMaterialLibrary.RenderType.ShadowLinear,
                        light.CascadeViewProjection[i], true, true, false, 0, light.CascadeView[i], renderModule: this);
                }
                finally
                {
                    meshMaterialLibrary.SkipShadowClear = false;
                }
            }
            return true;
        }

        private static void SetComponent(ref Vector4 v, int i, float value)
        {
            switch (i)
            {
                case 0: v.X = value; break;
                case 1: v.Y = value; break;
                case 2: v.Z = value; break;
                default: v.W = value; break;
            }
        }

        private static float GetComponent(Vector4 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : i == 2 ? v.Z : v.W;

        public void Apply(Matrix localWorldMatrix, Matrix? view, Matrix viewProjection)
        {
            _WorldViewProj.SetValue(localWorldMatrix * viewProjection);
            // Swaying (Foliage Wind) meshes are moved in world space, then projected.
            _World.SetValue(localWorldMatrix);
            _ViewProjection.SetValue(viewProjection);

            switch (_pass)
            {
                case Passes.Directional:
                    _WorldView.SetValue(localWorldMatrix * (Matrix)view);
                    _View.SetValue((Matrix)view);
                    _linearPass.Apply();
                    break;
                case Passes.DirectionalAlpha:
                    _WorldView.SetValue(localWorldMatrix * (Matrix)view);
                    _View.SetValue((Matrix)view);
                    _linearAlphaPass.Apply();
                    break;
                case Passes.Omnidirectional:
                    _World.SetValue(localWorldMatrix);
                    _distancePass.Apply();
                    break;
                case Passes.OmnidirectionalAlpha:
                    _World.SetValue(localWorldMatrix);
                    _distanceAlphaPass.Apply();
                    break;
            }
        }

        public void SetMaterialSettings(MaterialEffect material, MeshMaterialLibrary.RenderType renderType)
        {
            if (renderType == MeshMaterialLibrary.RenderType.ShadowOmnidirectional)
            {
                //Check if we have a mask texture
                if (material.HasMask)
                {
                    _pass = Passes.OmnidirectionalAlpha;
                    _MaskTexture.SetValue(material.Mask);

                }
                else
                {
                    _pass = Passes.Omnidirectional;
                }

            }
            else if (renderType == MeshMaterialLibrary.RenderType.ShadowLinear)
            {
                // Leaves and other masked materials cast cut-out shadows from the sun too.
                if (material.HasMask)
                {
                    _pass = Passes.DirectionalAlpha;
                    _MaskTexture.SetValue(material.Mask);
                }
                else
                {
                    _pass = Passes.Directional;
                }
            }
        }
    }
}
