using System;
using Engine.Entities;
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

        public RenderTargetCube Cubemap
        {
            set { _paramReflectionCubeMap.SetValue(value); }
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

        public Texture2D SkyTexture
        {
            set
            {
                _skyMapTexture = value;
                if (_paramSkyMap2D != null) _paramSkyMap2D.SetValue(value);
                if (_paramUseSkyMap2D != null) _paramUseSkyMap2D.SetValue(value != null);
            }
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
            _paramProbeSHR = _deferredEnvironmentShader.Parameters["ProbeSHR"];
            _paramProbeSHG = _deferredEnvironmentShader.Parameters["ProbeSHG"];
            _paramProbeSHB = _deferredEnvironmentShader.Parameters["ProbeSHB"];

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
        /// </summary>
        public void SetProbeVolume(LightingSystem lighting, float intensity)
        {
            if (_paramUseProbeVolume == null) return;

            ProbeVolumeData data = lighting?.GpuData;
            if (data == null || lighting.ProbeSHR == null || _paramProbeSHR == null)
            {
                _paramUseProbeVolume.SetValue(false);
                return;
            }

            Vector3 extent = data.BoundsMax - data.BoundsMin;
            _paramUseProbeVolume.SetValue(true);
            _paramProbeVolumeIntensity?.SetValue(intensity);
            _paramProbeVolumeMin?.SetValue(data.BoundsMin);
            _paramProbeVolumeExtentRcp?.SetValue(new Vector3(1f / extent.X, 1f / extent.Y, 1f / extent.Z));
            _paramProbeVolumeCells?.SetValue(new Vector3(data.CountX - 1, data.CountY - 1, data.CountZ - 1));
            _paramProbeSHR.SetValue(lighting.ProbeSHR);
            _paramProbeSHG?.SetValue(lighting.ProbeSHG);
            _paramProbeSHB?.SetValue(lighting.ProbeSHB);
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
