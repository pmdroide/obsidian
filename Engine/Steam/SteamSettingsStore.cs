using System;
using System.IO;
using System.Text.Json;
using Engine.Editor;
using Engine.Recources;

namespace Engine.Steam
{
    public sealed record SteamSettings(bool Enabled = false, uint AppId = SteamService.SpacewarAppId);

    internal sealed class SteamSettingsStore
    {
        private const string FileName = "System/SteamSettings.json";
        private readonly string _path;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public SteamSettingsStore(string path = null)
        {
            string sourceRoot = AssetImporter.LocateEngineContentRoot();
            string root = File.Exists(Path.Combine(sourceRoot, "Content.mgcb"))
                ? sourceRoot : Path.Combine(AppContext.BaseDirectory, "Content");
            _path = path ?? Path.Combine(root, FileName);
        }

        public SteamSettings Read()
        {
            try
            {
                if (!File.Exists(_path)) return new SteamSettings();
                var settings = JsonSerializer.Deserialize<SteamSettings>(File.ReadAllText(_path));
                if (settings != null && settings.AppId > 0) return settings;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                EditorBridge.Log("Steam settings could not be loaded: " + ex.Message);
            }
            return new SteamSettings();
        }

        public void Save(SteamSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temporaryPath = _path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
