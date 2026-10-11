using System.Collections.ObjectModel;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Lighting;
using Microsoft.Xna.Framework;

/// <summary>Spot lights: cone math, editor bridge and inspector, cloning, play mode and save/load.</summary>
internal static class SpotLightChecks
{
    public static void Run()
    {
        static bool Near(float a, float b, float tolerance = 0.001f) => Math.Abs(a - b) < tolerance;
        static bool NearV(Vector3 a, Vector3 b, float tolerance = 0.001f) => Vector3.Distance(a, b) < tolerance;

        // Cone math
        var down = new SpotLight(Vector3.Zero, 10, Color.White, 5, -Vector3.UnitZ, spotAngle: 60, innerSpotAngle: 40);
        Check(NearV(down.Direction, -Vector3.UnitZ) && Near(down.CosOuter, (float)Math.Cos(MathHelper.ToRadians(30))) &&
              Near(down.CosInner, (float)Math.Cos(MathHelper.ToRadians(20))),
            "a spot light stores its axis and the cosines of its half angles");
        Check(Near(down.ConeFactor(-Vector3.UnitZ), 1) && down.ConeFactor(Vector3.UnitX) == 0 &&
              down.ConeFactor(Vector3.Normalize(new Vector3(1, 0, -1))) == 0,
            "the cone is fully lit on its axis and dark outside the spot angle");
        float edge = down.ConeFactor(Vector3.Normalize(new Vector3((float)Math.Tan(MathHelper.ToRadians(25)), 0, -1)));
        Check(edge > 0 && edge < 1, "the cone fades between the inner and outer angle");
        Check(SpotLight.ConeFactor(Vector3.UnitX, Vector3.UnitZ, -2, -1) == 1 && SpotLight.ConeFactor(-Vector3.UnitZ, Vector3.UnitZ, -2, -1) == 1,
            "point light cone constants (-2, -1) light every direction");

        var sideways = new SpotLight(Vector3.Zero, 10, Color.White, 5, Vector3.UnitX);
        Check(NearV(sideways.Direction, Vector3.UnitX) && NearV(new SpotLight(Vector3.Zero, 1, Color.White, 1, Vector3.UnitZ).Direction, Vector3.UnitZ),
            "a spot light can be built from any direction, including straight up");
        sideways.RotationMatrix = Matrix.Identity;
        Check(NearV(sideways.Direction, -Vector3.UnitZ), "rotating a spot light turns its cone (identity points down)");

        down.SpotAngle = 500;
        down.InnerSpotAngle = 400;
        Check(down.SpotAngle == SpotLight.MaxSpotAngle && down.InnerSpotAngle == down.SpotAngle,
            "spot angles are clamped and the inner angle never exceeds the outer one");
        down.SpotAngle = 60;
        Check(down.ConeTouchesCubeFace(-Vector3.UnitZ) && !down.ConeTouchesCubeFace(Vector3.UnitX) && !down.ConeTouchesCubeFace(Vector3.UnitZ),
            "a narrow downward cone only renders the -Z shadow cube face");
        down.SpotAngle = 120;
        Check(down.ConeTouchesCubeFace(Vector3.UnitX) && down.ConeTouchesCubeFace(-Vector3.UnitY) && !down.ConeTouchesCubeFace(Vector3.UnitZ),
            "a wide cone also renders the side faces it reaches");
        down.SpotAngle = 60;

        var clone = (SpotLight)new SpotLight(Vector3.One, 7, Color.Red, 3, Vector3.UnitY, 45, 30).Clone;
        Check(NearV(clone.Direction, Vector3.UnitY) && clone.SpotAngle == 45 && clone.InnerSpotAngle == 30 && clone.Radius == 7,
            "cloning a spot light keeps its cone");

        // Baked lighting sees the cone
        var bakeInput = new LightingBakeInput();
        bakeInput.PointLights.Add(new LightingBakeInput.PointLightInput
        {
            Position = new Vector3(0, 0, 5), Radius = 20, Radiance = Vector3.One * 10,
            SpotDirection = -Vector3.UnitZ, SpotCosOuter = down.CosOuter, SpotCosInner = down.CosInner,
        });
        var emptyBvh = new TriangleBvh(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<Vector3>());
        var bake = typeof(ProbeVolumeBaker).GetMethod("DirectIrradiance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var lit = (Vector3)bake.Invoke(null, new object[] { bakeInput, emptyBvh, Vector3.Zero, Vector3.UnitZ, 100f })!;
        var dark = (Vector3)bake.Invoke(null, new object[] { bakeInput, emptyBvh, new Vector3(10, 0, 0), Vector3.UnitZ, 100f })!;
        Check(lit.X > 0 && dark == Vector3.Zero, "baked lighting only receives spot light inside the cone");

        // Editor bridge + inspector
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();

        bridge.EnqueueAddSpotLight(new Vector3(1, 2, 3), Vector3.UnitX, 15, Color.Orange, 12, 50);
        Publish();
        var spot = logic.PointLights.OfType<SpotLight>().Single();
        Check(spot.Position == new Vector3(1, 2, 3) && NearV(spot.Direction, Vector3.UnitX) && spot.SpotAngle == 50 && spot.Radius == 15,
            "the bridge adds a spot light on the game thread");

        var snap = bridge.Snapshot.Single(s => s.Id == spot.Id);
        Check(snap.Kind == EditorObjectKind.SpotLight && snap.Light is { IsSpot: true, SpotAngle: 50f } &&
              NearV(Vector3.TransformNormal(SpotLight.LocalDirection, snap.Rotation), Vector3.UnitX),
            "spot light snapshots carry their kind, cone and rotation");

        var vm = objects.Single(o => o.EngineId == spot.Id);
        Check(vm.Type == SceneObjectType.Light && vm.Light is { Type: LightType.Spot, IsSpot: true, HasRadius: true } &&
              Math.Abs(vm.Light.SpotAngle - 50) < 0.001,
            "the inspector shows a spot light with its cone");

        vm.Light!.SpotAngle = 80;
        vm.Light.InnerSpotAngle = 20;
        // The light points along +X, so at least one angle changes and the last push is (0, 0, 0).
        vm.RotationX = 0; vm.RotationY = 0; vm.RotationZ = 0;
        Publish();
        Check(spot.SpotAngle == 80 && spot.InnerSpotAngle == 20 && NearV(spot.Direction, -Vector3.UnitZ),
            "inspector cone and rotation edits reach the engine spot light");

        // Play mode puts the rotation back
        var rotation = spot.RotationMatrix;
        var play = new PlayModeController(logic);
        play.Play();
        spot.RotationMatrix = SpotLight.RotationFromDirection(Vector3.UnitY);
        play.Stop();
        Check(NearV(spot.Direction, -Vector3.UnitZ) && spot.RotationMatrix == rotation,
            "Stop restores a spot light's rotation");

        // Save / load
        logic.PointLights.Add(new PointLight(Vector3.Zero, 5, Color.White, 1, false, false, 256, 0, false));
        spot.Name = "Stage Spot";
        spot.CastShadows = true;
        spot.RotationMatrix = SpotLight.RotationFromDirection(new Vector3(1, 1, -1));
        string file = Path.Combine(Path.GetTempPath(), $"anvil-spot-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var loaded = SceneSerialization.LoadFromFile(file, assets);
            var loadedSpot = loaded.PointLights.OfType<SpotLight>().Single();
            Check(loaded.PointLights.Count == 2 && loaded.PointLights.Count(l => l is not SpotLight) == 1 &&
                  loadedSpot.Id == spot.Id && loadedSpot.Name == "Stage Spot" && loadedSpot.CastShadows &&
                  NearV(loadedSpot.Direction, spot.Direction) && loadedSpot.SpotAngle == 80 && loadedSpot.InnerSpotAngle == 20 &&
                  loadedSpot.Color == Color.Orange && loadedSpot.Intensity == 12,
                "spot lights save and load with their cone and rotation, next to point lights");
        }
        finally { File.Delete(file); }

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    /// <summary>The compiled light shaders keep the cone uniforms (unused ones would be stripped).</summary>
    public static void RunGraphics(Microsoft.Xna.Framework.Content.ContentManager content)
    {
        var deferred = content.Load<Microsoft.Xna.Framework.Graphics.Effect>("Shaders/Deferred/DeferredPointLight");
        var forward = content.Load<Microsoft.Xna.Framework.Graphics.Effect>("Shaders/Forward/Forward");
        var froxel = content.Load<Microsoft.Xna.Framework.Graphics.Effect>("Shaders/Deferred/Froxel");
        Check(deferred.Parameters["spotDirection"] != null && deferred.Parameters["spotCosOuter"] != null &&
              deferred.Parameters["spotCosInner"] != null,
            "DeferredPointLight.fx exposes the spot cone");
        Check(forward.Parameters["LightSpotDirectionWS"] != null && forward.Parameters["LightSpotCos"] != null,
            "Forward.fx exposes per-light spot cones");
        Check(froxel.Parameters["PointLightSpotDirectionsVS"] != null && froxel.Parameters["PointLightSpotCos"] != null,
            "Froxel.fx exposes per-light spot cones");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
