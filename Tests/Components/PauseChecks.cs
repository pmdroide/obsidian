using System.Reflection;
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
/// GameFlow.Paused and the Pause Menu script on the PersistenceTest Player: Esc freezes scripts and physics while
/// the menu keeps running, Restart reloads the scene with the persistent player (and its menu) carried along, and
/// Main menu loads scene 0 with carryPersistent: false, leaving the player behind (Stop in Anvil puts it back).
/// </summary>
internal static class PauseChecks
{
    private const float Dt = 1 / 60f;

    public static void Run()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            IsoSphere = new ModelDefinition(null, bounds),
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
        };
        string root = new EditorBridge().ContentSourceRoot;
        var editScene = SceneSerialization.LoadFromFile(Path.Combine(root, "Scenes", "PersistenceTest.obsc"), assets);
        var second = SceneSerialization.LoadFromFile(Path.Combine(root, "Scenes", "PersistenceTest2.obsc"), assets);
        BasicEntity player = editScene.BasicEntities.Single(e => e.Name == "Player");
        static bool HasPauseMenu(BasicEntity e) =>
            e.GetComponents<ScriptBehaviourComponent>().Any(c => c.ScriptId == PauseMenuScript.ScriptId);
        Check(ScriptRegistry.Find(PauseMenuScript.ScriptId) != null && HasPauseMenu(player) &&
              HasPauseMenu(second.BasicEntities.Single(e => e.Name == "Player")),
            "both PersistenceTest scenes put the Pause Menu script on their persistent Player");
        string ui = GameInfo.ResolveContentFile("UI/PauseMenu");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the pause menu document ships with the script");

        int playerIndex = editScene.BasicEntities.IndexOf(player), playerId = player.Id, editCount = editScene.BasicEntities.Count;
        Vector3 playerStart = player.Position;
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
            SceneList.Load();
            var logic = new MainSceneLogic();
            var scenePhysics = new ScenePhysics(physicsSystem) { FallbackGeometry = CollisionChecks.World.BoxGeometry };
            PersistenceChecks.Wire(logic, assets, scenePhysics);
            logic.SceneManager.SetActiveScene(editScene);

            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
            void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    PersistenceChecks.ApplyPending(logic);
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(frame);
                    logic.UpdatePhysics(Dt);
                }
            }
            // Pressed for one frame, released the next.
            void Tap(Keys key)
            {
                Input.keyboardState = new KeyboardState(key);
                Frames(1);
                Input.keyboardState = new KeyboardState();
                Frames(1);
            }
            bool FramesUntil(Func<bool> done, int limit = 120)
            {
                for (int i = 0; i < limit && !done(); i++) Frames(1);
                return done();
            }

            logic.PlayMode.Play();
            Frames(30);
            var pause = PersistenceChecks.Behaviour<PauseMenuScript>(player)!;
            var mover = PersistenceChecks.Behaviour<PersistentPlayerScript>(player)!;
            ScriptBehaviourComponent pauseComponent = player.GetComponents<ScriptBehaviourComponent>().Single(c => c.ScriptId == PauseMenuScript.ScriptId);
            Check(!pause.IsMenuOpen && !GameFlow.Paused && !GameFlow.EscapeQuits && pause.TimePlayed > 0.4f,
                "Play starts the pause menu closed, counting session time; Esc no longer quits the game");

            // ---- Esc pauses: the player is falling, and freezes in mid-air ----
            player.Position = new Vector3(0, -10, 6);
            Frames(10);
            Tap(Keys.Escape);
            Vector3 position = player.Position, velocity = mover.Velocity;
            float moverTime = mover.PlayTime, played = pause.TimePlayed;
            Check(GameFlow.Paused && pause.IsMenuOpen && pause.Pauses == 1 && velocity.Z < -1f, "Esc pauses the game and opens the menu");
            // R reloads the scene in the player script: paused, it must not.
            Input.keyboardState = new KeyboardState(Keys.R);
            Frames(60);
            Input.keyboardState = new KeyboardState();
            Frames(1);
            Check(player.Position == position && mover.Velocity == velocity && mover.PlayTime == moverTime && pause.TimePlayed == played &&
                  ReferenceEquals(logic.ActiveScene, editScene),
                "while paused the body holds its pose and velocity, and other scripts (the player's R reload) don't run");

            // ---- Esc again resumes, after the menu's short fade ----
            Tap(Keys.Escape);
            Check(GameFlow.Paused && pause.IsMenuOpen, "Esc on the pause page resumes, but the game waits for the menu to fade out");
            Check(FramesUntil(() => !GameFlow.Paused, 30) && !pause.IsMenuOpen, "then the game resumes");
            Frames(5);
            Check(player.Position.Z < position.Z && mover.PlayTime > moverTime && pause.TimePlayed > played,
                "and carries on where it stopped: the player keeps falling, the clocks run again");

            // ---- Restart scene: the persistent player and its menu come along ----
            Frames(60);
            Tap(Keys.Escape);
            Tap(Keys.Down);  // Restart scene
            Tap(Keys.Enter); // asks first
            Tap(Keys.Right); // Restart
            Tap(Keys.Enter);
            Check(GameFlow.Paused && ReferenceEquals(logic.ActiveScene, editScene), "Restart fades out first, still paused");
            Check(FramesUntil(() => !ReferenceEquals(logic.ActiveScene, editScene)), "then reloads the scene");
            Scene reloaded = logic.ActiveScene;
            Check(reloaded.Name == "PersistenceTest" && reloaded.BasicEntities.Contains(player) &&
                  reloaded.BasicEntities.Count(e => e.Name == "Player") == 1 && !GameFlow.Paused,
                "the scene reloads unpaused, with the carried player in place of its file copy");
            Check(ReferenceEquals(PersistenceChecks.Behaviour<PauseMenuScript>(player), pause) && pauseComponent.IsRunning &&
                  pause.ScenesLoaded == 1 && pause.Pauses == 2 && pause.Route.SequenceEqual(new[] { "PersistenceTest", "PersistenceTest" }) &&
                  pause.SceneTime < pause.TimePlayed,
                "it is the same pause menu instance: its session numbers carried over, the scene clock restarted");
            Frames(60);
            Check(!pause.IsMenuOpen, "the menu has closed after arriving");

            // ---- Main menu: scene 0, leaving persistent gameobjects behind ----
            Tap(Keys.Escape);
            for (int i = 0; i < 3; i++) Tap(Keys.Down); // Main menu
            Tap(Keys.Enter);
            Tap(Keys.Right);
            Tap(Keys.Enter);
            Check(FramesUntil(() => logic.ActiveScene?.Name == GameFlow.SceneName(0)), "Main menu loads scene list entry 0");
            Check(!logic.BasicEntities.Contains(player) && !logic.BasicEntities.Any(e => e.Name == "Player") &&
                  !pauseComponent.IsRunning && !GameFlow.Paused && GameFlow.EscapeQuits,
                "without the persistent player: its scripts stopped with the scene they were in, and the game is unpaused");
            Check(editScene.BasicEntities.Contains(player) && editScene.BasicEntities.IndexOf(player) == playerIndex &&
                  player.Id == playerId && player.Position == playerStart,
                "inside Anvil the player left behind is already back in the edited scene, as it was when Play started");

            logic.PlayMode.Stop();
            Check(ReferenceEquals(logic.ActiveScene, editScene) && editScene.BasicEntities.Count == editCount &&
                  editScene.BasicEntities.Count(e => e.Name == "Player") == 1 && editScene.BasicEntities.IndexOf(player) == playerIndex,
                "Stop returns to the edited scene with its player once, in its place");

            // ---- Standalone: a load without carrying starts the next scene's own Player ----
            host.SetHostedByEditor(false);
            logic.PlayMode.Play();
            Frames(2);
            Check(GameFlow.LoadScene("PersistenceTest2", carryPersistent: false), "LoadScene(..., carryPersistent: false) queues the load");
            Frames(1);
            Scene next = logic.ActiveScene;
            BasicEntity? ownPlayer = next.BasicEntities.SingleOrDefault(e => e.Name == "Player");
            Check(next.Name == "PersistenceTest2" && ownPlayer != null && ownPlayer != player &&
                  PersistenceChecks.Behaviour<PauseMenuScript>(ownPlayer) is { ScenesLoaded: 0 },
                "standalone, the next scene starts with its own Player and a fresh pause menu");
            GameFlow.Paused = true;
            logic.PlayMode.Stop();
            Check(!GameFlow.Paused, "Stop always unpauses");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(Dt);
            GameFlow.Paused = false;
            host.SetHostedByEditor(false);
            Input.HostBridge = previousHost;
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editor;
            GameSettings.p_physics = physicsOn;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  THE VISTA LAYER (--graphics)
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The menu opens its layer above the player's HUD on pause, fills it, and closes it on resume.</summary>
    public static void RunGraphics(GraphicsDevice graphics, ContentManager content)
    {
        var fonts = new Vista.UIFontRegistry();
        fonts.Register("default", content.Load<SpriteFont>("Fonts/UI/Body"), isDefault: true);
        foreach (string family in new[] { "Display", "Heading", "Body", "Caption" })
            fonts.Register(family.ToLowerInvariant(), content.Load<SpriteFont>("Fonts/UI/" + family));
        GameUI.Bind(graphics, fonts);

        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets { Cube = new ModelDefinition(null, bounds), IsoSphere = new ModelDefinition(null, bounds), Capsule = new ModelDefinition(null, bounds) };
        var scene = SceneSerialization.LoadFromFile(Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "PersistenceTest.obsc"), assets);
        BasicEntity player = scene.BasicEntities.Single(e => e.Name == "Player");

        KeyboardState keyboard = Input.keyboardState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool overlay = GameSettings.u_showdisplayinfo > 0;
        int displayInfo = GameSettings.u_showdisplayinfo;
        var logic = new MainSceneLogic();
        try
        {
            GameUI.CloseAll();
            PersistenceChecks.Wire(logic, assets, null);
            logic.SceneManager.SetActiveScene(scene);
            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));
            void Step(KeyboardState state)
            {
                Input.keyboardState = state;
                GameInput.Update(Dt);
                logic.PlayMode.UpdateScripts(frame);
                GameUI.Update(frame);
            }
            void Tap(Keys key)
            {
                Step(new KeyboardState(key));
                Step(new KeyboardState());
            }

            logic.PlayMode.Play();
            Check(GameUI.Count == 1, "Play opens only the player's HUD");
            var pause = PersistenceChecks.Behaviour<PauseMenuScript>(player)!;
            Tap(Keys.Escape);
            var layer = (Vista.UIManager)typeof(PauseMenuScript).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pause)!;
            Check(GameUI.Count == 2 && layer.Query("#root")!.ClassList.Contains("open") &&
                  layer.Query("#pause-screen")!.ClassList.Contains("active") &&
                  layer.Query("#pause-list .option.selected")?.GetAttribute("data-action") == "resume",
                "pausing opens the menu's layer on top, on the pause page with Resume selected");
            Check(layer.QueryAll("#settings-list .setting").Count() == 9 &&
                  layer.Query("#scene-line")!.TextContent.StartsWith("PersistenceTest") &&
                  layer.Query("#session-note")!.TextContent.Contains("persistent Player"),
                "the document is filled in: settings rows, scene line, and the session card");

            Tap(Keys.Down);
            Tap(Keys.Down);
            Tap(Keys.Enter); // Settings
            Tap(Keys.Down);  // Performance overlay
            Tap(Keys.Right);
            var row = layer.QueryAll("#settings-list .setting").ElementAt(1);
            Check(layer.Query("#settings-screen")!.ClassList.Contains("active") && (GameSettings.u_showdisplayinfo > 0) != overlay &&
                  row.QuerySelector(".value")!.TextContent == (overlay ? "Off" : "On"),
                "Settings changes a value in place and shows it");

            Tap(Keys.Escape); // back to the pause page
            Tap(Keys.Escape); // resume
            for (int i = 0; i < 30; i++) Step(new KeyboardState());
            Check(!GameFlow.Paused && GameUI.Count == 1, "resuming closes the menu's layer; the HUD stays");
            logic.PlayMode.Stop();
            Check(GameUI.Count == 0, "Stop closes the rest");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(Dt);
            GameFlow.Paused = false;
            GameSettings.u_showdisplayinfo = displayInfo;
            GameUI.CloseAll();
            GameFlow.SceneLogic = previousLogic;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
