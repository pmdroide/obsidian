# Script behaviours

Select a mesh gameobject in Anvil, choose **Add Component > Script Behaviour**,
and pick **Spin Example**. Press **Play** to rotate the object around Z at
45 degrees per second. **Stop** restores its editor transform.

The example appears under **Assets > Scripts > SpinExampleScript.cs**. Double-click
it to open the actual source file in the existing external editor (VS Code,
falling back to Notepad). Scripts live under `Engine/Content/Scripts` and are
copied into build output as editable source assets.

Use **Add Component > Script Behaviour** again to attach more scripts to the same
gameobject. Each attachment has its own script picker, Enabled checkbox and
Remove Component button. You can also attach the same script more than once;
each attachment runs a separate instance. Editing or removing one attachment
leaves the others alone.

The component saves its script ID and Enabled setting in `.obsc` scenes. Each
attachment and each Play session gets a fresh C# script instance. Clones share
the script selection, but have separate runtime state. Behaviours run only in
Play mode. Disabling the component or gameobject, removing the component,
deleting the object, or stopping Play releases the instance and calls `Stop`.
Re-enabling or attaching during Play calls `Start` before the first `Update`.
Changing the selected script stops the old instance and starts the new one on
the next Play frame. An exception or missing script ID is logged to the engine
editor log and stops that instance's updates until restarted or changed.

## Debug logging

`ScriptBehaviour` already provides `Log(message)`. Call it from any behaviour:

```csharp
public override void Start()
{
    Log("Player ready");
}
```

Anvil's **Console** shows `[ObjectName] Player ready` (or `[Main Camera]` for camera scripts). Outside a behaviour, use `Engine.Editor.EditorBridge.Log("message")`. Logging works in Debug and Release builds, including standalone games.

Messages are also appended to `anvil-bridge.log` on the Desktop, with `%LOCALAPPDATA%/Anvil/anvil-bridge.log` as a fallback. Debug builds additionally write to the attached debugger. If neither file can be written, recent messages remain available in the Console. File writes and the recent history are synchronized across threads; the history and Console retain at most 1,000 entries. Clearing the Console discards pending messages too.

## Scripts on the Main Camera

Select **Main Camera** in the Hierarchy and choose **Add Component > Script
Behaviour**. The camera accepts only Script Behaviours, and a new one on the
camera defaults to **Freecam**. Camera scripts run in Play mode, are saved with
the scene, and can be enabled, changed or removed like any other attachment.

While an enabled script is attached, the camera's built-in Play controls are
turned off, so the script fully controls the view. **Stop** puts the camera back
where it was before Play.

### Freecam

`Engine/Content/Scripts/FreecamScript.cs` flies the camera in Play mode:

| Input | Action |
| --- | --- |
| Hold right mouse + move | Look around (pitch stops just short of straight up/down) |
| W / A / S / D | Move forward / left / back / right along the view |
| E / Q | Move up / down along world Z |
| Shift | Move 4× faster |
| Scroll while holding right mouse | Change speed (0.5–200 m/s, starts at 8) |

Movement eases in and out. Freecam starts from the camera's saved view. The
`Scenes/AutoExposureTest.obsc` sample already has it attached. The script is
short, so copy it as a starting point for your own camera controllers.

### Main Menu

`Engine/Content/Scripts/MainMenuScript.cs` runs the `Scenes/MainMenu.obsc`
sample: a Vista menu (title, main menu, scene list, settings, credits, quit
dialog) and a slow camera drift. It is the reference for scripts that load
scenes (`GameFlow`), read menu input from keyboard, mouse and gamepad
(`GameInput`) and show UI (`GameUI`). See
[Scenes_and_Game_Flow.md](Scenes_and_Game_Flow.md).

### Multiplayer Test

`Engine/Content/Scripts/MultiplayerTestScript.cs` runs the
`Scenes/MultiplayerTest.obsc` sample: peer-to-peer play through a Steam lobby,
with a spawned capsule for each player and a follow camera. It is the
reference for `SteamP2PSession` and for spawning gameobjects at runtime. See
[Steam_Multiplayer.md](Steam_Multiplayer.md).

### Collision Test

`Engine/Content/Scripts/CollisionTestPlayerScript.cs` runs on the **Player**
capsule of the `Scenes/CollisionTest.obsc` sample, a first-person physics
sandbox. Its stations each run a small script: Trigger Zone, Impact Reporter,
Launch Pad, Ball Dispenser, Gate Lever and Color Cycle. It is the reference for
collision and trigger events, camera rays and the Interactable component. See
[Collisions_and_Interaction.md](Collisions_and_Interaction.md).

### Weather Test

`Engine/Content/Scripts/WeatherTestScript.cs` runs on the Main Camera of the
`Scenes/WeatherTest.obsc` sample, next to Freecam. Keys 1-4 cross-fade between
clear, rain, sandstorm and snow. The arrows, Z/X, H and T tune intensity, wind,
haze and time of day. It is the reference for changing the weather from a script.
See [Weather.md](Weather.md).

### NPC Test

`Engine/Content/Scripts/NpcTestPlayerScript.cs` runs on the **Player** capsule of
the `Scenes/NpcTest.obsc` sample, a third-person player among four NPCs (a
patrolling guard, a villager, a critter and a sentry on a lookout). F shouts to
alert the NPCs nearby and T shows their paths and view cones. Each NPC runs
**NPC Reporter** (`NpcReporterScript.cs`), which tints it by state and posts its
sightings to the HUD. The sentry's reporter sends the guard to search where it
saw the player. It is the reference for the NPC component and its hooks. See
[NPC_Navigation.md](NPC_Navigation.md).

### Voxel Test

`Engine/Content/Scripts/VoxelTestScript.cs` runs on the **Player** capsule of the
`Scenes/VoxelTest.obsc` sample, a first-person builder in the scene's voxel world.
The left mouse button digs, a right click (or F) builds the block picked with 1-0
or the wheel, B blasts a crater, G throws a physics ball, V flies and N generates a
new world from a random seed. A frame of four spawned bars marks the face under the
crosshair. It is the reference for `ScriptBehaviour.Voxels`. See
[Voxel_World.md](Voxel_World.md).

### Water Test

`Engine/Content/Scripts/WaterTestScript.cs` runs on the Main Camera of the
`Scenes/WaterTest.obsc` sample (a pool and a wave tank in the rain), next to
Freecam. A click drops the object picked with 1-4 (beach ball, crate, plank,
boulder) from the drop height (Up/Down) onto the water under the mouse. Space
drops a volley, X splashes without an object, V steps the pool's waves, R toggles
the rain and G the automatic drops. It is the reference for `OnWaterEnter`,
`Splash`, `IsInWater` and `RaycastWater`. See [Water.md](Water.md).

### Helpers on a camera

In a camera script, `GameObject` is `null` and `Camera` is the camera. The
transform helpers move and turn the camera:

- `Position`, `Rotation`, `Translate`, `Rotate`, `LookAt` and `SetRotation` work as usual.
- `Forward` is the view direction, `Right` points to the screen's right, and `Up` to its top.
- `Scale` is always one.

`Raycast`, the camera rays, `FindInteractable`/`Interact`, the other-gameobject
physics helpers and the `PlaySound...` methods also work; 3D sounds follow the camera.
Collision and trigger hooks never run on a camera, because it has no collider.
The physics body helpers and the Audio-component helpers (`PlayAudio`,
`StopAudio`) do nothing on a camera, because it has no body or Audio component.

## Writing a script

Add a C# file under `Engine/Content/Scripts`, deriving from `ScriptBehaviour`:

```csharp
using Engine.Scripting;
using Microsoft.Xna.Framework;

public sealed class MoveExampleScript : ScriptBehaviour
{
    public override void Start()
    {
        // Initialize this gameobject's behaviour once.
    }

    public override void Update()
    {
        // Move one unit per second along X, independent of frame rate.
        Translate(Vector3.UnitX * DeltaTime);
    }

    public override void Stop()
    {
        // Optional: release resources or subscriptions owned by this instance.
    }
}
```

Register it in the static constructor of `ScriptRegistry`:

```csharp
Register<MoveExampleScript>("move-example", "Move Example");
```

Keep the ID stable because scenes store it. Rebuild and restart Anvil to compile
the script and add it to the Inspector picker. Scripts are compiled as part of
the engine; this component does not compile loose source files during gameplay.
Use private fields for runtime state; these are recreated at the next start.
Public fields and `[SerializeField]` fields are settings saved with the scene
(see [Serialized fields](#serialized-fields)).
`GameObject` provides transforms and `GetComponent<T>()`, `GetComponents<T>()`,
`AddComponent(...)`, and `RemoveComponent(...)`. All hooks run on the game thread.
`GetComponent<T>()` returns the first matching attachment; `GetComponents<T>()`
returns all matches. Each Script Behaviour component selects one behaviour.

Attach from game code with:

```csharp
entity.AddComponent(new Engine.Components.ScriptBehaviourComponent
{
    ScriptId = "move-example"
});
```

The working example is in `Engine/Content/Scripts/SpinExampleScript.cs`;
`TimerExampleScript.cs` (**Timer Example**) shows timers, a coroutine and tweens. The older
code-only `BasicEntity.Scripts` / `IScript` API remains supported separately.

## Serialized fields

Script settings work like Unity's serialized fields. A field shows in the
Inspector under the script picker, and each attachment saves its own value in
the scene:

```csharp
using Microsoft.Xna.Framework;

namespace Engine.Scripting;

public sealed class DoorScript : ScriptBehaviour
{
    public float OpenHeight = 3f;                       // public: serialized

    [SerializeField, Range(0.1f, 10f), Tooltip("Metres per second")]
    private float _speed = 2f;                          // private + [SerializeField]

    [SerializeField] private Color _lockedTint = Color.Red;
    [SerializeField, HideInInspector] private int _timesOpened;  // saved, not shown

    [NonSerialized] public bool IsOpen;                 // public, but runtime state only
    private float _progress;                            // private: runtime state only
}
```

**Which fields are serialized:**

- Instance fields that are public, or private/protected with `[SerializeField]`.
- Fields of base classes between your script and `ScriptBehaviour` count too; they are listed first.
- **Not serialized:** `[NonSerialized]`, `readonly`, `static` and `const` fields, and properties.

**Supported types:** `bool`, `int`, `float`, `double`, `string`, any enum,
`Vector2`, `Vector3`, `Color`, `BasicEntity` (a gameobject reference, see
[Gameobject references](#gameobject-references)) and `Prefab` (see
[Prefab fields](#prefab-fields)). Fields of other types are ignored.

**Attributes** (namespace `Engine.Scripting`):

| Attribute | Effect |
| --- | --- |
| `[SerializeField]` | Serializes a private or protected field. |
| `[NonSerialized]` | Keeps a public field out (the standard .NET attribute). |
| `[HideInInspector]` | Saved and applied, but not shown in the Inspector. |
| `[Range(min, max)]` | Number fields show a slider. It only limits the Inspector; code can set any value. |
| `[Tooltip("...")]` | Hover text on the Inspector label. |
| `[FormerlySerializedAs("oldName")]` | Loads values saved under an old field name after a rename. The value moves to the new name the next time it is edited. |

**Inspector:**

- Labels are nicified like Unity's: `_moveSpeed` and `m_moveSpeed` show as "Move Speed".
- A field without a saved value shows the C# initializer. A field you have changed shows a
  reset button that goes back to the initializer.
- Picking another script clears the previous script's values.

**When values apply:**

- Saved values are written into the new script instance **before `Start`**. Fields
  without a saved value keep their initializer.
- Edits during Play reach the running script before its next `Update`. Only the edited
  field changes, so values the script changed itself are kept. Like other component
  edits in this engine, they are not undone on Stop.
- A saved value that no longer fits its field (for example after changing the field's
  type) is skipped with a log line, and the field keeps its initializer.

To read a field's default, the engine constructs one instance of the script the
first time it needs it. That instance is never attached or started, so keep
constructors free of side effects; put setup in `Start`.

From game code, set values on the component with `SetField`. It throws for an
unknown field or a value that does not fit. Use `GetField<T>` to read a value
and `ResetField` to go back to the default:

```csharp
var door = new ScriptBehaviourComponent { ScriptId = "door" };
entity.AddComponent(door);
door.SetField("OpenHeight", 4);          // an int is converted for a float field
door.SetField("_lockedTint", Color.Blue);
float height = door.GetField<float>("OpenHeight");
```

Values are stored in the component's `Fields` record in the `.obsc` file:
numbers and bools as JSON values, enums by name, vectors as `[x, y, z]` arrays,
colours as `"#RRGGBBAA"`, gameobjects as their scene ID (`null` for none) and prefabs as
their Content path (`"Prefabs/Crate.prefab"`, `null` for none). `SpinExampleScript` (Degrees Per Second) and
`LaunchPadScript` (Up Speed, Forward Speed) are working examples.

### Gameobject references

A `BasicEntity` field references another gameobject of the scene, like a
`GameObject` field in Unity:

```csharp
public sealed class DoorButtonScript : ScriptBehaviour
{
    public BasicEntity Door;                          // pick the door in the Inspector
    [SerializeField] private BasicEntity _light;

    public override void OnInteract(Interaction interaction)
    {
        if (Door != null) Door.Position += Vector3.UnitZ * 3f;
    }
}
```

- **Inspector:** a picker with **None** and every gameobject in the scene, in
  Hierarchy order. Gameobjects with the same name show their ID, for example
  "Crate (#12)".
- **Saving:** the reference is saved as the target's scene ID. It survives save/load
  and renaming the target.
- **Before `Start`:** the field holds the gameobject with that ID in the active scene.
- **Deleted targets:** if the target was deleted, the field is `null` and the Inspector
  shows **Missing (#id)** until you pick another one or reset the field.
- **Duplicating:** a duplicated gameobject's references to itself point at the copy.
  Its references to other gameobjects are kept.
- **Persistent gameobjects:** a scene's reference to its own copy of a persistent
  gameobject (left out because the carried one came along) gets the carried one.
- **From code:** `SetField("Door", entity)` (or `null`) and `GetField<BasicEntity>("Door")`.

Only gameobjects can be referenced. Lights, decals and the Main Camera cannot.

### Prefab fields

A `Prefab` field (`using Engine.Logic;`) holds a prefab to place copies of, like a
prefab in a Unity `GameObject` field:

```csharp
public sealed class TurretScript : ScriptBehaviour
{
    public Prefab Bullet;                 // pick it in the Inspector, or drag it from Assets
    public float Interval = 0.5f;

    public override void Start() =>
        Every(Interval, () => Instantiate(Bullet, Position + Forward, Rotation));
}
```

- **Inspector:** a picker with **None** and every `.prefab` in Content. Prefabs with the
  same name show their path. You can also drag a prefab from Assets onto it.
- **Saving:** the prefab's Content path. A file that is gone gives `null` and shows
  **Missing (path)**; one that moved is found again by its name.
- **From code:** `SetField("Bullet", prefab)` (a `Prefab`, its path or `null`).

See [Prefabs.md](Prefabs.md) for making prefabs.

## Built-in helpers

Every `ScriptBehaviour` has helpers for the transform, physics and audio of the
gameobject it is attached to. Call them from `Start`, `Update` or `Stop`. Add
`using Engine.Physics;` to use `RaycastHit`.

The engine is **Z-up**. Positions are in world units (metres). Helper rotation
angles are in **degrees**. `AngularVelocity` is in radians per second, the unit
the physics engine uses.

### General

| Member | What it does |
| --- | --- |
| `GameObject` | The `BasicEntity` this script runs on (`null` on the Main Camera). |
| `Camera` | The camera this script runs on (`null` on a gameobject). |
| `DeltaTime` | Seconds since the last frame. Multiply speeds by it. |
| `Time` / `FrameCount` | Game-time seconds since this script started (held while paused) / Updates run so far. See [Timers, coroutines and tweens](#timers-coroutines-and-tweens). |
| `GetComponent<T>()` | First component of type `T` on this gameobject, or `null`. |
| `Log(message)` | Writes `[ObjectName] message` to Anvil's Console and the engine/editor log file. |
| `SceneEnvironment` | The active scene's sky and weather settings (`Weather`, `WeatherIntensity`, `WindSpeed`, ...). Changes are undone when Play stops. See [Weather.md](Weather.md). |
| `Voxels` | The scene's voxel world (`VoxelWorld`), or `null` when it has none: `GetBlock`, `SetBlock`, `FillSphere`, `FillBox`, `Raycast`, `TryGetSurface`. Edited chunks are remeshed before the next physics step; Stop regenerates the world. Fetch it each frame, because changing a voxel setting replaces it. See [Voxel_World.md](Voxel_World.md). |
| `Spawn(modelKey, position, name)` | Adds a gameobject from a model key (`"Capsule"`, `"Cube"`, `"IsoSphere"`) for this Play session. Never saved. Returns `null` for an unknown key. |
| `Instantiate(prefab, position, rotation)` | Places a copy of a prefab (a `Prefab` field, or a name/path such as `"Crate"` or `"Prefabs/Crate.prefab"`) with its components and settings. Without a rotation it uses the prefab's own. Never saved, removed when the script stops (like `Spawn`). Returns `null` when the prefab doesn't exist. `LoadPrefab(name)` gets the `Prefab` itself. See [Prefabs.md](Prefabs.md). |
| `Destroy(entity)` / `DestroyAllSpawned()` | Removes gameobjects this script spawned or instantiated. Happens automatically when the script stops. |
| `DontDestroyOnLoad()` / `DontDestroyOnLoad(entity)` | Keeps this gameobject (or another, e.g. one it spawned) when a script loads another scene. It moves into the new scene with its scripts still running. Lasts for this Play session; the saved equivalent is **Inspector > Persistent**. See [Persistent_GameObjects.md](Persistent_GameObjects.md). |
| `IsPersistent` | True when this gameobject survives scene loads. |
| `OnSceneLoaded()` | Override it: runs on a persistent gameobject's scripts after a scene load carried it over, once the new scene's scripts have started. For example, move to the new scene's spawn point. |
| `UpdateWhilePaused` | Override it to return true and `Update` keeps running while `GameFlow.Paused` freezes the game, for a pause menu. Every other script waits. Reference: `PauseMenuScript.cs`. |

### Transform

| Member | What it does |
| --- | --- |
| `Position` | World position (get/set). |
| `Rotation` | Rotation matrix (get/set), without scale or translation. |
| `Scale` | Per-axis scale (get/set). |
| `Right`, `Forward`, `Up` | The object's local +X, +Y and +Z axes in world space (unit length). |
| `Translate(offset, local = false)` | Moves by `offset`. With `local: true`, `offset` uses the object's own axes. |
| `Rotate(axis, degrees, local = false)` | Rotates around a world axis, or around one of the object's own axes with `local: true`. |
| `SetRotation(x, y, z)` | Sets the rotation from angles around world X, then Y, then Z. |
| `LookAt(target, up = Z)` | Turns the object so `Forward` points at `target` and `Up` stays as close to `up` as possible. |
| `TransformPoint(localPoint)` | Converts a point from object space (scale, rotation, position) to world space. |
| `TransformDirection(localDir)` | Rotates a direction from object space to world space. |

```csharp
public override void Update()
{
    Rotate(Vector3.UnitZ, 90f * DeltaTime);              // turn 90 degrees/s around world up
    Translate(Vector3.UnitY * 2f * DeltaTime, local: true); // drive forward at 2 m/s
}
```

When a gameobject has a **Dynamic** Physics body, setting `Position` or
`Rotation` during Play teleports the body and keeps its velocity. To move a body
smoothly, use velocities or forces so it still collides properly.

### Physics

Physics helpers need an enabled **Physics** component (Add Component > Physics).
Velocity and force helpers act only on **Dynamic** bodies. Without one, they do
nothing and getters return zero. Check `HasRigidbody` if your script needs a body.

| Member | What it does |
| --- | --- |
| `Physics` | The `PhysicsComponent`, or `null`. Change `BodyType`, `Mass`, `IsTrigger`, `FreezeRotation` or `Buoyancy` here. The body is rebuilt on the next physics update. |
| `HasRigidbody` | `true` while a Dynamic body exists. |
| `Velocity` | Linear velocity in m/s (get/set). |
| `AngularVelocity` | Spin in rad/s around each world axis (get/set). |
| `AddForce(force)` | Pushes with `force` newtons for this frame. Call it every `Update` while the force should act. |
| `AddForceAtPosition(force, worldPoint)` | Like `AddForce`, but at a world point. An off-centre push also spins the body. |
| `AddImpulse(impulse)` | Gives one instant kick (mass × velocity change), e.g. a jump. Call it once. |
| `AddTorque(torque)` | Twists with `torque` newton-metres around a world axis for this frame. Call it every `Update`. |
| `AddAngularImpulse(impulse)` | Gives one instant spin kick. |
| `Raycast(origin, direction, maxDistance)` | `true` if a collider lies on the ray within `maxDistance`. |
| `Raycast(origin, direction, maxDistance, out RaycastHit hit, includeTriggers = false)` | Same, and returns the closest hit: `hit.GameObject`, `hit.Point`, `hit.Normal`, `hit.Distance`. |
| `Raycast(ray, maxDistance, out RaycastHit hit, includeTriggers = false)` | The same along a `Ray`, e.g. `CameraRay` or `MouseRay`. |
| `IsTouching(other)` | `true` while this gameobject touches or overlaps `other` (as of the last physics step). |
| `GetVelocity(target)` / `SetVelocity(target, v)` / `SetAngularVelocity(target, w)` | Read or set another gameobject's Dynamic body. |
| `AddImpulse(target, impulse)` / `AddImpulseAtPosition(target, impulse, worldPoint)` | Kick another gameobject's Dynamic body, e.g. a launch pad or a shove. |

Raycasts always ignore the script's own gameobject. They only hit gameobjects
with an enabled Physics component, and skip triggers unless `includeTriggers` is
set. Water-role objects have no collider, so rays pass through them. `direction`
does not need to be normalized.

### Water

Water is a gameobject with the **Water** role (Inspector > Role, or the material's
**Apply Water Example**). Dynamic bodies that fall in splash on their own. See
[Water.md](Water.md).

| Member | What it does |
| --- | --- |
| `OnWaterEnter(WaterContact water)` | This gameobject's Dynamic body went into the water: its collider's bottom crossed the surface. Runs after the physics step, with or without Buoyancy. |
| `OnWaterExit(WaterContact water)` | It came back out (its bottom rose more than 5 cm above the surface). |
| `IsInWater(target = null)` | `true` while `target` (default: this gameobject) is partly under water. Play only. |
| `RaycastWater(ray, maxDistance, out Vector3 point)` | Where a ray first meets a moving water surface, e.g. the water under `MouseRay`. |
| `Splash(point, speed = 6, radius = 0.3f)` | Throws droplets, spray and a ring at `point` on the water, as if something `radius` m wide hit it at `speed` m/s. Below 1.2 m/s it only leaves a ring. |
| `WaterSplashes` | The scene's splashes: `Enabled`, `SplashCount`, `ParticleCount`, `Ripples`, `AddRipple(...)` and the `Splashed(point, speed, radius)` event. |

`WaterContact` has `GameObject`, `Point` (on the surface, under the collider's
centre), `Velocity`, `Speed` (m/s into the water on enter, out of it on exit),
`Radius` (the collider's horizontal half-size) and `Mass`.

```csharp
public override void OnWaterEnter(WaterContact water)
{
    if (water.Speed > 4f) PlaySoundAt("Audio/blip.wav", water.Point, water.Speed / 10f);
}
```

### Collision and trigger events

Override these hooks to react to contacts. They need a Physics component on the
script's gameobject and run on the game thread, right after the physics step
(after every `Update` of that frame). Both gameobjects of a pair get the event.

| Hook | When |
| --- | --- |
| `OnCollisionEnter(Collision c)` | This gameobject started touching another collider. |
| `OnCollisionStay(Collision c)` | Every physics step while they keep touching. |
| `OnCollisionExit(Collision c)` | They separated, or the other was removed or lost its collider. `c` holds the last contact. |
| `OnTriggerEnter(BasicEntity other)` | An overlap with a trigger started (this gameobject is the trigger, or entered one). |
| `OnTriggerStay(BasicEntity other)` / `OnTriggerExit(BasicEntity other)` | Every step while overlapping / the overlap ended. |
| `OnInteract(Interaction i)` | Another script used this gameobject's Interactable component (see below). |

`Collision` has `GameObject` (the other one), `Point`, `Normal` (unit, pointing
from the other gameobject towards this one), `Depth` and `ImpactSpeed` (how fast
the surfaces were closing just before the step resolved the contact: the impact
speed on Enter, about zero while resting). A trigger is a Physics component with
**Is Trigger** ticked: it overlaps instead of colliding. See
[Collisions_and_Interaction.md](Collisions_and_Interaction.md).

### NPC hooks and paths

An NPC component raises these hooks on the scripts of its own gameobject. See
[NPC_Navigation.md](NPC_Navigation.md).

| Hook / member | What it does |
| --- | --- |
| `OnTargetSpotted(BasicEntity target)` | This NPC noticed its target (saw it, heard it close by, or was alerted). |
| `OnTargetLost(BasicEntity target)` | This NPC gave up after losing sight of the target for its Memory Time. |
| `OnNpcStateChanged(NpcState previous, NpcState current)` | This NPC changed what it does, for example from Patrolling to Alerted. |
| `OnNpcAttack(BasicEntity target)` | This NPC attacked (Chase reaction, within Attack Range). |
| `FindPath(from, to, path, agentRadius = 0.5f)` | Fills `path` with the ground points of a walkable route on the scene's navigation grid. `false` without a grid. |

Drive an NPC through `GetComponent<NpcComponent>()`. It has `MoveTo`, `Investigate`,
`Alert`, `Calm`, `CanSee`, and runtime state such as `State`, `CanSeeTarget` and
`LastKnownTargetPosition`.

```csharp
public override void OnCollisionEnter(Collision collision)
{
    if (collision.ImpactSpeed > 3f)
        PlaySoundAt("Audio/blip.wav", collision.Point, collision.ImpactSpeed / 10f);
}
```

### Animation

`Animator` is the gameobject's Animator component (or `null`). Name its clips in Inspector >
Animator > **Actions**, then play them by name. See
[Skeletal_Animation.md](Skeletal_Animation.md).

| Member | What it does |
| --- | --- |
| `Play("Walk", fade = -1)` | Blends to the action over Fade (or `fade` seconds). Does nothing if that action is already playing, so call it every frame. `""` plays the default clip. Returns `false` for an unknown name. |
| `CrossFade("Walk", seconds)` | Same as `Play` with an explicit fade. |
| `PlayOnce("Jump", then = null)` | Plays the action once from its start, then blends to `then` (`null` means the action that was playing before). |
| `Stop()` / `Play()` | Bind pose / play the current action again from its start. |
| `Pause()` / `Resume()` / `Restart()` | Hold the pose / continue / jump back to the start. |
| `CurrentAction`, `IsPlayingAction(name)`, `HasAction(name)` | Which action plays. |
| `Time`, `NormalizedTime`, `Duration`, `IsFinished`, `IsFading`, `IsPaused`, `IsStopped` | Playback state. Setting `Time` seeks. |
| `ActionFinished` | Event `(animator, actionName)` raised when an action that doesn't loop reaches its end. |

```csharp
public override void Update()
{
    bool moving = GameInput.IsDown(Keys.Up);
    Animator?.Play(moving ? "Walk" : "Idle");
    if (GameInput.WasPressed(Keys.Space)) Animator?.PlayOnce("Jump");
}
```

### Camera rays and interaction

| Member | What it does |
| --- | --- |
| `MainCamera` | The scene's main camera (the one Play renders), whichever object the script runs on. |
| `CameraRay` | From the main camera through the screen centre (a crosshair). |
| `MouseRay` / `ScreenPointToRay(pixel)` | From the main camera through the cursor / a pixel of the game view. |
| `FindInteractable(ray, maxDistance, out RaycastHit hit)` | The `InteractableComponent` the ray points at, or `null`. The first collider hit must be interactable and within its Range. |
| `Interact(hit)` / `Interact(gameobject)` | Runs `OnInteract` on the target's scripts and raises `InteractableComponent.Interacted`. `false` without an enabled Interactable. |
| `FindGameObject(name)` | First gameobject in the active scene with this name, or `null`. |

```csharp
var target = FindInteractable(CameraRay, 5f, out RaycastHit hit);
if (target != null && GameInput.WasPressed(Keys.E)) Interact(hit); // HUD text: target.Prompt
```

The physics step runs after scripts in each frame. Forces you add in `Update`
apply on that frame's step. `AddForce` uses `DeltaTime`, so the result does not
depend on the frame rate.

```csharp
using Engine.Physics;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>Hover above the ground, and jump when something passes in front.</summary>
public sealed class HoverScript : ScriptBehaviour
{
    private const float HoverHeight = 2f;

    public override void Start()
    {
        if (!HasRigidbody) Log("HoverScript needs a Dynamic Physics component.");
    }

    public override void Update()
    {
        if (!HasRigidbody) return;

        // Spring towards HoverHeight above whatever is below us.
        if (Raycast(Position, -Vector3.UnitZ, 10f, out RaycastHit ground))
        {
            float mass = Physics.Mass;
            float lift = (HoverHeight - ground.Distance) * 40f - Velocity.Z * 8f;
            AddForce(Vector3.UnitZ * (9.81f + lift) * mass); // cancel gravity, then add the spring
        }

        // Jump when an object is within 3 m in front of us.
        if (Raycast(Position, Forward, 3f, out RaycastHit ahead) && Velocity.Z < 0.1f)
        {
            Log($"Jumping over {ahead.GameObject?.Name}");
            AddImpulse(Vector3.UnitZ * 5f * Physics.Mass);
        }
    }
}
```

### Timers, coroutines and tweens

Delays, repeats, sequences and smooth motion without counting `DeltaTime` by
hand. Everything here advances **after this script's `Update`**, in game time
(held while `GameFlow.Paused`), and ends when the script stops (Stop, disable,
removal). Pass `realtime: true` (or yield `WaitForSecondsRealtime`) to keep
counting while paused; that only matters in a script with `UpdateWhilePaused`,
because other scripts get no frames while paused. A callback or coroutine that
throws stops the script like a throwing `Update`.

| Member | What it does |
| --- | --- |
| `After(seconds, action)` | Runs `action` once after the delay (`0` = next frame). |
| `Every(seconds, action, times)` | Runs `action` every interval, `times` in total (`0` = until cancelled). A long frame catches up on the runs it missed. |
| `StartTimer(seconds)` | A countdown with no action: poll `IsDone`, `Remaining`, `Progress`. A cooldown: `if (_reload.IsDone) { Fire(); _reload.Restart(); }`. |
| `CancelAllTimers()` | Cancels every timer and tween this script started. |
| `Tween(from, to, seconds, apply, ease)` | Calls `apply(value)` every frame from `from` to `to` (`float`, `Vector3` or `Color`). `Tween(seconds, apply, ease)` passes the eased 0-1 progress. |
| `MoveTo(target, seconds, ease)` / `MoveBy(offset, ...)` | Slides this gameobject (or camera). For objects without a Dynamic body; move bodies with `Velocity`. |
| `ScaleTo(target, seconds, ease)` | Grows or shrinks this gameobject. |
| `RotateTo(matrix or x, y, z degrees, seconds, ease)` | Turns the short way round (quaternion slerp). |
| `StartCoroutine(IEnumerator)` | Runs an iterator method as a coroutine. It runs to its first `yield` straight away. Returns a `ScriptCoroutine` (`IsDone`, `Stop()`). |
| `StopCoroutine(c)` / `StopAllCoroutines()` | Ends one or all of this script's coroutines. |

Timers and tweens return a `ScriptTimer`:

| Member | What it does |
| --- | --- |
| `Pause()` / `Resume()` / `Cancel()` | Hold, continue, or end it without running the action or `OnComplete`. A cancelled tween keeps the value it reached. |
| `Complete()` | Finish now. A tween jumps to the end of this cycle, then the action and `OnComplete` run. |
| `Restart()` | Start over from zero, also after it finished or was cancelled. |
| `OnComplete(action)` | Runs after the last cycle. Chainable. |
| `Loop(cycles, pingPong)` | Repeat, `cycles` runs in total (`0` = forever). With `pingPong`, a tween plays backwards every other cycle. Chainable. |
| `Elapsed`, `Remaining`, `Progress`, `Duration`, `Completed` | Where it is in the current cycle, and how many cycles have finished. |
| `IsRunning`, `IsPaused`, `IsDone`, `IsCancelled`, `Realtime` | State. |

A coroutine can yield:

| Yield | Resumes |
| --- | --- |
| `null` | On the next frame. |
| `0.5f` or `new WaitForSeconds(0.5f)` | After that many seconds of game time. |
| `new WaitForSecondsRealtime(s)` | After `s` seconds, still counting while paused. |
| `new WaitForFrames(n)` | After `n` frames. |
| `new WaitUntil(() => cond)` / `new WaitWhile(() => cond)` | When the condition turns true / false. It is checked after each `Update`. |
| A `ScriptTimer`, for example `MoveTo(...)` | When the timer or tween is done. |
| A `ScriptCoroutine` | When that coroutine ends. |
| An `IEnumerator`, for example `OpenDoor()` | After running it as a nested step. |

`Ease` sets the shape of a tween. It can also be a serialized field, so it shows
as a dropdown in the Inspector. The values are `Linear`, `SmoothStep`, and
`In`/`Out`/`InOut` variants of `Quad`, `Cubic`, `Quart`, `Sine`, `Expo`,
`Circ`, `Back` (overshoots), `Elastic` (wobbles) and `Bounce`.
`Easing.Evaluate(ease, t)` gives the curve's value for your own maths.

```csharp
using System.Collections;
using Microsoft.Xna.Framework;

public sealed class DoorScript : ScriptBehaviour
{
    private ScriptTimer _cooldown;

    public override void Start()
    {
        _cooldown = StartTimer(0f);                                         // ready at once
        Tween(1f, 1.05f, 0.8f, s => Scale = new Vector3(s), Ease.InOutSine)
            .Loop(pingPong: true);                                          // idle "breathing"
    }

    public override void OnInteract(Engine.Components.Interaction interaction)
    {
        if (!_cooldown.IsDone) return;
        _cooldown = StartTimer(3f);
        StartCoroutine(OpenAndClose());
    }

    private IEnumerator OpenAndClose()
    {
        Vector3 closed = Position;
        yield return MoveBy(Vector3.UnitZ * 2.5f, 0.6f, Ease.OutCubic);    // waits for the tween
        yield return 2f;                                                    // stays open 2 s
        yield return MoveTo(closed, 0.8f, Ease.OutBounce);
        Log("Door closed");
    }
}
```

#### Maths and random helpers

These are static classes in `Engine.Scripting`, so scripts can call them without
a `using`. Pass `DeltaTime` where a method asks for it.

| Member | What it does |
| --- | --- |
| `ScriptMath.Damp(current, target, sharpness, dt)` | Eases towards a target the same at any frame rate. Works on `float` and `Vector3`. `DampAngle` does the same for angles in degrees, the short way round. |
| `ScriptMath.SmoothDamp(current, target, ref velocity, smoothTime, dt)` | A spring that settles without overshooting, for example a follow camera. Works on `float` and `Vector3`. |
| `ScriptMath.MoveTowards(current, target, maxDelta)` | Steps towards a target without passing it. |
| `InverseLerp`, `Remap`, `Repeat`, `PingPong`, `DeltaAngle`, `Approximately` | Small number helpers. |
| `ScriptRandom.Range(min, max)` | A random number. For `int`, `max` is exclusive. |
| `ScriptRandom.Value`, `Chance(p)`, `Sign` | 0 to 1, true with probability `p`, or ±1. |
| `ScriptRandom.OnUnitSphere`, `InsideUnitSphere`, `InsideUnitCircle` | Random directions and points. The circle is on X/Y, the ground plane. |
| `ScriptRandom.Color(saturation, value)`, `Pick(list)`, `Shuffle(list)` | A random hue, a random element, or the list shuffled in place. |
| `ScriptRandom.Seed(n)` | Makes the numbers repeatable. |

### Audio

Clip paths are relative to `Engine/Content` and include the extension, for
example `"Audio/blip.wav"`. This is the path the Audio component's clip picker
shows. Supported formats are `.wav`, `.mp3`, `.ogg` and `.flac`.

| Member | What it does |
| --- | --- |
| `PlayAudio()` | Plays the first enabled **Audio** component on this gameobject, using its Inspector settings. Returns `true` if it started. |
| `StopAudio()` | Stops every Audio component on this gameobject. |
| `IsAudioPlaying` | `true` while any Audio component on this gameobject is playing. |
| `PlaySound(clip, volume = 1, loop = false, pitch = 1, bus = Effects)` | Plays a 2D (non-positional) sound. |
| `PlaySound3D(clip, volume = 1, loop = false, minDistance = 10, maxDistance = 1000, bus = Effects)` | Plays a 3D sound that follows this gameobject. It is at full volume within `minDistance` and gets quieter farther away. |
| `PlaySoundAt(clip, position, volume = 1, loop = false, minDistance = 10, maxDistance = 1000, bus = Effects)` | Plays a 3D sound at a fixed world position. |
| `PlayMusic(clip, volume = 1, loop = true)` | Streams a music track on the Music bus. It replaces the track playing now (one at a time). |
| `StopMusic()` | Stops the music track, whoever started it. |
| `StopAllSounds()` | Stops every sound this script started. |

`bus` picks the volume setting that applies: `AudioBus.Effects` (default), `AudioBus.Music`
or `AudioBus.Master`. Change the volumes with `Audio.MasterVolume`, `Audio.MusicVolume`
and `Audio.EffectsVolume`. See [Audio_Volume.md](Audio_Volume.md).

The `PlaySound...` methods return a `ScriptSound` that you can control while it
plays: `IsPlaying`, `Volume` (0-1), `Pitch`, `Loop`, `Paused` and `Stop()`. They
return `null` if audio is unavailable (for example, FMOD is not installed) or the
clip cannot load. Use `?.` when you call the result.

Sounds a script starts stop automatically when the script stops. That happens
when Play ends, when the component or gameobject is disabled or removed, or when
the selected script changes. Sounds from an Audio component follow that
component's own lifecycle instead.

```csharp
using Engine.Scripting;
using Engine.Physics;
using Microsoft.Xna.Framework;

/// <summary>Engine hum whose pitch follows speed, plus a thud on hard landings.</summary>
public sealed class EngineSoundScript : ScriptBehaviour
{
    private ScriptSound _hum;
    private float _lastFallSpeed;

    public override void Start()
    {
        _hum = PlaySound3D("Audio/engine_loop.wav", volume: 0.6f, loop: true, minDistance: 5f);
    }

    public override void Update()
    {
        if (_hum != null) _hum.Pitch = 0.8f + Velocity.Length() * 0.05f;

        // Landing: falling fast last frame, nearly stopped now.
        if (_lastFallSpeed < -6f && Velocity.Z > -1f)
            PlaySoundAt("Audio/thud.wav", Position, volume: 1f);
        _lastFallSpeed = Velocity.Z;
    }

    // No Stop() override needed: _hum stops with the script.
}
```

### Input actions

Read the project's named actions (Anvil > Window > Input Actions) instead of keys, so keyboard, mouse and
gamepad all work and players can rebind them. Details: [Input_Actions.md](Input_Actions.md).

| Helper | Returns |
| --- | --- |
| `WasActionPressed("Jump")`, `IsActionDown("Sprint")`, `WasActionReleased("Attack")` | Button state this frame |
| `GetAxis("Zoom")` / `GetVector("Move")` | an Axis value / a Vector2 value (+Y forward) |
| `BindingText("Interact")` | the key or button to show in a prompt, for the device the player uses ("E" or "X") |
| `GetAction("Jump")` | the `InputAction`: `HeldTime`, `WasPressedWithin` + `ConsumePress` (input buffering), `Enabled`, `Rebind` |

```csharp
Vector2 move = GetVector("Move");                     // length up to 1
Translate((Forward * move.Y + Right * move.X) * Speed * DeltaTime);
if (WasActionPressed("Jump")) AddImpulse(Vector3.UnitZ * JumpImpulse);
Prompt = $"{BindingText("Interact")}  Open";          // "E  Open" or "X  Open"
```

### Game flow, input and UI

Static APIs in `Engine.Logic` for game scripts (details in
[Scenes_and_Game_Flow.md](Scenes_and_Game_Flow.md)):

| API | Use it for |
| --- | --- |
| `GameFlow.LoadScene(index or name)`, `LoadNextScene()`, `Quit()` | Moving between the scenes in Game Settings > Scenes. Pass `carryPersistent: false` to leave persistent gameobjects behind. |
| `GameFlow.Paused` | Freezing the game (scripts, components, physics) behind a pause menu. |
| `GameInput.MenuUp/Down/Confirm/Back`, `AnyInputPressed`, `WasPressed(...)` | Keyboard, mouse and gamepad input with pressed edges and key repeat. |
| `InputActions.ListenForControl()`, `SaveBindings()` / `LoadBindings()`, `ResetBindings()` | Rebinding menus ([Input_Actions.md](Input_Actions.md#rebinding-in-a-game-menu)). |
| `GameUI.Open("UI/Name")` / `GameUI.Close(ui)` | A Vista XML/CSS layer over the scene. |
| `SaveData.Save(name, data)` / `Load<T>(name, fallback)`, `SaveAsync`, `SaveText`, `List`, `Delete` | Save games, settings and high scores in the player's save folder ([Save_Data.md](Save_Data.md)). |

Run `dotnet run --project Tests/Components/Components.csproj` to verify script
selection, bridge edits, cloning, scene persistence, lifecycle, error isolation,
the example's rotation, and the built-in transform, physics and audio helpers.
`ScriptTimingChecks` covers the timers, coroutines, tweens, easing, pause
behaviour, `ScriptMath`/`ScriptRandom` and the Timer Example.
