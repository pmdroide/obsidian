using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.Lighting;
using Engine.Renderer.RenderModules.Default;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules
{
    /// <summary>
    /// Forward shaded materials (glass, transparent plastics: MaterialTypes.ForwardShaded), lit like the deferred path
    /// (Source 2 look, Docs/Research/Source2Look phase 3.4): their own colour/texture, roughness and metalness, the sun
    /// with cascaded shadows, the <see cref="MaxLights"/> most important point and spot lights with atlas shadows and cookies, the probe volume and
    /// reflection probes, and the froxel fog. Drawn premultiplied over the composed image.
    /// </summary>
    public class ForwardRenderModule : IRenderModule, IDisposable
    {
        public const int MaxLights = 40;

        /// <summary>What the forward pass reads from the rest of the frame.</summary>
        public struct Context
        {
            public Camera Camera;
            public BoundingFrustum Frustum;
            public List<PointLight> PointLights;
            public List<DirectionalLight> DirectionalLights;
            public Texture2D ShadowAtlas;
            public TextureCube GlobalProbe;
            public Texture2D BrdfLut;
            public IReadOnlyList<ReflectionProbes.Probe> LocalProbes;
            public LightingSystem Lighting;
            public float LightingIntensity;
            public Vector3 GlobalCapturePosition;
            public float AmbientStrength;
            /// <summary>Sets the froxel fog parameters (Common/FroxelFog.fx) on the effect, or disables the fog.</summary>
            public Action<EffectParameterCollection> ApplyFog;
            public Vector2 Resolution;
            /// <summary>The background sky (cube, or equirectangular when <see cref="SkyMap"/> is set) for mirror images; none = keep the probes.</summary>
            public TextureCube SkyCube;
            public Texture2D SkyMap;
        }

        private Effect _shader;
        private EffectPass _pass;
        private EffectPass _skyPass;

        private EffectParameter _worldParam;
        private EffectParameter _worldViewProjParam;
        private EffectParameter _worldViewITParam;
        private EffectParameter _usePlanarParam;
        private EffectParameter _planarMapParam;
        private EffectParameter _planarSizeParam;
        private PlanarReflectionView _boundPlanar;

        private readonly Vector3[] _lightPositionWS = new Vector3[MaxLights];
        private readonly float[] _lightRadius = new float[MaxLights];
        private readonly float[] _lightIntensity = new float[MaxLights];
        private readonly Vector3[] _lightColor = new Vector3[MaxLights];
        private readonly Vector3[] _lightSpotDirectionWS = new Vector3[MaxLights];
        private readonly Vector2[] _lightSpotCos = new Vector2[MaxLights];
        private readonly float[] _lightSourceRadius = new float[MaxLights];
        private readonly Vector4[] _lightShadowSettings = new Vector4[MaxLights];
        private readonly Vector4[] _lightShadowTiles = new Vector4[MaxLights * 6];
        private readonly Vector4[] _lightCookieRect = new Vector4[MaxLights];
        private readonly Vector4[] _lightCookieRight = new Vector4[MaxLights];
        private readonly Vector3[] _lightCookieUp = new Vector3[MaxLights];
        private readonly ForwardCookieAtlas _cookieAtlas = new();
        private readonly List<Texture2D> _cookies = new();
        private readonly List<(PointLight Light, float Score)> _ranked = new List<(PointLight, float)>();

        public ForwardRenderModule(ContentManager content, string shaderPath)
        {
            Load(content, shaderPath);
            Initialize();
        }

        public void Initialize()
        {
            _worldParam = _shader.Parameters["World"];
            _worldViewProjParam = _shader.Parameters["WorldViewProj"];
            _worldViewITParam = _shader.Parameters["WorldViewIT"];
            _usePlanarParam = _shader.Parameters["UsePlanarReflection"];
            _planarMapParam = _shader.Parameters["PlanarReflectionMap"];
            _planarSizeParam = _shader.Parameters["PlanarReflectionSize"];
            _pass = _shader.Techniques["Default"].Passes[0];
            _skyPass = _shader.Techniques["PlanarSky"].Passes[0];
        }

        public void Load(ContentManager content, string shaderPath)
        {
            _shader = content.Load<Effect>(shaderPath);
        }

        /// <summary>Draws the forward shaded materials into <paramref name="output"/> (its depth must hold the opaque scene).</summary>
        public RenderTarget2D Draw(GraphicsDevice graphicsDevice, RenderTarget2D output, MeshMaterialLibrary meshMat, Matrix viewProjection, Context context)
        {
            graphicsDevice.DepthStencilState = DepthStencilState.Default;

            EffectParameterCollection p = _shader.Parameters;
            SetupFrame(context, context.Camera.Position, context.Camera.Forward);
            if (context.ApplyFog != null) context.ApplyFog(p);
            else p["UseFog"]?.SetValue(false);
            p["ReflectionPass"]?.SetValue(false);
            UnbindPlanarReflection();

            meshMat.Draw(MeshMaterialLibrary.RenderType.Forward, viewProjection, renderModule: this);
            UnbindPlanarReflection();
            return output;
        }

        /// <summary>
        /// Draws the opaque scene lit like the forward pass, seen from <paramref name="eye"/> (the camera mirrored about
        /// <paramref name="clipPlane"/>), into the bound target; pixels behind the plane are clipped. The mesh library's
        /// <see cref="MeshMaterialLibrary.ReflectionCull"/> must be set for this view.
        /// </summary>
        public void DrawReflection(MeshMaterialLibrary meshMat, Matrix viewProjection, Context context, Vector3 eye, Vector3 forward, Plane clipPlane)
        {
            EffectParameterCollection p = _shader.Parameters;
            SetupFrame(context, eye, forward);
            p["UseFog"]?.SetValue(false);
            p["ReflectionPass"]?.SetValue(true);
            p["ClipPlane"]?.SetValue(new Vector4(clipPlane.Normal, clipPlane.D));
            //The image being drawn must not stay bound as a texture: D3D drops one of the two bindings
            UnbindPlanarReflection();

            meshMat.Draw(MeshMaterialLibrary.RenderType.Reflection, viewProjection, renderModule: this);
            p["ReflectionPass"]?.SetValue(false);
        }

        /// <summary>
        /// Fills the pixels of the bound mirror image nothing was drawn on (depth still 1) with the background sky seen
        /// along the mirrored rays, at the brightness the main view shows it. False without a sky texture.
        /// </summary>
        public bool DrawReflectionSky(GraphicsDevice graphics, FullScreenTriangle triangle, Matrix viewProjection, Vector3 eye, Context context)
        {
            if (context.SkyCube == null && context.SkyMap == null) return false;
            EffectParameterCollection p = _shader.Parameters;
            p["InverseViewProjection"]?.SetValue(Matrix.Invert(viewProjection));
            p["CameraPositionWS"]?.SetValue(eye);
            p["UseSkyMap2D"]?.SetValue(context.SkyMap != null);
            if (context.SkyMap != null) p["SkyMap2D"]?.SetValue(context.SkyMap);
            else p["SkyCubeMap"]?.SetValue(context.SkyCube);

            graphics.BlendState = BlendState.Opaque;
            graphics.DepthStencilState = DepthStencilState.DepthRead;
            graphics.RasterizerState = RasterizerState.CullNone;
            _skyPass.Apply();
            triangle.Draw(graphics);
            return true;
        }

        /// <summary>The camera, lights and image based light shared by the forward and the reflection passes.</summary>
        private void SetupFrame(Context context, Vector3 eye, Vector3 forward)
        {
            EffectParameterCollection p = _shader.Parameters;
            p["CameraPositionWS"]?.SetValue(eye);
            p["CameraForwardWS"]?.SetValue(Vector3.Normalize(forward));
            p["Resolution"]?.SetValue(context.Resolution);
            p["AmbientStrength"]?.SetValue(context.AmbientStrength);

            SetupLights(context);
            SetupSun(context.DirectionalLights);
            ImageBasedLightingParameters.ApplyGlobal(p, context.GlobalProbe, context.BrdfLut);
            ImageBasedLightingParameters.ApplyProbeVolume(p, context.Lighting, context.LightingIntensity, context.GlobalCapturePosition);
            ImageBasedLightingParameters.ApplyLocalProbes(p, context.LocalProbes, context.Lighting?.GpuData != null && context.Lighting.ProbeData != null ? context.Lighting.GpuData : null);
        }

        private void UnbindPlanarReflection()
        {
            _boundPlanar = null;
            _usePlanarParam?.SetValue(false);
            _planarMapParam?.SetValue((Texture2D)null);
        }

        /// <summary>Per instance (MeshMaterialLibrary): the mirror image a Planar Reflection pane shows, or null for the probes.</summary>
        public void SetPlanarReflection(PlanarReflectionView view)
        {
            if (view != null && (!view.Ready || view.Map == null || view.Map.IsDisposed)) view = null;
            if (view == _boundPlanar) return;
            _boundPlanar = view;
            _usePlanarParam?.SetValue(view != null);
            if (view == null) return;
            _planarMapParam?.SetValue(view.Map);
            _planarSizeParam?.SetValue(new Vector3(view.Map.Width, view.Map.Height, view.Map.LevelCount));
        }

        /// <summary>Prepares cookie textures before scene rendering; rebuilding the atlas switches render targets.</summary>
        public void PrepareCookies(GraphicsDevice graphics, Camera camera, BoundingFrustum frustum, List<PointLight> lights)
        {
            RankLights(new Context { Camera = camera, Frustum = frustum, PointLights = lights });
            _cookies.Clear();
            for (int i = 0; i < Math.Min(_ranked.Count, MaxLights); i++)
                if (_ranked[i].Light is SpotLight { Cookie: { IsDisposed: false } cookie } && !_cookies.Contains(cookie))
                    _cookies.Add(cookie);
            _cookieAtlas.Update(graphics, _cookies);
        }

        /// <summary>Ranks the visible point/spot lights by brightness reaching the camera.</summary>
        private void RankLights(Context context)
        {
            _ranked.Clear();
            List<PointLight> lights = context.PointLights;
            Vector3 camera = context.Camera.Position;
            if (lights != null)
            {
                foreach (PointLight light in lights)
                {
                    if (!light.IsEnabled || light.Intensity <= 0 || light.Radius <= 0) continue;
                    if (context.Frustum != null && context.Frustum.Contains(light.BoundingSphere) == ContainmentType.Disjoint) continue;
                    Vector3 color = light.ColorV3 * light.Intensity;
                    float brightness = 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;
                    float reach = light.Radius / Math.Max(Vector3.Distance(camera, light.Position), light.Radius * 0.25f);
                    _ranked.Add((light, brightness * reach * reach));
                }
            }
            _ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        }

        private void SetupLights(Context context)
        {
            RankLights(context);
            int count = Math.Min(_ranked.Count, MaxLights);
            for (int i = 0; i < count; i++)
            {
                PointLight light = _ranked[i].Light;
                _lightPositionWS[i] = light.Position;
                _lightColor[i] = light.ColorV3;
                _lightIntensity[i] = light.Intensity;
                _lightRadius[i] = light.Radius;
                _lightSourceRadius[i] = light.SourceRadius;
                //Point lights: outer cosine -2 lights every direction
                SpotLight spot = light as SpotLight;
                _lightSpotDirectionWS[i] = spot?.Direction ?? Vector3.UnitZ;
                _lightSpotCos[i] = spot != null ? new Vector2(spot.CosOuter, spot.CosInner) : new Vector2(-2, -1);
                bool shadowed = context.ShadowAtlas != null && light.HasShadowThisFrame;
                _lightShadowSettings[i] = shadowed ? new Vector4(light.ShadowStrength, light.ShadowTileSize, light.ShadowMapRadius, 0.02f) : Vector4.Zero;
                for (int face = 0; face < 6; face++)
                    _lightShadowTiles[i * 6 + face] = shadowed ? light.ShadowTiles[face] : Vector4.Zero;
                _lightCookieRect[i] = _cookieAtlas.Rect(spot?.Cookie);
                _lightCookieRight[i] = spot != null ? new Vector4(spot.Right,
                    (float)Math.Tan(MathHelper.ToRadians(Math.Min(spot.SpotAngle, 170) * 0.5f))) : Vector4.Zero;
                _lightCookieUp[i] = spot?.Up ?? Vector3.Zero;
            }

            EffectParameterCollection p = _shader.Parameters;
            p["LightAmount"]?.SetValue(count);
            p["LightPositionWS"]?.SetValue(_lightPositionWS);
            p["LightColor"]?.SetValue(_lightColor);
            p["LightIntensity"]?.SetValue(_lightIntensity);
            p["LightRadius"]?.SetValue(_lightRadius);
            p["LightSpotDirectionWS"]?.SetValue(_lightSpotDirectionWS);
            p["LightSpotCos"]?.SetValue(_lightSpotCos);
            p["LightSourceRadius"]?.SetValue(_lightSourceRadius);
            p["LightShadowSettings"]?.SetValue(_lightShadowSettings);
            p["LightShadowTiles"]?.SetValue(_lightShadowTiles);
            p["PointShadowAtlas"]?.SetValue(context.ShadowAtlas);
            p["LightCookieRect"]?.SetValue(_lightCookieRect);
            p["LightCookieRight"]?.SetValue(_lightCookieRight);
            p["LightCookieUp"]?.SetValue(_lightCookieUp);
            p["CookieAtlas"]?.SetValue(_cookieAtlas.Texture);
        }

        /// <summary>The first enabled sun, with its cascades when it casts shadows.</summary>
        private void SetupSun(List<DirectionalLight> suns)
        {
            EffectParameterCollection p = _shader.Parameters;
            DirectionalLight sun = null;
            if (suns != null)
                foreach (DirectionalLight light in suns)
                    if (light.IsEnabled) { sun = light; break; }

            p["UseSun"]?.SetValue(sun != null);
            if (sun == null) return;
            p["SunDirection"]?.SetValue(Vector3.Normalize(sun.Direction));
            p["SunColor"]?.SetValue(sun.ColorV3 * sun.Intensity);
            bool shadowed = sun.CastShadows && sun.ShadowMap != null && sun.ActiveCascades > 0;
            p["SunCascadeCount"]?.SetValue(shadowed ? (float)sun.ActiveCascades : 0f);
            if (!shadowed) return;
            p["SunCascadeViewProjection"]?.SetValue(sun.CascadeViewProjection);
            p["SunCascadeView"]?.SetValue(sun.CascadeView);
            p["SunCascadeEnds"]?.SetValue(sun.CascadeEnds);
            p["SunCascadeFarClip"]?.SetValue(sun.CascadeFarClip);
            p["SunCascadeTexelSize"]?.SetValue(sun.CascadeTexelSize);
            p["SunShadowMapSize"]?.SetValue((float)sun.CascadeResolution);
            p["SunShadowMap"]?.SetValue(sun.ShadowMap);
        }

        /// <summary>The material's surface (MeshMaterialLibrary calls it per material).</summary>
        public void SetMaterialSettings(MaterialEffect material)
        {
            EffectParameterCollection p = _shader.Parameters;
            bool hasAlbedo = material.HasDiffuse && material.AlbedoMap != null;
            p["DiffuseColor"]?.SetValue(material.DiffuseColor);
            p["Roughness"]?.SetValue(material.Roughness);
            p["Metallic"]?.SetValue(material.Metallic);
            p["Opacity"]?.SetValue(MathHelper.Clamp(material.Opacity, 0, 1));
            p["HasAlbedo"]?.SetValue(hasAlbedo);
            if (hasAlbedo) p["AlbedoMap"]?.SetValue(material.AlbedoMap);
            //Cut-out materials (foliage) in mirror images
            p["HasMask"]?.SetValue(material.HasMask);
            if (material.HasMask) p["MaskMap"]?.SetValue(material.Mask);
            p["TextureTiling"]?.SetValue(material.TextureTiling);
        }

        public void Dispose()
        {
            _cookieAtlas.Dispose();
            _shader?.Dispose();
        }

        public void Apply(Matrix localWorldMatrix, Matrix? view, Matrix viewProjection)
        {
            _worldParam.SetValue(localWorldMatrix);
            _worldViewProjParam.SetValue(localWorldMatrix * viewProjection);
            _worldViewITParam.SetValue(Matrix.Transpose(Matrix.Invert(localWorldMatrix)));
            _pass.Apply();
        }
    }
}
