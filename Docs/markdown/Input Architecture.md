# Input Architecture

How keyboard and mouse input flows through Obsidian, in both the **standalone
engine** (`Engine.exe`) and the **Anvil editor** (engine embedded in Avalonia),
and the exact steps to add a new input binding.

---

## 1. The mental model

There is one and only one place the engine reads input: the static class
[`Engine.Logic.Input`](../Engine/Logic/Input.cs). Everything else (camera fly,
gizmos, selection, the debug console, render‑mode cycling, audio test hooks)
**polls** that class. There is no event system and no input‑mapping table — input
is read imperatively each frame.

```
                       ┌────────────────────────────────────────────┐
                       │  Input  (static, Engine/Logic/Input.cs)      │
                       │                                              │
  Keyboard.GetState()  │  keyboardState / keyboardLastState           │
  Mouse.GetState()  ─► │  mouseState   / mouseLastState               │ ─► consumers poll
  HostBridge (Anvil)   │                                              │     every frame
                       │  IsKeyDown / WasKeyPressed / WasLMBClicked …  │
                       └────────────────────────────────────────────┘
```

`Input.Update(gameTime, camera)` is called **once per frame** from
[`MainSceneLogic.Update`](../Engine/Logic/MainSceneLogic.cs#L223). It:

1. Copies the current state into the `…LastState` fields (so edge detection works).
2. Reads the raw Win32 mouse via `Mouse.GetState()` and **gates** it (see §4).
3. Reads the keyboard via `Keyboard.GetState()`.
4. Drives the camera directly (editor camera vs. game camera).

After `Input.Update` runs, every other system reads the now‑current state through
the helper methods below.

---

## 2. The two runtime contexts

The same engine code runs in two very different hosting situations, and this is
the single most important thing to understand before adding input.

### A) Standalone — `Engine.exe`
The MonoGame window owns OS keyboard focus. `Keyboard.GetState()` and
`Mouse.GetState()` both return real data. **Everything works.**

### B) Hosted — inside Anvil
[`MonoGameHost`](../Editor/Anvil/Controls/MonoGameHost.cs) reparents the MonoGame
`HWND` under an Avalonia window. Consequences:

- **Keyboard focus belongs to Avalonia, not the engine.** `Keyboard.GetState()`
  inside the engine returns *empty*. So keyboard has to be **forwarded** from
  Avalonia → engine (see §3).
- **The mouse still reports globally.** `Mouse.GetState()` returns real cursor
  data even when hosted, but coordinates are relative to the engine window's
  client area, and the cursor can stray over Avalonia panels (Inspector,
  Hierarchy, Console). So mouse input is **gated** by viewport ownership (see §4).

The bridge knows which context it is in via
[`IEditorBridge.IsHostedByEditor`](../Engine/Editor/IEditorBridge.cs#L91), set
true by `MonoGameHost` at boot.

---

## 3. Keyboard

### Reading keyboard (the consumer API)

| Call | Detects | Sees Anvil‑forwarded keys? |
|------|---------|----------------------------|
| `Input.IsKeyDown(Keys.W)` | key **held** | ✅ native **+** host‑forwarded |
| `Input.keyboardState.IsKeyDown(Keys.W)` | key **held** | ❌ native only |
| `Input.WasKeyPressed(Keys.R)` | rising edge (just pressed) | ❌ native only |
| `Input.WasKeyReleased(Keys.R)` | falling edge (just released) | ❌ native only |
| `Input.GetKeyPressed()` | next typed character (for text) | ❌ native only |

> ⚠️ **The key rule:** only [`Input.IsKeyDown`](../Engine/Logic/Input.cs#L33)
> merges the host‑forwarded set. `WasKeyPressed`, `WasKeyReleased`,
> `GetKeyPressed`, and direct `keyboardState.IsKeyDown` read **native MonoGame
> state only**, which is empty when hosted in Anvil. Therefore **one‑shot key
> shortcuts (`WasKeyPressed`) only fire in standalone `Engine.exe`** — inside the
> editor they do nothing unless you forward edges too (see the advanced recipe in
> §6.4). Held‑key behavior built on `IsKeyDown` works in both contexts.

### How keys get forwarded into Anvil

```
Avalonia window KeyDown/KeyUp                (Editor/Anvil/Controls/MonoGameHost.cs)
        │  MapAvaloniaKey(e.Key)  → XNA Keys
        ▼
EditorBridge.SetHostKeyState((int)key, down) (Engine/Editor/EditorBridge.cs#L70)
        │  stored in a locked HashSet<int> _hostKeysDown
        ▼
Input.IsKeyDown(key)                          merges _hostKeysDown with native state
        │  via HostBridge.IsHostKeyDown((int)key)
        ▼
EditorCamera fly (W/A/S/D/Q/E), etc.
```

Two things make a key reachable in the editor:

1. It must be mapped in
   [`MonoGameHost.MapAvaloniaKey`](../Editor/Anvil/Controls/MonoGameHost.cs#L168)
   (only a curated subset — WASDQE, Shift/Ctrl/Alt, F1–F3, Space, Escape — is
   currently mapped; everything else returns `Keys.None` and is dropped).
2. The consumer must read it via `Input.IsKeyDown`, **not** `WasKeyPressed`.

The handler is registered at the Avalonia *window* level
([`EnsureKeyForwarding`](../Editor/Anvil/Controls/MonoGameHost.cs#L141)), so the
engine sees WASD even when focus is on the toolbar — the reparented engine HWND
never gets keyboard focus directly.

---

## 4. Mouse

### Reading mouse (the consumer API)

| Call | Meaning |
|------|---------|
| `Input.mouseState` / `Input.mouseLastState` | full XNA `MouseState` this/last frame |
| `Input.WasLMBClicked()` | left button rising edge (one click) |
| `Input.IsLMBPressed()` | left button currently held |
| `Input.GetMousePosition()` | cursor `Point` in viewport pixels |
| `Input.GetMousePositionNormalized()` | cursor as `Vector2` in `[0,1]` |
| `mouseState.RightButton == ButtonState.Pressed` | check any button directly |
| `mouseState.ScrollWheelValue - mouseLastState.ScrollWheelValue` | scroll delta |

Unlike keyboard, the mouse is **not** forwarded through the bridge — it is read
straight from Win32 (`Mouse.GetState()`) and works in both contexts.

### The viewport gate (Anvil only)

Because Win32 reports mouse buttons globally, a click on the Inspector would
otherwise reach the engine and pick/move objects. `Input.Update`
([Input.cs#L46‑L93](../Engine/Logic/Input.cs#L46)) solves this with a
**viewport‑ownership latch**:

- A drag is "owned by the viewport" only if its **initial press** landed inside
  the engine's client rect `[0,w) × [0,h)`.
- Once owned, the drag keeps working even if the cursor strays over a panel (so
  RMB‑orbit doesn't break at the viewport edge), until **all buttons release**.
- When the gate is active and input isn't allowed, `Input.Update` synthesizes an
  **idle** `MouseState` (buttons released) so `WasLMBClicked`, camera drag, etc.
  see nothing.

This is all internal — consumers just read `Input.mouseState` and get correctly
gated values. You normally don't touch this when adding a new mouse interaction.

---

## 5. Camera input (special‑cased)

`Input.Update` routes camera control based on the active camera type
([Input.cs#L99](../Engine/Logic/Input.cs#L99)):

- **`EditorCamera`** → `EditorCameraEvents` — DCC‑style controls:
  - RMB drag → yaw/pitch look (`camera.ApplyYawPitch()`)
  - MMB drag → pan
  - Scroll → dolly forward/back
  - WASD/QE → fly, **only while RMB is held** (uses `Input.IsKeyDown`, so it
    works in Anvil), and only when the console is closed.
- **Any other `Camera`** (game/play camera) → legacy `KeyboardEvents` +
  `MouseEvents` free‑flight, preserved so Play mode is unchanged.

---

## 6. Where input is consumed (the map)

| System | File | Reads |
|--------|------|-------|
| App exit (`Escape`) | [Engine.cs#L168](../Engine/Engine.cs#L168) | `WasKeyPressed` |
| Global keybinds: editor toggle (`Space`), spawn light (`L`), render‑mode cycle (`F1`), audio test (`X`/`M`) | [MainSceneLogic.cs#L245‑L282](../Engine/Logic/MainSceneLogic.cs#L245) | `WasKeyPressed`, `IsKeyDown` |
| Editor selection / gizmos / delete (`Delete`) / copy (`Ctrl+C`,`Insert`) / mode (`R`/`T`/`Z`) | [EditorLogic.cs](../Engine/Logic/EditorLogic.cs#L73) | `WasLMBClicked`, `WasKeyPressed`, `mouseState` |
| Debug console: open (`|`), edit, submit | [DebugScreen.cs#L106‑L140](../Engine/Logic/DebugScreen.cs#L106) | `WasKeyPressed`, `GetKeyPressed` |
| Editor outline/hover redraw | [EditorRender.cs#L42](../Engine/Renderer/RenderModules/EditorRender.cs#L42) | `mouseState` |
| Debug ray‑march probe | [CPURayMarch.cs](../Engine/Recources/Helper/CPURayMarch.cs#L75) | `GetMousePositionNormalized` |
| Camera fly/orbit/pan/zoom | [Input.cs](../Engine/Logic/Input.cs#L132) | internal |

**Console gate:** most global/editor keybinds are skipped while the console is
open via an early `if (DebugScreen.ConsoleOpen) return;`. Respect that guard when
adding new keybinds so typing into the console doesn't trigger actions.

---

## 7. How to add a new input

Pick the recipe that matches what you want. Each is a small, local change.

### 6.1 — Add a global keybind (works in standalone; held‑key version works in Anvil too)

Add to [`MainSceneLogic.Update`](../Engine/Logic/MainSceneLogic.cs#L218), **after**
the `if (DebugScreen.ConsoleOpen) return;` guard (so it's suppressed while typing
in the console):

```csharp
// One-shot action (standalone only — WasKeyPressed is native-state only)
if (Input.WasKeyPressed(Keys.G))
{
    DoMyThing();
}

// Held-key action that ALSO works inside Anvil (uses the merged host set)
if (Input.IsKeyDown(Keys.H))
{
    DoMyContinuousThing();
}
```

If the key must work **inside the Anvil editor too**, also add it to
[`MonoGameHost.MapAvaloniaKey`](../Editor/Anvil/Controls/MonoGameHost.cs#L168):

```csharp
Key.G => XnaKeys.G,
Key.H => XnaKeys.H,
```

…and read it with `Input.IsKeyDown` (not `WasKeyPressed`).

### 6.2 — Add an editor‑mode keybind (selection/gizmo context)

Add to [`EditorLogic.Update`](../Engine/Logic/EditorLogic.cs#L73). It already
guards `if (!GameSettings.e_enableeditor) return;` and checks
`!DebugScreen.ConsoleOpen`:

```csharp
if (!DebugScreen.ConsoleOpen)
{
    if (Input.WasKeyPressed(Keys.F)) FocusOnSelectedObject();
}
```

Remember: this fires in standalone. In Anvil, prefer routing the same action
through a toolbar button + bridge (§6.5), or forward the edge (§6.4).

### 6.3 — Add a mouse interaction

Read `Input.mouseState` / the helpers anywhere that runs each frame (typically
`EditorLogic` or a render module). The viewport gate (§4) already keeps clicks
from leaking across Avalonia panels — you don't need to handle that yourself:

```csharp
if (Input.WasLMBClicked())
{
    Point p = Input.GetMousePosition();          // viewport pixels
    Vector2 uv = Input.GetMousePositionNormalized();
    // …do picking, painting, etc.
}
```

### 6.4 — (Advanced) Make a **one‑shot** key work inside Anvil

The bridge currently forwards only *held* state (`_hostKeysDown`), so
`WasKeyPressed` can't see forwarded keys. To get edge detection for a forwarded
key without routing through the UI, add a tiny "was‑down‑last‑frame" check on top
of the host set:

1. Map the key in
   [`MonoGameHost.MapAvaloniaKey`](../Editor/Anvil/Controls/MonoGameHost.cs#L168).
2. In [`Input.cs`](../Engine/Logic/Input.cs), add a host‑aware edge helper that
   remembers the previous merged state:

```csharp
// in Input
private static readonly HashSet<Keys> _hostEdgePrev = new();

/// Rising-edge that also sees Anvil-forwarded keys.
public static bool WasKeyPressedHosted(Keys key)
{
    bool now  = IsKeyDown(key);                 // native + host
    bool prev = _hostEdgePrev.Contains(key);
    if (now) _hostEdgePrev.Add(key); else _hostEdgePrev.Remove(key);
    return now && !prev;
}
```

   (Call it once per key per frame; for many keys, track a snapshot set in
   `Input.Update` instead.) Then consume with `Input.WasKeyPressedHosted(...)`.

> Simpler alternative: for editor one‑shots, prefer §6.5 — a toolbar button +
> bridge command is the established pattern (it's how gizmo‑mode switching and
> Play/Stop already work in Anvil).

### 6.5 — Add a UI‑driven action (Anvil toolbar / menu → engine)

This is "input" too, just from the editor UI, and it's the recommended path for
editor commands. The flow is **UI command → `IEditorBridge` → engine game thread**.

1. Add a method to [`IEditorBridge`](../Engine/Editor/IEditorBridge.cs) (e.g.
   `void RequestMyAction();`) — mirror the existing `RequestPlay` / `RequestStop`
   / `RequestGizmoMode` pattern.
2. Implement it in [`EditorBridge`](../Engine/Editor/EditorBridge.cs) by enqueuing
   onto the game‑thread queue (use `EnqueueGameThreadAction` or a dedicated queue)
   — **never mutate engine state from the UI thread**.
3. Add a command in
   [`MainWindowViewModel`](../Editor/Anvil/ViewModels/MainWindowViewModel.cs) and
   bind a button to it in
   [`MainWindow.axaml`](../Editor/Anvil/Views/MainWindow.axaml).

The engine drains the queue between frames, so the action runs safely on the
game thread.

### 6.6 — Add a debug console command

The console (`|` to open) already auto‑discovers and sets `GameSettings`
properties by reflection
([DebugScreen.cs#L145](../Engine/Logic/DebugScreen.cs#L145)). To expose a new
toggle/value, just add a public property to
[`GameSettings`](../Engine/Recources/GameSettings.cs) — no input wiring needed.
For richer commands, extend `UseConsoleCommand()` in `DebugScreen`.

---

## 8. Rules & gotchas

- **One update site.** `Input.Update` runs once per frame from
  `MainSceneLogic.Update`. Don't call `Keyboard.GetState()` / `Mouse.GetState()`
  yourself elsewhere — read through `Input` so you get the same gated, edge‑aware
  state everyone else sees.
- **`WasKeyPressed` ≠ Anvil.** Native‑state edge helpers don't fire when hosted.
  Use `Input.IsKeyDown` (held) or a UI/bridge command (one‑shot) for editor
  features.
- **Map before you forward.** A key not in `MonoGameHost.MapAvaloniaKey` is
  invisible to the engine inside Anvil, no matter how you read it.
- **Console guard.** Gate new keybinds behind `!DebugScreen.ConsoleOpen` so they
  don't fire while the user is typing.
- **Game thread only.** From Avalonia, never touch engine lists/objects directly;
  enqueue via `IEditorBridge`. From the engine side, mutating scene state inside
  an input handler is fine — it's already on the game thread.
- **Mouse coordinates** are relative to the engine viewport's client area; the
  gate keeps panel clicks out, but if you add a feature that needs absolute screen
  coordinates you must convert yourself.

---

## 9. Quick reference

| I want to… | Do this |
|------------|---------|
| Held key, both contexts | `Input.IsKeyDown(Keys.X)` + map in `MapAvaloniaKey` |
| One‑shot key, standalone only | `Input.WasKeyPressed(Keys.X)` |
| One‑shot action in the editor | Toolbar button → `IEditorBridge` command (§6.5) |
| Detect a click | `Input.WasLMBClicked()` |
| Held mouse button | `Input.IsLMBPressed()` / `mouseState.<Button> == ButtonState.Pressed` |
| Mouse position | `Input.GetMousePosition()` / `GetMousePositionNormalized()` |
| Scroll amount | `mouseState.ScrollWheelValue - mouseLastState.ScrollWheelValue` |
| Text entry | `Input.GetKeyPressed()` (see `DebugScreen`) |
| New tunable/toggle | Add a `GameSettings` property → usable from console |

**Key files:** [Input.cs](../Engine/Logic/Input.cs) ·
[MonoGameHost.cs](../Editor/Anvil/Controls/MonoGameHost.cs) ·
[EditorBridge.cs](../Engine/Editor/EditorBridge.cs) ·
[IEditorBridge.cs](../Engine/Editor/IEditorBridge.cs) ·
[EditorLogic.cs](../Engine/Logic/EditorLogic.cs) ·
[MainSceneLogic.cs](../Engine/Logic/MainSceneLogic.cs) ·
[DebugScreen.cs](../Engine/Logic/DebugScreen.cs)
