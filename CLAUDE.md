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
- `Engine/Logic/ScreenManager.cs` - central coordinator for load/init/update/draw across renderer, scene logic, GUI, editor logic, debug screen (stats), intro video, and script UI (`GameUI`).
- `Engine/Logic/MainSceneLogic.cs` - owns the live scene lists: entities, decals, lights, debug entities, camera, environment sample, and editor-side add/delete helpers.
- `Engine/Logic/EditorLogic.cs` - in-engine editor mode, selection, gizmos, delete/copy behavior.
- `Engine/Logic/GameFlow.cs` - script API for the scene list: queued `LoadScene(index/name)` (one frame), `LoadSceneAsync` (background load + loading screen, returns a `SceneLoadOperation`), `Quit`, `EscapeQuits`, `PlayStopped`.
- `Engine/Logic/SceneLoadOperation.cs` + `LoadingScreen.cs` - scene loads: worker-thread `SceneSerialization.ReadFile`, `SceneBuilder` steps within `GameFlow.LoadBudgetMs` per frame, swap (`MainSceneLogic.ActivateLoadedScene`), 3 finishing frames; real `Progress`, `AllowSceneActivation`, `Cancel`, timings (`LongestFrameMs`). The loading screen is an engine overlay layer (`GameUI.OpenOverlay`, `Content/UI/LoadingScreen.xml`). Sample: `Content/Scenes/SceneLoadingTest.obsc` <-> `SceneLoadingArena.obsc` (`Content/Scripts/SceneLoadingTestScript.cs`); see `Docs/markdown/Scenes_and_Game_Flow.md`.
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
- `Engine/Physics/RagdollRig.cs` + `Ragdoll.cs` + `Engine/Components/RagdollComponent.cs` - ragdolls for skinned gameobjects: `RagdollRig` splits a model into parts (convex hulls of each main bone's vertices, joint limits from bone names), `Ragdoll` owns the BEPU bodies/joints (kinematic while animated, dynamic when limp; `ScenePhysics.CreateRagdoll` drives it around each step), the component handles Play, impacts and the skin. Bodies of one gameobject share a collision group (`PhysicsSystem.SetCollisionGroup`). Sample: the ragdoll row of `Content/Scenes/AnimationTest.obsc` + `Content/Scripts/RagdollTestScript.cs`; see `Docs/markdown/Ragdoll_Physics.md`.
- `Engine/Physics/Cloth.cs` + `ClothCollider.cs` + `ClothMeshInstance.cs` + `Engine/Components/ClothComponent.cs` - cloth: a mesh's welded vertices simulated as XPBD particles (10 substeps, stretch/bend constraints, air drag + scene wind, `ClothPinMode` pins following the gameobject) that collide one-way with the BEPU colliders `ScenePhysics.CollectClothColliders` gathers (hulls/boxes as planes, static meshes as two-sided triangles, triggers skipped). `ClothMeshInstance` writes the particles into per-gameobject vertex buffers drawn via `TransformMatrix.Cloth` (like `Skin`). Mesh: `Assets.Cloth` (`Content/GameObjects/Cloth/cloth.obj`, double-sided 24x24 plane in X/Z). Sample: the Curtain/Drape Sheet station of `Content/Scenes/CollisionTest.obsc` + `Content/Scripts/ClothReporterScript.cs`; see `Docs/markdown/Cloth.md`.
- `Engine/AI/NavGrid.cs` + `Navigation.cs` + `Engine/Components/NpcComponent.cs` - NPCs: `NavGrid` is a 0.25 m height field cast down through the **static** colliders only (`ScenePhysics.RaycastStatic`; dynamic bodies are see-through), eroded by agent radius + half a cell (walls, ledges, holes), with step/slope limits, regions and A* + string pulling (`FindPath`, `TryRandomPoint`, `TryFleePoint`). `Navigation.For` caches one grid per physics scene and agent size, rebuilding (at most every 30 frames) when a static moves; `Navigation.Invalidate()` forces it. `NpcComponent` (type id `npc`) = idle (`NpcIdleBehaviour` Stand/Wander/Patrol by gameobject names) + sight (range, FOV around +Y, hearing range, line-of-sight rays every 0.1 s) + reaction (`NpcReaction` Chase+attack impulse/Follow/Flee/Watch) + search after losing the target; moves a Dynamic body by velocity (or the gameobject directly without one). Script hooks `OnTargetSpotted/OnTargetLost/OnNpcStateChanged/OnNpcAttack`, API `MoveTo/Investigate/Alert/Calm/CanSee`, `ScriptBehaviour.FindPath`. Sample: `Content/Scenes/NpcTest.obsc` (`NpcTestPlayerScript`, `NpcReporterScript`, `UI/NpcTest`); see `Docs/markdown/NPC_Navigation.md`.
- `Engine/Components/LodComponent.cs` - level of detail (type id `lod`): `Levels` (model key + camera distance) and `CullDistance`. `BasicEntity.RegisterInLibrary` registers the model as level 0 and each LOD model as level 1..n (`MeshLibrary.LodLevels`); `Renderer.Draw` calls `MeshMaterialLibrary.SelectLevelsOfDetail` before the shadow maps, which sets `TransformMatrix.LodLevel` (-1 = culled) and `Draw` skips other levels. Visual only (physics/SDF/bounds use the own model); works in edit mode and Play. Sample: `Content/Scenes/LodTest.obsc`; see `Docs/markdown/Level_of_Detail.md`.
- `Engine/Components/FoliageWindComponent.cs` + `Engine/Renderer/Helper/FoliageWind.cs` + `Content/Shaders/Common/FoliageWind.fx` - trees swaying in the scene wind (type id `foliage-wind`, Edit and Play, held while paused): `MainSceneLogic.Update` calls `FoliageWindComponent.UpdateAll` (wind clock, strength = `WeatherProfile.Wind` m/s / 6 after Wind Scale/Min Wind, height from the model bounds) which puts a `FoliageWind` on `TransformMatrix.Wind` and flags `HasChanged` every frame (shadows redraw). The vertex shaders of the G-buffer, `ShadowMap.fx` and `IdRender.fx` include `FoliageWind.fx` (trunk bend ~height², branch sway by distance from the trunk axis, leaf flutter only for `MaterialTypes.Foliage` = 8, fading to the card's stem at texture v = 1, per-card phase from `floor(u)`); `MeshMaterialLibrary.Draw` sets it per material (`SetMaterial`) and instance (`SetInstance`) through `IFoliageWindModule`/`Shaders.IdRenderEffectWind`. Foliage pixels get sunlight through their back (`FoliageTransmission` in `DeferredDirectionalLight.fx`); masked materials now cast cut-out sun shadows (`DrawLinearDepthAlpha`). Built-in model `Assets.Tree` (`Content/GameObjects/Tree/`, Bark + Leaves parts with `TreeBarkMaterial`/`TreeLeavesMaterial`, generated by `Source/generate_tree.py`); Anvil GameObject > Nature > Tree adds it with the component. Sample: `Content/Scenes/FoliageTest.obsc`; see `Docs/markdown/Foliage_Wind.md`.
- `Engine/Voxels/` - voxel world from `EnvironmentSettings.Voxels`/`Voxel*` (Anvil viewport toolbar > World > Voxel World): `VoxelWorld` (byte grid of `VoxelBlock`s in 32³ chunks, centred on X/Y = 0, floor at `VoxelBaseHeight`; `SetBlock`/`FillSphere`/DDA `Raycast`; bottom layer not editable), `VoxelGenerator` (Flat/Hills/Mountains/Islands + caves, trees, sand shores, seeded `VoxelNoise`), `VoxelMesher` (greedy meshing per surface, coarser collision mesh, clockwise-from-outside winding), `VoxelTextures` (painted pixel-art albedo + normal maps; `VoxelSurfaceStyle`: everything but the Lamp is Wet Surface), `VoxelSystem` (`MainSceneLogic.Voxels`: synced in `UpdatePhysics` before the step; builds on a worker thread in Edit and synchronously in Play; registers chunk `ModelMeshPart`s in the `MeshMaterialLibrary` with ID 0 and adds one BEPU static mesh per chunk; remeshes edited chunks; `Reset` on scene swap; regenerates an edited world on Stop; draws the sea as a unit quad through the water pass). Scripts: `ScriptBehaviour.Voxels`. Sample: `Content/Scenes/VoxelTest.obsc` + `Content/Scripts/VoxelTestScript.cs` + `UI/VoxelTest`; see `Docs/markdown/Voxel_World.md`.
- `Engine/Particles/` + `Engine/Components/ParticleSystemComponent.cs` + `Engine/Renderer/RenderModules/ParticleRenderModule.cs` + `Content/Shaders/Forward/Particles.fx` - Unity-style particle systems (type id `particles`): module settings in `ParticleModules` (Main, Emission/Bursts, Shape, Velocity/Limit/Force, External Forces = scene wind, Noise, Color/Size/Rotation over Lifetime, Collision Plane/World, Texture Sheet, Sub Emitters by gameobject name, Renderer), CPU `ParticleSimulation` (GPU-free), procedural `ParticleTextures`. Play On Awake systems also preview in Edit (`MainSceneLogic.Update` -> `UpdatePreviews`); in Play the marker mesh is hidden via `TransformMatrix.Hidden`. Drawn as instanced quads after water, before weather, occluded/softened by the linear G-buffer depth. Script API `Play/Stop/Pause/Restart/Emit/EmitAt`. Sample: `Content/Scenes/ParticleTest.obsc` + `Content/Scripts/ParticleTestScript.cs` + `UI/ParticleTest`; see `Docs/markdown/Particle_Systems.md`. The **Volumetric** module draws a system as a raymarched volume (`Engine/Particles/ParticleVolume.cs`, `Volume` technique in `Particles.fx`): Source **Particles** splats each particle into a density grid, Source **Voxels** is a Counter-Strike 2 smoke that floods a voxel grid from the emitter (Volume m³, never through Static colliders, sun rays per voxel), holds for Duration and fades. `CarveTunnel/CarveHole` (bullets, HE) and `Opacity`/static `VolumeOpacity` (NPC sight uses it). Sample: `Content/Scenes/SmokeTest.obsc` + `Content/Scripts/SmokeTestScript.cs` + `UI/SmokeTest`. The **Lights** module (`ParticleLightsModule`, `Engine/Particles/ParticleLights.cs`) makes a system light the scene: one flickering System light at the particles' glowing centre (fades with them; optional shadows) or Per Particle lights on the brightest carriers. They are engine-owned `PointLight`s, never in `Scene.PointLights`: `Renderer.Draw` appends them (`ParticleSystemComponent.CollectLights`) to a per-frame copy of the scene's point lights for every lighting pass, while editor picking/icons use the scene list. Lit particles take the 4 strongest point lights. Built-in **FlameSheet** texture: a 4 x 4 flipbook of a flame tongue coloured by temperature (the ParticleTest campfire). **FireballSheet**: a 4 x 4 flipbook of an explosion billow cooling from white-hot to soot, drawn with Alpha blend + Brightness so the soot occludes (the ParticleTest explosion).
- `Engine/Physics/ScenePhysics.cs` + `PhysicsContact.cs` - BEPU bodies per gameobject, raycasts, and collision/trigger Enter/Stay/Exit events dispatched to Script Behaviours (`OnCollisionEnter`, `OnTriggerEnter`, ...). `Engine/Components/InteractableComponent.cs` + `ScriptBehaviour.FindInteractable/Interact/CameraRay` - raycast interaction. Sample: `Content/Scenes/CollisionTest.obsc` (first-person player `Content/Scripts/CollisionTestPlayerScript.cs` + station scripts); see `Docs/markdown/Collisions_and_Interaction.md`.
- Volumetric fog / god rays (`Content/Shaders/Deferred/Froxel.fx`, `FroxelRenderModule`, composed in `DeferredCompose.fx`): saved per scene in `EnvironmentSettings.Fog*` + `MaxAutoExposure` (Anvil Inspector > Environment > Volumetric Fog), copied into `GameSettings.g_Froxel*` each frame by `Renderer.ApplySceneFog` (Post Processing keeps only Enable/History). Density ramps from `FogNearDensity` at `FogStartDistance` (30 m) to full at `FogFullDistance`; sky pixels keep fog up to `FogSkyShaftDistance` whole. `Engine/Components/FogVolumeComponent.cs` (type id `fog-volume`) adds density inside the gameobject's box without the ramp (dusty rooms; mesh hidden, max 8). Shafts only exist inside the sun's fixed shadow box. Sample: the hall and the attic of `Content/Scenes/GodRayTest.obsc`; see `Docs/markdown/Scene_Lighting_and_Fog.md`.
- `Engine/Logic/Weather.cs` + `Engine/Renderer/RenderModules/WeatherRenderModule.cs` + `Content/Shaders/Forward/Weather.fx` - scene weather (rain, sandstorm, snow) from `EnvironmentSettings.Weather`/`WeatherIntensity`/`WeatherHaze`/`WindSpeed`/`WindDirection`: GPU particles in a camera-wrapped box plus a depth haze, drawn after water. Per-type look is `WeatherProfile`. Scripts use `ScriptBehaviour.SceneEnvironment`; PlayMode restores the environment on Stop. Sample: `Content/Scenes/WeatherTest.obsc` + `Content/Scripts/WeatherTestScript.cs`; see `Docs/markdown/Weather.md`.
- Wet Surface material type (`MaterialEffect.MaterialTypes.WetSurface` = 7, Inspector > Material > Shader > WetSurface): soaked albedo/roughness, world-space puddles that fill texture cavities (Displacement map = height, no parallax) and raindrop ripples, all in `ApplyWetSurface` in `Content/Shaders/GBufferSetup/Gbuffer.fx`. Rain drives it through `SurfaceWetness` (`Engine/Logic/Weather.cs`, eased by the renderer; `WeatherProfile.SurfaceWetness`/`PuddleRipples`). Wet pixels set albedo alpha so `DeferredEnvironmentMap.fx` uses a view-dependent fresnel for them. Materials also have UV `TilingU/V`. CC0 ground textures: `Content/GameObjects/Textures/Ground/`. Sample: the Ground/Street/Plaza of `Content/Scenes/WeatherTest.obsc`; see `Docs/markdown/Material_Component.md`.
- Water (`GameObjectRole.Water` + Water material): `Engine/Physics/WaterVolume.cs` (footprint + wave surface), `Buoyancy.cs`, and water contacts in `ScenePhysics.UpdateWaterContacts` (every Dynamic body whose collider bottom crosses the surface, 5 cm exit margin) -> `WaterContact`, events `ScenePhysics.WaterEntered/WaterExited`, script hooks `OnWaterEnter/OnWaterExit`, `IsInWater`, `RaycastWater`. `Engine/Particles/WaterSplashes.cs` (`MainSceneLogic.WaterSplashes`): entries >= 1.2 m/s throw a crown/jet/spray from a pool of 12 engine-owned particle systems (no gameobject; drawn via `ParticleRenderModule.Draw(..., extraSystems)`, droplets die on a per-splash plane at the water), and every entry/exit leaves a ring (max 16, 4 s) that `Water.fx` (`SplashRings`) draws with foam; rain adds raindrop rings (`RainRings`, from `SurfaceWetness.Ripples`). Script API `Splash(point, speed, radius)`, `WaterSplashes`. Sample: `Content/Scenes/WaterTest.obsc` (pool + wave tank in rain) + `Content/Scripts/WaterTestScript.cs` + `UI/WaterTest`; see `Docs/markdown/Water.md`.
- `Engine/Logic/PersistentGameObjects.cs` - persistent gameobjects (`BasicEntity.Persistent`, saved / Inspector > Persistent, or `ScriptBehaviour.DontDestroyOnLoad()`): on `GameFlow.LoadScene` during Play they move into the new scene still running (scripts, bodies, HUD layers they opened); the new scene's own copy is left out; Anvil's Stop returns them to the edited scene. Carried objects get `ScriptBehaviour.OnSceneLoaded()`. `GameFlow.LoadScene(..., carryPersistent: false)` leaves them behind. Sample: `Content/Scenes/PersistenceTest.obsc` <-> `PersistenceTest2.obsc` (`Content/Scripts/PersistentPlayerScript.cs`, `ScenePortalScript.cs`, `CompanionOrbScript.cs`); see `Docs/markdown/Persistent_GameObjects.md`.
- `Engine/Logic/SaveData.cs` - save files for scripts (save games, settings, high scores): `Save<T>/SaveAsync<T>/Load<T>/TryLoad<T>` (JSON: public fields + properties, enums by name, XNA vectors/colours as arrays), `SaveText/LoadText`, `SaveBytes/LoadBytes`, `Exists/Delete/List/GetLastWriteTime/Flush`. Files live in `%LOCALAPPDATA%/<Game Settings window name>/Saves` (`SaveData.Folder` overrides), shared by Anvil Play and builds; a name without an extension gets `.json`, names can't leave the folder. Writes are atomic (`.tmp` then replace, previous kept as `.bak`, used when the file is damaged) and ordered per file; the engine flushes async writes on exit. See `Docs/markdown/Save_Data.md`.
- `GameFlow.Paused` - freezes Play: `PlayModeController.UpdateScripts` runs only script behaviours whose `ScriptBehaviour.UpdateWhilePaused` is true, `MainSceneLogic.UpdatePhysics` doesn't step, weather particles stop; scene loads and Stop unpause. `Content/Scripts/PauseMenuScript.cs` (+ `Content/UI/PauseMenu.xml`/`.css`) is the pause menu on the PersistenceTest Player; menu settings rows are shared with the main menu in `Content/Scripts/MenuSettings.cs`.
- `Engine/Steam/SteamP2PSession.cs` - Steam lobby + `SteamNetworkingMessages` peer-to-peer session for scripts (`SteamService.Current` is the engine's Steam session). Sample: `Content/Scenes/MultiplayerTest.obsc` + `Content/Scripts/MultiplayerTestScript.cs`; see `Docs/markdown/Steam_Multiplayer.md`.

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

## Common Change Recipes

- Start over with an empty project: Anvil > Edit > **Project Settings...** > Delete Project (red, asks for the project name, Edit mode only). `Engine/Recources/ProjectReset.cs` removes everything outside `ProjectReset.KeptPaths` (shaders, fonts, `System`, scripts, built-in models) with its Content.mgcb entries and built copies, writes default System settings and makes `Scenes/Main.obsc` the only scene; `IEditorBridge.EnqueueResetProject` swaps in the empty scene and unregisters imported models first, and Anvil sends the source files to the Recycle Bin. Content the engine loads at boot must stay in `KeptPaths`. `ProjectReset.Plan` lists what would go without deleting.
- Build the standalone game: Anvil > File > **Build...** (Ctrl+Shift+B, asks for the folder) or **Build and Run** (Ctrl+B, last folder, `Builds/<window name>` at first; `Builds/` is git-ignored). `Editor/Anvil/Services/GameBuilder.cs` runs a self-contained win-x64 Release `dotnet publish` of `Engine/Engine.csproj` and renames `Engine.exe` after Game Settings > window name; the folder is remembered in `Content/System/BuildSettings.json`. Builds ship saved files only, and loose runtime files need `CopyToPublishDirectory` in `Engine.csproj`.
- Load a scene behind a loading screen (or in the background): `GameFlow.LoadSceneAsync("Level2")` from a script; `loadingScreen: false` keeps the running scene visible (show `load.Progress` yourself); `AllowSceneActivation = false` holds the built scene at 90% ("press any key"). Restyle the screen in `Content/UI/LoadingScreen.xml`/`.css` (keep the ids `LoadingScreen.cs` fills) or point `GameFlow.LoadingScreenDocument` at your own. Tune the per-frame build time with `GameFlow.LoadBudgetMs`.
- Change the startup scene or build order: Anvil > Game Settings > Scenes, or `Engine/Content/System/SceneList.json` (index 0 boots the standalone game). Scripts switch scenes with `GameFlow.LoadScene(...)`; in Anvil, Stop returns to the edited scene.
- Add a game menu or HUD: a Vista document in `Engine/Content/UI/<Name>.xml` + `.css`, opened with `GameUI.Open("UI/<Name>")` from a script behaviour (reference: `Engine/Content/Scripts/MainMenuScript.cs`). The document is parsed as HTML (always close `div`s); layout is absolute.
- Add an engine asset: update `Content.mgcb`, then add a load/register field in `Assets.cs`.
- Add an animated (skinned) model: `Content.mgcb` entry with `/processor:SkinnedModelProcessor` (Mixamo: `RotationX=90`, `Scale=0.01`, `GenerateTangentFrames=True`; copy the `GameObjects/Player` entries), a `ModelDefinition` field loaded with `Assets.LoadSkinnedModel`, then an Animator component (Source = content path of another FBX to share its clips). Sample: `Content/Scenes/AnimationTest.obsc`. After changing the processor, delete the model's `.xnb`/`.mgcontent` so mgcb rebuilds it.
- Make a skinned gameobject fall like a ragdoll: add a **Ragdoll** component (works with or without an Animator; the ground needs a Physics component). Go limp with Active on Start, an Impact speed, or `GetComponent<RagdollComponent>().Activate()` from a script; `Deactivate()` stands it back up. Tune joint limits in `RagdollJointLimits.For` and bone-name matching in `RagdollRig.JointFor`.
- Add cloth (curtain, banner, sheet): a gameobject with the **Cloth** model (Scale X = width, Z = height; rotate -90° about X to lay it flat) + a **Cloth** component (Pin, Mass, Stiffness, Bend, Wind, Collisions). It collides with Physics colliders but never pushes them. Tune the solver in `Engine/Physics/Cloth.cs` (`Compliance`, `Substeps`, `ApplyAir`).
- Add an NPC: a Dynamic capsule (Freeze Rotation, +Y = forward) + an **NPC** component (Behaviour, Patrol Points by name, Target `Player`, Sight Range/Field of View/Hearing, On Sight reaction). The floor and walls need **Static** Physics: the navigation grid is built from them. React in its scripts with `OnTargetSpotted`/`OnNpcStateChanged`; drive it with `GetComponent<NpcComponent>().MoveTo/Investigate/Alert`. Tune the grid in `NavGridSettings`/`NavGrid` and the behaviour in `NpcComponent.Think`.
- Add a particle effect (fire, smoke, sparks): GameObject > Effects > **Particle System** (a marker sphere hidden in Play) or Add Component > **Particle System**; tune the modules in the Inspector (it previews while editing). Effects triggered by scripts: Play On Awake off, then `GetComponent<ParticleSystemComponent>().Play()/Restart()/EmitAt(...)`. Sub-emitter targets: Play On Awake off, Rate 0, World space. Add a built-in texture in `ParticleTextures.Pixel`/`ParticleTexture` (saved by name, never rename).
- Make fire light the scene (campfire, torch, explosion flash, glowing sparks): Particle System > **Lights** > Enabled. System mode (Intensity/Range on a Point Light's scale, Flicker 0.3-0.4, Offset, Alpha Affects Intensity so it fades with the fire) or Per Particle (Ratio, Max Lights). Realistic flames: Flame Sheet texture + Texture Sheet Animation 4 x 4, Additive, few large particles with an upward Force, a Glow core at the base (copy the ParticleTest Campfire + Campfire Core). Tune the light in `ParticleLights.UpdateSystem`/`Flicker` and the flame look in `ParticleTextures.Flame`/`FireRamp`.
- Make thick smoke / a CS2 smoke grenade: Particle System > **Volumetric** > Enabled. Source **Voxels** (Main: Looping off, Duration = how long it holds; Volume, Voxel Size, Expand/Fade Time; the walls it must stay out of need Static Physics) or **Particles** (any effect as a volume). Bullets: `ParticleSystemComponent.CarveTunnels(from, to)`; blasts: `CarveHoles(point, radius)`. Tune the flood in `ParticleVolume.FillOrder`/`Pack` and the look in `VolumePS` (`Particles.fx`). Reference: `Content/Scripts/SmokeTestScript.cs`.
- Make a realistic explosion: copy the ParticleTest `Explosion ...` gameobjects. The fireball is **Fireball Sheet** + Texture Sheet Animation 4 x 4, **Alpha** blend, Distance sort, Brightness ~3, not Lit, with Color over Lifetime darkening to grey after ~30% (the soot frames) and a System light; around it a delayed Lit dark smoke column, a Lit ground dust skirt (Circle shape), stretched sparks, and Glow chunks with a smoke **Trail** sub-emitter. Many Additive smoke-sheet particles only blow out into a flat orange blob. Tune the billows/cooling in `ParticleTextures.Fireball`.
- Draw simpler models far away: Inspector > Add Component > **LOD**, one level per simpler model (model key + distance it starts at), optional Cull Distance. LOD models share the gameobject's material and transform, so model them at the same size/pivot/UVs. Tune the switch hysteresis in `LodComponent.Hysteresis`.
- Add trees that move in the wind: Anvil GameObject > Nature > **Tree** (the built-in Tree + a **Foliage Wind** component; never give it a Material component, which would flatten bark and leaves into one surface). The scene wind is Environment > Wind Speed/Direction; per tree: Trunk Bend, Branch Sway, Leaf Flutter, Wind Scale, Min Wind, Speed, Height. Own models: base at the origin, Z up, leaf parts on a **Foliage** material with a Mask, card stems at texture v = 1. Tune the motion in `ApplyFoliageWind` (`Content/Shaders/Common/FoliageWind.fx`; keep every wave frequency a multiple of 0.001 so the clock wraps at 1000 s), the response to wind in `FoliageWindComponent.Update`, the backlit glow in `FoliageTransmission`, and the tree mesh/textures in `Content/GameObjects/Tree/Source/generate_tree.py` (re-run it, outputs are already in Content.mgcb).
- Add a voxel world (block terrain): Anvil > World (viewport toolbar, beside Environment) > Voxel World (terrain, seed, size, block size, floor Z, trees, sea level, caves, collision); scripts dig/build through `Voxels.SetBlock`/`FillSphere`/`Raycast` (reference: `Engine/Content/Scripts/VoxelTestScript.cs`). Add a block: append to `VoxelBlock` (saved as a byte, never renumber) and `VoxelBlocks.Count`/`Surface`/`SwatchColor`/`Placeable`, plus a `VoxelSurface` with a pattern in `VoxelTextures.PaintPixel` and a `VoxelSurfaceStyle`. Tune terrain in `VoxelGenerator.Height`. Geometry that isn't a gameobject registers with `MeshMaterialLibrary.Register(material, part, transform, sphere)` / `Unregister` like `VoxelSystem` does.
- Delay, repeat, sequence or animate something from a script: `After(seconds, action)`, `Every(seconds, action, times)`, `StartTimer(seconds)` (cooldowns), `StartCoroutine(IEnumerator)` (yield `null`, seconds, `WaitForSeconds`/`WaitUntil`/`WaitWhile`/`WaitForFrames`, a `ScriptTimer` or another coroutine), tweens `Tween(from, to, seconds, apply, Ease)`/`MoveTo`/`MoveBy`/`ScaleTo`/`RotateTo` returning a `ScriptTimer` (`Loop(cycles, pingPong)`, `OnComplete`, `Pause/Cancel/Complete/Restart`), plus `Time`/`FrameCount`. All of them run after the script's `Update` in game time (held while paused; pass `realtime: true` to keep counting), and end with the script (`Engine/Scripting/ScriptBehaviour.Timing.cs`, `ScriptTimer.cs`, `ScriptCoroutine.cs`, `Ease.cs`). Static helpers: `ScriptMath` (`Damp`, `SmoothDamp`, `MoveTowards`, `Remap`, ...) and `ScriptRandom` (`Range`, `Chance`, `InsideUnitSphere`, `Pick`, `Seed`, ...) in `ScriptMath.cs`. Reference: `Content/Scripts/TimerExampleScript.cs` (**Timer Example**). See `Docs/markdown/Script_Behaviours.md`.
- Give a script Inspector settings: public fields or `[SerializeField]` private fields (`bool`/`int`/`float`/`double`/`string`/enum/`Vector2`/`Vector3`/`Color`, or `BasicEntity` for a gameobject reference saved by scene ID and resolved via `MainSceneLogic.FindGameObjectById`) on the `ScriptBehaviour`, optionally `[Range]`, `[Tooltip]`, `[HideInInspector]`, `[FormerlySerializedAs]`. Values live per attachment in `ScriptBehaviourComponent.Fields` and are applied before `Start` (`Engine/Scripting/ScriptFields.cs`). Use `[NonSerialized]` for public runtime state.
- Keep a gameobject (e.g. the player) across scene loads: tick Inspector > Persistent (saved) or call `DontDestroyOnLoad()` from its script (this Play session only); move it to the new scene's spawn in `OnSceneLoaded()`. Reference: `Engine/Content/Scripts/PersistentPlayerScript.cs`.
- Add a pause menu: attach the **Pause Menu** script to a persistent gameobject (the player); one instance then pauses every scene. A custom one sets `GameFlow.Paused` and overrides `UpdateWhilePaused => true` (reference: `Engine/Content/Scripts/PauseMenuScript.cs`).
- Change or add a volume setting: `Audio.MasterVolume`/`MusicVolume`/`EffectsVolume` or `Audio.SetVolume(AudioBus, v)` (`Engine/Recources/AudioManager.cs`; Music and Effects are FMOD channel groups under the master group). `PlayMusic` uses the Music bus, other sounds Effects unless a `bus` is passed; Audio component > Output picks one. The menus' rows come from `MenuSettings.Audio()`; see `Docs/markdown/Audio_Volume.md`.
- Save game progress or player settings: `SaveData.Save("slot1", state)` / `SaveData.Load<GameState>("slot1")` from a script (`using Engine.Logic`); use `SaveAsync` for autosaves. Don't save runtime objects (`BasicEntity`, components): save names/positions and look them up after loading. Tune JSON in `SaveData.CreateJsonOptions`.
- Spawn gameobjects at runtime from a script: `Spawn("Capsule", position)` / `Destroy(entity)` on `ScriptBehaviour`. Spawned objects are flagged `BasicEntity.IsRuntimeSpawned`, never saved, and removed when the script stops. Built-in model keys are the public `ModelDefinition` fields on `Assets` (`Assets.FindModel`).
- Add a gameobject component (Inspector > Add Component): see "Adding a Gameobject Component" below.
- React to contacts or let the player use objects: override `OnCollisionEnter/Stay/Exit`, `OnTriggerEnter/Stay/Exit` or `OnInteract` on a `ScriptBehaviour`; triggers are Physics > Is Trigger (Static triggers are convex hulls), usable objects get an Interactable component plus a non-trigger Physics component. Reference: the CollisionTest sample scripts.
- Add a gameobject role: append a value to `Engine/Entities/GameObjectRole.cs` (saved by name, so never rename one) and handle it where it matters; `ScenePhysics`/`WaterVolume` handle `Water`.
- Add or tune a weather: edit/add a `WeatherProfile` in `Engine/Logic/Weather.cs` (new `WeatherType` values are saved by name, so never rename one), then list it in `EnvironmentViewModel.WeatherTypes`/`WeatherTypeValues`.
- Make a ground look wet (bricks, stone, dirt): Material component with base color/normal/roughness maps (+ a height map in Displacement), Shader **WetSurface** (or **Apply Wet Floor Example**), Tiling for large meshes. **Wet in Rain** lets the scene weather soak it. Tune the look in `ApplyWetSurface` (`Gbuffer.fx`) and the rain response in `WeatherProfile`/`SurfaceWetness`.
- Add god rays / light shafts indoors (an attic, a window beam): a closed room with an opening, inside the sun's shadow box (Directional light Shadow Size around its Position), plus a gameobject filling the room with Add Component > **Fog Volume** (Density ~0.15). Lower Environment > Volumetric Fog > **Max Auto Exp.** (0 EV) so the dark room stays dark. Outdoor haze and shafts are the scene fog (Environment > Volumetric Fog); keep the camera past Start Dist. or raise Near Density.
- Make water splash or react to things falling in: Water role + Water material (Material > **Apply Water Example**); Dynamic bodies splash on their own (any body, Buoyancy only decides whether it floats). React with `OnWaterEnter/OnWaterExit(WaterContact)` on the body's script, splash from code with `Splash(point, speed, radius)` (find the surface with `RaycastWater`). Tune detection in `ScenePhysics.UpdateWaterContacts`, the droplets in `WaterSplashes.Throw`/`CreateCrown/CreateJet/CreateMist`, rings and rain rings in `SplashRings`/`RainRings` (`Water.fx`; keep `MAX_SPLASHES` = `WaterSplashes.MaxRipples`). Reference: `Content/Scripts/WaterTestScript.cs`.
- Change water waves: the swell lives in both `Engine/Content/Shaders/Forward/Water.fx` (`Swell`) and `Engine/Physics/WaterWaves.cs` (buoyancy); keep them identical.
- Add an editor-backed scene property: extend snapshot/mutation structs in `IEditorBridge.cs`, populate it in `EditorBridge.BuildSnapshot()`, reconcile it in `BridgeReconciler`, and expose it through `SceneObjectViewModel`/XAML.
- Add an Anvil command: add command in `MainWindowViewModel`, bind it from `MainWindow.axaml`, and route engine work through `IEditorBridge`.
- Fix Anvil selection/focus problems: inspect `BridgeReconciler`, `SceneObjectViewModel.SuppressPush`, `MainWindowViewModel.ReconcilerActive`, and `EditorBridge.PublishEveryNFrames`.
- Change embedded viewport behavior: inspect `MonoGameHost.cs`, especially HWND reparenting, resize debounce, and `RunOneFrame()` loop.
- Change in-engine editor gizmos/selection: inspect `EditorLogic.cs`, `EditorRender`, and ID/outline render modules.
- Show/hide or tune the editor ground grid: Anvil's viewport **Grid** button (`MainWindowViewModel.IsGridVisible` -> `GameSettings.e_showgrid`). `Engine/Renderer/RenderModules/GridRenderModule.cs` + `Content/Shaders/Editor/Grid.fx` draw an infinite plane at Z = `e_gridheight` (the ground, default 0) over the final image, under the gizmos, edit mode only; cell size/fade in `GameSettings.e_gridcellsize`/`e_gridfadedistance`.
- Change the debug stats overlay: edit `Engine/Logic/DebugScreen.cs` (detail level `GameSettings.u_showdisplayinfo`: 0 off, 1 FPS, 3 full). Anvil's viewport **Stats** button toggles it (`MainWindowViewModel.IsStatsVisible`).

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

`Tests/Components` is a framework-free integration check app (not in `Engine.slnx`) covering components, the editor bridge/reconciler, environment, Steam and input. Add `--graphics` for GPU material checks, `--build` to also publish the real game (File > Build). Otherwise use targeted builds/runs:

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
Make sure to always update the [Engine_Wiring.md](Engine_Wiring.md).