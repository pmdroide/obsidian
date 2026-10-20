# Water: floating, splashes and rain

Water in Obsidian is a gameobject with the **Water** role. Everything below its
top surface, inside its XY footprint, counts as water. With a Water material its
surface moves with the swell the shader draws. On top of that:

- **Floating:** Dynamic bodies with **Buoyancy** float and ride the waves.
- **Water contacts:** every Dynamic body that goes in or comes out raises
  `OnWaterEnter` / `OnWaterExit`.
- **Splashes:** fast entries throw droplets and spray, and every entry leaves an
  expanding ring with foam on the surface.
- **Rain:** the scene's rain dimples the water with raindrop rings.

The sample is `Engine/Content/Scenes/WaterTest.obsc` (see the end of this page).

## Setting up water

1. Add a Cube, scale it to the size of the pool (the top face is the surface) and
   place it so its top is where the water should be.
2. Add Component > **Material**, select **Water** in **Shader**, and assign the
   gameobject the **Water** role in the Inspector.
3. Tune the look in the material: colour, Wave Height, Wave Scale, Clarity, Foam.
   See [Material_Component.md](Material_Component.md).

Water has no collider: rays and bodies pass through it. Give the pool's floor and
walls a **Static** Physics component, or things sink forever. Keep the water's
sides a couple of centimetres inside the walls so they don't flicker against the
tiles.

## Floating

Give a gameobject a Physics component with Body Type **Dynamic** and tick
**Buoyancy**:

- **Buoyancy Strength:** the push when fully under water, relative to the body's
  weight. 2 floats half-submerged, more rides higher, below 1 sinks.
- **Water Drag:** how quickly water slows movement and spin.

The body is split into 3×3×3 samples that each push up where they are under the
surface, so tilted bodies right themselves and rock with the waves
(`Engine/Physics/Buoyancy.cs`). The CPU copy of the swell is
`Engine/Physics/WaterWaves.cs`. Keep it identical to `Swell()` in
`Content/Shaders/Forward/Water.fx`, or floaters bob out of step with the surface.

## Going in and coming out

After each physics step, `ScenePhysics` compares the bottom of every Dynamic
body's collider with the wave height under it:

- It goes **in** when its bottom drops below the surface.
- It comes **out** when its bottom rises more than 5 cm above the surface
  (`ScenePhysics.WaterExitMargin`), so floaters bobbing on waves don't flicker in
  and out.

Each crossing is a `WaterContact`:

| Field | Meaning |
| --- | --- |
| `GameObject` | The body that went in or came out. |
| `Point` | Where it crossed: on the surface, under the collider's centre. |
| `Velocity` | Its velocity as it crossed. |
| `Speed` | m/s downwards on enter, upwards on exit. |
| `Radius` | Half the collider's width: how wide a splash it makes. |
| `Mass` | The body's mass. |

It goes to `ScenePhysics.WaterEntered` / `WaterExited` first, then to the
gameobject's scripts:

```csharp
public override void OnWaterEnter(WaterContact water)
{
    if (water.Speed > 4f) PlaySoundAt("Audio/blip.wav", water.Point, water.Speed / 10f);
}

public override void OnWaterExit(WaterContact water) => Log($"{GameObject.Name} left the water");
```

Bodies don't need Buoyancy for this: a stone that sinks enters the water too.
Bodies already in the water when Play starts enter on the first step, at about
zero speed, so they don't splash.

## Splashes

`MainSceneLogic.WaterSplashes` (`Engine/Particles/WaterSplashes.cs`) turns
contacts into effects:

| Entry speed | Effect |
| --- | --- |
| below 0.2 m/s | nothing (it drifted in) |
| 0.2 to 1.2 m/s | a ring only |
| 1.2 m/s and up (`MinSplashSpeed`, a 7 cm fall) | a splash: a ring with foam plus droplets and spray |
| leaving at 2 m/s and up (`MinExitSplashSpeed`) | a few droplets shed and a small ring |

How much a splash throws scales with the speed (6 m/s, a 1.8 m fall, is the
reference) and the square root of the body's size:

- **Crown:** droplets thrown up and out from the rim of the body.
- **Jet:** a column of droplets from the middle, only for hard hits.
- **Spray:** a soft puff that drifts with the scene wind.

Droplets fall under gravity and die when they land back on the water. The body's
sideways motion leans them. They are lit by the sun and sky, so they darken at
night.

The droplets are engine-owned particle systems without a gameobject. Twelve
slots let twelve splashes play at once. A thirteenth reuses the slot that
splashed longest ago. The particle pass draws them after the water
(`ParticleRenderModule.Draw(..., extraSystems)`).

### Rings and foam

The water shader draws up to 16 rings at once (`WaterSplashes.MaxRipples`, kept
equal to `MAX_SPLASHES` in `Water.fx`). Each ring is a packet of small waves whose
front starts at the body's edge and travels outwards at about 1.3 m/s. It
stretches, fades and is gone after 4 s. Where the body went in, bubbly white water
thins out over about 2.5 s. This foam shows even when the material's Foam is 0.
The rings only bend the surface's lighting and reflections, not its mesh.

### Rain on the water

While it rains (Environment > Weather > Rain), the water gets raindrop rings all
over, like the puddles of Wet Surface materials. Their strength follows the
rain's intensity through the renderer's `SurfaceWetness.Ripples`, and fades in
and out over about 1.5 s. Rings fade with distance (gone at 45 m) and once they
are smaller than a pixel.

## Script API

| Member | What it does |
| --- | --- |
| `OnWaterEnter(WaterContact)` / `OnWaterExit(WaterContact)` | Hooks on the body's own scripts (see above). |
| `IsInWater(target = null)` | `true` while `target` (default: this gameobject) is partly under water. Play only. |
| `RaycastWater(ray, maxDistance, out Vector3 point)` | Where a ray first meets a moving water surface. `Raycast` ignores water. |
| `Splash(point, speed = 6, radius = 0.3f)` | A splash anywhere on the water, e.g. a bullet (fast, small radius) or a cannonball. |
| `WaterSplashes.AddRipple(point, strength, radius)` | Only a ring, e.g. a fish surfacing or a footstep in shallow water. |
| `WaterSplashes.Enabled` | Off: bodies still float and scripts still get the hooks, but nothing splashes. |
| `WaterSplashes.Splashed` | Event `(point, speed, radius)` raised for every splash, e.g. to play a sound. |
| `WaterSplashes.SplashCount` / `ParticleCount` / `Ripples` | What is happening now. |

```csharp
// Shoot at the water: a fast, thin splash where the crosshair meets it.
if (GameInput.MouseClicked && RaycastWater(CameraRay, 200, out Vector3 hit))
    Splash(hit, speed: 25, radius: 0.05f);
```

## The WaterTest sample

`Engine/Content/Scenes/WaterTest.obsc` is in the scene list. It is a rainy deck
with two water bodies:

- **Pool** (20 × 16 m, 2.6 m deep): gentle waves and two underwater lights.
  A raft, two buoys, a barrel that tips over onto its side, and a sunken crate on
  the floor.
- **Wave tank** (16 × 12 m): a 0.9 m swell with foamy crests. A boat, crates and
  a light ball that the waves sometimes toss out of the water.
- A diving tower and three lamp posts.

The Main Camera runs Freecam and **Water Test** (`Content/Scripts/WaterTestScript.cs`,
HUD `Content/UI/WaterTest.xml`/`.css`):

| Key | What it does |
| --- | --- |
| Click | Drop the selected object onto the water under the mouse. |
| 1-4 | Select beach ball, crate, plank or boulder (the boulder sinks). |
| Up / Down | Drop height, 1 to 20 m. The HUD shows the impact speed. |
| Space | A volley of six random objects over the pool. |
| X | A splash under the mouse, without an object. |
| V | Pool waves: calm, gentle, choppy, storm. |
| R / [ ] | Rain on or off / rain intensity. |
| G | Automatic drops on or off (every 2.5 s, on by default). |
| T | Time of day +3 h. |
| C | Clear everything dropped. |

Dropped objects (at most 40) are removed when Play stops. The pool's waves and
the weather are put back too.

## Tuning

- **Detection:** `ScenePhysics.UpdateWaterContacts` and `WaterExitMargin`.
- **Thresholds and sizes:** `WaterSplashes.MinSplashSpeed`, `MinExitSplashSpeed`,
  and `Throw` (counts, speeds, sizes, the jet's threshold).
- **Droplet look:** `CreateCrown`, `CreateJet`, `CreateMist` (colours, lifetimes,
  drag, renderer settings).
- **Rings and foam:** `SplashRings` in `Water.fx` (front speed, wavelength,
  amplitude, foam), `WaterSplashes.RippleLifetime`.
- **Rain rings:** `RainLayer` / `RainRings` in `Water.fx`.

## Limits

- Only Dynamic bodies are tracked. A gameobject moved by a script without a body
  doesn't splash. Call `Splash` yourself.
- The test uses the point under the collider's centre. A body whose centre is
  outside the water's footprint doesn't enter it, even if a corner dips in.
- Rings bend the lighting only. The mesh doesn't rise with them, and there is no
  refraction.
- Water volumes are boxes from the gameobject's bounds with no depth limit. A rotated
  water gameobject counts as its axis-aligned bounding box.
