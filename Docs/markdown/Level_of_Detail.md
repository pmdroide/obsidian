# Level of detail (LOD)

The **LOD** component draws simpler models as the camera moves away from a gameobject, and can stop
drawing it entirely beyond a cull distance. Up close the gameobject draws its own model (level 0). Each
level you add swaps in another model from its distance on. Try it in the `LodTest` sample scene
(`Content/Scenes/LodTest.obsc`): a row of spheres turns into cubes at 20 m and disappears at 50 m. Fly
toward them with the free camera and watch them switch.

LOD works in the editor viewport and in Play. It changes only what is drawn: shadows, the G-buffer,
forward/water passes and viewport picking all use the current level. Physics colliders, SDFs, bounds and
the selection box keep using the gameobject's own model.

## Setting it up

1. Select a gameobject and use **Inspector > Add Component > LOD**. It starts with one empty level at 20 m.
2. For each level, drag a mesh from Assets into **Model** and set
   **From (m)**, the camera distance at which it starts to draw. **Add Level** adds another level twice as
   far as the farthest one; **Remove** deletes a level. The slot's clear button removes its model.
3. Optionally set **Cull Distance**: from there on nothing draws. 0 never culls.

Levels can be listed in any order; they are sorted by distance. A level with no model, an unknown model
or a distance of 0 or less is skipped, and the console says why.

The LOD models use the gameobject's material, including a Material component's settings, so make them
with the same UV layout as the full model. Model them at the same size and pivot as the full model: the
gameobject's transform applies to every level.

## How the level is chosen

- The distance is measured from the camera to the centre of the gameobject's own model bounds.
- The level is the last one whose distance the camera has passed; past **Cull Distance** it is culled.
- Switching back to a finer level (or reappearing) needs the camera 5% closer than the switch distance
  (`LodComponent.Hysteresis`), so a camera resting on a boundary doesn't flicker between two models.
- The renderer picks the levels once per frame, before the shadow maps, for the camera it draws from
  (the editor camera in Anvil, the main camera in Play). Environment-map and shadow passes use the same
  level.

## Scripts

```csharp
var lod = GetComponent<LodComponent>();
int level = lod.CurrentLevel;          // 0 = own model, 1..n = a level (nearest first), -1 = culled
lod.CullDistance = 80;
lod.OnChanged(GameObject);             // re-registers the levels after a code edit
```

## Limits

- LOD models draw unposed. An animated (Animator/Ragdoll) or Cloth gameobject should only use
  **Cull Distance**.
- There is no cross-fade between levels: the switch is a hard swap.
- The distance is in world metres and doesn't account for the field of view or screen resolution.

## How it works

- `Engine/Components/LodComponent.cs`: the settings (`Levels`, `CullDistance`), `ResolveLevels` (model
  keys through `Assets.FindModel`, nearest first) and `SelectLevel` (distance, hysteresis, culling).
  An inspector edit (`OnChanged`), adding or removing it calls `BasicEntity.RefreshMaterials`, which
  registers the meshes again.
- `BasicEntity.RegisterInLibrary` registers the gameobject's model as level 0 and each resolved LOD model
  as level 1..n in the `MeshMaterialLibrary`; `LodModels` remembers them so `DeleteFromRegistry` removes
  them all. `BasicEntity.SelectLevelOfDetail` sets `TransformMatrix.LodLevel` and flags the transform as
  changed so culling and shadow maps update that frame.
- `MeshLibrary.LodLevels` stores the level of every registered instance; `MeshMaterialLibrary.Draw` skips
  instances whose level isn't the gameobject's current one. `MeshMaterialLibrary.SelectLevelsOfDetail` is
  called from `Renderer.Draw` right before the shadow maps.
- Editor: `Editor/Anvil/Models/LodComponentViewModel.cs` (one `LodLevelViewModel` row per level) and
  its template in `MainWindow.axaml`.
- Checks: `Tests/Components/LodChecks.cs` (`--graphics` also checks the mesh library with real models).
