using System;
using System.Collections.Generic;
using System.IO;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.RenderModules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DirectionalLight = Engine.Entities.DirectionalLight;

namespace Engine.Renderer
{
    /// <summary>Owns runtime sky textures and the cycle's light; saved scene lights are untouched.</summary>
    internal sealed class EnvironmentSky : IDisposable
    {
        private Scene _scene;
        private EnvironmentSettings _settings;
        private Texture2D _customSky;
        private string _loadedPath;
        private double _elapsed;
        private double _lastProbeUpdate;
        private DirectionalLight _sun;
        private readonly List<DirectionalLight> _lights = new List<DirectionalLight>();

        private const float DefaultSunIntensity = 100;
        // Moonlight relative to the sun's peak; also the night floor for baked ambient.
        private const float MoonIntensity = 0.08f;
        // Cloud drift in noise units per second at CloudSpeed 1; must match CLOUD_PERIOD in clouds.fx.
        private const double CloudDrift = 0.015;
        private const double CloudPeriod = 256;
        private double _cloudOffsetX;
        private double _cloudOffsetY;

        /// <summary>Multiplier for baked probe ambient; below 1 at night while the cycle runs.</summary>
        public float AmbientScale { get; private set; } = 1;

        /// <summary>Exposure offset in EV the cycle adds on top of the post-processing exposure.</summary>
        public float ExposureOffset { get; private set; }

        public List<DirectionalLight> Apply(Scene scene, GameTime time, GraphicsDevice device,
            Assets assets, DeferredEnvironmentMapRenderModule module)
        {
            var settings = scene.Environment;
            double delta = Math.Max(0, time.ElapsedGameTime.TotalSeconds);
            // Only timing edits restart the cycle, so cloud tweaks leave the current hour alone.
            bool restart = _scene != scene || _settings == null || settings.DayNightCycle != _settings.DayNightCycle ||
                settings.TimeOfDay != _settings.TimeOfDay || settings.CycleDurationMinutes != _settings.CycleDurationMinutes;
            if (restart)
            {
                _elapsed = 0;
                _lastProbeUpdate = -1;
            }
            else _elapsed += delta;
            if (_scene != scene || _settings != settings)
            {
                _scene = scene;
                _settings = settings;
                if (scene.EnvironmentSample != null) scene.EnvironmentSample.NeedsUpdate = true;
            }

            // Accumulate drift in doubles and wrap it on the shader's noise period so it never loses precision.
            _cloudOffsetX = (_cloudOffsetX + delta * CloudDrift * 0.8 * settings.CloudSpeed) % CloudPeriod;
            _cloudOffsetY = (_cloudOffsetY + delta * CloudDrift * 0.6 * settings.CloudSpeed) % CloudPeriod;
            module.SetClouds(settings.CloudCoverage, new Vector2((float)_cloudOffsetX, (float)_cloudOffsetY));

            if (_loadedPath != settings.SkyboxPath)
            {
                Texture2D next = null;
                if (!string.IsNullOrWhiteSpace(settings.SkyboxPath))
                {
                    try
                    {
                        string root = Path.GetFullPath(Globals.content.RootDirectory);
                        string path = Path.GetFullPath(Path.Combine(root, settings.SkyboxPath));
                        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Skybox must be inside Content.");
                        using var stream = File.OpenRead(path);
                        next = Texture2D.FromStream(device, stream);
                    }
                    catch (Exception ex) { EditorBridge.Log("Load skybox: " + ex.Message); }
                }
                // Bind the replacement before disposing the texture it supersedes.
                module.SkyTexture = next ?? assets.SkyTexture;
                _customSky?.Dispose();
                _customSky = next;
                _loadedPath = settings.SkyboxPath;
            }

            module.SkyCubemap = assets.SkyCubemap;
            module.SkyTexture = _customSky ?? assets.SkyTexture;
            float hour = EnvironmentSettings.AdvanceHour(settings.TimeOfDay, _elapsed, settings.CycleDurationMinutes);
            Vector3 sunDirection = EnvironmentSettings.SunDirection(hour);
            float daylight = MathHelper.Clamp((sunDirection.Z + 0.12f) / 0.3f, 0, 1);
            module.SetDayNightCycle(settings.DayNightCycle, sunDirection, daylight);
            AmbientScale = 1;
            ExposureOffset = 0;
            if (!settings.DayNightCycle) return scene.DirectionalLights;
            module.SetSkyLook(settings);
            // The tonemapper lifts dark values a lot, so nights need less exposure to read as night.
            ExposureOffset = MathHelper.Lerp(settings.NightExposure, settings.DayExposure, Smoothstep(0, 1, daylight));

            // The cycle owns sky lighting: scene directional lights would otherwise keep the night lit.
            // The brightest one supplies peak intensity and shadow settings; the saved lights are untouched.
            DirectionalLight template = null;
            foreach (DirectionalLight light in scene.DirectionalLights)
                if (light.IsEnabled && (template == null || light.Intensity > template.Intensity)) template = light;
            float peak = template != null && template.Intensity > 0 ? template.Intensity : DefaultSunIntensity;
            EnsureCycleLight(template);

            // Sun and moon share one shadowed light; it swaps sides while both sit on the horizon at zero.
            Vector3 moonDirection = EnvironmentSettings.MoonDirection(hour);
            if (sunDirection.Z >= 0)
            {
                _sun.Direction = -sunDirection;
                _sun.Intensity = peak * settings.SunBrightness * Smoothstep(0, 0.25f, sunDirection.Z);
                _sun.Color = Color.Lerp(new Color(255, 145, 85), new Color(255, 248, 230),
                    MathHelper.Clamp(sunDirection.Z * 2, 0, 1));
            }
            else
            {
                _sun.Direction = -moonDirection;
                _sun.Intensity = peak * MoonIntensity * settings.MoonBrightness * Smoothstep(0, 0.25f, moonDirection.Z);
                _sun.Color = new Color(170, 190, 255);
            }
            // Heavy cloud cover softens direct light; light scattered clouds barely change it.
            _sun.Intensity *= 1 - 0.5f * settings.CloudCoverage * settings.CloudCoverage;
            // Baked probes hold the daytime bounce; dim them toward the moonlight level at night.
            AmbientScale = MathHelper.Lerp(Math.Min(1, MoonIntensity * settings.MoonBrightness), 1, daylight);
            _lights.Clear();
            _lights.Add(_sun);
            // Throttle expensive reflection captures while the visible sky animates every frame.
            if (_elapsed - _lastProbeUpdate >= 0.5)
            {
                if (scene.EnvironmentSample != null) scene.EnvironmentSample.NeedsUpdate = true;
                _lastProbeUpdate = _elapsed;
            }
            return _lights;
        }

        /// <summary>Shadow settings are read-only on a light, so rebuild the cycle light when the template's change.</summary>
        private void EnsureCycleLight(DirectionalLight template)
        {
            bool shadows = template?.CastShadows ?? true;
            float size = template?.ShadowSize ?? 450;
            float depth = template?.ShadowDepth ?? 180;
            int resolution = template?.ShadowResolution ?? 1024;
            var filtering = template?.ShadowFiltering ?? DirectionalLight.ShadowFilteringTypes.SoftPCF3x;
            bool blur = template?.ScreenSpaceShadowBlur ?? false;
            Vector3 position = template?.Position ?? Vector3.UnitZ * 2;

            if (_sun == null || _sun.CastShadows != shadows || _sun.ShadowSize != size || _sun.ShadowDepth != depth ||
                _sun.ShadowResolution != resolution || _sun.ShadowFiltering != filtering || _sun.ScreenSpaceShadowBlur != blur)
            {
                _sun?.ShadowMap?.Dispose();
                _sun = new DirectionalLight(Color.White, 0, -Vector3.UnitZ, position, shadows, size, depth, resolution,
                    filtering, blur) { Name = "Environment Sun" };
            }
            if (_sun.Position != position) _sun.Position = position;
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = MathHelper.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        public void Dispose()
        {
            _sun?.ShadowMap?.Dispose();
            _sun = null;
            _customSky?.Dispose();
            _customSky = null;
            _loadedPath = null;
        }
    }
}
