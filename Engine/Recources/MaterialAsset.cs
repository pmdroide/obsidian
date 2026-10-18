using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Recources
{
    /// <summary>
    /// A reusable surface (colour, roughness, metallic, emission, shader settings and the Base Color / Normal /
    /// Roughness / Metallic / Mask / Displacement textures) saved as a <c>.mat</c> JSON file in Content (by default
    /// <c>Content/Materials</c>), like a Unity material. Gameobjects use one through their Material component's
    /// <see cref="MaterialComponent.MaterialPath"/>: the component keeps a copy of the asset's values (saved in the
    /// scene as a fallback) and <see cref="SyncScene"/> refreshes every linked component when the file changes, so
    /// editing the asset (Anvil Inspector or the file itself) updates every gameobject that uses it.
    ///
    /// Scripts assign one with <see cref="Assign"/> ("Brick" or "Materials/Brick.mat"). File access is plain I/O,
    /// safe from the editor (UI) thread too.
    /// </summary>
    public sealed class MaterialAsset
    {
        public const string Extension = ".mat";
        /// <summary>Content folder new materials go to.</summary>
        public const string DefaultFolder = "Materials";
        public const int CurrentVersion = 1;

        /// <summary>Content-relative path of the file, '/'-separated ("Materials/Brick.mat").</summary>
        public string Path { get; }

        /// <summary>The file name without extension ("Brick").</summary>
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        // Parsed once per file version and shared: never modify it, CopyTo copies its values.
        internal MaterialComponent Settings { get; }

        private MaterialAsset(string path, MaterialComponent settings)
        {
            Path = path;
            Settings = settings;
        }

        public override string ToString() => Name;

        /// <summary>File contents: a version and the surface (the Material component's settings).</summary>
        public sealed class Document
        {
            public int Version { get; set; } = CurrentVersion;
            public MaterialComponent Material { get; set; }
        }

        /// <summary>Copies the asset's surface into <paramref name="component"/> (not its link or Enabled).</summary>
        public void CopyTo(MaterialComponent component) => CopySurface(Settings, component);

        /// <summary>A new Material component with this asset's surface (unlinked).</summary>
        public MaterialComponent CreateSettings()
        {
            var copy = new MaterialComponent();
            CopyTo(copy);
            return copy;
        }

        // ------------------------------------------------------------------
        //  Surface values
        // ------------------------------------------------------------------

        // Every persisted setting of the component except the link and Enabled: new settings are picked up on their own.
        private static readonly PropertyInfo[] SurfaceProperties = typeof(MaterialComponent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetSetMethod() != null && p.GetIndexParameters().Length == 0)
            .Where(p => p.DeclaringType == typeof(MaterialComponent))
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Where(p => p.Name != nameof(MaterialComponent.MaterialPath))
            .ToArray();

        /// <summary>Copies every surface setting (colour, shader, textures, ...) from one component to another.</summary>
        public static void CopySurface(MaterialComponent from, MaterialComponent to)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return;
            foreach (var property in SurfaceProperties) property.SetValue(to, property.GetValue(from));
        }

        /// <summary>True when two components show the same surface (links and Enabled aside).</summary>
        public static bool SameSurface(MaterialComponent a, MaterialComponent b) =>
            a != null && b != null && SurfaceProperties.All(p => Equals(p.GetValue(a), p.GetValue(b)));

        // ------------------------------------------------------------------
        //  Files
        // ------------------------------------------------------------------

        /// <summary>Tests point this at a temporary folder instead of the source Content folder.</summary>
        internal static string SourceRootOverride;

        /// <summary>Content folder materials are written to: the source <c>Engine/Content</c> of a dev checkout.</summary>
        public static string SourceRoot => SourceRootOverride ?? GameInfo.SourceContentRoot;

        // The source copy when it exists (dev runs, Anvil), else the one the build placed next to the exe.
        private static string FullPath(string relative)
        {
            if (SourceRootOverride != null)
                return System.IO.Path.Combine(SourceRootOverride, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            return GameInfo.ResolveContentFile(relative);
        }

        /// <summary>"Materials\\Brick.mat" -> "Materials/Brick.mat"; null for an empty path.</summary>
        public static string Normalize(string path) =>
            string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/').TrimStart('/');

        /// <summary>True when two material paths name the same file.</summary>
        public static bool SamePath(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the Content-relative material file exists (exact path, no name search).</summary>
        public static bool Exists(string path)
        {
            string rel = Normalize(path);
            return rel != null && IsMaterialAsset(rel) && !rel.Split('/').Contains("..") && File.Exists(FullPath(rel));
        }

        public static bool IsMaterialAsset(string path) =>
            !string.IsNullOrWhiteSpace(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Content-relative path of a material named by <paramref name="nameOrPath"/>: a path ("Materials/Brick.mat",
        /// the extension and the Materials folder may be left out) or a file name anywhere in Content ("Brick").
        /// Null when there is none.
        /// </summary>
        public static string Resolve(string nameOrPath)
        {
            string key = Normalize(nameOrPath);
            if (key == null) return null;
            var candidates = new List<string> { key };
            if (!IsMaterialAsset(key)) candidates.Add(key + Extension);
            foreach (string c in candidates.ToArray()) candidates.Add(DefaultFolder + "/" + c);
            foreach (string c in candidates)
                if (IsMaterialAsset(c) && !c.Split('/').Contains("..") && File.Exists(FullPath(c))) return c;
            string name = System.IO.Path.GetFileNameWithoutExtension(key);
            return List().FirstOrDefault(p => string.Equals(System.IO.Path.GetFileNameWithoutExtension(p), name,
                StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every material in Content (Content-relative paths, sorted), Materials folder first.</summary>
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
            catch (Exception ex) { EditorBridge.Log("MaterialAsset.List: " + ex.Message); }
            return found
                .OrderBy(p => p.StartsWith(DefaultFolder + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, (DateTime Written, MaterialAsset Asset)> Cache = new(StringComparer.OrdinalIgnoreCase);
        // Bumped by every write from this process, so SyncScene checks the files at once instead of on its next poll.
        private static int _revision;

        /// <summary>
        /// The material named by <paramref name="nameOrPath"/> (see <see cref="Resolve"/>), or null when it doesn't
        /// exist or can't be read (logged). Re-read when the file changes; the same instance until then.
        /// </summary>
        public static MaterialAsset Load(string nameOrPath) => LoadExact(Resolve(nameOrPath));

        // Exactly this Content-relative file, no name search (linked components poll it).
        private static MaterialAsset LoadExact(string rel)
        {
            rel = Normalize(rel);
            if (rel == null || !IsMaterialAsset(rel)) return null;
            string full = FullPath(rel);
            try
            {
                if (!File.Exists(full)) return null;
                DateTime written = File.GetLastWriteTimeUtc(full);
                lock (CacheLock)
                    if (Cache.TryGetValue(full, out var cached) && cached.Written == written) return cached.Asset;

                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(full), SceneSerialization.JsonOptions);
                if (doc?.Material == null) throw new InvalidDataException("no Material");
                if (doc.Version < 1 || doc.Version > CurrentVersion)
                    throw new InvalidDataException($"unsupported version {doc.Version} (this build reads {CurrentVersion})");
                doc.Material.MaterialPath = null;
                var asset = new MaterialAsset(rel, doc.Material);
                lock (CacheLock) Cache[full] = (written, asset);
                return asset;
            }
            catch (Exception ex)
            {
                // Anvil may be halfway through replacing the file; the next poll reads it again.
                if (ex is IOException) return null;
                EditorBridge.Log($"Material: couldn't read '{rel}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes <paramref name="settings"/>' surface as a material file in the source Content folder (atomically)
        /// and returns the loaded asset. Gameobjects linked to it follow on the next frame.
        /// </summary>
        public static MaterialAsset Write(string relativePath, MaterialComponent settings)
        {
            string rel = Normalize(relativePath);
            if (rel == null || !IsMaterialAsset(rel))
                throw new ArgumentException($"'{relativePath}' is not a {Extension} path", nameof(relativePath));
            if (rel.Split('/').Contains("..")) throw new ArgumentException("A material must stay inside Content.", nameof(relativePath));
            string full = System.IO.Path.Combine(SourceRoot, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            var surface = new MaterialComponent();
            CopySurface(settings ?? new MaterialComponent(), surface);
            string json = JsonSerializer.Serialize(new Document { Material = surface }, SceneSerialization.JsonOptions);
            lock (CacheLock)
            {
                string tmp = full + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, full, overwrite: true);
                Cache.Remove(full);
            }
            Interlocked.Increment(ref _revision);
            return LoadExact(rel);
        }

        /// <summary>
        /// A free Content-relative path for a new material called <paramref name="name"/> in <paramref name="folder"/>
        /// ("Materials" when null): "Materials/Brick.mat", else "Materials/Brick_2.mat", ...
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
            if (string.IsNullOrWhiteSpace(name)) return "New Material";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            string safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
            return safe.Length == 0 ? "New Material" : safe;
        }

        /// <summary>
        /// Creates a new material file in <paramref name="folder"/> ("Materials" when null) from
        /// <paramref name="settings"/> (a default surface when null; its texture slots then keep each model's own maps).
        /// </summary>
        public static MaterialAsset Create(string folder, string name, MaterialComponent settings = null) =>
            Write(UniquePath(folder, name), settings);

        // ------------------------------------------------------------------
        //  Gameobjects
        // ------------------------------------------------------------------

        /// <summary>
        /// The surface <paramref name="entity"/> shows right now as material settings: its Material component's values,
        /// or the model's material without one. Texture slots that follow the model are filled with the textures the
        /// model actually draws (Content-relative source files), and maps the model doesn't have become "None", so the
        /// material looks the same on any other mesh. Maps that can't be traced to a Content file keep following the model.
        /// </summary>
        public static MaterialComponent Capture(BasicEntity entity)
        {
            var component = entity?.GetComponent<MaterialComponent>();
            var settings = component != null ? new MaterialComponent() : MaterialComponent.FromOwner(entity);
            if (component != null) CopySurface(component, settings);
            var shown = DisplayedMaterial(entity);
            settings.BaseColorTexture ??= TexturePathOf(shown?.AlbedoMap);
            settings.NormalTexture ??= TexturePathOf(shown?.NormalMap);
            settings.RoughnessTexture ??= TexturePathOf(shown?.RoughnessMap);
            settings.MetallicTexture ??= TexturePathOf(shown?.MetallicMap);
            settings.MaskTexture ??= TexturePathOf(shown?.Mask);
            settings.DisplacementTexture ??= TexturePathOf(shown?.DisplacementMap);
            settings.AmbientOcclusionTexture ??= TexturePathOf(shown?.AmbientOcclusionMap);
            return settings;
        }

        // The model's own material (before the component's overrides): the entity's material or the first textured part's.
        private static MaterialEffect DisplayedMaterial(BasicEntity entity)
        {
            if (entity == null) return null;
            if (entity.Material != null) return entity.Material;
            var parts = entity.Model?.Meshes.SelectMany(m => m.MeshParts).Select(p => p.Effect).OfType<MaterialEffect>().ToList();
            return parts?.FirstOrDefault(m => m.AlbedoMap != null) ?? parts?.FirstOrDefault();
        }

        private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".dds", ".bmp" };

        /// <summary>
        /// Content-relative source file ("GameObjects/Tree/bark_albedo.png") of a texture loaded through the content
        /// pipeline; "" for no texture, null when the source file can't be found.
        /// </summary>
        public static string TexturePathOf(Texture2D texture)
        {
            if (texture == null) return string.Empty;
            string name = Normalize(texture.Name);
            if (name == null) return null;
            string root = SourceRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
            if (System.IO.Path.IsPathRooted(name))
            {
                name = Normalize(System.IO.Path.GetRelativePath(root, name));
                if (name.StartsWith("../", StringComparison.Ordinal)) return null;
            }
            var stems = new List<string> { name };
            // Textures a model references are built next to it as "<name>_0".
            if (name.EndsWith("_0", StringComparison.Ordinal)) stems.Add(name.Substring(0, name.Length - 2));
            foreach (string stem in stems)
                foreach (string ext in TextureExtensions)
                {
                    string candidate = stem + ext;
                    if (File.Exists(System.IO.Path.Combine(root, candidate.Replace('/', System.IO.Path.DirectorySeparatorChar))))
                        return candidate;
                }
            return null;
        }

        /// <summary>
        /// Saves the surface <paramref name="entity"/> shows (see <see cref="Capture"/>) as a new material in
        /// <paramref name="folder"/> ("Materials" when null), named after the gameobject, and links the gameobject to it.
        /// Game thread.
        /// </summary>
        public static MaterialAsset CreateFrom(BasicEntity entity, string folder = null)
        {
            if (entity == null) return null;
            var asset = Create(folder, entity.Name, Capture(entity));
            if (asset != null) Assign(entity, asset.Path);
            return asset;
        }

        /// <summary>
        /// Makes <paramref name="entity"/> use the material named by <paramref name="nameOrPath"/>, adding a Material
        /// component when it has none. False (and a log line) when there is no such material. Game thread.
        /// </summary>
        public static bool Assign(BasicEntity entity, string nameOrPath)
        {
            if (entity == null) return false;
            var asset = Load(nameOrPath);
            if (asset == null)
            {
                EditorBridge.Log($"Material: no material asset '{nameOrPath}'.");
                return false;
            }
            var component = entity.GetComponent<MaterialComponent>();
            if (component == null)
            {
                // OnAdded copies the asset's surface and builds the material instances.
                return entity.AddComponent(new MaterialComponent { MaterialPath = asset.Path });
            }
            if (!component.Link(asset.Path)) return false;
            component.OnChanged(entity);
            return true;
        }

        /// <summary>
        /// Writes a linked component's surface into its material file (Anvil's Inspector edits of a linked Material
        /// component); every other gameobject using it follows on the next frame. Does nothing when unlinked.
        /// </summary>
        public static MaterialAsset SaveFrom(MaterialComponent component)
        {
            string path = Normalize(component?.MaterialPath);
            if (path == null) return null;
            var current = LoadExact(path);
            if (current != null && SameSurface(current.Settings, component)) return current;
            var written = Write(path, component);
            component.Sync(written, copy: false);
            return written;
        }

        private static int _syncedRevision = -1;
        private static int _framesSinceCheck;
        // Frames between checks of the files for outside edits (a text editor, git, Anvil's material Inspector).
        private const int PollFrames = 30;

        /// <summary>
        /// Keeps linked Material components equal to their material files: new links at once, file changes after this
        /// process wrote one or every <see cref="PollFrames"/> frames. A missing file leaves the component's last
        /// values (saved in the scene). Game thread, every frame (edit and Play).
        /// </summary>
        public static void SyncScene(IReadOnlyList<BasicEntity> entities)
        {
            if (entities == null) return;
            int revision = Volatile.Read(ref _revision);
            bool checkFiles = revision != _syncedRevision || ++_framesSinceCheck >= PollFrames;
            if (checkFiles)
            {
                _syncedRevision = revision;
                _framesSinceCheck = 0;
            }
            Dictionary<string, MaterialAsset> loaded = null;
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null) continue;
                var components = entity.Components;
                for (int c = 0; c < components.Count; c++)
                {
                    if (components[c] is not MaterialComponent material || material.MaterialPath == null) continue;
                    if (!checkFiles && SamePath(material.SyncedPath, material.MaterialPath)) continue;
                    loaded ??= new Dictionary<string, MaterialAsset>(StringComparer.OrdinalIgnoreCase);
                    string path = Normalize(material.MaterialPath);
                    if (path == null) continue;
                    if (!loaded.TryGetValue(path, out var asset)) loaded[path] = asset = LoadExact(path);
                    if (asset == null)
                    {
                        // Missing: keep the saved values and retry on the next file check.
                        material.SyncedPath = path;
                        continue;
                    }
                    if (material.IsSyncedWith(asset)) continue;
                    material.Sync(asset);
                    if (material.Enabled) entity.RefreshMaterials();
                }
            }
        }
    }
}
