# Liquid in bottles: how Half-Life: Alyx does it

Research behind the Liquid component (`Docs/markdown/Bottle_Liquid.md`). Target: Half-Life: Alyx build of 2023-04-09
(`game/hlvr/bin/win64/server.dll` 22 730 680 bytes; hashes of every file read are in [`evidence/hashes.txt`](evidence/hashes.txt)).

**Short answer:** there is no fluid simulation. A bottle's liquid is a **plane** drawn by a shader
(`vr_simple_liquid`) on the bottle's own liquid shell. The server only keeps three numbers per bottle: how
agitated it is, how much foam it has, and which way is "down". It sends them to the shader in the bottle's
**render colour**.

## How it was found

The REA MCP server was available, but `server.dll` is too large for a full Ghidra pass. The evidence comes from the
project's own tool (`Docs/Research/Source2Look/re_tool`: Iced disassembler, ValvePak and ValveResourceFormat):

```
grep -aoE "[ -~]{5,}" server.dll | grep -iE "liquid|slosh|agitat|foam"            # names, convars, fields
dotnet run -c Release -- cvars server.dll "prop_liquid"                              # convar defaults
dotnet run -c Release -- xref  server.dll 0x1814A1350,...  3 3                       # code reading each convar (+0x38 = value ptr)
dotnet run -c Release -- xref  server.dll @,1809D9300,1809D9C00 0 0                  # disassembly of a range
dotnet run -c Release -- list  <hlvr> shaders_pc_dir.vpk liquid                       # vr_simple_liquid_pc_50_{vs,ps,features}.vcs
dotnet run -c Release -- res   <hlvr> pak01_dir.vpk "waterbottle_liquid.vmat_c|..."   # material parameters
dotnet run -c Release -- shader <hlvr> shaders/vfx/vr_simple_liquid_pc_50_ps.vcs out/ # DXBC disassembly
```

## Observed

**Strings / fields** (`server.dll`): `UpdateLiquidMaterial`, `m_bCalcLiquidMaterial`, `m_flCurAgitation`,
`m_flCurFoamAgitation`, `m_flPrevHighAgitation`, `m_flPrevHighFoamAgitation`, `m_flFoamSettleTime`, `m_bIsAgitated`,
`m_bAgitatedSound`. The client has no liquid code beyond names.

**Convars** ([`evidence/convars.txt`](evidence/convars.txt)):

| Convar | Default | Use (traced) |
|---|---|---|
| `prop_liquid_settle_duration` | 20 | seconds for the agitation peak to fall to 0 |
| `prop_liquid_velocity_max_agitation` | 5 | clamp of the agitation; the shader gets agitation / 5 |
| `prop_liquid_velocity_additive_power` | 10 | exponent of (impulse / max) when a new impulse lands on a decaying peak |
| `prop_liquid_velocity_dampening` | 0.4 | impulse = (change of velocity)^(1 − 0.4) |
| `prop_liquid_foam_settle_duration` | 30 | same, foam |
| `prop_liquid_velocity_foam_max_agitation` | 200 | same, foam |
| `prop_liquid_velocity_foam_additive_power` | 1 | same, foam |
| `prop_liquid_velocity_foam_dampening` | 0.4 | same, foam |
| `prop_liquid_gravity_velocity_scale` | 1 | down = gravity − scale × velocity |
| `prop_liquid_gravity_interpolation` | 0.2 | share of the new down direction taken per update |
| `prop_liquid_debug`, `_debug_gravity` | 0 | text overlay; red/blue lines of the encoded and decoded direction |
| `sv_gravity` | 386 | in/s² (9.8 m/s²) |

**The think, `0x1809DC010`** ([`evidence/agitation.asm`](evidence/agitation.asm), [`agitation_tail.asm`](evidence/agitation_tail.asm)):

1. Reads the physics body's velocity (vtable +0x200) and a second vector (+0x228, inferred: angular velocity, deg/s).
2. `delta = |v − v_prev| + |w − w_prev|` (stored at `+0xC94..0xCA8`).
3. `impulse = pow(delta, 1 − dampening)`, `foamImpulse = pow(delta, 1 − foam dampening)`.
4. Calls the gravity routine with `v + w`, then stores `v`, `w` as the previous values.
5. If `impulse > 0.1`: when the last peak hasn't finished decaying (`peakTime + settle > now`),
   `peak = clamp(pow(impulse / max, additivePower) × impulse + remaining × peak, impulse, max)` with
   `remaining = (peakTime + settle − now) / settle`; otherwise `peak = min(max, impulse)`. `peakTime = now`.
   The same for foam.
6. `current = peak × (1 − saturate((now − peakTime) / settle))`, linear decay.
7. `SetRenderColor(R kept, G = foam / foamMax × 255, B = agitation / max × 255, A = encoded down direction)` on the
   render colour at `+0x464`.
8. While G or B is non-zero it thinks again at `now + 0.1 s`.

**The down direction, `0x1809D94B0`** ([`evidence/gravity_dir.asm`](evidence/gravity_dir.asm)):

1. `g = (0, 0, −sv_gravity × gravityScale)` (the body's gravity scale, 1 by default).
2. `target = normalize(g − prop_liquid_gravity_velocity_scale × input)`, or `(0, 0, −1)` when zero.
3. `stored += (target − stored) × 0.2` (stored at `+0xCAC`).
4. Encoded into one byte: high nibble `floor((x + 1) / 2 × 15 + 0.15)`, low nibble
   `floor((atan2(y, z) / π + 1) / 2 × 15 + 0.15)`. Decoded in the shader as `x = ((n + 1) / 16) × 2 − 1` (0 → −1),
   `angle = (((m + 1) / 16) × 2 − 1) × π`, `(y, z) = sqrt(1 − x²) × (sin, cos)`. The direction is in world space.

**The shader `vr_simple_liquid`** ([`evidence/shaders/`](evidence/shaders/): VS with reflection, PS stripped):

- **Vertex shader**: decodes the down direction from the instance's tint (alpha byte) and the agitation/foam bytes,
  passes the bottle's local Z axis in world space and a pivot at local `(0, 0, Upwards.mid)`, and a clock
  `(time + R/255) mod 200`.
- **Material** ([`evidence/liquid_materials.txt`](evidence/liquid_materials.txt), e.g. `waterbottle_liquid.vmat`):
  `g_vLiquidLevelUpwardsMinMidMax (−0.5, 4, 10)`, `…SidewardsMinMidMax (−2, 0, 0.8)`,
  `…DownwardsMinMidMax (−12, −5, 0)`, `g_flLiquidLevelHeight 0.5`, `g_flLiquidWobbleScale 0.02`,
  `…WobbleSpeed 1`, `…WobbleWavelength 0.1`, `g_flSurfaceTension 1`, `g_flLiquidSharpness 6`, bubble and
  colour settings, `g_tLiquidMask`. The shell is a separate `_liquid` material inside the glass.
- **Pixel shader** (offsets inferred from use, the PS has no reflection data):
  - `amp = agitation^10 + 0.2 × agitation + 0.01`, bubbles = `agitation^9 + 0.25 × foam`.
  - `t = 3 × floor(200 × WobbleSpeed) × clock` (600 per second at speed 1).
  - **Wobble of the down direction**: bob towards the bottle axis by `sin(0.035 t) × 0.1 × amp²`, sway by
    `amp × sin(0.035 t) × 0.3` around a direction turning at `0.001 t`, blended by
    `(1 − |axis · up|) × (1 − amp)`. With speed 1 that's 21 rad/s (3.3 Hz), which matches the slosh frequency of a
    7 cm wide bottle: `sqrt(g × π / width)` ≈ 21 rad/s.
  - **Level**: `cos = axis · up`. Interpolate Downwards → Sidewards over cos −1..0, then → Upwards over 0..1.
    Subtract `Upwards.mid × cos` (the pivot), pick min → mid → max with the fill (`LevelHeight × lerp(1, R, k)`,
    piecewise linear at 0, 0.5, 1), and add `pivot · up`.
  - **Meniscus**: `((0.75 f + 0.25) × (1 − N·V) + 0.8 (1 − N·V)^5) × SurfaceTension` inches, with
    `f = |N ∘ up|`, which raises the line at grazing angles.
  - **Ripples**: sum of 4 travelling sines on the world XY offset from the pivot (directions ±1.24…±3.74, phases
    0.034 t…0.029 t), times 4 higher-frequency modulators (8.7…31.7, ±0.104 t) weighted by `1 − f`, × WobbleScale
    × amp² × (|cos| × 0.5 + 0.5) × 150, × (1.5 − (1 − N·V)) inches.
  - `depth = level + meniscus + ripple − p · up`. Liquid where depth > 0, with a soft edge (sharpness) and two
    water lines at 0.05″ and 0.09″ below it, then bubbles (4 layers of cell noise) and lighting.

## What the engine does (`Engine/Physics/LiquidSlosh.cs`, `LiquidVolume.cs`, `Forward.fx`)

Same as Alyx (Source units inside so the defaults apply unchanged):
- the 10 Hz think, impulse, additive peak, linear decay, the 0.1 threshold, foam;
- down = normalize(gravity − scale × (v + w)), eased 20 % per 0.1 s;
- the wobble of the down direction, the three orientation tables, the meniscus and ripple formulas with their
  constants and the 600 t clock.

Different (design choices):
- **The tables are computed, not typed.** `LiquidVolume` measures the closed mesh: for upright (+Z), sideways (+X)
  and upside down (−Z) it bisects for the plane under which 0, 1/8 … 1 of the volume lies. The volume below a
  plane is the sum of tetrahedra from each clipped triangle to a point on the plane, so the plane's cap needs no
  polygon. That gives 9 fills per orientation (Alyx has 3), all measured from the bounds centre.
- **Velocity comes from the transform**, so a script, a ragdoll hand or the editor gizmo slosh it as well as physics.
  The down direction eases per frame (`1 − 0.8^(dt / 0.1)`) instead of per think, and stays a float (no 8-bit
  encoding).
- **The per-instance part of the PS runs on the CPU** (plane normal and height); Forward.fx only does per-pixel work.
- **The surface top**: the container is drawn inside first (back faces). Where the view ray crosses the plane
  inside the container's radius, the top is shaded with the liquid's roughness and the ripple slope. Alyx's top
  shading wasn't traced.
- Foam band (foam × Foaminess × 2 cm under the surface) and bubbles (2 layers rising at per-column speeds) are the
  engine's own; Alyx's bubbles are screen/normal-map driven cell noise (not ported).

## Unknown / not recovered

- What vtable +0x228 returns (angular velocity is the likely reading) and in which space.
- `g_flLiquidLevelHeightDelta` is inferred as the weight of the render colour's red byte on the fill (`cb0[11].y`).
  How the game sets R (drinking? never?) wasn't traced.
- The exact `$Globals` layout of the pixel shader (stripped). The mapping of `cb0[n]` to material parameters is
  inferred from values and use.
- The bubble layer's screen-space inputs and the lighting part of the PS.
