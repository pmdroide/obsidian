using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anvil.Models;
using Engine.Recources;

namespace Anvil.Services;

/// <summary>
/// File > Build / Build and Run: publishes <c>Engine/Engine.csproj</c> as a self-contained Windows x64
/// game (Release) into an output folder, renames the executable after the game's window name
/// (Game Settings) and optionally starts it. The build runs <c>dotnet publish</c> on the source checkout,
/// so it ships the saved scenes, scripts and content, never unsaved editor state.
/// </summary>
public static class GameBuilder
{
    /// <summary>Content-relative; System/ is engine-managed and not copied into builds.</summary>
    public const string SettingsFileName = "System/BuildSettings.json";

    public sealed class Settings
    {
        /// <summary>Output folder: absolute, or relative to the repository root.</summary>
        public string? OutputFolder { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The checkout root: the folder holding <c>Engine/Content</c>.</summary>
    public static string RepoRoot =>
        Path.GetFullPath(Path.Combine(GameInfo.SourceContentRoot, "..", ".."));

    public static string ProjectPath => Path.Combine(RepoRoot, "Engine", "Engine.csproj");

    /// <summary><c>Builds/&lt;window name&gt;</c> under the repository.</summary>
    public static string DefaultOutputFolder => Path.Combine(RepoRoot, "Builds", ExecutableName(GameInfo.Read().WindowTitle));

    public static Settings ReadSettings()
    {
        string path = Path.Combine(GameInfo.SourceContentRoot, SettingsFileName);
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings();
        }
        catch { /* a broken file falls back to the default folder */ }
        return new Settings();
    }

    public static void SaveOutputFolder(string folder)
    {
        string full = Path.GetFullPath(folder);
        string relative = Path.GetRelativePath(RepoRoot, full);
        // Inside the checkout it stays relative, so the setting survives moving the repository.
        bool inside = !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        string path = Path.Combine(GameInfo.SourceContentRoot, SettingsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Settings { OutputFolder = inside ? relative : full }, JsonOptions));
    }

    /// <summary>The saved output folder (absolute), or <see cref="DefaultOutputFolder"/>.</summary>
    public static string ResolveOutputFolder()
    {
        string? saved = ReadSettings().OutputFolder;
        if (string.IsNullOrWhiteSpace(saved)) return DefaultOutputFolder;
        return Path.IsPathRooted(saved) ? saved : Path.GetFullPath(Path.Combine(RepoRoot, saved));
    }

    /// <summary>A file name from the game's window name: invalid characters dropped, "Game" when empty.</summary>
    public static string ExecutableName(string? title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new string((title ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return name.Length == 0 ? "Game" : name;
    }

    /// <summary>
    /// Publishes the game into <paramref name="outputFolder"/> and returns the executable's path, or null
    /// when the build failed or was cancelled. Progress, warnings and errors go to <paramref name="log"/>
    /// (called from a worker thread).
    /// </summary>
    public static async Task<string?> BuildAsync(string outputFolder, Action<ConsoleLevel, string> log, CancellationToken cancel)
    {
        if (!File.Exists(ProjectPath))
        {
            log(ConsoleLevel.Error, $"Build needs the engine source: {ProjectPath} was not found.");
            return null;
        }
        if (IsInsideEngineProject(outputFolder))
        {
            // The engine project globs Content/**: a build in there would be published into the next build.
            log(ConsoleLevel.Error, $"Choose an output folder outside {Path.Combine(RepoRoot, "Engine")}.");
            return null;
        }

        Directory.CreateDirectory(outputFolder);
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepoRoot,
        };
        foreach (string arg in new[]
                 {
                     "publish", ProjectPath, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                     "-o", outputFolder, "-nologo", "-v", "minimal", "-clp:NoSummary",
                     "-p:GenerateFullPaths=true",
                     // The build is x64 only: skip LibVLC's x86 natives (~170 MB) and the
                     // framework's translated resource folders.
                     "-p:VlcWindowsX86Enabled=false", "-p:SatelliteResourceLanguages=en",
                 })
            psi.ArgumentList.Add(arg);

        log(ConsoleLevel.Log, $"Building to {outputFolder} ...");
        var watch = Stopwatch.StartNew();
        int warnings = 0, errors = 0;
        using var process = new Process { StartInfo = psi };
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (line.Contains(": error ", StringComparison.Ordinal)) { errors++; log(ConsoleLevel.Error, line.Trim()); }
            // Compiler warnings would flood the console; only their count is reported.
            else if (line.Contains(": warning ", StringComparison.Ordinal)) warnings++;
        }
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            log(ConsoleLevel.Error, "Couldn't start dotnet (is the .NET SDK installed?): " + ex.Message);
            return null;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            log(ConsoleLevel.Warn, "Build cancelled.");
            return null;
        }

        if (process.ExitCode != 0)
        {
            log(ConsoleLevel.Error, $"Build failed ({errors} error{(errors == 1 ? "" : "s")}, exit code {process.ExitCode}).");
            return null;
        }

        string exe = RenameExecutable(outputFolder, log);
        log(ConsoleLevel.Log, $"Build succeeded in {watch.Elapsed.TotalSeconds:0}s ({warnings} warning{(warnings == 1 ? "" : "s")}): {exe}");
        return exe;
    }

    /// <summary>Starts a built game with its own folder as the working directory.</summary>
    public static bool TryRun(string exe, out string error)
    {
        error = "";
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsInsideEngineProject(string folder)
    {
        string engine = Path.GetFullPath(Path.Combine(RepoRoot, "Engine")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(engine, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Engine.exe becomes "&lt;window name&gt;.exe". The .NET app host finds Engine.dll by the name built
    /// into it, so only the executable is renamed.
    /// </summary>
    private static string RenameExecutable(string outputFolder, Action<ConsoleLevel, string> log)
    {
        string built = Path.Combine(outputFolder, "Engine.exe");
        string named = Path.Combine(outputFolder, ExecutableName(GameInfo.Read().WindowTitle) + ".exe");
        if (string.Equals(built, named, StringComparison.OrdinalIgnoreCase)) return built;
        try
        {
            File.Move(built, named, overwrite: true);
            return named;
        }
        catch (Exception ex)
        {
            log(ConsoleLevel.Warn, $"Couldn't rename Engine.exe to {Path.GetFileName(named)}: {ex.Message}");
            return built;
        }
    }
}
