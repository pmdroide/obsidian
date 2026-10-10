# Ragdoll physics

The **Ragdoll** component gives a skinned gameobject (a model built with the `SkinnedModelProcessor`,
see [Skeletal_Animation.md](Skeletal_Animation.md)) a rigid body for each of its main bones. Try it
in `Content/Scenes/AnimationTest.obsc`: open it in Anvil and press Play.

During Play the ragdoll has two states:

- **Animated** (the default). The parts are kinematic and follow the pose the Animator plays (or the
  bind pose without an Animator). Other bodies bounce off the character, it reports contacts to its
  scripts, and raycasts hit it.
- **Limp** (active). The parts become dynamic bodies joined by joints. They keep the velocity the
  animation gave them, fall under gravity and collide with the scene and with each other. The skin
  follows the bodies, and the gameobject's position follows the hips.

Outside Play nothing changes: the component only stores its settings.

## The sample scene

`AnimationTest` has a ragdoll row behind the animation samples. The ground has a Static Physics
component so the ragdolls have something to land on.

| Gameobject | Tests |
| --- | --- |
| Y Bot - Ragdoll (limp on Play) | **Active on Start**: it collapses when Play starts |
| X Bot - Ragdoll (hit by ball) | walks in place until the **Ragdoll Ball** falls on its head (**Impact** 3 m/s) |
| Y Bot - Ragdoll (click to knock over) | walks slowly; throw a ball at it to knock it over (**Impact** 3 m/s) |
| Ragdoll Ball | a 6 kg dynamic sphere 4 m above the X Bot |

The Main Camera runs Freecam and the **Ragdoll Test** script (`Content/Scripts/RagdollTestScript.cs`):

- **Left click** throws a ball where the mouse points (Ball Speed and Ball Mass are script fields).
- **G** makes every ragdoll go limp.
- **R** stands every ragdoll back up and removes the thrown balls.

## Inspector settings

- **Mass**: the whole body's mass in kg (default 70). Parts get shares by their collider volume.
- **Impact**: the ragdoll goes limp when a moving body hits one of its parts at least this fast,
  in m/s. 0 turns this off. The hit part then gets the striking body's momentum.
- **Friction**: 0 to 1. How strongly the joints resist bending. 0 is floppy; 1 nearly holds the pose.
- **Active on Start**: go limp as soon as Play starts.

A Ragdoll works with or without an Animator. While the ragdoll is limp, the Animator stops posing the
mesh. It continues from its current time when the ragdoll recovers.

## Scripts

```csharp
var ragdoll = GetComponent<RagdollComponent>();      // or target.GetComponent<RagdollComponent>()
ragdoll.Activate();                                  // go limp
ragdoll.AddImpulse(direction * 400, hit.Point);     // kg*m/s on the part nearest the point (while limp)
ragdoll.Deactivate();                                // stand up where the hips lie and animate again
bool limp = ragdoll.IsActive;
```

`Activate()` called before the first Play update takes effect as soon as the ragdoll is built.
`Deactivate()` puts the gameobject upright on the ground below the hips (a downward raycast) and keeps
its facing. Collisions with ragdoll parts reach the gameobject's scripts as ordinary
`OnCollisionEnter/Stay/Exit` events, and `Raycast` reports the gameobject as `hit.GameObject`.
`Raycast(..., ignore: gameObject)` skips its ragdoll as well as its own collider.

## How the body is split

`RagdollRig` (`Engine/Physics/RagdollRig.cs`) works this out once per model from the bind pose:

1. Each vertex belongs to its most heavily weighted bone.
2. A bone becomes a **part** when it owns at least 16 vertices (and 0.4% of them), and those vertices
   span at least 7% of the model's size. The vertices of smaller bones (fingers, toes, a short
   neck, end bones) join the nearest part above them.
3. Each part's collider is the convex hull of its vertices in its bone's space. Parts are joined to
   the nearest part above them, at the child bone's origin. The first part is the root (the hips).

Y Bot and X Bot get 18 parts: hips, two spine parts, head, and on each side a shoulder, upper arm,
forearm, hand, thigh, shin and foot.

Joint limits come from the child bone's name (`RagdollRig.JointFor`; Mixamo, Unreal, Unity and Blender
names, with any `prefix:` ignored). They are measured from the bind pose:

| Joint | Bones | Limit |
| --- | --- | --- |
| Elbow | `forearm`, `lowerarm`, `elbow` | hinge, -5° to 140°, bending forward |
| Knee | `leg`, `calf`, `shin`, `knee` | hinge, -5° to 135°, bending backward |
| Upper arm | `arm` | 100° cone, ±60° twist |
| Thigh | `upleg`, `thigh` | 75° cone, ±30° twist |
| Clavicle | `shoulder`, `clavicle` | 15° cone, ±10° twist |
| Spine | `spine`, `chest`, `hips` | 30° cone, ±20° twist |
| Neck | `neck`, `head` | 40° cone, ±40° twist |
| Hand / Foot | `hand`, `wrist` / `foot`, `ankle`, `toe` | 60° / 35° cone |
| Other | anything else | 45° cone, ±30° twist |

Hinges need the character's facing. Up is Z. Left points from each `Right...` bone to its mirrored
`Left...` bone (`_r`/`_l` and `.R`/`.L` work too), and forward follows from both. Mixamo characters
face -Y in the engine.

Parts that share a joint, share a parent (the two thighs, the two shoulders) or are a grandparent and
grandchild never collide. All other pairs do, so a hand can rest on the chest and the legs can't pass
through each other. A gameobject's own Physics collider (a character controller, for example) never
collides with its parts. While the ragdoll is limp that collider is removed, and it comes back on
recovery.

## How it runs

- `RagdollComponent` builds the ragdoll on its first Play update through `ScenePhysics.CreateRagdoll`.
  `Ragdoll` (`Engine/Physics/Ragdoll.cs`) owns the BEPU bodies and constraints.
- Before each physics step, an animated ragdoll gives its kinematic parts the velocity that carries
  them to this frame's pose. A part that would move more than 1 m in one step (a teleport) jumps instead.
- Going limp makes the parts dynamic and adds, per joint, a `BallSocket`, a `SwingLimit` + `TwistLimit`
  (cones) or an `AngularHinge` + `TwistLimit` (hinges), and an `AngularMotor` for friction.
  Part inertia is doubled to keep long, thin parts steady.
- After each step a limp ragdoll's bones follow the bodies. Bones without a part keep the pose they had
  relative to their parent when it went limp. `SkinnedMeshInstance` then re-skins the mesh. A sleeping
  ragdoll isn't re-skinned.
- Collision groups (`PhysicsSystem.SetCollisionGroup`) filter pairs in the narrow phase and let a
  raycast skip a whole gameobject.
- Stopping Play, disabling or removing the component, deleting the gameobject or loading another scene
  removes the bodies. Persistent gameobjects keep their ragdoll across scene loads.

Limitations: the colliders come from the bind pose, so fingers curled by the animation can poke a few
centimetres through the ground. Recovery snaps straight back to the animation, with no get-up blend.
Picking still uses the bind pose.

Checks: `RagdollChecks` in `Tests/Components`. The real-model checks (Y Bot falling, the sample's ball
knocking the X Bot over) need `--graphics`.
