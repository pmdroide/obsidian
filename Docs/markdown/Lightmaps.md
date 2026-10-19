# Lightmaps

Baked light for gameobjects that never move (Source 2 look, plan phase 4.2). This is what Half-Life: Alyx does with
`m_bHasBakedLightingFromLightMap`, `vr_directional_lightmaps` and the indexed baked lights. A Lightmap Static
gameobject gets three things from the lighting bake:

- **Indirect light per texel.** This is sky and bounce light, including emissive panels, stored as a directional
  lightmap so normal maps still shade it.
- **Baked ambient occlusion.**
- **A shadow mask.** It stores how much of each static light reaches the texel, and gives soft, accurate shadows
  even for lights without Cast Shadows.

The probe volume keeps lighting everything else: characters, props and anything that moves.

## Using it

1. Select the walls, floors and other fixed geometry, and tick **Inspector > Lightmap Static**.
   - **Scale** multiplies the scene's texel density for that gameobject. Use 0.5 for a huge floor, or 2 for a detailed
     prop.
2. Open the **Lighting** workspace (Window > Lighting) and press **Bake Lighting**.
   - The bake does the probes first, then the lightmaps. The status line reports both.
   - Lightmap Static gameobjects that can't take a lightmap are listed in the console. These are animated ones,
     cloth, Foliage Wind, particle systems, water, and Dynamic physics bodies.
3. Save the scene. The bake goes into `<scene>.lightmaps` next to the `.obsc`, beside `<scene>.probes`. Builds ship
   both files.

Lights have a **Baked Shadows** setting, on by default. On Lightmap Static surfaces such a light's shadows come from
the mask. Turn it off for a light a script moves, otherwise its shadows stay where it was baked. A light that moved
or changed range since the bake falls back to its shadow map on its own.

### Lighting > Lightmaps settings

| Setting | Default | What it does |
|---|---|---|
| Use lightmaps | on | Runtime switch. Off = the probes light everything (no rebake needed). |
| Bake lightmaps | on | Bake Lighting also bakes lightmaps. Off = only the probes are rebaked, old lightmaps are kept. |
| Denoise | on | Edge-aware 5 × 5 blur per gameobject after the gather (3 × 3 for the mask). |
| Texels / m | 8 | Lightmap resolution (each gameobject's Scale multiplies it). |
| Max Size | 2048 | Largest page, texels a side. Bigger gameobjects get a lower density; more go on new pages. |
| Rays / Texel | 128 | Hemisphere rays per texel (indirect light and AO). |
| Shadow Rays | 16 | Rays per texel and static light for the soft mask. |
| AO Distance | 1 m | Hits closer than this darken the baked AO. |
| Sun Angle | 1.5° | Angular diameter of the sun: how soft its baked shadow is. |

The probe settings (sky colour and intensity, validity threshold, Intensity) apply to the lightmaps too.

## How it works

### UVs (`LightmapUnwrap`)

A model whose vertices carry a second texture coordinate (TEXCOORD1) keeps that layout. Otherwise the bake unwraps
the model on the CPU, once per model:

- Triangles are grouped by the axis their normal points along most (±X/±Y/±Z).
- Each connected group is projected flat onto that axis' plane.
- A group that overlaps itself in the projection (a ramp winding over itself) is split until it doesn't.
- Vertices are split where charts meet.

Every Lightmap Static gameobject then packs its model's charts at its own size into a block, with a one-texel gutter
around each chart. The blocks are packed into pages. Nothing is written into the model's `.xnb`. The split geometry
and each gameobject's UVs are stored in the `.lightmaps` file.

### Bake (`LightmapBaker`)

The lightmap bake runs on the bake worker after the probes, with the same scene snapshot and BVH. For each texel:

- Its world position and normal are rasterised from the UVs. Texels within ¾ texel of a triangle count too, so
  bilinear filtering at chart edges never reads empty texels.
- It gathers **cosine-weighted rays** over the hemisphere:
  - Misses see the sky above the horizon and dim ground below it.
  - Hits see albedo × (direct light + the probes' irradiance) at the hit point. That is one exact bounce, with the
    rest taken from the probes.
  - Emissive triangles are sampled as area lights.
  - Rays that hit back faces mean the texel is inside geometry. Such texels are filled from their neighbours.
- **AO** is the share of rays hitting something within AO Distance.
- **Direction** is the luminance-weighted mean direction of the incoming light, and its length (0 to 1) says how
  directional the light is. The stored irradiance is divided by the directional term at the baked normal. The
  lightmap pass multiplies it back with the G-buffer normal:
  `E(N) = E_stored × ((1 − k) + k × (N·d × 0.5 + 0.5))`.
- **Shadow mask**: for each static light reaching the texel, the visible share of its source:
  - for the sun, a disc of Sun Angle;
  - for point and spot lights, a sphere of Source Radius, at least 3 cm.

  The mask stores visibility only. Falloff, cone, cookie and N·L stay real-time.

Direct light from the scene's lights is **not** baked. It stays real-time (real-time specular, like HLA), and on
lightmapped surfaces it is multiplied by its mask channel.

**Mask channels.** The mask has 4 channels (RGBA).

- The sun takes the first channel.
- Point and spot lights are graph-coloured, strongest first. Two lights whose spheres overlap never share a channel,
  so lights in different rooms reuse channels.
- Lights left over keep only their shadow maps. The bake status says how many.

Lightmap units match the probes: a white surface's diffuse light in the light buffer.

### Runtime (`LightmapRenderModule`, `Lightmap.fx`)

1. After the G-buffer and decals, the **lightmap pass** redraws every Lightmap Static gameobject that has a lightmap.
   - It uses the gameobject's current transform, its lightmap geometry, and its UVs as a second vertex stream.
   - Where the fragment is what the G-buffer shows, it writes two screen targets. The depth test happens in the
     shader against the linear depth, as for decals.
     - `_renderTargetLightmap` (`HalfVector4`): rgb = irradiance for the G-buffer normal, a = 1 + baked AO
       (0 = not lightmapped).
     - `_renderTargetShadowMask` (`Color`): the 4 mask channels (1 = lit off lightmapped surfaces).
2. **Lights** (`DeferredPointLight.fx` `ShadeLight`, `DeferredDirectionalLight.fx`): each light with a channel
   multiplies its light by that channel of the mask (`ShadowMaskChannel`, one-hot; zero = no baked shadows).
   - Shadow maps still apply on top, so dynamic objects keep casting real-time shadows onto lightmapped floors.
   - Beyond the sun's cascades the mask is the only sun shadow.
3. **Environment pass** (`DeferredEnvironmentMap.fx`): on lightmapped pixels:
   - the lightmap replaces the probe volume's irradiance;
   - SSAO and SDF AO are skipped, because the lightmap already holds its own occlusion; material AO and AO proxies
     still apply;
   - the baked AO darkens only the reflections (specular occlusion);
   - reflection normalisation uses the lightmap irradiance.
4. Reflection captures don't use the mask, because it belongs to the main view's screen.

A gameobject is drawn with its lightmap only while its model matches the bake: same mesh parts and vertex counts.
It must also be drawn at LOD level 0 and not be hidden. Moving it after the bake keeps its lightmap but the light
gets stale, so rebake.

## Debug views

Post Processing > Viewport > Debug View (or `--rendermode`):

- **Lightmap**: the lightmap pass's irradiance. Dynamic objects are black.
- **ShadowMask**: the mask as colour. Red = the sun, green/blue/alpha = other static lights; white = lit.
- **IndirectDiffuse** and **AmbientOcclusion** show what the environment pass ended up using.

## Sample

`Content/Scenes/LookDev.obsc`: the floor (Lightmap Scale 0.5), the colour checker, the dark room (walls, roof,
lintel, crate, emissive panel) and the glass wall are Lightmap Static. The spheres, Y Bot and the Dragon use the
probes.

```powershell
Engine\bin\Debug\net10.0-windows\Engine.exe --scene Scenes/LookDev.obsc --bake --save --shot room.png --camera 10.5,-1.0,1.7,17,0.5,1.0
Engine\bin\Debug\net10.0-windows\Engine.exe --scene Scenes/LookDev.obsc --shot mask.png --rendermode ShadowMask --camera 4,-1,2.0,12,0,1.2
```

The LookDev bake: 37 gameobjects, ~168k texels on one page, about 6 s for the lightmaps (2 s for the probes). The
`.lightmaps` file is about 1 MB.

## Code map

| Area | Files |
|---|---|
| Settings | `Renderer/Lighting/LightingSettings.cs` (`Lightmap*`, `SunAngle`), `BasicEntity.LightmapStatic/LightmapScale`, `PointLight/DirectionalLight.BakedShadows` |
| Gather | `LightingBakeInput.Gather(scene, lightmaps)` → `LightmapInstances`, `LightmapSkipReason`, TEXCOORD1 |
| Unwrap | `Renderer/Lighting/LightmapUnwrap.cs` |
| Bake | `Renderer/Lighting/LightmapBaker.cs` (layout, rasterise, gather, mask channels, filtering); `LightingSystem.Bake` runs probes then lightmaps |
| Data | `Renderer/Lighting/LightmapData.cs` (`.lightmaps`, deflate-compressed, `ChannelOf`), `Scene.BakedLightmaps`, `SceneSerialization` (`LightingRecord.LightmapDataFile`) |
| GPU | `Renderer/Lighting/LightmapResources.cs` (`LightingSystem.Lightmaps`), `RenderModules/LightmapRenderModule.cs`, `Content/Shaders/Deferred/Lightmap.fx` |
| Shaders reading it | `DeferredEnvironmentMap.fx` (`LightmapMap`), `DeferredPointLight.fx` / `DeferredDirectionalLight.fx` (`ShadowMaskMap`, `ShadowMaskChannel`) |
| Editor | Inspector Lightmap Static / Scale (`IEditorBridge.EnqueueSetLightmap`), light Baked Shadows, Lighting > Lightmaps (`LightingViewModel`) |
| Checks | `Tests/Components/LightmapChecks.cs` |

## Not done

- **UV2 in the content pipeline.** The plan said to generate UV2 on import, for example with xatlas. Here it is built
  at bake time and stored in the sidecar instead, so models and `Content.mgcb` are untouched. Authored TEXCOORD1 is
  used when a model has it.
- **HLA's per-texel light indices.** HLA stores 4 light indices per texel at 1/4 resolution (R6). This uses graph
  colouring with 4 fixed channels, which needs no index texture and blends without seams. More than 3 overlapping
  static lights besides the sun don't all get baked shadows.
- **Shadow mask for dynamic objects.** HLA also packs the light indices into its probe volumes. Here dynamic objects
  use the shadow maps only.
- **Forward (glass) surfaces** aren't lightmapped. They use the probes.
- **Seam stitching between charts.** Charts are only padded and dilated. There is also no automatic bake hash: moved
  gameobjects keep their old lightmap until the next bake.
