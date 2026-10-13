using System.Text.Json;
using Anvil.ViewModels;
using Engine.Recources;

/// <summary>
/// Edit > Project Settings > Delete Project. The real Content folder is only planned (read-only);
/// the delete runs on a fake project in a temp folder.
/// </summary>
internal static class ProjectResetChecks
{
    public static void Run()
    {
        CheckPlanOnRealContent();
        CheckRunOnFakeProject();
        CheckSettingsDialog();
    }

    private static void CheckPlanOnRealContent()
    {
        string root = GameInfo.SourceContentRoot;
        List<string> plan = ProjectReset.Plan(root);
        Check(plan.Contains("Scenes") && plan.Contains("UI/MainMenu.xml") && plan.Contains("GameObjects/Textures"),
            "Delete Project removes the scenes, UI documents and sample textures");
        Check(!plan.Contains("UI") && !plan.Any(p => p.StartsWith("UI/LoadingScreen", StringComparison.OrdinalIgnoreCase)),
            "the engine's loading screen document is kept");
        Check(plan.All(p => !ProjectReset.IsKept(p)) &&
              !plan.Any(p => p.StartsWith("Shaders", StringComparison.OrdinalIgnoreCase) ||
                             p.StartsWith("Fonts", StringComparison.OrdinalIgnoreCase) ||
                             p.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                             p.StartsWith("Scripts", StringComparison.OrdinalIgnoreCase) ||
                             p.StartsWith("GameObjects/Default", StringComparison.OrdinalIgnoreCase) ||
                             p.Equals("Content.mgcb", StringComparison.OrdinalIgnoreCase) ||
                             p.Equals("bin", StringComparison.OrdinalIgnoreCase)),
            "Delete Project keeps what the engine loads at boot (shaders, fonts, System, scripts, default models)");
        Check(Directory.Exists(Path.Combine(root, "Scenes")) && File.Exists(Path.Combine(root, "Content.mgcb")),
            "planning Delete Project leaves the real Content folder untouched");
    }

    private static void CheckRunOnFakeProject()
    {
        string temp = Path.Combine(Path.GetTempPath(), "obsidian-project-reset-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "Content");
        string built = Path.Combine(root, "bin", "Windows");
        string builtContent = Path.Combine(built, "Content");
        string exe = Path.Combine(temp, "exe", "Content");
        try
        {
            Write(root, "Shaders/a.fx");
            Write(root, "Fonts/defaultFont.spritefont");
            Write(root, "Scripts/MyScript.cs");
            Write(root, "GameObjects/Default/cube.fbx");
            Write(root, "GameObjects/sky.dds");
            Write(root, "GameObjects/Models/Rock/rock.fbx");
            Write(root, "GameObjects/Textures/stone.png");
            Write(root, "Scenes/Level.obsc");
            Write(root, "UI/Menu.xml");
            Write(root, "UI/LoadingScreen.xml");
            Write(root, "UI/LoadingScreen.css");
            Write(root, "Content/userfile.png");   // a user folder named like a built-output root
            Write(root, "readme.txt");
            Write(root, "System/Editor/arrow.fbx");
            Write(root, GameInfo.DefaultIconPath);
            File.WriteAllText(Path.Combine(root, "System", "GameInfo.json"), "{\"WindowTitle\":\"My Game\"}");
            File.WriteAllText(Path.Combine(root, "System", "SceneList.json"), "{\"Scenes\":[\"Scenes/Level.obsc\"]}");
            File.WriteAllText(Path.Combine(root, "Content.mgcb"), string.Join("\n",
                "/outputDir:bin/$(Platform)", "",
                "#begin Shaders/a.fx", "/build:Shaders/a.fx", "",
                "#begin GameObjects/Default/cube.fbx", "/build:GameObjects/Default/cube.fbx", "",
                "#begin GameObjects/Models/Rock/rock.fbx", "/build:GameObjects/Models/Rock/rock.fbx", "",
                "#begin GameObjects/Textures/stone.png", "/build:GameObjects/Textures/stone.png", "",
                "#begin Content/userfile.png", "/build:Content/userfile.png") + "\n");
            Write(built, "GameObjects/Models/Rock/rock.xnb");
            Write(built, "Shaders/a.xnb");
            Write(builtContent, "Shaders/a.xnb");
            Write(exe, "GameObjects/Textures/stone.xnb");
            Write(exe, "Scenes/Level.obsc");

            var deletedThroughCallback = new List<string>();
            ProjectReset.Result result = ProjectReset.Run(new ProjectReset.Options
            {
                ContentRoot = root,
                OutputRoots = { built, builtContent, exe },
                DeleteSource = path =>
                {
                    deletedThroughCallback.Add(path);
                    if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path);
                },
            });

            Check(result.Succeeded && result.Removed.Count == deletedThroughCallback.Count &&
                  result.Removed.SequenceEqual(new[] { "Content", "GameObjects/Models", "GameObjects/Textures", "readme.txt", "Scenes", "UI/Menu.xml" }),
                "Delete Project removes every top-most project item through the delete callback (Recycle Bin in Anvil)");
            Check(!File.Exists(Path.Combine(root, "GameObjects", "Models", "Rock", "rock.fbx")) &&
                  !File.Exists(Path.Combine(root, "UI", "Menu.xml")) &&
                  !File.Exists(Path.Combine(root, "Scenes", "Level.obsc")) &&
                  Directory.Exists(Path.Combine(root, "Scenes")),
                "project files are gone and an empty Scenes folder is left for the new scene");
            Check(File.Exists(Path.Combine(root, "Shaders", "a.fx")) && File.Exists(Path.Combine(root, "Scripts", "MyScript.cs")) &&
                  File.Exists(Path.Combine(root, "GameObjects", "Default", "cube.fbx")) && File.Exists(Path.Combine(root, "GameObjects", "sky.dds")) &&
                  File.Exists(Path.Combine(root, "System", "Editor", "arrow.fbx")) && File.Exists(Path.Combine(root, "Fonts", "defaultFont.spritefont")) &&
                  File.Exists(Path.Combine(root, "UI", "LoadingScreen.xml")) && File.Exists(Path.Combine(root, "UI", "LoadingScreen.css")),
                "engine content, scripts, editor gizmos and the loading screen survive Delete Project");

            string mgcb = File.ReadAllText(Path.Combine(root, "Content.mgcb"));
            Check(mgcb.Contains("#begin Shaders/a.fx") && mgcb.Contains("#begin GameObjects/Default/cube.fbx") &&
                  !mgcb.Contains("Rock/rock.fbx") && !mgcb.Contains("stone.png") && !mgcb.Contains("userfile.png") &&
                  result.ManifestEntriesRemoved.Count == 3,
                "Content.mgcb loses only the removed items' entries");
            Check(!File.Exists(Path.Combine(built, "GameObjects", "Models", "Rock", "rock.xnb")) &&
                  !File.Exists(Path.Combine(exe, "GameObjects", "Textures", "stone.xnb")) &&
                  !File.Exists(Path.Combine(exe, "Scenes", "Level.obsc")) &&
                  File.Exists(Path.Combine(built, "Shaders", "a.xnb")),
                "built copies of removed content are deleted so they don't come back at the next boot");
            Check(File.Exists(Path.Combine(builtContent, "Shaders", "a.xnb")),
                "a user folder named Content never takes the built engine content (bin/Windows/Content) with it");

            var gameInfo = JsonSerializer.Deserialize<GameInfo.Data>(File.ReadAllText(Path.Combine(root, "System", "GameInfo.json")))!;
            var scenes = JsonSerializer.Deserialize<SceneList.Data>(File.ReadAllText(Path.Combine(root, "System", "SceneList.json")))!;
            var intro = JsonSerializer.Deserialize<IntroVideoList.Data>(File.ReadAllText(Path.Combine(root, "System", "IntroVideos.json")))!;
            string steam = File.ReadAllText(Path.Combine(root, "System", "SteamSettings.json"));
            Check(gameInfo.WindowTitle == new GameInfo.Data().WindowTitle && scenes.Scenes.SequenceEqual(new[] { ProjectReset.FirstScene }) &&
                  intro.Videos.Count == 0 && steam.Contains("\"Enabled\": false") &&
                  !File.Exists(Path.Combine(root, GameInfo.DefaultIconPath.Replace('/', Path.DirectorySeparatorChar))),
                "settings are reset: default window, Scenes/Main.obsc as the only scene, no intro, Steam off, no icon");
            Check(File.ReadAllText(Path.Combine(root, "System", "InputActions.json")) ==
                  Engine.Logic.InputActionList.ToJson(Engine.Logic.InputActionList.Defaults()),
                "a new project starts with the default input actions");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private static void CheckSettingsDialog()
    {
        var vm = new ProjectSettingsViewModel(isPlaying: false);
        Check(vm.CanDelete && vm.PlannedRemovals.Count == ProjectReset.Plan(vm.ContentRoot).Count && !vm.IsConfirmationValid,
            "Project Settings lists what Delete Project removes and starts unconfirmed");
        vm.BeginDeleteCommand.Execute(null);
        vm.ConfirmationText = "wrong";
        bool wrongRejected = vm.IsConfirmingDelete && !vm.IsConfirmationValid;
        vm.ConfirmationText = vm.ConfirmationPhrase;
        Check(wrongRejected && vm.IsConfirmationValid, "Delete Project needs the project name typed to confirm");
        vm.CancelDeleteCommand.Execute(null);
        Check(!vm.IsConfirmingDelete && !vm.IsConfirmationValid, "Cancel clears the confirmation");

        var playing = new ProjectSettingsViewModel(isPlaying: true);
        playing.BeginDeleteCommand.Execute(null);
        playing.ConfirmationText = playing.ConfirmationPhrase;
        Check(!playing.CanDelete && !playing.IsConfirmingDelete && !playing.IsConfirmationValid,
            "Delete Project is blocked during Play");

        Check(new MainWindowViewModel().OpenProjectSettingsCommand != null, "Edit > Project Settings has a command");
    }

    private static void Write(string root, string rel)
    {
        string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, rel);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
