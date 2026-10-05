using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;

/// <summary>Gameobject roles, the CPU wave mirror, buoyancy and their editor/scene plumbing.</summary>
internal static class WaterChecks
{
    public static void Run()
    {
        CheckWaves();
        CheckBuoyancy();
        CheckRolesAndSettings();
    }

    private static void CheckWaves()
    {
        var calm = new WaterWaveSettings(0.3f, 1, 0);
        Check(WaterWaves.Displacement(new Vector2(3, 4), 7, calm) == Vector3.Zero && WaterWaves.HeightAt(new Vector2(3, 4), 7, calm) == 0,
            "water without wave height stays flat");

        var swell = new WaterWaveSettings(0.3f, 1, 2);
        float maxError = 0, maxHeight = 0;
        for (int i = 0; i < 50; i++)
        {
            var rest = new Vector2(i * 3.7f - 90, i * -2.3f + 40);
            Vector3 moved = WaterWaves.Displacement(rest, i * 0.37f, swell);
            maxHeight = Math.Max(maxHeight, Math.Abs(moved.Z));
            float height = WaterWaves.HeightAt(rest + new Vector2(moved.X, moved.Y), i * 0.37f, swell);
            maxError = Math.Max(maxError, Math.Abs(height - moved.Z));
        }
        Check(maxHeight > 0.2f && maxHeight <= 1.0001f, "swell moves the surface by at most half the crest-to-trough wave height");
        Check(maxError < 0.02f, $"surface height lookup follows the sideways Gerstner motion (max error {maxError:0.0000} m)");
    }

    private static void CheckBuoyancy()
    {
        var ocean = new[] { new WaterVolume(new Vector2(-100), new Vector2(100), 0) };
        Check(ocean[0].Contains(0, 0) && !ocean[0].Contains(150, 0), "water volume covers its XY footprint only");

        float Simulate(float strength, WaterVolume[] water, float seconds, out float speed, Func<float, float>? surface = null)
        {
            using var physics = new PhysicsSystem(new Vector3(0, 0, -9.81f));
            var body = physics.AddDynamicBox(new Vector3(0, 0, 3), 1, 1, 1, 1);
            float worstGap = 0;
            int steps = (int)(seconds * 60);
            for (int i = 0; i < steps; i++)
            {
                float time = i / 60f;
                Buoyancy.Apply(physics, body, new Vector3(-0.5f), new Vector3(0.5f), 1, strength, new PhysicsComponent().WaterDrag, water, time, 1 / 60f);
                physics.Step(1 / 60f);
                if (surface != null && i > steps / 2)
                {
                    physics.GetBodyState(body, out Vector3 p, out _, out _, out _);
                    worstGap = Math.Max(worstGap, Math.Abs(p.Z - surface(time)));
                }
            }
            physics.GetBodyState(body, out Vector3 position, out _, out Vector3 velocity, out _);
            speed = velocity.Length();
            return surface != null ? worstGap : position.Z;
        }

        float floating = Simulate(2, ocean, 12, out float floatingSpeed);
        Check(Math.Abs(floating) < 0.1f && floatingSpeed < 0.2f,
            $"a buoyant cube with float 2 settles half-submerged on calm water (centre at {floating:0.000})");
        float sinking = Simulate(0.5f, ocean, 6, out _);
        Check(sinking < -5, "a body with float below 1 sinks");
        float dry = Simulate(2, Array.Empty<WaterVolume>(), 3, out _);
        Check(dry < -30, "buoyancy does nothing without water");

        var waves = new WaterWaveSettings(0.3f, 1, 1);
        var sea = new[] { new WaterVolume(new Vector2(-100), new Vector2(100), 0, waves) };
        float gap = Simulate(2, sea, 12, out _, t => WaterWaves.HeightAt(Vector2.Zero, t, waves));
        Check(gap < 0.35f, $"a floating body rides the moving swell (largest gap to the surface {gap:0.000} m)");
    }

    private static void CheckRolesAndSettings()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, new Vector3(0, 0, 2), Matrix.Identity, new Vector3(10, 10, 0.5f)) { Name = "Lake" };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });

        Check(entity.Role == GameObjectRole.Default && WaterVolume.From(entity) == null, "gameobjects start with the Default role and hold no water");
        Drain(bridge);
        var objects = new ObservableCollection<SceneObjectViewModel>();
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var vm = objects.Single(o => o.EngineId == entity.Id);
        Check(vm.HasRole && vm.Role == GameObjectRole.Default, "inspector shows the role of a gameobject");

        logic.ActiveScene.IsDirty = false;
        vm.Role = GameObjectRole.Water;
        Drain(bridge);
        Check(entity.Role == GameObjectRole.Water && logic.ActiveScene.IsDirty && bridge.Snapshot.Single(s => s.Id == entity.Id).Role == GameObjectRole.Water,
            "role edits reach the engine, mark the scene dirty and return in snapshots");

        var volume = WaterVolume.From(entity);
        Check(volume is { SurfaceZ: 2.5f, HasWaves: false } && volume.Value.Contains(9.9f, -9.9f) && !volume.Value.Contains(10.1f, 0),
            "a Water-role gameobject is water below the top of its bounds, inside its footprint");
        entity.IsEnabled = false;
        Check(WaterVolume.From(entity) == null, "disabled water holds no water");
        entity.IsEnabled = true;

        bridge.EnqueueAddComponent(entity.Id, MaterialComponent.TypeId);
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var material = entity.GetComponent<MaterialComponent>();
        var materialVm = vm.Components.OfType<MaterialInfo>().Single();
        materialVm.WaveHeight = 3;
        Drain(bridge);
        Check(material.WaveHeight == 3, "wave height edits reach the material component");
        material.WaveHeight = 99;
        Check(WaterWaveSettings.From(material).WaveHeight == MaterialComponent.MaxWaveHeight, "wave height is clamped like the rendered material");
        material.WaveHeight = 3;
        material.MaterialType = MaterialEffect.MaterialTypes.Water;
        Check(WaterVolume.From(entity) is { HasWaves: true } water && water.Waves.WaveHeight == 3,
            "water with a water material follows its waves");

        bridge.EnqueueAddComponent(entity.Id, PhysicsComponent.TypeId);
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var physics = entity.GetComponent<PhysicsComponent>();
        var physicsVm = vm.Components.OfType<PhysicsComponentViewModel>().Single();
        Check(!physics.Buoyancy && !physicsVm.Buoyancy, "buoyancy is off by default");
        physicsVm.Buoyancy = true;
        physicsVm.BuoyancyStrength = 1.5;
        physicsVm.WaterDrag = 3;
        Drain(bridge);
        Check(physics.Buoyancy && physics.BuoyancyStrength == 1.5f && physics.WaterDrag == 3, "buoyancy inspector edits reach the engine");
        physics.BuoyancyStrength = float.NaN;
        Check(physics.ClampedBuoyancyStrength == 2, "invalid float strength falls back to the default");
        physics.BuoyancyStrength = 1.5f;

        var clone = (BasicEntity)entity.Clone;
        Check(clone.Role == GameObjectRole.Water && clone.GetComponent<PhysicsComponent>().Buoyancy, "copies keep their role and buoyancy");

        string path = Path.Combine(Path.GetTempPath(), $"anvil-water-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, path, assets);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            var saved = json["Entities"]![0]!.AsObject();
            Check((string?)saved["Role"] == "Water", "scenes save the role by name");
            var loaded = SceneSerialization.LoadFromFile(path, assets).BasicEntities.Single();
            var loadedPhysics = loaded.GetComponent<PhysicsComponent>();
            Check(loaded.Role == GameObjectRole.Water && loaded.GetComponent<MaterialComponent>().WaveHeight == 3 &&
                  loadedPhysics.Buoyancy && loadedPhysics.BuoyancyStrength == 1.5f && loadedPhysics.WaterDrag == 3,
                "scene save/load keeps role, wave height and buoyancy");

            saved.Remove("Role");
            File.WriteAllText(path, json.ToJsonString());
            Check(SceneSerialization.LoadFromFile(path, assets).BasicEntities.Single().Role == GameObjectRole.Default,
                "older scenes without a role load as Default");
        }
        finally { File.Delete(path); }

        vm.Role = GameObjectRole.Default;
        Drain(bridge);
        Check(entity.Role == GameObjectRole.Default && WaterVolume.From(entity) == null, "switching back to Default removes the water");
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 6; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
