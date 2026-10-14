using System.Collections.ObjectModel;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.Animation;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using World = CollisionChecks.World;

/// <summary>
/// Ragdoll physics: rig building, joint limits, the Ragdoll component on a hand-built skeleton (no GPU),
/// the AnimationTest sample scene, and the real Mixamo characters (--graphics).
/// </summary>
internal static class RagdollChecks
{
    private const float Dt = 1 / 60f;
    private static readonly GameTime Frame = new(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));

    public static void Run()
    {
        try
        {
            CheckComponent();
            CheckJointNames();
            CheckRig();
            CheckKinematicFollow();
            CheckImpactAndRecovery();
            CheckJointLimits();
            CheckQuadrupedRig();
            CheckGenericRig();
            CheckQuadrupedFall();
            CheckSampleScene();
        }
        finally { RagdollComponent.FallbackRig = null; }
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(RagdollComponent.TypeId)?.ComponentType == typeof(RagdollComponent) &&
              ComponentEditorRegistry.Supports(RagdollComponent.TypeId),
            "Ragdoll is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "Ragdoll").AddCommand.Execute(null);
        Publish();
        var ragdoll = entity.GetComponent<RagdollComponent>();
        var editor = owner.Components.OfType<RagdollComponentViewModel>().Single();
        Check(ragdoll is { Mass: 70, ActiveOnStart: false, ImpactThreshold: 0, JointFriction: 0.2f, Skeleton: RagdollSkeletonType.Auto } && !ragdoll.IsActive &&
              !owner.AddableComponents.Single(c => c.DisplayName == "Ragdoll").AddCommand.CanExecute(null),
            "Ragdoll defaults to a 70 kg body that stays animated, and is unique per gameobject");

        editor.Mass = 85;
        editor.ActiveOnStart = true;
        editor.ImpactThreshold = 3;
        editor.JointFriction = 0.5;
        editor.Skeleton = RagdollSkeletonType.Quadruped;
        Publish();
        Check(ragdoll is { Mass: 85, ActiveOnStart: true, ImpactThreshold: 3, JointFriction: 0.5f, Skeleton: RagdollSkeletonType.Quadruped },
            "inspector edits reach the engine component");
        editor.Mass = double.NaN;
        editor.JointFriction = 4;
        Publish();
        Check(ragdoll.Mass == 85 && ragdoll.JointFriction == 1, "invalid editor values are ignored or clamped");

        var record = ComponentRegistry.Capture(ragdoll);
        Check(!record.Data.TryGetProperty(nameof(RagdollComponent.IsActive), out _) &&
              !record.Data.TryGetProperty(nameof(RagdollComponent.Ragdoll), out _) &&
              record.Data.GetProperty(nameof(RagdollComponent.Skeleton)).GetString() == "Quadruped" &&
              ComponentRegistry.Restore(record) is RagdollComponent { Mass: 85, ActiveOnStart: true, ImpactThreshold: 3, Skeleton: RagdollSkeletonType.Quadruped },
            "saved Ragdoll records round-trip their settings and exclude runtime state");
        var clone = (BasicEntity)entity.Clone;
        var copied = clone.GetComponent<RagdollComponent>();
        copied.Mass = 50;
        Check(!ReferenceEquals(copied, ragdoll) && ragdoll.Mass == 85, "cloning copies Ragdoll settings independently");

        // A model without a skeleton logs and stays an ordinary gameobject.
        using var world = new World();
        world.Entities.Add(entity);
        var play = new PlayModeController(logic);
        play.Play();
        play.UpdateScripts(Frame);
        world.Step(1);
        Check(ragdoll.Ragdoll == null && !ragdoll.IsActive && entity.WorldTransform.Skin == null,
            "Ragdoll on a static model builds no bodies");
        play.Stop();

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void CheckJointNames()
    {
        var expected = new (string Name, RagdollJointKind Kind)[]
        {
            ("mixamorig:Hips", RagdollJointKind.Spine), ("mixamorig:Spine1", RagdollJointKind.Spine),
            ("mixamorig:Head", RagdollJointKind.Neck), ("mixamorig:LeftShoulder", RagdollJointKind.Clavicle),
            ("mixamorig:LeftArm", RagdollJointKind.UpperArm), ("mixamorig:LeftForeArm", RagdollJointKind.Elbow),
            ("mixamorig:RightHand", RagdollJointKind.Hand), ("mixamorig:LeftUpLeg", RagdollJointKind.Thigh),
            ("mixamorig:LeftLeg", RagdollJointKind.Knee), ("mixamorig:RightFoot", RagdollJointKind.Foot),
            ("thigh_l", RagdollJointKind.Thigh), ("calf_r", RagdollJointKind.Knee), ("upperarm_l", RagdollJointKind.UpperArm),
            ("lowerarm_r", RagdollJointKind.Elbow), ("clavicle_l", RagdollJointKind.Clavicle), ("Tail", RagdollJointKind.Other),
        };
        Check(expected.All(e => RagdollRig.JointFor(e.Name) == e.Kind),
            "joint kinds follow Mixamo and Unreal bone names (elbows/knees hinge, the rest are cones)");
        Check(RagdollJointLimits.For(RagdollJointKind.Knee) is { IsHinge: true, MinAngle: < 0, MaxAngle: > 90 } &&
              RagdollJointLimits.For(RagdollJointKind.Elbow).IsHinge && !RagdollJointLimits.For(RagdollJointKind.Thigh).IsHinge &&
              RagdollJointLimits.For(RagdollJointKind.UpperArm).MaxAngle > RagdollJointLimits.For(RagdollJointKind.Spine).MaxAngle,
            "knees and elbows bend one way; shoulders swing wider than the spine");
    }

    private static void CheckRig()
    {
        RagdollRig rig = Humanoid.Rig;
        string[] names = rig.Parts.Select(p => p.Name).ToArray();
        int Part(string name) => Array.IndexOf(names, name);
        Check(rig.Type == RagdollSkeletonType.Humanoid, "Auto takes an upright figure with leg bone names for a humanoid");
        Check(names.SequenceEqual(new[] { "Hips", "Spine", "Head", "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg" }) &&
              rig.Parts[0].Parent == -1 && rig.Parts[Part("Spine")].Parent == 0 && rig.Parts[Part("Head")].Parent == Part("Spine") &&
              rig.Parts[Part("LeftLeg")].Parent == Part("LeftUpLeg") && rig.Parts[Part("RightUpLeg")].Parent == 0,
            "every sizeable bone becomes a part joined to the part above it, the hips at the root");
        Check(rig.PartOfBone[Humanoid.Toe] == Part("LeftLeg") && rig.PartOfBone[Humanoid.HeadTop] == Part("Head"),
            "small and vertex-less bones (a toe, an end bone) ride on the part above them");
        Check(Near(rig.Left, Vector3.UnitX) && Near(rig.Forward, -Vector3.UnitY) && Near(rig.Up, Vector3.UnitZ),
            "the character's left comes from the Left/Right bone names; it faces -Y like the Mixamo models");
        // The shin ends in the toe merged into it, so it leans a little forward.
        Check(Near(rig.Parts[Part("LeftUpLeg")].RestDirection, -Vector3.UnitZ) && Near(rig.Parts[Part("Spine")].RestDirection, Vector3.UnitZ) &&
              Near(rig.Parts[Part("LeftLeg")].RestDirection, -Vector3.UnitZ, 0.15f),
            "each part's rest direction points along its limb");
        Check(Math.Abs(rig.Parts.Sum(p => p.MassShare) - 1) < 1e-4f &&
              rig.Parts.Min(p => p.MassShare) >= rig.Parts.Max(p => p.MassShare) / 10 - 1e-5f,
            "mass shares sum to one and no part is under a tenth of the heaviest");

        // Two boxes of volume 1 and 2: the hull volumes split the mass 1:2.
        var skeleton = Humanoid.Skeleton(new[] { ("A", -1, Vector3.Zero), ("B", 0, new Vector3(0, 0, 3)) });
        var positions = new List<Vector3>();
        var bones = new List<int>();
        Humanoid.Box(positions, bones, 0, new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 1));
        Humanoid.Box(positions, bones, 1, new Vector3(-0.5f, -0.5f, 3), new Vector3(0.5f, 0.5f, 5));
        RagdollRig boxes = RagdollRig.Build(skeleton, positions, bones);
        Check(boxes.Parts.Length == 2 && Math.Abs(boxes.Parts[0].MassShare - 1 / 3f) < 0.01f && Math.Abs(boxes.Parts[1].MassShare - 2 / 3f) < 0.01f,
            "part masses follow their convex hull volumes");
    }

    private static void CheckKinematicFollow()
    {
        using var world = NewWorld();
        var (entity, ragdoll) = AddCharacter(world, Vector3.Zero, new RagdollComponent());
        int bodies = world.Physics.Simulation.Bodies.ActiveSet.Count;
        Step(world, 1, ragdoll);
        Ragdoll parts = ragdoll.Ragdoll;
        Check(parts != null && !parts.IsActive && parts.PartCount == 7 &&
              world.Physics.Simulation.Bodies.ActiveSet.Count == bodies + 7 && entity.Ragdoll == parts,
            "Play builds one kinematic body per part");
        Check(Near(parts.PartPosition(0), new Vector3(0, 0, 1), 0.02f) && Near(parts.PartPosition(2), new Vector3(0, 0, 1.675f), 0.02f),
            "the parts start on the bones they follow");

        entity.Position = new Vector3(1, 0, 0);
        Step(world, 2, ragdoll);
        Check(Near(parts.PartPosition(0), new Vector3(1, 0, 1), 0.02f) && !parts.IsActive,
            "kinematic parts follow the gameobject (and its animated pose)");

        Check(world.Scene.Raycast(new Vector3(1, -3, 1.3f), Vector3.UnitY, 10, out RaycastHit hit) && hit.GameObject == entity &&
              Math.Abs(hit.Point.Y + 0.1f) < 0.02f,
            "rays hit the parts and report the gameobject");
        Check(!world.Scene.Raycast(new Vector3(1, -3, 1.3f), Vector3.UnitY, 10, out _, ignore: entity),
            "a ray that ignores the gameobject passes through its ragdoll");

        // Without an impact threshold a falling box just lands on the head.
        BasicEntity box = world.Add("Box", new Vector3(1, 0, 2.6f), new Vector3(0.08f), PhysicsBodyType.Dynamic, mass: 3, recorder: false);
        Step(world, 90, ragdoll);
        Check(!ragdoll.IsActive && box.Position.Z is > 1.82f and < 1.95f && world.Scene.IsTouching(box, entity),
            "a body lands on the animated character instead of falling through");
        world.Entities.Remove(box);

        ragdoll.OnStop();
        Step(world, 1);
        Check(ragdoll.Ragdoll == null && entity.Ragdoll == null && world.Physics.Simulation.Bodies.ActiveSet.Count == bodies,
            "stopping removes every part");
    }

    private static void CheckImpactAndRecovery()
    {
        using var world = NewWorld();
        var (entity, ragdoll) = AddCharacter(world, Vector3.Zero, new RagdollComponent { ImpactThreshold = 2, Mass = 70 });
        // The gameobject's own collider (a character controller) shares its group with the parts.
        entity.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = 70, FreezeRotation = true });
        Step(world, 30, ragdoll);
        Ragdoll parts = ragdoll.Ragdoll;
        Check(entity.DynamicBody != null && !world.Physics.GroupsCollide(entity.DynamicBody.Value, parts.BodyOf(0)) &&
              entity.Position.Length() < 0.05f,
            "the gameobject's own collider ignores its parts (no self-launching)");
        Check(!world.Physics.GroupsCollide(parts.BodyOf(3), parts.BodyOf(5)) && !world.Physics.GroupsCollide(parts.BodyOf(1), parts.BodyOf(0)) &&
              world.Physics.GroupsCollide(parts.BodyOf(4), parts.BodyOf(6)) && world.Physics.GroupsCollide(parts.BodyOf(2), parts.BodyOf(4)),
            "joined and sibling parts don't collide; the shins and the head and a shin do");

        BasicEntity box = world.Add("Box", new Vector3(0.05f, 0.04f, 3.6f), new Vector3(0.1f), PhysicsBodyType.Dynamic, mass: 6, recorder: false);
        bool wentLimp = false;
        for (int i = 0; i < 120 && !wentLimp; i++)
        {
            Step(world, 1, ragdoll);
            wentLimp = ragdoll.IsActive;
        }
        Check(wentLimp && box.Position.Z > 1.6f, "a hard enough hit makes the character go limp");
        Step(world, 1, ragdoll);
        Check(entity.DynamicBody == null, "a limp ragdoll replaces the gameobject's own collider (from the next physics update)");
        world.Entities.Remove(box);

        Step(world, 240, ragdoll);
        float[] heights = Enumerable.Range(0, parts.PartCount).Select(p => parts.PartPosition(p).Z).ToArray();
        Vector3 hips = parts.RootBonePosition;
        float fastest = Enumerable.Range(0, parts.PartCount)
            .Max(p => world.Physics.Simulation.Bodies[parts.BodyOf(p)].Velocity.Linear.Length());
        Check(hips.Z < 0.5f && heights.Min() > -0.02f && fastest < 0.3f,
            $"the ragdoll falls and comes to rest on the ground (hips at {hips.Z:0.00} m)");
        Check(Near(entity.Position, hips - new Vector3(0, 0, 1), 0.01f),
            "the gameobject follows the hips (so its bounds and origin stay with the body)");

        ragdoll.Deactivate();
        Check(!ragdoll.IsActive && Math.Abs(entity.Position.Z) < 0.05f &&
              Math.Abs(entity.Position.X - hips.X) < 0.01f && Math.Abs(entity.Position.Y - hips.Y) < 0.01f,
            "recovering stands the gameobject up on the ground below the hips");
        Step(world, 2, ragdoll);
        Check(Near(parts.PartPosition(0), entity.Position + new Vector3(0, 0, 1), 0.02f) &&
              world.Physics.Simulation.Bodies[parts.BodyOf(0)].LocalInertia.InverseMass == 0 && entity.DynamicBody != null,
            "after recovering the parts follow the pose again and the gameobject's collider is back");

        ragdoll.Activate();
        Check(ragdoll.IsActive, "scripts can make it go limp again");
        ragdoll.AddImpulse(new Vector3(0, 300, 0), parts.PartPosition(2));
        Step(world, 1, ragdoll);
        Check(world.Physics.Simulation.Bodies[parts.BodyOf(2)].Velocity.Linear.Y > 2, "AddImpulse pushes the part nearest the point");
        ragdoll.OnStop();
    }

    /// <summary>In free fall (no relative gravity), twist each knee both ways and the hips sideways.</summary>
    private static void CheckJointLimits()
    {
        using var world = NewWorld();
        var (_, flexed) = AddCharacter(world, new Vector3(-3, 0, 40), new RagdollComponent { ActiveOnStart = true, JointFriction = 0 });
        var (_, hyper) = AddCharacter(world, new Vector3(0, 0, 40), new RagdollComponent { ActiveOnStart = true, JointFriction = 0 });
        var (_, splits) = AddCharacter(world, new Vector3(3, 0, 40), new RagdollComponent { ActiveOnStart = true, JointFriction = 0 });
        Step(world, 1, flexed, hyper, splits);
        Check(flexed.IsActive && hyper.IsActive && splits.IsActive, "Active on Start goes limp on the first update");

        const int thigh = 3, shin = 4;
        Twist(flexed.Ragdoll, thigh, shin, Vector3.UnitX, 2);
        Twist(hyper.Ragdoll, thigh, shin, -Vector3.UnitX, 2);
        Twist(splits.Ragdoll, 0, thigh, Vector3.UnitY, 10);
        float maxFlex = 0, minHyper = 0, maxSwing = 0;
        for (int i = 0; i < 40; i++)
        {
            Step(world, 1, flexed, hyper, splits);
            maxFlex = Math.Max(maxFlex, HingeAngle(world, flexed.Ragdoll, shin));
            minHyper = Math.Min(minHyper, HingeAngle(world, hyper.Ragdoll, shin));
            maxSwing = Math.Max(maxSwing, SwingAngle(world, splits.Ragdoll, thigh));
        }
        Check(maxFlex > 30 && maxFlex < 150, $"a knee bends backward, up to its limit ({maxFlex:0}°)");
        Check(minHyper > -12, $"a knee doesn't bend forward past straight ({minHyper:0}°)");
        Check(maxSwing > 40 && maxSwing < 90, $"a hip swings sideways only within its cone ({maxSwing:0}°)");

        // A spun-up part pair: equal and opposite angular impulses.
        void Twist(Ragdoll ragdoll, int parent, int child, Vector3 axis, float strength)
        {
            world.Physics.ApplyAngularImpulse(ragdoll.BodyOf(child), axis * strength);
            world.Physics.ApplyAngularImpulse(ragdoll.BodyOf(parent), -axis * strength);
        }
    }

    private static void CheckQuadrupedRig()
    {
        RagdollRig rig = Quadruped.Build();
        RagdollRig.Part Part(RagdollRig r, string name) => r.Parts.Single(p => p.Name == name);
        RagdollJointKind Joint(RagdollRig r, string name) => Part(r, name).Joint;
        Check(rig.Type == RagdollSkeletonType.Quadruped && rig.Parts.Length == 19 && rig.Parts[0].Name == "Pelvis",
            "Auto takes a long body on four legs for a quadruped, even with human limb names");
        Check(Near(rig.Forward, -Vector3.UnitY) && Near(rig.Left, Vector3.UnitX), "it faces away from its tail; left comes from the .L/.R names");
        Check(Joint(rig, "upper_arm.L") == RagdollJointKind.ForeShoulder && Joint(rig, "forearm.R") == RagdollJointKind.ForeElbow &&
              Joint(rig, "hand.L") == RagdollJointKind.ForeWrist && Joint(rig, "thigh.R") == RagdollJointKind.HindHip &&
              Joint(rig, "shin.L") == RagdollJointKind.HindKnee && Joint(rig, "foot.R") == RagdollJointKind.HindHock,
            "front legs get shoulder, elbow and wrist joints; hind legs hip, knee and hock");
        Check(Joint(rig, "Tail1") == RagdollJointKind.Tail && Joint(rig, "Tail2") == RagdollJointKind.Tail &&
              Joint(rig, "Neck") == RagdollJointKind.Neck && Joint(rig, "Head") == RagdollJointKind.Neck && Joint(rig, "Chest") == RagdollJointKind.Spine,
            "the tail is a chain of cones, the neck and head keep their names, the chest is spine");
        Check(Near(Part(rig, "forearm.L").Flex, rig.Forward) && Near(Part(rig, "hand.L").Flex, -rig.Forward) &&
              Near(Part(rig, "shin.L").Flex, -rig.Forward) && Near(Part(rig, "foot.L").Flex, rig.Forward) &&
              Part(rig, "Tail1").Flex == Vector3.Zero && Part(rig, "upper_arm.L").Flex == Vector3.Zero,
            "elbows and hocks bend forward, wrists and knees backward; cones have no bend direction");
        Check(new[] { RagdollJointKind.ForeElbow, RagdollJointKind.ForeWrist, RagdollJointKind.HindKnee, RagdollJointKind.HindHock }
                  .All(k => RagdollJointLimits.For(k).IsHinge) &&
              !RagdollJointLimits.For(RagdollJointKind.Tail).IsHinge && !RagdollJointLimits.For(RagdollJointKind.HindHip).IsHinge,
            "a quadruped's elbows, wrists, knees and hocks are hinges; hips, shoulders and tail links are cones");

        RagdollRig human = Quadruped.Build(RagdollSkeletonType.Humanoid);
        Check(human.Type == RagdollSkeletonType.Humanoid && Joint(human, "shin.L") == RagdollJointKind.Knee &&
              Joint(human, "forearm.L") == RagdollJointKind.Elbow && Joint(human, "Tail1") == RagdollJointKind.Other,
            "Skeleton Humanoid forces the human bone-name joints");
        RagdollRig generic = Quadruped.Build(RagdollSkeletonType.Generic);
        Check(generic.Type == RagdollSkeletonType.Generic && generic.Parts.All(p => p.Flex == Vector3.Zero) &&
              Joint(generic, "thigh.L") == RagdollJointKind.Limb && Joint(generic, "shin.L") == RagdollJointKind.Chain &&
              Joint(generic, "Tail2") == RagdollJointKind.Tail,
            "Skeleton Generic has no hinges: parts off a branch are limbs, the rest chain links");
    }

    private static void CheckGenericRig()
    {
        RagdollRig snake = Snake.Build();
        Check(snake.Type == RagdollSkeletonType.Generic && snake.Parts.Length == 6 &&
              snake.Parts.Skip(1).Take(4).All(p => p.Joint == RagdollJointKind.Chain) && snake.Parts[5].Joint == RagdollJointKind.Neck &&
              snake.Parts.All(p => p.Flex == Vector3.Zero),
            "Auto takes a legless chain (a snake) for a generic skeleton of chain links");
        Check(Near(snake.Forward, Vector3.UnitX) && Near(snake.Left, Vector3.UnitY),
            "without left/right bone names the facing points from the root to the head");
    }

    /// <summary>The box quadruped falls over and rests within its hinges; the snake drapes over a bar.</summary>
    private static void CheckQuadrupedFall()
    {
        using var world = NewWorld();
        world.Add("Bar", new Vector3(4, 0, 0.15f), new Vector3(0.1f, 1, 0.15f), PhysicsBodyType.Static, recorder: false);
        RagdollRig quadruped = Quadruped.Build(), snake = Snake.Build();
        RagdollComponent.FallbackRig = (e, type) => e.Name == "Snake" ? snake
            : type is RagdollSkeletonType.Auto or RagdollSkeletonType.Quadruped ? quadruped : Quadruped.Build(type);

        var (_, dog) = AddCharacter(world, new Vector3(0, 0, 0.3f), new RagdollComponent { ActiveOnStart = true, Mass = 35 });
        var (_, forced) = AddCharacter(world, new Vector3(-4, 0, 0.3f), new RagdollComponent { Skeleton = RagdollSkeletonType.Generic });
        var (snakeEntity, slither) = AddCharacter(world, new Vector3(4, 0, 0.4f), new RagdollComponent { ActiveOnStart = true, Mass = 8 });
        snakeEntity.Name = "Snake";
        Step(world, 1, dog, forced, slither);
        Check(dog.Ragdoll is { IsActive: true, PartCount: 19 } && dog.Ragdoll.Rig.Type == RagdollSkeletonType.Quadruped &&
              forced.Ragdoll?.Rig.Type == RagdollSkeletonType.Generic,
            "the component builds the rig for its Skeleton setting");

        Ragdoll parts = dog.Ragdoll;
        int chest = Array.FindIndex(parts.Rig.Parts, p => p.Name == "Chest");
        dog.AddImpulse(new Vector3(40, 0, 0), parts.PartPosition(chest));
        Step(world, 360, dog, slither);
        float lowest = Enumerable.Range(0, parts.PartCount).Min(p => parts.PartPosition(p).Z);
        float fastest = Enumerable.Range(0, parts.PartCount)
            .Max(p => world.Physics.Simulation.Bodies[parts.BodyOf(p)].Velocity.Linear.Length());
        Check(lowest > -0.03f && fastest < 0.3f, $"the quadruped topples and comes to rest on the ground (lowest part at {lowest:0.00} m)");
        float[] hinges = parts.Rig.Parts.Select((p, i) => (p, i)).Where(x => RagdollJointLimits.For(x.p.Joint).IsHinge)
            .Select(x => HingeAngle(world, parts, x.i)).ToArray();
        Check(hinges.Length == 8 && hinges.All(a => a is > -25 and < 135),
            $"its elbows, wrists, knees and hocks stay within their hinges ({string.Join(", ", hinges.Select(a => a.ToString("0")))}°)");

        Ragdoll body = slither.Ragdoll;
        float[] z = Enumerable.Range(0, body.PartCount).Select(p => body.PartPosition(p).Z).ToArray();
        Check(Math.Max(z[2], z[3]) > 0.3f && z[0] < 0.15f && z[5] < 0.15f,
            $"the snake's chain drapes over a bar, both ends on the ground ({string.Join(", ", z.Select(h => h.ToString("0.00")))} m)");
        dog.OnStop();
        forced.OnStop();
        slither.OnStop();
    }

    private static void CheckSampleScene()
    {
        string contentRoot = new EditorBridge().ContentSourceRoot;
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            IsoSphere = new ModelDefinition(null, bounds),
            PlayerYBot = new ModelDefinition(null, bounds),
            PlayerWalking = new ModelDefinition(null, bounds),
            Wolf = new ModelDefinition(null, bounds),
        };
        var scene = SceneSerialization.LoadFromFile(Path.Combine(contentRoot, "Scenes", "AnimationTest.obsc"), assets);
        BasicEntity Named(string name) => scene.BasicEntities.Single(e => e.Name == name);

        var limp = Named("Y Bot - Ragdoll (limp on Play)");
        var hit = Named("X Bot - Ragdoll (hit by ball)");
        var ball = Named("Ragdoll Ball");
        Check(limp.GetComponent<RagdollComponent>() is { ActiveOnStart: true } && limp.GetComponent<AnimatorComponent>() != null &&
              hit.GetComponent<RagdollComponent>() is { ActiveOnStart: false, ImpactThreshold: > 0 } && hit.GetComponent<AnimatorComponent>() != null &&
              ball.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Dynamic } &&
              Math.Abs(ball.Position.X - hit.Position.X) < 0.2f && Math.Abs(ball.Position.Y - hit.Position.Y) < 0.2f && ball.Position.Z > 2.5f,
            "the animation sample has a ragdoll that goes limp on Play and one a falling ball knocks over");
        var limpWolf = Named("Wolf - Ragdoll (limp on Play)");
        var standingWolf = Named("Wolf - Ragdoll (click to knock over)");
        Check(limpWolf.ModelDefinition == assets.Wolf && standingWolf.ModelDefinition == assets.Wolf &&
              limpWolf.GetComponent<RagdollComponent>() is { ActiveOnStart: true, Skeleton: RagdollSkeletonType.Quadruped, Mass: 35 } &&
              standingWolf.GetComponent<RagdollComponent>() is { ActiveOnStart: false, Skeleton: RagdollSkeletonType.Auto, ImpactThreshold: > 0 } &&
              standingWolf.GetComponent<AnimatorComponent>() != null,
            "the sample has two Wolf ragdolls: one limp on Play (Quadruped) and one idling until knocked over (Auto)");
        Check(Named("Ground").GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Static },
            "the sample's ground is a static collider for the ragdolls to land on");
        Check(scene.MainCamera?.GetComponents<ScriptBehaviourComponent>().Any(s => s.ScriptId == Engine.Scripting.RagdollTestScript.ScriptId) == true,
            "the sample camera carries the Ragdoll Test script (shoot balls, toggle the ragdolls)");
    }

    /// <summary>The real Mixamo characters: their rig, a full fall and recovery with the Animator and the skin.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var previousContent = Globals.content;
        Globals.content = content;
        try
        {
            var yBot = new ModelDefinition(content, "GameObjects/Player/Y Bot", graphics);
            RagdollRig rig = RagdollRig.For(yBot.Model);
            string[] names = rig.Parts.Select(p => p.Name.Replace("mixamorig:", "")).ToArray();
            Console.WriteLine("INFO: Y Bot ragdoll parts: " + string.Join(", ", names));
            Check(names.Length is >= 12 and <= 20 && names[0] == "Hips" &&
                  new[] { "Head", "LeftArm", "LeftForeArm", "RightUpLeg", "RightLeg", "LeftFoot", "RightHand" }.All(names.Contains) &&
                  !names.Any(n => n.Contains("Index") || n.Contains("Thumb") || n.Contains("Toe")),
                "Y Bot splits into a torso, head and limb parts; fingers and toes ride on their hand or foot");
            Check(rig.Parts.Count(p => p.Joint == RagdollJointKind.Knee) == 2 && rig.Parts.Count(p => p.Joint == RagdollJointKind.Elbow) == 2 &&
                  Near(rig.Left, Vector3.UnitX, 0.01f) && Near(rig.Forward, -Vector3.UnitY, 0.01f),
                "Y Bot has two knee and two elbow hinges and faces -Y");
            Check(ReferenceEquals(RagdollRig.For(yBot.Model), rig), "a model's rig is built once and cached");

            using var world = NewWorld();
            var entity = new BasicEntity(yBot, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Y Bot", IsEnabled = true };
            var animator = new AnimatorComponent { ClipSource = "GameObjects/Player/Walking", InPlace = true };
            var ragdoll = new RagdollComponent();
            entity.AddComponent(animator);
            entity.AddComponent(ragdoll);
            world.Entities.Add(entity);
            animator.OnStart(entity);
            ragdoll.OnStart(entity);
            SkinnedMeshInstance walkingSkin = entity.WorldTransform.Skin;
            void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    animator.OnUpdate(entity, Frame);
                    ragdoll.OnUpdate(entity, Frame);
                    world.Scene.Update(world.Entities, Dt, simulate: true);
                }
            }

            Frames(20);
            Check(walkingSkin != null && entity.WorldTransform.Skin == walkingSkin && ragdoll.Ragdoll is { IsActive: false, PartCount: > 10 } &&
                  SkinBounds(entity).Max.Z > 1.6f,
                "while animated the Animator owns the skin and the parts follow the walk");

            ragdoll.Activate();
            // A push between the shoulders so it topples backwards (+Y) rather than folding on the spot.
            int chest = Array.FindIndex(names, n => n == "Spine2");
            ragdoll.AddImpulse(new Vector3(0, 60, 0), ragdoll.Ragdoll.PartPosition(chest));
            Frames(1);
            Check(entity.WorldTransform.Skin != walkingSkin && entity.WorldTransform.Skin != null, "going limp swaps in the ragdoll's skin");
            Frames(300);
            BoundingBox lying = SkinBounds(entity);
            float span = Math.Max(lying.Max.X - lying.Min.X, lying.Max.Y - lying.Min.Y);
            Check(lying.Max.Z < 0.75f && lying.Min.Z > -0.06f && span is > 1.2f and < 2.4f,
                $"Y Bot collapses onto the ground in one piece (skin from {lying.Min.Z:0.00} to {lying.Max.Z:0.00} m high, {span:0.00} m long)");
            Ragdoll parts = ragdoll.Ragdoll;
            float[] knees = rig.Parts.Select((p, i) => (p, i)).Where(x => x.p.Joint == RagdollJointKind.Knee)
                .Select(x => HingeAngle(world, parts, x.i)).ToArray();
            float[] elbows = rig.Parts.Select((p, i) => (p, i)).Where(x => x.p.Joint == RagdollJointKind.Elbow)
                .Select(x => HingeAngle(world, parts, x.i)).ToArray();
            Check(knees.Concat(elbows).All(a => a is > -12 and < 150),
                $"knees ({string.Join(", ", knees.Select(a => a.ToString("0")))}°) and elbows ({string.Join(", ", elbows.Select(a => a.ToString("0")))}°) stay within their hinge range");

            ragdoll.Deactivate();
            Frames(2);
            Check(entity.WorldTransform.Skin == walkingSkin && SkinBounds(entity).Max.Z > 1.6f && Math.Abs(entity.Position.Z) < 0.05f,
                "recovering hands the skin back to the Animator, standing on the ground");

            ragdoll.OnStop();
            animator.OnStop();
            Check(entity.WorldTransform.Skin == null && entity.Ragdoll == null, "Stop removes the ragdoll and the skins");

            CheckSampleBall(graphics, content);
            CheckWolf(graphics, content);
        }
        finally
        {
            Globals.content = previousContent;
            RagdollComponent.FallbackRig = null;
        }
    }

    /// <summary>The AnimationTest ball, placed and weighted as in the scene, against the real walking X Bot.</summary>
    private static void CheckSampleBall(GraphicsDevice graphics, ContentManager content)
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            IsoSphere = new ModelDefinition(null, bounds),
            PlayerYBot = new ModelDefinition(null, bounds),
            PlayerWalking = new ModelDefinition(null, bounds),
        };
        var scene = SceneSerialization.LoadFromFile(
            Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "AnimationTest.obsc"), assets);
        BasicEntity saved = scene.BasicEntities.Single(e => e.Name == "X Bot - Ragdoll (hit by ball)");
        BasicEntity savedBall = scene.BasicEntities.Single(e => e.Name == "Ragdoll Ball");

        using var world = NewWorld();
        var walking = new ModelDefinition(content, "GameObjects/Player/Walking", graphics);
        var xBot = new BasicEntity(walking, null, saved.Position, saved.RotationMatrix, saved.Scale) { Name = saved.Name, IsEnabled = true };
        var animator = (AnimatorComponent)ComponentRegistry.Copy(saved.GetComponent<AnimatorComponent>());
        var ragdoll = (RagdollComponent)ComponentRegistry.Copy(saved.GetComponent<RagdollComponent>());
        xBot.AddComponent(animator);
        xBot.AddComponent(ragdoll);
        world.Entities.Add(xBot);
        // The fallback collider is the model's bounds: a 0.4 m box for the 0.2-radius sphere.
        BasicEntity ball = world.Add(savedBall.Name, savedBall.Position, savedBall.Scale, PhysicsBodyType.Dynamic,
            mass: savedBall.GetComponent<PhysicsComponent>().Mass, recorder: false);
        animator.OnStart(xBot);
        ragdoll.OnStart(xBot);
        for (int i = 0; i < 120 && !ragdoll.IsActive; i++)
        {
            animator.OnUpdate(xBot, Frame);
            ragdoll.OnUpdate(xBot, Frame);
            world.Scene.Update(world.Entities, Dt, simulate: true);
        }
        Check(ragdoll.IsActive && ball.Position.Z > 1.4f, "in the sample, the falling ball hits the walking X Bot's head and knocks it over");
        ragdoll.OnStop();
        animator.OnStop();
    }

    /// <summary>The built-in Wolf: a quadruped rig on Auto, the idle playing, then a fall onto its side.</summary>
    private static void CheckWolf(GraphicsDevice graphics, ContentManager content)
    {
        var model = new ModelDefinition(content, "GameObjects/Wolf/Wolf", graphics);
        RagdollRig rig = RagdollRig.For(model.Model);
        Console.WriteLine("INFO: Wolf ragdoll parts: " + string.Join(", ", rig.Parts.Select(p => $"{p.Name} ({p.Joint})")));
        RagdollJointKind Joint(string name) => rig.Parts.Single(p => p.Name == name).Joint;
        Check(rig.Type == RagdollSkeletonType.Quadruped && rig.Parts[0].Name == "Pelvis" &&
              Near(rig.Forward, -Vector3.UnitY, 0.01f) && Near(rig.Left, Vector3.UnitX, 0.01f),
            "Auto makes the Wolf (human limb names on four legs) a quadruped facing -Y");
        Check(Joint("forearm.L") == RagdollJointKind.ForeElbow && Joint("hand.R") == RagdollJointKind.ForeWrist &&
              Joint("shin.R") == RagdollJointKind.HindKnee && Joint("foot.L") == RagdollJointKind.HindHock &&
              rig.Parts.Count(p => p.Joint == RagdollJointKind.Tail) >= 3 && Joint("Head") == RagdollJointKind.Neck,
            "the Wolf's legs get elbow/wrist and knee/hock hinges and its tail is a chain");
        Check(ReferenceEquals(RagdollRig.For(model.Model), rig) && RagdollRig.For(model.Model, RagdollSkeletonType.Generic).Type == RagdollSkeletonType.Generic,
            "rigs are cached per model and skeleton type");

        using var world = NewWorld();
        var entity = new BasicEntity(model, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Wolf", IsEnabled = true };
        var animator = new AnimatorComponent { ClipSource = "GameObjects/Wolf/Wolf" };
        var ragdoll = new RagdollComponent { Mass = 35 };
        entity.AddComponent(animator);
        entity.AddComponent(ragdoll);
        world.Entities.Add(entity);
        animator.OnStart(entity);
        ragdoll.OnStart(entity);
        void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                animator.OnUpdate(entity, Frame);
                ragdoll.OnUpdate(entity, Frame);
                world.Scene.Update(world.Entities, Dt, simulate: true);
            }
        }

        Frames(20);
        BoundingBox standing = SkinBounds(entity);
        Check(animator.IsPlaying && ragdoll.Ragdoll is { IsActive: false } && standing.Max.Z is > 0.9f and < 1.25f &&
              standing.Min.Z is > -0.03f and < 0.05f && standing.Max.Y - standing.Min.Y is > 1.5f and < 2.1f,
            $"the Wolf stands {standing.Max.Z:0.00} m tall and {standing.Max.Y - standing.Min.Y:0.00} m long, playing its idle");

        ragdoll.Activate();
        Ragdoll parts = ragdoll.Ragdoll;
        int chest = Array.FindIndex(rig.Parts, p => p.Name == "Chest");
        ragdoll.AddImpulse(new Vector3(60, 0, 0), parts.PartPosition(chest));
        Frames(300);
        BoundingBox lying = SkinBounds(entity);
        float fastest = Enumerable.Range(0, parts.PartCount)
            .Max(p => world.Physics.Simulation.Bodies[parts.BodyOf(p)].Velocity.Linear.Length());
        Check(lying.Max.Z < 0.6f && lying.Min.Z > -0.06f && fastest < 0.3f,
            $"the Wolf falls onto its side and rests (skin from {lying.Min.Z:0.00} to {lying.Max.Z:0.00} m high)");
        float[] hinges = rig.Parts.Select((p, i) => (p, i)).Where(x => RagdollJointLimits.For(x.p.Joint).IsHinge)
            .Select(x => HingeAngle(world, parts, x.i)).ToArray();
        Check(hinges.Length == 8 && hinges.All(a => a is > -25 and < 135),
            $"its leg hinges stay within range ({string.Join(", ", hinges.Select(a => a.ToString("0")))}°)");

        ragdoll.Deactivate();
        Frames(2);
        Check(SkinBounds(entity).Max.Z > 0.9f && Math.Abs(entity.Position.Z) < 0.05f, "recovering stands the Wolf back on its paws");
        ragdoll.OnStop();
        animator.OnStop();
    }

    private static BoundingBox SkinBounds(BasicEntity entity)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in entity.Model.Meshes.SelectMany(m => m.MeshParts))
        {
            byte[] data = entity.WorldTransform.Skin.PosedVertexData(part.VertexBuffer);
            int stride = part.VertexBuffer.VertexDeclaration.VertexStride;
            int offset = part.VertexBuffer.VertexDeclaration.GetVertexElements()
                .First(e => e.VertexElementUsage == VertexElementUsage.Position).Offset;
            for (int v = 0; v < part.VertexBuffer.VertexCount; v++)
            {
                int at = v * stride + offset;
                var p = new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
                p = Vector3.Transform(p, Matrix.CreateScale(entity.Scale) * entity.RotationMatrix * Matrix.CreateTranslation(entity.Position));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        return new BoundingBox(min, max);
    }

    /// <summary>Signed bend of a hinge part from straight, in degrees: positive towards its flex direction.</summary>
    private static float HingeAngle(World world, Ragdoll ragdoll, int child)
    {
        RagdollRig rig = ragdoll.Rig;
        RagdollRig.Part part = rig.Parts[child];
        Vector3 hinge = Vector3.Normalize(Vector3.Cross(part.RestDirection, part.Flex));
        Vector3 straight = InWorld(world, ragdoll, part.Parent, part.RestDirection);
        Vector3 actual = InWorld(world, ragdoll, child, part.RestDirection);
        Vector3 axis = InWorld(world, ragdoll, part.Parent, hinge);
        return MathHelper.ToDegrees(MathF.Atan2(Vector3.Dot(Vector3.Cross(straight, actual), axis), Vector3.Dot(straight, actual)));
    }

    /// <summary>Angle between where a part's rest direction points and where its parent says it would at rest.</summary>
    private static float SwingAngle(World world, Ragdoll ragdoll, int child)
    {
        RagdollRig.Part part = ragdoll.Rig.Parts[child];
        Vector3 straight = InWorld(world, ragdoll, part.Parent, part.RestDirection);
        Vector3 actual = InWorld(world, ragdoll, child, part.RestDirection);
        return MathHelper.ToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(straight, actual), -1, 1)));
    }

    // A model-space bind direction as carried by a part's body now.
    private static Vector3 InWorld(World world, Ragdoll ragdoll, int part, Vector3 bindDirection)
    {
        ragdoll.Rig.BindModel[ragdoll.Rig.Parts[part].Bone].Decompose(out _, out Quaternion bind, out _);
        world.Physics.GetBodyPose(ragdoll.BodyOf(part), out _, out Quaternion orientation);
        return Vector3.Transform(Vector3.Transform(bindDirection, Quaternion.Inverse(bind)), orientation);
    }

    private static World NewWorld()
    {
        var world = new World();
        world.Add("Ground", new Vector3(0, 0, -0.5f), new Vector3(20, 20, 0.5f), PhysicsBodyType.Static, recorder: false);
        RagdollComponent.FallbackRig = (_, _) => Humanoid.Rig;
        return world;
    }

    // Its own collider (when it gets a Physics component) is a box up to the chest, below the head.
    private static readonly ModelDefinition CharacterBounds =
        new(null, new BoundingBox(new Vector3(-0.25f, -0.25f, 0), new Vector3(0.25f, 0.25f, 1.2f)));

    private static (BasicEntity Entity, RagdollComponent Ragdoll) AddCharacter(World world, Vector3 position, RagdollComponent ragdoll)
    {
        var entity = new BasicEntity(CharacterBounds, null, position, Matrix.Identity, Vector3.One) { Name = "Character", IsEnabled = true };
        world.Entities.Add(entity);
        entity.AddComponent(ragdoll);
        ragdoll.OnStart(entity);
        return (entity, ragdoll);
    }

    private static void Step(World world, int frames, params RagdollComponent[] ragdolls)
    {
        for (int i = 0; i < frames; i++)
        {
            foreach (RagdollComponent ragdoll in ragdolls)
            {
                BasicEntity owner = world.Entities.First(e => e.Components.Contains(ragdoll));
                ragdoll.OnUpdate(owner, Frame);
            }
            world.Scene.Update(world.Entities, Dt, simulate: true);
        }
    }

    /// <summary>A 1.8 m figure without arms, standing on the origin and facing -Y (left is +X), built from boxes of vertices.</summary>
    private static class Humanoid
    {
        public const int Toe = 7, HeadTop = 8;
        public static readonly RagdollRig Rig = Build();

        private static RagdollRig Build()
        {
            var skeleton = Skeleton(new[]
            {
                ("Hips", -1, new Vector3(0, 0, 1)),
                ("Spine", 0, new Vector3(0, 0, 1.15f)),
                ("Head", 1, new Vector3(0, 0, 1.55f)),
                ("LeftUpLeg", 0, new Vector3(0.12f, 0, 0.95f)),
                ("LeftLeg", 3, new Vector3(0.12f, 0, 0.52f)),
                ("RightUpLeg", 0, new Vector3(-0.12f, 0, 0.95f)),
                ("RightLeg", 5, new Vector3(-0.12f, 0, 0.52f)),
                ("LeftToe", 4, new Vector3(0.12f, -0.05f, 0.03f)),
                ("HeadTop_End", 2, new Vector3(0, 0, 1.8f)),
            });
            var positions = new List<Vector3>();
            var bones = new List<int>();
            Box(positions, bones, 0, new Vector3(-0.18f, -0.1f, 0.88f), new Vector3(0.18f, 0.1f, 1.12f));
            Box(positions, bones, 1, new Vector3(-0.17f, -0.1f, 1.15f), new Vector3(0.17f, 0.1f, 1.5f));
            Box(positions, bones, 2, new Vector3(-0.1f, -0.1f, 1.55f), new Vector3(0.1f, 0.1f, 1.8f));
            Box(positions, bones, 3, new Vector3(0.06f, -0.07f, 0.55f), new Vector3(0.18f, 0.07f, 0.9f));
            Box(positions, bones, 4, new Vector3(0.07f, -0.06f, 0f), new Vector3(0.17f, 0.06f, 0.5f));
            Box(positions, bones, 5, new Vector3(-0.18f, -0.07f, 0.55f), new Vector3(-0.06f, 0.07f, 0.9f));
            Box(positions, bones, 6, new Vector3(-0.17f, -0.06f, 0f), new Vector3(-0.07f, 0.06f, 0.5f));
            Box(positions, bones, Toe, new Vector3(0.1f, -0.1f, 0f), new Vector3(0.14f, -0.07f, 0.03f));
            return RagdollRig.Build(skeleton, positions, bones);
        }

        /// <summary>A skeleton of untranslated, unrotated bones at the given model-space positions.</summary>
        public static SkinningData Skeleton((string Name, int Parent, Vector3 Position)[] bones) => new()
        {
            BoneNames = bones.Select(b => b.Name).ToArray(),
            ParentIndices = bones.Select(b => b.Parent).ToArray(),
            BindPose = bones.Select(b => Matrix.CreateTranslation(b.Position - (b.Parent >= 0 ? bones[b.Parent].Position : Vector3.Zero))).ToArray(),
            InverseBindPose = bones.Select(b => Matrix.CreateTranslation(-b.Position)).ToArray(),
        };

        /// <summary>A 4x4x4 grid of vertices filling a box, all weighted to one bone.</summary>
        public static void Box(List<Vector3> positions, List<int> bones, int bone, Vector3 min, Vector3 max)
        {
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    for (int z = 0; z < 4; z++)
                    {
                        positions.Add(min + (max - min) * new Vector3(x, y, z) / 3f);
                        bones.Add(bone);
                    }
        }
    }

    /// <summary>
    /// A 1.8 m long four-legged animal facing -Y (left is +X) with a two-part tail, built from boxes. Its legs
    /// carry Rigify's human names (upper_arm/forearm/hand, thigh/shin/foot), as many animal rigs do.
    /// </summary>
    private static class Quadruped
    {
        public static RagdollRig Build(RagdollSkeletonType type = RagdollSkeletonType.Auto)
        {
            var bones = new List<(string Name, int Parent, Vector3 Position)>
            {
                ("Pelvis", -1, new Vector3(0, 0.35f, 0.7f)),
                ("Spine", 0, new Vector3(0, 0.05f, 0.72f)),
                ("Chest", 1, new Vector3(0, -0.25f, 0.74f)),
                ("Neck", 2, new Vector3(0, -0.45f, 0.8f)),
                ("Head", 3, new Vector3(0, -0.6f, 0.95f)),
                ("Tail1", 0, new Vector3(0, 0.5f, 0.72f)),
                ("Tail2", 5, new Vector3(0, 0.75f, 0.68f)),
            };
            var boxes = new List<(int Bone, Vector3 Min, Vector3 Max)>
            {
                (0, new Vector3(-0.15f, 0.2f, 0.6f), new Vector3(0.15f, 0.5f, 0.85f)),
                (1, new Vector3(-0.15f, -0.1f, 0.6f), new Vector3(0.15f, 0.2f, 0.85f)),
                (2, new Vector3(-0.15f, -0.4f, 0.58f), new Vector3(0.15f, -0.1f, 0.88f)),
                (3, new Vector3(-0.07f, -0.55f, 0.75f), new Vector3(0.07f, -0.4f, 0.92f)),
                (4, new Vector3(-0.08f, -0.8f, 0.88f), new Vector3(0.08f, -0.55f, 1.05f)),
                (5, new Vector3(-0.03f, 0.5f, 0.66f), new Vector3(0.03f, 0.75f, 0.74f)),
                (6, new Vector3(-0.03f, 0.75f, 0.6f), new Vector3(0.03f, 1f, 0.7f)),
            };
            foreach (float side in new[] { 1f, -1f })
            {
                string suffix = side > 0 ? ".L" : ".R";
                Leg(new[] { "thigh", "shin", "foot" }, 0, 0.38f, 0.42f, 0.36f);
                Leg(new[] { "upper_arm", "forearm", "hand" }, 2, -0.3f, -0.3f, -0.3f);

                // Upper, lower and foot segments down to the ground at y = upper/lower/foot.
                void Leg(string[] names, int parent, float upper, float lower, float foot)
                {
                    int first = bones.Count;
                    bones.Add((names[0] + suffix, parent, new Vector3(0.12f * side, upper, 0.62f)));
                    bones.Add((names[1] + suffix, first, new Vector3(0.12f * side, lower, 0.38f)));
                    bones.Add((names[2] + suffix, first + 1, new Vector3(0.12f * side, foot, 0.15f)));
                    boxes.Add((first, new Vector3(0.12f * side - 0.05f, upper - 0.08f, 0.4f), new Vector3(0.12f * side + 0.05f, upper + 0.08f, 0.62f)));
                    boxes.Add((first + 1, new Vector3(0.12f * side - 0.04f, lower - 0.06f, 0.17f), new Vector3(0.12f * side + 0.04f, lower + 0.04f, 0.38f)));
                    boxes.Add((first + 2, new Vector3(0.12f * side - 0.04f, foot - 0.08f, 0f), new Vector3(0.12f * side + 0.04f, foot + 0.04f, 0.15f)));
                }
            }
            var positions = new List<Vector3>();
            var owners = new List<int>();
            foreach (var box in boxes) Humanoid.Box(positions, owners, box.Bone, box.Min, box.Max);
            return RagdollRig.Build(Humanoid.Skeleton(bones.ToArray()), positions, owners, type);
        }
    }

    /// <summary>A 1.8 m snake of six boxes along +X from its tail (the root) to its head, no left/right names.</summary>
    private static class Snake
    {
        public static RagdollRig Build()
        {
            var bones = new (string, int, Vector3)[6];
            var positions = new List<Vector3>();
            var owners = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float x = -0.9f + 0.3f * i;
                bones[i] = (i == 5 ? "Head" : i == 0 ? "Body" : $"Body.00{i}", i - 1, new Vector3(x, 0, 0.1f));
                Humanoid.Box(positions, owners, i, new Vector3(x, -0.06f, 0.04f), new Vector3(x + 0.3f, 0.06f, 0.16f));
            }
            return RagdollRig.Build(Humanoid.Skeleton(bones), positions, owners);
        }
    }

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-3f) => Vector3.Distance(a, b) < tolerance;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
