# Prefabs

A prefab is a saved gameobject that you place copies of. It works like a Unity prefab: it keeps the model,
scale, material, role, Persistent flag and every component with its settings. Scenes and scripts place
**instances** of it. When you change the prefab, its instances change with it, except for the values an
instance overrides.

Prefabs are `.prefab` files in Content. New ones go to `Content/Prefabs`, but any Content folder works.

## Making a prefab

Set up a gameobject in the scene (model, Material, Physics, scripts, ...), then do one of these:

- Drag it from the **Hierarchy** into the **Assets** panel. Dropped on a folder, the prefab goes into that folder.
  Dropped on a file, it goes into that file's folder. Dropped on empty space, it goes into `Prefabs`.
- Right-click it in the Hierarchy > **Create Prefab**.
- Inspector > **Create Prefab**.
- **GameObject > Create Prefab from Selection**.

The file is named after the gameobject (`Crate.prefab`). If that name is taken, it becomes `Crate_2.prefab`, so
an existing prefab is never overwritten. The gameobject becomes an instance of the new prefab. Prefab
instances show a blue box icon and a blue name in the Hierarchy.

Lights, decals and the camera can't be prefabs. Only gameobjects can.

## Placing prefabs

- **Editor:** double-click a prefab in Assets, drag it into the Hierarchy, or right-click it > **Place in
  Scene**. The copy appears in front of the editor camera, already selected. It is named after the prefab.
- **Duplicating** an instance gives another instance of the same prefab.
- **Scripts:** `Instantiate(prefab, position)` or `Instantiate(prefab, position, rotation)`. The prefab is a
  `Prefab` Inspector field or a name/path such as `"Crate"` or `"Prefabs/Crate.prefab"`.

  ```csharp
  using Engine.Logic;

  public sealed class SpawnerScript : ScriptBehaviour
  {
      public Prefab Enemy;                       // pick it in the Inspector or drag it from Assets

      public override void Start()
      {
          Instantiate(Enemy, Position + new Vector3(0, 4, 0));
          Instantiate("Crate", Position + new Vector3(2, 0, 0), Matrix.CreateRotationZ(MathHelper.PiOver4));
      }
  }
  ```

  Like `Spawn`, script copies are never saved, and they are removed when the script stops. `Destroy(entity)`
  removes one earlier. Without a rotation, a copy uses the prefab's own rotation (the rotation the
  gameobject had when the prefab was made). `LoadPrefab("Crate")` returns the `Prefab` itself, or `null`.

## Editing a prefab: Apply, Revert, Unpack

To change a prefab, change one of its instances, then press **Apply**. The prefab bar in the Inspector has
these buttons, and the Hierarchy's right-click menu has them too. Apply, Revert and Unpack work in Edit mode
only.

| Button | What it does |
| --- | --- |
| **Apply** | Saves this instance's values into the prefab file. The other instances in the open scene update straight away, and keep their own overrides. Instances in other scenes update when those scenes load. |
| **Revert** | Drops this instance's overrides, so it takes the prefab's values again. |
| **Unpack** | Unlinks the instance. It becomes a plain gameobject that keeps its current values and no longer follows the prefab. |

The bar also shows the prefab's name and how many values this instance overrides, for example "2 overrides".
Hover the count to see the list, for example "Physics > Mass" or "Material > Roughness".

## What follows the prefab

| The instance's own (never from the prefab) | From the prefab, unless overridden |
| --- | --- |
| Name, position, rotation | Model, scale, Enabled, role, Persistent, material values |
| | Every component and every setting on it, including script fields |

An **override** is any value that differs from the prefab. A component the instance added or removed counts
as one override. Overrides are worked out when the scene is saved. Only the overridden values stay the
instance's own: everything else is read from the prefab file each time the scene loads. That is how a prefab
edit reaches every scene.

Script fields that point at the prefab's own gameobject (for example `public BasicEntity Self`) point at each
copy. Fields that point at other gameobjects in a scene don't mean anything inside a prefab.

## Files

A `.prefab` file is JSON in the scene format, holding one gameobject record:

```json
{
  "Version": 1,
  "GameObject": { "Name": "Crate", "ModelKey": "Cube", "Scale": [2, 2, 2], "Components": [ ... ] }
}
```

- In the scene, an instance is saved with its prefab's path and its overrides:
  `"Prefab": "Prefabs/Crate.prefab", "PrefabOverrides": ["Components/physics#0/Mass"]`.
- **Missing prefab:** the instance loads with the values saved in the scene. If the file is gone when the
  scene is saved, the instance keeps all its values (`"PrefabOverrides": ["*"]`). The Inspector shows "Prefab
  file missing"; Unpack it or make a new prefab.
- **Moved or renamed prefab:** a prefab that moved to another folder is found again by its file name.
- **Builds:** `Engine.csproj` copies every `Content/**/*.prefab` into the game. Prefabs are loose files, not
  in `Content.mgcb`. Their models and textures must be built-in or imported models, as for scenes.

## Code

- `Engine/Logic/Prefab.cs`: the file, `Load` (by name or path, cached until the file changes), `Resolve`,
  `List`, `UniquePath`.
- `Engine/Logic/PrefabUtility.cs`: `Overrides` and `Sync` (the merge), `Describe`, and the editor operations
  `CreatePrefab`, `ApplyToPrefab`, `RevertToPrefab` and `Unpack`.
- `MainSceneLogic.InstantiatePrefab` places a copy. `ReplaceGameObject` rebuilds an instance in place (same ID)
  after Apply or Revert, because a new model or material needs a new `BasicEntity`.
- `SceneSerialization.CaptureEntity`/`RestoreEntity` are shared by scenes and prefabs.
  `BasicEntityRecord.Prefab`/`PrefabOverrides` hold the link.
- Anvil: `IEditorBridge.EnqueueCreatePrefab/ApplyPrefab/RevertPrefab/UnpackPrefab/InstantiatePrefab`,
  `EditorObjectSnapshot.PrefabPath/PrefabOverrides/PrefabMissing` (overrides are worked out for the selected
  gameobject only), and `SceneObjectViewModel.IsPrefabInstance/PrefabStatus`.
- Checks: `Tests/Components/PrefabChecks.cs`.
