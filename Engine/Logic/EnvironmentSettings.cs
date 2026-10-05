using System;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>Scene-owned sky selection and initial time for the animated sky.</summary>
    public sealed class EnvironmentSettings
    {
        public bool DayNightCycle { get; set; }
        public string SkyboxPath { get; set; }
        public float TimeOfDay { get; set; } = 12;
        public float CycleDurationMinutes { get; set; } = 10;

        public EnvironmentSettings Clone() => (EnvironmentSettings)MemberwiseClone();

        public void Normalize()
        {
            TimeOfDay = float.IsFinite(TimeOfDay) ? Math.Clamp(TimeOfDay, 0, 24) % 24 : 12;
            CycleDurationMinutes = float.IsFinite(CycleDurationMinutes)
                ? Math.Clamp(CycleDurationMinutes, 0.1f, 1440) : 10;
        }

        public static float AdvanceHour(float initialHour, double elapsedSeconds, float durationMinutes) =>
            (float)((initialHour + elapsedSeconds * 24 / (Math.Max(0.1f, durationMinutes) * 60)) % 24);

        public static Vector3 SunDirection(float hour)
        {
            float angle = (hour - 6) / 24 * MathHelper.TwoPi;
            // Z is up. A slight Y component avoids the light's look-at pole at noon.
            return Vector3.Normalize(new Vector3((float)Math.Cos(angle), 0.15f, (float)Math.Sin(angle)));
        }
    }
}
