using System;
using Microsoft.Xna.Framework;

namespace Engine.Entities
{
    /// <summary>
    /// Editor-only camera. Tracks pitch/yaw as scalars so the renderer can clamp pitch
    /// and rebuild the forward vector without accumulating drift from repeated
    /// orientation deltas applied to the base <see cref="Camera"/>'s forward vector.
    /// Lives on <see cref="Engine.Logic.MainSceneLogic"/>; never serialised to <c>.obsc</c>.
    /// </summary>
    public class EditorCamera : Camera
    {
        // Pitch clamp keeps the camera from flipping past straight up/down.
        private const float PitchLimitRadians = 1.55f; // ~88.8°

        public float Yaw;   // radians, rotation around world up (Z)
        public float Pitch; // radians, rotation around the right vector

        public EditorCamera(Vector3 position, Vector3 lookat) : base(position, lookat)
        {
            DeriveYawPitchFromForward();
        }

        /// <summary>Rebuilds the camera's forward vector from <see cref="Yaw"/> + <see cref="Pitch"/>.</summary>
        public void ApplyYawPitch()
        {
            if (Pitch > PitchLimitRadians) Pitch = PitchLimitRadians;
            if (Pitch < -PitchLimitRadians) Pitch = -PitchLimitRadians;

            float cp = (float)Math.Cos(Pitch);
            // Z is up in this engine. Yaw rotates around Z.
            Vector3 fwd = new Vector3(
                cp * (float)Math.Cos(Yaw),
                cp * (float)Math.Sin(Yaw),
                (float)Math.Sin(Pitch));
            fwd.Normalize();
            Forward = fwd;
        }

        public Vector3 Right
        {
            get
            {
                Vector3 r = Vector3.Cross(Forward, Up);
                if (r.LengthSquared() < 1e-6f) return Vector3.UnitX;
                r.Normalize();
                return r;
            }
        }

        private void DeriveYawPitchFromForward()
        {
            Vector3 f = Forward;
            f.Normalize();
            Pitch = (float)Math.Asin(MathHelper.Clamp(f.Z, -1f, 1f));
            Yaw = (float)Math.Atan2(f.Y, f.X);
        }
    }
}
