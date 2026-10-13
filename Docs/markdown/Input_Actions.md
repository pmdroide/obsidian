# Input actions

Input actions let scripts ask for **what the player wants to do** ("Jump", "Move", "Interact") instead of
which key was pressed. Each action has a list of bindings to keyboard keys, mouse buttons and axes, and
gamepad buttons, triggers and sticks. Change a binding in Anvil and every script follows, and players
can rebind controls in your settings menu.

```csharp
public override void Update()
{
    Vector2 move = GetVector("Move");            // W/A/S/D or the left stick, +Y forward, length up to 1
    Position += (Forward * move.Y + Right * move.X) * 5f * DeltaTime;

    if (WasActionPressed("Jump")) Jump();         // Space or gamepad A, this frame only
    bool running = IsActionDown("Sprint");        // held
    _yaw -= GetVector("Look").X;                  // mouse drag, arrows or right stick, in degrees
}
```

## Editing the actions: Anvil > Window > Input Actions

Open it from **Window > Input Actions...**, or with **Edit Input Actions...** in the Window > Input panel.

- **Actions** (left): the project's actions in order. **+ Add**, **Remove**, **Duplicate**, **Up**/**Down**.
- **Name** and **Type** of the selected action, and the line to paste into a script.
- **Bindings**: one card per binding.
  - **+ Binding** adds a single control. Pick its **device** (Keyboard, Mouse, GamePad) and **control**, or click
    **Listen** and press a key, click a mouse button, scroll, or use a gamepad button, trigger or stick
    direction. Esc cancels listening; pick Escape from the list instead.
  - **+ Composite** (Axis and Vector2 actions) builds a value from several keys: Up/Down/Left/Right for a
    Vector2 (W/A/S/D), Negative/Positive for an Axis (Q/E).
  - **Modifier**: a control that must be held for the binding to count. *Right Mouse* + *Mouse Delta* turns
    the camera only while dragging; *Left Ctrl* + *S* is a shortcut.
  - **Scale** multiplies the value (mouse sensitivity, degrees per second). **Per second** multiplies by the
    frame time, so a held stick or key adds up like mouse movement. **Invert** / **Invert X/Y** flip it.
- **Reset to Defaults** puts back the starting list. **Save** writes `Content/System/InputActions.json`; the
  running engine reloads it on the next frame, also during Play.

Scripts find actions by name (case-insensitive), so renaming an action means updating the scripts that
use it. Names must be unique; Save is disabled until they are.

## Action types

| Type | Value | Typical bindings | Read it with |
| --- | --- | --- | --- |
| **Button** | pressed or not; `Value` 0..1 (analog triggers in between) | keys, mouse buttons, gamepad buttons, triggers | `WasActionPressed`, `IsActionDown`, `WasActionReleased` |
| **Axis** | one float, usually -1..1 | Q/E composite, a stick axis, a trigger, `Mouse/Scroll` | `GetAxis` |
| **Vector2** | a 2D value, +Y up/forward | W/A/S/D composite, a stick, the D-pad, `Mouse/Delta` | `GetVector` |

Analog values count as **pressed** at 0.5 and as released again below 0.375, so a trigger resting near the
middle doesn't flicker. Every action type has `IsPressed`, so "Move" is pressed while walking.

Each frame every binding is read and **the strongest one wins**: keyboard and stick don't add up, and a
slightly pushed stick doesn't slow the keys down. Composite Vector2 values are normalized on diagonals.

## The default actions

New projects (and Project Settings > Delete Project) start with Unity's default set:

| Action | Type | Keyboard / mouse | Gamepad |
| --- | --- | --- | --- |
| Move | Vector2 | W/A/S/D | Left Stick |
| Look | Vector2 | Right Mouse + Mouse Delta (0.15°/px), Arrow Keys (120°/s) | Right Stick (150°/s) |
| Jump | Button | Space | A |
| Sprint | Button | Left/Right Shift | LS Press |
| Crouch | Button | C, Left Ctrl | B |
| Interact | Button | E | X |
| Attack | Button | Left Mouse | RT |
| Previous / Next | Button | 1 / 2 | LB / RB |
| Pause | Button | Esc | Start |

`Look` is already in degrees for the frame: `_yaw -= look.X; _pitch += look.Y;` works for all three bindings.
Scripts can still read raw keys with `GameInput` (menus use `GameInput.MenuUp/Confirm/...`).

## Control paths

Bindings store controls as paths (`InputControls.All` lists them; the Anvil dropdowns show friendly names):

| Device | Paths |
| --- | --- |
| Keyboard | `Keyboard/<XNA Keys name>`: `Keyboard/Space`, `Keyboard/D1` (the 1 key), `Keyboard/LeftShift`, `Keyboard/OemMinus`... |
| Mouse | `Mouse/LeftButton`, `RightButton`, `MiddleButton`, `XButton1`, `XButton2` (buttons); `Mouse/Delta` (2D, pixels, +Y up), `DeltaX`, `DeltaY`; `Mouse/Scroll` (notches, + up), `ScrollUp`, `ScrollDown` (buttons) |
| GamePad | `GamePad/A`, `B`, `X`, `Y`, `LeftShoulder`, `RightShoulder`, `LeftStickButton`, `RightStickButton`, `Start`, `Back`, `DPadUp/Down/Left/Right`; `GamePad/LeftTrigger`, `RightTrigger` (0..1); `GamePad/LeftStick`, `RightStick`, `DPad` (2D); `LeftStickX/Y` (signed); `LeftStickUp/Down/Left/Right` (0..1, like buttons), same for the right stick |

Gamepad controls read **every connected controller** and keep the strongest value. `Mouse/Delta` is zero in
Anvil while the cursor is over an editor panel (unless a drag started in the viewport).

## Script API

On every `ScriptBehaviour` (`Engine/Scripting/ScriptBehaviour.Input.cs`):

| Member | Returns |
| --- | --- |
| `WasActionPressed("Jump")` | the action went down this frame |
| `IsActionDown("Sprint")` | held this frame |
| `WasActionReleased("Attack")` | came up this frame |
| `GetAxis("Zoom")` | an Axis value (a Button's 0..1, a Vector2's length) |
| `GetVector("Move")` | a Vector2 value |
| `BindingText("Interact")` | the binding for the device the player uses: `"E"` or `"X"`, `"W/A/S/D"`, for prompts |
| `GetAction("Jump")` | the `InputAction` itself |

The same shortcuts are static on `GameInput` (`GameInput.WasActionPressed("Jump")`, `GameInput.Action("Jump")`).
An unknown name logs once and returns an action that never fires; it starts working if the action is added.

`InputAction` (`Engine/Logic/InputActions.cs`) also has:

- `IsPressed`, `WasPressed`, `WasReleased`, `Value`, `Vector`, `HeldTime` (seconds held), `ActiveDevice`.
- `Enabled`: a disabled action reads as released (e.g. while a dialog is open).
- **Input buffering:** `WasPressedWithin(0.15f)` is true for a press in the last 0.15 s; call `ConsumePress()`
  once you act on it. The CollisionTest player jumps this way, so Space just before landing still jumps.
- `BindingText(device)`, `FindBinding(device)`, `Bindings`.

While the debug console is open every action reads as released.

## Rebinding in a game menu

```csharp
private InputAction _waiting;

public override void Start() => InputActions.LoadBindings();     // the player's saved rebinds, if any

public void StartRebind(string action) => _waiting = GetAction(action);

public override void Update()
{
    if (_waiting == null) return;
    string control = InputActions.ListenForControl();   // "Keyboard/K", "GamePad/RightTrigger"... or null
    if (control == null) return;
    _waiting.Rebind(control);       // replaces the binding on that control's device (keyboard and gamepad stay separate)
    InputActions.SaveBindings();    // SaveData "InputBindings", only the actions that changed
    _waiting = null;
}
```

- `ListenForControl(InputDevice.GamePad)` limits listening to one device.
- `SetBinding(index, path)`, `SetBindingPart(index, InputCompositePart.Up, path)`, `AddBinding`, `RemoveBinding`
  and `SetBindings` edit bindings directly; `IsOverridden` tells whether they differ from the project's.
- `ResetBindings()` (one action) or `InputActions.ResetBindings()` (all) go back to the project's.
- In Anvil, **Stop** reloads the project's actions: rebinds and disabled actions last one Play session
  unless the script saves and loads them.

## How it is wired

- `InputActionList` (`Engine/Logic/InputActionList.cs`) is the file: `Data { Actions }`, `Read` (the defaults
  when the file is missing), `Save`, `ToJson`/`FromJson`, `Normalize` (trims, drops nameless/duplicate actions
  and empty bindings), `Defaults`. Types: `InputActionDefinition`, `InputBinding`, `InputActionType`,
  `InputCompositePart` (saved by name).
- `InputControls` (`Engine/Logic/InputControls.cs`) is the control catalogue: `Find(path)`, `Read`, `Magnitude`,
  `DetectPressed` (rebinding) and `DetectGamePad` (Anvil's Listen).
- `InputActions` loads the list in `ScreenManager.Load` and updates at the end of `GameInput.Update`, after the
  keyboard, mouse and pads, before scripts. `Load()` keeps `InputAction` instances (scripts may store them).
  `RequestReload()` is the only thread-safe call: Anvil's Save sets it, the next update reloads.
  `PlayModeController.Stop` calls `Load()`.
- Anvil: `InputActionsViewModel` (+ `InputActionItemViewModel`, `InputBindingItemViewModel`,
  `InputControlSlotViewModel`) and `Views/InputActionsWindow.axaml`. Listening uses window key events through
  `Services/KeyMap.cs` (the Avalonia-to-XNA key map, also used by `MonoGameHost` to forward the whole keyboard),
  overlay pointer and wheel events, and polls `GamePad.GetState` every 30 ms.
- The file ships in builds (`Engine.csproj`), and `ProjectReset.WriteDefaultSettings` writes the defaults.
- Checks: `Tests/Components/InputActionChecks.cs`.

## Limits

- One player: gamepad bindings read every controller. There are no per-player action maps yet.
- No cursor lock: mouse look uses a held button (the default Look) because the cursor still stops at the
  window edge.
- No hold/tap/double-tap interactions; use `HeldTime` and `WasPressedWithin` in the script.
