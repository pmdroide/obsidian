using System;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Renderer.RenderModules.Default;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules
{
    //Just a template
    public class GBufferRenderModule : IRenderModule, IVelocityModule, IFoliageWindModule, IDisposable
    {
        //Motion vectors (TAA): unjittered view projection of this frame and the last one; set by the Renderer per view
        public Matrix StaticViewProjection = Matrix.Identity;
        public Matrix PreviousStaticViewProjection = Matrix.Identity;
        private Matrix _previousWorld;
        private EffectParameter _CurrentWorldViewProj;
        private EffectParameter _PreviousWorldViewProj;

        public void SetPreviousWorld(Matrix previousWorld) => _previousWorld = previousWorld;

        private Effect _clearShader;
        private Effect _gbufferShader;
        private EffectPass _clearGBufferPass;

        private EffectParameter _World;
        private EffectParameter _View;
        private EffectParameter _WorldView;
        private EffectParameter _WorldViewProj;
        private EffectParameter _WorldViewIT;
        private EffectParameter _ViewProjection;
        private EffectParameter _Camera;
        private EffectParameter _FarClip;

        private EffectParameter _Material_Metallic;
        private EffectParameter _Material_MetallicMap;
        private EffectParameter _Material_DiffuseColor;
        private EffectParameter _Material_Roughness;
        private EffectParameter _Material_MaskMap;
        private EffectParameter _Material_Texture;
        private EffectParameter _Material_NormalMap;
        private EffectParameter _Material_DisplacementMap;
        private EffectParameter _Material_RoughnessMap;
        private EffectParameter _Material_MaterialType;
        private EffectParameter _OverrideSurface;
        private EffectParameter _SurfaceTint;
        private EffectParameter _ComponentSurface;
        private EffectParameter _ComponentRoughnessMap;
        private EffectParameter _ComponentMetallicMap;
        private EffectParameter _TextureTiling;
        private EffectParameter _WetSurface;
        private EffectParameter _WetParams;
        private EffectParameter _Ripples;
        private EffectParameter _Time;

        private EffectTechnique _DrawTextureDisplacement;
        private EffectTechnique _DrawTextureSpecularNormalMask;
        private EffectTechnique _DrawTextureNormalMask;
        private EffectTechnique _DrawTextureSpecularMask;
        private EffectTechnique _DrawTextureMask;
        private EffectTechnique _DrawTextureSpecularNormalMetallic;
        private EffectTechnique _DrawTextureSpecularNormal;
        private EffectTechnique _DrawTextureNormal;
        private EffectTechnique _DrawTextureSpecular;
        private EffectTechnique _DrawTextureSpecularMetallic;
        private EffectTechnique _DrawTexture;
        private EffectTechnique _DrawNormal;
        private EffectTechnique _DrawBasic;

        private FullScreenTriangle _fullScreenTriangle;

        public GBufferRenderModule(ContentManager content, string shaderPathClear, string shaderPathGbuffer)
        {
            Load(content, shaderPathClear, shaderPathGbuffer);
        }

        private float _farClip;
        public float FarClip
        {
            get { return _farClip; }
            set
            {
                _farClip = value; 
                _FarClip.SetValue(value);
            }
        }

        private Vector3 _camera;
        public Vector3 Camera
        {
            get { return _camera; }
            set
            {
                _camera = value; 
                _Camera.SetValue(value);
            }
        }


        /// <summary>Weather wetness (<see cref="Logic.SurfaceWetness"/>) for Wet Surface materials that follow the weather.</summary>
        public float SceneWetness { get; set; }
        /// <summary>Weather raindrop ripples in Wet Surface puddles.</summary>
        public float SceneRipples { get; set; }

        /// <summary>Seconds that animate the puddle ripples.</summary>
        public float Time
        {
            set { _Time?.SetValue(value); }
        }

        /// <summary>Foliage Wind parameters of the G-buffer shader.</summary>
        public FoliageWindParameters Wind { get; private set; }

        public void Initialize(GraphicsDevice graphicsDevice)
        {
            _clearGBufferPass = _clearShader.Techniques["Clear"].Passes[0];

            _fullScreenTriangle = new FullScreenTriangle(graphicsDevice);
            
            _World = _gbufferShader.Parameters["World"];
            _View = _gbufferShader.Parameters["View"];
            _WorldView = _gbufferShader.Parameters["WorldView"];
            _WorldViewProj = _gbufferShader.Parameters["WorldViewProj"];
            _WorldViewIT = _gbufferShader.Parameters["WorldViewIT"];
            _ViewProjection = _gbufferShader.Parameters["ViewProjection"];
            _CurrentWorldViewProj = _gbufferShader.Parameters["CurrentWorldViewProj"];
            _PreviousWorldViewProj = _gbufferShader.Parameters["PreviousWorldViewProj"];
            Wind = new FoliageWindParameters(_gbufferShader);
            _Camera = _gbufferShader.Parameters["Camera"];
            _FarClip = _gbufferShader.Parameters["FarClip"];

            _Material_Metallic = _gbufferShader.Parameters["Metallic"];
            _Material_MetallicMap = _gbufferShader.Parameters["MetallicMap"];
            _Material_DiffuseColor = _gbufferShader.Parameters["DiffuseColor"];
            _Material_Roughness = _gbufferShader.Parameters["Roughness"];

            _Material_MaskMap = _gbufferShader.Parameters["Mask"];
            _Material_Texture = _gbufferShader.Parameters["Texture"];
            _Material_NormalMap = _gbufferShader.Parameters["NormalMap"];
            _Material_RoughnessMap = _gbufferShader.Parameters["RoughnessMap"];
            _Material_DisplacementMap = _gbufferShader.Parameters["DisplacementMap"];

            _Material_MaterialType = _gbufferShader.Parameters["MaterialType"];
            _OverrideSurface = _gbufferShader.Parameters["OverrideSurface"];
            _SurfaceTint = _gbufferShader.Parameters["SurfaceTint"];
            _ComponentSurface = _gbufferShader.Parameters["ComponentSurface"];
            _ComponentRoughnessMap = _gbufferShader.Parameters["ComponentRoughnessMap"];
            _ComponentMetallicMap = _gbufferShader.Parameters["ComponentMetallicMap"];
            _TextureTiling = _gbufferShader.Parameters["TextureTiling"];
            _WetSurface = _gbufferShader.Parameters["WetSurface"];
            _WetParams = _gbufferShader.Parameters["WetParams"];
            _Ripples = _gbufferShader.Parameters["Ripples"];
            _Time = _gbufferShader.Parameters["Time"];

            //Techniques

            _DrawTextureDisplacement = _gbufferShader.Techniques["DrawTextureDisplacement"];
            _DrawTextureSpecularNormalMask = _gbufferShader.Techniques["DrawTextureSpecularNormalMask"];
            _DrawTextureNormalMask = _gbufferShader.Techniques["DrawTextureNormalMask"];
            _DrawTextureSpecularMask = _gbufferShader.Techniques["DrawTextureSpecularMask"];
            _DrawTextureMask = _gbufferShader.Techniques["DrawTextureMask"];
            _DrawTextureSpecularNormalMetallic = _gbufferShader.Techniques["DrawTextureSpecularNormalMetallic"];
            _DrawTextureSpecularNormal = _gbufferShader.Techniques["DrawTextureSpecularNormal"];
            _DrawTextureNormal = _gbufferShader.Techniques["DrawTextureNormal"];
            _DrawTextureSpecular = _gbufferShader.Techniques["DrawTextureSpecular"];
            _DrawTextureSpecularMetallic = _gbufferShader.Techniques["DrawTextureSpecularMetallic"];
            _DrawTexture = _gbufferShader.Techniques["DrawTexture"];
            _DrawNormal = _gbufferShader.Techniques["DrawNormal"];
            _DrawBasic = _gbufferShader.Techniques["DrawBasic"];
        }
        
        public void Load(ContentManager content, string shaderPathClear, string shaderPathGbuffer)
        {
            _clearShader = content.Load<Effect>(shaderPathClear);
            _gbufferShader = content.Load<Effect>(shaderPathGbuffer);
        }

        public void Draw(GraphicsDevice _graphicsDevice, RenderTargetBinding[] _renderTargetBinding, MeshMaterialLibrary meshMaterialLibrary, Matrix _viewProjection, Matrix _view)
        {
            _graphicsDevice.SetRenderTargets(_renderTargetBinding);

            //Clear the GBuffer
            if (GameSettings.g_ClearGBuffer)
            {
                _graphicsDevice.RasterizerState = RasterizerState.CullNone;
                _graphicsDevice.BlendState = BlendState.Opaque;
                _graphicsDevice.DepthStencilState = DepthStencilState.Default;

                _clearGBufferPass.Apply();
                _fullScreenTriangle.Draw(_graphicsDevice);
            }

            //Draw the Gbuffer!

            meshMaterialLibrary.Draw(renderType: MeshMaterialLibrary.RenderType.Opaque, viewProjection: _viewProjection, lightViewPointChanged: true, view: _view, renderModule: this);

        }

        public void Apply(Matrix localWorldMatrix, Matrix? view, Matrix viewProjection)
        {
            Matrix worldView = localWorldMatrix * (Matrix)view;
            _World.SetValue(localWorldMatrix);
            _View.SetValue((Matrix)view);
            _WorldView.SetValue(worldView);
            _WorldViewProj.SetValue(localWorldMatrix * viewProjection);
            _ViewProjection.SetValue(viewProjection);
            _CurrentWorldViewProj?.SetValue(localWorldMatrix * StaticViewProjection);
            _PreviousWorldViewProj?.SetValue(_previousWorld * PreviousStaticViewProjection);

            worldView = Matrix.Invert(Matrix.Transpose(worldView));
            _WorldViewIT.SetValue(worldView);

            _gbufferShader.CurrentTechnique.Passes[0].Apply();
        }

        public void SetMaterialSettings(MaterialEffect material)
        {
            bool componentSurface = material.IsInstanceMaterial && !GameSettings.d_defaultmaterial;
            _OverrideSurface.SetValue(componentSurface);
            _ComponentRoughnessMap.SetValue(componentSurface && material.UseComponentRoughnessMap);
            _ComponentMetallicMap.SetValue(componentSurface && material.UseComponentMetallicMap &&
                material.Type != MaterialEffect.MaterialTypes.Emissive);
            if (componentSurface)
            {
                _Material_RoughnessMap.SetValue(material.RoughnessMap);
                _Material_MetallicMap.SetValue(material.MetallicMap);
            }
            _SurfaceTint.SetValue(material.HasDiffuse ? material.DiffuseColor : Vector3.One);
            _ComponentSurface.SetValue(new Vector2(material.Roughness,
                material.Type == MaterialEffect.MaterialTypes.Emissive
                    ? Math.Clamp(material.EmissiveStrength / 8f, 0, 1) : material.Metallic));
            bool wet = material.Type == MaterialEffect.MaterialTypes.WetSurface && !GameSettings.d_defaultmaterial;
            _WetSurface.SetValue(wet);
            bool ao = material.HasAmbientOcclusion && !GameSettings.d_defaultmaterial;
            _gbufferShader.Parameters["UseAoMap"]?.SetValue(ao);
            if (ao) _gbufferShader.Parameters["AoMap"]?.SetValue(material.AmbientOcclusionMap);
            _TextureTiling.SetValue(GameSettings.d_defaultmaterial ? Vector2.One : material.TextureTiling);
            if (wet)
            {
                // The displacement map is the height puddles fill (cavities first); no parallax.
                if (material.HasDisplacement)
                    _Material_DisplacementMap.SetValue(material.DisplacementMap);
                float wetness = material.WeatherWetness ? Math.Max(material.Wetness, SceneWetness) : material.Wetness;
                _WetParams.SetValue(new Vector4(wetness, material.PuddleAmount, 1f / Math.Max(material.PuddleSize, 0.1f),
                    material.HasDisplacement ? 1 : 0));
                _Ripples.SetValue(material.WeatherWetness ? SceneRipples : 0f);
            }
            if (GameSettings.d_defaultmaterial)
            {
                _Material_DiffuseColor.SetValue(Color.Gray.ToVector3());
                _Material_Roughness.SetValue(GameSettings.m_defaultroughness > 0
                        ? GameSettings.m_defaultroughness
                        : 0.3f);
                _Material_Metallic.SetValue(0.0f);
                _Material_MaterialType.SetValue(0);
                _gbufferShader.CurrentTechnique = _DrawBasic;
            }
            else
            {
                if (material.HasDisplacement && material.HasDiffuse && material.HasNormalMap && !wet)
                {
                    _Material_Texture.SetValue(material.AlbedoMap);
                    _Material_NormalMap.SetValue(material.NormalMap);
                    _Material_DisplacementMap.SetValue(material.DisplacementMap);
                    _gbufferShader.CurrentTechnique =
                        _DrawTextureDisplacement;
                }
                else if (material.HasMask && material.HasDiffuse)
                {
                    if (material.HasNormalMap && material.HasRoughnessMap)
                    {
                        _Material_MaskMap.SetValue(material.Mask);
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_NormalMap.SetValue(material.NormalMap);
                        _Material_RoughnessMap.SetValue(material.RoughnessMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureSpecularNormalMask;
                    }

                    else if (material.HasNormalMap)
                    {
                        _Material_MaskMap.SetValue(material.Mask);
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_NormalMap.SetValue(material.NormalMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureNormalMask;
                    }

                    else if (material.HasRoughnessMap)
                    {
                        _Material_MaskMap.SetValue(material.Mask);
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_RoughnessMap.SetValue(material.RoughnessMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureSpecularMask;
                    }
                    else
                    {
                        _Material_MaskMap.SetValue(material.Mask);
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureMask;
                    }
                }
                else
                {
                    if (material.HasNormalMap && material.HasRoughnessMap && material.HasDiffuse &&
                        material.HasMetallic)
                    {
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_NormalMap.SetValue(material.NormalMap);
                        _Material_RoughnessMap.SetValue(material.RoughnessMap);
                        _Material_MetallicMap.SetValue(material.MetallicMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureSpecularNormalMetallic;
                    }

                    else if (material.HasNormalMap && material.HasRoughnessMap && material.HasDiffuse)
                    {
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_NormalMap.SetValue(material.NormalMap);
                        _Material_RoughnessMap.SetValue(material.RoughnessMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureSpecularNormal;
                    }

                    else if (material.HasNormalMap && material.HasDiffuse)
                    {
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_NormalMap.SetValue(material.NormalMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureNormal;
                    }

                    else if (material.HasMetallic && material.HasRoughnessMap && material.HasDiffuse)
                    {
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_RoughnessMap.SetValue(material.RoughnessMap);
                        _Material_MetallicMap.SetValue(material.MetallicMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureSpecularMetallic;
                    }

                    else if (material.HasRoughnessMap && material.HasDiffuse)
                    {
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _Material_RoughnessMap.SetValue(material.RoughnessMap);
                        _gbufferShader.CurrentTechnique =
                            _DrawTextureSpecular;
                    }

                    else if (material.HasNormalMap && !material.HasDiffuse)
                    {
                        _Material_NormalMap.SetValue(material.NormalMap);
                        _gbufferShader.CurrentTechnique = _DrawNormal;
                    } 

                    else if (material.HasDiffuse)
                    {
                        _Material_Texture.SetValue(material.AlbedoMap);
                        _gbufferShader.CurrentTechnique = _DrawTexture;
                    }

                    else
                    {
                        _gbufferShader.CurrentTechnique = _DrawBasic;
                    }
                }


                if (!material.HasDiffuse)
                {
                    if (material.Type == MaterialEffect.MaterialTypes.Emissive && material.EmissiveStrength > 0)
                    {
                        _Material_DiffuseColor.SetValue(material.DiffuseColor);
                        _Material_Metallic.SetValue(material.EmissiveStrength / 8);
                    }
                    //* Math.Max(material.EmissiveStrength,1));
                    //}
                    else
                        //{
                        _Material_DiffuseColor.SetValue(material.DiffuseColor);
                    //}
                }

                //m_defaultroughness only applies to the default-material debug view above; here the material's own value is used
                if (!material.HasRoughnessMap)
                    _Material_Roughness.SetValue(material.Roughness);
                //Emissive materials carry their strength in the metallic channel (set above); don't overwrite it
                if (material.Type != MaterialEffect.MaterialTypes.Emissive || material.EmissiveStrength <= 0 || material.HasDiffuse)
                    _Material_Metallic.SetValue(material.Metallic);

                if (material.Type == MaterialEffect.MaterialTypes.SubsurfaceScattering)
                {
                    if(GameSettings.sdf_subsurface)
                        _Material_MaterialType.SetValue(material.MaterialTypeNumber);
                    else
                        _Material_MaterialType.SetValue(0);
                }
                // Wet Surface bakes its look into the G-buffer and lights like a basic material; type 7 flags it for
                // the environment pass's view-dependent puddle reflections.
                else if (wet)
                    _Material_MaterialType.SetValue((int)MaterialEffect.MaterialTypes.WetSurface);
                else
                _Material_MaterialType.SetValue(material.MaterialTypeNumber);
            }
        }

        public void Dispose()
        {
            _clearShader?.Dispose();
            _gbufferShader?.Dispose();
            _fullScreenTriangle?.Dispose();
        }
    }
}
