using System;
using System.Collections.Generic;
using Engine.Entities;
using Engine.Logic;
using Engine.Renderer.Helper;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules
{
    //Just a template
    public class DeferredEnvironmentMapRenderModule : IDisposable
    {
        private Effect _deferredEnvironmentShader;
        private EffectParameter _paramAlbedoMap;
        private EffectParameter _paramNormalMap;
        private EffectParameter _paramSSRMap;
        private EffectParameter _paramDepthMap;
        private EffectParameter _paramFrustumCorners;
        private EffectParameter _paramCameraPositionWS;
        private EffectParameter _paramReflectionCubeMap;
        private EffectParameter _paramSkyCubeMap;
        private EffectParameter _paramSkyMap2D;
        private EffectParameter _paramUseSkyMap2D;
        private TextureCube _skyCubeMapTexture;
        private Texture2D _skyMapTexture;
        private EffectParameter _paramResolution;
        private EffectParameter _paramFireflyReduction;
        private EffectParameter _paramFireflyThreshold;
        private EffectParameter _paramTransposeView;
        private EffectParameter _paramSpecularStrength;
        private EffectParameter _paramSpecularStrengthRcp;
        private EffectParameter _paramDiffuseStrength;
        private EffectParameter _paramTime;

        public EffectParameter ParamVolumeTexParam;
        public EffectParameter ParamVolumeTexSizeParam;
        public EffectParameter ParamVolumeTexResolution;
        public EffectParameter ParamInstanceInverseMatrix;
        public EffectParameter ParamInstanceScale;
        public EffectParameter ParamInstanceSDFIndex;
        public EffectParameter ParamInstancesCount;

        public EffectParameter ParamUseSDFAO;

        //Baked probe volume (see LightingSystem / ProbeVolumeData)
        private EffectParameter _paramUseProbeVolume;
        private EffectParameter _paramProbeVolumeIntensity;
        private EffectParameter _paramProbeVolumeMin;
        private EffectParameter _paramProbeVolumeExtentRcp;
        private EffectParameter _paramProbeVolumeCells;
        private EffectParameter _paramProbeSHR;
        private EffectParameter _paramProbeSHG;
        private EffectParameter _paramProbeSHB;
        private EffectParameter _paramProbeDistPos;
        private EffectParameter _paramProbeDistNeg;
        private EffectParameter _paramCaptureSHR;
        private EffectParameter _paramCaptureSHG;
        private EffectParameter _paramCaptureSHB;


        private EffectPass _passBasic;
        private EffectPass _passSky;
        private bool _fireflyReduction;
        private float _fireflyThreshold;
        private float _specularStrength;
        private float _diffuseStrength;
        private bool _useSDFAO;

        public DeferredEnvironmentMapRenderModule(ContentManager content, string shaderPath)
        {
            Load(content, shaderPath);
            Initialize();
        }

        /// <summary>The scene's GGX-prefiltered global probe (Lighting.EnvironmentPrefilter): mip m = roughness m / (mips - 1).</summary>
        public void SetGlobalProbe(TextureCube prefiltered)
        {
            _paramReflectionCubeMap.SetValue(prefiltered);
            if (prefiltered != null) _deferredEnvironmentShader.Parameters["GlobalMaxMip"]?.SetValue((float)Math.Max(prefiltered.LevelCount - 1, 0));
        }

        /// <summary>The split-sum BRDF lookup texture (Lighting.EnvironmentPrefilter.BrdfLut).</summary>
        public Texture2D BrdfLut
        {
            set
            {
                _deferredEnvironmentShader.Parameters["BrdfLut"]?.SetValue(value);
                _deferredEnvironmentShader.Parameters["UseBrdfLut"]?.SetValue(value != null);
            }
        }


        /// <summary>
        /// The local Reflection Probes this view blends (highest priority first). With <paramref name="bake"/> each
        /// is normalised by the baked irradiance at its capture point (Source 2 look, phase 3.3).
        /// </summary>
        public void SetLocalProbes(IReadOnlyList<ReflectionProbes.Probe> probes, ProbeVolumeData bake) =>
            ImageBasedLightingParameters.ApplyLocalProbes(_deferredEnvironmentShader.Parameters, probes, bake);

        /// <summary>The AO Proxy shapes of this frame (Common/AoProxies.fx).</summary>
        public void SetAoProxies(Components.AoProxyShapes proxies)
        {
            var p = _deferredEnvironmentShader.Parameters;
            p["AoProxyCount"]?.SetValue(proxies?.Count ?? 0);
            if (proxies == null || proxies.Count == 0) return;
            p["AoProxyWorldToUnit"]?.SetValue(proxies.WorldToUnit);
            p["AoProxyA"]?.SetValue(proxies.A);
            p["AoProxyB"]?.SetValue(proxies.B);
            p["AoProxySettings"]?.SetValue(proxies.Settings);
        }

        /// <summary>The capture pass of a cubemap face: only the baked diffuse light (probe volume or sky), added to the lights.</summary>
        public void DrawCapture(GraphicsDevice graphicsDevice, Vector3 origin, Matrix view, FullScreenTriangle fullScreenTriangle)
        {
            var p = _deferredEnvironmentShader.Parameters;
            p["CaptureMode"]?.SetValue(true);
            CameraPositionWS = origin;
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            _paramTransposeView.SetValue(Matrix.Transpose(view));
            _passBasic.Apply();
            fullScreenTriangle.Draw(graphicsDevice);
            p["CaptureMode"]?.SetValue(false);
        }

        /// <summary>Bilateral-upsampled SSAO; it darkens the indirect light only (Source 2 look, phase 1.2).</summary>
        public Texture2D SSAOMap
        {
            set { _deferredEnvironmentShader.Parameters["SSAOMap"]?.SetValue(value); }
        }

        private bool _useSSAO = true;
        public bool UseSSAO
        {
            get { return _useSSAO; }
            set
            {
                _useSSAO = value;
                _deferredEnvironmentShader.Parameters["UseSSAO"]?.SetValue(value);
            }
        }

        public TextureCube SkyCubemap
        {
            set
            {
                _skyCubeMapTexture = value;
                if (_paramSkyCubeMap != null) _paramSkyCubeMap.SetValue(value);
                if (_paramUseSkyMap2D != null) _paramUseSkyMap2D.SetValue(false);
            }
        }

        /// <summary>The background sky's cube map (planar reflections draw it too); null under the day/night cycle's procedural sky.</summary>
        public TextureCube BackgroundSkyCube => _dayNightCycle || _skyMapTexture != null ? null : _skyCubeMapTexture;
        /// <summary>The background sky's equirectangular map; null without one or under the day/night cycle.</summary>
        public Texture2D BackgroundSkyMap => _dayNightCycle ? null : _skyMapTexture;
        private bool _dayNightCycle;

        public Texture2D SkyTexture
        {
            set
            {
                _skyMapTexture = value;
                if (_paramSkyMap2D != null) _paramSkyMap2D.SetValue(value);
                if (_paramUseSkyMap2D != null) _paramUseSkyMap2D.SetValue(value != null);
            }
        }

        public void SetDayNightCycle(bool enabled, Vector3 sunDirection, float daylight)
        {
            _dayNightCycle = enabled;
            _deferredEnvironmentShader.Parameters["DayNightCycle"]?.SetValue(enabled);
            _deferredEnvironmentShader.Parameters["SunDirection"]?.SetValue(sunDirection);
            _deferredEnvironmentShader.Parameters["Daylight"]?.SetValue(daylight);
        }

        public void SetSkyLook(EnvironmentSettings settings)
        {
            var p = _deferredEnvironmentShader.Parameters;
            p["DaySkyZenith"]?.SetValue(ToLinear(settings.DaySkyColor));
            p["DaySkyHorizon"]?.SetValue(ToLinear(settings.DayHorizonColor));
            p["SunsetColor"]?.SetValue(ToLinear(settings.SunsetColor));
            p["NightSkyZenith"]?.SetValue(ToLinear(settings.NightSkyColor));
            p["NightSkyHorizon"]?.SetValue(ToLinear(settings.NightHorizonColor));
            // Disc edges as cosines of the default angular radii scaled by the size setting
            p["SunDisc"]?.SetValue(DiscEdges(0.0316f, 0.0200f, settings.SunSize));
            p["MoonDisc"]?.SetValue(DiscEdges(0.0283f, 0.0200f, settings.MoonSize));
            p["SunBrightness"]?.SetValue(settings.SunBrightness);
            p["MoonBrightness"]?.SetValue(settings.MoonBrightness);
            p["StarBrightness"]?.SetValue(settings.StarBrightness);
        }

        /// <summary>The image skybox's sun: direction towards it, the light's colour, strength (0 = off) and size.</summary>
        public void SetSkyboxSun(Vector3 towardsSun, Color color, float strength, float size)
        {
            var p = _deferredEnvironmentShader.Parameters;
            if (towardsSun.LengthSquared() < 1e-8f) strength = 0;
            else towardsSun.Normalize();
            p["SkySunDirection"]?.SetValue(towardsSun);
            p["SkySunColor"]?.SetValue(ToLinear(color));
            p["SkySunStrength"]?.SetValue(strength);
            // The disc, then the cone the image's own bright pixels are boosted in (a few degrees: the light is rarely exact)
            p["SkySunDisc"]?.SetValue(DiscEdges(0.0316f, 0.0200f, size));
            p["SkySunCone"]?.SetValue(DiscEdges(0.12f, 0.05f, size));
        }

        private static Vector3 ToLinear(Color c) =>
            new Vector3(MathF.Pow(c.R / 255f, 2.2f), MathF.Pow(c.G / 255f, 2.2f), MathF.Pow(c.B / 255f, 2.2f));

        private static Vector2 DiscEdges(float outer, float inner, float size) =>
            new Vector2(MathF.Cos(outer * size), MathF.Cos(inner * size));

        public void SetClouds(float coverage, Vector2 offset)
        {
            _deferredEnvironmentShader.Parameters["CloudCoverage"]?.SetValue(coverage);
            _deferredEnvironmentShader.Parameters["CloudOffset"]?.SetValue(offset);
        }

        public Texture2D AlbedoMap
        {
            set { _paramAlbedoMap.SetValue(value); }
        }
        public Texture2D DepthMap
        {
            set { _paramDepthMap.SetValue(value); }
        }

        public Texture2D NormalMap
        {
            set { _paramNormalMap.SetValue(value); }
        }
        public Texture2D SSRMap
        {
            set { _paramSSRMap.SetValue(value); }
        }

        public Vector3[] FrustumCornersWS
        {
            set { _paramFrustumCorners.SetValue(value); }
        }

        public Vector3 CameraPositionWS
        {
            set { _paramCameraPositionWS.SetValue(value); }
        }

        public Vector2 Resolution
        {
            set { _paramResolution.SetValue(value); }
        }

        public float Time
        {
            set { _paramTime.SetValue(value); }
        }

        public bool FireflyReduction
        {
            get { return _fireflyReduction; }
            set
            {
                if (value != _fireflyReduction)
                {
                    _fireflyReduction = value;
                    _paramFireflyReduction.SetValue(value);
                }
            }
        }

        public float FireflyThreshold
        {
            get { return _fireflyThreshold; }
            set {
                if (Math.Abs(value - _fireflyThreshold) > 0.0001f)
                {
                    _fireflyThreshold = value;
                    _paramFireflyThreshold.SetValue(value);
                }
            }
        }

        public float SpecularStrength
        {
            get { return _specularStrength; }
            set {
                if (Math.Abs(value - _specularStrength) > 0.0001f)
                {
                    _specularStrength = value;
                    _paramSpecularStrength.SetValue(value);
                    _paramSpecularStrengthRcp.SetValue(1.0f / value);
                }
            }
        }

        public float DiffuseStrength
        {
            get { return _diffuseStrength; }
            set
            {
                if (Math.Abs(value - _diffuseStrength) > 0.0001f)
                {
                    _diffuseStrength = value;
                    _paramDiffuseStrength.SetValue(value);
                }
            }
        }

        public bool UseSDFAO
        {
            get { return _useSDFAO; }
            set
            {
                if (_useSDFAO != value)
                {
                    _useSDFAO = value;
                    ParamUseSDFAO.SetValue(value);
                }
            }
        }

        public void Initialize()
        {
            //Environment
            _paramAlbedoMap = _deferredEnvironmentShader.Parameters["AlbedoMap"];
            _paramNormalMap = _deferredEnvironmentShader.Parameters["NormalMap"];
            _paramDepthMap = _deferredEnvironmentShader.Parameters["DepthMap"];
            _paramFrustumCorners = _deferredEnvironmentShader.Parameters["FrustumCorners"];
            _paramSSRMap = _deferredEnvironmentShader.Parameters["ReflectionMap"];
            _paramReflectionCubeMap = _deferredEnvironmentShader.Parameters["ReflectionCubeMap"];
            _paramSkyCubeMap = _deferredEnvironmentShader.Parameters["SkyCubeMap"];
            _paramSkyMap2D = _deferredEnvironmentShader.Parameters["SkyMap2D"];
            _paramUseSkyMap2D = _deferredEnvironmentShader.Parameters["UseSkyMap2D"];
            _paramResolution = _deferredEnvironmentShader.Parameters["Resolution"];
            _paramFireflyReduction = _deferredEnvironmentShader.Parameters["FireflyReduction"];
            _paramFireflyThreshold = _deferredEnvironmentShader.Parameters["FireflyThreshold"];
            _paramTransposeView = _deferredEnvironmentShader.Parameters["TransposeView"];
            _paramSpecularStrength = _deferredEnvironmentShader.Parameters["EnvironmentMapSpecularStrength"];
            _paramSpecularStrengthRcp = _deferredEnvironmentShader.Parameters["EnvironmentMapSpecularStrengthRcp"];
            _paramDiffuseStrength = _deferredEnvironmentShader.Parameters["EnvironmentMapDiffuseStrength"];
            _paramCameraPositionWS = _deferredEnvironmentShader.Parameters["CameraPositionWS"];
            _paramTime = _deferredEnvironmentShader.Parameters["Time"];
            
            //SDF
            ParamVolumeTexParam = _deferredEnvironmentShader.Parameters["VolumeTex"];
            ParamVolumeTexSizeParam = _deferredEnvironmentShader.Parameters["VolumeTexSize"];
            ParamVolumeTexResolution = _deferredEnvironmentShader.Parameters["VolumeTexResolution"];
            ParamInstanceInverseMatrix = _deferredEnvironmentShader.Parameters["InstanceInverseMatrix"];
            ParamInstanceScale = _deferredEnvironmentShader.Parameters["InstanceScale"];
            ParamInstanceSDFIndex = _deferredEnvironmentShader.Parameters["InstanceSDFIndex"];
            ParamInstancesCount = _deferredEnvironmentShader.Parameters["InstancesCount"];

            ParamUseSDFAO = _deferredEnvironmentShader.Parameters["UseSDFAO"];

            _paramUseProbeVolume = _deferredEnvironmentShader.Parameters["UseProbeVolume"];
            _paramProbeVolumeIntensity = _deferredEnvironmentShader.Parameters["ProbeVolumeIntensity"];
            _paramProbeVolumeMin = _deferredEnvironmentShader.Parameters["ProbeVolumeMin"];
            _paramProbeVolumeExtentRcp = _deferredEnvironmentShader.Parameters["ProbeVolumeExtentRcp"];
            _paramProbeVolumeCells = _deferredEnvironmentShader.Parameters["ProbeVolumeCells"];
            _paramProbeSHR = _deferredEnvironmentShader.Parameters["ProbeData"];
            _paramProbeSHG = _deferredEnvironmentShader.Parameters["ProbeSHG"];
            _paramProbeSHB = _deferredEnvironmentShader.Parameters["ProbeSHB"];
            _paramProbeDistPos = _deferredEnvironmentShader.Parameters["ProbeDistPos"];
            _paramProbeDistNeg = _deferredEnvironmentShader.Parameters["ProbeDistNeg"];
            _paramCaptureSHR = _deferredEnvironmentShader.Parameters["CaptureSHR"];
            _paramCaptureSHG = _deferredEnvironmentShader.Parameters["CaptureSHG"];
            _paramCaptureSHB = _deferredEnvironmentShader.Parameters["CaptureSHB"];

            _passSky = _deferredEnvironmentShader.Techniques["Sky"].Passes[0];
            _passBasic = _deferredEnvironmentShader.Techniques["Basic"].Passes[0];

            if (_skyCubeMapTexture != null)
            {
                _paramSkyCubeMap.SetValue(_skyCubeMapTexture);
            }

            if (_skyMapTexture != null)
            {
                _paramSkyMap2D.SetValue(_skyMapTexture);
                _paramUseSkyMap2D.SetValue(true);
            }
            else
            {
                _paramUseSkyMap2D.SetValue(false);
            }
        }
        
        public void Load(ContentManager content, string shaderPath)
        {
            _deferredEnvironmentShader = content.Load<Effect>(shaderPath);

        }

        /// <summary>
        /// Bind the baked probe volume for the next DrawEnvironmentMap, or disable it (null).
        /// Parameters may be missing while an older compiled shader is loaded; that just disables it.
        /// <paramref name="capturePosition"/> is where the reflection cubemap was rendered: the shader
        /// darkens cubemap reflections where the probes see less light than they do there.
        /// </summary>
        public void SetProbeVolume(LightingSystem lighting, float intensity, Vector3 capturePosition) =>
            ImageBasedLightingParameters.ApplyProbeVolume(_deferredEnvironmentShader.Parameters, lighting, intensity, capturePosition);

        /// <summary>
        /// Lightmaps (Source 2 look, phase 4.2): the lightmap pass's screen target (indirect irradiance + baked AO), or null.
        /// Lightmapped pixels take their indirect diffuse and AO from it instead of the probes and SSAO.
        /// </summary>
        public void SetLightmaps(Texture2D lightmapTarget, float intensity)
        {
            _deferredEnvironmentShader.Parameters["UseLightmaps"]?.SetValue(lightmapTarget != null);
            _deferredEnvironmentShader.Parameters["LightmapIntensity"]?.SetValue(intensity);
            if (lightmapTarget != null) _deferredEnvironmentShader.Parameters["LightmapMap"]?.SetValue(lightmapTarget);
        }

        public void DrawEnvironmentMap(GraphicsDevice graphicsDevice, Camera camera, Matrix view, FullScreenTriangle fullScreenTriangle, EnvironmentSample envSample, GameTime gameTime, bool fireflyReduction, float ffThreshold)
        {
            FireflyReduction = fireflyReduction;
            FireflyThreshold = ffThreshold;
            
            SpecularStrength = envSample.SpecularStrength;
            DiffuseStrength = envSample.DiffuseStrength;
            CameraPositionWS = camera.Position;

            Time = (float)gameTime.TotalGameTime.TotalSeconds % 1000;
            
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            UseSDFAO = envSample.UseSDFAO;
            _paramTransposeView.SetValue(Matrix.Transpose(view));
            _passBasic.Apply();
            fullScreenTriangle.Draw(graphicsDevice);
        }

        public void DrawSky(GraphicsDevice graphicsDevice, FullScreenTriangle quadRenderer)
        {
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            _passSky.Apply();
            quadRenderer.Draw(graphicsDevice);

        }

        public void Dispose()
        {
            _deferredEnvironmentShader?.Dispose();
        }
    }
}
