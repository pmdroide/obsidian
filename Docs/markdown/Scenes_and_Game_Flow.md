# Scenes and game flow

How the game decides which scene to run, how scripts move between scenes, read menu input and show UI,
and the MainMenu sample scene that uses all of it.

## The scene list

The scene list is the game's scenes in build order. Edit it in **Anvil > Game Settings > Scenes**:
**Add Scenes...**, **Remove**, **Move Up**, **Move Down**, then **Save**.

- It is saved to `Engine/Content/System/SceneList.json` as Content-relative paths:
  ```json
  { "Scenes": [ "Scenes/MainMenu.obsc", "Scenes/GodRayTest.obsc", "Scenes/AutoExposureTest.obsc" ] }
  ```
- **Index 0 is the startup scene.** The standalone game (`Engine.exe`) loads it after the intro video
  and starts Play. With an empty list (or a missing file) the game starts in an empty scene, as before.
- Scenes must live under `Engine/Content`; the dialog refuses files elsewhere. The build copies
  `Content/Scenes/**/*.obsc` and `.probes`, the scene list and `Content/UI/**` next to the executable.
- Saving in Anvil updates the running engine immediately (it is the same process).

Anvil itself still opens scenes from the Assets panel and never auto-loads index 0.

## Intro videos

Before scene 0, the standalone game plays its intro videos back to back. Edit them in
**Anvil > Game Settings > Intro Videos**: **Add Videos...**, **Remove**, **Move Up**, **Move Down**, then **Save**.

- Saved to `Engine/Content/System/IntroVideos.json` as Content-relative paths, in play order:
  ```json
  { "Videos": [ "Video/obsidian_intro.mp4", "Video/fmod_intro.mp4" ] }
  ```
- **Enter skips the current video** and starts the next one; after the last video the game loads scene 0.
- Without the file the game plays `Video/intro.mp4`; an empty list skips the intro. Missing files are
  logged and left out.
- Videos must live under `Engine/Content` (normally `Content/Video`). The build copies every
  `.mp4`/`.mkv`/`.mov`/`.avi`/`.wmv`/`.webm` under `Content` next to the executable. Playback is LibVLC,
  scaled to the window. Anvil never plays the intro.

## Switching scenes from a script: `GameFlow`

`Engine.Logic.GameFlow` is the script API for game flow.

| Member | What it does |
| --- | --- |
| `LoadScene(int index)` | Queues the scene at that index in the list. Returns false if out of range or missing. |
| `LoadScene(string nameOrPath)` | Same, by name (`"GodRayTest"`, any case) or Content path (`"Scenes/GodRayTest.obsc"`). |
| `LoadScene(..., carryPersistent: false)` | Leaves persistent gameobjects behind, so they unload with the departing scene (going back to the main menu). |
| `LoadNextScene()` / `ReloadScene()` | The next entry (wrapping to 0) / the running scene again from its file. |
| `SceneCount`, `SceneName(i)` | The list, for building menus. |
| `ActiveSceneIndex`, `ActiveSceneName` | The running scene (-1 when it isn't in the list). |
| `Quit()` | Closes the standalone game. Inside Anvil it only logs. |
| `IsEditor` | True when running in Anvil's Play mode. |
| `EscapeQuits` | Escape closes the standalone game unless a script sets this to false (menus use Escape for Back). Reset when Play stops. |
| `Paused` | Freezes the game during Play (see *Pausing* below). A scene load or Stop unpauses. |
| `PlayStopped` | Event raised when Play stops (not on scene switches). Use it to undo engine-wide changes made in Anvil. |

Loads are **queued** and happen at the start of the next frame, so it is safe to call `LoadScene` from
`Update`. `LoadScene` does the whole load in that frame; for big scenes use `LoadSceneAsync` (next section). During Play, the departing scene's scripts stop and the new scene's scripts start in the
same frame; Play mode never ends.

**Inside Anvil**, switching scenes during Play does not lose your work. The scene you were editing is
kept, with its transforms rewound, and **Stop** returns to it. Opening a scene from the Assets panel
during Play replaces it for good, as before.

**Persistent gameobjects** (Inspector > **Persistent**, or `DontDestroyOnLoad()` in a script) don't
unload: they move into the new scene with their scripts still running. A player keeps its state from
level to level. See [Persistent_GameObjects.md](Persistent_GameObjects.md) and the PersistenceTest
sample.

## Loading screens and background loads: `GameFlow.LoadSceneAsync`

`LoadScene` does the whole load in one frame: read the file, create every gameobject, swap. On a big
scene that is a visible freeze. `LoadSceneAsync` spreads the same work out while the running scene
keeps playing, and by default covers the switch with the engine's loading screen:

```csharp
// Loading screen, then the new scene (Play carries on in it).
GameFlow.LoadSceneAsync("Level2");

// No screen: this scene keeps running and you show the progress yourself.
SceneLoadOperation load = GameFlow.LoadSceneAsync("Level2", loadingScreen: false);
_ui.SetText("#progress", $"{load.Progress:P0}");

// "Press any key to continue": built, then held until the script allows the swap.
load.AllowSceneActivation = false;
load.ReadyMessage = "Press Enter to continue";
if (load.IsReady && GameInput.MenuConfirm) load.AllowSceneActivation = true;

// In a coroutine: yield the load to wait until it is built (or ended).
yield return GameFlow.LoadSceneAsync("Level2", loadingScreen: false);
```

| Member | What it does |
| --- | --- |
| `LoadSceneAsync(index or name, carryPersistent = true, loadingScreen = true)` | Starts a background load and returns its `SceneLoadOperation` (null when the scene doesn't exist). A new load cancels one that hasn't swapped yet. |
| `CurrentLoad`, `IsLoading` | The load in progress (null when none). |
| `LastLoad` | The last load that finished, blocking or not. Its timings say what it cost. |
| `SceneLoaded` | Event raised when a load is Done, a few frames after the new scene started. |
| `CancelLoad()` | Stops the load if the scenes haven't swapped yet. |
| `LoadBudgetMs` | Milliseconds per frame spent creating gameobjects (default 4). |
| `LoadingScreenDocument` | The loading screen's Vista document (default `UI/LoadingScreen`). |

**What happens, frame by frame.** A `SceneLoadOperation` goes through these `Status` values:

1. **Reading**: a worker thread streams the `.obsc` (and its baked `.probes`) and parses the JSON.
   Nothing in the engine is touched.
2. **Building**: on the game thread, gameobjects, lights and decals are created one at a time until
   `LoadBudgetMs` is used up, then the frame goes on. They are created on the game thread because
   gameobject IDs and components aren't thread-safe.
3. **WaitingForActivation**: the scene is built. It swaps on the next frame, unless `AllowSceneActivation` is
   false or the loading screen is still fading in (the swap is never seen).
4. **Activating**: the swap itself, the same as a blocking load (persistent gameobjects carried, scripts
   stopped and started; see *Switching scenes* above).
5. **Finishing**: the new scene runs its first 3 frames: physics bodies, shadow and environment maps, the
   new scene's UI. Those frames are the slowest of a load, so the loading screen stays up over them.
6. **Done** (or **Failed** with `Error`, or **Canceled**). `Completed` fires.

`Progress` is the real work done: bytes read (0 to 30%), objects built (30 to 90%), then the finishing
frames (90 to 100%). A load held by `AllowSceneActivation = false` waits at 90%, like Unity.
`BytesRead`/`TotalBytes`, `ObjectsBuilt`/`ObjectCount`, `Frames`, `Seconds`, `ReadMs`, `BuildMs`,
`ActivationMs` (the swap) and `LongestFrameMs` (the worst frame while it ran, which is the hitch the
player saw) are there for menus and profiling. A blocking `LoadScene` fills in the same report
(`GameFlow.LastLoad.Blocking` is true).

**The loading screen.** `Content/UI/LoadingScreen.xml` + `.css`, driven by `Engine/Logic/LoadingScreen.cs`. It
is an engine layer: drawn above every script HUD, it survives the scene change and fades out once the
load is Done. It shows the scene name, the stage ("Reading scene file", "Building scene", "Starting"),
the progress bar and percentage, the bytes or objects so far, and the elapsed time. The light that
slides along the bar moves with real time, so if the game hitches, it stutters. Make your own by
pointing `GameFlow.LoadingScreenDocument` at another document that keeps the ids `#root`, `#scene`,
`#status`, `#fill`, `#sweep`, `#percent`, `#detail` and `#time`. While a held load waits, `#root` gets
the class `ready`; after an error it gets `failed`.

Stop (in Anvil) and opening a scene from the editor cancel a load in progress.

## The SceneLoadingTest sample

`Content/Scenes/SceneLoadingTest.obsc` (a hub with spinning cubes) and `SceneLoadingArena.obsc` (a city
of 1,550 gameobjects with falling spheres, 636 KB) both run the **Scene Loading Test** script on their
Main Camera (`Content/Scripts/SceneLoadingTestScript.cs`, HUD `Content/UI/SceneLoadingTest.xml`).

- **Load**: Arena, Hub, Auto Exposure Test (1.5 MB of baked lighting to read) or Water Test.
- **Mode** (M): *Loading screen* (`LoadSceneAsync`), *Background* (`LoadSceneAsync(..., loadingScreen: false)`, so
  the hub keeps spinning and the HUD shows the progress), or *Blocking* (`LoadScene`).
- **Hold at ready** (H): `AllowSceneActivation = false`. Enter starts the held scene.
- **Build budget** (B): `LoadBudgetMs` 1, 2, 4, 8 or 16.
- **Esc** cancels a load that hasn't swapped.
- **Frame time**: every frame measured with a stopwatch. The history carries across the swap, so a
  blocking load's freeze shows as one tall bar in the next scene.
- **Last loads**: `GameFlow.LastLoad` for each load: time, frames, worst frame, swap.

Both scenes are at the end of the scene list. Open `Scenes/SceneLoadingTest.obsc` in Anvil and press Play.

## Pausing: `GameFlow.Paused`

Set `GameFlow.Paused = true` to freeze the game during Play:

- gameobject and Main Camera components stop updating: scripts, Animator, Audio components;
- physics doesn't step, so bodies keep their pose and velocity, and no collision or trigger events fire;
- weather particles hang in the air.

A script that overrides `UpdateWhilePaused` to return true keeps getting `Update`: that is the pause menu.
Game UI layers keep updating and drawing. Set `Paused = false` to resume. A scene load or Stop also
unpauses.

Sounds that are already playing carry on, and the water shader keeps its waves moving.

The **Pause Menu** script (`Content/Scripts/PauseMenuScript.cs` + `Content/UI/PauseMenu.xml`/`.css`)
uses this. It runs on the persistent Player of the PersistenceTest samples, so one instance pauses every
scene the player reaches. Press Esc or gamepad Start. The menu has Resume, Restart scene, Settings,
Main menu and Quit game. Main menu loads scene list entry 0 with `carryPersistent: false`. See
[Persistent_GameObjects.md](Persistent_GameObjects.md#example-a-pause-menu-that-comes-along).

## Menu and gameplay input: `GameInput`

`Engine.Logic.GameInput` merges the keyboard (standalone, or the keys Anvil forwards), the mouse and up
to four XInput gamepads, with pressed-this-frame edges. It updates once per frame, before scripts.

For gameplay, prefer the project's **input actions** (Anvil > Window > Input Actions): named actions such as
Move, Look and Jump, each bound to keys, mouse and gamepad controls, read with `WasActionPressed("Jump")` or
`GetVector("Move")` from a script (also `GameInput.WasActionPressed/IsActionDown/GetAxis/GetVector/Action`).
See [Input_Actions.md](Input_Actions.md).

| Member | What it does |
| --- | --- |
| `MenuUp/Down/Left/Right` | Arrows, WASD, D-pad or left stick. Fire on press, then repeat after 0.4 s every 0.085 s while held. |
| `MenuConfirm` / `MenuBack` | Enter, Space, A or Start / Escape, Backspace, B or Back. |
| `AnyInputPressed` | Any key, mouse button or gamepad button went down this frame ("press any button"). |
| `WasPressed(Keys)`, `IsDown(Keys)`, `WasPressed(Buttons)`, `IsDown(Buttons)` | Single keys and buttons. |
| `MousePosition`, `MouseMoved`, `MouseClicked`, `MouseRightClicked`, `MouseScroll`, `MouseDelta` | Mouse, in viewport pixels (`MouseDelta` is zero in Anvil while the cursor is over a panel). |
| `LeftStick`, `RightStick`, `LeftTrigger`, `RightTrigger`, `KeysPressedThisFrame` | The first connected gamepad's analog values; the keys that went down this frame. |
| `LastDevice` | `Keyboard`, `Mouse` or `GamePad`: the device used last, for showing the right prompts. |

Anvil forwards the whole keyboard (`Anvil.Services.KeyMap`): letters, digits, F1–F24, the numpad,
punctuation, modifiers, arrows and navigation keys.

In Play, the old dev hotkeys (Space toggles editor mode, L spawns lights, X and M play test audio) are
off, because they would collide with game controls. F1 (render mode) still works.

## Game UI: `GameUI`

`GameUI.Open("UI/MyMenu")` loads `Content/UI/MyMenu.xml` and `.css` as a Vista layer, scaled from a
1080-high design canvas to the window. Layers draw over the scene and under the debug stats. Close a
layer in your script's `Stop` with `GameUI.Close(ui)`; any layer still open is closed when Play stops
or the scene changes. In a dev checkout, saving the XML or CSS reloads the layer while the game runs.
See [VistaUI_Architecture.md](VistaUI_Architecture.md) for the supported CSS and the scripting API.

## The MainMenu sample scene

`Content/Scenes/MainMenu.obsc` is entry 0 of the shipped scene list. It tests that Vista works and looks
good in a real game flow.

**Scene.** A ring of nine black obsidian monoliths around an emissive ember core on a dark dais, at
golden hour (17:36, with the day/night cycle at its slowest so the light holds still). The sun is
low behind the ring at three-quarters, for long shadows and fog shafts. The `Main Menu` script on the
Main Camera drifts the camera slowly around the ring. In the menus it pushes in and frames the ring to
the right of the menu column.

**Flow.**

1. Fade in from black to the title: the wordmark and a pulsing **Press any button**.
2. Any key, mouse button or gamepad button opens the **Main Menu**:
   - **Play** loads the scene after MainMenu in the list (GodRayTest).
   - **Scenes** lists the whole scene list (index, name, Start/Running tags) and loads any entry.
   - **Settings** changes engine settings live: performance overlay, VSync, frame rate cap,
     anti-aliasing, ambient occlusion, bloom, volumetric fog, reflections, interface scale. Changes are
     not saved. Inside Anvil they are restored when Play stops.
   - **Credits**: a wrapped-text test.
   - **Quit** opens a confirmation dialog. Inside Anvil, confirming goes back to the title instead
     of closing.
3. Back (Escape, Backspace, B, right click) returns to the main menu, and from there to the title.

**Controls.** Arrows/WASD, Enter/Space, Escape/Backspace; the mouse (hover selects, click activates,
wheel scrolls, right click goes back); a gamepad (D-pad or stick, A, B). The footer shows prompts
for the device used last.

**Files.**

| File | Contents |
| --- | --- |
| `Engine/Content/Scenes/MainMenu.obsc` | The 3D scene. |
| `Engine/Content/UI/MainMenu.xml` / `.css` | The menu document and its styles. |
| `Engine/Content/Scripts/MainMenuScript.cs` | Screen state, input, scene list and settings rows, camera drift. |
| `Engine/Content/System/SceneList.json` | The build order. |

To try it, run `dotnet run --project Engine\Engine.csproj`; the menu opens after the intro video. In
Anvil, open `Scenes/MainMenu.obsc` from the Assets panel and press Play.

## Making your own menu

1. Write `Content/UI/<Name>.xml` and `.css`. Screens are full-size containers toggled by a class; rows
   are absolutely positioned. Remember the closing tags.
2. Write a `ScriptBehaviour` (copy `MainMenuScript`): open the layer in `Start`, read `GameInput` in
   `Update`, toggle classes with `ui.SetClass`, and close the layer in `Stop`. Register it in
   `ScriptRegistry`.
3. Attach it to the Main Camera (or any gameobject) with **Add Component > Script Behaviour**.
4. Add the scene to **Game Settings > Scenes**, and move it to index 0 if the game should start there.

Run `dotnet run --project Tests\Components\Components.csproj` to check the scene list, scene switching
(blocking and `LoadSceneAsync`: spread over frames, held at ready, canceled, replaced, failed;
including the return to the edited scene in Anvil), menu input, and Vista layout and styles.
