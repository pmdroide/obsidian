# Breakable Glass

Windows that crack and shatter like Half-Life: Alyx's `func_shatterglass`:

1. The first hit cracks the whole pane into Voronoi panels.
2. A hole opens where it was hit and its shards fly off.
3. Panels no longer joined to the frame fall.
4. Later hits break the panels that are left.

Thrown bodies break it on impact and fly on through. Scripts shoot it.

How Valve does it, and what was copied: `Docs/Research/BreakableGlass/README.md`.

## Setting up a window

1. Add a flat gameobject, e.g. a **Cube** scaled `1.2 x 0.012 x 1` (a 2.4 x 2 m pane, 2.4 cm thick). The thinnest
   local side is the pane's normal.
2. Give it a glass material: Material > Shader **ForwardShaded**, Transparent, Opacity ~0.3, Roughness ~0.03. The
   shards draw with this material.
3. Add a **Physics** component (**Static**), so that things can hit it.
4. Add Component > **Breakable Glass**.

The sample is the window station of `Content/Scenes/CollisionTest.obsc`, west of the spawn. Shoot the windows with
**Attack**, or carry a crate and throw it (**F**).

## Settings

| Setting | Default | |
|---|---|---|
| Crack Size | 0.381 m | Size of the panels of the first crack (Valve: `shatterglass_voronoi_size` 15 in). A pane narrower than one cell stays one panel. |
| Hole Radius | 0.06 m | Radius of a bullet hole. Thrown bodies punch a hole the size of their bounding sphere. |
| Impact Speed | 4 m/s | Speed a body must hit with. 0 = only scripts break it. |
| Impact Momentum | 2 kg·m/s | Mass × speed a body must hit with. |
| Debris Lifetime | 15 s | Valve's `shatterglass_shard_lifetime`. The scene keeps at most 150 falling shards (`GlassShatter.MaxDebris`); the oldest go first. |
| Hit Tolerance | 2 | A hit finds a panel within this many times its size around its centre (Valve's `shatterglass_hit_tolerance`). |
| Drop Loose Shards | on | Panels not joined to the border through shared edges fall. |
| Distort | on | Each cracked panel leans slightly, so reflections break up along the cracks. |
| Solid Shards | on | Panels in the frame have colliders: bodies are stopped, and break them when they hit hard enough. |

## Scripts

```csharp
var glass = hit.GameObject?.GetComponent<BreakableGlassComponent>();
glass?.Shatter(hit, ray.Direction);                                    // bullet (Ballistic)
glass?.Shatter(point, direction, GlassStress.Blunt, speed: 3, radius: 0.3f); // melee
glass?.Shatter(point, direction, GlassStress.Explosive, speed: 8);     // blast: the whole pane goes
glass.Shattered += (point, stress, thrown) => Log($"{thrown} shards");
glass.Restore();                                                        // whole again
bool broken = glass.IsBroken; int left = glass.ShardCount, falling = glass.DebrisCount;
```

`Raycast` hits the panels left in the frame and reports the window's gameobject. Falling shards report
`GameObject == null`.

## How it works

- `Engine/Physics/GlassFracture.cs` is the 2D math on the pane (metres, centred): Valve's jittered-grid Voronoi
  (`CrackPane`), radial impact sites, half-plane clipping, the hit test, and frame support through shared edges. It is
  pure and deterministic per seed (the gameobject's ID).
- `Engine/Physics/GlassShatter.cs` is the runtime:
  - the panels in the frame: one mesh on the pane plus a static convex collider each, owned by the gameobject in
    `ScenePhysics`;
  - falling debris: a dynamic convex body plus its own mesh part each, ignoring each other and fading out at the end
    of their life;
  - texture coordinates fitted to the model's front face.
- `Engine/Components/BreakableGlassComponent.cs` holds the settings and the Play lifecycle, and turns contacts into
  Blunt hits. `ScenePhysics.CreateGlass` registers the pane like a ragdoll; contacts reach it in `UpdateTouches`, and
  hits apply after the contacts are read.
- Anvil: `BreakableGlassComponentViewModel` and its Inspector template.
- Checks: `Tests/Components/GlassChecks.cs`.

## Limitations

- Shards use the pane's material on every face. There is no separate damaged material or edge texture yet.
- No sounds or particles on breaking: hook `Shattered` from a script.
- Only flat panes. A curved model breaks as its flat bounding slab.
- The pane is set up when Play starts. Moving it during Play doesn't move panels that already cracked.
- Debris shards don't collide with each other.
