# Gameobject components in Anvil

Select a mesh gameobject and choose **Add Component > Audio** in the Inspector.
Drag a WAV, MP3, OGG or FLAC asset onto **Audio Clip**. Set volume, looping,
Play on Start, and 3D Spatial Audio. The component's Play and Stop buttons preview
the clip. Play on Start runs when entering Play mode; spatial emitters follow
their gameobject. Stopping Play, removing the component, disabling it, deleting
the gameobject, or changing scenes stops its audio.

Engine sound is enabled in Anvil by default. The speaker button in the viewport
toolbar mutes or enables both game playback and Inspector previews without
changing component volumes. The hosted engine keeps updating when focus moves
to editor controls, so sound and Play-mode behaviour continue while editing.

Components and their settings are saved in `.obsc` scenes. Existing scenes without
components remain compatible. Audio assets anywhere under `Engine/Content` are
copied into builds. Anvil also copies newly imported audio into its runtime
content directory on first playback, so assigning a new asset needs no rebuild.

## Roles and buoyancy

Every mesh gameobject has a **Role** under its name in the Inspector. **Default**
is an ordinary object. **Water** makes the object a water volume: the water fills
its XY footprint, from the top of its bounds downward without a floor. A Water
object builds no physics collider, so bodies can sink into it. With a Water
material, the surface follows the visible waves. Roles save by name in scenes;
older scenes load as Default. To add a role, add a value at the end of
`Engine/Entities/GameObjectRole.cs` and handle it where it matters. The Inspector
lists every value.

On a **Dynamic** Physics component, tick **Buoyancy** to float in Water objects
during Play. **Float** is the upward push when fully under water, relative to the
body's weight: 2 floats half-submerged and below 1 sinks. **Drag** slows motion
and spin in water. The body's bounds are sampled as a 3x3x3 grid, so a tilted body
rights itself and rides the swell.

## Adding another component type

**Script Behaviour** attaches a compiled C# behaviour with `Start()` and
`Update()` hooks. Select **Add Component > Script Behaviour**, keep **Spin
Example** selected, and press Play. See [Script behaviours](Script_Behaviours.md)
for the example and instructions for writing and registering your own scripts.

The **Material** component includes Standard and Water shaders. Select
**Add Component > Material**, then **Apply Water Example** to try the animated
water preset. See [Material component and water example](Material_Component.md)
for surface controls and rendering details.

1. Derive from `Engine.Components.GameComponent`. Use public properties for saved
   settings and private fields or `[JsonIgnore]` for runtime state. Override
   `OnStart`, `OnUpdate`, `OnChanged`, and `OnStop` as needed.
2. Register the type with a stable ID and display name in `ComponentRegistry`.
   The registry handles creation, snapshots, cloning, and JSON persistence.
   Each gameobject supports one component of each registered type by default.
   Register with `allowMultiple: true` to support repeated attachments, as Script
   Behaviour does. Each attachment has a saved identity so its editor can change
   or remove it independently.
3. Derive its editor from `Anvil.Models.ComponentViewModel`, implement `Apply`,
   and use `Push` for edits. Register its factory in `ComponentEditorRegistry`.
4. Add a typed `DataTemplate` to the Inspector's component `ItemsControl` in
   `MainWindow.axaml`. The Add Component menu uses the registries automatically.

Bridge operations queue component edits onto the game thread and mark the scene
dirty. Snapshot reconciliation preserves editor instances and suppresses feedback
while copying settings. Component collection changes reconcile even while an
Inspector control has focus.

## Verification

Run `dotnet run --project Tests/Components/Components.csproj` for integration
checks covering the registry, editor synchronization, scene compatibility,
cloning, and lifecycle. Add `-- --audio` for muted native FMOD playback checks,
including live volume changes, 3D movement, stopping, and newly imported assets.
These audio checks use the project's existing FMOD native libraries and require
an audio device; they do not open the editor window.
