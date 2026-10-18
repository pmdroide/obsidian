using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine.Editor;
using Engine.Recources;

namespace Engine.Logic
{
    /// <summary>
    /// Post processing profile assets: <c>.post</c> JSON files in Content (default <c>Content/PostProfiles</c>),
    /// <c>{ Version, Post }</c> = a <see cref="PostProcessSettings"/>. Post Process Volumes use one through their
    /// Profile Path, like Half-Life: Alyx's <c>lighting/postprocessing/*.vpost</c>. Shipped by Engine.csproj.
    /// </summary>
    public static class PostProfile
    {
        public const string Extension = ".post";
        public const string DefaultFolder = "PostProfiles";
        public const int CurrentVersion = 1;

        private sealed class Document
        {
            public int Version { get; set; } = CurrentVersion;
            public PostProcessSettings Post { get; set; }
        }

        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, (DateTime Written, PostProcessSettings Post)> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>"PostProfiles\\Dead.post" -> "PostProfiles/Dead.post"; adds the folder and extension to a bare name.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Replace('\\', '/').TrimStart('/');
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) path += Extension;
            if (!path.Contains('/')) path = DefaultFolder + "/" + path;
            return path;
        }

        /// <summary>
        /// The profile at a Content-relative path (or a name in PostProfiles), re-read when the file changes;
        /// null when it doesn't exist or can't be read (logged).
        /// </summary>
        public static PostProcessSettings Load(string path)
        {
            string rel = Normalize(path);
            if (rel == null || rel.Split('/').Contains("..")) return null;
            string full = MaterialAsset.SourceRootOverride != null
                ? Path.Combine(MaterialAsset.SourceRootOverride, rel)
                : GameInfo.ResolveContentFile(rel);
            try
            {
                if (!File.Exists(full)) return null;
                DateTime written = File.GetLastWriteTimeUtc(full);
                lock (CacheLock)
                    if (Cache.TryGetValue(full, out var cached) && cached.Written == written) return cached.Post;
                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(full), SceneSerialization.JsonOptions);
                if (doc?.Post == null) throw new InvalidDataException("no Post");
                if (doc.Version < 1 || doc.Version > CurrentVersion)
                    throw new InvalidDataException($"unsupported version {doc.Version} (this build reads {CurrentVersion})");
                doc.Post.Normalize();
                lock (CacheLock) Cache[full] = (written, doc.Post);
                return doc.Post;
            }
            catch (Exception ex)
            {
                if (ex is IOException) return null;
                EditorBridge.Log($"Post profile: couldn't read '{rel}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Writes a profile into the source Content folder and returns its Content-relative path.</summary>
        public static string Write(string path, PostProcessSettings settings)
        {
            string rel = Normalize(path) ?? throw new ArgumentException("empty path", nameof(path));
            if (rel.Split('/').Contains("..")) throw new ArgumentException("A profile must stay inside Content.", nameof(path));
            string full = Path.Combine(MaterialAsset.SourceRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string json = JsonSerializer.Serialize(new Document { Post = settings ?? new PostProcessSettings() }, SceneSerialization.JsonOptions);
            lock (CacheLock)
            {
                File.WriteAllText(full + ".tmp", json);
                File.Move(full + ".tmp", full, overwrite: true);
                Cache.Remove(full);
            }
            return rel;
        }
    }
}
