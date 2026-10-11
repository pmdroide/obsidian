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
using Engine.Scripting;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using World = CollisionChecks.World;

/// <summary>
/// Cloth: welding and pins, the simulation against BEPU colliders (box colliders from model bounds, no GPU),
/// the Cloth component and its editor, and the cloth station of the CollisionTest sample.
/// </summary>
internal static class ClothChecks
{
    private const float Dt = 1 / 60f;
    private const int Segments = 24;
    private static readonly GameTime Frame = new(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
    // -90 degrees about X: the cloth plane (X/Z, front -Y) lies flat, front up, its model Z along world Y.
    private static readonly Matrix Flat = Matrix.CreateRotationX(-MathHelper.PiOver2);

    public static void Run()
    {
        try
        {
            CheckTopology();
            CheckHanging();
            CheckEdgeDrop();
            CheckColliders();
            CheckDrape();
            CheckMovingBody();
            CheckWindAndImpulse();
            CheckComponent();
            CheckSampleScene();
        }
        finally { ClothComponent.FallbackTopology = null; }
    }

    /// <summary>
    /// A sheet dropped exactly on its edge can't fold out of its plane. If its edges resisted being pushed together it
    /// would bounce like a spring, tangle and gain energy; it has to crumple and settle.
    /// </summary>
    private static void CheckEdgeDrop()
    {
        using var world = new World();
        world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(5, 5, 0.5f), PhysicsBodyType.Static, recorder: false);
        world.Step(1);
        Matrix pose = Matrix.CreateTranslation(0, 0, 1.5f);
        var cloth = new Cloth(Topology(), pose, ClothPinMode.None);
        float highestAfterLanding = 0, worstStretch = 0;
        for (int frame = 0; frame < 240; frame++)
        {
            world.Scene.CollectClothColliders(cloth.Bounds.Min - new Vector3(0.3f), cloth.Bounds.Max + new Vector3(0.3f), cloth.Colliders);
            Check(cloth.Step(Dt, pose), "a sheet dropped on its edge stays stable", quiet: true);
            world.Step(1);
            if (frame >= 40) highestAfterLanding = Math.Max(highestAfterLanding, cloth.Bounds.Max.Z);
            worstStretch = Math.Max(worstStretch, MaxStretch(cloth));
        }
        Check(Lowest(cloth) > 0.02f && highestAfterLanding < 1.2f && worstStretch < 0.1f && cloth.TopSpeed < 1,
            $"a sheet dropped on its edge crumples and settles instead of bouncing (top {highestAfterLanding:0.00} m after landing, " +
            $"worst stretch {worstStretch:P0})");
    }

    private static void CheckTopology()
    {
        var (vertices, indices) = Grid(Segments, doubleSided: true);
        vertices.Add(new Vector3(5, 5, 5)); // no triangle uses it
        ClothTopology topology = ClothTopology.Build(vertices, indices);
        int n = Segments + 1, layer = n * n;
        Check(topology.ParticleCount == layer && topology.Triangles.Length == Segments * Segments * 2 * 3 &&
              Enumerable.Range(0, layer).All(v => topology.VertexParticles[v] == topology.VertexParticles[v + layer]) &&
              topology.VertexParticles[^1] == -1,
            "the two sides of a double-sided mesh weld into one particle per position, and unused vertices into none");
        Check(topology.Edges.Length / 2 == 2 * Segments * n + Segments * Segments && topology.OnBorder.Count(b => b) == 4 * Segments,
            "every distinct triangle edge is a stretch constraint, and the outline is found");
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < topology.Edges.Length; i += 2) edges.Add((topology.Edges[i], topology.Edges[i + 1]));
        bool bendsDistinct = true;
        for (int i = 0; i < topology.Bends.Length; i += 2)
            bendsDistinct &= !edges.Contains((topology.Bends[i], topology.Bends[i + 1])) && topology.Bends[i] != topology.Bends[i + 1];
        Check(topology.Bends.Length > 0 && bendsDistinct, "bend constraints join the far corners of neighbouring triangles, never an edge");

        List<int> top = topology.PinnedParticles(ClothPinMode.TopEdge), corners = topology.PinnedParticles(ClothPinMode.TopCorners);
        Check(top.Count == n && top.All(i => topology.RestPositions[i].Z == 1) &&
              corners.Select(i => topology.RestPositions[i]).OrderBy(p => p.X).SequenceEqual(new[] { new Vector3(-1, 0, 1), new Vector3(1, 0, 1) }) &&
              topology.PinnedParticles(ClothPinMode.Border).Count == 4 * Segments && topology.PinnedParticles(ClothPinMode.None).Count == 0,
            "pins: the top row (model Z), its two ends, the outline, or nothing");
    }

    private static void CheckHanging()
    {
        ClothTopology topology = Topology();
        // Starts flat and swings down from its pinned row (at y = +1, z = 3).
        Matrix world = Flat * Matrix.CreateTranslation(0, 0, 3);
        var cloth = new Cloth(topology, world, ClothPinMode.TopEdge);
        Vector3[] pins = Enumerable.Range(0, cloth.ParticleCount).Where(cloth.IsPinned).Select(i => cloth.Positions[i]).ToArray();
        float slowest = float.MaxValue;
        for (int frame = 0; frame < 300; frame++)
        {
            Check(cloth.Step(Dt, world), "a hanging cloth stays stable", quiet: true);
            if (frame >= 240) slowest = Math.Min(slowest, cloth.TopSpeed);
        }
        float lowest = Lowest(cloth);
        Check(cloth.PinnedCount == Segments + 1 &&
              Enumerable.Range(0, cloth.ParticleCount).Where(cloth.IsPinned).Select(i => cloth.Positions[i]).SequenceEqual(pins),
            "pinned particles hold still");
        Check(lowest < 3 - 1.9f && lowest > 3 - 2.1f && MaxStretch(cloth) < 0.05f,
            $"the cloth swings down to hang its full 2 m (lowest {lowest:0.00} m) without stretching ({MaxStretch(cloth):P1})");
        Check(slowest < 0.6f, $"air drag and damping settle it (slowest {slowest:0.00} m/s in the last second)");

        cloth.Step(Dt, world * Matrix.CreateTranslation(1, 0, 0));
        Check(Enumerable.Range(0, cloth.ParticleCount).Where(cloth.IsPinned).Select(i => cloth.Positions[i])
                  .Zip(pins).All(p => Vector3.Distance(p.First, p.Second + Vector3.UnitX) < 1e-4f),
            "pinned particles follow the gameobject's transform");
        cloth.SetPin(ClothPinMode.None);
        Check(cloth.PinnedCount == 0 && !cloth.IsPinned(0), "pins can change while simulating");
    }

    private static void CheckColliders()
    {
        using var world = new World();
        BasicEntity floor = world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(5, 5, 0.5f), PhysicsBodyType.Static, recorder: false);
        BasicEntity crate = world.Add("Crate", new Vector3(0, 0, 0.5f), new Vector3(0.5f), PhysicsBodyType.Dynamic, 10, recorder: false);
        BasicEntity trigger = world.Add("Zone", new Vector3(1.5f, 0, 0.5f), new Vector3(0.5f), PhysicsBodyType.Static, trigger: true, recorder: false);
        world.Step(1);

        var colliders = new ClothColliderList();
        world.Scene.CollectClothColliders(new Vector3(-3, -3, -1), new Vector3(3, 3, 2), colliders);
        var found = Enumerable.Range(0, colliders.Count).Select(i => colliders[i]).ToList();
        ClothCollider? floorCollider = found.SingleOrDefault(c => c.GameObject == floor), crateCollider = found.SingleOrDefault(c => c.GameObject == crate);
        Check(found.Count == 2 && floorCollider is { Triangles.Count: > 0, Planes.Count: 0 } && crateCollider is { Planes.Count: >= 6, Triangles.Count: 0, Body: not null },
            "a static mesh is collected as triangles, a dynamic hull as planes, and triggers are skipped");

        float Outside(Vector3 p) => crateCollider!.Planes.Max(plane => plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z - plane.W);
        Check(Outside(crate.Position) < -0.45f && Math.Abs(Outside(crate.Position + new Vector3(0, 0, 0.5f))) < 1e-3f &&
              Math.Abs(Outside(crate.Position + new Vector3(0.7f, 0, 0)) - 0.2f) < 1e-3f,
            "hull planes are in world space: negative inside, zero on a face, the gap outside");
        Check(floorCollider!.Triangles.Any(t => t.Contains(new Vector3(1, 1, 0)) && Math.Abs(Vector3.Dot(new Vector3(1, 1, 0) - t.A, t.Normal)) < 1e-4f),
            "mesh triangles are in world space");

        world.Scene.CollectClothColliders(new Vector3(-3, -3, -1), new Vector3(3, 3, 2), colliders, ignore: crate);
        Check(colliders.Count == 1 && colliders[0].GameObject == floor, "the cloth's own gameobject can be left out");
        world.Scene.CollectClothColliders(new Vector3(-3, -3, 1.3f), new Vector3(3, 3, 2), colliders);
        Check(colliders.Count == 0, "only colliders overlapping the query bounds are collected");
        _ = trigger;
    }

    private static void CheckDrape()
    {
        using var world = new World();
        BasicEntity floor = world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(5, 5, 0.5f), PhysicsBodyType.Static, recorder: false);
        BasicEntity table = world.Add("Table", new Vector3(0, 0, 0.45f), new Vector3(0.5f, 0.5f, 0.45f), PhysicsBodyType.Static, recorder: false);
        world.Step(1);
        // A 1.6 m sheet dropped onto a 1 m, 0.9 m high table.
        Matrix pose = Matrix.CreateScale(0.8f, 1, 0.8f) * Flat * Matrix.CreateTranslation(0, 0, 1.5f);
        var cloth = new Cloth(Topology(), pose, ClothPinMode.None, 0.6f) { Bend = 0.15f, Friction = 0.8f, Thickness = 0.025f };
        for (int frame = 0; frame < 300; frame++)
        {
            world.Scene.CollectClothColliders(cloth.Bounds.Min - new Vector3(0.3f), cloth.Bounds.Max + new Vector3(0.3f), cloth.Colliders);
            Check(cloth.Step(Dt, pose), "a falling cloth stays stable", quiet: true);
            world.Step(1);
        }

        var positions = cloth.Positions.ToArray();
        var onTop = positions.Where(p => Math.Abs(p.X) < 0.4f && Math.Abs(p.Y) < 0.4f).ToList();
        int inside = positions.Count(p => Math.Abs(p.X) < 0.49f && Math.Abs(p.Y) < 0.49f && p.Z < 0.89f);
        Check(onTop.Count > 50 && onTop.All(p => Math.Abs(p.Z - 0.925f) < 0.02f),
            $"the sheet lies on the table top, a thickness above it (z {onTop.Min(p => p.Z):0.000}..{onTop.Max(p => p.Z):0.000})");
        Check(inside == 0 && positions.All(p => p.Z > -0.01f), "no particle ends up inside the table or under the floor");
        Check(positions.Min(p => p.Z) < 0.7f && positions.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y))) < 0.78f,
            $"its edges drape down over the sides instead of sticking out (lowest {positions.Min(p => p.Z):0.00} m)");
        Check(cloth.Touching.Contains(table) && cloth.ContactCount > 50 && MaxStretch(cloth, 0.8f) < 0.1f,
            "it reports what it rests on and keeps its size");
        _ = floor;
    }

    private static void CheckMovingBody()
    {
        using var world = new World();
        world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(5, 5, 0.5f), PhysicsBodyType.Static, recorder: false);
        BasicEntity box = world.Add("Box", new Vector3(0, -1.5f, 0.6f), new Vector3(0.3f, 0.3f, 0.6f), PhysicsBodyType.Dynamic, 80, freeze: true, recorder: false);
        world.Step(1);
        // A 2 m curtain hanging at y = 0 from z = 2.2 down to 0.2.
        Matrix pose = Matrix.CreateTranslation(0, 0, 1.2f);
        var cloth = new Cloth(Topology(), pose) { Friction = 0.6f };
        bool touched = false;
        int inside = 0;
        for (int frame = 0; frame < 45; frame++)
        {
            world.Scene.SetVelocity(box, new Vector3(0, 3, 0), Vector3.Zero);
            world.Scene.CollectClothColliders(cloth.Bounds.Min - new Vector3(0.3f), cloth.Bounds.Max + new Vector3(0.3f), cloth.Colliders);
            Check(cloth.Step(Dt, pose), "cloth pushed by a body stays stable", quiet: true);
            touched |= cloth.Touching.Contains(box);
            // Against the pose the cloth was just resolved with (the next physics step moves the box on).
            Vector3 min = box.Position - new Vector3(0.29f, 0.29f, 0.59f), max = box.Position + new Vector3(0.29f, 0.29f, 0.59f);
            foreach (Vector3 p in cloth.Positions)
                if (p.X > min.X && p.Y > min.Y && p.Z > min.Z && p.X < max.X && p.Y < max.Y && p.Z < max.Z) inside++;
            world.Step(1);
        }

        var positions = cloth.Positions.ToArray();
        Check(box.Position.Y > 0.5f && touched && inside == 0,
            $"a body walking through the curtain touches it and never has particles inside it (box at y {box.Position.Y:0.00})");
        Check(positions.Max(p => p.Y) > box.Position.Y - 0.35f && positions.Where(p => Math.Abs(p.X) < 0.2f).Average(p => p.Y) > 0.3f,
            "the body carries the cloth in front of and around it");
        Check(Math.Abs(box.Position.X) < 0.05f && box.GetComponent<PhysicsComponent>() != null, "the cloth doesn't push the body back");
    }

    private static void CheckWindAndImpulse()
    {
        Matrix pose = Matrix.CreateTranslation(0, 0, 3);
        var still = new Cloth(Topology(), pose);
        var windy = new Cloth(Topology(), pose) { Wind = new Vector3(0, 5, 0) };
        for (int frame = 0; frame < 180; frame++)
        {
            still.Step(Dt, pose);
            windy.Step(Dt, pose);
        }
        float Downwind(Cloth cloth) => Enumerable.Range(0, cloth.ParticleCount).Where(i => !cloth.IsPinned(i)).Average(i => cloth.Positions[i].Y);
        Check(Math.Abs(Downwind(still)) < 0.01f && Downwind(windy) > 0.3f,
            $"wind blows the cloth downwind ({Downwind(windy):0.00} m), still air leaves it hanging");

        var poked = new Cloth(Topology(), pose);
        poked.AddImpulse(new Vector3(0, 0.5f, 0), new Vector3(0, 0, 2.5f), 0.4f);
        Vector3[] velocities = poked.Velocities.ToArray();
        int centre = Enumerable.Range(0, poked.ParticleCount).OrderBy(i => Vector3.Distance(poked.Positions[i], new Vector3(0, 0, 2.5f))).First();
        Check(velocities[centre].Y > 0 && Enumerable.Range(0, poked.ParticleCount).Where(poked.IsPinned).All(i => velocities[i] == Vector3.Zero) &&
              Math.Abs(Enumerable.Range(0, poked.ParticleCount).Sum(i => velocities[i].Y) * poked.Mass / (poked.ParticleCount - poked.PinnedCount) - 0.5f) < 1e-3f,
            "an impulse is shared by the free particles around the point, conserving momentum");
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(ClothComponent.TypeId)?.ComponentType == typeof(ClothComponent) &&
              ComponentEditorRegistry.Supports(ClothComponent.TypeId),
            "Cloth is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, new Vector3(0, 0, 3), Matrix.Identity, Vector3.One) { Name = "Banner", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Cloth").AddCommand.Execute(null);
        Publish();
        var cloth = entity.GetComponent<ClothComponent>();
        var editor = owner.Components.OfType<ClothComponentViewModel>().Single();
        Check(cloth is { Pin: ClothPinMode.TopEdge, Mass: 1, Stiffness: 0.95f, Bend: 0.3f, Wind: 1, Collisions: true } && cloth.Cloth == null &&
              !owner.AddableComponents.Single(c => c.DisplayName == "Cloth").AddCommand.CanExecute(null),
            "Cloth defaults to a 1 kg colliding cloth pinned at the top, and is unique per gameobject");

        editor.Pin = ClothPinMode.TopCorners;
        editor.Mass = 2.5;
        editor.Stiffness = 0.8;
        editor.Bend = 0.6;
        editor.Damping = 0.2;
        editor.Friction = 0.3;
        editor.Thickness = 0.05;
        editor.Wind = 0.5;
        editor.Collisions = false;
        Publish();
        Check(cloth is { Pin: ClothPinMode.TopCorners, Mass: 2.5f, Stiffness: 0.8f, Bend: 0.6f, Damping: 0.2f, Friction: 0.3f, Thickness: 0.05f, Wind: 0.5f, Collisions: false },
            "inspector edits reach the engine component");
        editor.Mass = double.NaN;
        editor.Stiffness = 3;
        editor.Thickness = -1;
        Publish();
        Check(cloth is { Mass: 2.5f, Stiffness: 1, Thickness: 0 }, "invalid editor values are ignored or clamped");
        editor.Thickness = 0.03;
        editor.Collisions = true;
        Publish();

        var record = ComponentRegistry.Capture(cloth);
        Check(!record.Data.TryGetProperty(nameof(ClothComponent.Cloth), out _) && !record.Data.TryGetProperty(nameof(ClothComponent.Touching), out _) &&
              record.Data.GetProperty(nameof(ClothComponent.Pin)).GetString() == "TopCorners" &&
              ComponentRegistry.Restore(record) is ClothComponent { Pin: ClothPinMode.TopCorners, Mass: 2.5f, Bend: 0.6f },
            "saved Cloth records round-trip their settings (the pin by name) and exclude runtime state");
        var copied = ((BasicEntity)entity.Clone).GetComponent<ClothComponent>();
        copied.Mass = 9;
        Check(!ReferenceEquals(copied, cloth) && cloth.Mass == 2.5f, "cloning copies Cloth settings independently");

        // ---- Play (no GPU: the topology comes from the fallback, so nothing is drawn) ----
        using var world = new World();
        world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(5, 5, 0.5f), PhysicsBodyType.Static, recorder: false);
        world.Entities.Add(entity);
        var play = new PlayModeController(logic);
        play.Play();
        play.UpdateScripts(Frame);
        Check(cloth.Cloth == null, "without a model or a fallback the gameobject stays solid");
        play.Stop();

        ClothComponent.FallbackTopology = _ => Topology();
        play.Play();
        for (int i = 0; i < 60; i++)
        {
            play.UpdateScripts(Frame);
            world.Step(1);
        }
        Check(cloth.Cloth is { PinnedCount: 2, Pin: ClothPinMode.TopCorners } && cloth.Cloth.Mass == 2.5f && entity.WorldTransform.Cloth == null &&
              Lowest(cloth.Cloth) < 2,
            "Play builds the cloth from the component's settings, hanging from its two top corners");
        cloth.Pin = ClothPinMode.None;
        cloth.Mass = 1;
        cloth.Wind = 0; // a loose sheet would blow along the floor in the active scene's wind
        cloth.OnChanged(entity);
        Check(cloth.Cloth is { PinnedCount: 0, Mass: 1 }, "pin and mass edits apply while playing");
        // A 1 kg, 2 x 2 m sheet floats down at under 2 m/s once it lies flat in the air.
        for (int i = 0; i < 420; i++)
        {
            play.UpdateScripts(Frame);
            world.Step(1);
        }
        Check(cloth.Cloth!.Positions.ToArray().All(p => p.Z > 0) && cloth.Touching.Any(e => e.Name == "Floor"),
            $"unpinned, it falls onto the floor collider and stays on it (lowest {Lowest(cloth.Cloth):0.000} m)");
        Cloth built = cloth.Cloth;
        entity.Scale = new Vector3(0.5f);
        play.UpdateScripts(Frame);
        Check(cloth.Cloth != null && cloth.Cloth != built, "a new scale rebuilds the cloth at its new size");
        cloth.Enabled = false;
        play.UpdateScripts(Frame);
        Check(cloth.Cloth == null, "disabling the component removes the cloth");
        cloth.Enabled = true;
        play.UpdateScripts(Frame);
        play.Stop();
        Check(cloth.Cloth == null && entity.WorldTransform.Cloth == null, "Stop removes the cloth");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
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
        BasicEntity Named(string name) => scene.BasicEntities.Single(e => e.Name == name);
        BasicEntity curtain = Named("Curtain"), sheet = Named("Drape Sheet"), table = Named("Drape Table"), player = Named("Player");
        Check(curtain.ModelDefinition == assets.Cloth && curtain.GetComponent<ClothComponent>() is { Pin: ClothPinMode.TopEdge, Collisions: true } &&
              sheet.ModelDefinition == assets.Cloth && sheet.GetComponent<ClothComponent>() is { Pin: ClothPinMode.None } &&
              new[] { curtain, sheet }.All(e => e.GetComponent<ScriptBehaviourComponent>()?.ScriptId == ClothReporterScript.ScriptId &&
                                               e.GetComponent<PhysicsComponent>() == null) &&
              ScriptRegistry.Find(ClothReporterScript.ScriptId) != null,
            "the collision sample has a pinned curtain and a loose sheet, both reporting to the feed");

        KeyboardState keyboard = Input.keyboardState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool editorMode = GameSettings.e_enableeditor, physicsOn = GameSettings.p_physics;
        using var physicsSystem = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        ClothComponent.FallbackTopology = _ => Topology();
        try
        {
            GameSettings.e_enableeditor = false;
            GameSettings.p_physics = true;
            var logic = new MainSceneLogic();
            typeof(MainSceneLogic).GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(logic, assets);
            typeof(MainSceneLogic).GetField("_scenePhysics", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(logic, new ScenePhysics(physicsSystem) { FallbackGeometry = World.BoxGeometry });
            logic.SceneManager.Assets = assets;
            logic.PlayMode = new PlayModeController(logic);
            GameFlow.SceneLogic = logic;
            logic.SceneManager.SetActiveScene(scene);
            void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(Frame);
                    logic.UpdatePhysics(Dt);
                }
            }

            logic.PlayMode.Play();
            Frames(240);
            Cloth hanging = curtain.GetComponent<ClothComponent>().Cloth, draped = sheet.GetComponent<ClothComponent>().Cloth;
            var curtainPositions = hanging.Positions.ToArray();
            Check(Enumerable.Range(0, hanging.ParticleCount).Where(hanging.IsPinned).All(i => Math.Abs(curtainPositions[i].Z - 2.3f) < 1e-3f) &&
                  Math.Abs(Lowest(hanging) - 0.1f) < 0.1f && curtainPositions.All(p => p.X > 9.65f && p.X < 12.35f && Math.Abs(p.Y - 1.5f) < 0.35f),
                $"the curtain hangs from its rail between the posts, swaying a little in the wind (lowest {Lowest(hanging):0.00} m)");
            var onTable = draped.Positions.ToArray().Where(p => Math.Abs(p.X - table.Position.X) < 0.4f && Math.Abs(p.Y - table.Position.Y) < 0.4f).ToList();
            Check(onTable.Count > 50 && onTable.All(p => Math.Abs(p.Z - 0.925f) < 0.02f) && Lowest(draped) < 0.7f &&
                  CollisionTestFeed.Recent.Contains("Drape Sheet landed on Drape Table"),
                "the sheet falls onto the table, drapes over its sides and the feed reports it");

            // Walk north through the doorway: the saved camera looks along +Y.
            player.Position = new Vector3(11, -0.3f, 1);
            player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
            Input.keyboardState = new KeyboardState(Keys.W);
            bool touched = false;
            for (int i = 0; i < 40; i++)
            {
                Frames(1);
                touched |= curtain.GetComponent<ClothComponent>().Touching.Contains(player);
            }
            Check(player.Position.Y > 2f && touched && CollisionTestFeed.Recent.Contains("Player pushed into the Curtain"),
                $"the player walks through the curtain (now at y {player.Position.Y:0.0}), which reports it");
            // The 2.2 m curtain drapes over the player's head until they are farther than that from the rail.
            Frames(40);
            Input.keyboardState = new KeyboardState();
            Frames(240);
            var hem = hanging.Positions.ToArray().OrderBy(p => p.Z).Take(Segments + 1).ToList();
            Check(player.Position.Y > 3.6f && Math.Abs(hem.Average(p => p.Y) - 1.5f) < 0.3f && hem.Average(p => p.Z) < 0.4f,
                $"once the player is past it the curtain falls back into the doorway (player at y {player.Position.Y:0.0}, " +
                $"hem at y {hem.Average(p => p.Y):0.00}, z {hem.Average(p => p.Z):0.00})");

            logic.PlayMode.Stop();
            Check(curtain.GetComponent<ClothComponent>().Cloth == null && sheet.GetComponent<ClothComponent>().Cloth == null,
                "Stop removes both cloths");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(Dt);
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editorMode;
            GameSettings.p_physics = physicsOn;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  GRAPHICS (--graphics)
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The real Cloth model: welding, the frames written for the GPU, and drawing the simulated vertices.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var previousContent = Globals.content;
        Globals.content = content;
        try
        {
            var definition = new ModelDefinition(content, "GameObjects/Cloth/cloth", graphics);
            using (var mesh = new ClothMeshInstance(graphics, definition.Model))
            {
                ClothTopology topology = mesh.Topology;
                Check(mesh.IsValid && topology.ParticleCount == 625 && topology.Triangles.Length == 1152 * 3 &&
                      topology.VertexParticles.All(p => p >= 0) && topology.PinnedParticles(ClothPinMode.TopEdge).Count == 25,
                    "the Cloth model welds its two sides into a 25 x 25 particle grid whose top row is at model Z = 1");

                // At rest the computed frames must match the content pipeline's (same facing and handedness).
                mesh.Update(topology.RestPositions, Matrix.Identity);
                float worstNormal = 1, worstTangent = 1, worstBinormal = 1;
                foreach (ModelMeshPart part in definition.Model.Meshes.SelectMany(m => m.MeshParts))
                {
                    VertexDeclaration declaration = part.VertexBuffer.VertexDeclaration;
                    var stored = new byte[part.VertexBuffer.VertexCount * declaration.VertexStride];
                    part.VertexBuffer.GetData(stored);
                    byte[] written = mesh.VertexData(part.VertexBuffer);
                    int Offset(VertexElementUsage usage) => declaration.GetVertexElements().First(e => e.VertexElementUsage == usage).Offset;
                    int normal = Offset(VertexElementUsage.Normal), tangent = Offset(VertexElementUsage.Tangent), binormal = Offset(VertexElementUsage.Binormal);
                    for (int v = 0; v < part.VertexBuffer.VertexCount; v++)
                    {
                        int at = v * declaration.VertexStride;
                        worstNormal = Math.Min(worstNormal, Vector3.Dot(Read(stored, at + normal), Read(written, at + normal)));
                        worstTangent = Math.Min(worstTangent, Vector3.Dot(Read(stored, at + tangent), Read(written, at + tangent)));
                        worstBinormal = Math.Min(worstBinormal, Vector3.Dot(Read(stored, at + binormal), Read(written, at + binormal)));
                    }
                }
                Check(worstNormal > 0.999f && worstTangent > 0.99f && worstBinormal > 0.99f,
                    $"at rest the cloth writes the model's own normals and tangent frames (worst dot {Math.Min(worstNormal, Math.Min(worstTangent, worstBinormal)):0.000})");
            }

            var entity = new BasicEntity(definition, null, new Vector3(0, 0, 1), Matrix.Identity, Vector3.One) { Name = "Cloth", IsEnabled = true };
            var cloth = new ClothComponent { Pin = ClothPinMode.None, Wind = 0, Collisions = false };
            entity.AddComponent(cloth);
            var meshes = new MeshMaterialLibrary(graphics);
            entity.RegisterInLibrary(meshes);
            using var target = new RenderTarget2D(graphics, 96, 96, false, SurfaceFormat.Color, DepthFormat.Depth24);
            Color[] Render(Vector3 eye)
            {
                Matrix viewProjection = Matrix.CreateLookAt(eye, new Vector3(0, 0, 1), Vector3.UnitZ) *
                                        Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 20);
                entity.WorldTransform.HasChanged = true;
                meshes.FrustumCulling(new List<BasicEntity> { entity }, new BoundingFrustum(viewProjection), true, eye);
                graphics.SetRenderTarget(target);
                graphics.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                graphics.DepthStencilState = DepthStencilState.Default;
                meshes.Draw(MeshMaterialLibrary.RenderType.IdRender, viewProjection);
                graphics.SetRenderTarget(null);
                var pixels = new Color[96 * 96];
                target.GetData(pixels);
                return pixels;
            }
            // Pixels just below the middle of the top edge: covered at rest, empty once the cloth has dropped.
            static int TopMiddle(Color[] pixels) =>
                Enumerable.Range(0, pixels.Length).Count(i => pixels[i] != Color.Black && Math.Abs(i % 96 - 48) < 6 && i / 96 is > 20 and < 26);
            static int Covered(Color[] pixels) => pixels.Count(p => p != Color.Black);

            Vector3 front = new(0, -4, 1), back = new(0, 4, 1);
            int restFront = Covered(Render(front)), restBack = Covered(Render(back)), restTop = TopMiddle(Render(front));
            Check(restFront > 1000 && Math.Abs(restBack - restFront) < restFront / 20 && restTop > 40,
                $"the cloth is drawn from both sides ({restFront} and {restBack} pixels)");

            // Let go, it falls about 1.2 m in half a second.
            cloth.OnStart(entity);
            for (int i = 0; i < 30; i++) cloth.OnUpdate(entity, Frame);
            Color[] fallen = Render(front);
            Check(entity.WorldTransform.Cloth != null && TopMiddle(fallen) == 0 && Covered(fallen) > restFront / 2,
                $"the renderer draws the simulated vertices: let go, the cloth drops from where it hung ({Covered(fallen)} pixels still in view)");
            cloth.OnStop();
            Check(entity.WorldTransform.Cloth == null && TopMiddle(Render(front)) == restTop, "Stop draws the cloth at rest again");
            entity.Dispose(meshes);
        }
        finally { Globals.content = previousContent; }

        static Vector3 Read(byte[] data, int at) =>
            new(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  FIXTURES
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The Cloth model's grid: a 2 x 2 plane in X/Z (front faces -Y), optionally with a back side.</summary>
    private static (List<Vector3> Vertices, List<int> Indices) Grid(int segments, bool doubleSided)
    {
        int n = segments + 1;
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        for (int side = 0; side < (doubleSided ? 2 : 1); side++)
        {
            int first = vertices.Count;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    vertices.Add(new Vector3(-1 + 2f * i / segments, 0, -1 + 2f * j / segments));
            for (int j = 0; j < segments; j++)
                for (int i = 0; i < segments; i++)
                {
                    int a = first + j * n + i, b = a + 1, d = a + n, c = d + 1;
                    int[] quad = (i + j) % 2 == 0 ? new[] { a, b, c, a, c, d } : new[] { a, b, d, b, c, d };
                    if (side == 1) (quad[1], quad[2], quad[4], quad[5]) = (quad[2], quad[1], quad[5], quad[4]);
                    indices.AddRange(quad);
                }
        }
        return (vertices, indices);
    }

    private static ClothTopology Topology()
    {
        var (vertices, indices) = Grid(Segments, doubleSided: true);
        return ClothTopology.Build(vertices, indices);
    }

    private static float Lowest(Cloth cloth) => cloth.Positions.ToArray().Min(p => p.Z);

    /// <summary>Largest relative stretch of any edge versus the rest pose, for a cloth posed with a uniform X/Z <paramref name="scale"/>.</summary>
    private static float MaxStretch(Cloth cloth, float scale = 1)
    {
        ClothTopology topology = cloth.Topology;
        var positions = cloth.Positions.ToArray();
        float worst = 0;
        for (int i = 0; i < topology.Edges.Length; i += 2)
        {
            int a = topology.Edges[i], b = topology.Edges[i + 1];
            float rest = Vector3.Distance(topology.RestPositions[a], topology.RestPositions[b]) * scale;
            worst = Math.Max(worst, Vector3.Distance(positions[a], positions[b]) / rest - 1);
        }
        return worst;
    }

    private static void Check(bool condition, string description, bool quiet = false)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        if (!quiet) Console.WriteLine("PASS: " + description);
    }
}
