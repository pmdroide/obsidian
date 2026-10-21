using System;
using System.IO;
using System.Text.Json;
using Engine.Editor;

namespace Engine.Recources
{
    /// <summary>
    /// Where the texture generator lives, read from <c>Tools/TexGen/texgen.json</c> (see texgen.example.json). A missing
    /// or damaged file gives the defaults. Relative paths are relative to the repository root.
    /// </summary>
    public sealed class TexGenSettings
    {
        public string RepoRoot { get; init; }
        public string PythonExe { get; init; }
        public string TexGenRoot { get; init; }
        public string CacheRoot { get; init; }
        public string DefaultBackend { get; init; } = "fake";
        public int TimeoutSeconds { get; init; } = 600;

        /// <summary>The checkout containing the source Engine/Content (Anvil and the engine run from bin folders inside it).</summary>
        public static string LocateRepoRoot()
        {
            string content = AssetImporter.LocateEngineContentRoot();
            return Path.GetFullPath(Path.Combine(content, "..", ".."));
        }

        public static TexGenSettings Load(string repoRoot = null)
        {
            repoRoot = Path.GetFullPath(repoRoot ?? LocateRepoRoot());
            string texgenRoot = Path.Combine(repoRoot, "Tools", "TexGen");
            string python = Path.Combine(texgenRoot, ".venv", "Scripts", "python.exe");
            string cache = Path.Combine(texgenRoot, "cache");
            string backend = "fake";
            int timeout = 600;

            string file = Path.Combine(texgenRoot, "texgen.json");
            try
            {
                if (File.Exists(file))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                    var root = doc.RootElement;
                    python = ReadPath(root, "pythonExe", repoRoot) ?? python;
                    texgenRoot = ReadPath(root, "texgenRoot", repoRoot) ?? texgenRoot;
                    cache = ReadPath(root, "cacheRoot", repoRoot) ?? cache;
                    if (root.TryGetProperty("defaultBackend", out var b) && b.ValueKind == JsonValueKind.String && b.GetString().Length > 0)
                        backend = b.GetString();
                    if (root.TryGetProperty("timeoutSeconds", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out int s) && s > 0)
                        timeout = s;
                }
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is InvalidOperationException)
            {
                EditorBridge.Log("TexGenSettings: ignoring unreadable texgen.json: " + e.Message);
            }
            return new TexGenSettings
            {
                RepoRoot = repoRoot, PythonExe = python, TexGenRoot = texgenRoot, CacheRoot = cache,
                DefaultBackend = backend, TimeoutSeconds = timeout,
            };
        }

        private static string ReadPath(JsonElement root, string name, string repoRoot)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
                return null;
            string path = v.GetString();
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path));
        }
    }
}
