using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.Services;
using Engine.Animation;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Particles;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;
using World = CollisionChecks.World;

/// <summary>
/// Breakables: the 3D Voronoi mesh fracture (GPU-free: closed pieces that add up to the mesh, hollow shells, both
/// windings), the Breakable component and its editor, Play (a dropped vase breaks on the floor, scripts break,
/// pieces fade, Restore, following a gameobject, Stop), bone collapsing and the BreakableTest sample scene.
/// </summary>
internal static class BreakableChecks
{
    private const float Dt = 1 / 60f;
    private static readonly GameTime Frame = new(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));

    public static void Run()
    {
        CheckFracture();
        CheckBoneCollapse();
        CheckComponent();
        CheckPlay();
        CheckSampleScene();
    }

    private static void CheckFracture()
    {
        FractureMesh box = Box(new Vector3(1, 0.5f, 0.5f), reverse: false);
        Check(Math.Abs(MeshFracture.SignedVolume(box) - 2) < 1e-4f, "a closed 2 x 1 x 1 box encloses 2 m³");

        FractureMesh half = MeshFracture.Clip(box, Vector3.UnitX, 0, 1)!;
        Check(Math.Abs(MeshFracture.SignedVolume(half) - 1) < 1e-4f && half.Groups.Any(g => g == MeshFracture.InteriorGroup) && Closed(half),
            "clipping a box at a plane keeps half its volume, closed by an interior cap");
        Check(ReferenceEquals(MeshFracture.Clip(box, Vector3.UnitX, 5, 1), box) && MeshFracture.Clip(box, Vector3.UnitX, -5, 1) == null,
            "a plane that misses the mesh keeps it whole; one past it leaves nothing");

        List<Vector3> sites = MeshFracture.SurfaceSites(box, 10, new Random(4), 0.1f);
        List<FracturePiece> pieces = MeshFracture.Fracture(box, sites);
        float sum = pieces.Sum(p => p.Volume);
        Check(sites.Count == 10 && pieces.Count == 10 && Math.Abs(sum - 2) < 2e-3f && pieces.All(p => Closed(p.Mesh)),
            $"a box breaks into {pieces.Count} closed pieces whose volumes add up to it ({sum:0.0000} m³)");
        List<FracturePiece> again = MeshFracture.Fracture(box, MeshFracture.SurfaceSites(box, 10, new Random(4), 0.1f));
        Check(again.Count == pieces.Count && again.Zip(pieces).All(p => p.First.Center == p.Second.Center), "the fracture is deterministic for a seed");

        FractureMesh reversed = Box(new Vector3(1, 0.5f, 0.5f), reverse: true);
        List<FracturePiece> reversedPieces = MeshFracture.Fracture(reversed, sites);
        Check(MeshFracture.SignedVolume(reversed) < 0 && Math.Abs(reversedPieces.Sum(p => p.Volume) - 2) < 2e-3f &&
              reversedPieces.All(p => MeshFracture.SignedVolume(p.Mesh) < 0),
            "a mesh wound the other way (as the importer flips them) breaks the same, its caps wound like it");

        // A hollow pot: a closed lathe with 1 cm walls; caps on its cuts are rings with holes.
        FractureMesh pot = Pot();
        float potVolume = MeshFracture.SignedVolume(pot);
        List<FracturePiece> shards = MeshFracture.Fracture(pot, MeshFracture.SurfaceSites(pot, 12, new Random(7), 0.02f));
        float shardVolume = shards.Sum(p => p.Volume);
        Check(potVolume > 0 && shards.Count >= 10 && Math.Abs(shardVolume / potVolume - 1) < 0.005f,
            $"a hollow pot breaks into {shards.Count} shell shards keeping its clay volume ({shardVolume * 1e6:0} of {potVolume * 1e6:0} cm³)");
        FractureMesh cut = MeshFracture.Clip(pot, Vector3.UnitZ, 0.1f, 1)!;
        FractureMesh rest = MeshFracture.Clip(pot, -Vector3.UnitZ, -0.1f, 1)!;
        Check(Closed(pot) && Closed(cut) && Closed(rest) && Math.Abs(MeshFracture.SignedVolume(cut) + MeshFracture.SignedVolume(rest) - potVolume) < potVolume * 1e-4f,
            "cutting a pot across its walls caps the ring the cut leaves (a cap with a hole)");
    }

    private static void CheckBoneCollapse()
    {
        // root (0,0,0) -> neck (0,0,1) -> head (0,0,1.5)
        var skeleton = new SkinningData
        {
            BoneNames = new[] { "root", "neck", "head" },
            ParentIndices = new[] { -1, 0, 1 },
            BindPose = new[] { Matrix.Identity, Matrix.CreateTranslation(0, 0, 1), Matrix.CreateTranslation(0, 0, 0.5f) },
            InverseBindPose = new[] { Matrix.Identity, Matrix.CreateTranslation(0, 0, -1), Matrix.CreateTranslation(0, 0, -1.5f) },
        };
        var skin = new[] { Matrix.Identity, Matrix.CreateTranslation(1, 0, 0), Matrix.CreateTranslation(1, 0, 0) };
        var body = new BasicEntity(new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)), null, Vector3.Zero, Matrix.Identity, Vector3.One);
        Check(ReferenceEquals(BoneCollapse.Apply(body, skeleton, skin), skin), "with nothing collapsed the skin passes through untouched");
        body.CollapsedBones.Add("neck");
        Matrix[] collapsed = BoneCollapse.Apply(body, skeleton, skin);
        Vector3 joint = new(1, 0, 1); // the neck joint, moved 1 m along X like its skin
        Vector3 top = Vector3.Transform(new Vector3(0, 0, 1.7f), collapsed[2]);
        Check(collapsed[0] == skin[0] && Vector3.Distance(Vector3.Transform(new Vector3(0, 0, 1.2f), collapsed[1]), joint) < 0.01f &&
              Vector3.Distance(top, joint) < 0.01f && skin[2] == Matrix.CreateTranslation(1, 0, 0),
            "collapsing a bone shrinks it and the bones under it to its joint, leaving the rest and the input alone");
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(BreakableComponent.TypeId)?.ComponentType == typeof(BreakableComponent) &&
              ComponentEditorRegistry.Supports(BreakableComponent.TypeId),
            "Breakable is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, new Vector3(0, 0, 1), Matrix.Identity, new Vector3(0.2f)) { Name = "Vase", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Breakable").AddCommand.Execute(null);
        Publish();
        var breakable = entity.GetComponent<BreakableComponent>();
        var editor = owner.Components.OfType<BreakableComponentViewModel>().Single();
        Check(breakable is { Pieces: 12, Effect: BreakEffect.Dust, ImpactSpeed: 4, DebrisLifetime: 10, CollapseBone: true, IsBroken: false } &&
              breakable.EffectColor == BreakableComponent.DustColor && !editor.IsFollowing,
            "Breakable defaults to 12 pieces and a dust puff");

        editor.Pieces = 20;
        editor.ImpactSpeed = 6;
        editor.ImpactMomentum = 5;
        editor.BreakForce = 4;
        editor.DebrisLifetime = 6;
        editor.Density = 1100;
        editor.Effect = BreakEffect.Blood;
        editor.EffectAmount = 2;
        editor.InteriorColor = Avalonia.Media.Color.FromRgb(110, 20, 20);
        editor.FollowObject = "Zombie";
        editor.FollowBone = "mixamorig:Head";
        editor.RagdollOnBreak = false;
        Publish();
        Check(breakable is { Pieces: 20, ImpactSpeed: 6, ImpactMomentum: 5, BreakForce: 4, DebrisLifetime: 6, Density: 1100, Effect: BreakEffect.Blood,
                EffectAmount: 2, FollowObject: "Zombie", FollowBone: "mixamorig:Head", RagdollOnBreak: false } &&
              breakable.InteriorColor == new Color(110, 20, 20) && editor.IsFollowing,
            "inspector edits reach the engine component");
        Check(breakable.EffectColor == BreakableComponent.BloodColor, "switching Dust to Blood swaps the untouched dust colour for blood red");
        editor.Pieces = 500;
        editor.BreakForce = double.NaN;
        Publish();
        Check(breakable is { Pieces: 64, BreakForce: 4 }, "invalid editor values are ignored or clamped");

        var record = ComponentRegistry.Capture(breakable);
        Check(record.Data.GetProperty(nameof(BreakableComponent.Effect)).GetString() == "Blood" &&
              record.Data.GetProperty(nameof(BreakableComponent.InteriorColor)).ValueKind == JsonValueKind.Array &&
              !record.Data.TryGetProperty(nameof(BreakableComponent.Fracture), out _) && !record.Data.TryGetProperty(nameof(BreakableComponent.IsBroken), out _) &&
              ComponentRegistry.Restore(record) is BreakableComponent { Pieces: 64, Effect: BreakEffect.Blood, FollowBone: "mixamorig:Head" } restored &&
              restored.InteriorColor == new Color(110, 20, 20),
            "saved Breakable records round-trip their settings (effect by name, colours as arrays) and exclude runtime state");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckPlay()
    {
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        using var world = new World();
        try
        {
            world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(6, 6, 0.5f), PhysicsBodyType.Static, recorder: false);
            // A 30 cm vase dropped from 2 m, one resting on the floor, and a static pot shot by a script.
            BasicEntity dropped = world.Add("Dropped Vase", new Vector3(-2, 0, 2), new Vector3(0.15f), PhysicsBodyType.Dynamic, mass: 2, recorder: false);
            BasicEntity resting = world.Add("Resting Vase", new Vector3(2, 0, 0.15f), new Vector3(0.15f), PhysicsBodyType.Dynamic, mass: 2, recorder: false);
            BasicEntity pot = world.Add("Pot", new Vector3(0, 2, 1), new Vector3(0.2f), PhysicsBodyType.Static, recorder: false);
            BasicEntity body = world.Add("Body", new Vector3(0, -2, 1), new Vector3(0.3f, 0.3f, 1), PhysicsBodyType.None, recorder: false);
            BasicEntity head = world.Add("Head", new Vector3(0, -2, 2.2f), new Vector3(0.12f), PhysicsBodyType.Static, recorder: false);
            var droppedBreakable = new BreakableComponent { Pieces = 8 };
            var restingBreakable = new BreakableComponent { Pieces = 8 };
            var potBreakable = new BreakableComponent { Pieces = 10, DebrisLifetime = 1.5f, Effect = BreakEffect.None };
            var headBreakable = new BreakableComponent { Pieces = 6, Effect = BreakEffect.Blood, FollowObject = "Body" };
            dropped.AddComponent(droppedBreakable);
            resting.AddComponent(restingBreakable);
            pot.AddComponent(potBreakable);
            head.AddComponent(headBreakable);

            var logic = new MainSceneLogic();
            logic.BasicEntities.AddRange(world.Entities);
            GameFlow.SceneLogic = logic;
            world.Step(1);
            var play = new PlayModeController(logic);
            play.Play();
            Step();
            Check(potBreakable.Fracture != null && !potBreakable.IsBroken && pot.StaticBody != null && SpinUntilReady(potBreakable),
                "Play fractures the gameobject in the background; it stays whole and solid until hit");

            int broken = 0, thrownPieces = 0;
            potBreakable.Broken += (_, pieces) => { broken++; thrownPieces = pieces; };
            Check(potBreakable.Break(new Vector3(0, 1.8f, 1), Vector3.UnitY), "a script breaks the pot");
            float startZ = AverageHeight(potBreakable);
            Step();
            Check(potBreakable.IsBroken && broken == 1 && thrownPieces == 10 && potBreakable.PieceCount == 10 && pot.StaticBody == null && pot.WorldTransform.Hidden,
                $"it is replaced by {potBreakable.PieceCount} piece bodies: hidden, its collider gone, Broken raised once");
            Check(!potBreakable.Break(new Vector3(0, 1.8f, 1), Vector3.UnitY), "a broken gameobject can't break again");
            for (int i = 0; i < 30; i++) Step();
            Check(AverageHeight(potBreakable) < startZ - 0.3f, "the pieces fall");
            for (int i = 0; i < 70; i++) Step();
            Check(potBreakable.PieceCount == 0, "pieces vanish after Debris Lifetime");
            potBreakable.Restore();
            Step();
            Check(!potBreakable.IsBroken && !pot.WorldTransform.Hidden && pot.StaticBody != null, "Restore puts it back whole and solid");

            for (int i = 0; i < 90; i++) Step();
            Check(droppedBreakable.IsBroken && droppedBreakable.PieceCount > 0 && dropped.DynamicBody == null,
                $"a vase dropped from 2 m breaks on the floor into {droppedBreakable.PieceCount} pieces");
            Check(!restingBreakable.IsBroken && resting.DynamicBody != null, "a vase resting on the floor stays whole");
            Check(logic.BreakBursts.BurstCount >= 1, "breaking throws a dust burst");

            body.Position += new Vector3(1, 0, 0.5f);
            Step();
            Check(Vector3.Distance(head.Position, new Vector3(1, -2, 2.7f)) < 1e-3f, "a breakable following a gameobject keeps its offset as it moves");
            Check(headBreakable.Break(), "a script breaks the head from its middle");
            Step();
            body.Position += new Vector3(1, 0, 0);
            Step();
            Check(headBreakable.IsBroken && Math.Abs(head.Position.X - 1) < 1e-3f, "once broken it no longer follows");

            play.Stop();
            world.Step(1);
            Check(potBreakable.Fracture == null && pot.Fracture == null && dropped.Fracture == null && !dropped.WorldTransform.Hidden &&
                  !head.WorldTransform.Hidden && pot.StaticBody != null,
                "Stop removes the pieces and shows the gameobjects again");

            void Step()
            {
                play.UpdateScripts(Frame);
                world.Step(1);
            }
        }
        finally
        {
            GameFlow.SceneLogic = previousLogic;
        }
    }

    private static void CheckSampleScene()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            Capsule = new ModelDefinition(null, bounds),
            Vase = new ModelDefinition(null, new BoundingBox(new Vector3(-0.124f, -0.124f, 0), new Vector3(0.124f, 0.124f, 0.406f))),
            Head = new ModelDefinition(null, new BoundingBox(new Vector3(-0.09f, -0.125f, -0.125f), new Vector3(0.09f, 0.104f, 0.125f))),
            PlayerYBot = new ModelDefinition(null, bounds),
        };
        var editorBridge = new EditorBridge();
        string path = Path.Combine(editorBridge.ContentSourceRoot, "Scenes", "BreakableTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var vases = scene.BasicEntities.Where(e => e.ModelDefinition == assets.Vase).ToList();
        var heads = scene.BasicEntities.Where(e => e.ModelDefinition == assets.Head).ToList();
        Check(vases.Count >= 6 && vases.All(v => v.GetComponent<BreakableComponent>() is { Effect: BreakEffect.Dust } &&
                                                v.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Dynamic }),
            $"the breakable sample has {vases.Count} Dynamic vases that break into dust");
        Check(heads.Count == 3 && heads.All(h =>
                h.GetComponent<BreakableComponent>() is { Effect: BreakEffect.Blood, FollowBone: "mixamorig:Head", CollapseBone: true, RagdollOnBreak: true } b &&
                scene.BasicEntities.Single(e => e.Name == b.FollowObject) is { } zombie && zombie.ModelDefinition == assets.PlayerYBot &&
                zombie.GetComponent<RagdollComponent>() != null && zombie.GetComponent<AnimatorComponent>() != null &&
                h.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Static }),
            "its three zombies each carry a Static breakable head on their head bone that bursts in blood and ragdolls them");

        string content = editorBridge.ContentSourceRoot;
        string manifest = File.ReadAllText(Path.Combine(content, "Content.mgcb"));
        Check(manifest.Contains("#begin GameObjects/Vase/vase.obj") && manifest.Contains("#begin GameObjects/Head/head.obj") &&
              File.Exists(Path.Combine(content, "GameObjects", "Vase", "vase.obj")) && File.Exists(Path.Combine(content, "GameObjects", "Head", "head.obj")) &&
              ProjectReset.KeptPaths.Contains("GameObjects/Vase") && ProjectReset.KeptPaths.Contains("GameObjects/Head"),
            "the Vase and Head models build from Content.mgcb and survive a project reset");
        var list = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "System", "SceneList.json")));
        Check(list.RootElement.GetProperty("Scenes").EnumerateArray().Any(s => s.GetString() == "Scenes/BreakableTest.obsc"),
            "the breakable sample is in the scene list");
    }

    // The background fracture finishes within a few seconds.
    private static bool SpinUntilReady(BreakableComponent breakable)
    {
        for (int i = 0; i < 500 && !breakable.IsReady; i++) Thread.Sleep(10);
        return breakable.IsReady;
    }

    private static float AverageHeight(BreakableComponent breakable)
    {
        var field = typeof(FractureShatter).GetField("_pieces", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var pieces = (System.Collections.IList)field.GetValue(breakable.Fracture)!;
        if (pieces.Count == 0) return breakable.Fracture!.Owner.Position.Z;
        var physics = (PhysicsSystem)typeof(FractureShatter).GetField("_physics", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(breakable.Fracture)!;
        float sum = 0;
        foreach (object piece in pieces)
        {
            var handle = (BepuPhysics.BodyHandle)piece.GetType().GetField("Body")!.GetValue(piece)!;
            physics.GetBodyPose(handle, out Vector3 position, out _);
            sum += position.Z;
        }
        return sum / pieces.Count;
    }

    // Closed: the signed volume of a closed surface doesn't change when it is moved (an open one's does).
    private static bool Closed(FractureMesh mesh)
    {
        double volume = Volume(mesh, 0, 0, 0), moved = Volume(mesh, 3, -5, 7);
        return Math.Abs(volume) > 1e-12 && Math.Abs(moved - volume) < Math.Abs(volume) * 1e-3;
    }

    private static double Volume(FractureMesh mesh, double dx, double dy, double dz)
    {
        double sum = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[t * 3]].Position, b = mesh.Vertices[mesh.Indices[t * 3 + 1]].Position,
                c = mesh.Vertices[mesh.Indices[t * 3 + 2]].Position;
            double ax = a.X + dx, ay = a.Y + dy, az = a.Z + dz, bx = b.X + dx, by = b.Y + dy, bz = b.Z + dz, cx = c.X + dx, cy = c.Y + dy, cz = c.Z + dz;
            sum += ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
        }
        return sum / 6;
    }

    private static FractureMesh Box(Vector3 half, bool reverse)
    {
        var entity = new BasicEntity(new ModelDefinition(null, new BoundingBox(-half, half)), null, Vector3.Zero, Matrix.Identity, Vector3.One);
        FractureMesh mesh = FractureShatter.BoxMesh(entity);
        if (!reverse) return mesh;
        var flipped = new FractureMesh();
        flipped.Vertices.AddRange(mesh.Vertices);
        for (int t = 0; t < mesh.TriangleCount; t++)
            flipped.AddTriangle(mesh.Indices[t * 3], mesh.Indices[t * 3 + 2], mesh.Indices[t * 3 + 1], mesh.Groups[t]);
        return flipped;
    }

    // A closed lathe: outside up from the bottom centre, over the lip, inside back down to the floor.
    private static FractureMesh Pot()
    {
        double[] r = { 0, 0.08, 0.11, 0.12, 0.09, 0.06, 0.07, 0.06, 0.05, 0.08, 0.11, 0.10, 0.07, 0 };
        double[] z = { 0, 0, 0.05, 0.15, 0.28, 0.34, 0.36, 0.36, 0.34, 0.28, 0.15, 0.05, 0.012, 0.012 };
        const int segments = 32;
        var mesh = new FractureMesh();
        for (int i = 0; i < r.Length; i++)
            for (int s = 0; s < segments; s++)
            {
                double a = 2 * Math.PI * s / segments;
                mesh.Vertices.Add(new FractureVertex { Position = new Vector3((float)(r[i] * Math.Cos(a)), (float)(r[i] * Math.Sin(a)), (float)z[i]), Normal = Vector3.UnitZ });
            }
        for (int i = 0; i + 1 < r.Length; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = i * segments + s, b = i * segments + (s + 1) % segments, c = a + segments, d = b + segments;
                mesh.AddTriangle(a, b, d, 0);
                mesh.AddTriangle(a, d, c, 0);
            }
        return mesh;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
