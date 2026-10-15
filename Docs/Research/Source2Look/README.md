# Source 2 look: what to copy from Half-Life: Alyx, and in what order

Plan date: 2026-10-14. Author: Claude.

**Goal:** make Obsidian frames look as close to Source 2 (Half-Life: Alyx) as possible.
This plan lists what HLA's renderer does, what Obsidian does today, and the work
to close each gap, ordered by how much it changes the look per unit of effort.

**Related:** volumetric fog was studied and rebuilt separately. See
[`../AlyxVolumetric/README.md`](../AlyxVolumetric/README.md); this plan only covers
what is left there (fog on transparents, clipmaps).

## How to read the evidence

| Tag | Meaning |
|---|---|
| **Observed** | The string, class name, setting or shader name exists in the shipped HLA files. Lists are in [`evidence/`](evidence/). |
| **Inferred** | My reading of what the observed names mean. Not traced in code. |
| **Unknown** | Not recovered. Exact numbers (curves, defaults, formulas) must come from the follow-up research below or from tuning against captures. |

Sources:
- `bin/win64/hlvr.exe` is only a launcher. It loads `engine2.dll` and calls `Source2Main`. REA Evidence `ev_f6884f5db55a9fa0ee253dc65bce8238ed7bc00bd269cb8c84e25bc4fe5c07f3`.
- `bin/win64/scenesystem.dll` holds the scene renderer: 6,954 procedures and 3,903 strings, all strings in [`evidence/scenesystem_strings.txt`](evidence/scenesystem_strings.txt). REA Evidence `ev_7660ec5071e9b03de6c5a1afb0fb6e98c0a4796bca709f80bc705213deff6f15`, Ghidra 12.1.4.
- `bin/win64/engine2.dll`: filtered strings in [`evidence/engine2_strings_filtered.txt`](evidence/engine2_strings_filtered.txt). REA Evidence `ev_9ebc5384c2886c2e1676e8cd4881acd50f2a83b98eb9f9eceaead7d7f44d9d34`.
- The game's `hlvr/bin/win64/client.dll` ran past REA's 330 s analysis limit. Its render-related strings were taken with plain `strings` and are **not REA Evidence**: [`evidence/client_render_strings.txt`](evidence/client_render_strings.txt).
- Shader names come from the `core` and `hlvr` `shaders_pc_dir.vpk` directories, also plain `strings`: [`evidence/shaders_core.txt`](evidence/shaders_core.txt), [`evidence/shaders_hlvr.txt`](evidence/shaders_hlvr.txt).

## What makes the Source 2 look

These are the traits to chase, in order of how much they define the image:

1. **Baked light everywhere.** Lightmaps with a directional variant, light probe volumes, and baked per-light shadowing. Static lights keep real-time specular while their shadows are baked. This gives soft, grounded indirect light and crisp highlights.
2. **Many local, normalised cubemaps.** Reflections come from placed cubemaps, darkened where the baked lighting is darker than at the capture point, so corners never glow.
3. **Correct metals and plain materials.** GGX specular, coloured metal reflections, no heavy screen-space effects.
4. **A sharp image.** MSAA, with no temporal smear.
5. **Restrained post.** Bloom only above a start value, and exposure metered from the share of bright pixels. Each area is graded through placed post-processing volumes.
6. **Clean, stable shadows.** Sun cascades, an atlas of cached spot shadows, a flashlight cookie, and lights with a physical size.
7. **Grounding without SSAO.** Baked AO for the world and analytic AO proxies under dynamic objects.

## HLA renderer, as observed

| Area | HLA | Tag |
|---|---|---|
| Shading model | Forward. Lights are binned per view into a texture (`CLightBinnerVr`, `lightbinner_total_data_%d.vtex`, `CLightBinner::UpdateBinTexture`), read by forward materials (`vr_complex`, `vr_standard`, `vr_skin`, `vr_eyeball`, `vr_hair`). Tonemapper `TonemappingVRForward`. | Observed names, inferred model |
| Depth prepass | Optional, can write real normals (`DepthPrepassTemp`, `DepthPrepassNormalsTemp`, "0: Use normals reconstructed from depth. 1: Output correct normals in z prepass."), shader `vr_depth_only`. | Observed |
| AA | MSAA (`-msaa`, `MSAADefaultNonVR`, `MsaaOverrideType`, `VrResolveMSAA`, `msaa_resolve_cs`). No TAA shader in `hlvr`. | Observed |
| Baked diffuse | Lightmaps from a file or vertex stream (`m_bHasBakedLightingFromLightMap`, `m_bHasBakedLightingFromVertexStream`), directional lightmaps (`vr_directional_lightmaps`), light probe volumes (`CSceneLightProbeVolumeObject`, `ChooseLightProbeVolume`). | Observed |
| Baked shadows for real-time lights | Per-light indices into baked data (`bakelightindex`, `bakelightindexscale`, `m_bUsesIndexedBakedLighting`, `CLightBinnerVr: Unsupported VrNumBakedLights`). | Observed names, inferred use |
| Reflections | Cubemap "envmap lights" (`CEnvMapLightObjectDesc`), normalised by diffuse (`vr_cubemap_normalization_max`, `vr_cubemap_normalization_roughness_begin`). No SSR shader in `hlvr`. | Observed |
| Sun shadows | Up to 4 cascades (`csm_cascade0..3_override_dist`, `csm_max_num_cascades_override`, `m_nShadowCascadeCount`, `SunLightMaxCascadeSize`, `csm_viewdir_shadow_bias`). | Observed |
| Local light shadows | One atlas (`CShadowAtlas`, `shadow_atlas_depth_%d.vtex`), cached (`vr_enable_cached_lights`, `VrLightBinnerCachedShadowMapTileSize`), spread over frames (`vr_allow_time_sliced_shadow_map_rendering`), culled by size (`r_size_cull_threshold_shadow`), with a fade distance (`sc_override_shadow_fade_min/max_dist`). | Observed |
| Light shape | Light source size (`LightSourceShape_t`, `m_flLightSourceSize0/1`, `m_flLightSourceDim0/1`), cookies (`lightcookie`, `materials/effects/lightcookies.vtex`). | Observed |
| AO | Proxies splatted for dynamic objects (`CAmbientOcclusionProxySceneObject`, `vr_aoproxy_splat`, `r_aoproxy_cull_dist`). SSAO exists only in shared `core` shaders. | Observed |
| Exposure | Occlusion queries count bright pixels (`tonemap_query`, `mat_tonemap_force_percent_bright_pixels`, `mat_tonemap_force_percent_target`, `mat_tonemap_force_log_lum_min/max`, `mat_tonemap_force_rate`). | Observed names, inferred method |
| Bloom | Part of the tonemapper (`mat_tonemap_bloom_scale`, `mat_tonemap_bloom_start_value`). | Observed |
| Grading | Compute-built colour-correction LUT (`combine_colorcorrection_lut_cs`); post-processing volumes (`C_PostProcessingVolume`, `TonemapParameters_t`, `lighting/postprocessing/*.vpost`). | Observed |
| Fog | Compute froxels plus clipmaps (`CVolumetricFogClipmapRenderer`, `cs_volumetric_fog`, `volume_fog_*`). Covered by the AlyxVolumetric research. | Observed |
| Culling | CPU software occlusion (`DepthBufferRasterizerSSEMT`, `COccluderSceneObject`), precomputed visibility (`NO_PVS_CULLING`), screen-size and distance culling. | Observed |
| HLA-only effects | Retina burn (`Engine2/RenderingPipeline/RetinaBurnFx`, `retina_burn_capture`), HMD brightness adaptation (`vr_hmd_brightness_*`). | Observed |

## Obsidian today: the gaps that matter for the look

Verified in code on 2026-10-14:

- **Metals are not coloured by albedo.** F0 is `lerp(0.04, color.g*0.25+0.75, metalness)`, built from the green channel only. It appears at `DeferredPointLight.fx:172/209/256`, `DeferredDirectionalLight.fx:350/517/571` and `DeferredEnvironmentMap.fx:416`. Gold and copper reflect grey-white.
- **SSAO darkens direct light.** `DeferredCompose.fx` returns `finalValue * ssaoContribution * transmittance`. The environment pass writes into the same diffuse/specular light buffers as the lights, so ambient and direct can't be told apart at compose time.
- **One fixed sun shadow box** (`ShadowMapRenderModule.CreateShadowMapDirectionalLight`), with no cascades. The god rays depend on it.
- **One environment cubemap**: no box projection, no GGX prefilter (hardware mips; mip = roughness / 0.04), no BRDF lookup table. A diffuse normalisation already exists, but only inside the probe volume (`DeferredEnvironmentMap.fx:496-503`).
- **Baked lighting is one L1 probe volume.** No lightmaps or UV2, no baked AO or shadows, albedo per triangle (`LightingBakeInput.cs:234` TODO).
- **Bloom threshold defaults to 0**, so the whole frame blooms (`BloomFilter.cs`, `g_BloomThreshold`). Bloom strengths are 0.25–0.5 on 5 mips.
- **Exposure is an average of log luminance** (`AutoExposureFilter.cs`), not a bright-pixel percentile.
- **Post settings are static and unsaved** (`GameSettings`, Anvil `PostProcessingViewModel`). There are no post-processing volumes.
- **TAA has no velocity buffer and no sharpening.** Every render target is single-sample.
- **The forward pass has no sun, shadows, probes or environment map** (`Forward.fx`, 40-light cap). Glass and other forward materials look flat next to deferred ones.
- **Output gamma is `pow(x, 1/2.2)`**, not exact sRGB; albedo is 8-bit gamma space.
- **Screen-space directional shadows** throw `NotImplementedException` when enabled (`Renderer.DrawScreenSpaceDirectionalShadow`).

Engine limit to plan around: MonoGame WindowsDX here uses SM4/SM5 vertex and pixel shaders only. There are no compute shaders, no `RWTexture` and no `Dispatch`. Everything below is designed as pixel-shader passes, like the froxel fog.

## Not copying

- **The forward + MSAA renderer.** It suits VR; switching would mean rewriting every lighting module. We get the sharpness through phase 1.6 instead.
- **The stereo path:** multiview instancing, per-eye SteamVR transfer, HMD brightness.
- **CPU software occlusion culling as a look item.** It is a performance feature, kept as optional phase 7.

---

## Phase 0: reference and look-dev setup (do first)

Without side-by-side references, "close to Source 2" can't be measured.

- **0.1 Reference captures.** Take screenshots from HLA in non-VR "spectator" view (the game's desktop window) at fixed spots: a lit interior, a dark interior with a flashlight, an outdoor courtyard, a metal/glass close-up. Store them under `Docs/Research/Source2Look/reference/` (git-ignored if large). These are for tuning only and never ship.
- **0.2 Look-dev scene** `Content/Scenes/LookDev.obsc`:
  - spheres on a grey floor: rough to smooth dielectric, gold, copper, chrome
  - a colour checker card
  - a dark room with a doorway to sunlight
  - a spot light with a cookie
  - glass in front of a wall
  - a character (Y Bot) on the floor
  Every phase is judged here against the captures.
- **0.3 Save post settings per scene.** Move tonemap, exposure, bloom and grading values out of static `GameSettings` into a saved per-scene block (e.g. `EnvironmentSettings.Post`, or a `PostProfile` asset) and copy them into `GameSettings` each frame, like `Renderer.ApplySceneFog`. This is a prerequisite for per-area volumes (phase 5) and for keeping tuned values.
- **0.4 Debug views.** Add render modes for indirect diffuse only, specular only, AO only and shadow cascade colours, next to the existing `RenderMode` outputs.

**Done when:** the look-dev scene loads, captures sit next to it, and post settings survive a save/load.

## Phase 1: cheap, large look wins

| # | Work | Source 2 reference | Files |
|---|---|---|---|
| 1.1 | **Tinted metal F0.** Make F0 a `float3`: `lerp(0.04, albedo, metalness)`. Thread it through `SpecularCookTorrance`, the point/directional/environment shaders and compose (metal = albedo × spec already, so check nothing is tinted twice). Replace Oren-Nayar ÷ 4 with Lambert, energy-split by `(1 - F)`. | GGX `vr_complex` materials (inferred standard PBR) | `Common/helper.fx`, `Deferred/DeferredPointLight.fx`, `DeferredDirectionalLight.fx`, `DeferredEnvironmentMap.fx`, `DeferredCompose.fx`, `Forward/Forward.fx` |
| 1.2 | **AO only on indirect light.** Write the environment/probe contribution into its own buffer (or alpha channels) so compose applies SSAO to ambient only. Add specular occlusion from AO and roughness (Lagarde). | HLA has no SSAO; AO is baked or proxy-based and never darkens direct light | `DeferredEnvironmentMapRenderModule.cs`, `DeferredCompose.fx`, `Renderer.cs` (targets + `UpdateRenderMapBindings`) |
| 1.3 | **Bloom start value and scale.** Default threshold well above 1.0 (scene-referred), soft knee, a single scale, lower strengths. Expose as `BloomStart`/`BloomScale` in the per-scene post block. | `mat_tonemap_bloom_start_value`, `mat_tonemap_bloom_scale` | `BloomFilter.cs`, `Shaders/PostProcessing/Bloom.fx`, `GameSettings.cs` |
| 1.4 | **Bright-pixel exposure metering.** Draw a full-screen pass that discards pixels below a luminance threshold inside an `OcclusionQuery` (MonoGame supports it), giving the bright-pixel count without a readback stall (read last frame's query). Steer exposure so the bright share hits `PercentTarget`, clamped by `LogLumMin/Max`, at `Rate`. Keep the current average-log metering as a fallback mode. | `tonemap_query`, `mat_tonemap_force_percent_bright_pixels`, `..._percent_target`, `..._log_lum_min/max`, `..._rate` | `AutoExposureFilter.cs`, `AutoExposure.fx`, `Renderer.UpdateAutoExposure` |
| 1.5 | **Tonemap curve and exact sRGB.** Make the curve selectable (Hejl, ACES fit, Uncharted 2 / Hable, Reinhard are already in `PostProcessing.fx`, commented out), add an exact sRGB encode, and tune curve + white point against the captures. HLA's actual curve is **Unknown** (see research R1). | `TonemapParameters_t`, `ToneMapping` | `Shaders/PostProcessing/PostProcessing.fx`, `GameSettings.cs` |
| 1.6 | **Sharp TAA.** Add a velocity target (skinned, cloth and moving objects), a contrast-adaptive sharpen after TAA, and a negative mip bias on G-buffer textures. Optionally offer FXAA as a "no TAA" mode for a pure MSAA-like still image. | HLA's sharpness comes from MSAA (`VrResolveMSAA`) | `TemporalAntialiasingRenderModule.cs`, `TemporalAntiAliasing.fx`, `GBufferRenderModule.cs`, `Gbuffer.fx`, `Renderer.SetUpRenderTargets` |

**Done when:** the look-dev gold sphere reflects gold, AO no longer darkens sunlit floors, highlights bloom but walls don't, and the image stays sharp while the camera moves.

## Phase 2: shadows and lights

- **2.1 Cascaded sun shadows.**
  - 4 cascades with practical (log/linear) splits.
  - Texel-snapped, stable cascade boxes fitted to the camera frustum.
  - Per-cascade bias scaled by texel size, plus a view-direction bias.
  - Blend between cascades and fade at the maximum distance.
  - Store the cascades in one atlas target.
  - Per-light settings: Cascade Count, Max Distance, Split Lambda, Resolution.
  - The froxel fog, foliage transmission and god rays must sample the cascades instead of the fixed box.
  - Fix or remove the `NotImplementedException` screen-space shadow path.
  - Source 2 reference: `csm_cascade0..3_override_dist`, `csm_max_shadow_dist_override`, `csm_viewdir_shadow_bias`, `SunLightMaxCascadeSize`.
  - Files: `ShadowMapRenderModule.cs`, `DirectionalLight.cs`, `DeferredDirectionalLight.fx`, `Froxel.fx`, `FroxelRenderModule.cs`, Anvil directional light Inspector.
- **2.2 Local light shadow atlas.**
  - One `HalfSingle`/`Single` atlas, e.g. 8192², replaces the per-light `size × size*6` targets.
  - Tile size by screen size or distance.
  - Static and unchanged lights keep their tiles (the cache).
  - At most N tiles redrawn per frame, highest priority first (time slicing). Lights below a size threshold lose shadows. Shadows fade between min/max distance.
  - Keep `StaticShadows` as "always cached".
  - Source 2 reference: `CShadowAtlas`, `vr_enable_cached_lights`, `vr_allow_time_sliced_shadow_map_rendering`, `r_size_cull_threshold_shadow`, `sc_override_shadow_fade_min/max_dist`.
  - Files: `ShadowMapRenderModule.cs`, `PointLight.cs`, `SpotLight.cs`, `DeferredPointLight.fx` (atlas UVs per face), `Froxel.fx`.
- **2.3 Light cookies.**
  - A `Cookie` texture on `SpotLight`, saved by content path like the decal `TexturePath`.
  - Multiplied into the spot cone in deferred, forward and fog.
  - Ship a flashlight cookie for the look-dev scene.
  - Source 2 reference: `lightcookie`, `materials/effects/lightcookies.vtex`, `VrLightCookieSettings`.
- **2.4 Light source size.**
  - `SourceRadius` (sphere) and optional `SourceLength` (tube) on point/spot lights.
  - Specular uses the representative-point method (Karis 2013) with the matching roughness widening.
  - Check that attenuation is inverse-square with a smooth window at `Radius`; change it if not.
  - Source 2 reference: `LightSourceShape_t`, `m_flLightSourceSize0/1`, `m_flLightSourceDim0/1`.

**Done when:** sun shadows stay sharp near the camera and reach far without shimmering, 20+ shadowed spot lights cost a fixed budget per frame, and the flashlight has a cookie.

## Phase 3: reflections and forward parity

- **3.1 GGX prefiltered cubemaps.**
  - After each capture, a pixel-shader pass per mip convolves the cubemap with GGX importance sampling, roughness = mip / (mips - 1).
  - Add a split-sum BRDF lookup texture (generated once at startup).
  - Replace `mip = roughness / 0.04` and the `(1-roughness)*fresnel` term.
  - Files: `Renderer.DrawCubeMap`, new `EnvironmentPrefilter.fx`, `DeferredEnvironmentMap.fx`.
- **3.2 Many reflection probes.**
  - A **Reflection Probe** component (type id `reflection-probe`, saved by that id; follow "Adding a Gameobject Component" in CLAUDE.md).
  - Settings: box size, blend distance, box projection on/off, priority, capture on bake.
  - Cubemaps are stored as `.dds`/`.xnb` sidecars like the probe file.
  - The environment pass renders each probe's box as a volume (like point lights) and blends by weight; the scene sky cubemap fills what no probe covers.
  - The current `EnvironmentSample` becomes the global probe.
  - Source 2 reference: `CEnvMapLightObjectDesc`, `materials/deferred_lights/envmaplight.vmat`.
- **3.3 Normalise every probe.**
  - Generalise the existing irradiance-ratio factor (`DeferredEnvironmentMap.fx:496-503`) to every probe.
  - Store each probe's L1 irradiance at capture time.
  - Scale specular by `min(irradiance(pixel) / irradiance(capture), NormalizationMax)`, faded in from `NormalizationRoughnessBegin` upwards.
  - Inside lightmapped areas (phase 4) use the lightmap irradiance.
  - Source 2 reference: `vr_cubemap_normalization_max`, `vr_cubemap_normalization_roughness_begin`.
- **3.4 Forward pass parity.**
  - `Forward.fx` gets the sun with cascaded shadows, reflection probes, the probe volume and ambient.
  - Lights are sorted by importance instead of truncated at 40.
  - Forward surfaces also get fog: the remaining AlyxVolumetric item, sampling the froxel atlas at the fragment.
  - Source 2 reference: every HLA material is forward and fully lit (`vr_glass`, `vr_complex`).

**Done when:** chrome spheres in the dark room reflect the room rather than the sky and don't glow, rough metals show blurred (not blocky) reflections, and glass picks up the sun, shadows and fog.

## Phase 4: baked lighting (largest look change, largest cost)

- **4.1 Better probe bake (short term).**
  - Per-texel albedo (the `LightingBakeInput.cs:234` TODO).
  - Emissive surfaces as light sources.
  - Several probe volumes per scene, each with a priority and blend margin.
  - Optional L2 SH.
  - Source 2 reference: `CSceneLightProbeVolumeObject`, `ChooseLightProbeVolume`.
- **4.2 Lightmaps (long term).**
  - UV2: generate on import with an atlas packer such as xatlas (native, MIT), or accept an authored UV2; store it in the model's `.xnb` via the content pipeline processor.
  - Baker: reuse `ProbeVolumeBaker` / `TriangleBvh`, tracing per lightmap texel, CPU parallel like today.
  - Output per texel: irradiance plus a dominant direction (directional lightmap), baked AO, and a shadow mask for up to 4 static lights (indices + occlusion).
  - Runtime:
    - The G-buffer reads lightmaps for static gameobjects.
    - Indirect diffuse = lightmap; probes are used only for dynamic objects.
    - Static lights evaluate specular and direct diffuse in real time, multiplied by their baked shadow mask channel instead of a shadow map, which also frees atlas budget.
  - Storage: per-scene sidecar `<scene>.lightmaps` + textures, invalidated by a bake hash like the probe file.
  - Anvil: Bake button (Inspector > Lighting), a "Static" flag per gameobject, and texel density.
  - Source 2 reference: `m_bHasBakedLightingFromLightMap`, `vr_directional_lightmaps`, `bakelightindex`, `m_bUsesIndexedBakedLighting`, `VrNumBakedLights`.

**Done when:** the dark room in look-dev is lit only by bounce light from the doorway, with soft contact shadows and no SSAO, and moving the camera doesn't change it.

## Phase 5: per-area look authoring

- **5.1 Post-processing volume component.**
  - Type id `post-volume`.
  - Box shape, blend distance, weight, priority, and an "infinite" (global) flag.
  - It overrides any subset of the phase 0.3 post block: exposure (target, min/max, rate, fixed), tonemap white point and curve, bloom start/scale, LUT, vignette, fog colour/density scale.
  - Blended by camera position each frame, highest priority last.
  - Profiles can live as assets (`.post` JSON in `Content/PostProfiles`, like `.mat` material assets).
  - Scripts can change weights (death and pause fades are how HLA uses `dead_game.vpost` / `paused_game.vpost`).
  - Source 2 reference: `C_PostProcessingVolume`, `TonemapParameters_t`, `lighting/postprocessing/*.vpost`, `hlvr_dead_postprocess_fadein`.
- **5.2 LUT per profile.** Allow a LUT per profile and blend two LUTs while crossing volumes (HLA builds a combined LUT, `combine_colorcorrection_lut_cs`; a pixel-shader blend into a 32³ strip does the same here).
- **5.3 Retina burn (optional, HLA signature).** Bright lights leave a fading afterimage: feed thresholded luminance into an accumulation target that decays slowly, and add it as a tint (`RetinaBurnFx`, `retina_burn_capture`). Render feature or built-in pass.

## Phase 6: grounding dynamic objects

- **6.1 AO proxies.**
  - An **AO Proxy** component (type id `ao-proxy`): a box or capsule (auto-fit to bounds or to skeleton bones for skinned models).
  - Drawn as analytic AO volumes into the indirect-light buffer before compose.
  - Distance cull via `CullDistance`.
  - Gives characters and props a soft contact shadow without SSAO.
  - Source 2 reference: `CAmbientOcclusionProxySceneObject`, `vr_aoproxy_splat`, `r_aoproxy_cull_dist`, "Distance to cull the AO proxy as a factor of size".
- **6.2 Material AO map.** A seventh material texture slot, packed into a G-buffer channel (e.g. albedo alpha, moving the WetSurface flag into the material type bits) and multiplied into indirect light only.

## Phase 7 (optional): performance parity

- Screen-size culling (`r_size_cull_threshold`-style) in `MeshMaterialLibrary.FrustumCulling`.
- Occlusion culling, in this order:
  1. `OcclusionQuery` on bounding boxes, using last frame's results.
  2. Or a Hi-Z test against last frame's depth.
  3. Or HLA's approach: a CPU depth rasterizer of artist-placed occluders (`DepthBufferRasterizerSSEMT`, `COccluderSceneObject`).
- An optional depth prepass with normals (`DepthPrepassNormalsTemp`) if G-buffer overdraw becomes a cost.

---

## Research follow-ups (REA), before tuning

Do each before the phase that needs its numbers. Run REA against the same files. The REA MCP server needs `GHIDRA_INSTALL_DIR` set in its `~/.claude.json` env; until then use the CLI with `--snapshot`.

| ID | Question | Where | For |
|---|---|---|---|
| R1 | Tonemap curve, white point, gamma | Decompile `CTonemapRenderer` / `CTonemapSystem` in `scenesystem.dll`; disassemble the DXBC of `post_process` in `hlvr/shaders_pc_*.vpk` | 1.5 |
| R2 | Exposure metering threshold and defaults | `CTonemapSystem::GenerateRendererData`, `mat_tonemap_force_*` registration sites | 1.4 |
| R3 | Bloom start value / scale defaults and filter | Same functions; `bloom`, `gaussian_bloom_blur` shaders | 1.3 |
| R4 | Cubemap normalisation formula | `vr_cubemap_normalization_*` xrefs in `scenesystem.dll`; `vr_complex` pixel shader | 3.3 |
| R5 | Cascade split defaults and bias values | `csm_*` registration sites, `CSunLightManager` | 2.1 |
| R6 | Baked light index packing (how many lights per texel, channel layout) | `bakelightindex` xrefs, `CLightBinnerVr::InitForFrustum` | 4.2 |
| R7 | AO proxy shape and falloff | `vr_aoproxy_splat` shader DXBC | 6.1 |

`client.dll` exceeds REA's 330 s analysis limit. If it is needed, analyse it in Ghidra directly and import the result, or ask the REA maintainers about a longer provider timeout.

## Working rules for every phase

- Every new setting is saved by name and appears in the Anvil Inspector (components follow "Adding a Gameobject Component" in `CLAUDE.md`).
- New render targets go into both `SetUpRenderTargets` and `UpdateRenderMapBindings` (`Engine_Wiring.md` § render target ownership).
- Shaders stay `vs_5_0`/`ps_5_0` or lower; no compute.
- Add checks to `Tests/Components` (`--graphics` for GPU passes) and compare the look-dev scene against the captures before closing a phase.
- Update `Docs/markdown` (new page per system), `Engine_Wiring.md`, the CLAUDE.md repo map, and `CHANGELOG.md`.

## Suggested order and rough size

| Phase | Look impact | Size |
|---|---|---|
| 0 Reference + per-scene post | Enables everything | S |
| 1 Cheap wins | High | M |
| 2 Shadows and lights | High | L |
| 3 Reflections + forward parity | High | L |
| 4.1 Better probes | Medium | M |
| 5 Post volumes | Medium (per-area mood) | M |
| 6 AO proxies | Medium | M |
| 4.2 Lightmaps | Very high | XL |
| 7 Culling | None (performance) | M–L |

Lightmaps change the look the most but cost the most. Phases 1–3 and 4.1 get most of the way first, and the lightmap baker reuses their pieces (BVH tracer, normalised probes, shadow-mask evaluation).
