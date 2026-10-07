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
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

/// <summary>
/// Collision and trigger events (ScenePhysics -> Script Behaviour hooks), Freeze Rotation, the
/// Interactable component with script rays, their editor/scene plumbing, and a scripted Play run of
/// Engine/Content/Scenes/CollisionTest.obsc. Bodies use box colliders from each model's bounds
/// (ScenePhysics.FallbackGeometry), so no GPU or model files are needed.
/// </summary>
internal static class CollisionChecks
{
    private const float Dt = 1 / 60f;

    public static void Run()
    {
        ScriptRegistry.Register<RecorderScript>(RecorderScript.Id, "Test Recorder");
        CheckCollisionEvents();
        CheckTriggers();
        CheckFreezeRotation();
        CheckInteraction();
        CheckEditorAndSaving();
        CheckSampleScene();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  ENGINE EVENTS
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckCollisionEvents()
    {
        using var world = new World();
        BasicEntity ground = world.Add("Ground", new Vector3(0, 0, -0.5f), new Vector3(10, 10, 0.5f), PhysicsBodyType.Static);
        BasicEntity box = world.Add("Box", new Vector3(0, 0, 3), new Vector3(0.5f), PhysicsBodyType.Dynamic, 2);
        RecorderScript boxEvents = RecorderScript.Of(box), groundEvents = RecorderScript.Of(ground);

        world.Step(120);
        Collision landing = boxEvents.Collisions("Enter").SingleOrDefault();
        Check(landing.GameObject == ground && groundEvents.Collisions("Enter").SingleOrDefault().GameObject == box,
            "a body landing on the ground raises OnCollisionEnter once on both gameobjects");
        // Dropped from 2.5 m: about 7 m/s at impact.
        Check(landing.ImpactSpeed is > 5.5f and < 7.6f && landing.Normal.Z > 0.95f && Math.Abs(landing.Point.Z) < 0.06f,
            $"the collision reports the impact speed ({landing.ImpactSpeed:0.00} m/s), the contact point and a normal pointing at the receiver");
        Check(groundEvents.Collisions("Enter").Single().Normal.Z < -0.95f, "each side gets the normal pointing from the other gameobject towards itself");
        Check(boxEvents.Collisions("Stay").Count > 30 && boxEvents.Collisions("Stay").All(c => c.GameObject == ground) &&
              boxEvents.Collisions("Exit").Count == 0, "OnCollisionStay repeats every step while resting, without an Exit");
        Check(Math.Abs(box.Position.Z - 0.5f) < 0.05f, "the box rests on the ground");

        world.Step(600);
        Check(!world.Physics.IsAwake(box.DynamicBody!.Value) && boxEvents.Collisions("Exit").Count == 0 &&
              world.Scene.IsTouching(box, ground) && world.Scene.IsTouching(ground, box),
            "a sleeping body keeps its contact (no Exit while BEPU skips the resting pair)");

        box.Position = new Vector3(0, 0, 5);
        world.Step(2);
        Check(boxEvents.Collisions("Exit").SingleOrDefault().GameObject == ground && groundEvents.Collisions("Exit").Count == 1 &&
              !world.Scene.IsTouching(box, ground), "moving the body away raises OnCollisionExit on both");
        world.Step(120);
        Check(boxEvents.Collisions("Enter").Count == 2, "landing again raises a second Enter");

        world.Entities.Remove(box);
        world.Step(1);
        Check(groundEvents.Collisions("Exit").Count == 2 && groundEvents.Collisions("Exit").Last().GameObject == box,
            "removing a gameobject raises OnCollisionExit on whatever it touched");

        BasicEntity second = world.Add("Second Box", new Vector3(0, 0, 0.5f), new Vector3(0.5f), PhysicsBodyType.Dynamic, 2);
        world.Step(10);
        Check(world.Scene.IsTouching(second, ground), "a new body resting on the ground touches it");
        world.Scene.Update(world.Entities, Dt, simulate: false);
        Check(!world.Scene.IsTouching(second, ground) && RecorderScript.Of(second).Collisions("Exit").Count == 0,
            "leaving Play forgets every contact without raising Exit");
    }

    private static void CheckTriggers()
    {
        using var world = new World();
        BasicEntity ground = world.Add("Ground", new Vector3(0, 0, -0.5f), new Vector3(10, 10, 0.5f), PhysicsBodyType.Static);
        BasicEntity zone = world.Add("Zone", new Vector3(0, 0, 3), new Vector3(1), PhysicsBodyType.Static, trigger: true);
        BasicEntity ball = world.Add("Ball", new Vector3(0, 0, 6), new Vector3(0.25f), PhysicsBodyType.Dynamic, 1);
        RecorderScript zoneEvents = RecorderScript.Of(zone), ballEvents = RecorderScript.Of(ball);

        world.Step(1); // builds the bodies
        Check(world.Scene.Raycast(new Vector3(0, 0, 10), -Vector3.UnitZ, 20, out var hit) && hit.GameObject == ball &&
              world.Scene.Raycast(new Vector3(0.5f, 0, 10), -Vector3.UnitZ, 20, out hit) && hit.GameObject == ground &&
              world.Scene.Raycast(new Vector3(0.5f, 0, 10), -Vector3.UnitZ, 20, out hit, includeTriggers: true) &&
              hit.GameObject == zone && Math.Abs(hit.Distance - 6) < 0.01f,
            "raycasts pass through triggers unless includeTriggers is set");

        float fallTime = 0;
        while (ball.Position.Z > 2.3f && fallTime < 3f)
        {
            world.Step(1);
            fallTime += Dt;
        }
        Check(zoneEvents.Triggers("Enter").SingleOrDefault() == ball && ballEvents.Triggers("Enter").SingleOrDefault() == zone,
            "a body entering a trigger raises OnTriggerEnter on the trigger and on the body");
        Check(zoneEvents.Collisions("Enter").Count == 0 && ballEvents.Collisions("Enter").Count == 0,
            "triggers raise no collision events");
        float speed = world.Scene.TryGetVelocity(ball, out var velocity, out _) ? -velocity.Z : 0;
        Check(speed > 9.81f * fallTime * 0.9f, $"a trigger doesn't slow what passes through it ({speed:0.0} m/s after {fallTime:0.00} s)");
        Check(zoneEvents.Triggers("Stay").Count > 3, "OnTriggerStay repeats while overlapping");

        world.Step(30);
        Check(zoneEvents.Triggers("Exit").SingleOrDefault() == ball && ballEvents.Triggers("Exit").Count == 1,
            "leaving the trigger raises OnTriggerExit on both");
        Check(ballEvents.Collisions("Enter").SingleOrDefault().GameObject == ground, "the body still lands on the solid ground below");

        // A static trigger is a solid hull: a body resting fully inside it stays inside, even asleep.
        BasicEntity room = world.Add("Room", new Vector3(5, 5, 1), new Vector3(2, 2, 1), PhysicsBodyType.Static, trigger: true);
        BasicEntity crate = world.Add("Crate", new Vector3(5, 5, 0.3f), new Vector3(0.25f), PhysicsBodyType.Dynamic, 5);
        world.Step(600);
        RecorderScript roomEvents = RecorderScript.Of(room);
        Check(roomEvents.Triggers("Enter").SingleOrDefault() == crate && roomEvents.Triggers("Exit").Count == 0 &&
              !world.Physics.IsAwake(crate.DynamicBody!.Value) && world.Scene.IsTouching(room, crate),
            "a body resting fully inside a static trigger stays inside after it falls asleep");

        crate.Physics.Enabled = false;
        world.Step(1);
        Check(roomEvents.Triggers("Exit").SingleOrDefault() == crate, "disabling a body's Physics component ends its contacts");
    }

    private static void CheckFreezeRotation()
    {
        using var world = new World();
        BasicEntity free = world.Add("Free", new Vector3(0, 0, 50), new Vector3(0.5f), PhysicsBodyType.Dynamic, 1, recorder: false);
        BasicEntity frozen = world.Add("Frozen", new Vector3(10, 0, 50), new Vector3(0.5f), PhysicsBodyType.Dynamic, 1, freeze: true, recorder: false);
        world.Step(1);
        foreach (var e in new[] { free, frozen })
            world.Scene.ApplyImpulse(e, new Vector3(0, 5, 0), e.Position + new Vector3(0.5f, 0, 0));
        world.Step(30);
        world.Scene.TryGetVelocity(free, out _, out var freeSpin);
        world.Scene.TryGetVelocity(frozen, out var frozenVelocity, out var frozenSpin);
        Check(freeSpin.Length() > 1 && frozenSpin == Vector3.Zero && frozenVelocity.Y > 4 &&
              Vector3.Distance(frozen.RotationMatrix.Up, Vector3.UnitY) < 1e-4f,
            "Freeze Rotation keeps a body upright under an off-centre impulse but still moves it");
    }

    private static void CheckInteraction()
    {
        using var world = new World();
        BasicEntity button = world.Add("Button", new Vector3(5, 0, 0), new Vector3(1), PhysicsBodyType.Static);
        var interactable = new InteractableComponent { Prompt = "Press", Range = 3 };
        button.AddComponent(interactable);
        BasicEntity caster = world.Add("Caster", Vector3.Zero, new Vector3(0.25f), PhysicsBodyType.None);
        world.Step(1);
        RecorderScript buttonEvents = RecorderScript.Of(button), player = RecorderScript.Of(caster);
        int raised = 0;
        interactable.Interacted += _ => raised++;
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);

        Check(player.FindInteractable(ray, 10, out _) == null, "an interactable beyond its Range isn't found");
        interactable.Range = 5;
        Check(player.FindInteractable(ray, 10, out var hit) == interactable && hit.GameObject == button && Math.Abs(hit.Distance - 4) < 0.01f,
            "FindInteractable returns the interactable the ray hits within its Range");
        Check(player.Interact(hit) && buttonEvents.Interactions.Count == 1 && raised == 1 &&
              buttonEvents.Interactions[0].Interactor == caster && buttonEvents.Interactions[0].Hit.GameObject == button &&
              Vector3.Distance(buttonEvents.Interactions[0].Hit.Point, new Vector3(4, 0, 0)) < 0.01f,
            "Interact runs OnInteract on the target's scripts with the interactor and hit, then raises Interacted");

        BasicEntity blocker = world.Add("Blocker", new Vector3(2, 0, 0), new Vector3(0.5f), PhysicsBodyType.Static, recorder: false);
        world.Step(1);
        Check(player.FindInteractable(ray, 10, out _) == null, "a collider in front blocks the interactable");
        blocker.Physics.IsTrigger = true;
        world.Step(1);
        Check(player.FindInteractable(ray, 10, out _) == interactable, "triggers don't block interaction rays");

        interactable.Enabled = false;
        Check(player.FindInteractable(ray, 10, out _) == null && !player.Interact(button) && buttonEvents.Interactions.Count == 1,
            "a disabled Interactable can't be found or used");
        interactable.Enabled = true;
        Check(player.Interact(button) && buttonEvents.Interactions.Count == 2 && buttonEvents.Interactions[1].Hit.GameObject == button,
            "Interact(gameobject) uses it without a ray");
        Check(!player.Interact(blocker), "gameobjects without an Interactable can't be used");
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  EDITOR + SCENE FILES
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckEditorAndSaving()
    {
        Check(ComponentRegistry.Find(InteractableComponent.TypeId) is { DisplayName: "Interactable", AllowMultiple: false } &&
              ComponentEditorRegistry.Supports(InteractableComponent.TypeId),
            "Interactable is registered and offered in Add Component");

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Lever", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var vm = objects.Single(o => o.EngineId == entity.Id);

        bridge.EnqueueAddComponent(entity.Id, PhysicsComponent.TypeId);
        bridge.EnqueueAddComponent(entity.Id, InteractableComponent.TypeId);
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var physicsVm = vm.Components.OfType<PhysicsComponentViewModel>().Single();
        var interactableVm = vm.Components.OfType<InteractableComponentViewModel>().Single();
        Check(!physicsVm.IsTrigger && !physicsVm.FreezeRotation && interactableVm is { Prompt: "Interact", Range: InteractableComponent.DefaultRange },
            "new Physics and Interactable components show their defaults in the inspector");

        physicsVm.IsTrigger = true;
        physicsVm.FreezeRotation = true;
        interactableVm.Prompt = "  Pull the lever ";
        interactableVm.Range = 4.5;
        Drain(bridge);
        var physics = entity.GetComponent<PhysicsComponent>();
        var interactable = entity.GetComponent<InteractableComponent>();
        Check(physics.IsTrigger && physics.FreezeRotation && interactable is { Prompt: "Pull the lever", Range: 4.5f },
            "Is Trigger, Freeze Rotation, Prompt and Range edits reach the engine");
        interactableVm.Range = double.NaN;
        Drain(bridge);
        Check(interactable.Range == 4.5f, "a transient invalid range from the editor is ignored");

        interactable.Prompt = "Changed by a script";
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        Check(interactableVm.Prompt == "Changed by a script", "engine-side prompt changes return to the inspector");

        var clone = (BasicEntity)entity.Clone;
        clone.GetComponent<InteractableComponent>().Prompt = "Copy";
        Check(interactable.Prompt == "Changed by a script" && clone.GetComponent<PhysicsComponent>() is { IsTrigger: true, FreezeRotation: true },
            "copies get independent Interactable and Physics settings");

        string path = Path.Combine(Path.GetTempPath(), $"anvil-collision-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, path, assets);
            var loaded = SceneSerialization.LoadFromFile(path, assets).BasicEntities.Single();
            Check(loaded.GetComponent<PhysicsComponent>() is { IsTrigger: true, FreezeRotation: true } &&
                  loaded.GetComponent<InteractableComponent>() is { Prompt: "Changed by a script", Range: 4.5f },
                "scene save/load keeps the trigger, freeze and interactable settings");
        }
        finally { File.Delete(path); }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  SAMPLE SCENE
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckSampleScene()
    {
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            IsoSphere = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
        };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "CollisionTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        BasicEntity Named(string name) => scene.BasicEntities.Single(e => e.Name == name);

        BasicEntity player = Named("Player");
        Check(scene.BasicEntities.Count == 37 && player.GetComponent<PhysicsComponent>() is
                  { BodyType: PhysicsBodyType.Dynamic, Mass: 80, FreezeRotation: true, IsTrigger: false } &&
              player.GetComponent<ScriptBehaviourComponent>()?.ScriptId == CollisionTestPlayerScript.ScriptId,
            "the collision sample loads with an upright dynamic player capsule running the player script");
        Check(new[] { "Goal Zone", "Launch Pad" }.All(n => Named(n).GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Static, IsTrigger: true }) &&
              new[] { "Gate Lever", "Ball Dispenser", "Prize Cube" }.All(n => Named(n).GetComponent<InteractableComponent>() != null &&
                                                                              Named(n).GetComponent<PhysicsComponent>() is { IsTrigger: false }),
            "its zones are static triggers and its interactables have colliders");
        Check(scene.BasicEntities.SelectMany(e => e.GetComponents<ScriptBehaviourComponent>()).All(s => ScriptRegistry.Find(s.ScriptId) != null) &&
              scene.BasicEntities.Count(e => e.GetComponent<ScriptBehaviourComponent>() != null) == 7,
            "every script in the collision sample is registered");
        Check(new[] { "Light Crate", "Medium Crate", "Heavy Crate", "Anchor Block" }.Select(n => Named(n).Mass).SequenceEqual(new[] { 10f, 60f, 180f, 2000f }),
            "the push lane has crates of increasing mass");
        SceneList.Load();
        Check(SceneList.Find("CollisionTest") >= 0, "the collision sample is in the scene list");
        string ui = GameInfo.ResolveContentFile("UI/CollisionTest");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the collision sample's HUD ships with the scene");

        // ---- Play: walk onto the launch pad, use the lever and the dispenser, then stop ----
        KeyboardState keyboard = Input.keyboardState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool editor = GameSettings.e_enableeditor, physicsOn = GameSettings.p_physics;
        using var physicsSystem = new PhysicsSystem(new Vector3(0, 0, -9.81f));
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

            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
            void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(frame);
                    logic.UpdatePhysics(Dt);
                }
            }

            logic.PlayMode.Play();
            Frames(30);
            Check(Math.Abs(player.Position.Z - 1) < 0.05f && Vector3.Distance(scene.MainCamera.Position, player.Position + new Vector3(0, 0, 0.7f)) < 0.05f &&
                  CollisionTestFeed.Recent.Count >= 1,
                "the player stands on the ground and the camera sits at its eyes");

            // The saved camera looks along +Y, straight at the launch pad.
            Input.keyboardState = new KeyboardState(Keys.W);
            float highest = 0;
            for (int i = 0; i < 150; i++)
            {
                Frames(1);
                highest = Math.Max(highest, player.Position.Z);
            }
            Input.keyboardState = new KeyboardState();
            Check(CollisionTestFeed.Recent.Contains("Launch Pad launched Player") &&
                  CollisionTestFeed.Recent.Contains("Player entered trigger 'Launch Pad'") && highest > 3,
                $"walking onto the launch pad raises trigger events on both and throws the player up (peak {highest:0.0} m)");
            Frames(120);
            Check(player.Position.Y > -1.8f && Math.Abs(player.Position.Z - 1) < 0.1f && Vector3.Distance(player.RotationMatrix.Up, Vector3.UnitY) < 1e-3f,
                "the player lands past the pad, still upright");

            // ---- Pushing: the player is a body, so the solver pushes by mass and friction ----
            void WalkFrom(Vector3 start, int frames)
            {
                player.Position = start;
                player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
                Input.keyboardState = new KeyboardState(Keys.W); // still facing +Y
                Frames(frames);
                Input.keyboardState = new KeyboardState();
                Frames(30);
            }
            BasicEntity medium = Named("Medium Crate"), anchor = Named("Anchor Block");
            Vector3 mediumStart = medium.Position, anchorStart = anchor.Position;
            WalkFrom(new Vector3(-8.5f, -5.5f, 1f), 90);
            Check(medium.Position.Y > mediumStart.Y + 1f && Math.Abs(medium.Position.X - mediumStart.X) < 0.3f &&
                  CollisionTestFeed.Recent.Any(l => l.StartsWith("Bumped into Medium Crate")),
                $"walking into the 60 kg crate pushes it ({medium.Position.Y - mediumStart.Y:0.0} m) and reports the bump");
            WalkFrom(new Vector3(-5f, -11.5f, 1f), 90);
            Check(Vector3.Distance(anchor.Position, anchorStart) < 0.05f && player.Position.Y < -10.2f,
                "the 2000 kg anchor doesn't budge and stops the player");

            BasicEntity gate = Named("Gate"), lever = Named("Gate Lever");
            Vector3 leverColor = new(lever.GetComponent<MaterialComponent>().Red, lever.GetComponent<MaterialComponent>().Green, lever.GetComponent<MaterialComponent>().Blue);
            Check(InteractableComponent.Use(lever, new Interaction { Interactor = player }) &&
                  lever.GetComponent<InteractableComponent>().Prompt == "Close the gate", "using the lever runs its script, which updates its prompt");
            Frames(90);
            Check(gate.Position.Z < -1 && CollisionTestFeed.Recent.Contains("Gate opening"), "the lever slides the gate into the floor");

            Check(InteractableComponent.Use(Named("Ball Dispenser"), new Interaction { Interactor = player }), "the dispenser can be used");
            BasicEntity ball = logic.BasicEntities.SingleOrDefault(e => e.Name == "Ball 1");
            Check(ball is { IsRuntimeSpawned: true } && ball.GetComponent<PhysicsComponent>()?.BodyType == PhysicsBodyType.Dynamic,
                "the dispenser spawns a dynamic ball");
            float spawnZ = ball!.Position.Z;
            Frames(30);
            Check(ball.Position.Z < spawnZ - 1, "the dispensed ball falls");

            logic.PlayMode.Stop();
            MaterialComponent restored = lever.GetComponent<MaterialComponent>();
            Check(!logic.BasicEntities.Any(e => e.IsRuntimeSpawned) && lever.GetComponent<InteractableComponent>().Prompt == "Open the gate" &&
                  new Vector3(restored.Red, restored.Green, restored.Blue) == leverColor && Math.Abs(gate.Position.Z - 1) < 1e-4f,
                "Stop removes the balls and puts back the lever's prompt and colour and the gate");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(Dt);
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editor;
            GameSettings.p_physics = physicsOn;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  FIXTURES
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>A physics scene of box colliders with recorder scripts, stepped like Play mode.</summary>
    private sealed class World : IDisposable
    {
        public readonly PhysicsSystem Physics = new(new Vector3(0, 0, -9.81f));
        public readonly ScenePhysics Scene;
        public readonly List<BasicEntity> Entities = new();
        private readonly ModelDefinition _cube = new(null, new BoundingBox(-Vector3.One, Vector3.One));

        public World() => Scene = new ScenePhysics(Physics) { FallbackGeometry = BoxGeometry };

        public BasicEntity Add(string name, Vector3 position, Vector3 scale, PhysicsBodyType type, float mass = 1,
            bool trigger = false, bool freeze = false, bool recorder = true)
        {
            var entity = new BasicEntity(_cube, null, position, Matrix.Identity, scale) { Name = name, IsEnabled = true };
            if (type != PhysicsBodyType.None)
                entity.AddComponent(new PhysicsComponent { BodyType = type, Mass = mass, IsTrigger = trigger, FreezeRotation = freeze });
            if (recorder)
            {
                var script = new ScriptBehaviourComponent { ScriptId = RecorderScript.Id };
                entity.AddComponent(script);
                script.OnStart(entity);
            }
            Entities.Add(entity);
            return entity;
        }

        public void Step(int frames)
        {
            for (int i = 0; i < frames; i++) Scene.Update(Entities, Dt, simulate: true);
        }

        /// <summary>The model's bounds as a box, wound both ways (BEPU mesh triangles are one-sided).</summary>
        public static (Vector3[] Vertices, int[] Indices) BoxGeometry(BasicEntity entity)
        {
            Vector3[] corners = entity.BoundingBox.GetCorners();
            int[] faces =
            {
                0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0,
            };
            var indices = new int[faces.Length * 2];
            for (int i = 0; i < faces.Length; i += 3)
            {
                indices[i] = faces[i];
                indices[i + 1] = faces[i + 1];
                indices[i + 2] = faces[i + 2];
                indices[faces.Length + i] = faces[i];
                indices[faces.Length + i + 1] = faces[i + 2];
                indices[faces.Length + i + 2] = faces[i + 1];
            }
            return (corners, indices);
        }

        public void Dispose()
        {
            Scene.DetachAll();
            Physics.Dispose();
        }
    }

    public sealed class RecorderScript : ScriptBehaviour
    {
        public const string Id = "test-collision-recorder";
        private static readonly List<RecorderScript> Instances = new();
        private readonly List<(string Phase, Collision Collision)> _collisions = new();
        private readonly List<(string Phase, BasicEntity Other)> _triggers = new();
        public readonly List<Interaction> Interactions = new();

        public RecorderScript() => Instances.Add(this);

        public static RecorderScript Of(BasicEntity entity) => Instances.Last(s => s.GameObject == entity);

        public List<Collision> Collisions(string phase) => _collisions.Where(c => c.Phase == phase).Select(c => c.Collision).ToList();
        public List<BasicEntity> Triggers(string phase) => _triggers.Where(t => t.Phase == phase).Select(t => t.Other).ToList();

        public override void OnCollisionEnter(Collision collision) => _collisions.Add(("Enter", collision));
        public override void OnCollisionStay(Collision collision) => _collisions.Add(("Stay", collision));
        public override void OnCollisionExit(Collision collision) => _collisions.Add(("Exit", collision));
        public override void OnTriggerEnter(BasicEntity other) => _triggers.Add(("Enter", other));
        public override void OnTriggerStay(BasicEntity other) => _triggers.Add(("Stay", other));
        public override void OnTriggerExit(BasicEntity other) => _triggers.Add(("Exit", other));
        public override void OnInteract(Interaction interaction) => Interactions.Add(interaction);
    }

    private static void Drain(EditorBridge bridge)
    {
        for (int i = 0; i < 6; i++)
            typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
