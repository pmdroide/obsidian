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

The working example is in `Engine/Content/Scripts/SpinExampleScript.cs`. The older
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
`Vector2`, `Vector3` and `Color`. Fields of other types are ignored.

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
numbers and bools as JSON values, enums by name, vectors as `[x, y, z]` arrays
and colours as `"#RRGGBBAA"`. `SpinExampleScript` (Degrees Per Second) and
`LaunchPadScript` (Up Speed, Forward Speed) are working examples.

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
| `GetComponent<T>()` | First component of type `T` on this gameobject, or `null`. |
| `Log(message)` | Writes `[ObjectName] message` to the engine/editor log. |
| `Spawn(modelKey, position, name)` | Adds a gameobject from a model key (`"Capsule"`, `"Cube"`, `"IsoSphere"`) for this Play session. Never saved. Returns `null` for an unknown key. |
| `Destroy(entity)` / `DestroyAllSpawned()` | Removes gameobjects this script spawned. Happens automatically when the script stops. |

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

```csharp
public override void OnCollisionEnter(Collision collision)
{
    if (collision.ImpactSpeed > 3f)
        PlaySoundAt("Audio/blip.wav", collision.Point, collision.ImpactSpeed / 10f);
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

### Audio

Clip paths are relative to `Engine/Content` and include the extension, for
example `"Audio/blip.wav"`. This is the path the Audio component's clip picker
shows. Supported formats are `.wav`, `.mp3`, `.ogg` and `.flac`.

| Member | What it does |
| --- | --- |
| `PlayAudio()` | Plays the first enabled **Audio** component on this gameobject, using its Inspector settings. Returns `true` if it started. |
| `StopAudio()` | Stops every Audio component on this gameobject. |
| `IsAudioPlaying` | `true` while any Audio component on this gameobject is playing. |
| `PlaySound(clip, volume = 1, loop = false, pitch = 1)` | Plays a 2D (non-positional) sound. |
| `PlaySound3D(clip, volume = 1, loop = false, minDistance = 10, maxDistance = 1000)` | Plays a 3D sound that follows this gameobject. It is at full volume within `minDistance` and gets quieter farther away. |
| `PlaySoundAt(clip, position, volume = 1, loop = false, minDistance = 10, maxDistance = 1000)` | Plays a 3D sound at a fixed world position. |
| `StopAllSounds()` | Stops every sound this script started. |

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

### Game flow, input and UI

Static APIs in `Engine.Logic` for game scripts (details in
[Scenes_and_Game_Flow.md](Scenes_and_Game_Flow.md)):

| API | Use it for |
| --- | --- |
| `GameFlow.LoadScene(index or name)`, `LoadNextScene()`, `Quit()` | Moving between the scenes in Game Settings > Scenes. |
| `GameInput.MenuUp/Down/Confirm/Back`, `AnyInputPressed`, `WasPressed(...)` | Keyboard, mouse and gamepad input with pressed edges and key repeat. |
| `GameUI.Open("UI/Name")` / `GameUI.Close(ui)` | A Vista XML/CSS layer over the scene. |

Run `dotnet run --project Tests/Components/Components.csproj` to verify script
selection, bridge edits, cloning, scene persistence, lifecycle, error isolation,
the example's rotation, and the built-in transform, physics and audio helpers.
