# CLAUDE.md

## Project Snapshot

Obsidian is a C#/.NET 10 game engine/editor workspace. The runtime is a MonoGame WindowsDX engine, the editor is an Avalonia desktop app named Anvil, and `Vista` is a custom XML/CSS UI layer rendered inside MonoGame.

Solution projects:

- `Engine/Engine.csproj` - MonoGame WindowsDX executable and core engine.
- `Editor/Anvil/Anvil.csproj` - Avalonia editor shell that embeds the engine viewport.
- `Vista/Vista.csproj` - lightweight XML/CSS UI renderer built on AngleSharp + MonoGame.

## Quick Commands

Use PowerShell from the repo root unless noted.

```powershell
dotnet restore
dotnet build Engine.sln /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
dotnet run --project Engine\Engine.csproj
dotnet run --project Editor\Anvil\Anvil.csproj
```

Notes:

- The engine targets `net10.0-windows` and `MonoGame.Framework.WindowsDX` 3.8.4.1.
- The Avalonia desktop app uses version 12.0.3
- VS Code's `build` task builds `Engine/Engine.csproj`; the launch config runs `Engine/bin/Debug/net10.0-windows/Engine.dll` with `cwd` set to `Engine`.
- `Engine/dotnet-tools.json` pins `dotnet-mgcb` 3.8.4.1 for the MonoGame content pipeline.
- `bootstrap.bat` is intended to restore/build, but this checkout appears to contain `necho`/`ndotnet` tokens. Prefer the explicit `dotnet` commands above unless that file is fixed.

## Repo Map

- `README.md`, `DOCUMENTATION.md` - high-level intro, controls, feature list, and planned work.
- `Docs/TODO.md` - current editor issues.
- `Docs/Importing Structure.md` - best summary of the asset/content pipeline.
- `Engine/Program.cs` - standalone engine entry point.
- `Engine/Engine.cs` - MonoGame `Game` subclass; creates graphics, physics, `ScreenManager`, and `EditorBridge`.
- `Engine/Logic/ScreenManager.cs` - central coordinator for load/init/update/draw across renderer, scene logic, GUI, editor logic, debug screen, intro video, and Vista UI.
- `Engine/Logic/MainSceneLogic.cs` - owns the live scene lists: entities, decals, lights, debug entities, camera, environment sample, and editor-side add/delete helpers.
- `Engine/Logic/EditorLogic.cs` - in-engine editor mode, selection, gizmos, delete/copy behavior.
- `Engine/Renderer/Renderer.cs` - main render pipeline and render target ownership.
- `Engine/Renderer/RenderModules/` - individual rendering modules: G-buffer, deferred lighting, froxels, shadows, TAA, bloom, decals, SDFs, forward pass, editor outlines.
- `Engine/Entities/` - scene object types like `BasicEntity`, `Camera`, lights, decals, transformable base type.
- `Engine/Recources/` - asset, shader, settings, stats, materials, model wrappers. Keep the existing `Recources` spelling.
- `Engine/Content/Content.mgcb` - MonoGame content manifest.
- `Engine/Content/` - models, textures, shaders, fonts, video, Sponza assets, UI XML/CSS.
- `Engine/Editor/IEditorBridge.cs` - editor-facing bridge contract and snapshot structs.
- `Engine/Editor/EditorBridge.cs` - thread-safe operation queue and snapshot publisher between Anvil and the engine.
- `Editor/Anvil/Controls/MonoGameHost.cs` - embeds the MonoGame HWND in Avalonia and drives `RunOneFrame`.
- `Editor/Anvil/Views/MainWindow.axaml` - main editor UI layout/styles.
- `Editor/Anvil/ViewModels/MainWindowViewModel.cs` - editor state, commands, bridge attachment.
- `Editor/Anvil/Models/SceneObjectViewModel.cs` - inspector/hierarchy models; property setters enqueue engine mutations.
- `Editor/Anvil/Services/BridgeReconciler.cs` - reconciles engine snapshots into stable Avalonia view models.
- `Vista/UI/UIManager.cs` - loads XML/CSS, builds UI tree, updates layout, draws via SpriteBatch.
- `Vista/UI/UIElement.cs` - DOM-backed layout node and draw logic.

## Runtime Flow

Standalone engine:

1. `Engine/Program.cs` creates `Engine.Engine` and calls `Run()`.
2. `Engine.Engine` sets content root to `Content`, creates `EditorBridge`, `ScreenManager`, graphics, and BEPU physics.
3. `ScreenManager.Load()` loads `Globals.content`, `Shaders`, `ShaderManager`, `Assets`, renderer modules, scene/debug/gui/video content, and Vista UI.
4. `ScreenManager.Initialize()` initializes renderer, scene, GUI, editor logic, debug UI, and binds the bridge to scene/editor/assets.
5. Per frame: `ScreenManager.Update()` updates logic, shader hot reload in debug, editor logic, scene, renderer SDFs, debug screen, Vista UI, then drains bridge operations and publishes snapshots.
6. `ScreenManager.Draw()` draws intro video or the main renderer, legacy GUI, debug overlay, and Vista UI.

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

## Common Change Recipes

- Change startup/demo scene: edit `MainSceneLogic.SetUpEditorScene(...)`.
- Add an engine asset: update `Content.mgcb`, then add a load/register field in `Assets.cs`.
- Add an editor-backed scene property: extend snapshot/mutation structs in `IEditorBridge.cs`, populate it in `EditorBridge.BuildSnapshot()`, reconcile it in `BridgeReconciler`, and expose it through `SceneObjectViewModel`/XAML.
- Add an Anvil command: add command in `MainWindowViewModel`, bind it from `MainWindow.axaml`, and route engine work through `IEditorBridge`.
- Fix Anvil selection/focus problems: inspect `BridgeReconciler`, `SceneObjectViewModel.SuppressPush`, `MainWindowViewModel.ReconcilerActive`, and `EditorBridge.PublishEveryNFrames`.
- Change embedded viewport behavior: inspect `MonoGameHost.cs`, especially HWND reparenting, resize debounce, and `RunOneFrame()` loop.
- Change in-engine editor gizmos/selection: inspect `EditorLogic.cs`, `EditorRender`, and ID/outline render modules.
- Change Vista overlay UI: edit `Engine/Content/UI/debug.xml`, `Engine/Content/UI/debug.css`, and `ScreenManager.UpdateVistaUI(...)`.

## Project-Specific Rules

- Keep scene mutations on the engine/game thread. From Avalonia, enqueue through `IEditorBridge`; do not mutate engine lists directly.
- Preserve existing naming and spelling, including `Engine.Recources`.
- MonoGame content paths are extensionless and relative to the `Content` root.
- Anvil sets the current directory to the Engine assembly directory before booting MonoGame so content paths resolve correctly.
- Coordinate comments indicate Z is up; gravity is set to negative Z.
- Many shader parameters are cached statically in `Shaders.cs`; ensure `Globals.content` is set before shader static access.
- Render targets are manually owned and disposed. When resolution changes, follow `Renderer.UpdateResolution()` / `SetUpRenderTargets(...)` patterns.
- Existing code style is older C# in `Engine` and `Vista` with nullable mostly disabled; `Anvil` uses nullable + MVVM source generators.

## Testing Status

No test project is present. For code changes, use targeted builds/runs:

```powershell
dotnet build Engine.sln /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
dotnet run --project Engine\Engine.csproj
dotnet run --project Editor\Anvil\Anvil.csproj
```
## In the end of the session
Write what was changed, added or/and removed in the `CHANGELOG.md`.