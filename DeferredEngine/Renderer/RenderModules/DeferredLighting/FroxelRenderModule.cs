using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DeferredEngine.Entities;
using DeferredEngine.Recources;
using DeferredEngine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DeferredEngine.Renderer.RenderModules.DeferredLighting
{
    /// <summary>
    /// Clustered Volumetric Fog (Froxels) Render Module
    /// Divides view frustum into a 3D grid and assigns lights to each voxel
    /// </summary>
    public class FroxelRenderModule : IDisposable
    {
        private GraphicsDevice _graphicsDevice;
        private Effect _froxelShader;
        private FullScreenTriangle _fullScreenTriangle;
        private ShaderManager _shaderManager;
        private int _shaderIndex = -1;

        // Froxel grid dimensions
        public const int FROXEL_GRID_X = 16;
        public const int FROXEL_GRID_Y = 16;
        public const int FROXEL_GRID_Z = 24;
        public const int MAX_LIGHTS_PER_FROXEL = 32;

        // Render targets and buffers
        public Texture3D _froxelGridTexture; // Stores light indices and count per voxel
        private RenderTarget2D _froxelLightAssignmentTarget; // Temp target for compute pass
        private Texture2D _dummyShadowMap;
        
        // Shader parameters
        private EffectParameter _paramView;
        private EffectParameter _paramInverseProjection;
        private EffectParameter _paramInverseView;
        private EffectParameter _paramNearClip;
        private EffectParameter _paramFarClip;
        private EffectParameter _paramGridDimensions;
        private EffectParameter _paramFroxelSize;
        private EffectParameter _paramAlbedoMap;
        private EffectParameter _paramNormalMap;
        private EffectParameter _paramDepthMap;
        private EffectParameter _paramScreenResolution;
        private EffectParameter _paramDirectionalLightDirectionVS;
        private EffectParameter _paramDirectionalLightColor;
        private EffectParameter _paramUseDirectionalLight;
        private EffectParameter _paramShadowMap;
        private EffectParameter _paramLightViewProjection;
        private EffectParameter _paramUseFroxelFog;
        private EffectParameter _paramFroxelDensity;
        private EffectParameter _paramFroxelScatter;
        private EffectParameter _paramFroxelAbsorption;

        private EffectTechnique _techniqueBuildFroxels;
        private EffectTechnique _techniqueComposeFroxels;
        private EffectPass _passBuildFroxels;
        private EffectPass _passComposeFroxels;

        // View frustum data
        private Vector3[] _frustumCorners = new Vector3[8];
        private Matrix _view;
        private Matrix _projection;
        private Matrix _inverseView;
        private Matrix _inverseProjection;
        private float _nearClip = 0.1f;
        private float _farClip = 1000.0f;

        private bool _isInitialized = false;
        private Stopwatch _debugTimer = new Stopwatch();

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
            _paramView = GetEffectParameter("View");
            _paramInverseProjection = GetEffectParameter("InverseProjection");
            _paramNearClip = GetEffectParameter("NearClip");
            _paramFarClip = GetEffectParameter("FarClip");
            _paramGridDimensions = GetEffectParameter("GridDimensions");
            _paramFroxelSize = GetEffectParameter("FroxelSize");
            _paramAlbedoMap = GetEffectParameter("AlbedoMap");
            _paramNormalMap = GetEffectParameter("NormalMap");
            _paramDepthMap = GetEffectParameter("DepthMap");
            _paramDirectionalLightDirectionVS = GetEffectParameter("DirectionalLightDirectionVS");
            _paramDirectionalLightColor = GetEffectParameter("DirectionalLightColor");
            _paramUseDirectionalLight = GetEffectParameter("UseDirectionalLight");
            _paramScreenResolution = GetEffectParameter("ScreenResolution");
            _paramShadowMap = GetEffectParameter("ShadowMap");
            _paramLightViewProjection = GetEffectParameter("LightViewProjection");
            _paramUseFroxelFog = GetEffectParameter("UseFroxelFog");
            _paramInverseView = GetEffectParameter("InverseView");
            _paramFroxelDensity = GetEffectParameter("FroxelDensity");
            _paramFroxelScatter = GetEffectParameter("FroxelScatter");
            _paramFroxelAbsorption = GetEffectParameter("FroxelAbsorption");

            _techniqueBuildFroxels = GetEffectTechnique("BuildFroxels");
            _techniqueComposeFroxels = GetEffectTechnique("ComposeFroxels");

            _passBuildFroxels = _techniqueBuildFroxels?.Passes.FirstOrDefault();
            _passComposeFroxels = _techniqueComposeFroxels?.Passes.FirstOrDefault();
        }

        /// <summary>
        /// Initialize froxel system with graphics device and render targets
        /// </summary>
        public void Initialize(GraphicsDevice graphicsDevice, FullScreenTriangle fullScreenTriangle, int screenWidth, int screenHeight)
        {
            _graphicsDevice = graphicsDevice;
            _fullScreenTriangle = fullScreenTriangle;

            // Create 3D texture to store light cluster data
            // Format: RGBA8 where we can store light indices
            // We'll use multiple slices if needed for more lights per voxel
            try
            {
                // For now, we'll use a 2D texture array approach since MonoGame has limited 3D texture support
                // We'll store froxel data in a tall 2D texture
                _froxelLightAssignmentTarget = new RenderTarget2D(
                    _graphicsDevice,
                    FROXEL_GRID_X * FROXEL_GRID_Y,
                    FROXEL_GRID_Z,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None
                );

                _dummyShadowMap = new Texture2D(_graphicsDevice, 1, 1, false, SurfaceFormat.Color);
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

        /// <summary>
        /// Update view and projection matrices
        /// </summary>
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
        /// Build froxel light clusters for this frame
        /// </summary>
        public void BuildFroxels(List<PointLight> pointLights, List<DeferredEngine.Entities.DirectionalLight> directionalLights, Vector3 cameraPosition, int screenWidth, int screenHeight)
        {
            if (!_isInitialized || _froxelShader == null || _graphicsDevice == null)
                return;

            if (_paramView == null || _paramInverseView == null || _paramInverseProjection == null || _paramNearClip == null || _paramFarClip == null || _paramGridDimensions == null || _paramFroxelSize == null || _paramScreenResolution == null || _paramUseDirectionalLight == null || _paramDirectionalLightDirectionVS == null || _paramDirectionalLightColor == null || _paramLightViewProjection == null || _paramShadowMap == null || _paramUseFroxelFog == null || _paramFroxelDensity == null || _paramFroxelScatter == null || _paramFroxelAbsorption == null)
            {
                Debug.WriteLine("FroxelRenderModule.BuildFroxels skipped due to missing shader parameters.");
                return;
            }

            _debugTimer.Restart();

            // Set shader parameters
            _paramView.SetValue(_view);
            _paramInverseView.SetValue(_inverseView);
            _paramInverseProjection.SetValue(_inverseProjection);
            _paramNearClip.SetValue(_nearClip);
            _paramFarClip.SetValue(_farClip);
            _paramGridDimensions.SetValue(new Vector3(FROXEL_GRID_X, FROXEL_GRID_Y, FROXEL_GRID_Z));
            _paramFroxelSize.SetValue(new Vector2(
                2.0f / FROXEL_GRID_X,
                2.0f / FROXEL_GRID_Y
            ));
            _paramScreenResolution.SetValue(new Vector2(screenWidth, screenHeight));
            _paramUseFroxelFog.SetValue(GameSettings.g_FroxelFogEnabled);
            _paramFroxelDensity.SetValue(GameSettings.g_FroxelDensity);
            _paramFroxelScatter.SetValue(GameSettings.g_FroxelScatter);
            _paramFroxelAbsorption.SetValue(GameSettings.g_FroxelAbsorption);

            if (directionalLights != null && directionalLights.Count > 0)
            {
                var directionalLight = directionalLights[0];
                Vector3 dirVS = Vector3.TransformNormal(directionalLight.Direction, _view);
                dirVS.Normalize();

                _paramUseDirectionalLight.SetValue(true);
                _paramDirectionalLightDirectionVS.SetValue(dirVS);
                _paramDirectionalLightColor.SetValue(directionalLight.ColorV3 * directionalLight.Intensity);
                _paramLightViewProjection.SetValue(directionalLight.LightViewProjection);
                _paramShadowMap.SetValue(directionalLight.ShadowMap ?? _dummyShadowMap);
            }
            else
            {
                _paramUseDirectionalLight.SetValue(false);
                _paramDirectionalLightDirectionVS.SetValue(Vector3.Zero);
                _paramDirectionalLightColor.SetValue(Vector3.Zero);
                _paramLightViewProjection.SetValue(Matrix.Identity);
                _paramShadowMap.SetValue(_dummyShadowMap);
            }

            // For now, we'll store point light data in a structured way
            // In a full implementation, this would be done via compute shader or structured buffer
            // This is a simplified CPU-side implementation

            // Create light cluster assignments (very simplified for now)
            Color[] clusterData = new Color[_froxelLightAssignmentTarget.Width * _froxelLightAssignmentTarget.Height];
            
            // Initialize with zeros
            for (int i = 0; i < clusterData.Length; i++)
            {
                clusterData[i] = Color.Black;
            }

            // Simple light assignment - in a real implementation this would be GPU-computed
            AssignLightsToFroxels(pointLights, cameraPosition, clusterData, screenWidth, screenHeight);

            // Update texture with cluster data
            _graphicsDevice.SetRenderTarget(_froxelLightAssignmentTarget);
            _graphicsDevice.Clear(Color.Black);
            
            _debugTimer.Stop();
            GameStats.d_froxelBuildTime = _debugTimer.ElapsedMilliseconds;
        }

        /// <summary>
        /// Simplified CPU-side light assignment to froxels
        /// In a production system, this would be done on GPU with compute shaders
        /// </summary>
        private void AssignLightsToFroxels(List<PointLight> pointLights, Vector3 cameraPosition, Color[] clusterData, int screenWidth, int screenHeight)
        {
            // Very simplified: mark voxels that contain lights
            foreach (var light in pointLights)
            {
                // Transform light to view space
                Vector3 lightVS = Vector3.Transform(light.Position, _view);

                // Calculate froxel indices
                float z = -lightVS.Z; // Depth in view space
                if (z < _nearClip || z > _farClip)
                    continue;

                float slice = (float)(
                    Math.Log(z / _nearClip) / Math.Log(_farClip / _nearClip)
                );

                int zIndex = (int)(slice * (FROXEL_GRID_Z - 1));
                zIndex = Math.Clamp(zIndex, 0, FROXEL_GRID_Z - 1);

                // For now, we'll just mark that there's a light in this region
                // Full implementation would compute exact x,y indices based on view projection
                int clusterIndex = Math.Min(zIndex * (FROXEL_GRID_X * FROXEL_GRID_Y), clusterData.Length - 1);
                
                if (clusterIndex >= 0 && clusterIndex < clusterData.Length)
                {
                    byte lightCount = clusterData[clusterIndex].R;
                    if (lightCount < 255)
                    {
                        clusterData[clusterIndex] = new Color(Math.Min(255, lightCount + 1), clusterData[clusterIndex].G, clusterData[clusterIndex].B, clusterData[clusterIndex].A);
                    }
                }
            }
        }

        /// <summary>
        /// Sample froxel data for volumetric lighting pass
        /// </summary>
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

            if (renderTargetOutput == null)
            {
                Debug.WriteLine("FroxelRenderModule.ComposeFroxelLighting skipped because renderTargetOutput is null.");
                return;
            }

            if (_paramAlbedoMap == null || _paramNormalMap == null || _paramDepthMap == null || _paramInverseView == null || _paramLightViewProjection == null || _paramShadowMap == null || _passComposeFroxels == null)
            {
                Debug.WriteLine("FroxelRenderModule.ComposeFroxelLighting skipped due to missing shader parameters or technique pass.");
                return;
            }

            _graphicsDevice.SetRenderTarget(renderTargetOutput);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.BlendState = BlendState.Additive;

            _paramAlbedoMap.SetValue(renderTargetAlbedo);
            _paramNormalMap.SetValue(renderTargetNormal);
            _paramDepthMap.SetValue(renderTargetDepth);
            _paramInverseView.SetValue(_inverseView);
            _paramUseFroxelFog.SetValue(GameSettings.g_FroxelFogEnabled);
            _paramFroxelDensity.SetValue(GameSettings.g_FroxelDensity);
            _paramFroxelScatter.SetValue(GameSettings.g_FroxelScatter);
            _paramFroxelAbsorption.SetValue(GameSettings.g_FroxelAbsorption);

            _froxelShader.CurrentTechnique = _techniqueComposeFroxels;
            _passComposeFroxels.Apply();

            _fullScreenTriangle.Draw(_graphicsDevice);

            _graphicsDevice.SetRenderTarget(null);
            _graphicsDevice.BlendState = BlendState.Opaque;
        }

        public Texture2D GetFroxelData()
        {
            return _froxelLightAssignmentTarget;
        }

        public void Dispose()
        {
            _froxelLightAssignmentTarget?.Dispose();
            _froxelShader?.Dispose();
        }
    }
}
