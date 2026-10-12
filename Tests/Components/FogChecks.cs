using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;

/// <summary>Scene volumetric fog (Environment), Fog Volume components and culling of stretched meshes.</summary>
internal static class FogChecks
{
    public static void Run()
    {
        CheckSceneFog();
        CheckFogVolume();
        CheckStretchedCulling();
    }

    private static void CheckSceneFog()
    {
        var defaults = new EnvironmentSettings();
        Check(defaults.FogDensity == 0.03f && defaults.FogAbsorption == 0.1f && defaults.FogSunScatter == 0.1f &&
              defaults.FogLightScatter == 1.5f && defaults.FogAnisotropy == 0.45f && defaults.FogStartDistance == 30 &&
              defaults.FogFullDistance == 400 && defaults.FogNearDensity == 0 && defaults.FogSkyStrength == 0.1f &&
              defaults.FogSkyShaftDistance == 200 && defaults.MaxAutoExposure == 3,
            "a new scene's fog is the previous global outdoor haze, without a near-camera floor or exposure cap");

        var logic = new MainSceneLogic();
        var assets = new Assets();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var vm = new EnvironmentViewModel();
        vm.AttachBridge(bridge);
        vm.FogDensity = 0.05; vm.FogSunScatter = 0.4; vm.FogStartDistance = 5; vm.FogFullDistance = 50;
        vm.FogNearDensity = 0.5; vm.FogSkyShaftDistance = 80; vm.MaxAutoExposure = 0.5;
        Drain(bridge);
        var fog = logic.ActiveScene.Environment;
        Check(fog.FogDensity == 0.05f && fog.FogSunScatter == 0.4f && fog.FogStartDistance == 5 && fog.FogFullDistance == 50 &&
              fog.FogNearDensity == 0.5f && fog.FogSkyShaftDistance == 80 && fog.MaxAutoExposure == 0.5f && logic.ActiveScene.IsDirty,
            "Environment > Volumetric Fog edits reach the scene and mark it dirty");

        Engine.Renderer.Renderer.ApplySceneFog(fog);
        Check(GameSettings.g_FroxelDensity == 0.05f && GameSettings.g_FroxelDirectionalScatter == 0.4f &&
              GameSettings.g_FroxelFogDistanceStart == 5 && GameSettings.g_FroxelFogDistanceFull == 50 &&
              GameSettings.g_FroxelNearDensity == 0.5f && GameSettings.g_FroxelSkyShaftDistance == 80,
            "the renderer copies the scene fog into the settings the froxel passes read");

        string file = Path.Combine(Path.GetTempPath(), $"anvil-fog-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.FogDensity == 0.05f && loaded.FogNearDensity == 0.5f && loaded.FogFullDistance == 50 &&
                  loaded.MaxAutoExposure == 0.5f, "scene fog survives save/load");
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            json["Environment"]!.AsObject().Remove("FogDensity");
            json["Environment"]!.AsObject().Remove("MaxAutoExposure");
            File.WriteAllText(file, json.ToJsonString());
            loaded = SceneSerialization.LoadFromFile(file, assets).Environment;
            Check(loaded.FogDensity == 0.03f && loaded.MaxAutoExposure == 3, "older scenes get the default fog");
        }
        finally { File.Delete(file); }

        bridge.EnqueueMutateEnvironment(s => { s.FogDensity = float.NaN; s.FogStartDistance = 100; s.FogFullDistance = 20; s.FogNearDensity = 3; });
        Drain(bridge);
        fog = logic.ActiveScene.Environment;
        Check(fog.FogDensity == 0.03f && fog.FogFullDistance == 101 && fog.FogNearDensity == 1,
            "invalid fog values are normalized (full distance stays past the start)");
        vm.ResetFogCommand.Execute(null);
        Drain(bridge);
        fog = logic.ActiveScene.Environment;
        Check(fog.FogDensity == 0.03f && fog.FogStartDistance == 30 && fog.FogFullDistance == 400 && fog.MaxAutoExposure == 3,
            "Reset fog restores the default outdoor haze");
    }

    private static void CheckFogVolume()
    {
        Check(ComponentRegistry.Find(FogVolumeComponent.TypeId)?.ComponentType == typeof(FogVolumeComponent) &&
              ComponentEditorRegistry.Supports(FogVolumeComponent.TypeId),
            "Fog Volume is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var room = new BasicEntity(assets.Cube, null, new Vector3(10, 0, 5), Matrix.Identity, new Vector3(4, 6, 5))
            { Name = "Dust", IsEnabled = true };
        logic.BasicEntities.Add(room);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Fog Volume").AddCommand.Execute(null);
        Publish();
        var volume = room.GetComponent<FogVolumeComponent>();
        var editor = owner.Components.OfType<FogVolumeComponentViewModel>().Single();
        Check(volume is { Density: 0.15f, EdgeFade: 0.1f } && room.WorldTransform.Hidden && editor.Density == 0.15,
            "a new Fog Volume adds 0.15 density with a soft edge and hides its box mesh");

        editor.Density = 0.4;
        editor.EdgeFade = 0;
        Publish();
        Check(volume.Density == 0.4f && volume.EdgeFade == 0, "inspector edits reach the engine");
        Check(volume.DensityAt(room, new Vector3(10, 0, 5)) == 0.4f && volume.DensityAt(room, new Vector3(13.9f, 5.9f, 9.9f)) == 0.4f &&
              volume.DensityAt(room, new Vector3(14.1f, 0, 5)) == 0, "the box is the gameobject's position and scale (a Cube)");
        volume.EdgeFade = 0.5f;
        Check(Math.Abs(volume.DensityAt(room, new Vector3(13, 0, 5)) - 0.4f * 0.25f / 0.5f) < 1e-4f,
            "Edge Fade ramps the density in from the faces");

        var other = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Off", IsEnabled = true };
        other.AddComponent(new FogVolumeComponent { Enabled = false });
        var plain = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
        var worldToBox = new Matrix[FogVolumeComponent.MaxVolumes];
        var settings = new Vector4[FogVolumeComponent.MaxVolumes];
        room.WorldTransform.Hidden = false; // as after a scene load, which skips the component hooks
        int count = FogVolumeComponent.Collect(new List<BasicEntity> { plain, other, room }, worldToBox, settings);
        Check(count == 1 && settings[0] == new Vector4(0.4f, 0.5f, 0, 0) &&
              Vector3.Distance(Vector3.Transform(new Vector3(14, 6, 10), worldToBox[0]), Vector3.One) < 1e-4f &&
              room.WorldTransform.Hidden && !other.WorldTransform.Hidden && !plain.WorldTransform.Hidden,
            "the renderer collects enabled volumes as world-to-box matrices and keeps their meshes hidden");
        var many = Enumerable.Range(0, 12).Select(_ =>
        {
            var e = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
            e.AddComponent(new FogVolumeComponent());
            return e;
        }).ToList();
        Check(FogVolumeComponent.Collect(many, worldToBox, settings) == FogVolumeComponent.MaxVolumes,
            "at most 8 volumes are sent to the shader");

        editor.Enabled = false;
        Publish();
        Check(!volume.Enabled && !room.WorldTransform.Hidden, "disabling the volume shows its mesh again");
        editor.Enabled = true;
        Publish();
        var record = ComponentRegistry.Capture(volume);
        Check(!record.Data.TryGetProperty(nameof(FogVolumeComponent.ClampedDensity), out _) &&
              ComponentRegistry.Restore(record) is FogVolumeComponent { Density: 0.4f, EdgeFade: 0.5f },
            "saved Fog Volume records round-trip");
        var copied = ((BasicEntity)room.Clone).GetComponent<FogVolumeComponent>();
        copied.Density = 2;
        Check(!ReferenceEquals(copied, volume) && volume.Density == 0.4f, "cloning copies the volume independently");
        editor.RemoveCommand.Execute(null);
        Publish();
        Check(room.GetComponent<FogVolumeComponent>() == null && !room.WorldTransform.Hidden,
            "removing the component shows the mesh again");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    // A 6 x 120 m roof slat whose centre is off screen but whose end is in view must still draw (and cast shadows).
    private static void CheckStretchedCulling()
    {
        var library = new MeshLibrary { MeshBoundingSphere = new BoundingSphere(Vector3.Zero, (float)Math.Sqrt(3)) };
        var slat = new TransformMatrix(Matrix.CreateScale(3, 60, 0.5f), 1);
        library.Register(slat);
        Matrix view = Matrix.CreateLookAt(new Vector3(0, 55, 20), new Vector3(0, 55, 0), Vector3.UnitY);
        var frustum = new BoundingFrustum(view * Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(20), 1, 0.1f, 100));
        library.UpdatePositionAndCheckRender(true, frustum, new Vector3(0, 55, 20), new BoundingSphere());
        Check(library.Rendered[0], "culling sizes the bounds by the largest axis scale, not Scale.X");
        var away = new BoundingFrustum(Matrix.CreateLookAt(new Vector3(0, 200, 20), new Vector3(0, 200, 0), Vector3.UnitY) *
            Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(20), 1, 0.1f, 100));
        library.UpdatePositionAndCheckRender(true, away, new Vector3(0, 200, 20), new BoundingSphere());
        Check(!library.Rendered[0], "a mesh far outside the view is still culled");
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 4; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
