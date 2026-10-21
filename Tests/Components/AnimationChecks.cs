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
        Check(animator is { Speed: 1, Loop: true, InPlace: false, ClipSource: "", ClipName: "", StartAction: "", FadeTime: 0.2f, Actions.Count: 0 } && !animator.IsPlaying &&
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

        editor.AddActionCommand.Execute(null);
        editor.AddActionCommand.Execute(null);
        Publish();
        Check(animator.Actions.Select(a => a.Name).SequenceEqual(new[] { "Action", "Action 2" }) &&
              animator.Actions.All(a => a is { Speed: 1, Loop: true, ClipSource: "", ClipName: "" }),
            "Add Action appends uniquely named actions to the engine component");
        var row = editor.Actions[1];
        row.Name = " Run ";
        row.ClipSource = WalkingPath;
        row.ClipName = "mixamo.com";
        row.Speed = -1.5;
        row.Loop = false;
        editor.StartAction = "Run";
        editor.FadeTime = 0.4;
        Publish();
        Check(animator.Actions[1] is { Name: "Run", ClipSource: WalkingPath, ClipName: "mixamo.com", Speed: -1.5f, Loop: false } &&
              animator is { StartAction: "Run", FadeTime: 0.4f } && editor.Actions.Count == 2 && ReferenceEquals(editor.Actions[1], row),
            "action edits, Start Action and Fade reach the engine (names trimmed) and the rows stay in place");
        row.Speed = double.NaN;
        editor.FadeTime = double.NaN;
        Publish();
        Check(animator.Actions[1].Speed == -1.5f && animator.FadeTime == 0.4f, "transient invalid action speed and fade are ignored");
        Check(animator.FindAction("run") == animator.Actions[1] && animator.HasAction("RUN ") && !animator.HasAction("Jump") &&
              animator.FindAction("") == null,
            "actions are found by name, ignoring case and spaces");
        editor.Actions[0].RemoveCommand.Execute(null);
        Publish();
        Check(animator.Actions.Single().Name == "Run" && editor.Actions.Single() == row, "Remove deletes one action");

        var record = ComponentRegistry.Capture(animator);
        Check(!record.Data.TryGetProperty(nameof(AnimatorComponent.Time), out _) &&
              !record.Data.TryGetProperty(nameof(AnimatorComponent.IsPlaying), out _) &&
              !record.Data.TryGetProperty(nameof(AnimatorComponent.CurrentAction), out _) &&
              record.Data.GetProperty(nameof(AnimatorComponent.Actions)).GetArrayLength() == 1,
            "saved Animator records keep the actions and exclude runtime playback state");
        var restored = (AnimatorComponent)ComponentRegistry.Restore(record);
        Check(restored.Actions.Single() is { Name: "Run", Speed: -1.5f, Loop: false } && restored.StartAction == "Run",
            "actions round-trip through the scene record");
        var clone = (BasicEntity)entity.Clone;
        var copied = clone.GetComponent<AnimatorComponent>();
        copied.ClipName = "Other";
        copied.Actions[0].Name = "Sprint";
        Check(!ReferenceEquals(copied, animator) && copied.ClipSource == WalkingPath && animator.ClipName == "" &&
              animator.Actions[0].Name == "Run",
            "cloning copies Animator settings and actions independently");

        CheckActionSelection();

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

    /// <summary>Which action plays, without a skeleton: Start Action, scripts calling Play before the Animator starts, Stop.</summary>
    private static void CheckActionSelection()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var animator = new AnimatorComponent
        {
            StartAction = "Idle",
            Actions = { new AnimationAction { Name = "Idle" }, new AnimationAction { Name = "Walk" } },
        };
        entity.AddComponent(animator);
        var play = new PlayModeController(logic);
        var frame = new GameTime(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1));

        Check(animator.CurrentAction == "Idle" && animator.IsPlayingAction("idle"), "before Play the current action is the Start Action");
        play.Play();
        play.UpdateScripts(frame);
        Check(animator.CurrentAction == "Idle" && !animator.IsPlaying, "Play starts the Start Action (bind pose without a skeleton)");
        Check(animator.Play("Walk") && animator.CurrentAction == "Walk" && !animator.Play("Jump") && animator.CurrentAction == "Walk",
            "Play switches to a named action and refuses unknown names");
        Check(animator.PlayOnce("Idle") && animator.CurrentAction == "Idle" && animator.Play("") && animator.CurrentAction == "",
            "PlayOnce plays an action and Play(\"\") goes back to the default clip");
        animator.Stop();
        Check(animator.IsStopped && !animator.IsPlayingAction("") && !animator.IsPlaying, "Stop stops playback");
        animator.Play();
        Check(!animator.IsStopped && animator.CurrentAction == "", "Play() resumes the current action");
        animator.Pause();
        Check(animator.IsPaused, "Pause holds playback");
        animator.Resume();
        Check(!animator.IsPaused, "Resume continues");
        play.Stop();
        Check(animator.CurrentAction == "Idle" && !animator.IsStopped, "stopping Play forgets what played; the Start Action is next");

        // A script listed above the Animator starts first: what it asks for survives the Animator's own start.
        animator.Play("Walk");
        play.Play();
        Check(animator.CurrentAction == "Walk", "Play called before the Animator starts is kept through its start");
        play.Stop();
        animator.Stop();
        play.Play();
        Check(animator.IsStopped && animator.CurrentAction == "Idle", "Stop called before the Animator starts keeps it in the bind pose");
        play.Stop();

        animator.Play("Walk");
        animator.StartAction = "Idle";
        animator.OnChanged(entity);
        Check(animator.CurrentAction == "Idle", "an inspector edit in Edit mode drops a stale early Play");
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

        // Crossfade: halfway between the bind pose and the bent pose is a 45 degree bend.
        var bind = new AnimationPlayer(target);
        bind.Evaluate(0);
        var bent = new AnimationPlayer(target);
        bent.SetClip(clip, target);
        bent.Evaluate(1);
        bent.BlendFrom(bind, 0.5f);
        Vector3 blendedUp = Vector3.TransformNormal(Vector3.UnitZ, bent.BoneTransforms[1]);
        Check(Near(blendedUp, Vector3.Normalize(new Vector3(0, -1, 1))) && Near(bent.BoneTransforms[0].Translation, new Vector3(0, -1, 1.05f)),
            "blending two poses slerps rotations and lerps translations bone by bone");
        bent.Evaluate(1);
        bent.BlendFrom(bind, 1f);
        Vector3 fullUp = Vector3.TransformNormal(Vector3.UnitZ, bent.BoneTransforms[1]);
        bent.BlendFrom(bind, 0f);
        Check(Near(fullUp, -Vector3.UnitY) && bent.SkinTransforms.All(m => Near(m, Matrix.Identity)),
            "blend weight 1 keeps this pose and 0 takes the other one's");

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

        // 9 animation gameobjects plus the ragdoll row (RagdollChecks covers those).
        Check(entities.Count == 13 && entities.All(e => e.IsEnabled) && scene.DirectionalLights.Single().CastShadows &&
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
        var actor = Named("Y Bot - Actions (arrow keys)");
        var actions = actor.GetComponent<AnimatorComponent>();
        Check(actor.ModelDefinition == assets.PlayerYBot && actions is { ClipSource: WalkingPath, InPlace: true, StartAction: "Idle" } &&
              actions.Actions.Select(a => a.Name).SequenceEqual(new[] { "Idle", "Walk", "Run", "Walk Back", "Dash" }) &&
              actions.Actions.All(a => a.ClipSource == "") && actions.FindAction("Idle").Speed == 0 &&
              actions.FindAction("Walk Back").Speed < 0 && actions.FindAction("Dash") is { Loop: false } &&
              actor.GetComponent<ScriptBehaviourComponent>() is { ScriptId: Engine.Scripting.AnimatorActionsScript.ScriptId },
            "the sample has an arrow-key character whose script plays Idle/Walk/Run/Walk Back actions and a one-shot Dash");
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
              CountProcessor(manifest, "GameObjects/Player/") == 3 && // Y Bot, Walking, Silly Dancing
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

        // Actions on the real character: Idle is Y Bot's own T-pose clip, the rest share the walk.
        animator.Actions = new List<AnimationAction>
        {
            new() { Name = "Idle", ClipSource = "GameObjects/Player/Y Bot" },
            new() { Name = "Walk" },
            new() { Name = "Back", Speed = -1 },
            new() { Name = "Dash", Speed = 2, Loop = false },
        };
        animator.StartAction = "Idle";
        animator.FadeTime = 0.2f;
        var finished = new List<string>();
        animator.ActionFinished += (_, name) => finished.Add(name);
        animator.OnStart(entity);
        void Step(int frames)
        {
            for (int i = 0; i < frames; i++) animator.OnUpdate(entity, new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
        }
        Step(2);
        Check(animator is { CurrentAction: "Idle", IsPlaying: true, IsFading: false } && entity.WorldTransform.Skin != null &&
              Outer(Render()) > bindOuter * 3 / 4,
            "the Start Action plays on start (Idle: the T-pose clip from another source)");

        Matrix[] idle = (Matrix[])animator.Player.BoneTransforms.Clone();
        var pure = new AnimationPlayer(SkinningData.From(yBot.Model)) { InPlace = true };
        pure.SetClip(walk, SkinningData.From(Globals.content.Load<Model>(WalkingPath)));
        Check(animator.Play("Walk") && animator is { CurrentAction: "Walk", IsFading: true, Time: 0 },
            "Play switches to another action from its start and starts a crossfade");
        Step(1);
        pure.Evaluate(animator.Time);
        Matrix[] blended = animator.Player.BoneTransforms;
        Check(animator.IsFading && Diff(blended, idle) > 0.01f && Diff(blended, idle) < Diff(blended, pure.BoneTransforms),
            "early in the crossfade the pose is between the two clips, still nearer the previous one");
        Step(4);
        pure.Evaluate(animator.Time);
        Check(!animator.IsFading && Diff(animator.Player.BoneTransforms, pure.BoneTransforms) < 1e-3f && Outer(Render()) < bindOuter / 4,
            "after Fade the new clip plays alone");
        float walkTime = animator.Time;
        Check(animator.Play("walk") && animator.Time == walkTime && !animator.IsFading,
            "Play for the action already playing changes nothing (safe to call every frame)");

        Check(animator.PlayOnce("Dash") && animator.CurrentAction == "Dash", "PlayOnce starts a one-shot action");
        Step((int)MathF.Ceiling(walk.Duration / 2 / 0.05f) + 1);
        Check(finished.SequenceEqual(new[] { "Dash" }) && animator is { CurrentAction: "Walk", IsFinished: false, IsFading: true },
            "a one-shot raises ActionFinished at its end and fades back to the action before it");

        Step(5);
        float held = animator.Time;
        animator.Pause();
        Step(3);
        Check(animator is { IsPaused: true, IsPlaying: true } && animator.Time == held, "Pause holds the time and the pose");
        animator.Resume();
        Step(1);
        Check(!animator.IsPaused && Math.Abs(animator.Time - (held + 0.05f) % walk.Duration) < 1e-3f, "Resume continues from there");

        animator.Play("Back", 0);
        Step(1);
        Check(animator is { CurrentAction: "Back", IsFading: false } && Math.Abs(animator.Time - (walk.Duration - 0.05f)) < 1e-3f,
            "a negative-speed action starts at the clip's end and plays backwards; fade 0 switches at once");

        animator.Stop();
        Step(2);
        Check(animator is { IsStopped: true, IsPlaying: false } && entity.WorldTransform.Skin == null && Outer(Render()) == bindOuter,
            "Stop shows the bind pose and stays there");
        animator.Play();
        Step(1);
        Check(animator is { IsStopped: false, IsPlaying: true, CurrentAction: "Back" } && entity.WorldTransform.Skin != null,
            "Play() plays the current action again");
        Check(!animator.Play("Nope") && animator.CurrentAction == "Back", "an unknown action is refused and the current one keeps playing");
        animator.OnStop();
        entity.Dispose(meshes);
    }

    // Largest distance between the same bone's position in two poses.
    private static float Diff(Matrix[] a, Matrix[] b) => a.Zip(b).Max(p => Vector3.Distance(p.First.Translation, p.Second.Translation));

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
