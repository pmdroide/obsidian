using System;
using System.Collections.Generic;
using Engine.Components;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// The water of one <see cref="GameObjectRole.Water"/> gameobject: its world-space XY footprint and
    /// top surface. With a water material the surface follows the same waves the shader draws.
    /// </summary>
    public readonly struct WaterVolume
    {
        public readonly Vector2 Min;
        public readonly Vector2 Max;
        public readonly float SurfaceZ;
        public readonly bool HasWaves;
        public readonly WaterWaveSettings Waves;

        public WaterVolume(Vector2 min, Vector2 max, float surfaceZ, WaterWaveSettings? waves = null)
        {
            Min = min;
            Max = max;
            SurfaceZ = surfaceZ;
            HasWaves = waves.HasValue;
            Waves = waves ?? default;
        }

        public bool Contains(float x, float y) => x >= Min.X && x <= Max.X && y >= Min.Y && y <= Max.Y;

        public float SurfaceHeight(Vector2 xy, float time) =>
            SurfaceZ + (HasWaves ? WaterWaves.HeightAt(xy, time, Waves) : 0);

        /// <summary>The water volume of an enabled Water-role gameobject, or null.</summary>
        public static WaterVolume? From(BasicEntity entity)
        {
            if (entity == null || entity.Role != GameObjectRole.Water || !entity.IsEnabled) return null;

            BoundingBox local = entity.BoundingBox;
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            Matrix world = Matrix.CreateScale(entity.Scale) * entity.RotationMatrix * Matrix.CreateTranslation(entity.Position);
            foreach (Vector3 corner in local.GetCorners())
            {
                Vector3 p = Vector3.Transform(corner, world);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (!float.IsFinite(min.X) || !float.IsFinite(max.Z)) return null;

            return new WaterVolume(new Vector2(min.X, min.Y), new Vector2(max.X, max.Y), max.Z, WavesOf(entity));
        }

        public static void Collect(IReadOnlyList<BasicEntity> entities, List<WaterVolume> into)
        {
            into.Clear();
            for (int i = 0; i < entities.Count; i++)
                if (From(entities[i]) is WaterVolume volume) into.Add(volume);
        }

        // The rendered surface: an enabled Material component wins over the model's own material.
        private static WaterWaveSettings? WavesOf(BasicEntity entity)
        {
            foreach (var material in entity.GetComponents<MaterialComponent>())
                if (material.Enabled)
                    return material.MaterialType == MaterialEffect.MaterialTypes.Water ? WaterWaveSettings.From(material) : null;
            return entity.Material?.Type == MaterialEffect.MaterialTypes.Water ? WaterWaveSettings.From(entity.Material) : null;
        }
    }
}
