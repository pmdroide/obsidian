using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine.Editor;
using Engine.Recources;
using BasicEntityRecord = Engine.Logic.SceneSerialization.BasicEntityRecord;

namespace Engine.Logic
{
    /// <summary>
    /// A saved gameobject (model, material, role, components and their settings) that scenes and scripts place
    /// copies of, like a Unity prefab. Stored as a <c>.prefab</c> JSON file in Content (by default
    /// <c>Content/Prefabs</c>) holding one gameobject record in the scene format. Instances keep a link to it
    /// (<see cref="Entities.BasicEntity.PrefabPath"/>) and follow its later edits; see <see cref="PrefabUtility"/>.
    ///
    /// Load one with <see cref="Load"/> ("Crate" or "Prefabs/Crate.prefab"); scripts place it with
    /// <c>ScriptBehaviour.Instantiate</c> or take it as an Inspector field (<c>public Prefab Bullet;</c>).
    /// File access is plain I/O, safe from the editor (UI) thread too.
    /// </summary>
    public sealed class Prefab
    {
        public const string Extension = ".prefab";
        /// <summary>Content folder new prefabs go to.</summary>
        public const string DefaultFolder = "Prefabs";
        public const int CurrentVersion = 1;

        /// <summary>Content-relative path of the file, '/'-separated ("Prefabs/Crate.prefab").</summary>
        public string Path { get; }

        /// <summary>The file name without extension ("Crate"); instances are named after it.</summary>
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        /// <summary>Model key (Assets field or imported model) the prefab's gameobject draws.</summary>
        public string ModelKey => Record.ModelKey;

        // Parsed once per file version and shared: never modify it, PrefabUtility clones it.
        internal BasicEntityRecord Record { get; }

        private Prefab(string path, BasicEntityRecord record)
        {
            Path = path;
            Record = record;
        }

        public override string ToString() => Name;

        /// <summary>File contents: a version and the gameobject.</summary>
        public sealed class Document
        {
            public int Version { get; set; } = CurrentVersion;
            public BasicEntityRecord GameObject { get; set; }
        }

        // ------------------------------------------------------------------
        //  Files
        // ------------------------------------------------------------------

        /// <summary>Tests point this at a temporary folder instead of the source Content folder.</summary>
        internal static string SourceRootOverride;

        /// <summary>Content folder prefabs are written to: the source <c>Engine/Content</c> of a dev checkout.</summary>
        public static string SourceRoot => SourceRootOverride ?? GameInfo.SourceContentRoot;

        // The source copy when it exists (dev runs, Anvil), else the one the build placed next to the exe.
        private static string FullPath(string relative)
        {
            string rel = relative.Replace('/', System.IO.Path.DirectorySeparatorChar);
            if (SourceRootOverride != null) return System.IO.Path.Combine(SourceRootOverride, rel);
            return GameInfo.ResolveContentFile(relative);
        }

        /// <summary>"Prefabs\\Crate.prefab" -> "Prefabs/Crate.prefab"; null for an empty path.</summary>
        public static string Normalize(string path) =>
            string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/').TrimStart('/');

        /// <summary>True when two prefab paths name the same file.</summary>
        public static bool SamePath(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Content-relative path of a prefab named by <paramref name="nameOrPath"/>: a path ("Prefabs/Crate.prefab",
        /// the extension and the Prefabs folder may be left out) or a file name anywhere in Content ("Crate").
        /// Null when there is none.
        /// </summary>
        public static string Resolve(string nameOrPath)
        {
            string key = Normalize(nameOrPath);
            if (key == null) return null;
            // Scripts instantiate by name every frame: remember what a name resolved to while that file exists.
            lock (CacheLock)
                if (Resolved.TryGetValue(key, out string known) && File.Exists(FullPath(known))) return known;

            string found = null;
            var candidates = new List<string> { key };
            if (!key.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) candidates.Add(key + Extension);
            foreach (string c in candidates.ToArray()) candidates.Add(DefaultFolder + "/" + c);
            foreach (string c in candidates)
            {
                if (!c.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || c.Split('/').Contains("..")) continue;
                string full = FullPath(c);
                if (File.Exists(full)) { found = OnDiskCasing(full, c); break; }
            }
            if (found == null)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(key);
                found = List().FirstOrDefault(p => string.Equals(System.IO.Path.GetFileNameWithoutExtension(p), name,
                    StringComparison.OrdinalIgnoreCase));
            }
            if (found != null) lock (CacheLock) Resolved[key] = found;
            return found;
        }

        // "prefabs/crate.prefab" -> "Prefabs/Crate.prefab" as the folders and file are named on disk, so instances
        // link to (and are named after) the real file whatever casing a script used.
        private static string OnDiskCasing(string full, string relative)
        {
            string[] parts = relative.Split('/');
            string current = full;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                string parent = System.IO.Path.GetDirectoryName(current);
                if (parent == null) break;
                try
                {
                    string match = Directory.EnumerateFileSystemEntries(parent, parts[i]).FirstOrDefault();
                    if (match != null) parts[i] = System.IO.Path.GetFileName(match);
                }
                catch (IOException) { /* keep the given casing */ }
                current = parent;
            }
            return string.Join("/", parts);
        }

        /// <summary>Every prefab in Content (Content-relative paths, sorted), Prefabs folder first.</summary>
        public static IReadOnlyList<string> List()
        {
            string root = SourceRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                root = System.IO.Path.Combine(AppContext.BaseDirectory, "Content");
            if (!Directory.Exists(root)) return Array.Empty<string>();
            var found = new List<string>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories))
                {
                    string rel = System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');
                    // mgcb output and build state are not assets.
                    string top = rel.Split('/')[0];
                    if (top.Equals("bin", StringComparison.OrdinalIgnoreCase) || top.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add(rel);
                }
            }
            catch (Exception ex) { EditorBridge.Log("Prefab.List: " + ex.Message); }
            return found
                .OrderBy(p => p.StartsWith(DefaultFolder + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, (DateTime Written, Prefab Prefab)> Cache = new(StringComparer.OrdinalIgnoreCase);
        // Name or path asked for -> the Content-relative file it resolved to.
        private static readonly Dictionary<string, string> Resolved = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The prefab named by <paramref name="nameOrPath"/> (see <see cref="Resolve"/>), or null when it doesn't exist
        /// or can't be read (logged). Re-read when the file changes.
        /// </summary>
        public static Prefab Load(string nameOrPath)
        {
            string rel = Resolve(nameOrPath);
            if (rel == null) return null;
            string full = FullPath(rel);
            try
            {
                DateTime written = File.GetLastWriteTimeUtc(full);
                lock (CacheLock)
                    if (Cache.TryGetValue(full, out var cached) && cached.Written == written) return cached.Prefab;

                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(full), SceneSerialization.JsonOptions);
                if (doc?.GameObject == null) throw new InvalidDataException("no GameObject");
                if (doc.Version < 1 || doc.Version > CurrentVersion)
                    throw new InvalidDataException($"unsupported version {doc.Version} (this build reads {CurrentVersion})");
                doc.GameObject.Prefab = null;
                doc.GameObject.PrefabOverrides = null;
                var prefab = new Prefab(rel, doc.GameObject);
                lock (CacheLock) Cache[full] = (written, prefab);
                return prefab;
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"Prefab: couldn't read '{rel}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Writes a prefab file to the source Content folder (atomically) and returns the loaded prefab.</summary>
        internal static Prefab Write(string relativePath, BasicEntityRecord record)
        {
            string rel = Normalize(relativePath);
            if (rel == null || !rel.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"'{relativePath}' is not a {Extension} path", nameof(relativePath));
            if (rel.Split('/').Contains("..")) throw new ArgumentException("A prefab must stay inside Content.", nameof(relativePath));
            string full = System.IO.Path.Combine(SourceRoot, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            record.Prefab = null;
            record.PrefabOverrides = null;
            // Attachment IDs belong to each placed copy, not to the prefab.
            foreach (var component in record.Components ?? new()) component.InstanceId = Guid.Empty;
            string json = JsonSerializer.Serialize(new Document { GameObject = record }, SceneSerialization.JsonOptions);
            string tmp = full + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, full, overwrite: true);
            lock (CacheLock) Cache.Remove(full);
            return Load(rel);
        }

        /// <summary>
        /// A free Content-relative path for a new prefab called <paramref name="name"/> in <paramref name="folder"/>
        /// ("Prefabs" when null): "Prefabs/Crate.prefab", else "Prefabs/Crate_2.prefab", ...
        /// </summary>
        public static string UniquePath(string folder, string name)
        {
            folder = Normalize(folder)?.TrimEnd('/') ?? DefaultFolder;
            string stem = SafeFileName(name);
            string rel = $"{folder}/{stem}{Extension}";
            for (int i = 2; File.Exists(System.IO.Path.Combine(SourceRoot, rel.Replace('/', System.IO.Path.DirectorySeparatorChar))); i++)
                rel = $"{folder}/{stem}_{i}{Extension}";
            return rel;
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "GameObject";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            string safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
            return safe.Length == 0 ? "GameObject" : safe;
        }
    }
}
