using System;
using System.Globalization;
using System.IO;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Logic
{
    /// <summary>
    /// Look-dev capture of the standalone engine (Source 2 look, phase 0): renders a scene in Edit mode and saves the
    /// frame as a PNG, without the intro, the editor overlays or the debug stats, then quits. For comparing the look
    /// against reference captures and before/after a renderer change:
    /// <code>Engine.exe --scene Scenes/LookDev.obsc --shot lookdev.png [--camera px,py,pz,tx,ty,tz] [--frames 90]
    ///     [--size 1280x720] [--rendermode IndirectDiffuse] [--play] [--bake [--save]]</code>
    /// --bake bakes the scene's probe volume first (Inspector > Lighting > Bake) and --save writes the scene with it.
    /// --set g_name=value (repeatable) sets a GameSettings field or property, e.g. --set g_occlusionculling=true.
    /// Paths are relative to the Content folder (scene) or the working directory (shot).
    /// </summary>
    public static class LookDevCapture
    {
        public static bool Active { get; private set; }
        public static string ScenePath { get; private set; }
        public static string ShotPath { get; private set; }
        public static int Frames { get; private set; } = 90;
        public static bool Play { get; private set; }
        public static Vector3? CameraPosition { get; private set; }
        public static Vector3? CameraTarget { get; private set; }
        public static Point? Size { get; private set; }
        public static Renderer.Renderer.RenderModes? RenderMode { get; private set; }
        public static bool Bake { get; private set; }
        public static bool Save { get; private set; }
        private static MainSceneLogic _sceneLogic;
        private static readonly System.Collections.Generic.List<(string Name, string Value)> Overrides = new();
        private static bool _bakeStarted;

        private static int _frame;

        static LookDevCapture() => Parse(Environment.GetCommandLineArgs());

        /// <summary>Reads the capture options; <see cref="Active"/> when a scene and a shot path are both given.</summary>
        public static void Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--scene": ScenePath = next; i++; break;
                    case "--shot": ShotPath = next; i++; break;
                    case "--frames": if (int.TryParse(next, out int frames)) Frames = Math.Max(1, frames); i++; break;
                    case "--play": Play = true; break;
                    case "--bake": Bake = true; break;
                    case "--save": Save = true; break;
                    case "--set":
                        int eq = next?.IndexOf('=') ?? -1;
                        if (eq > 0) Overrides.Add((next.Substring(0, eq), next.Substring(eq + 1)));
                        i++;
                        break;
                    case "--camera":
                        float[] v = ParseFloats(next);
                        if (v.Length == 6)
                        {
                            CameraPosition = new Vector3(v[0], v[1], v[2]);
                            CameraTarget = new Vector3(v[3], v[4], v[5]);
                        }
                        i++;
                        break;
                    case "--size":
                        string[] wh = (next ?? "").Split('x');
                        if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h))
                            Size = new Point(Math.Max(64, w), Math.Max(64, h));
                        i++;
                        break;
                    case "--rendermode":
                        if (Enum.TryParse(next, true, out Renderer.Renderer.RenderModes mode)) RenderMode = mode;
                        i++;
                        break;
                }
            }
            Active = !string.IsNullOrEmpty(ScenePath) && !string.IsNullOrEmpty(ShotPath);
        }

        private static float[] ParseFloats(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<float>();
            string[] parts = text.Split(',');
            var values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return Array.Empty<float>();
            return values;
        }

        /// <summary>Settings before the renderer creates its targets: window size, no editor overlays or stats.</summary>
        public static void ApplySettings()
        {
            if (!Active) return;
            if (Size is Point size)
            {
                GameSettings.g_screenwidth = size.X;
                GameSettings.g_screenheight = size.Y;
            }
            GameSettings.e_enableeditor = false;
            GameSettings.e_showgrid = false;
            GameSettings.u_showdisplayinfo = 0;
            if (RenderMode is Renderer.Renderer.RenderModes renderMode) GameSettings.g_rendermode = renderMode;
            foreach (var (name, value) in Overrides) ApplyOverride(name, value);
        }

        private static void ApplyOverride(string name, string text)
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase;
            System.Reflection.FieldInfo field = typeof(GameSettings).GetField(name, flags);
            System.Reflection.PropertyInfo property = field == null ? typeof(GameSettings).GetProperty(name, flags) : null;
            Type type = field?.FieldType ?? property?.PropertyType;
            if (type == null) { EditorBridge.Log("LookDevCapture: no GameSettings." + name); return; }
            try
            {
                object value = type.IsEnum ? Enum.Parse(type, text, true) : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                if (field != null) field.SetValue(null, value); else property.SetValue(null, value);
            }
            catch (Exception e) { EditorBridge.Log($"LookDevCapture: --set {name}={text}: {e.Message}"); }
        }

        /// <summary>Loads the scene (Edit mode unless --play) and places the camera.</summary>
        public static bool Start(MainSceneLogic sceneLogic)
        {
            string path = ScenePath;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppContext.BaseDirectory, "Content", path);
            if (sceneLogic.SceneManager.LoadScene(path) == null)
            {
                EditorBridge.Log("LookDevCapture: can't load " + path);
                return false;
            }
            _sceneLogic = sceneLogic;
            if (Bake) _bakeStarted = sceneLogic.Lighting.StartBake(sceneLogic.ActiveScene);
            if (Play) sceneLogic.PlayMode.Play();
            PlaceCamera(sceneLogic);
            return true;
        }

        public static void PlaceCamera(MainSceneLogic sceneLogic)
        {
            Camera camera = sceneLogic.Camera;
            if (camera == null) return;
            //Without --camera: where the scene's main camera stands
            Camera main = sceneLogic.ActiveScene?.MainCamera;
            Vector3? position = CameraPosition ?? main?.Position;
            Vector3? target = CameraTarget ?? (main != null ? main.Position + main.Forward : (Vector3?)null);
            if (position == null || target == null || ReferenceEquals(camera, main) && CameraPosition == null) return;
            camera.Position = position.Value;
            camera.Lookat = target.Value;
            camera.HasChanged = true;
        }

        /// <summary>Call after each drawn frame (before Present). Saves the back buffer once enough frames settled.</summary>
        /// <returns>True when the shot was written and the engine should quit.</returns>
        public static bool AfterDraw(GraphicsDevice graphics, Renderer.Renderer renderer = null)
        {
            if (!Active) return false;
            //A bake finishes first (frames don't count until the probes are in)
            if (_bakeStarted && _sceneLogic != null)
            {
                if (_sceneLogic.Lighting.IsBaking || _sceneLogic.ActiveScene?.BakedProbes == null &&
                    _sceneLogic.Lighting.Status.State == Renderer.Lighting.LightingBakeState.Baking) return false;
                _bakeStarted = false;
                EditorBridge.Log("LookDevCapture: bake " + _sceneLogic.Lighting.Status.Message);
                if (Save && _sceneLogic.ActiveScene?.FilePath is string path)
                {
                    //Into the source Content folder, not the build output copy the engine loaded
                    string rel = Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "Content"), path);
                    string source = Recources.GameInfo.SourceContentRoot;
                    if (!rel.StartsWith("..") && !string.IsNullOrEmpty(source)) path = Path.Combine(source, rel);
                    EditorBridge.Log("LookDevCapture: saved " + path + ": " + _sceneLogic.SceneManager.SaveScene(path));
                }
            }
            if (++_frame < Frames) return false;
            if (renderer != null)
                EditorBridge.Log($"LookDevCapture: auto exposure {renderer.ReadAutoExposureEV():0.00} EV, " +
                    $"compensation {GameSettings.Exposure:0.00} EV, metering {GameSettings.g_ExposureMetering}");

            int width = graphics.PresentationParameters.BackBufferWidth;
            int height = graphics.PresentationParameters.BackBufferHeight;
            var pixels = new Color[width * height];
            graphics.GetBackBufferData(pixels);
            for (int i = 0; i < pixels.Length; i++) pixels[i].A = 255;
            using (var texture = new Texture2D(graphics, width, height))
            {
                texture.SetData(pixels);
                string dir = Path.GetDirectoryName(Path.GetFullPath(ShotPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var stream = File.Create(ShotPath);
                texture.SaveAsPng(stream, width, height);
            }
            EditorBridge.Log("LookDevCapture: wrote " + Path.GetFullPath(ShotPath));
            return true;
        }
    }
}
