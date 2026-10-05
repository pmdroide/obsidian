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

        public List<DirectionalLight> Apply(Scene scene, GameTime time, GraphicsDevice device,
            Assets assets, DeferredEnvironmentMapRenderModule module)
        {
            var settings = scene.Environment;
            bool changed = _scene != scene || _settings != settings;
            if (changed)
            {
                _scene = scene;
                _settings = settings;
                _elapsed = 0;
                _lastProbeUpdate = -1;
                if (scene.EnvironmentSample != null) scene.EnvironmentSample.NeedsUpdate = true;
            }
            else _elapsed += Math.Max(0, time.ElapsedGameTime.TotalSeconds);

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
            if (!settings.DayNightCycle) return scene.DirectionalLights;

            _sun ??= new DirectionalLight(Color.White, 1, -sunDirection) { Name = "Environment Sun" };
            _sun.Direction = -sunDirection;
            _sun.Intensity = 3 * daylight;
            _sun.Color = Color.Lerp(new Color(255, 145, 85), new Color(255, 248, 230),
                MathHelper.Clamp(sunDirection.Z * 2, 0, 1));
            _lights.Clear();
            _lights.AddRange(scene.DirectionalLights);
            _lights.Add(_sun);
            // Throttle expensive reflection captures while the visible sky animates every frame.
            if (_elapsed - _lastProbeUpdate >= 0.5)
            {
                if (scene.EnvironmentSample != null) scene.EnvironmentSample.NeedsUpdate = true;
                _lastProbeUpdate = _elapsed;
            }
            return _lights;
        }

        public void Dispose()
        {
            _customSky?.Dispose();
            _customSky = null;
            _loadedPath = null;
        }
    }
}
