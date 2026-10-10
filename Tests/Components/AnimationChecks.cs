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
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

/// <summary>Skeletal animation: Animator component, AnimationPlayer math, the AnimationTest sample scene and real skinned assets.</summary>
internal static class AnimationChecks
{
    private const string WalkingPath = "GameObjects/Player/Walking";

    public static void Run()
    {
        CheckComponent();
        CheckPlayerMath();
        CheckSampleScene();
    }

    private static void CheckComponent()
    {
        Check(ComponentRegistry.Find(AnimatorComponent.TypeId)?.ComponentType == typeof(AnimatorComponent) &&
              ComponentEditorRegistry.Supports(AnimatorComponent.TypeId),
            "Animator is registered and offered in Add Component");

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
        owner.AddableComponents.Single(c => c.DisplayName == "Animator").AddCommand.Execute(null);
        Publish();
        var animator = entity.GetComponent<AnimatorComponent>();
        var editor = owner.Components.OfType<AnimatorComponentViewModel>().Single();
        Check(animator is { Speed: 1, Loop: true, InPlace: false, ClipSource: "", ClipName: "" } && !animator.IsPlaying &&
              !owner.AddableComponents.Single(c => c.DisplayName == "Animator").AddCommand.CanExecute(null),
            "Animator defaults to the model's own looping clip, idles in Edit mode and is unique per gameobject");

        editor.ClipSource = "  " + WalkingPath + "  ";
        editor.Speed = 0.5;
        editor.InPlace = true;
        editor.Loop = false;
        Publish();
        Check(animator is { ClipSource: WalkingPath, Speed: 0.5f, InPlace: true, Loop: false },
            "inspector edits reach the engine component (source path trimmed)");
        editor.Speed = double.NaN;
        Publish();
        Check(animator.Speed == 0.5f, "a transient invalid speed from the editor is ignored");

        var record = ComponentRegistry.Capture(animator);
        Check(!record.Data.TryGetProperty(nameof(AnimatorComponent.Time), out _) &&
              !record.Data.TryGetProperty(nameof(AnimatorComponent.IsPlaying), out _),
            "saved Animator records exclude runtime playback state");
        var clone = (BasicEntity)entity.Clone;
        var copied = clone.GetComponent<AnimatorComponent>();
        copied.ClipName = "Other";
        Check(!ReferenceEquals(copied, animator) && copied.ClipSource == WalkingPath && animator.ClipName == "",
            "cloning copies Animator settings independently");

        // A model without skinning data logs and stays in the bind pose instead of throwing.
        var play = new PlayModeController(logic);
        play.Play();
        play.UpdateScripts(new GameTime(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1)));
        Check(!animator.IsPlaying && entity.WorldTransform.Skin == null, "Animator on a static model leaves it unskinned");
        play.Stop();

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    /// <summary>
    /// Two-bone chain standing on Z: root (hips) 1 above the origin, child 1 above the root.
    /// The clip bends the child 90° about X and moves the root forward along -Y.
    /// </summary>
    private static void CheckPlayerMath()
    {
        SkinningData Skeleton(float rootHeight, float childLength) => Build(
            new[] { Matrix.CreateTranslation(0, 0, rootHeight), Matrix.CreateTranslation(0, 0, childLength) });
        var target = Skeleton(1, 1);
        var clip = new AnimationClip
        {
            Name = "bend",
            Duration = 1,
            Tracks = new[]
            {
                Track(0, new Vector3(0, 0, 1), new Vector3(0, -2, 1.1f), Quaternion.Identity, Quaternion.Identity),
                Track(1, new Vector3(0, 0, 1), new Vector3(0, 0, 1), Quaternion.Identity,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver2)),
            },
        };
        target.Clips = new[] { clip };

        var player = new AnimationPlayer(target);
        player.Evaluate(0);
        Check(player.SkinTransforms.All(m => Near(m, Matrix.Identity)), "without a clip every skin matrix is identity (bind pose)");

        player.SetClip(clip, target);
        player.Evaluate(1);
        // A vertex 0.5 above the child joint, bound to the child, swings to -Y and follows the root.
        Vector3 tip = Vector3.Transform(new Vector3(0, 0, 2.5f), player.SkinTransforms[1]);
        Check(!player.IsRetargeted && Near(tip, new Vector3(0, -2.5f, 2.1f)), "keys pose and skin the chain");
        player.Evaluate(0.5f);
        Vector3 half = Vector3.Transform(new Vector3(0, 0, 0.5f), player.BoneTransforms[1]) - player.BoneTransforms[1].Translation;
        Check(Near(half, new Vector3(0, -0.5f, 0.5f) * MathF.Sqrt(0.5f)), "rotation keys are slerped between frames");

        player.InPlace = true;
        player.Evaluate(1);
        Check(Near(player.BoneTransforms[0].Translation, new Vector3(0, 0, 1.1f)), "In Place drops horizontal root travel but keeps the bob");

        // Same clip from a taller rig with a longer child bone, driving the target by bone name.
        var source = Skeleton(2, 1.5f);
        var retargeted = new AnimationPlayer(target);
        retargeted.SetClip(clip, source);
        retargeted.Evaluate(1);
        Vector3 childOffset = retargeted.BoneTransforms[1].Translation - retargeted.BoneTransforms[0].Translation;
        Check(retargeted.IsRetargeted && Near(childOffset, new Vector3(0, 0, 1)) &&
              Near(retargeted.BoneTransforms[0].Translation, new Vector3(0, -1, 0.55f)),
            "retargeting keeps the target's bone lengths and scales root motion by hip height");
        Vector3 up = Vector3.TransformNormal(Vector3.UnitZ, retargeted.BoneTransforms[1]);
        Check(Near(up, -Vector3.UnitY), "retargeting carries the source bone's rotation");

        // A source whose child bind is rotated: only the change from its own bind is applied.
        var tilted = Build(new[] { Matrix.CreateTranslation(0, 0, 1),
            Matrix.CreateRotationY(0.4f) * Matrix.CreateTranslation(0, 0, 1) });
        var restClip = new AnimationClip { Name = "rest", Duration = 1, Tracks = new[]
            { Track(1, new Vector3(0, 0, 1), new Vector3(0, 0, 1), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f)) } };
        var rest = new AnimationPlayer(target);
        rest.SetClip(restClip, tilted);
        rest.Evaluate(0.5f);
        Check(rest.SkinTransforms.All(m => Near(m, Matrix.Identity)), "a clip holding the source's own bind pose leaves the target in its bind pose");
    }

    private static void CheckSampleScene()
    {
        var bridge = new EditorBridge();
        string contentRoot = bridge.ContentSourceRoot;
        string path = Path.Combine(contentRoot, "Scenes", "AnimationTest.obsc");
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            IsoSphere = new ModelDefinition(null, bounds),
            PlayerYBot = new ModelDefinition(null, bounds),
            PlayerWalking = new ModelDefinition(null, bounds),
        };
        var scene = SceneSerialization.LoadFromFile(path, assets);
        var entities = scene.BasicEntities;
        BasicEntity Named(string name) => entities.Single(e => e.Name == name);

        // 8 animation gameobjects plus the ragdoll row (RagdollChecks covers those).
        Check(entities.Count == 12 && entities.All(e => e.IsEnabled) && scene.DirectionalLights.Single().CastShadows &&
              scene.MainCamera?.GetComponent<ScriptBehaviourComponent>() is { ScriptId: Engine.Scripting.FreecamScript.ScriptId },
            "the animation sample scene loads its ground, characters and checker cube with a shadowed sun and a Freecam camera");

        var own = Named("X Bot - Walking (own clip)").GetComponent<AnimatorComponent>();
        var retargeted = Named("Y Bot - Walking (retargeted)").GetComponent<AnimatorComponent>();
        var rootMotion = Named("X Bot - Root Motion").GetComponent<AnimatorComponent>();
        var slow = Named("Y Bot - Slow Motion").GetComponent<AnimatorComponent>();
        Check(Named("X Bot - Walking (own clip)").ModelDefinition == assets.PlayerWalking && own is { ClipSource: "", InPlace: true } &&
              Named("Y Bot - Walking (retargeted)").ModelDefinition == assets.PlayerYBot && retargeted is { ClipSource: WalkingPath, InPlace: true } &&
              rootMotion is { InPlace: false, Loop: true } && slow is { Speed: 0.25f, ClipSource: WalkingPath },
            "the sample covers a model's own clip, a clip shared across rigs, root motion and slow motion");
        Check(Named("Y Bot - Bind Pose (no Animator)").GetComponent<AnimatorComponent>() == null,
            "the sample keeps an unanimated character as a bind-pose reference");

        var textured = Named("Y Bot - Textured");
        var surface = textured.GetComponent<MaterialComponent>();
        var cube = Named("UV Checker Cube").GetComponent<MaterialComponent>();
        Check(textured.GetComponent<AnimatorComponent>() != null && surface != null && cube != null &&
              surface.BaseColorTexture == cube.BaseColorTexture && surface.NormalTexture == cube.NormalTexture &&
              new[] { surface.BaseColorTexture, surface.NormalTexture }.All(t => File.Exists(Path.Combine(contentRoot, t))),
            "an animated character and a static cube share the UV checker albedo and normal maps");

        string manifest = File.ReadAllText(Path.Combine(contentRoot, "Content.mgcb"));
        Check(File.Exists(Path.Combine(contentRoot, WalkingPath + ".fbx")) &&
              File.Exists(Path.Combine(contentRoot, "GameObjects/Player/Y Bot.fbx")) &&
              manifest.Contains("/reference:../../ContentPipeline/bin/Obsidian.ContentPipeline.dll") &&
              CountProcessor(manifest, "GameObjects/Player/") == 2 &&
              surface.BaseColorTexture != null && manifest.Contains("#begin " + surface.BaseColorTexture) &&
              manifest.Contains("#begin " + surface.NormalTexture),
            "the Player FBX files build with the SkinnedModelProcessor and the checker maps are in the manifest");

        var list = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentRoot, "System", "SceneList.json")));
        Check(list.RootElement.GetProperty("Scenes").EnumerateArray().Any(s => s.GetString() == "Scenes/AnimationTest.obsc"),
            "the animation sample is in the scene list");
    }

    /// <summary>Real Mixamo assets (needs a graphics device and built content).</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var previousContent = Globals.content;
        Globals.content = content;
        try
        {
            var yBot = new ModelDefinition(content, "GameObjects/Player/Y Bot", graphics);
            var walking = new ModelDefinition(content, WalkingPath, graphics);
            SkinningData ySkeleton = SkinningData.From(yBot.Model), walkSkeleton = SkinningData.From(walking.Model);
            AnimationClip walk = walkSkeleton?.FindClip("");
            Check(ySkeleton?.BoneCount == 65 && walkSkeleton?.BoneCount == 65 && walk != null &&
                  Math.Abs(walk.Duration - 1.0167f) < 0.01f && walk.Tracks.Length == 51,
                "the Player models carry their 65-bone skeleton and the ~1 s walk clip");

            // Y Bot's single-key clip is its rest pose: equal to the bind pose unless pre-rotations are applied twice.
            float worst = ySkeleton.Clips.SelectMany(c => c.Tracks).Max(t =>
            {
                ySkeleton.BindPose[t.BoneIndex].Decompose(out _, out Quaternion bind, out _);
                return 2 * MathF.Acos(Math.Min(1, Math.Abs(Quaternion.Dot(bind, t.Rotations[0]))));
            });
            Check(MathHelper.ToDegrees(worst) < 0.5f, "FBX animation keys and bind pose agree (pre-rotations applied once)");

            var bounds = Bounds(yBot.Model, new AnimationPlayer(ySkeleton), 0);
            Check(Math.Abs(bounds.Min.Z) < 0.01f && bounds.Max.Z is > 1.7f and < 1.9f && bounds.Max.X > 0.9f,
                "Y Bot is built Z-up in metres, standing on the origin in a T-pose");

            var own = new AnimationPlayer(walkSkeleton);
            own.SetClip(walk, walkSkeleton);
            var retargeted = new AnimationPlayer(ySkeleton) { InPlace = true };
            retargeted.SetClip(walk, walkSkeleton);
            foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f })
            {
                var walker = Bounds(walking.Model, own, time);
                var yWalker = Bounds(yBot.Model, retargeted, time);
                Check(Math.Abs(walker.Min.Z) < 0.03f && walker.Max.Z > 1.6f && walker.Max.X - walker.Min.X < 0.8f &&
                      Math.Abs(yWalker.Min.Z) < 0.05f && yWalker.Max.Z > 1.6f && yWalker.Max.Y - yWalker.Min.Y < 1.3f,
                    $"walking at {time:0.##} s keeps both characters upright with their feet on the ground");
            }
            own.Evaluate(0);
            float startY = own.BoneTransforms[0].Translation.Y;
            own.Evaluate(walk.Duration);
            Check(startY - own.BoneTransforms[0].Translation.Y > 1.5f, "the walk clip carries root motion along -Y (the model's forward)");

            CheckAnimatorAndRendering(graphics, yBot, walk);
        }
        finally { Globals.content = previousContent; }
    }

    private static void CheckAnimatorAndRendering(GraphicsDevice graphics, ModelDefinition yBot, AnimationClip walk)
    {
        var entity = new BasicEntity(yBot, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
        var animator = new AnimatorComponent { ClipSource = WalkingPath + ".fbx", InPlace = true };
        entity.AddComponent(animator);
        var meshes = new MeshMaterialLibrary(graphics);
        entity.RegisterInLibrary(meshes);

        using var target = new RenderTarget2D(graphics, 96, 96, false, SurfaceFormat.Color, DepthFormat.Depth24);
        var eye = new Vector3(0, -3.5f, 0.9f);
        Matrix viewProjection = Matrix.CreateLookAt(eye, new Vector3(0, 0, 0.9f), Vector3.UnitZ) *
                                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 20);
        Color[] Render()
        {
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
        // Covered pixels over 0.45 m from the body's centre line: the T-pose arms reach there, the walk pose doesn't.
        static int Outer(Color[] pixels) => Enumerable.Range(0, pixels.Length)
            .Count(i => pixels[i] != Color.Black && Math.Abs(i % 96 - 48) > 15);

        int bindOuter = Outer(Render());
        animator.OnStart(entity);
        animator.OnUpdate(entity, new GameTime(TimeSpan.FromSeconds(0.3), TimeSpan.FromSeconds(0.3)));
        Check(animator.IsPlaying && entity.WorldTransform.Skin != null && animator.Player.IsRetargeted &&
              Math.Abs(animator.Time - 0.3f) < 1e-4f && entity.WorldTransform.HasChanged,
            "Play starts the Animator with the walk clip retargeted from its content path (extension ignored)");
        int posedOuter = Outer(Render());
        Check(bindOuter > 20 && posedOuter < bindOuter / 4, "the renderer draws the posed vertices instead of the bind pose");

        animator.OnUpdate(entity, new GameTime(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(walk.Duration)));
        Check(animator.Time is >= 0 && animator.Time < walk.Duration && Math.Abs(animator.Time - (0.3f + walk.Duration) % walk.Duration) < 1e-3f,
            "looping wraps playback time into the clip");
        animator.Loop = false;
        animator.OnUpdate(entity, new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
        Check(animator.Time == walk.Duration, "a one-shot clip holds its last frame");

        animator.OnStop();
        Check(!animator.IsPlaying && entity.WorldTransform.Skin == null && Outer(Render()) == bindOuter,
            "Stop returns the character to its bind pose");

        animator.ClipName = "No Such Clip";
        animator.OnStart(entity);
        Check(!animator.IsPlaying && entity.WorldTransform.Skin == null, "an unknown clip name leaves the static bind pose drawn");
        animator.ClipName = "";
        animator.OnStart(entity);
        Check(animator.IsPlaying && Outer(Render()) < bindOuter / 4,
            "starting Play uploads the first pose before any update, so the first frame is never stale");
        animator.OnStop();
        entity.Dispose(meshes);
    }

    private static BoundingBox Bounds(Model model, AnimationPlayer player, float time)
    {
        player.Evaluate(time);
        using var skin = new SkinnedMeshInstance(model.Meshes[0].MeshParts[0].VertexBuffer.GraphicsDevice, model);
        skin.Update(player.SkinTransforms);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in model.Meshes.SelectMany(m => m.MeshParts))
        {
            byte[] data = skin.PosedVertexData(part.VertexBuffer);
            int stride = part.VertexBuffer.VertexDeclaration.VertexStride;
            int offset = part.VertexBuffer.VertexDeclaration.GetVertexElements()
                .First(e => e.VertexElementUsage == VertexElementUsage.Position).Offset;
            for (int v = 0; v < part.VertexBuffer.VertexCount; v++)
            {
                int at = v * stride + offset;
                var p = new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        return new BoundingBox(min, max);
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

    private static BoneTrack Track(int bone, Vector3 from, Vector3 to, Quaternion rotationFrom, Quaternion rotationTo) => new()
    {
        BoneIndex = bone,
        Times = new[] { 0f, 1f },
        Translations = new[] { from, to },
        Rotations = new[] { rotationFrom, rotationTo },
        Scales = new[] { Vector3.One, Vector3.One },
    };

    private static int CountProcessor(string manifest, string folder)
    {
        int count = 0;
        string[] blocks = manifest.Split("#begin ");
        foreach (string block in blocks)
            if (block.StartsWith(folder, StringComparison.Ordinal) && block.Contains("/processor:SkinnedModelProcessor")) count++;
        return count;
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    private static bool Near(Matrix a, Matrix b)
    {
        for (int i = 0; i < 16; i++)
            if (Math.Abs(a[i] - b[i]) > 1e-4f) return false;
        return true;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
