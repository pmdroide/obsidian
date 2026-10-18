using System.Text.Json;

namespace Anvil.McpInstaller;

public sealed class ConnectionSettings
{
    public required string Url { get; init; }
    public string? Token { get; init; }
    public string? TokenEnvironment { get; init; }
    public bool EditorDisabled { get; init; }

    // Never include credential-bearing fields in record-generated ToString() or console output.
    public override string ToString() => Url;

    public static ConnectionSettings Load(InstallerOptions options, InstallerPaths paths)
    {
        string? variable = options.TokenEnvironment ?? (options.Scope == InstallScope.Project ? "ANVIL_MCP_TOKEN" : null);
        string? token = null;
        int? port = null;
        bool disabled = false;
        if (options.Settings != null || options.Url == null || variable == null)
        {
            string settingsPath = Path.GetFullPath(options.Settings ?? Path.Combine(paths.LocalAppData, "Anvil", "Mcp", "settings.json"));
            if (!File.Exists(settingsPath))
                throw new InstallerException("Anvil MCP settings were not found. Open Anvil > Window > MCP and press Apply, or use --settings PATH (or --url with --token-env).");
            try
            {
                if (new FileInfo(settingsPath).Length > 64 * 1024) throw new InstallerException("Anvil settings file is too large.");
                using var json = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (json.RootElement.TryGetProperty("Port", out var portValue) && portValue.TryGetInt32(out int parsedPort)) port = parsedPort;
                if (json.RootElement.TryGetProperty("Token", out var tokenValue) && tokenValue.ValueKind == JsonValueKind.String) token = tokenValue.GetString();
                disabled = !json.RootElement.TryGetProperty("Enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True;
            }
            catch (JsonException) { throw new InstallerException("Anvil settings contain invalid JSON. Re-save them from the editor MCP panel."); }
            if (options.Url == null && port is not (>= 1024 and <= 65535)) throw new InstallerException("Anvil MCP settings contain an invalid port.");
        }
        string url = options.Url ?? $"http://127.0.0.1:{port}/mcp";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            (uri.Host != "127.0.0.1" && uri.Host != "localhost") || uri.Port is < 1024 or > 65535 ||
            uri.AbsolutePath != "/mcp" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InstallerException("--url must be http://127.0.0.1:PORT/mcp or http://localhost:PORT/mcp (port 1024-65535).");
        if (variable == null && (token == null || token.Length < 32 || token.Any(char.IsControl)))
            throw new InstallerException("Anvil MCP settings contain an invalid token. Rotate it and press Apply in the editor.");
        return new() { Url = uri.AbsoluteUri, Token = variable == null ? token : null, TokenEnvironment = variable, EditorDisabled = disabled };
    }
}
