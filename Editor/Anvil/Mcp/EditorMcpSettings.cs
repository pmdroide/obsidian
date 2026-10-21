using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Anvil.Mcp;

public sealed record EditorMcpSettings
{
    public bool Enabled { get; init; }
    public bool ReadOnly { get; init; } = true;
    public int Port { get; init; } = 47831;
    public string Token { get; init; } = NewToken();
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

/// <summary>User-local settings and token. Never stored in source Content or shipped with games.</summary>
public sealed class EditorMcpSettingsStore(string? directory = null)
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Anvil", "Mcp");
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public EditorMcpSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var settings = JsonSerializer.Deserialize<EditorMcpSettings>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("MCP settings are empty.");
        if (settings.Port is < 1024 or > 65535 || settings.Token == null || settings.Token.Length < 32)
            throw new InvalidDataException("Invalid MCP port or token.");
        return settings;
    }

    public void Save(EditorMcpSettings settings)
    {
        // Restrict inheritance before writing any secret, including temporary files.
        var folder = Directory.CreateDirectory(DirectoryPath);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("Cannot identify this Windows user.");
        var permissions = new DirectorySecurity();
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        permissions.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        folder.SetAccessControl(permissions);
        string temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
