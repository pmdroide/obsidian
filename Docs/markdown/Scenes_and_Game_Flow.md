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

## Switching scenes from a script: `GameFlow`

`Engine.Logic.GameFlow` is the script API for game flow.

| Member | What it does |
| --- | --- |
| `LoadScene(int index)` | Queues the scene at that index in the list. Returns false if out of range or missing. |
| `LoadScene(string nameOrPath)` | Same, by name (`"GodRayTest"`, any case) or Content path (`"Scenes/GodRayTest.obsc"`). |
| `LoadNextScene()` / `ReloadScene()` | The next entry (wrapping to 0) / the running scene again from its file. |
| `SceneCount`, `SceneName(i)` | The list, for building menus. |
| `ActiveSceneIndex`, `ActiveSceneName` | The running scene (-1 when it isn't in the list). |
| `Quit()` | Closes the standalone game. Inside Anvil it only logs. |
| `IsEditor` | True when running in Anvil's Play mode. |
| `EscapeQuits` | Escape closes the standalone game unless a script sets this to false (menus use Escape for Back). Reset when Play stops. |
| `PlayStopped` | Event raised when Play stops (not on scene switches). Use it to undo engine-wide changes made in Anvil. |

Loads are **queued** and happen at the start of the next frame, so it is safe to call `LoadScene` from
`Update`. During Play, the departing scene's scripts stop and the new scene's scripts start in the
same frame; Play mode never ends.

**Inside Anvil**, switching scenes during Play does not lose your work. The scene you were editing is
kept, with its transforms rewound, and **Stop** returns to it. Opening a scene from the Assets panel
during Play replaces it for good, as before.

## Menu and gameplay input: `GameInput`

`Engine.Logic.GameInput` merges the keyboard (standalone, or the keys Anvil forwards), the mouse and up
to four XInput gamepads, with pressed-this-frame edges. It updates once per frame, before scripts.

| Member | What it does |
| --- | --- |
| `MenuUp/Down/Left/Right` | Arrows, WASD, D-pad or left stick. Fire on press, then repeat after 0.4 s every 0.085 s while held. |
| `MenuConfirm` / `MenuBack` | Enter, Space, A or Start / Escape, Backspace, B or Back. |
| `AnyInputPressed` | Any key, mouse button or gamepad button went down this frame ("press any button"). |
| `WasPressed(Keys)`, `IsDown(Keys)`, `WasPressed(Buttons)`, `IsDown(Buttons)` | Single keys and buttons. |
| `MousePosition`, `MouseMoved`, `MouseClicked`, `MouseRightClicked`, `MouseScroll` | Mouse, in viewport pixels. |
| `LastDevice` | `Keyboard`, `Mouse` or `GamePad`: the device used last, for showing the right prompts. |

Anvil forwards letters, Space, Shift/Ctrl/Alt, F1–F3, Escape, the arrows, Enter, Backspace and Tab.

In Play, the old dev hotkeys (Space toggles editor mode, L spawns lights, X and M play test audio) are
off, because they would collide with game controls. F1 (render mode) still works.

## Game UI: `GameUI`

`GameUI.Open("UI/MyMenu")` loads `Content/UI/MyMenu.xml` and `.css` as a Vista layer, scaled from a
1080-high design canvas to the window. Layers draw over the scene and under the debug overlay. Close a
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
(including the return to the edited scene in Anvil), menu input, and Vista layout and styles.
