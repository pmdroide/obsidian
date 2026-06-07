# Changelog

## Migrate physics from BEPUphysics v1 to BEPUphysics v2 (BepuPhysics)

Replaced the engine's long-dormant, vendored **BEPUphysics v1** DLLs (object-oriented `Space`/`Entity`/`StaticMesh`, 2020-era) with the modern **BEPUphysics v2** NuGet package (handle-based `Simulation` + `BufferPool` + callback structs + `System.Numerics` math). v1 was never actually exercised (physics off by default, no body or collider ever created), so this is a clean plumbing swap rather than a behavior change — physics remains **off by default** (`GameSettings.p_physics == false`). All BEPU v2 specifics are confined behind one new owner class so the rest of the engine never touches a raw physics type. Verified end-to-end by dropping a dynamic box onto a static triangle-mesh ground: it fell along −Z under gravity (z 25.0 → 7.0) and came to rest, with no exceptions.

Added:

- [Engine/Physics/PhysicsSystem.cs](Engine/Physics/PhysicsSystem.cs) — the single seam to BEPUphysics v2. Owns the `Simulation`, the `BufferPool` and the two required callback structs (`PoseIntegratorCallbacks` — applies Z-up gravity `(0,0,-9.81)` per substep, the v2 replacement for v1's `ForceUpdater.Gravity`; `NarrowPhaseCallbacks` — minimal contact material). Exposes everything in engine (XNA) math types: `Step`, `CreateMeshShape`/`CreateBoxShape`, `AddStatic`/`AddStaticMesh`, `AddDynamicBox`, `GetBodyMatrix` (per-frame pose read-back), `SetBodyPosition`, `RemoveStatic`/`RemoveDynamic` (return mesh buffers to the pool), and `Dispose`. `Step` skips non-positive timesteps — v2 throws on `dt <= 0` where v1's `Space.Update` tolerated MonoGame's first 0 ms frame.

Changed:

- [Engine/Engine.csproj](Engine/Engine.csproj) — removed the two `<Reference HintPath>` entries to the vendored `Content\BEPUphysics.dll`/`BEPUutilities.dll`; added `<PackageReference Include="BepuPhysics" Version="2.4.0" />` (pulls `BepuUtilities` transitively), matching the repo's NuGet-for-everything convention.
- [Engine/Engine.cs](Engine/Engine.cs) — `Space _physicsSpace` → `PhysicsSystem _physics`; ctor builds `new PhysicsSystem(new Vector3(0,0,-9.81f))`; the per-frame `space.Update(dt)` → `_physics.Step(dt)`; added `_physics.Dispose()` to `UnloadContent`.
- [Engine/Logic/ScreenManager.cs](Engine/Logic/ScreenManager.cs) and [Engine/Logic/MainSceneLogic.cs](Engine/Logic/MainSceneLogic.cs) — threaded `PhysicsSystem` through `Initialize` in place of `Space`. `AddStaticPhysics` now builds a v2 triangle-mesh collider + `StaticHandle` via `_physics.AddStaticMesh(...)`; `OnSceneChanged` detaches via `_physics.RemoveStatic(...)`. Dropped the unused v1 `Entity PhysicsEntity` parameter from the `AddEntity` overloads.
- [Engine/Entities/BasicEntity.cs](Engine/Entities/BasicEntity.cs) — physics fields changed from v1 objects (`Entity`/`StaticMesh`) to v2 handle structs (`BodyHandle?`/`StaticHandle?`) plus a `PhysicsSystem` reference. `RegisterPhysics`, `CheckPhysics` and `ApplyTransformation` now read the body pose via `_physics.GetBodyMatrix(...)` and write editor drags via `_physics.SetBodyPosition(...)`; the dead static-`AffineTransform` sync branch was removed.
- [Engine/Recources/Helper/MathConverter.cs](Engine/Recources/Helper/MathConverter.cs) — replaced the whole XNA↔`BEPUutilities` converter set with a small XNA↔`System.Numerics` one (`ToNumerics`/`ToXna` for `Vector3` and `Quaternion`), since v2 speaks `System.Numerics` directly.
- [Engine/Recources/Helper/ModelDataExtractor.cs](Engine/Recources/Helper/ModelDataExtractor.cs) — removed the `BEPUutilities.Vector3[]` overload; the XNA overload feeds `PhysicsSystem.CreateMeshShape` unchanged.

Removed:

- The vendored `Engine/Content/BEPUphysics.dll` / `BEPUutilities.dll` references, `Extensions.CopyFromBepuMatrix` (replaced by `PhysicsSystem.GetBodyMatrix`), and a stale unused `using BEPUphysics.Paths;` in [Engine/Renderer/Helper/HelperGeometry/LineHelper.cs](Engine/Renderer/Helper/HelperGeometry/LineHelper.cs).

Notes:

- BEPU v2 is up-axis-agnostic; the engine's Z-up convention is preserved by applying gravity down −Z in the pose-integrator callback. Threading is single-threaded for now (`Timestep` is given a null `IThreadDispatcher`); a real dispatcher can be slotted into `PhysicsSystem` later without touching callers. The `AddDynamicBox`/`CreateBoxShape` helpers are left in place as ready-to-use primitives for future gameplay physics.

## Build: copy FMOD DLLs from thirdparty/ and intro.mp4 into bin

Updated the Engine build to copy the FMOD native libraries from their new `thirdparty/fmod/` home and to deploy the intro video as a loose file, so a fresh checkout runs with audio and the intro without manual file copying.

Changed:

- [Engine/Engine.csproj](Engine/Engine.csproj) — repointed the `fmod.dll`/`fmodL.dll` copy items from the project root to `thirdparty\fmod\`, adding a `<Link>` so they still flatten to the output root (next to the executable, where FMOD loads them) instead of nesting under a `thirdparty/` subfolder in bin. Added a new item that copies `Content\Video\intro.mp4` to the output as `Content\intro.mp4` (flattened via `<Link>`), matching the loose-file path `VideoIntroLogic` reads at runtime.

Notes:

- Verified with `dotnet build`: `fmod.dll`/`fmodL.dll` land at the bin root and `intro.mp4` lands at `bin/.../Content/intro.mp4`, with no stray `thirdparty/` folder copied into the output.

## Docs website: dark/light theme toggle + PNG logo

Replaced the docs site's non-functional account button with a working dark/light theme switcher, reworked the two stale color palettes into proper Dark and Light themes, and swapped the placeholder "s&" logo box for the project's crystal logo as a PNG.

Added:

- [Docs/website/public/logo.png](Docs/website/public/logo.png) — the Obsidian crystal logo, generated from [Docs/Icon.png](Docs/Icon.png) with its opaque black background made transparent (alpha derived from luminance, RGB forced white) so the mark floats on the header and can be recolored per theme. Also used as the favicon (the previous `/favicon.svg` reference was broken).

Changed:

- [Docs/website/src/styles/theme.css](Docs/website/src/styles/theme.css) — replaced the old "ocean"/"emerald" palettes with semantic **Dark** (`:root` default + `[data-theme='dark']`) and **Light** (`[data-theme='light']`) themes. Added a `.brand-logo` rule that flips the white crystal to dark ink in light mode so it stays legible.
- [Docs/website/src/devdocs.tsx](Docs/website/src/devdocs.tsx) — added `theme` state (initialized from the `data-theme` set pre-paint), a `useEffect` that reflects it onto `<html>` and persists to `localStorage`, and a toggle handler. The account button is now a Sun/Moon theme switcher; the logo `<div>` is now an `<img src="/logo.png" class="brand-logo">`.
- [Docs/website/index.html](Docs/website/index.html) — added a pre-paint inline script that picks the theme (saved choice → OS `prefers-color-scheme` → dark fallback) to avoid a flash on load, and pointed the favicon at `/logo.png`.

Notes:

- Default theme follows the OS preference and falls back to dark; the user's choice is remembered across visits. Verified with `tsc -b` (typecheck clean) and `vite build` (logo copied into `dist/`, favicon reference updated).

## Add FMOD audio engine (FmodForFoxes)

Introduced game audio to the engine, which previously had none (only the LibVLC intro video carried sound). Built on FMOD via the FmodForFoxes wrapper for best-in-class 3D spatial audio. Supports 2D one-shot/looping SFX, 3D positional audio (listener follows the active camera, emitters follow world/entity positions with attenuation + Doppler-ready velocity), a master volume, and streamed music. Wired into the engine following the existing subsystem (Load/Initialize/Update/Dispose) convention, with a static `Audio` facade mirroring the `Globals` pattern.

Added:

- [Engine/Recources/AudioManager.cs](Engine/Recources/AudioManager.cs) — `AudioManager` subsystem (FMOD init/update/dispose, 2D `PlaySound`, 3D `PlaySound3D` with fixed or live-tracked positions, streamed `PlayMusic`, `MasterVolume`, `StopAll`) plus the static `Audio` facade. Initializes inside a try/catch so a missing native DLL degrades to a silent no-op instead of crashing the engine. Caches 2D and 3D sounds separately to avoid mode bleed; reaps finished channels each frame. Includes a one-line `ToFmod` coordinate hook (engine is Z-up; flip if 3D panning is mirrored).
- [Engine/Scripting/AudioTestScript.cs](Engine/Scripting/AudioTestScript.cs) — `IScript` that turns its owning entity into a looping 3D emitter in Play mode (3D verification).
- [Engine/Content/Audio/](Engine/Content/Audio/) — generated sample assets: `blip.wav` (mono one-shot), `loop3d.wav` (mono seamless loop), `music.wav` (streamed track).
- [Docs/Audio_Architecture.md](Docs/markdown/Audio_Architecture.md) — architecture doc: the FMOD/FmodForFoxes dependency stack, component map, engine lifecycle wiring, public API, load/playback paths (incl. the `TryPlay` workaround), 3D listener/emitter model and coordinate mapping, the exact native-DLL version requirement, graceful degradation, and how to extend the system.

Changed:

- [Engine/Engine.csproj](Engine/Engine.csproj) — added `FmodForFoxes` + `FmodForFoxes.Desktop` 3.2.0 package references; copy `fmod.dll`/`fmodL.dll` (supplied locally) and `Content/Audio/**` to the output directory.
- [Engine/Logic/ScreenManager.cs](Engine/Logic/ScreenManager.cs) — instantiate `AudioManager` in `Load`, init in `Initialize`, pump `SystemUpdate()` first every frame (intro included) and `UpdateListener(Camera)` after scene update, dispose in `Unload`.
- [Engine/Logic/MainSceneLogic.cs](Engine/Logic/MainSceneLogic.cs) — temporary test hooks: **X** plays a 2D blip, **M** toggles streamed music (remove/gate after verification).

Notes:

- FMOD's native libraries are not redistributed by the NuGet package. To hear audio, download the FMOD Engine (Windows) from fmod.com and place `fmod.dll` + `fmodL.dll` (x64) in `Engine/`; the build copies them next to the executable. Verified the engine boots, runs, and degrades gracefully when they are absent (logs "Audio: FMOD init failed" and continues). The FmodForFoxes managed assembly (net8.0) loads cleanly under net10.0-windows.
- **The native DLL must be exactly FMOD 2.02.37** (`0x00020225` — FmodForFoxes 3.2.0's bundled bindings; FMOD's `init` rejects any other patch with a header mismatch). Init logs the required version and a MATCH/MISMATCH verdict against the loaded DLL. Playback goes through a raw-FMOD helper (`TryPlay`) that wraps the result with the safe single-arg `Channel` ctor, deliberately avoiding FmodForFoxes' 2-arg `Channel(Sound, FMOD.Channel)` ctor — that ctor reads its computed `Sound` property during construction and throws a `NullReferenceException` whenever `playSound` returns a bad channel (e.g. a 2.03.x DLL mismatch). On a bad result we now log the FMOD error code instead of crashing. The detected native version is logged at init for diagnosis.

## Add "Exporting Shaders to Unity URP" guide

Documented how to port a shader out of this MonoGame/HLSL engine into Unity's Universal Render Pipeline, using the froxel volumetric fog as the worked (hardest) example.

Added:

- [Docs/Exporting Shaders to Unity URP.md](Docs/Exporting%20Shaders%20to%20Unity%20URP.md) — guide covering: the three froxel stages to port (inject → accumulate → compose), two porting strategies (compute + `RWTexture3D` recommended, or a faithful 2D-atlas blit port), a MonoGame-FX→URP-HLSL translation reference (sampler macros, matrix `mul` order, depth linearization, coordinate/clip-space flips, URP light/shadow APIs), a uniform→Unity mapping table seeded with current `GameSettings` defaults, a `ScriptableRendererFeature` hook-up note (RenderGraph vs. legacy), a port-blocking gotchas checklist, a validation sequence, and a section generalizing the playbook to other shaders. Sourced from `Froxel.fx`, `DeferredCompose.fx`, `FroxelRenderModule.cs`, and `GameSettings.cs`.

No code changes.

## Add Anvil editor architecture docs

Documented how the Anvil editor is structured and how it interacts with the engine, in a form meant to be extended as the editor grows.

Added:

- [Docs/Editor_Architecture.excalidraw](Docs/excalidraw/Editor_Architecture.excalidraw) — diagram of the editor architecture: the Avalonia UI thread, the MonoGame STA game thread, and the `EditorBridge` seam between them, with the mutation path (UI → engine, queued) and snapshot path (engine → UI, reconciled) drawn as labelled arrows. Colour-coded by thread/role with a legend.
- [Docs/Editor_Architecture.md](Docs/markdown/Editor_Architecture.md) — companion write-up: component map (Anvil + engine sides), the exact `IEditorBridge` contract (snapshot structs, mutation methods, events), startup/HWND embedding, data-flow diagrams, the threading model, and an "How to Extend This" section keyed to the bridge seams. Cross-links the existing engine and Vista UI docs.

No code changes.

## Fix game input leaking outside the engine viewport in Anvil

Addresses [Docs/TODO.md](Docs/TODO.md): holding right-click **outside** the viewport (over the
Inspector / Hierarchy / Console panels) and dragging still moved the engine camera. Reported as
having "reached a roadblock" on a previous attempt.

**Root cause.** The engine reads the *global* Win32 mouse state via `Mouse.GetState()`. When hosted
in Anvil, that global state still reports clicks made over Avalonia panels, so an RMB-drag anywhere
in the editor orbited the camera. An existing gate in `Input.Update` synthesizes an
all-buttons-released `MouseState` while the pointer is outside the viewport — but it was keyed off a
host-forwarded flag (`IsHostPointerOverViewport`) that nothing ever set.

**Why the host can't drive it.** A first fix wired `MonoGameHost` to forward pointer-enter/leave to
the bridge, but it broke input *inside* the viewport: the reparented engine HWND consumes Avalonia's
pointer events over the viewport, so Avalonia sees the pointer leave a panel but never sees it
re-enter the viewport — the flag latched off and the engine ignored all mouse input there.

**Fix.** The gate is now self-contained in the engine and needs no host cooperation.
`Mouse.GetState()` already reports the cursor relative to the engine window's **own** client area, so
a cursor over a surrounding panel lands outside `[0,w) x [0,h)`. `Input.Update` tests that on the
game thread each frame and synthesizes the idle state when hosted and outside. A small latch
(`_dragOwnedByViewport`) remembers when a drag's initial press landed inside the viewport, so an
RMB/MMB/LMB drag that starts in the viewport keeps working even if the cursor strays over a panel
(the window holds mouse capture) — while an RMB press that *starts* over a panel is still ignored.
Standalone `Engine.exe` is unaffected (no host → `IsHostedByEditor` false → gate skipped).

Changes:

- **`Engine/Logic/Input.cs`**: `Input.Update` reads the raw mouse state, computes
  `insideViewport` from the cursor vs. `GameSettings.g_screenwidth/height`, and gates with a
  `_dragOwnedByViewport` / `_anyButtonDownLast` latch so in-viewport drags survive straying out.
  The gate no longer depends on `IsHostPointerOverViewport`.
- `MonoGameHost.cs` is unchanged from before this fix; the `IEditorBridge.IsHostPointerOverViewport` /
  `SetHostPointerOverViewport` API remains but is no longer used by the input gate.

## Fix Anvil viewport rendering at 640×480 on startup until the first manual resize

Follow-up to the bloom-device fix below. With the device no longer dying, the embedded viewport
worked — but on startup it rendered at MonoGame's stale **640×480** default backbuffer
(white/overexposed borders around the scene; the in-engine overlay read `Res: 640 x 480`) and only
snapped to the real container size after the user dragged/resized the window once.

**Root cause.** When hosted in Anvil, `MonoGameHost` reparents the engine HWND under its container
and resizes it to the container's client rect via `SetWindowPos`/`MoveWindow`, pre-syncing
`PreferredBackBuffer` *without* `ApplyChanges` (the device reset must happen on the game thread).
The reconcile that actually rebuilds the swap chain + render targets
(`Engine.ApplyPendingResize` → `ApplyChanges` + `ScreenManager.UpdateResolution`) is driven by
`Engine.ClientChangedWindowSize`, i.e. a `WM_SIZE` the engine observes. But on boot the container
sizing happens before the engine begins ticking its own resize bookkeeping, so no reconcile is
primed and the engine keeps rendering at the 640×480 default the device fell back to. The first
*user* resize finally raises a `WM_SIZE` the engine sees, which reconciles everything — hence
"resize and it goes back to normal".

**Fix.** `Engine.ApplyPendingResize` now self-primes for the first ~30 Update ticks: if the actual
`Window.ClientBounds` differ from the resolution we're rendering at (`GameSettings.g_screenwidth/
height`), it sets `_pendingResize` so the existing reconcile path runs on the game thread and the
viewport fits the container on startup — no manual resize needed. It's idempotent (once reconciled,
`ClientBounds == GameSettings` so it stops firing) and a no-op for the standalone engine, whose
boot window already matches `GameSettings`.

Changes:

- **`Engine/Engine.cs`**: added `_bootReconcileTicks` (starts at 30); `ApplyPendingResize` decrements
  it each tick and flags a resize while the live client bounds don't match the rendered resolution.

## Fix all-white engine viewport — `BloomFilter.Dispose()` was killing the shared GraphicsDevice

Addresses [Docs/TODO.md](Docs/TODO.md): after the previous resize work, resizing no longer crashed
but the embedded engine viewport rendered all white, and `anvil-bridge.log` filled with
`NullReferenceException`s — `SharpDX.Direct3D11.Texture2D..ctor` inside
`BloomFilter.UpdateResolution`, then `Monitor.Enter(null)` inside
`GraphicsDevice.PlatformApplyRenderTargets` on the following frame.

**Root cause.** `BloomFilter.Dispose()` ended with `_graphicsDevice?.Dispose()` and
`_bloomEffect?.Dispose()`. That `_graphicsDevice` is **the engine's one shared `GraphicsDevice`**
(handed in at `BloomFilter.Initialize`, owned by the MonoGame `Game`), and `_bloomEffect` is a
`ContentManager`-owned `Effect`. Critically, `Dispose()` is **not** a shutdown-only path: it is
called from `BloomFilter.UpdateResolution()` on *every resolution change* to recycle the mip render
targets. The renderer reconciles bloom's resolution lazily — the first time `BloomFilter.Draw` sees
`width/height != _width/_height` (which happens as soon as the Anvil container settles to a size
other than the 1280×720 boot default), it calls `UpdateResolution` → `Dispose()` → **disposes the
live device**. The very next `RenderTarget2D` it tries to create (Mip0) NREs (first log entry); the
following frame's `GBufferRenderModule.Draw` → `SetRenderTargets` hits `Monitor.Enter(null)` on the
dead device (second log entry). The device never recovers, so the viewport stays white. This is the
only `Dispose()` in the engine that frees the shared device *and* runs during normal runtime
(`Renderer`/`DebugScreen`/`LightAccumulationModule` also free the device in their `Dispose()`, but
those are shutdown-only and unaffected).

**Fix.** `BloomFilter.Dispose()` now releases **only** the six bloom mip render targets it actually
owns — it no longer touches the shared `GraphicsDevice` or the content-managed `_bloomEffect`.
Resolution changes (startup container-fit and live resize) now recycle just the bloom targets and
the device survives, so the deferred pipeline keeps drawing.

Changes:

- **`Engine/Renderer/RenderModules/PostProcessingFilters/BloomFilter.cs`**: `Dispose()` drops the
  `_graphicsDevice?.Dispose()` and `_bloomEffect?.Dispose()` calls (with a comment explaining why
  this method must only free the mip targets, since it runs on every resolution change).
- **`Editor/Anvil/Controls/MonoGameHost.cs`**: the `CreateWindowEx` P/Invoke's `lpWindowName`
  parameter is now `string?`, clearing the runtime `CS8625` warning at line 232 (the `"STATIC"`
  container is created with a `null` window name).

Verified `dotnet build Engine.slnx` (0 errors) and `dotnet build Editor/Anvil/Anvil.csproj`
(0 warnings, 0 errors).

## Fix Anvil viewport resize — no black areas, no resize/startup crash, engine fills only its cell

Addresses [Docs/TODO.md](Docs/TODO.md). Two coupled problems:

1. **Black bars + skewed image/axes, resolution stuck at 1280×720.** The render resolution stayed
   at the boot default the whole session (the in-engine overlay literally read `Res: 1280 x 720`
   regardless of window size). The final present, `Renderer.DrawMapToScreenToFullScreen`, blits into
   `Rectangle(0, 0, g_screenwidth, g_screenheight)` and the projection aspect ratio also uses those
   values, so a stale resolution leaves the rest of the (larger) backbuffer black and the 3D content
   skewed. `Renderer.UpdateResolution()` (the only thing that refreshes `GameSettings` + rebuilds
   render targets) was reached via `Engine.ClientChangedWindowSize`, whose guard
   (`GraphicsDevice.Viewport != PreferredBackBuffer`) was self-defeating: the host had already made
   both sides equal, so it never ran.

2. **`NullReferenceException` in `GraphicsDevice.CreateSizeDependentResources` on startup and on
   resize.** MonoGame's `WinFormsGameWindow.OnResize` (subscribed to `Form.Resize`) calls
   `UpdateBackBufferSize` → `GraphicsDeviceManager.ApplyChanges()` → `GraphicsDevice.Reset()` **only
   when the form's client size differs from `PreferredBackBuffer`**. Resetting the device from inside
   that `WndProc`/`OnResize` callstack NREs on the **reparented child window** used by the Anvil
   viewport. The startup cases were fixed by pre-syncing `PreferredBackBuffer`, but the resize case
   persisted: Avalonia's `NativeControlHost` resizes the engine HWND *itself* on every layout pass via
   a **cross-thread `SetWindowPos`** that blocks inside `base.ArrangeOverride` until the game thread's
   `WndProc` runs — so no pre-sync done afterward could win that race.

The fix combines two rules:

- **An intermediate container HWND decouples Avalonia's resize from the engine.** A plain `STATIC`
  child window is created under the host HWND and handed back to Avalonia as the native control;
  the engine HWND is reparented *under* it. Avalonia now resizes the container (no graphics device →
  harmless), and the engine HWND is resized **only by us**, using the container's exact client-rect
  pixels for both `PreferredBackBuffer` and `MoveWindow` — so MonoGame's `OnResize` always early-returns
  (identical integers, no DPI rounding guesswork) and never resets the device from a `WndProc`.
- **The real swap-chain + render-target rebuild happens on the game thread, outside any `WndProc`.**

Changes:

- **`Engine/Engine.cs`**: `ClientChangedWindowSize` no longer touches the device — it only sets a
  `_pendingResize` flag (it runs inside the resize `WndProc`). New `ApplyPendingResize()` runs at the
  top of `Update` (game thread, outside `WndProc`, before the `_isActive` gate): it debounces until the
  client size settles, then updates `GameSettings`, sets `PreferredBackBuffer`, calls `ApplyChanges()`,
  sets `GraphicsDevice.Viewport = new Viewport(0, 0, w, h)`, and calls `_screenManager.UpdateResolution()`.
  This is the single, safe place the swap chain is reset. (Also fixes the stuck-resolution/black-bar
  bug in standalone, which now rebuilds render targets on user-drag too.)
- **`Editor/Anvil/Controls/MonoGameHost.cs`**: creates the `STATIC` container in
  `CreateNativeControlCore` and returns it; `ReparentGameWindow(parent)` parents the engine under the
  container. `ResizeEngineToContainer` (called from `ArrangeOverride`) reads the container's client
  rect and calls `SyncPreferredBackBuffer(w, h)` (sets `PreferredBackBuffer` **without** `ApplyChanges`
  — no UI-thread device reset) immediately before `MoveWindow`-ing the engine to the same size. The
  old `DispatcherTimer` resize-debounce path was removed (debounce now lives in the engine).
  `DestroyNativeControlCore` destroys the container. As a final safety net, the game-thread message
  pump now wraps `DispatchMessage` in try/catch so any stray resize can never hard-crash the app.

## Migrate solution to XML `.slnx` format

Replaced the legacy MSBuild `Engine.sln` with the XML-based `Engine.slnx` (supported natively by
the .NET 10 SDK, here 10.0.203). The new solution is functionally equivalent: it carries the same
`Any CPU`/`x86` platforms, builds the `Engine` (pinned to `x86`) and `Vista` projects, and keeps
`Anvil` excluded from the solution build (`<Build Project="false" />`) — matching the old `.sln`,
which had no build-config entries for Anvil. Verified `dotnet build Engine.slnx` and the
argument-less `dotnet build` both succeed (0 errors) after the swap.

- **`Engine.slnx`** (new): XML solution replacing `Engine.sln`.
- **`Engine.sln`** (removed): legacy solution file.
- **`.vscode/tasks.json`**: `publish` and `watch` tasks now point at `Engine.slnx`.
- **`bootstrap.bat`**, **`CLAUDE.md`**: build commands updated to reference `Engine.slnx`.
  (The historical `Engine.sln` mention in this changelog is left as-is.)

## Fix "+" add-button crash on Point Light + data-driven add-object catalog

Addresses [Docs/TODO.md](Docs/TODO.md): clicking the editor's **"+"** button (→ Point Light)
hard-crashed the engine and added nothing. The add path itself was already wired and fully
exception-protected (`EditorBridge.EnqueueAddPointLight` → `MainSceneLogic.AddPointLight` logs
`AddPointLight ok`), so the add *succeeded* — the crash was in the **render** path, which is not
wrapped in try/catch. The empty editor scene starts with zero point lights, so
`PointLightRenderModule.Draw` first executes its body the frame *after* a light is added. Its very
first line, `deferredPointLightParameter_Time.SetValue(...)` (gated only on
`GameSettings.g_VolumetricLights`, which defaults true), dereferenced a **null** `EffectParameter`:
the `Time` uniform is commented out of `DeferredPointLight.fx`, and MonoGame returns `null` (it does
not throw) for a missing parameter — so the `.SetValue` NRE'd in the unguarded render loop and took
down the process. No message surfaced because the froxel module logs via `Debug.WriteLine`
(compiled out of Release) and there is no global unhandled-exception handler.

- **`Engine/Renderer/RenderModules/DeferredLighting/PointLightRenderModule.cs`**: defense in depth
  so a point light can never hard-crash the engine again. Added one-time-log helpers (`WarnOnce`/
  `LogOnce`) that write to `anvil-bridge.log` via the existing `EditorBridge.Log` sink. Guarded the
  actual crash line — the `Time` parameter is only `SetValue`d when non-null (else logged once) —
  and the `SphereMeshPart` proxy mesh. Routed all six technique `Passes[0].Apply()` calls through a
  null-tolerant `ApplyTechnique` (`ApplyShader` now returns a bool so the matching
  `DrawIndexedPrimitives` is skipped when a technique is missing). Wrapped the per-light draw loop in
  try/catch → `LogOnce` so the first exception's full stack trace is always captured without
  per-frame spam.
- **`Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs`**: defensive null-check
  on the one direct `Parameters["FroxelInjectionTexture"].SetValue(...)` access (now `?.`), matching
  the `?.`-guarded sibling parameters in the same module.
- **`Editor/Anvil/Models/AddableObjectType.cs`** (new): a small catalog entry
  `{ string DisplayName; IRelayCommand AddCommand; }` whose command runs an
  `Action<IEditorBridge>` against the live bridge. The data-driven catalog makes adding a future
  object type a single line — no XAML.
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: added an `AddableObjects`
  `ObservableCollection<AddableObjectType>`, populated in `AttachBridge` with the single **Point
  Light** entry (per the TODO's "Pointlights only for now"), with commented-out Directional Light /
  Cube one-liners as the documented extension point. The existing `EnqueueAddDirectionalLight` /
  `EnqueueAddBasicEntity` bridge methods are kept (still used by Assets-panel drag-to-scene and as
  the re-enable hooks).
- **`Editor/Anvil/Views/MainWindow.axaml`**: the "+" `MenuFlyout` is now data-bound to
  `AddableObjects` (via the `#RootWindow` DataContext reach-back), with an `ItemContainerTheme`
  binding each generated `MenuItem`'s `Header`/`Command` to the `AddableObjectType`. The menu shows
  exactly one item, "Point Light", today.

## Texture binding pipeline + delete for meshes & entities

Addresses [Docs/TODO.md](Docs/TODO.md): models spawned from the editor rendered
untextured ("white"), `error.png` never appeared on texture-less models, the error
mesh showed no textures, and there was no way to delete a scene entity or an imported
model. Root cause: `EditorBridge.EnqueueAddBasicEntity` always overrode every model
with one flat `MaterialEffect`, which `MeshMaterialLibrary` only bypasses (using the
model's embedded per-mesh-part materials) when the passed material is `null`. There
was also no texture naming-convention binding at all.

- **`Engine/Recources/Assets.cs`**: added a per-model dynamic-material registry
  (`DynamicMaterials`, `RegisterMaterial`, `TryGetDynamicMaterial`, event
  `MaterialRegistered`) paralleling the dynamic-model registry. `UnregisterModel`
  removes an imported model + its material. `MakeMaterial` exposes the existing
  `CreateMaterial` path to the importer. New `BindEmbeddedTextures(Model)` (tolerant
  generalisation of `ProcessModel`) converts an FBX's embedded `BasicEffect` textures
  to engine materials; called on `ErrorModel` at load so the error mesh shows its own
  textures. `ReimportExistingModels` now also calls `TryBindStoredTextures` so textures
  dropped in a previous session are re-bound on launch (durable across restarts).
- **`Engine/Recources/AssetImporter.cs`**: convention-based texture binding. New
  `ClassifyTexture` maps a filename suffix (case-insensitive, after the last `_`) to a
  slot — `_BaseColor`/`_Albedo`/`_Diffuse`→albedo, `_Normal`, `_Roughness`,
  `_Metallic`, `_Mask`/`_Opacity`, `_Height`/`_Displacement`. `BindTextures` copies
  dropped images into `Art/Models/{key}/Textures/`, builds them via mgcb, and binds a
  material (no albedo ⇒ keeps the error material). `ComposeMaterial` builds from
  already-built textures; `ImportFbx` creates the `Textures/` folder and binds any
  convention-named siblings. `DeleteModelContent` removes a model's `Content.mgcb`
  blocks (model + textures) and its source/built/executable folders.
  `ListModelTextures` (static) lets the editor list a model's textures.
- **`Engine/Editor/EditorBridge.cs` / `IEditorBridge.cs`**: `EnqueueAddBasicEntity`
  material selection is now: ERROR mesh → `null` (own textures); convention-bound import
  → its material; import w/o textures → `ErrorMaterial` (visible `error.png`); built-in
  → `null` (embedded per-mesh-part materials, so Sponza/Helmets keep textures). New ops
  `EnqueueImportTextures` (binds + updates already-placed instances in place via
  `ApplyMaterialToExistingInstances`/`CopyMaterialSlots`) and `EnqueueDeleteModelAsset`
  (unregister + delete from disk). New reads `GetModelTextureFiles`, `IsDeletableModel`.
  Subscribes to `Assets.MaterialRegistered` → `ModelRegistryChanged` for UI refresh.
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: the Assets "Textures" folder
  now holds one subfolder per imported model (id `tex:{key}`), listing dropped texture
  files. New `ImportTexturesFromDisk`, `DeleteModelAsset`, `DeletableModelKeyFor`, and a
  `DeleteSceneObject` command (the existing `DeleteSelected` command is now wired to UI).
- **`Editor/Anvil/Views/MainWindow.axaml(.cs)`**: `AssetsPanel_Drop` routes image files
  to the imported model whose Textures-folder/mesh node they were dropped on (`.fbx`/
  `.obj` still import as models). Context-menu **Delete** on Hierarchy items
  (`DeleteSceneObjectCommand`) and Assets items (with a confirmation dialog, since it
  deletes files from disk); **Delete** key bound on both trees.

## Mesh → scene gestures + corrected error model

Follow-up to the robust-import work: dragging a mesh from the Meshes folder into
the scene didn't spawn anything, and the error model was replaced with a working
FBX.

- **`Editor/Anvil/Views/MainWindow.axaml.cs` / `MainWindow.axaml`**: the
  `AssetsTree_PointerPressed` drag-start handler was attached via a XAML attribute,
  which ignores handled events — but `TreeViewItem` marks `PointerPressed` as handled
  for selection, so the handler never ran and no drag began. It's now registered in
  code-behind on `AssetsTreeView` with `handledEventsToo: true` (bubble) so it fires
  regardless. Added an `AssetsTree_DoubleTapped` handler (also `handledEventsToo`) as a
  reliable drag-free way to add a mesh to the scene: double-click it. Shared model-key
  resolution extracted into `ModelKeyFromVisual`. Removed the redundant XAML
  `PointerPressed`/`PointerMoved` attributes.
- **`Engine/Content/Content.mgcb`**: added `Art/Error/ERRORText.fbx` (the corrected
  error mesh, supplied with its sibling textures). The earlier broken `Art/error.fbx`
  is gone; `Art/error.png` remains the default error albedo for imported models.
- **`Engine/Recources/Assets.cs`**: `ErrorModel` now loads `Art/Error/ERRORText`
  (still falling back to the `Cube` primitive if it can't load).

## Robust FBX import — no-crash, texture-optional, error fallbacks

Addresses the issues in [Docs/TODO.md](Docs/TODO.md): imports failed and crashed
when an FBX's textures lived in a subfolder, the broken `Content.mgcb` entry was
left behind (breaking the next startup build), and imported models did not survive
a restart. The import pipeline now never throws, never corrupts `Content.mgcb`,
always yields a draggable model (real or placeholder), and rehydrates prior imports
on launch.

- **`Engine/Content/Content.mgcb`**: added a build entry for `Art/error.png`
  (the fallback / "this asset is broken" texture). `Art/error.fbx` was intentionally
  **not** added — the supplied file has a material with an empty texture slot that
  crashes MonoGame's FBX importer at import time; re-export it without empty texture
  slots to use a custom error mesh.
- **`Engine/Recources/Assets.cs`**: added `ErrorModel` / `ErrorTexture` /
  `ErrorMaterial`, loaded in `Load()` (wrapped so a missing error asset never blocks
  boot). `ErrorModel` falls back to the `Cube` primitive since `error.fbx` is not
  buildable; `ErrorMaterial` uses `error.png` as its albedo. New
  `ReimportExistingModels()` scans the built `Content/Art/Models/{key}/{key}.xnb`
  outputs at startup and re-registers each via `RegisterModel`, so models imported in
  a previous session reappear in the Meshes folder and saved scenes can resolve them.
- **`Engine/Recources/AssetImporter.cs`**: the sibling-texture scan is now
  **recursive and preserves relative paths** (`SearchOption.AllDirectories` +
  `Path.GetRelativePath`), so textures stored in subfolders resolve where the
  ModelProcessor expects them. `AppendMgcbEntries` returns the appended text;
  `RunMgcbBuild` is wrapped so a build failure **rolls back** the just-added
  `Content.mgcb` block (new `RemoveMgcbBlock`), deletes the copied sources, and
  registers the error model under the requested key instead of throwing. The final
  `ContentManager.Load` is likewise guarded — `ImportFbx` always returns a registered
  key (real model or error placeholder).
- **`Engine/Editor/EditorBridge.cs`**: `EnqueueAddBasicEntity` now spawns
  runtime-imported models with `ErrorMaterial` (so they render with the `error.png`
  default texture instead of the base red material), keeps `BaseMaterial` for
  built-ins, and substitutes `ErrorModel` if a model's geometry never loaded.
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: `OnBridgeSnapshot` now also
  repopulates the Meshes folder when it is empty but the bridge reports models —
  closing the race where built-in models existed (and were registered) but were not
  visible/draggable because the folder was built before the bridge reported them.

## Runtime asset import pipeline (drop FBX → drag into scene)

Addresses [Docs/TODO.md](Docs/TODO.md): replace hard-coded scene creation with an
editor-driven flow where the user drops a `.fbx` (plus sibling textures) on the
Anvil Assets panel and drags from there into the Hierarchy to spawn a `BasicEntity`.

- **`Engine/Recources/Assets.cs`**: added dynamic registry alongside the existing
  hard-coded public-field model list. `RegisterModel(key, ModelDefinition)`
  with auto-dedup (`_2`, `_3`, …) and a `ModelRegistered` event. `Load()` now
  caches `Content`/`GraphicsDevice` so `AssetImporter` can reuse them.
- **`Engine/Recources/AssetImporter.cs`** (new): runtime importer that copies the
  source file into `Engine/Content/Art/Models/{key}/`, scans sibling images
  (.png/.jpg/.tga/.dds/.bmp), appends matching `Content.mgcb` entries under a
  cross-process mutex, invokes `mgcb` (via `dotnet mgcb`, falling back to
  `MGCB_PATH` env var or the legacy MSBuild install) to compile, copies the
  resulting `.xnb` next to the executable, then loads through the live
  `ContentManager` and calls `Assets.RegisterModel`. Mirrors the
  `ShaderManager.ShaderChanged` hot-reload pattern but without its hard-coded
  mgcb path.
- **`Engine/Editor/IEditorBridge.cs` / `EditorBridge.cs`**: new
  `EnqueueImportModel(string sourcePath, Action<string> onCompleted)` op and
  `ModelRegistryChanged` event. `BuildModelKeys` now unions
  `Assets.DynamicModels` with the reflection-scanned hard-coded fields, so
  imported models flow through the existing `EnqueueAddBasicEntity` path.
- **`Engine/Logic/MainSceneLogic.cs`**: `SetUpEditorScene` →
  `SetUpEmptyEditorScene`. Removed hard-coded Sponza spawn, 11×11 plane grid,
  Stanford dragon, physics sphere + 10-sphere roughness sweep, decal, and two
  foreground point lights. Boot scene is now just the editor + main cameras,
  environment sample, SDF generator, and one sun-like directional light.
  Removed the unused `testEntity` field.
- **`Editor/Anvil/Views/MainWindow.axaml`**: Assets panel `Border` now
  `AllowDrop`s file drags. Assets `TreeView` (`AssetsTreeView`) exposes
  `PointerPressed`/`PointerMoved` for drag-start. Hierarchy `TreeView`
  (`HierarchyTreeView`) accepts drops carrying the custom `obsidian/modelKey`
  data format.
- **`Editor/Anvil/Views/MainWindow.axaml.cs`**: handlers `AssetsPanel_DragOver`/
  `AssetsPanel_Drop` (accept Windows-Explorer file drops, route .fbx/.obj into
  `ImportFbxFromDisk`), `AssetsTree_PointerPressed`/`AssetsTree_PointerMoved`
  (threshold-gated drag start of mesh nodes, carries the model key),
  `Hierarchy_DragOver`/`Hierarchy_Drop` (accept model keys, call
  `AddEntityFromAsset`).
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: subscribed to
  `bridge.ModelRegistryChanged`; `BuildAssets()` reduced to empty folder stubs
  + live `Meshes` folder repopulated from `AvailableModelKeys` whenever the
  registry changes. New methods: `ImportFbxFromDisk(path)`,
  `AddEntityFromAsset(modelKey)`, `RefreshMeshAssetsFolder()`,
  `OnBridgeModelRegistryChanged()`.

## Editor overhaul (dev branch)

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

### Phase 9 — HelperSuite removal + post-processing migration

- **HelperSuite project deleted.** `HelperSuite/` removed from disk;
  `ProjectReference` dropped from `Engine.csproj`; project + per-config
  entries dropped from `Engine.sln`. `Engine/Logic/GUILogic.cs` deleted
  (entirely depended on HelperSuite controls). All `using HelperSuite.*`
  removed from `Engine.cs`, `ScreenManager.cs`, `EditorLogic.cs`,
  `ShaderManager.cs`. `GUIControl.Initialize` call removed from
  `Engine.Initialize`; the `!GUIControl.UIWasUsed` gate in `EditorLogic.Update`
  is gone. `ScreenManager` no longer holds `_guiLogic` or `_guiRenderer` —
  Load/Initialize/Update/Draw/Dispose simplified accordingly.
- **`IEditorBridge.EnqueueGameThreadAction(Action)`** — generic queue for
  arbitrary engine-thread work. The post-processing VM uses it so shader
  parameter setters in `GameSettings`/`Shaders` are pushed from the UI
  thread but actually executed between frames on the game thread.
- **`PostProcessingViewModel`** (Anvil) — mirrors the toggles that used to
  live in HelperSuite's right-side panel: TAA/Tonemap/WhitePoint/Exposure/
  S-Curve/Chromatic Aberration/Color Grading, SSR (enable + stochastic +
  temporal noise + firefly + thresholds + sample counts), SSAO (enable +
  blur + samples + radius + strength), Bloom (enable + threshold + 5 MIP
  radius/strength pairs), Viewport (highlight meshes + SDF distance/volume).
  One-shot read from `GameSettings` on attach; setters marshal writes
  through `EnqueueGameThreadAction`.
- **Inspector view switch.** Added `InspectorView` to
  `MainWindowViewModel` (`"Selection"` / `"PostProcessing"`) +
  `SetInspectorViewCommand`. `IsInspectorSelectionView` /
  `IsInspectorPostProcessingView` gate the two ScrollViewers in the
  inspector pane. Header chips ("Selection" / "Post FX") flip between them.
- **`Window > Post Processing` menu entry** replaces the redundant
  `Window > Assets` item (Assets was already a top-level menu). Clicking it
  invokes `SetInspectorViewCommand` with `"PostProcessing"`.
- **Docs**: `CLAUDE.md` solution-projects list pruned. `Docs/TODO.md`
  cleared — the previous items all landed in Phase 8 / Phase 9.
