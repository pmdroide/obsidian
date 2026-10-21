namespace Anvil.McpInstaller;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.In, Console.Out, Console.Error, InstallerPaths.Current(), Console.IsInputRedirected);

    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error, InstallerPaths paths, bool redirected = false)
    {
        int installed = 0;
        try
        {
            var options = InstallerOptions.Parse(args);
            if (options.Help) { output.WriteLine(Help); return 0; }
            if (options.List)
            {
                foreach (var harness in Enum.GetValues<Harness>()) output.WriteLine($"{harness}: {Installer.TargetPath(harness, options, paths)}");
                return 0;
            }
            if (options.Harnesses.Count == 0)
            {
                if (redirected) throw new InstallerException("Choose --harness claudecode,cursor,codex,vscode (or all). Use --yes for unattended installation.");
                output.WriteLine("Anvil MCP installer\n1. Claude Code\n2. Cursor\n3. Codex\n4. VS Code\n5. All four");
                output.Write("Choose harnesses (names or numbers, comma-separated): ");
                string choice = (input.ReadLine() ?? throw new InstallerException("No harness was selected.")).Trim();
                options.AddHarnesses(choice == "5" ? "all" : choice);
                output.Write($"Scope: user or project [{options.Scope.ToString().ToLowerInvariant()}]: ");
                string scope = (input.ReadLine() ?? "").Trim().ToLowerInvariant();
                options.Scope = scope switch { "" => options.Scope, "user" => InstallScope.User, "project" => InstallScope.Project, _ => throw new InstallerException("Scope must be user or project.") };
            }
            var connection = ConnectionSettings.Load(options, paths);
            var plans = Installer.Plan(options, paths, connection);
            output.WriteLine($"Endpoint: {connection.Url}\nServer name: {options.Name}\nScope: {options.Scope}");
            output.WriteLine(connection.TokenEnvironment == null ? "Authentication: saved Anvil token (not displayed)."
                : $"Authentication: environment variable {connection.TokenEnvironment} (token is not written to client configuration).");
            foreach (var plan in plans) output.WriteLine(plan);
            if (connection.EditorDisabled) output.WriteLine("Anvil MCP is currently disabled. Enable it in Window > MCP and press Apply before connecting.");
            if (options.DryRun) { output.WriteLine("Preview only. No files changed."); return 0; }
            if (plans.All(p => !p.Changed)) { output.WriteLine("Already configured. No files changed."); return 0; }
            if (!options.Yes)
            {
                if (redirected) throw new InstallerException("The plan is ready. Use --yes to apply it or --dry-run to preview without writing.");
                output.Write("Write these client settings? [y/N]: ");
                string answer = (input.ReadLine() ?? "").Trim();
                if (!answer.Equals("y", StringComparison.OrdinalIgnoreCase) && !answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    output.WriteLine("Cancelled. No files changed.");
                    return 0;
                }
            }
            foreach (var plan in plans.Where(p => p.Changed))
            {
                string? backup = Installer.Commit(plan);
                installed++;
                output.WriteLine($"Installed {plan.Harness}: {plan.Path}");
                if (backup != null) output.WriteLine("Backup: " + backup);
            }
            output.WriteLine("Restart the selected client(s), then ask the anvil MCP to call editor_get_status.");
            if (connection.TokenEnvironment != null)
                output.WriteLine($"Set {connection.TokenEnvironment} to the raw Anvil token in the environment that launches each client. See Docs/markdown/Editor_MCP.md.");
            return 0;
        }
        catch (Exception ex)
        {
            // Parser and IO exceptions can contain source lines/credentials. Only controlled messages reach the terminal.
            error.WriteLine(ex is InstallerException ? ex.Message : "Installation failed. Check file permissions and configuration syntax; client credentials were not printed.");
            if (installed > 0) error.WriteLine($"{installed} configuration(s) were updated before the failure. Use the printed backups to restore them if needed.");
            return 1;
        }
    }

    public const string Help = """
        Anvil MCP harness installer (Windows)

        anvil-mcp [--harness claudecode,cursor,codex,vscode|all] [options]

        With no --harness, opens an interactive selection menu. Only registers
        Anvil's existing local HTTP MCP; it does not install client applications.

        --harness, -h LIST   Harness names/numbers, comma-separated; repeatable.
        --scope user|project User settings (default), or current project files.
        --project PATH       Project root for --scope project (default: current folder).
        --settings PATH      Anvil settings JSON (default: LOCALAPPDATA/Anvil/Mcp/settings.json).
        --url URL            Override local endpoint; otherwise use Anvil's saved port.
        --token-env NAME     Store a token environment-variable reference instead of the token.
                             Project scope defaults to ANVIL_MCP_TOKEN.
        --name NAME          Server entry name (default: anvil).
        --config PATH        Override target file; requires exactly one explicit harness.
        --replace            Replace a conflicting named server; other settings stay intact.
        --dry-run            Preview target files without writing credentials or backups.
        --yes, -y            Apply without the final prompt (required for redirected input).
        --list               Show supported harnesses and target paths, then exit.
        --help, -?           Show this help.

        Existing configuration files receive a timestamped .bak before an atomic
        update. Re-running identical installation is a no-op. Re-run --replace
        after changing Anvil's port or rotating its token.
        """;
}
