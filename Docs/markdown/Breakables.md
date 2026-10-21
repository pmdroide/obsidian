# Breakables

The **Breakable** component breaks a gameobject into real 3D pieces during Play. Use it for a vase knocked off a
table, a pot that gets shot, a crate, or a zombie's head in a shooter.

- When Play starts, the model is cut into **Pieces** in the background. Each piece is a closed chunk of the mesh. The
  faces the cut makes show the **Interior** colour: clay inside a vase, red flesh inside a head.
- A hard enough hit hides the gameobject and removes its collider. The pieces then fly out from the hit as physics
  bodies, with a burst of dust or blood.
- A falling Dynamic gameobject hitting the floor counts as a hit, and so does a body thrown at it. Scripts can also
  break it.
- It can **follow** a bone of another gameobject. A head on a zombie pops, the bone it sat on shrinks away, and the
  body goes limp as a ragdoll.

The breakable glass pane (`Breakable_Glass.md`) is a separate, flat-only component.

## Setting one up

### A vase

GameObject > Effects > **Breakable Vase** adds the built-in **Vase** (`Assets.Vase`, a 41 cm glazed vase with real
walls) with these components:

- a Dynamic **Physics** component (set Mass to about 2 kg);
- a **Breakable** component.

Any closed model works the same way. Add Component > **Breakable** on a gameobject that has a **Physics** component,
so that bullets and bodies can hit it.

Give the vase a Material component for its glaze colour. The shards keep the vase's material on the outside.

### A zombie head

1. Add the character (for example the Y Bot) with an **Animator** and a **Ragdoll**.
2. Add a gameobject with the **Head** model (`Assets.Head`, a 25 cm head, face towards -Y). Place and scale it over
   the character's head in the editor. For the Y Bot: 1.675 m above its feet, scale (1.18, 1.12, 1.12).
3. Give the head a **Static Physics** component, so that rays hit it.
4. Add a **Breakable** component and set:
   - **Effect**: Blood;
   - **Interior**: dark red;
   - **Follow**: drop the character from the Hierarchy;
   - **Bone**: `mixamorig:Head` (the character's Armature in the Hierarchy lists its bones).

During Play the head rides on the bone with the offset it had in the editor. When it breaks:

- **Collapse Bone** shrinks that bone and every bone under it to the joint, so the character's own head vanishes into
  the neck;
- **Ragdoll On Break** makes the body go limp, pushed along the shot.

## Sample

`Content/Scenes/BreakableTest.obsc` (in the scene list, after CollisionTest) has:

- a table of vases;
- a high shelf with vases to push off with **Q**;
- two big urns on the floor;
- three Y Bot zombies shambling in place, each wearing a breakable head.

The player is the CollisionTest first-person player (`CollisionTestPlayerScript`). **Attack** shoots any Breakable
under the crosshair, up to 40 m away.

## Settings

| Setting | Default | |
|---|---|---|
| Pieces | 12 | How many pieces it breaks into (2..64). |
| Impact Speed | 4 m/s | Speed a contact must have to break it. 0 = only scripts break it. |
| Impact Momentum | 2 kg·m/s | Mass × speed of the contact. The mass is the lighter body's share (the reduced mass of the two), or its own mass against something immovable like the floor. A pebble barely counts against a heavy urn. |
| Break Force | 2.5 m/s | Speed the pieces fly apart with. Pieces near the hit go faster, and hits add some of their own speed. |
| Debris Lifetime | 10 s | The pieces shrink away at the end. The scene keeps at most 200 (`FractureShatter.MaxPieces`); the oldest go first. |
| Density | 1500 kg/m³ | Mass of the pieces when the gameobject isn't Dynamic. A Dynamic gameobject shares its own mass out by volume. |
| Interior | clay | Colour of the faces the break cuts. A see-through (Forward Shaded) material stays itself inside. |
| Effect | Dust | **None**, **Dust** (a puff and grit) or **Blood** (a spray, a red mist and splats on the floor below). Switching between Dust and Blood swaps the default colour. |
| Effect Color / Amount | | Colour of the dust or blood, and a scale on the particle count. |
| Follow / Bone | | Gameobject (and bone) to ride on during Play. Empty Bone = the gameobject itself. |
| Collapse Bone | on | When it breaks, the followed bone shrinks away. |
| Ragdoll On Break | on | When it breaks, the followed gameobject's Ragdoll goes limp. |

## Scripts

```csharp
var breakable = hit.GameObject?.GetComponent<BreakableComponent>();
breakable?.Break(hit, ray.Direction);                 // a bullet: pieces fly from the hit along the ray
breakable?.Break(point, direction, force: 6f);        // harder
breakable?.Break();                                   // from its middle
breakable.Broken += (point, pieces) => PlaySound("Audio/smash.wav");
breakable.Restore();                                  // whole again (during Play)
bool gone = breakable.IsBroken; int left = breakable.PieceCount; bool ready = breakable.IsReady;
```

A break in the first moments of Play waits for the background fracture to finish (usually 0.1-0.2 s per model).

## How it works

- **The cut** (`Engine/Physics/MeshFracture.cs`). Pure math, checked in `Tests/Components/BreakableChecks.cs`.
  - The sites are random points on the surface, pushed up to a fifth of the smallest side inwards. That gives
    shell shards for a hollow vase and wedges from the skin to the middle for a solid head.
  - Each piece is the mesh clipped by the bisector planes between its site and every other site.
  - Every cut is closed by a cap. The cut segments are welded into loops on the plane, sorted into outlines and holes
    by how deeply they nest, holes are bridged in, and the result is ear-clipped. A vase cut across its walls gets a
    ring.
  - Caps are wound like the mesh: the sign of the mesh's volume tells which way it faces. The pieces of a closed mesh
    add up to its volume exactly.
- **The runtime** (`Engine/Physics/FractureShatter.cs`).
  - It reads the model's vertex and index buffers when Play starts and runs the fracture on a worker thread.
  - On a break, each piece becomes a dynamic convex body (up to 64 hull points) and one mesh per material. The pieces
    of one gameobject don't collide with each other. A head's pieces also ignore the body they rode on.
  - Contacts reach it from `ScenePhysics.UpdateTouches` and break it after the step.
- **Bone collapse** (`Engine/Animation/BoneCollapse.cs`). The Animator, the Ragdoll and posed bones pass their skin
  matrices through it before skinning.
- **Dust and blood** (`Engine/Particles/BreakBursts.cs`). Engine-owned particle systems like the water splashes,
  drawn by the particle pass.
- **Models**. `Content/GameObjects/Vase/Source/generate_vase.cs` and `Content/GameObjects/Head/Source/generate_head.cs`
  (`dotnet run generate_vase.cs` in that folder) write the `.obj` files built by `Content.mgcb`.

## Limitations

- Models should be **closed**. An open mesh, such as a character body with holes, still breaks, but some cuts get no
  cap, so you can see into the pieces.
- The pattern is fixed when Play starts. It doesn't get finer around the hit, and pieces don't break again.
- Pieces don't collide with each other, and their colliders are convex hulls. A curved shard's collider covers its
  hollow, so shards lie a little proud of the floor.
- Blood splats are flat particles on the floor below. They are not decals, so they don't show on walls.
- There are no sounds. Hook `Broken` from a script to play one.
- The followed character's head goes away by shrinking the bone. The character's own mesh isn't cut.
