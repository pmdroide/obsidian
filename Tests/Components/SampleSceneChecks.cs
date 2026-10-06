using System.Reflection;
using Anvil.Models;
using Anvil.ViewModels;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;

/// <summary>Engine/Content/Scenes/AutoExposureTest.obsc: a closed house in sunlight for testing eye adaptation.</summary>
internal static class SampleSceneChecks
{
    public static void Run()
    {
        var bridge = new EditorBridge();
        string path = Path.Combine(bridge.ContentSourceRoot, "Scenes", "AutoExposureTest.obsc");
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        var scene = SceneSerialization.LoadFromFile(path, assets);

        Check(scene.BasicEntities.Count == 18 && scene.PointLights.Count == 1 && scene.DirectionalLights.Count == 1 &&
              scene.BasicEntities.All(e => e.IsEnabled) && scene.MainCamera != null && !scene.Environment.DayNightCycle,
            "the auto exposure sample scene loads every object, the sun, the interior lamp and the camera");
        Check(scene.MainCamera.GetComponent<Engine.Components.ScriptBehaviourComponent>() is { ScriptId: Engine.Scripting.FreecamScript.ScriptId, Enabled: true },
            "the sample scene's camera has Freecam attached, so Play can fly in and out of the house");
        var sun = scene.DirectionalLights[0];
        Check(sun.CastShadows && sun.Intensity >= 100 && scene.PointLights[0].Intensity < 10,
            "the sample scene has a bright shadowed sun and a dim interior lamp");

        // Rays from the middle of the room leave only through the doorway (south) and the window (east).
        var boxes = scene.BasicEntities.Where(e => e.Name != "Ground").Select(WorldBox).ToList();
        bool Blocked(Vector3 direction) =>
            boxes.Any(b => new Ray(new Vector3(0, 0, 1.8f), direction).Intersects(b) is float d && d < 20);
        Check(Blocked(Vector3.UnitY) && Blocked(-Vector3.UnitX) && Blocked(Vector3.UnitZ) &&
              Blocked(new Vector3(0, -1, 0.6f)) && Blocked(new Vector3(1, 0, 0.6f)),
            "the house is closed overhead, at the back and west, and above the door and window");
        Check(!Blocked(-Vector3.UnitY) && !Blocked(Vector3.UnitX),
            "the doorway and window let you look out from inside the house");

        // Without baked probes the outdoor cubemap lights the room as if it were outside.
        var probes = scene.BakedProbes;
        Check(probes != null && scene.Lighting.ProbeVolumeEnabled &&
              probes.BoundsMin.Z < 0.1f && probes.BoundsMax.Z > 3.7f &&
              new BoundingBox(probes.BoundsMin, probes.BoundsMax).Contains(scene.EnvironmentSample.Position) == ContainmentType.Contains,
            "the sample scene ships baked probes covering the house and the reflection capture point");
        static float Luma(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        var room = new Vector3(0, 0.5f, 1.6f);
        Vector3 capture = scene.EnvironmentSample.Position;
        probes!.SampleSH(capture, out var shR, out var shG, out var shB);
        var fromSH = new Vector3(shR.X + shR.Z, shG.X + shG.Z, shB.X + shB.Z); // irradiance facing +Y
        Check(Vector3.Distance(fromSH, probes.EvaluateIrradiance(capture, Vector3.UnitY)) < 0.1f * fromSH.Length(),
            "in open air the visibility-weighted lookup agrees with plain trilinear SH");
        // Towards the back and west walls; the ceiling right above the lamp is legitimately brighter.
        Check(new[] { Vector3.UnitY, -Vector3.UnitX }.All(n =>
                Luma(probes.EvaluateIrradiance(room, n)) < 0.25f * Luma(probes.EvaluateIrradiance(capture, n))),
            "inside the house the probes see far less light than at the cubemap's capture point, so reflections are darkened");

        // Thin walls and the 0.2 m ceiling sit between probes; the probes on the far side must not leak in.
        Check(probes.DistPos.All(d => d.X > 0 && d.Y > 0 && d.Z > 0) && probes.DistNeg.All(d => d.X > 0 && d.Y > 0 && d.Z > 0),
            "the bake stores a free distance per probe and axis");
        static float Leak(Engine.Renderer.Lighting.ProbeVolumeData p, Vector3 surface, Vector3 n)
        {
            // Plain trilinear at the same normal-offset point, i.e. what sampling did before visibility
            Vector3 cell = (p.BoundsMax - p.BoundsMin) / new Vector3(p.CountX - 1, p.CountY - 1, p.CountZ - 1);
            p.SampleSH(surface + n * cell * 0.5f, out var r, out var g, out var b);
            var plain = new Vector3(r.X + Vector3.Dot(new Vector3(r.Y, r.Z, r.W), n), g.X + Vector3.Dot(new Vector3(g.Y, g.Z, g.W), n), b.X + Vector3.Dot(new Vector3(b.Y, b.Z, b.W), n));
            return Luma(p.EvaluateIrradiance(surface, n)) / Math.Max(Luma(plain), 1e-6f);
        }
        // Top of the east wall under the ceiling (sunlit attic above), and the south-west corner (sunlit yard
        // behind the south wall). Plain trilinear makes both about 10x brighter than the middle of the wall.
        Check(Leak(probes, new Vector3(4.0f, 2, 3.4f), -Vector3.UnitX) < 0.25f && Leak(probes, new Vector3(-4.0f, -2.95f, 1.5f), Vector3.UnitX) < 0.25f,
            "probes behind the ceiling and the south wall are left out, so wall edges don't glow");
        var probeAtWall = new Vector3(0, 0.12f, 0);  // 0.12 m in front of a wall along +Y
        var wallDistances = new Vector4(2, 0.15f, 2, 0);
        Check(Engine.Renderer.Lighting.ProbeVolumeData.Visibility(new Vector3(0, 0.45f, 0), wallDistances, new Vector4(2)) < 0.05f &&
              Engine.Renderer.Lighting.ProbeVolumeData.Visibility(new Vector3(0, -0.4f, 0), wallDistances, new Vector4(2)) == 1 &&
              Engine.Renderer.Lighting.ProbeVolumeData.Visibility(new Vector3(0.4f, 0.4f, 0), wallDistances, new Vector4(2)) < 0.5f,
            $"a probe {probeAtWall.Y} m from a wall can't see through it, straight or diagonally, but sees the room behind it");

        string copy = Path.Combine(Path.GetTempPath(), $"probes-{Guid.NewGuid():N}.probes");
        try
        {
            probes.Save(copy);
            var loaded = Engine.Renderer.Lighting.ProbeVolumeData.Load(copy);
            Check(loaded.DistPos.SequenceEqual(probes.DistPos) && loaded.DistNeg.SequenceEqual(probes.DistNeg) && loaded.SHR.SequenceEqual(probes.SHR),
                "probe distances survive save and load");
        }
        finally { File.Delete(copy); }

        var vm = new MainWindowViewModel();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(MainWindowViewModel).GetField("_bridge", flags)!.SetValue(vm, bridge);
        var pending = (System.Collections.Concurrent.ConcurrentQueue<Action>)typeof(EditorBridge).GetField("_pendingOps", flags)!.GetValue(bridge)!;
        var node = new AssetNode { Name = "AutoExposureTest.obsc", RelativePath = "Scenes/AutoExposureTest.obsc", Kind = AssetKind.Scene };
        Check(vm.OpenSceneAsset(node) && pending.Count == 1 &&
              (string?)typeof(MainWindowViewModel).GetField("_lastSceneFolder", flags)!.GetValue(vm) == Path.GetDirectoryName(path) &&
              !vm.OpenSceneAsset(new AssetNode { Kind = AssetKind.Script }),
            "double-clicking a scene in Assets queues that scene file to load");
    }

    /// <summary>Engine/Content/Scenes/GodRayTest.obsc: a slatted hall for checking sun and moon shafts over the day/night cycle.</summary>
    public static void RunGodRays()
    {
        var bridge = new EditorBridge();
        string path = Path.Combine(bridge.ContentSourceRoot, "Scenes", "GodRayTest.obsc");
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        var scene = SceneSerialization.LoadFromFile(path, assets);

        Check(scene.BasicEntities.Count == 29 && scene.DirectionalLights.Count == 1 && scene.Environment.DayNightCycle &&
              scene.MainCamera.GetComponent<Engine.Components.ScriptBehaviourComponent>() is { ScriptId: Engine.Scripting.FreecamScript.ScriptId, Enabled: true },
            "the god ray scene loads the hall, the sun template, the day/night cycle and a Freecam camera");

        // The cycle copies the template's shadow box, and fog outside it counts as lit: the whole hall must fit inside.
        var sun = scene.DirectionalLights[0];
        var hall = scene.BasicEntities.Where(e => e.Name != "Ground" && e.Name != "Backdrop Wall").Select(WorldBox).ToList();
        float reach = hall.SelectMany(b => b.GetCorners()).Max(c => Vector3.Distance(c, sun.Position));
        Check(sun.CastShadows && reach < sun.ShadowSize / 2 && reach < sun.ShadowDepth,
            "every hall slat sits inside the sun's shadow box from any direction");

        // Froxel fog is zero within Start Dist. of the camera, so the shafts have to form well beyond it.
        Vector3 eye = scene.MainCamera.Position;
        float nearest = hall.Min(b => Vector3.Distance(eye, Vector3.Clamp(eye, b.Min, b.Max)));
        Check(nearest > 2 * GameSettings.g_FroxelFogDistanceStart,
            "the camera sits far enough from the hall for the default fog to show the shafts");
    }

    private static BoundingBox WorldBox(BasicEntity e)
    {
        var corners = new BoundingBox(-Vector3.One, Vector3.One).GetCorners();
        Matrix world = Matrix.CreateScale(e.Scale) * e.RotationMatrix * Matrix.CreateTranslation(e.Position);
        return BoundingBox.CreateFromPoints(corners.Select(c => Vector3.Transform(c, world)));
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
