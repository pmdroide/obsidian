# Engine Wiring: How Obsidian Fits Together

This is a low-level map of the engine. It covers who creates what, in which order, on which thread, and how data moves through one frame. Read it before you change the frame loop, the renderer, the scene model or the editor seam.

It complements the topic docs instead of repeating them:

- [Editor_Architecture.md](Editor_Architecture.md): Anvil in depth (MVVM, reconciler, XAML).
- [Scenes_and_Game_Flow.md](Scenes_and_Game_Flow.md): scene list, `GameFlow`, `GameInput`, `GameUI` from a script's view.
- [Gameobject_Components.md](Gameobject_Components.md) and [Script_Behaviours.md](Script_Behaviours.md): writing components and scripts.
- [Collisions_and_Interaction.md](Collisions_and_Interaction.md): physics events from a script's view.

Paths are relative to the repo root. Identifiers match the source. Line numbers are left out on purpose because they drift. Search for the method name instead.

---

## 1. Assemblies and build wiring

| Project | Output | Role |
|---|---|---|
| `Engine/Engine.csproj` | `Engine.exe` (WinExe, `net10.0-windows`) | MonoGame `Game`, renderer, scene, physics, scripts, editor bridge |
| `Vista/Vista.csproj` | library | XML/CSS UI (AngleSharp DOM, drawn with `SpriteBatch`), referenced by Engine |
| `ContentPipeline/ContentPipeline.csproj` | `Obsidian.ContentPipeline.dll` (net8.0) | `SkinnedModelProcessor` for mgcb. It is built first, but Engine does **not** reference it (`ReferenceOutputAssembly="false"`). `Content.mgcb` loads it with `/reference:../../ContentPipeline/bin/...` |
| `Editor/Anvil/Anvil.csproj` | `Anvil.exe` (Avalonia 12) | Editor. It references `Engine.csproj` and runs the engine **in-process** |
| `Tests/Components` | console app | Integration checks (`InternalsVisibleTo Include="Components"` gives it engine internals) |

How each kind of file reaches the running game (`Engine/Engine.csproj`):

- **MGCB content** (models, textures, `.fx` shaders, sprite fonts): `MonoGameContentReference Include="Content\Content.mgcb"` turns them into `.xnb` files under `bin/.../Content`. They are loaded with extensionless `content.Load<T>("path")`.
- **Scripts** (`Content/Scripts/**/*.cs`): compiled **into `Engine.dll`** by normal SDK globbing, in `namespace Engine.Scripting`. They are also copied as loose files so the editor can open them. There is no runtime compiler (no Roslyn), so editing a script means rebuilding the engine.
- **Loose runtime files** are copied with `CopyToOutputDirectory` and never pass through mgcb: scenes (`.obsc`, `.probes`), Vista UI (`.xml`/`.css`), audio (FMOD), intro videos (LibVLC), `Content/System/*.json`, skyboxes. A file a standalone build needs must also have `CopyToPublishDirectory`.
- **Native libraries:** `steam_api64.dll` and `fmod.dll` are linked flat into the output root.

NuGet dependencies that matter at runtime: `MonoGame.Framework.WindowsDX 3.8.4.1`, `BepuPhysics 2.4.0`, `FmodForFoxes 3.2.0`, `LibVLCSharp`, Steamworks.NET (from `thirdparty/steam`).

---

## 2. Threads

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
```

Rules that follow from this:

- **Everything in the engine is single-threaded on the game thread:** scene lists, components, scripts, physics (BEPU is created with a `null` thread dispatcher), rendering. The bake hands its result back through `LightingSystem.Update`, which runs on the game thread.
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
   - subscribes `SceneManager.SceneChanged → OnSceneChanged`
   - creates `PlayModeController` and subscribes `ModeChanged`
   - sets `GameFlow.SceneLogic = this`
   - builds the empty boot scene: a game camera, a separate `EditorCamera`, an `EnvironmentSample`, an `SdfGenerator` and one shadowed sun.
6. `_editorLogic.Initialize`, `_debug.Initialize`.
7. `_bridge.Bind(_sceneLogic, _editorLogic, _assets)`. This sets `Input.HostBridge` and subscribes to scene swaps, play mode changes, lighting status and model or material registration.
8. `SceneList.Load()` reads `Content/System/SceneList.json`.
9. `GameUI.Bind(device, _uiFonts)`.
10. When hosted, `GameStats.e_EnableSelection = true`, so ID and outline passes always run in Anvil.

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
│   ├─ _editorLogic.Update(..., _editorReceivedDataBuffer, ...)   ← uses LAST frame's hovered ID
│   ├─ _sceneLogic.Update
│   │   ├─ ApplyPendingSceneLoad()      ← GameFlow.LoadScene queued last frame
│   │   ├─ Input.Update(gameTime, Camera)   ← snapshot keyboard/mouse; drives the editor camera
│   │   ├─ GameInput.Update(dt)         ← script-facing input, gamepads, menu repeat
│   │   ├─ PlayMode.UpdateScripts(gameTime) ← component OnUpdate (Play only)
│   │   └─ dev hotkeys (F1 render mode; Space/L/X/M outside Play)
│   ├─ _audio.UpdateListener(Camera)
│   ├─ _renderer.Update(...)            ← editor mouse-moved timer, SDF generator
│   ├─ _sceneLogic.Lighting.Update(...) ← collect a finished bake, upload probe textures
│   ├─ _debug.Update
│   ├─ GameUI.Update                    ← Vista layout and XML/CSS hot reload, after scripts
│   └─ _bridge.DrainAndPublish()        ← apply Anvil's queued edits; publish a snapshot every 6 frames
└─ ScreenManager.UpdatePhysics(dt, totalSeconds)
    └─ MainSceneLogic.UpdatePhysics → ScenePhysics.Update(entities, dt, simulate, time)
        ├─ rebuild or sync bodies from components and transforms
        ├─ if simulate: Step(min(dt, 1/20)) → pull poses → contacts
        └─ dispatch OnCollision*/OnTrigger* to scripts
```

Consequences of this order:

- **Scripts run before physics in the same frame.** Collision and trigger callbacks fire after the step, so they run after every script's `Update` that frame.
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
    ├─ GameUI.Draw(spriteBatch)         ← script menus and HUDs over the 3D image
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

---

## 5. Global and static state

Most cross-system wiring goes through statics. Knowing them saves a lot of searching.

| Static | Set by | Used for |
|---|---|---|
| `Globals.content` | `ScreenManager.Load` | `Shaders.*` static initialisers, some loaders |
| `Shaders.*` | static init | Cached `Effect`, `EffectParameter` and `EffectPass` objects for shared shaders |
| `GameSettings.*` | defaults, `ApplySettings`, the editor | Render, editor and physics switches. Prefixes: `g_` graphics, `e_` editor, `d_` debug, `sdf_`, `u_` UI, `p_` physics. Some setters push straight into shader parameters |
| `GameStats.*` | renderer, editor | Per-frame counters, `e_gizmoMode`, `e_EnableSelection`, `e_LocalTransformation` |
| `GameFlow.SceneLogic`, `QuitHandler`, `Paused`, `EscapeQuits`, pending scene | `MainSceneLogic.Initialize`, `Engine` ctor, scripts | Script API for scenes and pausing |
| `ScenePhysics.Current` | `ScenePhysics` ctor | Script raycasts, navigation |
| `Input.*` state, `Input.HostBridge` | `Input.Update`, `EditorBridge.Bind` | Raw keyboard/mouse, Anvil key merge |
| `GameInput.*` | `GameInput.Update` | Script input |
| `GameUI.Layers` | `GameUI.Open/Close` | Script UI layers |
| `Audio.Instance` | `AudioManager.Initialize` | Static audio facade |
| `ScriptBehaviour.Running` | `ScriptBehaviourComponent` around each call | Records which script owns a `GameUI` layer |
| `SteamService.Current` | `Engine` ctor | Steam session for scripts |
| `IdGenerator._currentId` | `GetNewId`, `Reseed` | Object IDs (section 6.3) |

There is **no** global `GraphicsDevice`. It is passed into `Initialize` methods, and `Assets` keeps `GraphicsDevice` and `Content` as instance properties.

---

## 6. Scene model

### 6.1 Ownership

```
MainSceneLogic                                  (runtime drivers, one per process)
├─ SceneManager ──► ActiveScene : Scene         (data that round-trips to .obsc)
│                     ├─ BasicEntities, PointLights (incl. SpotLight), DirectionalLights, Decals
│                     ├─ MainCamera (game camera, has Components), EnvironmentSample
│                     ├─ Environment (EnvironmentSettings: sky, fog, weather, wind…)
│                     ├─ Lighting (LightingSettings) + BakedProbes (.probes sidecar)
│                     └─ Name, FilePath, IsDirty
├─ EditorCamera            (never saved)
├─ Camera  ⇒  Play && MainCamera != null ? ActiveScene.MainCamera : EditorCamera
├─ MeshMaterialLibrary     (render registry, section 9.2)
├─ ScenePhysics            (BEPU bodies, section 8)
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
4. `MeshMaterialLibrary.Clear()`.
5. It adds a default `MainCamera` and `EnvironmentSample` if the scene has none. Without them every `Draw` would throw.
6. `RegisterInLibrary` for every new entity.
7. `PlayMode.Stop()`, unconditionally.
8. `GameUI.CloseAll(carried)`.
9. The bridge clears the editor selection and raises `SceneChanged` toward Anvil.

Scene loads that scripts request during Play take a different path (section 7.5).

### 6.3 IDs and how picking encodes them

- `IdGenerator.GetNewId()` pre-increments a counter that starts at 103, so the first ID is 104. IDs 1–3 are reserved for gizmo axes, and 0 means "nothing".
- Every `BasicEntity`, light, decal and environment sample takes an ID in its constructor. `BasicEntity.Id` mirrors into `WorldTransform.Id`, which the ID render pass reads.
- Scene load keeps the IDs stored in the file, calls `IdGenerator.Reseed(maxId)`, and only then gives new IDs to duplicates or missing ones. Script `GameObject` fields are saved as these IDs.
- `EditorBridge.MainCameraId = -1` is a synthetic ID for the scene camera in snapshots.
- **ID ⇄ color:** `GetColorFromId` packs an ID into RGB in **base 255** (`r + g*255 + b*255²`), not 256. `GetIdFromColor` maps white to 0. Keep both functions in sync if you change one.

---

## 7. Entities, components, scripts, Play mode

### 7.1 `BasicEntity` and the transform dirty flag

`BasicEntity : TransformableObject` (`Engine/Entities/BasicEntity.cs`) holds:

- read-only model data: `ModelDefinition`, `Model`, `BoundingBox`, `BoundingBoxOffset`, `SignedDistanceField`, `Material`
- `Position`, `RotationMatrix`, `Scale`
- `IsEnabled`, `Role`, `Persistent` / `RuntimePersistent`, `IsRuntimeSpawned`
- `Components`
- BEPU runtime state: `StaticBody`, `DynamicBody`, `Attached*` caches, `SyncedPosition/Rotation`, `CollisionGroup`, `Ragdoll`
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
- Registration order (= Add Component menu order): `material`, `physics`, `audio`, `script-behaviour` (multiple), `animator`, `ragdoll`, `interactable`, `cloth`, `npc`.
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
2. Restores the snapshot by ID.
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
           → ApplyFieldEdits() (inspector edits during Play) → instance.Tick(dt) → Update()
  Notify(hook, call)  ← entry point for physics, interaction, NPC and OnSceneLoaded events
  OnStop   → instance.Shutdown(): Stop(), StopAllSounds(), DestroyAllSpawned()
  An exception sets _faulted; a faulted script stops ticking until it is restarted.
```

- **A new script needs two steps:** a class in `Content/Scripts/` *and* a line in the `ScriptRegistry` static constructor.
- `Spawn(modelKey, pos)` → `MainSceneLogic.SpawnRuntimeEntity` → `Assets.FindModel`, flagged `IsRuntimeSpawned`, never saved. `Destroy(entity)` only removes objects the same script spawned (through `EditorDelete`).
- Script transform helpers use Z-up conventions: Forward = local +Y, Up = local +Z.

### 7.5 Scene loads during Play (`GameFlow` → `ApplyPendingSceneLoad`)

`GameFlow.LoadScene(...)` only **queues** a path and a `carryPersistent` flag. The load happens at the start of the next `MainSceneLogic.Update`:

1. Unpause, then `SceneSerialization.LoadFromFile`.
2. If playing:
   - In Anvil, remember `_editScene` (the scene being edited) the first time.
   - If carrying, `_persistent.TakeFrom(ActiveScene)` removes the persistent objects first and unregisters them from the mesh library.
   - `PlayMode.Stop()` stops the old scene and restores its transforms. `_switchingScene` keeps `OnPlayModeChanged` from treating this as a real Stop.
   - `_persistent.PrepareArrival(loaded)` drops the new scene's own copies of carried objects and re-IDs any collisions.
3. `SceneManager.SetActiveScene(loaded)` (section 6.2).
4. If playing: `_persistent.Arrive`, register the carried objects in the library, `PlayMode.Play(carried)` (carried objects keep running instead of restarting), then `NotifySceneLoaded` (`ScriptBehaviour.OnSceneLoaded`).

On Anvil's Stop, `OnPlayModeChanged(Edit)` closes the UI, unpauses, drops any pending load, raises `PlayStopped`, sends carried objects back home, and swaps back to `_editScene`.

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
   Reconcile(e)   desired = None if no model, Role == Water, or a limp ragdoll; else e.PhysicsType
                  rebuild (Detach + Attach) when type, scale, trigger or collider stretch changes,
                  or (Dynamic) when mass or freeze-rotation changes
   sync direction:
     editing or Static, and moved      → PushPose(resetVelocity: true)    entity → body
     simulating Dynamic moved by code  → PushPose(resetVelocity: false)   (Position != SyncedPosition)
destroy bodies (and ragdolls) of entities that were not visited
if !simulate: clear touches; return
step = min(dt, 1/20)
ApplyBuoyancy (Water role) → ragdoll.BeforeStep → Simulation.Timestep(step)
PullPose for Dynamic bodies         body → entity (setters run only on change, so sleeping bodies stay clean)
ragdoll.AfterStep → UpdateTouches → DispatchContactEvents
```

The physics *component* is just data. `ScenePhysics` reads it every frame through `BasicEntity.PhysicsType` and `Mass`. Changing a field in the inspector triggers a rebuild on the next step.

**Contact events** are keyed by the `(BasicEntity, BasicEntity)` pair, so they survive body rebuilds:
- A new pair fires Enter, a pair seen again fires Stay, and a missing pair fires Exit unless both sides are resting.
- `Notify` sends `OnCollision*` or `OnTrigger*` to both owners' `ScriptBehaviourComponent`s.
- Water buoyancy reads `WaterWaves`, which must match the swell in `Water.fx`.

---

## 9. Renderer (`Engine/Renderer/`)

A deferred renderer. It renders a G-buffer, accumulates lights, composes, then draws forward, water and weather, then TAA, bloom, post-processing and editor overlays.

### 9.1 Modules (`Renderer.Load`, in creation order)

`BloomFilter` → `LightAccumulationModule` (owns `PointLightRenderModule`) → `ShadowMapRenderModule` → `GBufferRenderModule` → `TemporalAntialiasingRenderModule` → `DeferredEnvironmentMapRenderModule` → `DecalRenderModule` → `SubsurfaceScatterRenderModule` (unused in Draw) → `ForwardRenderModule` → `WaterRenderModule` → `WeatherRenderModule` → `HelperGeometryRenderModule` → `DistanceFieldRenderModule` → `FroxelRenderModule` → `ColorGradingFilter` → `AutoExposureFilter`, plus the LUT texture.

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
- **Batching:** `HasMaterial` only matches when `GameSettings.g_batchbymaterial` is true. The default is false, so each registration gets its own `MaterialLibrary`.
- **`FrustumCulling`** applies dirty transforms, re-tests spheres against the frustum, and sets `Rendered[i]`. It returns whether anything changed, which shadow passes use to skip redraws. Shadow passes call it with *light* frusta, so the flags are re-culled for the camera afterwards.
- **`Draw(RenderType, viewProjection, …, renderModule)`**: `RenderType` is one of `Opaque, ShadowOmnidirectional, ShadowLinear, Hologram, IdRender, IdOutline, SubsurfaceScattering, Forward, Water`. It filters by material:
  - Opaque skips transparent and `ForwardShaded`.
  - Forward draws only `ForwardShaded`.
  - Water draws only `Water`, sorted back to front.
  - Hologram is exclusive.
  - Shadow passes skip `!HasShadow`.

  It sets blend and rasterizer state per pass, calls the module's `SetMaterialSettings` once per material, then calls `Apply` per instance. For skinned or cloth instances, `transform.Skin?.Resolve(...) ?? transform.Cloth?.Resolve(...)` swaps in a per-instance vertex buffer for that draw.

### 9.3 Materials (`Engine/Recources/MaterialEffect.cs`)

`MaterialEffect : Effect` holds:
- texture slots, each with a `Has*` flag set by its setter: `AlbedoMap`, `RoughnessMap`, `MetallicMap`, `NormalMap`, `DisplacementMap`, `Mask`
- scalars: `DiffuseColor`, `Roughness`, `Metallic`, `EmissiveStrength`, tiling, water and wet parameters
- `Type : MaterialTypes`: `Basic 0, Hologram 1, ProjectHologram 2, Emissive 3, SubsurfaceScattering 4, ForwardShaded 5, Water 6, WetSurface 7`

Two things depend on the material:

- **Which pass draws it:** decided by `Type` inside `MeshMaterialLibrary.Draw`.
- **Which G-buffer technique:** decided by the `Has*` flags in `GBufferRenderModule.SetMaterialSettings`. Examples are `DrawTextureSpecularNormalMetallic`, `DrawTextureDisplacement` and the `*Mask` variants, down to `DrawBasic`.
  - The material type number is packed into the G-buffer (below).
  - WetSurface writes type 0 and sets `WetSurface` / `WetParams`.

### 9.4 `Renderer.Draw`: pass order and render targets

| # | Step | Writes | Reads |
|---|---|---|---|
| 1 | `_environmentSky.Apply` (day/night sun), exposure, `SurfaceWetness` | shader params | scene environment |
| 2 | `CheckRenderChanges` | pushes far clip, shadow filtering and SSR changes into modules | `GameSettings` vs cached copies |
| 3 | `DrawShadowMaps` | per point light: `HalfSingle` RT of `size × 6·size` (6 face strips); per directional light: `Single` RT | `RenderType.ShadowOmnidirectional/ShadowLinear`, light frusta |
| 4 | SDF transforms (if any light uses SDF shadows) | SDF data | |
| 5 | `DrawCubeMap` if `envSample.NeedsUpdate` | `_renderTargetCubeMap` (`HalfVector4` cube). Each face runs a small deferred pipeline with the render targets temporarily rebuilt at envmap size (`onlyEssentials`) | |
| 6 | `UpdateViewProjection` | `_view`, `_projection`, TAA-jittered `_viewProjection`, unjittered `_staticViewProjection`, `_currentViewToPreviousViewProjection`, frustum corners → SSR/SSAO/TAA/lights | camera |
| 7 | `FrustumCulling` (camera) | `Rendered[]` | |
| 8 | **G-buffer** (`GBufferRenderModule`) | MRT `[Albedo, Normal, Depth]` | `RenderType.Opaque` |
| 9 | Decals | Albedo (copied through `DecalOffTarget`) | Depth |
| 10 | SSR | `ScreenSpaceEffectReflection` | **previous** TAA buffer (or Composed) |
| 11 | SSAO + bilateral blur | `SSAOEffect` (half res) → `…BlurVertical/Horizontal` → `BlurFinal` | Normal, Depth |
| 12 | Froxel fog | froxel injection + ping-pong accumulation atlas (160 × 90·128) | lights, depth |
| 13 | `LightAccumulationModule.DrawLights` | rebuild hardware depth into `Diffuse`'s D24S8 from linear depth (`ReconstructDepth`), then additive MRT `[Diffuse, Specular, Volume]`. Point and spot lights draw sphere proxies (stencil-culled); directional lights draw full-screen | G-buffer, shadow maps |
| 14 | `DrawEnvironmentMap` | adds into the still-bound Diffuse/Specular | env cubemap, sky, baked probe SH (`Texture3D`s), SSR |
| 15 | `Compose` | `Composed` (`HalfVector4`) | Albedo, Diffuse, Specular, `BlurFinal` (AO), froxels |
| 16 | `DrawForward` | `Composed` (after `ReconstructDepth` into its depth buffer) | `RenderType.Forward`, up to 40 point lights as arrays |
| 17 | Water, then weather (haze + particles) | `Composed` | Depth, env cubemap, sun |
| 18 | TAA | `TAA_1`/`TAA_2` ping-pong; with `g_taa_tonemapped`, inverse-tonemapped back into `Composed` | previous TAA buffer, Depth, reprojection matrix |
| 19 | Bloom | Mip0..Mip5 chain → `_renderTargetBloom` | current output |
| 20 | Auto exposure | 256² luminance chain → 1×1 ping-pong | current output |
| 21 | `RenderMode` → `DrawPostProcessing` | `_renderTargetOutput` → color grading (LUT) → **backbuffer** | `g_rendermode` can show a single buffer instead (F1 cycles) |
| 22 | Editor: `DrawIds`, outlines, gizmos, billboards, helper lines, probe debug | ID RT, backbuffer | `RenderType.IdRender/IdOutline` |
| 23 | `FrustumCullingFinalizeFrame` | clears `HasChanged` | |

**G-buffer layout** (`Content/Shaders/GBufferSetup/Gbuffer.fx`):

| Target | Format | R | G | B | A |
|---|---|---|---|---|---|
| COLOR0 `_renderTargetAlbedo` | `Color` + D24 | albedo (gamma) | ← | ← | 1 = wet surface (view-dependent fresnel in env pass) |
| COLOR1 `_renderTargetNormal` | `HalfVector4` | sphere-map encoded view-space normal | ← | `metalness·0.9 + materialType` (decode: `trunc` = type, `frac/0.9` = metal) | roughness |
| COLOR2 `_renderTargetDepth` | `Single` | linear depth `viewZ / -FarClip` | | | |

**Render target ownership:**
- `Renderer.SetUpRenderTargets(w, h, onlyEssentials)` disposes and recreates every screen-sized target, then calls `UpdateRenderMapBindings` to re-point every cached texture parameter.
- Bloom resizes lazily inside its own `Draw`. The froxel atlas has a fixed size.
- Shadow maps belong to their lights. The env cubemap is created lazily.
- If you add a target, add it in both places, or the shaders will keep sampling a disposed texture after a resize.

### 9.5 Editor picking and outlines

- `IdAndOutlineRenderer` redraws the ID buffer only for 400 ms after the mouse last moved (`EditorRender.Update` keeps the timer).
- Each mesh is drawn with `ColorId = GetColorFromId(id)`. Billboards for lights, decals and the environment sample are drawn too, and gizmo arrows as IDs 1/2/3.
- It then reads back **one pixel** under the mouse (`GetData(..., new Rectangle(x, y, 1, 1))`) and returns `HoveredId` in `EditorReceivedData`.
- `EditorLogic.Update` uses it next frame. A click on 1–3 starts gizmo dragging. A click on nothing falls back to a CPU ray pick (`Picking.PickEntity`). Otherwise the object is selected.
- Outlines redraw the selected and hovered IDs into the ID target with `RenderType.IdOutline`, and the result is added onto the backbuffer.

### 9.6 Shaders and hot reload

- `Shaders.cs` holds `static readonly` effects and parameters for shared shaders. Missing parameters come back as `null`, so several call sites use `?.`.
- Modules load their own effects with `content.Load<Effect>`.
- `ShaderManager` (DEBUG only, `d_hotreloadshaders`) watches `.fx` timestamps. Only the decal and distance-field modules poll it, and it calls a **hard-coded legacy `mgcb.exe` path**. In practice: rebuild the content to see shader changes.

### 9.7 Settings and change detection

- `GameSettings` are plain statics. Some properties push into `Shaders.*` in their setters (SSAO, exposure, `g_Linear` → compose technique, SSR technique). `ApplySettings()` re-runs those setters once in `Renderer.Initialize`.
- Values that need resources rebuilt are polled once per frame in `Renderer.CheckRenderChanges`, which compares them against cached copies: far plane, SSR, shadow filtering.

---

## 10. Assets and content (`Engine/Recources/`)

- **`Assets`** is a hard-coded registry. Its public `ModelDefinition` and `Texture2D` fields *are* the built-in keys: `Cube`, `Capsule`, `Cloth`, `PlayerYBot`, …. `Assets.Load` fills them in a fixed order. It ends with `ReimportExistingModels`, which scans `<exe>/Content/GameObjects/Models/<key>/<key>.xnb` and registers what it finds in `_dynamicModels` through `RegisterModel` (raising `ModelRegistered`).
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
  - Model and texture keys are resolved by **reflection over the public fields of `Assets`**.
  - A legacy entity-level `Physics` record becomes a `PhysicsComponent` on load.
  - Baked probes go into a sibling `.probes` file.
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

**Hosting facts**
- `MonoGameHost` sets `Environment.CurrentDirectory` to the Engine assembly folder before it creates the engine, so `Content/` resolves.
- It reparents the engine window under a `STATIC` container window as `WS_CHILD`, with no caption or border and `WS_EX_TOOLWINDOW`.
- When hosted, the engine ignores window activation (`active = _isActive || IsHostedByEditor`) and skips `GameInfo`.

**Input from Anvil**
- **Keyboard:** Avalonia key events are mapped to XNA `Keys` (A–Z, modifiers, arrows, F1–F3, Esc, Enter, Back, Tab, Space). They go into `EditorBridge._hostKeysDown` through `SetHostKeyState`.
- **Mouse:** not forwarded. The engine reads `Mouse.GetState()` and `Input.FilterHostMouse` masks out presses that start outside the viewport or happen during an asset drag.

---

## 12. Input, script UI, audio

- **`Input`** (engine-level, raw):
  - `Update` copies the current state to "last", then reads `Keyboard.GetState()` and the filtered mouse.
  - It drives the editor camera, or the legacy game-camera controls when the camera has no active script.
  - `IsKeyDown` merges the Anvil keys. **`WasKeyPressed` / `WasKeyReleased` read only the native keyboard.**
- **`GameInput`** (script-level):
  - Rebuilds its key set each frame through `Input.IsKeyDown`, so its edges *do* include Anvil keys.
  - Adds XInput gamepads, `AnyKey`/`LastDevice`, menu directions with repeat, and the filtered mouse.
  - Scripts should use `GameInput`.
- **`GameUI`** (static):
  - `Open("UI/Name")` creates a `Vista.UIManager` layer, loading `.xml` + `.css` through `ResolveContentFile`. It records the owning script's gameobject.
  - `Update` hot-reloads changed files every 0.5 s, then lays out each layer, scaled from a 1080-high canvas.
  - `Draw` draws the layers in the order they were opened.
  - Vista has **no input handling**. Scripts poll `GameInput` and hit-test with `GameUI.ElementAt` / `UIManager.ElementAt`.
- **Audio** (`AudioManager`, FMOD Core through FmodForFoxes):
  - `SystemUpdate` runs every frame. `UpdateListener(camera)` moves the listener and the 3D emitters.
  - Files are loose (`Audio/<name>.wav`). In Anvil, changed source files are copied to `<exe>/Content` before loading.
  - If FMOD fails to start, every call becomes a no-op.

---

## 13. Sharp edges (checked against the source)

1. **Runtime-imported models don't round-trip through scene files.** `SceneSerialization` builds its key tables from the public fields of `Assets` only (`BuildAssetLookup` / `BuildReverseLookup`), never from `Assets.DynamicModels`. Saving an entity with an imported model logs "unregistered ModelDefinition, skipping", and loading an unknown key skips the entity. `FindModel` (used by `Spawn`) does check dynamic models.
2. **Two keyboard APIs.** `Input.WasKeyPressed` ignores Anvil-forwarded keys, but `GameInput` includes them. In-engine editor hotkeys (Delete, Insert, Ctrl+C, R/T/Z, F1, Space) only work when the engine window itself has keyboard focus. Delete and Insert are also missing from `MonoGameHost.MapAvaloniaKey`.
3. **`EditorLogic` has two delete paths.** The Delete key path removes the object from its list and disposes it from the mesh library. It does **not** call component `OnStop` or `ScenePhysics.Detach`. The bridge path (`MainSceneLogic.EditorDelete`) does both.
4. **Debug-entity picking indexes the wrong list.** In `EditorLogic.Update`, the `debugEntities` loop reads `dirLights[index]`.
5. **Play does not snapshot component state or spawned objects.** Stop restores only transforms, the environment and the camera pose. Spawned objects are removed by the scripts' `Shutdown`.
6. **`World` updates depend on CPU culling** (section 7.1).
7. **`RenderMode` runs twice per frame** (before and after `DrawIds`). Editor overlays then draw on top of the second pass.
8. **`g_batchbymaterial` is false by default**, so there is no material batching. `MeshMaterialLibrary.Draw` also iterates `MaterialLib` in index order. The distance sort (`MaterialLibPointer`) only affects culling.
9. **A shadowed directional light with `ScreenSpaceShadowBlur` throws** `NotImplementedException` in `DrawScreenSpaceDirectionalShadow`.
10. **Shader hot reload is effectively off** (section 9.6).
11. `BasicEntity.Scripts` (`IScript`) is a legacy hook path. `PlayModeController` still calls it, but nothing adds to it.

---

## 14. Where to start for a given change

| You want to… | Start at |
|---|---|
| Change what happens each frame, or its order | `Engine.Update` → `ScreenManager.Update` → `MainSceneLogic.Update` |
| Add a render pass | `Renderer.Load/Initialize/Draw`, `SetUpRenderTargets` + `UpdateRenderMapBindings`, a module in `RenderModules/`, `.fx` + `Content.mgcb`, settings in `GameSettings` |
| Change how a material is drawn | `GBufferRenderModule.SetMaterialSettings`, `Gbuffer.fx`, `MeshMaterialLibrary.Draw` filters |
| Make a new object visible to the renderer | `BasicEntity.RegisterInLibrary` / `Dispose(library)` |
| Add per-object behaviour | a `GameComponent` (see CLAUDE.md "Adding a Gameobject Component") or a script + `ScriptRegistry` |
| Change Play/Stop semantics | `PlayModeController.Play/Stop/UpdateScripts`, `MainSceneLogic.OnPlayModeChanged` |
| Change scene switching | `GameFlow`, `MainSceneLogic.ApplyPendingSceneLoad`, `OnSceneChanged`, `PersistentGameObjects` |
| Change physics sync | `ScenePhysics.Update/Reconcile/PushPose/PullPose`, `PhysicsSystem` |
| Expose engine state to Anvil | `IEditorBridge` + `EditorBridge.BuildSnapshot` + `BridgeReconciler` + view model + XAML |
| Change scene file format | `SceneSerialization` DTOs (bump `CurrentVersion`, keep loading v1–v2) |
| Change picking or gizmos | `IdAndOutlineRenderer`, `EditorRender`, `EditorLogic.Update/GizmoControl`, `IdGenerator` |
| Debug "Anvil viewport wrong size" | `Engine.ApplyPendingResize`, `MonoGameHost.ResizeEngineToContainer` |
