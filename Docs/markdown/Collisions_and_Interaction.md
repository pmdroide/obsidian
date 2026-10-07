# Collisions, triggers and interaction

Scripts can react when gameobjects touch, overlap a trigger volume, or are used by the player
through a ray. Try it in the `CollisionTest` sample scene (last in the scene list).

## Physics component options

| Setting | Effect |
| --- | --- |
| **Body: Static** | Immovable collider built from the model's triangles. Scripts may still move it (it shoves Dynamic bodies out of the way). |
| **Body: Dynamic** | Convex hull with mass. Gravity, collisions and the script force helpers apply. |
| **Is Trigger** | Overlaps instead of colliding. Nothing is pushed, raycasts pass through, and both sides get `OnTriggerEnter/Stay/Exit`. A Static trigger is the model's **convex hull**, so it is a solid volume: a body fully inside still counts. |
| **Freeze Rotation** | Dynamic only. Contacts and impulses never tip or spin the body (infinite inertia). Use it for characters and upright props. |

Two Static colliders never touch each other, so a Static trigger only detects Dynamic bodies.
A Dynamic trigger still falls under gravity, but nothing holds it up. Keep zones Static and move
them from a script if they need to travel.

## Collision and trigger events

Override the hooks on a `ScriptBehaviour` attached to a gameobject with a Physics component:

```csharp
using Engine.Entities;
using Engine.Physics;
using Engine.Scripting;

public sealed class BellScript : ScriptBehaviour
{
    public override void OnCollisionEnter(Collision collision)
    {
        if (collision.ImpactSpeed < 1f) return; // something settling against it
        Log($"{collision.GameObject.Name} hit at {collision.ImpactSpeed:0.0} m/s");
        PlaySoundAt("Audio/blip.wav", collision.Point, collision.ImpactSpeed / 10f);
    }

    public override void OnTriggerEnter(BasicEntity other) => Log($"{other.Name} entered");
    public override void OnTriggerExit(BasicEntity other) => Log($"{other.Name} left");
}
```

| Hook | Called |
| --- | --- |
| `OnCollisionEnter(Collision)` | Once, when the pair starts touching. |
| `OnCollisionStay(Collision)` | Every physics step while they touch. |
| `OnCollisionExit(Collision)` | When they separate, or the other gameobject is removed or loses its collider. Holds the last contact. |
| `OnTriggerEnter/Stay/Exit(BasicEntity other)` | The same for an overlap where either side has **Is Trigger** ticked. |

`Collision` fields, seen from the receiving gameobject:

| Field | Meaning |
| --- | --- |
| `GameObject` | The other gameobject. |
| `Point` | World position of the deepest contact. |
| `Normal` | Unit normal pointing **from the other gameobject towards this one**. `Normal.Z > 0.7` means this gameobject is on top. |
| `Depth` | Penetration in metres (about zero while resting). |
| `ImpactSpeed` | How fast the surfaces were closing along the normal just before the step resolved the contact. On Enter this is the impact speed, including spin. While resting it is near zero. |

Details:

- Hooks run on the game thread right after the physics step, so after every `Update` of that
  frame. Both gameobjects get each event. A hook may spawn, destroy or move gameobjects.
- A body that falls asleep keeps its contacts: no Exit while it rests, and no Stay events until
  something wakes it.
- Ticking or unticking **Is Trigger** on a touching pair ends the old kind of contact (Exit) and
  starts the new one (Enter). Rebuilding a body for another reason (mass, scale) keeps its contacts.
- Stopping Play forgets every contact without raising Exit; scripts are stopping anyway.
- `IsTouching(other)` on a script (or `ScenePhysics.IsTouching(a, b)`) asks whether two
  gameobjects touch right now.
- A throwing hook stops that script, like a throwing `Update`.

## Raycasts and interaction

`Raycast(...)` returns the closest collider on a ray, skipping the script's own gameobject and
triggers (pass `includeTriggers: true` to hit them too). `CameraRay` starts at the main camera and
goes through the screen centre. `MouseRay` goes through the cursor.

Add **Interactable** (Inspector > Add Component) to a gameobject the player can use. Set its
**Prompt** ("Open the gate") and **Range** (metres from the camera). It also needs a non-trigger
Physics component, because rays only hit colliders. In the player's script:

```csharp
InteractableComponent target = FindInteractable(CameraRay, 6f, out RaycastHit hit);
hud.SetText("#prompt", target != null ? "E  " + target.Prompt : "");
if (target != null && GameInput.WasPressed(Keys.E)) Interact(hit);
```

`FindInteractable` returns `null` unless the first collider hit is an enabled Interactable on an
enabled gameobject within its Range, so walls block it but triggers don't. `Interact` runs
`OnInteract(Interaction)` on the target's scripts, then raises the C# event
`InteractableComponent.Interacted`. Subscribe to that event from code that isn't a script; it is
runtime-only and never saved. `Interaction` holds the `Interactor` (the caller's gameobject,
`null` on the Main Camera) and the `Hit`. `Interact(gameobject)` uses a target without a ray.

To act on another gameobject's body (a launch pad, a shove, a carried crate), use
`GetVelocity(target)`, `SetVelocity(target, v)`, `SetAngularVelocity(target, w)`,
`AddImpulse(target, impulse)` and `AddImpulseAtPosition(target, impulse, point)`.

## The CollisionTest sample

`Content/Scenes/CollisionTest.obsc` is a walled 28 m arena with a first-person player. The
**Player** capsule is a Dynamic body (80 kg, Freeze Rotation) running **Collision Test Player**,
which drives the Main Camera from its eyes. Movement sets the body's velocity with a limited
acceleration (about 3200 N), so the physics solver does the pushing and heavy things resist.

| Input | Action |
| --- | --- |
| W A S D / left stick, Shift, Space / A | Move, run, jump (ground check: five short rays down) |
| Hold right mouse, or the arrow keys | Look |
| E / gamepad X | Use the Interactable under the crosshair |
| Hold left mouse, F | Carry an object up to 40 kg in front of you, throw it |
| Q | Shove the object under the crosshair: an impulse at the hit point, so off-centre shoves spin it |
| R | Reload the scene |

| Station | What it shows | Script |
| --- | --- | --- |
| Push lane (west): Light 10 kg, Medium 60 kg, Heavy 180 kg crates; Anchor Block 2000 kg near the start | Pushing by walking; mass and friction decide what moves | none |
| Goal Zone (north-west), a glowing green pad | Thin Static trigger (12 cm) that crates standing on it overlap; counts what is inside and glows brighter while occupied | Trigger Zone |
| Launch Pad (centre), a glowing cyan pad | Thin Static trigger that throws Dynamic bodies up and along its +Y | Launch Pad |
| Vault (north), with Gate and Gate Lever | Interactable lever slides a Static gate into the floor; its prompt follows the state | Gate Lever |
| Prize Cube inside the vault | Interactable on a Dynamic object (use it, or carry it) | Color Cycle |
| Dominoes and Impact Plate (north-east) | Chain reaction; the plate reports each hit's impact speed, flashes and clicks | Impact Reporter |
| Ball Dispenser, Ball Chute and Stack Blocks (south-east) | Interactable button spawns physics balls onto a stacked tower (at most 8) | Ball Dispenser |

The HUD (`Content/UI/CollisionTest.xml` + `.css`) shows a crosshair that turns green over
something usable, the action prompt, what the player is touching or carrying, and an event feed.
Whatever the crosshair is on is brightened. Every station posts its events there through
`CollisionTestFeed`, which also writes them to the editor log and owns the colour helpers
(`Paint`, `Glow`, `Highlight`), so a script can recolour the object the player is aiming at.
Material and prompt changes are restored when Play stops; transforms are rewound by Play mode
itself.

Glow (`EmissiveStrength`) only shows on the **Emissive** material type, which is why the pads use
it and the other stations flash by changing colour. A translucent trigger volume isn't possible
yet: `IsTransparent` on a Basic material hides it, and the ForwardShaded type draws a fixed grey.
That is why the zones are thin pads.

## How it works

- `PhysicsSystem` gives BEPU a `ContactRecorder` through `NarrowPhaseCallbacks`. For every pair
  in a step it records the deepest contact, the normal and the approach speed (from both bodies'
  linear and angular velocity at the contact). A pair counts as touching when the contact
  penetrates, rests within 5 mm, or is a speculative gap that the approach speed closes within
  the step. That is how a fast impact reports its real speed in the step that stops it. Pairs
  with a trigger return no constraint and must actually overlap.
- Trigger flags live in a set of packed collidable references. `RemoveStatic`/`RemoveDynamic`
  clear them, because BEPU reuses handles. The ray handler skips them unless asked.
- `ScenePhysics` maps handles to gameobjects. After each step it diffs the recorded pairs against
  its touches, which are keyed by gameobject pair so a rebuilt body keeps them. That gives Enter,
  Stay and Exit. A pair that vanished while all its bodies sleep is kept. The events go to every
  running Script Behaviour on both gameobjects through `ScriptBehaviourComponent.Notify`.
- **Freeze Rotation** zeroes the body's inverse inertia. A Static **trigger** is built with
  `PhysicsSystem.AddStaticConvex` (hull, or a bounding box if no hull can be built), offset by its
  centre like a Dynamic body.

## Limitations

- No collision layers or filters yet: every Dynamic body collides with everything, and raycasts
  can only skip their own gameobject and triggers.
- One contact per pair is reported (the deepest), not the full manifold.
- Fast, small bodies can pass through a thin trigger between two steps. Triggers have no
  continuous detection.

## Checks

`Tests/Components/CollisionChecks.cs` checks events and impact data on both sides, sleeping
contacts, removal and leaving Play, trigger pass-through and see-through rays, a body asleep
inside a trigger, Freeze Rotation, Interactable range/blocking/disable, the editor and scene-file
plumbing, and a scripted Play run of the sample. In that run the player walks onto the launch
pad, pushes the 60 kg crate, fails to move the anchor, uses the lever and the dispenser, and
Stop restores everything. Run `dotnet run --project Tests\Components\Components.csproj`.
