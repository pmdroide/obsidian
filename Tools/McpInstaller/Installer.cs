using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anvil.McpInstaller;

public sealed class InstallPlan(Harness harness, string path, byte[]? original, byte[] updated)
{
    public Harness Harness { get; } = harness;
    public string Path { get; } = path;
    internal byte[]? Original { get; } = original;
    internal byte[] Updated { get; } = updated;
    public bool Changed => Original == null || !Original.AsSpan().SequenceEqual(Updated);
    public bool Replacing => Original != null;
    public override string ToString() => $"{Harness}: {Path} ({(!Changed ? "already configured" : Original == null ? "create" : "update")})";
}

public static class Installer
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static IReadOnlyList<InstallPlan> Plan(InstallerOptions options, InstallerPaths paths, ConnectionSettings connection)
    {
        var plans = options.Harnesses.Select(h => PlanOne(h, options, paths, connection)).ToArray();
        if (plans.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Length)
            throw new InstallerException("Two selected harnesses resolve to the same configuration file. Install them separately.");
        return plans;
    }

    public static string TargetPath(Harness harness, InstallerOptions options, InstallerPaths paths)
    {
        if (options.Config != null) return Path.GetFullPath(options.Config);
        if (options.Scope == InstallScope.Project) return Path.GetFullPath(Path.Combine(options.Project, harness switch
        {
            Harness.ClaudeCode => ".mcp.json", Harness.Cursor => ".cursor/mcp.json",
            Harness.Codex => ".codex/config.toml", Harness.VSCode => ".vscode/mcp.json",
            _ => throw new InstallerException("Unknown harness."),
        }));
        if (harness == Harness.ClaudeCode && !string.IsNullOrWhiteSpace(paths.ClaudeConfigDirectory))
            throw new InstallerException("CLAUDE_CONFIG_DIR is customized. Select only claudecode and provide --config with its user JSON file.");
        return Path.GetFullPath(harness switch
        {
            Harness.ClaudeCode => Path.Combine(paths.UserHome, ".claude.json"),
            Harness.Cursor => Path.Combine(paths.UserHome, ".cursor", "mcp.json"),
            Harness.Codex => Path.Combine(string.IsNullOrWhiteSpace(paths.CodexHome) ? Path.Combine(paths.UserHome, ".codex") : paths.CodexHome, "config.toml"),
            Harness.VSCode => Path.Combine(paths.AppData, "Code", "User", "mcp.json"),
            _ => throw new InstallerException("Unknown harness."),
        });
    }

    private static InstallPlan PlanOne(Harness harness, InstallerOptions options, InstallerPaths paths, ConnectionSettings connection)
    {
        string path = TargetPath(harness, options, paths);
        if (Directory.Exists(path)) throw new InstallerException("A configuration target is a directory; use --config with a file path.");
        byte[]? bytes = ReadConfig(path);
        bool bom = bytes?.AsSpan().StartsWith(Encoding.UTF8.Preamble) == true;
        string source;
        try { source = bytes == null ? (harness == Harness.Codex ? "" : "{}\n") : Utf8.GetString(bytes.AsSpan(bom ? 3 : 0)); }
        catch (DecoderFallbackException) { throw new InstallerException("A client configuration is not UTF-8. Convert its encoding before installing."); }
        string updated;
        try
        {
            if (harness == Harness.Codex) updated = TomlConfigEditor.Install(source, options.Name, connection, options.Replace);
            else
            {
                string authorization = connection.TokenEnvironment == null ? "Bearer " + connection.Token
                    : harness == Harness.ClaudeCode ? "Bearer ${" + connection.TokenEnvironment + "}"
                    : "Bearer ${env:" + connection.TokenEnvironment + "}";
                var entry = new JsonObject { ["url"] = connection.Url, ["headers"] = new JsonObject { ["Authorization"] = authorization } };
                if (harness != Harness.Cursor) entry["type"] = "http";
                string section = harness == Harness.VSCode ? "servers" : "mcpServers";
                updated = JsonConfigEditor.Install(source, [section, options.Name], entry, options.Replace);
                _ = JsonNode.Parse(updated, documentOptions: JsonOptions); // Validate the complete result before any writes.
            }
        }
        catch (JsonException) { throw new InstallerException("A client configuration contains invalid JSON. Fix it before installing; no files were changed."); }
        catch (Tomlyn.TomlException) { throw new InstallerException("Codex configuration contains invalid or unsupported TOML. No files were changed."); }
        byte[] body = Utf8.GetBytes(updated);
        return new(harness, path, bytes, bom ? [.. Encoding.UTF8.Preamble, .. body] : body);
    }

    private static byte[]? ReadConfig(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InstallerException("A configuration file is a symbolic link. Use --config with the real file path to preserve the link.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InstallerException("A client configuration exceeds the 4 MiB installer limit.");
        return File.ReadAllBytes(path);
    }

    /// <summary>All plans are parsed/validated first. Commits are atomic per file; backups retain original bytes.</summary>
    public static string? Commit(InstallPlan plan)
    {
        if (!plan.Changed) return null;
        EnsureUnchanged(plan);
        string directory = Path.GetDirectoryName(plan.Path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".anvil-mcp-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? backup = null;
        try
        {
            WritePrivate(temporary, plan.Updated);
            if (plan.Original != null)
            {
                backup = plan.Path + ".anvil-mcp." + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "." + Guid.NewGuid().ToString("N") + ".bak";
                WritePrivate(backup, plan.Original);
                // Preserve the existing file's permissions; newly created files/backups remain user-only.
                new FileInfo(temporary).SetAccessControl(new FileInfo(plan.Path).GetAccessControl());
            }
            EnsureUnchanged(plan);
            File.Move(temporary, plan.Path, overwrite: plan.Original != null);
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureUnchanged(InstallPlan plan)
    {
        var current = ReadConfig(plan.Path);
        if ((current == null) != (plan.Original == null) || (current != null && !current.AsSpan().SequenceEqual(plan.Original)))
            throw new InstallerException("A configuration changed after the preview. Run the installer again; the changed file was not overwritten.");
    }

    private static void WritePrivate(string path, byte[] bytes)
    {
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InstallerException("Cannot identify the Windows user for configuration permissions.");
        var permissions = new FileSecurity();
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        permissions.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Read | FileSystemRights.Write,
            FileShare.None, 4096, FileOptions.None, permissions);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
