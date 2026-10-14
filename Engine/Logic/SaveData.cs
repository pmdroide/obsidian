using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Editor;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Logic;

/// <summary>
/// Save files for scripts and the engine: save games, player settings, unlocks, high scores.
/// Files live in the per-user save folder, <c>%LOCALAPPDATA%/&lt;game name&gt;/Saves</c> (game name = Anvil >
/// Game Settings > window name), shared by Play inside Anvil and the built game. Names are paths relative to
/// that folder (<c>"slot1"</c>, <c>"profiles/anna"</c>); a name without an extension gets <c>.json</c>.
///
/// Objects are saved as JSON (<see cref="JsonOptions"/>: public properties and fields, enums by name, XNA
/// vectors/colours as arrays). Every write goes to a temporary file first and then replaces the old one,
/// which is kept as <c>&lt;file&gt;.bak</c>; a damaged file is loaded from that backup. Writes to one file
/// happen in the order they were made, also with <see cref="SaveAsync{T}"/>. Safe from any thread.
///
/// A bad name (empty, rooted, <c>..</c>, invalid characters) throws <see cref="ArgumentException"/>; disk and
/// format errors are logged and reported through the return value instead.
/// </summary>
public static class SaveData
{
    public const string DefaultExtension = ".json";
    public const string BackupSuffix = ".bak";
    public const string TempSuffix = ".tmp";

    private static readonly object Gate = new();
    // Last write queued per file, so writes to one file never overtake each other.
    private static readonly Dictionary<string, Task<bool>> PendingWrites = new(StringComparer.OrdinalIgnoreCase);
    private static string _folder;
    private static string _defaultFolder;

    /// <summary>JSON settings for <see cref="Save{T}"/>/<see cref="Load{T}"/>; also usable for your own serialization.</summary>
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Absolute save folder. Defaults to <see cref="DefaultFolder"/>; set it to save somewhere else (tests, a
    /// portable build), or to null to go back to the default. Created on the first write.
    /// </summary>
    public static string Folder
    {
        get => _folder ?? DefaultFolder;
        set => _folder = string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }

    /// <summary><c>%LOCALAPPDATA%/&lt;game name&gt;/Saves</c>, from GameInfo.json's window title.</summary>
    public static string DefaultFolder => _defaultFolder ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), GameFolderName(), "Saves");

    // ----------------------------------------------------------------------------------------------------
    // Objects (JSON)
    // ----------------------------------------------------------------------------------------------------

    /// <summary>Saves <paramref name="data"/> as JSON. Returns false (and logs) when it couldn't be written.</summary>
    public static bool Save<T>(string name, T data)
    {
        string path = GetPath(name);
        if (!TrySerialize(name, data, out byte[] bytes)) return false;
        return QueueWrite(path, bytes).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Like <see cref="Save{T}"/>, but writes on a worker thread so a large save doesn't stall the frame.
    /// The data is serialized right away, so changing it afterwards doesn't affect what is saved.
    /// </summary>
    public static Task<bool> SaveAsync<T>(string name, T data)
    {
        string path = GetPath(name);
        if (!TrySerialize(name, data, out byte[] bytes)) return Task.FromResult(false);
        return QueueWrite(path, bytes, background: true);
    }

    /// <summary>Loads a saved object, or returns <paramref name="fallback"/> when there is none or it can't be read.</summary>
    public static T Load<T>(string name, T fallback = default) =>
        TryLoad(name, out T data) ? data : fallback;

    /// <summary>Loads a saved object. False when the file doesn't exist or neither it nor its backup can be read.</summary>
    public static bool TryLoad<T>(string name, out T data)
    {
        string path = GetPath(name);
        data = default;
        if (TryReadBytes(path, out byte[] bytes) && TryDeserialize(path, bytes, out data)) return true;
        string backup = path + BackupSuffix;
        if (!File.Exists(backup) || !TryReadBytes(backup, out bytes) || !TryDeserialize(backup, bytes, out data))
            return false;
        if (File.Exists(path)) EditorBridge.Log($"SaveData: '{name}' is damaged; loaded its backup.");
        return true;
    }

    // ----------------------------------------------------------------------------------------------------
    // Text and bytes
    // ----------------------------------------------------------------------------------------------------

    /// <summary>Saves text (UTF-8). Returns false (and logs) when it couldn't be written.</summary>
    public static bool SaveText(string name, string text) =>
        QueueWrite(GetPath(name), Encoding.UTF8.GetBytes(text ?? "")).GetAwaiter().GetResult();

    /// <summary>Loads saved text, or <paramref name="fallback"/> when there is none.</summary>
    public static string LoadText(string name, string fallback = null) =>
        TryReadBytes(GetPath(name), out byte[] bytes) ? Encoding.UTF8.GetString(StripBom(bytes)) : fallback;

    /// <summary>Saves raw bytes. Returns false (and logs) when it couldn't be written.</summary>
    public static bool SaveBytes(string name, byte[] bytes) =>
        QueueWrite(GetPath(name), (byte[])(bytes ?? Array.Empty<byte>()).Clone()).GetAwaiter().GetResult();

    /// <summary>Loads saved bytes, or null when there are none.</summary>
    public static byte[] LoadBytes(string name) =>
        TryReadBytes(GetPath(name), out byte[] bytes) ? bytes : null;

    // ----------------------------------------------------------------------------------------------------
    // Files
    // ----------------------------------------------------------------------------------------------------

    /// <summary>Whether a save with this name exists.</summary>
    public static bool Exists(string name)
    {
        string path = GetPath(name);
        WaitForWrites(path);
        return File.Exists(path);
    }

    /// <summary>Deletes a save and its backup. True when something was deleted.</summary>
    public static bool Delete(string name)
    {
        string path = GetPath(name);
        WaitForWrites(path);
        bool deleted = false;
        try
        {
            foreach (string file in new[] { path, path + BackupSuffix, path + TempSuffix })
            {
                if (!File.Exists(file)) continue;
                File.Delete(file);
                deleted |= file == path;
            }
        }
        catch (Exception e)
        {
            EditorBridge.Log($"SaveData: failed to delete '{name}': {e.Message}");
        }
        return deleted;
    }

    /// <summary>
    /// Names of the saves in the save folder or one of its subfolders (<c>"profiles"</c>), sorted, without
    /// backups. The <c>.json</c> extension is left off, so each name can be passed straight to <see cref="Load{T}"/>.
    /// </summary>
    public static IReadOnlyList<string> List(string subfolder = null)
    {
        WaitForWrites(null);
        string root = Folder;
        string directory = string.IsNullOrWhiteSpace(subfolder) ? root : GetPath(subfolder, addExtension: false);
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(f =>
                {
                    string relative = Path.GetRelativePath(root, f).Replace('\\', '/');
                    return relative.EndsWith(DefaultExtension, StringComparison.OrdinalIgnoreCase)
                        ? relative[..^DefaultExtension.Length]
                        : relative;
                })
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e)
        {
            EditorBridge.Log($"SaveData: failed to list '{directory}': {e.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>When a save was last written (local time), or null when it doesn't exist.</summary>
    public static DateTime? GetLastWriteTime(string name)
    {
        string path = GetPath(name);
        WaitForWrites(path);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    /// <summary>Waits until every <see cref="SaveAsync{T}"/> write has finished (the engine calls it on exit).</summary>
    public static void Flush() => WaitForWrites(null);

    /// <summary>
    /// Absolute path of a save. Adds <c>.json</c> when the name has no extension. Throws
    /// <see cref="ArgumentException"/> for names that would leave the save folder or aren't valid file names.
    /// </summary>
    public static string GetPath(string name) => GetPath(name, addExtension: true);

    private static string GetPath(string name, bool addExtension)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A save name is required.", nameof(name));
        string relative = name.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ArgumentException($"Save name '{name}' must be relative to the save folder.", nameof(name));
        char[] invalid = Path.GetInvalidFileNameChars();
        string[] parts = relative.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(invalid) >= 0 || part.EndsWith('.') || part.EndsWith(' '))
                throw new ArgumentException($"Save name '{name}' is not a valid file name.", nameof(name));
        }
        if (relative.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase) ||
            relative.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Save name '{name}' uses an extension reserved for backups.", nameof(name));
        if (addExtension && Path.GetExtension(parts[^1]).Length == 0) relative += DefaultExtension;

        string root = Folder;
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Save name '{name}' must stay inside the save folder.", nameof(name));
        return path;
    }

    // ----------------------------------------------------------------------------------------------------
    // Writing
    // ----------------------------------------------------------------------------------------------------

    private static Task<bool> QueueWrite(string path, byte[] bytes, bool background = false)
    {
        lock (Gate)
        {
            PendingWrites.TryGetValue(path, out Task<bool> previous);
            Task<bool> write;
            if (previous == null && !background)
                write = Task.FromResult(WriteFile(path, bytes));
            else
                write = (previous ?? Task.FromResult(true)).ContinueWith(_ => WriteFile(path, bytes),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            PendingWrites[path] = write;
            if (!write.IsCompleted)
                write.ContinueWith(_ =>
                {
                    lock (Gate)
                    {
                        if (PendingWrites.TryGetValue(path, out Task<bool> last) && last == write) PendingWrites.Remove(path);
                    }
                }, TaskScheduler.Default);
            else
                PendingWrites.Remove(path);
            return write;
        }
    }

    // Temp file first, then swap it in and keep the old file as the backup: a crash mid-write never loses the save.
    private static bool WriteFile(string path, byte[] bytes)
    {
        string temp = path + TempSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + BackupSuffix, ignoreMetadataErrors: true);
            else File.Move(temp, path);
            return true;
        }
        catch (Exception e)
        {
            EditorBridge.Log($"SaveData: failed to write '{path}': {e.Message}");
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* keep the original error */ }
            return false;
        }
    }

    private static void WaitForWrites(string path)
    {
        Task[] pending;
        lock (Gate)
        {
            pending = path == null
                ? PendingWrites.Values.Cast<Task>().ToArray()
                : PendingWrites.TryGetValue(path, out Task<bool> write) ? new Task[] { write } : Array.Empty<Task>();
        }
        if (pending.Length > 0) Task.WaitAll(pending);
    }

    // ----------------------------------------------------------------------------------------------------
    // Reading and JSON
    // ----------------------------------------------------------------------------------------------------

    private static bool TryReadBytes(string path, out byte[] bytes)
    {
        WaitForWrites(path);
        bytes = null;
        if (!File.Exists(path)) return false;
        try
        {
            bytes = File.ReadAllBytes(path);
            return true;
        }
        catch (Exception e)
        {
            EditorBridge.Log($"SaveData: failed to read '{path}': {e.Message}");
            return false;
        }
    }

    private static bool TrySerialize<T>(string name, T data, out byte[] bytes)
    {
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
            return true;
        }
        catch (Exception e)
        {
            EditorBridge.Log($"SaveData: can't save '{name}' as JSON ({typeof(T).Name}): {e.Message}");
            bytes = null;
            return false;
        }
    }

    private static bool TryDeserialize<T>(string path, byte[] bytes, out T data)
    {
        try
        {
            data = JsonSerializer.Deserialize<T>(StripBom(bytes), JsonOptions);
            return true;
        }
        catch (Exception e)
        {
            EditorBridge.Log($"SaveData: failed to read '{path}' as {typeof(T).Name}: {e.Message}");
            data = default;
            return false;
        }
    }

    // Files edited by hand (Notepad) may start with a UTF-8 byte order mark.
    private static ReadOnlySpan<byte> StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsSpan(3) : bytes;

    private static string GameFolderName()
    {
        string title = null;
        try { title = GameInfo.Read().WindowTitle; }
        catch { /* fall back below */ }
        if (string.IsNullOrWhiteSpace(title)) title = "Obsidian";
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new string(title.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        return clean.Length == 0 ? "Obsidian" : clean;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FloatArrayConverter<Vector2>(2, v => new[] { v.X, v.Y }, a => new Vector2(a[0], a[1])));
        options.Converters.Add(new FloatArrayConverter<Vector3>(3, v => new[] { v.X, v.Y, v.Z }, a => new Vector3(a[0], a[1], a[2])));
        options.Converters.Add(new FloatArrayConverter<Vector4>(4, v => new[] { v.X, v.Y, v.Z, v.W }, a => new Vector4(a[0], a[1], a[2], a[3])));
        options.Converters.Add(new FloatArrayConverter<Quaternion>(4, q => new[] { q.X, q.Y, q.Z, q.W }, a => new Quaternion(a[0], a[1], a[2], a[3])));
        options.Converters.Add(new FloatArrayConverter<Color>(4, c => new float[] { c.R, c.G, c.B, c.A },
            a => new Color((int)Math.Clamp(a[0], 0, 255), (int)Math.Clamp(a[1], 0, 255), (int)Math.Clamp(a[2], 0, 255), (int)Math.Clamp(a[3], 0, 255))));
        options.Converters.Add(new FloatArrayConverter<Point>(2, p => new float[] { p.X, p.Y }, a => new Point((int)a[0], (int)a[1])));
        options.Converters.Add(new FloatArrayConverter<Matrix>(16, m => new[]
            {
                m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
            },
            a => new Matrix(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7],
                a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15])));
        return options;
    }

    /// <summary>An XNA value type as a JSON number array: Vector3 = [x,y,z], Color = [r,g,b,a] (0-255).</summary>
    private sealed class FloatArrayConverter<T> : JsonConverter<T>
    {
        private readonly int _count;
        private readonly Func<T, float[]> _write;
        private readonly Func<float[], T> _read;

        public FloatArrayConverter(int count, Func<T, float[]> write, Func<float[], T> read)
        {
            _count = count;
            _write = write;
            _read = read;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected an array of {_count} numbers for {typeof(T).Name}.");
            var values = new float[_count];
            // Missing colour channels default to opaque white.
            if (typeof(T) == typeof(Color)) Array.Fill(values, 255f);
            int i = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                float value = reader.TokenType == JsonTokenType.String
                    ? float.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : reader.GetSingle();
                if (i < _count) values[i] = value;
                i++;
            }
            return _read(values);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (float component in _write(value))
            {
                if (float.IsFinite(component)) writer.WriteNumberValue(component);
                else writer.WriteStringValue(component.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            writer.WriteEndArray();
        }
    }
}
