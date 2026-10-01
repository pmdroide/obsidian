using System;
using System.IO;
using System.Text.Json;
using Engine.Editor;

namespace Engine.Recources
{
    /// <summary>
    /// Standalone-game settings edited from Anvil's Game Settings dialog: window title, icon,
    /// size, resizability, fullscreen, VSync and FPS cap. Persisted as
    /// <c>Content/System/GameInfo.json</c>; the icon is a .ico written next to it by the editor.
    /// Only applied when the engine runs standalone — never to the viewport embedded in Anvil.
    ///
    /// Plain file I/O only — safe to call from the editor (UI) thread as well as the game thread.
    /// </summary>
    public static class GameInfo
    {
        //Content-relative. System/ is engine-managed and hidden from Anvil's Assets panel.
        public const string FileName = "System/GameInfo.json";

        //Content-relative path the editor writes the converted icon to.
        public const string DefaultIconPath = "System/GameIcon.ico";

        public sealed class Data
        {
            public string WindowTitle { get; set; } = "Engine";

            //Content-relative path to a .ico file, or null/empty for the default executable icon.
            public string IconPath { get; set; }

            //Client size of the game window (ignored while Fullscreen).
            public int WindowWidth { get; set; } = 1280;
            public int WindowHeight { get; set; } = 720;

            public bool AllowResizing { get; set; } = true;

            //Borderless fullscreen at the desktop resolution.
            public bool Fullscreen { get; set; }

            public bool VSync { get; set; }

            //Frames per second limit; 0 = unlimited. When set it takes precedence over VSync.
            public int FpsCap { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static Data Current { get; private set; } = new Data();

        /// <summary>Absolute path of the source <c>Engine/Content</c> folder the editor writes to.</summary>
        public static string SourceContentRoot => AssetImporter.LocateEngineContentRoot();

        /// <summary>
        /// Content folder to read from. Prefers the source <c>Engine/Content</c> of a dev checkout
        /// (always the freshest copy the editor wrote), falling back to the Content folder next to
        /// the executable (shipped builds, where the file is copied in by the build).
        /// </summary>
        public static string ResolveContentRoot()
        {
            string source = SourceContentRoot;
            if (File.Exists(Path.Combine(source, FileName))) return source;
            return Path.Combine(AppContext.BaseDirectory, "Content");
        }

        /// <summary>Reads GameInfo.json from disk without touching <see cref="Current"/>.</summary>
        public static Data Read()
        {
            string path = Path.Combine(ResolveContentRoot(), FileName);
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOptions) ?? new Data();
            }
            catch (Exception e)
            {
                EditorBridge.Log($"GameInfo: failed to read '{path}': {e.Message}");
            }
            return new Data();
        }

        /// <summary>Reads GameInfo.json into <see cref="Current"/> (engine startup).</summary>
        public static Data Load() => Current = Read();

        /// <summary>
        /// Writes the settings to the source <c>Engine/Content</c> folder. Dev runs read that copy
        /// directly (see <see cref="ResolveContentRoot"/>); the build copies it next to the exe.
        /// </summary>
        public static void Save(Data data)
        {
            string json = JsonSerializer.Serialize(data ?? new Data(), JsonOptions);
            string path = Path.Combine(SourceContentRoot, FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Pushes the display/frame-rate settings into <see cref="GameSettings"/>. The engine then
        /// sizes the back buffer from g_screenwidth/height and applies the FPS limit on the next Draw.
        /// </summary>
        public static void ApplyToGameSettings()
        {
            Data d = Current;
            if (d.WindowWidth > 0) GameSettings.g_screenwidth = d.WindowWidth;
            if (d.WindowHeight > 0) GameSettings.g_screenheight = d.WindowHeight;
            GameSettings.g_vsync = d.VSync;
            GameSettings.g_fixedfps = Math.Max(0, d.FpsCap);
        }

        /// <summary>Absolute path of the configured icon, or null when unset / missing on disk.</summary>
        public static string ResolveIconFile()
        {
            if (string.IsNullOrWhiteSpace(Current.IconPath)) return null;
            string path = Path.Combine(ResolveContentRoot(), Current.IconPath);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Applies the title and icon to MonoGame's window. WindowsDX hosts the game in a WinForms
        /// Form, so the icon goes through <c>Form.Icon</c> (which re-applies it if the handle is
        /// ever recreated) rather than a raw WM_SETICON.
        /// </summary>
        public static void ApplyToWindow(Microsoft.Xna.Framework.GameWindow window)
        {
            if (window == null) return;

            if (!string.IsNullOrWhiteSpace(Current.WindowTitle))
                window.Title = Current.WindowTitle;

            string icon = ResolveIconFile();
            if (icon == null) return;
            try
            {
                if (System.Windows.Forms.Control.FromHandle(window.Handle) is System.Windows.Forms.Form form)
                    form.Icon = new System.Drawing.Icon(icon);
            }
            catch (Exception e)
            {
                EditorBridge.Log($"GameInfo: failed to apply icon '{icon}': {e.Message}");
            }
        }
    }
}
