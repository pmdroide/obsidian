using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;

/// <summary>
/// Liquid component (Half-Life: Alyx's bottle liquid, Docs/Research/BottleLiquid): the fill tables measured from the
/// Bottle mesh, the agitation / foam / down-direction rules recovered from server.dll, the component through the
/// bridge and inspector, saving, and the LiquidTest sample.
/// </summary>
internal static class LiquidChecks
{
    private const float Dt = 1 / 60f;
    private static readonly Vector3 Gravity = new(0, 0, -9.81f);

    public static void Run()
    {
        LiquidVolume bottle = CheckVolume();
        CheckSlosh(bottle);
        CheckComponent();
        CheckSampleScene();
    }

    private static LiquidVolume CheckVolume()
    {
        // A 0.1 x 0.1 x 0.3 box from z = 0: half full is its middle, whichever way up.
        LiquidVolume box = LiquidVolume.Compute(BoxVertices(out int[] boxIndices), boxIndices,
            new BoundingBox(new Vector3(-0.05f, -0.05f, 0), new Vector3(0.05f, 0.05f, 0.3f)));
        int mid = LiquidVolume.Samples / 2, quarter = LiquidVolume.Samples / 4;
        Check(Math.Abs(box.Volume - 0.003f) < 1e-6f && Math.Abs(box.Upwards[mid]) < 1e-4f && Math.Abs(box.Sidewards[mid]) < 1e-4f &&
              Math.Abs(box.Upwards[quarter] + 0.075f) < 1e-4f && Math.Abs(box.Downwards[quarter] + 0.075f) < 1e-4f,
            "a box's volume is exact and its fill heights split it by volume upright, on its side and upside down");
        Check(box.Upwards[0] < -0.15f && box.Upwards[^1] > 0.15f, "empty and full sit just outside the container");

        // The built-in Bottle (Content/GameObjects/Bottle/bottle.obj): closed, outward faces, narrow neck.
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "GameObjects", "Bottle", "bottle.obj");
        Vector3[] vertices = ReadObj(path, out int[] indices);
        var bounds = BoundingBox.CreateFromPoints(vertices);
        LiquidVolume bottle = LiquidVolume.Compute(vertices, indices, bounds);
        float signed = 0;
        for (int t = 0; t < indices.Length; t += 3)
            signed += Vector3.Dot(vertices[indices[t]], Vector3.Cross(vertices[indices[t + 1]], vertices[indices[t + 2]])) / 6;
        Check(signed > 0 && bottle.Volume is > 0.0009f and < 0.0013f,
            $"the Bottle mesh is closed with outward (counter-clockwise) faces, {bottle.Volume * 1000:0.00} litres");
        Check(bounds.Min.Z > -0.001f && Math.Abs(bounds.Max.Z - 0.3f) < 0.001f && Math.Abs(bottle.Radius - 0.037f) < 0.001f,
            "the Bottle stands on its origin, 0.30 m tall and 7.4 cm wide");
        Check(bottle.Upwards[mid] < -0.03f && Math.Abs(bottle.Downwards[mid] + bottle.Upwards[mid]) < 1e-3f &&
              Enumerable.Range(1, LiquidVolume.Samples - 1).All(i => bottle.Upwards[i] > bottle.Upwards[i - 1] && bottle.Sidewards[i] > bottle.Sidewards[i - 1]),
            "half a bottle sits well below its middle (the neck holds little), at the same plane upside down, and every table rises with the fill");
        Check(Math.Abs(bottle.Height(1, 0.5f) - bottle.Upwards[mid]) < 1e-5f && Math.Abs(bottle.Height(0, 0.5f) - bottle.Sidewards[mid]) < 1e-5f &&
              Math.Abs(bottle.Height(-1, 0.5f) - bottle.Downwards[mid]) < 1e-5f &&
              Math.Abs(bottle.Height(0.5f, 0.5f) - (bottle.Upwards[mid] + bottle.Sidewards[mid]) / 2) < 1e-5f,
            "the height blends upright -> side -> upside down with the container's tilt (Alyx's three tables)");
        return bottle;
    }

    private static void CheckSlosh(LiquidVolume bottle)
    {
        var liquid = new LiquidSlosh { Fill = 0.5f };
        Vector3 position = new(0, 0, 0.75f);
        Run(120, Vector3.Zero);
        Check(liquid.Agitation == 0 && liquid.Foam == 0 && Vector3.Dot(liquid.SurfaceUp, Vector3.UnitZ) > 0.999f &&
              Math.Abs(liquid.SurfaceHeight - (Vector3.Dot(liquid.Centre, liquid.SurfaceUp) + bottle.Upwards[LiquidVolume.Samples / 2])) < 1e-4f && Math.Abs(liquid.Centre.Z - 0.75f - bottle.Centre.Z) < 1e-5f,
            "at rest the surface is level at the half-full height");

        Run(40, new Vector3(1, 0, 0));
        Check(liquid.Down.X < -0.05f && liquid.Down.Z < -0.9f,
            $"moving at 1 m/s the down direction leans against the motion ({liquid.Down.X:0.000}), as gravity - velocity in Source units");
        Run(12, Vector3.Zero);
        float shaken = liquid.Agitation;
        Check(shaken > 0.9f && liquid.Foam > 0,
            $"stopping (a 39 in/s velocity change) agitates it to the max ({shaken:0.00}) and makes foam ({liquid.Foam:0.000})");
        Run(10 * 60, Vector3.Zero);
        Check(liquid.Agitation is > 0.35f and < 0.6f, $"agitation decays linearly over the 20 s settle time (10 s: {liquid.Agitation:0.00})");
        float held = liquid.Agitation;
        liquid.Update(Matrix.CreateTranslation(position), Gravity, 0, bottle);
        Check(liquid.Agitation == held, "a zero time step (pause menu) holds it");
        Run(11 * 60, Vector3.Zero);
        Check(liquid.Agitation == 0 && Vector3.Dot(liquid.Down, -Vector3.UnitZ) > 0.9999f, "after the settle time it is still and level again");

        liquid.Agitate(0.03f);
        Run(12, Vector3.Zero);
        Check(liquid.Agitation is > 0 and < 0.3f, "a small knock (Agitate) shakes it a little");
        liquid.Settle();
        Check(liquid.Agitation == 0 && liquid.Foam == 0, "Settle calms it at once");

        // On its side the surface still faces up, at the side table's height.
        Matrix side = Matrix.CreateRotationX(-MathHelper.PiOver2) * Matrix.CreateTranslation(position);
        for (int i = 0; i < 5; i++) liquid.Update(side, Gravity, Dt, bottle);
        Check(Math.Abs(liquid.CosUp) < 0.01f && Vector3.Dot(liquid.SurfaceUp, Vector3.UnitZ) > 0.999f &&
              Math.Abs(liquid.SurfaceHeight - (Vector3.Dot(liquid.Centre, liquid.SurfaceUp) + bottle.Sidewards[LiquidVolume.Samples / 2])) < 1e-4f,
            "lying on its side the surface stays level, from the side table");

        liquid.Fill = 0;
        liquid.Update(side, Gravity, Dt, bottle);
        Check(liquid.Empty && liquid.SurfaceHeight == float.MinValue, "an empty container draws no liquid");

        void Run(int frames, Vector3 velocity)
        {
            for (int i = 0; i < frames; i++)
            {
                position += velocity * Dt;
                liquid.Update(Matrix.CreateTranslation(position), Gravity, Dt, bottle);
            }
        }
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(LiquidComponent.TypeId)?.ComponentType == typeof(LiquidComponent) &&
              ComponentEditorRegistry.Supports(LiquidComponent.TypeId),
            "Liquid is registered and offered in Add Component");

        var assets = new Assets { Bottle = new ModelDefinition(null, new BoundingBox(new Vector3(-0.04f, -0.04f, 0), new Vector3(0.04f, 0.04f, 0.3f))) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Bottle, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Bottle", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Liquid").AddCommand.Execute(null);
        Publish();
        var liquid = entity.GetComponent<LiquidComponent>();
        var editor = owner.Components.OfType<LiquidComponentViewModel>().Single();
        Check(liquid is { Fill: 0.6f, SettleTime: 20, FoamSettleTime: 30, Sensitivity: 1, Lean: 1, WobbleScale: 0.02f, SurfaceTension: 1, Enabled: true } &&
              Math.Abs(editor.Fill - 0.6) < 1e-6,
            "Liquid starts with Alyx's settle times, wobble and meniscus");

        editor.Fill = 0.25;
        editor.LiquidColor = Avalonia.Media.Color.FromRgb(110, 12, 30);
        editor.Opacity = double.NaN;
        editor.SettleTime = 5;
        Publish();
        Check(liquid is { Fill: 0.25f, Opacity: 0.75f, SettleTime: 5 } && liquid.LiquidColor == new Color(110, 12, 30),
            "inspector edits (fill, colour) reach the engine and invalid input is ignored");
        liquid.Fizz = 2;
        Publish();
        Check(editor.Fizz == 2, "engine-side changes show in the inspector");

        LiquidComponent.UpdateAll(logic.BasicEntities, Dt, Gravity);
        Check(entity.WorldTransform.Liquid == liquid.State && entity.WorldTransform.LiquidLook.Opacity == 0.75f,
            "each frame the liquid's state and colours go on the gameobject's transform for the forward shader");
        liquid.Enabled = false;
        LiquidComponent.UpdateAll(logic.BasicEntities, Dt, Gravity);
        Check(entity.WorldTransform.Liquid == null, "a disabled component draws no liquid");
        liquid.Enabled = true;

        var record = ComponentRegistry.Capture(liquid);
        Check(!record.Data.TryGetProperty(nameof(LiquidComponent.State), out _) &&
              record.Data.GetProperty(nameof(LiquidComponent.LiquidColor)).ValueKind == System.Text.Json.JsonValueKind.Array &&
              ComponentRegistry.Restore(record) is LiquidComponent { Fill: 0.25f, SettleTime: 5, Fizz: 2 } restored &&
              restored.LiquidColor == new Color(110, 12, 30),
            "saved records round-trip the settings and colours ([r,g,b,a]) and leave out the runtime state");
        var copied = ((BasicEntity)entity.Clone).GetComponent<LiquidComponent>();
        copied.Fill = 0.9f;
        Check(!ReferenceEquals(copied.State, liquid.State) && liquid.Fill == 0.25f, "cloning gives the copy its own liquid");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(new Vector3(-0.04f, -0.04f, 0), new Vector3(0.04f, 0.04f, 0.3f));
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)), Bottle = new ModelDefinition(null, bounds) };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "LiquidTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var bottles = scene.BasicEntities.Where(e => e.ModelDefinition == assets.Bottle).ToList();
        Check(bottles.Count == 6 && bottles.All(b => b.GetComponent<LiquidComponent>() is { Enabled: true } && b.Material == null) &&
              bottles.Count(b => b.GetComponent<ScriptBehaviourComponent>()?.ScriptId == "liquid-test") == 3,
            "the liquid sample has six filled bottles (glass from the model) and three moved by the Liquid Test script");
    }

    private static Vector3[] BoxVertices(out int[] indices)
    {
        var min = new Vector3(-0.05f, -0.05f, 0);
        var max = new Vector3(0.05f, 0.05f, 0.3f);
        var v = new Vector3[8];
        for (int i = 0; i < 8; i++)
            v[i] = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        // Two counter-clockwise (outward) triangles per face.
        indices = new[]
        {
            0, 2, 3, 0, 3, 1, // -Z
            4, 5, 7, 4, 7, 6, // +Z
            0, 1, 5, 0, 5, 4, // -Y
            2, 6, 7, 2, 7, 3, // +Y
            0, 4, 6, 0, 6, 2, // -X
            1, 3, 7, 1, 7, 5, // +X
        };
        return v;
    }

    private static Vector3[] ReadObj(string path, out int[] indices)
    {
        var vertices = new List<Vector3>();
        var faces = new List<int>();
        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[0] == "v")
                vertices.Add(new Vector3(float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture)));
            else if (parts.Length == 4 && parts[0] == "f")
                for (int i = 1; i <= 3; i++) faces.Add(int.Parse(parts[i].Split('/')[0]) - 1);
        }
        indices = faces.ToArray();
        return vertices.ToArray();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
