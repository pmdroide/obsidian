using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine.Editor;

namespace Engine.Recources
{
    /// <summary>
    /// The game's scenes in build order, edited in Anvil > Game Settings > Scenes. Entry 0 is the
    /// startup scene: the standalone game loads it first. Scripts switch between entries with
    /// <see cref="Logic.GameFlow"/>. Persisted as <c>Content/System/SceneList.json</c>; entries are
    /// Content-relative <c>.obsc</c> paths ("Scenes/MainMenu.obsc").
    ///
    /// Plain file I/O only — safe to call from the editor (UI) thread as well as the game thread.
    /// </summary>
    public static class SceneList
    {
        //Content-relative. System/ is engine-managed and hidden from Anvil's Assets panel.
        public const string FileName = "System/SceneList.json";

        public sealed class Data
        {
            public List<string> Scenes { get; set; } = new List<string>();
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static Data Current { get; private set; } = new Data();

        public static IReadOnlyList<string> Scenes => Current.Scenes;

        /// <summary>Reads SceneList.json from disk without touching <see cref="Current"/>.</summary>
        public static Data Read()
        {
            string path = GameInfo.ResolveContentFile(FileName);
            try
            {
                if (File.Exists(path))
                    return Normalize(JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOptions));
            }
            catch (Exception e)
            {
                EditorBridge.Log($"SceneList: failed to read '{path}': {e.Message}");
            }
            return new Data();
        }

        /// <summary>Reads SceneList.json into <see cref="Current"/>.</summary>
        public static Data Load() => Current = Read();

        /// <summary>
        /// Writes the list to the source <c>Engine/Content</c> folder (the build copies it next to the
        /// exe) and makes it <see cref="Current"/>.
        /// </summary>
        public static void Save(Data data)
        {
            data = Normalize(data);
            string path = Path.Combine(GameInfo.SourceContentRoot, FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
            Current = data;
        }

        /// <summary>"Scenes/MainMenu.obsc" -> "MainMenu".</summary>
        public static string DisplayName(string entry) =>
            string.IsNullOrEmpty(entry) ? "" : Path.GetFileNameWithoutExtension(entry);

        /// <summary>Absolute path of a list entry: the dev checkout's copy first, else the one next to the exe.</summary>
        public static string ResolvePath(string entry) =>
            string.IsNullOrEmpty(entry) ? null : GameInfo.ResolveContentFile(entry);

        /// <summary>
        /// Content-relative entry for an absolute scene path ("Scenes/MainMenu.obsc"), or null when the
        /// file is outside both Content folders.
        /// </summary>
        public static string ToEntry(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;
            string full = Path.GetFullPath(absolutePath);
            foreach (string root in new[] { GameInfo.SourceContentRoot, Path.Combine(AppContext.BaseDirectory, "Content") })
            {
                string rel = Path.GetRelativePath(Path.GetFullPath(root), full);
                if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                    return rel.Replace('\\', '/');
            }
            return null;
        }

        /// <summary>Index of a scene file in the list, or -1. Matches by Content-relative path, then by name.</summary>
        public static int IndexOf(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return -1;
            string entry = ToEntry(absolutePath);
            for (int i = 0; i < Current.Scenes.Count; i++)
                if (entry != null && string.Equals(Current.Scenes[i], entry, StringComparison.OrdinalIgnoreCase)) return i;
            string name = Path.GetFileNameWithoutExtension(absolutePath);
            for (int i = 0; i < Current.Scenes.Count; i++)
                if (string.Equals(DisplayName(Current.Scenes[i]), name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>Index of an entry by scene name ("MainMenu") or Content-relative path, or -1.</summary>
        public static int Find(string nameOrEntry)
        {
            if (string.IsNullOrWhiteSpace(nameOrEntry)) return -1;
            string key = nameOrEntry.Replace('\\', '/').Trim();
            for (int i = 0; i < Current.Scenes.Count; i++)
            {
                string s = Current.Scenes[i];
                if (string.Equals(s, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(DisplayName(s), key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static Data Normalize(Data data)
        {
            var result = new Data();
            if (data?.Scenes == null) return result;
            foreach (string raw in data.Scenes)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string entry = raw.Trim().Replace('\\', '/').TrimStart('/');
                if (!result.Scenes.Contains(entry, StringComparer.OrdinalIgnoreCase)) result.Scenes.Add(entry);
            }
            return result;
        }
    }
}
