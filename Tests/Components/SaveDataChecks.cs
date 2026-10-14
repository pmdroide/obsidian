using Engine.Logic;
using Microsoft.Xna.Framework;

internal static class SaveDataChecks
{
    private enum Difficulty { Easy, Hard }

    // Script-style data: public fields and properties, nested lists and dictionaries, XNA types.
    private sealed class GameState
    {
        public string Name = "";
        public int Level;
        public float Health { get; set; }
        public Difficulty Difficulty;
        public Vector3 Position;
        public Quaternion Facing = Quaternion.Identity;
        public Color Tint;
        public List<string> Items = new();
        public Dictionary<string, int> Scores = new();
    }

    public static void Run()
    {
        string previous = SaveData.Folder;
        string folder = Path.Combine(Path.GetTempPath(), "obsidian-savedata-" + Guid.NewGuid().ToString("N"));
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Check(SaveData.DefaultFolder.StartsWith(local, StringComparison.OrdinalIgnoreCase) &&
                  SaveData.DefaultFolder.EndsWith(Path.DirectorySeparatorChar + "Saves"),
                "saves default to %LOCALAPPDATA%/<game name>/Saves");
            SaveData.Folder = folder;
            Check(SaveData.Folder == Path.GetFullPath(folder), "the save folder can be redirected");

            CheckObjects(folder);
            CheckBackups();
            CheckTextAndBytes();
            CheckFiles();
            CheckNames(folder);
            CheckAsync();
        }
        finally
        {
            SaveData.Flush();
            SaveData.Folder = previous == SaveData.DefaultFolder ? null : previous;
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    private static void CheckObjects(string folder)
    {
        Check(!SaveData.Exists("slot1") && SaveData.Load<GameState>("slot1") == null &&
              SaveData.Load("missing", 7) == 7 && !SaveData.TryLoad("slot1", out GameState _),
            "loading a save that doesn't exist returns the fallback");

        var state = new GameState
        {
            Name = "Anna", Level = 3, Health = 72.5f, Difficulty = Difficulty.Hard,
            Position = new Vector3(1.5f, -2, 10), Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1),
            Tint = new Color(10, 20, 30, 40), Items = { "key", "torch" }, Scores = { ["forest"] = 1200 },
        };
        Check(SaveData.Save("slot1", state) && File.Exists(Path.Combine(folder, "slot1.json")),
            "Save writes <name>.json into the save folder");
        string json = File.ReadAllText(Path.Combine(folder, "slot1.json"));
        Check(json.Contains("\"Hard\"") && json.Contains("\"Position\"") && json.Contains("1.5") && json.Contains("\"torch\""),
            "the file is readable JSON with enums by name");

        var loaded = SaveData.Load<GameState>("slot1");
        Check(loaded != null && loaded.Name == "Anna" && loaded.Level == 3 && loaded.Health == 72.5f &&
              loaded.Difficulty == Difficulty.Hard && loaded.Position == state.Position && loaded.Facing == state.Facing &&
              loaded.Tint == state.Tint && loaded.Items.SequenceEqual(state.Items) && loaded.Scores["forest"] == 1200,
            "Load round-trips fields, properties, enums, vectors, quaternions, colours, lists and dictionaries");

        Check(SaveData.Save("highscore", 9001) && SaveData.Load<int>("highscore") == 9001 &&
              SaveData.Save("unlocked", new[] { "a", "b" }) && SaveData.Load<string[]>("unlocked").SequenceEqual(new[] { "a", "b" }),
            "plain values and arrays save too");
        Check(SaveData.Save("profiles/anna", state) && File.Exists(Path.Combine(folder, "profiles", "anna.json")) &&
              SaveData.Load<GameState>("profiles\\anna")?.Name == "Anna",
            "names can use subfolders");

        File.WriteAllText(Path.Combine(folder, "hand-edited.json"),
            "\uFEFF{ \"name\": \"Bo\", // edited by hand\n \"position\": [1, 2, 3], }");
        var edited = SaveData.Load<GameState>("hand-edited");
        Check(edited?.Name == "Bo" && edited.Position == new Vector3(1, 2, 3),
            "hand-edited JSON loads (any case, comments, trailing commas, byte order mark)");
    }

    private static void CheckBackups()
    {
        SaveData.Save("backup-test", new GameState { Level = 1 });
        Check(!File.Exists(SaveData.GetPath("backup-test") + SaveData.BackupSuffix), "a first save has no backup");
        SaveData.Save("backup-test", new GameState { Level = 2 });
        Check(File.Exists(SaveData.GetPath("backup-test") + SaveData.BackupSuffix) &&
              !File.Exists(SaveData.GetPath("backup-test") + SaveData.TempSuffix),
            "overwriting keeps the previous file as .bak and leaves no temp file");

        File.WriteAllText(SaveData.GetPath("backup-test"), "{ broken");
        Check(SaveData.Load<GameState>("backup-test")?.Level == 1, "a damaged save loads its backup");
        File.WriteAllText(SaveData.GetPath("backup-test") + SaveData.BackupSuffix, "also broken");
        Check(SaveData.Load("backup-test", new GameState { Level = -1 }).Level == -1 &&
              !SaveData.TryLoad("backup-test", out GameState _),
            "with both damaged, Load returns the fallback");
        Check(SaveData.Load<int>("slot1", 5) == 5, "loading a save as the wrong type returns the fallback");
    }

    private static void CheckTextAndBytes()
    {
        Check(SaveData.SaveText("notes.txt", "line 1\nção") && SaveData.LoadText("notes.txt") == "line 1\nção" &&
              File.Exists(SaveData.GetPath("notes.txt")) && SaveData.GetPath("notes.txt").EndsWith("notes.txt"),
            "text round-trips as UTF-8 and keeps its own extension");
        Check(SaveData.LoadText("nothing.txt", "default") == "default", "missing text returns the fallback");
        var bytes = new byte[] { 0, 1, 2, 255 };
        Check(SaveData.SaveBytes("photo.bin", bytes) && SaveData.LoadBytes("photo.bin").SequenceEqual(bytes) &&
              SaveData.LoadBytes("nothing.bin") == null,
            "bytes round-trip");
    }

    private static void CheckFiles()
    {
        var names = SaveData.List();
        Check(names.Contains("slot1") && names.Contains("profiles/anna") && names.Contains("notes.txt") &&
              !names.Any(n => n.EndsWith(SaveData.BackupSuffix)),
            "List returns loadable names without .json or backups");
        Check(SaveData.List("profiles").SequenceEqual(new[] { "profiles/anna" }) && SaveData.List("nowhere").Count == 0,
            "List can look inside a subfolder");
        Check(SaveData.GetLastWriteTime("slot1") is DateTime written && (DateTime.Now - written).TotalMinutes < 5 &&
              SaveData.GetLastWriteTime("nothing") == null,
            "GetLastWriteTime reports when a save was written");
        Check(SaveData.Delete("backup-test") && !SaveData.Exists("backup-test") &&
              !File.Exists(SaveData.GetPath("backup-test") + SaveData.BackupSuffix) && !SaveData.Delete("backup-test"),
            "Delete removes a save with its backup");
    }

    private static void CheckNames(string folder)
    {
        foreach (string bad in new[] { "", " ", "../escape", "a/../../b", "C:\\Windows\\x", "/rooted", "a//b", "x.bak", "x.json.tmp", "a:b", "what?" })
        {
            bool threw = false;
            try { SaveData.Save(bad, 1); }
            catch (ArgumentException) { threw = true; }
            Check(threw, $"the save name '{bad}' is refused");
        }
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(folder)!, "escape*").Any(), "nothing is written outside the save folder");
    }

    private static void CheckAsync()
    {
        var tasks = Enumerable.Range(1, 20).Select(i => SaveData.SaveAsync("async", new GameState { Level = i })).ToArray();
        Check(SaveData.Load<GameState>("async")?.Level == 20 && tasks.All(t => t.IsCompleted && t.Result),
            "async saves to one file finish in order before it is read");

        var state = new GameState { Level = 1 };
        var write = SaveData.SaveAsync("snapshot", state);
        state.Level = 99;
        write.Wait();
        Check(SaveData.Load<GameState>("snapshot")?.Level == 1, "SaveAsync saves the data as it was when called");

        SaveData.SaveAsync("flushed", 1);
        SaveData.Flush();
        Check(File.Exists(SaveData.GetPath("flushed")), "Flush waits for pending async saves");
        Check(SaveData.Save("async", new GameState { Level = 21 }) && SaveData.Load<GameState>("async")?.Level == 21,
            "a normal save after async ones wins");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
