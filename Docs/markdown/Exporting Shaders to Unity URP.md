# Exporting Shaders to Unity URP

A practical guide for taking a shader out of this MonoGame/HLSL engine and getting it
running in Unity's **Universal Render Pipeline (URP)**. The **froxel volumetric fog**
(`Engine/Content/Shaders/Deferred/Froxel.fx`) is used as the worked example because it is
the hardest case — it is a multi-pass, render-target-driven, screen-space effect, so once
you can port that, simpler material shaders are trivial.

> TL;DR: You are **not** copy-pasting one file. The `.fx` HLSL is ~90% reusable math, but
> the *plumbing* (techniques/passes, render targets, parameter binding, depth, matrices,
> shadows) is engine-specific and must be re-built using Unity's APIs. This guide tells you
> what to keep, what to throw away, and what to rewrite.

---

## 1. Understand what you are actually porting

The froxel fog is **not** a surface shader. It is a screen-space post effect made of three
stages. You must port all three (or fold them together):

| Stage | Obsidian source | What it does |
|-------|-----------------|--------------|
| **1. Inject / Build** | `Froxel.fx` → `PixelShaderBuildFroxels` (technique `BuildFroxels`) | For every froxel (a frustum-aligned voxel) compute in-scattered light (Henyey–Greenstein phase × light × shadow) and density. Writes `(scatterRGB, density)` into a 2D **atlas** render target. |
| **2. Accumulate** | `Froxel.fx` → `PixelShaderAccumulateFroxels` (technique `AccumulateFroxels`) | March front-to-back through the slices, doing Beer–Lambert transmittance integration, then blend with the previous frame (temporal). Writes `(accumScatterRGB, transmittance)`. |
| **3. Compose** | `DeferredCompose.fx` → `SampleFroxelAccumulationTexture` / `SampleFroxelBilateral` | Per screen pixel: pick the froxel slice from scene depth, bilaterally upsample the fog, then `scene * transmittance + inScatter`. |

Key implementation facts you must carry over (these drive every design decision):

- **The "froxel grid" is `160 × 90 × 128`** (`FroxelRenderModule.FROXEL_GRID_*`). That is
  X×Y across the screen and **128 exponential depth slices**.
- **Slices are distributed logarithmically**: `dist = Near * (Far/Near)^(slice/Nz)`. Near
  geometry gets fine slices, distant geometry coarse. The inverse mapping in compose is
  `slice = log(depth/Near) / log(Far/Near) * Nz`.
- Because MonoGame WindowsDX (SM4 pixel shaders) can't easily write a 3D texture, Obsidian
  **fakes a 3D texture by packing it into a tall 2D atlas**: `width = Nx (160)`,
  `height = Ny*Nz (90*128 = 11520)`. Atlas texel `(fx,fy,fz)` lives at
  `px = fx, py = fz*Ny + fy`. **In Unity you do not need this hack** — see §3.
- Storage format is `HalfVector4` (RGBA16F). You need an HDR / float render target in Unity
  too (`GraphicsFormat.R16G16B16A16_SFloat`).
- Temporal blend is a **simple same-pixel exponential history** (`lerp(current, prev,
  HistoryAlpha)`), *not* full motion-reprojected TAA. Easy to reproduce.

---

## 2. Pick a porting strategy

There are two viable ways to land this in URP. Choose based on how faithful vs. how clean
you want to be.

### Strategy A — Compute shader + `RWTexture3D` (recommended)

This is how real engines (Frostbite, Unity HDRP) do volumetrics. Unity makes 3D textures
trivial, so you **drop the 2D-atlas packing entirely** and write directly to a
`RWTexture3D<float4>` of size `160×90×128`.

- Inject and Accumulate become **compute kernels** (`#pragma kernel`).
- Compose becomes a **full-screen blit** that reads the 3D texture with a hardware trilinear
  sampler (free bilinear in XY + linear across slices — replaces all the manual atlas-UV and
  `lerp(sample0, sample1, zFrac)` code).
- Cleaner, faster, and the slice/UV math gets much shorter.

**Pick this unless you have a strong reason not to.** The rest of this guide leads with it.

### Strategy B — Faithful 2D-atlas blit port (1:1 translation)

Keep the atlas, keep the three pixel shaders, and run them as three `Blit` passes into
`RenderTexture`s. Closest to the original code (you can almost paste the HLSL bodies), but you
inherit the atlas-UV gymnastics and the bilateral-upsample band-aids that only exist *because*
of the atlas. Use it only if you want a line-by-line port. See §8.

---

## 3. The MonoGame `.fx` → URP HLSL translation reference

This is the part that bites people. Same language (HLSL), different dialect and conventions.

### 3.1 Boilerplate / declarations

| MonoGame FX (`.fx`) | Unity URP HLSL |
|---|---|
| `#include "../Common/helper.fx"` | `#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"` |
| `Texture2D Foo;` + separate `SamplerState S {...}` | `TEXTURE2D(_Foo); SAMPLER(sampler_Foo);` |
| `Foo.Sample(LinearSampler, uv)` | `SAMPLE_TEXTURE2D(_Foo, sampler_Foo, uv)` |
| `Foo.SampleLevel(s, uv, 0)` | `SAMPLE_TEXTURE2D_LOD(_Foo, sampler_Foo, uv, 0)` |
| `Foo.Load(int3(x,y,0))` | `LOAD_TEXTURE2D(_Foo, int2(x,y))` |
| `RWTexture3D<float4>` (N/A in SM4 PS) | `RWTexture3D<float4> _FroxelVolume;` (compute) |
| `technique T { pass P { VertexShader = compile vs_4_0 VS(); PixelShader = compile ps_4_0 PS(); } }` | Delete. Use `HLSLPROGRAM / #pragma vertex Vert / #pragma fragment Frag` (blit) or `#pragma kernel CSMain` (compute). |
| `float4 PS(...) : COLOR0` | `half4 Frag(...) : SV_Target` |
| `: SV_POSITION`, `: POSITION0` | `: SV_POSITION`, `: POSITION` / use URP `Blit.hlsl` fullscreen vertex. |
| Global `float Foo;` uniforms | Put in a `CBUFFER_START(UnityPerMaterial) ... CBUFFER_END` block (or `cbuffer`), or set via compute. |

### 3.2 Matrices — **the #1 source of "everything is black"**

- **MonoGame/XNA = row-major, row vectors:** transform is `mul(vector, matrix)` (i.e. `v * M`).
  Note the order in `Froxel.fx`: `mul(float4(worldPos,1.0), LightViewProjection)`.
- **Unity HLSL = column-major convention, `mul(matrix, vector)`** (i.e. `M * v`).

**Do not port the matrix plumbing. Use Unity's built-ins instead** and flip the `mul` order:

| Obsidian uniform | Unity replacement |
|---|---|
| `InverseProjection` | `unity_CameraInvProjection` (or `UNITY_MATRIX_I_P`) |
| `InverseView` | `UNITY_MATRIX_I_V` |
| `View` (CPU side) | `UNITY_MATRIX_V` |
| `mul(v, InverseProjection)` | `mul(unity_CameraInvProjection, v)` |
| world pos from depth | `ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP)` (URP `Common.hlsl`) |
| `LightViewProjection` (dir shadow) | Use URP `TransformWorldToShadowCoord(worldPos)` instead of building it by hand. |

### 3.3 Depth

- Obsidian stores **linear depth** in its own `DepthMap` (G-buffer), already `depth*FarClip`.
- Unity gives you the **non-linear hardware depth** in `_CameraDepthTexture`. Convert:

```hlsl
float rawDepth = SampleSceneDepth(uv);              // URP DeclareDepthTexture.hlsl
float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);   // linear view-space distance
// 'eyeDepth' is the equivalent of Obsidian's `dist` used in the slice mapping.
```

Also: a depth of `1.0` (or `0.0` on reversed-Z) means **sky** — Obsidian special-cases sky
(`SkyFogStrength`); keep that branch.

### 3.4 Coordinate system & clip space

- This engine is **Z-up** (see `CLAUDE.md`; gravity is `-Z`). Unity is **Y-up, left-handed**.
  You are reconstructing positions from Unity's own matrices, so **don't carry over any Z-up
  assumptions** — feed Unity light directions/positions and the handedness takes care of
  itself. Just don't paste hardcoded "Z is up" swizzles.
- **UV / clip Y flip:** Obsidian does `shadowUV.y = 1.0 - shadowUV.y` and `ndcY = 1 - uv*2`.
  Unity handles render-target flips with `UNITY_UV_STARTS_AT_TOP` / `_ProjectionParams.x`.
  Use URP's `GetFullScreenTriangleTexCoord` / `Blit.hlsl` so you don't hand-roll flips.

### 3.5 Lights & shadows

The pure math ports verbatim — **keep `HenyeyGreenstein()` exactly as written**, it's
API-agnostic. Replace the *data sourcing*:

| Obsidian | URP equivalent |
|---|---|
| `DirectionalLightDirectionVS`, `DirectionalLightColor` | `Light mainLight = GetMainLight();` → `mainLight.direction`, `mainLight.color` |
| `ComputeShadow(worldPos)` (custom linearized compare) | `MainLightRealtimeShadow(TransformWorldToShadowCoord(worldPos))` |
| `PointLightPositionsWS/Colors/Radii[]` arrays + `MAX_FROXEL_POINT_LIGHTS=8` | Loop `GetAdditionalLightsCount()` → `GetAdditionalLight(i, worldPos)` (gives color, direction, distanceAttenuation, shadowAttenuation) |
| `ComputePointLightShadow(...)` (manual cube-strip) | `AdditionalLightRealtimeShadow(i, worldPos)` |
| Manual `1/r^2 * falloff` attenuation | `light.distanceAttenuation` (URP already computes range falloff) |

Add to your pass shader:
```hlsl
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile _ _ADDITIONAL_LIGHTS
#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _SHADOWS_SOFT
```

---

## 4. Step-by-step: the recommended compute-shader port

### Step 0 — Project setup
1. Unity 6 (URP 17, RenderGraph) or URP 12–16 (compatibility mode). Note which — the C# hook
   API differs (§7).
2. Create an HDR URP asset. Make sure **Depth Texture** is enabled on the renderer (the fog
   needs `_CameraDepthTexture`).

### Step 1 — Allocate the froxel volume (C#)
Create one `RenderTexture` used as the 3D froxel grid, plus a second for temporal history:
```csharp
var desc = new RenderTextureDescriptor(160, 90, GraphicsFormat.R16G16B16A16_SFloat, 0)
{
    dimension = TextureDimension.Tex3D,
    volumeDepth = 128,
    enableRandomWrite = true,   // required for RWTexture3D
};
_froxelVolume = new RenderTexture(desc); _froxelVolume.Create();
// _froxelHistory: same descriptor, ping-pong each frame for the temporal blend.
```

### Step 2 — Inject kernel (`Froxel.compute`)
Port `PixelShaderBuildFroxels`. Dispatch `[numthreads(8,8,1)]` over `160×90×128`. The body is
nearly identical — only the I/O and matrices change:

```hlsl
#pragma kernel CSInject
RWTexture3D<float4> _FroxelVolume;
float4x4 _InvProjection;   // set unity_CameraInvProjection from C#, or use built-in
float _Near, _Far;         // camera near/far
// ... density/scatter/light uniforms straight from GameSettings ...

static const float PI = 3.14159265;
float HenyeyGreenstein(float cosT, float g) { /* paste verbatim from Froxel.fx */ }

[numthreads(8,8,1)]
void CSInject(uint3 id : SV_DispatchThreadID)
{
    uint3 dim; _FroxelVolume.GetDimensions(dim.x, dim.y, dim.z);
    if (any(id >= dim)) return;

    float2 uv = (id.xy + 0.5) / float2(dim.xy);
    float t   = (id.z + 0.5) / dim.z;
    float dist = _Near * pow(_Far / _Near, t);            // exponential slice

    // view ray from NDC (note Unity mul order)
    float2 ndc = float2(uv.x * 2 - 1, uv.y * 2 - 1);
    float4 viewH = mul(_InvProjection, float4(ndc, 1, 1));
    float3 dirVS = normalize(viewH.xyz / viewH.w);
    float3 posVS = dirVS * dist;
    // ... reconstruct world pos via UNITY_MATRIX_I_V, evaluate HG phase + light + shadow ...

    _FroxelVolume[id] = float4(scatter, density);
}
```

### Step 3 — Accumulate kernel
Port `PixelShaderAccumulateFroxels`. Dispatch over `160×90` (one thread per XY column); each
thread marches `z = 0..127` doing the Beer–Lambert integration, writing every slice, then
blends with `_froxelHistory`. The slice-thickness math (`distFar - distNear` from the
exponential mapping) ports verbatim. Ping-pong the history target.

### Step 4 — Compose (full-screen blit shader)
Port `DeferredCompose.fx`'s fog section into a URP fullscreen pass. This is where the 3D
texture pays off — replace the whole atlas-UV + manual Z-lerp block with one trilinear sample:

```hlsl
float eyeDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
float slice    = saturate(log(max(eyeDepth,_Near)/_Near) / log(_Far/_Near)); // 0..1 -> w coord
float4 fog     = SAMPLE_TEXTURE3D(_FroxelVolume, sampler_LinearClamp, float3(uv, slice));

half3 scene = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
half3 outc  = scene * fog.a + fog.rgb;     // scene*transmittance + inScatter
return half4(outc, 1);
```
Keep the **sky branch** (`SkyFogStrength`) and, if you still see grid blocking, port
`SampleFroxelBilateral` — but with a real 3D texture and trilinear filtering you usually
won't need it.

### Step 5 — Drive it from a `ScriptableRendererFeature`
A single feature with one pass that, per frame:
1. Dispatches `CSInject` then `CSAccumulate` (writing `_froxelVolume`).
2. Blits the compose shader over the camera color (after opaques/skybox, before post).
3. Swaps the history target.

See §7 for the RenderGraph vs. legacy API note.

---

## 5. Parameter / uniform mapping

Map each `Froxel.fx` / `DeferredCompose.fx` uniform to its Unity source. Defaults are the
current `GameSettings` values.

| Shader uniform | Default (`GameSettings`) | Unity source |
|---|---|---|
| `GridDimensions` | `(160, 90, 128)` | constant / `_FroxelVolume.GetDimensions` |
| `NearClip` / `FarClip` | `1` / `1000` | `camera.nearClipPlane` / `farClipPlane` (set from feature) |
| `FroxelDensity` | `0.03` | material/feature float |
| `FroxelAbsorption` | `0.1` | material/feature float |
| `DirectionalScatter` | `0.1` | material/feature float |
| `PointScatter` | `1.5` | material/feature float |
| `FogDistanceStart` / `FogDistanceFull` | `30` / `400` | material/feature float |
| `G` (HG anisotropy) | `0.45` | material/feature float |
| `HistoryAlpha` | `0.7` | material/feature float |
| `SkyFogStrength` | `0.1` | material/feature float |
| `DirectionalLight*` | — | `GetMainLight()` |
| `PointLight*[]` (max 8) | — | `GetAdditionalLight(i, …)` loop |
| `ShadowMap` / point shadow maps | — | URP shadow keywords + `*RealtimeShadow()` |
| `NoiseMap` + `Time` (blue-noise jitter) | — | assign a blue-noise tex + `_Time.y` |
| `InverseProjection` / `InverseView` | — | `unity_CameraInvProjection` / `UNITY_MATRIX_I_V` |

---

## 6. Gotchas checklist (port-blocking issues)

- [ ] **Matrix mul order flipped** (`mul(v,M)` → `mul(M,v)`). #1 cause of black/garbage.
- [ ] **`enableRandomWrite = true`** on the 3D RT or the compute write silently no-ops.
- [ ] **Depth is non-linear** in Unity — run it through `LinearEyeDepth` before the slice map.
- [ ] **Reversed-Z**: on most platforms Unity uses reversed-Z; sky = depth `0`, not `1`. Use
      URP helpers and `UNITY_REVERSED_Z` rather than comparing against a literal.
- [ ] **HDR target**: use `R16G16B16A16_SFloat`; an LDR target clips the scatter to junk.
- [ ] **Shadow keywords** (`#pragma multi_compile _ _MAIN_LIGHT_SHADOWS …`) or shadows are off.
- [ ] **Depth texture** enabled on the URP renderer asset.
- [ ] **Y-flip**: use `Blit.hlsl`/`GetFullScreenTriangleTexCoord`; don't hand-roll `1-uv.y`.
- [ ] **Don't carry over the 2D-atlas packing** in Strategy A — it's pure MonoGame baggage.
- [ ] **`half` precision**: HG's forward peak can overflow `half` on mobile — Obsidian already
      clamps with `min(phase, 4.0)` and floors the denominator; keep those guards.

---

## 7. Where to hook it: `ScriptableRendererFeature`

A screen-space effect like this **must** run inside a render feature; there is no
"material on an object" equivalent.

- **URP 12–16 (legacy `ScriptableRenderPass.Execute`)**: override `Execute(context, ref
  renderingData)`, get a `CommandBuffer`, `cmd.DispatchCompute(...)`, then
  `Blitter.BlitCameraTexture(cmd, source, dest, composeMaterial, 0)`. Inject at
  `RenderPassEvent.BeforeRenderingPostProcessing`.
- **URP 17 / Unity 6 (RenderGraph)**: override `RecordRenderGraph(RenderGraph, Context
  Container)`. Import the 3D RT, add a `ComputePass` for inject/accumulate and a
  `RasterRenderPass` (or `AddBlitPass`) for compose. Read camera color/depth from the
  `UniversalResourceData`.

Decide your Unity version up front — these two APIs are not interchangeable.

---

## 8. Strategy B: the faithful 2D-atlas port (alternative)

If you want a near-1:1 translation:

1. Create three `RenderTexture`s sized `160 × 11520` (`Nx × Ny*Nz`), `R16G16B16A16_SFloat`:
   injection, accumulation A, accumulation B (history ping-pong).
2. Make a shader with three passes (or three blit materials) — paste the bodies of
   `PixelShaderBuildFroxels`, `PixelShaderAccumulateFroxels`, and the compose math, applying
   the §3 translations (samplers, `SV_Target`, matrices).
3. **Keep** `FroxelIndicesToAtlasUV` / `FroxelContinuousToAtlasUV` / `SampleFroxel-
   AccumulationTexture` verbatim — they are the atlas addressing and are correct as-is.
4. Drive the three blits from a `ScriptableRendererFeature` exactly like §7, just with `Blit`
   calls instead of `DispatchCompute`.

You'll likely also need `SampleFroxelBilateral` from `DeferredCompose.fx` because point
sampling a packed atlas shows the ~8px froxel blocks — the comments in that file explain why.
This is the maintenance cost you avoid by choosing Strategy A.

---

## 9. Validation

1. **Density only**: disable lighting, output `float4(density.xxx,1)` — confirm the
   exponential slice distribution looks right (more detail up close).
2. **Single directional light**, no shadows — confirm HG forward-scatter brightens toward the
   sun.
3. **Enable main-light shadows** — confirm god rays appear where geometry occludes the sun.
4. **Add point lights** — confirm local glow with `1/r^2` falloff.
5. **Temporal**: toggle `HistoryAlpha` 0 → 0.7 and confirm noise smooths out without ghosting
   on a static camera.
6. Compare side-by-side against Obsidian with matching `GameSettings` values.

---

## 10. Generalizing to other shaders

The froxel fog is the worst case. For ordinary shaders the same playbook shrinks:

- **Material/surface shaders** (e.g. `Forward.fx`, `EmissiveDraw.fx`, `HologramEffect.fx`):
  port into a URP `Shader Graph` or a hand-written URP `Lit`/`Unlit` pass. Keep the math,
  swap inputs to URP's `Attributes`/`Varyings` + `GetMainLight`/`GetAdditionalLight`.
- **Full-screen post** (e.g. `Bloom.fx`, `ColorGrading.fx`, `ScreenSpaceReflections.fx`,
  `TemporalAntiAliasing.fx`): same `ScriptableRendererFeature` + blit approach as here, minus
  the 3D volume.
- **Always** apply the §3 translation table (samplers, matrix order, depth, semantics) — that
  part is universal across every shader you move.

---

*Worked example sources in this repo:*
*`Engine/Content/Shaders/Deferred/Froxel.fx`,*
*`Engine/Content/Shaders/Deferred/DeferredCompose.fx`,*
*`Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs`,*
*`Engine/Recources/GameSettings.cs` (the tunable `g_Froxel*` knobs).*
