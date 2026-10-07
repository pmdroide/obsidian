# CLAUDE.md

## Project Snapshot

Obsidian is a C#/.NET 10 game engine/editor workspace. The runtime is a MonoGame WindowsDX engine, the editor is an Avalonia desktop app named Anvil, and `Vista` is a custom XML/CSS UI layer rendered inside MonoGame.

Solution projects:

- `Engine/Engine.csproj` - MonoGame WindowsDX executable and core engine.
- `Editor/Anvil/Anvil.csproj` - Avalonia editor shell that embeds the engine viewport.
- `Vista/Vista.csproj` - lightweight XML/CSS UI renderer built on AngleSharp + MonoGame.
- `ContentPipeline/ContentPipeline.csproj` - MonoGame pipeline extension (`SkinnedModelProcessor`), net8.0 because mgcb runs on .NET 8. `Engine.csproj` builds it before the content build; `Content.mgcb` loads it via `/reference`.

## Quick Commands

Use PowerShell from the repo root unless noted.

```powershell
dotnet restore
dotnet build Engine.slnx /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
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
- `Engine/Logic/GameFlow.cs` - script API for the scene list: queued `LoadScene(index/name)`, `Quit`, `EscapeQuits`, `PlayStopped`.
- `Engine/Logic/GameInput.cs` - script input merging keyboard (native + Anvil-forwarded), mouse and gamepads; menu navigation with key repeat.
- `Engine/Logic/GameUI.cs` - Vista layers opened by scripts (menus/HUDs), scaled from a 1080p canvas, hot-reloaded from source Content.
- `Engine/Recources/SceneList.cs` - build order of scenes (`Content/System/SceneList.json`); index 0 boots the standalone game.
- `Engine/Renderer/Renderer.cs` - main render pipeline and render target ownership.
- `Engine/Renderer/RenderModules/` - individual rendering modules: G-buffer, deferred lighting, froxels, shadows, TAA, bloom, decals, SDFs, forward pass, editor outlines.
- `Engine/Entities/` - scene object types like `BasicEntity`, `Camera`, lights, decals, transformable base type. `SpotLight` derives from `PointLight` and lives in the scene's `PointLights` list; every point light shader multiplies by a spot cone (point lights pass outer cosine -2).
- `Engine/Components/` - gameobject components (`GameComponent`, `ComponentRegistry`, Material, Physics, Audio, Animator). See "Adding a Gameobject Component".
- `Engine/Animation/` - skeletal animation: `SkinningData` (skeleton + clips from `Model.Tag`), `AnimationPlayer` (sampling, retargeting by bone name), `SkinnedMeshInstance` (CPU skinning into per-entity vertex buffers that `MeshMaterialLibrary` draws via `TransformMatrix.Skin`).
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
- `Vista/UI/UIElement.cs` - DOM-backed layout node and draw logic (absolute layout, gradients, borders, text, transitions, hit testing).
- `Docs/markdown/Scenes_and_Game_Flow.md`, `Docs/markdown/VistaUI_Architecture.md` - scene list/GameFlow/GameInput/GameUI and the supported Vista CSS.
- `Docs/markdown/Skeletal_Animation.md` - Animator component, the AnimationTest sample scene, and how skinned models are built and drawn.
- `Engine/Physics/ScenePhysics.cs` + `PhysicsContact.cs` - BEPU bodies per gameobject, raycasts, and collision/trigger Enter/Stay/Exit events dispatched to Script Behaviours (`OnCollisionEnter`, `OnTriggerEnter`, ...). `Engine/Components/InteractableComponent.cs` + `ScriptBehaviour.FindInteractable/Interact/CameraRay` - raycast interaction. Sample: `Content/Scenes/CollisionTest.obsc` (first-person player `Content/Scripts/CollisionTestPlayerScript.cs` + station scripts); see `Docs/markdown/Collisions_and_Interaction.md`.
- `Engine/Steam/SteamP2PSession.cs` - Steam lobby + `SteamNetworkingMessages` peer-to-peer session for scripts (`SteamService.Current` is the engine's Steam session). Sample: `Content/Scenes/MultiplayerTest.obsc` + `Content/Scripts/MultiplayerTestScript.cs`; see `Docs/markdown/Steam_Multiplayer.md`.

## Runtime Flow

Standalone engine:

1. `Engine/Program.cs` creates `Engine.Engine` and calls `Run()`.
2. `Engine.Engine` sets content root to `Content`, creates `EditorBridge`, `ScreenManager`, graphics, and BEPU physics.
3. `ScreenManager.Load()` loads `Globals.content`, `Shaders`, `ShaderManager`, `Assets`, renderer modules, scene/debug/gui/video content, and Vista UI.
4. `ScreenManager.Initialize()` initializes renderer, scene, GUI, editor logic, debug UI, and binds the bridge to scene/editor/assets.
5. When the intro video ends, `MainSceneLogic.StartFirstScene()` loads scene list entry 0 (`Content/System/SceneList.json`, the MainMenu sample) and starts Play. Anvil skips this.
6. Per frame: `ScreenManager.Update()` updates logic, shader hot reload in debug, editor logic, scene (queued `GameFlow` scene loads apply first, then `Input`/`GameInput`, then scripts), renderer SDFs, debug screen, `GameUI` layers, Vista debug UI, then drains bridge operations and publishes snapshots.
7. `ScreenManager.Draw()` draws intro video or the main renderer, `GameUI` layers, legacy GUI, debug overlay, and Vista UI.

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

- Change the startup scene or build order: Anvil > Game Settings > Scenes, or `Engine/Content/System/SceneList.json` (index 0 boots the standalone game). Scripts switch scenes with `GameFlow.LoadScene(...)`; in Anvil, Stop returns to the edited scene.
- Add a game menu or HUD: a Vista document in `Engine/Content/UI/<Name>.xml` + `.css`, opened with `GameUI.Open("UI/<Name>")` from a script behaviour (reference: `Engine/Content/Scripts/MainMenuScript.cs`). The document is parsed as HTML (always close `div`s); layout is absolute.
- Add an engine asset: update `Content.mgcb`, then add a load/register field in `Assets.cs`.
- Add an animated (skinned) model: `Content.mgcb` entry with `/processor:SkinnedModelProcessor` (Mixamo: `RotationX=90`, `Scale=0.01`, `GenerateTangentFrames=True`; copy the `GameObjects/Player` entries), a `ModelDefinition` field loaded with `Assets.LoadSkinnedModel`, then an Animator component (Source = content path of another FBX to share its clips). Sample: `Content/Scenes/AnimationTest.obsc`. After changing the processor, delete the model's `.xnb`/`.mgcontent` so mgcb rebuilds it.
- Give a script Inspector settings: public fields or `[SerializeField]` private fields (`bool`/`int`/`float`/`double`/`string`/enum/`Vector2`/`Vector3`/`Color`) on the `ScriptBehaviour`, optionally `[Range]`, `[Tooltip]`, `[HideInInspector]`, `[FormerlySerializedAs]`. Values live per attachment in `ScriptBehaviourComponent.Fields` and are applied before `Start` (`Engine/Scripting/ScriptFields.cs`). Use `[NonSerialized]` for public runtime state.
- Spawn gameobjects at runtime from a script: `Spawn("Capsule", position)` / `Destroy(entity)` on `ScriptBehaviour`. Spawned objects are flagged `BasicEntity.IsRuntimeSpawned`, never saved, and removed when the script stops. Built-in model keys are the public `ModelDefinition` fields on `Assets` (`Assets.FindModel`).
- Add a gameobject component (Inspector > Add Component): see "Adding a Gameobject Component" below.
- React to contacts or let the player use objects: override `OnCollisionEnter/Stay/Exit`, `OnTriggerEnter/Stay/Exit` or `OnInteract` on a `ScriptBehaviour`; triggers are Physics > Is Trigger (Static triggers are convex hulls), usable objects get an Interactable component plus a non-trigger Physics component. Reference: the CollisionTest sample scripts.
- Add a gameobject role: append a value to `Engine/Entities/GameObjectRole.cs` (saved by name, so never rename one) and handle it where it matters; `ScenePhysics`/`WaterVolume` handle `Water`.
- Change water waves: the swell lives in both `Engine/Content/Shaders/Forward/Water.fx` (`Swell`) and `Engine/Physics/WaterWaves.cs` (buoyancy); keep them identical.
- Add an editor-backed scene property: extend snapshot/mutation structs in `IEditorBridge.cs`, populate it in `EditorBridge.BuildSnapshot()`, reconcile it in `BridgeReconciler`, and expose it through `SceneObjectViewModel`/XAML.
- Add an Anvil command: add command in `MainWindowViewModel`, bind it from `MainWindow.axaml`, and route engine work through `IEditorBridge`.
- Fix Anvil selection/focus problems: inspect `BridgeReconciler`, `SceneObjectViewModel.SuppressPush`, `MainWindowViewModel.ReconcilerActive`, and `EditorBridge.PublishEveryNFrames`.
- Change embedded viewport behavior: inspect `MonoGameHost.cs`, especially HWND reparenting, resize debounce, and `RunOneFrame()` loop.
- Change in-engine editor gizmos/selection: inspect `EditorLogic.cs`, `EditorRender`, and ID/outline render modules.
- Change Vista overlay UI: edit `Engine/Content/UI/debug.xml`, `Engine/Content/UI/debug.css`, and `ScreenManager.UpdateVistaUI(...)`.

## Adding a Gameobject Component

Components live on `BasicEntity.Components` (at most one per type by default; Script Behaviour allows multiple), show in Anvil's Inspector component list and **Add Component** menu, and save inside the scene's `Components` records. Each attachment has an `InstanceId` persisted in its record; cloning assigns fresh IDs. The bridge and reconciler use these IDs to edit/remove individual attachments. The bridge, snapshots, cloning and scene format are generic, so a new component needs only these steps:

The scene's main camera also holds components (`Camera.Components`, bridge id `EditorBridge.MainCameraId` = -1), but only types allowed by `Camera.SupportsComponent` (Script Behaviour). Their hooks get a `null` owner; `ScriptBehaviourComponent.HostCamera` points at the camera.

1. **Engine type** - add `Engine/Components/<Name>Component.cs`:
   ```csharp
   public sealed class FooComponent : GameComponent
   {
       public const string TypeId = "foo";        // saved in scene files: never rename
       public float Speed { get; set; } = 1f;     // public get/set = persisted + sent to the editor
       [JsonIgnore] public bool IsRunning { get; private set; } // runtime-only state
       public override void OnStart(BasicEntity owner) { }                 // Play pressed
       public override void OnUpdate(BasicEntity owner, GameTime time) { } // each Play frame
       public override void OnChanged(BasicEntity owner) { }  // after an inspector edit
       public override void OnRemoved(BasicEntity owner) { }  // after removal (default: OnStop)
       public override void OnStop() { }                      // Play stopped / disabled
   }
   ```
   Hooks run on the game thread. Respect `Enabled` (the inspector's Enabled checkbox). Persisted properties must round-trip through `System.Text.Json`; add `[JsonConverter(typeof(JsonStringEnumConverter))]` to enums.
2. **Register it** in the static constructor of `Engine/Components/ComponentRegistry.cs`: `Register<FooComponent>(FooComponent.TypeId, "Foo");`. Registration order is the Add Component menu order. Pass `allowMultiple: true` for repeatable components. Pass a factory (`owner => ...`) when defaults depend on the gameobject; Material uses `MaterialComponent.FromOwner`. Do not add per-component special cases to `EditorBridge`.
3. **Editor view model** - add `Editor/Anvil/Models/FooComponentViewModel.cs` deriving `ComponentViewModel` (copy `PhysicsComponentViewModel`):
   - one `[ObservableProperty]` per field, and `partial void OnSpeedChanged(double v) => Push(c => ((FooComponent)c).Speed = (float)v);`. `Push` routes through the bridge to this view model's own gameobject and does nothing during reconciliation.
   - override `Apply(GameComponent component, bool freezeFields)`: call `base.Apply`, return if `freezeFields`, then copy each field from the engine component.
   - ignore transient values from controls (for example a ComboBox `SelectedIndex` of `-1`).
4. **Register the editor** in `ComponentEditorRegistry` (`Editor/Anvil/Models/ComponentViewModel.cs`): `Register(FooComponent.TypeId, owner => new FooComponentViewModel(owner));`. Components without an editor are not offered in Add Component.
5. **Inspector template** - add `<DataTemplate DataType="m:FooComponentViewModel" x:DataType="m:FooComponentViewModel">` to the Components `ItemsControl.DataTemplates` in `Editor/Anvil/Views/MainWindow.axaml`. Include the `Enabled` checkbox and a `Remove Component` button bound to `RemoveCommand`. Bind only to the template's own view model, never to `SelectedObject...`. A `Flyout` must use `Opening="OwnedFlyout_Opening"` so it stays attached to its component.
6. **Engine use** - read the first with `entity.GetComponent<FooComponent>()`, or all attachments with `entity.GetComponents<FooComponent>()`. Add or remove from code with `entity.AddComponent(...)` / `entity.RemoveComponent(...)`, not `Components.Add`, so hooks run and registry multiplicity is enforced.
7. **Checks** - extend `Tests/Components/Program.cs` (add via bridge, inspector edit reaches the engine, clone independence, save/load), then run `dotnet run --project Tests\Components\Components.csproj`.

Physics is the reference for a component that another system reads: `ScenePhysics` rebuilds BEPU bodies each update from `BasicEntity.PhysicsType` / `Mass`, which come from `PhysicsComponent`. Older scenes with an entity-level `Physics` record are converted to the component on load.

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

`Tests/Components` is a framework-free integration check app (not in `Engine.slnx`) covering components, the editor bridge/reconciler, environment, Steam and input. Add `--graphics` for GPU material checks. Otherwise use targeted builds/runs:

```powershell
dotnet build Engine.slnx /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
dotnet run --project Tests\Components\Components.csproj
dotnet run --project Engine\Engine.csproj
dotnet run --project Editor\Anvil\Anvil.csproj
```

# Blender

Blender is available through the Blender MCP server.

When performing Blender work:

1. Inspect the current scene before making changes.
2. Use Blender MCP instead of generating scripts for me to manually run.
3. Do not delete or modify unrelated objects.
4. Make changes incrementally.
5. Inspect the result after major changes.
6. Use viewport screenshots when useful.
7. Preserve existing object names unless renaming is necessary.
8. Save the Blender file only when explicitly requested.

# Game Assets

Assets are intended for Unity.

For game-ready meshes:

- Keep topology reasonably efficient.
- Apply transforms before export.
- Check normals.
- Check for non-manifold geometry.
- UV unwrap meshes when appropriate.
- Use sensible real-world scale.
- Set sensible object origins.
- Avoid unnecessary modifiers before export.

## In the end of the session
Write what was changed, added or/and removed in the `CHANGELOG.md`.
Also sign who did it in the changelog, for example: "- Claude", "- Codex" at the end of the text.
