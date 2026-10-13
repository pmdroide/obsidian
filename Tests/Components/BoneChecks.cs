using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.Services;
using Engine.Animation;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Posed bones: a skinned gameobject's Armature and bones in Anvil's Hierarchy (BoneObject proxies), rotating them
/// with the gizmo/Inspector (BasicEntity.BonePose), the mesh following, saving, cloning and Play/Stop.
/// </summary>
internal static class BoneChecks
{
    public static void Run()
    {
        CheckIds();
        CheckPoseMath();
    }

    private static void CheckIds()
    {
        int armature = BoneObject.IdFor(104, BoneObject.ArmatureIndex), bone = BoneObject.IdFor(104, 64);
        Check(armature < EditorBridge.MainCameraId && bone < EditorBridge.MainCameraId && armature != bone &&
              BoneObject.TryDecode(bone, out int owner, out int index) && owner == 104 && index == 64 &&
              BoneObject.TryDecode(armature, out owner, out index) && owner == 104 && index == BoneObject.ArmatureIndex &&
              !BoneObject.TryDecode(EditorBridge.MainCameraId, out _, out _) && !BoneObject.TryDecode(104, out _, out _),
            "bone IDs are negative (below the Main Camera's) and decode back to the gameobject and bone");
        Check(BoneObject.IdFor(105, BoneObject.ArmatureIndex) != BoneObject.IdFor(104, BoneObject.MaxBones - 1) &&
              BoneObject.IdFor(104, BoneObject.MaxBones) == 0 && BoneObject.IdFor(0, 0) == 0,
            "bone IDs of neighbouring gameobjects never collide; out-of-range bones get no ID");
    }

    private static void CheckPoseMath()
    {
        // Two bones stacked along +Z: joints at (0,0,1) and (0,0,2).
        var skeleton = Build(new[] { Matrix.CreateTranslation(0, 0, 1), Matrix.CreateTranslation(0, 0, 1) });
        var pose = new BonePose();
        var player = new AnimationPlayer(skeleton) { Overrides = pose };
        player.Evaluate(0);
        Check(Near(player.BoneTransforms[1].Translation, new Vector3(0, 0, 2)) && pose.IsEmpty, "an empty pose leaves the rest pose");

        pose.Set(skeleton, 0, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver2));
        player.Evaluate(0);
        Check(Near(player.BoneTransforms[0].Translation, new Vector3(0, 0, 1)) &&
              Near(player.BoneTransforms[1].Translation, new Vector3(0, -1, 1)),
            "a posed bone turns about its own joint and carries its child bone");
        Check(Near(Vector3.Transform(new Vector3(0, 0, 2), player.SkinTransforms[1]), new Vector3(0, -1, 1)),
            "skin matrices move the child's vertices with the posed bone");

        pose.Set(skeleton, BoneObject.ArmatureIndex, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver2));
        pose.Reset(skeleton, 0);
        player.Evaluate(0);
        Check(Near(player.BoneTransforms[0].Translation, new Vector3(0, -1, 0)) &&
              Near(player.BoneTransforms[1].Translation, new Vector3(0, -2, 0)),
            "the Armature turns the whole skeleton about the model origin");

        int version = pose.Version;
        pose.Set(skeleton, 1, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1e-6f));
        Check(pose.Count == 1 && pose.Version > version, "a rotation back at the rest pose is dropped");

        pose.Set(skeleton, 1, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
        var record = pose.ToRecord();
        string json = JsonSerializer.Serialize(record, SceneSerialization.JsonOptions);
        var loaded = new BonePose();
        loaded.Load(JsonSerializer.Deserialize<Dictionary<string, Quaternion>>(json, SceneSerialization.JsonOptions));
        Check(loaded.Count == 2 && Near(loaded.Get(skeleton, 1), pose.Get(skeleton, 1)) &&
              Near(loaded.Get(skeleton, BoneObject.ArmatureIndex), pose.Get(skeleton, BoneObject.ArmatureIndex)) &&
              record.ContainsKey(BonePose.ArmatureKey) && record.ContainsKey("bone1"),
            "poses save by bone name (\"\" = the Armature) and load back");
        pose.Clear();
        Check(pose.IsEmpty && pose.ToRecord() == null, "Reset Pose clears every bone and saves nothing");
    }

    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var previousContent = Globals.content;
        Globals.content = content;
        try { CheckEditor(graphics, content); }
        finally { Globals.content = previousContent; }
    }

    private static void CheckEditor(GraphicsDevice graphics, ContentManager content)
    {
        var yBot = new ModelDefinition(content, "GameObjects/Player/Y Bot", graphics);
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), PlayerYBot = yBot };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(yBot, null, new Vector3(2, 1, 0), Matrix.CreateRotationZ(0.7f), Vector3.One)
        { IsEnabled = true, Name = "Y Bot" };
        var cube = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true, Name = "Cube" };
        logic.BasicEntities.Add(entity);
        logic.BasicEntities.Add(cube);
        var editor = new EditorLogic();
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, editor, assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();

        SkinningData skeleton = SkinningData.From(yBot.Model)!;
        var owner = objects.Single(o => o.EngineId == entity.Id);
        var armature = owner.Children.Single();
        Check(armature is { Type: SceneObjectType.Armature, IsBone: true, Name: "Armature" } && Count(armature) == skeleton.BoneCount + 1 &&
              armature.Children.Single().Name == "mixamorig:Hips" && objects.Single(o => o.EngineId == cube.Id).Children.Count == 0 &&
              objects.Count == 2,
            "a skinned gameobject lists its Armature and bone tree as children; other gameobjects and the flat list don't change");

        int arm = skeleton.FindBone("mixamorig:LeftArm"), foreArm = skeleton.FindBone("mixamorig:LeftForeArm");
        var armVm = Find(armature, "mixamorig:LeftArm")!;
        var foreArmVm = Find(armVm, "mixamorig:LeftForeArm")!;
        Check(armVm.Type == SceneObjectType.Bone && foreArmVm.EngineId == BoneObject.IdFor(entity.Id, foreArm) && !armVm.IsPosed,
            "bones nest under their parent bone with their own editor IDs");

        bridge.RequestSelect(armVm.EngineId);
        Publish();
        var bone = editor.SelectedObject as BoneObject;
        Check(bone is { } b && b.Owner == entity && b.BoneIndex == arm && bridge.SelectedId == armVm.EngineId,
            "selecting a bone row selects that bone in the engine (the gizmo's object)");

        var foreArmBone = entity.BonePose.Proxy(entity, foreArm);
        Vector3 joint = bone!.Position, elbow = foreArmBone.Position;
        Check(Near(joint, Pos(armVm), 1e-3f),
            "a bone's Inspector Position is its joint in the world");
        Matrix target = bone.RotationMatrix * Matrix.CreateRotationZ(0.6f);
        // What EditorLogic.GizmoControl does for the world Z ring.
        bridge.EnqueueMutate(armVm.EngineId!.Value, o => o.RotationMatrix = o.RotationMatrix * Matrix.CreateRotationZ(0.6f));
        Publish();
        Vector3 expectedElbow = joint + Vector3.TransformNormal(elbow - joint, Matrix.CreateRotationZ(0.6f));
        Check(Near(bone.RotationMatrix, target) && Near(bone.Position, joint, 1e-3f) && Near(foreArmBone.Position, expectedElbow, 1e-3f),
            "rotating a bone with the gizmo turns it about its joint and its child bones follow");
        Check(armVm.IsPosed && !foreArmVm.IsPosed && Near(Pos(foreArmVm), expectedElbow, 1e-3f) &&
              ReferenceEquals(owner.Children.Single(), armature),
            "the Hierarchy marks the posed bone and moves its children's positions, keeping the same rows");

        var shared = yBot.Model.Meshes[0].MeshParts[0].VertexBuffer;
        BonePose.UpdateAll(logic.BasicEntities);
        var skin = entity.WorldTransform.Skin;
        byte[] posed = skin?.PosedVertexData(shared) ?? Array.Empty<byte>();
        var bind = new byte[shared.VertexCount * shared.VertexDeclaration.VertexStride];
        shared.GetData(bind);
        Check(skin != null && !posed.AsSpan().SequenceEqual(bind) && cube.WorldTransform.Skin == null,
            "a posed gameobject is skinned in Edit mode, so the mesh follows the bone");

        // The world rotation of the gameobject moves the armature with it.
        Vector3 jointBefore = bone.Position;
        bridge.EnqueueMutate(entity.Id, o => o.RotationMatrix = o.RotationMatrix * Matrix.CreateRotationZ(0.5f));
        Publish();
        Vector3 turned = entity.Position + Vector3.TransformNormal(jointBefore - entity.Position, Matrix.CreateRotationZ(0.5f));
        Check(Near(bone.Position, turned, 1e-3f) && Near(Pos(armVm), turned, 1e-3f),
            "rotating the gameobject carries its armature (the bone rows' positions follow)");

        foreArmVm.RotationX = 40;
        Publish();
        Check(Math.Abs(MathHelper.ToDegrees(2 * MathF.Acos(Math.Min(1, Math.Abs(entity.BonePose.Get(skeleton, foreArm).W)))) - 40) < 0.1f &&
              foreArmVm.IsPosed && Math.Abs(foreArmVm.RotationX - 40) < 0.1,
            "a bone's Inspector Rotation is its turn away from the rest pose");

        var record = SceneSerialization.CaptureEntity(entity, assets);
        string json = JsonSerializer.Serialize(record, SceneSerialization.JsonOptions);
        var restored = SceneSerialization.RestoreEntity(
            JsonSerializer.Deserialize<SceneSerialization.BasicEntityRecord>(json, SceneSerialization.JsonOptions)!, SceneSerialization.CurrentVersion, assets);
        Check(restored != null && restored.BonePose.Count == 2 && Near(restored.BonePose.Get(skeleton, arm), entity.BonePose.Get(skeleton, arm)) &&
              SceneSerialization.CaptureEntity(cube, assets)?.BonePose == null && !json.Contains("\"BonePose\":null"),
            "the pose saves with the scene (only for posed gameobjects)");

        var clone = (BasicEntity)entity.Clone;
        clone.BonePose.Clear();
        Check(entity.BonePose.Count == 2, "a duplicated gameobject gets its own copy of the pose");

        var play = new PlayModeController(logic);
        play.Play();
        bone.ResetPose();
        play.Stop();
        Check(entity.BonePose.Count == 2, "Stop puts back the pose a Play session changed");

        foreArmVm.ResetBoneCommand.Execute(null);
        Publish();
        Check(!foreArmVm.IsPosed && armVm.IsPosed && entity.BonePose.Count == 1, "Reset Bone puts one bone back");
        armVm.ResetPoseCommand.Execute(null);
        Publish();
        BonePose.UpdateAll(logic.BasicEntities);
        Check(entity.BonePose.IsEmpty && !armVm.IsPosed && entity.WorldTransform.Skin == null,
            "Reset Pose returns the mesh to its shared bind-pose buffers");

        logic.BasicEntities.Remove(entity);
        entity.BonePose.Dispose();
        Publish();
        Check(editor.SelectedObject == null && bridge.SelectedId == null && objects.Count == 1,
            "a bone's selection goes when its gameobject is deleted");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.SelectedEngineId = null;
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static Vector3 Pos(SceneObjectViewModel vm) => new((float)vm.PositionX, (float)vm.PositionY, (float)vm.PositionZ);

    private static int Count(SceneObjectViewModel vm) => 1 + vm.Children.Sum(Count);

    private static SceneObjectViewModel? Find(SceneObjectViewModel root, string name)
    {
        if (root.Name == name) return root;
        foreach (var child in root.Children)
            if (Find(child, name) is { } found) return found;
        return null;
    }

    private static SkinningData Build(Matrix[] bind)
    {
        var absolute = new Matrix[bind.Length];
        for (int i = 0; i < bind.Length; i++) absolute[i] = bind[i] * (i > 0 ? absolute[i - 1] : Matrix.Identity);
        return new SkinningData
        {
            BoneNames = Enumerable.Range(0, bind.Length).Select(i => "bone" + i).ToArray(),
            ParentIndices = Enumerable.Range(-1, bind.Length).ToArray(),
            BindPose = bind,
            InverseBindPose = absolute.Select(Matrix.Invert).ToArray(),
        };
    }

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-4f) => Vector3.Distance(a, b) < tolerance;

    private static bool Near(Quaternion a, Quaternion b) => Math.Abs(Math.Abs(Quaternion.Dot(a, b)) - 1) < 1e-5f;

    private static bool Near(Matrix a, Matrix b)
    {
        for (int i = 0; i < 16; i++)
            if (Math.Abs(a[i] - b[i]) > 1e-3f) return false;
        return true;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
