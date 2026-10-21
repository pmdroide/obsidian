# Engine Wiring: How Obsidian Fits Together

This is a low-level map of the engine. It covers who creates what, in which order, on which thread, and how data moves through one frame. Read it before you change the frame loop, the renderer, the scene model or the editor seam.

It complements the topic docs instead of repeating them:

- [Editor_Architecture.md](Editor_Architecture.md): Anvil in depth (MVVM, reconciler, XAML).
- [Editor_MCP.md](Editor_MCP.md): local Anvil MCP server setup, Claude Code/Codex connection instructions, tool contracts, game-thread command completion and validation.
- [Scenes_and_Game_Flow.md](Scenes_and_Game_Flow.md): scene list, `GameFlow`, `GameInput`, `GameUI` from a script's view.
- [Input_Actions.md](Input_Actions.md): named input actions (Move, Jump...) and their bindings, Anvil's Input Actions window, rebinding.
- [Gameobject_Components.md](Gameobject_Components.md) and [Script_Behaviours.md](Script_Behaviours.md): writing components and scripts.
- [Collisions_and_Interaction.md](Collisions_and_Interaction.md): physics events from a script's view.

Paths are relative to the repo root. Identifiers match the source. Line numbers are left out on purpose because they drift. Search for the method name instead.

The documentation website lives in `Docs/website`. Its shared neutral light/dark color tokens are defined in `src/styles/theme.css`; `src/styles/site.css` consumes them for the homepage and documentation reader. The theme toggle in `src/app.tsx` sets `data-theme` on `<html>` and saves the choice in local storage; `index.html` restores it before paint. The homepage crystal in `src/welcome.tsx` uses grayscale SVG fills.

---

## 1. Assemblies and build wiring

| Project | Output | Role |
|---|---|---|
| `Engine/Engine.csproj` | `Engine.exe` (WinExe, `net10.0-windows`) | MonoGame `Game`, renderer, scene, physics, scripts, editor bridge |
| `Vista/Vista.csproj` | library | XML/CSS UI (AngleSharp DOM, drawn with `SpriteBatch`), referenced by Engine |
| `ContentPipeline/ContentPipeline.csproj` | `Obsidian.ContentPipeline.dll` (net8.0) | `SkinnedModelProcessor` for mgcb (an FBX without a skinned mesh gets the nodes its clips animate as the skeleton: animation-only clip sources). It is built first, but Engine does **not** reference it (`ReferenceOutputAssembly="false"`). `Content.mgcb` loads it with `/reference:../../ContentPipeline/bin/...` |
| `Editor/Anvil/Anvil.csproj` | `Anvil.exe` (Avalonia 12) | Editor. It references `Engine.csproj` and runs the engine **in-process** |
| `Tests/Components` | console app | Integration checks (`InternalsVisibleTo Include="Components"` gives it engine internals) |
| `Tools/McpInstaller/McpInstaller.csproj` | `anvil-mcp.exe` (.NET 10 Windows console) | Standalone harness configuration installer; Tomlyn parses Codex TOML. No Engine/Anvil assembly dependency. |

How each kind of file reaches the running game (`Engine/Engine.csproj`):

- **MGCB content** (models, textures, `.fx` shaders, sprite fonts): `MonoGameContentReference Include="Content\Content.mgcb"` turns them into `.xnb` files under `bin/.../Content`. They are loaded with extensionless `content.Load<T>("path")`.
- **OBJ model sources:** `.gitignore` exempts `Engine/Content/**/*.[Oo]bj` from the compiler-object ignore rule so models such as `GameObjects/Cloth/cloth.obj` and `GameObjects/Tree/tree.obj` can be tracked. Generated `obj/` and `bin/` directories remain ignored.
- **Scripts** (`Content/Scripts/**/*.cs`): compiled **into `Engine.dll`** by normal SDK globbing, in `namespace Engine.Scripting`. They are also copied as loose files so the editor can open them. There is no runtime compiler (no Roslyn), so editing a script means rebuilding the engine.
- **Loose runtime files** are copied with `CopyToOutputDirectory` and never pass through mgcb: scenes (`.obsc`, `.probes`, `.lightmaps`, `.reflections`), Vista UI (`.xml`/`.css`), audio (FMOD), intro videos (LibVLC), `Content/System/*.json`, skyboxes. A file a standalone build needs must also have `CopyToPublishDirectory`.
- **Render feature shaders** (`Content/RenderFeatures/*.fx`) are the exception among `.fx` files: they are not in `Content.mgcb`. The editor compiles each one with mgcb into a loose `.xnb` next to it (`RenderFeatureCompiler`), and `Content\RenderFeatures\**\*.xnb` + `Content\System\RenderFeatures.json` are copied like other loose files. A shader with errors therefore never breaks the content build (section 9.6).
- **Native libraries:** `steam_api64.dll` and `fmod.dll` are linked flat into the output root.

NuGet dependencies that matter at runtime: `MonoGame.Framework.WindowsDX 3.8.4.1`, `BepuPhysics 2.4.0`, `FmodForFoxes 3.2.0`, `LibVLCSharp`, Steamworks.NET (from `thirdparty/steam`).

**Repository scripts:** `Tools/Scripts/` contains `bootstrap.bat`, `build-editor.ps1`, `build-mcp-installer.ps1`, `clean.ps1` and `install-mcp.ps1`. Each resolves the repository root two levels above its own location, so project paths and default outputs remain relative to the checkout. `bootstrap.bat` restores/builds `Engine.slnx`; `clean.ps1` removes only `bin/` and `obj/` folders next to project/content manifests (`-WhatIf` previews the cleanup). See the [README repository structure](../../README.md#repository-structure) for folder descriptions and script links.

**Release build of the editor:** `Tools/Scripts/build-editor.ps1` (`-Clean`, `-Run`, `-Output`) runs `dotnet tool restore` in `Engine/`, then a self-contained win-x64 Release `dotnet publish` of `Anvil.csproj` into `Builds/Anvil` (git-ignored). The Engine's `.xnb` content and native DLLs come along through the project reference. The script also copies `steam_appid.txt`, which `Engine.csproj` keeps out of published games, because `SteamService.Connect` needs it next to the exe outside the Steam client. The output must stay **inside the checkout**: Anvil finds the source `Engine/Content` through `AssetImporter.LocateEngineContentRoot`, which walks up from the exe folder. Asset import, scene/settings saving and `GameBuilder` (File > Build, which needs the .NET SDK) all depend on that path.

---

## 2. Threads

**Debug logging:** `ScriptBehaviour.Log(message)` prefixes the owning gameobject/camera and calls `EditorBridge.Log`. The bridge synchronizes file appends and a bounded history of 1,000 timestamped entries; file errors do not discard the in-memory entry. `MainWindowViewModel` polls copied entries every 200 ms on the Avalonia UI thread, preserving message timestamps and labeling MCP messages `Anvil:MCP`. Its Console is also capped at 1,000 entries, and Clear advances past pending history. The polling timer stops when the window closes. Standalone games retain file logging without an Avalonia dependency.

**MCP lifecycle logging:** `EditorMcpHost.LogStatus()` reports actual listener state separately from `EditorAutomationService.IsActive`, bridge attachment and read-only policy. Successful startup and shutdown log that state; startup failures log the exception type. The HTTP middleware logs the first authenticated request once per listener startup. `McpSettingsViewModel.LogStatusCommand` exposes the same report through the MCP panel's **Log Status** button. These messages omit tokens, headers and request arguments.

The separate harness installer (`Tools/McpInstaller`, launched by `Tools/Scripts/install-mcp.ps1` or published with `Tools/Scripts/build-mcp-installer.ps1`) registers the existing HTTP endpoint with Claude Code, Cursor, Codex and VS Code. Its menu/flags select user or project scope. It reads Anvil's saved user-local connection settings, plans every selected configuration, then applies per-file atomic updates with backups; it does not start the engine or listener. Project installs store environment references, and user installs default to the saved token. `Tests/McpInstaller` checks this filesystem behavior against temporary profiles without building the engine.

Anvil's optional MCP host (`Editor/Anvil/Mcp/EditorMcpHost.cs`, C# SDK 2.2.0) runs HTTP handlers on worker threads. `MainWindowViewModel.AttachBridge` connects `McpSettingsViewModel` to the bridge; Window > MCP controls the application-owned listener, independent of tab visibility. Typed requests enter `IEditorBridge.ExecuteAutomationAsync`; `Engine/Editor/EditorAutomation.cs` drains up to four of its 64 queued commands per frame after ordinary UI edits in `DrainAndPublish`. Completion includes copied state captured on the game thread, separate from the six-frame UI snapshot throttle. Scene swaps replace the generation token and reject stale writes. Queued cancellations never run; started operations finish and remain queryable after caller timeouts. `MonoGameHost` stops automation before engine teardown. HTTP/MCP dependencies and user-local token settings stay in Anvil; standalone games have no listener. See [Editor_MCP.md](Editor_MCP.md) for tools, limits and the `--mcp` integration checks.

```
STANDALONE (Engine.exe)                     ANVIL (Anvil.exe, one process)
──────────────────────                      ───────────────────────────────────────────────
[STAThread] Program.Main                    [STAThread] Avalonia UI thread
  └─ new Engine().Run()                       ├─ MainWindow / view models / reconciler
       MonoGame's own loop                    └─ MonoGameHost.CreateNativeControlCore
       ├─ Update(gameTime)                          └─ starts thread "MonoGameEngineThread" (STA, background)
       └─ Draw(gameTime)                                 ├─ new Engine.Engine()
                                                         ├─ bridge.SetHostedByEditor(true)
                                                         ├─ UIThread.Post(BridgeReady)
                                                         └─ loop: PeekMessage/Dispatch → RunOneFrame() → Sleep(1)
Thread pool (both cases):
  - LightingSystem.StartBake → ProbeVolumeBaker (Task.Run, Parallel.For per probe)
  - Vista UIManager.Load parses the DOM on a pool thread; the caller blocks on it
  - Anvil GameBuilder runs `dotnet publish` (Task.Run)
  - VoxelSystem generates and meshes a voxel world (Task.Run + Parallel.For) while editing;
    during Play it runs the same build on the game thread and waits for it
  - RenderFeatureCompiler runs mgcb for one render feature shader (Task.Run); the result is
    queued and the effect is loaded on the game thread by RenderFeatures.Update
```

Rules that follow from this:

- **Everything in the engine is single-threaded on the game thread:** scene lists, components, scripts, physics (BEPU is created with a `null` thread dispatcher), rendering. The bake hands its result back through `LightingSystem.Update`, which runs on the game thread. A voxel build works on its own new `VoxelWorld` and CPU meshes only. `VoxelSystem.Update` uploads the buffers and adds the BEPU statics on the game thread.
- Anvil never calls `Game.Run()`. Its WinForms message loop would deadlock against Avalonia's. `MonoGameHost.GameThreadProc` pumps Win32 messages itself and calls `RunOneFrame()`. The only pacing on top of that is `Thread.Sleep(1)` plus the engine's own VSync or fixed-FPS settings.
- The only sanctioned channel across threads is `EditorBridge` (section 11).

---

## 3. Boot sequence

### 3.1 `Engine` constructor (`Engine/Engine.cs`)

Order:

1. `SteamService` is created, started and stored in `SteamService.Current`.
2. `GraphicsDeviceManager`, then `Content.RootDirectory = "Content"`.
3. `EditorBridge`, which is then bound to Steam and to `InputDeviceMonitor`.
4. `ScreenManager(_bridge)`.
5. `GameFlow.QuitHandler = Exit`.
6. `PhysicsSystem(new Vector3(0, 0, -9.81f))`. **Z is up.**
7. Back buffer = `GameSettings.g_screenwidth/height`, `GraphicsProfile.HiDef`, resizable window. Then `ClientSizeChanged`, `Activated` and `Deactivated` are hooked.

### 3.2 `Engine.Initialize`

1. `ApplyGameInfo()`, standalone only. It applies `Content/System/GameInfo.json`: title, icon, size, borderless fullscreen and FPS cap.
2. `ScreenManager.Load(Content, GraphicsDevice)`.
3. `ScreenManager.Initialize(GraphicsDevice, _physics)`.

`LoadContent` is empty. All loading goes through `ScreenManager`.

### 3.3 `ScreenManager.Load`: the order matters

```
_renderer = new Renderer(); _sceneLogic = new MainSceneLogic(); _editorLogic, _assets, _debug, _videoIntro
Globals.content = content              ← must come first
Shaders.Load(content)                  ← empty method; touching it runs the static initialisers,
                                          which call Globals.content.Load<Effect>(...)
_shaderManager = new ShaderManager(content, device)
_assets.Load(content, device)          ← primitives, materials, sky, skinned models, fonts, error assets,
                                          re-registers previously imported models
_audio = new AudioManager()
_renderer.Load(content, _shaderManager)   ← creates every render module (section 9.1)
_sceneLogic.Load(content)              ← no-op
_debug.LoadContent(content)
_videoIntro.Load(...)                  ← standalone only; Anvil never starts LibVLC
LoadUIFonts(content)                   ← default, monospace, display, heading, body, caption
```

`Globals` is a one-field static class (`ContentManager content`) declared at the top of `Engine/Recources/Shaders.cs`. Anything that touches `Shaders.*` before `Globals.content` is set throws a `TypeInitializationException`. The tests swap `Globals.content` around such code for this reason.

### 3.4 `ScreenManager.Initialize`

1. `SpriteBatch`.
2. When hosted, the state is set to `GameState.MainGame` (no intro). Otherwise `_videoIntro.Initialize()`.
3. `_renderer.Initialize(device, _assets)`. This creates the full-screen triangle, blur, editor render, bloom, color grading, auto exposure and the module `Initialize`s. It then runs `GameSettings.ApplySettings()` and `SetUpRenderTargets(w, h, false)`.
4. `_audio.Initialize("Content")`. This starts FMOD and sets `Audio.Instance`.
5. `_sceneLogic.Initialize(assets, physics, device)`:
   - creates `ScenePhysics`, which sets `ScenePhysics.Current`
   - creates `MeshMaterialLibrary`
   - creates `VoxelSystem(device, MeshMaterialLibrary, physics)` (`MainSceneLogic.Voxels`)
   - subscribes `SceneManager.SceneChanged → OnSceneChanged`
   - creates `PlayModeController` and subscribes `ModeChanged`
   - sets `GameFlow.SceneLogic = this`
   - builds the empty boot scene: a game camera, a separate `EditorCamera`, an `EnvironmentSample`, an `SdfGenerator` and one shadowed sun.
6. `_editorLogic.Initialize`, `_debug.Initialize`.
7. `_bridge.Bind(_sceneLogic, _editorLogic, _assets)`. This sets `Input.HostBridge` and subscribes to scene swaps, play mode changes, lighting status and model or material registration.
8. `SceneList.Load()` reads `Content/System/SceneList.json`.
9. `GameUI.Bind(device, _uiFonts)`.
10. `RenderFeatures.Load(Globals.content)` reads `Content/System/RenderFeatures.json` and loads each feature's compiled `.xnb` (compiling first, in the background, when the `.fx` is newer). It keeps the content manager's services to load effects later.
11. When hosted, `GameStats.e_EnableSelection = true`, so ID and outline passes always run in Anvil.

### 3.5 First scene

- **Standalone:** `VideoIntroLogic` plays `IntroVideos.json`. When `HasFinished` becomes true, the state switches to `MainGame` and `MainSceneLogic.StartFirstScene()` runs. That method loads `SceneList.Scenes[0]` through `SceneManager.LoadScene` and calls `PlayMode.Play()`.
- **Anvil:** it stays in Edit mode on the empty scene. The user opens a scene through the bridge (`EnqueueLoadScene`).

---

## 4. One frame

MonoGame calls `Engine.Update`, then `Engine.Draw`. Anvil gets the same calls through `RunOneFrame()`.

### 4.1 Update (all on the game thread)

```
Engine.Update
├─ _steam.Update(); _inputDevices.Update(dt)
├─ ApplyPendingResize()                 ← before the active gate (section 4.3)
├─ active = _isActive || IsHostedByEditor   (if not active: return)
├─ Escape → Exit() if GameFlow.EscapeQuits  (reads last frame's Input state)
├─ ScreenManager.Update
│   ├─ _audio.SystemUpdate()            ← FMOD pump, even during the intro
│   ├─ [VideoIntro] update; on finish → StartFirstScene(); return
│   ├─ #if DEBUG _shaderManager.CheckForChanges()
│   ├─ RenderFeatures.Update()          ← load finished render feature compiles; every 30 frames recompile saved .fx files
│   ├─ _editorLogic.Update(..., _editorReceivedDataBuffer, ...)   ← uses LAST frame's hovered ID
│   ├─ _sceneLogic.Update
│   │   ├─ GameFlow.UpdateLoad(...)     ← CurrentLoad: a blocking load runs whole, an async one takes a step (7.5); LoadingScreen fades/fills
│   │   ├─ Input.Update(gameTime, Camera)   ← snapshot keyboard/mouse; drives the editor camera
│   │   ├─ GameInput.Update(dt)         ← script-facing input, gamepads, menu repeat, MouseDelta
│   │   │   └─ InputActions.Update(dt)  ← reload if Anvil saved; every action reads its bindings (strongest wins)
│   │   ├─ PlayMode.UpdateScripts(gameTime) ← component OnUpdate (Play only; particle systems step here)
│   │   ├─ ParticleSystemComponent.UpdatePreviews(...) ← Edit only: Play On Awake particle systems preview
│   │   ├─ FoliageWindComponent.UpdateAll(entities, dt (0 while paused), scene wind) ← Edit and Play: tree wind clocks, TransformMatrix.Wind, HasChanged
│   │   ├─ LiquidComponent.UpdateAll(entities, dt (0 while paused), physics gravity) ← Edit and Play: bottle liquid slosh (LiquidSlosh), TransformMatrix.Liquid/LiquidLook
│   │   ├─ MaterialAsset.SyncScene(entities) ← Edit and Play: Material components linked to a .mat copy the file's surface when it changed (10)
│   │   ├─ WaterSplashes.Update(dt, 0 while paused) ← splash droplets and water rings age and move
│   │   ├─ BreakBursts.Update(dt, 0 while paused) ← dust/blood of broken Breakables (droplets die on static colliders)
│   │   └─ dev hotkeys (F1 render mode; Space/L/X/M outside Play)
│   ├─ _audio.UpdateListener(Camera)
│   ├─ _renderer.Update(...)            ← editor mouse-moved timer, SDF generator
│   ├─ _sceneLogic.Lighting.Update(...) ← collect a finished bake, upload probe textures
│   ├─ _debug.Update
│   ├─ GameUI.Update                    ← Vista layout and XML/CSS hot reload, after scripts
│   └─ _bridge.DrainAndPublish()        ← apply Anvil's queued edits; publish a snapshot every 6 frames
└─ ScreenManager.UpdatePhysics(dt, totalSeconds)
    ├─ MainSceneLogic.UpdatePhysics → VoxelSystem.Update(ActiveScene, playing)   ← also while paused
    │   ├─ clear last frame's chunk HasChanged flags
    │   ├─ voxel settings changed? build a new world (worker thread in Edit, now in Play); swap it in when done
    │   └─ remesh chunks scripts edited this frame, rebuild their colliders
    └─ MainSceneLogic.UpdatePhysics → ScenePhysics.Update(entities, dt, simulate, time)
        ├─ rebuild or sync bodies from components and transforms
        ├─ if simulate: Step(min(dt, 1/20)) → pull poses → contacts
        ├─ dispatch OnCollision*/OnTrigger* to scripts
        └─ water contacts: bodies crossing a Water surface → WaterEntered/WaterExited (MainSceneLogic.WaterSplashes) + OnWaterEnter/OnWaterExit
```

Consequences of this order:

- **Scripts run before physics in the same frame.** Collision and trigger callbacks fire after the step, so they run after every script's `Update` that frame.
- **Voxel edits made by scripts are solid in the same frame's step.** The voxel sync runs between scripts and the physics step. A script that reads `ScriptBehaviour.Voxels` during Play builds a missing world right away (`VoxelSystem.Ensure`), so a scene loaded mid-Play has ground before its first step.
- **Editor edits land before physics** but after scripts. The snapshot Anvil receives is built *before* that frame's physics step.
- **Picking lags one frame.** The renderer computes the hovered ID during `Draw`. `ScreenManager` stores it in `_editorReceivedDataBuffer`, and `EditorLogic` reads it in the next `Update`.
- **The timestep is variable.** Physics uses the frame `dt`, clamped to 1/20 s. `IsFixedTimeStep` is on only when `GameSettings.g_fixedfps > 0`. The defaults are `g_vsync = false` and `g_fixedfps = 0`, so the frame rate is uncapped.

### 4.2 Draw

```
Engine.Draw
├─ standalone and window inactive → Sleep(20); return
├─ CheckFPSLimitChange()                ← applies g_vsync / g_fixedfps changes
└─ ScreenManager.Draw
    ├─ [VideoIntro] clear black, draw the video frame; return
    ├─ _editorReceivedDataBuffer = _renderer.Draw(camera, meshMaterialLibrary, entities, decals,
    │        pointLights, directionalLights, envSample, debugEntities, editorData, gameTime,
    │        lighting, lightingSettings, scene)          (section 9.4)
    ├─ GameUI.Draw(spriteBatch)         ← script menus and HUDs over the 3D image, then engine overlays (loading screen)
    └─ _debug.Draw(gameTime)            ← stats and console on top
```

### 4.3 Resizing

`Window.ClientSizeChanged` only sets `_pendingResize`. Resetting the device inside the WinForms `WndProc` throws a null-reference exception on Anvil's reparented child window. `ApplyPendingResize()` does the actual work at the top of the next `Update`:

1. For the first 120 ticks, it forces a reconcile whenever the real Win32 client rect (`GetClientRect`) differs from `g_screenwidth/height`. MonoGame's `ClientBounds` stays at 640x480 when Anvil resizes the window from outside.
2. **Debounce:** it rebuilds only after the size has held for one tick.
3. It sets `GameSettings.g_screenwidth/height` and `PreferredBackBuffer*`, calls `ApplyChanges()`, sets a full `Viewport`, then calls `ScreenManager.UpdateResolution()` → `Renderer.UpdateResolution()` → `SetUpRenderTargets(w, h, false)`.

Anvil's side is in `MonoGameHost`:
- `ArrangeOverride` calls `MoveWindow` on the engine window.
- A 30 ms "settle" timer keeps re-applying the size for about 240 ms.
- A boot watchdog does the same every 50 ms for 3 s.
- `SyncPreferredBackBuffer` pre-sets `PreferredBackBuffer*` so MonoGame's own `OnResize` returns early. `ApplyPendingResize` is the only place the swap chain is actually resized.
- Keys reach the engine through window-level `KeyDown`/`KeyUp` handlers (tunnel + bubble) that call `EditorBridge.SetHostKeyState`. In Play, `IsGameKeyInPlay` also marks Space/Enter handled on the tunnel pass, unless a `TextBox` or `MenuItem` has focus. Without that, the button that kept focus (usually Play itself) would click on them and stop Play.

Anvil's window chrome: `MainWindow` extends its client area into the title bar (`ExtendClientAreaToDecorationsHint`, 36 px), and Avalonia 12 draws the caption buttons itself (`WindowDrawnDecorations`). `MainWindow.WindowDecorationsTheme` derives from the Fluent theme and hides its fullscreen button (`PART_FullScreenButton`) and title text (the `TextBlock` in `PART_TitleTextPanel`; the panel's own `IsVisible` is a template binding that beats style setters), so only minimize/maximize/close remain and the brand mark isn't overdrawn. The render-stats badges keep a 136 px right margin for those three buttons. Every editor colour is an `Anvil*Brush` resource in `Editor/Anvil/App.axaml`: neutral dark greys with a blue accent (`AnvilPrimaryBrush`, `AnvilSelectedBackgroundBrush`). The views reference these keys; the only raw colours are a few overlay tints and the Delete Project button in `ProjectSettingsWindow`.

The viewport toolbar places workspace tabs on the left and the Environment, World and Pipeline shortcuts beside the tool icons on the right. It contains `ViewportTabs`, a native `TabStrip` bound to `MainWindowViewModel.WorkspaceTabs` and `SelectedWorkspaceTab`. Only Engine is open initially. The adjacent + button builds a fresh menu from `AvailableWorkspacePanels` in `WorkspaceTabAdd_Click`; its choices are Engine, Environment, World, Pipeline, Post Processing, Lighting, Input Devices and Steam. `OpenWorkspaceTabCommand` selects or adds the matching `WorkspaceTabViewModel` by panel kind, so reopening a panel never duplicates it. Every tab header has an accessible X button bound to `CloseWorkspaceTabCommand`. Closing the selected tab selects its right neighbor, or its left neighbor at the end; closing an inactive tab preserves selection. All tabs can be closed, leaving no panel selected, and reopened through +. Closing Engine hides its persistent host without destroying it. A horizontal scroll viewer brings the selected header into view while + remains accessible. The `viewportTabs` styles provide full-height, 32 px headers with a 100 px minimum width, accent underline, hover and keyboard focus. The tab strip and + button stretch through the toolbar height, with no inset around the tab background. Environment and World use the same icon-and-label layout, 12 px icons, spacing and button style as Pipeline (CloudSun, Globe and Layers respectively). `WorkspacePanel` contains the persistent engine viewport and the existing live settings views as siblings; visibility follows the selected panel flags. These views keep their controls and view-model instances when tabs change. `SetInspectorViewCommand` routes settings shortcuts from the toolbar, Inspector header and Window menu to the same workspace command; object selection/material assets remain in the right-hand Inspector. Pipeline selection refreshes the render-pass list. The engine host stays attached while hidden. `MonoGameHost` gates keyboard forwarding and focus restoration by visibility and releases held host keys; `IsHostPointerOverViewport` also suppresses hidden mouse buttons, native keyboard polling, camera scrolling, mouse delta and wheel values. Held pointer buttons stay suppressed until released after returning to Engine. `Tests/Components/Program.cs` checks panel opening/reuse, closing selected/inactive/all tabs, reopening Engine, Inspector and scene-selection preservation, returning to Engine and hidden input. Tab layout is session-local and no separate windows are involved.

Inspector component headers use the `InspectorComponent` control theme in `Editor/Anvil/Themes/InspectorControls.axaml`, merged by `App.axaml`. Each typed component template in `MainWindow.axaml` supplies its icon and name through `Expander.Header`; the shared theme places a left disclosure arrow and a two-way `ComponentViewModel.Enabled` checkbox before them in a 28 px row, with a compact X removal button at the far right. The removal button uses `ComponentRemoveButton`, binds to the attachment's existing `RemoveCommand`, and stays available while collapsed without toggling expansion. Component bodies omit the old Remove Component buttons. The arrow and title toggle `Expander.IsExpanded`; the separate checkbox edits enablement without collapsing the component and remains visible when collapsed. Nested property expanders retain the standard layout. The `InspectorCheckBox` theme gives main-window checkboxes a 12 px dark inset square with a subtle border, nearly square corners and a fine blue accent tick (an accent dash for mixed values). Checked and mixed states overlay the dark base with `AnvilPrimaryBrush` at 18% opacity; the tick/dash stays at full opacity. Hover, keyboard focus and disabled states remain distinct. New component templates should use `Theme="{StaticResource InspectorComponent}"` and omit a duplicate Enabled checkbox in the body.

The viewport debug stats in `Engine/Logic/DebugScreen.cs` draw in `Color.White` with their existing black shadow. The console prompt and suggestions still use `consoleColor`; stats no longer inherit that color.

---

## 5. Global and static state

Most cross-system wiring goes through statics. Knowing them saves a lot of searching.

| Static | Set by | Used for |
|---|---|---|
| `Globals.content` | `ScreenManager.Load` | `Shaders.*` static initialisers, some loaders |
| `Shaders.*` | static init | Cached `Effect`, `EffectParameter` and `EffectPass` objects for shared shaders |
| `GameSettings.*` | defaults, `ApplySettings`, the editor | Render, editor and physics switches. Prefixes: `g_` graphics, `e_` editor, `d_` debug, `sdf_`, `u_` UI, `p_` physics. Some setters push straight into shader parameters |
| `GameStats.*` | renderer, editor | Per-frame counters, `e_gizmoMode`, `e_EnableSelection`, `e_LocalTransformation` |
| `GameFlow.SceneLogic`, `QuitHandler`, `Paused`, `EscapeQuits`, `CurrentLoad`/`LastLoad`, `LoadBudgetMs`, `LoadingScreenDocument` | `MainSceneLogic.Initialize`, `Engine` ctor, scripts | Script API for scenes, loads and pausing |
| `LoadingScreen` (`_ui`, `_load`, fade) | `GameFlow.UpdateLoad` | The engine loading screen overlay of `LoadSceneAsync` |
| `ScenePhysics.Current` | `ScenePhysics` ctor | Script raycasts (also `RaycastWater`), navigation |
| `Input.*` state, `Input.HostBridge` | `Input.Update`, `EditorBridge.Bind` | Raw keyboard/mouse, Anvil key merge |
| `GameInput.*` (pad overrides for checks: `OverrideGamePad`) | `GameInput.Update` | Script input |
| `InputActions` (action instances by name, `_reloadRequested`), `InputControls` (control catalogue) | `ScreenManager.Load` → `Load`, `InputActions.Update`, Anvil's Save (`RequestReload`, thread-safe), `PlayModeController.Stop` (`Load`), scripts (rebinds) | Named actions scripts read (`WasActionPressed`, `GetVector`) |
| `GameUI.Layers` | `GameUI.Open/Close`, `OpenOverlay` (engine) | Script UI layers and engine overlays |
| `Audio.Instance` | `AudioManager.Initialize` | Static audio facade |
| `ScriptBehaviour.Running` | `ScriptBehaviourComponent` around each call | Records which script owns a `GameUI` layer |
| `SteamService.Current` | `Engine` ctor | Steam session for scripts |
| `SaveData.Folder`, pending async writes | default `%LOCALAPPDATA%/<game name>/Saves`, scripts, tests | Save files (`Engine/Logic/SaveData.cs`); `Engine.UnloadContent` calls `SaveData.Flush()` |
| `IdGenerator._currentId` | `GetNewId`, `Reseed` | Object IDs (section 6.3) |
| `RenderFeatures` (feature list, compile queue, published `Describe()` rows, `Changed`) | `ScreenManager.Initialize` → `Load`, the bridge, scripts (`Find`), `PlayModeController.Stop` (`RestoreSaved`) | User render passes drawn by `RenderFeatureRenderModule` (section 9.4) |

There is **no** global `GraphicsDevice`. It is passed into `Initialize` methods, and `Assets` keeps `GraphicsDevice` and `Content` as instance properties.

---

## 6. Scene model

### 6.1 Ownership

```
MainSceneLogic                                  (runtime drivers, one per process)
├─ SceneManager ──► ActiveScene : Scene         (data that round-trips to .obsc)
│                     ├─ BasicEntities, PointLights (incl. SpotLight), DirectionalLights, Decals
│                     ├─ MainCamera (game camera, has Components), EnvironmentSample
│                     ├─ Environment (EnvironmentSettings: sky, fog, weather, wind, voxel world…)
│                     ├─ Lighting (LightingSettings) + BakedProbes (.probes sidecar) + BakedLightmaps (.lightmaps sidecar)
│                     └─ Name, FilePath, IsDirty
├─ EditorCamera            (never saved)
├─ Camera  ⇒  Play && MainCamera != null ? ActiveScene.MainCamera : EditorCamera
├─ MeshMaterialLibrary     (render registry, section 9.2)
├─ ScenePhysics            (BEPU bodies, section 8)
├─ VoxelSystem (Voxels)    (the active scene's VoxelWorld: chunk meshes in the library, chunk statics in BEPU)
├─ WaterSplashes           (splash droplet systems + water rings; fed by ScenePhysics water contacts, read by the renderer through GameFlow.SceneLogic; cleared on scene swap and Stop)
├─ PlayModeController      (section 7.3)
├─ LightingSystem          (probe bakes and GPU data)
├─ SdfGenerator, DebugEntities
└─ PersistentGameObjects   (carried across scene loads during Play)
```

`MainSceneLogic.BasicEntities` and the other list properties just forward to `ActiveScene`. They are not copies.

### 6.2 Swapping scenes

All swaps go through `SceneManager.SetActiveScene(newScene)`. It fires `SceneChanged(old, new)`, which runs `MainSceneLogic.OnSceneChanged` and `EditorBridge.OnSceneSwap`:

1. If not switching during Play, it forgets `_editScene` and clears the persistent set.
2. For each old entity, it calls `OnStop()` on every component and `entity.Dispose(MeshMaterialLibrary)`.
3. `ScenePhysics.DetachAll(carried)`. Carried persistent objects keep their bodies and velocity.
4. `VoxelSystem.Reset()` drops the old voxel world: it unregisters the chunks, removes their statics and cancels a pending build. The new scene's world is built at the next `UpdatePhysics`, or earlier when a playing script asks for it.
5. `MeshMaterialLibrary.Clear()`.
6. It adds a default `MainCamera` and `EnvironmentSample` if the scene has none. Without them every `Draw` would throw.
7. `RegisterInLibrary` for every new entity.
8. `PlayMode.Stop()`, unconditionally.
9. `GameUI.CloseAll(carried)`.
10. The bridge clears the editor selection and raises `SceneChanged` toward Anvil.

Scene loads that scripts request during Play take a different path (section 7.5).

### 6.3 IDs and how picking encodes them

- `IdGenerator.GetNewId()` pre-increments a counter that starts at 103, so the first ID is 104. IDs 1–3 are reserved for gizmo axes, and 0 means "nothing".
- Every `BasicEntity`, light, decal and environment sample takes an ID in its constructor. `BasicEntity.Id` mirrors into `WorldTransform.Id`, which the ID render pass reads.
- Scene load keeps the IDs stored in the file, calls `IdGenerator.Reseed(maxId)`, and only then gives new IDs to duplicates or missing ones. Script `GameObject` fields are saved as these IDs.
- `EditorBridge.MainCameraId = -1` is a synthetic ID for the scene camera in snapshots.
- Bones of skinned gameobjects get synthetic **negative** IDs below -1: `BoneObject.IdFor(ownerId, bone) = -(2 + ownerId * 1024 + bone + 1)` (bone -1 = the Armature, up to 1023 bones), decoded by `BoneObject.TryDecode`. They are never saved and never drawn into the ID buffer, so bones are selected from the Hierarchy only.
- **ID ⇄ color:** `GetColorFromId` packs an ID into RGB in **base 255** (`r + g*255 + b*255²`), not 256. `GetIdFromColor` maps white to 0. Keep both functions in sync if you change one.

---

## 7. Entities, components, scripts, Play mode

### 7.1 `BasicEntity` and the transform dirty flag

`BasicEntity : TransformableObject` (`Engine/Entities/BasicEntity.cs`) holds:

- read-only model data: `ModelDefinition`, `Model`, `BoundingBox`, `BoundingBoxOffset`, `SignedDistanceField`, `Material`
- `Position`, `RotationMatrix`, `Scale`
- `IsEnabled`, `Role` (built-in behavior), `RoleName` / `RoleId`, `Tag` / `TagId`, `Layer` / `LayerId`, `Persistent` / `RuntimePersistent`, `IsRuntimeSpawned`
- `Components`
- `BonePose` (`Engine/Animation/BonePose.cs`): posed bones of a skinned model, saved as `BasicEntityRecord.BonePose`, copied by `Clone`, released (own skin dropped) by `Dispose(library)`
- BEPU runtime state: `StaticBody`, `DynamicBody`, `Attached*` caches, `SyncedPosition/Rotation`, `CollisionGroup`, `Ragdoll`, `Glass` (a Breakable Glass pane's `GlassShatter`), `Fracture` (a Breakable's `FractureShatter`)
- `CollapsedBones` / `CollapsedBonesVersion` (internal, runtime only): bone names `Animation.BoneCollapse` shrinks to their joint when skinning (a Breakable head shot off this body); the Animator, Ragdoll and `BonePose` pass their skin matrices through `BoneCollapse.Apply`
- `PartMaterial(part, index)` (internal): the material the library draws one model part with; Breakable pieces draw each part's triangles with it
- `DrawMaterial` (internal): the material the mesh library draws the model with (the Material component's instance, else `Material`, else the first part's effect); glass shards draw with it
- `WorldTransform : TransformMatrix`

`TransformMatrix` is the object the renderer actually holds. It has `World`, `InverseWorld`, `Scale`, `Id`, `HasChanged`, `Rendered`, plus `Skin` (`SkinnedMeshInstance`) and `Cloth` (`ClothMeshInstance`) when the vertices are deformed on the CPU.

How the dirty flag moves through a frame:

```
setter (Position / RotationMatrix / Scale)  →  WorldTransform.HasChanged = true
Renderer.Draw → MeshMaterialLibrary.FrustumCulling
      for entities with HasChanged: entity.ApplyTransformation()
          World = Scale · Rotation · Translation(Position)
      recompute bounding centres, re-test the frustum
Renderer.Draw (end) → FrustumCullingFinalizeFrame → HasChanged = false
```

`World` is rebuilt **only** inside `FrustumCulling`, and `FrustumCulling` returns early when `GameSettings.g_cpuculling` is false (the default is true). If you turn CPU culling off, moved objects stop updating their world matrix.

### 7.2 Components

- `GameComponent` (`Engine/Components/GameComponent.cs`) has `InstanceId` (a Guid; `[JsonIgnore]`, but saved in the `ComponentRecord`), `Enabled`, and the virtual hooks `OnStart`, `OnUpdate`, `OnChanged`, `OnAdded`, `OnRemoved`, `OnStop`.
- `BasicEntity.AddComponent` rejects a duplicate `InstanceId`, and rejects a second component of the same type unless the registry says `AllowMultiple`. It then calls `OnAdded`. `RemoveComponent` calls `OnRemoved`.
- `ComponentRegistry` handles polymorphism by hand, with no `[JsonDerivedType]`:
  - `Capture` writes a `ComponentRecord { Type = TypeId, InstanceId, Data = SerializeToElement(component, concreteType) }`.
  - `Restore` deserialises by `Type`.
  - `Copy` is `Restore(Capture)` with a fresh Guid.
  - The same records are used for scene files, bridge snapshots and cloning.
- Registration order (= Add Component menu order): `material`, `physics`, `audio`, `script-behaviour` (multiple), `animator`, `ragdoll`, `interactable`, `cloth`, `npc`, `lod`, `particles`, `fog-volume`, `foliage-wind`, `reflection-probe`, `planar-reflection`, `probe-volume`, `post-volume`, `ao-proxy`, `breakable-glass`, `breakable`.
- The four Source 2 look components ([Source2_Look.md](Source2_Look.md)) are data the renderer collects every frame, in Edit and Play; none has Play hooks:
  - `ReflectionProbeComponent` (`reflection-probe`): `ReflectionProbes.Update(entities, scene, graphics)` (called in `Renderer.Draw` before the shadow maps, so the box meshes are already `Hidden`) tracks each enabled probe and restores its saved filtered cube before capture selection. `NextCapture` returns the nearest probe whose capture is missing or whose box, resolution or `CaptureRequest` changed; `CaptureReflectionProbe` captures one per frame through `CaptureCube` (a small deferred pipeline per face at probe size, no SSR/fog, diffuse-only environment `CaptureMode`), then `EnvironmentPrefilter.Prefilter`. `StoreCapture` reads all six HDR faces and every mip into `Scene.ReflectionCaptures`, keyed by component `InstanceId`, and marks the scene dirty. `Visible` = at most 4, priority then distance, for the environment and forward passes.
  - `PlanarReflectionComponent` (`planar-reflection`): `PlanarReflectionRenderModule.Draw` (from `Renderer.DrawPlanarReflections`, before `Compose`) detaches last frame's images, then picks the enabled panes in the view frustum whose face the camera is in front of (`TryGetPlane`: the face across the thinnest local axis, normal towards the camera), nearest first, at most 2. Per pane: a `HalfVector4` + D24 mip-mapped target (`PlanarReflectionView`, screen size × Resolution), `Matrix.CreateReflection(plane) * viewProjection`, `MeshMaterialLibrary.ReflectionCull` (mirrored frustum, in front of the plane, Render Distance, never the pane itself), `ForwardRenderModule.DrawReflection` (`RenderType.Reflection`), then `DrawReflectionSky` (the `PlanarSky` full-screen triangle at depth 1, with the environment module's `BackgroundSkyCube`/`BackgroundSkyMap`). The image goes on the pane's `TransformMatrix.PlanarReflection`, and the forward pass binds it per instance (`ForwardRenderModule.SetPlanarReflection`). The module saves and restores the device's blend/depth/rasterizer state: Compose's full-screen triangle and `ReconstructDepth` don't set them, and a depth-writing state left behind kept the reconstructed depth from being written (every forward pixel then failed its depth test). The forward effect's `PlanarReflectionMap` is unset around both passes, because D3D drops one binding of a texture that is also the render target.
  - `ProbeVolumeComponent` (`probe-volume`): only read by the bake (`LightingBakeInput.DetailVolumes`), which lays out a grid over `Bounds(owner)` per volume and stores it in `ProbeVolumeData.Detail` (`.probes` v3). `LightingSystem` packs the main volume and up to 3 details into one `Texture3D` (`ProbeData`: 5 grids along X, volumes stacked along Z) because the environment shader is at MonoGame's 16 texture slots.
  - `PostProcessVolumeComponent` (`post-volume`): `PostProcessVolumes.Evaluate` (start of `Renderer.Draw`) blends the scene's `EnvironmentSettings.Post` towards each enabled volume by `WeightAt(camera)`, lowest priority first, only for the `Overrides` names, and keeps the meshes hidden; `Renderer.ApplyScenePost` copies the result into `GameSettings` (before `ApplySceneFog`, since volumes scale the fog density).
  - `AoProxyComponent` (`ao-proxy`): `AoProxyShapes.Collect(entities, camera)` fits ellipsoids/capsules (bones via `BonePose.CollectLines`) and keeps the 16 nearest for `DeferredEnvironmentMap.fx` (`AoProxies.fx`), which multiplies the indirect light only.
- `AnimatorComponent` (`animator`) plays clips only in Play. Persisted: the default clip (`ClipSource`/`ClipName`/`Speed`/`Loop`/`InPlace`), `Actions` (a `List<AnimationAction>`: Name, ClipSource (empty = the Animator's), ClipName, Speed, Loop; copied and saved through the generic JSON record), `StartAction` and `FadeTime`.
  - Scripts call `Play(name, fade)`, `CrossFade`, `PlayOnce(name, then)`, `Stop`/`Play()`, `Pause`/`Resume` and `Restart`. They read `CurrentAction`, `Time`, `IsFinished` and the other state properties, and subscribe to `ActionFinished`. `ScriptBehaviour.Animator` is the shortcut.
  - Selection survives the Animator's own start: `PlayModeController.Play` calls `OnStop` then `OnStart` per component in list order, so a script listed above the Animator may call `Play` first. `OnStop` only forgets the selection (`_selected`, action, stopped, one-shot) once the Animator has started (`_started`). An inspector edit in Edit mode also forgets it.
  - A switch while posing keeps the old `AnimationPlayer` as `_fadePlayer`. It is still advanced at its own speed/loop. `Pose` evaluates both and calls `AnimationPlayer.BlendFrom` with a smoothstep weight over the fade. `Player.BoneTransforms` is therefore the blended pose, which is what `RagdollComponent.AnimatedPose` follows.
  - `Stop()` only detaches `TransformMatrix.Skin` (bind pose). `Play()` then re-resolves. Resolution failures are logged once per session or edit (`Report`), because scripts may retry every frame.
  - Changes scripts make to persisted properties during Play are not undone by `PlayModeController.Stop` (it restores transforms only).
- `FoliageWindComponent` (`foliage-wind`) also runs outside Play: `MainSceneLogic.Update` calls the static `UpdateAll` every frame (dt 0 while `GameFlow.Paused`) with `WeatherProfile.Wind(scene environment)`. For each enabled component it advances the tree's wind clock (faster in stronger wind, slower for taller trees), fills its `FoliageWind` state (height = Height or the model's bounds top × Scale.Z, strength = wind m/s / 6 after Wind Scale and Min Wind, a phase hashed from the position) and puts it on `TransformMatrix.Wind`. It also sets `WorldTransform.HasChanged` every frame so culling and the cached shadow maps redraw. A disabled or missing component sets `Wind` back to null (scene loads don't run `OnAdded`, so attaching happens there too). The motion itself is vertex-shader only (section 9.2).
- `LiquidComponent` (`liquid`) also runs outside Play, like Foliage Wind: `MainSceneLogic.Update` calls `UpdateAll` every frame (dt 0 while paused) with the physics gravity. Each enabled component copies its settings into its `LiquidSlosh` (`Engine/Physics/LiquidSlosh.cs`, Half-Life: Alyx's server code: a 10 Hz think turning the change of transform velocity + angular velocity into agitation/foam peaks that decay linearly, and a down direction = gravity − velocity eased 20 % per 0.1 s, all in Source units), advances it from the change of `WorldTransform.World`, and puts it on `TransformMatrix.Liquid` with the colours in `TransformMatrix.LiquidLook`. `LiquidVolume.For(model)` measures the fill tables once per model (cached). Settings are persisted; `Color`s use `ParticleColorJsonConverter`. `OnStart`/`OnStop` settle the liquid. Rendering: section 9.2.
- `FogVolumeComponent` (`fog-volume`) is data for the renderer: `FroxelRenderModule.SetFogVolumes` collects the enabled ones each frame (Edit and Play) and the froxel injection adds their density inside the gameobject's box (unit cube × scale/rotation/position), skipping the scene fog's distance ramp. Scene loads don't run `OnAdded`, so the collection also keeps the box mesh hidden (`TransformMatrix.Hidden`); `OnChanged`/`OnRemoved` show it again when disabled or removed.
- `ParticleSystemComponent` (`particles`) is the one component that also runs outside Play: `MainSceneLogic.Update` calls `UpdatePreviews` in Edit mode, and `PlayModeController.Stop` (`OnStop`) clears the preview so Play restarts from scratch. Its settings are nested module objects (`Main`, `Emission`, `Shape`, ...) with property-level JSON converters for `Vector3`/`Color` (components serialise with default options, which skip XNA fields). The simulation is `Engine/Particles/ParticleSimulation.cs` (CPU, GPU-free); it hides the gameobject's marker mesh during Play through `TransformMatrix.Hidden`. With the `Volumetric` module on, each step ends by updating a `ParticleVolume` (`Engine/Particles/ParticleVolume.cs`): a density grid splatted from the particles, or (Source Voxels) a Counter-Strike 2 style flood fill from the emitter that `ParticleStepContext.RaycastStatic` (`ScenePhysics.RaycastStatic`) keeps out of Static colliders, with one sun ray per voxel (`SunDirection`). Scripts carve bullet tunnels/blast holes (`CarveTunnel`/`CarveHole`, static `CarveTunnels`/`CarveHoles`) and query `Opacity`/`VolumeOpacity`; `NpcComponent.CanSee` refuses sight through smoke above `SmokeBlocksSight`. With the `Lights` module on, each step also refreshes the simulation's `ParticleLights` (`Engine/Particles/ParticleLights.cs`): engine-owned `PointLight`s (one System light at the glow-weighted centre, or Per Particle lights on the brightest carriers) that never enter `Scene.PointLights`; the renderer gathers them each frame with `ParticleSystemComponent.CollectLights`.
- The main camera has its own `Components` list, which accepts only `ScriptBehaviourComponent` (`Camera.SupportsComponent`). Its hooks receive `owner == null`.

### 7.3 `PlayModeController` (`Engine/Logic/PlayMode.cs`)

**`Play(continuing)`**

1. `CaptureSnapshot()` stores, per ID, `Position`, `RotationMatrix` and `Scale` for entities, decals and lights. It also stores `Environment.Clone()` and the main camera pose. **This is not serialisation:** component fields and spawned objects are not captured.
2. It saves `GameSettings.e_enableeditor` and then sets it to `false`. That one flag switches off `EditorLogic.Update` and switches on physics simulation.
3. `Mode = Play`.
4. For each entity (except the `continuing` ones), for each component: `OnStop()`, then `OnStart(entity)` if both entity and component are enabled. Main camera components get `OnStart(null)`.
5. Raises `ModeChanged`.

**`UpdateScripts(gameTime)`** runs only in Play.
- If `GameFlow.Paused`, only `ScriptBehaviourComponent`s whose script has `UpdateWhilePaused` run.
- Otherwise, per component: `OnUpdate(e, gameTime)` if enabled, else `OnStop()`.
- Exceptions are caught and logged.

**`Stop()`**

1. `OnStop()` on every component.
2. Restores the snapshot by ID, then the saved render feature values (`RenderFeatures.RestoreSaved`) and the project's input actions (`InputActions.Load`: script rebinds and disabled actions are undone).
3. Clears `RuntimePersistent`.
4. Restores `e_enableeditor`.
5. `Mode = Edit`, then `ModeChanged`.

`ScriptBehaviour.Shutdown` removes runtime-spawned objects during `OnStop`.

### 7.4 Scripts

```
ScriptBehaviourComponent  (component "script-behaviour"; persisted: ScriptId, Fields{name → JsonElement})
  OnStart  → OnStop(); EnsureStarted(owner)
  EnsureStarted:
     ScriptRegistry.Find(ScriptId).Create()          ← new T(), hard-registered list in ScriptRegistry
     ScriptFields.Apply(instance, Fields)            ← reflection: public / [SerializeField] fields, before Start
     instance.Attach(owner, HostCamera)
     Run(s => s.Start())                             ← sets ScriptBehaviour.Running around the call
  OnUpdate → OnChanged (stop if disabled or ScriptId changed) → EnsureStarted (handles attach mid-Play)
           → ApplyFieldEdits() (inspector edits during Play) → instance.Tick(dt):
                Time += (GameFlow.Paused ? 0 : dt); FrameCount++; Update();
                AdvanceTiming(): ScriptTimer.Advance (timers/tweens), then ScriptCoroutine.Step
  Notify(hook, call)  ← entry point for physics, interaction, NPC and OnSceneLoaded events
  OnStop   → instance.Shutdown(): Stop(), CancelAllTimers(), StopAllCoroutines(), StopAllSounds(), DestroyAllSpawned()
  An exception sets _faulted; a faulted script stops ticking until it is restarted.
```

- **A new script needs two steps:** a class in `Content/Scripts/` *and* a line in the `ScriptRegistry` static constructor.
- `Spawn(modelKey, pos)` → `MainSceneLogic.SpawnRuntimeEntity` → `Assets.FindModel`, flagged `IsRuntimeSpawned`, never saved. `Destroy(entity)` only removes objects the same script spawned (through `EditorDelete`).
- Script transform helpers use Z-up conventions: Forward = local +Y, Up = local +Z.
- Timers, tweens and coroutines (`Engine/Scripting/ScriptBehaviour.Timing.cs`, `ScriptTimer.cs`, `ScriptCoroutine.cs`, `Ease.cs`) are per script instance, with no global scheduler. They advance only inside `Tick`, after `Update`, so they follow the script's own update rules: they get no frames while paused unless the script has `UpdateWhilePaused`, and even then game-time ones get a delta of 0 (only `realtime` ones count). A callback that throws escapes `Tick`, so `ScriptBehaviourComponent` marks the script faulted, the same as a throwing `Update`. Timers and coroutines started during the pass run from the next frame. `StartCoroutine` runs the first step at once, inside the caller (`Start`, `Update` or a hook). `ScriptMath`/`ScriptRandom` are static helpers with no engine state; `ScriptRandom` is one shared `System.Random`, game thread only.

### 7.5 Scene loads during Play (`GameFlow` → `SceneLoadOperation` → `ActivateLoadedScene`)

Every load is a `SceneLoadOperation` (`Engine/Logic/SceneLoadOperation.cs`) in `GameFlow.CurrentLoad`; one at a time, a new request cancels one that hasn't swapped (or ends one that is finishing). `GameFlow.UpdateLoad(assets, ActivateLoadedScene)` runs at the start of each `MainSceneLogic.Update` and advances it:

- **Blocking** (`LoadScene`, `ReloadScene`, `LoadNextScene`): the first tick does `SceneSerialization.ReadFile` + every `SceneBuilder.Step` + the swap. Same frame as before the async loader existed.
- **Async** (`LoadSceneAsync`): the constructor starts `Task.Run(ReadFile)` (worker thread: streams the `.obsc` and `.probes` with byte progress and parses the `SceneDocument`; it touches no engine object). Once the task completes, a tick creates the `SceneBuilder` and each tick runs `Step()` until `GameFlow.LoadBudgetMs` is used (one gameobject/light/decal per step, then a final step for camera, environment, baked probes and ID reseed). The built scene then waits (`WaitingForActivation`) at least one tick, longer while `AllowSceneActivation` is false or the loading screen isn't fully opaque (`LoadingScreen.IsCovering`).
- **Swap** (`ActivateLoadedScene`, both kinds):
  1. Unpause.
  2. If playing:
     - In Anvil, remember `_editScene` (the scene being edited) the first time.
     - If carrying, `_persistent.TakeFrom(ActiveScene)` removes the persistent objects first and unregisters them from the mesh library.
     - `PlayMode.Stop()` stops the old scene and restores its transforms. `_switchingScene` keeps `OnPlayModeChanged` from treating this as a real Stop.
     - `_persistent.PrepareArrival(loaded)` drops the new scene's own copies of carried objects and re-IDs any collisions.
  3. `SceneManager.SetActiveScene(loaded)` (section 6.2).
  4. If playing: `_persistent.Arrive`, register the carried objects in the library, `PlayMode.Play(carried)` (carried objects keep running instead of restarting), then `NotifySceneLoaded` (`ScriptBehaviour.OnSceneLoaded`).
- **Finishing**: `FinishFrames` (3) more ticks, so the new scene's first frames (physics bodies, shadow/environment maps, its UI's first layout) happen under the loading screen. Then `Done`: `GameFlow.LastLoad` and `SceneLoaded`.
- Each tick measures the previous whole frame with a `Stopwatch` (`LongestFrameMs`), plus `ReadMs`, `BuildMs`, `ActivationMs`. Measured in the real engine on the 1,550-object arena: read 4-6 ms (worker), build 3 ms over many frames, swap 19-42 ms; the worst frame is the one after the swap, in `GameUI.Update` (the new scene's HUD first layout, 70-90 ms) and the first physics step (BEPU creating the bodies, 190 ms the first time, 22 ms after).
- Cancel (`Cancel()`, `CancelLoad()`, a newer load, Stop, or an editor Open Scene through `OnSceneChanged` with `_switchingScene` false → `GameFlow.AbortLoad`) is only possible before the swap; the half-built scene is dropped (nothing was registered yet).
- `LoadingScreen` (`Engine/Logic/LoadingScreen.cs`) opens `GameFlow.LoadingScreenDocument` with `GameUI.OpenOverlay` (overlay layers draw after script layers and `CloseAll` skips them), fades in over 0.15 s, fills the ids from the operation each tick, keeps a failed load on screen for 2.5 s and fades out when the load ends. Without a graphics device (tests) it doesn't open and loads don't wait for it.
- `ScriptCoroutine` accepts a `SceneLoadOperation` as a yield (waits until it is ready or ended).

On Anvil's Stop, `OnPlayModeChanged(Edit)` closes the script UI, unpauses, cancels any load in progress and hides the loading screen (`GameFlow.AbortLoad`), raises `PlayStopped`, sends carried objects back home, and swaps back to `_editScene`. Before that it calls `VoxelSystem.OnPlayStopped()`: a voxel world that scripts edited (`EditCount > 0`) is regenerated on the next update. The environment snapshot restore also undoes voxel setting changes, which regenerates the world too.

---

## 8. Physics (`Engine/Physics/`)

### 8.1 `PhysicsSystem`: the BEPU v2 wrapper

- `Simulation.Create(BufferPool, NarrowPhaseCallbacks, PoseIntegratorCallbacks(gravity), SolveDescription(8, 1))`, single-threaded.
- Contact springs are `SpringSettings(30, 1)`, friction 1, maximum recovery speed 2. Damping is 0.03 per substep.
- **Shapes:**
  - Static non-trigger bodies use a triangle `Mesh` with the scale baked in.
  - Dynamic bodies and static triggers use a `ConvexHull` centred on the centre of mass. It falls back to a `Box` when the input is degenerate (fewer than 4 or more than 20000 points, flat, or a non-finite inertia).
  - Freeze rotation is done by zeroing the inverse inertia.
- **Contacts:**
  - `AllowContactGeneration` requires at least one dynamic body and passes the collision-group test. Ragdoll parts of the same object skip each other unless the pair is in `IgnoredPairs`.
  - `ConfigureContactManifold` records the contact into `ContactRecorder` (the deepest contact per pair, plus an impact speed). It returns `!trigger`, so triggers report contacts but get no constraint.

### 8.2 `ScenePhysics.Update(entities, dt, simulate, time)`

It is called after `ScreenManager.Update`. `simulate = !e_enableeditor && p_physics`, so physics only steps in Play. The update is skipped entirely while `GameFlow.Paused` in Play.

```
for each entity:
   Reconcile(e)   desired = None if no model, Role == Water, a limp ragdoll, a cracked glass pane or a broken Breakable; else e.PhysicsType
                  rebuild (Detach + Attach) when type, scale, trigger or collider stretch changes,
                  or (Dynamic) when mass or freeze-rotation changes
   sync direction:
     editing or Static, and moved      → PushPose(resetVelocity: true)    entity → body
     simulating Dynamic moved by code  → PushPose(resetVelocity: false)   (Position != SyncedPosition)
destroy bodies (and ragdolls, glass panes, breakables) of entities that were not visited
glass.BeginFrame, fracture.BeginFrame (clear last frame's moved shard/piece transforms)
if !simulate: clear touches and the in-water set; return
step = min(dt, 1/20)
ApplyBuoyancy (Water role) → ragdoll.BeforeStep → Simulation.Timestep(step)
PullPose for Dynamic bodies         body → entity (setters run only on change, so sleeping bodies stay clean)
ragdoll.AfterStep → glass.AfterStep → fracture.AfterStep (debris/piece poses, lifetime, NaN guard) → UpdateTouches
→ glass.ApplyPending → fracture.ApplyPending (contact hits break panes and breakables) → DispatchContactEvents
UpdateWaterContacts → DispatchWaterEvents   (WaterEntered/WaterExited events, then OnWaterEnter/OnWaterExit)
```

The physics *component* is just data. `ScenePhysics` reads it every frame through `BasicEntity.PhysicsType` and `Mass`. Changing a field in the inspector triggers a rebuild on the next step.

**Statics without a gameobject.** `VoxelSystem` adds one static triangle mesh per voxel chunk straight to `PhysicsSystem` (`AddStaticMesh` / `RemoveStatic`), with world-space vertices. These statics are not in `_staticOwners`:
- raycasts that hit them report `GameObject == null`
- `UpdateTouches` skips contacts with them, so they raise no collision events
- dynamic bodies still collide with them
- the triangles are one-sided (front = clockwise seen from outside), like model meshes.

**Contact events** are keyed by the `(BasicEntity, BasicEntity)` pair, so they survive body rebuilds:
- A new pair fires Enter, a pair seen again fires Stay, and a missing pair fires Exit unless both sides are resting.
- `Notify` sends `OnCollision*` or `OnTrigger*` to both owners' `ScriptBehaviourComponent`s.
- Water buoyancy reads `WaterWaves`, which must match the swell in `Water.fx`.

**Ragdolls** (`RagdollComponent` → `ScenePhysics.CreateRagdoll` → `Ragdoll`) are built from a `RagdollRig`, cached per model and per `RagdollSkeletonType` (`RagdollRig.For(model, type)`):
- `Build` splits the skin into parts (convex hulls of each main bone's vertices) as before, then works out the facing: `Left` from mirrored `Left/Right`, `_l/_r`, `.L/.R` names (forward = Left × Up); without them, the level direction from the root part to a `head`/`skull` bone; else -Y.
- **Auto** resolves to `Humanoid` when human limb names (`JointFor`: arm/forearm/thigh/leg...) sit on a body taller than 1.2× its length along the facing, to `Quadruped` with exactly four **legs**, else to `Generic`. A leg root is a part pointing within 60° of straight down whose parts below never branch and reach the bottom quarter of the body (so drooping tails don't count), hanging from the root, a branching part or a part that doesn't point down; parts named `tail` never are.
- `AssignJoints` sets each `Part.Joint` and, for hinges, `Part.Flex` (the model-space direction the joint bends the part towards). Humanoid: `JointFor(name)`; elbows flex +Forward, knees -Forward. Quadruped: legs ahead of the middle of all leg tops are front legs; the segment depth below the leg root picks `ForeShoulder/ForeElbow/ForeWrist/Paw` or `HindHip/HindKnee/HindHock/Paw` (a front `scapula/clavicle/shoulder` root is a `Clavicle` cone and shifts the count); elbows and hocks flex +Forward, wrists and knees -Forward; tails (by name, or chains reaching backwards from behind the root) are `Tail` cones; the rest is `Spine`/`Neck`. Generic: no hinges; `Tail`/`Neck`/`Spine` by name, a part on a branching parent is a `Limb` cone, everything else a `Chain` link.
- `Ragdoll.AddJoint` builds a hinge about `RestDirection × Flex`; a zero or parallel `Flex` falls back to an `Other` cone. Limits per kind are in `RagdollJointLimits.For`.

**Breakable glass** (`BreakableGlassComponent` → `ScenePhysics.CreateGlass` → `GlassShatter`, fracture math in `GlassFracture`; Half-Life: Alyx findings in `Docs/Research/BreakableGlass/README.md`):
- The pane is the model's bounds: the thinnest local side is the normal, the other two its plane (`GlassShatter.ToPanel`/`ToWorld`).
- Contacts with the pane's collider or its shards reach `GlassShatter.Hit` from `UpdateTouches` (as for ragdolls); the component queues a Blunt hit above Impact Speed/Momentum. Scripts' `Shatter` queue and apply at once. Queued hits apply after `UpdateTouches` so no static is removed while contacts are read.
- The first hit cracks the pane (`GlassFracture.CrackPane`: Valve's jittered-grid Voronoi), hides the intact mesh (`TransformMatrix.Hidden`) and makes `Reconcile` drop the gameobject's own collider. Panels left in the frame are one mesh part on the pane's matrix plus one `AddOwnedStatic` convex collider each, mapped in `_staticOwners` to the gameobject (rays and contacts report it). Slivers (area/perimeter < 4 mm) get no collider.
- Falling debris: a dynamic convex body each (in their own collision group, so they ignore each other; not in `_bodyOwners`, so no contact events and rays report `GameObject == null`), a mesh part with its own `TransformMatrix` (Id = the gameobject's), removed after Debris Lifetime or when `GlassShatter.MaxDebris` (150) is exceeded scene-wide, or when the solver loses the body.
- After a body breaks through, `PassThrough` gives it back 75 % of its approach speed (it bounced off the static in the step). Stop/Restore/disable remove everything and show the pane again.

**Breakables** (`BreakableComponent` → `ScenePhysics.CreateFracture` → `FractureShatter`, mesh math in `MeshFracture`; see [Breakables.md](Breakables.md)):
- Play start (`Build` → `FractureShatter.Prepare`): `ReadMesh` reads the model's parts on the game thread into one `FractureMesh` in the gameobject's scaled local space (group = part index; the bounding box when there is no readable model), then a `Task` runs `MeshFracture.SurfaceSites` (random surface points pushed up to a fifth of the smallest side inwards) and `MeshFracture.Fracture`: per site, the mesh clipped by the bisector planes to every other site (nearest first), each cut closed by `AddCap` (cut segments welded into loops, outlines/holes by nesting depth, holes bridged, ear clipped; wound like the mesh, whose winding is the sign of its volume). Prepared again if Pieces or the scale change.
- Contacts reach `FractureShatter.Hit` from `UpdateTouches` like glass; the component checks Impact Speed and the reduced mass (its own against immovable things) × speed against Impact Momentum and queues the break, applied after `UpdateTouches`. Scripts break at once.
- `Break` waits for the task if needed, `Detach`es the gameobject's body at once (Reconcile keeps it off while broken), hides the model and spawns a dynamic convex body per piece (≤ 64 hull points, a small box for slivers; in their own collision group, or the followed body's so a head's pieces ignore its ragdoll) with one mesh part per group: model parts with `PartMaterial`, cut faces with an interior material (`InteriorColor`; a see-through surface material is reused). Mass: a Dynamic gameobject's mass shared by volume, else volume × Density. Velocity: the gameobject's (body, or the per-frame position change when it follows something) + out from the hit at Break Force + along the hit. Pieces shrink away over the last 0.5 s of Debris Lifetime; `FractureShatter.MaxPieces` (200) scene-wide.
- Effects: `MainSceneLogic.BreakBursts` (`Engine/Particles/BreakBursts.cs`), engine-owned particle systems like `WaterSplashes` (spray dying on static colliders, mist/dust puff, blood splats placed by `RaycastStatic` below), drawn by the particle pass with the splash droplets (`Renderer.EngineParticles`).
- Follow (`FollowObject`/`FollowBone`): at Play start the offset to the bone's rest pose (`AnimationPlayer` with the editor's `BonePose`) × the followed gameobject's world is kept; each update the gameobject is placed at offset × current bone (the Animator's or the limp Ragdoll's `BoneModel`) × world. On break, `CollapseBone` adds the bone to the followed gameobject's `CollapsedBones` and `RagdollOnBreak` activates its Ragdoll with an impulse along the hit. Stop/Restore remove the pieces, show the model and uncollapse.

**Water contacts** (`UpdateWaterContacts`, after the contact events) track every Dynamic body, with or without Buoyancy:
- The body's collider box (the same one buoyancy samples) is rotated into world space. Its bottom is compared with the wave height (`WaterVolume.SurfaceHeight`) under the collider's centre, in the water volumes `ApplyBuoyancy` collected that step.
- In: the bottom goes below the surface. Out: it rises more than `WaterExitMargin` (5 cm) above it, so bobbing never flickers.
- Each crossing becomes a `WaterContact`: surface point, velocity, speed into or out of the water, the collider's horizontal half-size and the mass. It goes to the `WaterEntered`/`WaterExited` events first, then to the body's scripts (`OnWaterEnter`/`OnWaterExit`).
- `MainSceneLogic.Initialize` connects the events to `WaterSplashes`. `IsInWater(entity)` and `RaycastWater` (rays march the wave surface, which `Raycast` ignores) read the same state.
- Bodies that lose their body or leave the scene drop out quietly. Leaving simulation clears the set.

---

## 9. Renderer (`Engine/Renderer/`)

A deferred renderer. It renders a G-buffer, accumulates lights, composes, then draws forward, water and weather, then TAA, bloom, post-processing and editor overlays.

### 9.1 Modules (`Renderer.Load`, in creation order)

`BloomFilter` → `LightAccumulationModule` (owns `PointLightRenderModule`) → `ShadowMapRenderModule` → `GBufferRenderModule` → `TemporalAntialiasingRenderModule` → `DeferredEnvironmentMapRenderModule` → `DecalRenderModule` → `LightmapRenderModule` → `SubsurfaceScatterRenderModule` (unused in Draw) → `ForwardRenderModule` → `WaterRenderModule` → `WeatherRenderModule` → `GridRenderModule` → `ParticleRenderModule` → `RenderFeatureRenderModule` → `HelperGeometryRenderModule` → `DistanceFieldRenderModule` → `FroxelRenderModule` → `ColorGradingFilter` → `AutoExposureFilter`, plus the LUT texture. `PlanarReflectionRenderModule` is a readonly field that only owns its targets, and it is disposed first.

Source 2 look helpers owned by the `Renderer` ([Source2_Look.md](Source2_Look.md)): `EnvironmentPrefilter` (GGX prefilter of every captured cube + the CPU-integrated split-sum BRDF LUT), `ReflectionProbes`, `AoProxyShapes`, `OcclusionCulling` (`g_occlusionculling`), and the `Fxaa`, `RetinaBurn` and `Editor/DebugView` effects. `ShadowMapRenderModule` owns the local-light `ShadowAtlas` (`Atlas`) that `PointLightRenderModule` and `FroxelRenderModule` sample.

`ForwardRenderModule` also samples that local shadow atlas through `Context.ShadowAtlas`. Each selected light uploads six `LightShadowTiles` plus strength, tile size and PCF radius; `Forward.fx` uses the deferred path's distance encoding, slope bias, kernel weights and cube-face edge traversal. Missing/unrendered tiles are lit, and shadow strength follows the atlas distance fade. Cookies use `ForwardCookieAtlas`: `PrepareCookies` ranks the visible lights and packs distinct selected spot cookies before the G-buffer, so rebuilding cannot discard the composed image or its reconstructed depth. Cookie cells are capped at 512 texels and sampled within their texel centres; duplicate textures share a cell, and rank-only changes reuse the atlas. The forward loop projects each cookie using the spot's world-space right/up axes and cone angle and applies it, together with shadows, to diffuse and specular lighting. Atlas textures and SpriteBatch are module-owned and disposed with it; source cookie textures remain owned by content. `dotnet run --project Tests/Components/Components.csproj -- --forward-graphics` checks actual rendered pixels for local shadows, fade, fallback, cookie projection/rotation, multiple cookies and clear glass.

Modules that draw meshes implement `IRenderModule.Apply(world, view, viewProjection)`. `MeshMaterialLibrary` calls it for each instance.

### 9.2 `MeshMaterialLibrary`: the render registry

The renderer never walks `Scene.BasicEntities` to draw. It walks this three-level registry:

```
MeshMaterialLibrary
└─ MaterialLibrary[]          one MaterialEffect each (+ DistanceSquared, HasChangedThisFrame)
   └─ MeshLibrary[]           one ModelMeshPart each (+ bounding sphere)
      └─ TransformMatrix[]    one per instance (shared with BasicEntity.WorldTransform) + Rendered[]
```

- **Register:**
  - `BasicEntity.RegisterInLibrary` registers the model's material for every mesh part.
  - With an enabled `MaterialComponent`, it instead clones one `MaterialEffect` per part and applies the component to it.
  - **Unregister:** `entity.Dispose(library)` → `DeleteFromRegistry`, which removes the `TransformMatrix` by reference.
  - `RefreshMaterials()` is dispose + register.
  - Adding an entity to `Scene.BasicEntities` without registering it means it never draws. Removing it without disposing leaves a ghost.
  - **Geometry without a gameobject** (voxel chunks, the voxel sea) registers its own `ModelMeshPart`s with `Register(material, part, transform, sphere)` and removes them with `Unregister(part, transform)`. Its `TransformMatrix` is owned by `VoxelSystem`:
    - `Id` is 0, so it can't be picked or outlined.
    - `Scale` must be non-zero, because it scales the culling sphere.
    - Nothing clears `HasChanged` for these transforms (`FrustumCullingFinalizeFrame` only walks entities). The owner sets it for one frame after a rebuild, so shadow maps redraw, and clears it on the next update.
    - `MeshMaterialLibrary.Clear()` on a scene swap drops these registrations too, which is why `VoxelSystem.Reset()` runs first.
- **Batching:** `HasMaterial` only matches when `GameSettings.g_batchbymaterial` is true. The default is false, so each registration gets its own `MaterialLibrary`.
- **`FrustumCulling`** applies dirty transforms, re-tests spheres against the frustum, and sets `Rendered[i]`. The sphere radius is the mesh's radius times the **largest** axis scale of the instance's world matrix (`MeshLibrary.UpdatePositionAndCheckRender`); it used `Scale.X` only, which culled stretched meshes such as 3 × 60 roof slats from the view and the shadow maps while they were on screen. It returns whether anything changed, which shadow passes use to skip redraws. Shadow passes call it with *light* frusta, so the flags are re-culled for the camera afterwards.
- **`Draw(RenderType, viewProjection, …, renderModule)`**: `RenderType` is one of `Opaque, ShadowOmnidirectional, ShadowLinear, Hologram, IdRender, IdOutline, SubsurfaceScattering, Forward, Water, Reflection`. It filters by material:
  - Opaque skips transparent and `ForwardShaded`.
  - Forward draws only `ForwardShaded`, and binds each instance's `TransformMatrix.PlanarReflection` through `ForwardRenderModule.SetPlanarReflection`. It also binds `TransformMatrix.Liquid`/`LiquidLook` (`SetLiquid`). A non-empty liquid instance is drawn twice: first its back faces (`CullClockwise`, `ApplyLiquidFaces(backFaces: true)`: in `Forward.fx` a back pixel below the surface whose view ray crosses the plane inside the container shows the liquid's top there), then the front faces as usual (liquid colour, foam, bubbles and water line below the rippled plane).
  - Reflection draws what Opaque draws, through the forward module, culled per instance by `ReflectionCull` instead of `Rendered[]` (the main view's flags stay untouched), with the winding flipped (`CullClockwise`; `RenderCClockwise` materials the other way).
  - Water draws only `Water`, sorted back to front.
  - Hologram is exclusive.
  - Shadow passes skip `!HasShadow`.

  It sets blend and rasterizer state per pass, calls the module's `SetMaterialSettings` once per material, then calls `Apply` per instance. For skinned or cloth instances, `transform.Skin?.Resolve(...) ?? transform.Cloth?.Resolve(...)` swaps in a per-instance vertex buffer for that draw.
- **Foliage wind:** the G-buffer and shadow modules implement `IFoliageWindModule`; the ID passes use `Shaders.IdRenderEffectWind`. Per material `Draw` calls `FoliageWindParameters.SetMaterial` (`WindLeaf` = 1 for the Foliage type), and per instance `SetInstance(transform.Wind)` before `Apply` (`WindEnabled` off for static meshes, set only when it changes). The shared include `Content/Shaders/Common/FoliageWind.fx` (`FoliageWorldPosition` → `ApplyFoliageWind`) moves the vertex in world space, then the pass projects it with its `ViewProjection` (and `View` for linear depth); `Apply` of each module sets those. Shadow passes and picking have no UVs in their vertex input (except the alpha shadow passes), so they bend and sway but don't flutter.

### 9.3 Materials (`Engine/Recources/MaterialEffect.cs`)

`MaterialEffect : Effect` holds:
- texture slots, each with a `Has*` flag set by its setter: `AlbedoMap`, `RoughnessMap`, `MetallicMap`, `NormalMap`, `DisplacementMap`, `Mask`
- scalars: `DiffuseColor`, `Roughness`, `Metallic`, `EmissiveStrength`, tiling, water and wet parameters
- `Type : MaterialTypes`: `Basic 0, Hologram 1, ProjectHologram 2, Emissive 3, SubsurfaceScattering 4, ForwardShaded 5, Water 6, WetSurface 7, Foliage 8`

Two things depend on the material:

- **Which pass draws it:** decided by `Type` inside `MeshMaterialLibrary.Draw`.
- **Which G-buffer technique:** decided by the `Has*` flags in `GBufferRenderModule.SetMaterialSettings`. Examples are `DrawTextureSpecularNormalMetallic`, `DrawTextureDisplacement` and the `*Mask` variants, down to `DrawBasic`.
  - The material type number is packed into the G-buffer (below).
  - WetSurface writes type 0 and sets `WetSurface` / `WetParams`.
  - Foliage (leaves) draws through the ordinary `*Mask` techniques and writes type 8. `DeferredDirectionalLight.fx` (`IsFoliage`, `FoliageTransmission`) adds sunlight through the leaf's back for those pixels and shadow-tests them even when they face away from the sun. It flutters with the Foliage Wind component (`WindLeaf`).
- **Masked materials cast cut-out shadows from every light:** `ShadowMapRenderModule.SetMaterialSettings` now also runs for `ShadowLinear` and picks `DrawLinearDepthAlpha` for materials with a `Mask` (it used to be omnidirectional only).

### 9.4 `Renderer.Draw`: pass order and render targets

| # | Step | Writes | Reads |
|---|---|---|---|
| 1 | `_environmentSky.Apply` (day/night sun; without the cycle `SetSkyboxSun`: the brightest enabled directional light → `SkySunDirection`/`SkySunColor`/`SkySunStrength` = `EnvironmentSettings.SkyboxSunBrightness`, `SkySunDisc`/`SkySunCone` from `SkyboxSunSize`), `ApplyScenePost(PostProcessVolumes.Evaluate(...))` (scene post blended with Post Process Volumes → `GameSettings` tonemap/exposure/bloom/grading/retina burn), `ApplySceneFog` (scene `Fog*` × `g_FogDensityScale` → `GameSettings.g_Froxel*`, `MaxAutoExposure` → `AutoExposureFilter.SceneMaxExposure`), exposure, `SurfaceWetness` | shader params, post/fog/exposure settings | scene environment, volumes |
| 1b | Particle lights: `_frameLights` = the scene's point lights + `ParticleSystemComponent.CollectLights(entities)` (Lights modules). This list is used for shadow maps, SDF check, env capture, froxels, deferred lights, the forward pass and Lit particles; `DrawIds`/`RenderEditorOverlays` keep the scene's list (`sceneLights`) | `_frameLights` | entities' particle simulations |
| 2 | `CheckRenderChanges` | pushes far clip, shadow filtering and SSR changes into modules | `GameSettings` vs cached copies |
| 2b | `ReflectionProbes.Update`, `LodComponent` selection | probe list, hidden probe boxes | entities |
| 3 | `DrawShadowMaps` | point/spot lights: tiles of the shared `ShadowAtlas` (`HalfSingle`, `g_shadowatlassize`), one per cube face the light needs, sized by on-screen size, kept between frames, at most `g_shadowupdatesperframe` redrawn (`DrawLocalLightShadows`); sets `ShadowTiles[6]`/`ShadowStrength` (distance fade). Directional lights: up to 4 texel-snapped cascades packed 2 × 2 in the light's `Single` RT (`DrawCascades`, `SplitCascades`; `Cascade*` arrays on the light). Masked materials alpha-tested; Foliage Wind meshes swayed; meshes under `g_sizecullthresholdshadow` skipped | `RenderType.ShadowOmnidirectional/ShadowLinear`, light/cascade frusta |
| 4 | SDF transforms (if any light uses SDF shadows) | SDF data | |
| 5 | `DrawCubeMap` if `envSample.NeedsUpdate` | `_renderTargetCubeMap` (`HalfVector4` cube) via `CaptureCube`: each face runs a small deferred pipeline with the render targets temporarily rebuilt at envmap size (`onlyEssentials`); then `EnvironmentPrefilter.Prefilter` → `_prefilteredGlobal` (GGX mips down to 4 × 4, copied into a `TextureCube` with `GetData/SetData` because MonoGame can't render into a cube mip) | |
| 5b | `CaptureReflectionProbe` for `ReflectionProbes.NextCapture` (one per frame) | that probe's prefiltered cube | `CaptureCube(withIndirect)` |
| 6 | `UpdateViewProjection` | `_view`, `_projection`, TAA-jittered `_viewProjection`, unjittered `_staticViewProjection`, `_currentViewToPreviousViewProjection`, frustum corners → SSR/SSAO/TAA/lights | camera |
| 7 | `FrustumCulling` (camera) | `Rendered[]` | |
| 8 | **G-buffer** (`GBufferRenderModule`) | MRT `[Albedo, Normal, Depth, Velocity]` (velocity from `CurrentWorldViewProj`/`PreviousWorldViewProj`, previous worlds kept by `FrustumCullingFinalizeFrame`; `IVelocityModule`). Wet Surface materials get `ApplyWetSurface` (`SceneWetness`/`SceneRipples`); other materials with `RainDrops` get `ApplyRainDrops` (`SceneDrops` = `SurfaceWetness.Drops`: sheen, world-space beads and wall drips) | `RenderType.Opaque` (skips `MeshLibrary.Occluded` instances and meshes under `g_sizecullthreshold`) |
| 8b | `OcclusionCulling.Update` (when `g_occlusionculling`) | occlusion queries on instance boxes; results read next frame into `MeshLibrary.Occluded` | G-buffer depth |
| 9 | Decals (premultiplied-alpha blend; skips disabled ones and `g_drawdecals` off) | Albedo (copied through `DecalOffTarget`) | Depth |
| 9b | **Lightmaps** (`DrawLightmaps` → `LightmapRenderModule`, only when `LightingSystem.Lightmaps` exists and Lighting > Use lightmaps is on): clears `_renderTargetLightmap` to 0 and `_renderTargetShadowMask` to 1. Then it redraws each Lightmap Static gameobject that has a baked lightmap (`LightmapResources.Find`: same model part/vertex counts, LOD 0, not hidden, in the frustum). It uses the gameobject's split lightmap geometry plus its UVs as a second stream (TEXCOORD1), and `clip`s where the G-buffer linear depth differs. It points `PointLightRenderModule.ShadowMask` / `ShadowMaskChannel` and the environment module (`SetLightmaps`) at the targets; without lightmaps it unsets them | `_renderTargetLightmap` (`HalfVector4`: rgb directional irradiance for the G-buffer normal, a = 1 + baked AO, 0 = not lightmapped), `_renderTargetShadowMask` (`Color`: 4 static-light channels) | Depth, Normal, lightmap pages |
| 10 | SSR | `ScreenSpaceEffectReflection` | **previous** TAA buffer (or Composed) |
| 11 | SSAO + bilateral blur | `SSAOEffect` (half res) → `…BlurVertical/Horizontal` → `BlurFinal` | Normal, Depth |
| 12 | Froxel fog (`Renderer.DrawFroxelFog`): history dropped on a scene swap or while `g_FroxelsEnabled` is off; `SetFogVolumes(entities)` (up to 8 enabled `FogVolumeComponent` boxes as world-to-unit-cube matrices; keeps their meshes `Hidden`); `SetProbeVolume` (the baked probes and intensity the environment pass uses); then `RenderFroxelFog`: injection (rgb = scattering source per metre from the sun, the 8 ranked point/spot lights (`RankPointLights`) and the probes' L0 × `g_FroxelIndirectScatter`; a = density × absorption, density = scene density × lerp(Near Density, 1, distance ramp) + Fog Volume density), temporal resolve (reprojected into last frame's grid, neighbourhood-clamped, `g_FroxelHistoryBlend`), seed + 7 prefix-scan passes along Z | injection atlas (reused as a scan target), 2 history atlases (`FroxelMedium`), scan atlas; `FroxelAccumulation` = the prefixes (rgb in-scattering, a optical depth). All 160 × 90·128 `HalfVector4` | lights, shadow maps, entities, probe volume |
| 13 | `LightAccumulationModule.DrawLights` | rebuild hardware depth into `Diffuse`'s D24S8 from linear depth (`ReconstructDepth`), then additive MRT `[Diffuse, Specular, Volume]`. Point and spot lights draw sphere proxies (stencil-culled) with sized sources (`lightSourceRadius/Length/Axis`, representative point), cookies (`CookieTexture`) and atlas tiles (`ShadowTiles`); directional lights draw full-screen with cascades (`CascadeViewProjection` set in view space each frame). All share `EvaluateLightSplit` in `helper.fx` (tinted F0, Lambert × (1 - F)). A light with a lightmap shadow mask channel (`LightingSystem.ShadowMaskChannel`: Baked Shadows on, given a channel at bake time, not moved since) multiplies its light by that channel of `_renderTargetShadowMask` (`BakedShadow` in both shaders), on top of its shadow map. Reflection captures (`CaptureCube`) unset the mask first | G-buffer, shadow atlas, cascades, shadow mask |
| 14 | `DrawEnvironmentMap` | MRT `[Diffuse, Specular, Indirect]` (`_renderTargetEnvironmentBinding`): specular adds into Specular; indirect diffuse goes to `_renderTargetIndirect` (rgb light, a = SSAO × material AO × AO proxies; on lightmapped pixels the lightmap replaces the probes and SSAO/SDF AO, and a = material AO × AO proxies × baked AO). Sky pixels: `SampleSky` × 0.5 into Specular; an image sky gets `AddSkySun` (near-white pixels in a cone around the directional light boosted ×150, a ×400 disc and glare in HDR so bloom flares; main view only, captures and the `Sky` pass never get it) | `_renderTargetLightmap` (`LightmapMap`, the 16th texture slot), prefiltered global cube + BRDF LUT, up to 4 reflection probes (`ProbeCube0..3`, box projection, normalisation), packed probe volumes (`ProbeData`), SSAO, AO proxies, SSR |
| 6b | `_renderFeatureContext` | camera matrices, `CameraPosition`, `FarClip`, G-buffer targets, `Time`/`DeltaTime` for render features | |
| 14b | `DrawPlanarReflections` (Planar Reflection panes, see the components above) | one `PlanarReflectionView` target per pane (at most 2): the opaque scene mirrored about the pane through `Forward.fx` (`ReflectionPass`), then the sky (`PlanarSky`) where the depth is still clear; device state restored afterwards | entities with `PlanarReflectionComponent`, the forward context (lights, cascades, atlas, probes, background sky) |
| 15 | `Compose` | `Composed` (`HalfVector4`) = albedo × (1 - metal) × (Diffuse + Indirect × AO) + Specular; SSAO no longer touches direct light. Fog per pixel = prefix up to its slice + the exact integral over the partial slice in front of it (`SampleFroxelFog`, positive view Z = G-buffer depth × far plane), bilaterally filtered. Sky pixels get the froxel fog up to `g_FroxelSkyShaftDistance` at full strength and the rest scaled by `g_FroxelSkyFogStrength` (keeps shafts visible when facing the sun) | Albedo, Diffuse, Specular, Indirect, `FroxelAccumulation` + `FroxelMedium` (`Common/FroxelFog.fx`) |
| 15b | Render features **After Opaques** (`DrawRenderFeatures`, in place) | `Composed` (via two scratch targets of its format, then copied back; the forward pass rebuilds its depth afterwards) | `RenderFeatures.All` with `Event == AfterOpaques && IsActive` |
| 16 | `DrawForward` | `Composed` (after `ReconstructDepth` into its depth buffer), premultiplied alpha, depth read only | `RenderType.Forward`; `ForwardRenderModule.Context` (`Renderer.ForwardContext`): sun + cascades, up to 40 visible point/spot lights ranked by brightness reaching the camera, shared local shadow atlas, cached spot cookie atlas, probe volume, reflection probes, froxel fog; a pane with a `TransformMatrix.PlanarReflection` uses that image as its specular reflection (premultiplied by coverage, the probes behind it) |
| 17 | Water (`WaterRenderModule`: also splash rings from `WaterSplashes.FillRipples` as `SplashPositions/SplashShapes` arrays, up to 16, and raindrop rings from `SurfaceWetness.Ripples` as `RainRipples`), then particle systems (`ParticleRenderModule`: one instanced quad per particle, systems far to near, no depth buffer; Lit systems take the 4 point lights of `_frameLights` that shine most on their emitter (`SetPointLights`); water splash droplets are passed as `extraSystems` since they have no gameobject; Volumetric systems upload their `ParticleVolume` to a `Texture3D` and raymarch it with the `Volume` technique on a scissored full-screen triangle, premultiplied), then weather (haze, ground mist (`GroundFog` technique, from `SurfaceWetness.GroundFog`/`GroundFogColor`, drawn while it lingers after the weather), particles) | `Composed` | Depth (occlusion + ray end), env cubemap, sun, volume + 32³ noise 3D textures |
| 17b | Render features **After Transparents** (in place) | the current output | |
| 18 | TAA | `TAA_1`/`TAA_2` ping-pong (Catmull-Rom history, contrast-adaptive sharpen `g_taa_sharpness`); with `g_taa_tonemapped`, inverse-tonemapped back into `Composed` | previous TAA buffer, Depth, Velocity (`g_taa_velocity`), reprojection matrix |
| 19 | Bloom | Mip0..Mip5 chain → `_renderTargetBloom`, only what exceeds `g_BloomStart` (soft `g_BloomKnee`) after exposure, × `g_BloomScale` | current output, exposure |
| 20 | Auto exposure | 256² luminance meter → 64-bin histogram (Bright Pixels metering, `g_ExposureMetering`) or log-average chain → 1×1 ping-pong | current output |
| 20a | `UpdateRetinaBurn` (`g_RetinaBurn` > 0) | quarter-res afterimage ping-pong, added in the final pass | current output |
| 20b | Render features **After Bloom** (not in place: the output can be a TAA history target, which must not be changed) | a scratch target becomes `_currentOutput` | |
| 21 | `RenderMode` → `DrawPostProcessing` | `_renderTargetOutput` (curve `g_TonemapCurve`; `Source2` reads `g_FilmicCurve`/`g_FilmicToe` as `FilmicCurve`/`FilmicToe`, exact sRGB, saturation/contrast/filter/vignette, retina burn) → color grading (`CurrentLut()`: two LUTs combined while crossing volumes) → FXAA when TAA is off (`g_fxaa`) → render features **After Post Processing** (not in place) → `_finalImage` → **backbuffer**. Debug modes `IndirectDiffuse`/`AmbientOcclusion`/`ShadowCascades` go through `DrawDebugChannel` (`Editor/DebugView.fx`). `_finalImage` is built once per frame; the second `RenderMode` call only draws it again | `g_rendermode` can show a single buffer instead (F1 cycles) |
| 22 | Editor: `DrawIds`, then the ground grid (`GridRenderModule`, edit mode with `GameSettings.e_showgrid`: one full-screen pass that cuts each view ray with the plane Z = `e_gridheight`, 1/10/100-cell levels faded by pixel density, red X / green Y axes, hidden where the linear depth is nearer), outlines, gizmos, billboards, helper lines, probe debug | ID RT, backbuffer | `RenderType.IdRender/IdOutline`, Depth (grid) |
| 23 | `FrustumCullingFinalizeFrame` | clears `HasChanged` | |

**G-buffer layout** (`Content/Shaders/GBufferSetup/Gbuffer.fx`):

| Target | Format | R | G | B | A |
|---|---|---|---|---|---|
| COLOR0 `_renderTargetAlbedo` | `Color` + D24 | albedo (gamma) | ← | ← | material AO (AO map, 1 without) |
| COLOR1 `_renderTargetNormal` | `HalfVector4` | sphere-map encoded view-space normal | ← | `metalness·0.9 + materialType` (decode: `trunc` = type, `frac/0.9` = metal; Wet Surface = type 7, Foliage = 8) | roughness |
| COLOR2 `_renderTargetDepth` | `Single` | linear depth `viewZ / -FarClip` | | | |
| COLOR3 `_renderTargetVelocity` | `HalfVector2` | screen-space motion since last frame | ← | | |

**Render target ownership:**
- `Renderer.SetUpRenderTargets(w, h, onlyEssentials)` disposes and recreates every screen-sized target, then calls `UpdateRenderMapBindings` to re-point every cached texture parameter.
- Bloom resizes lazily inside its own `Draw`. The froxel atlas has a fixed size.
- Shadow maps: a directional light owns its cascade RT; point/spot lights own only tile coordinates in `ShadowMapRenderModule.Atlas` (freed when they leave the scene or lose their shadows). The env cubemap, the prefiltered cubes (global and per probe), the retina burn ping-pong and the combined LUT are created lazily. `_renderTargetVelocity`, `_renderTargetIndirect`, `_renderTargetFxaa`, `_renderTargetLightmap` and `_renderTargetShadowMask` are screen-sized and live in `SetUpRenderTargets`. The two lightmap targets use `PreserveContents`: they are cleared to different values one at a time, then bound together, and a `DiscardContents` target is cleared again whenever it is bound.
- Lightmap GPU resources (`LightmapResources`: pages, split geometry, per-gameobject UV streams) belong to `LightingSystem`, which rebuilds them when `Scene.BakedLightmaps` changes reference (bake, scene swap).
- If you add a target, add it in both places, or the shaders will keep sampling a disposed texture after a resize.

The froxel fog follows the design in
[`Docs/Research/AlyxVolumetric/README.md`](../Research/AlyxVolumetric/README.md)
(view-Z slices, exact segment transport, prefix scan, partial-slice sampling,
reprojected injection history, baked probe ambient, ranked lights). The standalone
`VolumetricTransportDraft.fx` there stays outside `Content.mgcb`; the production
passes are `Froxel.fx` / `DeferredCompose.fx`. Not adopted: fog on transparent
surfaces drawn after composition, and clipmaps.
- The Source 2 / Half-Life: Alyx look plan ([`Docs/Research/Source2Look/README.md`](../Research/Source2Look/README.md)) is implemented except the HLA reference captures (lightmaps, phase 4.2, are in [Lightmaps.md](Lightmaps.md)); how to use it is in [Source2_Look.md](Source2_Look.md). The table above includes it. The research follow-ups R1–R7 (README "Research follow-ups: findings", evidence in `evidence/followups/`, tool in `re_tool/`) set the defaults: `TonemapCurve.Source2`, `ExposureMetering.Source2` (`AutoExposure.fx` metering 2 = histogram median, needs the histogram pass like Bright Pixels), `BloomThresholdShape.Source2` (`Bloom.fx` `BloomShape`, `GameSettings.g_BloomShape`), HLA normalisation cap in `Reflections.fx` `Normalization` (`g_cubemapnormalizationmax` 32 / `roughnessbegin` 0.1) and `DirectionalLight.SplitLambda` 0.85.
- Froxel history: `FroxelRenderModule` keeps last frame's view/projection/camera position and a hash of the fog settings, and drops the history (`InvalidateHistory`) when they jump; previous matrices are only stored after a frame rendered. `UsedHistory` says whether the last frame blended it.

### 9.5 Editor picking and outlines

- `IdAndOutlineRenderer` redraws the ID buffer only for 400 ms after the mouse last moved (`EditorRender.Update` keeps the timer).
- Each mesh is drawn with `ColorId = GetColorFromId(id)`. Billboards for lights, decals and the environment sample are drawn too, and gizmo arrows as IDs 1/2/3.
- It then reads back **one pixel** under the mouse (`GetData(..., new Rectangle(x, y, 1, 1))`) and returns `HoveredId` in `EditorReceivedData`.
- `EditorLogic.Update` uses it next frame. A click on 1–3 starts gizmo dragging. A click on nothing falls back to a CPU ray pick (`Picking.PickEntity`). Otherwise the object is selected.
- Outlines redraw the selected and hovered IDs into the ID target with `RenderType.IdOutline`, and the result is added onto the backbuffer.
- Gameobjects with a Foliage Wind component are picked and outlined where they are drawn: `IdRender.fx` includes `FoliageWind.fx` and `ApplyShaders` sets its `World` and `ViewProjection` for every ID draw.

### 9.6 Shaders and hot reload

- `Shaders.cs` holds `static readonly` effects and parameters for shared shaders. Missing parameters come back as `null`, so several call sites use `?.`.
- Modules load their own effects with `content.Load<Effect>`.
- `ShaderManager` (DEBUG only, `d_hotreloadshaders`) watches `.fx` timestamps. Only the decal and distance-field modules poll it, and it calls a **hard-coded legacy `mgcb.exe` path**. In practice: rebuild the content to see shader changes.
- **Render features** (user shaders, [Render_Features.md](Render_Features.md)) do hot reload: `RenderFeatures.Update` compares each `.fx` write time with the one it last compiled, and `RenderFeatureCompiler.Compile` runs `mgcb` (`AssetImporter.LocateMgcb`) for that one file into a temp folder, copying the `.xnb` next to the `.fx` only on success. The effect is loaded with a content manager rooted at that folder (`FileContentManager`; MonoGame refuses absolute asset names) and replaces the old one; a failed compile keeps the old one and sets `Status = Error` with the compiler's lines. `RenderFeatureRenderModule` sets the parameters a shader declares by name (cached per feature), and a parameter of the wrong kind (`InvalidCastException`) unloads the feature (`ReportDrawError`) instead of throwing every frame.

### 9.7 Settings and change detection

- `GameSettings` are plain statics. Some properties push into `Shaders.*` in their setters (SSAO, exposure, `g_Linear` → compose technique, SSR technique). `ApplySettings()` re-runs those setters once in `Renderer.Initialize`.
- Values that need resources rebuilt are polled once per frame in `Renderer.CheckRenderChanges`, which compares them against cached copies: far plane, SSR, shadow filtering.

---

## 10. Assets and content (`Engine/Recources/`)

- **`Assets`** is a hard-coded registry. Its public `ModelDefinition` and `Texture2D` fields *are* the built-in keys: `Cube`, `Capsule`, `Cloth`, `Tree`, `PlayerYBot`, `Wolf`, …. `Wolf` (`Content/GameObjects/Wolf/Wolf.fbx`, skinned, one `Idle` clip) is generated by `Source/generate_wolf.py` in headless Blender. `LoadTree` binds `TreeBarkMaterial` and `TreeLeavesMaterial` (Foliage) to the tree's Bark/Leaves mesh parts, so a Tree gameobject without a material draws both (a Material component would override both parts alike). `Assets.Load` fills them in a fixed order. It ends with `ReimportExistingModels`, which scans `<exe>/Content/GameObjects/Models/<key>/<key>.xnb` and registers what it finds in `_dynamicModels` through `RegisterModel` (raising `ModelRegistered`).
- **`ModelDefinition`** wraps the `Model`, its bounds (read from a `<path>.bbox` sidecar or computed) and the signed distance field (`<path>.sdft`). `LoadSkinnedModel` also binds the embedded textures into `MaterialEffect`s and widens the bounding spheres.
- **`FindModel(key)`** looks at the public fields first, then `_dynamicModels`. `Spawn` and the editor use it.
- **`AssetImporter`** (editor import, game thread):
  1. Copies the source model and its textures into `Engine/Content/GameObjects/Models/<key>/`.
  2. Appends entries to `Content.mgcb` under the cross-process mutex `obsidian.mgcb.edit` (`ContentManifest`).
  3. Runs mgcb in a subprocess. On failure it rolls the entries back.
  4. Copies the resulting `.xnb` into `<exe>/Content`.
  5. Registers the model and builds a material from texture-name suffixes.
- **Scene files** (`SceneSerialization`, `.obsc`, JSON, `CurrentVersion = 2`):
  - Entities are stored as `ModelKey`, transform (rotation as a quaternion), `Material` record and `Components` (`ComponentRecord[]`).
  - Model keys are the public `ModelDefinition` fields of `Assets` or a runtime-imported model's registry key (`Assets.FindModel` on load, `BuildReverseLookup` adds `DynamicModels` on save). Texture keys are resolved by **reflection over the public fields of `Assets`**. Decals save a built-in texture this way (`TextureKey`, e.g. `DefaultDecal`), so renaming an `Assets` texture field breaks saved decals; a texture picked in the Inspector is saved as `TexturePath` instead (a path that fails to load falls back to `DefaultDecal` and keeps the path).
  - A legacy entity-level `Physics` record becomes a `PhysicsComponent` on load.
  - Gameobjects go through `SceneSerialization.CaptureEntity`/`RestoreEntity`, which prefab files share.
  - A **prefab instance** also saves `Prefab` (its `.prefab` path) and `PrefabOverrides` (the values that differ from the prefab, worked out by `PrefabUtility.Overrides` at save time). On load, `SceneBuilder.AddEntity` runs `PrefabUtility.Sync`: the prefab file's current record, with the instance's name, transform and overrides on top. That is how prefab edits reach every scene. `"*"` (prefab missing at save) keeps everything the instance had.
  - Baked probes go into a sibling `.probes` file, lightmaps into `.lightmaps` (`LightmapData`, deflate-compressed; `LightingRecord.LightmapDataFile`).
  - Reflection probe cubemaps go into a sibling `.reflections` file (`ReflectionProbeData`, version 1, lossless HalfVector4 faces and mips plus capture boxes), referenced by `SceneDocument.ReflectionDataFile`. Save/Save As includes only current captures of saved components; removed, runtime-spawned and stale captures are omitted. `ReadFile` loads this CPU data on the worker with cancellation; `SceneBuilder.Finish` associates captures with restored component IDs and request counters. Missing/corrupt files log and fall back to capture. GPU upload happens in `ReflectionProbes.Update` on the game thread. Both output and publish copy `.reflections` files.
  - Loading is two halves: `ReadFile` (I/O + JSON + probes, thread-safe, returns a `SceneFile`) and `SceneBuilder` (game thread, one object per `Step`). `LoadFromFile` runs both at once; `SceneLoadOperation` spreads them out.
- **Prefabs** (`Engine/Logic/Prefab.cs`, `PrefabUtility.cs`): `.prefab` files anywhere in Content (default `Content/Prefabs`) holding one `BasicEntityRecord` (`{ Version, GameObject }`, attachment IDs blanked, position at the origin). `Prefab.Load(nameOrPath)` resolves "Crate", "Prefabs/Crate" or a moved file by name (`Resolve`, on-disk casing, remembered per name), reads it through `GameInfo.ResolveContentFile` and caches the parsed record until the file's write time changes. It is written to the source Content (`Prefab.Write`, atomic `.tmp` + move); `Engine.csproj` copies `Content/**/*.prefab` into builds. See [Prefabs.md](Prefabs.md).
  - **Overrides** are JSON paths from diffing the instance's record against the prefab's: `Scale`, `Material/Roughness`, `Components/<type>#<n>` (a component added or removed; matched by type id + index among that type) or `Components/<type>#<n>/<setting>` (only public settable properties; derived get-only ones like `ClampedMass` are skipped). Id, Name, Position and Rotation are always the instance's own. Script fields pointing at the gameobject itself are compared in the prefab's ID space (`WithId` → `RemapGameObjectReferences`).
- **Material assets** (`Engine/Recources/MaterialAsset.cs`): `.mat` files anywhere in Content (default `Content/Materials`) holding `{ Version, Material }`, where `Material` is a `MaterialComponent`'s surface (colour, roughness/metallic/emission, shader type and its water/wet settings, tiling, the six texture slots; `null` slot = the model's own map, `""` = none). `MaterialAsset.CopySurface` copies every public settable property declared on `MaterialComponent` except `MaterialPath` (not `[JsonIgnore]`, not `Enabled`), so a new material setting is part of the asset without further wiring. Lookup/caching/atomic writes work like prefabs (`Resolve`, `Load` cached per write time, `Write` = `.tmp` + move); `Engine.csproj` copies `Content/**/*.mat` into builds.
  - **The link:** `MaterialComponent.MaterialPath`. A linked component keeps a *copy* of the asset's values (saved in the scene as the fallback for a missing file); `SyncedAsset`/`SyncedPath` (runtime only) record which asset version it copied. `MaterialAsset.SyncScene` (every frame in `MainSceneLogic.Update`) re-copies and calls `RefreshMaterials` only when the file's parsed instance changed: at once after a write from this process (`_revision`), else after a poll every 30 frames (outside edits). An unchanged asset never overwrites runtime changes, so a script can still tint a linked gameobject.
  - **Writes back:** only `MaterialAsset.SaveFrom(component)` (Anvil's linked Material Inspector) and `MaterialAsset.Write` (the asset Inspector) write a file. `OnAdded` with a `MaterialPath` (scripts, `Assign`, scene loads) takes the asset's values instead, so adding a component can never overwrite an asset.
  - **Capture** (`MaterialAsset.Capture/CreateFrom`): the component's values (or `MaterialComponent.FromOwner`) with every `null` slot filled from the model's displayed `MaterialEffect` (`entity.Material`, else the first textured part). `TexturePathOf` maps a `Texture2D.Name` (set by the content manager) to its source file, trying `<name>` and `<name minus _0>` (textures a model references are built as `<name>_0`) with each image extension; no texture becomes `""`, an untraceable one stays `null`. `MaterialComponent.ApplyTexture` loads `<path>_0` when `<path>` has no xnb of its own.
- **AI texture generator** (`Tools/TexGen`, Python, not a build dependency; `Docs/Research/AiTextures/PLAN.md`): `python -m texgen generate --prompt ... --seed ... --out <cache>/<id>` writes a PBR set (separate Roughness/Metallic/AO/Height grayscale PNGs, DirectX / Y- normal map by default, `texgen.json`) for the `source2` style. The engine side is `Engine/Recources/TextureGeneration/`:
  - `MaterialGenerationId` (id = SHA-256 prefix of the canonical request; `PipelineVersion` must equal texgen's `PIPELINE_VERSION`), `TexGenSettings` (`Tools/TexGen/texgen.json`), `TexGenProtocol` (stdout JSON lines).
  - `TexGenRunner.GenerateAsync` (off the game thread, kills the process tree on cancel/timeout, returns a valid `cache/<id>` without running Python).
  - `GeneratedMaterialImporter.Import(cacheDir)` (game thread, like `AssetImporter`): copies the maps to `Content/Materials/Generated/<slug>_<id>/`, `ContentManifest.RegisterGeneratedTextures` (no colour key, no premultiplied alpha, mipmaps), mgcb, `.xnb` copy, then a `.mat` through `MaterialAsset.Write`; a failed build rolls the manifest back. Reached through `IEditorBridge.EnqueueImportGeneratedMaterial(cacheDir, done, assignToId)`.
  - Anvil: `GenerateMaterialViewModel` + `Views/GenerateMaterialWindow` (Assets context menu, GameObject menu; `MainWindowViewModel.ShowGenerateMaterialAsync`) run the runner, show thumbnails from the cache folder and import through the bridge. `TexGenRunner.CheckBackendsAsync/ListStylesAsync` fill the backend and family lists.
  - MCP: `materials_generate` (`EditorWriteTools`) starts a job in `EditorAutomationService.StartMaterialGeneration` (texgen, then the bridge import); `editor_get_operation` reports it (`EditorOperationStatus.Material`). See [Generated_Materials.md](Generated_Materials.md).
  - Checking a material: `Content/Scenes/MaterialLab.obsc` (`Tools/TexGen/lab/generate_material_lab.py`) and, for the Source 2 look, `Content/Scenes/Source2MaterialTest.obsc` (`generate_source2_material_test.py`: a wall of stood-up `Cube` slabs under soft neutral light; `lab/compare_source2_look.py` measures the albedo against HLA screenshot crops). `LookDevCapture --material <.mat>` / `--import-generated <cache>` apply it to the `MaterialLab.Target*` gameobjects; `--tiling <t>` then overrides their linked copy's `TilingU/V` (the link only re-copies when the `.mat` file changes), so each 1 m slab repeats the texture once. Built-in models need tangent frames in `Content.mgcb` to take normal maps (`IsoSphere`, `Cube`, `Capsule` have them).
  - Texgen stages that shape the look (pipeline version 4): per-family `albedo_mean`/`albedo_clip`/`max_saturation` in `styles/source2.json` (`Style.albedo_mean` etc., used by `style.fit_dielectric` and `validate.py`), `imageops.make_tileable` for `--input-rgb` photos that fail the seam check (four half-shifted copies, variance-preserving blend), `classic_pbr.soften_shadows` + `limit_slope`.
- **Parallax (`DrawTextureDisplacement` in `Gbuffer.fx`)**: chosen by `GBufferRenderModule` for a non-wet material with albedo + normal + displacement maps. The tangent-space view vector is `mul(WorldToTangentSpace, WorldPos - Camera)` (the rows are the tangent frame); the UV offset is `xy / min(z, -0.25) * -height * 0.01`. Roughness/metallic come only from `WriteBuffers`' component override, so a model's own material with a displacement map uses the scalar values (see `ISSUES.md`).
- **`GameInfo.ResolveContentFile`** prefers the *source* `Engine/Content` copy when it exists, which is the dev case, and falls back to `<exe>/Content`. That is why scenes, UI and audio edits show up without a rebuild during development.

---

## 11. The editor seam (`EditorBridge`)

See [Editor_Architecture.md](Editor_Architecture.md) for the Anvil side in depth. These are the low-level facts that matter from the engine's side.

**UI → engine: a queue**
- Every `Enqueue*` and `Request*` call adds a lambda to `ConcurrentQueue<Action> _pendingOps`.
- `DrainAndPublish()`, at the end of `ScreenManager.Update`, runs every queued lambda on the game thread (each in a try/catch).
- That is the **only** point where Anvil's edits touch engine state.

**Engine → UI: throttled snapshots**
- After draining, the bridge raises `SelectionChanged` every frame if the selection changed.
- Every `PublishEveryNFrames = 6` frames it runs `BuildSnapshot()`, an immutable list of `EditorObjectSnapshot`. The camera comes first (ID -1), then entities, lights and decals. Components are captured as `ComponentRecord` JSON.
- It then raises `SnapshotUpdated`. **All bridge events fire on the game thread.** `MainWindowViewModel` marshals each one with `Dispatcher.UIThread.Post`.

**Reconciling into view models**
- `BridgeReconciler.Apply` merges the snapshot into the existing view models, keyed by engine ID and then by component `(TypeId, InstanceId)`.
- It runs inside `BeginSuppressPush/EndSuppressPush`, so writing a view-model property does not echo back as a mutation.
- While the inspector has focus, transform and light fields of the selected object are frozen (`freezeFields`), so typing isn't overwritten.

**One inspector edit, end to end**

```
Avalonia binding → [ObservableProperty] partial OnXChanged → (SuppressPush? stop)
→ bridge.EnqueueMutate / EnqueueMutateComponent(id, typeId, c => …, instanceId)       [UI thread]
→ next engine Update → DrainAndPublish runs it → component.OnChanged(owner); scene.IsDirty = true   [game thread]
→ ≤ 6 frames later the snapshot carries the value back; SetIfChanged sees no change
```

**Inspector and workspace panels**

- Reference fields use `Controls/ReferenceSlot` (read-only label, empty hint, valid-drop highlight and clear button). `MainWindow` transfers Hierarchy `GameObjectFormat` payloads to script gameobject fields, NPC Target/Patrol Points and particle sub-emitters (only objects with a Particle System). Assets transfers mesh keys to LOD levels, prefab paths to script prefab fields, and registered C# behaviour paths through `ScriptAssetFormat` to Script Behaviour attachments. `ScriptFromAsset` resolves the filename to a unique registered `ScriptType.Name`; helpers, XML/CSS and unregistered scripts are rejected. Hierarchy drags intercept pointer presses before selection, preserving the destination Inspector; an unsuccessful drag/click selects the source afterward. Mutations still use the attachment's `InstanceId` and `EnqueueMutateComponent`. Gameobject script fields retain scene IDs and missing labels; NPC/particle targets retain their existing name-based storage. Patrol drops append names in order; Clear removes the route. Water and WetSurface use normal Shader selection and property editing; the example preset buttons/commands were removed, and water physics requires assigning the Water role explicitly.

- Long component descriptions live in `controls:HelpButton.Description` beside the component or subsection heading in `MainWindow.axaml`, rather than as footer paragraphs. `HelpButton` (`Editor/Anvil/Controls/HelpButton.cs`) uses the existing compact button styling and a wrapped tooltip (360 px maximum width), opens on hover or click/keyboard activation, and dismisses on Escape, lost focus or detachment. Help clicks are handled so they do not bubble into the Expander header. Related notes share a button; particle Shape/Sub Emitters/Lights/Volumetric and the material Shader section keep their own contextual help. Dynamic status/error messages remain in the panel.

- The **Layers** toolbar button and workspace **+** menu open the `Classifications` tab (**Layers, Tags & Roles**) through `OpenWorkspaceTabCommand`. `ClassificationsView` binds `ClassificationsViewModel` and its three groups. Existing built-in layers (`Default`, `UI`, `IgnoreRaycast`), tags (`Untagged`, `Player`, `Enemy`, `static`, `renderable`) and all `GameObjectRole` values (`Default`, `Water`) are engine-owned, read-only rows with no rename/remove actions. `ClassificationCatalog` also rejects their mutation through the bridge or JSON flags.
- `ClassificationCatalog.Project` loads `Content/System/Classifications.json` at startup. It publishes immutable definition snapshots and atomically persists custom definitions on `EnqueueEditClassification`; validation rejects blank, duplicate, control-character and overlong names. The file is copied to output/publish and cleared by project reset. Custom entries have stable IDs: `BasicEntity.TagId`, `LayerId`, `RoleId` persist through scenes, prefabs and clones, while `Tag`, `Layer`, `RoleName` resolve current names. Existing `Role` enum callers retain Water behavior; custom roles report built-in Default. Renames preserve assignments without rewriting assets; deleted/missing IDs resolve to their category default and normalize on the next save.
- Inspector choices come from `IEditorBridge.Classifications` through `SceneObjectViewModel.AvailableTags/AvailableLayers/AvailableRoles`, with assignments queued through `EnqueueSetClassification`. Snapshots carry the resolved names and existing role enum. `BridgeReconciler` refreshes choices under suppression before applying names, so removed selections cannot enqueue accidental edits. The management groups preserve draft names across snapshot refreshes, surface validation/save errors and marshal completion callbacks to the UI thread. See [Layers_Tags_and_Roles.md](Layers_Tags_and_Roles.md); `ClassificationChecks` covers protected defaults, manager commands, assignments, rename/delete, scene/prefab/clone persistence, restart and failed writes.

- The right-hand Inspector uses `MainWindowViewModel.InspectorView` for object `Selection` or `MaterialAsset`. Settings live in workspace tabs selected through `SelectedWorkspaceTab`: `PostProcessing`, `Lighting`, `Environment`, `World`, `RenderPipeline`, `Input` and `Steam`. Their former toolbar/header/Window-menu shortcuts call `SetInspectorViewCommand`, which forwards those panel kinds to `OpenWorkspaceTabCommand` without changing the object Inspector.
- `RenderPipeline` (`RenderPipelineViewModel`) lists `IEditorBridge.GetRenderPipeline()` (the published `RenderFeatures.Describe()`: built-in passes, injection points, feature copies) and rebuilds on `RenderPipelineChanged`, keeping its row objects while the shape is unchanged so the selection survives. Edits go through `EnqueueCreateRenderFeature/EnqueueMutateRenderFeature/EnqueueMoveRenderFeature/EnqueueRecompileRenderFeature/EnqueueDeleteRenderFeature`; the engine saves `RenderFeatures.json` itself. Opening the view calls `EnqueueRefreshRenderPipeline`, since render setting toggles don't notify.
- `Environment` (sky, fog, weather, wind) and `World` (the voxel world) both bind to `MainWindowViewModel.Environment` (`EnvironmentViewModel`), because the voxel settings are part of `EnvironmentSettings` and go through the same `IEditorBridge.EnqueueMutateEnvironment` call.

**Adding objects (GameObject menu / Hierarchy +)**
- The title bar's GameObject menu and the Hierarchy "+" menu (`MainWindowViewModel.AddableObjects`) run the same `[RelayCommand]`s, which enqueue one bridge op at `IEditorBridge.SpawnPoint` (camera position + 5 m forward, refreshed while draining): `EnqueueAddBasicEntity` (Cube, Sphere, Particle System marker, Tree), `EnqueueAddPointLight/SpotLight/DirectionalLight` and `EnqueueAddDecal`.
- `EnqueueAddDecal(position, scale)` → `MainSceneLogic.EditorAddDecal` adds an enabled `Decal` with `Assets.DefaultDecal` (falls back to `ErrorTexture`; a decal whose texture isn't an `Assets` field is skipped when saving). The box spans ±`Scale` around its position and projects down its local -Z; the snapshot shows it as kind `Decal` (Name, Enabled, transform; no components or role).
- Inspector > Decal > Texture (`SceneObjectViewModel.DecalTexture`, Browse or a drop from Assets, Reset) → `IEditorBridge.EnqueueSetDecalTexture(id, path)` → `Decal.LoadTexture` (Content-relative source path through `Globals.content`, like Material textures; must already be built) sets `Decal.Texture` + `Decal.TexturePath` and dirties the scene. A texture that can't load leaves the decal unchanged; null/empty goes back to `Assets.DefaultDecal`. `EditorObjectSnapshot.DecalTexture` carries the path back.

**Armature and bones (Hierarchy children of a skinned gameobject)**
- `BuildSnapshot` adds `EditorObjectSnapshot.Bones` (`BoneSnapshot[]`) to each gameobject whose model has `SkinningData`: the Armature first (`Index` -1, `ParentIndex` -2), then every bone in skeleton order with its parent index, world joint position, pose rotation and `IsPosed`. The flat list keeps one entry per scene object.
- `BridgeReconciler.ApplyBones` nests them as `SceneObjectViewModel.Children` (kind `EditorObjectKind.Bone`, type `Armature`/`Bone`), rebuilding the tree only when the set of bone IDs changes.
- `RequestSelect`/`EnqueueMutate` with a bone ID resolve through `LookupById` -> `BonePose.Proxy(owner, bone)`, a cached `BoneObject : TransformableObject`. The gizmo writes its `RotationMatrix` (world): the setter turns the pose rotation by `value * inverse(current)`. The Inspector writes `LocalRotation` (the pose rotation itself). `Position`/`Scale`/`Name`/`IsEnabled` setters are no-ops. A selected bone whose owner left the scene list is re-resolved (or dropped) in `DrainAndPublish`.
- `BonePose` applies its rotations in `AnimationPlayer.Evaluate` (`Overrides`: `local[i] = R * local[i]`, the Armature's after the root transform). `BonePose.UpdateAll` (in `MainSceneLogic.Update`, Edit and Play) owns a `SkinnedMeshInstance` per posed gameobject, used only while `WorldTransform.Skin` is null or its own; the Animator and Ragdoll take over by setting theirs. The Animator's players also carry `Overrides` and re-pose when `BonePose.Version` changes. `PlayModeController` snapshots and restores the poses.
- `Renderer.RenderEditorOverlays` draws the selected skinned gameobject's armature with `HelperGeometryManager` lines (`BonePose.CollectLines`).

**Prefabs (Assets panel, Hierarchy, Inspector prefab bar)**
- `EnqueueCreatePrefab(id, folder, done)` → `PrefabUtility.CreatePrefab` writes `<folder>/<name>.prefab` (`Prefab.UniquePath`: `_2`, `_3` …, never overwrites) and sets `BasicEntity.PrefabPath`. Anvil: Hierarchy drag onto Assets (`GameObjectFormat` → `AssetsPanel_Drop` → `MainWindowViewModel.CreatePrefabIn(obj, FolderFor(node))`), the Hierarchy context menu, the Inspector button, GameObject > Create Prefab from Selection.
- `EnqueueInstantiatePrefab(nameOrPath, SpawnPoint, done)` → `MainSceneLogic.InstantiatePrefab` (fresh attachment IDs, self references remapped, named after the prefab, registered in the library, scene dirty) and selects it. Anvil: double-click, drag into the Hierarchy (`PrefabAssetFormat`), Assets context menu > Place in Scene. Scripts reach the same method with `runtime: true` through `ScriptBehaviour.Instantiate`.
- `EnqueueApplyPrefab` / `EnqueueRevertPrefab` / `EnqueueUnpackPrefab` are Edit mode only. Apply measures every other instance's overrides against the *old* prefab, writes the file, then `Sync`s them. Instances whose record changed are rebuilt by `MainSceneLogic.ReplaceGameObject`: a new `BasicEntity` with the same ID, list slot and runtime flags, the old one stopped, disposed and detached like a delete. `KeepSelectionOnReplaced` reselects a rebuilt selection.
- `BuildSnapshot` sends `PrefabPath` for every gameobject, but `PrefabOverrides` (or `PrefabMissing`) only for the **selected** instance, since working them out costs a capture and a JSON diff. `SceneObjectViewModel.PrefabStatus/PrefabOverridesTip` label them with `PrefabUtility.Describe`.

**Material assets (Assets panel, Hierarchy, Inspector)** — no bridge methods of their own
- Assets context menu > **Create Material** → `MainWindowViewModel.CreateMaterialAsset(node)` → `MaterialAsset.Create(FolderFor(node), "New Material")` on the UI thread (plain file I/O), then opens it.
- A left click on a `.mat` node (`AssetsTree_PointerPressed`) opens it in the Inspector (`OpenMaterialAsset` → `MaterialAssetViewModel`, `InspectorView = "MaterialAsset"`) and starts a `MaterialAssetFormat` drag. `MaterialAssetViewModel` derives `MaterialInfo` and overrides `Push`: edits go into a local `MaterialComponent` and `MaterialAsset.Write` 200 ms after they settle; the engine's `SyncScene` picks the file up. `RefreshAssetTree` (content watcher) calls `ReloadIfChanged` and closes it when the file is gone. Selecting a gameobject returns to the Selection view.
- **Assign** (`AssignMaterialAsset(obj, path)`): drop on a Hierarchy row (`Hierarchy_Drop`, else the selection), double-click, Assets context menu, the asset Inspector button. It enqueues `EnqueueAddComponent(material)` when the gameobject has none, then `EnqueueMutateComponent(c => c.Link(path))`; both mark the scene dirty.
- The Material component's **Asset** slot (`MaterialInfo.MaterialPath`, `MaterialAssetPicker_Click` menu of `MaterialAsset.List()`, `MaterialAsset_Drop`, Edit Asset, Unlink). `MaterialInfo.Push` (virtual `ComponentViewModel.Push`) wraps every edit of a **linked** component with `MaterialAsset.SaveFrom`, so the bridge op writes the asset on the game thread. Link/Unlink go through `base.Push` (no save).
- **Create Material from Selection** (GameObject menu, Inspector "Save as Material Asset") → `EnqueueMutate` runs `MaterialAsset.Capture` + `Create` on the game thread, then posts `AssignMaterialAsset`.
- The editor body of the Material component is the `MaterialSurfaceTemplate` resource in `MainWindow.axaml`, shared by the component template and the asset Inspector.

**Delete Project (Edit > Project Settings)**
- `ProjectSettingsWindow` only collects the confirmation (the project name typed). `MainWindowViewModel.ResetProject` then calls `IEditorBridge.EnqueueResetProject(MoveToRecycleBin, done)`, refused during Play because Stop would swap the old scene back in.
- On the game thread: `SceneManager.NewScene("Main")` (so no gameobject still uses an imported model) → `Assets.UnregisterModel` for every `DynamicModels` key → `ProjectReset.Run` → `SceneList.Load()` → `RenderFeatures.Load(null)` (its JSON is in `DeletedSettings` and the `RenderFeatures` folder isn't kept) → `AddDefaultSun()` → `SaveScene(Scenes/Main.obsc)`.
- `ProjectReset.Run` removes each top-most item outside `KeptPaths` through the callback (Recycle Bin), its Content.mgcb entries (`ContentManifest.Unregister(rel, manifestPath)`) and its built copies under `Content/bin/Windows`, `Content/bin/Windows/Content`, the running executable's `Content` and `Engine/bin/*/*/Content` (never a folder that holds one of those roots). Without the built copies, `Assets.ReimportExistingModels` would bring imported models back at the next boot.
- Settings are **written** to defaults, not deleted: `GameInfo.ResolveContentFile` would otherwise fall back to the stale copy next to the executable.

**Hosting facts**
- `MonoGameHost` sets `Environment.CurrentDirectory` to the Engine assembly folder before it creates the engine, so `Content/` resolves.
- It reparents the engine window under a `STATIC` container window as `WS_CHILD`, with no caption or border and `WS_EX_TOOLWINDOW`.
- When hosted, the engine ignores window activation (`active = _isActive || IsHostedByEditor`) and skips `GameInfo`.

**Input from Anvil**
- **Keyboard:** Avalonia key events are mapped to XNA `Keys` by `Anvil.Services.KeyMap.ToXna` (the whole keyboard: letters, digits, F1–F24, numpad, punctuation, modifiers, arrows, navigation keys). They go into `EditorBridge._hostKeysDown` through `SetHostKeyState`.
- **Mouse:** not forwarded. The engine reads `Mouse.GetState()` and `Input.FilterHostMouse` masks out presses that start outside the viewport or happen during an asset drag.

---

## 12. Input, script UI, audio

- **`Input`** (engine-level, raw):
  - `Update` copies the current state to "last", then reads `Keyboard.GetState()` and the filtered mouse.
  - It drives the editor camera, or the legacy game-camera controls when the camera has no active script.
  - `IsKeyDown` merges the Anvil keys. **`WasKeyPressed` / `WasKeyReleased` read only the native keyboard.**
- **`GameInput`** (script-level):
  - Rebuilds its key set each frame through `Input.IsKeyDown`, so its edges *do* include Anvil keys.
  - Adds XInput gamepads, `AnyKey`/`LastDevice`, menu directions with repeat, and the filtered mouse (`MouseDelta` is zero in Anvil while the cursor is over a panel).
  - Ends by updating the input actions.
- **`InputActions`** (script-level, named; [Input_Actions.md](Input_Actions.md)):
  - The project list is `Content/System/InputActions.json` (`InputActionList`; the defaults when it is missing), edited in Anvil's Input Actions window (`InputActionsViewModel`, a modal dialog like Game Settings). Save writes the file and calls `InputActions.RequestReload()`; the next `InputActions.Update` reloads it, also during Play.
  - Each `InputAction` resolves its binding paths once (`InputControls.Find`) and each frame keeps the strongest binding's value (modifiers gate a binding; Scale, Per Second and Invert apply per binding). Pressed at 0.5, released below 0.375. All actions read released while the debug console is open.
  - `Load` keeps `InputAction` instances (scripts may store them) and resets bindings and `Enabled`. `PlayModeController.Stop` calls it, so runtime rebinds last one Play session unless a script saves them (`SaveBindings`, SaveData).
  - Scripts should use the actions (`WasActionPressed`, `GetVector`, ...) for gameplay and `GameInput` for menus and raw keys.
- **`GameUI`** (static):
  - `Open("UI/Name")` creates a `Vista.UIManager` layer, loading `.xml` + `.css` through `ResolveContentFile`. It records the owning script's gameobject.
  - `Update` hot-reloads changed files every 0.5 s, then lays out each layer, scaled from a 1080-high canvas.
  - `Draw` draws the layers in the order they were opened, then the engine overlays (`OpenOverlay`: the loading screen), which `CloseAll` leaves open.
  - Vista has **no input handling**. Scripts poll `GameInput` and hit-test with `GameUI.ElementAt` / `UIManager.ElementAt`.
- **Audio** (`AudioManager`, FMOD Core through FmodForFoxes):
  - `SystemUpdate` runs every frame. `UpdateListener(camera)` moves the listener and the 3D emitters.
  - Files are loose (`Audio/<name>.wav`). In Anvil, changed source files are copied to `<exe>/Content` before loading.
  - Buses (`AudioBus`): `Initialize` creates FMOD channel groups `Music` and `Effects` under the master group. `PlayMusic` plays on Music; `PlaySound`/`PlaySound3D` take a `bus` (Effects by default; `Master` = no group). `MasterVolume` sets the master group, `MusicVolume`/`EffectsVolume` (or `SetVolume(bus, v)`) their group. The volumes are fields, applied again after FMOD starts.
  - `Audio.Instance` is set at the start of `Initialize`, before FMOD starts, so volume settings work without FMOD. If FMOD fails to start, every play call becomes a no-op.
  - Menus: `MenuSettings.Audio()` (Master/Music/Sound effects rows) heads the Settings page of `MainMenuScript` and `PauseMenuScript`. Both settings lists scroll (8 visible rows, `#settings-scroll-up/down`). See [Audio_Volume.md](Audio_Volume.md).

---

## 13. Sharp edges (checked against the source)

1. **Apply/Revert replace prefab instances.** A rebuilt instance is a new `BasicEntity` under the same ID, so a reference to the old object (outside the scene list, e.g. held by editor code) goes stale. Apply/Revert are Edit-mode only, so running scripts never see it. Look objects up by ID after a prefab operation.
2. **Two keyboard APIs.** `Input.WasKeyPressed` ignores Anvil-forwarded keys, but `GameInput` includes them. In-engine editor hotkeys (Delete, Insert, Ctrl+C, R/T/Z, F1, Space) only work when the engine window itself has keyboard focus, even though Anvil now forwards every key (`KeyMap`).
3. **`EditorLogic` has two delete paths.** The Delete key path removes the object from its list and disposes it from the mesh library. It does **not** call component `OnStop` or `ScenePhysics.Detach`. The bridge path (`MainSceneLogic.EditorDelete`) does both.
4. **Debug-entity picking indexes the wrong list.** In `EditorLogic.Update`, the `debugEntities` loop reads `dirLights[index]`.
5. **Play does not snapshot component state or spawned objects.** Stop restores only transforms, the environment and the camera pose. Spawned objects are removed by the scripts' `Shutdown`.
6. **`World` updates depend on CPU culling** (section 7.1).
7. **`RenderMode` runs twice per frame** (before and after `DrawIds`). Editor overlays then draw on top of the second pass. The post-processed image is built only on the first call (`_finalImage`), so After Post Processing render features run once per frame.
8. **`g_batchbymaterial` is false by default**, so there is no material batching. `MeshMaterialLibrary.Draw` also iterates `MaterialLib` in index order. The distance sort (`MaterialLibPointer`) only affects culling.
9. **Reflection captures only see sun shadows inside the camera's cascades.** Saved local probe cubemaps restore on load; probes without a valid saved capture fall back to the global cubemap until captured (one per frame).
10. **Shader hot reload is effectively off** (section 9.6).
11. `BasicEntity.Scripts` (`IScript`) is a legacy hook path. `PlayModeController` still calls it, but nothing adds to it.
12. **The environment shader is at MonoGame's 16 texture slots.** Probe volumes are packed into one `Texture3D` for that reason; a new texture there means packing or dropping another one (`IndexOutOfRangeException` in `TextureCollection` otherwise).
13. **Material asset edits during Play are kept.** Inspector edits of a linked Material component (or the asset Inspector) write the `.mat` file, which Stop doesn't restore (component state isn't snapshotted either, see 5). A prefab whose Material component is linked keeps a copy of the asset's values in the prefab file, so an asset edit can show up as overrides on its instances.
14. **`new ModelMeshPart()` is marked obsolete in MonoGame 3.8.4,** but it is the only way to build parts outside the content pipeline. `VoxelSystem` suppresses CS0618 around it.

---

## 14. Where to start for a given change

| You want to… | Start at |
|---|---|
| Change what happens each frame, or its order | `Engine.Update` → `ScreenManager.Update` → `MainSceneLogic.Update` |
| Add a render pass | `Renderer.Load/Initialize/Draw`, `SetUpRenderTargets` + `UpdateRenderMapBindings`, a module in `RenderModules/`, `.fx` + `Content.mgcb`, settings in `GameSettings` |
| Add a full-screen effect without touching the engine | a render feature: Anvil viewport toolbar > Pipeline > + New, then edit `Content/RenderFeatures/<Name>.fx`; see [Render_Features.md](Render_Features.md) |
| Change render features (injection points, blend modes, shader inputs, compiling) | `RenderFeatureEvent` (saved by name; a new point also needs a `DrawRenderFeatures` call in `Renderer.Draw` and a `Point(...)` in `RenderFeatures.Publish`), `RenderFeatureRenderModule.Draw/DrawFeature`, `RenderFeatures` (`Prepare`, `StartCompile`, `Update`, `Move`), `RenderFeatureCompiler`, `RenderFeatureShaderTemplate`; Anvil `RenderPipelineViewModel` + the Render Pipeline panel in `MainWindow.axaml` |
| Add or change gameplay controls (actions, bindings, rebinding) | Anvil > Window > Input Actions (`Content/System/InputActions.json`); code: `InputActions`/`InputAction` (`Engine/Logic/InputActions.cs`), `InputControls` (new control paths), `InputActionList` (file, defaults), script shortcuts in `ScriptBehaviour.Input.cs`; Anvil `InputActionsViewModel` + `InputActionsWindow`; see [Input_Actions.md](Input_Actions.md) |
| Change how a material is drawn | `GBufferRenderModule.SetMaterialSettings`, `Gbuffer.fx`, `MeshMaterialLibrary.Draw` filters |
| Change the lighting model (F0, diffuse, falloff, sized lights) | the PBR block of `Content/Shaders/Common/helper.fx` (`EvaluateLightSplit`, `LightAttenuation`, `AreaLightSpecularVector`), mirrored on the CPU by `PointLight.Attenuation` (bake); see [Source2_Look.md](Source2_Look.md) |
| Change sun cascades or the local-light shadow atlas | `ShadowMapRenderModule` (`DrawCascades`, `SplitCascades`, `DrawLocalLightShadows`, `AllocateFaces`), `ShadowAtlas`, `DirectionalLight` cascade fields, `SampleCascade`/`SunShadow` in `DeferredDirectionalLight.fx`, `LoadShadowMapUV` in `DeferredPointLight.fx` |
| Change lightmaps (unwrap, bake, lightmap pass, shadow mask) | `LightmapUnwrap`, `LightmapBaker`, `LightmapData`, `LightmapResources`, `LightmapRenderModule` + `Deferred/Lightmap.fx`, `BakedShadow` in the light shaders, `LightmapMap` in `DeferredEnvironmentMap.fx` ([Lightmaps.md](Lightmaps.md)) |
| Change reflections (probes, prefilter, normalisation) | `ReflectionProbes`, `Renderer.CaptureCube/CaptureReflectionProbe`, `EnvironmentPrefilter`, `ImageBasedLightingParameters`, `Common/Reflections.fx` |
| Change post processing settings or volumes | `PostProcessSettings` (new settings are saved, blended and listed in the volume Inspector automatically), `PostProcessVolumes`, `PostProfile`, `Renderer.ApplyScenePost`; Anvil `PostProcessingViewModel` + the Post Processing panel |
| Compare the look before/after a renderer change | `Engine.exe --scene ... --shot ...` (`LookDevCapture`), `.claude/skills/run-obsidian/lookdev_views.sh`; scene `Content/Scenes/LookDev.obsc` (generated by `Docs/Research/Source2Look/lookdev/generate_lookdev.py`) |
| Make a new object visible to the renderer | `BasicEntity.RegisterInLibrary` / `Dispose(library)` |
| Add per-object behaviour | a `GameComponent` (see CLAUDE.md "Adding a Gameobject Component") or a script + `ScriptRegistry` |
| Change Play/Stop semantics | `PlayModeController.Play/Stop/UpdateScripts`, `MainSceneLogic.OnPlayModeChanged` |
| Change scene switching | `GameFlow`, `SceneLoadOperation`, `MainSceneLogic.ActivateLoadedScene`, `OnSceneChanged`, `PersistentGameObjects` |
| Change the loading screen | `Content/UI/LoadingScreen.xml`/`.css` (ids listed in `LoadingScreen.cs`), or `GameFlow.LoadingScreenDocument` |
| Change physics sync | `ScenePhysics.Update/Reconcile/PushPose/PullPose`, `PhysicsSystem` |
| Expose engine state to Anvil | `IEditorBridge` + `EditorBridge.BuildSnapshot` + `BridgeReconciler` + view model + XAML |
| Change scene file format | `SceneSerialization` DTOs (bump `CurrentVersion`, keep loading v1–v2) |
| Change material assets (file format, what a material holds, capture, sync timing) | `MaterialAsset` (`SurfaceProperties`/`CopySurface`, `Capture`/`TexturePathOf`, `SyncScene`, `SaveFrom`), `MaterialComponent.MaterialPath/Link/OnAdded`; Anvil `MaterialInfo` (asset slot, `Push`), `MaterialAssetViewModel`, `MaterialSurfaceTemplate` in `MainWindow.axaml`, `MaterialAssetFormat` in `MainWindow.axaml.cs`; see [Material_Component.md](Material_Component.md) |
| Change prefabs (what follows the prefab, override granularity, Apply/Revert, file lookup) | `PrefabUtility` (`InstanceKeys`, `Overrides`/`DiffComponents`, `Sync`, `ApplyToPrefab`), `Prefab` (`Resolve`, `Load`, `Write`), `MainSceneLogic.InstantiatePrefab/ReplaceGameObject`, bridge `Enqueue*Prefab`; Anvil prefab bar in `MainWindow.axaml`, drag formats in `MainWindow.axaml.cs`; see [Prefabs.md](Prefabs.md) |
| Draw geometry that isn't a gameobject | `MeshMaterialLibrary.Register(material, part, transform, sphere)` / `Unregister`, as `VoxelSystem.Upload` / `Release` do |
| Ragdoll joints for a body type (humanoid, quadruped, generic creature) | `RagdollRig.Detect/AssignJoints/AssignQuadrupedJoints/AssignGenericJoints/IsLegRoot`, limits in `RagdollJointLimits.For`, hinges in `Ragdoll.AddJoint`; inspector `RagdollComponentViewModel.Skeleton`; sample Wolves in `Content/Scenes/AnimationTest.obsc`; see [Ragdoll_Physics.md](Ragdoll_Physics.md) |
| Pose bones in the editor / the Hierarchy Armature | `BonePose` (pose store, Edit-mode skin), `BoneObject` (gizmo proxy, IDs), `AnimationPlayer.Overrides`, `EditorBridge.BuildBones/LookupById`, `BridgeReconciler.ApplyBones`, `SceneObjectViewModel.IsBone/ResetBone/ResetPose`; checks in `Tests/Components/BoneChecks.cs`; see [Skeletal_Animation.md](Skeletal_Animation.md) |
| Change skeletal animation playback (actions, crossfades, one-shots) | `AnimatorComponent` (`Switch`, `Pose`, `Finish`, `Select`/`Forget`), `AnimationPlayer.Evaluate/BlendFrom`, inspector `AnimatorComponentViewModel` + `AnimationActionViewModel`; sample `Y Bot - Actions` in `Content/Scenes/AnimationTest.obsc` + `Content/Scripts/AnimatorActionsScript.cs`; see [Skeletal_Animation.md](Skeletal_Animation.md) |
| Change the voxel world | `Engine/Voxels/` (`VoxelGenerator`, `VoxelMesher`, `VoxelTextures`, `VoxelSystem`), settings in `EnvironmentSettings`; see [Voxel_World.md](Voxel_World.md) |
| Change particle systems | `Engine/Particles/` (`ParticleModules`, `ParticleSimulation`, `ParticleTextures` (Flame Sheet: `Flame` + `FireRamp`; Fireball Sheet: `Fireball`), `ParticleVolume`, `ParticleLights`), `ParticleSystemComponent`, `ParticleRenderModule` + `Shaders/Forward/Particles.fx` (techniques `Particles` and `Volume`); see [Particle_Systems.md](Particle_Systems.md) |
| Change the sample explosion (fireball, smoke, dust, sparks, chunks with smoke trails) | the `Explosion ...` gameobjects of `Content/Scenes/ParticleTest.obsc` (restarted together by `ParticleTestScript.Explode`); the fireball look is `ParticleTextures.Fireball` (Fireball Sheet, Alpha blend, Brightness 3); systems sort by gameobject distance, so the smoke sits a little below the fireball; see "A realistic explosion" in [Particle_Systems.md](Particle_Systems.md) |
| Change particle lights (fire lighting the scene) | `ParticleLightsModule` (`ParticleModules.cs`), `ParticleLights` (System centre/peak/flicker in `UpdateSystem`, carrier choice in `UpdatePerParticle`), `ParticleSystemComponent.CollectLights`, the `_frameLights` merge at the top of `Renderer.Draw`, `ParticleRenderModule.SetPointLights` + the point light loop in `ParticleVS` (`Particles.fx`); sample: the Campfire/Spark Fountain/Explosion Fireball of `Content/Scenes/ParticleTest.obsc` |
| Change volumetric / CS2 voxel smoke | `ParticleVolume` (flood fill order `FillOrder`, edge/fade in `Pack`, carves), `ParticleVolumetricModule`, `ParticleRenderModule.DrawVolume` + `VolumePS` in `Particles.fx`; sample `Content/Scenes/SmokeTest.obsc` + `Content/Scripts/SmokeTestScript.cs` + `UI/SmokeTest` |
| Change audio volumes / add a mixer bus | `AudioBus` + `AudioManager` (`Initialize` groups, `GroupFor`, `GetVolume`/`SetVolume`), `Audio` facade, `MenuSettings.Audio()`, `AudioComponent.Output`; see [Audio_Volume.md](Audio_Volume.md) |
| Save or load game data (save slots, settings) | `SaveData` (`Engine/Logic/SaveData.cs`): file naming in `GetPath`, atomic write + `.bak` in `WriteFile`, per-file ordering in `QueueWrite`, JSON in `CreateJsonOptions`; see [Save_Data.md](Save_Data.md) |
| Change water splashes, water rings or the in/out-of-water events | `ScenePhysics.UpdateWaterContacts/DispatchWaterEvents` (detection, `WaterExitMargin`), `Engine/Particles/WaterSplashes.cs` (droplet/jet/spray look in `CreateCrown/CreateJet/CreateMist`, sizes in `Throw`, thresholds `MinSplashSpeed`), `SplashRings`/`RainRings` in `Shaders/Forward/Water.fx`; sample `Content/Scenes/WaterTest.obsc` + `Content/Scripts/WaterTestScript.cs`; see [Water.md](Water.md) |
| Change bottle liquid (slosh, fill, look) | `Engine/Physics/LiquidSlosh.cs` (think, peaks, down direction, wobble; Alyx constants), `LiquidVolume.cs` (fill tables from the mesh), `LiquidComponent`, `Shaders/Forward/Forward.fx` (`LiquidRipple`, `LiquidDepth`, `LiquidBubbles`, the `UseLiquid` block), the back-face pass in `MeshMaterialLibrary.Draw`; sample `Content/Scenes/LiquidTest.obsc` + `Content/Scripts/LiquidTestScript.cs`; see [Bottle_Liquid.md](Bottle_Liquid.md) |
| Change breakables (fracture, pieces, dust/blood, zombie heads) | `MeshFracture` (sites, clipping, caps), `FractureShatter` (mesh readback, pieces, velocities, interior material, lifetime), `BreakableComponent` (thresholds, follow/collapse/ragdoll), `Animation/BoneCollapse.cs`, `Particles/BreakBursts.cs`; models `Content/GameObjects/Vase|Head/Source/generate_*.cs`; sample `Content/Scenes/BreakableTest.obsc` (shooting in `CollisionTestPlayerScript`); checks `Tests/Components/BreakableChecks.cs`; see [Breakables.md](Breakables.md) |
| Change tree swaying / leaf flutter, or add trees | `Content/Shaders/Common/FoliageWind.fx` (bend, branch sway, flutter), `FoliageWindComponent` (strength, clock rate, `UpdateAll`), `FoliageWindParameters` (`Renderer/Helper/FoliageWind.cs`), leaf translucency in `DeferredDirectionalLight.fx` (`FoliageTransmission`); the tree mesh and textures come from `Content/GameObjects/Tree/Source/generate_tree.py`; sample `Content/Scenes/FoliageTest.obsc`; see [Foliage_Wind.md](Foliage_Wind.md) |
| Change the AI texture generator (families, style ranges, estimator, seam check, output files, id, runner, import) | `Tools/TexGen/texgen/` (`pipeline.py` stages, `styles/source2.json` + `style.py`, `backends/classic_pbr.py`, `postprocess.py`, `validate.py`, `seams.py`); bump `PIPELINE_VERSION` in `texgen/__init__.py` **and** `MaterialGenerationId.PipelineVersion` when results change; `Engine/Recources/TextureGeneration/` (`TexGenRunner`, `GeneratedMaterialImporter`, `GeneratedMaterialSet`), `ContentManifest.RegisterGeneratedTextures`, `EditorBridge.EnqueueImportGeneratedMaterial`; Anvil `GenerateMaterialViewModel`/`GenerateMaterialWindow`; MCP `materials_generate` (`EditorAutomationService`); checks `Tests/Components/TexGenChecks*.cs`, `TexGenMcpChecks.cs` + `Tools/TexGen/tests`; plan `Docs/Research/AiTextures/PLAN.md` |
| Change the editor ground grid | `GridRenderModule` + `Shaders/Editor/Grid.fx`, `GameSettings.e_showgrid/e_gridheight/e_gridcellsize/e_gridfadedistance`, Anvil's viewport Grid button (`MainWindowViewModel.IsGridVisible`) |
| Change picking or gizmos | `IdAndOutlineRenderer`, `EditorRender`, `EditorLogic.Update/GizmoControl`, `IdGenerator` |
| Debug "Anvil viewport wrong size" | `Engine.ApplyPendingResize`, `MonoGameHost.ResizeEngineToContainer` |
