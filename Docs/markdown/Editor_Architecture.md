# Anvil Editor: Architecture

> Companion diagram: [Editor_Architecture.excalidraw](Editor_Architecture.excalidraw) — open `Docs/Editor_Architecture.excalidraw` in [excalidraw.com](https://excalidraw.com) or the VS Code Excalidraw extension.
>
> This document describes **the editor (Anvil) and how it talks to the engine**. For the engine's render pipeline see [architecture.excalidraw](architecture.excalidraw); for the in-engine UI layer see [VistaUI_Architecture.md](VistaUI_Architecture.md).

---

## Overview

**Anvil** is an [Avalonia](https://avaloniaui.net/) desktop application that hosts a live instance of the MonoGame **Engine** inside its window. The editor does not re-implement the renderer or the scene — it *embeds the real engine* and drives it.

Two worlds with very different threading models have to cooperate:

| World | Thread | Owns |
|-------|--------|------|
| **Anvil** (`Editor/Anvil`) | Avalonia **UI thread** | MVVM view-models, observable collections, XAML bindings, user input |
| **Engine** (`Engine`) | MonoGame **STA game thread** | scene lists, physics, renderer, picking, gizmos |

They are connected by a single seam: the **`EditorBridge`** (`Engine/Editor/EditorBridge.cs`), implementing **`IEditorBridge`** (`Engine/Editor/IEditorBridge.cs`). The bridge is the *only* sanctioned cross-thread channel. Everything obeys two rules:

- **UI → Engine** changes are **queued** and applied on the game thread between frames (never mutate engine lists from Avalonia).
- **Engine → UI** changes are **snapshotted** and **reconciled** into existing view-models, marshalled onto the UI thread.

---

## Component Map

### Avalonia side (`Editor/Anvil`)

| File | Role |
|------|------|
| `Program.cs` / `App.axaml.cs` | Avalonia bootstrap; creates `MainWindow` + `MainWindowViewModel`. |
| `Controls/MonoGameHost.cs` | `NativeControlHost` that launches the engine on an STA thread, reparents its **HWND** under Avalonia, syncs resize, and forwards keyboard input. Raises **`BridgeReady`** with the `IEditorBridge`. |
| `Views/MainWindow.axaml(.cs)` | Editor layout (tool rail, viewport, hierarchy, inspector, console). Tracks inspector **focus** to freeze snapshot writes while the user edits. |
| `ViewModels/MainWindowViewModel.cs` | Editor state and commands; subscribes to bridge events; owns the `SceneObjects` tree. |
| `Models/SceneObjectViewModel.cs` | Per-object hierarchy/inspector VM. Property setters enqueue engine mutations (gated by `SuppressPush`). |
| `Services/BridgeReconciler.cs` | Merges an incoming snapshot into the existing `SceneObjects` collection without destroying VM instances or stealing focus. |

### Engine side (`Engine`)

| File | Role |
|------|------|
| `Engine.cs` | MonoGame `Game`; creates the `EditorBridge`, `ScreenManager`, graphics, physics. Handles deferred resize. |
| `Editor/IEditorBridge.cs` | The bridge **contract**: snapshot structs, mutation methods, events, host-input plumbing. |
| `Editor/EditorBridge.cs` | Thread-safe op queue + snapshot publisher. Bound to scene/editor/assets via `Bind(...)`. |
| `Logic/ScreenManager.cs` | Per-frame orchestration; calls `bridge.Bind(...)` at init and drains/publishes the bridge each `Update()`. |
| `Logic/EditorLogic.cs` | In-engine selection, picking (ID buffer + ray sweep), and gizmos (`GizmoModes`). |
| `Logic/MainSceneLogic.cs` | Owns the live scene lists and the `EditorAdd*` / `EditorDelete` helpers the bridge calls. |

---

## The Bridge Contract

These are the exact types and members the rest of the doc refers to (from `IEditorBridge.cs`).

### Snapshot structs (engine → UI, immutable)

```csharp
enum EditorObjectKind { BasicEntity, PointLight, DirectionalLight, Camera, Decal }

readonly struct LightSnapshot     { Color Color; float Intensity; float Radius;
                                    bool CastShadows; bool IsDirectional; Vector3 Direction; }

readonly struct MaterialSnapshot  { Vector3 DiffuseColor; float Roughness; float Metallic;
                                    float EmissiveStrength; bool IsTransparent; int MaterialType; }

readonly struct EditorObjectSnapshot {
    int Id; string Name; EditorObjectKind Kind;
    Vector3 Position; Matrix Rotation; Vector3 Scale; bool IsEnabled;
    LightSnapshot? Light; MaterialSnapshot? Material;
}
```

### Key interface members

```csharp
// Snapshots (engine -> UI)
IReadOnlyList<EditorObjectSnapshot> Snapshot { get; }
event Action<IReadOnlyList<EditorObjectSnapshot>> SnapshotUpdated;

// Selection (bi-directional)
int? SelectedId { get; }
event Action<int?> SelectionChanged;
void RequestSelect(int? id);

// Mutations — all QUEUED, run on the game thread between frames
void EnqueueGameThreadAction(Action action);
void EnqueueMutate(int id, Action<TransformableObject> mutate);
void EnqueueMutateMaterial(int entityId, Action<MaterialEffect> mutate);
void EnqueueAddPointLight(Vector3 position, float radius, Color color, float intensity);
void EnqueueAddDirectionalLight(Vector3 direction, Color color, float intensity);
void EnqueueAddBasicEntity(string modelKey, Vector3 position);
void EnqueueDelete(int id);

// Gizmo / play mode
void RequestGizmoMode(Logic.EditorLogic.GizmoModes? mode);   // null = suppress gizmo drag
Logic.GameMode Mode { get; }
void RequestPlay(); void RequestStop();
event Action<Logic.GameMode> ModeChanged;

// Scene file ops (queued)
void EnqueueNewScene(); void EnqueueLoadScene(string path); void EnqueueSaveScene(string path);
string CurrentScenePath { get; } string CurrentSceneName { get; } bool IsSceneDirty { get; }
event Action SceneChanged;

// Asset pipeline (runtime import on the engine thread)
IReadOnlyList<string> AvailableModelKeys { get; }
void EnqueueImportModel(string sourceFilePath, Action<string> onCompleted);
void EnqueueImportTextures(string modelKey, string[] sourcePaths, Action onCompleted);
void EnqueueDeleteModelAsset(string modelKey, Action onCompleted);
IReadOnlyList<string> GetModelTextureFiles(string modelKey);
bool IsDeletableModel(string modelKey);
event Action ModelRegistryChanged;

// Host input plumbing (Avalonia owns focus, so the engine reads forwarded state)
bool IsHostedByEditor { get; }
bool IsHostKeyDown(int xnaKeyCode); void SetHostKeyState(int xnaKeyCode, bool down);
bool IsHostPointerOverViewport { get; } void SetHostPointerOverViewport(bool inside);
Vector3 SpawnPoint { get; }   // point in front of the editor camera, updated each frame
```

---

## Startup & Embedding

1. `Program.Main` → Avalonia desktop lifetime → `MainWindow` + `MainWindowViewModel`.
2. `MainWindow` hosts a **`MonoGameHost`** control.
3. On `CreateNativeControlCore`, `MonoGameHost`:
   - sets the working directory to the Engine assembly folder (so MonoGame resolves `Content/` paths),
   - starts an **STA game thread** that constructs `Engine.Engine`,
   - extracts the engine's **HWND**, creates an intermediate container window under the Avalonia parent, and **reparents** the engine window beneath it,
   - runs a manual message pump + `RunOneFrame()` loop (instead of the blocking `Game.Run()`),
   - runs a short boot-resize watchdog to defeat MonoGame's startup `640×480` shrink.
4. The game thread posts **`BridgeReady(IEditorBridge)`** to the UI thread; `MainWindow` forwards it to `MainWindowViewModel`, which subscribes to all bridge events.
5. On the engine side, `ScreenManager.Initialize()` calls `bridge.Bind(sceneLogic, editorLogic, assets)`, wiring the bridge to the live scene.

Because the engine HWND is reparented, it never has keyboard focus — so `MonoGameHost` translates Avalonia key events to XNA key codes and pushes them via `SetHostKeyState`. The engine's input layer reads `IsHostKeyDown` / `IsHostPointerOverViewport` instead of `Keyboard.GetState()` whenever `IsHostedByEditor` is true.

---

## Data Flow

### Mutation path — UI → Engine (orange in the diagram)

```
User edits inspector / clicks a command
   └─ SceneObjectViewModel setter (if !SuppressPush)  /  MainWindowViewModel command
        └─ bridge.Enqueue*(...)            // lock-free push onto _pendingOps (ConcurrentQueue)
             └─ [game thread] ScreenManager.Update -> bridge.DrainAndPublish()
                  └─ action runs: looks up object by Id, mutates MainSceneLogic / EditorLogic
                       └─ next frame: physics + renderer observe the new state
```

Example — dragging Position X:

```csharp
// SceneObjectViewModel
PositionX setter → bridge.EnqueueMutate(EngineId,
    obj => obj.Position = new Vector3(value, PositionY, PositionZ));
```

### Snapshot path — Engine → UI (blue in the diagram)

```
[game thread] bridge.DrainAndPublish()  (once per frame)
   └─ every PublishEveryNFrames: BuildSnapshot() walks entities/lights/decals (+ synthetic camera)
        └─ fires SnapshotUpdated, marshalled to the UI thread (Dispatcher.UIThread)
             └─ MainWindowViewModel.OnBridgeSnapshot
                  └─ BridgeReconciler.Apply(snapshot, SceneObjects, bridge)
                       └─ reuse existing VMs, create/remove as needed, copy fields
                            └─ XAML bindings update the hierarchy + inspector
```

Snapshots are **throttled** (`PublishEveryNFrames`) and **focus-aware**: the reconciler will not overwrite the fields of the object currently being edited in the inspector, and it skips sub-epsilon float deltas so a `NumericUpDown` doesn't lose focus to render-jitter.

### Selection — bi-directional

- **Viewport → UI:** `EditorLogic` picks an object → bridge fires `SelectionChanged` → VM sets `SelectedObjectId`.
- **Hierarchy → engine:** selecting in the tree calls `RequestSelect(id)` → queued onto the engine.
- A round-trip guard prevents `engine → UI → engine` selection loops.

---

## Threading & Synchronisation

| State | Writer | Reader | Mechanism |
|-------|--------|--------|-----------|
| Mutation queue (`_pendingOps`) | UI thread | game thread | `ConcurrentQueue<Action>` (lock-free) |
| Snapshot publication | game thread | UI thread | event + `Dispatcher.UIThread.Post` |
| Forwarded keyboard state | UI thread | game thread | `lock` over a key set |
| `IsHostPointerOverViewport`, `IsHostedByEditor` | UI thread | game thread | `volatile` bool |

Invariants:

- **Never** touch engine scene lists from the Avalonia thread — always go through `Enqueue*`.
- **Never** touch Avalonia VMs / collections from the game thread — always marshal via the bridge events (which `MainWindowViewModel` re-dispatches to the UI thread).
- VM property setters are no-ops while `SuppressPush` is set (the reconciler sets it while writing snapshot values back, so applying a snapshot doesn't re-enqueue a mutation).

---

## How to Extend This

The architecture is designed to grow along its existing seams. Common changes (see also `CLAUDE.md` → *Common Change Recipes*):

### Add an editor-backed scene property
1. Add the field to the relevant snapshot struct in `IEditorBridge.cs` (`EditorObjectSnapshot` / `LightSnapshot` / `MaterialSnapshot`).
2. Populate it in `EditorBridge.BuildSnapshot()`.
3. Copy it in `BridgeReconciler` (respect the focus-freeze + epsilon rules).
4. Expose it on `SceneObjectViewModel` with a setter that calls `EnqueueMutate` / `EnqueueMutateMaterial`.
5. Bind it in `MainWindow.axaml`.

### Add a new mutation / command
1. Add an `Enqueue*` method to `IEditorBridge` + implement it in `EditorBridge` (push an action onto `_pendingOps`).
2. Back it with an `EditorAdd*` / helper on `MainSceneLogic` (or `EditorLogic`) that runs on the game thread.
3. Add a command in `MainWindowViewModel` and bind it from `MainWindow.axaml`.

### Add a new object kind
1. Extend `EditorObjectKind`.
2. Emit it from `BuildSnapshot()`.
3. Handle it in `BridgeReconciler` / `SceneObjectViewModel` (icon, inspector section).
4. Add an `EnqueueAdd…` + `EditorAdd…` pair.

### Tuning knobs
- `EditorBridge.PublishEveryNFrames` — snapshot cadence (lower latency vs. more focus churn).
- `BridgeReconciler` float epsilon — how much render-jitter to ignore.
- `MonoGameHost` resize debounce / boot watchdog — embedded viewport behaviour.

### Keep the diagram in sync
`Editor_Architecture.excalidraw` mirrors this structure: blue = UI thread, green = game thread, orange = bridge/boundary, purple = HWND host. When you add a panel, snapshot field, or mutation, add the matching box/arrow so the picture stays truthful.

---

## File Reference

```
Editor/Anvil/
├── Program.cs / App.axaml.cs            Avalonia bootstrap
├── Controls/MonoGameHost.cs             HWND reparenting, STA game thread, resize, key forwarding
├── Views/MainWindow.axaml(.cs)          layout + focus tracking + BridgeReady wiring
├── ViewModels/MainWindowViewModel.cs    commands, SceneObjects, bridge event handlers
├── Models/SceneObjectViewModel.cs       per-object transform/material props, SuppressPush
└── Services/BridgeReconciler.cs         snapshot -> VM merge (focus-aware)

Engine/
├── Engine.cs                            MonoGame Game; creates bridge; deferred resize
├── Editor/IEditorBridge.cs              contract: snapshot structs, mutations, events
├── Editor/EditorBridge.cs               op queue + snapshot publisher; Bind(...)
└── Logic/
    ├── ScreenManager.cs                 per-frame orchestration; bind + DrainAndPublish
    ├── EditorLogic.cs                   picking, gizmos, SelectedObject
    └── MainSceneLogic.cs                scene lists; EditorAdd* / EditorDelete
```
