using System.Collections.ObjectModel;
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
using World = CollisionChecks.World;

/// <summary>
/// Breakable glass: the Half-Life: Alyx crack pattern and the fracture math (GPU-free), the Breakable Glass
/// component and its editor, and a pane in Play broken by a script and by a thrown body.
/// </summary>
internal static class GlassChecks
{
    private const float Dt = 1 / 60f;
    private static readonly GameTime Frame = new(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));

    public static void Run()
    {
        CheckCrack();
        CheckFracture();
        CheckComponent();
        CheckPlay();
        CheckSampleScene();
    }

    private static void CheckSampleScene()
    {
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            IsoSphere = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
            Cloth = new ModelDefinition(null, new BoundingBox(new Vector3(-1, 0, -1), new Vector3(1, 0, 1))),
        };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "CollisionTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var panes = scene.BasicEntities.Where(e => e.GetComponent<BreakableGlassComponent>() != null).ToList();
        Check(panes.Select(e => e.Name).OrderBy(n => n).SequenceEqual(new[] { "Small Window Pane", "Window Pane" }) &&
              panes.All(e => e.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Static } &&
                             e.GetComponent<MaterialComponent>() is { IsTransparent: true }) &&
              panes.Single(e => e.Name == "Small Window Pane").GetComponent<BreakableGlassComponent>()!.CrackSize == 0.2f,
            "the collision sample has two solid, transparent breakable windows (the small one cracks finer)");
    }

    private static void CheckCrack()
    {
        var half = new Vector2(1.2f, 1f);
        List<Vector2[]> cells = GlassFracture.CrackPane(half, 0.381f, new Random(5));
        float area = cells.Sum(c => GlassFracture.Area(c));
        Check(cells.Count is >= 20 and <= 60 && Math.Abs(area - 4.8f) < 1e-3f && cells.All(c => GlassFracture.Area(c) > 0),
            $"the first crack splits a 2.4 x 2 m pane into {cells.Count} counter-clockwise Voronoi panels covering it exactly (area {area:0.0000} m²)");
        Check(cells.All(IsConvex), "every panel is convex (so it can be a convex hull collider)");
        List<Vector2[]> again = GlassFracture.CrackPane(half, 0.381f, new Random(5));
        Check(again.Count == cells.Count && again.Zip(cells).All(p => p.First.SequenceEqual(p.Second)), "the crack is deterministic for a seed");
        Check(GlassFracture.CrackPane(new Vector2(0.15f, 1), 0.381f, new Random(1)).Count == 1,
            "a pane narrower than one cell stays one panel (Valve skips the crack too)");
        Check(GlassFracture.Supported(cells, half).All(s => s), "an untouched crack pattern hangs entirely in the frame");
    }

    private static void CheckFracture()
    {
        Vector2[] square = GlassFracture.Rectangle(new Vector2(0.5f));
        List<Vector2[]> pieces = GlassFracture.Voronoi(square, GlassFracture.ImpactSites(new Vector2(0.1f, 0), 0.1f, new Random(3)));
        Check(pieces.Count > 20 && Math.Abs(pieces.Sum(p => GlassFracture.Area(p)) - 1) < 1e-3f && pieces.All(IsConvex),
            $"an impact splits a panel into {pieces.Count} radial convex pieces that fill it");

        Vector2[] small = GlassFracture.Rectangle(new Vector2(0.1f));
        Check(GlassFracture.HitTest(small, new Vector2(0.15f, 0), 2) && !GlassFracture.HitTest(small, new Vector2(0.15f, 0), 1)
              && !GlassFracture.HitTest(small, new Vector2(0.25f, 0), 2),
            "the hit test scales a panel by the hit tolerance about its centroid (Half-Life: Alyx: 2)");

        var half = new Vector2(1, 1);
        var shards = new List<Vector2[]>
        {
            Box(-1, -1, -0.5f, 1),       // on the left border
            Box(-0.5f, -0.2f, 0, 0.2f),  // shares an edge with it
            Box(0.2f, -0.1f, 0.4f, 0.1f), // floating
            Box(0, 0.2f, 0.2f, 0.4f),     // touches the second only at a corner
        };
        bool[] supported = GlassFracture.Supported(shards, half);
        Check(supported.SequenceEqual(new[] { true, true, false, false }),
            "panels hang on through shared edges to the border; floating ones and corner-only contacts fall");
        Check(Math.Abs(GlassFracture.Area(GlassFracture.Clip(square, Vector2.Zero, Vector2.UnitX)) - 0.5f) < 1e-5f,
            "clipping a polygon by a half-plane keeps the right part");

        Vector2[] inset = GlassFracture.Inset(square, 0.01f, out float used);
        Check(used == 0.01f && Math.Abs(GlassFracture.Area(inset) - 0.98f * 0.98f) < 1e-5f
              && inset.Zip(square).All(p => Math.Abs(Math.Abs(p.First.X - p.Second.X) - 0.01f) < 1e-5f && Math.Abs(Math.Abs(p.First.Y - p.Second.Y) - 0.01f) < 1e-5f),
            "the edge bevel moves every side of a panel inwards by its width, vertex for vertex");
        Vector2[] sliver = GlassFracture.Inset(GlassFracture.Rectangle(new Vector2(0.5f, 0.002f)), 0.01f, out float sliverWidth);
        Check(sliverWidth < 0.002f && GlassFracture.Area(sliver) > 0 && pieces.All(p => GlassFracture.Area(GlassFracture.Inset(p, 0.003f, out _)) > 0),
            "the bevel narrows on thin shards and never turns a radial piece inside out");
        Check(GlassFracture.IsBorderEdge(new Vector2(-1, -1), new Vector2(-1, 0.5f), half) && !GlassFracture.IsBorderEdge(new Vector2(-1, 0), new Vector2(0, 0), half),
            "edges on the pane's sides count as frame, the others as cracks");
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(BreakableGlassComponent.TypeId)?.ComponentType == typeof(BreakableGlassComponent) &&
              ComponentEditorRegistry.Supports(BreakableGlassComponent.TypeId),
            "Breakable Glass is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, new Vector3(0, 0, 2), Matrix.Identity, new Vector3(1.2f, 0.02f, 1)) { Name = "Window", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Breakable Glass").AddCommand.Execute(null);
        Publish();
        var glass = entity.GetComponent<BreakableGlassComponent>();
        var editor = owner.Components.OfType<BreakableGlassComponentViewModel>().Single();
        Check(glass is { CrackSize: 0.381f, HoleRadius: 0.06f, DebrisLifetime: 15, HitTolerance: 2, DropLooseShards: true, IsBroken: false },
            "Breakable Glass defaults to Half-Life: Alyx's crack size, shard lifetime and hit tolerance");
        Check(glass is { DamagedMaterial: "", EdgeMaterial: "", EdgeWidth: 0.003f, EdgeTextureSize: 0.508f } && editor.DamagedMaterialIndex == 0 && editor.MaterialChoices[0] == "Built-in",
            "cracked glass defaults to the built-in damaged and edge looks (edge texture every 0.508 m, Half-Life: Alyx's 0.05 per inch)");

        editor.CrackSize = 0.5;
        editor.HoleRadius = 0.1;
        editor.ImpactSpeed = 6;
        editor.ImpactMomentum = 3;
        editor.DebrisLifetime = 8;
        editor.HitTolerance = 1.5;
        editor.DropLooseShards = false;
        editor.Distort = false;
        editor.SolidShards = false;
        editor.EdgeWidth = 0.005;
        editor.EdgeTextureSize = 1;
        string material = editor.MaterialChoices.Count > 1 ? editor.MaterialChoices[1] : "";
        editor.DamagedMaterialIndex = editor.MaterialChoices.Count > 1 ? 1 : 0;
        Publish();
        Check(glass is { EdgeWidth: 0.005f, EdgeTextureSize: 1 } && glass.DamagedMaterial == material && glass.EdgeMaterial == "",
            $"inspector edits pick the damaged material ('{material}') and the edge bevel");
        Check(glass is { CrackSize: 0.5f, HoleRadius: 0.1f, ImpactSpeed: 6, ImpactMomentum: 3, DebrisLifetime: 8, HitTolerance: 1.5f,
                DropLooseShards: false, Distort: false, SolidShards: false },
            "inspector edits reach the engine component");
        editor.CrackSize = double.NaN;
        editor.HitTolerance = 9;
        Publish();
        Check(glass is { CrackSize: 0.5f, HitTolerance: 5 }, "invalid editor values are ignored or clamped");

        var record = ComponentRegistry.Capture(glass);
        Check(!record.Data.TryGetProperty(nameof(BreakableGlassComponent.Glass), out _) && !record.Data.TryGetProperty(nameof(BreakableGlassComponent.IsBroken), out _) &&
              ComponentRegistry.Restore(record) is BreakableGlassComponent { CrackSize: 0.5f, DebrisLifetime: 8, SolidShards: false, EdgeWidth: 0.005f } restored
              && restored.DamagedMaterial == material,
            "saved Breakable Glass records round-trip their settings and exclude runtime state");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckPlay()
    {
        using var world = new World();
        world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(6, 6, 0.5f), PhysicsBodyType.Static, recorder: false);
        // A 2.4 x 2 m pane, 4 cm thick, facing Y, 1 m above the floor.
        BasicEntity pane = world.Add("Window", new Vector3(0, 0, 2), new Vector3(1.2f, 0.02f, 1), PhysicsBodyType.Static, recorder: false);
        var glass = new BreakableGlassComponent { DebrisLifetime = 1.5f };
        pane.AddComponent(glass);
        var logic = new MainSceneLogic();
        logic.BasicEntities.AddRange(world.Entities);
        world.Step(1);
        var play = new PlayModeController(logic);
        play.Play();
        Step();
        Check(glass.Glass != null && !glass.IsBroken && pane.StaticBody != null, "Play arms the pane; it stays whole and solid until hit");
        Check(Math.Abs(Math.Max(glass.Glass!.Size.X, glass.Glass.Size.Y) - 2.4f) < 1e-5f && Math.Abs(Math.Min(glass.Glass.Size.X, glass.Glass.Size.Y) - 2) < 1e-5f && Math.Abs(glass.Glass.Thickness - 0.04f) < 1e-5f,
            "the pane's plane is the model's two long sides; its thinnest side is the thickness");

        // A bullet through the middle.
        Check(glass.Shatter(new Vector3(0, -0.02f, 2), Vector3.UnitY), "a script shatters the pane");
        int panels = glass.ShardCount, debris = glass.DebrisCount;
        Step();
        Check(glass.IsBroken && panels > 15 && debris > 3 && pane.StaticBody == null && pane.WorldTransform.Hidden,
            $"a bullet cracks it into {panels} panels, throws {debris} shards from the hole, and the shards replace the mesh and collider");
        Check(world.Scene.Raycast(new Vector3(0.7f, -2, 2.4f), Vector3.UnitY, 4, out RaycastHit hit) && hit.GameObject == pane && Math.Abs(hit.Point.Y + 0.02f) < 0.01f,
            "rays still hit the panels left in the frame, reported as the pane");
        for (int i = 0; i < 30; i++) Step();
        Check(!world.Scene.Raycast(new Vector3(0, -2, 2), Vector3.UnitY, 4, out _), "rays pass through the hole once its shards have fallen");
        Check(glass.DebrisCount > 0, "shards keep falling for their lifetime");
        for (int i = 0; i < 90; i++) Step();
        Check(glass.DebrisCount == 0, "fallen shards vanish after Debris Lifetime");

        // Many hits leave only panels still joined to the frame.
        for (int i = 0; i < 12; i++) glass.Shatter(new Vector3(-0.9f + i * 0.16f, 0, 2 + (i % 3 - 1) * 0.3f), Vector3.UnitY);
        bool[] supported = GlassFracture.Supported(glass.Glass.Shards, glass.Glass.Size / 2);
        Check(supported.All(s => s) && glass.ShardCount > 0, $"after 13 hits every panel left ({glass.ShardCount}) still hangs in the frame");

        glass.Restore();
        Step();
        Check(!glass.IsBroken && !pane.WorldTransform.Hidden && pane.StaticBody != null, "Restore puts the pane back whole and solid");

        // Thrown bodies: a slow one bounces, a fast one breaks through.
        BasicEntity slow = world.Add("Slow Crate", new Vector3(0.6f, -1, 2.3f), new Vector3(0.15f), PhysicsBodyType.Dynamic, mass: 1, recorder: false);
        logic.BasicEntities.Add(slow);
        world.Step(1);
        world.Scene.SetVelocity(slow, new Vector3(0, 2, 0), Vector3.Zero);
        for (int i = 0; i < 40; i++) Step();
        Check(!glass.IsBroken, "a crate hitting it at 2 m/s doesn't break it");
        world.Entities.Remove(slow);
        logic.BasicEntities.Remove(slow);

        BasicEntity crate = world.Add("Crate", new Vector3(0, -1.2f, 2.2f), new Vector3(0.15f), PhysicsBodyType.Dynamic, mass: 2, recorder: false);
        logic.BasicEntities.Add(crate);
        world.Step(1);
        world.Scene.SetVelocity(crate, new Vector3(0, 10, 0), Vector3.Zero);
        int shattered = 0;
        glass.Shattered += (_, stress, _) => { if (stress == GlassStress.Blunt) shattered++; };
        for (int i = 0; i < 30; i++) Step();
        Check(glass.IsBroken && shattered >= 1 && crate.Position.Y > 0.3f,
            $"a 2 kg crate thrown at 10 m/s breaks it and flies on through (y = {crate.Position.Y:0.00} m)");

        play.Stop();
        world.Step(1);
        Check(glass.Glass == null && pane.Glass == null && !pane.WorldTransform.Hidden && pane.StaticBody != null,
            "Stop removes the shards and puts the pane back");

        void Step()
        {
            play.UpdateScripts(Frame);
            world.Step(1);
        }
    }

    private static Vector2[] Box(float x0, float y0, float x1, float y1) =>
        new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

    private static bool IsConvex(Vector2[] polygon)
    {
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length], c = polygon[(i + 2) % polygon.Length];
            if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) < -1e-6f) return false;
        }
        return true;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
