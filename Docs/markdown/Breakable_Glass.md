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
2. Give it a glass material: Material > Shader **ForwardShaded**, Transparent, Opacity ~0.3, Roughness ~0.03. Once
   cracked, the panels show a damaged version of it and the broken edges an edge material (see Broken look).
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

### Broken look

Like Half-Life: Alyx's `shatterglass` materials, the intact pane uses the gameobject's material, and cracked glass
uses two others:

| Setting | Default | |
|---|---|---|
| Damaged | Built-in | Material asset (`.mat`) for the faces of the cracked panels and falling shards. Built-in: the pane's own material with fine crazing, faint scratches and frost (a pane with its own albedo texture keeps it), slightly rougher. |
| Edge | Built-in | Material asset for the broken edges: their sides and a chipped bevel along every crack on both faces, so the cracks show from the front. Built-in: greenish glass, less clear than the faces, with rippled hackle bands. Edges on the pane's border (in the frame) keep the damaged material. |
| Edge Width | 0.003 m | Width of the bevel on each face. It is narrowed on thin shards, and 0 gives square edges. |
| Edge Tex. Size | 0.508 m | One repeat of the edge texture along an edge (Valve's `shatterglass_edge_uv_scale` 0.05 per inch). The texture's U runs along the edge and its V across the glass's profile. |

The materials are made at the first crack, so changes during Play show after **Restore** or on the next Play. A
material asset for the edge should be one that reads well on a narrow strip. A transparent glass material with a
texture whose alpha varies is a good choice, because the alpha scales the opacity.

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
  - texture coordinates fitted to the model's front face;
  - each shard is a bevelled prism split into two mesh parts: faces with the damaged material, cracked sides and
    bevels with the edge material (`GlassFracture.Inset` moves every side in by Edge Width).
- `Engine/Physics/GlassTextures.cs` paints the built-in damaged and edge textures once per graphics device (no
  content files).
- `Engine/Components/BreakableGlassComponent.cs` holds the settings and the Play lifecycle, and turns contacts into
  Blunt hits. `ScenePhysics.CreateGlass` registers the pane like a ragdoll; contacts reach it in `UpdateTouches`, and
  hits apply after the contacts are read.
- Anvil: `BreakableGlassComponentViewModel` and its Inspector template.
- Checks: `Tests/Components/GlassChecks.cs`.

## Limitations

- No sounds or particles on breaking: hook `Shattered` from a script.
- Only flat panes. A curved model breaks as its flat bounding slab.
- The pane is set up when Play starts. Moving it during Play doesn't move panels that already cracked.
- Debris shards don't collide with each other.
