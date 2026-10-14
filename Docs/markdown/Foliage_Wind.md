# Trees and foliage wind

Obsidian ships a built-in **Tree** model, a broadleaf tree about 8 m tall. The **Foliage Wind** component
makes a tree sway in the scene wind:

- the trunk leans and bends with gusts,
- the limbs bob and swing,
- the leaves flutter.

Leaves also let sunlight through, so a crown seen against the sun glows. Try it in the `FoliageTest` sample
scene (`Content/Scenes/FoliageTest.obsc`): a grove of six trees in a 5 m/s breeze, with one tree sheltered
from the wind. Fly around with the free camera, and change Environment > Wind Speed and Wind Direction to
see the trees react.

The wind runs in the editor viewport and in Play, and the pause menu holds it. All of the motion happens in
the vertex shaders. Shadows, picking and the selection outline move with the tree. Physics colliders stay at
the rest pose.

## Adding a tree

1. Use **GameObject > Nature > Tree** (or **Tree** in the Hierarchy "+" menu). It creates the built-in Tree
   with a Foliage Wind component, and it sways right away.
   - Alternatively, drag `GameObjects/Tree/tree.obj` from the Assets panel (model key `Tree`, also usable with
     `Spawn("Tree", position)`), then add **Inspector > Add Component > Foliage Wind**.
   - The tree comes with its own bark and leaf materials. Don't add a Material component: it would apply one
     surface to both parts.
2. Rotate and scale trees so a grove doesn't look copied. Each tree also sways out of step with its
   neighbours (the phase comes from its position), and gusts roll downwind across the scene.

### Foliage Wind settings

| Setting | Default | What it does |
|---|---|---|
| Trunk Bend | 1 | How far the whole tree leans and sways (0 = a rigid trunk). |
| Branch Sway | 1 | How much the limbs bob and swing; more towards their ends. |
| Leaf Flutter | 1 | How much the leaves flap. Only materials with Shader **Foliage** flutter. |
| Wind Scale | 1 | Multiplies the scene wind for this tree: < 1 for a sheltered tree, > 1 for an exposed one. |
| Min Wind (m/s) | 1 | Wind that always reaches the tree, so it never freezes in a calm scene. |
| Speed | 1 | Speeds up or slows down the motion. Taller trees already sway slower. |
| Height (m) | 0 | Tree height in model units, which scales the bending. 0 = the top of the model's bounds. |

The wind is Environment > **Wind Speed** (m/s) and **Wind Direction**, the same wind that blows rain, cloth
and particles. 6 m/s is "strength 1". The default 4 m/s is a gentle breeze, and 15+ m/s is a storm. Stronger
wind bends further and also moves everything faster.

## Your own trees, bushes and grass

The component works on any model. The shader only needs the vertex positions, so the model must have:

- **its base at the origin, Z up.** Bending grows with height above the origin, and branch sway with distance
  from the vertical axis through it.
- **leaves on a material with Shader = Foliage** (Inspector > Material > Shader). Foliage materials are
  alpha-cut through their **Mask** texture (0.49 threshold), flutter, and let sunlight through. Bark and other
  parts keep their usual shader and only bend and sway.
- **leaf cards with the stem at the bottom of the texture** (texture v = 1). Flutter fades to nothing there,
  so leaves stay attached to their twigs. Optional: offset each card's U by a whole number (0, 1, 2, ...).
  Sampling wraps, so the texture doesn't change, but each card then flutters with its own phase.
- **double-sided leaves as two faces** with opposite normals at the same positions. Both faces move as one.

## How it works

`Content/Shaders/Common/FoliageWind.fx` is included by the G-buffer (`Gbuffer.fx`), the shadow maps
(`ShadowMap.fx`) and editor picking (`IdRender.fx`). After the vertex is moved to world space,
`ApplyFoliageWind` adds three layers, loosely after Crytek's vegetation bending (GPU Gems 3, ch. 16):

1. **Branch sway:** limbs bob vertically and swing downwind. The amount grows towards the ends of the limbs
   (distance from the trunk axis) and up the crown. The phase comes from the position, so waves run through
   the crown.
2. **Leaf flutter** (Foliage material only): each card flaps along its normal and tilts its shading normal,
   so the light shimmers. It fades towards the card's stem. The phase comes from `floor(u)` and the card's
   normal. A canonical normal makes both faces of a card move the same way.
3. **Trunk bend:** the whole tree leans downwind with gusts and a little cross-wind sway, growing with
   height². Each vertex keeps its distance to the trunk base, so the crown arcs over instead of stretching.

All waves are smoothed triangle waves of the tree's wind clock. `FoliageWindComponent.UpdateAll` advances that
clock every frame (`MainSceneLogic.Update`), faster in stronger wind. Every frequency is a multiple of
1/1000 Hz, so the clock wraps at 1000 s without a jump.

Per draw, `MeshMaterialLibrary.Draw` sets the wind of the instance (`TransformMatrix.Wind`) and of the
material (`WindLeaf`) through `FoliageWindParameters` (`Engine/Renderer/Helper/FoliageWind.cs`). A swaying
tree flags itself as moved every frame, which keeps its shadows current. It costs the shadow-map redraw that
cloth and animated characters also cost.

**Translucency:** Foliage pixels store material type 8 in the G-buffer. `DeferredDirectionalLight.fx`
(`FoliageTransmission`) adds the sun's light through the back of the leaf, brightest when you look towards
the sun, and still shadow-tests those pixels, so inner leaves shaded by outer ones stay dark. Point lights
don't add translucency.

**Shadows:** directional shadow maps now alpha-test masked materials (`DrawLinearDepthAlpha`). Leaves cast
dappled shadows instead of squares. Shadow passes and picking bend and sway the tree, but they have no UVs,
so they don't flutter, except the alpha-tested shadow passes, which do.

## The Tree model

`Content/GameObjects/Tree/` holds `tree.obj` (+ `tree.mtl`) and five textures:

- `bark_albedo` and `bark_normal`: tileable bark,
- `leaves_albedo`, `leaves_mask` and `leaves_normal`: a sprig of nine leaves.

`Source/generate_tree.py` generates all of them (Python 3 + numpy + Pillow, fixed seed). Change the shape
(limbs, twigs, card density) or the leaf painting there and re-run it. Content.mgcb already lists the outputs.

| Part | Contents |
|---|---|
| Bark | Trunk with root flare, 12 limbs, ~100 twigs; ~8.8k triangles |
| Leaves | ~1500 double-sided cards (2 × 2 quads each, cupped and drooping); ~24k triangles |

`Assets.LoadTree` binds `TreeBarkMaterial` and `TreeLeavesMaterial` (Foliage, mask + normal map, roughness 0.8)
to the two parts and widens the bounds by 15% for the sway.

## Limits

- One wind per scene (the Environment wind). There is no local wind zone.
- The wind is visual: physics, raycasts and the CPU bounds use the rest pose.
- A Material component on a tree replaces the surface of both the bark and the leaves.
- Moving leaves have no motion vectors, so TAA can smear them a little in strong wind, like cloth and
  animated characters.
