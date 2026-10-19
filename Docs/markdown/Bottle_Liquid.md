# Liquid in bottles

The **Liquid** component fills a bottle, glass or jar with liquid the way Half-Life: Alyx does. The liquid:

- stays level as the container tilts, lies on its side or turns upside down, following the container's volume;
- leans and lags behind when the container moves, wobbles and ripples when it's shaken, then settles over 20 s;
- climbs the glass at the edges (meniscus), shows a bright water line, foams and fizzes.

Try it in the `LiquidTest` sample scene (`Content/Scenes/LiquidTest.obsc`). Six bottles stand on a table: one is
swung like a carried bottle, one is knocked sideways every 3 s, and one is tipped over until it's empty. The rest
are tilted, on their side and upside down. Press Play to see them move.

It runs in the editor viewport and in Play, and the pause menu holds it. Move a bottle with the gizmo and it sloshes.
It's visual only: physics don't change.

How Alyx does it, and what the engine copies: [`Docs/Research/BottleLiquid/README.md`](../Research/BottleLiquid/README.md).
In short, there's no fluid simulation. The liquid is a plane, and the container's own glass shows it below the
plane.

## Adding a bottle

1. Use **GameObject > Effects > Liquid Bottle** (or **Liquid Bottle** in the Hierarchy "+" menu). It creates the
   built-in Bottle (0.30 m, clear glass) with a Liquid component.
   - Or drag the `Bottle` model in, or use any other model, then **Inspector > Add Component > Liquid**.
2. Set **Fill**, **Liquid Color** and **Opacity** (water ~0.3, wine ~0.9, milk 1).
3. Move it, or press **Shake** in the Inspector, to see it slosh.

Your own containers need:
- a material with **Shader Forward Shaded** (glass); the liquid is drawn by the forward pass;
- a **closed** mesh with outward faces. The fill is measured from its volume. An open mesh falls back to its
  bounds, which is fine for a straight jar but not for a bottle with a neck;
- the container's **+Z axis** along its length (upright = +Z up). The side table is measured with +X up, which
  is exact for round containers.

## Settings

| Setting | Default | What it does |
|---|---|---|
| Fill | 0.6 | 0 empty, 1 to the brim, by volume. |
| Liquid Color / Foam Color | amber / cream | Colour of the liquid, and of its foam and bubbles. |
| Opacity | 0.75 | How much it hides what's behind it. It gets denser at grazing angles. |
| Roughness | 0.1 | Roughness of the liquid's top. |
| Emissive | 0 | Glow (potions, chemicals). |
| Sensitivity | 1 | How easily motion shakes it (1 = Alyx, 0.5 = syrup, 2 = very runny). |
| Settle Time (s) | 20 | Time a shaken liquid takes to calm down (Alyx). |
| Lean | 1 | How much the surface leans while it moves (Alyx's `prop_liquid_gravity_velocity_scale`). |
| Wobble / Wobble Speed | 0.02 / 1 | Size and speed of the wobble and ripples (Alyx's material defaults). |
| Meniscus | 1 | How far the surface climbs the glass at its edges. |
| Water Line | 1 | Brightness of the line under the surface. |
| Foaminess | 1 | How much foam shaking makes (0 = none, 1 = Alyx, 3 = beer). |
| Foam Time (s) | 30 | Time the foam and bubbles take to go (Alyx). |
| Fizz | 0.3 | Rising bubbles (0 = still, 1 = fizzy). Shaking adds more. |

## Scripts

```csharp
var liquid = GetComponent<LiquidComponent>();
liquid.Fill = Math.Max(liquid.Fill - DeltaTime * 0.2f, 0); // drink / pour
liquid.Agitate(3);          // a knock, as a 3 m/s change of speed
liquid.Settle();            // calm at once
if (liquid.Agitation > 0.5f) { /* sloshing sound */ }
```

`Agitation` and `FoamAmount` (0..1) read how shaken it is. Reference: `Content/Scripts/LiquidTestScript.cs`
(**Liquid Test**: Swing, Jolt, Pour).

## How it works

- `Engine/Components/LiquidComponent.cs`: settings. `UpdateAll` runs every frame from `MainSceneLogic.Update`
  (Edit and Play, dt 0 while paused) and puts the state on `TransformMatrix.Liquid` / `LiquidLook`.
- `Engine/Physics/LiquidSlosh.cs`: Alyx's server code. Velocity and angular velocity come from the change of the
  transform. Every 0.1 s, `pow(Δv + Δω, 0.6)` adds to an agitation and a foam peak that decay linearly over the
  settle times. The down direction is `normalize(gravity − velocity − angular velocity)` in Source units, eased
  20 % per 0.1 s. Then the shader's per-instance part: the down direction wobbles at ~21 rad/s with the agitation,
  and the surface height comes from the fill and the tilt.
- `Engine/Physics/LiquidVolume.cs`: the fill tables, measured once per model. The heights where 0, 1/8 … 1 of the
  volume lies below the surface, upright, on the side and upside down, blended by the tilt as Alyx does.
- `Content/Shaders/Forward/Forward.fx` (`UseLiquid`): ripples (Alyx's wave sum), meniscus, the soft liquid edge,
  water line, foam band and bubbles on the glass below the plane. `MeshMaterialLibrary` first draws a liquid
  container from the inside (`LiquidBackFace`): where the view ray crosses the surface inside the container, that
  pixel shows the liquid's top.
- Built-in model: `Content/GameObjects/Bottle/bottle.obj` (+ `Assets.BottleGlassMaterial`), made by
  `Source/generate_bottle.cs` (`dotnet run generate_bottle.cs`).

Limits:
- The camera's near plane is 1 m, so a bottle closer than that is clipped (see ISSUES.md).
- The liquid isn't in the shadow maps or in mirror images (like the glass).
- Very non-round containers on their side use the +X side table whatever side is down.
