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

In the scene file:

```json
"Environment": {
  "DayNightCycle": true, "TimeOfDay": 15,
  "Weather": "Rain", "WeatherIntensity": 0.7, "WeatherHaze": 0.6, "WindSpeed": 5, "WindDirection": 60
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

All values are scaled by **Intensity**. Each type's fixed look (counts, sizes, colours, haze distance,
sun and cloud effects) is a `WeatherProfile` in `Engine/Logic/Weather.cs`. Tune a weather there.

The sun and cloud effects only apply with the **day/night cycle**, because the cycle owns its sun.
With a custom skybox, the scene's own directional lights stay as they are, and only the particles and
haze are drawn.

## How it is drawn

`WeatherRenderModule` (`Engine/Renderer/RenderModules/WeatherRenderModule.cs`) with
`Engine/Content/Shaders/Forward/Weather.fx` runs after the water pass and before TAA, on the HDR image:

1. **Haze:** a full-screen pass. The haze thickens with the G-buffer depth, so the sky gets the most.
   It is lit by the captured sky and the sun, and glows when you look towards the sun.
2. **Particles:** one static buffer of quads, built once. The vertex shader places and animates them:
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

Cost: one full-screen pass and one draw of up to 24 000 quads.

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

- **No occlusion.** Rain and snow fall through roofs. There is no shelter or height map test yet.
- **No surface response.** No wet surfaces, puddles, splashes or snow cover. The ground looks the
  same in every weather.
- **Particles are lit by the sun and sky only.** Street lamps and other point lights do not light the
  rain.
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
| Checks | `Tests/Components/WeatherChecks.cs` (`--graphics` draws each weather on the GPU) |
