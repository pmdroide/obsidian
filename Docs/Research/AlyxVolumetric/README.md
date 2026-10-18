# Alyx volumetric lighting: evidence and Obsidian implementation draft

Investigation date: 2026-10-14. Author: Codex.

The installed Alyx artifacts describe a compute-based volumetric system with
froxel output, lighting clipmaps, indirect fog lighting, temporal history and
stereo support. Obsidian already has the basic froxel renderer. The useful work
is improving its transport, history and baked-light sampling, rather than adding
a second fog renderer.

**Status (2026-10-14):** the draft is implemented in the engine (`Froxel.fx`,
`DeferredCompose.fx`, `FroxelRenderModule`, Environment > Volumetric Fog > Indirect);
see `Docs/markdown/Scene_Lighting_and_Fog.md`. Not adopted yet: fog on forward
transparency and clipmaps. One deviation: prefixes store optical depth instead of
transmittance, because thin-slice transmittance rounds away in RGBA16F during the scan.

This report separates actual REA Evidence, supplementary observations of local
files, inferred architecture, and original implementation choices. The draft
shader is outside the engine content pipeline. No production renderer changes
are part of this draft.

## Evidence files and boundaries

- [rea-evidence-bundle.json](rea-evidence-bundle.json): actual REA Evidence for the
  executable's PE identity and absence of managed metadata.
- [rea-provider-status.json](rea-provider-status.json): initial connected MCP
  provider diagnostics, before the user supplied the existing Ghidra path.
- [native-fog-trace-evidence.json](native-fog-trace-evidence.json) and
  [native-fog-constants.json](native-fog-constants.json): REA/Ghidra reference
  trace and native function dossier for the fog constant-buffer preparation.
- [native-clipmap-trace.json](native-clipmap-trace.json),
  [native-clipmap-render.json](native-clipmap-render.json) and
  [native-fog-render.json](native-fog-render.json): REA/Ghidra clipmap reference
  trace and the two rendering-function dossiers.
- [shader-token-matches.json](shader-token-matches.json) and
  [resolve_shader_tokens.py](resolve_shader_tokens.py): supplementary correlation
  of 32 native binding-key occurrences with shader identifiers using lowercased
  ASCII MurmurHash2, seed `0x31415926`. These correlations are analyst derivations,
  not new REA Evidence records.
- [local-observations.json](local-observations.json): supplementary Python
  inspection, **not REA Evidence**. Contains SHA-256 identities, ASCII string
  offsets, regular PE imports, FGD line numbers, VPK directory entries and
  selected shader payload CRC/SHA-256 checks.
- [inspect_local_artifacts.py](inspect_local_artifacts.py): reproducible,
  read-only inspection of the selected game files. It never runs game code.
- [VolumetricTransportDraft.fx](VolumetricTransportDraft.fx): original draft
  transport shader, with a reprojection coordinate helper.
- [draft-validation.json](draft-validation.json) and
  [validate_draft.py](validate_draft.py): standalone shader compilation and
  analytic transport comparisons.

REA observation E1 is
`ev_d4ced12ae0b948d262ee1b297ecfd66ac7b4bfaf48eda50bbae9b128a59ac54e`,
operation `inspect_managed_artifact`, authority `shipped-artifact`, confidence
`observed`. It identifies the selected `hlvr.exe` as PE32+, x86-64, 149,456 bytes,
SHA-256 `5dececefab3caaa57df2efd1cbefdac0e964885deda8b34cac07a35f7dc5b18c`.
It has no CLI metadata directory. This is identity/classification evidence,
not evidence of native rendering behavior.

The initial MCP `search_strings` failed with `provider_unavailable`: Ghidra was
not configured, IDA was not configured, and Hopper requires another host. The
user subsequently supplied Ghidra 12.1.4. A CLI invocation with process-local
`GHIDRA_INSTALL_DIR` successfully analyzed `scenesystem.dll` after host access
was approved. No persistent REA configuration or Ghidra installation was changed.
The original MCP diagnostics remain historical.

The supplementary scan covers seven PE files and four shipped FGD files. It
parses both `core/shaders_pc_dir.vpk` and `hlvr/shaders_pc_dir.vpk`, and reads only
members matching the selected volumetric/fog patterns. It does not decode VCS
GPU instructions. Hashes bind findings to this installation, rather than every
Alyx release. CRC checks establish directory/payload consistency, not Valve
publisher authenticity.

## Observations from the installed files

All offsets below are hexadecimal **file offsets**, not virtual addresses.
VCS offsets are relative to the reconstructed shader member including preload
bytes. Full paths, hashes and source archive offsets are in the observations JSON.

| ID | Artifact and location | Observation | What it establishes |
|---|---|---|---|
| L1 | `game/bin/win64/hlvr.exe`, `0x10d60`, `0x10da8`, `0x10db8` | Engine DLL load failure text, `Source2Main`, `engine2`; regular imports include `LoadLibraryExW` and `GetProcAddress` | Strong evidence the executable is a launcher. The exact call path requires disassembly. |
| L2 | `scenesystem.dll`, `0x266720`, `0x266770` | Build-path suffix `scenesystem/utils/volumetricfog.cpp`; material `materials/dev/cs_volumetric_fog.vmat` | Places named fog implementation/resources in the scene system. Build paths are embedded strings, not accessible source files. |
| L3 | `scenesystem.dll`, `0x266828`–`0x266858` | `volume_fog_width`, `volume_fog_height`, `volume_fog_depth` | Three fog-resolution controls are present. Their actual values are not recovered by the ASCII scan. |
| L4 | `scenesystem.dll`, `0x2667d0`, `0x2667f0`, `0x266890`, `0x2668d8`, `0x2668f8`, `0x266a48` | Jitter/stereo/single-dispatch controls; clipmap update/enabled controls; `CVolumetricFogClipmapRenderer::Render` | Supports jitter, stereo and clipmap implementations, without establishing which options run in a given scene. |
| L5 | `renderingpipelines.dll`, `0x4c6c0`, `0x4c6e8`, `0x4c7b8` | Left/right graphics-to-compute signal labels and a compute-to-graphics signal label | Supports synchronization between volumetric compute work and rendering. Does not prove measured asynchronous overlap. |
| L6 | `core/base.fgd`, lines 8098–8130 | Global controller, draw distance, eye-distance fade, anisotropy, indirect strength, `fogirradiancevolume`, indirect resolution choices | Authoring interface explicitly supports fog irradiance and indirect lighting. The 64–512 choices are **indirect lighting dimensions**, not recovered froxel dimensions. |
| L7 | `core/base.fgd`, lines 8136–8150 | Local bounds, strength, falloff exponent, box/sphere shapes; description says fog is restricted to the union of fog volumes when present | Authoring semantics differ from Obsidian's additive fog boxes. Runtime conformance still needs tracing/testing. |
| L8 | `hlvr/shaders_pc_002.vpk`: member `cs_volumetric_fog_pc_50_cs.vcs`, archive offset 309904, length 40020 | SHA-256 `75386dfa4694a2980274d64dc862031bfff7acf4ca7d13f1e6a4a6a47c373071`; CRC agrees with directory | Binds the following shader identifiers to one specific installed member. |
| L9 | L8 member, `0xd4`, `0x16c`, `0x1d1c`, `0x1f00`, `0x20e4` | `D_PASS`, `D_ENABLE_INDIRECT`, `OutLightingClipmap`, `OutLightingFroxelTexture`, `OutScatteringTexture` | Names alone suggest separate stages; E5/E6 and the token correlation below establish the CPU's pass selections and binding sequence. |
| L10 | L8 member, `0x35b0`, `0x3794`, `0x3978`, `0x3b5c`, `0x3d40` | `g_tFogIndirectVolume`, `g_tLastFrameFogVolume`, three lighting clipmap identifiers | Named indirect, history and clipmap inputs exist. Their encoding and update rules remain unknown. |
| L11 | L8 member, `0x6024`, `0x6074`, `0x60c4`, `0x6114`, `0x33cc` | Current/last average and left-eye fog-frustum matrices; jitter offset | Strong support for reprojection/stereo history bookkeeping. Exact reconstruction/filter weights are unverified. |
| L12 | L8 member, `0x3f24`, `0x4108`, `0x6934`, `0x6984`, `0x6a74`, `0x6ac4`, `0x7424` | Light cookie, shadow depth, light position/inverse radius, color, spot cone cosines, world-to-shadow and sun-index identifiers | Inputs support shadowed, projected direct lighting. Shared constant-buffer declarations alone do not prove every field is consumed by every permutation. |

The VPK also contains SFM volumetric and hand-interaction shaders. Those are
not used here as proof of Alyx fog's algorithm. The selected FGD and fog shader
identifiers support sampling jitter; they do not establish a particular
animated density-noise model. No claims about Perlin noise, exact history
weights, grid dimensions, integration equations or GPU timings follow from
these strings.

## Native constant-buffer evidence

REA observation E2,
`ev_5324fdcef73ac75f370cdf1463fedf468ef464836a7dd805fbe723c556f34144`,
is a `trace_feature` result from Ghidra 12.1.4, with shipped-artifact authority
and derived confidence. The matching label is at virtual address
`0x180268578`; its code reference at `0x180112ab1` belongs to
`FUN_180111c80`. Another reference is data, not a containing function. The
trace result was returned intact as a partial observation when its initial
snapshot save failed because the destination directory did not exist. The
extracted record is saved separately; no trace results were fabricated.

REA observation E3,
`ev_9a411d14d975292f56cda6c7889c8cea3d6091191a4ee0279fa98d27c3cc03c5`,
is the successful `analyze_function` dossier. Its subject is `scenesystem.dll`,
SHA-256 `031bdf42e5219e943da88dd30348ac024b8f1ffa4aae4e9229fb438bf01bbdd3`.
The analyzed inclusive range is `0x180111c80`–`0x180112d5f` (4,320 bytes).
The default function database signature is unresolved; Ghidra's decompiler
recovers four parameters. Those are generated types, not original source.

| Native observation | Interpretation and limit |
|---|---|
| Reads three short values at object offsets `0x2a90/0x2a92/0x2a94`, writes their integer and reciprocal forms to buffer state | Supports width/height/depth bookkeeping alongside L3 and the shader dimension identifiers. Actual values are not established. |
| Copies matrix-like state between object regions and performs view/corner-vector calculations | Supports preparation of per-view data. Exact mapping of every region to a named shader member is not decoded. |
| Walks linked entries, calls a predicate on bounds, appends constant-buffer records and breaks when the count is `0x80` | Supports collecting up to 128 relevant volume records after a bounds test. The predicate is inferred to be culling; its implementation was not analyzed. |
| References assertion text `m_constantBufferData.m_nNumFogVolumeBoundingBoxes[0] <= 128` | Direct semantic corroboration of the CPU-side box-record limit. This is not Obsidian's eight-volume limit. |
| For an empty list, creates one large fallback box-like record | Supports a global-medium fallback when no authored bounds are supplied. Exact fallback density semantics are not recovered. |
| Final virtual call passes object buffer at `+0x428` with size `0x1b40` (6,976 bytes) | Consistent with uploading the prepared constant buffer; the indirect callee's typed API identity is unresolved. |
| Direct callers are `0x18010df00` and `0x18010ea40` | E6 follows the second caller's render scheduling. The first callback's body was not analyzed. |

The dossier includes a terminal-call warning at `0x180112ad0` with unresolved
callee/no-return basis. It lies in the assertion-related area; its exact effect
remains unknown. Provider body ranges and complete retained p-code do not remove
this limitation or establish GPU execution.

## Native pass and resource evidence

REA trace E4,
`ev_5528600942d952bd9cb5dd68fb50daef25dc03738bf2d22feef4e9f4dbed5592`,
resolves the clipmap-render label's code references to `0x18010e350`.
The two subsequent Ghidra 12.1.4 dossiers have shipped-artifact authority and
observed confidence:

| ID | Evidence ID | Function range | Size |
|---|---|---|---|
| E5 | `ev_6827aba27004f4f3be218270a67ff8cd634832908ddc2db6370e456c3b27bade` | `0x18010e350`–`0x18010ea39` | 1,770 bytes |
| E6 | `ev_171466c325ae86cfca5e3c6bb705802c76d1a86730613c611b907670473c37a0` | `0x18010ea40`–`0x18010f9d8` | 3,993 bytes |

E5 sets the same integer binding key to 3 before processing clipmap regions.
E6 sets it to 0 or 1 for one stage, then to 2 for another. Independently hashing
the shader identifier `D_PASS` produces exactly that key, `0xbe9e294b`.
The same procedure matches 31 other binding-key occurrences across these two
functions to shader identifiers. The coherent resource matches below support
the interpretation; hashing is a correlation method and does not eliminate
the possibility of collisions or resolve the callees' full types.

| Selected CPU permutation | Observed binding structure | Supported interpretation |
|---|---|---|
| `D_PASS = 3` in E5 | `OutLightingClipmap` (`0x9a4f69da`), `g_tFogIndirect` (`0x9c93435a`), integer texel offsets and bounding-box transforms | Regional lighting-clipmap updates. E5 iterates exactly three clipmap entries and each entry's region list. |
| `D_PASS = 0` in E6 when its clipmap-use condition is false | Previous fog texture, current `OutLightingFroxelTexture`, direct fog-indirect input and shared clipmap bindings | A fallback path for generating froxel lighting. Exact light evaluation is inside the unexamined GPU program. |
| `D_PASS = 1` in E6 when the clipmap-use condition is true | Same previous/current fog textures and the three lighting-clipmap bindings, plus clipmap UV and bounds transforms | A path that generates froxel lighting with clipmap support. Bound resources alone do not establish every executed shader load. |
| `D_PASS = 2` later in E6 | Current froxel texture as `InLightingTexture` (`0xb084183b`), separate `OutScatteringTexture` (`0x70d38317`) | A subsequent scattering-output stage. It is consistent with column integration, but the precise GPU loop and transport equation are not decoded. |

More concrete CPU findings:

- E6 selects the last-frame texture with an index XOR 1 and the current output
  with the current index: actual resource ping-pong, beyond just a history name.
- The `g_nJitterOffset` key is `0xcf5d01bd`. E6 supplies either a counter masked
  with `0x7f` or `RandomInt(0,0x7f)`. This establishes 128 candidate indices in
  these branches, not a particular blue-noise texture or sample distribution.
- E5 checks region extents against `0x100` on X/Y/Z. Assertion text names
  `c_nClipmapDimension`; E6 uses `1/256` in corresponding UV transforms. Together
  these support a 256-cell clipmap dimension in this build. They do not establish
  texture format/allocation, world-space spacing or hardware threadgroup size.
- The recovered dispatch-like virtual calls at context offset `0x2b8` receive
  region or fog width/height arguments. Their complete typed signatures are
  unresolved; the decompiler's displayed argument list is not proof that Z work
  is absent.
- E6 directly calls E3 on one path and can queue another constant-preparation
  callback on another path. This does not by itself prove GPU asynchronous overlap.

E5 carries terminal-call/no-return warnings in three assertion-related regions.
E6 starts with `Type propagation algorithm not settling`. Treat its parameter
types and indirect-call signatures as tentative. The native dossiers retain
assembly and p-code for checking the generated pseudocode.

## How the lighting appears to work

**Architecture supported by E3–E6 and L2–L12:** authored density and bounds describe
where participating media exist; direct-light data and an indirect irradiance
texture supply illumination; clipmaps can retain lighting in spatial volumes;
a view-aligned froxel stage converts that lighting into a fog representation;
history and jitter reduce sampling artifacts; integrated scattering is sampled
when drawing the image. Stereo data allows the fog representation to serve the
VR views.

```text
Authored fog bounds / strength       Lights + shadows / cookies
              |                               |
              +-------- fog lighting ---------+
                              |
             indirect texture + lighting clipmaps
                              |
                    view-aligned froxels
                              |
                   history / jitter handling
                              |
                   scattering integration
                              |
                  HDR scene composition
```

This diagram is a working reconstruction of responsibilities. It is not a
runtime GPU capture. The CPU selects clipmap update → froxel-lighting generation
with history bindings → scattering output responsibilities, with conditionals
described above. History is bound during the lighting-generation stage; the
shader's actual blending formula is unresolved. The three clipmaps and their
256-cell dimension do not prove particular world-space cascade spacing.

The physical explanation for a shaft is light scattered toward the camera by
air or dust. An opaque roof makes much of that medium unlit; a window admits
light into a narrow region. Scattering varies with viewing direction, so a
beam is often strongest when looking toward the light. Indirect lighting can
give the fog a softer colored glow even where no direct beam reaches it.
This explanation and the following transport equations are rendering theory,
not equations recovered from Valve's shader. A primary background reference is
Sébastien Hillaire's [2015 volumetric-rendering work](https://sebh.github.io/publications/).
Valve's authoring defaults should not be copied numerically into Obsidian:
world-unit conversions and coefficient transforms were not established here.

## Obsidian's current implementation and gaps

| Existing code | Finding | Proposed change |
|---|---|---|
| `FroxelRenderModule` constants / `Initialize` | 160×90×128 cells packed into a 160×11520 2D atlas; RGBA16F injection and two accumulation targets | Keep the atlas for MonoGame SM4 compatibility. Validate atlas dimensions against hardware limits for future quality presets. |
| `Froxel.fx:PixelShaderBuildFroxels` | Logarithmic distance, directional/point/spot light scattering, shadow maps, HG phase, local fog boxes | Preserve existing direct-light functionality; separate medium extinction from scattering source. |
| `Froxel.fx:PixelShaderAccumulateFroxels` | Each output Z slice re-integrates all preceding slices | Replace with a transport prefix scan, subject to GPU measurement. |
| Same accumulation function | History sampled at identical atlas UV, with no previous camera matrices | Reproject local injection history into the prior frustum; reject invalid/stale samples. |
| `Gbuffer.fx` versus `Froxel.fx` | G-buffer stores positive view-Z / FarClip; froxel reconstruction uses normalized ray × distance | Standardize on positive view-Z for slicing and lookup; use physical ray length for transport. Current conventions disagree away from screen center. |
| Froxel accumulation initialization | Both history atlases cleared to transparent, but transparent has transmittance 0 | Empty transport is `(0,0,0,1)`; disable history until a valid previous frame exists. |
| `LightingSystem` / `ProbeVolumeData` | Baked L1 diffuse probes exist, including visibility distances, but are not bound to the froxel shader | Add a volume-specific ambient sampler and an indirect strength parameter. |
| `ApplyPointLights` | Uses the first eight entries, with no visibility/importance ranking | Cull and rank visible lights with stable tie-breaking before selecting eight. Preserve shadow-map ownership. |
| `FogVolumeComponent` | Adds density inside oriented boxes | Keep existing scene semantics; optional restrict-to-volumes behavior needs its own explicit setting. |
| `DeferredCompose.fx` | Integrates fog into HDR as scene color × transmittance + scattering, with bilateral sampling and sky controls | Preserve these hooks, but update lookup to the new view-depth/boundary convention. |

The draft has **no requirement to copy Valve's clipmaps** in its first phase.
Obsidian's existing baked probes and small direct-light list offer a simpler
route to the relevant visual improvements.

## Draft implementation contract

### Medium and transport

Injection should store `float4(qRGB, sigmaT)`:

```text
sigmaT = density * extinctionScale                         [1/metre]
sigmaS = sigmaT * scatteringAlbedoRGB                      [1/metre]
qRGB   = sigmaS * (phaseWeightedDirectLight + indirectLight) [radiance/metre]
```

For a segment of physical length `ds`, constant within the sampled cell:

```text
T = exp(-sigmaT * ds)
C = qRGB * (1 - T) / sigmaT
```

The zero-extinction limit is `C = qRGB * ds`. The shader uses a small-optical-depth
series to avoid cancellation. For a first migration, keep existing authored
scattering values in RGB and put `density * FroxelAbsorption` in alpha. That
preserves the parameter meaning, but exact segment integration will still
change brightness in thick fog. A later physically constrained mode can clamp
scattering albedo to [0,1]. Do not silently reinterpret existing scene fields.

For positive view depth `z`, logarithmic slice boundary `B(k)` is
`near * (fogFar/near)^(k/Nz)`. Reconstruct a normalized camera ray `dirVS`, then:

```text
positionVS = dirVS * (z / -dirVS.z)
ds = (B(k+1) - B(k)) / -dirVS.z
```

Use this convention for density evaluation, directional slice-shadow sampling,
history coordinates, integration and composition. `fogFar` may be shorter than
the camera far plane, but the normalized G-buffer depth must first be multiplied
by the **camera** far plane, then clamped to `fogFar`.

### Prefix scan that works with the existing renderer

Represent each segment as `(C,T)`. Two ordered segments combine associatively:

```text
Combine(near, far) = (near.C + near.T * far.C, near.T * far.T)
```

The draft contains `SeedTransport` and `ScanTransport`. The host sequence is:

```csharp
// Integration outline: DrawAtlas must bind the effect/target, set an atlas-size
// viewport, apply its pass, and draw the existing FullScreenTriangle.
// Raw/filtered injection is produced separately. This is not a new runtime API.
DrawAtlas(seedTechnique, injection, scanA);
for (int stride = 1; stride < depthSlices; stride *= 2)
{
    // Unbind scanB from every shader resource slot before using it as a target.
    effect.Parameters["ScanStride"].SetValue(stride);
    DrawAtlas(scanTechnique, scanA, scanB);
    Swap(ref scanA, ref scanB);
}
Texture2D integratedFog = scanA;
```

At 128 slices this uses one seed pass and seven scan passes. The existing loop
reads `160*90*128*129/2 = 118,886,400` injection samples per frame in its worst
case without compiler optimization. The scan has about 25.8 million input reads
including seeding, but more output writes and draw calls. Those are algorithmic
counts, **not benchmarked speedups**. Profile both paths. If a future backend
exposes compute/UAVs, one thread per XY column can accumulate all Z cells in a
single linear scan; stock MonoGame effects do not supply that interface here.

An integrated prefix at index `k` ends at boundary `B(k+1)`. The draft's
`SampleFog` samples the prefix **before** the containing slice, then integrates
only the remaining partial slice. This gives identity at the near boundary and
avoids applying a whole slice to a surface inside it. XY filtering stays inside
each atlas tile. Port that lookup into `DeferredCompose.fx` and retain its
bilateral/sky handling. The debug technique renders only at grid XY resolution.

### History and lifetime

Store history of local `(qRGB,sigmaT)` injection, rather than a prefix integrated
along a different camera ray. `PreviousFroxelCoordinate` provides only the
coordinate mapping: current cell center → world → prior projection and prior
view-Z → previous atlas XY/Z. Interpolate adjacent previous Z slices explicitly;
a 2D sampler does not provide trilinear volume filtering. Use unjittered cell
centers for history coordinates; jitter the evaluation location within the cell.

Add a raw injection target and two filtered injection histories for a clear
first implementation. Clamp history to a small current injection neighborhood
and reject it on density/source disagreement, prior-frustum exit or invalid
history. Start with complete history resets on scene load, camera cut, projection
or fog-range change, atlas resize, shader reload, fog enable transition, bake
replacement, and changes to lights/fog settings. Reduce local history weight
around moving lights later; camera reprojection alone does not track them.

Clear/skip uninitialized history. Prefix targets carry `(0,0,0,1)` as empty;
local injection history carries `(0,0,0,0)` as empty. Reset CPU matrix snapshots
and validity flags alongside GPU resources. Update previous matrices only after
the frame has rendered successfully. Bind and dispose targets on the engine
thread; save/restore graphics state in `finally` blocks.

The current three RGBA16F atlases occupy about 42.2 MiB. Three injection targets
plus two scan targets would occupy about 70.3 MiB, excluding all other renderer
resources. Avoid silently adding this cost to low-end presets. These are storage
calculations, not observed Alyx memory usage.

### Baked indirect lighting

Add `SetProbeVolume(LightingSystem lighting, float strength)` to
`FroxelRenderModule`, mirroring the existing environment-map binding pattern.
Bind the three SH textures, visibility-distance textures, bounds and cell count.
Sample the eight neighbors at the froxel's world position. Retain visibility
weights, but remove the surface-normal offset and backface weighting: fog has
no surface normal. Reject out-of-volume positions; if no visible probe remains,
use zero rather than unoccluded interpolation that leaks light through walls.

Begin with isotropic ambient scattering from the interpolated RGB L0 values
`(SHR.x, SHG.x, SHB.x)`. `ProbeVolumeBaker` stores these as mean radiance; its
surface convolution is already divided by pi, and its light scale already
matches the engine. **Do not divide this L0 by pi again or multiply by the direct
light shader's 0.1 again.** This is a directional approximation for indirect
fog; the direct term can retain its existing HG phase.

Do not call `EvaluateIrradiance(position, viewDirection)` to pretend fog has a
normal. Do not add the unoccluded scene cubemap as fallback inside closed rooms.
The baked probe rays sample visible surface radiance and escaped sky; the
direct-light injection remains a separate source. Calibrate indirect strength
in a room lit only by bounce and inspect for double counting.

### Files to change when adopting the draft

| File | Concrete integration |
|---|---|
| `Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs` | New target ownership, seed/scan techniques, local history ping-pong, previous matrices, invalidation, probe bindings and light ranking |
| `Engine/Content/Shaders/Deferred/Froxel.fx` | View-Z reconstruction, extinction alpha, corrected slice shadows, probe ambient term, temporal resolve and exact transport/scan |
| `Engine/Content/Shaders/Deferred/DeferredCompose.fx` | Partial-slice view-depth lookup; keep bilateral and sky behavior |
| Forward surface/particle shaders that draw after opaque composition | Sample the same air fog at fragment depth before transparency blending; do not fog the already-composed background a second time |
| `Engine/Renderer/Renderer.cs` | Bind active baked lighting before injection and signal scene/camera/projection changes |
| `Engine/Logic/EnvironmentSettings.cs`, `Engine/Recources/GameSettings.cs` | Optional indirect-strength/quality settings with defaults compatible with old scenes |
| `Engine/Editor/IEditorBridge.cs`, editor environment model/XAML | Expose adopted settings through the existing environment mutation path |
| `Engine/Recources/Shaders.cs`, `Engine/Content/Content.mgcb` | Cache actual new parameters and techniques; register adopted shader assets |
| `Tests/Components/FogChecks.cs` | Add meaningful coordinate, history lifetime and integration checks once the implementation exists |

Suggested order: fix depth/transport and empty history; compare reference
captures; add reprojection and resets; add indirect sampling; then benchmark the
scan and decide whether clipmap caching is justified. Density animation and
additional local-volume shapes are optional later features, not recovered
requirements of this Alyx build.

## Validation performed

`python Docs/Research/AlyxVolumetric/validate_draft.py` compiles five SM4 entry
points with the installed Windows D3DCompiler: vertex, seed, scan, sampling,
and a wrapper exercising the reprojection helper. All compile. Compiler
warnings about integer division and `pow` input domains are saved in the JSON;
the host contract requires positive near/far values and positive grid sizes.

3,945 CPU comparisons check uniform-medium transport, parallel prefixes and
partial-depth evaluation against the analytic result over vacuum, thin and
thick media and several ray angles. Maximum absolute error is approximately
`1.36e-12` in double precision. This is not a GPU half-float accuracy result.

MonoGame packaging was attempted, but the pinned `mgcb` tool is not restored
for this shell. No dependency installation was performed. The D3DCompiler check
does not validate effect packaging or runtime parameter binding. No Alyx runtime,
GPU performance or Obsidian visual comparison has been measured.

Before enabling the implementation, check a closed dark room, a sunlit window,
a moving shadowed flashlight, a camera pan/cut/FOV change, scene reload, atlas
resize, fog toggle and probe rebake. Compare centers and screen edges against
the same world-space medium, and include empty/no-light scenes. Keep output
linear until existing exposure/tonemapping and apply `sceneHDR*T + C` exactly once.
Forward transparency needs a shared depth-aware fog sampler because it draws
after the opaque composition pass. With premultiplied transparency, use
`alpha * (surfaceHDR*T + C)` as the source contribution and the already fogged
background as destination. A second fullscreen fog pass would fog opaque
surfaces twice. Keep the existing separate particle-volume renderer out of the
medium injection until its density/lighting ownership is defined explicitly.

## Unresolved questions

The combined evidence establishes CPU pass choices, three clipmap entries,
256-cell clipmap bounds, a 128-box preparation limit and the history/resource
bindings described above. It does not establish exact GPU threadgroup sizes,
clipmap world spacing and update budgets, depth mapping,
scattering/extinction encoding, phase formula, temporal filter weights,
history reset policy or active runtime settings. Their resolution requires
further instruction/shader evidence and, for actual scheduling/performance, runtime
GPU observation. The Obsidian proposal remains valid as an original design
without claiming any of those choices were copied from Alyx.
