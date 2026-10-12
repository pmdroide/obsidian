# Audio Volume: Master, Music and Sound Effects

Every sound the engine plays goes through one of three mixer **buses**. Each bus has
its own volume, from 0 (silent) to 1 (full):

| Bus | What plays on it | Volume |
| --- | --- | --- |
| `AudioBus.Master` | Everything (the other two buses are inside it) | `Audio.MasterVolume` |
| `AudioBus.Music` | `PlayMusic` tracks, Audio components with Output = Music | `Audio.MusicVolume` |
| `AudioBus.Effects` | Every other sound: `PlaySound`, `PlaySound3D`, `PlaySoundAt`, Audio components (default), menu clicks | `Audio.EffectsVolume` |

How loud a sound is depends on three numbers multiplied together:

```
sound volume  x  bus volume (Music or Effects)  x  master volume
```

So Music 50% with Master 50% plays music at 25%. A volume change also applies to
sounds that are already playing.

For how sounds are loaded and played, see [Audio_Architecture.md](Audio_Architecture.md).

---

## Changing the volume from a script

The static `Audio` class (namespace `Engine.Recources`) has the volume settings:

```csharp
using Engine.Recources;

Audio.MasterVolume = 0.8f;   // everything
Audio.MusicVolume = 0.5f;    // music only
Audio.EffectsVolume = 1f;    // sound effects only

// The same, with the bus as a value (useful for a settings screen):
Audio.SetVolume(AudioBus.Music, 0.5f);
float effects = Audio.GetVolume(AudioBus.Effects);

Audio.IsMuted = true;        // silence everything without changing any volume
```

| Member | What it does |
| --- | --- |
| `MasterVolume` | Get or set the master volume, 0-1. |
| `MusicVolume` | Get or set the music volume, 0-1. |
| `EffectsVolume` | Get or set the sound effects volume, 0-1. |
| `GetVolume(bus)` | The volume of one bus. |
| `SetVolume(bus, volume)` | Sets the volume of one bus. |
| `IsMuted` | Silences all output and keeps the volumes. Anvil's speaker button uses it. |
| `IsMusicPlaying` | `true` while a `PlayMusic` track is playing. |

Notes:

- Values are clamped to 0-1. An invalid number (`NaN`, infinity) sets the volume to 1.
- If FMOD isn't installed, nothing plays, but the settings still keep their values.
  The menus still show and change them.
- Volumes are **not saved** on their own. They start at 100% when the game starts.
  To keep them, save them with [`SaveData`](Save_Data.md). See
  [Saving the volume](#saving-the-volume) below.
- Inside Anvil, a volume changed through a game menu goes back to its old value when
  you press Stop (`MenuSetting.Step`). Setting `Audio.MusicVolume` directly from a
  script doesn't do this.

---

## Choosing the bus of a sound

### Scripts (`ScriptBehaviour`)

```csharp
PlayMusic("Audio/theme.ogg");                       // Music bus, loops
PlayMusic("Audio/victory.wav", volume: 0.8f, loop: false);
StopMusic();

PlaySound("Audio/click.wav");                       // Effects bus (default)
PlaySound3D("Audio/radio.wav", loop: true, bus: AudioBus.Music); // a 3D radio counts as music
PlaySoundAt("Audio/thud.wav", Position);            // Effects bus
```

- Only one `PlayMusic` track plays at a time. A new one replaces the old one.
- Like the other script sounds, the track stops when the script stops. To keep music
  playing across scene loads, play it from a persistent gameobject (see
  [Persistent_GameObjects.md](Persistent_GameObjects.md)).
- `bus: AudioBus.Master` plays a sound that only the master volume affects.

### Audio component

Inspector > Audio > **Output**: `Effects` (default) or `Music`. It is saved by name
in the scene (`"Output": "Music"`). Changing it while the clip plays restarts the
clip on the new bus. Use Music for a level's background track.

### Engine code

`Audio.PlaySound(...)`, `Audio.PlaySound3D(...)` and the matching `AudioManager`
methods take an optional `bus` argument (Effects by default).
`Audio.PlayMusic(...)` always uses the Music bus.

---

## The menu settings

The sample main menu (`Content/Scripts/MainMenuScript.cs`) and the pause menu
(`Content/Scripts/PauseMenuScript.cs`) both start their Settings page with three
rows, in 10% steps:

- **Master volume**
- **Music volume**
- **Sound effects volume**

These rows come from `MenuSettings` (`Content/Scripts/MenuSettings.cs`):

| Method | Returns |
| --- | --- |
| `MenuSettings.Audio()` | The three rows, in this order. |
| `MenuSettings.MasterVolume()` / `MusicVolume()` / `EffectsVolume()` | One row. |
| `MenuSettings.Volume(name, description, bus)` | A 0-100% row for any bus. |

The menu click sound plays on the Effects bus, so you can hear the effects volume
change as you step it.

Both Settings lists show 8 rows at a time and scroll with the selection. "‹ more" and
"more ›" (`#settings-scroll-up` / `#settings-scroll-down`) show when there are rows
above or below. The number of rows is `VisibleSettingRows` in each script.

To add the rows to your own menu:

```csharp
List<MenuSetting> settings = MenuSettings.Audio();
settings.AddRange(MenuSettings.Graphics());
// change one with settings[i].Step(+1 or -1); show settings[i].ValueText
```

---

## Saving the volume

Example: a script on the main menu camera that loads the volumes when the game
starts and saves them when the menu closes.

```csharp
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;

public sealed class AudioPrefs
{
    public float Master = 1f, Music = 1f, Effects = 1f;
}

public sealed class AudioPrefsScript : ScriptBehaviour
{
    public override void Start()
    {
        if (SaveData.TryLoad("audio", out AudioPrefs prefs))
        {
            Audio.MasterVolume = prefs.Master;
            Audio.MusicVolume = prefs.Music;
            Audio.EffectsVolume = prefs.Effects;
        }
    }

    public override void Stop() => SaveData.Save("audio", new AudioPrefs
    {
        Master = Audio.MasterVolume,
        Music = Audio.MusicVolume,
        Effects = Audio.EffectsVolume,
    });
}
```

---

## How it works

- `AudioManager.Initialize` creates two FMOD channel groups, `Music` and `Effects`.
  FMOD makes new groups children of the master channel group.
- Every play call passes its group to `playSound` (`TryPlay(sound, group, ...)`).
  `AudioBus.Master` passes no group, so the sound plays on the master group.
- `MasterVolume` sets the master group's volume. `MusicVolume` and `EffectsVolume`
  set their own group's volume. FMOD multiplies the group volumes down the tree.
- The volumes are kept in `AudioManager` fields. They are applied again after FMOD
  starts, so values set before `Initialize` are kept.
- `Audio.Instance` is set at the start of `Initialize`, before FMOD starts, so the
  settings work even when FMOD fails to load. `Dispose` releases the groups and
  clears `Audio.Instance`.

| File | What's there |
| --- | --- |
| `Engine/Recources/AudioManager.cs` | `AudioBus`, the channel groups, `MusicVolume`/`EffectsVolume`/`GetVolume`/`SetVolume`, the `Audio` facade |
| `Engine/Scripting/ScriptBehaviour.cs` | `PlayMusic`, `StopMusic`, the `bus` argument of `PlaySound...` |
| `Engine/Components/AudioComponent.cs` | `Output` |
| `Editor/Anvil/Models/AudioComponentViewModel.cs`, `Views/MainWindow.axaml` | The Output dropdown |
| `Engine/Content/Scripts/MenuSettings.cs` | `Audio()`, `Volume(...)` and the three volume rows |
| `Engine/Content/Scripts/MainMenuScript.cs`, `PauseMenuScript.cs` | The rows and the scrolling settings list |
| `Engine/Content/UI/MainMenu.*`, `PauseMenu.*` | The scroll hints |
| `Tests/Components/Program.cs` | Volume, menu row and Output checks; with `--audio`, the live FMOD bus checks |

### Adding another bus (for example, Voice or UI)

1. Add a value to the end of `AudioBus`. It is saved by name, so never rename a value.
2. Create its group in `AudioManager.Initialize`, give it a volume property, and add
   it to `GroupFor`, `GetVolume`, `SetVolume` and `Dispose`.
3. Add a `MenuSettings.Volume(...)` row to `MenuSettings.Audio()`.
4. To offer it in the Inspector, add it to `AudioComponentViewModel.Outputs`.
