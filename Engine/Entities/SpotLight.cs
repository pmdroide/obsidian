using System;
using Microsoft.Xna.Framework;

namespace Engine.Entities
{
    /// <summary>
    /// A point light restricted to a cone. It lives in the scene's PointLights list and shares the
    /// point light pipeline (sphere light volume, cube shadow map, forward/froxel/bake paths); the
    /// shaders multiply the distance attenuation by <see cref="ConeFactor"/>.
    /// </summary>
    public sealed class SpotLight : PointLight
    {
        /// <summary>Cone axis before rotation: straight down, since Z is up.</summary>
        public static readonly Vector3 LocalDirection = -Vector3.UnitZ;

        public const float MinSpotAngle = 1f;
        public const float MaxSpotAngle = 179f;

        //Angle between the cube face axis and its corner rays, acos(1/sqrt(3))
        private const float CubeFaceHalfDiagonal = 0.9553166f;

        private Matrix _rotationMatrix = Matrix.Identity;
        private float _spotAngle;
        private float _innerSpotAngle;

        /// <summary>World space cone axis, unit length.</summary>
        public Vector3 Direction { get; private set; } = LocalDirection;

        /// <summary>cos of the outer / inner half angles, what the shaders consume.</summary>
        public float CosOuter { get; private set; }
        public float CosInner { get; private set; }

        /// <summary>
        /// A spot light shines from its position along <paramref name="direction"/>.
        /// </summary>
        /// <param name="spotAngle">full cone angle in degrees; the light is zero outside it</param>
        /// <param name="innerSpotAngle">full angle in degrees of the fully lit core; the edge fades from here to spotAngle</param>
        public SpotLight(Vector3 position, float radius, Color color, float intensity, Vector3 direction, float spotAngle = 60, float innerSpotAngle = 40, bool castShadows = false, bool isVolumetric = false, int shadowResolution = 256, int softShadowBlurAmount = 0, bool staticShadow = false, float volumeDensity = 1, bool isEnabled = true)
            : this(position, radius, color, intensity, RotationFromDirection(direction), spotAngle, innerSpotAngle, castShadows, isVolumetric, shadowResolution, softShadowBlurAmount, staticShadow, volumeDensity, isEnabled)
        {
        }

        public SpotLight(Vector3 position, float radius, Color color, float intensity, Matrix rotation, float spotAngle = 60, float innerSpotAngle = 40, bool castShadows = false, bool isVolumetric = false, int shadowResolution = 256, int softShadowBlurAmount = 0, bool staticShadow = false, float volumeDensity = 1, bool isEnabled = true)
            : base(position, radius, color, intensity, castShadows, isVolumetric, shadowResolution, softShadowBlurAmount, staticShadow, volumeDensity, isEnabled)
        {
            RotationMatrix = rotation;
            SpotAngle = spotAngle;
            InnerSpotAngle = innerSpotAngle;
        }

        public override Matrix RotationMatrix
        {
            get { return _rotationMatrix; }
            set
            {
                _rotationMatrix = value;
                Vector3 direction = Vector3.TransformNormal(LocalDirection, value);
                Direction = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : LocalDirection;
                HasChanged = true;
            }
        }

        /// <summary>Full cone angle in degrees, clamped to [1, 179].</summary>
        public float SpotAngle
        {
            get { return _spotAngle; }
            set
            {
                _spotAngle = MathHelper.Clamp(float.IsNaN(value) ? 60 : value, MinSpotAngle, MaxSpotAngle);
                UpdateCone();
            }
        }

        /// <summary>Full angle in degrees of the fully lit core, clamped to [0, SpotAngle].</summary>
        public float InnerSpotAngle
        {
            get { return Math.Min(_innerSpotAngle, _spotAngle); }
            set
            {
                _innerSpotAngle = Math.Max(float.IsNaN(value) ? 0 : value, 0);
                UpdateCone();
            }
        }

        private void UpdateCone()
        {
            CosOuter = (float)Math.Cos(MathHelper.ToRadians(_spotAngle * 0.5f));
            CosInner = (float)Math.Cos(MathHelper.ToRadians(InnerSpotAngle * 0.5f));
            HasChanged = true;
        }

        /// <summary>
        /// Smooth cone falloff for a unit vector from the light towards the shaded point.
        /// Mirrors SpotConeFactor in DeferredPointLight.fx, Forward.fx and Froxel.fx.
        /// </summary>
        public static float ConeFactor(Vector3 lightToPoint, Vector3 direction, float cosOuter, float cosInner)
        {
            float t = MathHelper.Clamp((Vector3.Dot(lightToPoint, direction) - cosOuter) / Math.Max(cosInner - cosOuter, 1e-4f), 0, 1);
            return t * t;
        }

        public float ConeFactor(Vector3 lightToPoint) => ConeFactor(lightToPoint, Direction, CosOuter, CosInner);

        /// <summary>
        /// Whether any part of the cone falls into the 90° cube shadow face looking along <paramref name="axis"/>.
        /// Faces outside the cone are skipped when rendering the shadow map.
        /// </summary>
        public bool ConeTouchesCubeFace(Vector3 axis)
        {
            float limit = MathHelper.ToRadians(_spotAngle * 0.5f) + CubeFaceHalfDiagonal;
            if (limit >= MathHelper.Pi) return true;
            return Vector3.Dot(axis, Direction) >= (float)Math.Cos(limit);
        }

        /// <summary>A rotation that turns <see cref="LocalDirection"/> onto <paramref name="direction"/>.</summary>
        public static Matrix RotationFromDirection(Vector3 direction)
        {
            if (direction.LengthSquared() < 1e-8f) return Matrix.Identity;
            direction.Normalize();

            float dot = Vector3.Dot(LocalDirection, direction);
            if (dot > 0.99999f) return Matrix.Identity;
            if (dot < -0.99999f) return Matrix.CreateRotationX(MathHelper.Pi);

            Vector3 axis = Vector3.Normalize(Vector3.Cross(LocalDirection, direction));
            return Matrix.CreateFromAxisAngle(axis, (float)Math.Acos(MathHelper.Clamp(dot, -1, 1)));
        }

        public override TransformableObject Clone
        {
            get { return new SpotLight(Position, Radius, Color, Intensity, RotationMatrix, SpotAngle, InnerSpotAngle, CastShadows, IsVolumetric, ShadowResolution, SoftShadowBlurAmount, StaticShadows, LightVolumeDensity, IsEnabled); }
        }
    }
}
