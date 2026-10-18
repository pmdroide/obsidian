using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Anvil.McpInstaller;

public static class TomlConfigEditor
{
    public static string Install(string source, string name, ConnectionSettings connection, bool replace)
    {
        var document = SyntaxParser.Parse(source);
        if (document.HasErrors) throw new InstallerException("Codex configuration contains invalid TOML. Resolve it before installing.");
        var before = TomlSerializer.Deserialize<TomlTable>(source)!;
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string entry = $"[mcp_servers.{name}]{newline}url = {Quote(connection.Url)}{newline}" +
            (connection.TokenEnvironment != null
                ? $"bearer_token_env_var = {Quote(connection.TokenEnvironment)}{newline}"
                : $"http_headers = {{ Authorization = {Quote("Bearer " + connection.Token)} }}{newline}");
        var desired = TomlSerializer.Deserialize<TomlTable>(entry)!;
        object? existing = Server(before, name), expected = Server(desired, name);
        if (existing != null && Equivalent(existing, expected)) return source;
        if (existing != null && !replace)
            throw new InstallerException("The selected Codex server name already exists with different settings. Use --replace or choose --name.");

        // Syntax-tree removal retains other tables, key values, comments and strings. A regex cannot
        // distinguish a real table header from one inside a multiline TOML string.
        bool removed = false;
        foreach (var table in document.Tables.ToArray())
        {
            var keys = Keys(table.Name!).ToArray();
            if (keys.Length >= 2 && keys[0] == "mcp_servers" && keys[1] == name)
            {
                document.Tables.RemoveChild(table);
                removed = true;
            }
        }
        if (existing != null && !removed)
            throw new InstallerException("This Codex entry uses compact/dotted TOML syntax. Convert it to a [mcp_servers.NAME] table or use another --name before installing.");
        string result = document.ToString();
        if (result.Length > 0 && !result.EndsWith('\n')) result += newline;
        result += newline + entry;
        var check = SyntaxParser.Parse(result);
        if (check.HasErrors)
            throw new InstallerException("Codex configuration cannot be safely extended in its current TOML layout. Use standard [mcp_servers.NAME] tables; no file was changed.");
        var after = TomlSerializer.Deserialize<TomlTable>(result)!;
        if (!Equivalent(Server(after, name), expected)) throw new InstallerException("Generated Codex entry did not validate.");
        RemoveServer(before, name);
        RemoveServer(after, name);
        if (!Equivalent(before, after)) throw new InstallerException("The TOML edit would change other Codex settings. No file was changed.");
        return result;
    }

    private static IEnumerable<string> Keys(KeySyntax key)
    {
        static string Value(BareKeyOrStringValueSyntax? part) => part switch
        {
            StringValueSyntax text => text.Value!,
            BareKeySyntax bare => bare.Key!.ToString().Trim(),
            _ => throw new InstallerException("Unsupported TOML key syntax."),
        };
        yield return Value(key.Key);
        foreach (var dotted in key.DotKeys) yield return Value(dotted.Key);
    }

    private static object? Server(TomlTable model, string name)
    {
        if (!model.TryGetValue("mcp_servers", out var value)) return null;
        if (value is not TomlTable servers) throw new InstallerException("Codex mcp_servers must be a TOML table.");
        return servers.TryGetValue(name, out var server) ? server : null;
    }
    private static void RemoveServer(TomlTable model, string name)
    {
        if (model.TryGetValue("mcp_servers", out var value) && value is TomlTable servers)
        {
            servers.Remove(name);
            if (servers.Count == 0) model.Remove("mcp_servers");
        }
    }
    private static bool Equivalent(object? a, object? b) => System.Text.Json.Nodes.JsonNode.DeepEquals(
        JsonSerializer.SerializeToNode(a), JsonSerializer.SerializeToNode(b));
    private static string Quote(string text) => JsonSerializer.Serialize(text);
}
