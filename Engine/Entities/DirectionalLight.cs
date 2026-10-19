using System;
using Engine.Recources;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Entities
{
    public sealed class DirectionalLight : TransformableObject
    {
        /// <summary>Most cascades a sun shadow can have (MAX_CASCADES in DeferredDirectionalLight.fx and Froxel.fx).</summary>
        public const int MaxCascades = 4;

        public float Intensity;
        private Vector3 _direction;
        private Vector3 _initialDirection;
        private Vector3 _position;
        public bool HasChanged;

        public Vector3 DirectionViewSpace;

        private bool _castShadows;
        private int _shadowResolution;
        private int _cascadeCount = MaxCascades;
        private float _shadowDistance = 150;
        private float _splitLambda = 0.85f; // HLA csm_split_log_scalar (Docs/Research/Source2Look R5)

        /// <summary>Whether the light casts (cascaded) shadows.</summary>
        public bool CastShadows { get => _castShadows; set { if (_castShadows != value) { _castShadows = value; HasChanged = true; } } }
        /// <summary>Legacy fixed shadow box width; kept for older scenes. Cascades follow the camera instead.</summary>
        public float ShadowSize { get; set; }
        /// <summary>How far (m) towards the light shadow casters outside the camera's view are still caught.</summary>
        public float ShadowDepth { get; set; }
        /// <summary>Resolution of one cascade (the atlas holds up to 2 x 2 of them).</summary>
        public int ShadowResolution
        {
            get => _shadowResolution;
            set
            {
                int clamped = Math.Clamp(value, 128, 4096);
                if (_shadowResolution == clamped) return;
                _shadowResolution = clamped;
                ShadowMap?.Dispose();
                ShadowMap = null;
                HasChanged = true;
            }
        }
        /// <summary>Shadow cascades, 1-4 (Source 2: csm_max_num_cascades_override).</summary>
        public int CascadeCount
        {
            get => _cascadeCount;
            set
            {
                int clamped = Math.Clamp(value, 1, MaxCascades);
                if (_cascadeCount == clamped) return;
                _cascadeCount = clamped;
                ShadowMap?.Dispose();
                ShadowMap = null;
                HasChanged = true;
            }
        }
        /// <summary>Distance (m) from the camera where sun shadows end, fading out over the last cascade's last tenth.</summary>
        public float ShadowDistance { get => _shadowDistance; set { _shadowDistance = float.IsFinite(value) ? Math.Clamp(value, 5, 5000) : 150; HasChanged = true; } }
        /// <summary>0 = evenly spaced cascades, 1 = logarithmic (most detail near the camera). Practical split scheme.</summary>
        public float SplitLambda { get => _splitLambda; set { _splitLambda = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.85f; HasChanged = true; } }

        /// <summary>The cascade atlas: cascade i sits in tile (i % 2, i / 2) of <see cref="ShadowResolution"/> texels.</summary>
        public RenderTarget2D ShadowMap;
        public Matrix ShadowViewProjection;

        public ShadowFilteringTypes ShadowFiltering;
        /// <summary>Obsolete (screen space shadow blur was never finished); saved for older scenes, ignored.</summary>
        public bool ScreenSpaceShadowBlur;

        /// <summary>World to light clip space of the outermost cascade (the editor draws its box).</summary>
        public Matrix LightViewProjection;
        public Matrix LightView;

        // ---- Cascades (ShadowMapRenderModule.FitCascades), world space ----
        public readonly Matrix[] CascadeViewProjection = new Matrix[MaxCascades];
        public readonly Matrix[] CascadeView = new Matrix[MaxCascades];
        /// <summary>View distance (camera Z) where each cascade ends.</summary>
        public Vector4 CascadeEnds;
        /// <summary>Light view far clip of each cascade (depth = light view Z / -far).</summary>
        public Vector4 CascadeFarClip;
        /// <summary>World size of one shadow texel in each cascade (bias, filtering).</summary>
        public Vector4 CascadeTexelSize;
        /// <summary>Cascades fitted this frame (CascadeCount, or 0 before the first shadow pass).</summary>
        public int ActiveCascades;
        /// <summary>Texels per side of one cascade: ShadowResolution, at most 2048 with more than one cascade (atlas memory).</summary>
        public int CascadeResolution => CascadeCount > 1 ? Math.Min(ShadowResolution, 2048) : ShadowResolution;

        // View space versions for the deferred shader (LightAccumulationModule)
        public readonly Matrix[] CascadeViewProjectionVS = new Matrix[MaxCascades];
        public readonly Matrix[] CascadeViewVS = new Matrix[MaxCascades];

        public enum ShadowFilteringTypes
        {
            PCF, SoftPCF3x, SoftPCF5x, Poisson, /*VSM*/
        }

        /// <summary>
        /// Create a Directional light, shadows are optional
        /// </summary>
        public DirectionalLight(Color color, float intensity, Vector3 direction,Vector3 position = default(Vector3), bool castShadows = false, float shadowSize = 100, float shadowDepth = 100, int shadowResolution = 512, ShadowFilteringTypes shadowFiltering = ShadowFilteringTypes.Poisson, bool screenspaceshadowblur = false, bool staticshadows = false)
        {
            Color = color;
            Intensity = intensity;

            Vector3 normalizedDirection = direction;
            normalizedDirection.Normalize();
            Direction = normalizedDirection;
            _initialDirection = normalizedDirection;

            _castShadows = castShadows;
            ShadowSize = shadowSize;
            ShadowDepth = shadowDepth;
            _shadowResolution = Math.Clamp(shadowResolution, 128, 4096);

            ScreenSpaceShadowBlur = screenspaceshadowblur;

            ShadowFiltering = shadowFiltering;

            Position = position;

            Id = IdGenerator.GetNewId();

            IsEnabled = true;

            TransformDirectionToAngles();

            _rotationMatrix = Matrix.Identity;

            Name = GetType().Name + " " + Id;
        }

        public Color Color
        {
            get { return _color; }
            set
            {
                _color = value;
                ColorV3 = (_color.ToVector3().Pow(2.2f));
            }
        }

        public Vector3 Direction
        {
            get { return _direction; }
            set
            {
                _direction = value;
                HasChanged = true;
            }
        }

        public override Vector3 Position
        {
            get { return _position; }
            set
            {
                _position = value;
                HasChanged = true;
            }
        }

        public override Vector3 Scale { get; set; }

        private int _id;

        public override int Id
        {
            get { return _id; }
            set { _id = value; }
        }

        public override Matrix RotationMatrix
        {
            get { return _rotationMatrix; }
            set
            {
                _rotationMatrix = value;
                TransformAnglesToDirection();
            }
        }

        public override bool IsEnabled { get; set; }

        private void TransformAnglesToDirection()
        {
            Direction = Vector3.Transform(_initialDirection, RotationMatrix);
        }

        private Matrix Trafo;
        private Matrix _rotationMatrix;
        private Color _color;
        public Vector3 ColorV3;

        private void TransformDirectionToAngles()
        {
            Trafo = Matrix.CreateLookAt(Vector3.Zero, Direction, Vector3.UnitZ);
        }

        /// <summary>Copies the shadow settings (not the cascade data) from another light.</summary>
        public void CopyShadowSettings(DirectionalLight from)
        {
            if (from == null) return;
            CastShadows = from.CastShadows;
            ShadowSize = from.ShadowSize;
            ShadowDepth = from.ShadowDepth;
            ShadowResolution = from.ShadowResolution;
            ShadowFiltering = from.ShadowFiltering;
            CascadeCount = from.CascadeCount;
            if (ShadowDistance != from.ShadowDistance) ShadowDistance = from.ShadowDistance;
            if (SplitLambda != from.SplitLambda) SplitLambda = from.SplitLambda;
        }

        public override TransformableObject Clone
        {
            get
            {
                var clone = new DirectionalLight(Color, Intensity, Direction, Position, CastShadows, ShadowSize, ShadowDepth, ShadowResolution, ShadowFiltering, false);
                clone.CopyShadowSettings(this);
                return clone;
            }
        }

        public override string Name { get; set; }

        public void ApplyShader()
        {
            if (CastShadows && ShadowMap != null && ActiveCascades > 0)
            {
                Shaders.deferredDirectionalLightParameter_CascadeViewProjection?.SetValue(CascadeViewProjectionVS);
                Shaders.deferredDirectionalLightParameter_CascadeView?.SetValue(CascadeViewVS);
                Shaders.deferredDirectionalLightParameter_CascadeEnds?.SetValue(CascadeEnds);
                Shaders.deferredDirectionalLightParameter_CascadeFarClip?.SetValue(CascadeFarClip);
                Shaders.deferredDirectionalLightParameter_CascadeTexelSize?.SetValue(CascadeTexelSize);
                Shaders.deferredDirectionalLightParameter_CascadeCount?.SetValue((float)ActiveCascades);
                Shaders.deferredDirectionalLightParameter_ShadowMap.SetValue(ShadowMap);
                Shaders.deferredDirectionalLightParameter_ShadowFiltering.SetValue((int)ShadowFiltering);
                Shaders.deferredDirectionalLightParameter_ShadowMapSize.SetValue((float)CascadeResolution);
                Shaders.deferredDirectionalLightShadowed.Passes[0].Apply();
            }
            else
            {
                Shaders.deferredDirectionalLightUnshadowed.Passes[0].Apply();
            }
        }
    }
}
