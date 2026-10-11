using System.Reflection;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;
using Engine.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

/// <summary>
/// Engine/Content/Scenes/MultiplayerTest.obsc and its script, without Steam (the lobby and
/// networking need two signed-in Steam clients): the local capsule spawns, moves and is removed on
/// Stop, and script-spawned gameobjects are never saved.
/// </summary>
internal static class MultiplayerChecks
{
    public static void Run()
    {
        var bounds = new BoundingBox(-Vector3.One, Vector3.One);
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, bounds),
            IsoSphere = new ModelDefinition(null, bounds),
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
        };
        Check(assets.FindModel("Capsule") == assets.Capsule && assets.FindModel("Cube") == assets.Cube &&
              assets.FindModel("NoSuchModel") == null && assets.FindModel(null) == null,
            "models are found by their scene key");

        // ---- The sample scene ----
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "MultiplayerTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        Check(scene.BasicEntities.Count == 10 && scene.BasicEntities.Count(e => e.Name.StartsWith("Wall")) == 4 &&
              scene.DirectionalLights.Single().CastShadows && scene.MainCamera != null,
            "the multiplayer sample scene loads the arena, its walls, the sun and the camera");
        Check(scene.MainCamera!.GetComponent<ScriptBehaviourComponent>() is { ScriptId: MultiplayerTestScript.ScriptId, Enabled: true } &&
              ScriptRegistry.Find(MultiplayerTestScript.ScriptId) != null,
            "the multiplayer scene's camera runs the registered Multiplayer Test script");
        SceneList.Load();
        Check(SceneList.Find("MultiplayerTest") >= 0, "the multiplayer scene is in the scene list");
        string ui = GameInfo.ResolveContentFile("UI/MultiplayerTest");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the multiplayer HUD's Vista document ships with the scene");

        // ---- Offline Play: no Steam session, the local capsule still works ----
        SteamService steam = SteamService.Current;
        KeyboardState keyboard = Input.keyboardState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        string saved = Path.Combine(Path.GetTempPath(), $"multiplayer-{Guid.NewGuid():N}.obsc");
        try
        {
            SteamService.Current = null;
            var logic = new MainSceneLogic();
            typeof(MainSceneLogic).GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(logic, assets);
            logic.SceneManager.Assets = assets;
            logic.PlayMode = new PlayModeController(logic);
            GameFlow.SceneLogic = logic;
            logic.SceneManager.SetActiveScene(scene);

            var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            logic.PlayMode.Play();
            logic.PlayMode.UpdateScripts(frame);
            var spawned = logic.BasicEntities.Where(e => e.IsRuntimeSpawned).ToList();
            var capsule = spawned.SingleOrDefault(e => e.ModelDefinition == assets.Capsule);
            Check(spawned.Count == 2 && capsule != null && capsule!.GetComponent<MaterialComponent>() != null &&
                  Math.Abs(capsule.Position.Z - 1f) < 1e-3f,
                "offline Play spawns the local player's capsule (standing on the ground) and its visor");

            // The saved camera looks along +X, so W walks the capsule that way and the camera follows.
            Vector3 start = capsule!.Position;
            Input.keyboardState = new KeyboardState(Keys.W);
            for (int i = 0; i < 30; i++)
            {
                GameInput.Update(1 / 60f);
                logic.PlayMode.UpdateScripts(frame);
            }
            Check(capsule.Position.X > start.X + 2f && Math.Abs(capsule.Position.Y - start.Y) < 0.1f,
                "W moves the local capsule forward relative to the camera");
            Check(Vector3.Distance(scene.MainCamera.Position, capsule.Position) < 10f &&
                  Vector3.Dot(scene.MainCamera.Forward, capsule.Position - scene.MainCamera.Position) > 0,
                "the camera follows behind the local capsule");
            Input.keyboardState = new KeyboardState();
            GameInput.Update(1 / 60f);

            SceneSerialization.SaveToFile(scene, saved, assets);
            Check(SceneSerialization.LoadFromFile(saved, assets).BasicEntities.Count == 10,
                "saving during Play leaves script-spawned players out of the scene file");

            logic.PlayMode.Stop();
            Check(logic.BasicEntities.Count == 10 && !logic.BasicEntities.Any(e => e.IsRuntimeSpawned),
                "Stop removes the gameobjects the script spawned");
        }
        finally
        {
            SteamService.Current = steam;
            Input.keyboardState = keyboard;
            GameInput.Update(1 / 60f);
            GameFlow.SceneLogic = previousLogic;
            if (File.Exists(saved)) File.Delete(saved);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
