using System;
using Engine.Recources;
using Engine.Recources.Helper;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Entities
{
    public class PointLight : TransformableObject
    {
        private Vector3 _position = Vector3.Zero;
        public Matrix WorldMatrix;
        private float _radius;
        private Color _color;
        public Vector3 ColorV3;
        public float Intensity;
        public int ShadowMapRadius = 3;

        public bool HasChanged = true;

        /// <summary>Largest shadow tile per cube face; the atlas gives smaller tiles to lights that cover less of the screen.</summary>
        public int ShadowResolution { get => _shadowResolution; set { _shadowResolution = Math.Clamp(value, 64, 2048); HasChanged = true; } }
        private int _shadowResolution;
        /// <summary>Shadows drawn once and kept (the light and what it lights never move).</summary>
        public bool StaticShadows { get; set; }

        // ---- Shadow atlas (Renderer.Lighting.ShadowAtlas, ShadowMapRenderModule) ----
        /// <summary>Atlas tile of each cube face: x, y (texels), size, 1 = in use (spot lights skip faces outside the cone).</summary>
        public readonly Vector4[] ShadowTiles = new Vector4[6];
        /// <summary>Size of this light's tiles in the atlas; 0 = no tiles.</summary>
        public int ShadowTileSize;
        /// <summary>The tiles hold this light's shadows (drawn since they were assigned).</summary>
        public bool ShadowTilesDrawn;
        /// <summary>0..1 this frame: shadows fade out with distance from the camera and vanish on small lights.</summary>
        public float ShadowStrength = 1;
        /// <summary>Whether the lighting passes read shadows for this light this frame.</summary>
        public bool HasShadowThisFrame => CastShadows && ShadowTileSize > 0 && ShadowTilesDrawn && ShadowStrength > 0.001f;

        public Matrix LightViewProjectionPositiveX;
        public Matrix LightViewProjectionNegativeX;
        public Matrix LightViewProjectionPositiveY;
        public Matrix LightViewProjectionNegativeY;
        public Matrix LightViewProjectionPositiveZ;
        public Matrix LightViewProjectionNegativeZ;

        public int[] faceBlurCount = {0, 0, 0, 0, 0, 0};

        public Matrix LightViewSpace;
        public Matrix LightWorldViewProj;

        public BoundingSphere BoundingSphere;

        public bool CastShadows;
        public bool CastSDFShadows;
        public int SoftShadowBlurAmount = 0;

        public readonly bool IsVolumetric;
        public readonly float LightVolumeDensity = 1;

        /// <summary>
        /// Radius (m) of the glowing sphere the light comes from: softer, wider highlights and a brighter core
        /// (Source 2's light source size). 0 = a point.
        /// </summary>
        public float SourceRadius { get => _sourceRadius; set { _sourceRadius = float.IsFinite(value) ? Math.Clamp(value, 0, 10) : 0; HasChanged = true; } }
        private float _sourceRadius;

        /// <summary>
        /// Length (m) of a tube light (a fluorescent tube) along the light's local X axis; 0 = a sphere/point.
        /// </summary>
        public float SourceLength { get => _sourceLength; set { _sourceLength = float.IsFinite(value) ? Math.Clamp(value, 0, 50) : 0; HasChanged = true; } }
        private float _sourceLength;

        /// <summary>World axis of a tube light (<see cref="SourceLength"/>): the light's rotated X axis.</summary>
        public Vector3 TubeAxis
        {
            get
            {
                Vector3 axis = Vector3.TransformNormal(Vector3.UnitX, RotationMatrix);
                return axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitX;
            }
        }

        /// <summary>
        /// Distance falloff of point and spot lights: inverse-square around a core of radius / 5 (or the source radius when
        /// larger), smoothly windowed to 0 at <paramref name="radius"/>. Mirrors LightAttenuation in Shaders/Common/helper.fx.
        /// </summary>
        public static float Attenuation(float distance, float radius, float sourceRadius = 0)
        {
            if (radius <= 0 || distance >= radius) return 0;
            float x = distance / radius;
            float window = Math.Clamp(1 - x * x * x * x, 0, 1);
            float core = Math.Max(radius * 0.2f, sourceRadius);
            return window * window * core * core / (distance * distance + core * core);
        }


        /// <summary>
        /// A point light is a light that shines in all directions
        /// </summary>
        /// <param name="position"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        /// <param name="intensity"></param>
        /// <param name="isVolumetric"></param>
        /// <param name="shadowResolution">shadow map resolution per face. Optional</param>
        /// <param name="staticShadow">if set to true the shadows will not update at all. Dynamic shadows in contrast update only when needed.</param>
        /// <param name="castShadows"></param>
        /// <param name="volumeDensity"></param>
        /// <returns></returns>
        public PointLight(Vector3 position, float radius, Color color, float intensity, bool castShadows, bool isVolumetric, int shadowResolution, int softShadowBlurAmount, bool staticShadow, float volumeDensity = 1, bool isEnabled = true)
        {
            BoundingSphere = new BoundingSphere(position, radius);
            //Point lights only turn to aim a tube light (SourceLength); spot lights set their cone after this
            RotationMatrix = Matrix.Identity;
            Position = position;
            Radius = radius;
            Color = color;
            Intensity = intensity;
            CastShadows = castShadows;
            IsVolumetric = isVolumetric;
            SoftShadowBlurAmount = softShadowBlurAmount;

            _shadowResolution = Math.Clamp(shadowResolution, 64, 2048);
            StaticShadows = staticShadow;
            LightVolumeDensity = volumeDensity;
            IsEnabled = isEnabled;
            Id = IdGenerator.GetNewId();
            Name = GetType().Name + " " + Id;
        }

        public Color Color
        {
            get { return _color; }
            set
            {
                _color = value;
                ColorV3 =  (_color.ToVector3().Pow(2.2f));
            }
        }

        public override Vector3 Position
        {
            get { return _position; }
            set
            {
                _position = value;
                BoundingSphere.Center = value;
                WorldMatrix = Matrix.CreateScale(Radius * 1.1f) * Matrix.CreateTranslation(Position);
                HasChanged = true;
            }
        }

        public override Vector3 Scale { get; set; }

        public float Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                BoundingSphere.Radius = value;
                WorldMatrix = Matrix.CreateScale(Radius * 1.1f) * Matrix.CreateTranslation(Position);
                HasChanged = true;
            }
        }

        public override int Id { get; set; }
        public override Matrix RotationMatrix { get; set; }

        public sealed override bool IsEnabled
        {
            get;
            set;
        }

        public override TransformableObject Clone
        {
            get { return CopyShape(new PointLight(Position, Radius, Color, Intensity, CastShadows, IsVolumetric, ShadowResolution, SoftShadowBlurAmount, StaticShadows)); }
        }

        public override string Name { get; set; }

        /// <summary>Copies the light shape settings onto a clone.</summary>
        protected T CopyShape<T>(T clone) where T : PointLight
        {
            clone.SourceRadius = SourceRadius;
            clone.SourceLength = SourceLength;
            clone.RotationMatrix = RotationMatrix;
            return clone;
        }

        protected PointLight()
        {

        }

        public virtual void ApplyShader(Matrix inverseView)
        {
            

        }
    }
    
}
