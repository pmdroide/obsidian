using System.Reflection;
using Anvil.ViewModels;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;

/// <summary>The title bar's GameObject menu and the Hierarchy "+" menu create scene objects.</summary>
internal static class GameObjectMenuChecks
{
    public static void Run()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds) };
        var logic = new MainSceneLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var vm = new MainWindowViewModel();
        vm.AttachBridge(bridge);

        int entities = logic.BasicEntities.Count, suns = logic.DirectionalLights.Count, points = logic.PointLights.Count;
        vm.AddEntityCommand.Execute("Cube");
        vm.AddEntityCommand.Execute("IsoSphere");
        vm.AddDirectionalLightCommand.Execute(null);
        vm.AddPointLightCommand.Execute(null);
        vm.AddSpotLightCommand.Execute(null);
        Check(logic.BasicEntities.Count == entities, "GameObject menu commands wait for the game thread");
        Drain();
        Check(logic.BasicEntities.Count == entities + 2 &&
              ReferenceEquals(logic.BasicEntities[^2].ModelDefinition, assets.Cube) &&
              ReferenceEquals(logic.BasicEntities[^1].ModelDefinition, assets.IsoSphere),
            "GameObject > 3D Object creates a Cube and a Sphere");
        Check(logic.DirectionalLights.Count == suns + 1 && logic.DirectionalLights[^1].Intensity >= 100 &&
              logic.PointLights.Count == points + 2 && logic.PointLights[^2] is not SpotLight &&
              logic.PointLights[^1] is SpotLight { SpotAngle: 60f } spot && spot.Direction == -Vector3.UnitZ,
            "GameObject > Light creates a visible directional light, a point light and a downward spot light");

        Check(vm.AddableObjects.Select(o => o.DisplayName).SequenceEqual(new[] { "Cube", "Sphere", "Directional Light", "Point Light", "Spot Light" }),
            "the Hierarchy + menu offers the same objects as the GameObject menu");
        foreach (var option in vm.AddableObjects) option.AddCommand.Execute(null);
        Drain();
        Check(logic.BasicEntities.Count == entities + 4 && logic.DirectionalLights.Count == suns + 2 && logic.PointLights.Count == points + 4,
            "every Hierarchy + menu entry creates its object");

        void Drain() =>
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
