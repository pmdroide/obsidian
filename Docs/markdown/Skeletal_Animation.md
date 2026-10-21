# Skeletal animation

Skinned FBX models (for example Mixamo characters) play their animation clips through the
**Animator** component. Try it in `Content/Scenes/AnimationTest.obsc`: open it in Anvil and press
Play. Outside Play every character shows its bind pose.

## The sample scene

`AnimationTest` (last entry of the scene list) uses the two files in `Content/GameObjects/Player`:
`Y Bot.fbx` (Y Bot, T-pose) and `Walking.fbx` (X Bot with a walk clip). Neither file has textures;
their materials are flat colours.

| Gameobject | Tests |
| --- | --- |
| X Bot - Walking (own clip) | a model playing its own clip, In Place |
| Y Bot - Walking (retargeted) | the X Bot walk played on Y Bot by bone name |
| Y Bot - Textured | albedo + normal map (UV checker) on a skinned mesh |
| X Bot - Root Motion | the clip's forward travel; it loops back every ~1 s |
| Y Bot - Slow Motion | Speed 0.25 |
| Y Bot - Bind Pose (no Animator) | the static bind pose, for comparison |
| UV Checker Cube | the same maps on a static mesh |
| Y Bot - Actions (arrow keys) | Animator **Actions** driven by the **Animator Actions** script (below) |

The row behind them tests the **Ragdoll** component (one goes limp on Play, a falling ball knocks
another over); see [Ragdoll_Physics.md](Ragdoll_Physics.md). The camera has Freecam, so you can fly
around in Play, and the Ragdoll Test script: left click throws a ball, G drops every ragdoll, R stands
them back up.

## Animator settings

- **Source**: content path of the model holding the clip, e.g. `GameObjects/Player/Walking`
  (an extension is ignored). Empty means this gameobject's own model.
- **Clip**: clip name inside the source. Empty plays its longest clip.
- **Speed**: playback rate. **Loop** wraps; without it the last frame holds.
- **In Place**: removes the clip's horizontal root motion and keeps the vertical bob.

- **Actions**: named clips that scripts play (see below). Each one has a **Name**, **Source**
  (empty means the Animator's Source), **Clip**, **Speed** (multiplies the Animator's Speed;
  negative plays backwards, 0 holds the first frame) and **Loop**.
- **Start Action**: the action that plays when Play starts. Empty plays the clip above.
- **Fade (s)**: how long `Play(action)` blends from the previous animation. 0 switches at once.

A clip from another model is matched by bone name. Each bone gets the source bone's rotation
change from its own bind pose, so rigs with slightly different rest poses still line up. The target
keeps its own bone lengths. The root's travel is scaled by the ratio of the two rigs' hip heights.

## Actions: playing animations from a script

Give each clip a name in **Actions** (for example Idle, Walk, Run, Jump), then call it from a script
on the same gameobject. `Animator` is a shortcut for `GetComponent<AnimatorComponent>()`.

```csharp
public override void Update()
{
    bool moving = GameInput.IsDown(Keys.Up);
    bool running = GameInput.IsDown(Keys.LeftShift);
    // Play does nothing while that action already plays, so calling it every frame is fine.
    Animator.Play(!moving ? "Idle" : running ? "Run" : "Walk");

    // One-shot: plays Jump once, then blends back to whatever played before.
    if (GameInput.WasPressed(Keys.Space)) Animator.PlayOnce("Jump");
}
```

| Call | What it does |
| --- | --- |
| `Play("Walk", fade = -1)` | Blends to the action over **Fade** (or `fade` seconds). It does nothing if that action already plays. `""` is the default clip. It also resumes a paused or stopped Animator. It returns `false` (and logs once) for an unknown name. |
| `CrossFade("Walk", seconds)` | `Play` with an explicit fade. |
| `PlayOnce("Jump", then = null, fade = -1)` | Plays the action once from its start, even if it loops or is already playing. Then it blends to `then`: `null` means the action that was playing before, `""` means the default clip. |
| `Stop()` | Shows the bind pose until `Play()` / `Play(action)`. |
| `Play()` | Plays the current action again (from its start after `Stop`). |
| `Pause()` / `Resume()` | Hold the current pose / continue. |
| `Restart()` | Jumps back to the current action's start (its end at a negative speed). |
| `CurrentAction`, `IsPlayingAction(name)`, `HasAction(name)`, `FindAction(name)` | Which action plays. Names ignore case and surrounding spaces. |
| `Time` (set to seek), `NormalizedTime`, `Duration`, `IsFinished`, `IsFading`, `IsPaused`, `IsStopped`, `IsPlaying` | Playback state. |
| `ActionFinished` | Event `(animator, actionName)`, raised when an action that doesn't loop reaches its end. |

Notes:

- A crossfade blends the two clips bone by bone. The old clip keeps moving underneath while the new
  one fades in, with a smoothstep weight.
- An action's Speed and Loop, and the Animator's Speed and Loop, can be changed from a script while
  the clip plays. Changing them in code during Play is not undone by Stop, so prefer separate
  actions (as the sample does with Walk Back).
- `Play`/`Stop` may run before the Animator itself has started. This happens when the script is
  listed above the Animator. The request is kept and the Animator starts with it.
- Stopping Play forgets what played. The next Play starts with **Start Action** again.

### The sample

`Y Bot - Actions (arrow keys)` in `AnimationTest` has five actions on the X Bot walk
(`GameObjects/Player/Walking`, set once as the Animator's Source):

| Action | Speed | Loop |
| --- | --- | --- |
| Idle | 0 (holds the walk's first frame; use a real idle FBX as its Source in a game) | yes |
| Walk | 1 | yes |
| Run | 1.8 | yes |
| Walk Back | -1 | yes |
| Dash | 2.5 | no |

The **Animator Actions** script (`Content/Scripts/AnimatorActionsScript.cs`) controls it:

- **Up / Down** walk forward / back. **Shift** runs.
- **Left / Right** turn.
- **Space** dashes: `PlayOnce("Dash")`. The script logs `ActionFinished` and the character returns to
  the action it had before.
- **P** pauses or resumes the animation.
- **K** stops it (bind pose) or plays it again.

The script moves the character at a speed that matches each clip's stride, and keeps it within
Leash of its start. The Mixamo bots face their local -Y.

## Posing bones in the editor

Every gameobject with a skinned model lists an **Armature** under it in the Hierarchy, with its bones
nested in skeleton order (Hips > Spine > Spine1 > ...). Expand the gameobject to see it.

- Select a bone and rotate it with the rotation gizmo (E) or the Inspector's **Rotation**. It turns
  about its joint, and its child bones and the mesh follow, in Edit mode too.
- The bone's Inspector **Rotation** is its turn away from the rest pose. **Position** is the joint in
  the world and is read-only. Bones don't scale.
- Rotating the **Armature** turns the whole skeleton about the model origin. Rotating the gameobject
  carries its armature with it.
- **Reset Bone** / **Reset Pose** (Inspector > Bone, or the row's context menu) go back to the rest
  pose. Posed rows show a marker.
- While a skinned gameobject or one of its bones is selected, the viewport draws its armature as lines.
  The selected bone and its children are orange.
- The pose is saved with the scene. During Play it sits on top of the Animator's clip, so a turned
  head stays turned while the character walks. Stop restores the pose from before Play.

Code: `BasicEntity.BonePose` (`Engine/Animation/BonePose.cs`) stores one rotation per posed bone by
name, and `AnimationPlayer.Overrides` applies them before the model-space pass. `BonePose.UpdateAll`
skins posed gameobjects that no Animator or limp Ragdoll skins. `Engine/Entities/BoneObject.cs` is the
selectable proxy the gizmo writes through. Checks: `BoneChecks` in `Tests/Components`.

## Adding an animated model

1. Put the FBX under `Engine/Content` and give it a `Content.mgcb` entry with
   `/processor:SkinnedModelProcessor`. For Mixamo files (centimetres, Y-up) also set
   `RotationX=90`, `Scale=0.01` and `GenerateTangentFrames=True`. Copy the `GameObjects/Player`
   entries.
2. Register it in `Assets.Load` with `LoadSkinnedModel(content, "path/without/extension", graphicsDevice)`
   and a public `ModelDefinition` field, so scenes can reference it by that field name.
3. Place it in a scene and add an **Animator**. Use **Source** for clips stored in another FBX, and
   **Actions** to name the clips your scripts switch between.

Animation-only files (Mixamo **Without Skin**, e.g. `GameObjects/Player/Silly Dancing.fbx`) use the
same entry. Without a skinned mesh, the importer leaves the rig as plain nodes, so the processor makes
the topmost node the clips animate the skeleton root. Use them as an Animator's Source.

Files imported from the Assets panel use the plain `ModelProcessor`. Change their manifest entry
to animate them.

## How it works

- `ContentPipeline/` is a MonoGame pipeline extension (net8.0, because mgcb runs on .NET 8).
  `Engine.csproj` builds it before the content build, and `Content.mgcb` loads it with `/reference`.
- `SkinnedModelProcessor` runs the stock `ModelProcessor` (vertices keep `BlendIndices0` and
  `BlendWeight0`) and stores the skeleton and clips in `Model.Tag`. The engine reads them as
  `Engine.Animation.SkinningData`.
- FBX clips are re-read with Assimp, with pivots merged (`FbxAnimationReader`). MonoGame 3.8.4's
  `FbxImporter` applies each bone's PreRotation to the keys a second time, which flips Mixamo legs
  about 180°.
- `AnimationPlayer` samples a clip (lerp/slerp) into skin matrices. `SkinnedMeshInstance` skins on
  the CPU (in parallel) into per-entity dynamic vertex buffers. `MeshMaterialLibrary` draws those in
  place of the shared buffer (`TransformMatrix.Skin`), so the G-buffer, shadow, forward, ID and
  outline passes need no skinned shaders.
- Cost: about 2 ms per frame for a 35k-vertex Mixamo character. A clip that has finished, plays at
  Speed 0, or is paused is not re-skinned. A crossfade evaluates both clips and blends them with
  `AnimationPlayer.BlendFrom` (decompose, lerp/slerp per bone), which adds little to the skinning.
- Picking, SDFs and baked lighting still use the bind pose (posed bones included). Bones can't be
  picked in the viewport; select them in the Hierarchy.

Checks: `AnimationChecks` in `Tests/Components`. The real-asset and rendering checks need `--graphics`.
