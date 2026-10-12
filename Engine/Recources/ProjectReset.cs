using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine.Editor;
using Engine.Steam;

namespace Engine.Recources
{
    /// <summary>
    /// Anvil > Edit > Project Settings > Delete Project: turns the project back into a new, empty one.
    /// Removes the project's content (scenes, UI documents, audio, video, imported models and
    /// textures, skyboxes, anything else the user added) with its Content.mgcb entries and built
    /// copies, and writes the System settings back to their defaults with a single empty
    /// <see cref="FirstScene"/> in the scene list.
    ///
    /// What the engine loads at boot is kept (<see cref="KeptPaths"/>): shaders, fonts, the editor
    /// gizmos and the built-in models of <see cref="Assets"/>. Scripts are kept too: they are compiled
    /// into Engine.dll and registered in ScriptRegistry, so deleting their source would break the build.
    ///
    /// Plain file I/O. <see cref="Plan"/> only reads, <see cref="Run"/> deletes. The editor calls Run
    /// through <c>IEditorBridge.EnqueueResetProject</c>, which also swaps in the new scene.
    /// </summary>
    public static class ProjectReset
    {
        /// <summary>Content-relative path of the new project's only scene (scene list entry 0).</summary>
        public const string FirstScene = "Scenes/Main.obsc";

        /// <summary>
        /// Content-relative files and folders a reset never removes. Ancestors of these (GameObjects)
        /// are entered, so their other children are removed.
        /// </summary>
        public static readonly string[] KeptPaths =
        {
            "Content.mgcb", "Content.mgcb.org", "bin", "obj",
            "Shaders",                      // Shaders.cs, Renderer.Load, render modules
            "Fonts",                        // Assets, DebugScreen, ScreenManager (UI fonts)
            "System",                       // editor gizmos/icons + settings (reset below, not deleted)
            "Scripts",                      // compiled into Engine.dll (ScriptRegistry)
            "Graphical User Interface",     // legacy, still in Content.mgcb
            "GameObjects/Default",          // Assets: cube, sphere, isosphere, capsule, dragons
            "GameObjects/Cloth",            // Assets.Cloth
            "GameObjects/Tree",             // Assets.Tree (+ its bark/leaf textures and generator)
            "GameObjects/Error",            // fallback model for failed imports
            "GameObjects/error.png",        // fallback material texture
            "GameObjects/sky.dds",          // default sky
            "GameObjects/Player",           // Assets.PlayerYBot / PlayerWalking (built-in model keys)
        };

        /// <summary>Project settings files deleted outright (their absence is the default).</summary>
        public static readonly string[] DeletedSettings = { GameInfo.DefaultIconPath, "System/BuildSettings.json" };

        public sealed class Options
        {
            /// <summary>Absolute path of the source <c>Engine/Content</c> folder.</summary>
            public string ContentRoot { get; set; }

            /// <summary>
            /// Folders holding built copies of the content (mgcb output, the Content folder next to an
            /// executable). Matching files there are deleted so they don't come back from a stale copy.
            /// </summary>
            public List<string> OutputRoots { get; set; } = new List<string>();

            /// <summary>Removes a source file or folder. Default: permanent delete; Anvil passes the Recycle Bin.</summary>
            public Action<string> DeleteSource { get; set; }

            /// <summary>Options for this checkout: the source Content and every built copy of it that exists.</summary>
            public static Options ForThisCheckout()
            {
                string root = AssetImporter.LocateEngineContentRoot();
                var outputs = new List<string>
                {
                    Path.Combine(root, "bin", "Windows"),
                    Path.Combine(root, "bin", "Windows", "Content"),
                    Path.Combine(AppContext.BaseDirectory, "Content"),
                };
                // The standalone engine's own output (Engine/bin/<config>/<framework>/Content).
                string engineBin = Path.Combine(Path.GetDirectoryName(root) ?? root, "bin");
                if (Directory.Exists(engineBin))
                    foreach (string config in Directory.GetDirectories(engineBin))
                        foreach (string framework in Directory.GetDirectories(config))
                            outputs.Add(Path.Combine(framework, "Content"));
                return new Options { ContentRoot = root, OutputRoots = outputs };
            }
        }

        public sealed class Result
        {
            /// <summary>Content-relative files and folders that were removed.</summary>
            public List<string> Removed { get; } = new List<string>();

            /// <summary>Content.mgcb entries that were removed.</summary>
            public List<string> ManifestEntriesRemoved { get; } = new List<string>();

            public List<string> Errors { get; } = new List<string>();

            public bool Succeeded => Errors.Count == 0;
        }

        /// <summary>True when a reset keeps this Content-relative path (it is, or is inside, a kept path).</summary>
        public static bool IsKept(string relPath)
        {
            string path = Normalize(relPath);
            return KeptPaths.Any(k => IsSameOrUnder(path, k));
        }

        /// <summary>
        /// The Content-relative files and folders a reset removes, without touching anything: every
        /// top-most entry that isn't kept. A folder is listed once, not its children.
        /// </summary>
        public static List<string> Plan(string contentRoot)
        {
            var plan = new List<string>();
            if (Directory.Exists(contentRoot)) Collect(contentRoot, "", plan);
            plan.Sort(StringComparer.OrdinalIgnoreCase);
            return plan;
        }

        private static void Collect(string contentRoot, string relDir, List<string> plan)
        {
            string dir = relDir.Length == 0 ? contentRoot : Path.Combine(contentRoot, relDir.Replace('/', Path.DirectorySeparatorChar));
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
            {
                string rel = (relDir.Length == 0 ? "" : relDir + "/") + Path.GetFileName(entry);
                if (IsKept(rel)) continue;
                // A folder that holds something kept (GameObjects) is entered instead of removed.
                if (Directory.Exists(entry) && KeptPaths.Any(k => IsSameOrUnder(k, rel)))
                    Collect(contentRoot, rel, plan);
                else
                    plan.Add(rel);
            }
        }

        /// <summary>
        /// Removes everything <see cref="Plan"/> lists (source, Content.mgcb entries, built copies) and
        /// writes default project settings. Keeps going after an error and reports it in the result.
        /// </summary>
        public static Result Run(Options options)
        {
            if (options == null || string.IsNullOrEmpty(options.ContentRoot) || !Directory.Exists(options.ContentRoot))
                throw new ArgumentException("ProjectReset needs an existing Content folder.", nameof(options));

            var result = new Result();
            string root = options.ContentRoot;
            Action<string> deleteSource = options.DeleteSource ?? DeletePermanently;
            string manifest = Path.Combine(root, "Content.mgcb");
            var outputs = (options.OutputRoots ?? new List<string>())
                .Where(o => !string.IsNullOrEmpty(o))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string rel in Plan(root))
            {
                try
                {
                    deleteSource(ToAbsolute(root, rel));
                    if (File.Exists(manifest))
                        result.ManifestEntriesRemoved.AddRange(ContentManifest.Unregister(rel, manifest));
                    foreach (string output in outputs)
                        DeleteBuiltCopies(output, rel, outputs);
                    result.Removed.Add(rel);
                }
                catch (Exception e)
                {
                    result.Errors.Add($"{rel}: {e.Message}");
                }
            }

            try { WriteDefaultSettings(root); }
            catch (Exception e) { result.Errors.Add("settings: " + e.Message); }

            try { Directory.CreateDirectory(ToAbsolute(root, "Scenes")); }
            catch (Exception e) { result.Errors.Add("Scenes: " + e.Message); }

            EditorBridge.Log($"ProjectReset: removed {result.Removed.Count} item(s), {result.ManifestEntriesRemoved.Count} Content.mgcb entr(ies), {result.Errors.Count} error(s)");
            return result;
        }

        /// <summary>
        /// The settings a new project starts with: default game/window settings, no intro videos,
        /// Steam off, and <see cref="FirstScene"/> as the only scene. Written rather than deleted:
        /// a missing file would make the engine read the stale copy next to the executable.
        /// </summary>
        public static void WriteDefaultSettings(string contentRoot)
        {
            var json = new JsonSerializerOptions { WriteIndented = true };
            WriteJson(contentRoot, GameInfo.FileName, new GameInfo.Data(), json);
            WriteJson(contentRoot, IntroVideoList.FileName, new IntroVideoList.Data(), json);
            WriteJson(contentRoot, SceneList.FileName, new SceneList.Data { Scenes = { FirstScene } }, json);
            new SteamSettingsStore(ToAbsolute(contentRoot, "System/SteamSettings.json")).Save(new SteamSettings());
            foreach (string rel in DeletedSettings)
            {
                string path = ToAbsolute(contentRoot, rel);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void WriteJson<T>(string contentRoot, string rel, T data, JsonSerializerOptions options)
        {
            string path = ToAbsolute(contentRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(data, options));
        }

        // A built copy is the file or folder itself (loose files) or the .xnb mgcb made from a file.
        // Never removes a folder that is (or holds) another output root: a user folder named
        // "Content" must not take bin/Windows/Content, the built engine content, with it.
        private static void DeleteBuiltCopies(string outputRoot, string rel, List<string> outputRoots)
        {
            if (!Directory.Exists(outputRoot)) return;
            var targets = new List<string> { ToAbsolute(outputRoot, rel) };
            string extension = Path.GetExtension(rel);
            if (extension.Length > 0)
                targets.Add(ToAbsolute(outputRoot, rel.Substring(0, rel.Length - extension.Length) + ".xnb"));
            foreach (string target in targets.Select(Path.GetFullPath))
            {
                if (outputRoots.Any(o => IsSameOrUnderDirectory(o, target))) continue;
                DeletePermanently(target);
            }
        }

        private static bool IsSameOrUnderDirectory(string path, string directory)
        {
            string p = path.TrimEnd(Path.DirectorySeparatorChar), d = directory.TrimEnd(Path.DirectorySeparatorChar);
            return p.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void DeletePermanently(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }

        private static string ToAbsolute(string root, string rel) =>
            Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

        private static bool IsSameOrUnder(string path, string target) =>
            path.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string relPath) =>
            (relPath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
    }
}
