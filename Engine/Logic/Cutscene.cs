using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine.Editor;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Logic
{
    /// <summary>What a cutscene event does when the playhead reaches it. Saved by name, never rename one.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum CutsceneEventKind
    {
        /// <summary>Shows <see cref="CutsceneEvent.Text"/> at the bottom of the screen for its Duration.</summary>
        Subtitle,
        /// <summary>Fades the screen to black over its Duration (stays black until a Fade In).</summary>
        FadeOut,
        /// <summary>Fades up from black over its Duration (it starts black, so one at 0 s opens a cutscene).</summary>
        FadeIn,
        /// <summary>Plays the sound named by Text (Audio.PlaySound, a file in Content/Audio).</summary>
        Sound,
        /// <summary>Raises <see cref="Cutscenes.EventFired"/> with Text, for scripts (open a door, start an animation...).</summary>
        Message,
    }

    /// <summary>A camera pose at a time. Between keys the position follows a Catmull-Rom curve and yaw/pitch/FOV blend.</summary>
    public sealed class CutsceneCameraKey
    {
        public float Time { get; set; }
        public Vector3 Position { get; set; }
        /// <summary>Degrees around world up (Z), 0 = +X (EditorCamera.Yaw).</summary>
        public float Yaw { get; set; }
        /// <summary>Degrees up from the horizon.</summary>
        public float Pitch { get; set; }
        /// <summary>Vertical field of view in degrees.</summary>
        public float Fov { get; set; } = 45f;
        /// <summary>Jump straight to this key instead of moving from the previous one (a camera cut).</summary>
        public bool Cut { get; set; }
        /// <summary>Ease in/out of this key (slow start and stop) instead of moving at an even speed.</summary>
        public bool Smooth { get; set; } = true;

        public CutsceneCameraKey Clone() => (CutsceneCameraKey)MemberwiseClone();
    }

    /// <summary>Something that happens at a time on the cutscene's timeline (see <see cref="CutsceneEventKind"/>).</summary>
    public sealed class CutsceneEvent
    {
        public float Time { get; set; }
        public float Duration { get; set; } = 2f;
        public CutsceneEventKind Kind { get; set; } = CutsceneEventKind.Subtitle;
        public string Text { get; set; } = "";

        public CutsceneEvent Clone() => (CutsceneEvent)MemberwiseClone();
    }

    /// <summary>A camera pose evaluated from a cutscene at a time.</summary>
    public readonly struct CutsceneCameraPose
    {
        public readonly Vector3 Position;
        public readonly float Yaw, Pitch, Fov;

        public CutsceneCameraPose(Vector3 position, float yaw, float pitch, float fov)
        {
            Position = position;
            Yaw = yaw;
            Pitch = pitch;
            Fov = fov;
        }

        /// <summary>Unit forward vector (Z is up), matching EditorCamera.ApplyYawPitch.</summary>
        public Vector3 Forward
        {
            get
            {
                float yaw = MathHelper.ToRadians(Yaw), pitch = MathHelper.ToRadians(MathHelper.Clamp(Pitch, -89f, 89f));
                float cp = (float)Math.Cos(pitch);
                return Vector3.Normalize(new Vector3(cp * (float)Math.Cos(yaw), cp * (float)Math.Sin(yaw), (float)Math.Sin(pitch)));
            }
        }
    }

    /// <summary>
    /// A cutscene asset: <c>.cutscene</c> JSON in Content (default <c>Content/Cutscenes</c>) with a camera track
    /// (<see cref="CameraKeys"/>) and timed <see cref="Events"/> (subtitles, fades, sounds, script messages).
    /// Made in Anvil > Window > Cutscenes, played with <see cref="Cutscenes.Play(string)"/>. Shipped by Engine.csproj.
    /// </summary>
    public sealed class Cutscene
    {
        public const string Extension = ".cutscene";
        public const string DefaultFolder = "Cutscenes";
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        /// <summary>Length in seconds; the cutscene ends here.</summary>
        public float Duration { get; set; } = 10f;
        /// <summary>Black bars top and bottom while it plays.</summary>
        public bool Letterbox { get; set; } = true;
        /// <summary>Enter/Space/gamepad A or Start skips to the end.</summary>
        public bool Skippable { get; set; } = true;
        /// <summary>Put the game camera back where it was when the cutscene ends.</summary>
        public bool RestoreCamera { get; set; } = true;
        public List<CutsceneCameraKey> CameraKeys { get; set; } = new();
        public List<CutsceneEvent> Events { get; set; } = new();

        /// <summary>Content-relative path it was loaded from or saved to (not saved).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Path { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string Name => Path == null ? "" : System.IO.Path.GetFileNameWithoutExtension(Path);

        // ------------------------------------------------------------------
        //  Evaluation
        // ------------------------------------------------------------------

        /// <summary>Keys sorted by time (the editor and files may keep them in any order).</summary>
        public List<CutsceneCameraKey> SortedKeys() => CameraKeys.OrderBy(k => k.Time).ToList();

        /// <summary>The camera pose at <paramref name="time"/>, or null without camera keys.</summary>
        public CutsceneCameraPose? EvaluateCamera(float time) => EvaluateCamera(SortedKeys(), time);

        public static CutsceneCameraPose? EvaluateCamera(IReadOnlyList<CutsceneCameraKey> keys, float time)
        {
            if (keys == null || keys.Count == 0) return null;
            if (keys.Count == 1 || time <= keys[0].Time) return PoseOf(keys[0]);
            if (time >= keys[keys.Count - 1].Time) return PoseOf(keys[keys.Count - 1]);

            int i = 0;
            while (i < keys.Count - 2 && time >= keys[i + 1].Time) i++;
            CutsceneCameraKey a = keys[i], b = keys[i + 1];
            // A cut holds the previous shot until the key, then jumps.
            if (b.Cut) return PoseOf(a);

            float span = Math.Max(b.Time - a.Time, 1e-4f);
            float t = MathHelper.Clamp((time - a.Time) / span, 0f, 1f);
            // Only the Smooth ends ease (zero speed there); the other end keeps an even speed.
            if (a.Smooth && b.Smooth) t = t * t * (3f - 2f * t);
            else if (a.Smooth) t = t * t * (2f - t);
            else if (b.Smooth) t = 1f - (1f - t) * (1f - t) * (1f + t);

            // The curve continues through the neighbours unless a cut separates them.
            Vector3 p0 = i > 0 && !a.Cut ? keys[i - 1].Position : a.Position;
            Vector3 p3 = i + 2 < keys.Count && !keys[i + 2].Cut ? keys[i + 2].Position : b.Position;
            Vector3 position = Vector3.CatmullRom(p0, a.Position, b.Position, p3, t);

            float yaw = a.Yaw + WrapDegrees(b.Yaw - a.Yaw) * t;
            return new CutsceneCameraPose(position, yaw, MathHelper.Lerp(a.Pitch, b.Pitch, t), MathHelper.Lerp(a.Fov, b.Fov, t));
        }

        private static CutsceneCameraPose PoseOf(CutsceneCameraKey k) => new(k.Position, k.Yaw, k.Pitch, k.Fov);

        /// <summary>-180..180: the short way round.</summary>
        public static float WrapDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees < -180f) degrees += 360f;
            return degrees;
        }

        /// <summary>How black the screen is at <paramref name="time"/> (0 clear, 1 black), from the Fade events.</summary>
        public float FadeAt(float time)
        {
            float fade = 0f;
            foreach (var e in Events.Where(e => e.Kind is CutsceneEventKind.FadeOut or CutsceneEventKind.FadeIn && e.Time <= time).OrderBy(e => e.Time))
            {
                float t = e.Duration <= 0 ? 1f : MathHelper.Clamp((time - e.Time) / e.Duration, 0f, 1f);
                // Fade Out darkens from wherever it is; Fade In always comes up from black (an opening fade needs no Fade Out).
                fade = e.Kind == CutsceneEventKind.FadeOut ? MathHelper.Lerp(fade, 1f, t) : 1f - t;
            }
            return fade;
        }

        /// <summary>The subtitle showing at <paramref name="time"/> (the latest one that started), or null.</summary>
        public string SubtitleAt(float time) =>
            Events.Where(e => e.Kind == CutsceneEventKind.Subtitle && time >= e.Time && time < e.Time + e.Duration)
                .OrderBy(e => e.Time).LastOrDefault()?.Text;

        // ------------------------------------------------------------------
        //  Files
        // ------------------------------------------------------------------

        /// <summary>"Cutscenes\\Intro.cutscene" -> "Cutscenes/Intro.cutscene"; adds the folder and extension to a bare name.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Replace('\\', '/').TrimStart('/');
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) path += Extension;
            if (!path.Contains('/')) path = DefaultFolder + "/" + path;
            return path;
        }

        /// <summary>Every cutscene in the Cutscenes folder (Content-relative paths, sorted).</summary>
        public static IReadOnlyList<string> List()
        {
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in new[] { GameInfo.SourceContentRoot, System.IO.Path.Combine(AppContext.BaseDirectory, "Content") })
            {
                string folder = string.IsNullOrEmpty(root) ? null : System.IO.Path.Combine(root, DefaultFolder);
                if (folder == null || !Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*" + Extension, SearchOption.AllDirectories))
                    found.Add(System.IO.Path.GetRelativePath(root, file).Replace('\\', '/'));
                if (found.Count > 0) break;
            }
            return found.ToList();
        }

        /// <summary>The cutscene at a Content-relative path (or a name in Cutscenes); null when missing or unreadable (logged).</summary>
        public static Cutscene Load(string path)
        {
            string rel = Normalize(path);
            if (rel == null || rel.Split('/').Contains("..")) return null;
            string full = GameInfo.ResolveContentFile(rel);
            try
            {
                if (!File.Exists(full))
                {
                    EditorBridge.Log($"Cutscene: '{rel}' not found");
                    return null;
                }
                var cutscene = JsonSerializer.Deserialize<Cutscene>(File.ReadAllText(full), SceneSerialization.JsonOptions)
                               ?? throw new InvalidDataException("empty file");
                if (cutscene.Version < 1 || cutscene.Version > CurrentVersion)
                    throw new InvalidDataException($"unsupported version {cutscene.Version} (this build reads {CurrentVersion})");
                cutscene.CameraKeys ??= new();
                cutscene.Events ??= new();
                cutscene.Path = rel;
                return cutscene;
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"Cutscene: couldn't read '{rel}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Writes the cutscene into the source Content folder and returns its Content-relative path.</summary>
        public string Save(string path = null)
        {
            string rel = Normalize(path ?? Path) ?? throw new ArgumentException("empty path", nameof(path));
            if (rel.Split('/').Contains("..")) throw new ArgumentException("A cutscene must stay inside Content.", nameof(path));
            string full = System.IO.Path.Combine(GameInfo.SourceContentRoot, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            Version = CurrentVersion;
            string json = JsonSerializer.Serialize(this, SceneSerialization.JsonOptions);
            File.WriteAllText(full + ".tmp", json);
            File.Move(full + ".tmp", full, overwrite: true);
            Path = rel;
            return rel;
        }

        public Cutscene Clone()
        {
            var copy = (Cutscene)MemberwiseClone();
            copy.CameraKeys = CameraKeys.Select(k => k.Clone()).ToList();
            copy.Events = Events.Select(e => e.Clone()).ToList();
            return copy;
        }
    }
}
