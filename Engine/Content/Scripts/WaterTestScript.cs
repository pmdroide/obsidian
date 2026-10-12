using System.Globalization;
using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// The WaterTest sample scene (Content/Scenes/WaterTest.obsc): a swimming pool and a wave tank under rain, with
/// floating rafts, buoys, a barrel and a boat. Runs on the Main Camera next to Freecam and drops things into the
/// water so buoyancy, splashes (droplets, spray, rings and foam) and the water shader can be tested:
///   Click     drop the selected object above the water under the mouse
///   1-4       beach ball · crate · plank · boulder (sinks)       Up / Down   drop height
///   Space     a volley of random objects over the pool           X           a splash under the mouse, no object
///   V         pool waves calm / gentle / choppy / storm          G           auto drop on / off
///   R         rain on / off      [ ]  rain intensity              T           time of day +3 h
///   C         clear everything dropped
/// Dropped objects are removed when Play stops; the waves and weather go back too.
/// </summary>
public sealed class WaterTestScript : ScriptBehaviour
{
    public const string ScriptId = "water-test";
    public const int MaxDropped = 40;

    private const string DocumentPath = "UI/WaterTest";

    /// <summary>What can be dropped: model, size, mass, float strength (below 1 sinks) and colour.</summary>
    public sealed record DropKind(string Name, string Model, Vector3 Scale, float Mass, float Buoyancy, Vector3 Color, float Roughness);

    public static readonly DropKind[] Kinds =
    {
        new("Beach Ball", "IsoSphere", new Vector3(0.35f), 2, 6f, new Vector3(0.95f, 0.3f, 0.22f), 0.35f),
        new("Crate", "Cube", new Vector3(0.4f), 80, 1.8f, new Vector3(0.55f, 0.38f, 0.22f), 0.8f),
        new("Plank", "Cube", new Vector3(1.2f, 0.18f, 0.1f), 30, 2.2f, new Vector3(0.72f, 0.55f, 0.36f), 0.75f),
        new("Boulder", "IsoSphere", new Vector3(0.45f), 400, 0.4f, new Vector3(0.42f, 0.42f, 0.44f), 0.9f),
    };

    /// <summary>Pool wave presets for V: name, crest-to-trough height (m), Wave Scale.</summary>
    public static readonly (string Name, float Height, float Scale)[] WavePresets =
    {
        ("Calm", 0f, 0.6f), ("Gentle", 0.15f, 0.6f), ("Choppy", 0.4f, 0.8f), ("Storm", 0.8f, 0.5f),
    };

    [Tooltip("The pool the drops aim at when the mouse isn't over water, and whose waves V changes.")]
    public BasicEntity Pool;
    [Tooltip("Drop something at a random spot on the water every few seconds.")]
    public bool AutoDrop = true;
    [Range(0.5f, 20)]
    [Tooltip("Seconds between automatic drops.")]
    public float AutoDropInterval = 2.5f;
    [Range(1, 20)]
    [Tooltip("Metres above the water objects are dropped from: 1.8 m hits at 6 m/s, 10 m at 14 m/s.")]
    public float DropHeight = 5;

    private UIManager _ui;
    private readonly List<BasicEntity> _dropped = new();
    private readonly List<BasicEntity> _waterBodies = new();
    private int _kind = 1, _waveIndex = 1, _drops;
    private float _autoTimer, _lastSplashSpeed, _lastSplashRadius, _rainIntensity = 0.6f;
    private (float Height, float Scale)? _poolWaves;
    private string _shown;

    public IReadOnlyList<BasicEntity> Dropped => _dropped;
    public DropKind Selected => Kinds[_kind];
    public int Drops => _drops;

    public override void Start()
    {
        _ui = GameUI.Open(DocumentPath);
        _shown = null;
        _dropped.Clear();
        _drops = 0;
        _autoTimer = 0;
        _lastSplashSpeed = _lastSplashRadius = 0;
        Pool ??= FindGameObject("Pool Water");
        _waterBodies.Clear();
        foreach (BasicEntity entity in GameFlow.SceneLogic?.ActiveScene?.BasicEntities ?? new List<BasicEntity>())
            if (entity.Role == GameObjectRole.Water) _waterBodies.Add(entity);

        // The pool's own waves, put back on Stop; V steps through the presets from the closest one.
        MaterialComponent material = PoolMaterial;
        if (material != null)
        {
            _poolWaves = (material.WaveHeight, material.WaveScale);
            _waveIndex = Array.FindIndex(WavePresets, p => p.Height >= material.WaveHeight - 0.01f);
            if (_waveIndex < 0) _waveIndex = WavePresets.Length - 1;
        }
        EnvironmentSettings environment = SceneEnvironment;
        if (environment?.Weather == WeatherType.Rain && environment.WeatherIntensity > 0) _rainIntensity = environment.WeatherIntensity;
        if (WaterSplashes != null) WaterSplashes.Splashed += OnSplashed;
    }

    public override void Stop()
    {
        if (WaterSplashes != null) WaterSplashes.Splashed -= OnSplashed;
        MaterialComponent material = PoolMaterial;
        if (material != null && _poolWaves is { } waves) SetWaves(material, waves.Height, waves.Scale);
        _poolWaves = null;
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        if (!DebugScreen.ConsoleOpen) HandleInput();
        if (AutoDrop)
        {
            _autoTimer += DeltaTime;
            if (_autoTimer >= AutoDropInterval)
            {
                _autoTimer = 0;
                if (RandomWaterPoint(out Vector3 point)) Drop(Kinds[Random.Shared.Next(Kinds.Length)], point);
            }
        }
        // Whatever fell out of the world is gone for good.
        for (int i = _dropped.Count - 1; i >= 0; i--)
            if (_dropped[i].Position.Z < -30) Remove(i);
        UpdateHud();
    }

    /// <summary>Drops <paramref name="kind"/> from <see cref="DropHeight"/> above <paramref name="waterPoint"/>.</summary>
    public BasicEntity Drop(DropKind kind, Vector3 waterPoint)
    {
        Vector3 at = waterPoint + new Vector3(0, 0, DropHeight + kind.Scale.Z);
        BasicEntity body = Spawn(kind.Model, at, $"{kind.Name} {++_drops}");
        if (body == null) return null;
        body.Scale = kind.Scale;
        // A random spin, so planks and crates land on an edge now and then.
        body.RotationMatrix = Matrix.CreateRotationZ(Random.Shared.NextSingle() * MathHelper.TwoPi) *
                              Matrix.CreateRotationX((Random.Shared.NextSingle() - 0.5f) * 0.6f);
        body.AddComponent(new PhysicsComponent
        {
            BodyType = PhysicsBodyType.Dynamic, Mass = kind.Mass, Buoyancy = true, BuoyancyStrength = kind.Buoyancy,
        });
        body.AddComponent(new MaterialComponent
        {
            Red = kind.Color.X, Green = kind.Color.Y, Blue = kind.Color.Z, Roughness = kind.Roughness,
        });
        _dropped.Add(body);
        if (_dropped.Count > MaxDropped) Remove(0);
        return body;
    }

    /// <summary>Steps the pool's waves to preset <paramref name="index"/> (wraps around).</summary>
    public void SetPoolWaves(int index)
    {
        _waveIndex = ((index % WavePresets.Length) + WavePresets.Length) % WavePresets.Length;
        MaterialComponent material = PoolMaterial;
        if (material != null) SetWaves(material, WavePresets[_waveIndex].Height, WavePresets[_waveIndex].Scale);
    }

    private MaterialComponent PoolMaterial => Pool?.GetComponent<MaterialComponent>();

    // The rendered surface and the buoyancy both read the material (WaterVolume), so floaters follow at once.
    private void SetWaves(MaterialComponent material, float height, float scale)
    {
        material.WaveHeight = height;
        material.WaveScale = scale;
        material.OnChanged(Pool);
    }

    private void HandleInput()
    {
        for (int i = 0; i < Kinds.Length; i++)
            if (GameInput.WasPressed(Keys.D1 + i) || GameInput.WasPressed(Keys.NumPad1 + i)) _kind = i;
        if (GameInput.IsDown(Keys.Up)) DropHeight = Math.Min(20, DropHeight + 6 * DeltaTime);
        if (GameInput.IsDown(Keys.Down)) DropHeight = Math.Max(1, DropHeight - 6 * DeltaTime);

        if (GameInput.MouseClicked && PointedWater(out Vector3 point)) Drop(Selected, point);
        if (GameInput.WasPressed(Keys.X) && PointedWater(out point)) Splash(point, ImpactSpeed(DropHeight), Selected.Scale.X);
        if (GameInput.WasPressed(Keys.Space))
            for (int i = 0; i < 6; i++)
                if (RandomWaterPoint(out point, Pool)) Drop(Kinds[Random.Shared.Next(Kinds.Length)], point);

        if (GameInput.WasPressed(Keys.V)) SetPoolWaves(_waveIndex + 1);
        if (GameInput.WasPressed(Keys.G))
        {
            AutoDrop = !AutoDrop;
            _autoTimer = 0;
        }
        if (GameInput.WasPressed(Keys.C))
            for (int i = _dropped.Count - 1; i >= 0; i--) Remove(i);

        EnvironmentSettings environment = SceneEnvironment;
        if (environment == null) return;
        if (GameInput.WasPressed(Keys.R))
        {
            bool raining = environment.Weather == WeatherType.Rain && environment.WeatherIntensity > 0;
            environment.Weather = raining ? WeatherType.None : WeatherType.Rain;
            environment.WeatherIntensity = raining ? 0 : _rainIntensity;
        }
        if (environment.Weather == WeatherType.Rain)
        {
            if (GameInput.IsDown(Keys.OemCloseBrackets)) _rainIntensity = Math.Min(1, _rainIntensity + 0.5f * DeltaTime);
            if (GameInput.IsDown(Keys.OemOpenBrackets)) _rainIntensity = Math.Max(0.05f, _rainIntensity - 0.5f * DeltaTime);
            environment.WeatherIntensity = _rainIntensity;
        }
        // A new starting hour restarts the day/night cycle there.
        if (GameInput.WasPressed(Keys.T) && environment.DayNightCycle)
            environment.TimeOfDay = (float.Floor(environment.TimeOfDay) + 3) % 24;
    }

    /// <summary>The water under the mouse, else the middle of the pool.</summary>
    private bool PointedWater(out Vector3 point)
    {
        if (RaycastWater(MouseRay, 300, out point)) return true;
        return RandomWaterPoint(out point, Pool, spread: 0);
    }

    /// <summary>A random spot on <paramref name="water"/> (default: any water body), 1 m in from its edges.</summary>
    private bool RandomWaterPoint(out Vector3 point, BasicEntity water = null, float spread = 1)
    {
        point = default;
        water ??= _waterBodies.Count > 0 ? _waterBodies[Random.Shared.Next(_waterBodies.Count)] : Pool;
        if (WaterVolume.From(water) is not WaterVolume volume) return false;
        Vector2 margin = Vector2.Min(new Vector2(1), (volume.Max - volume.Min) * 0.25f);
        Vector2 min = volume.Min + margin, max = volume.Max - margin;
        Vector2 center = (min + max) * 0.5f;
        var xy = new Vector2(MathHelper.Lerp(center.X, MathHelper.Lerp(min.X, max.X, Random.Shared.NextSingle()), spread),
            MathHelper.Lerp(center.Y, MathHelper.Lerp(min.Y, max.Y, Random.Shared.NextSingle()), spread));
        point = new Vector3(xy, volume.SurfaceZ);
        return true;
    }

    private void Remove(int index)
    {
        Destroy(_dropped[index]);
        _dropped.RemoveAt(index);
    }

    private void OnSplashed(Vector3 point, float speed, float radius)
    {
        _lastSplashSpeed = speed;
        _lastSplashRadius = radius;
    }

    /// <summary>m/s after falling <paramref name="height"/> metres.</summary>
    public static float ImpactSpeed(float height) => (float)Math.Sqrt(2 * 9.81f * Math.Max(height, 0));

    private void UpdateHud()
    {
        if (_ui == null) return;
        var c = CultureInfo.InvariantCulture;
        int wet = 0, floating = 0, sunk = 0;
        foreach (BasicEntity body in _dropped)
        {
            if (!IsInWater(body)) continue;
            wet++;
            if (body.Physics?.BuoyancyStrength < 1) sunk++;
            else floating++;
        }
        Particles.WaterSplashes splashes = WaterSplashes;
        EnvironmentSettings environment = SceneEnvironment;
        bool raining = environment?.Weather == WeatherType.Rain && environment.WeatherIntensity > 0;

        string counts = string.Format(c, "{0} dropped  ·  {1} floating  ·  {2} on the bottom", _dropped.Count, floating, sunk);
        string splash = splashes == null ? "" : string.Format(c, "{0} splashes  ·  {1:N0} droplets  ·  {2} rings", splashes.SplashCount,
            splashes.ParticleCount, splashes.Ripples.Count);
        string last = _lastSplashSpeed > 0
            ? string.Format(c, "Last splash {0:0.0} m/s, {1:0.00} m wide", _lastSplashSpeed, _lastSplashRadius * 2)
            : "No splash yet  ·  click the water";
        string drop = string.Format(c, "{0} from {1:0.0} m  ({2:0.0} m/s)", Selected.Name, DropHeight, ImpactSpeed(DropHeight));
        var preset = WavePresets[_waveIndex];
        string waves = string.Format(c, "Pool waves {0} ({1:0.00} m)", preset.Name, PoolMaterial?.WaveHeight ?? preset.Height);
        string rain = raining ? string.Format(c, "Rain {0:0}%", environment.WeatherIntensity * 100) : "No rain";
        string auto = AutoDrop
            ? string.Format(c, "Auto drop every {0:0.0} s  ·  G to stop", AutoDropInterval)
            : "Auto drop off  ·  G to start";

        string text = string.Join("|", counts, splash, last, drop, waves, rain, auto);
        if (text == _shown) return;
        _shown = text;
        _ui.SetText("#counts", counts);
        _ui.SetText("#splashes", splash);
        _ui.SetText("#last", last);
        _ui.SetText("#drop", drop);
        _ui.SetText("#waves", waves + "  ·  " + rain);
        _ui.SetText("#auto", auto);
    }
}
