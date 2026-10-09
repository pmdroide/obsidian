using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
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
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

/// <summary>
/// Persistent gameobjects (Inspector > Persistent, ScriptBehaviour.DontDestroyOnLoad): the saved flag and its
/// editor plumbing, then a scripted Play run of the PersistenceTest sample scenes. The persistent player walks
/// through the portal into PersistenceTest2 and back, still the same running instance, and Stop in Anvil puts
/// it back in the edited scene. Bodies use box colliders from each model's bounds, so no GPU is needed.
/// </summary>
internal static class PersistenceChecks
{
    private const float Dt = 1 / 60f;

    public static void Run()
    {
        ScriptRegistry.Register<KeeperSpawnerScript>(KeeperSpawnerScript.Id, "Test Keeper Spawner");
        CheckSavingAndEditor();
        CheckSampleScenes();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  SAVED FLAG AND ANVIL
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckSavingAndEditor()
    {
        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var kept = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Kept", IsEnabled = true, Persistent = true };
        var plain = new BasicEntity(assets.Cube, null, Vector3.One, Matrix.Identity, Vector3.One) { Name = "Plain", IsEnabled = true };
        plain.RuntimePersistent = true; // DontDestroyOnLoad: this Play session only
        Check(kept.IsPersistent && plain.IsPersistent && !plain.Persistent && ((BasicEntity)kept.Clone).Persistent,
            "Persistent and DontDestroyOnLoad both make a gameobject persistent, and clones keep the flag");

        var scene = new Scene { Name = "Saved" };
        scene.BasicEntities.Add(kept);
        scene.BasicEntities.Add(plain);
        string path = Path.Combine(Path.GetTempPath(), $"persistent-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(scene, path, assets);
            var saved = JsonNode.Parse(File.ReadAllText(path))!["Entities"]!.AsArray();
            Check((bool?)saved.Single(e => (string?)e!["Name"] == "Kept")!["Persistent"] == true &&
                  saved.Single(e => (string?)e!["Name"] == "Plain")!["Persistent"] == null,
                "only the Inspector flag is saved, and only when set (DontDestroyOnLoad is never saved)");
            var loaded = SceneSerialization.LoadFromFile(path, assets).BasicEntities;
            Check(loaded.Single(e => e.Name == "Kept").Persistent && !loaded.Single(e => e.Name == "Plain").IsPersistent,
                "Persistent round-trips through the scene file");
        }
        finally { File.Delete(path); }

        // ---- Inspector > Persistent through the bridge ----
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Hero", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        var vm = objects.Single(o => o.EngineId == entity.Id);
        Check(!vm.Persistent && vm.HasRole, "a gameobject shows Persistent unticked in the inspector");

        vm.Persistent = true;
        Drain(bridge);
        Check(entity.Persistent && logic.ActiveScene.IsDirty, "ticking Persistent reaches the engine and marks the scene dirty");
        logic.ActiveScene.IsDirty = false;
        entity.Persistent = false;
        entity.RuntimePersistent = true;
        Drain(bridge);
        BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        Check(!vm.Persistent && !logic.ActiveScene.IsDirty,
            "the inspector follows the saved flag (not a script's DontDestroyOnLoad) without pushing it back");
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  SAMPLE SCENES: PersistenceTest <-> PersistenceTest2
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckSampleScenes()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            IsoSphere = new ModelDefinition(null, bounds),
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
        };
        string root = new EditorBridge().ContentSourceRoot;
        string firstPath = Path.Combine(root, "Scenes", "PersistenceTest.obsc");
        string secondPath = Path.Combine(root, "Scenes", "PersistenceTest2.obsc");
        var editScene = SceneSerialization.LoadFromFile(firstPath, assets);
        var second = SceneSerialization.LoadFromFile(secondPath, assets);
        BasicEntity Named(Scene scene, string name) => scene.BasicEntities.Single(e => e.Name == name);

        // ---- The scene files ----
        BasicEntity player = Named(editScene, "Player"), orb = Named(editScene, CompanionOrbScript.OrbName);
        Check(player.Persistent && player.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Dynamic, FreezeRotation: true } &&
              player.GetComponent<ScriptBehaviourComponent>()?.ScriptId == PersistentPlayerScript.ScriptId &&
              Named(second, "Player").Persistent && !orb.Persistent && orb.Physics == null,
            "both sample scenes have a Persistent dynamic Player; the companion orb is not Persistent and has no collider");
        Check(new[] { editScene, second }.All(s => Named(s, "Portal").GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Static, IsTrigger: true } &&
                                                    Named(s, PersistentPlayerScript.SpawnName) != null) &&
              Named(editScene, "Portal").GetComponent<ScriptBehaviourComponent>()!.GetField<string>("TargetScene") == "PersistenceTest2" &&
              Named(second, "Portal").GetComponent<ScriptBehaviourComponent>()!.GetField<string>("TargetScene") == "PersistenceTest",
            "each scene has a Player Spawn and a trigger portal to the other");
        Check(new[] { editScene, second }.SelectMany(s => s.BasicEntities).SelectMany(e => e.GetComponents<ScriptBehaviourComponent>())
                  .All(s => ScriptRegistry.Find(s.ScriptId) != null),
            "every script in the persistence samples is registered");
        // The carried player's ID (from the first file) is taken in the second, so arriving must renumber it.
        Check(second.BasicEntities.Any(e => e.Id == player.Id) && second.BasicEntities.Any(e => e.Id == orb.Id),
            "the second scene's file reuses the carried gameobjects' IDs (exercises renumbering)");
        SceneList.Load();
        Check(SceneList.Find("PersistenceTest") >= 0 && SceneList.Find("PersistenceTest2") >= 0,
            "both persistence samples are in the scene list");
        string ui = GameInfo.ResolveContentFile("UI/PersistenceTest");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the player's HUD ships with the samples");

        // ---- Play inside Anvil ----
        int playerIndex = editScene.BasicEntities.IndexOf(player), orbIndex = editScene.BasicEntities.IndexOf(orb);
        int playerId = player.Id, orbId = orb.Id, editCount = editScene.BasicEntities.Count;
        Vector3 playerStart = player.Position, orbStart = orb.Position;
        // A script spawns a gameobject and keeps it with DontDestroyOnLoad, plus one it doesn't keep.
        BasicEntity spawner = Named(editScene, "Pillar 1");
        spawner.AddComponent(new ScriptBehaviourComponent { ScriptId = KeeperSpawnerScript.Id });

        KeyboardState keyboard = Input.keyboardState;
        IEditorBridge previousHost = Input.HostBridge;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool editor = GameSettings.e_enableeditor, physicsOn = GameSettings.p_physics;
        var host = new EditorBridge();
        using var physicsSystem = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        try
        {
            GameSettings.e_enableeditor = false;
            GameSettings.p_physics = true;
            Input.HostBridge = host;
            host.SetHostedByEditor(true);
            var logic = new MainSceneLogic();
            var scenePhysics = new ScenePhysics(physicsSystem) { FallbackGeometry = CollisionChecks.World.BoxGeometry };
            Wire(logic, assets, scenePhysics);
            logic.SceneManager.SetActiveScene(editScene);

            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
            void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    // MainSceneLogic.Update: queued scene loads first, then input and scripts; then physics.
                    ApplyPending(logic);
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(frame);
                    logic.UpdatePhysics(Dt);
                }
            }
            // Holds W until the portal's load has happened (or gives up).
            bool WalkThroughPortal(Scene from, Vector3 start)
            {
                player.Position = start;
                Input.keyboardState = new KeyboardState(Keys.W);
                for (int i = 0; i < 240 && ReferenceEquals(logic.ActiveScene, from); i++) Frames(1);
                Input.keyboardState = new KeyboardState();
                return !ReferenceEquals(logic.ActiveScene, from);
            }

            logic.PlayMode.Play();
            Frames(30);
            PersistentPlayerScript script = Behaviour<PersistentPlayerScript>(player);
            Check(script != null && Math.Abs(player.Position.Z - 1) < 0.05f && script.Route.SequenceEqual(new[] { "PersistenceTest" }) &&
                  Vector3.Distance(logic.ActiveScene.MainCamera.Position, player.Position) > 3,
                "Play starts the persistent player on the ground with the camera behind it");
            BasicEntity keeper = logic.BasicEntities.Single(e => e.Name == KeeperSpawnerScript.KeptName);
            Check(keeper.IsRuntimeSpawned && keeper.RuntimePersistent && logic.BasicEntities.Any(e => e.Name == KeeperSpawnerScript.TempName),
                "a script can keep a gameobject it spawned with DontDestroyOnLoad(entity)");

            // The orb joins when the player walks up to it, and keeps itself with DontDestroyOnLoad().
            player.Position = orbStart + new Vector3(0, -1.2f, -0.4f);
            Frames(2);
            CompanionOrbScript orbScript = Behaviour<CompanionOrbScript>(orb);
            Check(orbScript.IsFollowing && orb.RuntimePersistent && !orb.Persistent,
                "the companion orb joins the player and calls DontDestroyOnLoad()");
            Frames(60);

            // ---- Through the portal into PersistenceTest2 ----
            ScriptBehaviourComponent playerComponent = player.GetComponent<ScriptBehaviourComponent>();
            float walked = script.DistanceWalked, time = script.PlayTime;
            int? body = player.DynamicBody?.Value;
            Check(WalkThroughPortal(editScene, new Vector3(0, 7.5f, 1)), "walking into the portal loads the next scene");
            Scene arrived = logic.ActiveScene;
            Check(arrived.Name == "PersistenceTest2" && logic.PlayMode.Mode == GameMode.Play && arrived.BasicEntities.Contains(player) &&
                  arrived.BasicEntities.Count(e => e.Name == "Player") == 1,
                "the player arrives in PersistenceTest2; the scene's own Player (same name, Persistent) is left out");
            Check(ReferenceEquals(Behaviour<PersistentPlayerScript>(player), script) && playerComponent.IsRunning &&
                  script.ScenesLoaded == 1 && script.Route.SequenceEqual(new[] { "PersistenceTest", "PersistenceTest2" }) &&
                  script.DistanceWalked > walked && script.PlayTime > time,
                $"it is the same running script instance: its state carried over ({script.DistanceWalked:0.0} m, {script.PlayTime:0.0} s, OnSceneLoaded ran once)");
            Vector3 spawn = Named(arrived, PersistentPlayerScript.SpawnName).Position;
            Check(new Vector2(player.Position.X - spawn.X, player.Position.Y - spawn.Y).Length() < 0.5f,
                "OnSceneLoaded moved it to the new scene's Player Spawn");
            Check(body.HasValue && player.DynamicBody?.Value == body,
                "its physics body was kept, not rebuilt");
            var ids = arrived.BasicEntities.Select(e => e.Id).Concat(arrived.PointLights.Select(l => l.Id)).Concat(arrived.DirectionalLights.Select(l => l.Id)).ToList();
            Check(ids.Count == ids.Distinct().Count() && player.Id != playerId && player.WorldTransform.Id == player.Id,
                "carried gameobjects whose IDs the new scene already uses are renumbered (picking follows)");
            Check(arrived.BasicEntities.Contains(orb) && arrived.BasicEntities.Contains(keeper) &&
                  !arrived.BasicEntities.Any(e => e.Name == KeeperSpawnerScript.TempName || e.Name.StartsWith("Crate")) &&
                  !editScene.BasicEntities.Contains(player) && !editScene.BasicEntities.Contains(orb),
                "DontDestroyOnLoad gameobjects came along; everything else stayed behind");
            Frames(60);
            Check(Math.Abs(player.Position.Z - 1) < 0.1f && !scenePhysics.IsTouching(player, Named(editScene, "Ground")) &&
                  Vector3.Distance(orb.Position, player.Position) < 4,
                "the player lands on the new ground (the old ground's contact ended) and the orb follows");

            // ---- And back: a fresh copy of PersistenceTest loads ----
            Check(WalkThroughPortal(arrived, new Vector3(0, 15, 1)), "the second scene's portal leads back");
            Scene back = logic.ActiveScene;
            Check(back.Name == "PersistenceTest" && !ReferenceEquals(back, editScene) &&
                  back.BasicEntities.Count(e => e.Name == "Player") == 1 && back.BasicEntities.Count(e => e.Name == CompanionOrbScript.OrbName) == 1 &&
                  back.BasicEntities.Contains(player) && back.BasicEntities.Contains(orb),
                "returning to the scene they came from doesn't duplicate them: the file's copies are left out");
            Check(ReferenceEquals(Behaviour<PersistentPlayerScript>(player), script) && script.ScenesLoaded == 2 &&
                  script.Route.SequenceEqual(new[] { "PersistenceTest", "PersistenceTest2", "PersistenceTest" }) &&
                  Vector3.Distance(player.Position, Named(back, PersistentPlayerScript.SpawnName).Position + new Vector3(0, 0, 1.1f)) < 0.3f,
                "still the same player, now at the first scene's spawn");
            Frames(10);

            // ---- Stop: back to the edited scene, as it was ----
            logic.PlayMode.Stop();
            Check(ReferenceEquals(logic.ActiveScene, editScene) && editScene.BasicEntities.Count == editCount &&
                  editScene.BasicEntities.IndexOf(player) == playerIndex && editScene.BasicEntities.IndexOf(orb) == orbIndex,
                "Stop puts the player and the orb back in the edited scene, at their places in the hierarchy");
            Check(player.Id == playerId && orb.Id == orbId && player.Position == playerStart && orb.Position == orbStart &&
                  !orb.RuntimePersistent && player.Persistent && !playerComponent.IsRunning && !keeper.IsPersistent &&
                  !editScene.BasicEntities.Contains(keeper),
                "with their IDs and start poses, scripts stopped, DontDestroyOnLoad cleared, and spawned ones gone");

            // A new Play session is a new player.
            logic.PlayMode.Play();
            Frames(2);
            var fresh = Behaviour<PersistentPlayerScript>(player);
            Check(!ReferenceEquals(fresh, script) && fresh.ScenesLoaded == 0 && fresh.Route.Count == 1, "the next Play starts a new player");
            logic.PlayMode.Stop();

            // ---- Standalone: no edited scene to return to ----
            host.SetHostedByEditor(false);
            logic.PlayMode.Play();
            Frames(2);
            Check(WalkThroughPortal(editScene, new Vector3(0, 7.5f, 1)) && logic.ActiveScene.Name == "PersistenceTest2",
                "the standalone game carries the player too");
            Scene standalone = logic.ActiveScene;
            logic.PlayMode.Stop();
            Check(ReferenceEquals(logic.ActiveScene, standalone) && standalone.BasicEntities.Contains(player) &&
                  standalone.BasicEntities.Count(e => e.Name == "Player") == 1,
                "outside Anvil, a carried gameobject simply belongs to the scene it is in");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(Dt);
            host.SetHostedByEditor(false);
            Input.HostBridge = previousHost;
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editor;
            GameSettings.p_physics = physicsOn;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  GAME UI LAYERS (--graphics: Vista needs a device)
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>A layer opened by a persistent gameobject's script stays open across a scene load; others close.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        ScriptRegistry.Register<LayerScript>(LayerScript.Id, "Test Layer");
        var fonts = new Vista.UIFontRegistry();
        fonts.Register("default", content.Load<SpriteFont>("Fonts/UI/Body"), isDefault: true);
        foreach (string family in new[] { "Display", "Heading", "Body", "Caption" })
            fonts.Register(family.ToLowerInvariant(), content.Load<SpriteFont>("Fonts/UI/" + family));
        GameUI.Bind(graphics, fonts);

        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds), Capsule = new ModelDefinition(null, bounds) };
        var scene = new Scene { Name = "Layers" };
        BasicEntity Add(string name, bool persistent)
        {
            var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = name, IsEnabled = true, Persistent = persistent };
            entity.AddComponent(new ScriptBehaviourComponent { ScriptId = LayerScript.Id });
            scene.BasicEntities.Add(entity);
            return entity;
        }
        Add("Scene HUD", persistent: false);
        // Named Player, so PersistenceTest2's own Player (whose script opens a HUD too) is left out on arrival.
        BasicEntity hud = Add("Player", persistent: true);

        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        var logic = new MainSceneLogic();
        try
        {
            GameUI.CloseAll();
            Wire(logic, assets, null);
            logic.SceneManager.SetActiveScene(scene);
            logic.PlayMode.Play();
            Check(GameUI.Count == 2 && ScriptBehaviour.Running == null, "both scripts opened a layer (and Running is reset after Start)");
            GameFlow.LoadScene("PersistenceTest2");
            ApplyPending(logic);
            Check(logic.BasicEntities.Contains(hud) && GameUI.Count == 1 && GameUI.Open("UI/PersistenceTest") is { } extra &&
                  GameUI.Count == 2 && Close(extra),
                "after the scene load only the persistent gameobject's layer is still open");
            logic.PlayMode.Stop();
            Check(GameUI.Count == 0, "Stop closes it");
        }
        finally
        {
            GameUI.CloseAll();
            GameFlow.SceneLogic = previousLogic;
        }

        static bool Close(Vista.UIManager ui)
        {
            GameUI.Close(ui);
            return true;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  FIXTURES
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>Opens a HUD layer in Start and (deliberately) never closes it, so GameUI.CloseAll decides.</summary>
    private sealed class LayerScript : ScriptBehaviour
    {
        public const string Id = "test-layer";
        public override void Start() => GameUI.Open("UI/PersistenceTest");
    }

    /// <summary>Spawns two cubes on Start and keeps one with DontDestroyOnLoad; checks it runs as ScriptBehaviour.Running.</summary>
    private sealed class KeeperSpawnerScript : ScriptBehaviour
    {
        public const string Id = "test-keeper-spawner";
        public const string KeptName = "Keeper", TempName = "Temporary";

        public override void Start()
        {
            if (Running != this) throw new InvalidOperationException("ScriptBehaviour.Running is not the starting script");
            DontDestroyOnLoad(Spawn("Cube", Position + new Vector3(0, 0, 6), KeptName));
            Spawn("Cube", Position + new Vector3(0, 0, 9), TempName);
        }
    }

    internal static T Behaviour<T>(BasicEntity entity) where T : ScriptBehaviour =>
        entity.GetComponents<ScriptBehaviourComponent>()
            .Select(c => typeof(ScriptBehaviourComponent).GetField("_instance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c))
            .OfType<T>().FirstOrDefault();

    /// <summary>The parts of MainSceneLogic.Initialize that scene switching needs (no GPU).</summary>
    internal static void Wire(MainSceneLogic logic, Assets assets, ScenePhysics? physics)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(MainSceneLogic).GetField("_assets", flags)!.SetValue(logic, assets);
        if (physics != null) typeof(MainSceneLogic).GetField("_scenePhysics", flags)!.SetValue(logic, physics);
        logic.SceneManager.Assets = assets;
        logic.SceneManager.SceneChanged += (Action<Scene, Scene>)Delegate.CreateDelegate(
            typeof(Action<Scene, Scene>), logic, typeof(MainSceneLogic).GetMethod("OnSceneChanged", flags)!);
        logic.PlayMode = new PlayModeController(logic);
        logic.PlayMode.ModeChanged += (Action<GameMode>)Delegate.CreateDelegate(
            typeof(Action<GameMode>), logic, typeof(MainSceneLogic).GetMethod("OnPlayModeChanged", flags)!);
        GameFlow.SceneLogic = logic;
    }

    internal static void ApplyPending(MainSceneLogic logic) =>
        typeof(MainSceneLogic).GetMethod("ApplyPendingSceneLoad", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(logic, null);

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
