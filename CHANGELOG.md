# Changelog

## Editor overhaul (engine-3d branch)

Implements the TODO in `Docs/TODO.md`. Phased plan in
`C:/Users/mano3/.claude/plans/docs-todo-md-read-the-todo-wobbly-fern.md`.

### Phase 0 — Foundations

- **EditorBridge logging**: secondary log path at `%LOCALAPPDATA%/Anvil/anvil-bridge.log`
  when the Desktop write fails; static-ctor bootstrap line so crashes before `Bind()`
  still leave a breadcrumb; `MonoGameHost` now logs engine construction + a fatal
  catch for `GameThreadProc` so an early crash is no longer silent.
- **`EditorObjectKind.Decal`** added; `EditorBridge.BuildSnapshot` and
  `LookupById` now include decals so they show up in the hierarchy.
- **`Engine/Renderer/Helper/Picking.cs`**: `ScreenPointToWorldRay`,
  `RayIntersectsAabb`, `PickEntity(ray, entities)`, `ComputeWorldBoundingBox`.

### Phase 1 — Scene + SceneManager refactor & inspector focus fix

- **`Engine/Logic/Scene.cs`** owns the lists previously hard-wired into
  `MainSceneLogic`: `BasicEntities`, `Decals`, `PointLights`, `DirectionalLights`,
  `EnvironmentSample`, `MainCamera`, plus `Name`, `FilePath`, `IsDirty`.
- **`Engine/Logic/SceneManager.cs`** owns the active `Scene`, exposes
  `SceneChanged` event, `NewScene`, `LoadScene`, `SaveScene`.
- **`MainSceneLogic`** keeps runtime drivers (physics, mesh library, SDF, editor
  camera, debug entities) and forwards `BasicEntities`/`Decals`/`PointLights`/
  `DirectionalLights`/`EnvironmentSample`/`Camera` to the active scene so existing
  callers (ScreenManager, Renderer, EditorLogic) compile unchanged. `OnSceneChanged`
  detaches old physics bodies, clears `MeshMaterialLibrary`, and re-registers the
  new scene's content.
- **`MeshMaterialLibrary.Clear()`** added.
- **Inspector focus fix**: `BridgeReconciler.IsInspectorFocused` + `SelectedEngineId`
  combined with window-wide focus tracking in `MainWindow.axaml.cs`. When the user
  is editing a control inside the inspector ScrollViewer (`InspectorScroll`), the
  reconciler skips numeric/material/light writes to the selected object — focus is
  no longer stolen mid-edit. Also stopped nulling `vm.Material` / `vm.Light` /
  `vm.Camera` while the inspector is focused (was collapsing open expanders/flyouts).

### Phase 2 — `.obsc` scene file format

- **`Engine/Logic/SceneSerialization.cs`** — System.Text.Json-based, schema-versioned
  (`Version: 1`). Custom `Vector3`/`Quaternion`/`Color` converters keep files compact
  (`[x,y,z]`). Models/textures referenced by their `Assets` field-name (the same
  string the bridge uses for the model picker). Material values inlined per-entity
  on top of a cloned `BaseMaterial`. Rotation stored as quaternion to avoid matrix
  drift on round-trip. `IdGenerator.Reseed(maxId)` after load.
- **Bridge ops**: `EnqueueNewScene`, `EnqueueLoadScene`, `EnqueueSaveScene`,
  `CurrentScenePath`, `CurrentSceneName`, `IsSceneDirty`, `event SceneChanged`.
- **Anvil File menu** wired to `NewSceneCommand` / `OpenSceneCommand` /
  `SaveSceneCommand` / `SaveSceneAsCommand`. Uses Avalonia `StorageProvider` for
  file pickers; remembers the last scene folder.

### Phase 3 — Editor camera

- **`Engine/Entities/EditorCamera.cs`** — `Camera` subclass with scalar `Yaw`/`Pitch`,
  pitch clamped to ±88.8° to prevent flip, `ApplyYawPitch()` rebuilds forward.
- **`Input.UpdateEditorCamera`** drives the new editor camera with DCC-style
  controls: RMB drag = look (yaw + pitch), MMB drag = pan along right/up,
  scroll wheel = zoom along forward, WASD/QE = fly *only while RMB held* (so it
  doesn't fight the Avalonia UI).
- `MainSceneLogic.Camera` now picks between `EditorCamera` (edit mode) and
  `ActiveScene.MainCamera` (play mode). Game camera serialized to `.obsc`,
  editor camera is not.

### Phase 4 — Tool buttons → gizmo wiring

- `IEditorBridge.RequestGizmoMode(GizmoModes?)` with null meaning the Select tool
  (gizmo hidden, picking only). `EditorLogic.IsGizmoSuppressed` flag honored by
  the LMB click branch.
- `MainWindowViewModel.OnActiveToolChanged` forwards Move/Rotate/Scale → engine
  gizmo mode; Select sets the suppressed flag. T/R/Z hotkeys still work.

### Phase 5 — Raycast object selection fallback

- `EditorLogic.Update` now falls back to `Picking.PickEntity(ray, entities)` when
  the render-target ID buffer reports no hit. Lets selection keep working in
  render modes that skip the ID pass.

### Phase 6 — Play/Stop loop hook

- **`Engine/Logic/PlayMode.cs`** — `GameMode { Edit, Play }` + `PlayModeController`.
  `Play()` snapshots transforms (entities/decals/lights), drops
  `GameSettings.e_enableeditor`, and fires `OnStart` on every attached script.
  `Stop()` restores transforms and the editor flag.
- Bridge: `Mode`, `RequestPlay`, `RequestStop`, `event ModeChanged`. Anvil's
  existing Play/Stop button (`TogglePlay` command) now drives these; engine-side
  mode changes flow back to keep `IsPlaying` in sync.

### Phase 7 — `IScript` sketch

- **`Engine/Scripting/IScript.cs`** — `IScript { OnStart(ctx); OnUpdate(ctx, gt); }`
  + `IScriptContext { Owner, Scene }`. Scripts attached via
  `BasicEntity.Scripts` (`List<IScript>`). Not serialized in `.obsc` v1.
- `PlayModeController.UpdateScripts(gameTime)` ticks scripts every frame while
  in Play mode; called from `MainSceneLogic.Update`. Exceptions logged, not fatal.
