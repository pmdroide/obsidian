# Voxel World

A scene can have a **voxel world**: block terrain generated from a few settings, drawn by the deferred
renderer and solid for physics. It is part of the scene's environment. Turn it on in Anvil under
**Hierarchy > Environment > Voxel World**. It is saved in the `.obsc`, shown in both Edit and Play, and
scripts can dig and build in it during Play.

## Settings

| Setting | Range | Default | What it does |
| --- | --- | --- | --- |
| **Generate a voxel world** | on / off | off | Turns the world on. The settings below are hidden while it is off. |
| **Terrain** | Flat, Hills, Mountains, Islands | Hills | The shape of the generated land (below). |
| **Seed** | any integer | 1337 | The same seed always generates the same world. **New seed** picks a random one. |
| **Size (blocks)** | 32-256, steps of 32 | 128 | Blocks along X and Y. The world is square and split into 32 x 32 x 32 chunks. |
| **Height (blocks)** | 16-128, steps of 16 | 64 | Blocks along Z. |
| **Block size (m)** | 0.25-4 | 1 | Edge length of one block. |
| **Floor Z (m)** | -1000-1000 | -24 | World Z of the bottom of the world. The world is centred on X/Y = 0. |
| **Trees** | 0-1 | 0.5 | How many trees grow on grass. 0 plants none. |
| **Sea level** | 0-0.9 | 0.3 | Water surface as a fraction of the height. 0 means no water. |
| **Caves** | on / off | on | Tunnels under the surface. |
| **Collision** | on / off | on | Static colliders for the chunks: bodies, characters and raycasts hit the blocks. |

Below the sliders the Inspector shows the world's size in blocks and metres. Changing any setting
regenerates the world. While editing this happens on a worker thread, and the old world stays visible
until the new one is ready.

In the scene file:

```json
"Environment": {
  "Voxels": true, "VoxelTerrain": "Hills", "VoxelSeed": 1337,
  "VoxelWorldSize": 128, "VoxelWorldHeight": 64, "VoxelBlockSize": 1, "VoxelBaseHeight": -24,
  "VoxelCaves": true, "VoxelTrees": 0.5, "VoxelSeaLevel": 0.3, "VoxelCollision": true
}
```

The terrain is saved by name. Scenes saved before voxel worlds load without one and with the defaults.
Out-of-range values are clamped on load, and again when the world is built (scripts skip the clamping).

## Terrains

| Terrain | Shape |
| --- | --- |
| **Flat** | One grass level at 35% of the height (at least 3 blocks above the sea). |
| **Hills** | Rolling hills. The default sea fills the deepest valleys as lakes. |
| **Mountains** | Ridged ranges and valleys. Steep slopes are bare stone, and peaks above 70% of the height get snow. |
| **Islands** | Land rising from a shallow sea, highest near the centre. The world's edges are under water. |

Every terrain has the same layers: grass over 3-4 blocks of dirt over stone. Columns near or under the
waterline are sand, cliffs are stone, and the bottom layer is always solid and can't be dug, so nothing
falls out of the world. Caves are tunnels where two 3D noise fields both cross zero. Trees have 4-6 wood
blocks and a rounded crown of leaves, and keep at least 4 blocks apart.

## Blocks

| # | Block | Faces | Notes |
| --- | --- | --- | --- |
| 0 | Air | | Empty. |
| 1 | Grass | grass top, grass-fringed sides, dirt bottom | |
| 2 | Dirt | dirt | |
| 3 | Stone | stone with cracks | |
| 4 | Sand | sand | Shores and the sea floor. |
| 5 | Snow | snow | Mountain peaks. Never gets wet in rain. |
| 6 | Wood | bark sides, ring ends | Tree trunks. |
| 7 | Leaves | leaves | Darken in rain, but don't hold puddles. |
| 8 | Planks | boards | Building block, not generated. |
| 9 | Brick | brick and mortar | Building block, not generated. |
| 10 | Lamp | glowing panel | Emissive. It glows (and blooms), but does not light its surroundings. |

Blocks are stored as one byte each and their numbers are stable: append new ones, never renumber. All
blocks are opaque cubes.

## How it works

`VoxelSystem` (`Engine/Voxels/VoxelSystem.cs`) keeps the active scene's world in step with its
environment once per frame. `MainSceneLogic.UpdatePhysics` calls it before the physics step, also while
the game is paused.

1. **Generate.** When the settings change, `VoxelGenerator.Generate` builds a `VoxelWorld`. It computes a
   height per column for the terrain type, then fills the soil layers, caves and trees. The column passes
   run in parallel, and seeded Perlin noise (`VoxelNoise`) makes it deterministic. While editing, this and
   the meshing run on a worker thread. During Play they run at once, so bodies never fall through missing
   ground: when Play starts, when a script changes a setting, or when a scene loads during Play.
2. **Mesh.** `VoxelMesher.Build` meshes one 32³ chunk with greedy meshing. For each axis and side it finds
   the block faces that border air and merges equal neighbours into rectangles.
   - Render faces merge per surface (grass top, grass side, dirt, ...). Collision faces merge regardless
     of look, so a flat field collides as a handful of large quads.
   - The underside of the world is never meshed. Faces between neighbouring chunks are hidden in both.
   - Triangles wind clockwise seen from outside, like the engine's models. That is what back-face culling
     and BEPU's one-sided triangles expect.
   - UVs are in blocks, so each block shows its texture once. Side faces run their texture top-up.
3. **Draw.** Each chunk has one vertex and index buffer. Each of its surfaces is registered in the
   `MeshMaterialLibrary` as a `ModelMeshPart` range with the surface's material, like a model's mesh
   parts. So the G-buffer, every shadow map, the environment capture and editor picking draw them like
   any model. Chunks use ID 0, so clicking one selects nothing. Vertices are in world space (identity
   transform). A new or remeshed chunk flags its transform as changed for one frame, so shadow maps
   redraw.
4. **Look.** `VoxelTextures` paints each surface in code: a 16 x 16 pixel-art pattern scaled up to 64²
   texels with mipmaps, plus a normal map that embosses the pattern.
   - Every surface except the lamp uses the **Wet Surface** material type (see
     [Material_Component.md](Material_Component.md#wet-surfaces)). Rain soaks the blocks and fills
     puddles on top faces, and all blocks get the same view-dependent ambient light.
   - The lamp is **Emissive**.
   - Roughness, emboss strength and rain response per surface are in `VoxelSurfaceStyle`.
5. **Sea.** With a sea level, one unit quad, scaled over the world, is drawn by the water pass (which
   subdivides it for waves). It sits 0.15 block above the tops of the highest underwater blocks.
6. **Collide.** Each chunk with faces gets one static BEPU triangle mesh. Chunk colliders have no
   gameobject: raycasts hit them with `RaycastHit.GameObject == null`, and they raise no collision events.
7. **Edit.** `VoxelWorld.SetBlock` marks the chunk dirty, and also its neighbour when the block is on a
   chunk border. Before the next physics step `VoxelSystem` remeshes the dirty chunks and rebuilds their
   colliders. **Stop regenerates a world that Play changed**, like every other Play change, and the
   environment itself is restored by `PlayModeController`.

Measured on the sample's 128 x 128 x 64 hills: 24-32 chunks with faces, about 55 000 triangles, about
110 draw parts, built in about 100 ms. A 96-high Mountains world of the same size has about 110 000
triangles and builds in about 300 ms.

## Scripts

`ScriptBehaviour.Voxels` is the scene's `VoxelWorld`, or `null` when it has none. During Play, a world
that isn't built yet is built when it is asked for, so a script can use it in `Start`.

```csharp
VoxelWorld world = Voxels;
if (world != null && world.Raycast(CameraRay, 8, out VoxelHit hit))
{
    world.SetBlock(hit.Block, VoxelBlock.Air);          // dig the block in view
    world.SetBlock(hit.Adjacent, VoxelBlock.Brick);     // or build against the face in view
}
world.FillSphere(position, 3, VoxelBlock.Air);          // a crater (radius in metres)
world.TryGetSurface(position, out Vector3 ground);      // the top of the column under a point
```

| Member | What it does |
| --- | --- |
| `GetBlock(x, y, z)` / `GetBlock(Vector3)` | The block at a cell or world point. Air outside the world. |
| `SetBlock(cell, block)` / `SetBlock(Vector3, block)` | Changes one block. Returns false outside the world, in the bottom layer, or when nothing changes. |
| `FillBox(a, b, block)`, `FillSphere(center, radius, block)` | Bulk edits. Return how many blocks changed. |
| `Raycast(origin, direction, maxDistance, out VoxelHit)` | A grid walk (no physics). `Block`, `Adjacent` (the empty cell in front of the face), `Normal`, `Point`, `Distance`, `Type`. |
| `WorldToBlock`, `BlockCenter`, `BlockToWorld`, `BlockBounds` | Converts between metres and cells. |
| `SurfaceHeight(x, y)`, `TryGetSurface(Vector3, out top)` | The highest solid block of a column. |
| `SizeX/Y/Z`, `BlockSize`, `Origin`, `SeaLevel`, `Bounds`, `Settings` | The world's layout. |
| `Version`, `EditCount` | Change counters. |
| `IsEditable(cell)` | False outside the world and in the bottom layer. |

Changing a voxel setting from a script (`SceneEnvironment.VoxelSeed = 42`) replaces the world, so fetch
`Voxels` every frame instead of keeping it. `GameFlow.SceneLogic.Voxels` (`VoxelSystem`) has the stats:
`VisibleChunkCount`, `TriangleCount`, `ColliderTriangleCount`, `DrawPartCount`, `LastBuildMilliseconds`
and `Generation`.

## Sample scene: `Scenes/VoxelTest.obsc`

The last entry in the scene list. It is a 128 x 128 x 64 world of hills (seed 1337) with caves, trees and
lakes under the day/night sky (10:00, 20-minute cycle). The **Player** capsule (Dynamic, Freeze Rotation)
runs **Voxel Test** (`Engine/Content/Scripts/VoxelTestScript.cs`). It starts on the surface at the world's
centre, and its HUD is `Content/UI/VoxelTest.xml` + `.css`.

| Key | Action |
| --- | --- |
| Left mouse | Dig the block under the crosshair (hold to keep digging) |
| Right click / F | Build the picked block against the face in view (not where you stand) |
| 1-9, 0 / wheel | Pick grass, dirt, stone, sand, snow, wood, leaves, planks, brick or lamp |
| B | Blast a crater (**Blast Radius**, 3 m) |
| G | Throw a physics ball (at most 12) |
| V | Fly: Space up, Ctrl / C down |
| N | New world from a random seed |
| T | Time of day +3 h |
| R | Reload the scene |
| Hold right mouse, arrows, WASD, Shift, Space | Look, move, run, jump |

**Reach** (2-12 m, default 7) and **Blast Radius** are Inspector fields on the script. The block under the
crosshair is framed by four glowing bars (runtime-spawned cubes).

## Limitations

- **Edits aren't saved.** There is no in-editor sculpting, and Play edits are undone on Stop. The world is
  always what its settings generate.
- **No block lighting.** Lamps glow but don't light anything, and there is no per-block ambient occlusion
  (SSAO still darkens corners). Caves get sky ambient light, and rain falls through roofs, as it does
  everywhere.
- **Opaque cubes only.** No glass, water blocks, slabs or other shapes. The sea is one flat quad: it has
  no buoyancy, and characters walk on the sea floor.
- **The whole world regenerates** on any setting change. Script edits only remesh the touched chunks.
- **NPC navigation ignores voxels.** `NavGrid` takes its extent from static gameobjects.
- **One-sided colliders.** A body that ends up inside a block (a block built on top of a ball) falls
  through it.
- **Shadows** only reach as far as the scene's sun `ShadowSize`. The sample uses 150 m for its 128 m world.
- At most 256 x 256 x 128 blocks (8 M cells, up to 256 chunks). Draw calls grow with the number of
  chunks times the surfaces in each.

## Where the code lives

| Area | Files |
| --- | --- |
| Settings | `Engine/Logic/EnvironmentSettings.cs` (`Voxel*`), `VoxelWorldSettings` in `Engine/Voxels/VoxelWorld.cs` |
| Blocks and the grid | `Engine/Voxels/VoxelBlock.cs`, `Engine/Voxels/VoxelWorld.cs` |
| Generation | `Engine/Voxels/VoxelGenerator.cs`, `Engine/Voxels/VoxelNoise.cs` |
| Meshing | `Engine/Voxels/VoxelMesher.cs` |
| Textures and materials | `Engine/Voxels/VoxelTextures.cs` (`VoxelSurfaceStyle`) |
| Runtime (draw, collide, sync) | `Engine/Voxels/VoxelSystem.cs`, `MainSceneLogic.Voxels` / `UpdatePhysics` / `OnSceneChanged`, `MeshMaterialLibrary.Unregister` |
| Scripts | `ScriptBehaviour.Voxels` |
| Anvil | `Editor/Anvil/Models/EnvironmentViewModel.cs`, Environment panel in `Editor/Anvil/Views/MainWindow.axaml` |
| Sample | `Engine/Content/Scenes/VoxelTest.obsc`, `Engine/Content/Scripts/VoxelTestScript.cs`, `Engine/Content/UI/VoxelTest.*` |
| Checks | `Tests/Components/VoxelChecks.cs` (`--graphics` uploads a world and checks the culling on the GPU) |
