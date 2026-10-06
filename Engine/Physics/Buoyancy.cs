using System;
using System.Collections.Generic;
using BepuPhysics;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// Floats dynamic bodies in <see cref="WaterVolume"/>s. The body's bounds are split into a 3x3x3
    /// grid; every cell under water pushes up at its own position (so tilted bodies right themselves and
    /// ride the waves) and is slowed by the water.
    /// </summary>
    public static class Buoyancy
    {
        private const int Samples = 3;

        /// <param name="localMin">Collider bounds in body-local space, relative to the body's centre of mass.</param>
        /// <param name="strength">Upward push when fully submerged, relative to the body's weight.</param>
        /// <param name="drag">Fraction of the motion through water removed per second.</param>
        /// <returns>True when any part of the body is in water.</returns>
        public static bool Apply(PhysicsSystem physics, BodyHandle body, Vector3 localMin, Vector3 localMax,
            float mass, float strength, float drag, IReadOnlyList<WaterVolume> water, float time, float dt)
        {
            if (water.Count == 0 || !(dt > 0) || !(mass > 0)) return false;

            physics.GetBodyState(body, out Vector3 center, out Quaternion orientation,
                out Vector3 linearVelocity, out Vector3 angularVelocity);
            Vector3 cell = (localMax - localMin) / Samples;
            Matrix rotation = Matrix.CreateFromQuaternion(orientation);
            // Vertical extent of one rotated cell: how deep a sample goes from dry to fully submerged.
            float cellHeight = Math.Max(Math.Abs(rotation.M13) * cell.X + Math.Abs(rotation.M23) * cell.Y +
                                        Math.Abs(rotation.M33) * cell.Z, 0.01f);
            float gravity = Math.Max(-physics.Gravity.Z, 0);
            float cellMass = mass / (Samples * Samples * Samples);
            float dragFactor = Math.Min(drag * dt, 1);
            bool wet = false;

            for (int x = 0; x < Samples; x++)
            for (int y = 0; y < Samples; y++)
            for (int z = 0; z < Samples; z++)
            {
                Vector3 offset = Vector3.Transform(localMin + cell * new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), orientation);
                Vector3 point = center + offset;
                float submerged = 0;
                for (int i = 0; i < water.Count; i++)
                {
                    if (!water[i].Contains(point.X, point.Y)) continue;
                    float surface = water[i].SurfaceHeight(new Vector2(point.X, point.Y), time);
                    submerged = Math.Max(submerged, MathHelper.Clamp((surface - point.Z) / cellHeight + 0.5f, 0, 1));
                }
                if (submerged <= 0) continue;

                wet = true;
                Vector3 velocity = linearVelocity + Vector3.Cross(angularVelocity, offset);
                Vector3 impulse = Vector3.UnitZ * (strength * cellMass * gravity * submerged * dt)
                                  - velocity * (cellMass * submerged * dragFactor);
                physics.ApplyImpulse(body, impulse, offset);
            }
            return wet;
        }
    }
}
