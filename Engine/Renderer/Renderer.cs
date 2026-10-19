using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Components;
using Engine.Entities;
using Engine.Editor;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Recources.Helper;
using Engine.Renderer.Features;
using Engine.Renderer.Helper;
using Engine.Renderer.Helper.HelperGeometry;
using Engine.Renderer.Lighting;
using Engine.Renderer.RenderModules;
using Engine.Renderer.RenderModules.Default;
using Engine.Renderer.RenderModules.DeferredLighting;
using Engine.Renderer.RenderModules.PostProcessingFilters;
using Engine.Renderer.RenderModules.Signed_Distance_Fields;
using Engine.Renderer.RenderModules.Signed_Distance_Fields.SDF_Generator;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DirectionalLight = Engine.Entities.DirectionalLight;
using ParticleSystemComponent = Engine.Components.ParticleSystemComponent;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//    MAIN RENDER FUNCTIONS, TheKosmonaut 2016

namespace Engine.Renderer
{
    public class Renderer : IDisposable
    {
        private readonly EnvironmentSky _environmentSky = new EnvironmentSky();
        //This frame's post processing: the scene's, blended with the Post Process Volumes at the camera
        private readonly PostProcessSettings _postBlend = new PostProcessSettings();
        //This frame's suns (the day/night cycle's when it runs) and environment sample, for the forward pass
        private List<DirectionalLight> _frameDirectionalLights;
        private EnvironmentSample _frameEnvSample;
        private float _lastSizeCull = -1;
        private List<BasicEntity> _frameEntities;
        #region VARIABLES
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  VARIABLES
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

        //Graphics & Helpers
        private GraphicsDevice _graphicsDevice;
        private SpriteBatch _spriteBatch;
        //private QuadRenderer _quadRenderer;
        private FullScreenTriangle _fullScreenTriangle;
        private GaussianBlur _gaussianBlur;
        private EditorRender _editorRender;
        private CPURayMarch _cpuRayMarch;
        private LightAccumulationModule _lightAccumulationModule;
        private FroxelRenderModule _froxelRenderModule;
        //Scene the fog history belongs to
        private Scene _froxelScene;

        private ShadowMapRenderModule _shadowMapRenderModule;
        private GBufferRenderModule _gBufferRenderModule;
        // Wet Surface materials soak and dry with the scene's weather
        private readonly SurfaceWetness _surfaceWetness = new SurfaceWetness();
        private double _wetSurfaceTime;
        private TemporalAntialiasingRenderModule _temporalAntialiasingRenderModule;
        private DeferredEnvironmentMapRenderModule _deferredEnvironmentMapRenderModule;

        //Baked lighting for the current frame (set at the top of Draw; may be null)
        private LightingSystem _lighting;
        private LightingSettings _lightingSettings;
        private DecalRenderModule _decalRenderModule;
        private SubsurfaceScatterRenderModule _subsurfaceScatterRenderModule;
        private ForwardRenderModule _forwardRenderModule;
        private WaterRenderModule _waterRenderModule;
        private WeatherRenderModule _weatherRenderModule;
        private GridRenderModule _gridRenderModule;
        private ParticleRenderModule _particleRenderModule;
        // User render features (Anvil > Render Pipeline) at their injection points.
        private RenderFeatureRenderModule _renderFeatureRenderModule;
        private RenderFeatureRenderModule.Context _renderFeatureContext;
        // The post-processed image of this frame (RenderMode draws it twice per frame).
        private RenderTarget2D _finalImage;
        // The scene's point lights plus the particle systems' (Lights module), rebuilt each frame.
        private readonly List<PointLight> _frameLights = new List<PointLight>();
        private HelperGeometryRenderModule _helperGeometryRenderModule;
        private DistanceFieldRenderModule _distanceFieldRenderModule;

        //Debug views (RenderModes.IndirectDiffuse / AmbientOcclusion / ShadowCascades)
        private Effect _debugViewEffect;
        //View Z where each sun shadow cascade ends, for RenderModes.ShadowCascades (set by the shadow pass)
        private Vector4 _cascadeEnds = new Vector4(float.MaxValue);
        private int _cascadeCount;

        private BloomFilter _bloomFilter;
        private ColorGradingFilter _colorGradingFilter;
        private AutoExposureFilter _autoExposureFilter;

        //Assets
        private Assets _assets;

        //View Projection
        private bool _viewProjectionHasChanged;
        private Vector3 _inverseResolution;

        //Temporal Anti Aliasing
        private bool _temporalAAOffFrame = true;
        private Vector3[] _haltonSequence;
        private int _haltonSequenceIndex = -1;
        private const int HaltonSequenceLength = 16;
        
        //Projection Matrices and derivates used in shaders
        private Matrix _view;
        private Matrix _inverseView;
        private Matrix _viewIT;
        private Matrix _projection;
        private Matrix _viewProjection;
        private Matrix _staticViewProjection;
        private Matrix _inverseViewProjection;
        private Matrix _inverseProjection;
        private Matrix _previousViewProjection;
        private Matrix _currentViewToPreviousViewProjection;

        //Bounding Frusta of our view projection, to calculate which objects are inside the view
        private BoundingFrustum _boundingFrustum;

        //Used for the view space directions in our shaders. Far edges of our view frustum
        private readonly Vector3[] _cornersWorldSpace = new Vector3[8];
        private readonly Vector3[] _cornersViewSpace = new Vector3[8];
        private readonly Vector3[] _currentFrustumCorners = new Vector3[4];

        //Checkvariables to see which console variables have changed from the frame before
        private float _g_FarClip;
        private float _supersampling = 1;
        //private bool _hologramDraw;
        private int _forceShadowFiltering;
        private bool _ssr = true;
        private bool _g_SSReflectionNoise;

        //SDF
        private List<SignedDistanceField> _sdfDefinitions;

        //Render modes
        public enum RenderModes {
            Deferred,
            Albedo,
            Normal,
            Depth,
            Diffuse,
            Specular,
            Volumetric,
            //Hologram,
            SSAO,
            SSBlur,
            //Emissive,
            SSR,
            HDR,
            // Source 2 look debug views (Docs/Research/Source2Look phase 0.4)
            IndirectDiffuse,
            AmbientOcclusion,
            ShadowCascades,
            // Lightmaps (phase 4.2): the lightmap pass's indirect light and its static lights' shadow mask
            Lightmap,
            ShadowMask
        }

        //Render targets
        private RenderTarget2D _renderTargetAlbedo;
        private readonly RenderTargetBinding[] _renderTargetBinding = new RenderTargetBinding[4];

        private RenderTarget2D _renderTargetDepth;
        private RenderTarget2D _renderTargetNormal;

        private RenderTarget2D _renderTargetDecalOffTarget;

        //Motion vectors of the G-buffer (TAA, Source 2 look phase 1.6) and the unjittered view projection of last frame
        private RenderTarget2D _renderTargetVelocity;
        private Matrix _lastStaticViewProjection;
        private bool _hasLastStaticViewProjection;

        //Retina burn (Half-Life: Alyx): quarter resolution afterimage ping-pong
        private Effect _retinaBurnEffect;
        private RenderTarget2D _retinaBurnA, _retinaBurnB;
        private bool _retinaBurnFlip;

        //FXAA of the final image while TAA is off
        private Effect _fxaaEffect;
        private RenderTarget2D _renderTargetFxaa;

        //Subsurface Scattering
        private RenderTarget2D _renderTargetSSS;

        private RenderTarget2D _renderTargetDiffuse;
        private RenderTarget2D _renderTargetSpecular;
        private RenderTarget2D _renderTargetVolume;
        private readonly RenderTargetBinding[] _renderTargetLightBinding = new RenderTargetBinding[3];
        //Environment pass: diffuse, specular and its own indirect diffuse (rgb) + ambient occlusion (a) for the debug views
        private RenderTarget2D _renderTargetIndirect;
        private readonly RenderTargetBinding[] _renderTargetEnvironmentBinding = new RenderTargetBinding[3];

        //Lightmaps (Source 2 look, phase 4.2): the lightmap pass writes the indirect light + AO of Lightmap Static pixels and
        //the static lights' shadow mask (LightmapRenderModule)
        private RenderTarget2D _renderTargetLightmap;
        private RenderTarget2D _renderTargetShadowMask;
        private LightmapRenderModule _lightmapRenderModule;

        private RenderTarget2D _renderTargetComposed;
        private RenderTarget2D _renderTargetBloom;

        //TAA
        private RenderTarget2D _renderTargetTAA_1;
        private RenderTarget2D _renderTargetTAA_2;
        
        private RenderTarget2D _renderTargetScreenSpaceEffectReflection;

        //private RenderTarget2D _renderTargetHologram;

        private RenderTarget2D _renderTargetSSAOEffect;

        private RenderTarget2D _renderTargetScreenSpaceEffectUpsampleBlurVertical;

        private RenderTarget2D _renderTargetScreenSpaceEffectUpsampleBlurHorizontal;
        private RenderTarget2D _renderTargetScreenSpaceEffectBlurFinal;

        //private RenderTarget2D _renderTargetEmissive;

        private RenderTarget2D _renderTargetOutput;

        private RenderTarget2D _currentOutput;
        
        //Cubemap: the raw capture of the scene's EnvironmentSample (water, particles and weather read it)
        private RenderTargetCube _renderTargetCubeMap;
        //Image based lighting (Source 2 look, phase 3): GGX-prefiltered global probe, BRDF lookup, Reflection Probes
        private EnvironmentPrefilter _environmentPrefilter;
        private TextureCube _prefilteredGlobal;
        private readonly ReflectionProbes _reflectionProbes = new ReflectionProbes();
        //AO Proxy components of this frame (Source 2 look, phase 6.1)
        private readonly AoProxyShapes _aoProxies = new AoProxyShapes();
        //Planar Reflection components: mirror images of glass panes, drawn before the forward pass
        private readonly PlanarReflectionRenderModule _planarReflections = new PlanarReflectionRenderModule();
        //Optional occlusion culling (phase 7)
        private readonly OcclusionCulling _occlusionCulling = new OcclusionCulling();
        private readonly Dictionary<int, RenderTargetCube> _probeCaptureTargets = new Dictionary<int, RenderTargetCube>();

        //Color Correction LUT

        private Texture2D _lut;
        

        //Performance Profiler

        private readonly Stopwatch _performanceTimer = new Stopwatch();
        private long _performancePreviousTime;

        #endregion

        #region FUNCTIONS

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////

            #region BASE FUNCTIONS

            ////////////////////////////////////////////////////////////////////////////////////////////////////////
            //  BASE FUNCTIONS
            ////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Initialize variables
        /// </summary>
        /// <param name="content"></param>
        public void Load(ContentManager content, ShaderManager shaderManager)
        {
            _bloomFilter = new BloomFilter();
            _bloomFilter.Load(content);


            _lightAccumulationModule = new LightAccumulationModule(shaderManager, "Shaders/Deferred/DeferredPointLight");
            _shadowMapRenderModule = new ShadowMapRenderModule(content, "Shaders/Shadow/ShadowMap");
            _gBufferRenderModule = new GBufferRenderModule(content, "Shaders/GbufferSetup/ClearGBuffer", "Shaders/GbufferSetup/Gbuffer");
            _temporalAntialiasingRenderModule = new TemporalAntialiasingRenderModule(content, "Shaders/TemporalAntiAliasing/TemporalAntiAliasing");
            _deferredEnvironmentMapRenderModule = new DeferredEnvironmentMapRenderModule(content, "Shaders/Deferred/DeferredEnvironmentMap");
            _decalRenderModule = new DecalRenderModule(shaderManager, "Shaders/Deferred/DeferredDecal");
            _lightmapRenderModule = new LightmapRenderModule(content, "Shaders/Deferred/Lightmap");
            _subsurfaceScatterRenderModule = new SubsurfaceScatterRenderModule(content, "Shaders/SubsurfaceScattering/SubsurfaceScattering");
            _forwardRenderModule = new ForwardRenderModule(content, "Shaders/forward/forward");
            _waterRenderModule = new WaterRenderModule(content);
            _weatherRenderModule = new WeatherRenderModule(content);
            _gridRenderModule = new GridRenderModule(content);
            _particleRenderModule = new ParticleRenderModule(content);
            _renderFeatureRenderModule = new RenderFeatureRenderModule();
            _helperGeometryRenderModule = new HelperGeometryRenderModule(content, "Shaders/Editor/LineEffect");
            _distanceFieldRenderModule = new DistanceFieldRenderModule(shaderManager, "Shaders/SignedDistanceFields/volumeProjection");
            _froxelRenderModule = new FroxelRenderModule(shaderManager, "Shaders/Deferred/Froxel");

            _inverseResolution = new Vector3(1.0f / GameSettings.g_screenwidth, 1.0f / GameSettings.g_screenheight, 0);
            
            _colorGradingFilter = new ColorGradingFilter(content, "Shaders/PostProcessing/ColorGrading");
            _autoExposureFilter = new AutoExposureFilter(content, "Shaders/PostProcessing/AutoExposure");

            _lut = content.Load<Texture2D>("Shaders/PostProcessing/lut");
            _debugViewEffect = content.Load<Effect>("Shaders/Editor/DebugView");
            _environmentPrefilter = new EnvironmentPrefilter(content);
            _fxaaEffect = content.Load<Effect>("Shaders/PostProcessing/Fxaa");
            _retinaBurnEffect = content.Load<Effect>("Shaders/PostProcessing/RetinaBurn");
        }

        /// <summary>
        /// Initialize all our rendermodules and helpers. Done after the Load() function
        /// </summary>
        /// <param name="graphicsDevice"></param>
        /// <param name="assets"></param>
        public void Initialize(GraphicsDevice graphicsDevice, Assets assets)
        {
            _graphicsDevice = graphicsDevice;
            //_quadRenderer = new QuadRenderer();
            _fullScreenTriangle = new FullScreenTriangle(graphicsDevice);
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _gaussianBlur = new GaussianBlur();
            _gaussianBlur.Initialize(graphicsDevice);

            _editorRender = new EditorRender();
            _editorRender.Initialize(graphicsDevice, assets);

            _cpuRayMarch = new CPURayMarch();
            _cpuRayMarch.Initialize(_graphicsDevice);

            _bloomFilter.Initialize(_graphicsDevice, GameSettings.g_screenwidth, GameSettings.g_screenheight, _fullScreenTriangle);

            _colorGradingFilter.Initialize(graphicsDevice);
            _autoExposureFilter.Initialize(graphicsDevice, _fullScreenTriangle);

            _lightAccumulationModule.Initialize(graphicsDevice, _fullScreenTriangle, assets);

            _gBufferRenderModule.Initialize(_graphicsDevice);

            _decalRenderModule.Initialize(graphicsDevice);

            _subsurfaceScatterRenderModule.Initialize();

            _forwardRenderModule.Initialize();

            _helperGeometryRenderModule.Initialize();

            _renderFeatureRenderModule.Initialize(graphicsDevice);
            
            _froxelRenderModule.Initialize(graphicsDevice, _fullScreenTriangle, GameSettings.g_screenwidth, GameSettings.g_screenheight);
            _froxelRenderModule.SetNoiseMap(assets.NoiseMap);
            
            _assets = assets;
            //Apply some base settings to overwrite shader defaults with game settings defaults
            GameSettings.ApplySettings();

            Shaders.ScreenSpaceReflectionParameter_NoiseMap.SetValue(_assets.NoiseMap);

            // Prefer a cubemap sky, but allow 2D lat-long sky textures as fallback.
            if (_assets.SkyCubemap != null)
            {
                _deferredEnvironmentMapRenderModule.SkyCubemap = _assets.SkyCubemap;
            }
            else if (_assets.SkyTexture != null)
            {
                _deferredEnvironmentMapRenderModule.SkyTexture = _assets.SkyTexture;
            }
            SetUpRenderTargets(GameSettings.g_screenwidth, GameSettings.g_screenheight, false);
            _deferredEnvironmentMapRenderModule.BrdfLut = _environmentPrefilter.BrdfLut(graphicsDevice);
            
        }

        /// <summary>
        /// Update our function
        /// </summary>
        /// <param name="gameTime"></param>
        /// <param name="isActive"></param>
        public void Update(GameTime gameTime, bool isActive, SdfGenerator sdfGenerator, List<BasicEntity> entities)
        {
            if (!isActive) return;
            _editorRender.Update(gameTime);

            //SDF Updating
            sdfGenerator.Update(entities, _graphicsDevice, _distanceFieldRenderModule, _fullScreenTriangle, ref _sdfDefinitions);

        }

        #endregion

        #region RENDER FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  RENDER FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////////

        #region MAIN DRAW FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        //  MAIN DRAW FUNCTIONS
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Main Draw function of the game
        /// </summary>
        /// <param name="camera">view point of the renderer</param>
        /// <param name="meshMaterialLibrary">a class that has stored all our mesh data</param>
        /// <param name="entities">entities and their properties</param>
        /// <param name="pointLights"></param>
        /// <param name="directionalLights"></param>
        /// <param name="editorData">The data passed from our editor logic</param>
        /// <param name="gameTime"></param>
        /// <param name="lighting">Baked lighting (probe volume GPU data); optional</param>
        /// <param name="lightingSettings">The active scene's lighting settings; optional</param>
        /// <returns></returns>
        public EditorLogic.EditorReceivedData Draw(Camera camera, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities, List<Decal> decals, List<PointLight> pointLights, List<DirectionalLight> directionalLights, EnvironmentSample envSample, List<DebugEntity> debugEntities, EditorLogic.EditorSendData editorData, GameTime gameTime, LightingSystem lighting = null, LightingSettings lightingSettings = null, Scene scene = null)
        {
            // The day/night cycle swaps in its own sun/moon; editor gizmos keep showing the scene's lights.
            List<DirectionalLight> sceneDirectionalLights = directionalLights;
            if (scene != null)
            {
                directionalLights = _environmentSky.Apply(scene, gameTime, _graphicsDevice, _assets, _deferredEnvironmentMapRenderModule);
                // Post processing of the scene, blended with the Post Process Volumes around the camera (before the fog:
                // volumes scale its density)
                ApplyScenePost(PostProcessVolumes.Evaluate(scene.Environment?.Post, entities, camera.Position, _postBlend));
                ApplySceneFog(scene.Environment);
                _autoExposureFilter.SceneMaxExposure = scene.Environment?.MaxAutoExposure ?? float.MaxValue;
            }
            // Set every frame: GameSettings.Exposure writes the base value whenever it is edited.
            Shaders.PostProcessingParameter_PowExposure.SetValue(
                (float)Math.Pow(2, GameSettings.Exposure + (scene != null ? _environmentSky.ExposureOffset : 0)));
            _lighting = lighting;
            _lightingSettings = lightingSettings;

            // Paused (pause menu): puddles keep their wetness and ripples hold still.
            double wetSeconds = GameFlow.Paused ? 0 : gameTime.ElapsedGameTime.TotalSeconds;
            _surfaceWetness.Update(scene, scene?.Environment, wetSeconds);
            _wetSurfaceTime = (_wetSurfaceTime + wetSeconds) % 3600;
            _gBufferRenderModule.SceneWetness = _surfaceWetness.Wetness;
            _gBufferRenderModule.SceneRipples = _surfaceWetness.Ripples;
            _gBufferRenderModule.SceneDrops = _surfaceWetness.Drops;
            _gBufferRenderModule.Time = (float)_wetSurfaceTime;

            _frameDirectionalLights = directionalLights;
            _frameEnvSample = envSample;
            _frameEntities = entities;

            //Reset the stat counter, so we can count stats/information for this frame only
            ResetStats();
            _finalImage = null;

            if (GameSettings.d_drawnothing)
            {
                _graphicsDevice.Clear(Color.Black);
                return new EditorLogic.EditorReceivedData
                {
                    HoveredId = 0,
                    ViewMatrix = _view,
                    ProjectionMatrix = _projection
                };
            } 

            //Particle System Lights modules: their lights join the scene's for every lighting pass (shadows,
            //deferred, forward, fog), while the editor passes below keep using the scene's own list.
            List<PointLight> sceneLights = pointLights;
            _frameLights.Clear();
            _frameLights.AddRange(sceneLights);
            ParticleSystemComponent.CollectLights(entities, _frameLights);
            pointLights = _frameLights;

            //Update the mesh data for changes in physics etc.
            meshMaterialLibrary.FlagMovedObjects(entities);

            //Reflection Probe components (their box meshes are hidden before any pass draws)
            _reflectionProbes.Update(entities, scene, _graphicsDevice);

            //LOD components: pick each gameobject's level for this camera before any pass draws
            meshMaterialLibrary.SelectLevelsOfDetail(entities, camera.Position);

            //Check if we changed some drastic stuff for which we need to reload some elements
            CheckRenderChanges(directionalLights);

            //Render ShadowMaps (their own screen-size culling threshold)
            MeshMaterialLibrary.SizeCullThreshold = Math.Max(GameSettings.g_sizecullthresholdshadow, 0);
            meshMaterialLibrary.SkipOccluded = false;
            DrawShadowMaps(meshMaterialLibrary, entities, pointLights, directionalLights, camera);
            MeshMaterialLibrary.SizeCullThreshold = 0;

            //Update SDFs
            if (IsSDFUsed(pointLights))
            {
                _distanceFieldRenderModule.UpdateDistanceFieldTransformations(entities, _sdfDefinitions, _deferredEnvironmentMapRenderModule, _graphicsDevice, _spriteBatch, _lightAccumulationModule);
            }
            //Render EnvironmentMaps
            //We do this either when pressing C or at the start of the program (_renderTargetCube == null) or when the game settings want us to do it every frame
            if (envSample.NeedsUpdate || GameSettings.g_envmapupdateeveryframe)
            {
                DrawCubeMap(envSample.Position, meshMaterialLibrary, entities, pointLights, directionalLights, envSample, 300, gameTime, camera);
                envSample.NeedsUpdate = false;
            }

            //Reflection Probe components: one capture per frame, nearest first (Source 2 look, phase 3.2)
            if (GameSettings.g_environmentmapping && _prefilteredGlobal != null)
            {
                ReflectionProbes.Probe next = _reflectionProbes.NextCapture(camera.Position);
                if (next != null)
                {
                    CaptureReflectionProbe(next, meshMaterialLibrary, entities, pointLights, directionalLights, gameTime, camera);
                    _reflectionProbes.StoreCapture(next, scene);
                }
            }

            //Update our view projection matrices if the camera moved (the view culls by screen size; the size test needs
            //the culling to run again whenever the threshold changes)
            MeshMaterialLibrary.SizeCullThreshold = Math.Max(GameSettings.g_sizecullthreshold, 0);
            if (_lastSizeCull != MeshMaterialLibrary.SizeCullThreshold) { _lastSizeCull = MeshMaterialLibrary.SizeCullThreshold; camera.HasChanged = true; }
            UpdateViewProjection(camera, meshMaterialLibrary, entities);
            MeshMaterialLibrary.SizeCullThreshold = 0;

            //Rebuild cookie textures before the frame targets contain scene colour or reconstructed depth.
            if (GameSettings.g_forwardenable)
                _forwardRenderModule.PrepareCookies(_graphicsDevice, camera, _boundingFrustum, pointLights);

            //What render features read: camera, G-buffer, time
            _renderFeatureContext = new RenderFeatureRenderModule.Context
            {
                View = _view,
                Projection = _projection,
                ViewProjection = _viewProjection,
                InverseViewProjection = _inverseViewProjection,
                CameraPosition = camera.Position,
                FarClip = _g_FarClip,
                DepthMap = _renderTargetDepth,
                NormalMap = _renderTargetNormal,
                AlbedoMap = _renderTargetAlbedo,
                Time = (float)(gameTime.TotalGameTime.TotalSeconds % 3600),
                DeltaTime = GameFlow.Paused ? 0 : (float)gameTime.ElapsedGameTime.TotalSeconds,
            };
            
            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "G-Buffer Pass");
            try
            {
                //Draw our meshes to the G Buffer, with motion vectors since last frame
                _gBufferRenderModule.StaticViewProjection = _staticViewProjection;
                _gBufferRenderModule.PreviousStaticViewProjection = _hasLastStaticViewProjection ? _lastStaticViewProjection : _staticViewProjection;
                meshMaterialLibrary.SkipOccluded = GameSettings.g_occlusionculling;
                DrawGBuffer(meshMaterialLibrary);

                //Occlusion queries against this G-buffer's depth, read next frame
                _occlusionCulling.Update(_graphicsDevice, meshMaterialLibrary, _staticViewProjection, camera.Position);
                GameStats.OccludedMeshes = _occlusionCulling.Occluded;

                //Deferred Decals
                DrawDecals(decals);

                //Baked lightmaps of Lightmap Static gameobjects: indirect light, AO and the static lights' shadow mask
                DrawLightmaps(entities);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }

            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Lighting Pass");
            try
            {
                //Draw Screen Space reflections to a different render target
                DrawScreenSpaceReflections(gameTime);

                //SSAO
                DrawScreenSpaceAmbientOcclusion(camera);

                //Upsample/blur our SSAO / screen space shadows
                DrawBilateralBlur();

                // Froxel fog: medium injection, temporal resolve and transport scan (composed in DeferredCompose)
                DrawFroxelFog(camera, entities, pointLights, directionalLights, scene, gameTime);

                //Light the scene
                _lightAccumulationModule.DrawLights(pointLights, directionalLights, camera.Position, gameTime, _renderTargetLightBinding, _renderTargetDiffuse);

                //Draw the environment cube map as a fullscreen effect on all meshes
                DrawEnvironmentMap(envSample, camera, gameTime);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }

            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Post-Processing");
            try
            {
                //Draw emissive materials on an offscreen render target
                //DrawEmissiveEffect(camera, meshMaterialLibrary, gameTime);

                //Planar Reflection panes: their mirror images for the forward pass
                DrawPlanarReflections(meshMaterialLibrary, camera, pointLights);

                //Compose the scene by combining our lighting data with the gbuffer data
                _currentOutput = Compose(); //-> output _renderTargetComposed

                //Render features: After Opaques (the forward pass reconstructs the depth it needs afterwards)
                _currentOutput = DrawRenderFeatures(RenderFeatureEvent.AfterOpaques, _currentOutput, inPlace: true);

                ///*_currentOutput =*/ DrawSubsurfaceScattering(_renderTargetSSS, meshMaterialLibrary);

                //Forward
                _currentOutput = DrawForward(_currentOutput, meshMaterialLibrary, camera, pointLights);
                // The opaque depth is reconstructed for both forward passes.
                if (!GameSettings.g_forwardenable)
                {
                    _graphicsDevice.SetRenderTarget(_currentOutput);
                    ReconstructDepth();
                }
                // Splash rings (MainSceneLogic.WaterSplashes) and raindrop rings on the water surface.
                Particles.WaterSplashes splashes = GameFlow.SceneLogic?.WaterSplashes;
                _waterRenderModule.Splashes = splashes;
                _waterRenderModule.RainRipples = _surfaceWetness.Ripples;
                _waterRenderModule.Draw(_graphicsDevice, meshMaterialLibrary, _viewProjection,
                    camera, _renderTargetCubeMap, directionalLights, gameTime, _renderTargetDepth, _view, _g_FarClip);

                // Particle System components: fire, smoke, sparks... fading into the depth they read.
                // Splash droplets have no gameobject and are passed alongside.
                _particleRenderModule.Draw(_graphicsDevice, entities, camera, _view, _viewProjection,
                    _renderTargetCubeMap, directionalLights, _renderTargetDepth, _g_FarClip, splashes?.VisibleSystems(), pointLights);

                // Rain, sandstorm or snow: haze and particles over the opaque and transparent scene.
                // Paused (pause menu): the particles hang in the air.
                _weatherRenderModule.Update(scene?.Environment, GameFlow.Paused ? 0 : gameTime.ElapsedGameTime.TotalSeconds);
                _weatherRenderModule.Draw(_graphicsDevice, scene?.Environment, camera, _view, _viewProjection,
                    _renderTargetCubeMap, directionalLights, _renderTargetDepth, _g_FarClip, _fullScreenTriangle, _surfaceWetness);

                //Render features: After Transparents
                _currentOutput = DrawRenderFeatures(RenderFeatureEvent.AfterTransparents, _currentOutput, inPlace: true);
                
                //Compose the image and add information from previous frames to apply temporal super sampling
                _currentOutput = TonemapAndCombineTemporalAntialiasing(_currentOutput); // -> output: _temporalAAOffFrame ? _renderTargetTAA_2 : _renderTargetTAA_1

                //Do Bloom
                _currentOutput = DrawBloom(_currentOutput); // -> output: _renderTargetBloom

                //Eye adaptation: meter the final HDR image here, once per frame (RenderMode runs twice)
                UpdateAutoExposure(_currentOutput, gameTime);

                //Bright lights burn an afterimage in (Post Processing > Retina Burn)
                UpdateRetinaBurn(_currentOutput, gameTime);

                //Render features: After Bloom. Not in place: the image can be the TAA history.
                _currentOutput = DrawRenderFeatures(RenderFeatureEvent.AfterBloom, _currentOutput, inPlace: false);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }

            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Final Compose");
            try
            {
                //Draw the final rendered image, change the output based on user input to show individual buffers/rendertargets
                RenderMode(_currentOutput);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }
            
            //Draw the elements that we are hovering over with outlines
            if(GameSettings.e_enableeditor && GameStats.e_EnableSelection)
                _editorRender.DrawIds(meshMaterialLibrary, decals, sceneLights, sceneDirectionalLights, envSample, debugEntities, _staticViewProjection, _view, editorData);

            //Draw the final rendered image, change the output based on user input to show individual buffers/rendertargets
            RenderMode(_currentOutput);

            //Draw signed distance field functions
            DrawSignedDistanceFieldFunctions(camera);
            
            //Editor ground grid (Anvil's viewport Grid button), under the outlines and gizmos
            _gridRenderModule.Draw(_graphicsDevice, camera, _view, _staticViewProjection, _renderTargetDepth, _g_FarClip, _fullScreenTriangle);

            //Additional editor elements that overlay our screen
            
            RenderEditorOverlays(editorData, meshMaterialLibrary, decals, sceneLights, sceneDirectionalLights, envSample, debugEntities);
            

            //Debug ray marching
                CpuRayMarch(camera);

            //Baked lighting probes / volume bounds (Inspector > Lighting > Show Probes)
            if (GameSettings.e_enableeditor && _lightingSettings != null && _lightingSettings.ShowProbes)
                _lighting?.DrawDebug(_lightingSettings, _lightingSettings.AutoBounds
                    ? (BoundingBox?)null
                    : new BoundingBox(_lightingSettings.BoundsMin, _lightingSettings.BoundsMax));

            //Draw debug geometry
            _helperGeometryRenderModule.Draw(_graphicsDevice, _staticViewProjection);
            
            meshMaterialLibrary.SkipOccluded = false;

            //Set up the frustum culling for the next frame (and keep this frame's transforms for the motion vectors)
            meshMaterialLibrary.FrustumCullingFinalizeFrame(entities);
            _lastStaticViewProjection = _staticViewProjection;
            _hasLastStaticViewProjection = true;

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileTotalRender = performanceCurrentTime;
            }
            
            //return data we have recovered from the editor id, so we know what entity gets hovered/clicked on and can manipulate in the update function
            return new EditorLogic.EditorReceivedData
            {
                HoveredId =  _editorRender.GetHoveredId(),
                ViewMatrix =  _view,
                ProjectionMatrix =  _projection
            };
        }

        private bool IsSDFUsed(List<PointLight> pointLights)
        {
            for (var index = 0; index < pointLights.Count; index++)
            {
                if (pointLights[index].CastSDFShadows)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly List<(Vector3 From, Vector3 To, int Bone)> _armatureLines = new();

        // True when `bone` is `root` or one of its descendants.
        private static bool IsInBranch(Animation.SkinningData skeleton, int bone, int root)
        {
            for (int i = bone; i >= 0; i = skeleton.ParentIndices[i])
                if (i == root) return true;
            return false;
        }

        private void RenderEditorOverlays(EditorLogic.EditorSendData editorData, MeshMaterialLibrary meshMaterialLibrary, List<Decal> decals, List<PointLight> pointLights, List<DirectionalLight> directionalLights, EnvironmentSample envSample, List<DebugEntity> debugEntities)
        {

            if (GameSettings.e_enableeditor && GameStats.e_EnableSelection)
            {
                if (GameSettings.e_drawoutlines)
                    DrawMapToScreenToFullScreen(_editorRender.GetOutlines(), BlendState.Additive);

                _editorRender.DrawEditorElements(meshMaterialLibrary, decals, pointLights, directionalLights, envSample,
                    debugEntities,
                    _staticViewProjection, _view, editorData);


                if (editorData.SelectedObject != null)
                {
                    if (editorData.SelectedObject is Decal)
                    {
                        _decalRenderModule.DrawOutlines(_graphicsDevice, editorData.SelectedObject as Decal,
                            _staticViewProjection, _view);
                    }

                    if (GameSettings.e_drawboundingbox)
                        if (editorData.SelectedObject is BasicEntity)
                        {
                            HelperGeometryManager.GetInstance()
                                .AddBoundingBox(editorData.SelectedObject as BasicEntity);
                        }

                    // The armature of a selected skinned gameobject (or of the selected bone's), the selected bone's
                    // branch highlighted, so the bones listed in Anvil's Hierarchy can be found in the viewport.
                    var bone = editorData.SelectedObject as BoneObject;
                    if ((editorData.SelectedObject as BasicEntity ?? bone?.Owner) is { } skinned)
                    {
                        _armatureLines.Clear();
                        skinned.BonePose.CollectLines(skinned, _armatureLines);
                        var skeleton = Animation.SkinningData.From(skinned.Model);
                        foreach (var (from, to, index) in _armatureLines)
                        {
                            bool highlighted = bone != null && (bone.IsArmature || IsInBranch(skeleton, index, bone.BoneIndex));
                            Color color = highlighted ? Color.Orange : Color.DeepSkyBlue;
                            HelperGeometryManager.GetInstance().AddLineStartEnd(from, to, 1, color, color);
                        }
                    }

                    // Physics > Collider Size, so it can be fitted by eye.
                    if (editorData.SelectedObject is BasicEntity { Physics: { Enabled: true } physics } selected)
                    {
                        ScenePhysics.ColliderBounds(selected, physics.ColliderStretchFor(selected), out Vector3 min, out Vector3 max);
                        HelperGeometryManager.GetInstance().AddBox(min, max, selected.WorldTransform.World, Color.LimeGreen);
                    }
                }
            }

            if(GameSettings.sdf_debug && _distanceFieldRenderModule.GetAtlas()!= null)
            {
                _spriteBatch.Begin(0, BlendState.Opaque, SamplerState.PointClamp);
                _spriteBatch.Draw(_distanceFieldRenderModule.GetAtlas() , new Rectangle(0, GameSettings.g_screenheight - 200, GameSettings.g_screenwidth, 200), Color.White);
                _spriteBatch.End();
            }

            // //Show shadow maps
            //if (editorData.SelectedObject != null)
            //{
            //    if (editorData.SelectedObject is PointLight)
            //    {
            //        int size = 128;
            //        PointLight light = pointLights[2]; /*(PointLightSource)editorData.SelectedObject*/
            //        ;
            //        if (light.CastShadows)
            //        {
            //            _spriteBatch.Begin(0, BlendState.Opaque, SamplerState.PointClamp);
            //            _spriteBatch.Draw(light.ShadowMap,
            //                new Rectangle(0, GameSettings.g_screenheight - size*6, size, size*6), Color.White);
            //            _spriteBatch.End();
            //        }
            //    }

            //}
        }

        /// <summary>
        /// Another draw function, but this time for cubemaps. Doesn't need all the stuff we have in the main draw function
        /// </summary>
        /// <param name="origin">from where do we render the cubemap</param>
        /// <param name="meshMaterialLibrary"></param>
        /// <param name="entities"></param>
        /// <param name="pointLights"></param>
        /// <param name="dirLights"></param>
        /// <param name="farPlane"></param>
        /// <param name="gameTime"></param>
        /// <param name="camera"></param>
        private void DrawCubeMap(Vector3 origin, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities, List<PointLight> pointLights, List<DirectionalLight> dirLights, EnvironmentSample envSample, float farPlane, GameTime gameTime, Camera camera)
        {
            //If our cubemap is not yet initialized, create a new one
            if (_renderTargetCubeMap == null)
            {
                //Create a new cube map
                _renderTargetCubeMap = new RenderTargetCube(_graphicsDevice, GameSettings.g_envmapresolution, true, SurfaceFormat.HalfVector4,
                    DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
            }

            CaptureCube(origin, _renderTargetCubeMap, meshMaterialLibrary, entities, pointLights, dirLights, gameTime, camera, withIndirect: false);

            //The global probe: GGX prefiltered for the environment pass (Source 2 look, phase 3.1)
            _prefilteredGlobal = _environmentPrefilter.Prefilter(_graphicsDevice, _fullScreenTriangle, _renderTargetCubeMap,
                Math.Clamp(GameSettings.g_reflectionprobesize, 32, 1024), _prefilteredGlobal);
            _deferredEnvironmentMapRenderModule.SetGlobalProbe(_prefilteredGlobal);

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawCubeMap = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>Captures and prefilters one Reflection Probe component (its box centre, with the baked indirect light).</summary>
        private void CaptureReflectionProbe(ReflectionProbes.Probe probe, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities,
            List<PointLight> pointLights, List<DirectionalLight> dirLights, GameTime gameTime, Camera camera)
        {
            int size = probe.Component.ClampedResolution;
            //Captured at twice the probe size so the prefilter's mirror mip stays sharp
            int captureSize = Math.Min(size * 2, 1024);
            if (!_probeCaptureTargets.TryGetValue(captureSize, out RenderTargetCube target))
                _probeCaptureTargets[captureSize] = target = new RenderTargetCube(_graphicsDevice, captureSize, true,
                    SurfaceFormat.HalfVector4, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

            //Its own mesh is hidden; nothing else changes while capturing
            CaptureCube(probe.Owner.Position, target, meshMaterialLibrary, entities, pointLights, dirLights, gameTime, camera, withIndirect: true);
            probe.Cube = _environmentPrefilter.Prefilter(_graphicsDevice, _fullScreenTriangle, target, size, probe.Cube);
            probe.CapturedRequest = probe.Component.CaptureRequest;
            probe.CapturedResolution = size;
            probe.CapturedBox = ReflectionProbeComponent.BoxWorld(probe.Owner);
        }

        /// <summary>
        /// Renders the scene around <paramref name="origin"/> into <paramref name="target"/>'s six faces (no TAA, fog,
        /// SSR or AO). <paramref name="withIndirect"/> adds the baked diffuse light (probe volume / sky) like the main view.
        /// </summary>
        private void CaptureCube(Vector3 origin, RenderTargetCube target, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities,
            List<PointLight> pointLights, List<DirectionalLight> dirLights, GameTime gameTime, Camera camera, bool withIndirect)
        {
            float farPlane = GameSettings.g_farplane;

            //Set up all the base rendertargets with the resolution of our cubemap
            SetUpRenderTargets(target.Size, target.Size, true);

            //Create our projection, which is a basic pyramid
            _projection = Matrix.CreatePerspectiveFieldOfView((float)(Math.PI / 2), 1, 1, farPlane);

            //Now we need to actually render for each cubemapface (6 direcetions)
            for (int i = 0; i < 6; i++)
            {
                // render the scene to all cubemap faces
                CubeMapFace cubeMapFace = (CubeMapFace)i;
                switch (cubeMapFace)
                {
                    case CubeMapFace.NegativeX:
                        {
                            _view = Matrix.CreateLookAt(origin, origin + Vector3.Left, Vector3.Up);
                            break;
                        }
                    case CubeMapFace.NegativeY:
                        {
                            _view = Matrix.CreateLookAt(origin, origin + Vector3.Down, Vector3.Forward);
                            break;
                        }
                    case CubeMapFace.NegativeZ:
                        {
                            _view = Matrix.CreateLookAt(origin, origin + Vector3.Backward, Vector3.Up);
                            break;
                        }
                    case CubeMapFace.PositiveX:
                        {
                            _view = Matrix.CreateLookAt(origin, origin + Vector3.Right, Vector3.Up);
                            break;
                        }
                    case CubeMapFace.PositiveY:
                        {
                            _view = Matrix.CreateLookAt(origin, origin + Vector3.Up, Vector3.Backward);
                            break;
                        }
                    case CubeMapFace.PositiveZ:
                        {
                            _view = Matrix.CreateLookAt(origin, origin + Vector3.Forward, Vector3.Up);
                            break;
                        }
                }

                //Create our projection matrices
                _inverseView = Matrix.Invert(_view);
                _viewProjection = _view * _projection;
                _inverseViewProjection = Matrix.Invert(_viewProjection);
                _viewIT = Matrix.Transpose(_inverseView);

                //Pass these values to our shader
                Shaders.ScreenSpaceEffectParameter_InverseViewProjection.SetValue(_inverseView);
                _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameter_InverseView.SetValue(_inverseView);

                //yep we changed
                _viewProjectionHasChanged = true;

                if (_boundingFrustum == null) _boundingFrustum = new BoundingFrustum(_viewProjection);
                else _boundingFrustum.Matrix = _viewProjection;
                ComputeFrustumCorners(_boundingFrustum, origin);

                _lightAccumulationModule.UpdateViewProjection(_boundingFrustum, _viewProjectionHasChanged, _view, _inverseView, _viewIT, _projection, _viewProjection, _inverseViewProjection);

                //Base stuff, for description look in Draw()
                meshMaterialLibrary.FrustumCulling(entities, _boundingFrustum, true, origin);

                _gBufferRenderModule.StaticViewProjection = _viewProjection;
                _gBufferRenderModule.PreviousStaticViewProjection = _viewProjection;
                DrawGBuffer(meshMaterialLibrary);

                bool volumeEnabled = GameSettings.g_VolumetricLights;
                GameSettings.g_VolumetricLights = false;
                //The shadow mask is the main view's screen: captures light without it
                _lightAccumulationModule.PointLightRenderModule.ShadowMask = null;
                _lightAccumulationModule.DrawLights(pointLights, dirLights, origin, gameTime, _renderTargetLightBinding, _renderTargetDiffuse);

                _deferredEnvironmentMapRenderModule.DrawSky(_graphicsDevice, _fullScreenTriangle);

                //Baked diffuse light, so probes in rooms show the room's bounce light like the main view does
                if (withIndirect && GameSettings.g_environmentmapping)
                    _deferredEnvironmentMapRenderModule.DrawCapture(_graphicsDevice, origin, _view, _fullScreenTriangle);

                GameSettings.g_VolumetricLights = volumeEnabled;

                //We don't use temporal AA obviously for the cubemap
                bool tempAa = GameSettings.g_taa;
                GameSettings.g_taa = false;
                
                //Shaders.DeferredCompose.CurrentTechnique = Shaders.DeferredComposeTechnique_NonLinear;
                //The froxel fog belongs to the main camera's frustum, not to this face
                Compose(withFog: false);
                //Shaders.DeferredCompose.CurrentTechnique = GameSettings.g_SSReflection
                //    ? Shaders.DeferredComposeTechnique_Linear
                //    : Shaders.DeferredComposeTechnique_NonLinear;
                GameSettings.g_taa = tempAa;
                DrawMapToScreenToCube(_renderTargetComposed, target, cubeMapFace);
            }

            //Change RTs back to normal
            SetUpRenderTargets(GameSettings.g_screenwidth, GameSettings.g_screenheight, true);

            //Our camera has changed we need to reinitialize stuff because we used a different camera in the cubemap render
            camera.HasChanged = true;
        }

                #endregion

                #region DEFERRED RENDERING FUNCTIONS
                /////////////////////////////////////////////////////////////////////////////////////////////////////
                //  DEFERRED RENDERING FUNCTIONS, IN ORDER OF USAGE
                ////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Reset our stat counting for this frame
        /// </summary>
        private void ResetStats()
        {
            GameStats.MaterialDraws = 0;
            GameStats.MeshDraws = 0;
            GameStats.LightsDrawn = 0;
            GameStats.shadowMaps = 0;
            GameStats.activeShadowMaps = 0;
            GameStats.EmissiveMeshDraws = 0;

            //Profiler
            if (GameSettings.d_profiler)
            {
                _performanceTimer.Restart();
                _performancePreviousTime = 0;
            }
            else if (_performanceTimer.IsRunning)
            {
                _performanceTimer.Stop();
            }
        }

        /// <summary>
        /// Check whether any GameSettings have changed that need setup
        /// </summary>
        /// <param name="dirLights"></param>
        private void CheckRenderChanges(List<DirectionalLight> dirLights)
        {
            if (Math.Abs(_g_FarClip - GameSettings.g_farplane) > 0.0001f)
            {
                _g_FarClip = GameSettings.g_farplane;
                _gBufferRenderModule.FarClip = _g_FarClip;
                _decalRenderModule.FarClip = _g_FarClip;
                _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameter_FarClip.SetValue(_g_FarClip);
                Shaders.BillboardEffectParameter_FarClip.SetValue(_g_FarClip);
                Shaders.ScreenSpaceReflectionParameter_FarClip.SetValue(_g_FarClip);
                Shaders.ReconstructDepthParameter_FarClip.SetValue(_g_FarClip);
            }

            if (_g_SSReflectionNoise != GameSettings.g_SSReflectionNoise)
            {
                _g_SSReflectionNoise = GameSettings.g_SSReflectionNoise;
                if (!_g_SSReflectionNoise) Shaders.ScreenSpaceReflectionParameter_Time.SetValue(0.0f);
            }
            
            //if (_hologramDraw != GameSettings.g_HologramDraw)
            //{
            //    _hologramDraw = GameSettings.g_HologramDraw;

            //    if (!_hologramDraw)
            //    {
            //        _graphicsDevice.SetRenderTarget(_renderTargetHologram);
            //        _graphicsDevice.Clear(Color.Black);
            //    }
            //}

            if (_forceShadowFiltering != GameSettings.g_shadowforcefiltering)
            {
                _forceShadowFiltering = GameSettings.g_shadowforcefiltering;

                for (var index = 0; index < dirLights.Count; index++)
                {
                    DirectionalLight light = dirLights[index];
                    if (light.ShadowMap != null) light.ShadowMap.Dispose();
                    light.ShadowMap = null;

                    light.ShadowFiltering = (DirectionalLight.ShadowFilteringTypes) (_forceShadowFiltering - 1);

                    light.HasChanged = true;
                }
            }

            if (_ssr != GameSettings.g_SSReflection)
            {
                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectReflection);
                _graphicsDevice.Clear(Color.Transparent);

                _ssr = GameSettings.g_SSReflection;
            }

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileRenderChanges = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>
        /// Draw our shadow maps from the individual lights. Check if something has changed first, otherwise leave as it is
        /// </summary>
        /// <param name="meshMaterialLibrary"></param>
        /// <param name="entities"></param>
        /// <param name="pointLights"></param>
        /// <param name="dirLights"></param>
        /// <param name="camera"></param>
        private void DrawShadowMaps(MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities, List<PointLight> pointLights, List<DirectionalLight> dirLights, Camera camera)
        {
            //Don't render for the first frame, we need a guideline first
            if (_boundingFrustum == null) UpdateViewProjection(camera, meshMaterialLibrary, entities);

            _shadowMapRenderModule.Draw(_graphicsDevice, meshMaterialLibrary, entities, pointLights, dirLights, camera);
            _lightAccumulationModule.PointLightRenderModule.ShadowAtlas = _shadowMapRenderModule.Atlas?.Target;
            _froxelRenderModule.ShadowAtlas = _shadowMapRenderModule.Atlas?.Target;

            //Cascade distances of the first shadowed sun, for RenderModes.ShadowCascades
            _cascadeCount = 0;
            foreach (DirectionalLight light in dirLights)
                if (light.IsEnabled && light.CastShadows && light.ActiveCascades > 0)
                {
                    _cascadeEnds = light.CascadeEnds;
                    _cascadeCount = light.ActiveCascades;
                    break;
                }

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawShadows = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }
        
        /// <summary>
        /// Create the projection matrices
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="meshMaterialLibrary"></param>
        /// <param name="entities"></param>
        private void UpdateViewProjection(Camera camera, MeshMaterialLibrary meshMaterialLibrary, List<BasicEntity> entities)
        {
            _viewProjectionHasChanged = camera.HasChanged;

            //alternate frames with temporal aa
            if (GameSettings.g_taa)
            {
                _viewProjectionHasChanged = true;
                _temporalAAOffFrame = !_temporalAAOffFrame;
            }

            //If the camera didn't do anything we don't need to update this stuff
            if (_viewProjectionHasChanged)
            {
                //We have processed the change, now setup for next frame as false
                camera.HasChanged = false;
                camera.HasMoved = false;

                //View matrix
                _view = Matrix.CreateLookAt(camera.Position, camera.Lookat, camera.Up);
                _inverseView = Matrix.Invert(_view);

                _viewIT = Matrix.Transpose(_inverseView);

                _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameter_InverseView.SetValue(_inverseView);

                _projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView,
                    GameSettings.g_screenwidth / (float)GameSettings.g_screenheight, 1, GameSettings.g_farplane);
                
                _gBufferRenderModule.Camera = camera.Position;

                _viewProjection = _view * _projection;

                //this is the unjittered viewProjection. For some effects we don't want the jittered one
                _staticViewProjection = _viewProjection;

                //Transformation for TAA - from current view back to the old view projection
                _currentViewToPreviousViewProjection = Matrix.Invert(_view) * _previousViewProjection;
                
                //Temporal AA
                if (GameSettings.g_taa)
                {
                    switch (GameSettings.g_taa_jittermode)
                    {
                        case 0: //2 frames, just basic translation. Worst taa implementation. Not good with the continous integration used
                        {
                            float translation = _temporalAAOffFrame ? 0.5f : -0.5f;
                            _viewProjection = _viewProjection *
                                              Matrix.CreateTranslation(new Vector3(translation / GameSettings.g_screenwidth,
                                                  translation / GameSettings.g_screenheight, 0));
                        }
                            break;
                        case 1: // Just random translation
                        {
                            float randomAngle = FastRand.NextAngle();
                            Vector3 translation = new Vector3((float)Math.Sin(randomAngle) / GameSettings.g_screenwidth, (float)Math.Cos(randomAngle) / GameSettings.g_screenheight, 0) * 0.5f;
                            _viewProjection = _viewProjection *
                                              Matrix.CreateTranslation(translation);

                        }
                            break;
                        case 2: // Halton sequence, default
                        {
                            Vector3 translation = GetHaltonSequence();
                            _viewProjection = _viewProjection *
                                              Matrix.CreateTranslation(translation);
                        }
                            break;
                    }
                }

                _previousViewProjection = _viewProjection;
                _inverseViewProjection = Matrix.Invert(_viewProjection);
                _inverseProjection = Matrix.Invert(_projection);

                if (_boundingFrustum == null) _boundingFrustum = new BoundingFrustum(_staticViewProjection);
                else _boundingFrustum.Matrix = _staticViewProjection;

                // Compute the frustum corners for cheap view direction computation in shaders
                ComputeFrustumCorners(_boundingFrustum, camera.Position);
            }

            //We need to update whether or not entities are in our boundingFrustum and then cull them or not!
            meshMaterialLibrary.FrustumCulling(entities, _boundingFrustum, _viewProjectionHasChanged, camera.Position);

            _lightAccumulationModule.UpdateViewProjection(_boundingFrustum, _viewProjectionHasChanged, _view, _inverseView, _viewIT, _projection, _viewProjection, _inverseViewProjection);

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileUpdateViewProjection = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>
        /// The halton sequence is a good way to create good distribution
        /// I use a 2,3 sequence
        /// https://en.wikipedia.org/wiki/Halton_sequence
        /// </summary>
        /// <returns></returns>
        private Vector3 GetHaltonSequence()
        {
            //First time? Create the sequence
            if (_haltonSequence == null)
            {
                _haltonSequence = new Vector3[HaltonSequenceLength];
                for (int index = 0; index < HaltonSequenceLength; index++)
                {
                    for (int baseValue = 2; baseValue <= 3; baseValue++)
                    {
                        float result = 0;
                        float f = 1;
                        int i = index + 1;

                        while (i > 0)
                        {
                            f = f / baseValue;
                            result = result + f * (i % baseValue);
                            i = i / baseValue; //floor / int()
                        }

                        if (baseValue == 2)
                            _haltonSequence[index].X = (result - 0.5f) * 2 * _inverseResolution.X;
                        else
                            _haltonSequence[index].Y = (result - 0.5f) * 2 * _inverseResolution.Y;
                    }
                }
            }
            _haltonSequenceIndex++;
            if (_haltonSequenceIndex >= HaltonSequenceLength) _haltonSequenceIndex = 0;
            return _haltonSequence[_haltonSequenceIndex];
        }

        /// <summary>
        /// From https://jcoluna.wordpress.com/2011/01/18/xna-4-0-light-pre-pass/
        /// Compute the frustum corners for a camera.
        /// Its used to reconstruct the pixel position using only the depth value.
        /// Read here for more information
        /// http://mynameismjp.wordpress.com/2009/03/10/reconstructing-position-from-depth/
        /// </summary>
        /// <param name="cameraFrustum"></param>
        private void ComputeFrustumCorners(BoundingFrustum cameraFrustum, Vector3 viewPosition)
        {
            cameraFrustum.GetCorners(_cornersWorldSpace);

            /*this part is used for volume projection*/
            //World Space Corners - Camera Position
            for (int i = 0; i < 4; i++) //take only the 4 farthest points
            {
                _currentFrustumCorners[i] = _cornersWorldSpace[i + 4] - viewPosition;
            }
            Vector3 temp = _currentFrustumCorners[3];
            _currentFrustumCorners[3] = _currentFrustumCorners[2];
            _currentFrustumCorners[2] = temp;

            _distanceFieldRenderModule.FrustumCornersWorldSpace = _currentFrustumCorners;
            _deferredEnvironmentMapRenderModule.FrustumCornersWS = _currentFrustumCorners;

            //View Space Corners
            //this is the inverse of our camera transform
            Vector3.Transform(_cornersWorldSpace, ref _view, _cornersViewSpace); //put the frustum into view space
            for (int i = 0; i < 4; i++) //take only the 4 farthest points
            {
                _currentFrustumCorners[i] = _cornersViewSpace[i + 4];
            }
            temp = _currentFrustumCorners[3];
            _currentFrustumCorners[3] = _currentFrustumCorners[2];
            _currentFrustumCorners[2] = temp;

            Shaders.ScreenSpaceReflectionParameter_FrustumCorners.SetValue(_currentFrustumCorners);
            Shaders.ScreenSpaceEffectParameter_FrustumCorners.SetValue(_currentFrustumCorners);
            _temporalAntialiasingRenderModule.FrustumCorners = _currentFrustumCorners;
            Shaders.ReconstructDepthParameter_FrustumCorners.SetValue(_currentFrustumCorners);
            Shaders.deferredDirectionalLightParameterFrustumCorners.SetValue(_currentFrustumCorners);
            _subsurfaceScatterRenderModule.FrustumCorners = _currentFrustumCorners;
        }
        
        /// <summary>
        /// Draw all our meshes to the GBuffer - albedo, normal, depth - for further computation
        /// </summary>
        /// <param name="meshMaterialLibrary"></param>
        private void DrawGBuffer(MeshMaterialLibrary meshMaterialLibrary)
        {
            _gBufferRenderModule.Draw(_graphicsDevice, _renderTargetBinding, meshMaterialLibrary, _viewProjection, _view);
            
            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawGBuffer = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>
        /// Draw deferred Decals
        /// </summary>
        /// <param name="decals"></param>
        /// <summary>
        /// The lightmap pass (Source 2 look, phase 4.2). Without lightmaps the environment and light passes are told to
        /// ignore its targets.
        /// </summary>
        private void DrawLightmaps(List<BasicEntity> entities)
        {
            PointLightRenderModule pointLights = _lightAccumulationModule.PointLightRenderModule;
            LightmapResources lightmaps = _lightingSettings?.LightmapsEnabled != false ? _lighting?.Lightmaps : null;
            if (lightmaps == null)
            {
                pointLights.ShadowMask = null;
                _deferredEnvironmentMapRenderModule.SetLightmaps(null, 1);
                return;
            }

            _lightmapRenderModule.Draw(_graphicsDevice, lightmaps, entities, _view, _viewProjection, _boundingFrustum, _g_FarClip,
                _renderTargetLightmap, _renderTargetShadowMask);
            GameStats.LightmappedMeshes = _lightmapRenderModule.DrawnInstances;

            pointLights.ShadowMask = _renderTargetShadowMask;
            pointLights.ShadowMaskChannel ??= light => _lighting?.ShadowMaskChannel(light) ?? Vector4.Zero;
            _lightAccumulationModule.DirectionalShadowMaskChannel ??= light => _lighting?.ShadowMaskChannel(light) ?? Vector4.Zero;
            _deferredEnvironmentMapRenderModule.SetLightmaps(_renderTargetLightmap,
                (_lightingSettings?.Intensity ?? 1f) * _environmentSky.AmbientScale);
        }

        private void DrawDecals(List<Decal> decals)
        {
            if (!GameSettings.g_drawdecals) return;
            
            //First copy albedo to decal offtarget
            DrawMapToScreenToFullScreen(_renderTargetAlbedo, BlendState.Opaque, _renderTargetDecalOffTarget);

            DrawMapToScreenToFullScreen(_renderTargetDecalOffTarget, BlendState.Opaque, _renderTargetAlbedo);
            
            _decalRenderModule.Draw(_graphicsDevice, decals, _view, _viewProjection, _inverseView);
        }
        
        /// <summary>
        /// Draw Screen Space Reflections
        /// </summary>
        /// <param name="gameTime"></param>
        private void DrawScreenSpaceReflections(GameTime gameTime)
        {
            if (!GameSettings.g_SSReflection) return;


            //todo: more samples for more reflective materials!
            _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectReflection);
            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            if (GameSettings.g_taa)
            {
                    Shaders.ScreenSpaceReflectionParameter_TargetMap.SetValue(_temporalAAOffFrame ? _renderTargetTAA_1 : _renderTargetTAA_2);
            }
            else
            {
            Shaders.ScreenSpaceReflectionParameter_TargetMap.SetValue(_renderTargetComposed);
            }

            if (GameSettings.g_SSReflectionNoise)
                Shaders.ScreenSpaceReflectionParameter_Time.SetValue((float)gameTime.TotalGameTime.TotalSeconds % 1000);

            Shaders.ScreenSpaceReflectionParameter_Projection.SetValue(_projection);
            
            Shaders.ScreenSpaceReflectionEffect.CurrentTechnique.Passes[0].Apply();
            _fullScreenTriangle.Draw(_graphicsDevice);

            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawSSR = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }

        }

        /// <summary>
        /// Draw SSAO to a different rendertarget
        /// </summary>
        /// <param name="camera"></param>
        private void DrawScreenSpaceAmbientOcclusion(Camera camera)
        {
            if (!GameSettings.g_ssao_draw) return;

            _graphicsDevice.SetRenderTarget(_renderTargetSSAOEffect);

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            Shaders.ScreenSpaceEffectParameter_InverseViewProjection.SetValue(_inverseViewProjection);
            Shaders.ScreenSpaceEffectParameter_Projection.SetValue(_projection);
            Shaders.ScreenSpaceEffectParameter_ViewProjection.SetValue(_viewProjection);
            Shaders.ScreenSpaceEffectParameter_CameraPosition.SetValue(camera.Position);

            Shaders.ScreenSpaceEffect.CurrentTechnique = Shaders.ScreenSpaceEffectTechnique_SSAO;
            Shaders.ScreenSpaceEffect.CurrentTechnique.Passes[0].Apply();
            _fullScreenTriangle.Draw(_graphicsDevice);
            
            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawScreenSpaceEffect = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>
        /// Bilateral blur, to upsample our undersampled SSAO
        /// </summary>
        private void DrawBilateralBlur()
        {
            _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectUpsampleBlurVertical);

            _spriteBatch.Begin(0, BlendState.Additive);

            _spriteBatch.Draw(_renderTargetSSAOEffect, new Rectangle(0, 0, GameSettings.g_screenwidth, GameSettings.g_screenheight), Color.Red);

            _spriteBatch.End();

            if (GameSettings.g_ssao_blur &&  GameSettings.g_ssao_draw)
            {
                _graphicsDevice.RasterizerState = RasterizerState.CullNone;

                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectUpsampleBlurHorizontal);

                Shaders.ScreenSpaceEffectParameter_InverseResolution.SetValue(new Vector2(1.0f / _renderTargetScreenSpaceEffectUpsampleBlurVertical.Width,
                    1.0f / _renderTargetScreenSpaceEffectUpsampleBlurVertical.Height) * 2);
                Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetScreenSpaceEffectUpsampleBlurVertical);
                Shaders.ScreenSpaceEffectTechnique_BlurVertical.Passes[0].Apply();

                _fullScreenTriangle.Draw(_graphicsDevice);

                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectBlurFinal);

                Shaders.ScreenSpaceEffectParameter_InverseResolution.SetValue(new Vector2(1.0f / _renderTargetScreenSpaceEffectUpsampleBlurHorizontal.Width,
                    1.0f / _renderTargetScreenSpaceEffectUpsampleBlurHorizontal.Height)*0.5f);
                Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetScreenSpaceEffectUpsampleBlurHorizontal);
                Shaders.ScreenSpaceEffectTechnique_BlurHorizontal.Passes[0].Apply();

                _fullScreenTriangle.Draw(_graphicsDevice);

            }
            else
            {
                _graphicsDevice.SetRenderTarget(_renderTargetScreenSpaceEffectBlurFinal);

                _spriteBatch.Begin(0, BlendState.Opaque, SamplerState.LinearClamp);

                _spriteBatch.Draw(_renderTargetScreenSpaceEffectUpsampleBlurVertical, new Rectangle(0, 0, _renderTargetScreenSpaceEffectBlurFinal.Width, _renderTargetScreenSpaceEffectBlurFinal.Height), Color.White);

                _spriteBatch.End();
            }

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawBilateralBlur = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }
        
        /// <summary>
        /// Apply our environment cubemap to the renderer
        /// </summary>
        private void DrawEnvironmentMap(EnvironmentSample envSample, Camera camera, GameTime gameTime)
        {
            if (!GameSettings.g_environmentmapping) return;

            //Baked probe volume replaces the cubemap's diffuse ambient where it has data
            bool useProbes = _lighting?.GpuData != null && _lighting.ProbeSHR != null &&
                             _lightingSettings != null && _lightingSettings.ProbeVolumeEnabled;
            _deferredEnvironmentMapRenderModule.SetProbeVolume(useProbes ? _lighting : null,
                (_lightingSettings?.Intensity ?? 1f) * _environmentSky.AmbientScale, envSample.Position);

            //Soft contact shadows of dynamic objects (AO Proxy components), indirect light only
            _aoProxies.Collect(_frameEntities, camera.Position);
            _deferredEnvironmentMapRenderModule.SetAoProxies(_aoProxies);

            //Local reflection probes seen by this view, normalised against the baked light at their capture points
            _reflectionProbes.SelectVisible(_boundingFrustum, camera.Position);
            _deferredEnvironmentMapRenderModule.SetLocalProbes(_reflectionProbes.Visible, useProbes ? _lighting?.GpuData : null);

            //Indirect light goes on top of the lights (additive) and into its own debug target
            _graphicsDevice.SetRenderTarget(_renderTargetIndirect);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTargets(_renderTargetEnvironmentBinding);
            _deferredEnvironmentMapRenderModule.UseSSAO = GameSettings.g_ssao_draw;

            _deferredEnvironmentMapRenderModule.DrawEnvironmentMap(_graphicsDevice, camera, _view, _fullScreenTriangle, envSample, gameTime, GameSettings.g_SSReflection_FireflyReduction, GameSettings.g_SSReflection_FireflyThreshold);

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawEnvironmentMap = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>
        /// Emissive materials have some screen space lighting properties
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="meshMatLib"></param>
        /// <param name="gameTime"></param>
        private void DrawEmissiveEffect(Camera camera, MeshMaterialLibrary meshMatLib, GameTime gameTime)
        {
            //if (!GameSettings.g_EmissiveDraw) return;

            throw new NotImplementedException("Check an older build, emissives are currently not implemented because I switched from World Space to View Space but did not update all the effects yet");

            //Make a new _viewProjection
            //This should actually scale dynamically with the position of the object
            //Note: It would be better if the screen extended the same distance in each direction, right now it would probably be wider than tall

            //Matrix newProjection = Matrix.CreatePerspectiveFieldOfView(Math.Min((float)Math.PI, camera.FieldOfView * GameSettings.g_EmissiveDrawFOVFactor),
            //        GameSettings.g_screenwidth / (float)GameSettings.g_screenheight, 1, GameSettings.g_farplane);

            //Matrix transformedViewProjection = _view * newProjection;

            ////meshMatLib.DrawEmissive(_graphicsDevice, camera, _viewProjection, transformedViewProjection, _inverseViewProjection, _renderTargetEmissive, _renderTargetDiffuse, _renderTargetSpecular, _lightBlendState, _assets.Sphere.Meshes, gameTime);
            ////Performance Profiler
            //if (GameSettings.d_profiler)
            //{
            //    long performanceCurrentTime = _performanceTimer.ElapsedTicks;
            //    GameStats.d_profileDrawEmissive = performanceCurrentTime - _performancePreviousTime;

            //    _performancePreviousTime = performanceCurrentTime;
            //}
        }

        /// <summary>
        /// Volumetric fog atlases for this frame. Their history is dropped on a scene swap and while the froxels are off,
        /// the module drops it itself for camera cuts, projection, fog setting and baked probe changes.
        /// </summary>
        private void DrawFroxelFog(Camera camera, List<BasicEntity> entities, List<PointLight> pointLights,
            List<DirectionalLight> directionalLights, Scene scene, GameTime gameTime)
        {
            if (!ReferenceEquals(scene, _froxelScene))
            {
                _froxelScene = scene;
                _froxelRenderModule.InvalidateHistory();
            }
            if (!GameSettings.g_FroxelsEnabled)
            {
                _froxelRenderModule.InvalidateHistory();
                return;
            }

            _froxelRenderModule.UpdateMatrices(
                _view,
                _projection,
                _inverseView,
                _inverseProjection,
                FroxelRenderModule.CameraNearPlane,
                _g_FarClip);
            _froxelRenderModule.SetFogVolumes(entities);

            //Baked bounce light in the fog: the same probe volume and intensity the surfaces use
            bool useProbes = _lighting?.GpuData != null && _lighting.ProbeSHR != null &&
                             _lightingSettings != null && _lightingSettings.ProbeVolumeEnabled;
            _froxelRenderModule.SetProbeVolume(useProbes ? _lighting : null,
                (_lightingSettings?.Intensity ?? 1f) * _environmentSky.AmbientScale);

            _froxelRenderModule.RenderFroxelFog(
                pointLights,
                directionalLights,
                camera.Position,
                GameSettings.g_screenwidth,
                GameSettings.g_screenheight,
                (float)(gameTime.TotalGameTime.TotalSeconds % 3600));
        }

        /// <summary>The froxel fog lookup of Common/FroxelFog.fx on any effect (the forward pass fogs transparents too).</summary>
        private void ApplyFroxelFogParameters(EffectParameterCollection p, string useParameter)
        {
            Texture2D froxelAtlas = _froxelRenderModule?.FroxelAccumulation;
            bool fogActive = GameSettings.g_FroxelsEnabled && GameSettings.g_FroxelFogEnabled && froxelAtlas != null;
            p[useParameter]?.SetValue(fogActive);
            if (!fogActive) return;
            p["FroxelAccumulationTexture"]?.SetValue(froxelAtlas);
            p["FroxelInjectionTexture"]?.SetValue(_froxelRenderModule.FroxelMedium);
            p["InverseProjection"]?.SetValue(_inverseProjection);
            p["NearClip"]?.SetValue(FroxelRenderModule.CameraNearPlane);
            p["FarClip"]?.SetValue(_g_FarClip);
            p["GridDimensions"]?.SetValue(new Vector3(FroxelRenderModule.FROXEL_GRID_X, FroxelRenderModule.FROXEL_GRID_Y, FroxelRenderModule.FROXEL_GRID_Z));
            p["ScreenResolution"]?.SetValue(new Vector2(GameSettings.g_screenwidth, GameSettings.g_screenheight));
        }

        private void ApplyDeferredComposeFroxelParameters(bool withFog)
        {
            Texture2D froxelAtlas = _froxelRenderModule?.FroxelAccumulation;
            bool fogActive =
                withFog &&
                GameSettings.g_FroxelsEnabled &&
                GameSettings.g_FroxelFogEnabled &&
                froxelAtlas != null;

            Shaders.DeferredComposeEffectParameter_FroxelAccumulationTexture?.SetValue(
                fogActive ? froxelAtlas : _renderTargetAlbedo);
            Shaders.DeferredComposeEffectParameter_FroxelInjectionTexture?.SetValue(
                fogActive ? _froxelRenderModule.FroxelMedium : _renderTargetAlbedo);
            Shaders.DeferredComposeEffectParameter_InverseProjection?.SetValue(_inverseProjection);

            Shaders.DeferredComposeEffectParameter_DepthMap?.SetValue(_renderTargetDepth);
            Shaders.DeferredComposeEffectParameter_NearClip?.SetValue(FroxelRenderModule.CameraNearPlane);
            Shaders.DeferredComposeEffectParameter_FarClip?.SetValue(_g_FarClip);
            Shaders.DeferredComposeEffectParameter_GridDimensions?.SetValue(
                new Vector3(
                    FroxelRenderModule.FROXEL_GRID_X,
                    FroxelRenderModule.FROXEL_GRID_Y,
                    FroxelRenderModule.FROXEL_GRID_Z));
            Shaders.DeferredComposeEffectParameter_ScreenResolution?.SetValue(
                new Vector2(GameSettings.g_screenwidth, GameSettings.g_screenheight));

            Shaders.DeferredComposeEffectParameter_UseFroxelFog?.SetValue(fogActive);
            Shaders.DeferredComposeEffectParameter_SkyFogStrength?.SetValue(GameSettings.g_FroxelSkyFogStrength);
            Shaders.DeferredComposeEffectParameter_SkyShaftDistance?.SetValue(GameSettings.g_FroxelSkyShaftDistance);
        }

        /// <summary>
        /// Compose the render by combining the albedo channel with the light channels
        /// </summary>
        private RenderTarget2D Compose(bool withFog = true)
        {
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            _graphicsDevice.SetRenderTarget(_renderTargetComposed);
            _graphicsDevice.BlendState = BlendState.Opaque;

            ApplyDeferredComposeFroxelParameters(withFog);

            //combine!
            Shaders.DeferredCompose.CurrentTechnique.Passes[0].Apply();
            _fullScreenTriangle.Draw(_graphicsDevice);

            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileCompose = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }

            return _renderTargetComposed;
        }

        /// <summary>
        /// Not working right now
        /// </summary>
        /// <param name="input"></param>
        /// <param name="meshMaterialLibrary"></param>
        /// <returns></returns>
        private RenderTarget2D DrawSubsurfaceScattering(RenderTarget2D input, MeshMaterialLibrary meshMaterialLibrary)
        {
            return _subsurfaceScatterRenderModule.Draw(_graphicsDevice, input, input, meshMaterialLibrary, _viewProjection );
        }
        
        private void ReconstructDepth()
        {
            if (_viewProjectionHasChanged)
                Shaders.ReconstructDepthParameter_Projection.SetValue(_projection);

            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            Shaders.ReconstructDepth.CurrentTechnique.Passes[0].Apply();
            _fullScreenTriangle.Draw(_graphicsDevice);
        }

        private RenderTarget2D DrawForward(RenderTarget2D input, MeshMaterialLibrary meshMaterialLibrary, Camera camera, List<PointLight> pointLights)
        {
            if (!GameSettings.g_forwardenable) return input;

            _graphicsDevice.SetRenderTarget(input);
            ReconstructDepth();

            return _forwardRenderModule.Draw(_graphicsDevice, input, meshMaterialLibrary, _viewProjection, ForwardContext(camera, pointLights));
        }

        /// <summary>
        /// Mirror images of the Planar Reflection panes for the forward pass. Drawn before Compose: binding another
        /// target afterwards would discard the composed image.
        /// </summary>
        private void DrawPlanarReflections(MeshMaterialLibrary meshMaterialLibrary, Camera camera, List<PointLight> pointLights)
        {
            if (!GameSettings.g_forwardenable) return;
            _planarReflections.Draw(_graphicsDevice, _fullScreenTriangle, _frameEntities, meshMaterialLibrary, _forwardRenderModule, ForwardContext(camera, pointLights),
                _viewProjection, _boundingFrustum, GameSettings.g_screenwidth, GameSettings.g_screenheight);
        }

        /// <summary>What the forward pass (and the planar reflections drawn with it) reads from the frame.</summary>
        private ForwardRenderModule.Context ForwardContext(Camera camera, List<PointLight> pointLights)
        {
            bool useProbes = _lighting?.GpuData != null && _lighting.ProbeData != null &&
                             _lightingSettings != null && _lightingSettings.ProbeVolumeEnabled;
            var context = new ForwardRenderModule.Context
            {
                Camera = camera,
                Frustum = _boundingFrustum,
                PointLights = pointLights,
                DirectionalLights = _frameDirectionalLights,
                ShadowAtlas = _shadowMapRenderModule.Atlas?.Target,
                GlobalProbe = _prefilteredGlobal,
                BrdfLut = _environmentPrefilter.BrdfLut(_graphicsDevice),
                LocalProbes = _reflectionProbes.Visible,
                Lighting = useProbes ? _lighting : null,
                LightingIntensity = (_lightingSettings?.Intensity ?? 1f) * _environmentSky.AmbientScale,
                GlobalCapturePosition = _frameEnvSample?.Position ?? Vector3.Zero,
                AmbientStrength = (_frameEnvSample?.DiffuseStrength ?? 0.2f) * 5,
                ApplyFog = p => ApplyFroxelFogParameters(p, "UseFog"),
                Resolution = new Vector2(GameSettings.g_screenwidth, GameSettings.g_screenheight),
                SkyCube = _deferredEnvironmentMapRenderModule.BackgroundSkyCube,
                SkyMap = _deferredEnvironmentMapRenderModule.BackgroundSkyMap,
            };
            return context;
        }

        /// <summary>
        /// The user render features of one injection point (Anvil > Render Pipeline), see <see cref="RenderFeatures"/>.
        /// </summary>
        private RenderTarget2D DrawRenderFeatures(RenderFeatureEvent at, RenderTarget2D image, bool inPlace)
        {
            if (!RenderFeatureRenderModule.HasActive(at)) return image;
            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Render Features: " + at);
            try
            {
                return _renderFeatureRenderModule.Draw(_graphicsDevice, at, image, _renderFeatureContext, _fullScreenTriangle, inPlace);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }
        }

        private RenderTarget2D DrawBloom(RenderTarget2D input)
        {
            if (GameSettings.g_BloomEnable && GameSettings.g_BloomScale > 0)
            {
                //Bloom Start is in exposed units: the exposure the tonemapper applies (last frame's auto exposure)
                _bloomFilter.SetExposure(Shaders.PostProcessingParameter_PowExposure.GetValueSingle(),
                    GameSettings.g_AutoExposure ? _autoExposureFilter.CurrentExposure : null);
                Texture2D bloom = _bloomFilter.Draw(input, GameSettings.g_screenwidth,
                    GameSettings.g_screenheight);
                
                _graphicsDevice.SetRenderTargets(_renderTargetBloom);

                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

                _spriteBatch.Draw(input,
                    new Rectangle(0, 0, GameSettings.g_screenwidth, GameSettings.g_screenheight), Color.White);
                _spriteBatch.Draw(bloom, new Rectangle(0, 0, GameSettings.g_screenwidth, GameSettings.g_screenheight),
                    new Color(new Vector3(MathHelper.Clamp(GameSettings.g_BloomScale, 0, 1))));

                _spriteBatch.End();

                return _renderTargetBloom;
            }
            else
            {

                //_graphicsDevice.SetRenderTarget(_renderTargetBloom);
                //_spriteBatch.Begin(0, BlendState.Opaque, _supersampling > 1 ? SamplerState.LinearWrap : SamplerState.PointClamp);
                //_spriteBatch.Draw(_renderTargetComposed, new Rectangle(0, 0, GameSettings.g_screenwidth, GameSettings.g_screenheight), Color.White);
                //_spriteBatch.End();
                return input;
            }
        }

        /// <summary>
        /// The scene owns its volumetric fog (Anvil > Environment > Volumetric Fog); the froxel and compose passes
        /// read the GameSettings copies, so scripts change fog through ScriptBehaviour.SceneEnvironment.
        /// </summary>
        public static void ApplySceneFog(EnvironmentSettings fog)
        {
            if (fog == null) return;
            GameSettings.g_FroxelDensity = fog.FogDensity * GameSettings.g_FogDensityScale;
            GameSettings.g_FroxelAbsorption = fog.FogAbsorption;
            GameSettings.g_FroxelDirectionalScatter = fog.FogSunScatter;
            GameSettings.g_FroxelPointScatter = fog.FogLightScatter;
            GameSettings.g_FroxelIndirectScatter = fog.FogIndirectScatter;
            GameSettings.g_FroxelAnisotropy = fog.FogAnisotropy;
            GameSettings.g_FroxelFogDistanceStart = fog.FogStartDistance;
            GameSettings.g_FroxelFogDistanceFull = fog.FogFullDistance;
            GameSettings.g_FroxelNearDensity = fog.FogNearDensity;
            GameSettings.g_FroxelSkyFogStrength = fog.FogSkyStrength;
            GameSettings.g_FroxelSkyShaftDistance = fog.FogSkyShaftDistance;
        }

        /// <summary>
        /// The scene's post processing (Anvil > Post Processing, EnvironmentSettings.Post, blended with Post Process
        /// Volumes): copied into GameSettings, which the post passes read. Scripts change it through
        /// ScriptBehaviour.SceneEnvironment.Post.
        /// </summary>
        public static void ApplyScenePost(PostProcessSettings post)
        {
            if (post == null) return;
            GameSettings.g_TonemapCurve = post.Tonemap;
            GameSettings.Exposure = post.Exposure;
            GameSettings.WhitePoint = post.WhitePoint;
            GameSettings.SCurveStrength = post.SCurveStrength;
            GameSettings.g_FilmicCurve = new Vector4(post.FilmicShoulder, post.FilmicLinearStrength, post.FilmicLinearAngle, post.FilmicToeStrength);
            GameSettings.g_FilmicToe = new Vector3(post.FilmicToeNumerator, post.FilmicToeDenominator, post.FilmicWhitePoint);
            GameSettings.g_ExactSrgb = post.ExactSrgb;
            GameSettings.g_ColorGrading = post.ColorGrading;
            GameSettings.g_LutPath = post.LutPath;
            GameSettings.g_LutBlendPath = post.BlendLutPath;
            GameSettings.g_LutBlend = post.LutBlend;
            GameSettings.g_Saturation = post.Saturation;
            GameSettings.g_Contrast = post.Contrast;
            GameSettings.g_ColorFilter = post.ColorFilter.ToVector3().Pow(2.2f);
            GameSettings.g_Vignette = post.Vignette;
            if (GameSettings.ChromaticAbberationStrength != post.ChromaticAberration)
                GameSettings.ChromaticAbberationStrength = post.ChromaticAberration;
            GameSettings.g_AutoExposure = post.AutoExposure;
            GameSettings.g_ExposureMetering = post.Metering;
            GameSettings.g_AutoExposureKey = post.AutoExposureKey;
            GameSettings.g_BrightPixelPercent = post.BrightPixelPercent;
            GameSettings.g_BrightPixelTarget = post.BrightPixelTarget;
            GameSettings.g_MinAverageLuminance = post.MinAverageLuminance;
            GameSettings.g_AutoExposureMin = post.AutoExposureMin;
            GameSettings.g_AutoExposureMax = post.AutoExposureMax;
            GameSettings.g_AutoExposureSpeedDarkToLight = post.AdaptationSpeedUp;
            GameSettings.g_AutoExposureSpeedLightToDark = post.AdaptationSpeedDown;
            GameSettings.g_AutoExposureCenterWeight = post.CenterWeight;
            GameSettings.g_BloomEnable = post.Bloom;
            GameSettings.g_BloomShape = post.BloomShape;
            GameSettings.g_BloomStart = post.BloomStart;
            GameSettings.g_BloomKnee = post.BloomKnee;
            GameSettings.g_BloomScale = post.BloomScale;
            GameSettings.g_FogDensityScale = post.FogDensityScale;
            GameSettings.g_RetinaBurn = post.RetinaBurn;
        }

        //Colour lookup textures of post profiles (EnvironmentSettings.Post.LutPath), by content path; null = failed
        private readonly Dictionary<string, Texture2D> _luts = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        //The two LUTs of a volume blend combined into one (Source 2 look, phase 5.2)
        private RenderTarget2D _combinedLut;

        private Texture2D CurrentLut()
        {
            Texture2D lut = LoadLut(GameSettings.g_LutPath);
            if (GameSettings.g_LutBlendPath == null || GameSettings.g_LutBlend <= 0.001f) return lut;
            Texture2D second = LoadLut(GameSettings.g_LutBlendPath);
            if (second == lut || second.Width != lut.Width || second.Height != lut.Height) return GameSettings.g_LutBlend < 0.5f ? lut : second;
            if (_combinedLut == null || _combinedLut.Width != lut.Width || _combinedLut.Height != lut.Height)
            {
                _combinedLut?.Dispose();
            _retinaBurnEffect?.Dispose();
            _occlusionCulling.Dispose();
            _retinaBurnA?.Dispose();
            _retinaBurnB?.Dispose();
                _combinedLut = new RenderTarget2D(_graphicsDevice, lut.Width, lut.Height, false, SurfaceFormat.Color, DepthFormat.None);
            }
            _colorGradingFilter.Combine(_graphicsDevice, lut, second, GameSettings.g_LutBlend, _combinedLut, _fullScreenTriangle);
            return _combinedLut;
        }

        private Texture2D LoadLut(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return _lut;
            path = path.Replace('\\', '/');
            int dot = path.LastIndexOf('.');
            if (dot > path.LastIndexOf('/')) path = path.Substring(0, dot);
            if (!_luts.TryGetValue(path, out Texture2D lut))
            {
                try { lut = Globals.content.Load<Texture2D>(path); }
                catch (Exception e) { EditorBridge.Log("Post processing: can't load LUT '" + path + "': " + e.Message); lut = null; }
                _luts[path] = lut;
            }
            return lut ?? _lut;
        }

        /// <summary>
        /// Retina burn (Source 2 look, phase 5.3): what is far brighter than white leaves an afterimage that fades over
        /// a second or two. Off (and cleared) while Post Processing > Retina Burn is 0.
        /// </summary>
        private void UpdateRetinaBurn(RenderTarget2D input, GameTime gameTime)
        {
            float strength = GameSettings.g_RetinaBurn;
            Shaders.PostProcessing.Parameters["RetinaBurnStrength"]?.SetValue(strength);
            if (strength <= 0)
            {
                _retinaBurnA?.Dispose(); _retinaBurnA = null;
                _retinaBurnB?.Dispose(); _retinaBurnB = null;
                return;
            }
            int w = Math.Max(GameSettings.g_screenwidth / 4, 1), h = Math.Max(GameSettings.g_screenheight / 4, 1);
            if (_retinaBurnA == null || _retinaBurnA.Width != w || _retinaBurnA.Height != h)
            {
                _retinaBurnA?.Dispose(); _retinaBurnB?.Dispose();
                _retinaBurnA = new RenderTarget2D(_graphicsDevice, w, h, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                _retinaBurnB = new RenderTarget2D(_graphicsDevice, w, h, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                foreach (RenderTarget2D rt in new[] { _retinaBurnA, _retinaBurnB })
                {
                    _graphicsDevice.SetRenderTarget(rt);
                    _graphicsDevice.Clear(Color.Transparent);
                }
            }
            RenderTarget2D previous = _retinaBurnFlip ? _retinaBurnA : _retinaBurnB;
            RenderTarget2D output = _retinaBurnFlip ? _retinaBurnB : _retinaBurnA;
            _retinaBurnFlip = !_retinaBurnFlip;

            EffectParameterCollection p = _retinaBurnEffect.Parameters;
            p["FrameTexture"]?.SetValue(input);
            p["PreviousBurn"]?.SetValue(previous);
            bool auto = GameSettings.g_AutoExposure && _autoExposureFilter.CurrentExposure != null;
            p["AutoExposureEnabled"]?.SetValue(auto ? 1f : 0f);
            if (auto) p["AutoExposureTexture"]?.SetValue(_autoExposureFilter.CurrentExposure);
            p["PowExposure"]?.SetValue(Shaders.PostProcessingParameter_PowExposure.GetValueSingle());
            p["DeltaTime"]?.SetValue(GameFlow.Paused ? 0f : MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0, 0.1f));

            _graphicsDevice.SetRenderTarget(output);
            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.None;
            _graphicsDevice.RasterizerState = RasterizerState.CullNone;
            _retinaBurnEffect.Techniques["Burn"].Passes[0].Apply();
            _fullScreenTriangle.Draw(_graphicsDevice);
            p["PreviousBurn"]?.SetValue((Texture2D)null);

            Shaders.PostProcessing.Parameters["RetinaBurnTexture"]?.SetValue(output);
        }

        /// <summary>The adapted auto exposure in EV (reads the GPU value back: for tools and checks, not per frame).</summary>
        public float ReadAutoExposureEV()
        {
            Texture2D exposure = _autoExposureFilter?.CurrentExposure;
            if (exposure == null || !GameSettings.g_AutoExposure) return 0;
            var value = new float[1];
            exposure.GetData(value);
            return value[0];
        }

        /// <summary>
        /// Meter the HDR image and adapt the exposure that the post processing tonemapper applies
        /// </summary>
        private void UpdateAutoExposure(RenderTarget2D input, GameTime gameTime)
        {
            if (!GameSettings.g_AutoExposure)
            {
                // Snap to the current scene instead of fading from a stale value when re-enabled.
                _autoExposureFilter.Reset();
                Shaders.PostProcessingParameter_AutoExposureEnabled.SetValue(0f);
                return;
            }

            Texture2D exposure = _autoExposureFilter.Draw(_graphicsDevice, input, (float)gameTime.ElapsedGameTime.TotalSeconds);
            Shaders.PostProcessingParameter_AutoExposureTexture.SetValue(exposure);
            Shaders.PostProcessingParameter_AutoExposureEnabled.SetValue(1f);
        }

        /// <summary>
        /// Combine the render with previous frames to get more information per sample and make the image anti-aliased / super sampled
        /// </summary>
        private RenderTarget2D TonemapAndCombineTemporalAntialiasing(RenderTarget2D input)
        {
            if (!GameSettings.g_taa) return input;

            RenderTarget2D output = _temporalAAOffFrame ? _renderTargetTAA_2 : _renderTargetTAA_1;

            _temporalAntialiasingRenderModule.Draw(_graphicsDevice, 
                useTonemap: GameSettings.g_taa_tonemapped,
                currentFrame: input,
                previousFrames: _temporalAAOffFrame? _renderTargetTAA_1 : _renderTargetTAA_2,
                output: output,
                fullScreenTriangle: _fullScreenTriangle, 
                currentViewToPreviousViewProjection: _currentViewToPreviousViewProjection);
            
            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileCombineTemporalAntialiasing = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
            
            return GameSettings.g_taa_tonemapped ? input : output;
        }
        
        private void DrawSignedDistanceFieldFunctions(Camera camera)
        {
            if (!GameSettings.sdf_drawdistance) return;
            
            _distanceFieldRenderModule.Draw(_graphicsDevice, camera, _fullScreenTriangle);

            //_spriteBatch.Begin(0, BlendState.Opaque, SamplerState.PointClamp);

            //int height = Math.Max(volumeTexture.Texture.Height / volumeTexture.Texture.Width * GameSettings.g_screenheight, 40);
            //_spriteBatch.Draw(volumeTexture.Texture,
            //    new Rectangle(0, GameSettings.g_screenheight - height, GameSettings.g_screenwidth, height), Color.White);
            //_spriteBatch.End();
            
        }

        /// <summary>
        /// Draw the final rendered image, change the output based on user input to show individual buffers/rendertargets
        /// </summary>
        /// <param name="currentOutput"></param>
        /// <param name="editorData"></param>
        private void RenderMode(RenderTarget2D currentInput)
        {
            switch (GameSettings.g_rendermode)
            {
               case RenderModes.Albedo:
                    DrawMapToScreenToFullScreen(_renderTargetAlbedo);
                    break;
               case RenderModes.Normal:
                    DrawMapToScreenToFullScreen(_renderTargetNormal);
                    break;
               case RenderModes.Depth:
                    DrawMapToScreenToFullScreen(_renderTargetDepth);
                    break;
               case RenderModes.Diffuse:
                    DrawMapToScreenToFullScreen(_renderTargetDiffuse);
                    break;
               case RenderModes.Specular:
                    DrawMapToScreenToFullScreen(_renderTargetSpecular);
                    break;
                case RenderModes.Volumetric:
                    DrawMapToScreenToFullScreen(_froxelRenderModule.FroxelAccumulation ?? (Texture2D)_renderTargetVolume);
                    break;
                case RenderModes.SSAO:
                    DrawMapToScreenToFullScreen(_renderTargetSSAOEffect);
                    break;
                //case RenderModes.Hologram:
                //    DrawMapToScreenToFullScreen(_renderTargetHologram);
                //    break;
                case RenderModes.SSBlur:
                    DrawMapToScreenToFullScreen(_renderTargetScreenSpaceEffectBlurFinal);
                    break;
                //case RenderModes.Emissive:
                //    DrawMapToScreenToFullScreen(_renderTargetEmissive);
                //    break;
                case RenderModes.SSR:
                    DrawMapToScreenToFullScreen(_renderTargetScreenSpaceEffectReflection);
                    break;
                //case RenderModes.SubsurfaceScattering:
                //    DrawMapToScreenToFullScreen(_renderTargetSSS);
                //    break;
                case RenderModes.HDR:
                        DrawMapToScreenToFullScreen(currentInput);
                    break;
                case RenderModes.IndirectDiffuse:
                    DrawDebugChannel(_renderTargetIndirect, 0);
                    break;
                case RenderModes.AmbientOcclusion:
                    DrawDebugChannel(_renderTargetIndirect, 1);
                    break;
                case RenderModes.ShadowCascades:
                    DrawDebugChannel(_renderTargetComposed, 2);
                    break;
                case RenderModes.Lightmap:
                    DrawDebugChannel(_renderTargetLightmap, 0);
                    break;
                case RenderModes.ShadowMask:
                    DrawMapToScreenToFullScreen(_renderTargetShadowMask);
                    break;
                default:
                    DrawPostProcessing(currentInput);
                    break;
            }


            //Performance Profiler
            if (GameSettings.d_profiler)
            {
                long performanceCurrentTime = _performanceTimer.ElapsedTicks;
                GameStats.d_profileDrawFinalRender = performanceCurrentTime - _performancePreviousTime;

                _performancePreviousTime = performanceCurrentTime;
            }
        }

        /// <summary>
        /// Add some post processing to the image
        /// </summary>
        /// <param name="currentInput"></param>
        private void DrawPostProcessing(RenderTarget2D currentInput)
        {
            if (!GameSettings.g_PostProcessing) return;

            //Drawn once per frame; the second RenderMode call shows the same image again
            if (_finalImage == null)
            {
                RenderTarget2D destinationRenderTarget;

                destinationRenderTarget = _renderTargetOutput;

                Shaders.PostProcessingParameter_ScreenTexture.SetValue(currentInput);
                EffectParameterCollection post = Shaders.PostProcessing.Parameters;
                post["TonemapCurve"]?.SetValue((float)(int)GameSettings.g_TonemapCurve);
                post["FilmicCurve"]?.SetValue(GameSettings.g_FilmicCurve);
                post["FilmicToe"]?.SetValue(GameSettings.g_FilmicToe);
                post["ExactSrgb"]?.SetValue(GameSettings.g_ExactSrgb ? 1f : 0f);
                post["Saturation"]?.SetValue(GameSettings.g_Saturation);
                post["Contrast"]?.SetValue(GameSettings.g_Contrast);
                post["ColorFilter"]?.SetValue(GameSettings.g_ColorFilter);
                post["VignetteStrength"]?.SetValue(GameSettings.g_Vignette);
                _graphicsDevice.SetRenderTarget(destinationRenderTarget);

                _graphicsDevice.DepthStencilState = DepthStencilState.Default;
                _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                Shaders.PostProcessing.CurrentTechnique.Passes[0].Apply();
                _fullScreenTriangle.Draw(_graphicsDevice);

                if(GameSettings.g_ColorGrading)
                destinationRenderTarget = _colorGradingFilter.Draw(_graphicsDevice, destinationRenderTarget, CurrentLut());

                //Without TAA: FXAA on the final image (Source 2 look, phase 1.6)
                if (!GameSettings.g_taa && GameSettings.g_fxaa)
                {
                    _graphicsDevice.SetRenderTarget(_renderTargetFxaa);
                    _graphicsDevice.BlendState = BlendState.Opaque;
                    _graphicsDevice.DepthStencilState = DepthStencilState.None;
                    _graphicsDevice.RasterizerState = RasterizerState.CullNone;
                    _fxaaEffect.Parameters["ScreenTexture"].SetValue(destinationRenderTarget);
                    _fxaaEffect.Parameters["InverseResolution"].SetValue(new Vector2(1f / destinationRenderTarget.Width, 1f / destinationRenderTarget.Height));
                    _fxaaEffect.Techniques["Fxaa"].Passes[0].Apply();
                    _fullScreenTriangle.Draw(_graphicsDevice);
                    destinationRenderTarget = _renderTargetFxaa;
                }

                //Render features: After Post Processing (final LDR image)
                _finalImage = DrawRenderFeatures(RenderFeatureEvent.AfterPostProcessing, destinationRenderTarget, inPlace: false);
            }

            DrawMapToScreenToFullScreen(_finalImage);
        }
        #endregion

        #endregion

            #region RENDERTARGET SETUP FUNCTIONS
            ////////////////////////////////////////////////////////////////////////////////////////////////////////
            //  RENDERTARGET SETUP FUNCTIONS
            ////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Update the resolution of our rendertargets
        /// </summary>
        public void UpdateResolution()
        {
            _inverseResolution = new Vector3(1.0f / GameSettings.g_screenwidth, 1.0f / GameSettings.g_screenheight, 0);
            _haltonSequence = null;

            SetUpRenderTargets(GameSettings.g_screenwidth, GameSettings.g_screenheight, false);
        }

        private void SetUpRenderTargets(int width, int height, bool onlyEssentials)
        {
            //Discard first
            if (_renderTargetAlbedo != null)
            {
                _renderTargetAlbedo.Dispose();
                _renderTargetDecalOffTarget.Dispose();
                _renderTargetVelocity.Dispose();
                _renderTargetFxaa.Dispose();
                _renderTargetDepth.Dispose();
                _renderTargetNormal.Dispose();
                _renderTargetComposed.Dispose();
                _renderTargetBloom.Dispose();
                _renderTargetDiffuse.Dispose();
                _renderTargetSpecular.Dispose();
                _renderTargetVolume.Dispose();
                _renderTargetIndirect.Dispose();
                _renderTargetLightmap.Dispose();
                _renderTargetShadowMask.Dispose();
                _renderTargetOutput.Dispose();
                //_renderTargetSSS.Dispose();

                _renderTargetScreenSpaceEffectUpsampleBlurVertical.Dispose();

                if (!onlyEssentials)
                {
                    //_renderTargetHologram.Dispose();
                    _renderTargetTAA_1.Dispose();
                    _renderTargetTAA_2.Dispose();
                    _renderTargetSSAOEffect.Dispose();
                    _renderTargetScreenSpaceEffectReflection.Dispose();

                    _renderTargetScreenSpaceEffectUpsampleBlurHorizontal.Dispose();
                    _renderTargetScreenSpaceEffectBlurFinal.Dispose();
                }
            }

            float ssmultiplier = _supersampling;

            int targetWidth = (int)(width * ssmultiplier);
            int targetHeight = (int)(height * ssmultiplier);

            Shaders.BillboardEffectParameter_AspectRatio.SetValue((float)targetWidth / targetHeight);

            _renderTargetAlbedo = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

            _renderTargetDecalOffTarget = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

            _renderTargetNormal = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetDepth = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            //_renderTargetSSS = new RenderTarget2D(_graphicsDevice, targetWidth,
            //    targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

            _renderTargetVelocity = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.HalfVector2, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetFxaa = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetBinding[0] = new RenderTargetBinding(_renderTargetAlbedo);
            _renderTargetBinding[1] = new RenderTargetBinding(_renderTargetNormal);
            _renderTargetBinding[2] = new RenderTargetBinding(_renderTargetDepth);
            _renderTargetBinding[3] = new RenderTargetBinding(_renderTargetVelocity);

            _renderTargetDiffuse = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);

            _renderTargetSpecular = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetVolume = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameterResolution.SetValue(new Vector2(targetWidth, targetHeight));

            _renderTargetLightBinding[0] = new RenderTargetBinding(_renderTargetDiffuse);
            _renderTargetLightBinding[1] = new RenderTargetBinding(_renderTargetSpecular);
            _renderTargetLightBinding[2] = new RenderTargetBinding(_renderTargetVolume);

            _renderTargetIndirect = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            _renderTargetEnvironmentBinding[0] = new RenderTargetBinding(_renderTargetDiffuse);
            _renderTargetEnvironmentBinding[1] = new RenderTargetBinding(_renderTargetSpecular);
            _renderTargetEnvironmentBinding[2] = new RenderTargetBinding(_renderTargetIndirect);

            //Preserved: they are cleared to different values, then bound together for the lightmap pass
            _renderTargetLightmap = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _renderTargetShadowMask = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

            _renderTargetComposed = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
            
            _renderTargetBloom = new RenderTarget2D(_graphicsDevice, targetWidth,
               targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            
            _renderTargetScreenSpaceEffectUpsampleBlurVertical = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _renderTargetOutput = new RenderTarget2D(_graphicsDevice, targetWidth,
                targetHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            if (!onlyEssentials)
            {
                _editorRender.SetUpRenderTarget(width, height);

                _renderTargetTAA_1 = new RenderTarget2D(_graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                _renderTargetTAA_2 = new RenderTarget2D(_graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

                _temporalAntialiasingRenderModule.Resolution = new Vector2(targetWidth, targetHeight);
                // Shaders.SSReflectionEffectParameter_Resolution.SetValue(new Vector2(target_width, target_height));
                Shaders.EmissiveEffectParameter_Resolution.SetValue(new Vector2(targetWidth, targetHeight));
                
                Shaders.ScreenSpaceReflectionParameter_Resolution.SetValue(new Vector2(targetWidth, targetHeight));
                _deferredEnvironmentMapRenderModule.Resolution = new Vector2(targetWidth, targetHeight);
                _renderTargetScreenSpaceEffectReflection = new RenderTarget2D(_graphicsDevice, targetWidth,
                    targetHeight, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);



                ///////////////////
                // HALF RESOLUTION

                targetWidth /= 2;
                targetHeight /= 2;

                _renderTargetScreenSpaceEffectUpsampleBlurHorizontal = new RenderTarget2D(_graphicsDevice, targetWidth,
                    targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

                _renderTargetScreenSpaceEffectBlurFinal = new RenderTarget2D(_graphicsDevice, targetWidth,
                    targetHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

                _renderTargetSSAOEffect = new RenderTarget2D(_graphicsDevice, targetWidth,
                    targetHeight, false, SurfaceFormat.HalfSingle, DepthFormat.None, 0,
                    RenderTargetUsage.DiscardContents);



                Shaders.ScreenSpaceEffectParameter_InverseResolution.SetValue(new Vector2(1.0f / targetWidth,
                    1.0f / targetHeight));


                Vector2 aspectRatio = new Vector2(Math.Min(1.0f, targetWidth / (float)targetHeight), Math.Min(1.0f, targetHeight / (float)targetWidth));
                
                Shaders.ScreenSpaceEffectParameter_AspectRatio.SetValue(aspectRatio);


                //_renderTargetHologram = new RenderTarget2D(_graphicsDevice, targetWidth,
                //    targetHeight, false, SurfaceFormat.Single, DepthFormat.Depth24, 0,
                //    RenderTargetUsage.PreserveContents);
            }

            UpdateRenderMapBindings(onlyEssentials);
        }

        private void UpdateRenderMapBindings(bool onlyEssentials)
        {
            Shaders.BillboardEffectParameter_DepthMap.SetValue(_renderTargetDepth);

            Shaders.ReconstructDepthParameter_DepthMap.SetValue(_renderTargetDepth);

            _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameter_AlbedoMap.SetValue(_renderTargetAlbedo);
            _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameter_DepthMap.SetValue(_renderTargetDepth);
            _lightAccumulationModule.PointLightRenderModule.deferredPointLightParameter_NormalMap.SetValue(_renderTargetNormal);

            Shaders.deferredDirectionalLightParameter_AlbedoMap.SetValue(_renderTargetAlbedo);
            Shaders.deferredDirectionalLightParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.deferredDirectionalLightParameter_NormalMap.SetValue(_renderTargetNormal);


            _deferredEnvironmentMapRenderModule.AlbedoMap = _renderTargetAlbedo;
            _deferredEnvironmentMapRenderModule.NormalMap = _renderTargetNormal;
            _deferredEnvironmentMapRenderModule.SSRMap = _renderTargetScreenSpaceEffectReflection;
            _deferredEnvironmentMapRenderModule.DepthMap = _renderTargetDepth;

            _decalRenderModule.DepthMap = _renderTargetDepth;

            _lightmapRenderModule.DepthMap = _renderTargetDepth;
            _lightmapRenderModule.NormalMap = _renderTargetNormal;

            _distanceFieldRenderModule.DepthMap = _renderTargetDepth;
            
            //_subsurfaceScatterRenderModule.NormalMap = _renderTargetNormal;
            //_subsurfaceScatterRenderModule.AlbedoMap = _renderTargetAlbedo;

            Shaders.DeferredComposeEffectParameter_ColorMap.SetValue(_renderTargetAlbedo);
            Shaders.DeferredComposeEffectParameter_NormalMap.SetValue(_renderTargetNormal);
            Shaders.DeferredComposeEffectParameter_diffuseLightMap.SetValue(_renderTargetDiffuse);
            Shaders.DeferredComposeEffectParameter_specularLightMap.SetValue(_renderTargetSpecular);
            // volumeLightMap is omitted from compiled DeferredCompose when unused in HLSL (parameter becomes null).
            Shaders.DeferredComposeEffectParameter_volumeLightMap?.SetValue(_renderTargetVolume);
            // SSAO darkens indirect light only: the environment pass reads it (Source 2 look, phase 1.2)
            _deferredEnvironmentMapRenderModule.SSAOMap = _renderTargetScreenSpaceEffectBlurFinal;
            //Shaders.DeferredComposeEffectParameter_HologramMap.SetValue(_renderTargetHologram);
            // Shaders.DeferredComposeEffectParameter_SSRMap.SetValue(_renderTargetScreenSpaceEffectReflection);

            Shaders.ScreenSpaceEffectParameter_NormalMap.SetValue(_renderTargetNormal);

            Shaders.ScreenSpaceEffectParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.ScreenSpaceEffectParameter_SSAOMap.SetValue(_renderTargetSSAOEffect);

            Shaders.ScreenSpaceReflectionParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.ScreenSpaceReflectionParameter_NormalMap.SetValue(_renderTargetNormal);
            //Shaders.ScreenSpaceReflectionParameter_TargetMap.SetValue(_renderTargetFinal);

            Shaders.EmissiveEffectParameter_DepthMap.SetValue(_renderTargetDepth);
            Shaders.EmissiveEffectParameter_NormalMap.SetValue(_renderTargetNormal);

            _temporalAntialiasingRenderModule.DepthMap = _renderTargetDepth;
            _temporalAntialiasingRenderModule.VelocityMap = _renderTargetVelocity;
        }

            #endregion

            #region HELPER FUNCTIONS
            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //  HELPER FUNCTIONS
            ////////////////////////////////////////////////////////////////////////////////////////////////////

        private void DrawMapToScreenToCube(RenderTarget2D map, RenderTargetCube target, CubeMapFace? face)
        {

            if (face != null) _graphicsDevice.SetRenderTarget(target, (CubeMapFace)face);
           // _graphicsDevice.Clear(Color.CornflowerBlue);
            _spriteBatch.Begin(0, BlendState.Opaque, SamplerState.PointClamp);
            _spriteBatch.Draw(map, new Rectangle(0, 0, map.Width, map.Height), Color.White);
            _spriteBatch.End();
        }

        /// <summary>
        /// Full-screen debug view: 0 = a target's colour (tonemapped), 1 = its alpha, 2 = the lit image tinted by sun shadow cascade.
        /// </summary>
        private void DrawDebugChannel(Texture2D map, int view)
        {
            _graphicsDevice.SetRenderTarget(null);
            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.None;
            _graphicsDevice.RasterizerState = RasterizerState.CullNone;
            _debugViewEffect.Parameters["InputTexture"]?.SetValue(map);
            _debugViewEffect.Parameters["DepthMap"]?.SetValue(_renderTargetDepth);
            _debugViewEffect.Parameters["FarClip"]?.SetValue(_g_FarClip);
            _debugViewEffect.Parameters["CascadeEnds"]?.SetValue(_cascadeEnds);
            _debugViewEffect.Parameters["CascadeCount"]?.SetValue((float)_cascadeCount);
            _debugViewEffect.Techniques[view == 0 ? "Color" : view == 1 ? "Alpha" : "Cascades"].Passes[0].Apply();
            _fullScreenTriangle.Draw(_graphicsDevice);
        }

        private void DrawMapToScreenToFullScreen(Texture2D map, BlendState blendState = null, RenderTarget2D output = null)
        {
            if(blendState == null) blendState = BlendState.Opaque;

            int height;
            int width;
            if (Math.Abs(map.Width / (float)map.Height - GameSettings.g_screenwidth / (float)GameSettings.g_screenheight) < 0.001)
            //If same aspectratio
            {
                height = GameSettings.g_screenheight;
                width = GameSettings.g_screenwidth;
            }
            else
            {
                if (GameSettings.g_screenheight < GameSettings.g_screenwidth)
                {
                    //Should be squared!
                    height = GameSettings.g_screenheight;
                    width = GameSettings.g_screenheight;
                }
                else
                {
                    height = GameSettings.g_screenwidth;
                    width = GameSettings.g_screenwidth;
                }
            }
            _graphicsDevice.SetRenderTarget(output);
            _spriteBatch.Begin(0, blendState, _supersampling>1 ? SamplerState.LinearWrap : SamplerState.PointClamp);
            _spriteBatch.Draw(map, new Rectangle(0, 0, width, height), Color.White);
            _spriteBatch.End();
        }
        
        private void CpuRayMarch(Camera camera)
        {
            if(GameSettings.e_CPURayMarch)

            if (Input.WasKeyPressed(Keys.K))
                _cpuRayMarch.Calculate(_renderTargetDepth, _renderTargetNormal, _projection, _inverseView, _inverseViewProjection,
                    camera, _currentFrustumCorners);

            _cpuRayMarch.Draw();
        }
        
        #endregion

        #endregion

        public void UnloadEnvironment() => _environmentSky.Dispose();

        public void Dispose()
        {
            _planarReflections.Dispose();
            _environmentSky.Dispose();
            _graphicsDevice?.Dispose();
            _spriteBatch?.Dispose();
            _gaussianBlur?.Dispose();
            _bloomFilter?.Dispose();
            _autoExposureFilter?.Dispose();
            _lightAccumulationModule?.Dispose();
            _froxelRenderModule?.Dispose();
            _gBufferRenderModule?.Dispose();
            _temporalAntialiasingRenderModule?.Dispose();
            _deferredEnvironmentMapRenderModule?.Dispose();
            _decalRenderModule?.Dispose();
            _waterRenderModule?.Dispose();
            _weatherRenderModule?.Dispose();
            _gridRenderModule?.Dispose();
            _particleRenderModule?.Dispose();
            _renderFeatureRenderModule?.Dispose();
            _debugViewEffect?.Dispose();
            _combinedLut?.Dispose();
            _assets?.Dispose();
            _renderTargetAlbedo?.Dispose();
            _renderTargetDepth?.Dispose();
            _renderTargetNormal?.Dispose();
            _renderTargetDecalOffTarget?.Dispose();
            _renderTargetVelocity?.Dispose();
            _renderTargetFxaa?.Dispose();
            _fxaaEffect?.Dispose();
            _renderTargetDiffuse?.Dispose();
            _renderTargetSpecular?.Dispose();
            _renderTargetVolume?.Dispose();
            _renderTargetIndirect?.Dispose();
            _renderTargetLightmap?.Dispose();
            _renderTargetShadowMask?.Dispose();
            _lightmapRenderModule?.Dispose();
            _renderTargetComposed?.Dispose();
            _renderTargetBloom?.Dispose();
            _renderTargetTAA_1?.Dispose();
            _renderTargetTAA_2?.Dispose();
            _renderTargetScreenSpaceEffectReflection?.Dispose();
            _renderTargetSSAOEffect?.Dispose();
            _renderTargetScreenSpaceEffectUpsampleBlurVertical?.Dispose();
            _renderTargetScreenSpaceEffectUpsampleBlurHorizontal?.Dispose();
            _renderTargetScreenSpaceEffectBlurFinal?.Dispose();
            _renderTargetOutput?.Dispose();
            _currentOutput?.Dispose();
            _renderTargetCubeMap?.Dispose();
            _prefilteredGlobal?.Dispose();
            _environmentPrefilter?.Dispose();
            _reflectionProbes.Dispose();
            foreach (RenderTargetCube cube in _probeCaptureTargets.Values) cube.Dispose();
        }
    }

}

