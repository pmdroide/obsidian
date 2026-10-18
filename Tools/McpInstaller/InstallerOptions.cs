using System.Text.RegularExpressions;

namespace Anvil.McpInstaller;

public enum Harness { ClaudeCode, Cursor, Codex, VSCode }
public enum InstallScope { User, Project }

public sealed class InstallerOptions
{
    public List<Harness> Harnesses { get; } = [];
    public InstallScope Scope { get; set; } = InstallScope.User;
    public string Project { get; set; } = Directory.GetCurrentDirectory();
    public string? Config { get; set; }
    public string? Settings { get; set; }
    public string? Url { get; set; }
    public string? TokenEnvironment { get; set; }
    public string Name { get; set; } = "anvil";
    public bool Replace { get; set; }
    public bool DryRun { get; set; }
    public bool Yes { get; set; }
    public bool Help { get; set; }
    public bool List { get; set; }

    public static InstallerOptions Parse(string[] args)
    {
        var options = new InstallerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new InstallerException($"Missing value for {args[i - 1]}.");
            switch (args[i])
            {
                case "--harness": case "-h": options.AddHarnesses(Value()); break;
                case "--scope":
                    options.Scope = Value().ToLowerInvariant() switch
                    {
                        "user" => InstallScope.User, "project" => InstallScope.Project,
                        _ => throw new InstallerException("--scope must be user or project."),
                    };
                    break;
                case "--project": options.Project = Value(); break;
                case "--config": options.Config = Value(); break;
                case "--settings": options.Settings = Value(); break;
                case "--url": options.Url = Value(); break;
                case "--token-env": options.TokenEnvironment = Value(); break;
                case "--name": options.Name = Value(); break;
                case "--replace": options.Replace = true; break;
                case "--dry-run": options.DryRun = true; break;
                case "--yes": case "-y": options.Yes = true; break;
                case "--help": case "-?": options.Help = true; break;
                case "--list": options.List = true; break;
                default: throw new InstallerException("Unknown option. Run --help for supported options.");
            }
        }
        if (!Regex.IsMatch(options.Name, "^[A-Za-z0-9_-]{1,64}$"))
            throw new InstallerException("--name must contain 1-64 letters, digits, underscores or hyphens.");
        if (options.TokenEnvironment != null && !Regex.IsMatch(options.TokenEnvironment, "^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new InstallerException("--token-env must be an environment variable name.");
        if (options.Config != null && options.Harnesses.Count != 1)
            throw new InstallerException("--config requires exactly one explicit --harness.");
        options.Project = Path.GetFullPath(options.Project);
        return options;
    }

    public void AddHarnesses(string values)
    {
        foreach (string value in values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var choices = value.ToLowerInvariant() switch
            {
                "all" => Enum.GetValues<Harness>(),
                "1" or "claude" or "claudecode" or "claude-code" => [Harness.ClaudeCode],
                "2" or "cursor" => [Harness.Cursor],
                "3" or "codex" => [Harness.Codex],
                "4" or "vscode" or "vs-code" => [Harness.VSCode],
                _ => throw new InstallerException("Unknown harness. Choose claudecode, cursor, codex, vscode or all."),
            };
            foreach (var choice in choices) if (!Harnesses.Contains(choice)) Harnesses.Add(choice);
        }
        if (Harnesses.Count == 0) throw new InstallerException("Choose at least one harness.");
    }
}

public sealed class InstallerException(string message) : Exception(message);

public sealed record InstallerPaths(string UserHome, string AppData, string LocalAppData, string? CodexHome = null,
    string? ClaudeConfigDirectory = null)
{
    public static InstallerPaths Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("CODEX_HOME"), Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
}
