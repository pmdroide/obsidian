# Weather

A scene can have **rain**, a **sandstorm** or **snow**. Weather is part of the scene's environment.
Set it in Anvil under **Hierarchy > Environment > Weather**. It is saved in the `.obsc`, drawn in
both Edit and Play, and scripts can change it during Play.

## Settings

| Setting | Range | Default | What it does |
| --- | --- | --- | --- |
| **Weather** | None, Rain, Sandstorm, Snow | None | The weather type. The sliders below are hidden while it is None. |
| **Intensity** | 0-1 | 0.7 | How many particles are drawn and how thick the haze is. 0 draws nothing. |
| **Haze** | 0-1 | 0.6 | Scales the haze. 0 leaves only the particles. |
| **Wind (m/s)** | 0-40 | 4 | Horizontal wind that blows the particles. Rain takes all of it, snow 60%. A sandstorm always blows at least 7 m/s. |
| **Wind towards** | 0-359° | 45° | Compass angle the wind blows **towards**: 0° = +X, 90° = +Y. |
| **Ground Fog** | 0-1 | 0.6 | Scales the low mist the weather raises over the ground. 0 = none. |
| **Fog Height (m)** | 0.2-20 (slider to 10) | 1.2 | The mist thins to about a third this far above its base. |
| **Fog Base Z** | any | 0 | World height (Z) of the ground the mist lies on. |
| **Rain Drops** | 0-1 | 1 | Scales the raindrops that bead on and run down surfaces. 0 = none. |

In the scene file:

```json
"Environment": {
  "DayNightCycle": true, "TimeOfDay": 15,
  "Weather": "Rain", "WeatherIntensity": 0.7, "WeatherHaze": 0.6, "WindSpeed": 5, "WindDirection": 60,
  "GroundFog": 0.6, "GroundFogHeight": 1.2, "GroundFogBase": 0, "SurfaceDrops": 1
}
```

The type is saved by name. Scenes saved before weather existed load with `None` and the defaults.
Out-of-range values are clamped on load.

## The three weathers

| | Rain | Sandstorm | Snow |
| --- | --- | --- | --- |
| Particles (at intensity 1) | 24 000 thin streaks | 20 000 short grain streaks | 16 000 round flakes |
| Fall speed | 9 m/s | 0.6 m/s, gusty sway | 1.1 m/s, fluttering |
| Haze | light grey, distant | thick orange, close | pale blue-white |
| Day/night sun | dims by 60% | dims by 65% and reddens | dims by 50% |
| Day/night clouds | towards 95% cover | towards 50% cover | towards 90% cover |
| Ground mist | grey-blue, 0.12/m at the ground | orange dust, 0.12/m | pale, thin (0.05/m) |
| Raindrops on surfaces | yes | no | no |

All values are scaled by **Intensity**. Each type's fixed look (counts, sizes, colours, haze distance,
sun and cloud effects) is a `WeatherProfile` in `Engine/Logic/Weather.cs`. Tune a weather there.

The sun and cloud effects only apply with the **day/night cycle**, because the cycle owns its sun.
With a custom skybox, the scene's own directional lights stay as they are, and only the particles and
haze are drawn.

## Ground mist

In the weather, a low mist lies on the ground (dust in a sandstorm). It is densest at **Fog Base Z** and
thins out with height, so the street and the feet of the houses fade while the roofs stay clear.
Wind-blown banks and curls of noise thin it out in patches and drift with the wind.

The density at the ground is the type's `WeatherProfile.GroundFog` × **Intensity** × **Ground Fog**.
It does not switch on and off with the weather: it rises over about 12 s and clears over about 30 s
after the weather stops (`SurfaceWetness.GroundFog`, eased like the ground's wetness). A newly loaded
scene starts with its weather's mist.

Set **Fog Base Z** to the ground's height when the scene's ground is not at Z = 0. Raise **Fog Height**
for a deeper fog bank and lower it for a thin layer around the ankles.

## Raindrops on surfaces

In the rain, every material with **Rain Drops** ticked (Material component, on by default) gets:

- a **wet sheen**: darker, glossier surfaces;
- **beads**: two sizes of drops that land, sit and evaporate. On walls they sag downwards and there
  are fewer of them;
- **drips** on walls: drops running down in columns, leaving a glossy trail behind them.

Undersides stay dry. Drops are placed in world space (tops use X/Y, walls their horizontal axis and Z),
so they never stretch with the UVs and always run down. They are lenses of water: their own smooth
normal, glossy and slightly darker than what they sit on. Drops smaller than about 2 pixels fade into
the sheen, so they show within a few metres of the camera and never shimmer far away.

The strength is `WeatherProfile.SurfaceDrops` × **Intensity** × **Rain Drops**. It builds up over about
4 s and dries over about 25 s after the rain (`SurfaceWetness.Drops`). **WetSurface** materials don't get
drops: they pool puddles instead (see [Material_Component.md](Material_Component.md#wet-surfaces)).
Emissive and hologram materials stay as they are.

## How it is drawn

`WeatherRenderModule` (`Engine/Renderer/RenderModules/WeatherRenderModule.cs`) with
`Engine/Content/Shaders/Forward/Weather.fx` runs after the water pass and before TAA, on the HDR image:

1. **Haze:** a full-screen pass. The haze thickens with the G-buffer depth, so the sky gets the most.
   It is lit by the captured sky and the sun, and glows when you look towards the sun.
2. **Ground mist** (`GroundFog` technique): a full-screen pass. Each pixel's view ray, up to its
   G-buffer depth (300 m for the sky), is clipped to the layer below 4.5 × Fog Height and marched in
   12 steps packed towards the camera, jittered per pixel and frame so TAA smooths it. The noise
   detail fades out from 40 to 140 m. The wind drift is accumulated on the CPU per layer and wraps
   every 2000 m, where the noise repeats exactly, so it never jumps. It is lit like the haze.
   It is drawn while `SurfaceWetness.GroundFog` lingers too, after the weather is set to None.
3. **Particles:** one static buffer of quads, built once. The vertex shader places and animates them:
   - The particles sit on a world-space lattice that repeats every box size (rain 36 × 36 × 22 m) and
     wraps around the camera. Moving the camera does not drag the rain along.
   - Fall and wind are accumulated on the CPU per speed group (4 groups of different speeds), so
     changing the wind never makes particles jump.
   - Rain and sand are streaks along their velocity. Snow is camera-facing flakes.
   - Particles thinner than a pixel are widened to one pixel and faded by the same amount, so distant
     rain stays smooth instead of flickering.
   - Particles fade at the box faces, close to the eye, and where they meet geometry (soft particles
     against the G-buffer depth).
   - Each particle is lit by the sky ambient from the environment cubemap plus the first enabled
     directional light. It is brighter when backlit.

Raindrops on surfaces are drawn earlier, in the G-buffer: `ApplyRainDrops` in
`Engine/Content/Shaders/GBufferSetup/Gbuffer.fx`, with `RainDrops` set per material by
`GBufferRenderModule` from `SurfaceWetness.Drops` (0 for Wet Surface, Emissive and hologram materials
and for materials with Rain Drops off).

Cost: two full-screen passes (haze, mist), one draw of up to 24 000 quads, and a few hash lookups per
G-buffer pixel while it rains.

## Changing the weather from a script

`ScriptBehaviour.SceneEnvironment` is the active scene's `EnvironmentSettings`:

```csharp
SceneEnvironment.Weather = WeatherType.Snow;
SceneEnvironment.WeatherIntensity = 0.5f;
SceneEnvironment.WindSpeed = 8;
SceneEnvironment.WindDirection = 270; // towards -Y
```

Changes show on the next frame. Keep values in range, because scripts skip the clamping the Inspector
does. **Stop puts back the environment the scene had when Play started**, as it does for transforms.
That includes weather and time-of-day changes made in the Environment panel during Play.

## Sample scene: `Scenes/WeatherTest.obsc`

The last entry in the scene list. A village street:

- six houses, a tower 88 m away and dunes further back;
- **wet ground** that soaks and fills with puddles in the rain and dries after it: rocky dirt
  around the village, a brick street and a cobblestone plaza (WetSurface materials, see
  [Material_Component.md](Material_Component.md#wet-surfaces));
- a pond with the water material, and trees;
- three reference spheres (matte white, matte dark, glossy metal);
- street lamps with warm point lights for night rain;
- white **visibility markers** with red bands 10, 25, 50, 100 and 150 m in front of the camera, for
  judging the haze.

It starts in rain at 15:00 under a slow day/night cycle (30 minutes). The Main Camera runs
**Freecam** and **Weather Test** (`Engine/Content/Scripts/WeatherTestScript.cs`). The HUD is
`Content/UI/WeatherTest.xml` + `.css`.

| Key | Action |
| --- | --- |
| 1 / 2 / 3 / 4 | Clear / rain / sandstorm / snow. The old weather fades out, then the new one fades in with its own wind (rain 5, sandstorm 14, snow 2 m/s). |
| Up / Down | Intensity |
| Left / Right | Turn the wind |
| Z / X | Wind speed |
| H | Haze steps: 0, 0.3, 0.6, 1 |
| T | Starting hour +3 h (restarts the cycle there) |
| C | Auto-cycle the weathers. **Seconds Per Weather** (5-120, default 20) and **Auto Cycle** are Inspector fields on the script. |
| Right mouse, WASD, E/Q, Shift | Freecam |

## Limitations

- **No occlusion.** Rain and snow fall through roofs, and raindrops bead on surfaces under a roof too.
  There is no shelter or height map test yet; untick the material's **Rain Drops** for sheltered
  surfaces.
- **Surface response.** Only **WetSurface** materials soak, fill puddles and ripple in the rain (see
  [Material_Component.md](Material_Component.md#wet-surfaces)); other materials get raindrops and a wet
  sheen. Water gets raindrop rings (see [Water.md](Water.md#rain-on-the-water)). Raindrops don't
  splash off the ground, and there is no snow cover.
- **The ground mist is one flat layer** at Fog Base Z. It does not follow terrain heights, and it
  fills rooms below its top like the outdoors.
- **Particles, haze and mist are lit by the sun and sky only.** Street lamps and other point lights do
  not light the rain or the mist (the volumetric fog does: see
  [Scene_Lighting_and_Fog.md](Scene_Lighting_and_Fog.md)).
- **No sound.** Add rain or wind loops with an Audio component or `PlaySound(..., loop: true)`.
- **Weather does not change the scene's directional lights with a custom skybox,** only with the
  day/night cycle.
- The orange speckles on the glossy street at night in the sample are not from the weather. They show
  with the weather cleared too.

## Where the code lives

| Area | Files |
| --- | --- |
| Settings and profiles | `Engine/Logic/EnvironmentSettings.cs`, `Engine/Logic/Weather.cs` (`WeatherType`, `WeatherProfile`) |
| Rendering | `Engine/Renderer/RenderModules/WeatherRenderModule.cs`, `Engine/Content/Shaders/Forward/Weather.fx`, `Engine/Renderer/Renderer.cs` |
| Sun and clouds | `Engine/Renderer/EnvironmentSky.cs` |
| Play/Stop restore | `Engine/Logic/PlayMode.cs` |
| Anvil | `Editor/Anvil/Models/EnvironmentViewModel.cs`, Environment panel in `Editor/Anvil/Views/MainWindow.axaml` |
| Sample | `Engine/Content/Scenes/WeatherTest.obsc`, `Engine/Content/Scripts/WeatherTestScript.cs`, `Engine/Content/UI/WeatherTest.*` |
| Wet ground, mist and drops over time | `SurfaceWetness` in `Engine/Logic/Weather.cs` (`Wetness`, `Ripples`, `GroundFog`, `GroundFogColor`, `Drops`) |
| Ground mist | `GroundFog` technique in `Engine/Content/Shaders/Forward/Weather.fx`, drawn by `WeatherRenderModule` |
| Wet ground and raindrops | `ApplyWetSurface` and `ApplyRainDrops` in `Engine/Content/Shaders/GBufferSetup/Gbuffer.fx`, set per material in `GBufferRenderModule` |
| Checks | `Tests/Components/WeatherChecks.cs` (`--graphics` draws each weather on the GPU), `Tests/Components/WetSurfaceChecks.cs` (incl. ground mist and drops) |
