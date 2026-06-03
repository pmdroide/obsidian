using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer.RenderModules.DeferredLighting
{
    /// <summary>
    /// Volumetric fog via frustum-aligned voxels packed in a 2D atlas
    /// (width = Nx, height = Ny * Nz). Matches perspective near plane used in Renderer (1 world unit).
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

        public static int AtlasWidth => FROXEL_GRID_X;
        public static int AtlasHeight => FROXEL_GRID_Y * FROXEL_GRID_Z;

        private GraphicsDevice _graphicsDevice;
        private Effect _froxelShader;
        private FullScreenTriangle _fullScreenTriangle;
        private int _shaderIndex = -1;

        private RenderTarget2D _froxelInjectionTexture;
        private RenderTarget2D[] _froxelAccumulationTargets = new RenderTarget2D[2];
        private int _currentAccumulationIndex;
        private Texture2D _noiseMap;
        private Texture2D _dummyShadowMap;

        private EffectParameter _paramInverseProjection;
        private EffectParameter _paramInverseView;
        private EffectParameter _paramNearClip;
        private EffectParameter _paramFarClip;
        private EffectParameter _paramGridDimensions;
        private EffectParameter _paramScreenResolution;
        private EffectParameter _paramDirectionalLightDirectionVS;
        private EffectParameter _paramDirectionalLightColor;
        private EffectParameter _paramUseDirectionalLight;
        private EffectParameter _paramShadowMap;
        private EffectParameter _paramPreviousFroxelAccumulationTexture;
        private EffectParameter _paramNoiseMap;
        private EffectParameter _paramTime;
        private EffectParameter _paramLightViewProjection;
        private EffectParameter _paramLightView;
        private EffectParameter _paramLightFarClip;
        private EffectParameter _paramUseFroxelFog;
        private EffectParameter _paramFroxelDensity;
        private EffectParameter _paramFroxelAbsorption;
        private EffectParameter _paramDirectionalScatter;
        private EffectParameter _paramPointScatter;
        private EffectParameter _paramFogDistanceStart;
        private EffectParameter _paramFogDistanceFull;
        private EffectParameter _paramHistoryAlpha;
        private EffectParameter _paramPointLightCount;
        private EffectParameter _paramPointLightPositionsVS;
        private EffectParameter _paramPointLightPositionsWS;
        private EffectParameter _paramPointLightColors;
        private EffectParameter _paramPointLightRadii;
        private EffectParameter _paramPointLightCastShadows;
        private EffectParameter _paramPointLightShadowMapSize;
        private readonly EffectParameter[] _paramPointLightShadowMaps = new EffectParameter[MAX_FROXEL_POINT_LIGHTS];

        private readonly Vector3[] _pointLightPositionsVS = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly Vector3[] _pointLightPositionsWS = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly Vector3[] _pointLightColors = new Vector3[MAX_FROXEL_POINT_LIGHTS];
        private readonly float[] _pointLightRadii = new float[MAX_FROXEL_POINT_LIGHTS];
        private readonly float[] _pointLightCastShadows = new float[MAX_FROXEL_POINT_LIGHTS];
        private readonly float[] _pointLightShadowMapSizes = new float[MAX_FROXEL_POINT_LIGHTS];

        private EffectTechnique _techniqueBuildFroxels;
        private EffectTechnique _techniqueAccumulateFroxels;
        private EffectTechnique _techniqueComposeFroxels;

        private EffectPass _passBuildFroxels;
        private EffectPass _passAccumulateFroxels;
        private EffectPass _passComposeFroxels;

        private Matrix _view;
        private Matrix _inverseView;
        private Matrix _inverseProjection;
        private float _nearClip = CameraNearPlane;
        private float _farClip = 1000f;

        private bool _isInitialized;

        public Texture2D FroxelAccumulation => _froxelAccumulationTargets[_currentAccumulationIndex];

        public FroxelRenderModule(ShaderManager shaderManager, string shaderPath)
        {
            Load(shaderManager, shaderPath);
        }

        private void Load(ShaderManager shaderManager, string shaderPath)
        {
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
            _paramNearClip = GetEffectParameter("NearClip");
            _paramFarClip = GetEffectParameter("FarClip");
            _paramGridDimensions = GetEffectParameter("GridDimensions");
            _paramScreenResolution = GetEffectParameter("ScreenResolution");
            _paramDirectionalLightDirectionVS = GetEffectParameter("DirectionalLightDirectionVS");
            _paramDirectionalLightColor = GetEffectParameter("DirectionalLightColor");
            _paramUseDirectionalLight = GetEffectParameter("UseDirectionalLight");
            _paramShadowMap = GetEffectParameter("ShadowMap");
            _paramPreviousFroxelAccumulationTexture = GetEffectParameter("PreviousFroxelAccumulationTexture");
            _paramNoiseMap = GetEffectParameter("NoiseMap");
            _paramTime = GetEffectParameter("Time");
            _paramLightViewProjection = GetEffectParameter("LightViewProjection");
            _paramLightView = GetEffectParameter("LightView");
            _paramLightFarClip = GetEffectParameter("LightFarClip");
            _paramUseFroxelFog = GetEffectParameter("UseFroxelFog");
            _paramFroxelDensity = GetEffectParameter("FroxelDensity");
            _paramFroxelAbsorption = GetEffectParameter("FroxelAbsorption");
            _paramDirectionalScatter = GetEffectParameter("DirectionalScatter");
            _paramPointScatter = GetEffectParameter("PointScatter");
            _paramFogDistanceStart = GetEffectParameter("FogDistanceStart");
            _paramFogDistanceFull = GetEffectParameter("FogDistanceFull");
            _paramHistoryAlpha = GetEffectParameter("HistoryAlpha");
            _paramPointLightCount = GetEffectParameter("PointLightCount");
            _paramPointLightPositionsVS = GetEffectParameter("PointLightPositionsVS");
            _paramPointLightPositionsWS = GetEffectParameter("PointLightPositionsWS");
            _paramPointLightColors = GetEffectParameter("PointLightColors");
            _paramPointLightRadii = GetEffectParameter("PointLightRadii");
            _paramPointLightCastShadows = GetEffectParameter("PointLightCastShadows");
            _paramPointLightShadowMapSize = GetEffectParameter("PointLightShadowMapSize");
            for (int i = 0; i < MAX_FROXEL_POINT_LIGHTS; ++i)
                _paramPointLightShadowMaps[i] = GetEffectParameter("PointLightShadowMap" + i);

            _techniqueBuildFroxels = GetEffectTechnique("BuildFroxels");
            _techniqueAccumulateFroxels = GetEffectTechnique("AccumulateFroxels");
            _techniqueComposeFroxels = GetEffectTechnique("ComposeFroxels");

            _passBuildFroxels = _techniqueBuildFroxels?.Passes.FirstOrDefault();
            _passAccumulateFroxels = _techniqueAccumulateFroxels?.Passes.FirstOrDefault();
            _passComposeFroxels = _techniqueComposeFroxels?.Passes.FirstOrDefault();
        }

        public void Initialize(GraphicsDevice graphicsDevice, FullScreenTriangle fullScreenTriangle, int screenWidth, int screenHeight)
        {
            _graphicsDevice = graphicsDevice;
            _fullScreenTriangle = fullScreenTriangle;

            try
            {
                _froxelInjectionTexture?.Dispose();
                for (int i = 0; i < _froxelAccumulationTargets.Length; ++i)
                {
                    _froxelAccumulationTargets[i]?.Dispose();
                    _froxelAccumulationTargets[i] = null;
                }

                _froxelInjectionTexture = new RenderTarget2D(
                    _graphicsDevice,
                    AtlasWidth,
                    AtlasHeight,
                    false,
                    SurfaceFormat.HalfVector4,
                    DepthFormat.None);

                _froxelAccumulationTargets[0] = new RenderTarget2D(
                    _graphicsDevice,
                    AtlasWidth,
                    AtlasHeight,
                    false,
                    SurfaceFormat.HalfVector4,
                    DepthFormat.None);

                _froxelAccumulationTargets[1] = new RenderTarget2D(
                    _graphicsDevice,
                    AtlasWidth,
                    AtlasHeight,
                    false,
                    SurfaceFormat.HalfVector4,
                    DepthFormat.None);

                _currentAccumulationIndex = 0;
                _graphicsDevice.SetRenderTarget(_froxelAccumulationTargets[0]);
                _graphicsDevice.Clear(Color.Transparent);
                _graphicsDevice.SetRenderTarget(_froxelAccumulationTargets[1]);
                _graphicsDevice.Clear(Color.Transparent);
                _graphicsDevice.SetRenderTarget(null);

                _dummyShadowMap ??= new Texture2D(_graphicsDevice, 1, 1, false, SurfaceFormat.Color);
                _dummyShadowMap.SetData(new[] { Color.White });

                _isInitialized = true;
                GameStats.d_froxelSystemReady = true;
            }
            catch (Exception ex)
            {
                GameStats.d_froxelSystemReady = false;
                Debug.WriteLine($"Failed to initialize froxel system: {ex.Message}");
            }
        }

        public void UpdateMatrices(Matrix view, Matrix projection, Matrix inverseView, Matrix inverseProjection, float nearClip, float farClip)
        {
            _ = projection;
            _view = view;
            _inverseView = inverseView;
            _inverseProjection = inverseProjection;
            _nearClip = nearClip;
            _farClip = farClip;
        }

        public void SetNoiseMap(Texture2D noiseMap)
        {
            _noiseMap = noiseMap;
            if (_paramNoiseMap != null)
            {
                _paramNoiseMap.SetValue(_noiseMap);
            }
        }

        private void ApplyCommonParameters(int screenWidth, int screenHeight, float timeSeconds = 0)
        {
            if (_paramInverseProjection != null) _paramInverseProjection.SetValue(_inverseProjection);
            if (_paramInverseView != null) _paramInverseView.SetValue(_inverseView);
            if (_paramTime != null) _paramTime.SetValue(timeSeconds);
            if (_paramNearClip != null) _paramNearClip.SetValue(_nearClip);
            if (_paramFarClip != null) _paramFarClip.SetValue(_farClip);
            if (_paramGridDimensions != null)
                _paramGridDimensions.SetValue(new Vector3(FROXEL_GRID_X, FROXEL_GRID_Y, FROXEL_GRID_Z));
            if (_paramScreenResolution != null)
                _paramScreenResolution.SetValue(new Vector2(screenWidth, screenHeight));
            if (_paramUseFroxelFog != null) _paramUseFroxelFog.SetValue(GameSettings.g_FroxelFogEnabled);
            if (_paramFroxelDensity != null) _paramFroxelDensity.SetValue(GameSettings.g_FroxelDensity);
            if (_paramFroxelAbsorption != null) _paramFroxelAbsorption.SetValue(GameSettings.g_FroxelAbsorption);
            if (_paramDirectionalScatter != null) _paramDirectionalScatter.SetValue(GameSettings.g_FroxelDirectionalScatter);
            if (_paramPointScatter != null) _paramPointScatter.SetValue(GameSettings.g_FroxelPointScatter);
            if (_paramFogDistanceStart != null) _paramFogDistanceStart.SetValue(GameSettings.g_FroxelFogDistanceStart);
            if (_paramFogDistanceFull != null) _paramFogDistanceFull.SetValue(GameSettings.g_FroxelFogDistanceFull);
            if (_paramHistoryAlpha != null) _paramHistoryAlpha.SetValue(GameSettings.g_FroxelHistoryBlend);
        }

        private void ApplyPointLights(List<PointLight> pointLights)
        {
            int count = 0;

            if (pointLights != null && _paramPointLightCount != null)
            {
                int limit = Math.Min(pointLights.Count, MAX_FROXEL_POINT_LIGHTS);
                for (int i = 0; i < limit; ++i)
                {
                    PointLight light = pointLights[i];
                    if (light == null)
                        continue;

                    _pointLightPositionsVS[count] = Vector3.Transform(light.Position, _view);
                    _pointLightPositionsWS[count] = light.Position;
                    _pointLightColors[count] = light.ColorV3 * light.Intensity;
                    _pointLightRadii[count] = light.Radius;

                    bool hasShadow = light.CastShadows && light.ShadowMap != null;
                    _pointLightCastShadows[count] = hasShadow ? 1f : 0f;
                    _pointLightShadowMapSizes[count] = hasShadow ? light.ShadowResolution : 0f;
                    _paramPointLightShadowMaps[count]?.SetValue(hasShadow ? light.ShadowMap : (Texture2D)_dummyShadowMap);

                    count++;
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
                _paramPointLightShadowMaps[i]?.SetValue((Texture2D)_dummyShadowMap);
            }

            _paramPointLightCount?.SetValue(count);
            _paramPointLightPositionsVS?.SetValue(_pointLightPositionsVS);
            _paramPointLightPositionsWS?.SetValue(_pointLightPositionsWS);
            _paramPointLightColors?.SetValue(_pointLightColors);
            _paramPointLightRadii?.SetValue(_pointLightRadii);
            _paramPointLightCastShadows?.SetValue(_pointLightCastShadows);
            _paramPointLightShadowMapSize?.SetValue(_pointLightShadowMapSizes);
        }

        private void ApplyDirectionalLight(List<DirectionalLight> directionalLights)
        {
            if (directionalLights != null && directionalLights.Count > 0)
            {
                var directionalLight = directionalLights[0];
                Vector3 dirVS = Vector3.TransformNormal(directionalLight.Direction, _view);
                dirVS.Normalize();

                if (_paramUseDirectionalLight != null) _paramUseDirectionalLight.SetValue(true);
                if (_paramDirectionalLightDirectionVS != null) _paramDirectionalLightDirectionVS.SetValue(dirVS);
                if (_paramDirectionalLightColor != null)
                    _paramDirectionalLightColor.SetValue(directionalLight.ColorV3 * directionalLight.Intensity);
                if (_paramLightViewProjection != null) _paramLightViewProjection.SetValue(directionalLight.LightViewProjection);
                if (_paramLightView != null) _paramLightView.SetValue(directionalLight.LightView);
                if (_paramLightFarClip != null) _paramLightFarClip.SetValue(directionalLight.ShadowDepth);
                if (_paramShadowMap != null) _paramShadowMap.SetValue(directionalLight.ShadowMap ?? _dummyShadowMap);
            }
            else
            {
                if (_paramUseDirectionalLight != null) _paramUseDirectionalLight.SetValue(false);
                if (_paramDirectionalLightDirectionVS != null) _paramDirectionalLightDirectionVS.SetValue(Vector3.Zero);
                if (_paramDirectionalLightColor != null) _paramDirectionalLightColor.SetValue(Vector3.Zero);
                if (_paramLightViewProjection != null) _paramLightViewProjection.SetValue(Matrix.Identity);
                if (_paramLightView != null) _paramLightView.SetValue(Matrix.Identity);
                if (_paramLightFarClip != null) _paramLightFarClip.SetValue(1f);
                if (_paramShadowMap != null) _paramShadowMap.SetValue(_dummyShadowMap);
            }
        }

        /// <summary>
        /// Injection pass then accumulation pass into internal atlases (after G-buffer / shadows).
        /// </summary>
        public void RenderFroxelFog(List<PointLight> pointLights, List<DirectionalLight> directionalLights, int screenWidth, int screenHeight, float timeSeconds)
        {
            if (!_isInitialized || _froxelShader == null || _graphicsDevice == null || _fullScreenTriangle == null)
                return;

            if (_passBuildFroxels == null || _passAccumulateFroxels == null)
            {
                Debug.WriteLine("FroxelRenderModule.RenderFroxelFog skipped: missing shader pass.");
                return;
            }

            ApplyCommonParameters(screenWidth, screenHeight, timeSeconds);
            ApplyDirectionalLight(directionalLights);
            ApplyPointLights(pointLights);

            if (_paramShadowMap != null)
            {
                Texture2D activeShadowMap = _dummyShadowMap;
                if (directionalLights != null && directionalLights.Count > 0)
                    activeShadowMap = directionalLights[0].ShadowMap ?? _dummyShadowMap;
                _paramShadowMap.SetValue(activeShadowMap);
            }

            if (_paramNoiseMap != null && _noiseMap != null)
            {
                _paramNoiseMap.SetValue(_noiseMap);
            }

            var prevBlend = _graphicsDevice.BlendState;
            var prevDepth = _graphicsDevice.DepthStencilState;

            _graphicsDevice.BlendState = BlendState.Opaque;
            _graphicsDevice.DepthStencilState = DepthStencilState.None;

            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Froxel: Injection");
            try
            {
                _graphicsDevice.SetRenderTarget(_froxelInjectionTexture);
                _graphicsDevice.Clear(Color.Transparent);
                _froxelShader.CurrentTechnique = _techniqueBuildFroxels;
                _passBuildFroxels.Apply();
                _fullScreenTriangle.Draw(_graphicsDevice);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }

            int nextAccumulationIndex = (_currentAccumulationIndex + 1) % _froxelAccumulationTargets.Length;
            RenderTarget2D nextAccumulationTarget = _froxelAccumulationTargets[nextAccumulationIndex];
            RenderTarget2D previousAccumulationTarget = _froxelAccumulationTargets[_currentAccumulationIndex];

            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Froxel: Accumulation");
            try
            {
                _graphicsDevice.SetRenderTarget(nextAccumulationTarget);
                _graphicsDevice.Clear(Color.Transparent);
                _froxelShader.Parameters["FroxelInjectionTexture"]?.SetValue(_froxelInjectionTexture);
                if (_paramPreviousFroxelAccumulationTexture != null)
                    _paramPreviousFroxelAccumulationTexture.SetValue(previousAccumulationTarget);
                _froxelShader.CurrentTechnique = _techniqueAccumulateFroxels;
                _passAccumulateFroxels.Apply();
                _fullScreenTriangle.Draw(_graphicsDevice);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }

            _currentAccumulationIndex = nextAccumulationIndex;
            _graphicsDevice.SetRenderTarget(null);
            _graphicsDevice.BlendState = prevBlend;
            _graphicsDevice.DepthStencilState = prevDepth;
        }

        /// <summary>Legacy entry point — forwards to <see cref="RenderFroxelFog"/>.</summary>
        public void BuildFroxels(List<PointLight> pointLights, List<DirectionalLight> directionalLights, Vector3 cameraPosition, int screenWidth, int screenHeight, float timeSeconds)
        {
            RenderFroxelFog(pointLights, directionalLights, screenWidth, screenHeight, timeSeconds);
        }

        /// <summary>Optional screen-space compose using Froxel.fx Compose technique (debug).</summary>
        public void ComposeFroxelLighting(
            RenderTarget2D renderTargetAlbedo,
            RenderTarget2D renderTargetNormal,
            RenderTarget2D renderTargetDepth,
            RenderTarget2D renderTargetOutput,
            int screenWidth,
            int screenHeight)
        {
            if (!_isInitialized || _froxelShader == null || _graphicsDevice == null || _fullScreenTriangle == null)
                return;

            if (renderTargetOutput == null || _passComposeFroxels == null)
                return;

            ApplyCommonParameters(screenWidth, screenHeight);

            GraphicsDeviceDebugMarkers.BeginEventGroup(_graphicsDevice, "Froxel: Scene Integration");
            try
            {
                _graphicsDevice.SetRenderTarget(renderTargetOutput);
                _graphicsDevice.Clear(Color.Transparent);
                _graphicsDevice.BlendState = BlendState.Opaque;

                var paramAlbedo = GetEffectParameter("AlbedoMap");
                var paramNormal = GetEffectParameter("NormalMap");
                var paramDepth = GetEffectParameter("DepthMap");
                var paramAccum = GetEffectParameter("FroxelAccumulationTexture");

                if (paramAlbedo != null) paramAlbedo.SetValue(renderTargetAlbedo);
                if (paramNormal != null) paramNormal.SetValue(renderTargetNormal);
                if (paramDepth != null) paramDepth.SetValue(renderTargetDepth);
                if (paramAccum != null) paramAccum.SetValue(FroxelAccumulation);

                _froxelShader.CurrentTechnique = _techniqueComposeFroxels;
                _passComposeFroxels.Apply();
                _fullScreenTriangle.Draw(_graphicsDevice);
            }
            finally
            {
                GraphicsDeviceDebugMarkers.EndEventGroup(_graphicsDevice);
            }

            _graphicsDevice.SetRenderTarget(null);
        }

        public void Dispose()
        {
            _froxelInjectionTexture?.Dispose();
            _froxelInjectionTexture = null;
            for (int i = 0; i < _froxelAccumulationTargets.Length; ++i)
            {
                _froxelAccumulationTargets[i]?.Dispose();
                _froxelAccumulationTargets[i] = null;
            }
            _dummyShadowMap?.Dispose();
            _dummyShadowMap = null;
        }
    }
}
