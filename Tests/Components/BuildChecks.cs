using Anvil.Models;
using Anvil.Services;
using Engine.Recources;

/// <summary>
/// Anvil's File > Build / Build and Run (<see cref="GameBuilder"/>): executable naming, the remembered output
/// folder and refused output folders. <c>--build</c> also publishes the real game (about a minute, ~360 MB in
/// the temp folder) and checks the renamed executable, the shipped content and the trimmed LibVLC natives.
/// </summary>
internal static class BuildChecks
{
    public static void Run()
    {
        Check(GameBuilder.ExecutableName("My: Game?") == "My Game" && GameBuilder.ExecutableName("  ") == "Game" &&
              GameBuilder.ExecutableName("Dots...") == "Dots", "the executable is named after the window name, without invalid characters");
        Check(File.Exists(GameBuilder.ProjectPath) && GameBuilder.DefaultOutputFolder ==
              Path.Combine(GameBuilder.RepoRoot, "Builds", GameBuilder.ExecutableName(GameInfo.Read().WindowTitle)),
            "the engine project is found and the default output is Builds/<window name>");

        string settings = Path.Combine(GameInfo.SourceContentRoot, GameBuilder.SettingsFileName);
        string? saved = File.Exists(settings) ? File.ReadAllText(settings) : null;
        try
        {
            string inside = Path.Combine(GameBuilder.RepoRoot, "Builds", "Check");
            GameBuilder.SaveOutputFolder(inside);
            Check(GameBuilder.ReadSettings().OutputFolder == Path.Combine("Builds", "Check") && GameBuilder.ResolveOutputFolder() == inside,
                "an output folder inside the checkout is remembered relative to it");
            string outside = Path.Combine(Path.GetTempPath(), "ObsidianBuildCheck");
            GameBuilder.SaveOutputFolder(outside);
            Check(GameBuilder.ResolveOutputFolder() == Path.GetFullPath(outside), "an output folder elsewhere is remembered as an absolute path");
        }
        finally
        {
            if (saved != null) File.WriteAllText(settings, saved); else File.Delete(settings);
        }

        var log = new List<(ConsoleLevel Level, string Message)>();
        string? exe = GameBuilder.BuildAsync(Path.Combine(GameBuilder.RepoRoot, "Engine", "Builds"),
            (level, message) => log.Add((level, message)), CancellationToken.None).GetAwaiter().GetResult();
        Check(exe == null && log.Count == 1 && log[0].Level == ConsoleLevel.Error,
            "a build into the engine project (which would ship itself next time) is refused");
    }

    public static void RunPublish()
    {
        string output = Path.Combine(Path.GetTempPath(), "ObsidianBuildCheck");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        var log = new List<(ConsoleLevel Level, string Message)>();
        string? exe = GameBuilder.BuildAsync(output, (level, message) => { lock (log) log.Add((level, message)); },
            CancellationToken.None).GetAwaiter().GetResult();
        foreach (var (level, message) in log) Console.WriteLine($"  [{level}] {message}");
        string name = GameBuilder.ExecutableName(GameInfo.Read().WindowTitle);
        Check(exe == Path.Combine(output, name + ".exe") && File.Exists(exe) && File.Exists(Path.Combine(output, "Engine.dll")) &&
              (name == "Engine" || !File.Exists(Path.Combine(output, "Engine.exe"))),
            "Build publishes the game with the executable named after the window name");
        Check(File.Exists(Path.Combine(output, "Content", "System", "SceneList.json")) &&
              File.Exists(Path.Combine(output, "Content", "System", "GameInfo.json")) &&
              Directory.EnumerateFiles(Path.Combine(output, "Content", "Scenes"), "*.obsc").Any() &&
              Directory.EnumerateFiles(Path.Combine(output, "Content"), "*.xnb", SearchOption.AllDirectories).Any(),
            "the build ships the scene list, game settings, scenes and built content");
        Check(Directory.Exists(Path.Combine(output, "libvlc", "win-x64")) && !Directory.Exists(Path.Combine(output, "libvlc", "win-x86")) &&
              !Directory.Exists(Path.Combine(output, "de")), "the x64 build leaves out LibVLC's x86 natives and translated resources");
        Directory.Delete(output, true);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
