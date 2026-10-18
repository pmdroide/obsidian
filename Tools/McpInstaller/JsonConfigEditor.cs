using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Anvil.McpInstaller;

/// <summary>Edits a single JSON value/insertion while retaining unrelated bytes, JSONC comments and trailing commas.</summary>
public static class JsonConfigEditor
{
    private sealed class ValueSpan(int start, JsonTokenType kind)
    {
        public int Start = start, End, Close;
        public JsonTokenType Kind = kind;
        public Dictionary<string, ValueSpan> Properties = new(StringComparer.Ordinal);
    }

    public static string Install(string source, IReadOnlyList<string> path, JsonObject entry, bool replace)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read()) throw new InstallerException("Configuration is empty; expected a JSON object.");
        var root = ReadValue(ref reader);
        if (root.Kind != JsonTokenType.StartObject || reader.Read()) throw new InstallerException("Configuration must contain one JSON object.");
        var container = root;
        for (int i = 0; i < path.Count; i++)
        {
            if (!container.Properties.TryGetValue(path[i], out var child))
            {
                JsonNode nested = entry.DeepClone();
                for (int j = path.Count - 1; j > i; j--) nested = new JsonObject { [path[j]] = nested };
                return Insert(utf8, container, path[i], nested, i);
            }
            if (i == path.Count - 1)
            {
                var previous = JsonNode.Parse(utf8.AsSpan(child.Start, child.End - child.Start), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (JsonNode.DeepEquals(previous, entry)) return source;
                if (!replace) throw new InstallerException("The selected server name already exists with different settings. Use --replace to replace only that entry, or choose --name.");
                return Patch(utf8, child.Start, child.End, Render(entry, i));
            }
            if (child.Kind != JsonTokenType.StartObject) throw new InstallerException("A required configuration section is not a JSON object. No files were changed.");
            container = child;
        }
        throw new InstallerException("Empty JSON configuration path.");
    }

    private static ValueSpan ReadValue(ref Utf8JsonReader reader)
    {
        var node = new ValueSpan(checked((int)reader.TokenStartIndex), reader.TokenType);
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new InstallerException("Invalid JSON object.");
                string name = reader.GetString()!;
                if (!reader.Read()) throw new InstallerException("Incomplete JSON value.");
                if (!node.Properties.TryAdd(name, ReadValue(ref reader))) throw new InstallerException("Configuration has duplicate JSON keys. Resolve them before installing.");
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new InstallerException("Incomplete JSON object.");
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) ReadValue(ref reader);
            if (reader.TokenType != JsonTokenType.EndArray) throw new InstallerException("Incomplete JSON array.");
        }
        node.Close = checked((int)reader.TokenStartIndex);
        node.End = checked((int)reader.BytesConsumed);
        return node;
    }

    private static string Insert(byte[] source, ValueSpan container, string key, JsonNode value, int depth)
    {
        string newline = Encoding.UTF8.GetString(source).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string insertion = newline + new string(' ', (depth + 1) * 2) + JsonSerializer.Serialize(key) + ": " + Render(value, depth) + newline + new string(' ', depth * 2);
        if (container.Properties.Count == 0) return Patch(source, container.Close, container.Close, insertion);
        var last = container.Properties.Last().Value;
        string tail = Encoding.UTF8.GetString(source.AsSpan(last.End, container.Close - last.End));
        string punctuation = Regex.Replace(tail, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
        string withProperty = Patch(source, container.Close, container.Close, insertion);
        if (punctuation.Contains(',')) return withProperty;
        return Patch(Encoding.UTF8.GetBytes(withProperty), last.End, last.End, ",");
    }

    private static string Render(JsonNode node, int depth) => node.ToJsonString(new() { WriteIndented = true }).Replace("\n", "\n" + new string(' ', (depth + 1) * 2));
    private static string Patch(byte[] source, int start, int end, string value) =>
        Encoding.UTF8.GetString(source.AsSpan(0, start)) + value + Encoding.UTF8.GetString(source.AsSpan(end));
}
