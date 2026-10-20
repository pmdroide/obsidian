# Scene lighting and fog

How a scene is lit, what each Anvil panel controls, how to light an interior, and what
doesn't work well yet. Coordinates are Z-up throughout.

## What lights a pixel

Every frame, `Renderer.Draw` combines these sources:

1. **Direct lights.** Directional lights (the sun), point lights and spot lights from the scene,
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

Add lights from **GameObject > Light > Directional Light / Point Light / Spot Light**, or from the
Hierarchy **+** menu. New directional lights use intensity 100, which is a sunny day.

- **Directional light:** direction, colour, intensity and **Cast Shadows** in Anvil.
  The shadow settings (`ShadowSize` and `ShadowDepth` in metres, `ShadowResolution`,
  `ShadowFiltering`) are only in the scene file. The shadow map is an orthographic
  box around the light's position, so keep the scene inside it.
- **Point light:** position, radius, colour and intensity. For scale, intensity 3
  with radius 7 is barely visible next to a sun of 100.
- **Spot light:** a point light limited to a cone. It has the point light's settings plus
  **Spot Angle** (full cone angle, 1-179°) and **Inner** (the fully lit core). The edge
  fades from Inner to Spot Angle. With no rotation it points straight down (-Z); turn it
  with the Rotation fields or the rotate gizmo. A selected spot light draws its cone in
  the viewport. New spot lights use radius 25, intensity 40 and a 60° cone.
  - Spot lights are `SpotLight : PointLight` (`Engine/Entities/SpotLight.cs`) and live in
    the scene's `PointLights` list, so they use the same light volume, the same cube
    shadow map and the same forward, fog and bake paths. Each of those shaders
    multiplies the distance falloff by the cone (`SpotConeFactor`; point lights pass an
    outer cosine of -2, which lights every direction).
  - Shadows render only the cube faces the cone reaches, so a narrow spot costs one
    face instead of six.
  - Scene files store them in a separate `SpotLights` list with a rotation quaternion,
    `SpotAngle` and `InnerSpotAngle`. Older scenes load unchanged.
  - Volumetric spot lights (`IsVolumetric`, scene file only) are approximate when
    unshadowed: the analytic glow is weighted by the cone at the point of each view ray
    closest to the light.
- **Day/night cycle** (Environment panel, below): while it is on, the renderer swaps
  in its own sun and moon and ignores the scene's directional lights for shading.

## Environment panel

Open it with the **Environment** button in the Hierarchy. Settings are saved in the
scene (`Environment` in the `.obsc`).

- **Sky mode:** the default sky, a 360° panorama (**Choose skybox…**), or the
  day/night cycle (starting hour, cycle length).
- **Skybox > Sun glare / Sun size:** a panorama is an ordinary (LDR) image, so its sun
  is no brighter than a white cloud. The scene's brightest directional light marks where
  the sun is: aim it from the panorama's sun and the sky there is pushed into HDR (the
  image's own bright pixels plus a sun disc and glare), so looking at it blooms like the
  real sun. Glare 0 shows the image as it is. Only the visible sky gets it, not
  reflections (the light already draws the sun's highlight); thick fog in front of the
  sky dims it, like the sun through fog.
- **Sky / Sun, moon and stars / Clouds:** look of the procedural sky.
- **Exposure > Day (EV) / Night (EV):** offsets added to the Post Processing exposure,
  blended by daylight. They only apply while the day/night cycle is on.
- **Weather:** rain, sandstorm or snow, with intensity, haze and wind. See
  [Weather.md](Weather.md).

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

A froxel grid of 160 × 90 × 128 over the view frustum, with logarithmic slices of
view-space depth from 1 m to the far plane (`FroxelRenderModule`, `Froxel.fx`). The fog is **saved with
the scene**: **Inspector > Environment > Volumetric Fog** (`EnvironmentSettings.Fog*`).
The renderer copies it into `GameSettings.g_Froxel*` every frame
(`Renderer.ApplySceneFog`), so scripts change it through `SceneEnvironment`. Only
**Enable Fog** and **History** stay in **Window > Post Processing > Volumetric Fog**.

| Setting | Default | Meaning |
| --- | --- | --- |
| Density | 0.03 | Fog density at full distance. |
| Absorption | 0.1 | How much the fog darkens what is behind it. |
| Sun Scatter | 0.1 | In-scattering strength for the sun. |
| Light Scatter | 1.5 | In-scattering strength for point and spot lights. Higher than the sun's because of 1/r² falloff. |
| Indirect | 0.1 | In-scattering strength for baked bounce light (the probe volume, **Window > Lighting**). Does nothing without a bake. |
| Anisotropy | 0.45 | Henyey-Greenstein *g*. Above 0, light scatters forwards: looking towards the sun glows. |
| Start Dist. / Full Dist. | 30 m / 400 m | Density ramps linearly from Near Density at Start to full at Full. |
| Near Density | 0 | Fraction of the density kept before Start. 0 = clear near the camera, 1 = no ramp. |
| Sky Fog | 0.1 | How much of the fog beyond Sky Shafts covers sky pixels. |
| Sky Shafts | 200 m | Fog in front of the sky up to this distance stays whole, so shafts still show when you look towards the sun through an opening. |
| Max Auto Exp. | 3 EV | Caps how far eye adaptation brightens dark views (it can only lower Post Processing's **Max EV**). Lower it to keep a dark room dark around a bright opening. |
| History *(Post Processing)* | 0.7 | Weight of last frame's reprojected fog. Higher is smoother, lower reacts faster. 0 turns it off. |

The defaults are an outdoor haze: thin near the camera, thicker with distance. Fog dense
enough for shafts indoors would haze the whole outdoors, so put it in a **Fog Volume**
instead.

### Fog Volume component

**Add Component > Fog Volume** (`FogVolumeComponent`, type id `fog-volume`) adds fog inside
a box: the gameobject's position, rotation and scale, like a Cube with that scale. Its
fog skips the Start/Full ramp, so a dusty room shows shafts right in front of the camera.

| Setting | Default | Meaning |
| --- | --- | --- |
| Density | 0.15 | Added to the scene density inside the box (same units, so 5× the default haze). |
| Edge Fade | 0.1 | Fraction of the box, from each face inwards, over which the density fades in. |

- While the component is enabled, the gameobject's mesh isn't drawn and can't be picked
  in the viewport; select it in the hierarchy.
- At most 8 volumes draw (`FogVolumeComponent.MaxVolumes`, `MAX_FOG_VOLUMES` in `Froxel.fx`).
- Shafts need the light's shadows: keep the box within the sun's **Shadow Dist.** of the camera (its cascades).
- `FogVolumeComponent.DensityAt(owner, point)` matches the shader, for scripts and checks.

The sun's scattering uses its shadow map, so shafts form wherever the sun reaches,
for example through a window. Point lights use their cube shadow maps when they cast
shadows. Spot lights only scatter inside their cone.

Each froxel tests the sun's shadow map at 6 points spread through its depth
(`ComputeShadowAlongSlice`). Distant slices are about 10 m deep, so a single test aliased
against the 12 m roof-slat spacing in `GodRayTest` and turned the shafts into blotches.

The fog takes at most 8 point and spot lights (`MAX_FROXEL_POINT_LIGHTS`). Each frame
`FroxelRenderModule.RankPointLights` keeps the enabled ones whose range reaches into
the view, brightest at the camera first (brightness × (radius / distance)², 1 inside the
radius). Particle System lights compete with the scene's lights.

**Bounce light.** With a baked probe volume, the fog also scatters the probes' mean
radiance (the SH L0 band; fog has no normal), scaled by **Indirect** and by the bake's
Intensity (dimmed at night like the surfaces). Probes are blended with the same
visibility test as surfaces, and a point no probe can see gets none, so a closed room
doesn't glow with the light outside it. Outside the probe volume there is no bounce
light in the fog.

### How the fog is computed

Each frame (`FroxelRenderModule.RenderFroxelFog`):

1. **Injection** (`BuildFroxels`): per froxel, at a jittered point inside it, the
   scattering source per metre (sun, point lights, bounce light) and the extinction per
   metre (`density × Absorption`).
2. **Temporal resolve** (`ResolveFroxels`): the froxel's centre is projected into last
   frame's grid, last frame's medium is read there (blending the two nearest slices),
   clamped to this frame's neighbours (so moving lights and fog volumes don't smear) and
   blended by **History**. Outside last frame's view it isn't used.
3. **Transport** (`SeedTransport` + 7 × `ScanTransport`): each froxel's slice becomes a
   segment along its view ray, integrated exactly for a constant medium (light
   scattered towards the camera, optical depth); a prefix scan then sums the segments
   from the camera outwards, 7 passes for 128 slices instead of one loop over every
   nearer slice per froxel.
4. **Compose** (`DeferredCompose.fx`, `SampleFroxelFog`): a pixel at view depth *z*
   takes the prefix up to the start of its slice and integrates only the part of its
   slice in front of it, so a wall inside a 10 m deep slice isn't fogged as if it were
   at the slice's far end.

The history is dropped (the next frame uses only its own samples) on a scene swap,
while the froxels are off, on a camera jump over 10 m (`CameraCutDistance`), a new
projection (FOV, aspect, far plane), any fog setting change, a new or removed probe
bake, and a debug hot reload of `Froxel.fx`.

The atlases are half floats: the injection (reused by the scan), two history atlases
and a scan atlas, about 56 MiB together. The prefixes keep optical depth rather than
transmittance, because a thin slice's transmittance (0.9992…) rounds away in a half
float. `Tests/Components` (`--graphics`, `FogChecks.RunGraphics`) renders a uniform
sunlit medium and compares the result with the exact solution at the centre and
corners of the screen.

Transparent materials drawn after composition (forward pass, water, particles) are not
fogged.

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
| `CastShadows`, `ShadowResolution`, `ShadowFiltering`, `CascadeCount`, `ShadowDistance`, `SplitLambda`, `ShadowDepth` | The cycle light's cascaded shadows. |

Its `Direction`, colour and `Position` are ignored. A scene without one gets intensity
100 and four 1024 px cascades over 150 m.

The sun's shadows are cascades that follow the camera (see
[Source2_Look.md](Source2_Look.md#sun-shadows-cascades)): up to 4 slices of the view out
to **Shadow Dist.**, split by **Split Lambda**. `ShadowSize` and `Position` are no longer
used; `ShadowDepth` is how far towards the sun each cascade still catches casters.

### Recommended settings

Start from these and change one thing at a time. Values marked *default* are the
engine defaults; the rest come from `GodRayTest`.

| Setting | Where | Recommended | Why |
| --- | --- | --- | --- |
| Template `Intensity` | Inspector | 100 | A sunny day; everything else is tuned against it. Use **Sun brightness** for look changes instead. |
| `CastShadows` | Inspector | On | Without it there are no shadows and no light shafts in the fog. |
| `ShadowFiltering` | `.obsc` | 1 (soft PCF 3×3) | Has the slope bias that stops flicker when the sun or moon is overhead. 2 (5×5) has it too. 0 and 3 have not been checked overhead. |
| **Shadow Dist.** | Inspector | Farther than the farthest shadow or shaft you need to see | Beyond it, surfaces and fog count as fully lit: shadows disappear and shafts turn into a uniform glow. Default 150 m; `GodRayTest`: 420 m. |
| **Cascades**, **Split Lambda** | Inspector | 4, 0.75 *(default)*; raise lambda towards 0.85 for long distances | More of the resolution goes to the near cascades as lambda rises. |
| **Shadow Res.** | Inspector | 2048 | Per cascade (capped at 2048 with several cascades). The far cascade's texels must stay below your thinnest distant occluder. |
| `ShadowDepth` | `.obsc` | The height of the tallest caster above the area | Casters up to this far towards the sun from each cascade are still caught. `GodRayTest`: 300. |
| **Starting hour** | Environment | Whatever the scene should open on | Changing it, or the duration, restarts the cycle. |
| **Full cycle duration** | Environment | 2–4 min while tuning; the game's real length for shipping | 1440 (the maximum) holds an hour almost still: one game hour per real hour. |
| **Sun brightness** | Environment | 1 *(default)* | Multiplies the template's intensity and the sun disc. |
| **Moon brightness** | Environment | 1 *(default)*; raise it if nights are too dark to play | Night light, moon shafts and the night probe ambient scale with it. |
| **Coverage** | Environment | 0.2–0.45 | Above about 0.6 the sun gets noticeably weaker. |
| **Day (EV)** / **Night (EV)** | Environment | −1 / −2.5 *(default)* | Added to the Post Processing **Exposure** (0.75), blended over sun elevations −7° to +10°. Lower **Night (EV)** for darker nights. |
| **Eye Adaptation** | Post Processing | On *(default)* | Lifts nights noticeably: with the cycle at midnight the average pixel was about 48/255 once adapted, and 14/255 without it. To keep nights darker, lower **Max EV** (or the scene's **Max Auto Exp.**) rather than turning it off. |
| **Volumetric Fog** | Environment | Defaults | Shafts need distance: fog is zero within 30 m of the camera (**Start Dist.**), so keep the camera at least 60 m from the openings you want beams through, or add a **Fog Volume** where you want beams up close. |
| `EnvironmentSample` | `.obsc` | Outdoors at eye height, `AutoUpdate: true` | The cycle recaptures it every 0.5 s, so it has to see the sky. |
| Probe bake | **Window > Lighting** | Bake with the template's `Direction` set to a typical midday sun | The bake uses the scene's lights, not the cycle. The cycle only dims baked ambient at night. |

The Post Processing panel's *scene* sections (tonemapping, grading, eye adaptation, bloom)
are saved with the scene (`EnvironmentSettings.Post`), like the fog and **Max Auto Exp.**.

### Checking a scene

1. Set the duration to 4 minutes and watch a whole cycle in Play mode with Freecam. Or
   set **Starting hour** to the times below one at a time.
2. Check these hours:

   | Hour | What to look for |
   | --- | --- |
   | 07:00 and 17:30 | Low warm light through openings along X; long shadows that must stay within Shadow Dist. |
   | 12:00 and 00:00 | Overhead sun or moon: flat ground and roofs must not shimmer. |
   | 19:00 and 05:00 | The darkest time: is the scene still readable? |
   | 21:00–03:00 | Moonlight and moon shafts, about a third of daytime brightness in `GodRayTest`. |
3. If shadows or shafts end at a distance, they are beyond the sun's **Shadow Dist.**
   Raise it (and lambda), or use the **ShadowCascades** debug view to see the cascades.
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

A test for sun shafts outdoors (the hall, default scene fog) and indoors (the attic, a
Fog Volume), with a fixed sun: the day/night cycle is **off**. Turn it on in the
Environment panel to check shafts over the day (starts at 07:00, 4 minutes).

- **The hall:** 120 × 120 m and 40 m tall, with a slatted roof (10 slats, 6 m wide,
  6 m gaps) and horizontal louvres on the east and west faces.
  - The cycle's sun and moon move from +X through overhead to −X. The roof cuts them
    from mid-morning to mid-afternoon; the louvres cut them near the horizon.
  - All slats run along Y, the camera's view axis, so each line of sight stays inside
    one lit or shadowed sheet.
- **The camera:** 110–230 m from the hall, well past the 30 m **Start Dist.**, with a
  dark backdrop wall behind the hall.
- **The attic:** at (-105, 0), 18 × 26 m inside and 11 m tall, closed except for an
  east window (with a cross bar) and a 5 × 8 m roof skylight split by three slats and
  a beam. Rafters, a couch and a crate give the shafts something to cross. The
  **Attic Dust** gameobject is a Fog Volume (density 0.15) filling the room. Stand at
  about (-111, -10, 2) and look up towards the skylight.
- **The `Sun` light:** direction (-0.6, 0.1, -0.8), intensity 100, four cascades out to
  420 m (Split Lambda 0.85), so the hall stays shadowed from the far camera spots. Fog
  beyond Shadow Dist. counts as lit. With the cycle on it is only a template (peak
  intensity and the shadow settings).
- **Exposure:** **Max Auto Exp.** is 0 EV, so eye adaptation doesn't lift the dark
  attic to grey (it went near-white at the default 3 EV).
- Freecam is on the main camera.

With the cycle on (07:00 start, 4 minutes), rendered offscreen with default Post
Processing settings and eye adaptation on:

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

- **SSAO, SSR and anti-aliasing are project-wide.** They are `GameSettings` values and
  reset when Anvil restarts; the tonemapping/exposure/bloom/grading values are per
  scene.
- **Fog Volumes have no viewport outline.** Their mesh is hidden, so the box can only
  be seen through its fog; select it in the hierarchy.
- **Shafts and shadows end at the sun's Shadow Dist.** from the camera (the last
  cascade fades out there).
- **Fog sparkles at high density.** White speckles appear on walls when density is
  raised a lot. This comes from the froxel jitter, temporal history and bilateral
  upsampling.
- **Fog beams are soft.** At 160 × 90 cells (about 8 px per froxel) plus the
  temporal filter, beam edges blur. The blotches inside the beams are fixed (see
  **Volumetric fog**). A faint ripple of about 1/255 remains along the beams.
- **Transparent surfaces aren't fogged.** The forward pass, water and particles draw
  after the fog is composed and don't sample it, so glass or water far away stays
  clear in thick fog.
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
| Weather | `Engine/Logic/Weather.cs`, `Engine/Renderer/RenderModules/WeatherRenderModule.cs`, `Engine/Content/Shaders/Forward/Weather.fx` ([Weather.md](Weather.md)) |
| Point / spot lights | `Engine/Entities/PointLight.cs`, `Engine/Entities/SpotLight.cs`, `Engine/Content/Shaders/Deferred/DeferredPointLight.fx`, `Engine/Renderer/RenderModules/DeferredLighting/PointLightRenderModule.cs`, `Engine/Renderer/RenderModules/ShadowMapRenderModule.cs` |
| Fog | `Engine/Content/Shaders/Deferred/Froxel.fx`, `Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs`, `Engine/Content/Shaders/Deferred/DeferredCompose.fx`, `Engine/Components/FogVolumeComponent.cs`, `Engine/Logic/EnvironmentSettings.cs` (`Fog*`) |
| Exposure | `Engine/Content/Shaders/PostProcessing/AutoExposure.fx`, `Engine/Renderer/RenderModules/PostProcessingFilters/AutoExposureFilter.cs`, `Engine/Content/Shaders/PostProcessing/PostProcessing.fx` |
| Defaults | `Engine/Recources/GameSettings.cs` |
| Checks | `Tests/Components/SampleSceneChecks.cs`, `Tests/Components/AutoExposureChecks.cs`, `Tests/Components/SpotLightChecks.cs` |
