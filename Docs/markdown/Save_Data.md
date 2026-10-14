# Save data

`SaveData` (`Engine/Logic/SaveData.cs`, namespace `Engine.Logic`) saves and loads files for your game:
save games, player settings, unlocked levels and high scores. It's a static class, so any script
behaviour, component or engine code can call it.

```csharp
using Engine.Logic;

SaveData.Save("slot1", state);                    // writes slot1.json
GameState loaded = SaveData.Load<GameState>("slot1");
```

## Where the files go

Saves live in a per-user folder:

```
%LOCALAPPDATA%\<game name>\Saves\
```

- **Game name** is the window name from Anvil > Game Settings (`Content/System/GameInfo.json`).
  Characters that can't be in a folder name become `_`. The folder name is read once, the first time a
  save is used. If you rename the game, restart Anvil so saves move to the new folder.
- Play inside Anvil and the built game use the **same folder**. A save made while testing in Anvil
  shows up in the built game, and the other way round.
- The folder is created on the first write.
- `SaveData.Folder` is the folder in use. Set it to save somewhere else, such as a portable build that
  saves next to the exe or a test's temp folder. Set it to `null` to go back to `SaveData.DefaultFolder`.

Saves are **not** part of the project. Builds don't ship them, and Delete Project doesn't remove them.

## Names

A save name is a path relative to the save folder:

| Name | File |
| --- | --- |
| `"slot1"` | `Saves\slot1.json` |
| `"profiles/anna"` (or `profiles\anna`) | `Saves\profiles\anna.json` |
| `"notes.txt"` | `Saves\notes.txt` |
| `"photo.png"` | `Saves\photo.png` |

- A name without an extension gets `.json`, whichever function you call.
- Subfolders are created as needed.
- These names throw `ArgumentException`: empty names, absolute paths (`C:\...`, `/...`), `..`,
  characters Windows doesn't allow in file names (`: ? * < > | "`), and names ending in `.bak` or
  `.tmp`, which are reserved for backups. A name can never reach outside the save folder.
- `SaveData.GetPath(name)` returns the absolute path a name maps to.

## Saving and loading objects

`Save<T>` writes any object as indented JSON. `Load<T>` reads it back.

```csharp
public sealed class GameState
{
    public string Scene = "Level1";
    public int Coins;
    public float Health = 100;
    public Difficulty Difficulty;          // enum: saved by name ("Hard")
    public Vector3 Position;               // saved as [x, y, z]
    public List<string> Inventory = new();
    public Dictionary<string, int> BestTimes = new();
}
```

| Function | What it does |
| --- | --- |
| `bool Save<T>(name, data)` | Writes the object. Returns false, and logs the reason, if it couldn't be written. |
| `Task<bool> SaveAsync<T>(name, data)` | Same, but the file is written on a worker thread, so a big save doesn't stall the frame. |
| `T Load<T>(name, fallback = default)` | Reads the object. Returns `fallback` if there's no save or it can't be read. |
| `bool TryLoad<T>(name, out T data)` | Same, but returns whether a save was actually loaded. |

What can be saved:

- Public **fields and properties** with getters and setters, so plain script-style classes work as they
  are. Nested classes, arrays, `List<T>` and `Dictionary<string, T>` work too.
- Plain values: `SaveData.Save("highscore", 9001)`, `SaveData.Save("unlocked", new[] { "forest" })`.
- **Enums** are saved by name. Don't rename an enum value used in old saves, or those saves load it as
  the default.
- **XNA types** are saved as number arrays:
  - `Vector2`: `[x, y]`
  - `Vector3`: `[x, y, z]`
  - `Vector4` and `Quaternion`: `[x, y, z, w]`
  - `Color`: `[r, g, b, a]` (0-255)
  - `Point`: `[x, y]`
  - `Matrix`: 16 numbers, row by row
- **Not saved:** private fields, `[JsonIgnore]` members, and anything that only makes sense while the
  game runs, such as `BasicEntity`, components and textures. Save a gameobject's **name** or **position**
  and look the object up again after loading, for example with `FindGameObject(name)`.

Loading is forgiving, so a file can be edited by hand. Names are case-insensitive, and comments,
trailing commas and a UTF-8 byte order mark are all fine. Members missing from the file keep the
defaults your class gives them, and members the class no longer has are skipped. That makes it safe to
add new fields to a save class: old saves still load.

`SaveData.JsonOptions` holds these JSON settings, if you want to serialize something yourself.

## Text and bytes

| Function | What it does |
| --- | --- |
| `bool SaveText(name, text)` / `string LoadText(name, fallback = null)` | UTF-8 text. Give the name an extension (`"log.txt"`), otherwise it gets `.json`. |
| `bool SaveBytes(name, bytes)` / `byte[] LoadBytes(name)` | Raw bytes, for example a screenshot. `LoadBytes` returns null if there's no file. |

## Managing saves

| Function | What it does |
| --- | --- |
| `bool Exists(name)` | Whether the save exists. |
| `bool Delete(name)` | Deletes the save and its backup. Returns true if there was a save. |
| `IReadOnlyList<string> List(subfolder = null)` | All save names, sorted, with `/` between folders and without `.json`. You can pass each one straight back to `Load`. Backups aren't listed. |
| `DateTime? GetLastWriteTime(name)` | When the save was last written (local time), or null. Useful for "Saved 5 minutes ago". |
| `void Flush()` | Waits until all `SaveAsync` writes have finished. The engine calls it when it shuts down. |

## Safety: atomic writes and backups

Every write goes to `<file>.tmp` first and is flushed to disk. Then it replaces the old file in one
step, and the previous version is kept as `<file>.bak`. So:

- A crash or power loss mid-save never leaves a half-written save. You keep either the old file or the
  new one.
- If a save is damaged, for example by a bad hand edit, `Load`/`TryLoad` read the `.bak` instead and log
  it.
- If both are damaged, you get the fallback.

Writes to the same file always happen in the order they were made, including `SaveAsync` calls. A
`Load`, `Exists` or `List` waits for writes still in progress, so it never sees an older version.
`SaveData` is safe to call from any thread.

`SaveAsync` turns the object into JSON **immediately**, on the calling thread. Changing the object after
the call doesn't change what gets saved. Only the disk write happens in the background.

## Errors

- **Bad names** throw `ArgumentException`. That's a bug in the calling code. In a script behaviour, the
  script runner catches it and logs it like any other script exception.
- **Disk and format problems** never throw. Examples: a full disk, a read-only folder, a file locked by
  another program, or JSON that doesn't fit the type. `Save*` returns false, `Load*` returns the
  fallback, and the reason goes to the engine log (`EditorBridge.Log`).

## Examples

### Save slots

```csharp
using Engine.Logic;
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

public sealed class SaveSlotsScript : ScriptBehaviour
{
    public sealed class SlotData
    {
        public string Scene;
        public Vector3 PlayerPosition;
        public int Coins;
        public DateTime SavedAt;
    }

    [Range(1, 3)] public int Slot = 1;
    [NonSerialized] public int Coins;

    private string SlotName => $"slots/slot{Slot}";

    public void SaveGame()
    {
        var data = new SlotData
        {
            Scene = GameFlow.SceneLogic?.ActiveScene?.Name,
            PlayerPosition = Position,
            Coins = Coins,
            SavedAt = DateTime.Now,
        };
        if (!SaveData.Save(SlotName, data)) Log("Saving failed, see the log.");
    }

    public void LoadGame()
    {
        if (!SaveData.TryLoad(SlotName, out SlotData data)) return;
        Position = data.PlayerPosition;
        Coins = data.Coins;
    }

    // For a "Continue" button: the most recent slot.
    public static string LatestSlot() =>
        SaveData.List("slots").OrderByDescending(n => SaveData.GetLastWriteTime(n)).FirstOrDefault();
}
```

### Settings that survive restarts

```csharp
public sealed class PlayerSettings
{
    public float MasterVolume = 1;
    public bool VSync;
    public int FpsCap;
}

// On start (main menu Start):
var settings = SaveData.Load("settings", new PlayerSettings());
Audio.MasterVolume = settings.MasterVolume;
GameSettings.g_vsync = settings.VSync;

// When the player changes something:
SaveData.Save("settings", settings);
```

Inside Anvil, the sample menus put engine-wide settings back when Play stops (`MenuSettings`). A setting
loaded from a save is applied again on the next Play.

### Autosave without hitches

```csharp
private float _sinceAutosave;

public override void Update()
{
    _sinceAutosave += DeltaTime;
    if (_sinceAutosave < 60) return;
    _sinceAutosave = 0;
    _ = SaveData.SaveAsync("autosave", BuildState());   // serialized now, written on a worker thread
}
```

### High score

```csharp
int best = SaveData.Load("highscore", 0);
if (score > best) SaveData.Save("highscore", score);
```

## Checks

`Tests/Components/SaveDataChecks.cs` covers:

- the default folder;
- round trips of fields, properties, enums, XNA types, lists and dictionaries;
- subfolders and hand-edited JSON;
- backups after a damaged save, and wrong-type fallbacks;
- text and bytes;
- `List`, `GetLastWriteTime` and `Delete`;
- refused names, with nothing written outside the folder;
- async ordering, snapshots and `Flush`.

Run it with:

```powershell
dotnet run --project Tests\Components\Components.csproj
```
