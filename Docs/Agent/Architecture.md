# Architecture

Moved from `CLAUDE.md`; read when the task touches this area.

## Runtime Flow

Standalone engine:

1. `Engine/Program.cs` creates `Engine.Engine` and calls `Run()`.
2. `Engine.Engine` sets content root to `Content`, creates `EditorBridge`, `ScreenManager`, graphics, and BEPU physics.
3. `ScreenManager.Load()` loads `Globals.content`, `Shaders`, `ShaderManager`, `Assets`, renderer modules, scene/debug/gui/video content, and the UI fonts for `GameUI`.
4. `ScreenManager.Initialize()` initializes renderer, scene, GUI, editor logic, debug UI, and binds the bridge to scene/editor/assets.
5. `VideoIntroLogic` plays the intro videos of `Content/System/IntroVideos.json` (`IntroVideoList`, Anvil > Game Settings > Intro Videos) in order; Enter skips one. When the last ends, `MainSceneLogic.StartFirstScene()` loads scene list entry 0 (`Content/System/SceneList.json`, the MainMenu sample) and starts Play. Anvil skips this.
6. Per frame: `ScreenManager.Update()` updates logic, shader hot reload in debug, editor logic, scene (queued `GameFlow` scene loads apply first, then `Input`/`GameInput`, then scripts), renderer SDFs, debug screen, `GameUI` layers, then drains bridge operations and publishes snapshots.
7. `ScreenManager.Draw()` draws intro video or the main renderer, `GameUI` layers, then the `DebugScreen` stats/console on top.

Anvil editor:

1. `Editor/Anvil/Program.cs` starts Avalonia.
2. `MainWindow.axaml` hosts `MonoGameHost`.
3. `MonoGameHost` starts `Engine.Engine` on an STA thread, reparents the MonoGame HWND under Avalonia, and manually drives `RunOneFrame()`.
4. `BridgeReady` passes `IEditorBridge` to `MainWindowViewModel`.
5. UI changes enqueue bridge mutations; the engine drains them on the game thread.
6. Engine snapshots are throttled in `EditorBridge` and reconciled into existing view models by `BridgeReconciler` to avoid losing binding/focus state.

## Renderer Flow

`Renderer.Draw(...)` is the main pipeline. Important phases:

1. Reset stats and update mesh/material movement.
2. Check render setting changes.
3. Render shadow maps.
4. Update SDF/environment maps when needed.
5. Update camera view/projection matrices.
6. Draw G-buffer and deferred decals.
7. Draw SSR, SSAO, screen-space directional shadows, bilateral blur, froxel fog, deferred lights, and environment lighting.
8. Compose, forward-render transparent/special materials, apply TAA and bloom.
9. Draw selected render mode/final post-processing.
10. Draw editor outlines, gizmos, helpers, and debug overlays.

When adding a render feature, expect to touch:

- `Engine/Renderer/RenderModules/...`
- `Engine/Renderer/Renderer.cs`
- `Engine/Recources/Shaders.cs`
- `Engine/Recources/GameSettings.cs`
- `Engine/Content/Shaders/...`
- `Engine/Content/Content.mgcb`

## Asset Pipeline

This repo does not have broad automatic asset discovery. Assets generally flow through the MonoGame content pipeline:

1. Put raw assets under `Engine/Content`.
2. Add them to `Engine/Content/Content.mgcb`.
3. Build the project so MonoGame produces `.xnb` outputs.
4. Load them manually with extensionless `content.Load<T>("path/without/extension")`.
5. Register common runtime assets in `Engine/Recources/Assets.cs`.

Important types:

- `Assets` - central hard-coded registry for models, textures, fonts, sky maps, and materials.
- `ModelDefinition` - wraps `Model`, bounding boxes, and signed distance field sidecars.
- `MaterialEffect` - engine material type and texture-slot flags.
- `MeshMaterialLibrary` - registers/draws mesh/material batches; delete removed `BasicEntity` instances from it.

