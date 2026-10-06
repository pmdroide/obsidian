# Scene lighting and fog

How a scene is lit, what each Anvil panel controls, how to light an interior, and what
doesn't work well yet. Coordinates are Z-up throughout.

## What lights a pixel

Every frame, `Renderer.Draw` combines these sources:

1. **Direct lights.** Directional lights (the sun) and point lights from the scene,
   with shadow maps. They are drawn by `LightAccumulationModule`.
2. **Environment cubemap.** A cubemap captured at the scene's environment sample
   position. It gives every pixel a reflection (specular) term and a small diffuse
   ambient term (`DeferredEnvironmentMap.fx`). On its own it is not occluded, so
   it lights a closed room as if the room were standing where the cubemap was captured.
3. **Baked probe volume** (optional). A grid of irradiance probes baked offline. Where
   it exists, it replaces the cubemap's diffuse ambient and darkens the cubemap
   reflections. It is the only large-scale occlusion the engine has, so **every scene
   with interiors needs a bake**.
4. **Screen-space effects.** SSAO, SSR and screen-space sun shadows. These only see
   what is on screen.
5. **Volumetric (froxel) fog.** Light scattered by the air, including sun shafts. It is
   added in `DeferredCompose.fx`.
6. **Exposure.** A manual exposure, a day/night offset and eye adaptation (auto
   exposure) are applied before tonemapping in `PostProcessing.fx`.

## Direct lights

Add lights from **GameObject > Light > Directional Light / Point Light**, or from the
Hierarchy **+** menu. New directional lights use intensity 100, which is a sunny day.

- **Directional light:** direction, colour, intensity and **Cast Shadows** in Anvil.
  The shadow settings (`ShadowSize` and `ShadowDepth` in metres, `ShadowResolution`,
  `ShadowFiltering`) are only in the scene file. The shadow map is an orthographic
  box around the light's position, so keep the scene inside it.
- **Point light:** position, radius, colour and intensity. For scale, intensity 3
  with radius 7 is barely visible next to a sun of 100.
- **Day/night cycle** (Environment panel, below): while it is on, the renderer swaps
  in its own sun and moon and ignores the scene's directional lights for shading.

## Environment panel

Open it with the **Environment** button in the Hierarchy. Settings are saved in the
scene (`Environment` in the `.obsc`).

- **Sky mode:** the default sky, a 360° panorama (**Choose skybox…**), or the
  day/night cycle (starting hour, cycle length).
- **Sky / Sun, moon and stars / Clouds:** look of the procedural sky.
- **Exposure > Day (EV) / Night (EV):** offsets added to the Post Processing exposure,
  blended by daylight. They only apply while the day/night cycle is on.

The environment sample (where the reflection cubemap is captured, and its
`SpecularStrength` and `DiffuseStrength`) is **not editable in Anvil**. Change it in
the scene file:

```json
"EnvironmentSample": { "Position": [0, -12, 2], "SpecularStrength": 1, "DiffuseStrength": 0.2, "AutoUpdate": true }
```

Put it somewhere representative of most of the scene, usually outdoors at eye height.

## Baked lighting (probe volume)

Open **Window > Lighting**. Settings are saved in the scene (`Lighting` in the `.obsc`).
The bake itself is written to `<scene>.probes` next to the `.obsc` **when the scene
is saved**.

| Control | What it does |
| --- | --- |
| **Bake Lighting** / **Cancel** / **Clear** | Bakes on a worker thread; the editor stays usable. |
| **Use baked lighting** | Turns the probes on or off at runtime (no rebake). |
| **Show probes** | Draws every probe, coloured by its light, plus the volume bounds. Magenta probes were inside geometry. |
| **Intensity** | Runtime multiplier on the baked light. |
| **Fit to scene** / **Padding** | Volume = all meshes' bounds + padding. Untick it to set **Min** / **Max** by hand. |
| **Probe Spacing** / **Max / Axis** | Distance between probes. The spacing grows automatically if an axis would need more than Max / Axis (default 48) probes. |
| **Rays / Probe** | Monte Carlo rays per probe per bounce. More rays means smoother light and a longer bake. |
| **Bounces** | Indirect bounces (default 2). |
| **Validity** | Fraction of back-face hits above which a probe counts as inside geometry and is filled from its neighbours. |
| **Sky Color / Intensity** | Light from rays that escape the scene. Rebake to apply. |

How the probes are used (`DeferredEnvironmentMap.fx`, `SampleProbeVolume`):

- **Diffuse:** inside the volume, probe irradiance replaces the cubemap's diffuse
  ambient. It fades back to the cubemap within half a cell of the volume's border.
- **Reflections:** cubemap reflections are scaled by *probe light here ÷ probe light at
  the cubemap's capture point*, clamped to 1. A closed room therefore stops reflecting
  the sunny outdoors. SSR is not scaled.
- **Wall leaks:** walls and ceilings are usually thinner than the probe spacing.
  Sampling therefore:
  - reads half a cell out along the surface normal;
  - blends the 8 nearest probes by hand, leaving out any probe whose view of the
    point is blocked. Each probe stores its mean free distance along ±X/±Y/±Z
    (`ProbeVolumeData.DistPos` / `DistNeg`);
  - down-weights probes behind the surface.

  The CPU version (`ProbeVolumeData.EvaluateIrradiance`) matches it, so later
  bounces in the bake don't leak either.

### Lighting an interior: checklist

1. Bake. Without probes, interiors get the outdoor cubemap and look flat and milky.
2. Use manual bounds around the building, not **Fit to scene**. A large ground
   plane stretches an automatic volume, and the spacing grows.
3. Pick the spacing and **Min** so that probe rows fall *beside* thin walls, not
   inside them. The volume's bottom must be below the floor. `AutoExposureTest`
   uses 0.57 m spacing with bounds (-9.12, -15.39, -0.27) to (9.12, 7.98, 7.14).
   That puts probes at x = ±3.99 / ±4.56 around walls spanning 4.0–4.3.
4. If walls look blotchy, raise **Rays / Probe**. Small bright openings (doors,
   windows) are noisy at 256; 1024 is clean.
5. Rebake after moving geometry, lights or the sun, then save the scene to write the
   `.probes` file.

## Exposure and eye adaptation

**Window > Post Processing**:

- **Exposure** (EV, default 0.75) is the base exposure.
- **Eye Adaptation** meters the HDR image (centre-weighted log-average luminance) and
  moves an exposure offset in EV towards the value that maps the average to
  **Key Value**:

  | Setting | Default | Meaning |
  | --- | --- | --- |
  | Key Value | 0.18 | Target average (middle grey). |
  | Min EV / Max EV | -3 / +3 | Clamp on the automatic offset. |
  | Dark → Light | 3 /s | Speed when the scene gets brighter. |
  | Light → Dark | 1 /s | Speed when it gets darker (slower, like eyes). |
  | Center Weight | 0.5 | 0 meters the whole frame equally; 1 favours the centre. |

Eye adaptation can only react to what the lighting gives it. Outdoors to a baked
interior in `AutoExposureTest` is about 3 stops, and the offset swings from about
-2 EV outside to +0.5…+1.7 EV inside.

## Volumetric fog

**Window > Post Processing > Volumetric Fog**. It is a froxel grid of 160 × 90 × 128
over the view frustum, with logarithmic depth slices from 1 m to the far plane
(`FroxelRenderModule`, `Froxel.fx`).

| Setting | Default | Meaning |
| --- | --- | --- |
| Density | 0.03 | Fog density at full distance. |
| Absorption | 0.1 | How much the fog darkens what is behind it. |
| Sun Scatter | 0.1 | In-scattering strength for the sun. |
| Point Scatter | 1.5 | In-scattering strength for point lights. Higher than the sun's because of 1/r² falloff. |
| Anisotropy | 0.45 | Henyey-Greenstein *g*. Above 0, light scatters forwards: looking towards the sun glows. |
| Start Dist. / Full Dist. | 30 m / 400 m | Density ramps linearly from 0 at Start to full at Full. |
| Sky Fog | 0.1 | How much fog covers sky pixels. |
| History | 0.7 | Temporal blend. Higher is smoother but laggier. |

The sun's scattering uses its shadow map, so shafts form wherever the sun reaches,
for example through a window. Point lights use their cube shadow maps when they cast
shadows.

Each froxel tests the sun's shadow map at 6 points spread through its depth
(`ComputeShadowAlongSlice`). Distant slices are about 10 m deep, so a single test aliased
against the 12 m roof-slat spacing in `GodRayTest` and turned the shafts into blotches.

## Image clarity

What keeps the frame free of speckle and haze, and what to check if it comes back.
`GodRayTest` is the reference scene.

- **Material roughness is used as authored.** `GameSettings.m_defaultroughness` only
  applies in the default-material debug view. It used to replace the roughness of every
  material without a roughness map with 0.5, so matte surfaces got glossy reflections.
- **SSR blends into the cubemap reflection.** The SSR pass writes the reflected colour
  and a coverage value (edge and facing fade). `DeferredEnvironmentMap.fx` (`GetSSR`)
  averages a depth-aware 5 × 5 neighbourhood, then blends by coverage with the same
  `(1 - roughness) × fresnel` weight as the cubemap. Before, a ray hit replaced the
  reflection with the full-strength scene colour, so each pixel flipped between hit and
  miss: salt-and-pepper speckle on every surface below 0.8 roughness.
- **SSR ray spread follows roughness²** (the GGX lobe width), not roughness.
- **Bloom** strengths default to 0.25 / 0.5 / 0.5 / 0.5 / 0.5 (were 0.5 / 1 / 1 / 1 / 1).
  With threshold 0 the whole frame blooms, and at 1 the widest mips smeared it and cost
  contrast.
- **Chromatic aberration** defaults to 0. Even 0.01 leaves a red line beside bright
  edges. The vignette no longer switches off with it.

Measured in `GodRayTest` at 1280 × 720 over six views (still camera, mean change
in luma per pixel per frame, out of 255): shimmer 0.83 → 0.17. Without TAA, the raw
per-frame grain went from 3.7 to 0.07.

## Tuning a scene for the day/night cycle

Turn the cycle on in the **Environment** panel (**Sky mode > Day / night cycle**). It
runs in both Edit and Play mode.

### What the cycle controls

- **One light for the sun and the moon.** The renderer uses its own directional light
  and ignores the scene's directional lights for shading. Gizmos still show the scene's
  lights.
- **The path is fixed.** You can't rotate it, so orient the scene to it: put windows,
  gaps and long views along X for sunrise and sunset light.

  | Hour | Sun | Moon |
  | --- | --- | --- |
  | 06:00 | Rises in +X | Sets in −X |
  | 12:00 | Highest, 81°, tilted slightly towards +Y | Below the horizon |
  | 18:00 | Sets in −X | Rises in +X |
  | 00:00 | Below the horizon | Highest, 81° |

- **Brightness and colour:**
  - The sun fades in over its first 15° above the horizon and is warm until about 30°.
  - Moonlight is blue-white at **8 % of the sun's peak** × **Moon brightness**.
  - The light is zero while it switches from sun to moon at 18:00 and 06:00. Just
    after sunset (about 19:00) is the darkest time.
- **Clouds** dim direct light by 1 − 0.5 × **Coverage**²: 0.45 (default) about −10 %,
  1.0 halves it.
- **These follow the cycle automatically:**
  - the sky;
  - the reflection cubemap, recaptured every 0.5 s;
  - the exposure offset (below);
  - baked probe ambient, scaled down to moonlight level at night;
  - volumetric fog, which scatters the cycle's sun or moon.

### The scene's directional light is a template

The brightest **enabled** directional light supplies:

| Value | Used for |
| --- | --- |
| `Intensity` | The sun's peak brightness. |
| `CastShadows`, `ShadowSize`, `ShadowDepth`, `ShadowResolution`, `ShadowFiltering` | The cycle light's shadow map. |
| `Position` | The centre of the shadow box. |

Its `Direction` and colour are ignored. A scene without one gets intensity 100 and a
450 m, 1024 px shadow box at (0, 0, 2).

### Recommended settings

Start from these and change one thing at a time. Values marked *default* are the
engine defaults; the rest come from `GodRayTest`.

| Setting | Where | Recommended | Why |
| --- | --- | --- | --- |
| Template `Intensity` | Inspector | 100 | A sunny day; everything else is tuned against it. Use **Sun brightness** for look changes instead. |
| `CastShadows` | Inspector | On | Without it there are no shadows and no light shafts in the fog. |
| `ShadowFiltering` | `.obsc` | 1 (soft PCF 3×3) | Has the slope bias that stops flicker when the sun or moon is overhead. 2 (5×5) has it too. 0 and 3 have not been checked overhead. |
| `ShadowSize` | `.obsc` | Smallest square that covers the area the player sees, plus anything that casts into it | Outside the box, surfaces and fog count as fully lit: shadows disappear and shafts turn into a uniform glow. |
| `ShadowResolution` | `.obsc` | 2048 | Keep `ShadowSize / ShadowResolution` well below your thinnest occluder. `GodRayTest`: 260 m / 2048 ≈ 13 cm for 6 m slats. |
| `ShadowDepth` | `.obsc` | At least the distance from `Position` to the farthest corner of the area | The box reaches ±`ShadowDepth` along the light. `GodRayTest`: 300. |
| `Position` | `.obsc` | Centre of the area | Centres the shadow box. |
| **Starting hour** | Environment | Whatever the scene should open on | Changing it, or the duration, restarts the cycle. |
| **Full cycle duration** | Environment | 2–4 min while tuning; the game's real length for shipping | 1440 (the maximum) holds an hour almost still: one game hour per real hour. |
| **Sun brightness** | Environment | 1 *(default)* | Multiplies the template's intensity and the sun disc. |
| **Moon brightness** | Environment | 1 *(default)*; raise it if nights are too dark to play | Night light, moon shafts and the night probe ambient scale with it. |
| **Coverage** | Environment | 0.2–0.45 | Above about 0.6 the sun gets noticeably weaker. |
| **Day (EV)** / **Night (EV)** | Environment | −1 / −2.5 *(default)* | Added to the Post Processing **Exposure** (0.75), blended over sun elevations −7° to +10°. Lower **Night (EV)** for darker nights. |
| **Eye Adaptation** | Post Processing | On *(default)* | Lifts nights noticeably: in `GodRayTest` at midnight the average pixel is about 48/255 once adapted, and 14/255 without it. To keep nights darker, lower **Max EV** rather than turning it off. |
| **Volumetric Fog** | Post Processing | Defaults | Shafts need distance: fog is zero within 30 m of the camera (**Start Dist.**), so keep the camera at least 60 m from the openings you want beams through. |
| `EnvironmentSample` | `.obsc` | Outdoors at eye height, `AutoUpdate: true` | The cycle recaptures it every 0.5 s, so it has to see the sky. |
| Probe bake | **Window > Lighting** | Bake with the template's `Direction` set to a typical midday sun | The bake uses the scene's lights, not the cycle. The cycle only dims baked ambient at night. |

Post Processing settings (exposure, eye adaptation, fog) are **not saved** with the
scene. Note the values you settle on.

### Checking a scene

1. Set the duration to 4 minutes and watch a whole cycle in Play mode with Freecam. Or
   set **Starting hour** to the times below one at a time.
2. Check these hours:

   | Hour | What to look for |
   | --- | --- |
   | 07:00 and 17:30 | Low warm light through openings along X; long shadows that must stay inside the shadow box. |
   | 12:00 and 00:00 | Overhead sun or moon: flat ground and roofs must not shimmer. |
   | 19:00 and 05:00 | The darkest time: is the scene still readable? |
   | 21:00–03:00 | Moonlight and moon shafts, about a third of daytime brightness in `GodRayTest`. |
3. If shadows or shafts cut off at a straight edge, the area is outside the shadow box.
   Raise `ShadowSize`, or move `Position`.
4. If nights look too bright or too flat, lower **Night (EV)** or eye adaptation's
   **Max EV**. If they look too dark, raise **Moon brightness**.

## Sample scene: `Scenes/AutoExposureTest.obsc`

A plaster house (8 × 6 m room, 3.5 m walls, south doorway, east window, ceiling with
an open attic above) on a grass field. The sun comes from the south-east, (-0.6, 0.6,
-0.55) at intensity 100. Inside there is a dim lamp (intensity 3, radius 7). Probes
are baked with the bounds above at 1024 rays per probe (`AutoExposureTest.probes`).
Freecam is on the main camera, so press Play and fly through the door to watch eye
adaptation, or look at the sun patches from the window and doorway.

## Sample scene: `Scenes/GodRayTest.obsc`

A test for sun and moon shafts over the day/night cycle, using the default fog settings.

- **The hall:** 120 × 120 m and 40 m tall, with a slatted roof (10 slats, 6 m wide,
  6 m gaps) and horizontal louvres on the east and west faces.
  - The cycle's sun and moon move from +X through overhead to −X. The roof cuts them
    from mid-morning to mid-afternoon; the louvres cut them near the horizon.
  - All slats run along Y, the camera's view axis, so each line of sight stays inside
    one lit or shadowed sheet.
- **The camera:** 110–230 m from the hall, well past the 30 m **Start Dist.**, with a
  dark backdrop wall behind the hall.
- **The `Sun` light:** only a template. It gives the cycle its peak intensity (100)
  and a 260 m shadow box around the hall. Fog outside the shadow box counts as lit,
  so keep new geometry inside it.
- **The cycle:** starts at 07:00 and lasts 4 minutes.
- Freecam is on the main camera.

Rendered offscreen with default Post Processing settings and eye adaptation on:

| Time | Light | What the side view shows |
| --- | --- | --- |
| 06:30 | Sun, 7° | Faint horizontal bands from the louvres. |
| 08:00–15:00 | Sun, 30–81° | Clear slanted or vertical shafts against the backdrop. |
| 17:30 | Sun, 7° | Faint bands. Looking into the sun shows glow and bright gaps rather than shafts. |
| 19:00 | Moon, 15° | Barely visible; the dimmest time. |
| 21:00–03:00 | Moon, 44–81° | Visible moon shafts, about a third as strong as daytime. |

Shafts read best from the side. Looking straight at the sun or moon, each beam lines
up with the line of sight, so you see bright gaps instead.

## Current issues

- **Indoor god rays are faint.** Fog density is zero within 30 m of the camera
  (**Start Dist.**), and even near the camera the default density is too low for
  clear beams. Fog settings are global, so fog dense enough for beams indoors also
  hazes the outdoors. Not decided yet: a **Fog Volume** gameobject (a local box with
  its own density, the preferred option) or per-scene fog in the Environment panel.
  To try beams now, set **Start Dist.** to 0 and raise **Density** and
  **Sun Scatter**; the outdoors will haze over.
- **Post Processing settings are not saved.** Exposure, eye adaptation, fog, SSAO
  and SSR are session-wide `GameSettings` values. They reset when Anvil restarts and
  are the same for every scene.
- **Fog sparkles at high density.** White speckles appear on walls when density is
  raised a lot. This comes from the froxel jitter, temporal history and bilateral
  upsampling.
- **Fog beams are soft.** At 160 × 90 cells (about 8 px per froxel) plus the
  temporal filter, beam edges blur. The blotches inside the beams are fixed (see
  **Volumetric fog**). A faint ripple of about 1/255 remains along the beams.
- **Scenes without a bake look wrong indoors.** The outdoor cubemap lights and
  reflects inside every room, giving a pale haze and almost no difference between
  inside and outside for eye adaptation.
- **One environment sample per scene,** and it is not editable in Anvil (see above).
- **The bake ignores the day/night cycle.** It uses the scene's directional lights, so
  a scene with the cycle on keeps a bake from a fixed sun.
- **Bakes are manual.** Moving geometry, lights or the sun leaves the bake stale until
  you rebake and save.
- **Probe lighting is low-frequency.** Small objects get the light of the 0.5 m grid
  around them. The visibility test uses mean distances only (no variance as in full
  DDGI), so a probe near a doorway can still see a little through the wall next to
  it. At 0.57 m spacing, the measured leak at wall edges is about the same as
  mid-wall light.
- **Old bakes have no visibility data.** `.probes` files from before format version 2
  load with unlimited free distances and leak as before. Rebake them.
- **Fog direction changed.** The Henyey-Greenstein angle used to be inverted, which
  put the glow opposite the sun. It is fixed now, so scenes tuned against the old
  look may need **Sun Scatter** or **Anisotropy** adjusted.
- **Not yet reviewed in Anvil.** The probe, fog and image-clarity fixes were checked
  in a hidden engine instance and by `Tests/Components`, not in the editor viewport.
- **Glossy surfaces are still slightly blotchy at grazing angles.** SSR marches only
  3 steps across the screen, so on the 0.4-roughness sphere in `GodRayTest` the
  reflected floor stripes break up near the rim.

## Where the code lives

| Area | Files |
| --- | --- |
| Pipeline order | `Engine/Renderer/Renderer.cs` (`Draw`, `DrawEnvironmentMap`, `UpdateAutoExposure`) |
| Cubemap + probe sampling | `Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx`, `Engine/Renderer/RenderModules/DeferredEnvironmentMapRenderModule.cs` |
| Probe bake | `Engine/Renderer/Lighting/` (`LightingSettings`, `LightingSystem`, `ProbeVolumeBaker`, `ProbeVolumeData`, `LightingBakeInput`) |
| Sky / day-night | `Engine/Logic/EnvironmentSettings.cs`, `Engine/Renderer/EnvironmentSky.cs` |
| Fog | `Engine/Content/Shaders/Deferred/Froxel.fx`, `Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs`, `Engine/Content/Shaders/Deferred/DeferredCompose.fx` |
| Exposure | `Engine/Content/Shaders/PostProcessing/AutoExposure.fx`, `Engine/Renderer/RenderModules/PostProcessingFilters/AutoExposureFilter.cs`, `Engine/Content/Shaders/PostProcessing/PostProcessing.fx` |
| Defaults | `Engine/Recources/GameSettings.cs` |
| Checks | `Tests/Components/SampleSceneChecks.cs`, `Tests/Components/AutoExposureChecks.cs` |
