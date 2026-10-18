# Layers, tags and roles

Click the viewport toolbar's **Layers** button to open **Layers, Tags & Roles**. The workspace **+** menu can also open the panel. It keeps its state when another workspace is selected or its tab is closed and reopened.

Each category lists its system defaults and custom entries. Enter a new name and click **Add**. To change a custom name, edit its field and click **Rename**. **Remove** deletes the custom definition and returns its assignments to that category's default. Names are trimmed, limited to 64 characters, and unique within their category without regard to case.

Select a gameobject to assign its **Tag**, **Layer** and **Role** in the Inspector. These assignments are saved with scenes, prefabs and duplicated gameobjects. Renaming a custom entry preserves every assignment, including those in scenes and prefabs that are not open.

System defaults are defined by the engine and cannot be edited or removed:

| Category | Protected entries | Default assignment |
| --- | --- | --- |
| Layers | Default, UI, IgnoreRaycast | Default |
| Tags | Untagged, Player, Enemy, static, renderable | Untagged |
| Roles | Default, Water | Default |

The Water role retains its existing water-volume and buoyancy behavior. Custom roles, tags and layers classify objects for game logic; their names are available to scripts:

```csharp
GameObject.Tag = "Enemy";
GameObject.Layer = "Default";
GameObject.RoleName = "Quest Giver"; // Add this custom role in the project first.

if (GameObject.RoleName == "Quest Giver") { /* game-specific behavior */ }
```

`BasicEntity.Role` remains the built-in `GameObjectRole` API for engine behavior. A custom role is exposed through `RoleName` and `RoleId`; its built-in `Role` is Default. Tag and layer names are exposed through `Tag` and `Layer`, with stable `TagId` and `LayerId` for saved references.

Custom definitions are saved immediately to `Content/System/Classifications.json`, included in standalone output and publish. System entries are reconstructed from engine definitions; an edited JSON flag cannot unlock or replace one. Scene and prefab records store custom IDs, so names can change without rewriting those assets. Deleted or missing IDs resolve to the category default and are normalized on the next save. Project reset clears custom definitions.

Implementation: `Engine/Recources/ClassificationCatalog.cs`, `BasicEntity`, the editor bridge, `ClassificationsViewModel`, `ClassificationsView`, and `SceneObjectViewModel`. Definition and assignment changes run on the game thread; the UI reads immutable definition snapshots. `Tests/Components/ClassificationChecks.cs` checks protected defaults, manager commands, validation, Inspector assignment, scene/prefab/clone persistence, rename/delete behavior, restart, file protection and failed writes.
