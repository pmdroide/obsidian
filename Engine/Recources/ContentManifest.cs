using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Engine.Recources
{
    /// <summary>
    /// Edits Engine/Content/Content.mgcb: adds build entries for new content files and
    /// removes / renames entries when files go away. Only file types that go through the
    /// MonoGame pipeline are registered (models, textures, effects, sprite fonts); loose
    /// files the engine reads directly (FMOD audio, the intro video, Vista UI XML/CSS,
    /// .bbox/.sdft sidecars, .fxh includes) are left out.
    ///
    /// Safe to call from any thread: every read-modify-write holds the same cross-process
    /// mutex the <see cref="AssetImporter"/> uses. Paths are Content-relative, '/'-separated.
    /// </summary>
    public static class ContentManifest
    {
        // Cross-process guard around the Content.mgcb read-modify-write.
        internal static readonly Mutex EditMutex = new Mutex(false, "obsidian.mgcb.edit");

        private const string BeginPrefix = "#begin ";
        private const string BuildPrefix = "/build:";

        // Top-level folders whose files are never registered (build output / legacy GUI).
        private static readonly string[] ExcludedTopFolders = { "bin", "obj" };

        private static readonly Dictionary<string, string[]> EntryTemplates =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [".fbx"] = ModelEntry("FbxImporter"),
                [".obj"] = ModelEntry("OpenAssetImporter"),
                [".dae"] = ModelEntry("OpenAssetImporter"),
                [".gltf"] = ModelEntry("OpenAssetImporter"),
                [".glb"] = ModelEntry("OpenAssetImporter"),
                [".x"] = ModelEntry("XImporter"),
                [".png"] = TextureEntry(),
                [".jpg"] = TextureEntry(),
                [".jpeg"] = TextureEntry(),
                [".tga"] = TextureEntry(),
                [".bmp"] = TextureEntry(),
                [".dds"] = TextureEntry(),
                [".fx"] = new[]
                {
                    "/importer:EffectImporter",
                    "/processor:EffectProcessor",
                    "/processorParam:DebugMode=Auto",
                },
                [".spritefont"] = new[]
                {
                    "/importer:FontDescriptionImporter",
                    "/processor:FontDescriptionProcessor",
                    "/processorParam:PremultiplyAlpha=True",
                    "/processorParam:TextureFormat=Compressed",
                },
            };

        public static string ManifestPath =>
            Path.Combine(AssetImporter.LocateEngineContentRoot(), "Content.mgcb");

        /// <summary>True if a file at this Content-relative path would get a build entry.</summary>
        public static bool IsBuildable(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return false;
            string top = relPath.Split('/')[0];
            if (ExcludedTopFolders.Any(f => f.Equals(top, StringComparison.OrdinalIgnoreCase))) return false;
            return EntryTemplates.ContainsKey(Path.GetExtension(relPath));
        }

        /// <summary>
        /// Adds build entries for every buildable path not already in the manifest.
        /// Returns the paths that were added.
        /// </summary>
        public static IReadOnlyList<string> Register(IEnumerable<string> relPaths)
        {
            var added = new List<string>();
            var candidates = relPaths.Select(Normalize).Where(IsBuildable).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (candidates.Count == 0) return added;

            Edit(lines =>
            {
                var existing = new HashSet<string>(
                    lines.Where(l => l.StartsWith(BeginPrefix, StringComparison.Ordinal))
                         .Select(l => l.Substring(BeginPrefix.Length).Trim()),
                    StringComparer.OrdinalIgnoreCase);

                foreach (string rel in candidates)
                {
                    if (!existing.Add(rel)) continue;
                    if (lines.Count > 0 && lines[lines.Count - 1].Length != 0) lines.Add(string.Empty);
                    lines.Add(BeginPrefix + rel);
                    lines.AddRange(EntryTemplates[Path.GetExtension(rel)]);
                    lines.Add(BuildPrefix + rel);
                    added.Add(rel);
                }
                return added.Count > 0;
            });
            return added;
        }

        /// <summary>
        /// Removes the entry for <paramref name="relPath"/> and, when it names a folder,
        /// every entry beneath it. Returns the paths that were removed.
        /// </summary>
        public static IReadOnlyList<string> Unregister(string relPath)
        {
            var removed = new List<string>();
            string target = Normalize(relPath);
            if (target.Length == 0) return removed;

            Edit(lines =>
            {
                var kept = new List<string>(lines.Count);
                bool skipping = false;
                foreach (string line in lines)
                {
                    // A block runs from its #begin line to the line before the next #begin.
                    if (line.StartsWith(BeginPrefix, StringComparison.Ordinal))
                    {
                        string path = line.Substring(BeginPrefix.Length).Trim();
                        skipping = IsSameOrUnder(path, target);
                        if (skipping) removed.Add(path);
                    }
                    if (!skipping) kept.Add(line);
                }
                if (removed.Count == 0) return false;
                // Removing the last block leaves the blank separator that preceded it.
                while (kept.Count > 0 && kept[kept.Count - 1].Length == 0) kept.RemoveAt(kept.Count - 1);
                lines.Clear();
                lines.AddRange(kept);
                return true;
            });
            return removed;
        }

        /// <summary>
        /// Points entries for <paramref name="oldRelPath"/> (a file, or everything under a
        /// folder) at <paramref name="newRelPath"/>, keeping their importer/processor
        /// settings. Returns how many entries were renamed.
        /// </summary>
        public static int Rename(string oldRelPath, string newRelPath)
        {
            string from = Normalize(oldRelPath), to = Normalize(newRelPath);
            if (from.Length == 0 || to.Length == 0) return 0;

            int renamed = 0;
            Edit(lines =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    string prefix = line.StartsWith(BeginPrefix, StringComparison.Ordinal) ? BeginPrefix
                        : line.StartsWith(BuildPrefix, StringComparison.Ordinal) ? BuildPrefix
                        : null;
                    if (prefix == null) continue;
                    string path = line.Substring(prefix.Length).Trim();
                    if (!IsSameOrUnder(path, from)) continue;
                    lines[i] = prefix + to + path.Substring(from.Length);
                    if (prefix == BeginPrefix) renamed++;
                }
                return renamed > 0;
            });
            return renamed;
        }

        // -------- Helpers --------

        private static void Edit(Func<List<string>, bool> mutate)
        {
            string path = ManifestPath;
            EditMutex.WaitOne();
            try
            {
                if (!File.Exists(path)) return;
                string text = File.ReadAllText(path);
                string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
                // Drop the empty element produced by a trailing newline; re-added on write.
                bool trailingNewline = lines.Count > 0 && lines[lines.Count - 1].Length == 0;
                if (trailingNewline) lines.RemoveAt(lines.Count - 1);

                if (!mutate(lines)) return;

                var sb = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    sb.Append(lines[i]);
                    if (i < lines.Count - 1 || trailingNewline) sb.Append(nl);
                }
                File.WriteAllText(path, sb.ToString());
            }
            finally { EditMutex.ReleaseMutex(); }
        }

        private static bool IsSameOrUnder(string path, string target) =>
            path.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string relPath) =>
            (relPath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');

        private static string[] ModelEntry(string importer) => new[]
        {
            "/importer:" + importer,
            "/processor:ModelProcessor",
            "/processorParam:ColorKeyColor=0,0,0,0",
            "/processorParam:ColorKeyEnabled=True",
            "/processorParam:DefaultEffect=BasicEffect",
            "/processorParam:GenerateMipmaps=True",
            "/processorParam:GenerateTangentFrames=True",
            "/processorParam:PremultiplyTextureAlpha=True",
            "/processorParam:PremultiplyVertexColors=True",
            "/processorParam:ResizeTexturesToPowerOfTwo=False",
            "/processorParam:RotationX=0",
            "/processorParam:RotationY=0",
            "/processorParam:RotationZ=0",
            "/processorParam:Scale=1",
            "/processorParam:SwapWindingOrder=False",
            "/processorParam:TextureFormat=Compressed",
        };

        private static string[] TextureEntry() => new[]
        {
            "/importer:TextureImporter",
            "/processor:TextureProcessor",
            "/processorParam:ColorKeyColor=255,0,255,255",
            "/processorParam:ColorKeyEnabled=True",
            "/processorParam:GenerateMipmaps=True",
            "/processorParam:PremultiplyAlpha=True",
            "/processorParam:ResizeToPowerOfTwo=False",
            "/processorParam:MakeSquare=False",
            "/processorParam:TextureFormat=Color",
        };
    }
}
