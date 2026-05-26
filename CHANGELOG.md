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

### Phase 8 — TODO bugfix pass

Addresses the `Docs/TODO.md` issue list. HelperSuite removal is intentionally
deferred until Vista/Anvil cover the in-engine GUI surface.

- **`IEditorBridge.IsHostedByEditor`** — set by `MonoGameHost` before the first
  frame ticks. Anvil now hides the legacy HelperSuite GUI (Update + Draw) so the
  in-engine panel doesn't double up with Anvil's inspector. Standalone
  `Engine.exe` is unchanged — the side panels still appear and Space still toggles.
- **`GameStats.e_EnableSelection` defaults on under Anvil** — this flag gates the
  ID-buffer / outline / gizmo render passes. In standalone it defaulted to `false`
  and the legacy HelperSuite GUI's "Editor Mode" toggle flipped it. With that GUI
  hidden under Anvil, viewport clicks resolved to no entity and felt dead.
  `ScreenManager.Initialize` now forces it on when `IsHostedByEditor` is true;
  standalone keeps the default-off behaviour.
- **Add GameObject crash** — `EditorBridge.RequestSelect` no longer clobbers the
  current selection with null when `LookupById` misses. A transient miss (entity
  just added, not yet in snapshot; or just deleted) was causing the Inspector to
  open then close immediately. Combined with the GUILogic gating above, the
  legacy in-engine inspector no longer touches freshly added entities.
- **NewScene break** — `MainSceneLogic.OnSceneChanged` now populates a default
  `MainCamera` + `EnvironmentSample` on empty scenes (a fresh `Scene{}` has both
  null and the renderer's environment probe NREs). Also calls `PlayMode.Stop()`
  on scene swap so Play-mode state from the old scene can't bleed into the new
  one. Matches the implicit reset that LoadScene already gets through deserialized
  fields.
- **Save/load drops textures** — `SceneSerialization.ResolveMaterial` now returns
  `null` for entities saved without a custom material so `BasicEntity` keeps the
  model-embedded materials (Sponza textures stay intact). For entities with a
  custom material, `MaterialRecord` now persists `AlbedoKey`/`NormalKey`/
  `RoughnessKey`/`MetallicKey`/`MaskKey` looked up via `Assets`' field names, and
  the load path restores those textures.
- **RMB + WASD camera** — `IEditorBridge.SetHostKeyState` / `IsHostKeyDown` plus
  a thread-safe `HashSet<int>` on `EditorBridge`. `MonoGameHost` subscribes to
  `KeyDown`/`KeyUp` at the `TopLevel` (so the engine sees WASD even when focus
  is on the toolbar) and forwards mapped Avalonia keys. `Input.IsKeyDown(Keys)`
  merges native + forwarded state; `EditorCameraEvents` uses it for WASD/QE.
- **Select tool** — `EditorLogic.EditorSendData.GizmoSuppressed` propagated to
  `EditorRender.DrawGizmo` and `IdAndOutlineRenderer.DrawGizmos`. Both now skip
  the arrow draw when Anvil's Select tool is active, so the visible gizmo
  doesn't intercept clicks and the ID buffer never returns gizmo IDs 1-3.
- **Inspector focus** — `MainWindow.OnAnyLostFocus` no longer leaves
  `IsInspectorFocused` stale when focus exits the inspector. It defers to the
  next dispatcher tick and reads `FocusManager.GetFocusedElement`, clearing the
  flag once focus has settled outside the inspector ScrollViewer.
