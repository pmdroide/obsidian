using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules.DeferredLighting
{
    /// <summary>
    /// Volumetric fog via frustum-aligned voxels packed in a 2D atlas
    /// (width = Nx, height = Ny * Nz). Slices are logarithmic in view-space Z between the camera's
    /// near plane (1 world unit, see <see cref="Renderer"/>) and far plane.
    /// Per frame: inject the medium (scattering source, extinction), resolve it against last frame's
    /// reprojected medium, then integrate it along Z with an exact segment integral and a prefix scan.
    /// DeferredCompose reads <see cref="FroxelAccumulation"/> (the prefixes) and <see cref="FroxelMedium"/>.
    /// </summary>
    public class FroxelRenderModule : IDisposable
    {
        /// <summary>Must match <see cref="Renderer"/> projection near plane.</summary>
        public const float CameraNearPlane = 1f;

        public const int FROXEL_GRID_X = 160;
        public const int FROXEL_GRID_Y = 90;
        public const int FROXEL_GRID_Z = 128;

        /// <summary>Maximum point lights injected per frame. Must match MAX_FROXEL_POINT_LIGHTS in Froxel.fx.</summary>
        public const int MAX_FROXEL_POINT_LIGHTS = 8;

        /// <summary>A camera that moves further than this (m) in one frame is a cut: the fog history is dropped.</summary>
        public const float CameraCutDistance = 10f;

        public static int AtlasWidth => FROXEL_GRID_X;
        public static int AtlasHeight => FROXEL_GRID_Y * FROXEL_GRID_Z;

        /// <summary>Prefix scan passes after the seed: log2 of the slice count.</summary>
        public static int ScanPassCount
        {
            get
            {
                int passes = 0;
                for (int stride = 1; stride < FROXEL_GRID_Z; stride *= 2) passes++;
                return passes;
            }
        }

        private GraphicsDevice _graphicsDevice;
        private ShaderManager _shaderManager;
        private Effect _froxelShader;
        private FullScreenTriangle _fullScreenTriangle;
        private int _shaderIndex = -1;

        //Raw injection; free after the resolve, so it doubles as one of the two scan targets
        private RenderTarget2D _injectionTarget;
        //Resolved medium of this and last frame (ping-pong)
        private readonly RenderTarget2D[] _historyTargets = new RenderTarget2D[2];
        private int _historyIndex;
        private RenderTarget2D _scanTarget;
        //Where the last scan pass wrote: the prefixes DeferredCompose reads
        private RenderTarget2D _integrated;
        private Texture2D _noiseMap;
        private Texture2D _dummyShadowMap;

        private EffectParameter _paramInverseProjection;
        private EffectParameter _paramInverseView;
        private EffectParameter _paramPreviousView;
        private EffectParameter _paramPreviousViewProjection;
        private EffectParameter _paramHistoryValid;
        private EffectParameter _paramNearClip;
        private EffectParameter _paramFarClip;
        private EffectParameter _paramGridDimensions;
        private EffectParameter _paramScreenResolution;
        private EffectParameter _paramDirectionalLightDirectionVS;
        private EffectParameter _paramDirectionalLightColor;
        private EffectParameter _paramUseDirectionalLight;
        private EffectParameter _paramShadowMap;
        private EffectParameter _paramInjectionTexture;
        private EffectParameter _paramPreviousHistoryTexture;
        private EffectParameter _paramScanInputTexture;
        private EffectParameter _paramScanStride;
        private EffectParameter _paramNoiseMap;
        private EffectParameter _paramTime;
        private EffectParameter _paramFroxelDensity;
        private EffectParameter _paramFroxelAbsorption;
        private EffectParameter _paramDirectionalScatter;
        private EffectParameter _paramPointScatter;
        private EffectParameter _paramIndirectScatter;
        private EffectParameter _paramFogDistanceStart;
        private EffectParameter _paramFogDistanceFull;
        private EffectParameter _paramFogNearDensity;
        private EffectParameter _paramFogVolumeCount;
        private EffectParameter _paramFogVolumeWorldToBox;
        private EffectParameter _paramFogVolumeSettings;
        private EffectParameter _paramHistoryAlpha;
        private EffectParameter _paramAnisotropy;
        private EffectParameter _paramPointLightCount;
        private EffectParameter _paramPointLightPositionsVS;
        private EffectParameter _paramPointLightPositionsWS;
        private EffectParameter _paramPointLightColors;
        private EffectParameter _paramPointLightRadii;
        private EffectParameter _paramPointLightCastShadows;
        private EffectParameter _paramPointLightShadowMapSize;
        private EffectParameter _paramPointLightSpotDirectionsVS;
        private EffectParameter _paramPointLightSpotCos;
        private readonly Vector4[] _pointLightShadowTiles = new Vector4[MAX_FROXEL_POINT_LIGHTS * 6];
        private int _fogCookieLight = -1;

        /// <summary>The point/spot light shadow atlas (ShadowMapRenderModule.Atlas), set by the Renderer each frame.</summary>
        public Texture2D ShadowAtlas;
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

        private readonly Vector3[] _pointLightPositionsVS = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly Vector3[] _pointLightPositionsWS = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly Vector3[] _pointLightColors = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly float[] _pointLightRadii = new float[MAX_FROXEL_POINT_LIGHTS];
        private readonly float[] _pointLightCastShadows = new float[MAX_FROXEL_POINT_LIGHTS];
        private readonly float[] _pointLightShadowMapSizes = new float[MAX_FROXEL_POINT_LIGHTS];
        private readonly Vector3[] _pointLightSpotDirectionsVS = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly Vector2[] _pointLightSpotCos = new Vector2[MAX_FROXEL_POINT_LIGHTS];
        private readonly List<PointLight> _rankedLights = new List<PointLight>();
        private readonly List<LightRank> _lightRanks = new List<LightRank>();

        private readonly Matrix[] _fogVolumeWorldToBox = new Matrix[Components.FogVolumeComponent.MaxVolumes];
        private readonly Vector4[] _fogVolumeSettings = new Vector4[Components.FogVolumeComponent.MaxVolumes];
        private int _fogVolumeCount;

        private EffectTechnique _techniqueBuildFroxels;
        private EffectTechnique _techniqueResolveFroxels;
        private EffectTechnique _techniqueSeedTransport;
        private EffectTechnique _techniqueScanTransport;

        private Matrix _view;
        private Matrix _projection;
        private Matrix _inverseView;
        private Matrix _inverseProjection;
        private float _nearClip = CameraNearPlane;
        private float _farClip = 1000f;
        private readonly BoundingFrustum _frustum = new BoundingFrustum(Matrix.Identity);

        //Last rendered frame, for reprojection and invalidation. Only updated after a frame rendered.
        private bool _historyValid;
        private Matrix _previousView;
        private Matrix _previousProjection;
        private Vector3 _previousCameraPosition;
        private int _previousSettingsHash;
        private ProbeVolumeData _probeData;

        private bool _isInitialized;

        /// <summary>The integrated fog: texel (x, y, z) = light scattered towards the camera (rgb) and optical depth (a,
        /// transmittance = exp(-a)) from the camera to the far end of slice z.</summary>
        public Texture2D FroxelAccumulation => _integrated;

        /// <summary>This frame's resolved medium: scattering source per metre (rgb), extinction per metre (a).</summary>
        public Texture2D FroxelMedium => _historyTargets[_historyIndex];

        /// <summary>True when the next frame blends in this frame's fog (no cut, reset or settings change since).</summary>
        public bool HasHistory => _historyValid;

        /// <summary>Whether the last rendered frame blended in the previous frame's (reprojected) fog.</summary>
        public bool UsedHistory { get; private set; }

        public FroxelRenderModule(ShaderManager shaderManager, string shaderPath)
        {
            Load(shaderManager, shaderPath);
        }

        private void Load(ShaderManager shaderManager, string shaderPath)
        {
            _shaderManager = shaderManager;
            _shaderIndex = shaderManager.AddShader(shaderPath);
            if (_shaderIndex >= 0)
            {
                _froxelShader = shaderManager.GetShader(_shaderIndex);
                InitializeShaderParameters();
            }
        }

        private EffectParameter GetEffectParameter(string name)
        {
            if (_froxelShader == null)
                return null;

            var parameter = _froxelShader.Parameters[name];
            if (parameter == null)
                Debug.WriteLine($"Froxel shader parameter not found: {name}");

            return parameter;
        }

        private EffectTechnique GetEffectTechnique(string name)
        {
            if (_froxelShader == null)
                return null;

            var technique = _froxelShader.Techniques[name];
            if (technique == null)
                Debug.WriteLine($"Froxel shader technique not found: {name}");

            return technique;
        }

        private void InitializeShaderParameters()
        {
            if (_froxelShader == null)
            {
                Debug.WriteLine("ERROR: Froxel shader is null.");
                return;
            }

            _paramInverseProjection = GetEffectParameter("InverseProjection");
            _paramInverseView = GetEffectParameter("InverseView");
            _paramPreviousView = GetEffectParameter("PreviousView");
            _paramPreviousViewProjection = GetEffectParameter("PreviousViewProjection");
            _paramHistoryValid = GetEffectParameter("HistoryValid");
            _paramNearClip = GetEffectParameter("NearClip");
            _paramFarClip = GetEffectParameter("FarClip");
            _paramGridDimensions = GetEffectParameter("GridDimensions");
            _paramScreenResolution = GetEffectParameter("ScreenResolution");
            _paramDirectionalLightDirectionVS = GetEffectParameter("DirectionalLightDirectionVS");
            _paramDirectionalLightColor = GetEffectParameter("DirectionalLightColor");
            _paramUseDirectionalLight = GetEffectParameter("UseDirectionalLight");
            _paramShadowMap = GetEffectParameter("ShadowMap");
            _paramInjectionTexture = GetEffectParameter("FroxelInjectionTexture");
            _paramPreviousHistoryTexture = GetEffectParameter("PreviousFroxelHistoryTexture");
            _paramScanInputTexture = GetEffectParameter("ScanInputTexture");
            _paramScanStride = GetEffectParameter("ScanStride");
            _paramNoiseMap = GetEffectParameter("NoiseMap");
            _paramTime = GetEffectParameter("Time");
            _paramFroxelDensity = GetEffectParameter("FroxelDensity");
            _paramFroxelAbsorption = GetEffectParameter("FroxelAbsorption");
            _paramDirectionalScatter = GetEffectParameter("DirectionalScatter");
            _paramPointScatter = GetEffectParameter("PointScatter");
            _paramIndirectScatter = GetEffectParameter("IndirectScatter");
            _paramFogDistanceStart = GetEffectParameter("FogDistanceStart");
            _paramFogDistanceFull = GetEffectParameter("FogDistanceFull");
            _paramFogNearDensity = GetEffectParameter("FogNearDensity");
            _paramFogVolumeCount = GetEffectParameter("FogVolumeCount");
            _paramFogVolumeWorldToBox = GetEffectParameter("FogVolumeWorldToBox");
            _paramFogVolumeSettings = GetEffectParameter("FogVolumeSettings");
            _paramHistoryAlpha = GetEffectParameter("HistoryAlpha");
            _paramAnisotropy = GetEffectParameter("G");
            _paramPointLightCount = GetEffectParameter("PointLightCount");
            _paramPointLightPositionsVS = GetEffectParameter("PointLightPositionsVS");
            _paramPointLightPositionsWS = GetEffectParameter("PointLightPositionsWS");
            _paramPointLightColors = GetEffectParameter("PointLightColors");
            _paramPointLightRadii = GetEffectParameter("PointLightRadii");
            _paramPointLightCastShadows = GetEffectParameter("PointLightCastShadows");
            _paramPointLightShadowMapSize = GetEffectParameter("PointLightShadowMapSize");
            _paramPointLightSpotDirectionsVS = GetEffectParameter("PointLightSpotDirectionsVS");
            _paramPointLightSpotCos = GetEffectParameter("PointLightSpotCos");
            _paramUseProbeVolume = GetEffectParameter("UseProbeVolume");
            _paramProbeVolumeIntensity = GetEffectParameter("ProbeVolumeIntensity");
            _paramProbeVolumeMin = GetEffectParameter("ProbeVolumeMin");
            _paramProbeVolumeExtentRcp = GetEffectParameter("ProbeVolumeExtentRcp");
            _paramProbeVolumeCells = GetEffectParameter("ProbeVolumeCells");
            _paramProbeSHR = GetEffectParameter("ProbeData");
            _paramProbeSHG = GetEffectParameter("ProbeSHG");
            _paramProbeSHB = GetEffectParameter("ProbeSHB");
            _paramProbeDistPos = GetEffectParameter("ProbeDistPos");
            _paramProbeDistNeg = GetEffectParameter("ProbeDistNeg");

            _techniqueBuildFroxels = GetEffectTechnique("BuildFroxels");
            _techniqueResolveFroxels = GetEffectTechnique("ResolveFroxels");
            _techniqueSeedTransport = GetEffectTechnique("SeedTransport");
            _techniqueScanTransport = GetEffectTechnique("ScanTransport");
        }

        public void Initialize(GraphicsDevice graphicsDevice, FullScreenTriangle fullScreenTriangle, int screenWidth, int screenHeight)
        {
            _graphicsDevice = graphicsDevice;
            _fullScreenTriangle = fullScreenTriangle;

            try
            {
                DisposeTargets();

                _injectionTarget = CreateAtlas();
                _historyTargets[0] = CreateAtlas();
                _historyTargets[1] = CreateAtlas();
                _scanTarget = CreateAtlas();
                _historyIndex = 0;

                //Until the first frame renders, compose reads no fog: empty prefixes and an empty medium are 0
                _graphicsDevice.SetRenderTarget(_scanTarget);
                _graphicsDevice.Clear(Color.Transparent);
                _graphicsDevice.SetRenderTarget(_historyTargets[0]);
                _graphicsDevice.Clear(Color.Transparent);
                _graphicsDevice.SetRenderTarget(_historyTargets[1]);
                _graphicsDevice.Clear(Color.Transparent);
                _graphicsDevice.SetRenderTarget(null);
                _integrated = _scanTarget;

                _dummyShadowMap ??= new Texture2D(_graphicsDevice, 1, 1, false, SurfaceFormat.Color);
                _dummyShadowMap.SetData(new[] { Color.White });

                InvalidateHistory();
                _isInitialized = true;
                GameStats.d_froxelSystemReady = true;
            }
            catch (Exception ex)
            {
                GameStats.d_froxelSystemReady = false;
                Debug.WriteLine($"Failed to initialize froxel system: {ex.Message}");
            }
        }

        private RenderTarget2D CreateAtlas() =>
            new RenderTarget2D(_graphicsDevice, AtlasWidth, AtlasHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None);

        public void UpdateMatrices(Matrix view, Matrix projection, Matrix inverseView, Matrix inverseProjection, float nearClip, float farClip)
        {
            _view = view;
            _projection = projection;
            _inverseView = inverseView;
            _inverseProjection = inverseProjection;
            _nearClip = nearClip;
            _farClip = farClip;
        }

        /// <summary>
        /// Drop the fog history: the next frame uses only its own samples. Called for scene swaps, the froxels being
        /// switched off, and internally for camera cuts, projection/fog setting changes, re-baked probes and shader reloads.
        /// </summary>
        public void InvalidateHistory()
        {
            _historyValid = false;
        }

        /// <summary>Local fog boxes (Fog Volume components) of <paramref name="entities"/> for the next build.</summary>
        public void SetFogVolumes(IReadOnlyList<BasicEntity> entities)
        {
            _fogVolumeCount = Components.FogVolumeComponent.Collect(entities, _fogVolumeWorldToBox, _fogVolumeSettings);
        }

        /// <summary>
        /// Bind the baked probe volume whose bounce light the fog scatters (Environment > Volumetric Fog > Indirect),
        /// or disable it (null). <paramref name="intensity"/> is the scene's probe intensity, as for surfaces.
        /// </summary>
        public void SetProbeVolume(LightingSystem lighting, float intensity)
        {
            ProbeVolumeData data = lighting?.GpuData;
            if (data == null || lighting.ProbeData == null || _paramProbeSHR == null || _paramUseProbeVolume == null)
            {
                if (_probeData != null) InvalidateHistory();
                _probeData = null;
                _paramUseProbeVolume?.SetValue(false);
                return;
            }

            if (!ReferenceEquals(data, _probeData)) InvalidateHistory();
            _probeData = data;

            Vector3 extent = data.BoundsMax - data.BoundsMin;
            _paramUseProbeVolume.SetValue(true);
            _paramProbeVolumeIntensity?.SetValue(intensity);
            _paramProbeVolumeMin?.SetValue(data.BoundsMin);
            _paramProbeVolumeExtentRcp?.SetValue(new Vector3(1f / extent.X, 1f / extent.Y, 1f / extent.Z));
            _paramProbeVolumeCells?.SetValue(new Vector3(data.CountX - 1, data.CountY - 1, data.CountZ - 1));
            _paramProbeSHR?.SetValue(lighting.ProbeData);
        }

        public void SetNoiseMap(Texture2D noiseMap)
        {
            _noiseMap = noiseMap;
            _paramNoiseMap?.SetValue(_noiseMap);
        }

        private void ApplyCommonParameters(int screenWidth, int screenHeight, float timeSeconds)
        {
            _paramInverseProjection?.SetValue(_inverseProjection);
            _paramInverseView?.SetValue(_inverseView);
            _paramTime?.SetValue(timeSeconds);
            _paramNearClip?.SetValue(_nearClip);
            _paramFarClip?.SetValue(_farClip);
            _paramGridDimensions?.SetValue(new Vector3(FROXEL_GRID_X, FROXEL_GRID_Y, FROXEL_GRID_Z));
            _paramScreenResolution?.SetValue(new Vector2(screenWidth, screenHeight));
            _paramFroxelDensity?.SetValue(GameSettings.g_FroxelDensity);
            _paramFroxelAbsorption?.SetValue(GameSettings.g_FroxelAbsorption);
            _paramDirectionalScatter?.SetValue(GameSettings.g_FroxelDirectionalScatter);
            _paramPointScatter?.SetValue(GameSettings.g_FroxelPointScatter);
            _paramIndirectScatter?.SetValue(GameSettings.g_FroxelIndirectScatter);
            _paramFogDistanceStart?.SetValue(GameSettings.g_FroxelFogDistanceStart);
            _paramFogDistanceFull?.SetValue(GameSettings.g_FroxelFogDistanceFull);
            _paramFogNearDensity?.SetValue(GameSettings.g_FroxelNearDensity);
            _paramFogVolumeCount?.SetValue(_fogVolumeCount);
            _paramFogVolumeWorldToBox?.SetValue(_fogVolumeWorldToBox);
            _paramFogVolumeSettings?.SetValue(_fogVolumeSettings);
            _paramHistoryAlpha?.SetValue(GameSettings.g_FroxelHistoryBlend);
            _paramAnisotropy?.SetValue(GameSettings.g_FroxelAnisotropy);
        }

        private struct LightRank
        {
            public PointLight Light;
            public float Score;
            public int Index;
        }

        /// <summary>
        /// The point lights the fog uses this frame: enabled lights whose range reaches into the view, strongest at the
        /// camera first (brightness falling off with the distance beyond their radius); ties keep the scene order.
        /// </summary>
        public static void RankPointLights(List<PointLight> pointLights, Vector3 cameraPosition, BoundingFrustum frustum,
            List<PointLight> result, int maxCount = MAX_FROXEL_POINT_LIGHTS)
        {
            var ranks = new List<LightRank>();
            RankPointLights(pointLights, cameraPosition, frustum, result, ranks, maxCount);
        }

        private static void RankPointLights(List<PointLight> pointLights, Vector3 cameraPosition, BoundingFrustum frustum,
            List<PointLight> result, List<LightRank> ranks, int maxCount)
        {
            result.Clear();
            ranks.Clear();
            if (pointLights == null) return;

            for (int i = 0; i < pointLights.Count; i++)
            {
                PointLight light = pointLights[i];
                if (light == null || !light.IsEnabled || light.Intensity <= 0 || light.Radius <= 0) continue;
                if (frustum != null && frustum.Contains(new BoundingSphere(light.Position, light.Radius)) == ContainmentType.Disjoint)
                    continue;

                Vector3 color = light.ColorV3 * light.Intensity;
                float brightness = 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;
                float distance = Vector3.Distance(cameraPosition, light.Position);
                float reach = light.Radius / Math.Max(distance, light.Radius);
                ranks.Add(new LightRank { Light = light, Score = brightness * reach * reach, Index = i });
            }

            ranks.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Index.CompareTo(b.Index));
            for (int i = 0; i < ranks.Count && result.Count < maxCount; i++)
                result.Add(ranks[i].Light);
        }

        private void ApplyPointLights(List<PointLight> pointLights, Vector3 cameraPosition)
        {
            _frustum.Matrix = _view * _projection;
            RankPointLights(pointLights, cameraPosition, _frustum, _rankedLights, _lightRanks, MAX_FROXEL_POINT_LIGHTS);
            int count = _paramPointLightCount != null ? _rankedLights.Count : 0;
            _fogCookieLight = -1;

            for (int i = 0; i < count; ++i)
            {
                PointLight light = _rankedLights[i];
                _pointLightPositionsVS[i] = Vector3.Transform(light.Position, _view);
                _pointLightPositionsWS[i] = light.Position;
                _pointLightColors[i] = light.ColorV3 * light.Intensity;
                _pointLightRadii[i] = light.Radius;

                //Point lights: outer cosine -2 scatters in every direction
                SpotLight spot = light as SpotLight;
                _pointLightSpotDirectionsVS[i] = spot != null ? Vector3.Normalize(Vector3.TransformNormal(spot.Direction, _view)) : Vector3.UnitZ;
                _pointLightSpotCos[i] = spot != null ? new Vector2(spot.CosOuter, spot.CosInner) : new Vector2(-2, -1);

                bool hasShadow = light.HasShadowThisFrame && ShadowAtlas != null;
                _pointLightCastShadows[i] = hasShadow ? light.ShadowStrength : 0f;
                _pointLightShadowMapSizes[i] = hasShadow ? light.ShadowTileSize : 0f;
                for (int f = 0; f < 6; f++)
                    _pointLightShadowTiles[i * 6 + f] = hasShadow ? light.ShadowTiles[f] : Vector4.Zero;

                //The first ranked spot light with a cookie shapes its light in the fog
                if (_fogCookieLight < 0 && spot?.Cookie != null)
                {
                    _fogCookieLight = i;
                    GetEffectParameter("FogCookie")?.SetValue(spot.Cookie);
                    GetEffectParameter("FogCookieRightVS")?.SetValue(Vector3.Normalize(Vector3.TransformNormal(spot.Right, _view)));
                    GetEffectParameter("FogCookieUpVS")?.SetValue(Vector3.Normalize(Vector3.TransformNormal(spot.Up, _view)));
                    GetEffectParameter("FogCookieTanOuter")?.SetValue((float)Math.Tan(MathHelper.ToRadians(Math.Min(spot.SpotAngle, 170) * 0.5f)));
                }
            }

            for (int i = count; i < MAX_FROXEL_POINT_LIGHTS; ++i)
            {
                _pointLightPositionsVS[i] = Vector3.Zero;
                _pointLightPositionsWS[i] = Vector3.Zero;
                _pointLightColors[i] = Vector3.Zero;
                _pointLightRadii[i] = 0f;
                _pointLightCastShadows[i] = 0f;
                _pointLightShadowMapSizes[i] = 0f;
                _pointLightSpotDirectionsVS[i] = Vector3.UnitZ;
                _pointLightSpotCos[i] = new Vector2(-2, -1);
                for (int f = 0; f < 6; f++) _pointLightShadowTiles[i * 6 + f] = Vector4.Zero;
            }

            GameStats.d_froxelsLightCount = count;
            _paramPointLightCount?.SetValue(count);
            _paramPointLightPositionsVS?.SetValue(_pointLightPositionsVS);
            _paramPointLightPositionsWS?.SetValue(_pointLightPositionsWS);
            _paramPointLightColors?.SetValue(_pointLightColors);
            _paramPointLightRadii?.SetValue(_pointLightRadii);
            _paramPointLightCastShadows?.SetValue(_pointLightCastShadows);
            _paramPointLightShadowMapSize?.SetValue(_pointLightShadowMapSizes);
            GetEffectParameter("PointLightShadowTiles")?.SetValue(_pointLightShadowTiles);
            GetEffectParameter("PointShadowAtlas")?.SetValue(ShadowAtlas ?? (Texture2D)_dummyShadowMap);
            GetEffectParameter("FogCookieLight")?.SetValue((float)_fogCookieLight);
            _paramPointLightSpotDirectionsVS?.SetValue(_pointLightSpotDirectionsVS);
            _paramPointLightSpotCos?.SetValue(_pointLightSpotCos);
        }

        private void ApplyDirectionalLight(List<DirectionalLight> directionalLights)
        {
            if (directionalLights != null && directionalLights.Count > 0)
            {
                var directionalLight = directionalLights[0];
                Vector3 dirVS = Vector3.TransformNormal(directionalLight.Direction, _view);
                dirVS.Normalize();

                _paramUseDirectionalLight?.SetValue(true);
                _paramDirectionalLightDirectionVS?.SetValue(dirVS);
                _paramDirectionalLightColor?.SetValue(directionalLight.ColorV3 * directionalLight.Intensity);
                // Sun shadow cascades (world space): the shafts follow the camera out to the light's Shadow Distance
                bool shadowed = directionalLight.CastShadows && directionalLight.ShadowMap != null;
                GetEffectParameter("CascadeViewProjection")?.SetValue(directionalLight.CascadeViewProjection);
                GetEffectParameter("CascadeView")?.SetValue(directionalLight.CascadeView);
                GetEffectParameter("CascadeFarClip")?.SetValue(directionalLight.CascadeFarClip);
                GetEffectParameter("CascadeTexelSize")?.SetValue(directionalLight.CascadeTexelSize);
                GetEffectParameter("CascadeCount")?.SetValue(shadowed ? (float)directionalLight.ActiveCascades : 0f);
                GetEffectParameter("CascadeResolution")?.SetValue((float)directionalLight.CascadeResolution);
                _paramShadowMap?.SetValue(shadowed ? directionalLight.ShadowMap : _dummyShadowMap);
            }
            else
            {
                _paramUseDirectionalLight?.SetValue(false);
                _paramDirectionalLightDirectionVS?.SetValue(Vector3.Zero);
                _paramDirectionalLightColor?.SetValue(Vector3.Zero);
                GetEffectParameter("CascadeCount")?.SetValue(0f);
                _paramShadowMap?.SetValue(_dummyShadowMap);
            }
        }

        //Everything that changes the fog's look at once; history from before such a change is dropped.
        private int FogSettingsHash()
        {
            int lighting = HashCode.Combine(GameSettings.g_FroxelDensity, GameSettings.g_FroxelAbsorption,
                GameSettings.g_FroxelDirectionalScatter, GameSettings.g_FroxelPointScatter, GameSettings.g_FroxelIndirectScatter,
                GameSettings.g_FroxelAnisotropy);
            return HashCode.Combine(lighting, GameSettings.g_FroxelFogDistanceStart, GameSettings.g_FroxelFogDistanceFull,
                GameSettings.g_FroxelNearDensity, _nearClip, _farClip);
        }

        /// <summary>Reload the effect after a debug hot reload of Froxel.fx (the old one is disposed).</summary>
        private void CheckShaderReload()
        {
            if (_shaderManager == null || _shaderIndex < 0 || !_shaderManager.GetShaderHasChanged(_shaderIndex)) return;
            _froxelShader = _shaderManager.GetShader(_shaderIndex);
            InitializeShaderParameters();
            SetNoiseMap(_noiseMap);
            InvalidateHistory();
        }

        /// <summary>
        /// Injection, temporal resolve and transport integration into the internal atlases (after G-buffer / shadows).
        /// </summary>
        public void RenderFroxelFog(List<PointLight> pointLights, List<DirectionalLight> directionalLights, Vector3 cameraPosition,
            int screenWidth, int screenHeight, float timeSeconds)
        {
            if (!_isInitialized || _graphicsDevice == null || _fullScreenTriangle == null)
                return;

            CheckShaderReload();
            if (_froxelShader == null || _techniqueBuildFroxels == null || _techniqueResolveFroxels == null ||
                _techniqueSeedTransport == null || _techniqueScanTransport == null)
            {
                Debug.WriteLine("FroxelRenderModule.RenderFroxelFog skipped: missing shader technique.");
                return;
            }

            //Camera cut, new projection (FOV, aspect, far plane) or other fog: last frame's medium doesn't apply
            int settingsHash = FogSettingsHash();
            if (settingsHash != _previousSettingsHash || _projection != _previousProjection ||
                Vector3.DistanceSquared(cameraPosition, _previousCameraPosition) > CameraCutDistance * CameraCutDistance)
                InvalidateHistory();

            ApplyCommonParameters(screenWidth, screenHeight, timeSeconds);
            ApplyDirectionalLight(directionalLights);
            ApplyPointLights(pointLights, cameraPosition);
            if (_noiseMap != null) _paramNoiseMap?.SetValue(_noiseMap);

            _paramPreviousView?.SetValue(_previousView);
            _paramPreviousViewProjection?.SetValue(_previousView * _previousProjection);
            _paramHistoryValid?.SetValue(_historyValid);
            UsedHistory = _historyValid && GameSettings.g_FroxelHistoryBlend > 0;

            var prevBlend = _graphicsDevice.BlendState;
            var prevDepth = _graphicsDevice.DepthStencilState;
            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.None;

            RenderTarget2D previousHistory = _historyTargets[_historyIndex];
            int nextHistoryIndex = 1 - _historyIndex;
            RenderTarget2D history = _historyTargets[nextHistoryIndex];
            bool rendered = false;

            try
            {
                DrawAtlas("Froxel: Injection", _techniqueBuildFroxels, _injectionTarget);

                _paramInjectionTexture?.SetValue(_injectionTarget);
                _paramPreviousHistoryTexture?.SetValue(previousHistory);
                DrawAtlas("Froxel: Temporal Resolve", _techniqueResolveFroxels, history);

                //Seed into the scan target, then ping-pong with the (now free) injection target
                _paramInjectionTexture?.SetValue(history);
                DrawAtlas("Froxel: Transport Seed", _techniqueSeedTransport, _scanTarget);
                RenderTarget2D source = _scanTarget;
                RenderTarget2D destination = _injectionTarget;
                for (int stride = 1; stride < FROXEL_GRID_Z; stride *= 2)
                {
                    _paramScanStride?.SetValue(stride);
                    _paramScanInputTexture?.SetValue(source);
                    DrawAtlas("Froxel: Transport Scan", _techniqueScanTransport, destination);
                    (source, destination) = (destination, source);
                }

                _integrated = source;
                _historyIndex = nextHistoryIndex;
                rendered = true;
            }
            finally
            {
                _graphicsDevice.SetRenderTarget(null);
                //Unbind the atlases so the next frame can render into them
                _paramInjectionTexture?.SetValue((Texture2D)null);
                _paramPreviousHistoryTexture?.SetValue((Texture2D)null);
                _paramScanInputTexture?.SetValue((Texture2D)null);
                _graphicsDevice.BlendState = prevBlend;
                _graphicsDevice.DepthStencilState = prevDepth;
            }

            if (rendered)
            {
                _previousView = _view;
                _previousProjection = _projection;
                _previousCameraPosition = cameraPosition;
                _previousSettingsHash = settingsHash;
                _historyValid = true;
            }
        }

        private void DrawAtlas(string marker, EffectTechnique technique, RenderTarget2D target)
        {
            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, marker);
            try
            {
                _graphicsDevice.SetRenderTarget(target);
                _froxelShader.CurrentTechnique = technique;
                technique.Passes[0].Apply();
                _fullScreenTriangle.Draw(_graphicsDevice);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }
        }

        private void DisposeTargets()
        {
            _injectionTarget?.Dispose();
            _injectionTarget = null;
            for (int i = 0; i < _historyTargets.Length; ++i)
            {
                _historyTargets[i]?.Dispose();
                _historyTargets[i] = null;
            }
            _scanTarget?.Dispose();
            _scanTarget = null;
            _integrated = null;
        }

        public void Dispose()
        {
            DisposeTargets();
            _dummyShadowMap?.Dispose();
            _dummyShadowMap = null;
            _isInitialized = false;
        }
    }
}
