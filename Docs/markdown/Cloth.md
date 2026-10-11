# Cloth

The **Cloth** component turns a gameobject's mesh into cloth during Play. Its vertices become particles
that fall, flutter in the scene's wind and collide with the Physics colliders around them. A player can
walk through a curtain, and a sheet drapes over a table. Try it in the `CollisionTest` sample scene, at
the east side of the arena.

Outside Play the mesh shows at rest. Stopping Play puts it back.

## Making a cloth

1. Add a gameobject with the **Cloth** model (`Assets.Cloth`, `Content/GameObjects/Cloth/cloth.obj`).
   The model is a 2 x 2 m plane in the model's X/Z, subdivided into 24 x 24 quads. It is double-sided:
   the front faces -Y and the back faces +Y.
2. Size it with the gameobject's Scale (X is the width, Z the height) and turn it with its rotation. To
   lay a sheet flat, rotate it -90° about X.
3. Add **Inspector > Add Component > Cloth**. A Material component sets its colour.

Any other mesh also works, as long as it is subdivided enough to bend. Vertices that share a position
(the two sides of a double-sided mesh, UV seams) become one particle.

| Setting | Effect |
| --- | --- |
| **Pin** | Which particles hold still and follow the gameobject. **TopEdge**: the highest row in the model's Z (a curtain). **TopCorners**: the two ends of that row (washing on a line). **Border**: the outline (a trampoline or tent roof). **None**: the whole cloth falls. |
| **Mass** | The whole cloth, in kg. It is shared by the free particles. Heavier cloth stretches a little more and is pushed less by the wind. |
| **Stiffness** | How well it keeps its size: 1 doesn't stretch, 0 is elastic. |
| **Bend** | How much it resists folding: 0 is silk, 1 is cardboard. |
| **Damping** | Share of its speed lost per second, on top of air drag. |
| **Friction** | Grip on colliders. 0 slides off everything. |
| **Thickness** | The gap in metres that particles keep from colliders. |
| **Wind** | Multiplies the scene's wind (Environment > Wind Speed and Wind Direction, and the weather's minimum wind). 0 is still air. Air drag applies either way. |
| **Collisions** | Stay out of Physics colliders. |

Pinned particles move with the gameobject, so a script can carry a banner around or slide a curtain
along its rail. A new Scale rebuilds the cloth at its new size.

## Collisions

Each frame the cloth collects every collider near it from the physics scene
(`ScenePhysics.CollectClothColliders`):

- **Dynamic bodies, ragdoll parts and convex statics** are their convex hull (or box). A particle inside
  one, or closer than Thickness, is pushed out through the nearest face.
- **Static meshes** (floors, walls, Static Physics components) are their triangles. A particle keeps to
  the side of each triangle it was on at the start of the step. This is two-sided, so open meshes such
  as a single plane work too. A fast particle is caught where its path crossed the triangle.
- **Triggers** and the cloth's own gameobject are ignored. Water has no collider.

Friction is Coulomb-like: the slide along a surface shrinks by up to Friction times how far the particle
was pushed. A moving body drags the cloth along with it, which is how the player carries the curtain
aside. Contacts are inelastic, so cloth never bounces off a collider.

The coupling is one-way, as with Unity's Cloth: bodies push the cloth, but the cloth never pushes bodies
back. Raycasts don't hit cloth, and the cloth doesn't collide with itself.

## Scripts

```csharp
var cloth = GetComponent<ClothComponent>();
cloth.AddImpulse(new Vector3(0, 3, 0), hitPoint, radius: 0.5f); // a gust or a hit, kg*m/s
cloth.ResetCloth();                                              // back to rest on the gameobject
foreach (BasicEntity other in cloth.Touching) { ... }            // what it touched last frame
```

`ClothComponent.Cloth` is the simulation during Play (`null` otherwise). It exposes `Positions`
(world space), `Velocities`, `Bounds`, `ContactCount`, `TopSpeed`, `SetPin(...)` and `SetMass(...)`.
Inspector edits to Pin and Mass apply immediately during Play; the other settings are read every frame.

## The sample

East side of `Content/Scenes/CollisionTest.obsc`:

| Gameobject | What it shows |
| --- | --- |
| **Curtain** (between Curtain Post West/East, under the Curtain Rail) | A 2.5 x 2.2 m velvet curtain pinned at its top edge in a doorway, swaying lightly in the scene wind (Wind 0.15). Walk through it: it slides over the player and falls back behind them. |
| **Cloth Crate** | A 15 kg crate in front of the doorway. Carry it (hold left mouse) and throw it through the curtain. |
| **Drape Sheet** on the **Drape Table** | A 1.6 m linen sheet lying flat (Pin None). On Play it falls onto the 1 m table and drapes over its sides. |

Both cloths run the **Cloth Reporter** script (`Content/Scripts/ClothReporterScript.cs`). It posts to
the HUD feed when something starts touching them, for example "Player pushed into the Curtain" or
"Drape Sheet landed on Drape Table". The Ground is ignored.

## How it works

- `ClothTopology` (`Engine/Physics/Cloth.cs`) welds the vertices into particles. It keeps each distinct
  triangle once and makes every triangle edge a stretch constraint. The far corners of two triangles
  sharing an edge become a bend constraint. Edges used by one triangle are the border.
- `Cloth` runs extended position-based dynamics in 10 substeps per frame, with one solver pass each.
  Gravity, then quadratic air drag and gusting wind per triangle, then the constraints, then collisions.
  Stiffness and Bend map to compliances, so the result doesn't depend on the frame rate. Edges resist
  being pushed together only as weakly as Bend does, so cloth buckles instead of springing back.
- `ClothMeshInstance` (`Engine/Physics/ClothMeshInstance.cs`) gives every vertex buffer of the model a
  dynamic copy. Each frame it writes the particles back in model space, with recomputed normals and
  tangent frames, matched to the facing and handedness the content pipeline stored.
  `TransformMatrix.Cloth` makes `MeshMaterialLibrary` draw that copy in every pass (G-buffer, shadows,
  forward, ID and outlines), the same way a skinned mesh is drawn.
- `PhysicsSystem.CollectClothColliders` reads BEPU's broad phase and turns hulls and boxes into
  world-space planes, and mesh triangles inside the query bounds into `ClothTriangle`s.
- The model's bounding sphere is doubled in `Assets`, so a swinging cloth isn't culled.

## Limitations

- One-way coupling: the cloth never pushes or slows bodies, and a body standing on cloth sinks through it.
- No self-collision, no tearing, and no per-vertex pin painting (only the pin modes above).
- Collision with spheres and capsules uses their hull. A static mesh moved by a script (the vault gate)
  only stops particles that are moving, not ones it sweeps into.
- Colliders are read from the previous physics step.

## Checks

`Tests/Components/ClothChecks.cs` covers:

- welding and pins;
- a cloth swinging down from its pins without stretching;
- a sheet dropped on its edge settling, a sheet draping over a table, and a body walking through a curtain;
- wind, impulses and the collider query;
- the component and its Inspector, saving and cloning;
- a scripted Play run of the sample, in which the player walks through the curtain.

With `--graphics` it also loads the real model, checks the written normals and tangent frames, and
renders the cloth from both sides and after it falls. Run
`dotnet run --project Tests\Components\Components.csproj`.
