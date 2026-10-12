using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Particles;
using Engine.Physics;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>
/// Bodies crossing water surfaces (ScenePhysics water contacts, OnWaterEnter/OnWaterExit), the splashes they throw
/// (Particles/WaterSplashes: droplets, rings), and a physics run of Engine/Content/Scenes/WaterTest.obsc.
/// Box colliders come from each model's bounds (ScenePhysics.FallbackGeometry), so no GPU is needed.
/// </summary>
internal static class WaterSplashChecks
{
    private const float Dt = 1 / 60f;
    private static readonly Vector3 Gravity = new(0, 0, -9.81f);

    public static void Run()
    {
        ScriptRegistry.Register<WaterRecorder>(WaterRecorder.Id, "Test Water Recorder");
        CheckWaterContacts();
        CheckSplashes();
        CheckSampleScene();
    }

    private static void CheckWaterContacts()
    {
        using var world = new CollisionChecks.World();
        // A pool 20 x 20 m whose surface is at Z = 0.
        BasicEntity pool = world.Add("Pool", new Vector3(0, 0, -2), new Vector3(10, 10, 2), PhysicsBodyType.None, recorder: false);
        pool.Role = GameObjectRole.Water;
        BasicEntity crate = world.Add("Crate", new Vector3(0, 0, 3.5f), new Vector3(0.5f), PhysicsBodyType.Dynamic, 10, recorder: false);
        crate.Physics.Buoyancy = true;
        crate.Physics.BuoyancyStrength = 2;
        var recorder = new ScriptBehaviourComponent { ScriptId = WaterRecorder.Id };
        crate.AddComponent(recorder);
        recorder.OnStart(crate);
        BasicEntity rock = world.Add("Rock", new Vector3(5, 5, 1), new Vector3(0.3f), PhysicsBodyType.Dynamic, 50, recorder: false);
        BasicEntity dry = world.Add("Dry", new Vector3(30, 0, 1), new Vector3(0.3f), PhysicsBodyType.Dynamic, recorder: false);

        var entered = new List<WaterContact>();
        var exited = new List<WaterContact>();
        world.Scene.WaterEntered += entered.Add;
        world.Scene.WaterExited += exited.Add;

        // The crate's bottom starts 3 m above the water.
        world.Step(90);
        WaterContact crateIn = entered.FirstOrDefault(c => c.GameObject == crate);
        float expected = (float)Math.Sqrt(2 * 9.81f * 3);
        Check(crateIn.GameObject == crate && Math.Abs(crateIn.Speed - expected) < 0.6f,
            $"a body falling into water enters it at its impact speed ({crateIn.Speed:0.00} m/s, expected about {expected:0.00})");
        Check(Math.Abs(crateIn.Point.Z) < 0.01f && Math.Abs(crateIn.Point.X) < 0.05f && Math.Abs(crateIn.Radius - 0.5f) < 0.01f && crateIn.Mass == 10,
            "the water contact gives the surface point under the body, its half-size and its mass");
        var hooks = WaterRecorder.Of(crate);
        Check(hooks.Entered.Count == 1 && hooks.Entered[0].Speed == crateIn.Speed && hooks.Exited.Count == 0,
            "the body's scripts get OnWaterEnter once");
        Check(entered.Count(c => c.GameObject == rock) == 1 && !entered.Any(c => c.GameObject == dry),
            "bodies without Buoyancy enter water too; bodies outside its footprint never do");
        Check(world.Scene.IsInWater(crate) && world.Scene.IsInWater(rock) && !world.Scene.IsInWater(dry) && !world.Scene.IsInWater(pool),
            "IsInWater tells which bodies are in the water");

        // Bobbing on the surface must not flicker in and out.
        world.Step(300);
        Check(entered.Count(c => c.GameObject == crate) == 1 && exited.Count == 0 && hooks.Entered.Count == 1,
            "a body floating on the surface stays in the water without repeated enter/exit events");
        Check(world.Scene.IsInWater(rock) && rock.Position.Z < -3, "a body that sinks stays in the water");

        Vector3 hit = default;
        Check(world.Scene.RaycastWater(new Vector3(-3, 0, 10), new Vector3(0.3f, 0, -1), 50, out hit) && Math.Abs(hit.Z) < 0.01f &&
              Math.Abs(hit.X - -3 - 0.3f * 10) < 0.05f,
            $"a ray finds the water surface ({hit})");
        Check(!world.Scene.RaycastWater(new Vector3(30, 0, 10), -Vector3.UnitZ, 50, out _) && !world.Scene.RaycastWater(new Vector3(0, 0, 10), Vector3.UnitZ, 50, out _),
            "rays outside the water or pointing away from it miss");

        // Thrown up and out.
        world.Scene.ApplyImpulse(crate, new Vector3(0, 0, 10 * 9));
        world.Step(30);
        WaterContact crateOut = exited.FirstOrDefault(c => c.GameObject == crate);
        Check(crateOut.GameObject == crate && crateOut.Speed > 3 && hooks.Exited.Count == 1 && !world.Scene.IsInWater(crate),
            $"a body leaving the water raises OnWaterExit with its upward speed ({crateOut.Speed:0.00} m/s)");

        world.Scene.Update(world.Entities, Dt, simulate: false);
        Check(!world.Scene.IsInWater(rock), "leaving Play forgets which bodies were in the water");
    }

    private static void CheckSplashes()
    {
        var splashes = new WaterSplashes();
        var thrown = new List<(Vector3 Point, float Speed, float Radius)>();
        splashes.Splashed += (p, s, r) => thrown.Add((p, s, r));
        var point = new Vector3(2, 3, 1.5f);

        splashes.Splash(point, 8, 0.4f);
        Check(splashes.SplashCount == 1 && thrown.Count == 1 && thrown[0].Speed == 8 && splashes.Ripples.Count == 1 && splashes.ParticleCount > 30,
            $"a splash throws droplets ({splashes.ParticleCount}) and leaves a ring");
        var positions = new Vector4[WaterSplashes.MaxRipples];
        var shapes = new Vector4[WaterSplashes.MaxRipples];
        Check(splashes.FillRipples(positions, shapes) == 1 && positions[0] == new Vector4(point, 0) && shapes[0].Y == 0.4f && shapes[0].X > 0.5f,
            "rings are packed for the water shader: point, age, strength and radius");

        float highest = point.Z, lowest = float.MaxValue;
        int peak = splashes.ParticleCount;
        for (int i = 0; i < 60 * 3; i++)
        {
            splashes.Update(Dt, Gravity, Vector2.Zero);
            foreach (ParticleSystemComponent system in splashes.VisibleSystems())
                foreach (Particle p in system.Simulation.Particles)
                {
                    highest = Math.Max(highest, p.Position.Z);
                    lowest = Math.Min(lowest, p.Position.Z);
                }
        }
        Check(highest > point.Z + 0.5f && lowest >= point.Z - 0.05f,
            $"droplets fly up ({highest - point.Z:0.00} m) and die when they fall back onto the water (lowest {lowest - point.Z:0.000} m)");
        Check(splashes.ParticleCount == 0 && peak > 0, "a splash is over within a few seconds");
        Check(splashes.Ripples.Count == 1 && Math.Abs(splashes.Ripples[0].Age - 3) < 0.05f, "rings age with the updates");
        splashes.Update(0, Gravity, Vector2.Zero);
        Check(Math.Abs(splashes.Ripples[0].Age - 3) < 0.05f, "a paused update (0 s) holds the rings still");
        for (int i = 0; i < 70; i++) splashes.Update(Dt, Gravity, Vector2.Zero);
        Check(splashes.Ripples.Count == 0, "rings fade away after their lifetime");

        splashes.OnWaterEntered(new WaterContact { Point = point, Speed = 0.8f, Radius = 0.3f });
        Check(splashes.Ripples.Count == 1 && splashes.ParticleCount == 0 && splashes.SplashCount == 1,
            "a slow entry only leaves a ring");
        splashes.OnWaterEntered(new WaterContact { Point = point, Speed = 0.1f, Radius = 0.3f });
        Check(splashes.Ripples.Count == 1, "drifting into the water leaves nothing");
        splashes.OnWaterEntered(new WaterContact { Point = point, Speed = 9, Radius = 0.5f, Velocity = new Vector3(4, 0, -9) });
        Check(splashes.SplashCount == 2 && splashes.ParticleCount > 0, "a fast entry splashes");
        int before = splashes.SplashCount;
        splashes.OnWaterExited(new WaterContact { Point = point, Speed = 5, Radius = 0.5f });
        Check(splashes.SplashCount == before && splashes.Ripples.Count == 3, "leaving the water fast sheds droplets and a ring but isn't counted as a splash");

        for (int i = 0; i < 40; i++) splashes.Splash(point + new Vector3(i, 0, 0), 12, 1);
        Check(splashes.Ripples.Count == WaterSplashes.MaxRipples && splashes.Ripples[^1].Point.X == point.X + 39,
            "the water keeps the newest rings, up to the shader's limit");
        Check(splashes.ParticleCount < 12 * (400 + 200 + 160), "many splashes at once reuse a fixed pool of droplet systems");

        splashes.Splash(new Vector3(float.NaN, 0, 0), 8);
        splashes.Splash(point, float.PositiveInfinity, float.NaN);
        Check(splashes.Ripples.All(r => float.IsFinite(r.Strength) && float.IsFinite(r.Radius) && float.IsFinite(r.Point.X)),
            "invalid splash input is ignored or clamped");

        splashes.Clear();
        Check(splashes.ParticleCount == 0 && splashes.Ripples.Count == 0 && splashes.SplashCount == 0, "Clear removes every droplet and ring");
        splashes.Enabled = false;
        splashes.Splash(point, 10);
        splashes.OnWaterEntered(new WaterContact { Point = point, Speed = 10, Radius = 0.5f });
        Check(splashes.ParticleCount == 0 && splashes.Ripples.Count == 0, "disabled splashes do nothing");

        Check(new MainSceneLogic().WaterSplashes != null, "the scene logic owns the water splashes the renderer draws");
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds), Capsule = new ModelDefinition(null, bounds),
        };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "WaterTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        List<BasicEntity> entities = scene.BasicEntities;
        BasicEntity Find(string name) => entities.Single(e => e.Name == name);

        BasicEntity pool = Find("Pool Water"), tank = Find("Wave Tank Water");
        WaterVolume? poolOrNull = WaterVolume.From(pool), tankOrNull = WaterVolume.From(tank);
        Check(entities.Count(e => e.Role == GameObjectRole.Water) == 2 && poolOrNull is { HasWaves: true } && tankOrNull is { HasWaves: true } &&
              tankOrNull.Value.Waves.WaveHeight > poolOrNull.Value.Waves.WaveHeight * 3,
            "the water sample has a gently waving pool and a wave tank with a big swell");
        WaterVolume poolWater = poolOrNull.GetValueOrDefault(), tankWater = tankOrNull.GetValueOrDefault();
        Check(pool.Physics == null && tank.Physics == null && entities.Where(e => e.Name.Contains("Floor") || e.Name.Contains("Wall"))
                  .All(e => e.Physics is { BodyType: PhysicsBodyType.Static }),
            "the water has no collider; the pool and tank floors and walls are static");
        Check(scene.Environment.Weather == WeatherType.Rain && scene.Environment.WeatherIntensity > 0 && scene.DirectionalLights.Count == 1 &&
              scene.PointLights.Count(l => l.Position.Z < poolWater.SurfaceZ) == 2,
            "it rains on the sample, and the pool has underwater lights");

        var scripts = scene.MainCamera.GetComponents<ScriptBehaviourComponent>().ToList();
        Check(scripts.Select(s => s.ScriptId).SequenceEqual(new[] { FreecamScript.ScriptId, WaterTestScript.ScriptId }) &&
              ScriptRegistry.Find(WaterTestScript.ScriptId) != null &&
              scripts[1].Fields.TryGetValue(nameof(WaterTestScript.Pool), out var poolField) && poolField.ToString() == pool.Id.ToString(),
            "the camera runs Freecam and the Water Test script, aimed at the pool");
        string ui = GameInfo.ResolveContentFile("UI/WaterTest");
        Check(SceneList.Find("WaterTest") >= 0 && File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the water sample is in the scene list with its HUD");
        Check(Math.Abs(WaterTestScript.ImpactSpeed(1.8f) - 5.94f) < 0.01f && WaterTestScript.Kinds.Count(k => k.Buoyancy < 1) == 1 &&
              WaterTestScript.WavePresets[0].Height == 0,
            "the script drops floaters and one sinker, and its wave presets start calm");

        // Play the scene's physics: the floaters settle on the water, the sunken crate on the pool floor.
        using var physicsSystem = new PhysicsSystem(Gravity);
        var physics = new ScenePhysics(physicsSystem) { FallbackGeometry = CollisionChecks.World.BoxGeometry };
        var splashes = new WaterSplashes();
        physics.WaterEntered += splashes.OnWaterEntered;
        var firstFrame = new List<WaterContact>();
        physics.WaterEntered += c => { if (physics.Frame == 1) firstFrame.Add(c); };
        try
        {
            for (int i = 0; i < 60 * 8; i++) physics.Update(entities, Dt, simulate: true, time: i * Dt);
            var dynamic = entities.Where(e => e.Physics?.BodyType == PhysicsBodyType.Dynamic).ToList();
            Check(dynamic.Count >= 8 && dynamic.All(e => physics.IsInWater(e) && e.Position.Z > -4.5f),
                "every floater in the sample is in the water and nothing falls through a floor");
            foreach (BasicEntity body in dynamic.Where(e => e.Physics.BuoyancyStrength >= 1))
            {
                WaterVolume water = poolWater.Contains(body.Position.X, body.Position.Y) ? poolWater : tankWater;
                float surface = water.SurfaceHeight(new Vector2(body.Position.X, body.Position.Y), 8);
                Check(water.Contains(body.Position.X, body.Position.Y) && Math.Abs(body.Position.Z - surface) < 0.7f,
                    $"'{body.Name}' floats on its water ({body.Position.Z - surface:+0.00;-0.00} m from the surface)");
            }
            BasicEntity sunk = Find("Sunken Crate");
            Check(Math.Abs(sunk.Position.Z - -2.6f) < 0.1f, $"the sunken crate rests on the pool floor (Z {sunk.Position.Z:0.00})");
            Check(firstFrame.Count == dynamic.Count && firstFrame.All(c => c.Speed < 0.2f),
                "floaters placed on the water are in it from the first step, without a splash");
        }
        finally { physics.DetachAll(); }
    }

    public sealed class WaterRecorder : ScriptBehaviour
    {
        public const string Id = "test-water-recorder";
        private static readonly List<WaterRecorder> Instances = new();
        public readonly List<WaterContact> Entered = new();
        public readonly List<WaterContact> Exited = new();

        public WaterRecorder() => Instances.Add(this);

        public static WaterRecorder Of(BasicEntity entity) => Instances.Last(s => s.GameObject == entity);

        public override void OnWaterEnter(WaterContact water) => Entered.Add(water);
        public override void OnWaterExit(WaterContact water) => Exited.Add(water);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
