# Sound Engine: Architecture

> This document describes the **game audio subsystem** — how Obsidian plays SFX, 3D
> positional sound, and music. For the editor see [Editor_Architecture.md](Editor_Architecture.md);
> for the in-engine UI layer see [VistaUI_Architecture.md](VistaUI_Architecture.md).

---

## Overview

The engine had **no game audio** until this subsystem was added (only the LibVLC-played
`intro.mp4` carried sound). Audio is now provided by **FMOD**, the industry-standard audio
middleware, accessed from C# through the lightweight **[FmodForFoxes](https://github.com/Martenfur/FmodForFoxes)**
wrapper. FMOD was chosen over MonoGame's built-in `SoundEffect`/`AudioEmitter` for its
superior 3D spatialisation, streaming, and headroom (occlusion, reverb zones) for later.

The subsystem deliberately mirrors the engine's existing conventions:

- A single manager class, **`AudioManager`** (`Engine/Recources/AudioManager.cs`), with the
  familiar **Load → Initialize → Update → Dispose** lifecycle, owned and driven by
  [`ScreenManager`](../../Engine/Logic/ScreenManager.cs) exactly like the renderer, scene,
  and debug subsystems.
- A static facade, **`Audio`**, mirroring the `Globals` convention so gameplay code can call
  `Audio.PlaySound("blip")` from anywhere without threading a reference through.

It supports four things: **2D one-shot/looping SFX**, **3D positional audio** (listener
driven by the active camera, emitters by world or live entity positions), a **master volume**,
and **streamed music**.

---

## The Dependency Stack

```
  Gameplay / engine code
        │  Audio.PlaySound("blip")  /  Audio.PlaySound3D(...)  /  Audio.PlayMusic(...)
        ▼
  Audio            (static facade — null-safe, forwards to the live instance)
        ▼
  AudioManager     (this subsystem: caching, 3D listener/emitters, lifecycle, master volume)
        ▼
  FmodForFoxes     (managed C# wrapper: Sound, Channel, Listener3D, CoreSystem, FmodManager)
        ▼
  FMOD C# bindings (the FMOD namespace: FMOD.System, FMOD.Channel, FMOD.RESULT, …)
        ▼
  fmod.dll / fmodL.dll   (native FMOD Core — supplied locally by the dev, see below)
```

Two NuGet packages provide the managed layers: **`FmodForFoxes`** and
**`FmodForFoxes.Desktop`** (both `3.2.0`), referenced in
[`Engine/Engine.csproj`](../../Engine/Engine.csproj). The **native** libraries are *not*
redistributed by NuGet (FMOD licensing) and must be supplied by the developer.

---

## Component Map

| File | Role |
|------|------|
| `Engine/Recources/AudioManager.cs` | The subsystem. Contains both `AudioManager` (instance) and the static `Audio` facade. |
| `Engine/Logic/ScreenManager.cs` | Owns the `AudioManager`; wires it into the engine lifecycle (instantiate, init, per-frame pump + listener, dispose). |
| `Engine/Entities/Camera.cs` | Source of the 3D **listener** transform (`Position`, `Forward`, `Up`). |
| `Engine/Scripting/AudioTestScript.cs` | `IScript` that turns its owning entity into a looping 3D emitter in Play mode (verification). |
| `Engine/Logic/MainSceneLogic.cs` | Temporary keyboard test hooks (**X** = blip, **M** = music). Also the owner of the active `Camera`. |
| `Engine/Content/Audio/` | Loose audio files loaded directly by FMOD: `blip.wav`, `loop3d.wav`, `music.wav`. |
| `Engine/Engine.csproj` | Package references + output-copy rules for the native DLLs and audio files. |
| `fmod.dll`, `fmodL.dll` | Native FMOD Core (x64), placed in `Engine/`, copied next to the executable. **Dev-supplied.** |

---

## Lifecycle & Engine Wiring

`AudioManager` plugs into the four `ScreenManager` phases. Order matters — the relevant hook
points are:

| Phase | `ScreenManager` site | Audio call | Why here |
|-------|----------------------|------------|----------|
| **Load** | after `_assets.Load(...)` | `_audio = new AudioManager();` | Construct alongside the other subsystems. |
| **Initialize** | after `_renderer.Initialize(...)` | `_audio.Initialize("Content");` | Boot FMOD once; sets the `Audio.Instance` facade target. |
| **Update** (top, *before* the intro early-return) | first line of `Update(...)` | `_audio?.SystemUpdate();` | FMOD must be pumped **every** frame, including during the intro video. |
| **Update** (after scene logic) | after `_sceneLogic.Update(...)` | `_audio?.UpdateListener(_sceneLogic.Camera);` | The listener and 3D emitters reconcile *after* this frame's positions are final. |
| **Unload** | in `Unload(...)` | `_audio?.Dispose();` | Stop channels, release sounds, shut FMOD down cleanly. |

`Engine.cs` itself is unchanged — it already delegates `Update`/`Unload` to `ScreenManager`,
so the audio subsystem rides the existing game loop. The same wiring works whether the engine
runs standalone (`Program.cs`) or embedded in the Anvil editor (the editor drives `RunOneFrame`).

### Per-frame flow

```
ScreenManager.Update(gameTime, isActive)
  │
  ├─ _audio.SystemUpdate()              → FmodManager.Update()   (always; intro included)
  │
  ├─ (if intro) return
  │
  ├─ _editorLogic.Update(...)
  ├─ _sceneLogic.Update(...)            → camera + entity positions advance
  ├─ _audio.UpdateListener(camera)      → listener attrs + 3D emitter attrs + reap stopped
  ├─ _renderer.Update(...)
  ├─ _debug.Update(...)
  └─ _bridge?.DrainAndPublish()
```

---

## Public API

### `AudioManager` (instance)

```csharp
void Initialize(string contentRoot = "Content");   // boot FMOD, create listener, set facade
void SystemUpdate();                                // FmodManager.Update() — every frame
void UpdateListener(Camera cam);                    // listener + tracked 3D emitters + reaping

// 2D
Channel? PlaySound(string name, float volume = 1f, float pitch = 1f, bool loop = false);

// 3D positional (fixed point, or a live-tracked position source)
Channel? PlaySound3D(string name, Vector3 worldPosition, float volume = 1f, bool loop = false,
                     float minDist = 1f, float maxDist = 10000f);
Channel? PlaySound3D(string name, Func<Vector3> positionSource, float volume = 1f, bool loop = false,
                     float minDist = 1f, float maxDist = 10000f);
void StopInstance(Channel channel);

// music (streamed)
Channel? PlayMusic(string name, float volume = 1f, bool loop = true);
void StopMusic();  void PauseMusic();  void ResumeMusic();

// global
float MasterVolume { get; set; }     // 0..1, applied to FMOD's master channel group
void StopAll();
void Dispose();
```

### `Audio` (static facade)

Each method forwards to the live `AudioManager` via a null-conditional (`Instance?.…`), so
**every call is safe before audio has initialized or if FMOD failed to load** — it simply does
nothing. This is what gameplay code uses:

```csharp
Audio.PlaySound("blip");                                   // 2D one-shot
Audio.PlaySound("engine_hum", volume: 0.5f, loop: true);   // 2D loop
Audio.PlaySound3D("explosion", worldPos);                  // 3D one-shot at a point
Audio.PlaySound3D("siren", () => entity.Position, loop: true); // 3D follows an entity
Audio.PlayMusic("music");                                   // streamed, looping
Audio.MasterVolume = 0.8f;
```

> **Return type note:** FmodForFoxes' `Channel` is a **value type** (a thin handle wrapper),
> so the play methods return `Channel?` — `null` means "did not play" (not initialized, file
> missing, or FMOD rejected it). Callers usually ignore the return value; keep it only if you
> need to stop/inspect a specific instance.

---

## How a Sound is Loaded

`GetOrLoad(name, streamed, is3d)` is the single load path:

1. Looks the sound up in a cache. There are **two** caches — `_cache2D` and `_cache3D` — keyed
   by name (case-insensitive). They are separate so that toggling FMOD's 3D mode on a shared
   `Sound` can never make a 2D one-shot suddenly spatialise (or vice-versa).
2. On a miss, resolves the path as `"Audio/" + name (+ ".wav" if no extension)` and loads via
   FMOD: `CoreSystem.LoadSound` for SFX (decoded into memory) or `CoreSystem.LoadStreamedSound`
   for music (streamed from a buffer). File errors are caught and logged, returning `null`.
3. Sets `sound.Is3D` and caches the result.

### Path resolution

FmodForFoxes' `FileLoader` resolves a load path as
`TitleContainer.OpenStream(Path.Combine(contentRoot, path))`, which is relative to the
**application base directory** (the `bin` folder), independent of the working directory. With
`contentRoot = "Content"`, a call to `PlaySound("blip")` reads
`…/bin/.../Content/Audio/blip.wav`. The build copies `Content/Audio/**` there automatically
(see [Asset Pipeline](#asset-pipeline)).

---

## How Playback Works (and the `TryPlay` workaround)

All three play methods route through a single private helper, **`TryPlay`**:

```csharp
FMOD.RESULT result = CoreSystem.Native.playSound(sound.Native, default, paused, out FMOD.Channel native);
if (result != FMOD.RESULT.OK) { /* log + return false */ }
channel = new Channel(native);   // SAFE single-arg ctor
```

This **deliberately bypasses** FmodForFoxes' high-level `sound.Play()`. That path constructs a
`Channel` via the 2-arg `Channel(Sound, FMOD.Channel)` constructor, which — during
construction — reads its own *computed* `Sound` property
(`getCurrentSound → getUserData → PointerLinker.Get`). When `playSound` returns a bad channel
(most commonly an **FMOD native/bindings version mismatch**), that lookup returns `null` and
the constructor throws a `NullReferenceException`.

`TryPlay` instead calls the raw FMOD API, **checks the result first**, and wraps the native
channel with the safe single-arg `Channel(FMOD.Channel)` constructor (which only stores the
handle). The result: when FMOD is healthy the sound plays; when it isn't, we **log the FMOD
error and degrade to a no-op** instead of crashing the engine.

Once a channel exists, properties (`Volume`, `Pitch`, `Looping`, `Is3D`, `Position3D`,
`MinDistance3D`, …) are setters that operate directly on the native handle.

---

## 3D Positional Audio

FMOD spatialisation = **one listener** (the "ears") + **per-channel 3D attributes**, reconciled
each frame by `FmodManager.Update()`.

- **Listener** — a single `Listener3D`, created in `Initialize`. Every frame `UpdateListener`
  copies the active camera's `Position`, normalized `Forward`, and `Up` into it (velocity is
  currently zero; supply `(pos − lastPos)/dt` later for Doppler).
- **Emitter (fixed)** — `PlaySound3D(name, Vector3, …)` plays **paused**, sets `Is3D`,
  `MinDistance3D`/`MaxDistance3D`, the position and zero velocity, then unpauses — so the very
  first audible frame is already positioned and attenuated.
- **Emitter (tracked)** — `PlaySound3D(name, Func<Vector3>, …)` additionally records the
  channel + its position source in `_tracked`. Each frame `UpdateListener` writes
  `channel.Position3D = positionSource()` for every tracked entry, and **reaps** any whose
  `IsPlaying` is false (FMOD reuses channel handles, so this is checked every frame).

### Coordinate mapping

The engine world is **Z-up, right-handed**; FMOD Core defaults to a left-handed frame. A single
helper isolates the conversion:

```csharp
private static Vector3 ToFmod(Vector3 v) => v;   // identity (default)
// private static Vector3 ToFmod(Vector3 v) => new Vector3(v.X, v.Z, v.Y);  // alt remap
```

Identity preserves **distances**, so attenuation is always correct; only left/right **panning**
is sensitive to handedness. If a 3D test pans the wrong way, switch to the commented remap.
Because the listener and all emitters pass through the same `ToFmod`, they always stay in the
same space.

### Distance tuning

World units here are large (the camera sits at ≈ `(-88, -11, 4)`; light radii ≈ 20). The default
`minDist = 1`, `maxDist = 10000` is a starting point — tune per sound so falloff feels right
(e.g. `minDist ≈ 5`, `maxDist ≈ 150`).

---

## Music

`PlayMusic` loads the track **streamed** (`LoadStreamedSound`), stops any current track, plays
it, and stores the channel in `_music`. It is plain 2D (no spatialisation). `StopMusic`,
`PauseMusic`, and `ResumeMusic` operate on that single stored channel. Only one music track
plays at a time.

## Master Volume

`MasterVolume` clamps to `0..1` and applies to FMOD's **master channel group**
(`CoreSystem.Native.getMasterChannelGroup → setVolume`), so it scales *everything* — SFX, 3D,
and music — at once.

---

## Asset Pipeline

Audio files are **not** part of the MonoGame content pipeline (`Content.mgcb`). FMOD reads loose
files directly, so they are simply copied to the output. From `Engine.csproj`:

```xml
<ItemGroup>
  <None Update="fmod.dll"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  <None Update="fmodL.dll"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  <None Include="Content\Audio\**\*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>Content\Audio\%(RecursiveDir)%(Filename)%(Extension)</Link>
  </None>
</ItemGroup>
```

Supported formats include WAV, OGG, and MP3. For 3D point sources, **mono** files are
recommended. The sample assets (`blip.wav`, `loop3d.wav`, `music.wav`) are mono 16-bit PCM.

---

## Native Libraries & Version Requirement

The native `fmod.dll` / `fmodL.dll` (x64) must be placed in `Engine/`. **They must match the
exact FMOD version the FmodForFoxes 3.2.0 bindings were built against: FMOD `2.02.37`
(`0x00020225`).** FMOD's `System::init` rejects any other version (even a different patch) with
a header mismatch.

`Initialize` makes this diagnosable: it logs the required version up front, and after init logs
the actually-loaded DLL version with a **`MATCH` / `MISMATCH`** verdict. On failure it
distinguishes a *version mismatch* from a *missing DLL*. Logs go to the `EditorBridge` log
(`anvil-bridge.log` on the Desktop). Use `fmodL.dll` (logging build) for Debug, `fmod.dll` for
Release.

### Graceful degradation

If FMOD can't initialize (missing or wrong-version DLL), `Initialize` catches the exception,
leaves `_initialized = false`, and logs why. Every subsequent call short-circuits: the engine
runs completely normally, just **silent**. Nothing in the audio subsystem can crash the engine.

---

## Lifecycle Diagram

```
 Load        Initialize                 Update (per frame)            Unload
 ─────       ────────────────           ──────────────────           ──────────────
 new         FmodManager.Init           SystemUpdate()  ── FmodManager.Update()
 Audio       new Listener3D             UpdateListener()── listener + 3D emitters
 Manager     Audio.Instance = this                                    StopAll()
             log MATCH/MISMATCH         PlaySound / PlaySound3D /     release Sounds
                                        PlayMusic (on demand)         Listener3D.Destroy()
                                                                      FmodManager.Unload()
```

---

## Testing

There is no automated test project, so audio is verified in-engine (see
[CHANGELOG.md](../../CHANGELOG.md) entry and the plan). Temporary hooks in `MainSceneLogic`:
**X** plays a 2D blip, **M** toggles streamed music. Attaching `AudioTestScript` to an entity
and entering Play mode exercises the 3D path — fly the camera around it to hear attenuation and
panning. Remove/gate the X/M hooks once satisfied.

---

## Extending the System

- **More listeners / split-screen** — `Listener3D` already supports multiple instances; add and
  position extra listeners.
- **Doppler** — pass a real velocity into `_listener.SetAttributes` and `channel.Velocity3D`
  (compute from per-frame position deltas).
- **Buses / categories (SFX vs music vs UI)** — give each category its own FMOD `ChannelGroup`
  and play into it (currently everything plays on the master group), enabling per-category
  volume.
- **Editor integration** — the Anvil editor already has an `Audio` asset-folder stub
  (`AssetKind.Audio`). Surfacing audio assets and a per-entity emitter component there is the
  natural next step (see [Editor_Architecture.md](Editor_Architecture.md) for the bridge pattern
  to follow).
- **FMOD Studio banks** — `FmodManager.Init` can be switched from `FmodInitMode.Core` to
  `CoreAndStudio` to load `.bank` files authored in FMOD Studio, for event-driven audio.
```
