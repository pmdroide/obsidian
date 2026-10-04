using Microsoft.Xna.Framework;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Per-scene baked lighting settings (Anvil > Inspector > Lighting). Saved inside the .obsc
    /// scene file; the baked result lives in a sidecar <c>.probes</c> file (see
    /// <see cref="ProbeVolumeData"/>). Properties (not fields) so System.Text.Json round-trips them.
    ///
    /// Mutated on the game thread only (the editor goes through IEditorBridge.EnqueueMutateLighting);
    /// a bake works on a <see cref="Clone"/> so edits during a bake don't race the worker.
    /// </summary>
    public sealed class LightingSettings
    {
        // ---------------- Runtime ----------------

        //Use the baked probe volume for diffuse ambient instead of the environment cubemap
        public bool ProbeVolumeEnabled { get; set; } = true;

        //Multiplier on the baked irradiance at runtime (no rebake needed)
        public float Intensity { get; set; } = 1f;

        //Draw every probe (coloured by its average irradiance) and the volume bounds in the editor
        public bool ShowProbes { get; set; }

        // ---------------- Volume layout ----------------

        //Fit the volume to the enabled entities' bounds (+ padding) instead of BoundsMin/Max
        public bool AutoBounds { get; set; } = true;
        public float BoundsPadding { get; set; } = 1f;
        public Vector3 BoundsMin { get; set; } = new Vector3(-20, -20, 0);
        public Vector3 BoundsMax { get; set; } = new Vector3(20, 20, 10);

        //World units between neighbouring probes. Raised automatically when an axis would
        //exceed MaxProbesPerAxis.
        public float ProbeSpacing { get; set; } = 2f;
        public int MaxProbesPerAxis { get; set; } = 48;

        // ---------------- Quality ----------------

        //Rays traced per probe per bounce
        public int SamplesPerProbe { get; set; } = 256;

        //Indirect bounces. 1 = light reflected once off surfaces; each extra bounce re-traces using
        //the previous pass's probes for the lighting at the hit points (DDGI-style).
        public int Bounces { get; set; } = 2;

        //Fraction of back-face hits above which a probe counts as inside geometry; such probes
        //are replaced with the average of their valid neighbours to stop dark leaks.
        public float ValidityThreshold { get; set; } = 0.25f;

        // ---------------- Sky ----------------

        //Radiance of rays that escape the scene. Colour is sRGB 0..1 (the baker linearises it).
        public Vector3 SkyColor { get; set; } = new Vector3(0.55f, 0.7f, 1f);
        public float SkyIntensity { get; set; } = 0.3f;

        public LightingSettings Clone() => (LightingSettings)MemberwiseClone();
    }
}
