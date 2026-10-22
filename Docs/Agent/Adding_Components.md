# Adding a Gameobject Component

Moved from `CLAUDE.md`; read when the task touches this area.

Components live on `BasicEntity.Components` (at most one per type by default; Script Behaviour allows multiple), show in Anvil's Inspector component list and **Add Component** menu, and save inside the scene's `Components` records. Each attachment has an `InstanceId` persisted in its record; cloning assigns fresh IDs. The bridge and reconciler use these IDs to edit/remove individual attachments. The bridge, snapshots, cloning and scene format are generic, so a new component needs only these steps:

The scene's main camera also holds components (`Camera.Components`, bridge id `EditorBridge.MainCameraId` = -1), but only types allowed by `Camera.SupportsComponent` (Script Behaviour). Their hooks get a `null` owner; `ScriptBehaviourComponent.HostCamera` points at the camera.

1. **Engine type** - add `Engine/Components/<Name>Component.cs`:
   ```csharp
   public sealed class FooComponent : GameComponent
   {
       public const string TypeId = "foo";        // saved in scene files: never rename
       public float Speed { get; set; } = 1f;     // public get/set = persisted + sent to the editor
       [JsonIgnore] public bool IsRunning { get; private set; } // runtime-only state
       public override void OnStart(BasicEntity owner) { }                 // Play pressed
       public override void OnUpdate(BasicEntity owner, GameTime time) { } // each Play frame
       public override void OnChanged(BasicEntity owner) { }  // after an inspector edit
       public override void OnRemoved(BasicEntity owner) { }  // after removal (default: OnStop)
       public override void OnStop() { }                      // Play stopped / disabled
   }
   ```
   Hooks run on the game thread. Respect `Enabled` (the inspector's Enabled checkbox). Persisted properties must round-trip through `System.Text.Json`; add `[JsonConverter(typeof(JsonStringEnumConverter))]` to enums.
2. **Register it** in the static constructor of `Engine/Components/ComponentRegistry.cs`: `Register<FooComponent>(FooComponent.TypeId, "Foo");`. Registration order is the Add Component menu order. Pass `allowMultiple: true` for repeatable components. Pass a factory (`owner => ...`) when defaults depend on the gameobject; Material uses `MaterialComponent.FromOwner`. Do not add per-component special cases to `EditorBridge`.
3. **Editor view model** - add `Editor/Anvil/Models/FooComponentViewModel.cs` deriving `ComponentViewModel` (copy `PhysicsComponentViewModel`):
   - one `[ObservableProperty]` per field, and `partial void OnSpeedChanged(double v) => Push(c => ((FooComponent)c).Speed = (float)v);`. `Push` routes through the bridge to this view model's own gameobject and does nothing during reconciliation.
   - override `Apply(GameComponent component, bool freezeFields)`: call `base.Apply`, return if `freezeFields`, then copy each field from the engine component.
   - ignore transient values from controls (for example a ComboBox `SelectedIndex` of `-1`).
4. **Register the editor** in `ComponentEditorRegistry` (`Editor/Anvil/Models/ComponentViewModel.cs`): `Register(FooComponent.TypeId, owner => new FooComponentViewModel(owner));`. Components without an editor are not offered in Add Component.
5. **Inspector template** - add `<DataTemplate DataType="m:FooComponentViewModel" x:DataType="m:FooComponentViewModel">` to the Components `ItemsControl.DataTemplates` in `Editor/Anvil/Views/MainWindow.axaml`. Include the `Enabled` checkbox and a `Remove Component` button bound to `RemoveCommand`. Bind only to the template's own view model, never to `SelectedObject...`. A `Flyout` must use `Opening="OwnedFlyout_Opening"` so it stays attached to its component.
6. **Engine use** - read the first with `entity.GetComponent<FooComponent>()`, or all attachments with `entity.GetComponents<FooComponent>()`. Add or remove from code with `entity.AddComponent(...)` / `entity.RemoveComponent(...)`, not `Components.Add`, so hooks run and registry multiplicity is enforced.
7. **Checks** - extend `Tests/Components/Program.cs` (add via bridge, inspector edit reaches the engine, clone independence, save/load), then run `dotnet run --project Tests\Components\Components.csproj`.

Physics is the reference for a component that another system reads: `ScenePhysics` rebuilds BEPU bodies each update from `BasicEntity.PhysicsType` / `Mass`, which come from `PhysicsComponent`. Older scenes with an entity-level `Physics` record are converted to the component on load.

