using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Entities;

namespace Engine.Recources
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ClassificationKind { Layer, Tag, Role }
    public enum ClassificationEdit { Add, Rename, Remove }

    public sealed record ClassificationDefinition(string Id, string Name, ClassificationKind Kind, bool IsSystem = false);

    /// <summary>Project-wide gameobject classifications. Stable IDs keep saved scenes and prefabs valid across renames.</summary>
    public sealed class ClassificationCatalog
    {
        public const string FileName = "System/Classifications.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        public static ClassificationCatalog Project { get; internal set; } = new(Path.Combine(GameInfo.ResolveContentRoot(), FileName));
        private readonly string _path;
        private volatile IReadOnlyList<ClassificationDefinition> _entries;
        public IReadOnlyList<ClassificationDefinition> Entries => _entries;

        public static string DefaultId(ClassificationKind kind) => kind == ClassificationKind.Tag ? "Untagged" : "Default";
        private static List<ClassificationDefinition> Defaults()
        {
            var entries = new List<ClassificationDefinition>();
            foreach (string name in new[] { "Default", "UI", "IgnoreRaycast" }) entries.Add(new(name, name, ClassificationKind.Layer, true));
            foreach (string name in new[] { "Untagged", "Player", "Enemy", "static", "renderable" }) entries.Add(new(name, name, ClassificationKind.Tag, true));
            foreach (string name in Enum.GetNames<GameObjectRole>()) entries.Add(new(name, name, ClassificationKind.Role, true));
            return entries;
        }

        public ClassificationCatalog(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            var entries = Defaults();
            if (File.Exists(path))
            {
                // Invalid project settings are surfaced, rather than silently overwriting custom IDs on the next edit.
                var data = JsonSerializer.Deserialize<List<ClassificationDefinition>>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("Empty classification catalog.");
                foreach (var raw in data)
                {
                    if (raw == null || !Enum.IsDefined(raw.Kind)) throw new InvalidDataException("Invalid classification category.");
                    // System definitions always come from engine code; edited files cannot override or unlock them.
                    if (entries.Any(e => e.Kind == raw.Kind && e.Id == raw.Id && e.IsSystem)) continue;
                    if (!Guid.TryParseExact(raw.Id, "N", out _) || !ValidName(raw.Name, out _) ||
                        entries.Any(e => e.Kind == raw.Kind && (e.Id == raw.Id || SameName(e.Name, raw.Name))))
                        throw new InvalidDataException("Invalid or duplicate classification entry.");
                    entries.Add(raw with { Name = raw.Name.Trim(), IsSystem = false });
                }
            }
            _entries = entries.AsReadOnly();
        }

        public static void LoadProject() => Project = new(Path.Combine(GameInfo.ResolveContentRoot(), FileName));
        public ClassificationDefinition Find(ClassificationKind kind, string id) => Entries.FirstOrDefault(e => e.Kind == kind && e.Id == id);
        public ClassificationDefinition FindName(ClassificationKind kind, string name) => Entries.FirstOrDefault(e => e.Kind == kind && SameName(e.Name, name));
        public string ResolveId(ClassificationKind kind, string id) => Find(kind, id)?.Id ?? DefaultId(kind);
        public string NameOf(ClassificationKind kind, string id) => Find(kind, id)?.Name ?? DefaultId(kind);

        public bool Add(ClassificationKind kind, string name, out string error)
        {
            if (!Enum.IsDefined(kind)) { error = "Choose a valid category."; return false; }
            if (!ValidName(name, out error)) return false;
            if (FindName(kind, name.Trim()) != null) { error = "That name already exists in this category."; return false; }
            var entries = Entries.ToList();
            entries.Add(new(Guid.NewGuid().ToString("N"), name.Trim(), kind));
            return Commit(entries, out error);
        }

        public bool Rename(ClassificationKind kind, string id, string name, out string error)
        {
            var entry = Find(kind, id);
            if (!CanEdit(entry, out error) || !ValidName(name, out error)) return false;
            var duplicate = FindName(kind, name.Trim());
            if (duplicate != null && duplicate.Id != id) { error = "That name already exists in this category."; return false; }
            return Commit(Entries.Select(e => e == entry ? e with { Name = name.Trim() } : e).ToList(), out error);
        }

        public bool Remove(ClassificationKind kind, string id, out string error)
        {
            var entry = Find(kind, id);
            if (!CanEdit(entry, out error)) return false;
            return Commit(Entries.Where(e => e != entry).ToList(), out error);
        }

        private static bool CanEdit(ClassificationDefinition entry, out string error)
        {
            error = entry == null ? "This entry no longer exists." : entry.IsSystem ? "System defaults cannot be changed or removed." : null;
            return error == null;
        }

        private static bool SameName(string a, string b) => string.Equals(a, b?.Trim(), StringComparison.OrdinalIgnoreCase);
        private static bool ValidName(string name, out string error)
        {
            error = string.IsNullOrWhiteSpace(name) ? "Enter a name." : name.Trim().Length > 64 ? "Names must be 64 characters or fewer." :
                name.Any(char.IsControl) ? "Names cannot contain control characters." : null;
            return error == null;
        }

        private bool Commit(List<ClassificationDefinition> entries, out string error)
        {
            string temporary = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
                File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Where(e => !e.IsSystem).ToList(), JsonOptions));
                File.Move(temporary, _path, true);
                _entries = entries.AsReadOnly();
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = "Couldn't save project classifications: " + ex.Message;
                return false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
