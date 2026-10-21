using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anvil.McpInstaller;
using Tomlyn;
using Tomlyn.Model;
using InstallerProgram = Anvil.McpInstaller.Program;

string root = Path.Combine(Path.GetTempPath(), "anvil-installer-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
try
{
    var paths = new InstallerPaths(Path.Combine(root, "user"), Path.Combine(root, "appdata"), Path.Combine(root, "localappdata"));
    string settings = Path.Combine(paths.LocalAppData, "Anvil", "Mcp", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
    string token = new('A', 64);
    File.WriteAllText(settings, JsonSerializer.Serialize(new { Enabled = true, Port = 47831, Token = token }));
    var connection = new ConnectionSettings { Url = "http://127.0.0.1:47831/mcp", Token = token };
    var options = InstallerOptions.Parse(["--harness", "all", "--yes"]);
    var plans = Installer.Plan(options, paths, ConnectionSettings.Load(options, paths));
    Check(plans.Count == 4 && plans.All(p => p.Changed), "all selects the four harnesses");
    foreach (var plan in plans) Installer.Commit(plan);
    Check(Installer.Plan(options, paths, connection).All(p => !p.Changed), "repeating identical installation does not change any harness");
    Check(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories).Length == 0, "new installs and identical reruns do not create unnecessary backups");
    var claude = JsonNode.Parse(File.ReadAllText(Path.Combine(paths.UserHome, ".claude.json")))!;
    Check(claude["mcpServers"]!["anvil"]!["type"]!.GetValue<string>() == "http", "Claude Code receives an HTTP server entry");
    var cursor = JsonNode.Parse(File.ReadAllText(Path.Combine(paths.UserHome, ".cursor", "mcp.json")))!;
    Check(cursor["mcpServers"]!["anvil"]!["type"] == null && cursor["mcpServers"]!["anvil"]!["headers"]!["Authorization"]!.GetValue<string>() == "Bearer " + token,
        "Cursor receives its documented URL and header format");
    var vscode = JsonNode.Parse(File.ReadAllText(Path.Combine(paths.AppData, "Code", "User", "mcp.json")))!;
    Check(vscode["servers"]!["anvil"]!["type"]!.GetValue<string>() == "http" && vscode["mcpServers"] == null, "VS Code uses servers rather than mcpServers");
    var codex = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(Path.Combine(paths.UserHome, ".codex", "config.toml")))!;
    Check(((TomlTable)((TomlTable)codex["mcp_servers"]!)["anvil"]!)["http_headers"] is TomlTable, "Codex receives a valid TOML authorization table");

    var acl = new FileInfo(plans[0].Path).GetAccessControl();
    Check(acl.AreAccessRulesProtected && acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Count == 1,
        "new credential-bearing configuration files use a restricted Windows ACL");

    string jsonc = """
        {
          // Keep settings, comments, trailing commas and non-ASCII text.
          "label": "Olá 日本語",
          "other": [1, {"nested": true}],
          "mcpServers": {
            "existing": {"url": "https://example.test/mcp"}, // keep this too
          },
        }
        """;
    string jsonPath = Path.Combine(root, "jsonc.json");
    File.WriteAllBytes(jsonPath, [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(jsonc)]);
    var custom = InstallerOptions.Parse(["--harness", "cursor", "--config", jsonPath]);
    var jsonPlan = Installer.Plan(custom, paths, connection).Single();
    string? backup = Installer.Commit(jsonPlan);
    Check(backup != null && File.ReadAllBytes(backup).SequenceEqual([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(jsonc)]),
        "backup preserves exact original bytes including BOM");
    Check(new FileInfo(backup!).GetAccessControl().AreAccessRulesProtected, "credential-bearing backups have restricted permissions");
    string updatedJson = File.ReadAllText(jsonPath);
    Check(updatedJson.Contains("// Keep settings") && updatedJson.Contains("// keep this too") && updatedJson.Contains("Olá 日本語") &&
        updatedJson.Contains("\"existing\": {\"url\": \"https://example.test/mcp\"}"), "JSONC insertion leaves unrelated bytes and Unicode intact");
    Check(Installer.Plan(custom, paths, connection).Single().Changed == false, "JSONC with trailing commas remains valid and idempotent");

    var rotated = new ConnectionSettings { Url = "http://127.0.0.1:47832/mcp", Token = new string('B', 64) };
    Expect(() => Installer.Plan(custom, paths, rotated), "already exists");
    custom.Replace = true;
    Installer.Commit(Installer.Plan(custom, paths, rotated).Single());
    Check(File.ReadAllText(jsonPath).Contains("47832") && File.ReadAllText(jsonPath).Contains("// keep this too"), "explicit replacement updates only the named JSON server");

    string toml = """"
        # Keep this comment and the formatting of unrelated options.
        model = 'example-model'
        instructions = """
        A fake header in a string must survive:
        [mcp_servers.anvil]
        """
        [mcp_servers.other]
        url = 'https://example.test/mcp'
        # Keep this server's comment.
        [mcp_servers."anvil"]
        url = "http://127.0.0.1:11111/mcp"
        bearer_token_env_var = "OLD_TOKEN"
        [mcp_servers.anvil.http_headers]
        Old = "remove me"
        [profiles.work]
        model = 'keep-profile'
        """";
    string tomlPath = Path.Combine(root, "config.toml");
    File.WriteAllText(tomlPath, toml);
    var codexOptions = InstallerOptions.Parse(["--harness", "codex", "--config", tomlPath, "--replace"]);
    Installer.Commit(Installer.Plan(codexOptions, paths, connection).Single());
    string updatedToml = File.ReadAllText(tomlPath);
    Check(updatedToml.Contains("[mcp_servers.anvil]\n\"\"\"") && updatedToml.Contains("# Keep this comment") && updatedToml.Contains("model = 'keep-profile'"),
        "TOML editing preserves multiline strings, comments and unrelated profile formatting");
    Check(!updatedToml.Contains("OLD_TOKEN") && !updatedToml.Contains("remove me") && Installer.Plan(codexOptions, paths, connection).Single().Changed == false,
        "TOML replacement handles quoted table names and removes stale authentication subtables");

    // A TOML inline table cannot be extended by a later table. Refuse instead of altering unrelated structure.
    Expect(() => TomlConfigEditor.Install("mcp_servers = {}\n", "anvil", connection, true), "safely extended");
    Expect(() => JsonConfigEditor.Install("{\"mcpServers\": {}, \"mcpServers\": {}}", ["mcpServers", "anvil"], new(), false), "duplicate");
    File.WriteAllText(jsonPath, "{invalid json");
    Expect(() => Installer.Plan(custom, paths, connection), "invalid JSON");
    Check(File.ReadAllText(jsonPath) == "{invalid json", "invalid configurations are left untouched");

    string project = Path.Combine(root, "project");
    var projectOptions = InstallerOptions.Parse(["--harness", "all", "--scope", "project", "--project", project]);
    var projectConnection = ConnectionSettings.Load(projectOptions, paths);
    var projectPlans = Installer.Plan(projectOptions, paths, projectConnection);
    foreach (var plan in projectPlans) Installer.Commit(plan);
    Check(projectConnection.Token == null && projectConnection.TokenEnvironment == "ANVIL_MCP_TOKEN" &&
        projectPlans.All(p => !File.ReadAllText(p.Path).Contains(token)), "project installs never copy the live token into repository files");
    Check(File.ReadAllText(Path.Combine(project, ".mcp.json")).Contains("${ANVIL_MCP_TOKEN}") &&
        File.ReadAllText(Path.Combine(project, ".cursor", "mcp.json")).Contains("${env:ANVIL_MCP_TOKEN}") &&
        File.ReadAllText(Path.Combine(project, ".codex", "config.toml")).Contains("bearer_token_env_var"), "project references use each harness's environment-variable syntax");

    var customHomes = paths with { CodexHome = Path.Combine(root, "custom-codex") };
    Check(Installer.TargetPath(Harness.Codex, options, customHomes) == Path.Combine(customHomes.CodexHome!, "config.toml"), "CODEX_HOME controls the Codex user target");

    string concurrent = Path.Combine(root, "concurrent.json");
    var concurrentOptions = InstallerOptions.Parse(["--harness", "vscode", "--config", concurrent]);
    var concurrentPlan = Installer.Plan(concurrentOptions, paths, connection).Single();
    File.WriteAllText(concurrent, "{\"changedByAnotherProcess\":true}");
    Expect(() => Installer.Commit(concurrentPlan), "changed after the preview");
    Check(File.ReadAllText(concurrent).Contains("changedByAnotherProcess"), "concurrent changes are not overwritten");

    string dry = Path.Combine(root, "dry", "config.json");
    var output = new StringWriter();
    var error = new StringWriter();
    int result = InstallerProgram.Run(["--harness", "cursor", "--config", dry, "--dry-run"], new StringReader(""), output, error, paths, redirected: true);
    Check(result == 0 && !Directory.Exists(Path.GetDirectoryName(dry)) && output.ToString().Contains("Preview only") && !output.ToString().Contains(token),
        "CLI dry-run prints a redacted preview and creates no folders, files or backups");
    result = InstallerProgram.Run(["--harness", "cursor", "--config", dry], new StringReader(""), output, error, paths, redirected: true);
    Check(result == 1 && !File.Exists(dry), "unattended writes require --yes");
    result = InstallerProgram.Run(["--harness", "cursor", "--config", dry, "--yes"], new StringReader(""), output, error, paths, redirected: true);
    Check(result == 0 && File.Exists(dry) && !output.ToString().Contains(token) && !error.ToString().Contains(token), "CLI installation writes the chosen config without printing credentials");

    var menuPaths = new InstallerPaths(Path.Combine(root, "menu-user"), Path.Combine(root, "menu-appdata"), paths.LocalAppData);
    output.GetStringBuilder().Clear();
    result = InstallerProgram.Run([], new StringReader("1,2\nuser\ny\n"), output, error, menuPaths);
    Check(result == 0 && File.Exists(Path.Combine(menuPaths.UserHome, ".claude.json")) && File.Exists(Path.Combine(menuPaths.UserHome, ".cursor", "mcp.json")) &&
        !File.Exists(Path.Combine(menuPaths.UserHome, ".codex", "config.toml")), "interactive menu installs only selected harnesses");

    var cancelledPaths = new InstallerPaths(Path.Combine(root, "cancel-user"), paths.AppData, paths.LocalAppData);
    result = InstallerProgram.Run([], new StringReader("3\nuser\nn\n"), output, error, cancelledPaths);
    Check(result == 0 && !Directory.Exists(cancelledPaths.UserHome), "interactive cancellation leaves settings untouched");
    var failedPaths = new InstallerPaths(Path.Combine(root, "fail-user"), paths.AppData, paths.LocalAppData);
    Directory.CreateDirectory(Path.Combine(failedPaths.UserHome, ".cursor"));
    File.WriteAllText(Path.Combine(failedPaths.UserHome, ".cursor", "mcp.json"), "{invalid}");
    result = InstallerProgram.Run(["--harness", "claudecode,cursor", "--yes"], new StringReader(""), output, error, failedPaths, redirected: true);
    Check(result == 1 && !File.Exists(Path.Combine(failedPaths.UserHome, ".claude.json")), "all harness plans validate before the first file is changed");
    var projectMenu = new InstallerPaths(Path.Combine(root, "project-menu-user"), paths.AppData, paths.LocalAppData);
    string menuProject = Path.Combine(root, "project-menu");
    result = InstallerProgram.Run(["--scope", "project", "--project", menuProject], new StringReader("1\n\ny\n"), output, error, projectMenu);
    Check(result == 0 && File.Exists(Path.Combine(menuProject, ".mcp.json")) && !Directory.Exists(projectMenu.UserHome),
        "interactive defaults preserve an explicit project scope");
    Check(!ConnectionSettings.Load(InstallerOptions.Parse(["--harness", "cursor", "--url", "http://127.0.0.1:47831/mcp", "--token-env", "TEST_TOKEN"]), paths).ToString().Contains(token),
        "connection descriptions never contain credentials");
    Expect(() => ConnectionSettings.Load(InstallerOptions.Parse(["--harness", "cursor", "--url", "https://external.test/mcp", "--token-env", "TEST_TOKEN"]), paths), "--url must");
    Expect(() => InstallerOptions.Parse(["--harness", "all", "--config", dry]), "exactly one");
    Expect(() => InstallerOptions.Parse(["--name", "invalid.name"]), "--name must");
    Expect(() => InstallerOptions.Parse(["--token-env", "X\nY"]), "--token-env must");
    Console.WriteLine($"All {passed} MCP installer checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
void Expect(Action action, string expected)
{
    try { action(); }
    catch (InstallerException ex) when (ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
    {
        Check(true, expected + " is reported without changing configuration");
        return;
    }
    throw new Exception("Expected: " + expected);
}
