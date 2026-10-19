# Source 2 look: lighting, reflections, shadows and post processing

Obsidian's renderer follows the look of Source 2 (Half-Life: Alyx): physically based metals, ambient occlusion only on
indirect light, cascaded sun shadows, a shared shadow atlas for local lights, light cookies and sized light sources,
GGX-prefiltered and normalised reflection probes, a better probe bake with detail volumes, AO proxies under moving
objects, and post processing saved per scene and blended by volumes. The plan and the evidence it is based on are in
[`Docs/Research/Source2Look/README.md`](../Research/Source2Look/README.md); this page is how to use what was built.

The quickest way to see it is the **LookDev** scene (`Content/Scenes/LookDev.obsc`): rough-to-smooth plastic
spheres, gold/copper/chrome/iron, a colour checker, a dark room lit through a doorway with a flashlight cookie, a tube
light, a reflection probe, a detail probe volume and an emissive panel, a glass pane, and Y Bot and the Dragon on the
floor with AO proxies.

## At a glance

| Feature | Where to set it | Saved in |
|---|---|---|
| Tonemap curve, exposure, grading, LUT, eye adaptation, bloom, retina burn | Anvil Window > **Post Processing** (sections marked *scene*) | the scene (`EnvironmentSettings.Post`) |
| Per-area post processing | Add Component > **Post Process Volume**, or a `.post` profile | the scene / `Content/PostProfiles/*.post` |
| Sun cascades | Inspector of a directional light: Cascades, Shadow Dist., Split Lambda, Shadow Res. | the scene |
| Local light shadows | Inspector of a point/spot light: Cast Shadows, Shadow Res., Static Shadows | the scene |
| Light cookie | Inspector of a spot light: **Cookie** (a built Content texture) | the scene |
| Light source size | Inspector of a point/spot light: **Source Radius**, **Tube Length** | the scene |
| Local reflections | Add Component > **Reflection Probe** on a box gameobject | the scene + `.reflections` sidecar |
| Finer baked light in a room | Add Component > **Probe Volume** on a box gameobject, then Inspector > Lighting > Bake | the scene + `<scene>.probes` |
| Contact shadows under moving objects | Add Component > **AO Proxy** | the scene |
| Material ambient occlusion map | Material component > Textures > **Ambient Occlusion** | the scene / `.mat` |
| Anti-aliasing, shadow atlas, reflection probe size, culling | Post Processing > Anti-Aliasing / Shadows & Reflections | project settings (`GameSettings`) |
| Debug views | Post Processing > Viewport > Debug View, or F1 | not saved |

## Shading model

- **Metals reflect their own colour.** F0 = `lerp(0.04, albedo, metallic)` as a colour (`GetF0` in
  `Content/Shaders/Common/helper.fx`). Gold reflects gold.
- **Lambert diffuse, energy-split by Fresnel**: diffuse = `(1 - F) · NdotL / π`, specular = GGX
  (`EvaluateLightSplit`). Every light shader (deferred point/spot, sun, forward) uses the same functions.
- **Ambient occlusion darkens only indirect light.** The environment pass writes the indirect diffuse into its own
  target (`_renderTargetIndirect`, rgb = indirect diffuse, a = AO); SSAO, the material AO map and AO proxies multiply
  only that, and compose adds it to the direct light. Specular reflections get a specular occlusion from AO and
  roughness (Lagarde). Sunlit floors are never darkened by SSAO.
- **Material AO map**: a seventh texture slot on the Material component (`AmbientOcclusionTexture`) and
  `MaterialEffect.AmbientOcclusionMap`. It is stored in the G-buffer albedo alpha. (The Wet Surface flag moved into
  the material type bits: type 7 in the normal target's blue channel.)

## Post processing per scene

The tonemapping, exposure, grading and bloom values are part of the scene now (`EnvironmentSettings.Post`, a
`PostProcessSettings`). Every frame `Renderer.ApplyScenePost` copies them, blended with the volumes around the camera,
into `GameSettings`. Anvil's Post Processing panel edits the scene values in its *scene* sections; the other sections
are project-wide renderer settings.

| Setting | What it does |
|---|---|
| Tonemap | Curve: **Source2** (default): Half-Life: Alyx's `post_process` curve. It is Hable's 8-parameter form on 2.8 × the exposed colour, normalised by its value at 2^*White (stops)*, with parameters *Shoulder, Linear Strength, Linear Angle, Toe Strength, Toe Numerator, Toe Denominator* from the `.vpost` profiles; the defaults are HLA's neutral 0.15 / 0.5 / 0.1 / 0.2 / 0.02 / 0.3 / 4. Also **Hejl** (the engine's original filmic fit), **Aces**, **Hable** (Uncharted 2), **Reinhard**, **Linear**. See research R1. |
| Exposure, White Point, S-Curve | Base exposure in EV (HLA's exposure bias, default 0), White Point (Hejl/Hable/Reinhard only), S-curve. |
| Exact sRGB | The exact piecewise sRGB encode instead of `pow(x, 1/2.2)`. |
| Color Grading, LUT | A 32³ strip LUT (Content path). Two volumes with different LUTs cross-fade (`ColorGradingFilter.Combine`). |
| Saturation, Contrast, Color Filter, Vignette, Chromatic Aberration | Final-pass grading. |
| Auto Exposure, Metering | **Source2** (default, Half-Life: Alyx's log-luminance tonemap system): the frame's median luminance is exposed to *Key* (0.18) as `Key / 2^ln(median)`, so it follows L^-0.69 and dark rooms stay darker. Defaults from the game: EV -2 to +3 (0.25 to 8), adaptation 1.2 / 0.3 per second, no centre weight. **Bright Pixels** (Source 1's tonemap controller): the brightest *Bright Pixel %* of the frame is exposed to *Bright Target*, read off a 64-bin luminance histogram built on the GPU, never darker than *Min Average Luminance*. **Average**: the centre-weighted log average is exposed to *Key*. Min/Max EV, adaptation speeds and centre weight apply to all three. |
| Bloom Threshold, Start, Knee, Scale | **Source2** (default): the colour blooms by `saturate((luma × exposure - Start) / Knee)`, luma = 0.3/0.59/0.11, as in HLA's bloom shader; defaults are HLA's base profile (1.976, width 2.3655). **Soft Knee**: the brightest channel above *Start* with a quadratic knee below it. *Scale* is the bloom's strength (added before the curve, like HLA's Add mode; still chosen by eye). Walls don't bloom; highlights and lamps do. |
| Fog Density Scale | Multiplies the scene's volumetric fog (useful in volumes: a dusty room). |
| Retina Burn | Bright lights leave a fading afterimage (Half-Life: Alyx). 0 = off. |

From a script: `SceneEnvironment.Post.Exposure = 1.2f;` (restored when Play stops, like the rest of the environment).

## Post Process Volumes

A **Post Process Volume** component turns the gameobject's box (a Cube with its position, rotation and scale; the
mesh is hidden while the volume is enabled) into an area with its own post processing:

- Tick the rows to override in the component's **Overrides** list; editing a value ticks its row. Unticked settings
  keep the scene's values.
- **Infinite** applies everywhere. **Blend Distance** fades the volume in over that many metres outside the box.
  **Weight** (0..1) scales it. Volumes blend lowest **Priority** first, so the highest wins where they overlap.
- **Profile**: a `.post` file (`PostProfiles/Dark.post`; a bare name goes into `Content/PostProfiles`). When it
  loads, its values replace the component's own (still only the ticked rows). Files are `{ "Version": 1, "Post": {...} }`
  and are shipped with builds. Write one from code with `PostProfile.Write("Dark", settings)`.
- Death or pause fades: give an Infinite volume weight 0 and raise it from a script:
  `GetComponent<PostProcessVolumeComponent>().Weight = fade;`.

Numbers and colours interpolate; switches, curves and paths flip at half weight; LUTs cross-fade. Code:
`PostProcessVolumes.Evaluate`, `PostProcessSettings.BlendTowards`.

## Anti-aliasing and sharpness

- The G-buffer writes **motion vectors** (`_renderTargetVelocity`, 4th MRT, from each instance's previous world
  matrix), so TAA reprojects moving, skinned and cloth objects correctly (`g_taa_velocity`).
- History is sampled with Catmull-Rom and a **contrast-adaptive sharpen** runs on the result
  (`g_taa_sharpness`, default 0.35). G-buffer textures use a -0.5 mip bias.
- With TAA off, **FXAA** (`g_fxaa`) smooths the final image instead.

## Sun shadows (cascades)

Directional lights have up to 4 cascades: practical splits between uniform and logarithmic (**Split Lambda**, default\n0.85 like HLA's `csm_split_log_scalar`),
fitted as stable, texel-snapped spheres around each slice of the view up to **Shadow Distance** (default 150 m), packed
2 × 2 in one atlas (each cascade at most 2048 texels when there are several). The shader biases by texel size along
the normal and the view direction, blends across cascade borders and fades out at the end. The fog, the forward pass
and foliage translucency sample the same cascades. The old fixed shadow box and the screen-space sun shadow path are
gone. Large outdoor scenes raise Shadow Distance (GodRayTest uses 420 m, lambda 0.85).

## Point and spot light shadows: the shadow atlas

Every point/spot light shadow lives in one `ShadowAtlas` (`g_shadowatlassize`, default 4096²). Each cube face the
light needs (a spot light only the faces its cone reaches) gets a power-of-two tile from a quadtree allocator. The
tile size follows the light's size on screen (radius / distance) up to its **Shadow Res.**, and the tile stays with
the light, so lights and objects that didn't move keep their shadows without redrawing (**Static Shadows** = never
redraw once drawn, and a higher priority). Lights are served biggest on screen first; when the atlas is full a light
gets smaller tiles, down to 64 texels, and then none. At most `g_shadowupdatesperframe` (8) lights redraw per frame
(the rest keep last frame's shadow). Lights smaller on screen than `g_shadowsizecull` lose their shadows, and shadows
fade out between `g_shadowfadestart` and `g_shadowfadeend` metres from the light's sphere.

## Cookies and light size

- **Cookie** (spot lights): a texture projected along the cone, multiplied into the deferred light and the fog (one
  cookie light in the fog at a time). Use any built Content texture; `System/Cookies/flashlight.png` ships with the
  engine.
- **Source Radius** makes a light a glowing sphere, **Tube Length** a tube along the light's X axis (rotate the
  light). Specular highlights widen to match (Karis' representative point), so smooth floors show a lamp's shape
  instead of a pin-point.
- **Falloff** is a windowed inverse square around a small core: `window² · core² / (d² + core²)`, core =
  max(radius / 5, Source Radius), window = `1 - (d / radius)^4` (`PointLight.Attenuation`). It reaches zero at the
  light's radius. Point lights look brighter close up than before this change.

## Reflections

- **GGX prefiltered cubemaps**: every capture (the scene's environment sample and each reflection probe) is
  convolved per mip (`EnvironmentPrefilter`, `Shaders/Deferred/EnvironmentPrefilter.fx`); mip m holds roughness
  m / (mips - 1). A split-sum **BRDF lookup texture** replaces the old `(1 - roughness) · fresnel` term.
- **Reflection Probe** component: a cubemap captured at the gameobject's position that is used inside its box. Box
  Projection lines reflections up with the walls; Blend Distance fades it in from the faces; Priority wins where
  boxes overlap; Resolution 64-512 per face. Save the scene after capturing to retain the filtered HDR cubemaps in
  its `.reflections` sidecar. Saved probes restore on load; missing captures, moved or changed probes, and
  Inspector > **Capture** trigger capture, one probe per frame; at most 4 are blended per view. Captures include the sun's
  shadows only where the camera's cascades reach.
- **Normalisation**: with baked probes, a reflection is scaled by the baked light at the pixel versus at the
  capture point, so chrome in a dark room no longer glows with the sky. This is HLA's formula: darkening is never
  limited; brightening is capped at 1 up to roughness `g_cubemapnormalizationroughnessbegin` (0.1) and rises linearly
  to `g_cubemapnormalizationmax` (32) at roughness 1 (Post Processing > Normalize Max / Rough.).
- Diffuse ambient without baked probes comes from the prefiltered global cubemap's last mip. Environment >
  **Diffuse Strength** 0.2 equals the full irradiance.

## Forward pass (glass and other Forward Shaded materials)

`Forward.fx` lights forward materials like the deferred ones: the sun with its cascades, up to 40 point/spot lights
ranked by brightness reaching the camera (sized sources, spot cones), the probe volume and reflection probes,
volumetric fog at the fragment, and premultiplied alpha (Opacity). Point/spot shadows use the shared shadow atlas,
including filtering and distance fade. Spot cookies tint and shape both diffuse light and specular reflections,
including those on clear glass. Multiple cookies share a cached atlas (up to 512 texels per cookie) prepared before
scene rendering; projection follows each spotlight's position, cone and rotation.

## Baked probes

Inspector > Lighting > **Bake** now:

- reads each surface's albedo per texel (from the Material component when there is one, else the model's material),
- treats emissive surfaces as lights (next-event estimation, so a glowing panel lights a room without noise),
- bakes up to 3 **detail probe volumes** (Probe Volume components: Spacing, Priority, Blend Margin) next to the main
  volume, saved in the same `.probes` file (format v3). Shaders blend them by priority; the main volume fills the rest.
- treats the ground below the horizon as a grey surface instead of sky blue.

## AO proxies

An **AO Proxy** component gives a moving gameobject a soft contact shadow on the indirect light around it:
**Auto** uses one capsule per main bone for skinned models and an ellipsoid around the model otherwise; or pick
Ellipsoid, Capsule or Bones. Intensity, Size Scale, Falloff (in radii), and Cull Distance (times the object's size).
At most 16 shapes are drawn, nearest first. Direct light is never darkened.

## Culling (performance)

- **Screen-size culling**: meshes smaller on screen than `g_sizecullthreshold` (shadows: `g_sizecullthresholdshadow`)
  are skipped.
- **Occlusion culling** (off by default, `g_occlusionculling`): after the G-buffer each instance's bounding box is
  tested against the depth in an occlusion query; the next frame skips the hidden ones (one frame late, no stall).
  Shadows and captures never skip. `GameStats.OccludedMeshes` counts them.

## Debug views

Post Processing > Viewport > Debug View (or F1): **IndirectDiffuse** (baked/ambient light only),
**AmbientOcclusion** (what multiplies the indirect light), **ShadowCascades** (cascade colours over the image), next
to the existing Diffuse, Specular, SSAO, HDR... modes.

## Look-dev captures

The standalone engine can render a scene to a PNG and quit, for before/after comparisons:

```powershell
Engine.exe --scene Scenes/LookDev.obsc --shot lookdev.png --camera 0,-14,3.2,0,0,0.8 --frames 90 --size 1280x720
Engine.exe --scene Scenes/LookDev.obsc --shot indirect.png --rendermode IndirectDiffuse
Engine.exe --scene Scenes/LookDev.obsc --bake --save --shot baked.png   # bake the probes and save the scene
Engine.exe --scene Scenes/LookDev.obsc --shot oc.png --set g_occlusionculling=true
```

`--play` starts Play first. The log prints the adapted exposure. `.claude/skills/run-obsidian/lookdev_views.sh`
captures the standard LookDev views. Code: `Engine/Logic/LookDevCapture.cs`.

## Not done

- **Lightmaps** (plan phase 4.2: UV2, per-texel baking, directional lightmaps, baked shadow masks). The probe volumes
  are still the only baked diffuse.
- **Reference captures from Half-Life: Alyx** (plan 0.1). The research follow-ups R1–R7 are done
  (`Docs/Research/Source2Look/README.md`, "Research follow-ups: findings"). Still chosen by eye: Bloom Scale (HLA's
  base profile uses a Screen blend, which isn't implemented) and the AO proxy falloff. HLA uses a precomputed
  sphere/cone visibility LUT over 4 directions (R7). Sun shadows keep 150 m and 4 cascades; HLA stops at 1000 units
  (about 25 m) with 3.
- Scenes without a valid saved reflection capture generate it at runtime; save the scene to retain it for later loads.
- An optional depth prepass with normals.

## Code map

| Area | Files |
|---|---|
| Shading helpers | `Content/Shaders/Common/helper.fx` (PBR block), `Common/ProbeVolume.fx`, `Common/Reflections.fx`, `Common/AoProxies.fx`, `Common/FroxelFog.fx` |
| Lights | `Deferred/DeferredDirectionalLight.fx`, `Deferred/DeferredPointLight.fx`, `PointLightRenderModule`, `Entities/DirectionalLight.cs`, `PointLight.cs`, `SpotLight.cs` |
| Shadows | `ShadowMapRenderModule` (`DrawCascades`, `SplitCascades`, `DrawLocalLightShadows`), `Renderer/Lighting/ShadowAtlas.cs` |
| Reflections | `Renderer/Lighting/EnvironmentPrefilter.cs`, `ReflectionProbes.cs`, `Components/ReflectionProbeComponent.cs`, `DeferredEnvironmentMapRenderModule` + `ImageBasedLightingParameters`, `Deferred/DeferredEnvironmentMap.fx` |
| Bake | `LightingBakeInput`, `ProbeVolumeBaker`, `ProbeVolumeData`, `LightingSystem`, `Components/ProbeVolumeComponent.cs` |
| Post | `Logic/PostProcessSettings.cs`, `PostProcessVolumes.cs`, `PostProfile.cs`, `Components/PostProcessVolumeComponent.cs`, `Renderer.ApplyScenePost`, `AutoExposureFilter` + `AutoExposure.fx`, `BloomFilter` + `Bloom.fx`, `PostProcessing.fx`, `ColorGrading.fx`, `RetinaBurn.fx`, `Fxaa.fx` |
| TAA | `GBufferRenderModule` (velocity), `TemporalAntialiasingRenderModule`, `TemporalAntiAliasing.fx` |
| AO proxies | `Components/AoProxyComponent.cs` (`AoProxyShapes`) |
| Culling | `Renderer/Helper/OcclusionCulling.cs`, `MeshMaterialLibrary.SizeCullThreshold` |
| Anvil | `PostProcessingViewModel`, `PostProcessVolumeComponentViewModel`, `ReflectionProbeComponentViewModel`, `ProbeVolumeComponentViewModel`, `AoProxyComponentViewModel`, light rows in `SceneObjectViewModel.LightInfo` |
| Checks | `Tests/Components/Source2LookChecks.cs` (+ `RunGraphics` with `--graphics`), `AutoExposureChecks` (bright-pixel metering) |
