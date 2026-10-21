# Particle systems

The **Particle System** component is Obsidian's take on Unity's particle system (Shuriken): fire, smoke,
sparks, magic, explosions, fireworks. Particles are born from a **Shape** at the gameobject, over time and in
bursts, and change over their lifetime through optional **modules**. They are simulated on the CPU and drawn
as camera-facing quads on the HDR image, so bright additive particles bloom.

Try it in the `ParticleTest` sample scene (`Content/Scenes/ParticleTest.obsc`, last in the scene list). It
has five stations:

| Station | Shows |
|---|---|
| Campfire | Flame Sheet flipbook tongues (licking up, tearing and lifting off) leaning with the wind over a hot Glow core, lighting the scene with a flickering **Lights** module (no scene light); lit flipbook smoke (Smoke Sheet + Texture Sheet Animation) drifting with the scene wind (External Forces) and glowing orange from the fire below; glowing embers |
| Spark Fountain | stretched sparks with **World** collisions bouncing off the ground and a tilted deflector; a few carry their own light (Lights, Per Particle) |
| Fireworks | a launcher whose rockets leave **Trail** sub-emitter sparks and explode into a **Death** sub-emitter burst in their own colour |
| Magic Vortex | Circle shape with orbital + radial velocity (a spiral), Star particles, ground rings (Horizontal Billboard), and a comet the script flies in a circle (Rate over Distance trail) |
| Explosion | seven one-shot systems restarted together: a Fireball Sheet fireball that cools into soot, a rising lit smoke column, a lit ground dust skirt, colliding sparks, burning chunks with **Trail** smoke, and a flash; the fireball lights the area and fades as it cools |

Keys (the `Particle Test` script on the Main Camera): **Space** explosion, **Click** a burst of sparks where
the mouse points, **F** fireworks on/off, **P** pause/resume every system, **R** restart every system.
Freecam flies the camera (hold right mouse, WASD, E/Q).

## Adding one

- **GameObject > Effects > Particle System** (or the Hierarchy **+** menu) adds a small sphere with a
  Particle System. The sphere marks and selects the emitter while editing and is hidden during Play
  (**Hide Mesh in Play**).
- Or **Inspector > Add Component > Particle System** on any gameobject (untick Hide Mesh to keep its mesh).

Systems with **Play On Awake** play in the editor viewport too, so you see the effect while you tune it. A
finished one-shot preview starts again after a second. Play restarts every system from scratch.

The gameobject's position and rotation place the shape; its **scale is ignored**, so the marker can be any
size. Axes are the engine's: **Z is up**, so a Cone emits upwards by default.

## Modules

| Module | Settings |
|---|---|
| Main | Duration, Looping, Prewarm, Start Delay, Start Lifetime/Speed/Size/Rotation (random between two values), Start Color + Color 2 (random between), Gravity (share of the scene's gravity), Space (Local follows the gameobject, World stays put), Sim Speed, Play On Awake, Max Particles |
| Emission | Rate / Second, Rate / Metre (trails behind moving emitters), Bursts (time, min/max count, cycles, interval) |
| Shape | Cone, Sphere, Hemisphere, Box, Circle, Edge; Angle, Radius, Thickness (1 volume, 0 surface), Arc, Box Size, offset Position/Rotation, Randomize direction |
| Velocity over Lifetime | Linear (Local/World), Orbital (°/s around the local axes), Radial, Speed Modifier curve |
| Limit Velocity | speed limit + Dampen, Drag |
| Force over Lifetime | constant acceleration (Local/World) |
| External Forces | the scene's wind (Environment > Wind Speed/Direction) carries particles; Wind multiplier, Response |
| Noise | smooth turbulence: Strength, Frequency, Scroll Speed |
| Color over Lifetime | gradient keys (time + colour with alpha) that multiply the start colour |
| Size over Lifetime | curve keys that multiply the start size |
| Rotation over Lifetime | spin, °/s (random between two values) |
| Collision | Plane (a floor at a world height) or World (every Physics collider, one ray per moving particle); Bounce, Dampen, Lifetime Loss, Radius Scale, Min Kill Speed |
| Texture Sheet Animation | flipbook: Tiles X/Y, Cycles, Random Start Frame |
| Sub Emitters | rows of Trigger (Birth, Trail, Collision, Death) + target drop slot + count, Inherit Velocity/Color. Drag a Hierarchy gameobject with a Particle System into the target slot; Clear removes it. Targets are stored by name. |
| Renderer | Render Mode (Billboard, Stretched, Horizontal, Vertical), Blend (Alpha, Additive), Texture, Sort Mode, Brightness (above 1 blooms), Lit (sun + sky shading for smoke/dust), Soft Particles, Camera Fade, Length/Speed Scale (stretched) |
| Lights | the particles light the scene: Mode (System, Per Particle), Color or Use Particle Color, Intensity, Range, Alpha Affects Intensity, Flicker + Flicker Speed; System: Offset, Cast Shadows; Per Particle: Ratio, Max Lights, Size Affects Range (see below) |
| Volumetric | draw as a raymarched volume: Source (Particles or Voxels), Draw Particles, Density, Noise Erosion/Scale/Speed, Steps, Refill Time; Particles: Resolution; Voxels: Voxel Size, Volume, Max Radius, Expand Time, Fade Time, Flatten (see below) |

Curves and gradients join their keys with straight lines and hold flat before the first and after the last.

**Textures.** The built-in ones are painted in code at startup (`ParticleTextures`): Soft Circle, Glow,
Spark, Smoke, Smoke Sheet (a 4 x 4 flipbook), Flame, Flame Sheet (a 4 x 4 flipbook of a flame tongue, coloured by
temperature: keep Start Color near white), Fireball Sheet (a 4 x 4 flipbook of an explosion billow cooling into
soot, coloured like Flame Sheet; meant for Alpha blending), Star, Ring, Square. **Custom** loads a texture from
`Content.mgcb` by its content path without extension; if it doesn't load, the console says so and Soft
Circle is drawn.

**Sub-emitters.** The target is another gameobject with a Particle System, found by name. Give it
**Play On Awake off**, **Rate 0** and **World** space: it then emits only when its parent's particles are
born, trail, bounce or die, from its own shape centred on that particle. Trail counts are per second.

## Lights (fire that lights the scene)

Tick **Lights > Enabled** and the system lights the scene, like Unity's Lights module. Its lights are point
lights the renderer adds to the scene's own each frame. They light every surface, forward materials, the
volumetric fog and **Lit** particles (smoke over a fire glows orange from below). They are never saved, can't be
selected, play in the editor preview and die with the particles.

- **System**: one light at the glowing heart of the particles (each particle weighted by its alpha, brightest
  colour channel and size), eased so it glides, plus **Offset** (world metres). Use it for a campfire, a torch or
  a fireball. With **Alpha Affects Intensity** the light follows how much the particles glow compared with their
  recent peak, so it fades when a fire is stopped or an explosion cools. **Cast Shadows** gives it point light
  shadows. They cost six shadow faces, redrawn whenever the light moves, and the shadow cube's near plane is
  1 m, so things closer than that cast no shadow.
- **Per Particle**: lights ride on a share of the particles (**Ratio**), at most **Max Lights** (16), the
  brightest carriers first: sparks, fireworks, magic. **Alpha Affects Intensity** dims each by its particle's
  alpha; **Size Affects Range** scales its range with the particle's size.

**Intensity** and **Range** use a scene Point Light's scale (a campfire is about 6 and 10 m). **Use Particle
Color** takes the particles' hue; the light's brightness stays the Intensity. **Flicker** (0..1) wavers the
intensity with two octaves of noise at about **Flicker Speed** changes per second, and sways a System light by a
few centimetres: 0.3-0.4 is a campfire, 0.6 a torch in the wind. The volumetric fog takes the first 8 point
lights (scene lights come first), the forward pass 40.

## A realistic fire

The ParticleTest campfire is three systems over the logs, plus embers and smoke:

1. **Flames**: Flame Sheet, Texture Sheet Animation 4 x 4 with 1 cycle (each particle plays the tongue's life
   once), Additive, Brightness about 2.5, white Start Color. Few large particles (30/s, 0.8-1.3 m) overlap into
   one mass. A small Cone (radius 0.25, angle 6) sits at the fuel, with low Start Speed and an upward Force (hot
   gas accelerates as it rises), a little Noise and External Forces (Wind 0.25) so the flames lean with the
   wind. Alpha fades in over the first 10% and out at the end; size grows slightly, then shrinks. Little or no
   spin: flames stay upright.
2. **Core**: short-lived Glow particles at the base (yellow-white to orange, Additive) fill the bed of the fire
   where the flames start.
3. **Light**: the flames' Lights module, System, colour (255, 140, 60), Intensity 6, Range 10, Flicker 0.35,
   Offset 0.3 m up.

Embers (tiny Glow particles with Noise, Drag and wind) and Lit flipbook smoke (Alpha, Distance sort) finish it.

## A realistic explosion

Real blasts are opaque: a white-hot fireball whose billows cool to orange and soot, then a dark cloud that keeps
rising. Many overlapping Additive particles only add up to a flat orange glow. The ParticleTest explosion
(`Explosion ...` gameobjects, restarted together by the `Particle Test` script):

1. **Fireball**: Fireball Sheet, Texture Sheet Animation 4 x 4 with 1 cycle, **Alpha** blend, Distance sort,
   Brightness 3, not Lit (the fire glows by itself and its soot hides what is behind it). About 20 large
   particles in two bursts from a Hemisphere, fast Start Speed with Drag 4.5 (they burst out and stop), an upward
   Force (the hot cloud rises) and some Noise. Color over Lifetime stays white while it burns, then darkens to
   grey (the soot frames) and fades. Its System light (Intensity 60, Range 20) lights everything around and
   fades as the colour darkens.
2. **Smoke**: Lit Smoke Sheet with dark Start Colors, delayed 0.25 s so it grows out of the fireball, rising
   slower than the fire and drifting with the wind. Its gameobject sits a little lower than the fireball's, so
   from above it sorts behind the fire.
3. **Dust**: a Circle shape on the ground throwing Lit, dust-coloured Smoke Sheet puffs outwards with strong
   Drag: the skirt kicked up by the pressure wave.
4. **Debris** (stretched sparks with World collisions) and **Chunks**: a few larger Glow particles that fall
   with gravity and leave a smoke **Trail** sub-emitter (`Explosion Chunk Smoke`, Play On Awake off, World).
5. **Flash**: one short, large Glow for the first frames.

## Volumetric (thick smoke, Counter-Strike 2 smokes)

Tick **Volumetric > Enabled** to draw the system as a lit volume instead of flat billboards: thick smoke you can
walk into, which hides what is behind it and which bullets punch tunnels through. The density comes from one of
two **Sources**:

- **Particles**: every particle adds a soft ball of density (its size, colour and alpha) to a grid fitted
  around the particles each frame (**Resolution** cells along the longest side). Any effect becomes a volume:
  a chimney, an explosion's smoke, dust.
- **Voxels**: a Counter-Strike 2 style smoke grenade. From the emitter (the shape's position) the smoke floods
  a voxel grid (**Voxel Size**), nearest cells first, until it fills **Volume** cubic metres, blooming over
  **Expand Time**. A voxel is only reached when no **Static** collider lies between it and its neighbour, so
  the smoke fills rooms, flows through doorways and around corners and never through walls: in a corridor the
  same volume makes a long thin smoke. **Max Radius** bounds how far it goes, **Flatten** (above 1) makes it
  spread wider than tall. It holds while the system emits (**Main > Duration**, Looping off: 18 s like CS2),
  then dissipates from the outside in over **Fade Time**. The colour is **Start Color** (its alpha scales the
  density). Voxels under a roof or behind a wall from the sun get only sky light.

Shared settings: **Density** (extinction per metre: higher is thicker), **Noise Erosion / Scale / Speed** (the
detail noise that eats into the edges and drifts upwards), **Steps** (raymarch samples per pixel), **Refill
Time** (how long holes take to close), **Draw Particles** (also draw the billboards, e.g. the jet of smoke from
the canister). The Renderer's **Lit**, **Brightness** and **Blend** apply to the volume too.

Try it in the `SmokeTest` sample scene (`Content/Scenes/SmokeTest.obsc`, in the scene list): a smoke in
the open (a round dome), one in a corridor (stretched along it), one in a roofed room (fills it, leaks out of
the door, darker inside), a Particles-source smoke stack, and one the script throws. Keys (the `Smoke Test`
script on the Main Camera, aiming with the crosshair): **G** throw a smoke grenade, **Click** shoot a tunnel
through the smoke, **H** an HE grenade blast that blows a hole into it, **R** restart every smoke, **V**
Volumetric on/off (billboards only, for comparison). The HUD shows how much of the crosshair's line of sight the
smoke hides.

NPCs (`NpcComponent`) don't see through smoke that hides more than 90% of the view to the target
(`NpcComponent.SmokeBlocksSight`); they still hear within their Hearing Range.

## Scripts

```csharp
var fire = GetComponent<ParticleSystemComponent>();      // or FindGameObject("Campfire").GetComponent<...>()
fire.Play();                  // start (or resume after Pause)
fire.Stop();                  // stop emitting; the particles alive live out their lives
fire.Stop(clear: true);       // ...and remove them
fire.Pause();                 // freeze everything
fire.Restart();               // clear and play from the start (a one-shot explosion)
fire.Emit(20);                // 20 particles from the shape now
fire.EmitAt(hit.Point, 40, hit.Normal * 3);   // around a world position, with extra velocity
fire.Simulate(2);             // fast-forward 2 s
fire.ParticleCollided += e => Log($"hit {e.GameObject?.Name} at {e.Point}");

int alive = fire.ParticleCount;  bool busy = fire.IsAlive;  // also IsPlaying, IsEmitting, IsPaused, Time
fire.Main.StartColor = Color.OrangeRed;                     // settings are live; read every step

// Volumetric systems
smoke.CarveTunnel(gunPosition, hit.Point, 0.15f);            // a bullet through this smoke (refills over Refill Time)
smoke.CarveHole(blastPoint, 3f);                            // an HE grenade's hole
float hidden = smoke.Opacity(eye, target);                  // 0..1 of the line of sight it hides
ParticleSystemComponent.CarveTunnels(from, to, 0.15f);      // ...through every smoke of the active scene
ParticleSystemComponent.CarveHoles(point, 3f);
float blocked = ParticleSystemComponent.VolumeOpacity(eye, target);   // all smokes together
int voxels = smoke.Volume?.FilledVoxels ?? 0;               // also TargetVoxels, IsDissipating, DensityAt(point)

// Lights module
fire.Lights.Enabled = true;   fire.Lights.Intensity = 8;    // live, like every module
int lights = fire.LightCount;                               // lights shining this frame
PointLight glow = fire.Simulation?.Lights?[0];              // the engine-owned light (read only: rewritten each step)
```

Settings changed by a script during Play stay changed after Stop (Play only restores transforms and the
environment), so drive runtime effects with the methods above rather than by editing the modules.

## How it works

- `Engine/Particles/ParticleModules.cs`: the module classes (saved in the scene with enums by name and
  vectors/colours as `[x,y,z]` / `[r,g,b,a]`), `ParticleRange`, `ParticleCurve`, `ParticleGradient`.
- `Engine/Particles/ParticleSimulation.cs`: the CPU simulation of one system. It reads the settings live, so
  inspector edits apply at once. Emission runs through the looping cycle (rate, bursts, distance); particles
  born mid-frame are aged by the part of the frame they already lived, so emission is smooth at any frame
  rate. Local-space particles are moved by the gameobject's motion each step. Steps longer than 0.1 s are cut.
  It needs no graphics device, so `Tests/Components/ParticleChecks.cs` runs it headless.
- `Engine/Components/ParticleSystemComponent.cs`: the component, the script API, the Play lifecycle
  (`OnStart` plays Play On Awake systems and hides the marker mesh through `TransformMatrix.Hidden`;
  `OnUpdate` steps; `OnStop` clears) and the edit-mode preview (`UpdatePreviews`, called by
  `MainSceneLogic.Update` outside Play). Pausing the game (`GameFlow.Paused`) freezes particles because
  components don't update while paused.
- `Engine/Renderer/RenderModules/ParticleRenderModule.cs` + `Content/Shaders/Forward/Particles.fx`: after water
  and before the weather, every system's particles go into one instance buffer and draw as instanced quads,
  systems far to near, each sorted by its Sort Mode. There is no depth buffer: like the weather, the shader
  reads the linear G-buffer depth, which both hides particles behind geometry and fades them softly into it.
  Colours are linearised in the shader; Lit particles get the sky (environment map) and sun with a bulging
  normal, so a smoke puff reads as a volume, plus the four point lights that shine most on the system
  (`SetPointLights`, judged from the emitter, with the deferred point light falloff).
- `Engine/Particles/ParticleLights.cs`: the Lights module's engine-owned `PointLight` pool, refreshed at the end
  of each simulation step (`ParticleSimulation.Lights`; cleared with the particles). `Renderer.Draw` copies the
  scene's point lights into a per-frame list and appends every enabled system's lights
  (`ParticleSystemComponent.CollectLights`). That list feeds the shadow maps, deferred lights, froxel fog, the
  forward pass and Lit particles; the editor passes (ID picking, light icons) keep the scene's own list. A
  System light only moves (and dirties its shadow) past 1 cm. Its shadow map is disposed when Cast Shadows goes
  off or the system clears.
- `ParticleTextures.Flame`: the Flame Sheet frames. A teardrop whose width, licking and erosion change over the
  flame's life: two domain-warped noise layers scroll up through it, and noise eats it from the top down, more
  with age. Old frames lift off at the bottom. The temperature (density, hotter low and in the middle) picks
  the colour from `FireRamp` (soot red, orange, yellow, pale yellow-white, stored linear), and young flames are
  blue at the root.
- `ParticleTextures.Fireball`: the Fireball Sheet frames. A lumpy outline with rolling billows inside it; the
  temperature is hottest in the middle of each billow and falls in the crevices, at the rim and with age, so the
  frames go white-hot, orange, soot red and then dark grey soot that the noise tears into wisps. Fire colours
  come from `FireRamp`; the soot is a light-shaded dark grey so it reads as volume unlit.
- `Engine/Particles/ParticleVolume.cs`: the density grid of a Volumetric system, updated at the end of each
  simulation step (GPU-free). Particles: splats each particle into a grid snapped to quarter-octave cell sizes
  (so it doesn't swim). Voxels: a best-first flood fill over 26 neighbours from the emitter; each step between
  two voxel centres is checked with `ScenePhysics.RaycastStatic` (`ParticleStepContext.RaycastStatic`), and
  cells fill in order of their ellipsoidal straight-line distance or, around walls, their path length, plus a
  slow noise so the outline billows. Each new voxel also casts one ray to the sun (`SunDirection`). Density
  thins over the last-filled 55% (soft edges), fades in as the flood arrives and out (outer and random voxels
  first) when dissipating. Holes are capsules (`Carves`, at most 16) that shrink as they refill; the CPU
  `DensityAt`/`Transmittance` and the shader cut the same shape.
- The renderer uploads each volume into a 3D texture (RGBA8: colour or sunlight + density) when its `Version`
  changes and draws it in the same far-to-near order as the billboards, with the `Volume` technique of
  `Particles.fx`: a full-screen triangle scissored to the volume's box on screen marches the camera ray through
  the box up to the scene depth (jittered per pixel and frame; TAA smooths it), erodes the density with a
  tiling 32³ detail noise (Worley + value noise, domain-warped), cuts the holes, and lights each sample with
  the sky plus the sun marched five cells through the volume (with a multiple-scattering term) and a
  Henyey-Greenstein phase. Output is premultiplied (Additive blend adds it).

Limits: particles don't cast shadows, aren't reflected by SSR or the environment capture, and aren't sorted
against water or transparent forward meshes. Lit billboards take at most four point lights per system, chosen
at the emitter, so a long smoke trail is lit as if it were all there. Volumes don't cast shadows on the scene, ignore point lights, and
are sorted per system (two overlapping smokes don't mix sample by sample). A voxel smoke stays where it
bloomed (moving the gameobject doesn't move it) and is only blocked by Static colliders; a voxel grid is at
most 64³ (a large Max Radius coarsens the voxels). World collisions use the Physics colliders only (gameobjects
without a Physics component, and the voxel world's chunks without Collision, are see-through).
