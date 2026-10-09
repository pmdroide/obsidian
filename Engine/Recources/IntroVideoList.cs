using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine.Editor;

namespace Engine.Recources
{
    /// <summary>
    /// The intro videos the standalone game plays before scene list entry 0, in play order. Edited in
    /// Anvil > Game Settings > Intro Videos; Enter skips the current video. Persisted as
    /// <c>Content/System/IntroVideos.json</c>; entries are Content-relative paths ("Video/intro.mp4").
    /// Without the file the game plays <see cref="DefaultVideo"/>; an empty list plays no intro.
    ///
    /// Plain file I/O only — safe to call from the editor (UI) thread as well as the game thread.
    /// </summary>
    public static class IntroVideoList
    {
        //Content-relative. System/ is engine-managed and hidden from Anvil's Assets panel.
        public const string FileName = "System/IntroVideos.json";

        public const string DefaultVideo = "Video/intro.mp4";

        /// <summary>Extensions offered by Anvil's picker and copied to the output by the build.</summary>
        public static readonly string[] Extensions = { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm" };

        public sealed class Data
        {
            public List<string> Videos { get; set; } = new List<string>();
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>Reads IntroVideos.json, or the default intro when the file doesn't exist yet.</summary>
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
                EditorBridge.Log($"IntroVideoList: failed to read '{path}': {e.Message}");
            }
            return new Data { Videos = { DefaultVideo } };
        }

        /// <summary>Writes the list to the source <c>Engine/Content</c> folder (the build copies it next to the exe).</summary>
        public static void Save(Data data)
        {
            data = Normalize(data);
            string path = Path.Combine(GameInfo.SourceContentRoot, FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
        }

        /// <summary>"Video/intro.mp4" -> "intro.mp4".</summary>
        public static string DisplayName(string entry) =>
            string.IsNullOrEmpty(entry) ? "" : Path.GetFileName(entry);

        /// <summary>Absolute path of a list entry: the dev checkout's copy first, else the one next to the exe.</summary>
        public static string ResolvePath(string entry) =>
            string.IsNullOrEmpty(entry) ? null : GameInfo.ResolveContentFile(entry);

        /// <summary>Content-relative entry for an absolute video path, or null when it is outside both Content folders.</summary>
        public static string ToEntry(string absolutePath) => SceneList.ToEntry(absolutePath);

        private static Data Normalize(Data data)
        {
            var result = new Data();
            if (data?.Videos == null) return result;
            foreach (string raw in data.Videos)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string entry = raw.Trim().Replace('\\', '/').TrimStart('/');
                if (!result.Videos.Contains(entry, StringComparer.OrdinalIgnoreCase)) result.Videos.Add(entry);
            }
            return result;
        }
    }
}
