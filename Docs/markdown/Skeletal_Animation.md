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

A clip from another model is matched by bone name. Each bone gets the source bone's rotation
change from its own bind pose, so rigs with slightly different rest poses still line up. The target
keeps its own bone lengths. The root's travel is scaled by the ratio of the two rigs' hip heights.

## Adding an animated model

1. Put the FBX under `Engine/Content` and give it a `Content.mgcb` entry with
   `/processor:SkinnedModelProcessor`. For Mixamo files (centimetres, Y-up) also set
   `RotationX=90`, `Scale=0.01` and `GenerateTangentFrames=True`. Copy the `GameObjects/Player`
   entries.
2. Register it in `Assets.Load` with `LoadSkinnedModel(content, "path/without/extension", graphicsDevice)`
   and a public `ModelDefinition` field, so scenes can reference it by that field name.
3. Place it in a scene and add an **Animator**. Use **Source** for clips stored in another FBX.

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
- Cost: about 2 ms per frame for a 35k-vertex Mixamo character. A clip that has finished, or plays at
  Speed 0, is not re-skinned.
- Picking, SDFs and baked lighting still use the bind pose.

Checks: `AnimationChecks` in `Tests/Components`. The real-asset and rendering checks need `--graphics`.
