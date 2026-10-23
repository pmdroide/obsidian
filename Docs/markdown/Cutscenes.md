# Cutscenes

A basic movie maker for in-game cutscenes: a camera path plus timed subtitles, fades, sounds and script messages.
Cutscenes are made in Anvil and saved as `.cutscene` files; the game plays them on the scene's game camera.

## Making one in Anvil

Open **Window > Cutscenes**, or double-click a `.cutscene` file in Assets (they have a clapperboard icon) to open that cutscene. The editor is a panel docked in the main window, above Console and Assets, so you can keep using the viewport while it is open. Drag the line above it to resize it, and use **x** next to its title to close it. Window > Cutscenes also opens and closes it, and it comes back at the height you left it.

1. Type a name under the list and press **New**. This creates `Content/Cutscenes/<Name>.cutscene` with a camera key
   at 0 s (taken from the viewport camera) and a 1 s **Fade In**.
2. Frame the next shot in the viewport (hold the right mouse button and use WASD), move the playhead (click or drag on the
   timeline), then press **+ Key from View**. If a key already sits at the playhead, it gets the new pose instead.
3. Add events at the playhead with **+ Subtitle**, **+ Fade Out**, **+ Fade In**, **+ Sound** and **+ Message**, then
   edit their time, duration and text in the Events list.
4. Press **Play** to preview in the viewport, then **Save**.

| Control | What it does |
|---|---|
| Duration | Length in seconds. The cutscene ends here. |
| Letterbox | Draws 2.39:1 black bars while it plays (each bar is at most 1/8 of the screen). |
| Skippable | Enter, Space, gamepad A or Start jumps to the end. |
| Restore Camera | Puts the game camera back where it was when the cutscene ends. |
| Camera follows timeline | When ticked, scrubbing and editing keys move the viewport camera to the shot. Untick it to look around freely. |
| Play with Scene | Enters Play mode, so scripts, physics and Audio components run, and plays the edited (unsaved) cutscene on the game camera with its sounds and messages. If Play is already on, it just plays the cutscene. |
| Stop Play when done | If Play with Scene started Play, Play stops again when the cutscene ends. |
| Revert / Delete | Revert reloads the saved file. Delete needs a second click to confirm. |

**Timeline.** The top row shows seconds. The **Camera** track has a green diamond for each key and a yellow diamond
for a cut. The **Events** track has a bar for each event. Click empty space to scrub. Drag a diamond or bar to retime
it. To change how long a subtitle or fade lasts, drag the right edge of its bar. Drag the left edge to move its start while the end stays put. To make the whole cutscene longer or shorter, drag the grey handle at the end of the tracks. An event can't be stretched past the end, so drag the end handle first. The cursor turns into a resize arrow over these edges. Drags snap to 0.05 s, or to 0.1 s with Shift held.

**Camera keys.** Each key has Time, FOV (degrees) and two options:

- **Cut** jumps straight to this shot. The previous shot holds until the cut.
- **Ease** slows the camera down as it arrives at this key and as it leaves it.

**Set Selected from View** gives the selected key the viewport camera's pose and keeps its time. **Go** moves the playhead to the key.

**The preview in Edit mode** moves the editor camera, draws the letterbox, subtitles and fades, and plays the Sound events as the playhead passes them. Pausing or closing the panel stops those sounds. The scene itself doesn't run, so Audio components, scripts and physics stay still. Use **Play with Scene** to hear and see everything together. It raises no messages. Closing the panel clears the overlay and puts the editor camera back where it was before the preview moved it. While Camera follows timeline is ticked, scrubbing and key edits move the viewport camera, so untick it while you frame a new shot.

## Events

| Kind | Uses | Effect |
|---|---|---|
| Subtitle | Text, Duration | Shows white text near the bottom of the screen. If subtitles overlap, the one that started last is shown. |
| Fade Out | Duration | Fades to black and stays black. |
| Fade In | Duration | Fades up from black. One at 0 s opens the cutscene from black. |
| Sound | Sound picker | `Audio.PlaySound(name)`. Pick any file in `Content/Audio` from the list (`blip`, `Test/clap`; `.wav` is left off). A skip does not play it. If a sound can't play, the console says so. |
| Message | Text | Raises `Cutscenes.EventFired(Text)` for scripts. Messages still fire when the cutscene is skipped, so scripts reach their end state. |

## Playing from scripts

```csharp
using Engine.Logic;

public override void Start()
{
    Cutscenes.EventFired += OnCutsceneMessage;   // Message events
    Cutscenes.Finished += _ => GameUI.Open("UI/Hud");
    Cutscenes.Play("Intro");                     // Content/Cutscenes/Intro.cutscene, or a Content path
}

private void OnCutsceneMessage(string message)
{
    if (message == "OpenGate") GetComponent<AnimatorComponent>()?.Play("Open");
}

public override void Stop() => Cutscenes.EventFired -= OnCutsceneMessage;
```

- `Cutscenes.Play(name | Cutscene)`, `Skip()`, `Stop()`.
- `Cutscenes.IsPlaying`, `Current`, `Time`.
- Events: `Finished`, `EventFired`.

While a cutscene plays, it moves the game camera after the scripts have run, so it overrides camera scripts. Check
`Cutscenes.IsPlaying` to stop player input during the cutscene. The pause menu holds it (`GameFlow.Paused`).

**Without code:** attach the **Cutscene Trigger** script (`Content/Scripts/CutsceneTriggerScript.cs`). Its fields are:

- **Cutscene**: the name of the cutscene to play.
- **Play On Start**: plays it when Play starts.
- **Play On Trigger Enter**: plays it when something enters the trigger. The gameobject needs Physics > Is Trigger.
- **Required Tag**: only a gameobject with this tag starts it. The default is `Player`.
- **Once**: plays it at most once per Play session.

## File format

`Content/Cutscenes/<Name>.cutscene` is a JSON file. It is written in the same format as scenes, so vectors are `[x,y,z]`, and event kinds are
saved by name. Engine.csproj copies these files into builds.

```json
{
  "Version": 1,
  "Duration": 8,
  "Letterbox": true,
  "Skippable": true,
  "RestoreCamera": true,
  "CameraKeys": [
    { "Time": 0, "Position": [-20, 0, 4], "Yaw": 0, "Pitch": -5, "Fov": 45, "Cut": false, "Smooth": true }
  ],
  "Events": [
    { "Time": 0, "Duration": 1, "Kind": "FadeIn", "Text": "" },
    { "Time": 1.5, "Duration": 3, "Kind": "Subtitle", "Text": "Long ago..." }
  ]
}
```

Yaw is measured in degrees around the world up axis (Z). Yaw 0 looks along +X. Pitch is measured in degrees up from the horizon.

## How it works

- `Engine/Logic/Cutscene.cs` holds the asset. It loads and saves the file (`Load`, `Save`, `List`, `Normalize`) and
  evaluates the cutscene:
  - `EvaluateCamera` interpolates the position along a Catmull-Rom curve through the keys. Cuts break the curve. Yaw
    turns the short way round. Pitch and FOV change linearly. With Ease on, the camera slows near the key.
  - `FadeAt` and `SubtitleAt` give the fade and the subtitle at a given time.
- `Engine/Logic/Cutscenes.cs` is the static player.
  - `MainSceneLogic.Update` calls `Update(dt)` right after `PlayMode.UpdateScripts`, in Play mode only. It plays the
    events and sets the game camera from the camera track.
  - `ScreenManager.Draw` calls `Draw` after `GameUI.Draw` to draw the letterbox, fade and subtitle. The subtitle uses
    the `body` UI font, scaled from a 1080-pixel-high screen.
  - `PlayModeController.Stop` calls `Reset()`. Its own snapshot then restores the camera.
  - In Edit mode, `Preview` and `EndPreview` show a preview on the editor camera. `CaptureEditorCamera` turns the editor camera into a key.
- Anvil uses `Views/CutsceneEditorView.axaml` (docked in `MainWindow.axaml`, root grid rows 2-3, sized by `MainWindow.UpdateCutscenePanelRows`), `ViewModels/CutscenesViewModel.cs` (held by `MainWindowViewModel.CutsceneEditor`) and `Controls/CutsceneTimeline.cs`. All
  engine work goes through `IEditorBridge.EnqueueGameThreadAction`, and each preview sends a fresh copy of the cutscene.

## Limits

- Only one cutscene plays at a time. Playing a new one ends the current one.
- There are no tracks for animating gameobjects or characters. Use Message events and script them, for example with
  `Animator.Play` or `MoveTo` tweens.
- The camera's roll and depth of field are not keyed.
- The Edit mode preview plays the cutscene's own sounds only. The scene's Audio components and scripts need Play with Scene.
- Closing the panel keeps unsaved edits, but opening another cutscene or another `.cutscene` from Assets discards them without asking. The title shows `*` while there are unsaved changes.
- Loading a scene ends a playing cutscene: `Finished` is raised, and the camera is not restored.
