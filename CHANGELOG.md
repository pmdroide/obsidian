# Changelog

## Added: Unity-style serialized fields for Script Behaviours

**Why:** Scripts had no per-object settings. Tuning a value meant editing constants in C# and rebuilding, and two gameobjects could not share a script with different values.

- **Serialized fields:** public instance fields and private/protected `[SerializeField]` fields are saved per Script Behaviour attachment and shown in the Inspector. Supported types: `bool`, `int`, `float`, `double`, `string`, enums, `Vector2`, `Vector3`, `Color`.
  - `[NonSerialized]`, `readonly`, `static`, `const` fields and properties are skipped.
  - Base class fields between the script and `ScriptBehaviour` count, and are listed first.
- **Attributes** (new `Engine/Scripting/ScriptFieldAttributes.cs`): `[SerializeField]`, `[HideInInspector]`, `[Range(min, max)]`, `[Tooltip]`, `[FormerlySerializedAs]`.
- **Engine:**
  - New `Engine/Scripting/ScriptFields.cs` finds the fields, nicifies labels (`_moveSpeed` → "Move Speed"), reads C# defaults from one lazily built instance, and encodes values as readable JSON (enums by name, vectors as arrays, colours as `#RRGGBBAA`).
  - `ScriptDefinition` now records the script's type (`ScriptType`, `Fields`).
  - `ScriptBehaviourComponent` has a `Fields` record (saved in `.obsc` files, copied on clone) plus `SetField`, `SetFieldJson`, `GetField<T>`, `ResetField` and `ClearFields`.
  - Values are applied before `Start`. Edits during Play reach the running script before its next `Update`, field by field, so values the script changed itself are kept.
  - Values that no longer fit their field are skipped with a log line. Renamed fields load through `[FormerlySerializedAs]`.
- **Anvil Inspector:** the Script Behaviour panel lists the fields under the script picker, each with its own editor:
  - checkbox, number box, slider for `[Range]`, text box, enum dropdown, X/Y(/Z) boxes, and colour picker;
  - the label shows the `[Tooltip]` on hover, and a reset button appears on changed fields;
  - picking another script shows its fields at once and clears the old values.
  - New `Editor/Anvil/Models/ScriptFieldViewModels.cs`.
- **Samples:** `SpinExampleScript` has **Degrees Per Second** (`[Range(-360, 360)]`, default 45). `LaunchPadScript` has **Up Speed** / **Forward Speed** (defaults 9 / 4, unchanged).
- **Docs:** new "Serialized fields" section in `Docs/markdown/Script_Behaviours.md`; `CLAUDE.md` recipe.
- **Checks:** new `Tests/Components/ScriptFieldChecks.cs` covers:
  - which fields serialize, labels and attributes;
  - encoding, and rejecting bad values;
  - applying before Start, live edits and Reset during Play;
  - stale values and renamed fields;
  - clone and save/load;
  - Inspector display and edits, reset, and switching scripts.
  - `ScriptBehaviourChecks`' counting fixture marks its runtime counters `[NonSerialized]`.

- Claude

## Added: spot lights

**Why:** The engine only had directional and point lights.

- **`SpotLight`** (`Engine/Entities/SpotLight.cs`) is a `PointLight` limited to a cone: **Spot Angle** (full angle, 1-179°) and **Inner** angle (fully lit core, fades out to Spot Angle). Its rotation sets the cone axis; with no rotation it points straight down (-Z). It lives in the scene's `PointLights` list, so selection, delete/copy, Play/Stop and the light volume work as for point lights.
- **Rendering:** a shared cone falloff (`SpotConeFactor`, mirrored by `SpotLight.ConeFactor` on the CPU) is applied in:
  - `DeferredPointLight.fx`: unshadowed, shadowed and SDF-shadowed lighting, and both volumetric glows.
  - `Forward.fx` (transparent materials) and `Froxel.fx` (volumetric fog), through new per-light cone arrays.
  - The probe volume bake (`LightingBakeInput` / `ProbeVolumeBaker`).
  - Point lights pass an outer cosine of -2, so they are unchanged.
- **Shadows:** spot lights reuse the cube shadow map but render only the faces their cone reaches (`ShadowMapRenderModule.LightSeesFace`).
- **Anvil:** **GameObject > Light > Spot Light** and the Hierarchy **+** menu add a spot light. The Inspector shows Type Spot, Radius, and **Spot Angle** / **Inner** sliders. A selected spot light draws its cone in the viewport. The bridge has `EnqueueAddSpotLight`, `EditorObjectKind.SpotLight`, and spot fields on `LightSnapshot`.
- **Scene files:** a new optional `SpotLights` list (rotation quaternion, `SpotAngle`, `InnerSpotAngle` plus the point light fields). Older scenes load unchanged.
- **Docs:** `Docs/markdown/Scene_Lighting_and_Fog.md` and `CLAUDE.md`.
- **Checks:** new `Tests/Components/SpotLightChecks.cs` (cone math, cube face culling, clone, bake, bridge add/snapshot, Inspector edits, Play/Stop, save/load; with `--graphics`, the compiled shaders expose the cone uniforms). `GameObjectMenuChecks` now expects the Spot Light menu entries.

- Claude

## Added: collision and trigger events, raycast interaction, and the CollisionTest sample scene

**Why:** To test collisions properly. Scripts could raycast and push bodies, but nothing told them when gameobjects touched, there were no triggers, a dynamic character tipped over, and there was no way to use objects.

- **Collision and trigger events** for Script Behaviours: `OnCollisionEnter/Stay/Exit(Collision)` and `OnTriggerEnter/Stay/Exit(BasicEntity other)`. They run right after the physics step, on both gameobjects of a pair.
  - `Collision` has the other `GameObject`, `Point`, `Normal` (pointing at the receiver), `Depth` and `ImpactSpeed`.
  - `PhysicsSystem` records each pair's deepest contact through BEPU's narrow-phase callbacks (new `Engine/Physics/PhysicsContact.cs`). Speculative contacts that close within the step count, so a fast impact reports its real speed.
  - `ScenePhysics` turns the contacts into Enter/Stay/Exit, keyed by gameobject pair. A body that falls asleep keeps its contacts. A removed or disabled body raises Exit. Leaving Play forgets contacts silently.
  - Delivered through the new `ScriptBehaviourComponent.Notify`; a throwing hook stops its script.
- **Physics component:**
  - **Is Trigger** overlaps without colliding. Raycasts skip triggers unless `includeTriggers: true`. A Static trigger is built as the model's convex hull (new `PhysicsSystem.AddStaticConvex`), so a body fully inside still counts.
  - **Freeze Rotation** (Dynamic) gives the body infinite inertia, for upright characters.
  - Both appear in Anvil's Inspector and are saved in scenes.
- **Interactable component** (Inspector > Add Component > Interactable, `Engine/Components/InteractableComponent.cs`): Prompt and Range. `ScriptBehaviour.FindInteractable(ray, ...)` finds the interactable the ray points at (blocked by colliders, not by triggers). `Interact(hit)` / `Interact(gameobject)` run the target's new `OnInteract(Interaction)` hook and raise `InteractableComponent.Interacted`. Anvil editor: `InteractableComponentViewModel` plus an Inspector template.
- **New script helpers:**
  - Rays: `MainCamera`, `CameraRay`, `MouseRay`, `ScreenPointToRay(pixel)`, and `Raycast(Ray, ...)`.
  - Contacts and lookup: `IsTouching(other)`, `FindGameObject(name)`.
  - Physics on other gameobjects: `GetVelocity`, `SetVelocity`, `SetAngularVelocity`, `AddImpulse` and `AddImpulseAtPosition`.
- **CollisionTest sample** (`Content/Scenes/CollisionTest.obsc`, last in the scene list; HUD `Content/UI/CollisionTest.xml` + `.css`):
  - A first-person player capsule (Dynamic, 80 kg, Freeze Rotation) moves with a limited acceleration, so the solver does the pushing.
  - Controls: WASD, Shift, Space, hold right mouse / arrows to look, E to use, hold left mouse to carry, F to throw, Q to shove at the hit point, R to reset.
  - Stations:
    - Crates of 10 / 60 / 180 / 2000 kg.
    - A Goal Zone trigger that counts what's inside.
    - A Launch Pad trigger.
    - A lever that slides a Static gate into the floor.
    - A colour-cycling Prize Cube.
    - Dominoes falling onto an Impact Plate that reports impact speeds.
    - A Ball Dispenser dropping balls on a block tower.
  - New scripts in `Content/Scripts`: Collision Test Player, Trigger Zone, Impact Reporter, Launch Pad, Ball Dispenser, Gate Lever, Color Cycle, plus the `CollisionTestFeed` helper (HUD event feed, colour and highlight helpers). Every runtime material or prompt change is restored on Stop.
- **Docs:** new `Docs/markdown/Collisions_and_Interaction.md` (also on the docs website under Gameplay & Input). Updated `Script_Behaviours.md`, `Gameobject_Components.md` and `CLAUDE.md`.
- **Checks:** new `Tests/Components/CollisionChecks.cs`. It covers:
  - Events, impact data and normals on both sides; sleeping contacts; removal; leaving Play.
  - Trigger pass-through and see-through rays; a sleeping body inside a trigger.
  - Freeze Rotation; Interactable range, blocking and disable.
  - Inspector edits, clone and save/load.
  - A scripted Play run of the sample: the launch pad throws the player, the 60 kg crate is pushed, the anchor holds, the lever opens the gate, the dispenser drops a ball, and Stop restores everything.

  All component checks pass. The standalone engine was also launched into the scene: the HUD, contact tracking, impact reports and interactions worked on the real models.
- **Not possible yet:** a see-through trigger volume. `IsTransparent` on a Basic material hides the object, and the ForwardShaded type draws a fixed grey, so the zones are thin glowing (Emissive) pads instead. `EmissiveStrength` only shows on the Emissive material type.

- Claude

## Added: Steam peer-to-peer multiplayer sample with capsule players

**Why:** To test basic peer-to-peer multiplayer through Steam. The engine could connect to Steam, but it had no lobbies or networking yet.

- **MultiplayerTest sample** (`Content/Scenes/MultiplayerTest.obsc`, last in the scene list): a walled 20 m arena. The Main Camera runs the new **Multiplayer Test** script.
  - Each player is a coloured capsule with a dark visor showing its facing. The colour comes from the Steam ID, so every peer sees the same colours.
  - H hosts a public lobby (up to 4 players). J finds a lobby and joins it. I opens the Steam invite dialog, L leaves, and C connects to Steam (the same as Anvil > Window > Steam > Connect).
  - WASD or the left stick moves the player relative to the camera, Space jumps, and holding the right mouse button orbits the follow camera. Without Steam the local capsule still works.
  - Each peer sends its own capsule's position and yaw to every other peer about 20 times a second (unreliable, 21 bytes, out-of-order packets dropped). Remote capsules ease towards the newest state.
  - Vista HUD (`Content/UI/MultiplayerTest.xml` + `.css`) in the top-right, clear of the performance overlay: Steam account, lobby state, status line, and the player list with colour, ping and host.
  - Testing needs two Steam accounts, each in its own Steam client (normally two PCs) with the same App ID (default 480).
- **`Engine/Steam/SteamP2PSession.cs`** (reusable): lobby host/find/join/leave/invite through `SteamMatchmaking`, and peer join/leave events from the lobby member list. Send, broadcast and receive go through `SteamNetworkingMessages` (Valve relay, no open ports). Sessions are accepted only from lobby members. Also reports the connection state and ping per peer, and follows Steam invites and Join Game from the friends list. Searches only match lobbies with the same game key, because App 480 is shared by every developer.
- **`SteamService.Current`** gives scripts the engine's Steam session, and **`SteamService.ConfiguredAppId`** returns the saved App ID.
- **Capsule primitive:** `Content/GameObjects/Default/capsule.obj`, model key `Capsule` (`Assets.Capsule`). It is Z-up, with radius 0.5 and height 2, centred on its origin (the same size as Unity's capsule). It was generated with smooth normals and UVs, and is built with `FbxImporter`. `OpenAssetImporter` produced a material the runtime couldn't read, which crashed the engine at boot. `.gitignore` now allows this file, because its `*.obj` rule is meant for compiler output.
- **Runtime spawning for scripts:** `ScriptBehaviour.Spawn(modelKey, position, name)`, `Destroy(entity)` and `DestroyAllSpawned()`.
  - Spawned gameobjects get `BasicEntity.IsRuntimeSpawned` and are removed when the script stops (like script sounds).
  - Scene saves skip them.
  - Backed by `MainSceneLogic.SpawnRuntimeEntity` and the new `Assets.FindModel(key)`.
- **Docs:** new `Docs/markdown/Steam_Multiplayer.md` (also on the docs website under Gameplay & Input). Updated `Script_Behaviours.md` (sample, spawn helpers), `Engine/thirdparty/steam/README.md`, and `CLAUDE.md`.
- **Checks:** `Tests/Components/MultiplayerChecks.cs` checks the scene, script registration, scene list and HUD files. Offline, it checks that Play spawns the capsule and visor, W moves the capsule relative to the camera, the camera follows, saving during Play leaves spawned objects out, Stop removes them, and model-key lookup works. All component checks pass. The standalone engine was also launched into the scene to check rendering (capsule, shadow, visor, HUD). The two-PC Steam lobby was not tested, because it needs a second Steam account.

- Claude

## Updated: current engine docs and a separate welcome page

- Connected the documentation website directly to all 13 current guides in `Docs/markdown`, grouped into their respective topics. New unmapped Markdown files appear under Reference until assigned a topic.
- Replaced the old example introduction, first steps, and issue reporting pages with Obsidian documentation, and removed placeholder topics from the published navigation.
- Added a separate responsive welcome page with documentation and GitHub links, a crystal illustration, topic shortcuts, and light/dark themes.
- Added shareable hash routes, full-content documentation search, heading navigation, previous/next guides, GitHub source links, and a mobile navigation drawer with keyboard support.
- Resolved links between Markdown guides and converted local code/diagram links into GitHub links. Fixed the logo path for deployments under `/obsidian/`.
- Split the documentation renderer into a separate bundle and updated the website README with source locations, routes, and the Markdown deployment trigger.
- Verified the production build, lint, and GitHub Pages base path. Browser checks passed for all 13 engine guides at desktop, tablet, and phone widths, including routing, search, links, heading navigation, theme persistence, the mobile drawer, and missing-page recovery.

- Codex

## Added: documentation website build and GitHub Pages instructions

- Added `Docs/website/README.md` instructions for dependency installation, local builds and previews, and GitHub Pages deployment using a GitHub Actions workflow.
- Documented the repository base path, the required sidebar logo path change, deployment updates, and adjustments for forks or custom domains.

- Codex

## Added: skeletal animation and the AnimationTest sample scene

**Why:** To test animations with the Mixamo FBX files in `Content/GameObjects/Player`, and to test textures on an animated mesh in the same scene. The engine had no skeletal animation before this.

- **AnimationTest sample** (`Content/Scenes/AnimationTest.obsc`, last in the scene list): open it in Anvil and press Play. The camera has Freecam.
  - X Bot playing its own walk clip, In Place.
  - Y Bot playing the X Bot walk (retargeted by bone name).
  - A textured Y Bot (UV checker albedo + normal map), walking.
  - X Bot with root motion.
  - Y Bot at 0.25 speed.
  - A Y Bot bind-pose reference with no Animator.
  - A cube with the same checker maps.
  - The FBX files have no textures of their own. The checker maps (`GameObjects/Textures/UVChecker_BaseColor.png`, `UVChecker_Normal.png`) were generated for this.
- **`ContentPipeline/`** (new net8.0 project, also in `Engine.slnx`): a MonoGame pipeline extension with `SkinnedModelProcessor`.
  - Stores the skeleton and clips in `Model.Tag`. A file without a skeleton builds exactly like `ModelProcessor`.
  - `Engine.csproj` builds it before the content build, and `Content.mgcb` references it.
  - Re-reads FBX clips with Assimp, with pivots merged. MonoGame 3.8.4's `FbxImporter` applied each bone's PreRotation to the animation keys twice, which flipped Mixamo legs about 180°.
- **`Engine/Animation/`**:
  - `SkinningData` (+ content reader).
  - `AnimationPlayer`: keyframe lerp/slerp, retargeting by bone name using model-space rotation deltas, root height scaling, In Place.
  - `SkinnedMeshInstance`: parallel CPU skinning into per-entity dynamic vertex buffers.
- **Rendering**: `MeshMaterialLibrary.Draw` swaps in an instance's posed vertex buffer (`TransformMatrix.Skin`). The G-buffer, shadow, forward, ID and outline passes all show the pose, with no new shaders.
  - Animated entities flag `HasChanged` each frame, so culling and shadow maps update.
  - Cost: about 2 ms per frame per 35k-vertex character.
- **Animator component** (`AnimatorComponent`, Inspector > Add Component > Animator): Source (content path of another skinned model), Clip, Speed, Loop, In Place.
  - Plays in Play mode. Edit mode and Stop show the bind pose.
- **Assets**: `PlayerYBot` and `PlayerWalking` (`GameObjects/Player`, built Z-up in metres), loaded with their FBX material colours and widened culling bounds.
- Docs: new `Docs/markdown/Skeletal_Animation.md`. `CLAUDE.md`, `Gameobject_Components.md` and `Importing Structure.md` are updated.
- Tests: new `AnimationChecks` in `Tests/Components`.
  - Covers the component and editor, player math, retargeting, the sample scene, and the real assets (the pre-rotation regression, feet on the ground, render output, loop and one-shot timing).
  - All checks pass, including `--graphics`.
- Checked in the standalone game: a screenshot of the scene in Play (all characters animating, shadows following, checker on the skinned mesh). The look is hazy and washed out because of the engine-wide froxel fog and auto exposure. Not yet checked in Anvil's Play mode or Inspector.

- Claude

## Added: MainMenu sample scene, scene list and game UI

**Why:** To test that Vista UI works and can look good in a real game flow, and to give the game a build order of scenes with a fixed startup scene.

- **Scene list** (`Engine/Recources/SceneList.cs`, `Content/System/SceneList.json`): the game's scenes in build order. Index 0 is the startup scene.
  - The standalone game loads entry 0 after the intro video and starts Play (`MainSceneLogic.StartFirstScene`). With an empty list it starts in an empty scene, as before.
  - Edited in **Anvil > Game Settings > Scenes** (Add Scenes..., Remove, Move Up, Move Down). It refuses files outside `Engine/Content`.
  - Shipped list: `MainMenu`, `GodRayTest`, `AutoExposureTest`.
  - The build now copies `Content/Scenes/**` (`.obsc`, `.probes`), all of `Content/UI/**` and `SceneList.json` next to the exe.
- **`GameFlow`** (`Engine/Logic/GameFlow.cs`): script API to load a scene by index or name (queued to the start of the next frame, keeps Play running), `LoadNextScene`, `ReloadScene`, `Quit`, `EscapeQuits`, `PlayStopped`.
  - Inside Anvil, Stop returns to the scene being edited, with its transforms rewound. Opening a scene from the Assets panel during Play still replaces it for good.
- **`GameInput`** (`Engine/Logic/GameInput.cs`): keyboard (native and Anvil-forwarded), mouse and XInput gamepads merged, with pressed edges.
  - `MenuUp/Down/Left/Right` repeat while held; also `MenuConfirm`, `MenuBack`, `AnyInputPressed` and `LastDevice`.
  - Anvil now also forwards the arrows, Enter, Backspace and Tab.
- **`GameUI`** (`Engine/Logic/GameUI.cs`): Vista layers opened by scripts, drawn over the scene and under the debug overlay, scaled from a 1080-high canvas.
  - Layers are closed when Play stops or the scene changes.
  - Saving the XML/CSS in a dev checkout reloads the layer while the game runs.
- **Vista**:
  - Cached styles: only restyled elements are recomputed. A computed style costs about 0.15 ms, and Vista previously computed every element twice per frame.
  - Box geometry is read from the declared CSS, because AngleSharp resolves percentages against its render device rather than the parent box.
  - New CSS: `right`/`bottom` anchoring, left+right stretching, margins, `display: none`, `visibility`, `opacity`, `pointer-events`, linear and radial gradients, borders, `text-align`, `vertical-align` (centres capitals), `letter-spacing`, `line-height`, `text-transform`, `white-space`, word wrap, `text-shadow`, and transitions on opacity, colours, position and size.
  - Premultiplied colours (translucent colours were too bright before).
  - `ReferenceHeight` scaling, `ElementAt` hit testing, `SetClass`, `Invalidate`/`Refresh`, `RuntimeOpacity`/`RuntimeOffset`, a synchronous `Load` with a `Loaded` event, and a shared font registry.
  - Characters a font lacks draw as `?` instead of throwing.
  - AngleSharp.Css throws on every `radial-gradient`, so stylesheets are rewritten on load to a marked linear gradient that reads back as radial.
  - A style that still fails to compute hides its element instead of crashing.
- **UI fonts**: `Fonts/UI/Display`, `Heading`, `Caption` (Bahnschrift) and `Body` (Segoe UI, with arrows), registered as `display`, `heading`, `caption`, `body`.
- **MainMenu sample** (`Content/Scenes/MainMenu.obsc`, `Content/UI/MainMenu.xml/.css`, `Content/Scripts/MainMenuScript.cs`, script "Main Menu" on the Main Camera):
  - The scene is a ring of black obsidian monoliths around an emissive ember core at golden hour, with the camera drifting slowly.
  - Flow: fade from black, **Press any button**, then the main menu (Play, Scenes, Settings, Credits, Quit), with a confirmation dialog for Quit.
  - Play loads the next scene in the list. The Scenes screen lists and loads the whole list.
  - Settings toggle overlay, VSync, FPS cap, TAA, SSAO, bloom, fog, reflections and UI scale live. In Anvil they are restored when Play stops.
  - Keyboard, mouse (hover, click, wheel, right click) and gamepad all work. The footer prompts follow the last device used.
- **Fixed: emissive materials rendered black.** `GBufferRenderModule` wrote the emissive strength into the metallic slot, then overwrote it with `material.Metallic` (0).
- In Play, the dev hotkeys Space (editor mode), L, X and M are off, so they don't collide with game controls. F1 still cycles render modes.
- Escape closes the standalone game only while `GameFlow.EscapeQuits` is true. The menu turns it off and uses Escape for Back.
- Docs: new `Docs/markdown/Scenes_and_Game_Flow.md`. `VistaUI_Architecture.md` is rewritten as a reference for the supported CSS and API. `Script_Behaviours.md` and `CLAUDE.md` are updated.
- Tests: new `GameFlowChecks` and `VistaChecks` in `Tests/Components`.
  - `GameFlowChecks`: the scene list, the MainMenu scene, the Game Settings list editor, scene switching including the return to the edited scene, and menu input.
  - `VistaChecks`: layout, hit testing, transitions, gradients and styles.
  - All checks pass, including `--graphics`.
- Checked in the standalone game: screenshots of every screen at 1280×720; Play loads GodRayTest. Not yet checked in Anvil's Play mode, where only the automated checks cover it.

- Claude

## Fixed: noisy, hazy image

**Why:** The engine looked grainy and washed out. In `GodRayTest`, every surface had salt-and-pepper speckle, the god rays were blotchy, and bloom and chromatic aberration softened the frame. Four causes:
- **Authored roughness was ignored.** `GBufferRenderModule` wrote `GameSettings.m_defaultroughness` (0.5) instead of the material's roughness for every material without a roughness map. So the 0.9–0.95 rough hall got glossy reflections.
- **SSR replaced the reflection instead of blending.** On a ray hit, `DeferredEnvironmentMap.fx` swapped the specular term for the raw scene colour, ignoring SSR's alpha and the `(1 - roughness) × fresnel` weight the cubemap uses. Each jittered ray hit or missed per pixel, giving speckle.
- **Fog shafts aliased.** Each froxel tested the shadow map once, but distant slices are about 10 m deep against 12 m slat spacing. Also, `noise.png` is greyscale, so the froxel X/Y jitter only moved along the diagonal, which left diagonal streaks.
- **Post-processing defaults.** Bloom at threshold 0 with mip strengths of 1 smeared the whole frame. Chromatic aberration at 0.035 left red fringes on edges.

- `GBufferRenderModule.SetMaterialSettings`: uses `material.Roughness`. `m_defaultroughness` now only applies in the default-material debug view.
- `ScreenSpaceReflections.fx`: alpha is coverage only (edge and facing fade). Ray spread uses roughness² instead of roughness.
- `DeferredEnvironmentMap.fx`: `GetSSR` is a depth-aware 5×5 resolve (colour weighted by coverage, firefly weight `1 / (1 + luma / FireflyThreshold)`). The result blends into the cubemap reflection by coverage, with the same roughness/fresnel weight.
- `Froxel.fx`: `GetBlueNoiseJitter` returns three decorrelated values. `ComputeShadowAlongSlice` averages 6 stratified shadow tests through each froxel's depth.
- `GameSettings`: bloom strengths 0.25 / 0.5 / 0.5 / 0.5 / 0.5 (were 0.5 / 1 / 1 / 1 / 1). Chromatic aberration 0 (was 0.035). The vignette no longer turns off when chromatic aberration is 0 (`PostProcessing.fx` skips the fringe sample instead).
- Measured offscreen in `GodRayTest` at 1280×720, six views, still camera:
  - frame-to-frame shimmer went from 0.83 to 0.17 (mean luma change per pixel, out of 255);
  - without TAA, raw grain went from 3.7 to 0.07;
  - RMS contrast went from 64 to 71 (inside the hall) and from 45 to 52 (side view).
- `Docs/markdown/Scene_Lighting_and_Fog.md`: new **Image clarity** section, a fog shadow-sampling note, and updated **Current issues**.
- Effect on other scenes: materials without a roughness map now render with their own roughness, so ones that relied on the 0.5 override will look different.
- `Tests/Components` passes, including `--graphics`. Not yet checked in the Anvil viewport.

- Claude

## Added: day/night cycle tuning guide

- New section **Tuning a scene for the day/night cycle** in `Docs/markdown/Scene_Lighting_and_Fog.md`. It covers:
  - the sun and moon path and timings;
  - how the scene's brightest directional light acts as a template for the cycle light;
  - a table of recommended settings, saying where each is edited (Inspector, Environment panel, Post Processing, or the `.obsc` file);
  - a list of hours to check, with what to look for at each.
- Corrected the **Direct lights** section: Anvil only shows colour, intensity and Cast Shadows for directional lights. The shadow settings are edited in the scene file.

- Claude

## Fixed: lighting flickers when the sun or moon is overhead

**Why:** In `GodRayTest`, sunlight flickered while the sun or moon passed overhead:
- At noon the frame's average brightness jumped by about 13/255 every frame, against about 0.5 at 09:00.
- The cause was shadow acne on surfaces facing the light, such as the ground and roof tops.
  - The shadow map's write bias, `(1 - |normal.z|) * SizeBias`, is almost zero for those surfaces.
  - The soft PCF lookup (filtering 1 and 2) reads texels up to 2–3 away with no bias of its own.
  - So a surface tilted slightly from the light shadowed itself in a noisy pattern, and TAA's per-frame jitter made it shimmer.

- `DeferredDirectionalLight.fx`: `CalcShadowTermSoftPCF` subtracts a slope-scaled receiver bias (`GetSlopeBias`). It is half a shadow texel plus the kernel reach times tan(angle to the light), capped at tan = 4. The texel size comes from a new `ShadowSize` parameter, set in `DirectionalLight.ApplyShader` through `Shaders.deferredDirectionalLightParameter_ShadowSize`.
- After the fix, the per-frame brightness change at noon is 0.09/255, and at midnight 0.1–0.3/255 (it was 3.3).
- Shadow edges stay attached.
- God rays are unchanged: the froxel fog uses its own shadow test in `Froxel.fx`, and the shadow map itself is untouched.

- Claude

## Added: god ray test scene

**Why:** To check that sun and moon shafts (froxel fog) show up across the whole day/night cycle.

- New `Engine/Content/Scenes/GodRayTest.obsc`. It has:
  - a 120 m hall with a slatted roof and east and west louvres, a dark backdrop wall and a reference sphere;
  - a camera 110–230 m away with Freecam, past the fog's 30 m start distance;
  - the day/night cycle on, from 07:00, lasting 4 minutes;
  - a `Sun` template light with intensity 100 and a 260 m shadow box covering the hall.
- Checked by rendering it offscreen at ten hours with default fog, fog on and off. Shafts show at every hour:
  - clear from 08:00 to 15:00;
  - faint at sunrise and sunset;
  - about a third as strong under the moon from 21:00 to 03:00;
  - weakest at 19:00, with the moon low.
- `Tests/Components/SampleSceneChecks.cs` (`RunGodRays`) checks that the scene loads, that the hall fits inside the shadow box, and that the camera is past the fog start.
- `Docs/markdown/Scene_Lighting_and_Fog.md` documents the scene and the results.

- Claude

## Added: scene lighting and fog documentation

- New `Docs/markdown/Scene_Lighting_and_Fog.md`. It covers:
  - what lights a pixel: direct lights, the environment cubemap, baked probes, screen-space effects, froxel fog and exposure;
  - the Environment, Lighting and Post Processing panels, with their defaults;
  - how probes are sampled: normal offset, visibility test and reflection darkening;
  - a checklist for lighting interiors;
  - the `AutoExposureTest` sample scene;
  - a code map.
- Its **Current issues** section lists what is still wrong or missing:
  - indoor god rays are faint because fog is global and starts 30 m from the camera; Fog Volume versus per-scene fog is undecided;
  - Post Processing settings are not saved;
  - fog sparkles at high density, and beams are soft;
  - unbaked interiors are lit by the outdoor cubemap;
  - there is one environment sample per scene, and it can't be edited in Anvil;
  - the bake ignores the day/night cycle and has to be redone by hand;
  - probe lighting is low-frequency, and old `.probes` files have no visibility data;
  - the fog phase change affects existing scenes.

- Claude

## Fixed: probe light leaking through walls, and backwards fog scattering

**Why:** The house interior looked odd: corners and wall edges glowed, there was a bright smear under the ceiling, and walls were blotchy. All of it was baked probe light leaking through geometry thinner than the 0.57 m probe spacing:
- the 0.2 m ceiling let in light from the open attic,
- the 0.3 m walls let in light from the sunlit yard.

Normal-offset sampling only helped along a surface's own axis, not in corners. Separately, there were no sun shafts through the window, for two reasons:
- The sun didn't shine through it.
- The froxel fog's phase function was backwards: looking towards the sun gave the back-scatter minimum (about 18× too dim), and looking away gave the peak.

- **Probe visibility** (a simplified DDGI visibility test):
  - `ProbeVolumeBaker` records each probe's mean free distance along ±X/±Y/±Z from the rays it already traces. Each is cos⁴-weighted and capped at 4 cells.
  - `ProbeVolumeData` has new `DistPos`/`DistNeg` fields. The `.probes` format is now version 2; version-1 files still load and behave as before (unlimited distances).
  - `DeferredEnvironmentMap.fx` `SampleProbeVolume` now blends the 8 surrounding probes by hand. Each probe is weighted by `ProbeVisibility`: a harmonic blend of the axis distances, falling off with (free/d)⁴ once the shaded point lies beyond a wall. It is also weighted by DDGI's smooth backface term. If every neighbour is blocked, it falls back to plain trilinear.
  - `ProbeVolumeData.EvaluateIrradiance` does the same on the CPU, including the half-cell normal offset, so later bake bounces don't leak either. `SampleSH` stays plain trilinear (used for the open-air capture point).
  - `LightingSystem` uploads the distances as `ProbeDistPos`/`ProbeDistNeg`, and `DeferredEnvironmentMapRenderModule` binds them.
  - Measured on the rebaked scene: the south-west corner leak dropped from 0.40 to 0.050, and the top of the east wall from 0.74 to 0.069. Mid-wall is 0.045.
- **Froxel fog phase** (`Froxel.fx`): the Henyey-Greenstein angle now uses the light's travel direction against the direction to the camera, for both the sun and point lights. Looking towards the sun now gets the forward-scatter peak. **This changes every scene's fog:** the glow is now around the sun instead of opposite it.
- `Scenes/AutoExposureTest.obsc`: the sun now comes from the south-east, (-0.6, 0.6, -0.55), so it shines through the east window as well as the doorway. Probes were rebaked with distances and 1024 rays per probe (was 256; the blotches were sampling noise from the small openings), which takes about 15 s.
- **Still open:** with the default fog (density 0.03, which only starts 30 m from the camera), shafts indoors are faint. Fog density is a global Post Processing setting, so fog dense enough for clear indoor beams would also haze the outdoors. That needs a local or per-scene fog, which is not decided yet. At high density the froxel fog also shows sparkle noise on walls (an existing issue).
- `Tests/Components/SampleSceneChecks.cs`: added checks that the bake stores distances, that the corner and ceiling-seam leaks stay under 25% of plain trilinear, that `Visibility` blocks straight and diagonal views through a wall, that distances survive save/load, and that open-air lookups still match plain SH.
- Validation: all component and `--graphics` checks pass (233). Rendered and measured in a hidden engine instance from the reported viewpoint and three others. Not yet looked at in Anvil.

- Claude

## Fixed: house interior in the auto exposure scene was lit like the outdoors

**Why:** Auto exposure was working, but the room was only about 1 stop darker than the sunny outside, so it barely changed. Measured in the running engine:
- Turning off environment mapping made the interior about 7 stops darker. The outdoor reflection cubemap (captured at y = -12) was being applied inside the closed room with nothing blocking it.
- Most of that light was the cubemap's **specular** term: a flat, view-dependent reflection of the bright outdoors on every wall. That caused the milky haze.
- The scene also had no baked probes, so the diffuse ambient was unoccluded too.

- `DeferredEnvironmentMap.fx`: inside a baked probe volume, cubemap reflections are now scaled by *probe irradiance here ÷ probe irradiance at the cubemap's capture point* (luminance, clamped to 1, faded out with the volume's border weight). A closed room no longer reflects the sunny outdoors. SSR is not scaled, because it shows real on-screen geometry.
- `DeferredEnvironmentMap.fx`: probes are sampled half a cell out along the surface normal. A wall's inner face now reads the probes in the room it faces instead of blending in the sunlit ones on the other side, which reduces light leaking through walls.
- `DeferredEnvironmentMapRenderModule.SetProbeVolume(...)` takes the capture position and uploads the probe SH there (`CaptureSHR/G/B`). `Renderer` passes `EnvironmentSample.Position`.
- `ProbeVolumeData.SampleSH(...)`: new method that returns the trilinear SH coefficients at a point. `EvaluateIrradiance` now uses it.
- `Scenes/AutoExposureTest.obsc`: now has a `Lighting` section and a baked `AutoExposureTest.probes` (33×42×14 probes at 0.57 m spacing, about 3 s to bake). The grid is placed so that no probe row falls inside the 0.3 m walls. To rebake, use Inspector > Lighting > Bake. Manual bounds are saved in the scene.
- Result in the running engine (scene HDR log-average luminance and adapted EV, with defaults Min -3 / Max +3):

  | View | Before | After |
  | --- | --- | --- |
  | Outside | -1.8 log2, EV -1.8 | -1.6 log2, EV -2.0 |
  | Inside, facing the back wall | -2.8 log2, EV -0.65 | -4.3 log2, EV +1.2 |
  | Inside, facing the doorway | -2.6 log2, EV -0.9 | -4.8 log2, EV +1.7, doorway blows out |

- Scenes without a probe bake are unchanged. Interiors in other scenes need a bake to get this occlusion.
- `Tests/Components/SampleSceneChecks.cs`: added checks that the scene ships probes covering the house and the capture point, that `SampleSH` matches `EvaluateIrradiance`, and that the room's probes see under 25% of the capture point's light towards the back and west walls.
- Validation: all component and `--graphics` checks pass (229). The scene was rendered and measured in a hidden engine instance. It has not been looked at in Anvil itself.

- Claude

## Added: Freecam script and Script Behaviours on the Main Camera

- New `Engine/Content/Scripts/FreecamScript.cs`, registered as **Freecam** (`freecam`). In Play mode: hold right mouse to look, W/A/S/D to move, E/Q for up/down, Shift for 4× speed, and scroll while holding right mouse to change speed. Movement eases in and out, pitch stops short of vertical, and it starts from the camera's saved view.
- The Main Camera can now hold Script Behaviours. Select **Main Camera** in the Hierarchy, then **Add Component > Script Behaviour**; on the camera it defaults to Freecam. Only Script Behaviours are offered, because other components need a gameobject.
  - `Camera`: `Components`, `AddComponent`/`RemoveComponent`/`GetComponent(s)`, `SupportsComponent` and `HasActiveScript`.
  - `ScriptBehaviourComponent.HostCamera`: camera-hosted scripts get a `null` owner.
  - `ScriptBehaviour.Camera`: on a camera, the transform helpers (`Position`, `Rotation`, `Forward`, `LookAt`, ...) move and turn the camera. `Raycast` and sounds work; physics-body and Audio-component helpers do nothing there.
  - `EditorBridge`: id -1 (`MainCameraId`) routes component add/edit/remove to the main camera, and the camera's snapshot carries its components.
  - `SceneSerialization`: `MainCamera.Components` is saved and loaded. Older scenes load with none.
  - `PlayModeController` starts, updates and stops camera scripts. **Stop now restores the main camera's position and direction.** Before, anything that moved the game camera in Play also changed the saved camera.
  - `Input`: the built-in Play camera controls are skipped while an enabled script is on the camera, so the two don't fight.
  - Anvil: the Main Camera shows **Add Component** (Script Behaviour only) and no Role.
- `Scenes/AutoExposureTest.obsc`: Freecam is attached to the camera, so Play flies in and out of the house.
- Docs: "Scripts on the Main Camera" section in `Docs/markdown/Script_Behaviours.md` (Freecam controls, helpers on a camera), plus a note in `CLAUDE.md`.
- Added `Tests/Components/FreecamChecks.cs`:
  - Add via the Inspector, defaulting to Freecam, and refusal of non-script components.
  - Flying forward, Shift speed, rising with E, easing to a stop, mouse look without roll, and the pitch limit.
  - Enable/disable from the Inspector, and Stop restoring the camera.
  - Save/load and removal.
- Validation: the solution builds, and all component checks pass. Not yet flown in the running editor.

- Claude

## Added: auto exposure sample scene

- New `Engine/Content/Scenes/AutoExposureTest.obsc` for testing eye adaptation by walking between bright sunlight and a dark interior. It is built from the built-in `Cube` and `IsoSphere` meshes:
  - A 120 m grass ground, and a plaster house with an 8 x 6 m room and 3.5 m walls.
  - A doorway in the south wall (1.5 x 2.4 m) and a window in the east wall, so you can look out from inside and in from outside.
  - A ceiling slab sealing the room, with a pitched red roof on top.
  - A table, ball and crate inside, and a white post outside.
  - A bright shadow-casting sun (intensity 100), and a dim warm interior lamp (intensity 3, radius 7).
  - The day/night cycle is off at 13:00. The game camera stands 14 m south, facing the doorway.
- Anvil: double-clicking a `.obsc` file in the Assets panel now opens that scene (`MainWindowViewModel.OpenSceneAsset`). Before, it did nothing.
- Added `Tests/Components/SampleSceneChecks.cs`. It checks that the scene loads every object, the sun is bright and the lamp dim, the room is closed apart from the door and window, and double-clicking a scene asset queues it to load.
- Validation: all component checks pass. Not yet opened in Anvil, so the look and the amount of exposure change are untested.

- Claude

## Fixed: GameObject menu in the Anvil title bar did nothing

**Why:** The **GameObject** menu items had no commands. `MainWindowViewModel` already had `AddEntity`, `AddDirectionalLight` and `AddPointLight` commands, but nothing used them.

- `MainWindow.axaml`: **GameObject > 3D Object > Cube / Sphere** and **GameObject > Light > Directional Light / Point Light** now create the object at the camera focus through the editor bridge. Sphere uses the built-in `IsoSphere` mesh.
- Removed **Create Empty** and **Capsule**. Every gameobject needs a mesh, so the engine has no empty gameobject, and there is no capsule mesh in Content.
- `MainWindowViewModel`: the Hierarchy **+** menu now runs the same commands and lists the same four objects. It used to offer only Point Light. New directional lights use intensity 100, which matches the starter scene's sun; the old value of 1 was too dim to see.
- Added `Tests/Components/GameObjectMenuChecks.cs`: the menu commands queue on the game thread and create a cube, a sphere and both light types, and the **+** menu matches.
- Validation: Anvil builds, and all component checks pass. Not yet clicked through in the running editor.

- Claude

## Added: built-in transform, physics and audio helpers for Script Behaviours

- `ScriptBehaviour` (`Engine/Scripting/ScriptBehaviour.cs`) now has helpers for the gameobject it runs on:
  - **Transform:** `Position`, `Rotation`, `Scale`, `Right`/`Forward`/`Up` (local +X/+Y/+Z), `Translate`, `Rotate` (degrees, world or local axis), `SetRotation`, `LookAt`, `TransformPoint`, `TransformDirection`.
  - **Physics:** `Physics`, `HasRigidbody`, `Velocity`, `AngularVelocity`, `AddForce`, `AddForceAtPosition`, `AddImpulse`, `AddTorque`, `AddAngularImpulse`, `Raycast` (ignores the caster and returns a `RaycastHit` with gameobject, point, normal and distance). Without a Dynamic body these do nothing and getters return zero.
  - **Audio:** `PlayAudio`/`StopAudio`/`IsAudioPlaying` for the gameobject's Audio component. `PlaySound` (2D), `PlaySound3D` (follows the object) and `PlaySoundAt` (fixed point) return a new `ScriptSound` handle (`Volume`, `Pitch`, `Loop`, `Paused`, `Stop`). `StopAllSounds` stops them. Sounds a script starts stop automatically when the script stops.
  - `GetComponent<T>()` and `Log(message)`.
- `PhysicsSystem`: added `ApplyAngularImpulse`, `SetBodyVelocity` and a closest-hit `RayCast`.
- `ScenePhysics`: maps BEPU handles back to gameobjects for raycasts. Added per-entity `TryGetVelocity`/`SetVelocity`/`ApplyImpulse`/`ApplyAngularImpulse`, a static `Current` and the `RaycastHit` struct. `BasicEntity.PhysicsScene` records which `ScenePhysics` owns its body.
- `ScriptBehaviourComponent` stops a script through `ScriptBehaviour.Shutdown()`, which runs `Stop()` and then releases the script's sounds.
- `Docs/markdown/Script_Behaviours.md`: new "Built-in helpers" section with reference tables, units, and hover and engine-sound example scripts. Both examples compile against the engine.
- `Tests/Components/ScriptBehaviourChecks.cs`: 16 new checks covering transform math, physics on a real BEPU body (velocity, impulse, force × DeltaTime, torque, off-centre force, raycast hit and miss), and audio/physics helpers being safe to call without a body or audio device.
- Validation: the engine builds, and all component checks pass. Not tried in a running Play session, and audio playback was not tested with FMOD installed.

- Claude

## Fixed: clipped Inspector header tabs

**Why:** The Inspector's **Selection / Post FX / Lighting** buttons used a `toolbarChip` class that had no style. They rendered as full-size default buttons (32px minimum height) in the 20px header row, so they were cut off top and bottom and ran past the right edge of the 260px column. The active tab was not highlighted.

- `MainWindow.axaml`: the tabs now use the existing compact `pill` style, which highlights the active view. They share the header width equally (`UniformGrid`) beside the gear icon.
- The "Inspector" label is removed from this header because the column cannot fit it beside three readable tabs. The gear icon keeps an "Inspector" tooltip.
- Validation: Anvil compiles (built to a scratch folder, because the running Anvil locked its output). Not yet checked by eye in Anvil.

- Claude

## Added: eye adaptation / auto exposure

- New post-processing effect: the image's exposure now follows how bright the scene is, so the view brightens slowly after moving into a dark area and darkens quickly when stepping into light.
- `Shaders/PostProcessing/AutoExposure.fx` + `AutoExposureFilter` (`RenderModules/PostProcessingFilters`): each frame the HDR image is metered at 256x256 as a center-weighted average of log2 luminance. That average is reduced to 1x1 (256 -> 64 -> 16 -> 4 -> 1), and a 1x1 EV value moves toward the target that maps it to the key value. The value stays on the GPU (no readback stall). NaN/Inf pixels are ignored, and the first frame (or re-enabling) snaps instead of fading.
- `PostProcessing.fx` multiplies its exposure by `exp2` of the adapted EV. The manual **Exposure** and the day/night exposure offset still apply on top as compensation, so nights still read as night.
- `Renderer.Draw` meters once per frame after bloom (`UpdateAutoExposure`). `RenderMode` runs twice per frame, so metering there would adapt twice.
- New `GameSettings`: `g_AutoExposure` (on by default), `g_AutoExposureKey` (0.18), `g_AutoExposureMin`/`Max` (-3/+3 EV), `g_AutoExposureSpeedDarkToLight` (3/s), `g_AutoExposureSpeedLightToDark` (1/s), `g_AutoExposureCenterWeight` (0.5).
- Anvil: new **Eye Adaptation** section in the Post Processing inspector with all of the above.
- Added `Tests/Components/AutoExposureChecks.cs` (`--graphics`). It checks the first-frame snap, gradual adaptation both ways at the right speeds, settling, the EV clamp, and that NaN/Inf pixels are ignored.
- Note: the bloom threshold is still applied before exposure, so it does not follow the adapted exposure.
- Validation: solution build passes, and all component checks pass including `--graphics`. The standalone engine ran for 25 s without errors. Not yet checked by eye in the running engine or Anvil.

- Claude

## Fixed: Add Component adding to the wrong gameobject

**Why:** The Inspector's **Add Component** button reused one `MenuFlyout`. Its menu items kept running the first gameobject's add commands after the selection changed. The engine, bridge and reconciler addressed the right object; the wrong id came from the stale menu items.

- `MainWindow.axaml`: the Add Component button no longer has a `Button.Flyout`. It uses `Click="AddComponentButton_Click"`.
- `MainWindow.axaml.cs`: new `AddComponentButton_Click` builds a new `MenuFlyout` on every click from the `AddableComponents` of the gameobject shown in the Inspector, and shows it at the button.
- Two earlier tries in this session did not fix it: rebinding the flyout's items when it opened (the menu then stayed closed, because a `MenuFlyout` with no items never opens), and when the selection changed (the old items still ran).
- Validation: Anvil compiles (built to a scratch folder, because the running Anvil locked its output). All component checks pass. Not yet checked by eye in Anvil.

- Claude

## Added: deforming water, buoyancy, and gameobject roles

- **Water deforms:** four Gerstner swell waves now move the mesh in `Water.fx`. The surface rises and falls, and crests sharpen as points move sideways. `WaterRenderModule` subdivides each water mesh part once (up to about 131k triangles) and `MeshMaterialLibrary` draws that copy, so a flattened Cube or plane has vertices to move. Swell waves shorter than three grid cells fade out on very large meshes. Eight shorter ripple waves still shape the normal only.
- Squeezed and high crests gather foam, and sunlight through thin crests tints them turquoise. Foam, depth colour and shore effects follow the moving surface.
- New material setting **Wave Height** (crest-to-trough swell in metres, default 0.5, max 10). It saves with the Material component, is shown in the inspector, and is set by the water example. Existing water picks up the default swell. **Wave Strength** is now labelled **Ripple Strength**.
- **Roles:** every gameobject has a `Role` (`Default` or `Water`), chosen in the Inspector under the name. It saves by name in scenes, and older scenes load as Default. Copies keep it, and it travels through the editor snapshot. New `IEditorBridge.EnqueueSetRole` marks the scene dirty. **Apply Water Example** also sets the Water role.
- A Water-role object is a water volume: its XY footprint, from the top of its bounds downward. It builds no physics collider. With a Water material, its surface follows the same waves the shader draws (`WaterWaves.cs` mirrors the shader's swell).
- **Buoyancy:** the Physics component has a **Buoyancy** option for Dynamic bodies, with **Float** (push when submerged relative to weight, default 2 = floats half-submerged) and **Drag** (default 3). `ScenePhysics` samples each buoyant body's bounds as a 3x3x3 grid before each Play step. It pushes up the submerged cells at their own positions and slows them, so bodies right themselves and ride the swell. Physics now receives the game clock so it matches the shader.
- Added `Tests/Components/WaterChecks.cs`. It covers wave height lookup, settling half-submerged, sinking, riding the swell, and role and buoyancy editing, cloning and save/load, including older scenes. GPU checks now confirm water meshes are subdivided and that the swell moves the geometry.
- Updated `Material_Component.md`, `Gameobject_Components.md` and the CLAUDE.md recipes.
- Validation: solution build passes. All 182 component checks pass, including `--graphics`. Not yet checked by eye in the running engine or Anvil.

- Claude

## Fixed: ghosting trails behind meshes when the camera moves

**Why:** The TAA pass blended 93.75% of the reprojected previous frame into every pixel and never rejected history that no longer matched. Background that an object edge had just uncovered kept the object's old colour for about 16 frames, which left an afterimage.

- `TemporalAntiAliasing.fx` now clips the history colour to the current frame's 3x3 neighbourhood (variance clipping in YCoCg, bounded by the min/max box) before blending. Stale history is pulled back into the colour range that is visible now.
- Reprojection uses the closest depth in the 3x3 neighbourhood, so silhouette pixels move with the foreground object instead of the background.
- Neighbourhood loads are clamped to the screen edge. Removed the commented-out legacy overlap/depth-rejection experiments.
- Validation: the engine build compiles the shader. Not yet checked in the running engine.

- Claude

## Updated: script assets and multiple scripts per gameobject

- Moved the compiled example source to `Engine/Content/Scripts/SpinExampleScript.cs`. It now appears in Anvil's Assets > Scripts folder and uses the existing `.cs` double-click editor launcher (VS Code, with Notepad fallback). Script sources are copied into build and publish Content folders.
- Gameobjects can now attach multiple Script Behaviour components, including multiple independent instances of the same script. Each attachment has its own script selection, Enabled checkbox, and removal command; Add Component remains available for adding more scripts.
- Added generic repeatable component registration and persisted attachment IDs. Inspector reconciliation and bridge mutations target each attachment by ID, so removing a middle script or receiving a stale editor event cannot edit a neighbouring script. Cloning gives every copied attachment a fresh ID. Other component types retain their single-attachment limit; scenes without attachment IDs still load.
- Updated script authoring and component documentation. Added integration checks for asset visibility and editor routing, copied script source, multiple script instances on one gameobject, independent edits/disable/removal, error isolation, focused inspector stability, cloning, scene persistence and older records.
- Validation: engine and editor builds and the full component integration suite pass. Existing build warnings remain.

- Codex

## Added: attachable C# Script Behaviour component

- Added **Inspector > Add Component > Script Behaviour** with an Enabled checkbox, script picker, and removal. Script IDs and Enabled settings save in scenes and clone independently through the existing component registry and editor bridge.
- Added `ScriptBehaviour` with `GameObject`, frame delta in seconds (`DeltaTime`), and `Start()`, `Update()`, and optional `Stop()` hooks. `ScriptRegistry` maps stable scene IDs to compiled C# behaviours; each object and Play session receives a fresh instance. Scripts added or re-enabled during Play start before updating. Script changes, disabling, removal and stopping release the old instance. Missing script IDs and hook exceptions are logged and prevent repeated failing updates.
- Added `SpinExampleScript`: `Start` captures the initial rotation; `Update` rotates around Z at 45 degrees per second using delta time. Stop restores the editor transform through the existing Play snapshot.
- Play-mode component iteration now tolerates scripts removing components during hooks.
- Added script authoring and attachment documentation in `Docs/markdown/Script_Behaviours.md`, linked from the gameobject component guide.
- Verified the solution build and full component integration suite, including the editor build, script picker edits, clone independence, scene save/load, Play lifecycle, runtime attachment, example rotation, error isolation, and self-removal. Existing build warnings remain.

- Codex

## Fixed: Audio components were silent in the editor

**Why:** `BasicEntity.IsEnabled` defaulted to `false` and nothing ever set it to `true`. The renderer ignores the flag, so objects looked normal. But `AudioComponent.Play`, Play-mode component start/update and ray picking all skip disabled entities, so the inspector's Play button and Play on Start did nothing. FMOD itself was fine (the log shows 2.02.37, matched). A second problem: 3D audio used a 1-unit minimum distance, so even playing sounds were −14 dB at the 5 units where new objects spawn and −26 dB at 20 units (measured with FMOD's channel audibility).

- `BasicEntity.IsEnabled` now defaults to `true`.
- Scene format is now version 2. Version-1 scenes load their entities as enabled, because the saved `false` was the stuck default. Version-1 files still load; version-2 files keep deliberately disabled objects.
- Audio component: new **Min Dist** (default 10, full volume inside it) and **Max Dist** (default 1000) settings, shown in the inspector when 3D Spatial is on. They apply live to a playing sound and save with the scene.
- Added checks for new entities starting enabled, version-1 migration versus deliberate disable, falloff distance edits/persistence, and (with `--audio`) real FMOD playback at full volume from a freshly spawned object 5 units from the listener. The solution build and the full suite pass, with `--audio` and `--graphics`.

- Claude

## Added: Physics component; fixed components landing on the wrong gameobject

**Why components went to the wrong object:** loading a scene gave each entity its saved ID, but the viewport's ID/picking pass kept the temporary ID the entity got on construction (`WorldTransform.Id`). After loading a saved scene, clicking one object could select a different one, so **Add Component** and inspector edits went to that other object.

- Fixed: `BasicEntity.Id` now updates `WorldTransform.Id`, so picking, outlines and selection use the saved ID. Scene load also gives a new ID to entities with a missing or duplicate saved ID, and to the environment probe, so two objects never share one.
- Fixed: the Material and Light color pickers bound to "whatever is selected now". With a picker open, selecting another object sent color edits to it. Pickers now stay attached to the component that opened them (`OwnedFlyout_Opening`).
- Fixed: the Material shader ComboBox could send an invalid `-1` type while its template switched, which reset the material to Basic.
- Fixed: when a saved ID is reused by a different kind of object after a scene swap, the inspector now creates new editors for it instead of reusing the old object's.
- **Physics is now a component** (`Engine/Components/PhysicsComponent.cs`): Inspector > Add Component > Physics, with Enabled, Body (Static/Dynamic, default Dynamic) and Mass. The old fixed Physics section, `PhysicsInfo` and `PhysicsSnapshot` were removed. `BasicEntity.PhysicsType`/`Mass` are now read-only values taken from the component, and a disabled component means no body. Scenes save physics in `Components`; older scenes with an entity-level `Physics` record load it as the component.
- Component system cleanup: `BasicEntity.GetComponent<T>()`, `AddComponent` (rejects duplicates) and `RemoveComponent`; `GameComponent.OnAdded`/`OnRemoved` hooks; registry definitions can take a per-gameobject factory (Material uses `MaterialComponent.FromOwner`). The Material special cases in `EditorBridge` are gone. The registry also rejects duplicate IDs and looks up types directly. Add Component order: Material, Physics, Audio.
- Documented how to add a component in `CLAUDE.md` ("Adding a Gameobject Component").
- Added checks for physics (add once, inspector edits, mass clamp, disable, clone, save/load, legacy record migration, remove), persisted IDs matching picking IDs, duplicate ID reassignment, components staying on their own gameobject, and editor reset on ID reuse. The solution build and the full component suite pass, including `--graphics`.

- Claude

## Added: day/night sky settings; reworked water that darkens at night

Why night still looked lit: offscreen captures of the real engine showed night values in the HDR buffer were already dark (night water was about 0.04 in blue). The tonemapper then lifted them a lot (to 118/255), because the Hejl filmic curve already returns display-ready values and the shader applies gamma 2.2 again. Water also added a fixed ambient term, and it sampled the reflection cubemap upside down: the capture stores Z flipped and the deferred shader corrects for that, but the water shader did not. So the water reflected the ground under the probe instead of the sky.

**Sky settings** (Environment inspector, day/night mode; saved with the scene, older scenes get the defaults):
- **Sky** colors: day sky, day horizon, sunset, night sky, night horizon. They drive the procedural sky in [DeferredEnvironmentMap.fx](Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx).
- **Sun, moon and stars:** sun brightness and size, moon brightness and size, and star brightness. Sun and moon brightness also scale the cycle's sun and moon light.
- **Exposure:** day (default −1 EV) and night (default −2.5 EV) offsets, blended by daylight and added to the Post FX exposure (`EnvironmentSky.ExposureOffset`, applied each frame in `Renderer.Draw`). Nights now look dark and noon is less washed out. The offset is zero when the cycle is off.
- **Reset sky look** restores colors, sun/moon/stars and exposure, and keeps the time and cloud settings.
- The reflection capture now stores the procedural sky at the brightness the main view shows it (half of what it was), so reflections are never brighter than the sky. Static skybox scenes are unchanged.

**Water** ([Water.fx](Engine/Content/Shaders/Forward/Water.fx), [WaterRenderModule.cs](Engine/Renderer/RenderModules/WaterRenderModule.cs)):
- Lighting now comes only from the cycle's light and the captured sky. The fixed ambient term is gone, so water follows the time of day. Cubemap lookups are flipped to match the capture, and reflections stay above the horizon.
- Eight directional waves that move at real deep-water speeds. Short waves fade out with distance to avoid shimmer.
- Depth-aware: the renderer passes the G-buffer depth, so shallow water lets the scene show through and turns turquoise (red light is absorbed first), while deep water takes the surface color. Edges where geometry meets the surface are softened.
- Shore foam and wave-crest foam, lit like a white surface, so it dims at night. A sun/moon highlight, and a backlit glow on wave crests.
- New material parameters: **Clarity** (visible depth in metres, default 4) and **Foam** (0–1, default 0.5), in `MaterialComponent`/`MaterialEffect` and in the water section of the Material inspector. The water example sets both.
- Added checks for sky settings (inspector, save/load, defaults, normalization, reset), day and night exposure, moon brightness, sky color, night water versus day water, shallow versus deep water, shore foam, and the Clarity/Foam round trip. The solution build and the full component suite pass, including `--graphics`. Noon, dawn, dusk and midnight were also checked in renders of the real engine with water, a ground slab and objects crossing the surface.

- Claude

## Added: procedural clouds for the day/night sky

- New shader include [clouds.fx](Engine/Content/Shaders/Common/clouds.fx), used by the day/night sky in [DeferredEnvironmentMap.fx](Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx). It draws one cloud layer from distorted value noise projected onto a flat plane above the camera. Clouds fade out toward the horizon and are drawn last, so they cover the sun, moon and stars.
- Lighting follows the cycle. Clouds are white at noon, warm at sunrise and sunset, and dark blue-grey at night with faint moonlight. They darken where more cloud lies toward the light, have darker cores, and get a bright edge when you look toward the sun or moon. Full overcast keeps visible structure instead of a flat grey sheet.
- Added **Cloud coverage** (0 clear to 1 overcast, default 0.45) and **Cloud speed** (0–10, default 1) to `EnvironmentSettings`. They save with the scene, older scenes load with the defaults, and they appear as sliders in the Environment inspector's day/night section. Heavy cover also dims the cycle's sun and moon light, down to 50% at full overcast.
- Cloud drift builds up in `EnvironmentSky` and wraps at the noise's 256-cell repeat period, so it never loses precision or jumps. The drift is passed to the shader through `DeferredEnvironmentMapRenderModule.SetClouds`.
- Only changing the cycle toggle, starting hour or duration restarts the cycle. Before, any environment edit (including the new cloud sliders) reset the time of day.
- Added checks for cloud settings in the inspector, save/load, older-scene defaults and invalid values. Rendering checks cover clouds drifting without restarting the cycle, overcast covering the noon sky, overcast halving sunlight, and night clouds staying dark and cool. Noon, sunrise, dusk, midnight and four coverage levels were also checked visually in offscreen renders.

- Claude

## Fixed: day/night cycle nights stayed lit and warm

Nights stayed bright because the scene's own sun (intensity 100, white, shadowed) was still rendered next to the cycle's sun. The cycle's sun only reached intensity 3, so the fixed scene sun did nearly all of the lighting at every hour. The baked probe ambient also kept the daytime bounce light at night.

- While the cycle is on, its light replaces the scene's directional lights for rendering ([EnvironmentSky.cs](Engine/Renderer/EnvironmentSky.cs)). The saved lights are not changed and still show as editor gizmos. The cycle light takes its peak intensity and shadow settings from the brightest enabled scene directional light. With no scene light, it falls back to intensity 100 with the default shadow settings.
- Added a moon opposite the sun (`EnvironmentSettings.MoonDirection`). Below the horizon the same shadowed light becomes cool blue moonlight at 8% of the sun's peak intensity. The sun fades to zero at the horizon, so the switch from sun to moon is not visible.
- Baked probe ambient is scaled from 100% at day down to 8% at night (`EnvironmentSky.AmbientScale`, applied in `Renderer.DrawEnvironmentMap`).
- The procedural sky now draws a faint moon disc and glow ([DeferredEnvironmentMap.fx](Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx)).
- Fixed directional lights without shadows not updating their view-space direction while the camera was still. A moving sun now changes the lighting even when the camera does not move ([LightAccumulationModule.cs](Engine/Renderer/RenderModules/DeferredLighting/LightAccumulationModule.cs)).
- Added checks for the moon direction, scene-sun replacement, moonlight intensity/colour/shadows, night ambient and noon intensity. The solution build and the component suite pass, including `--graphics`.

- Claude

## Added: Input device overview and connected controller list

- Added **Window > Input**, opening an Inspector section with current keyboard, mouse, controller, touch, and pen availability. Connected controllers show their player slot, name/type, and supported sticks, triggers, D-pad, and vibration.
- Discover keyboard/mouse devices through Windows Raw Input, touch/pen readiness through Windows digitizer capabilities, and Xbox-compatible controllers through MonoGame's WindowsDX GamePad API. Poll twice per second on the game thread, including unfocused frames, and publish immutable snapshots through the editor bridge.
- Update controller rows as devices connect, disconnect, or change; retain unchanged rows and show explicit waiting, empty, and detection-error states.
- Verified the editor build and full component integration suite, including simulated controller connection/disconnection, snapshot independence, stable rows, detection errors/recovery, and a native Windows scan. The live scan found keyboard/mouse devices and no connected controller.

- Codex

## Updated: configurable Steam App ID and persistent enable preference

- Added an editable App ID and Apply button to **Window > Steam**, plus an Enable Steam toggle. Steam now defaults to off, with Spacewar (480) as the initial test ID.
- Save the App ID and enable preference to `Engine/Content/System/SteamSettings.json`. Reopening restores both settings and connects only when enabled; closing the editor keeps the saved preference. Connect/Disconnect also persist the enabled/disabled choice.
- Applying an ID while enabled releases the current session and reconnects using the new ID, including updating the output development app ID file. Validate IDs, preserve unsaved input during status refreshes, and report save failures without changing the saved configuration.
- Added checks for first launch without native initialization, custom IDs reaching initialization, enabled/disabled reopen behavior, queued controls, reconnects after ID changes, failed connections, invalid settings, and save errors.
- Verified the editor build and complete component integration suite, including a live Spacewar connection; the project's saved default remains disabled.

- Codex

## Added: Steam connection panel using Spacewar (480)

- Added **Window > Steam** to the Anvil editor. The Inspector section shows the Steam account, Steam ID, online/offline connection status, errors, and Connect/Disconnect controls.
- Added an engine-owned Steam service that connects before renderer initialization, pumps callbacks every update even while unfocused, and shuts down with the engine. Editor connection requests run through the game-thread bridge and status uses immutable snapshots.
- Included the pinned Steamworks.NET 2025.164.1 Windows x64 wrapper, native Steam API, license, and development app ID under `Engine/thirdparty/steam`. Shared reference imports register the wrapper in each executable's dependency manifest; builds copy the native DLL and app ID, while publishing excludes the development app ID file.
- Documented the extension points for Steam achievements, matchmaking lobbies, and peer networking. This change establishes the connection; achievements and lobby gameplay remain future work.
- Verified the editor build, existing component integration checks, Steam lifecycle/queued UI checks, and a live native Steam connection with app ID 480.

- Codex

## Added: Environment skybox and day/night controls

- Replaced the viewport's inactive View label with an Environment button that opens an Inspector section. Choose a custom PNG/JPEG panorama, reset the default sky, or select a day/night cycle with a starting hour and duration.
- Added a procedural sky with sunrise, sunset, stars, and a moving sun light in Edit and Play. Reflection captures refresh twice per second during the cycle; existing scene lights remain editable.
- Saved environment settings with scenes, retained default skies for older scenes, and copied imported panoramas into Content so they survive rebuilds. Runtime textures are replaced and disposed on the game thread.
- Verified editor/shader builds, scene persistence and legacy defaults, queued controls, panorama rendering, day/night output, reset, and texture disposal with integration and DirectX checks.

## Added: texture slots and a separate Shader section in Material

- Added a Textures section with Base Color, Normal, Roughness, Metallic, Mask, and Displacement slots. Choose Content images with the file picker or drag textures from Assets; Clear removes a map and Reset restores inheritance. Assignments persist in scenes and duplicate independently.
- Moved shader selection, the water preset, and wave settings into a separate Shader section.
- Applied maps to owned material instances through the game-thread component bridge, using shared content-managed textures. Assigned roughness/metallic maps now affect the G-buffer independently; clearing maps also resets their renderer flags.
- Fixed mask technique selection without a roughness map and metallic texture binding without a normal map. Added persistence, drag/drop, cloning, texture-loading, removal, and G-buffer checks; documented texture workflow and shader choices.

## Fixed: one addable Material inspector using the original controls

- Converted the original `MaterialInfo` inspector into the registered Material component editor, reusing its color picker, roughness/metallic/emission/opacity sliders, transparency checkbox, and existing material-type selector. Water and its wave controls are part of this same component.
- Removed the separate `MaterialComponentViewModel` and automatic base-material inspector. Selecting a mesh no longer shows two material sections; **Add Component > Material** adds the single section and copies the object's current material settings.
- Preserved the original material types in component serialization and rendering, and retained compatibility with earlier water component records. Removing the component removes its inspector and restores the source surface.
- Updated component documentation and integration checks for the reused editor, existing material settings/types, and older water records.

## Added: Material component with a water shader example

- Added `MaterialComponent` and its Anvil inspector editor. **Add Component > Material** offers Standard/Water shader selection, color, roughness, metallic, emission, shadow settings, and water opacity/wave controls. **Apply Water Example** supplies a teal preset. Component settings save in scenes and clone independently.
- Added `Shaders/Forward/Water.fx` to the content pipeline and `WaterRenderModule` to the renderer. Water uses animated procedural normals, Fresnel, environment cubemap reflections, and directional highlights. It renders with alpha blending and depth testing, without depth writes, before TAA/bloom, and animates in Edit and Play modes.
- Added per-object material instances that retain imported maps. Standard materials tint albedo textures and override roughness/metallic through the G-buffer. Disabling/removing the component restores the source materials; edits, deletions, scene swaps, and unload dispose owned instances without disposing shared assets.
- Fixed material-library sort pointers when adding/removing batches, needed when inspector edits replace material instances.
- Added material persistence/inspector/clone checks and optional hidden-window WindowsDX graphics checks for water pixels, animation, reflections, depth occlusion, zero opacity, Standard tint/roughness, and material disposal. Documented usage and example limitations in `Docs/markdown/Material_Component.md`.

## Added: Physics section in the Anvil inspector (static / dynamic bodies)

Selecting a model in Anvil now shows a **Physics** section in the Inspector with a **Body** dropdown:

- **None**: no collider (the default).
- **Static**: an immovable triangle-mesh collider built from the model. Other bodies collide with it.
- **Dynamic**: a rigid body that falls with gravity and collides with other bodies. Its collider is the convex hull of the model. A **Mass** field appears for this option.

Physics only runs in Play mode. While editing, every collider follows its object, so moving, rotating or scaling with the gizmo or the inspector also moves the collider. In Play mode, dynamic bodies fall and collide. If a script or the gizmo moves a dynamic body during Play, the body is teleported there and keeps its velocity. Stop puts every object back where it was before Play and clears all velocities.

Added:

- [Engine/Physics/ScenePhysics.cs](Engine/Physics/ScenePhysics.cs): runs once per frame on the game thread and keeps BEPU bodies matching each entity's physics component. It creates, rebuilds and removes bodies when the type, scale or mass changes. It removes the bodies of deleted entities and of entities left behind by a scene swap. While editing it moves bodies to match their entities; in Play mode it steps the simulation and writes dynamic poses back into `Position`/`RotationMatrix`. The timestep is clamped to 1/20 s so a frame hitch can't push bodies through colliders. Model geometry is cached per `Model`.
- [Engine/Physics/PhysicsSystem.cs](Engine/Physics/PhysicsSystem.cs): the `PhysicsBodyType` enum (`None`/`Static`/`Dynamic`), plus `AddDynamicConvex` and `GetBodyPose`/`SetBodyPose`/`SetStaticPose`. `AddDynamicConvex` builds a convex hull and falls back to a bounding box when the model is flat or has more than 20k unique vertices. The body is centred on its centre of mass, and the offset from the entity origin is returned.
- `PhysicsSnapshot` in [IEditorBridge.cs](Engine/Editor/IEditorBridge.cs), filled for each `BasicEntity` in `EditorBridge.BuildSnapshot()`.
- `PhysicsInfo` in [SceneObjectViewModel.cs](Editor/Anvil/Models/SceneObjectViewModel.cs), reconciled in [BridgeReconciler.cs](Editor/Anvil/Services/BridgeReconciler.cs) and shown in the new Physics expander in [MainWindow.axaml](Editor/Anvil/Views/MainWindow.axaml). Edits go through `EnqueueMutate`.
- `.obsc` scenes save an optional `Physics` block (`Type`, `Mass`) per entity, and skip it for `None`. Older files still load, and the scene version is still 1.

Changed:

- [Engine/Entities/BasicEntity.cs](Engine/Entities/BasicEntity.cs): new public `PhysicsType`/`Mass` fields and internal runtime body state. `ApplyTransformation` now has a single code path. `Clone` (Ctrl+C / Insert) copies the physics component.
- [Engine/Logic/MainSceneLogic.cs](Engine/Logic/MainSceneLogic.cs): the new `UpdatePhysics(dt)`, called by `Engine.Update` through `ScreenManager.UpdatePhysics`, replaces the direct `PhysicsSystem.Step`. The simulation still only steps when `!e_enableeditor && p_physics`. On a scene swap, `DetachAll` replaces the manual static-collider removal. `EditorDelete` detaches the entity's body. `AddStaticPhysics` now only sets `PhysicsType = Static`.
- [Engine/Recources/GameSettings.cs](Engine/Recources/GameSettings.cs): `p_physics` now defaults to `true`. With `false` nothing could ever be simulated. Scenes with no physics components behave the same as before.

Removed:

- `BasicEntity.RegisterPhysics`, `BasicEntity.CheckPhysics` and the per-frame `CheckPhysics` loop in `MeshMaterialLibrary.FlagMovedObjects`. `ScenePhysics` replaces them.

Notes:

- BEPU mesh colliders are one-sided. A headless drop test confirmed that triangles with XNA's normal clockwise front faces collide, but reverse-wound triangles let bodies fall through. A model with flipped winding won't work as a Static collider.

## Added: `clean.ps1` build-output cleanup script

[clean.ps1](clean.ps1) at the repo root deletes the `bin/` and `obj/` folders of every project: Engine, Engine/Content, Editor/Anvil and Vista. It only deletes `bin/` and `obj/` folders that sit next to a `.csproj` or `.mgcb` file, so stale `.xnb` outputs can't keep removed assets loading. `-WhatIf` lists what would be deleted without deleting anything.

## Fixed: standalone engine crashed on boot due to stale asset paths

The standalone engine crashed on startup without any message. [Assets.cs](Engine/Recources/Assets.cs) still loaded assets that the content reorg had moved or removed (`GameObjects/Plane`, `GameObjects/test/cube`, `GameObjects/Tiger/Tiger`, `GameObjects/Editor/*`, `GameObjects/test/squarebricks-*`). These loads only worked because old `.xnb` files were still in `bin`. When a model had no `.bbox`, `ModelDefinition` tried to save one relative to the working directory, which was the source `Engine/Content` folder. This created a stray `Plane.bbox` there, then threw `DirectoryNotFoundException` for `GameObjects/test/cube.bbox`.

Changed:

- [Engine/Recources/Assets.cs](Engine/Recources/Assets.cs): the editor arrows and icons now load from `System/Editor/*`, and `Cube` loads from `GameObjects/Default/cube`.
- [Engine/Renderer/RenderModules/TexFilter.cs](Engine/Renderer/RenderModules/TexFilter.cs): `texStrip` now loads from `System/Editor/texStrip`.
- [Engine/Recources/ModelDefinition.cs](Engine/Recources/ModelDefinition.cs): if saving a `.bbox` cache fails, the error is logged and the engine keeps booting.
- [Engine/Program.cs](Engine/Program.cs): unhandled exceptions are written to `crash.log` next to the executable, then rethrown.

Removed:

- The unused `Assets.Plane`, `Assets.Tiger` and `Assets.RockMaterial`, whose source assets no longer exist.
- The stray `Engine/Content/GameObjects/Plane.bbox` generated by the crash.

## Added: baked lighting (irradiance probe volume) with a Lighting tab in Anvil

A first version of baked global illumination. The Inspector has a new **Lighting** tab (also under Window > Lighting). From it you can bake a 3D grid of light probes for the scene on the CPU. Inside the grid, the deferred renderer then uses the probes for diffuse ambient light instead of the environment cubemap. Probes need no lightmap UVs, so they work with every existing model.

How it works:

- **Bake.** `LightingBakeInput.Gather` takes a world-space copy of all meshes on the game thread: triangles, outward normals, and an approximate albedo per material, plus the enabled directional and point lights. `ProbeVolumeBaker` then works on that copy on a background thread. It builds a `TriangleBvh` (SAH), and each probe casts *Rays / Probe* rays:
  - a ray that escapes picks up the sky;
  - a ray that hits a back face counts towards marking the probe as inside geometry;
  - a ray that hits a front face picks up albedo � (direct light at the hit point + the previous pass's probes).
  
  Each pass adds one bounce (the DDGI approach). The result is stored per probe and colour channel as L1 spherical harmonics, already in the deferred shaders' light units (�0.1, linear colour). Probes inside geometry are replaced by the average of their valid neighbours.
- **Runtime.** `LightingSystem` uploads the probes as three `HalfVector4` `Texture3D`s. `DeferredEnvironmentMap.fx` rebuilds each pixel's world position from depth, samples the volume with trilinear filtering, and blends from the cubemap's diffuse to the probe irradiance near the volume's edges. Specular reflections still come from the cubemap/SSR.
- **Persistence.** Lighting settings are saved inside the `.obsc` scene file (an optional `Lighting` block, so older v1 files still load). The bake is saved as a binary `<scene>.probes` file next to it.

Added:

- [Engine/Renderer/Lighting/LightingSettings.cs](Engine/Renderer/Lighting/LightingSettings.cs): per-scene settings.
  - Runtime: enable, intensity, show probes.
  - Volume: fit to scene + padding or manual bounds, probe spacing, max probes per axis.
  - Quality: rays per probe, bounces, validity threshold.
  - Sky colour and intensity.
- [Engine/Renderer/Lighting/ProbeVolumeData.cs](Engine/Renderer/Lighting/ProbeVolumeData.cs): the baked grid, trilinear `EvaluateIrradiance`, and the `.probes` read/write code (versioned header).
- [Engine/Renderer/Lighting/LightingBakeInput.cs](Engine/Renderer/Lighting/LightingBakeInput.cs): scene gather. Reads positions and normals from the vertex buffers using the vertex declaration, and averages the albedo texture from a small mip level.
- [Engine/Renderer/Lighting/TriangleBvh.cs](Engine/Renderer/Lighting/TriangleBvh.cs): binned-SAH BVH with closest-hit and any-hit queries.
- [Engine/Renderer/Lighting/ProbeVolumeBaker.cs](Engine/Renderer/Lighting/ProbeVolumeBaker.cs): the multi-bounce CPU baker. Uses `Parallel.For`, can be cancelled, reports progress, and uses deterministic per-probe ray rotations.
- [Engine/Renderer/Lighting/LightingSystem.cs](Engine/Renderer/Lighting/LightingSystem.cs), [LightingBakeStatus.cs](Engine/Renderer/Lighting/LightingBakeStatus.cs): starts, cancels and finishes bakes, uploads to the GPU, publishes status, and draws the probe debug view (probes coloured by irradiance, magenta for invalid ones, plus the volume bounds).
- [Editor/Anvil/Models/LightingViewModel.cs](Editor/Anvil/Models/LightingViewModel.cs): the Lighting tab's view model. Reloads when the scene changes, sends edits to the game thread, and logs bake results to the Anvil console.

Changed:

- [Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx](Engine/Content/Shaders/Deferred/DeferredEnvironmentMap.fx), [DeferredEnvironmentMapRenderModule.cs](Engine/Renderer/RenderModules/DeferredEnvironmentMapRenderModule.cs): probe volume parameters, `SampleProbeVolume`, and a new `SetProbeVolume(...)`.
- [Engine/Renderer/Renderer.cs](Engine/Renderer/Renderer.cs): `Draw` takes optional `lighting` / `lightingSettings` arguments, binds the probe volume before the environment pass, and draws the probe debug view when *Show probes* is on (editor mode only).
- [Engine/Logic/Scene.cs](Engine/Logic/Scene.cs): new `Lighting` (settings) and `BakedProbes` fields. [MainSceneLogic.cs](Engine/Logic/MainSceneLogic.cs) owns a `LightingSystem`. [ScreenManager.cs](Engine/Logic/ScreenManager.cs) updates and disposes it and passes it to the renderer.
- [Engine/Logic/SceneSerialization.cs](Engine/Logic/SceneSerialization.cs): `LightingRecord` (settings + probe file name). Saves and loads the `.probes` file next to the scene; a missing or unreadable file is logged and ignored.
- [Engine/Editor/IEditorBridge.cs](Engine/Editor/IEditorBridge.cs), [EditorBridge.cs](Engine/Editor/EditorBridge.cs): `GetLightingSettings`, `EnqueueMutateLighting`, `EnqueueBakeLighting`, `CancelLightingBake`, `EnqueueClearBakedLighting`, `GetBakedLightingSummary`, `LightingStatus`, `LightingStatusChanged`.
- [Engine/Renderer/Helper/HelperGeometry/OctahedronHelperManager.cs](Engine/Renderer/Helper/HelperGeometry/OctahedronHelperManager.cs), [HelperGeometryManager.cs](Engine/Renderer/Helper/HelperGeometry/HelperGeometryManager.cs): octahedron helpers accept an optional radius. Existing callers still use 0.005.
- [Editor/Anvil/Views/MainWindow.axaml](Editor/Anvil/Views/MainWindow.axaml), [MainWindowViewModel.cs](Editor/Anvil/ViewModels/MainWindowViewModel.cs): a third Inspector chip, **Lighting**, and Window > Lighting. The panel has Bake (Bake/Cancel/Clear, progress bar, last-bake summary), Probe Volume, Volume (manual min/max when *Fit to scene* is off), Quality and Sky sections.

Known limits / next steps:

- Albedo is one average per material (no per-texel UV lookup) and emissive surfaces don't emit light.
- Rays that escape the scene pick up a flat sky colour, not the sky cubemap.
- Thin walls can leak light: there is no per-probe depth/visibility test like DDGI's Chebyshev test.
- Probe irradiance is only applied while `g_environmentmapping` is on, because it shares the environment pass.
- Mesh `IsEnabled` is ignored when gathering geometry, matching the renderer (editor-spawned entities leave it false). Lights still respect it.
- Probes don't move with objects; rebake after editing the scene.

## Added: window size, resizing, fullscreen, VSync and FPS cap in Game Settings; System folder hidden

The Game Settings dialog now also controls how the standalone game window is sized and paced. These settings are only applied when the engine runs standalone. The viewport embedded in Anvil is unaffected.

Added:

- [Engine/Recources/GameInfo.cs](Engine/Recources/GameInfo.cs): new `WindowWidth`/`WindowHeight` (default 1280x720), `AllowResizing` (default on), `Fullscreen`, `VSync` and `FpsCap` (0 = unlimited) fields, plus `ApplyToGameSettings()`, which pushes them into `GameSettings.g_screenwidth/height`, `g_vsync` and `g_fixedfps`.
- [Editor/Anvil/Views/GameSettingsWindow.axaml](Editor/Anvil/Views/GameSettingsWindow.axaml), [Editor/Anvil/ViewModels/GameSettingsViewModel.cs](Editor/Anvil/ViewModels/GameSettingsViewModel.cs): **Window** section (Width, Height, "Allow the player to resize the window", "Fullscreen (borderless, desktop resolution)") and **Frame Rate** section (VSync, FPS Cap). Width/Height and resizing are greyed out while Fullscreen is on.

Changed:

- [Engine/Engine.cs](Engine/Engine.cs): `Initialize()` now calls a new `ApplyGameInfo()` only when `IsHostedByEditor` is false. It applies the title and icon, sets `Window.AllowUserResizing`, switches to borderless fullscreen at the desktop resolution when requested, and resizes the back buffer before the render targets are created.
- [Engine/Engine.cs](Engine/Engine.cs): `SetFPSLimit()` now calls `ApplyChanges()` in the fixed-FPS branch. Before, VSync stayed on, so an FPS cap was silently limited to the monitor refresh rate.
- `GameInfo.json` moved from `Engine/Content/` to `Engine/Content/System/GameInfo.json` ([Engine.csproj](Engine/Engine.csproj) copy rule updated).
- [Editor/Anvil/ViewModels/MainWindowViewModel.cs](Editor/Anvil/ViewModels/MainWindowViewModel.cs): the top-level `System` folder is always hidden in the Assets panel. It holds engine-managed files: editor gizmo meshes/icons, `GameInfo.json` and `GameIcon.ico`.

## Added: Game Settings (window name + icon)

A new **Game Settings** entry in Anvil's title-bar menu opens a dialog where you set the standalone game's window name and upload a custom icon image.

Added:

- [Engine/Recources/GameInfo.cs](Engine/Recources/GameInfo.cs): reads and writes `Engine/Content/System/GameInfo.json` (`WindowTitle`, `IconPath`). Dev runs read the source `Engine/Content` copy directly, and shipped builds read the copy next to the executable. `ApplyToWindow` sets the MonoGame window title and sets the icon through the WinForms `Form.Icon`.
- [Editor/Anvil/Views/GameSettingsWindow.axaml](Editor/Anvil/Views/GameSettingsWindow.axaml) / [.axaml.cs](Editor/Anvil/Views/GameSettingsWindow.axaml.cs), [Editor/Anvil/ViewModels/GameSettingsViewModel.cs](Editor/Anvil/ViewModels/GameSettingsViewModel.cs): a modal dialog with a Window Name field, an icon preview, **Upload Image...** (png/jpg/bmp/ico/gif/webp) and **Reset**. Nothing is written until you press Save.
- [Editor/Anvil/Services/IconWriter.cs](Editor/Anvil/Services/IconWriter.cs): converts the chosen image into a multi-size `.ico` (256/64/48/32/16, PNG-compressed entries). Non-square images are fitted into a transparent square, not stretched. The icon is written to `Engine/Content/System/GameIcon.ico`.

Changed:

- [Engine/Engine.cs](Engine/Engine.cs): `Initialize()` loads `GameInfo` and applies it, replacing the hard-coded `"Engine"` title.
- [Engine/Engine.csproj](Engine/Engine.csproj): `Content/GameInfo.json` and `Content/System/GameIcon.ico` are copied to the output. When `GameIcon.ico` exists it also becomes the executable's `ApplicationIcon` on the next build.
- [Editor/Anvil/Views/MainWindow.axaml](Editor/Anvil/Views/MainWindow.axaml), [Editor/Anvil/ViewModels/MainWindowViewModel.cs](Editor/Anvil/ViewModels/MainWindowViewModel.cs): new top-level `Game Settings` menu item (`OpenGameSettingsCommand`). The title bar's project name now shows the game's window name. Saving logs to the Anvil console.

## Fix: volumetric fog hid the scene; fog settings in the inspector

With the default sun (intensity 100) the froxel fog covered the whole editor viewport, and objects only showed through at extreme light intensities such as 10000. The deferred light shaders scale their output by `0.1`, but the fog used the light's full `colour � intensity`, so lit fog was far brighter than the lit surfaces behind it.

Changed:

- [Engine/Content/Shaders/Deferred/Froxel.fx](Engine/Content/Shaders/Deferred/Froxel.fx): light scattered into the fog by directional and point lights is now scaled by the same `0.1` (`LIGHT_OUTPUT_SCALE`) as surface lighting. Fog keeps its density, but no longer outshines the objects inside it.
- [Engine/Recources/GameSettings.cs](Engine/Recources/GameSettings.cs): new `g_FroxelAnisotropy` (Henyey-Greenstein `G`, default 0.45, clamped to �0.95). It used to be fixed in the shader.
- [Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs](Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs): passes `g_FroxelAnisotropy` to the shader's `G` parameter each frame.

Added:

- A **Volumetric Fog** section in the Anvil inspector's Post FX tab ([MainWindow.axaml](Editor/Anvil/Views/MainWindow.axaml), [PostProcessingViewModel.cs](Editor/Anvil/Models/PostProcessingViewModel.cs)). It has an Enable Fog toggle, which sets both `g_FroxelsEnabled` and `g_FroxelFogEnabled` so turning fog off also skips the froxel passes. It also has Density, Absorption, Sun Scatter, Point Scatter, Anisotropy, Start/Full Distance, Sky Fog and History. The fields are greyed out while fog is off. Changes are applied on the game thread, like the other Post FX settings.

## Fix: ERROR mesh spawned white

When the ERROR mesh was dropped into the scene it showed up plain white. It didn't use its own textures, and it didn't fall back to `error.png` either. The cause: `EnqueueAddBasicEntity` gave it a `null` material so it would use its embedded FBX materials, but `ERRORText.fbx` doesn't reference any textures. Its `ERRORText_typeBlinn_*.jpg` maps were also missing from the content build.

Changed:

- [Engine/Content/Content.mgcb](Engine/Content/Content.mgcb): added the `ERRORText_typeBlinn_` BaseColor, Normal, Roughness and Metallic textures.
- [Engine/Recources/Assets.cs](Engine/Recources/Assets.cs): new `ErrorModelMaterial`, built from those four maps. If any of them fails to load, it falls back to `ErrorMaterial` (`error.png`). Removed the `BindEmbeddedTextures(ErrorModel.Model)` call, since the FBX has nothing to bind. `ErrorModelMaterial` is disposed in `Dispose()`.
- [Engine/Editor/EditorBridge.cs](Engine/Editor/EditorBridge.cs): `EnqueueAddBasicEntity` gives the ERROR mesh a clone of `ErrorModelMaterial` (or `ErrorMaterial`) instead of `null`. This also applies to imports that failed and were registered with the error mesh.
- [Engine/Recources/MaterialEffect.cs](Engine/Recources/MaterialEffect.cs): `Clone()` only called the `Effect` copy constructor, so every clone lost its texture maps, colour, roughness, metallic and type, and rendered as the default gray. This was the main reason the ERROR mesh (and imported models using `error.png` or textures bound by convention) still showed white. `Clone()` now copies all the material state.

## Content: `Art` → `GameObjects`; Anvil Assets panel mirrors `Engine/Content`

The `Engine/Content/Art` folder was renamed to `Engine/Content/GameObjects`. Every path that pointed at it now uses the new name, and the Anvil Assets panel shows the real Content folder instead of fixed virtual folders.

Changed:

- [Engine/Content/Content.mgcb](Engine/Content/Content.mgcb): all `Art/...` build entries now point to `GameObjects/...`.
- [Engine/Recources/Assets.cs](Engine/Recources/Assets.cs), [Engine/Recources/AssetImporter.cs](Engine/Recources/AssetImporter.cs), [Engine/Renderer/RenderModules/TexFilter.cs](Engine/Renderer/RenderModules/TexFilter.cs): content load paths changed from `Art/...` to `GameObjects/...`. Runtime-imported models now go to `GameObjects/Models/{key}/` and standalone textures to `GameObjects/Textures/`. `AssetImporter.LocateEngineContentRoot()` is now `internal` so the bridge can use it.
- [Engine/Recources/ModelDefinition.cs](Engine/Recources/ModelDefinition.cs): new `AssetPath` field that stores the content path the model was loaded from.
- [Engine/Editor/IEditorBridge.cs](Engine/Editor/IEditorBridge.cs), [Engine/Editor/EditorBridge.cs](Engine/Editor/EditorBridge.cs): added `ContentSourceRoot` (absolute path of `Engine/Content`) and `GetModelAssetPath(key)`. Both are safe to call from the UI thread. The key→path map is rebuilt on the game thread and swapped in whole.
- [Editor/Anvil/ViewModels/MainWindowViewModel.cs](Editor/Anvil/ViewModels/MainWindowViewModel.cs): the Assets tree is now built from the files and folders in `Engine/Content`. The top-level `bin`, `obj` and `Graphical User Interface` folders are hidden. A mesh file that a registered model was loaded from (for example `GameObjects/Test/cube.fbx` → `Cube`) can still be double-clicked or dragged into the Hierarchy. Anything under `GameObjects/Models/{key}/` still accepts texture drops and Delete for that imported model. A debounced `FileSystemWatcher` rebuilds the tree when Content changes on disk (changes under `bin`/`obj` are ignored), and expanded folders stay expanded after a rebuild.
- [Editor/Anvil/Models/AssetNode.cs](Editor/Anvil/Models/AssetNode.cs): added `RelativePath`, `ModelKey` and `OwnerModelKey`, plus the asset kinds `Shader`, `Font`, `Video` and `File`.
- [Editor/Anvil/Views/MainWindow.axaml](Editor/Anvil/Views/MainWindow.axaml), [MainWindow.axaml.cs](Editor/Anvil/Views/MainWindow.axaml.cs): icons for the new kinds. Tree items now bind `IsExpanded` two-way. Drag and double-click now read `AssetNode.ModelKey` instead of parsing `"model:"` ids.
- [Docs/markdown/Importing Structure.md](Docs/markdown/Importing%20Structure.md): paths updated to `GameObjects`.

Removed:

- The virtual Meshes / Textures / Scripts / Audio / Materials folders in the Assets panel (`BuildAssets`, `RefreshMeshAssetsFolder`).

Added:

- Double-clicking a text asset in the Assets panel opens it in an external editor. This covers `.fx`, `.fxh`, `.hlsl`, `.xml`, `.css`, `.cs`, `.spritefont`, `.mgcb`, `.json`, `.txt` and `.md`. The new [Editor/Anvil/Services/ExternalTextEditor.cs](Editor/Anvil/Services/ExternalTextEditor.cs) looks for VS Code's `Code.exe` in the per-user and machine install folders and next to the `code` shim on PATH. If it can't find VS Code, it opens the file in Notepad. If neither can start, an error goes to the Anvil console. The file opened is the source copy in `Engine/Content`, so shader hot reload picks up your edits. `MainWindowViewModel` gained `OpenInTextEditor`, `IsTextAsset` and a small `AddConsoleEntry` helper.
- The Assets search box now filters the tree. It matches file and folder names, ignoring case. A folder whose name matches is shown with everything inside it. A folder that only contains matches is shown expanded, with just those matches, and your own expanded/collapsed folders are left as they were. Clearing the box brings back the full tree.
- The Assets panel accepts any file or folder dragged in from Explorer (`MainWindowViewModel.CopyFilesIntoContent`). Dropped items are copied into the folder you drop onto. If you drop onto a file, they go into that file's folder; if you drop onto empty space, they go into the Content root. Folders are copied with everything inside them. If a name is already taken, the copy gets a `_2`, `_3`, … suffix. Copying runs off the UI thread and the file watcher refreshes the tree. Copy errors go to the Anvil console. `.fbx`/`.obj` files still go through the model importer, and images dropped on an imported model's folder still bind to that model. Plain copies are **not** added to `Content.mgcb`.

- New [Engine/Recources/ContentManifest.cs](Engine/Recources/ContentManifest.cs) keeps `Content.mgcb` in step with the Content folder:
  - `Register` adds build entries for pipeline assets: models (`.fbx` → FbxImporter; `.obj`/`.dae`/`.gltf`/`.glb` → OpenAssetImporter; `.x` → XImporter), textures (`.png`/`.jpg`/`.jpeg`/`.tga`/`.bmp`/`.dds`), effects (`.fx`) and sprite fonts (`.spritefont`). It uses the same settings as the existing entries and skips paths that are already listed. Files the engine reads directly are not registered: FMOD audio, video, Vista XML/CSS, `.bbox`/`.sdft` and `.fxh`.
  - `Unregister` removes a file's entry, or every entry under a folder.
  - `Rename` repoints entries to the new path and keeps their importer and processor settings.
  - Every edit holds the same named mutex as `AssetImporter`, whose `MgcbEditMutex` now points at it, and keeps the file's line endings. Registering and then unregistering leaves the file byte-identical (tested on a copy of the manifest).
- Anvil registers dropped files automatically. Pipeline assets copied into Content, including everything inside dropped folders, get a `Content.mgcb` entry, and the Anvil console lists what was added.
- Anvil removes manifest entries automatically. When the file watcher sees a Content file or folder deleted, from Explorer or from the editor, its entries are removed. When one is renamed or moved, its entries follow it. Paths are checked again after the debounce, so editors that save by delete-then-rename, and git checkouts, don't strip entries for files that come straight back.
- Assets panel **Delete** (context menu or the Delete key) now works on any file or folder. It moves the item to the Recycle Bin and removes its manifest entries, after a confirmation that warns if the engine loads the file as a model. Deleting an imported model's mesh or its `GameObjects/Models/{key}` folder still removes the whole model through the engine. Files inside that folder are now deleted on their own instead of taking the whole model with them (`MainWindowViewModel.ModelKeyToDeleteFor` / `DeleteAssetFromDisk`).

Hidden in the Assets panel:

- `Content.mgcb`, `Content.mgcb.org` (Content root) and every `*.mgcontent` build-state file. The file watcher ignores `*.mgcontent` changes as well.

## Engine: fix startup crash / white Anvil viewport from missing built-in models

Fixed the standalone engine silently exiting on launch and the Anvil viewport staying white. Both came from the same `ContentLoadException` in `Assets.Load`: the engine hard-loaded built-in models whose source files are not in the repo, so `Initialize` threw. Standalone, that killed the process. In Anvil, `MonoGameHost` caught the exception inside `RunOneFrame()`, logged it to `anvil-bridge.log`, and retried every frame, so nothing was ever drawn.

Root cause: `daft_helmets.obj`, `skull.obj` and `Sponza/Sponza.obj` were never committed, because `.gitignore`'s `*.[Oo]bj` rule (meant for compiled object files) also matches Wavefront `.obj` models. A fresh clone therefore can't build them. The Sponza folder, `Art/Human` and their `Content.mgcb` entries had also been removed from the working copy. None of these models were placed in the startup scene.

Removed:

- [Engine/Recources/Assets.cs](Engine/Recources/Assets.cs) — the `HelmetModel`/`SkullModel` fields, loads and `ProcessHelmets()`; the `SponzaModel` field, its ~35 Sponza texture loads, `_sponzaTextures`, the `sponza_*` texture fields (and their `Dispose` calls) and `ProcessSponza()`; the `HumanModel` field and load. As a result, `SponzaModel` and `HumanModel` no longer appear as built-in model keys in the editor.

## Editor: fix dead engine keyboard input after maximize / fullscreen

Fixed keyboard input to the embedded engine going dead after the Anvil window was maximized/fullscreened/restored — the mouse (camera orbit/pan/zoom) kept working, but WASD and other forwarded keys did nothing until the user clicked an Avalonia control (e.g. a hierarchy item), which "returned" input. This surfaced alongside the resize fix: once the viewport actually fills the window on fullscreen, the camera is usable and the dead keys became noticeable.

Root cause: while hosted in Anvil the engine HWND never holds Win32 keyboard focus, so MonoGame's native keyboard state is empty and the **only** keyboard path is Avalonia → `MonoGameHost.OnTopLevelKeyDown` → `EditorBridge.SetHostKeyState` → `Input.IsKeyDown`. Avalonia raises `KeyDown`/`KeyUp` only when some element holds focus, but a maximize/fullscreen/restore transition clears the window's focused element while leaving the window active — so forwarding went silent. The mouse was unaffected because it flows through a separate, focus-independent Win32 viewport-gate path. Clicking a hierarchy item re-established a focused element, restoring forwarding.

Key detail: the focus must be restored to a **plain managed control**, not to the `MonoGameHost` itself — focusing a `NativeControlHost` hands OS focus to the embedded engine child window, where MonoGame's keyboard reads empty (hosted) *and* Avalonia stops raising KeyDown, so forwarding would stay dead. A managed control keeps OS focus with Avalonia, which is the only state in which TopLevel KeyDown forwarding works (and is exactly what clicking a hierarchy item does).

Changed:

- [Editor/Anvil/Views/MainWindow.axaml](Editor/Anvil/Views/MainWindow.axaml) — added an invisible, focusable `Border` (`ViewportKeyboardSink`, `IsHitTestVisible=False`) in the viewport grid to serve as the managed focus target.
- [Editor/Anvil/Controls/MonoGameHost.cs](Editor/Anvil/Controls/MonoGameHost.cs) — added focus restoration. `OnAttachedToVisualTree` subscribes to the parent window's `PropertyChanged`/`Activated`; on a `WindowState` change (and on activation) it re-asserts focus across the resize-settle window (`RestoreEditorKeyboardFocus` runs each settle tick), since Avalonia clears the focused element slightly after raising the change. `RestoreEditorKeyboardFocus` focuses the managed sink only when nothing meaningful is focused (focus is `null`, or is the host itself meaning OS focus leaked to the engine child) — so it never steals focus from an inspector field mid-edit. The window subscriptions are removed in `OnDetachedFromVisualTree`.
- [Engine/Editor/EditorBridge.cs](Engine/Editor/EditorBridge.cs) — added `ClearHostKeys()`, which the host calls on a window-state transition to release any forwarded key whose `KeyUp` was swallowed by the transition (otherwise it stays latched "down" and drifts the editor camera).

## Editor: fix empty ColorPicker flyout in the inspector

Fixed the material **Color** picker in the inspector opening as a small empty flyout panel with no spectrum/sliders to pick a color.

Root cause: Avalonia's `ColorPicker`/`ColorView` ship their control templates in a **separate** theme dictionary that `<FluentTheme />` does not merge. [App.axaml](Editor/Anvil/App.axaml) declared only `<FluentTheme />`, so the `ColorPicker` had no applied template and rendered as an empty container.

Changed:

- [Editor/Anvil/App.axaml](Editor/Anvil/App.axaml) — added `<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />` to `Application.Styles`, after `<FluentTheme />` so the base `Theme*` brushes the ColorPicker theme references via `DynamicResource` are already defined. The picker (bound to `SelectedObject.Material.Color`) now renders its full spectrum/sliders/palette.

## Editor: fix engine viewport not resizing on maximize / fullscreen

Fixed the embedded engine viewport staying at its previous (smaller) size — leaving white borders — when the Anvil window was maximized, restored, or taken fullscreen. Incremental drag-resizes worked, but a single discrete size jump did not.

Root cause: Avalonia's `NativeControlHost` applies the container HWND's new geometry **after** [`MonoGameHost.ArrangeOverride`](Editor/Anvil/Controls/MonoGameHost.cs) returns. `ResizeEngineToContainer()` reads the container's client rect synchronously inside that arrange pass, so on a discrete jump (maximize/restore/fullscreen) it saw the stale pre-jump size and sized the engine HWND to that. A live drag emits a continuous stream of layout passes that masks the one-pass lag; a single maximize has no follow-up pass to correct it, so the viewport stayed shrunk until the next manual resize. This is the runtime analogue of the boot-time deferred-resize the existing boot watchdog already handles.

Changed:

- [Editor/Anvil/Controls/MonoGameHost.cs](Editor/Anvil/Controls/MonoGameHost.cs) — generalized the boot-watchdog pattern to runtime. `ArrangeOverride` now calls a new `NudgeResizeSettle()` after its synchronous `ResizeEngineToContainer()`, starting/refreshing a short-lived `DispatcherTimer` (`_resizeSettleTimer`, ~30 ms × 8 ticks ≈ 240 ms) that re-asserts the container size for a brief settle window — catching the container's final size once Avalonia (and Win32) have applied it. `ResizeEngineToContainer()` already no-ops once the engine matches the container, so the timer settles to cheap no-ops and self-stops; its tick budget resets on every layout change so a live drag keeps it alive and it converges ~240 ms after the last change. The timer is stopped in `DestroyNativeControlCore` alongside the boot watchdog.

## Docs: input architecture guide

Added [Docs/Input Architecture.md](Docs/Input%20Architecture.md) — documents how keyboard/mouse input flows through the engine in both the standalone `Engine.exe` and the Anvil-hosted editor, and gives step-by-step recipes for adding new input.

Added:

- [Docs/Input Architecture.md](Docs/Input%20Architecture.md) — covers the single polling seam ([`Engine.Logic.Input`](Engine/Logic/Input.cs)) and its per-frame `Input.Update` call site; the two runtime contexts (standalone window focus vs. Anvil HWND reparenting); keyboard forwarding (Avalonia `KeyDown`/`KeyUp` → `MonoGameHost.MapAvaloniaKey` → `EditorBridge.SetHostKeyState` → `Input.IsKeyDown`); the critical nuance that only `IsKeyDown` merges host-forwarded keys while `WasKeyPressed`/`GetKeyPressed` are native-state-only (so one-shot shortcuts don't fire inside Anvil); the Win32 mouse viewport-ownership gate; camera-input special-casing; a consumer map of where input is read; and six "how to add a new input" recipes plus a rules/gotchas and quick-reference section.

This is a documentation-only change — no engine, editor, or build behavior was modified.

## Migrate physics from BEPUphysics v1 to BEPUphysics v2 (BepuPhysics)

Replaced the engine's long-dormant, vendored **BEPUphysics v1** DLLs (object-oriented `Space`/`Entity`/`StaticMesh`, 2020-era) with the modern **BEPUphysics v2** NuGet package (handle-based `Simulation` + `BufferPool` + callback structs + `System.Numerics` math). v1 was never actually exercised (physics off by default, no body or collider ever created), so this is a clean plumbing swap rather than a behavior change — physics remains **off by default** (`GameSettings.p_physics == false`). All BEPU v2 specifics are confined behind one new owner class so the rest of the engine never touches a raw physics type. Verified end-to-end by dropping a dynamic box onto a static triangle-mesh ground: it fell along −Z under gravity (z 25.0 → 7.0) and came to rest, with no exceptions.

Added:

- [Engine/Physics/PhysicsSystem.cs](Engine/Physics/PhysicsSystem.cs) — the single seam to BEPUphysics v2. Owns the `Simulation`, the `BufferPool` and the two required callback structs (`PoseIntegratorCallbacks` — applies Z-up gravity `(0,0,-9.81)` per substep, the v2 replacement for v1's `ForceUpdater.Gravity`; `NarrowPhaseCallbacks` — minimal contact material). Exposes everything in engine (XNA) math types: `Step`, `CreateMeshShape`/`CreateBoxShape`, `AddStatic`/`AddStaticMesh`, `AddDynamicBox`, `GetBodyMatrix` (per-frame pose read-back), `SetBodyPosition`, `RemoveStatic`/`RemoveDynamic` (return mesh buffers to the pool), and `Dispose`. `Step` skips non-positive timesteps — v2 throws on `dt <= 0` where v1's `Space.Update` tolerated MonoGame's first 0 ms frame.

Changed:

- [Engine/Engine.csproj](Engine/Engine.csproj) — removed the two `<Reference HintPath>` entries to the vendored `Content\BEPUphysics.dll`/`BEPUutilities.dll`; added `<PackageReference Include="BepuPhysics" Version="2.4.0" />` (pulls `BepuUtilities` transitively), matching the repo's NuGet-for-everything convention.
- [Engine/Engine.cs](Engine/Engine.cs) — `Space _physicsSpace` → `PhysicsSystem _physics`; ctor builds `new PhysicsSystem(new Vector3(0,0,-9.81f))`; the per-frame `space.Update(dt)` → `_physics.Step(dt)`; added `_physics.Dispose()` to `UnloadContent`.
- [Engine/Logic/ScreenManager.cs](Engine/Logic/ScreenManager.cs) and [Engine/Logic/MainSceneLogic.cs](Engine/Logic/MainSceneLogic.cs) — threaded `PhysicsSystem` through `Initialize` in place of `Space`. `AddStaticPhysics` now builds a v2 triangle-mesh collider + `StaticHandle` via `_physics.AddStaticMesh(...)`; `OnSceneChanged` detaches via `_physics.RemoveStatic(...)`. Dropped the unused v1 `Entity PhysicsEntity` parameter from the `AddEntity` overloads.
- [Engine/Entities/BasicEntity.cs](Engine/Entities/BasicEntity.cs) — physics fields changed from v1 objects (`Entity`/`StaticMesh`) to v2 handle structs (`BodyHandle?`/`StaticHandle?`) plus a `PhysicsSystem` reference. `RegisterPhysics`, `CheckPhysics` and `ApplyTransformation` now read the body pose via `_physics.GetBodyMatrix(...)` and write editor drags via `_physics.SetBodyPosition(...)`; the dead static-`AffineTransform` sync branch was removed.
- [Engine/Recources/Helper/MathConverter.cs](Engine/Recources/Helper/MathConverter.cs) — replaced the whole XNA↔`BEPUutilities` converter set with a small XNA↔`System.Numerics` one (`ToNumerics`/`ToXna` for `Vector3` and `Quaternion`), since v2 speaks `System.Numerics` directly.
- [Engine/Recources/Helper/ModelDataExtractor.cs](Engine/Recources/Helper/ModelDataExtractor.cs) — removed the `BEPUutilities.Vector3[]` overload; the XNA overload feeds `PhysicsSystem.CreateMeshShape` unchanged.

Removed:

- The vendored `Engine/Content/BEPUphysics.dll` / `BEPUutilities.dll` references, `Extensions.CopyFromBepuMatrix` (replaced by `PhysicsSystem.GetBodyMatrix`), and a stale unused `using BEPUphysics.Paths;` in [Engine/Renderer/Helper/HelperGeometry/LineHelper.cs](Engine/Renderer/Helper/HelperGeometry/LineHelper.cs).

Notes:

- BEPU v2 is up-axis-agnostic; the engine's Z-up convention is preserved by applying gravity down −Z in the pose-integrator callback. Threading is single-threaded for now (`Timestep` is given a null `IThreadDispatcher`); a real dispatcher can be slotted into `PhysicsSystem` later without touching callers. The `AddDynamicBox`/`CreateBoxShape` helpers are left in place as ready-to-use primitives for future gameplay physics.

## Editor: skip the intro video when hosted by Anvil

Made the Anvil editor an exception for the standalone intro video. When the engine runs embedded in the editor it now boots straight into the live scene instead of playing (and waiting on) the LibVLC intro, while standalone `Engine.exe` still plays the intro as before.

Changed:

- [Engine/Logic/ScreenManager.cs](Engine/Logic/ScreenManager.cs) — gated the intro on `_bridge.IsHostedByEditor`. `Load` skips `VideoIntroLogic.Load` (no LibVLC instance, no mp4 decode, no 1080p frame buffer) and `Initialize` skips `VideoIntroLogic.Initialize` and starts `_currentState` at `MainGame` instead of `VideoIntro`. `VideoIntroLogic` is null-safe throughout, so leaving it unloaded keeps its Update/Draw/Unload paths as no-ops.

Notes:

- The host flag is valid at both `Load` and `Initialize` time: `MonoGameHost` calls `SetHostedByEditor(true)` before the first `RunOneFrame()` that triggers MonoGame's `Initialize`. Standalone never sets it, so the intro path is unaffected.

## Build: copy FMOD DLLs from thirdparty/ and intro.mp4 into bin

Updated the Engine build to copy the FMOD native libraries from their new `thirdparty/fmod/` home and to deploy the intro video as a loose file, so a fresh checkout runs with audio and the intro without manual file copying.

Changed:

- [Engine/Engine.csproj](Engine/Engine.csproj) — repointed the `fmod.dll`/`fmodL.dll` copy items from the project root to `thirdparty\fmod\`, adding a `<Link>` so they still flatten to the output root (next to the executable, where FMOD loads them) instead of nesting under a `thirdparty/` subfolder in bin. Added a new item that copies `Content\Video\intro.mp4` to the output as `Content\intro.mp4` (flattened via `<Link>`), matching the loose-file path `VideoIntroLogic` reads at runtime.

Notes:

- Verified with `dotnet build`: `fmod.dll`/`fmodL.dll` land at the bin root and `intro.mp4` lands at `bin/.../Content/intro.mp4`, with no stray `thirdparty/` folder copied into the output.

## Docs website: dark/light theme toggle + PNG logo

Replaced the docs site's non-functional account button with a working dark/light theme switcher, reworked the two stale color palettes into proper Dark and Light themes, and swapped the placeholder "s&" logo box for the project's crystal logo as a PNG.

Added:

- [Docs/website/public/logo.png](Docs/website/public/logo.png) — the Obsidian crystal logo, generated from [Docs/Icon.png](Docs/Icon.png) with its opaque black background made transparent (alpha derived from luminance, RGB forced white) so the mark floats on the header and can be recolored per theme. Also used as the favicon (the previous `/favicon.svg` reference was broken).

Changed:

- [Docs/website/src/styles/theme.css](Docs/website/src/styles/theme.css) — replaced the old "ocean"/"emerald" palettes with semantic **Dark** (`:root` default + `[data-theme='dark']`) and **Light** (`[data-theme='light']`) themes. Added a `.brand-logo` rule that flips the white crystal to dark ink in light mode so it stays legible.
- [Docs/website/src/devdocs.tsx](Docs/website/src/devdocs.tsx) — added `theme` state (initialized from the `data-theme` set pre-paint), a `useEffect` that reflects it onto `<html>` and persists to `localStorage`, and a toggle handler. The account button is now a Sun/Moon theme switcher; the logo `<div>` is now an `<img src="/logo.png" class="brand-logo">`.
- [Docs/website/index.html](Docs/website/index.html) — added a pre-paint inline script that picks the theme (saved choice → OS `prefers-color-scheme` → dark fallback) to avoid a flash on load, and pointed the favicon at `/logo.png`.

Notes:

- Default theme follows the OS preference and falls back to dark; the user's choice is remembered across visits. Verified with `tsc -b` (typecheck clean) and `vite build` (logo copied into `dist/`, favicon reference updated).

## Add FMOD audio engine (FmodForFoxes)

Introduced game audio to the engine, which previously had none (only the LibVLC intro video carried sound). Built on FMOD via the FmodForFoxes wrapper for best-in-class 3D spatial audio. Supports 2D one-shot/looping SFX, 3D positional audio (listener follows the active camera, emitters follow world/entity positions with attenuation + Doppler-ready velocity), a master volume, and streamed music. Wired into the engine following the existing subsystem (Load/Initialize/Update/Dispose) convention, with a static `Audio` facade mirroring the `Globals` pattern.

Added:

- [Engine/Recources/AudioManager.cs](Engine/Recources/AudioManager.cs) — `AudioManager` subsystem (FMOD init/update/dispose, 2D `PlaySound`, 3D `PlaySound3D` with fixed or live-tracked positions, streamed `PlayMusic`, `MasterVolume`, `StopAll`) plus the static `Audio` facade. Initializes inside a try/catch so a missing native DLL degrades to a silent no-op instead of crashing the engine. Caches 2D and 3D sounds separately to avoid mode bleed; reaps finished channels each frame. Includes a one-line `ToFmod` coordinate hook (engine is Z-up; flip if 3D panning is mirrored).
- [Engine/Scripting/AudioTestScript.cs](Engine/Scripting/AudioTestScript.cs) — `IScript` that turns its owning entity into a looping 3D emitter in Play mode (3D verification).
- [Engine/Content/Audio/](Engine/Content/Audio/) — generated sample assets: `blip.wav` (mono one-shot), `loop3d.wav` (mono seamless loop), `music.wav` (streamed track).
- [Docs/Audio_Architecture.md](Docs/markdown/Audio_Architecture.md) — architecture doc: the FMOD/FmodForFoxes dependency stack, component map, engine lifecycle wiring, public API, load/playback paths (incl. the `TryPlay` workaround), 3D listener/emitter model and coordinate mapping, the exact native-DLL version requirement, graceful degradation, and how to extend the system.

Changed:

- [Engine/Engine.csproj](Engine/Engine.csproj) — added `FmodForFoxes` + `FmodForFoxes.Desktop` 3.2.0 package references; copy `fmod.dll`/`fmodL.dll` (supplied locally) and `Content/Audio/**` to the output directory.
- [Engine/Logic/ScreenManager.cs](Engine/Logic/ScreenManager.cs) — instantiate `AudioManager` in `Load`, init in `Initialize`, pump `SystemUpdate()` first every frame (intro included) and `UpdateListener(Camera)` after scene update, dispose in `Unload`.
- [Engine/Logic/MainSceneLogic.cs](Engine/Logic/MainSceneLogic.cs) — temporary test hooks: **X** plays a 2D blip, **M** toggles streamed music (remove/gate after verification).

Notes:

- FMOD's native libraries are not redistributed by the NuGet package. To hear audio, download the FMOD Engine (Windows) from fmod.com and place `fmod.dll` + `fmodL.dll` (x64) in `Engine/`; the build copies them next to the executable. Verified the engine boots, runs, and degrades gracefully when they are absent (logs "Audio: FMOD init failed" and continues). The FmodForFoxes managed assembly (net8.0) loads cleanly under net10.0-windows.
- **The native DLL must be exactly FMOD 2.02.37** (`0x00020225` — FmodForFoxes 3.2.0's bundled bindings; FMOD's `init` rejects any other patch with a header mismatch). Init logs the required version and a MATCH/MISMATCH verdict against the loaded DLL. Playback goes through a raw-FMOD helper (`TryPlay`) that wraps the result with the safe single-arg `Channel` ctor, deliberately avoiding FmodForFoxes' 2-arg `Channel(Sound, FMOD.Channel)` ctor — that ctor reads its computed `Sound` property during construction and throws a `NullReferenceException` whenever `playSound` returns a bad channel (e.g. a 2.03.x DLL mismatch). On a bad result we now log the FMOD error code instead of crashing. The detected native version is logged at init for diagnosis.

## Add "Exporting Shaders to Unity URP" guide

Documented how to port a shader out of this MonoGame/HLSL engine into Unity's Universal Render Pipeline, using the froxel volumetric fog as the worked (hardest) example.

Added:

- [Docs/Exporting Shaders to Unity URP.md](Docs/Exporting%20Shaders%20to%20Unity%20URP.md) — guide covering: the three froxel stages to port (inject → accumulate → compose), two porting strategies (compute + `RWTexture3D` recommended, or a faithful 2D-atlas blit port), a MonoGame-FX→URP-HLSL translation reference (sampler macros, matrix `mul` order, depth linearization, coordinate/clip-space flips, URP light/shadow APIs), a uniform→Unity mapping table seeded with current `GameSettings` defaults, a `ScriptableRendererFeature` hook-up note (RenderGraph vs. legacy), a port-blocking gotchas checklist, a validation sequence, and a section generalizing the playbook to other shaders. Sourced from `Froxel.fx`, `DeferredCompose.fx`, `FroxelRenderModule.cs`, and `GameSettings.cs`.

No code changes.

## Add Anvil editor architecture docs

Documented how the Anvil editor is structured and how it interacts with the engine, in a form meant to be extended as the editor grows.

Added:

- [Docs/Editor_Architecture.excalidraw](Docs/excalidraw/Editor_Architecture.excalidraw) — diagram of the editor architecture: the Avalonia UI thread, the MonoGame STA game thread, and the `EditorBridge` seam between them, with the mutation path (UI → engine, queued) and snapshot path (engine → UI, reconciled) drawn as labelled arrows. Colour-coded by thread/role with a legend.
- [Docs/Editor_Architecture.md](Docs/markdown/Editor_Architecture.md) — companion write-up: component map (Anvil + engine sides), the exact `IEditorBridge` contract (snapshot structs, mutation methods, events), startup/HWND embedding, data-flow diagrams, the threading model, and an "How to Extend This" section keyed to the bridge seams. Cross-links the existing engine and Vista UI docs.

No code changes.

## Fix game input leaking outside the engine viewport in Anvil

Addresses [Docs/TODO.md](Docs/TODO.md): holding right-click **outside** the viewport (over the
Inspector / Hierarchy / Console panels) and dragging still moved the engine camera. Reported as
having "reached a roadblock" on a previous attempt.

**Root cause.** The engine reads the *global* Win32 mouse state via `Mouse.GetState()`. When hosted
in Anvil, that global state still reports clicks made over Avalonia panels, so an RMB-drag anywhere
in the editor orbited the camera. An existing gate in `Input.Update` synthesizes an
all-buttons-released `MouseState` while the pointer is outside the viewport — but it was keyed off a
host-forwarded flag (`IsHostPointerOverViewport`) that nothing ever set.

**Why the host can't drive it.** A first fix wired `MonoGameHost` to forward pointer-enter/leave to
the bridge, but it broke input *inside* the viewport: the reparented engine HWND consumes Avalonia's
pointer events over the viewport, so Avalonia sees the pointer leave a panel but never sees it
re-enter the viewport — the flag latched off and the engine ignored all mouse input there.

**Fix.** The gate is now self-contained in the engine and needs no host cooperation.
`Mouse.GetState()` already reports the cursor relative to the engine window's **own** client area, so
a cursor over a surrounding panel lands outside `[0,w) x [0,h)`. `Input.Update` tests that on the
game thread each frame and synthesizes the idle state when hosted and outside. A small latch
(`_dragOwnedByViewport`) remembers when a drag's initial press landed inside the viewport, so an
RMB/MMB/LMB drag that starts in the viewport keeps working even if the cursor strays over a panel
(the window holds mouse capture) — while an RMB press that *starts* over a panel is still ignored.
Standalone `Engine.exe` is unaffected (no host → `IsHostedByEditor` false → gate skipped).

Changes:

- **`Engine/Logic/Input.cs`**: `Input.Update` reads the raw mouse state, computes
  `insideViewport` from the cursor vs. `GameSettings.g_screenwidth/height`, and gates with a
  `_dragOwnedByViewport` / `_anyButtonDownLast` latch so in-viewport drags survive straying out.
  The gate no longer depends on `IsHostPointerOverViewport`.
- `MonoGameHost.cs` is unchanged from before this fix; the `IEditorBridge.IsHostPointerOverViewport` /
  `SetHostPointerOverViewport` API remains but is no longer used by the input gate.

## Fix Anvil viewport rendering at 640×480 on startup until the first manual resize

Follow-up to the bloom-device fix below. With the device no longer dying, the embedded viewport
worked — but on startup it rendered at MonoGame's stale **640×480** default backbuffer
(white/overexposed borders around the scene; the in-engine overlay read `Res: 640 x 480`) and only
snapped to the real container size after the user dragged/resized the window once.

**Root cause.** When hosted in Anvil, `MonoGameHost` reparents the engine HWND under its container
and resizes it to the container's client rect via `SetWindowPos`/`MoveWindow`, pre-syncing
`PreferredBackBuffer` *without* `ApplyChanges` (the device reset must happen on the game thread).
The reconcile that actually rebuilds the swap chain + render targets
(`Engine.ApplyPendingResize` → `ApplyChanges` + `ScreenManager.UpdateResolution`) is driven by
`Engine.ClientChangedWindowSize`, i.e. a `WM_SIZE` the engine observes. But on boot the container
sizing happens before the engine begins ticking its own resize bookkeeping, so no reconcile is
primed and the engine keeps rendering at the 640×480 default the device fell back to. The first
*user* resize finally raises a `WM_SIZE` the engine sees, which reconciles everything — hence
"resize and it goes back to normal".

**Fix.** `Engine.ApplyPendingResize` now self-primes for the first ~30 Update ticks: if the actual
`Window.ClientBounds` differ from the resolution we're rendering at (`GameSettings.g_screenwidth/
height`), it sets `_pendingResize` so the existing reconcile path runs on the game thread and the
viewport fits the container on startup — no manual resize needed. It's idempotent (once reconciled,
`ClientBounds == GameSettings` so it stops firing) and a no-op for the standalone engine, whose
boot window already matches `GameSettings`.

Changes:

- **`Engine/Engine.cs`**: added `_bootReconcileTicks` (starts at 30); `ApplyPendingResize` decrements
  it each tick and flags a resize while the live client bounds don't match the rendered resolution.

## Fix all-white engine viewport — `BloomFilter.Dispose()` was killing the shared GraphicsDevice

Addresses [Docs/TODO.md](Docs/TODO.md): after the previous resize work, resizing no longer crashed
but the embedded engine viewport rendered all white, and `anvil-bridge.log` filled with
`NullReferenceException`s — `SharpDX.Direct3D11.Texture2D..ctor` inside
`BloomFilter.UpdateResolution`, then `Monitor.Enter(null)` inside
`GraphicsDevice.PlatformApplyRenderTargets` on the following frame.

**Root cause.** `BloomFilter.Dispose()` ended with `_graphicsDevice?.Dispose()` and
`_bloomEffect?.Dispose()`. That `_graphicsDevice` is **the engine's one shared `GraphicsDevice`**
(handed in at `BloomFilter.Initialize`, owned by the MonoGame `Game`), and `_bloomEffect` is a
`ContentManager`-owned `Effect`. Critically, `Dispose()` is **not** a shutdown-only path: it is
called from `BloomFilter.UpdateResolution()` on *every resolution change* to recycle the mip render
targets. The renderer reconciles bloom's resolution lazily — the first time `BloomFilter.Draw` sees
`width/height != _width/_height` (which happens as soon as the Anvil container settles to a size
other than the 1280×720 boot default), it calls `UpdateResolution` → `Dispose()` → **disposes the
live device**. The very next `RenderTarget2D` it tries to create (Mip0) NREs (first log entry); the
following frame's `GBufferRenderModule.Draw` → `SetRenderTargets` hits `Monitor.Enter(null)` on the
dead device (second log entry). The device never recovers, so the viewport stays white. This is the
only `Dispose()` in the engine that frees the shared device *and* runs during normal runtime
(`Renderer`/`DebugScreen`/`LightAccumulationModule` also free the device in their `Dispose()`, but
those are shutdown-only and unaffected).

**Fix.** `BloomFilter.Dispose()` now releases **only** the six bloom mip render targets it actually
owns — it no longer touches the shared `GraphicsDevice` or the content-managed `_bloomEffect`.
Resolution changes (startup container-fit and live resize) now recycle just the bloom targets and
the device survives, so the deferred pipeline keeps drawing.

Changes:

- **`Engine/Renderer/RenderModules/PostProcessingFilters/BloomFilter.cs`**: `Dispose()` drops the
  `_graphicsDevice?.Dispose()` and `_bloomEffect?.Dispose()` calls (with a comment explaining why
  this method must only free the mip targets, since it runs on every resolution change).
- **`Editor/Anvil/Controls/MonoGameHost.cs`**: the `CreateWindowEx` P/Invoke's `lpWindowName`
  parameter is now `string?`, clearing the runtime `CS8625` warning at line 232 (the `"STATIC"`
  container is created with a `null` window name).

Verified `dotnet build Engine.slnx` (0 errors) and `dotnet build Editor/Anvil/Anvil.csproj`
(0 warnings, 0 errors).

## Fix Anvil viewport resize — no black areas, no resize/startup crash, engine fills only its cell

Addresses [Docs/TODO.md](Docs/TODO.md). Two coupled problems:

1. **Black bars + skewed image/axes, resolution stuck at 1280×720.** The render resolution stayed
   at the boot default the whole session (the in-engine overlay literally read `Res: 1280 x 720`
   regardless of window size). The final present, `Renderer.DrawMapToScreenToFullScreen`, blits into
   `Rectangle(0, 0, g_screenwidth, g_screenheight)` and the projection aspect ratio also uses those
   values, so a stale resolution leaves the rest of the (larger) backbuffer black and the 3D content
   skewed. `Renderer.UpdateResolution()` (the only thing that refreshes `GameSettings` + rebuilds
   render targets) was reached via `Engine.ClientChangedWindowSize`, whose guard
   (`GraphicsDevice.Viewport != PreferredBackBuffer`) was self-defeating: the host had already made
   both sides equal, so it never ran.

2. **`NullReferenceException` in `GraphicsDevice.CreateSizeDependentResources` on startup and on
   resize.** MonoGame's `WinFormsGameWindow.OnResize` (subscribed to `Form.Resize`) calls
   `UpdateBackBufferSize` → `GraphicsDeviceManager.ApplyChanges()` → `GraphicsDevice.Reset()` **only
   when the form's client size differs from `PreferredBackBuffer`**. Resetting the device from inside
   that `WndProc`/`OnResize` callstack NREs on the **reparented child window** used by the Anvil
   viewport. The startup cases were fixed by pre-syncing `PreferredBackBuffer`, but the resize case
   persisted: Avalonia's `NativeControlHost` resizes the engine HWND *itself* on every layout pass via
   a **cross-thread `SetWindowPos`** that blocks inside `base.ArrangeOverride` until the game thread's
   `WndProc` runs — so no pre-sync done afterward could win that race.

The fix combines two rules:

- **An intermediate container HWND decouples Avalonia's resize from the engine.** A plain `STATIC`
  child window is created under the host HWND and handed back to Avalonia as the native control;
  the engine HWND is reparented *under* it. Avalonia now resizes the container (no graphics device →
  harmless), and the engine HWND is resized **only by us**, using the container's exact client-rect
  pixels for both `PreferredBackBuffer` and `MoveWindow` — so MonoGame's `OnResize` always early-returns
  (identical integers, no DPI rounding guesswork) and never resets the device from a `WndProc`.
- **The real swap-chain + render-target rebuild happens on the game thread, outside any `WndProc`.**

Changes:

- **`Engine/Engine.cs`**: `ClientChangedWindowSize` no longer touches the device — it only sets a
  `_pendingResize` flag (it runs inside the resize `WndProc`). New `ApplyPendingResize()` runs at the
  top of `Update` (game thread, outside `WndProc`, before the `_isActive` gate): it debounces until the
  client size settles, then updates `GameSettings`, sets `PreferredBackBuffer`, calls `ApplyChanges()`,
  sets `GraphicsDevice.Viewport = new Viewport(0, 0, w, h)`, and calls `_screenManager.UpdateResolution()`.
  This is the single, safe place the swap chain is reset. (Also fixes the stuck-resolution/black-bar
  bug in standalone, which now rebuilds render targets on user-drag too.)
- **`Editor/Anvil/Controls/MonoGameHost.cs`**: creates the `STATIC` container in
  `CreateNativeControlCore` and returns it; `ReparentGameWindow(parent)` parents the engine under the
  container. `ResizeEngineToContainer` (called from `ArrangeOverride`) reads the container's client
  rect and calls `SyncPreferredBackBuffer(w, h)` (sets `PreferredBackBuffer` **without** `ApplyChanges`
  — no UI-thread device reset) immediately before `MoveWindow`-ing the engine to the same size. The
  old `DispatcherTimer` resize-debounce path was removed (debounce now lives in the engine).
  `DestroyNativeControlCore` destroys the container. As a final safety net, the game-thread message
  pump now wraps `DispatchMessage` in try/catch so any stray resize can never hard-crash the app.

## Migrate solution to XML `.slnx` format

Replaced the legacy MSBuild `Engine.sln` with the XML-based `Engine.slnx` (supported natively by
the .NET 10 SDK, here 10.0.203). The new solution is functionally equivalent: it carries the same
`Any CPU`/`x86` platforms, builds the `Engine` (pinned to `x86`) and `Vista` projects, and keeps
`Anvil` excluded from the solution build (`<Build Project="false" />`) — matching the old `.sln`,
which had no build-config entries for Anvil. Verified `dotnet build Engine.slnx` and the
argument-less `dotnet build` both succeed (0 errors) after the swap.

- **`Engine.slnx`** (new): XML solution replacing `Engine.sln`.
- **`Engine.sln`** (removed): legacy solution file.
- **`.vscode/tasks.json`**: `publish` and `watch` tasks now point at `Engine.slnx`.
- **`bootstrap.bat`**, **`CLAUDE.md`**: build commands updated to reference `Engine.slnx`.
  (The historical `Engine.sln` mention in this changelog is left as-is.)

## Fix "+" add-button crash on Point Light + data-driven add-object catalog

Addresses [Docs/TODO.md](Docs/TODO.md): clicking the editor's **"+"** button (→ Point Light)
hard-crashed the engine and added nothing. The add path itself was already wired and fully
exception-protected (`EditorBridge.EnqueueAddPointLight` → `MainSceneLogic.AddPointLight` logs
`AddPointLight ok`), so the add *succeeded* — the crash was in the **render** path, which is not
wrapped in try/catch. The empty editor scene starts with zero point lights, so
`PointLightRenderModule.Draw` first executes its body the frame *after* a light is added. Its very
first line, `deferredPointLightParameter_Time.SetValue(...)` (gated only on
`GameSettings.g_VolumetricLights`, which defaults true), dereferenced a **null** `EffectParameter`:
the `Time` uniform is commented out of `DeferredPointLight.fx`, and MonoGame returns `null` (it does
not throw) for a missing parameter — so the `.SetValue` NRE'd in the unguarded render loop and took
down the process. No message surfaced because the froxel module logs via `Debug.WriteLine`
(compiled out of Release) and there is no global unhandled-exception handler.

- **`Engine/Renderer/RenderModules/DeferredLighting/PointLightRenderModule.cs`**: defense in depth
  so a point light can never hard-crash the engine again. Added one-time-log helpers (`WarnOnce`/
  `LogOnce`) that write to `anvil-bridge.log` via the existing `EditorBridge.Log` sink. Guarded the
  actual crash line — the `Time` parameter is only `SetValue`d when non-null (else logged once) —
  and the `SphereMeshPart` proxy mesh. Routed all six technique `Passes[0].Apply()` calls through a
  null-tolerant `ApplyTechnique` (`ApplyShader` now returns a bool so the matching
  `DrawIndexedPrimitives` is skipped when a technique is missing). Wrapped the per-light draw loop in
  try/catch → `LogOnce` so the first exception's full stack trace is always captured without
  per-frame spam.
- **`Engine/Renderer/RenderModules/DeferredLighting/FroxelRenderModule.cs`**: defensive null-check
  on the one direct `Parameters["FroxelInjectionTexture"].SetValue(...)` access (now `?.`), matching
  the `?.`-guarded sibling parameters in the same module.
- **`Editor/Anvil/Models/AddableObjectType.cs`** (new): a small catalog entry
  `{ string DisplayName; IRelayCommand AddCommand; }` whose command runs an
  `Action<IEditorBridge>` against the live bridge. The data-driven catalog makes adding a future
  object type a single line — no XAML.
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: added an `AddableObjects`
  `ObservableCollection<AddableObjectType>`, populated in `AttachBridge` with the single **Point
  Light** entry (per the TODO's "Pointlights only for now"), with commented-out Directional Light /
  Cube one-liners as the documented extension point. The existing `EnqueueAddDirectionalLight` /
  `EnqueueAddBasicEntity` bridge methods are kept (still used by Assets-panel drag-to-scene and as
  the re-enable hooks).
- **`Editor/Anvil/Views/MainWindow.axaml`**: the "+" `MenuFlyout` is now data-bound to
  `AddableObjects` (via the `#RootWindow` DataContext reach-back), with an `ItemContainerTheme`
  binding each generated `MenuItem`'s `Header`/`Command` to the `AddableObjectType`. The menu shows
  exactly one item, "Point Light", today.

## Texture binding pipeline + delete for meshes & entities

Addresses [Docs/TODO.md](Docs/TODO.md): models spawned from the editor rendered
untextured ("white"), `error.png` never appeared on texture-less models, the error
mesh showed no textures, and there was no way to delete a scene entity or an imported
model. Root cause: `EditorBridge.EnqueueAddBasicEntity` always overrode every model
with one flat `MaterialEffect`, which `MeshMaterialLibrary` only bypasses (using the
model's embedded per-mesh-part materials) when the passed material is `null`. There
was also no texture naming-convention binding at all.

- **`Engine/Recources/Assets.cs`**: added a per-model dynamic-material registry
  (`DynamicMaterials`, `RegisterMaterial`, `TryGetDynamicMaterial`, event
  `MaterialRegistered`) paralleling the dynamic-model registry. `UnregisterModel`
  removes an imported model + its material. `MakeMaterial` exposes the existing
  `CreateMaterial` path to the importer. New `BindEmbeddedTextures(Model)` (tolerant
  generalisation of `ProcessModel`) converts an FBX's embedded `BasicEffect` textures
  to engine materials; called on `ErrorModel` at load so the error mesh shows its own
  textures. `ReimportExistingModels` now also calls `TryBindStoredTextures` so textures
  dropped in a previous session are re-bound on launch (durable across restarts).
- **`Engine/Recources/AssetImporter.cs`**: convention-based texture binding. New
  `ClassifyTexture` maps a filename suffix (case-insensitive, after the last `_`) to a
  slot — `_BaseColor`/`_Albedo`/`_Diffuse`→albedo, `_Normal`, `_Roughness`,
  `_Metallic`, `_Mask`/`_Opacity`, `_Height`/`_Displacement`. `BindTextures` copies
  dropped images into `Art/Models/{key}/Textures/`, builds them via mgcb, and binds a
  material (no albedo ⇒ keeps the error material). `ComposeMaterial` builds from
  already-built textures; `ImportFbx` creates the `Textures/` folder and binds any
  convention-named siblings. `DeleteModelContent` removes a model's `Content.mgcb`
  blocks (model + textures) and its source/built/executable folders.
  `ListModelTextures` (static) lets the editor list a model's textures.
- **`Engine/Editor/EditorBridge.cs` / `IEditorBridge.cs`**: `EnqueueAddBasicEntity`
  material selection is now: ERROR mesh → `null` (own textures); convention-bound import
  → its material; import w/o textures → `ErrorMaterial` (visible `error.png`); built-in
  → `null` (embedded per-mesh-part materials, so Sponza/Helmets keep textures). New ops
  `EnqueueImportTextures` (binds + updates already-placed instances in place via
  `ApplyMaterialToExistingInstances`/`CopyMaterialSlots`) and `EnqueueDeleteModelAsset`
  (unregister + delete from disk). New reads `GetModelTextureFiles`, `IsDeletableModel`.
  Subscribes to `Assets.MaterialRegistered` → `ModelRegistryChanged` for UI refresh.
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: the Assets "Textures" folder
  now holds one subfolder per imported model (id `tex:{key}`), listing dropped texture
  files. New `ImportTexturesFromDisk`, `DeleteModelAsset`, `DeletableModelKeyFor`, and a
  `DeleteSceneObject` command (the existing `DeleteSelected` command is now wired to UI).
- **`Editor/Anvil/Views/MainWindow.axaml(.cs)`**: `AssetsPanel_Drop` routes image files
  to the imported model whose Textures-folder/mesh node they were dropped on (`.fbx`/
  `.obj` still import as models). Context-menu **Delete** on Hierarchy items
  (`DeleteSceneObjectCommand`) and Assets items (with a confirmation dialog, since it
  deletes files from disk); **Delete** key bound on both trees.

## Mesh → scene gestures + corrected error model

Follow-up to the robust-import work: dragging a mesh from the Meshes folder into
the scene didn't spawn anything, and the error model was replaced with a working
FBX.

- **`Editor/Anvil/Views/MainWindow.axaml.cs` / `MainWindow.axaml`**: the
  `AssetsTree_PointerPressed` drag-start handler was attached via a XAML attribute,
  which ignores handled events — but `TreeViewItem` marks `PointerPressed` as handled
  for selection, so the handler never ran and no drag began. It's now registered in
  code-behind on `AssetsTreeView` with `handledEventsToo: true` (bubble) so it fires
  regardless. Added an `AssetsTree_DoubleTapped` handler (also `handledEventsToo`) as a
  reliable drag-free way to add a mesh to the scene: double-click it. Shared model-key
  resolution extracted into `ModelKeyFromVisual`. Removed the redundant XAML
  `PointerPressed`/`PointerMoved` attributes.
- **`Engine/Content/Content.mgcb`**: added `Art/Error/ERRORText.fbx` (the corrected
  error mesh, supplied with its sibling textures). The earlier broken `Art/error.fbx`
  is gone; `Art/error.png` remains the default error albedo for imported models.
- **`Engine/Recources/Assets.cs`**: `ErrorModel` now loads `Art/Error/ERRORText`
  (still falling back to the `Cube` primitive if it can't load).

## Robust FBX import — no-crash, texture-optional, error fallbacks

Addresses the issues in [Docs/TODO.md](Docs/TODO.md): imports failed and crashed
when an FBX's textures lived in a subfolder, the broken `Content.mgcb` entry was
left behind (breaking the next startup build), and imported models did not survive
a restart. The import pipeline now never throws, never corrupts `Content.mgcb`,
always yields a draggable model (real or placeholder), and rehydrates prior imports
on launch.

- **`Engine/Content/Content.mgcb`**: added a build entry for `Art/error.png`
  (the fallback / "this asset is broken" texture). `Art/error.fbx` was intentionally
  **not** added — the supplied file has a material with an empty texture slot that
  crashes MonoGame's FBX importer at import time; re-export it without empty texture
  slots to use a custom error mesh.
- **`Engine/Recources/Assets.cs`**: added `ErrorModel` / `ErrorTexture` /
  `ErrorMaterial`, loaded in `Load()` (wrapped so a missing error asset never blocks
  boot). `ErrorModel` falls back to the `Cube` primitive since `error.fbx` is not
  buildable; `ErrorMaterial` uses `error.png` as its albedo. New
  `ReimportExistingModels()` scans the built `Content/Art/Models/{key}/{key}.xnb`
  outputs at startup and re-registers each via `RegisterModel`, so models imported in
  a previous session reappear in the Meshes folder and saved scenes can resolve them.
- **`Engine/Recources/AssetImporter.cs`**: the sibling-texture scan is now
  **recursive and preserves relative paths** (`SearchOption.AllDirectories` +
  `Path.GetRelativePath`), so textures stored in subfolders resolve where the
  ModelProcessor expects them. `AppendMgcbEntries` returns the appended text;
  `RunMgcbBuild` is wrapped so a build failure **rolls back** the just-added
  `Content.mgcb` block (new `RemoveMgcbBlock`), deletes the copied sources, and
  registers the error model under the requested key instead of throwing. The final
  `ContentManager.Load` is likewise guarded — `ImportFbx` always returns a registered
  key (real model or error placeholder).
- **`Engine/Editor/EditorBridge.cs`**: `EnqueueAddBasicEntity` now spawns
  runtime-imported models with `ErrorMaterial` (so they render with the `error.png`
  default texture instead of the base red material), keeps `BaseMaterial` for
  built-ins, and substitutes `ErrorModel` if a model's geometry never loaded.
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: `OnBridgeSnapshot` now also
  repopulates the Meshes folder when it is empty but the bridge reports models —
  closing the race where built-in models existed (and were registered) but were not
  visible/draggable because the folder was built before the bridge reported them.

## Runtime asset import pipeline (drop FBX → drag into scene)

Addresses [Docs/TODO.md](Docs/TODO.md): replace hard-coded scene creation with an
editor-driven flow where the user drops a `.fbx` (plus sibling textures) on the
Anvil Assets panel and drags from there into the Hierarchy to spawn a `BasicEntity`.

- **`Engine/Recources/Assets.cs`**: added dynamic registry alongside the existing
  hard-coded public-field model list. `RegisterModel(key, ModelDefinition)`
  with auto-dedup (`_2`, `_3`, …) and a `ModelRegistered` event. `Load()` now
  caches `Content`/`GraphicsDevice` so `AssetImporter` can reuse them.
- **`Engine/Recources/AssetImporter.cs`** (new): runtime importer that copies the
  source file into `Engine/Content/Art/Models/{key}/`, scans sibling images
  (.png/.jpg/.tga/.dds/.bmp), appends matching `Content.mgcb` entries under a
  cross-process mutex, invokes `mgcb` (via `dotnet mgcb`, falling back to
  `MGCB_PATH` env var or the legacy MSBuild install) to compile, copies the
  resulting `.xnb` next to the executable, then loads through the live
  `ContentManager` and calls `Assets.RegisterModel`. Mirrors the
  `ShaderManager.ShaderChanged` hot-reload pattern but without its hard-coded
  mgcb path.
- **`Engine/Editor/IEditorBridge.cs` / `EditorBridge.cs`**: new
  `EnqueueImportModel(string sourcePath, Action<string> onCompleted)` op and
  `ModelRegistryChanged` event. `BuildModelKeys` now unions
  `Assets.DynamicModels` with the reflection-scanned hard-coded fields, so
  imported models flow through the existing `EnqueueAddBasicEntity` path.
- **`Engine/Logic/MainSceneLogic.cs`**: `SetUpEditorScene` →
  `SetUpEmptyEditorScene`. Removed hard-coded Sponza spawn, 11×11 plane grid,
  Stanford dragon, physics sphere + 10-sphere roughness sweep, decal, and two
  foreground point lights. Boot scene is now just the editor + main cameras,
  environment sample, SDF generator, and one sun-like directional light.
  Removed the unused `testEntity` field.
- **`Editor/Anvil/Views/MainWindow.axaml`**: Assets panel `Border` now
  `AllowDrop`s file drags. Assets `TreeView` (`AssetsTreeView`) exposes
  `PointerPressed`/`PointerMoved` for drag-start. Hierarchy `TreeView`
  (`HierarchyTreeView`) accepts drops carrying the custom `obsidian/modelKey`
  data format.
- **`Editor/Anvil/Views/MainWindow.axaml.cs`**: handlers `AssetsPanel_DragOver`/
  `AssetsPanel_Drop` (accept Windows-Explorer file drops, route .fbx/.obj into
  `ImportFbxFromDisk`), `AssetsTree_PointerPressed`/`AssetsTree_PointerMoved`
  (threshold-gated drag start of mesh nodes, carries the model key),
  `Hierarchy_DragOver`/`Hierarchy_Drop` (accept model keys, call
  `AddEntityFromAsset`).
- **`Editor/Anvil/ViewModels/MainWindowViewModel.cs`**: subscribed to
  `bridge.ModelRegistryChanged`; `BuildAssets()` reduced to empty folder stubs
  + live `Meshes` folder repopulated from `AvailableModelKeys` whenever the
  registry changes. New methods: `ImportFbxFromDisk(path)`,
  `AddEntityFromAsset(modelKey)`, `RefreshMeshAssetsFolder()`,
  `OnBridgeModelRegistryChanged()`.

## Editor overhaul (dev branch)

Implements the TODO in `Docs/TODO.md`. Phased plan in
`C:/Users/mano3/.claude/plans/docs-todo-md-read-the-todo-wobbly-fern.md`.

### Phase 0 — Foundations

- **EditorBridge logging**: secondary log path at `%LOCALAPPDATA%/Anvil/anvil-bridge.log`
  when the Desktop write fails; static-ctor bootstrap line so crashes before `Bind()`
  still leave a breadcrumb; `MonoGameHost` now logs engine construction + a fatal
  catch for `GameThreadProc` so an early crash is no longer silent.
- **`EditorObjectKind.Decal`** added; `EditorBridge.BuildSnapshot` and
  `LookupById` now include decals so they show up in the hierarchy.
- **`Engine/Renderer/Helper/Picking.cs`**: `ScreenPointToWorldRay`,
  `RayIntersectsAabb`, `PickEntity(ray, entities)`, `ComputeWorldBoundingBox`.

### Phase 1 — Scene + SceneManager refactor & inspector focus fix

- **`Engine/Logic/Scene.cs`** owns the lists previously hard-wired into
  `MainSceneLogic`: `BasicEntities`, `Decals`, `PointLights`, `DirectionalLights`,
  `EnvironmentSample`, `MainCamera`, plus `Name`, `FilePath`, `IsDirty`.
- **`Engine/Logic/SceneManager.cs`** owns the active `Scene`, exposes
  `SceneChanged` event, `NewScene`, `LoadScene`, `SaveScene`.
- **`MainSceneLogic`** keeps runtime drivers (physics, mesh library, SDF, editor
  camera, debug entities) and forwards `BasicEntities`/`Decals`/`PointLights`/
  `DirectionalLights`/`EnvironmentSample`/`Camera` to the active scene so existing
  callers (ScreenManager, Renderer, EditorLogic) compile unchanged. `OnSceneChanged`
  detaches old physics bodies, clears `MeshMaterialLibrary`, and re-registers the
  new scene's content.
- **`MeshMaterialLibrary.Clear()`** added.
- **Inspector focus fix**: `BridgeReconciler.IsInspectorFocused` + `SelectedEngineId`
  combined with window-wide focus tracking in `MainWindow.axaml.cs`. When the user
  is editing a control inside the inspector ScrollViewer (`InspectorScroll`), the
  reconciler skips numeric/material/light writes to the selected object — focus is
  no longer stolen mid-edit. Also stopped nulling `vm.Material` / `vm.Light` /
  `vm.Camera` while the inspector is focused (was collapsing open expanders/flyouts).

### Phase 2 — `.obsc` scene file format

- **`Engine/Logic/SceneSerialization.cs`** — System.Text.Json-based, schema-versioned
  (`Version: 1`). Custom `Vector3`/`Quaternion`/`Color` converters keep files compact
  (`[x,y,z]`). Models/textures referenced by their `Assets` field-name (the same
  string the bridge uses for the model picker). Material values inlined per-entity
  on top of a cloned `BaseMaterial`. Rotation stored as quaternion to avoid matrix
  drift on round-trip. `IdGenerator.Reseed(maxId)` after load.
- **Bridge ops**: `EnqueueNewScene`, `EnqueueLoadScene`, `EnqueueSaveScene`,
  `CurrentScenePath`, `CurrentSceneName`, `IsSceneDirty`, `event SceneChanged`.
- **Anvil File menu** wired to `NewSceneCommand` / `OpenSceneCommand` /
  `SaveSceneCommand` / `SaveSceneAsCommand`. Uses Avalonia `StorageProvider` for
  file pickers; remembers the last scene folder.

### Phase 3 — Editor camera

- **`Engine/Entities/EditorCamera.cs`** — `Camera` subclass with scalar `Yaw`/`Pitch`,
  pitch clamped to ±88.8° to prevent flip, `ApplyYawPitch()` rebuilds forward.
- **`Input.UpdateEditorCamera`** drives the new editor camera with DCC-style
  controls: RMB drag = look (yaw + pitch), MMB drag = pan along right/up,
  scroll wheel = zoom along forward, WASD/QE = fly *only while RMB held* (so it
  doesn't fight the Avalonia UI).
- `MainSceneLogic.Camera` now picks between `EditorCamera` (edit mode) and
  `ActiveScene.MainCamera` (play mode). Game camera serialized to `.obsc`,
  editor camera is not.

### Phase 4 — Tool buttons → gizmo wiring

- `IEditorBridge.RequestGizmoMode(GizmoModes?)` with null meaning the Select tool
  (gizmo hidden, picking only). `EditorLogic.IsGizmoSuppressed` flag honored by
  the LMB click branch.
- `MainWindowViewModel.OnActiveToolChanged` forwards Move/Rotate/Scale → engine
  gizmo mode; Select sets the suppressed flag. T/R/Z hotkeys still work.

### Phase 5 — Raycast object selection fallback

- `EditorLogic.Update` now falls back to `Picking.PickEntity(ray, entities)` when
  the render-target ID buffer reports no hit. Lets selection keep working in
  render modes that skip the ID pass.

### Phase 6 — Play/Stop loop hook

- **`Engine/Logic/PlayMode.cs`** — `GameMode { Edit, Play }` + `PlayModeController`.
  `Play()` snapshots transforms (entities/decals/lights), drops
  `GameSettings.e_enableeditor`, and fires `OnStart` on every attached script.
  `Stop()` restores transforms and the editor flag.
- Bridge: `Mode`, `RequestPlay`, `RequestStop`, `event ModeChanged`. Anvil's
  existing Play/Stop button (`TogglePlay` command) now drives these; engine-side
  mode changes flow back to keep `IsPlaying` in sync.

### Phase 7 — `IScript` sketch

- **`Engine/Scripting/IScript.cs`** — `IScript { OnStart(ctx); OnUpdate(ctx, gt); }`
  + `IScriptContext { Owner, Scene }`. Scripts attached via
  `BasicEntity.Scripts` (`List<IScript>`). Not serialized in `.obsc` v1.
- `PlayModeController.UpdateScripts(gameTime)` ticks scripts every frame while
  in Play mode; called from `MainSceneLogic.Update`. Exceptions logged, not fatal.

### Phase 8 — TODO bugfix pass

Addresses the `Docs/TODO.md` issue list. HelperSuite removal is intentionally
deferred until Vista/Anvil cover the in-engine GUI surface.

- **`IEditorBridge.IsHostedByEditor`** — set by `MonoGameHost` before the first
  frame ticks. Anvil now hides the legacy HelperSuite GUI (Update + Draw) so the
  in-engine panel doesn't double up with Anvil's inspector. Standalone
  `Engine.exe` is unchanged — the side panels still appear and Space still toggles.
- **`GameStats.e_EnableSelection` defaults on under Anvil** — this flag gates the
  ID-buffer / outline / gizmo render passes. In standalone it defaulted to `false`
  and the legacy HelperSuite GUI's "Editor Mode" toggle flipped it. With that GUI
  hidden under Anvil, viewport clicks resolved to no entity and felt dead.
  `ScreenManager.Initialize` now forces it on when `IsHostedByEditor` is true;
  standalone keeps the default-off behaviour.
- **Add GameObject crash** — `EditorBridge.RequestSelect` no longer clobbers the
  current selection with null when `LookupById` misses. A transient miss (entity
  just added, not yet in snapshot; or just deleted) was causing the Inspector to
  open then close immediately. Combined with the GUILogic gating above, the
  legacy in-engine inspector no longer touches freshly added entities.
- **NewScene break** — `MainSceneLogic.OnSceneChanged` now populates a default
  `MainCamera` + `EnvironmentSample` on empty scenes (a fresh `Scene{}` has both
  null and the renderer's environment probe NREs). Also calls `PlayMode.Stop()`
  on scene swap so Play-mode state from the old scene can't bleed into the new
  one. Matches the implicit reset that LoadScene already gets through deserialized
  fields.
- **Save/load drops textures** — `SceneSerialization.ResolveMaterial` now returns
  `null` for entities saved without a custom material so `BasicEntity` keeps the
  model-embedded materials (Sponza textures stay intact). For entities with a
  custom material, `MaterialRecord` now persists `AlbedoKey`/`NormalKey`/
  `RoughnessKey`/`MetallicKey`/`MaskKey` looked up via `Assets`' field names, and
  the load path restores those textures.
- **RMB + WASD camera** — `IEditorBridge.SetHostKeyState` / `IsHostKeyDown` plus
  a thread-safe `HashSet<int>` on `EditorBridge`. `MonoGameHost` subscribes to
  `KeyDown`/`KeyUp` at the `TopLevel` (so the engine sees WASD even when focus
  is on the toolbar) and forwards mapped Avalonia keys. `Input.IsKeyDown(Keys)`
  merges native + forwarded state; `EditorCameraEvents` uses it for WASD/QE.
- **Select tool** — `EditorLogic.EditorSendData.GizmoSuppressed` propagated to
  `EditorRender.DrawGizmo` and `IdAndOutlineRenderer.DrawGizmos`. Both now skip
  the arrow draw when Anvil's Select tool is active, so the visible gizmo
  doesn't intercept clicks and the ID buffer never returns gizmo IDs 1-3.
- **Inspector focus** — `MainWindow.OnAnyLostFocus` no longer leaves
  `IsInspectorFocused` stale when focus exits the inspector. It defers to the
  next dispatcher tick and reads `FocusManager.GetFocusedElement`, clearing the
  flag once focus has settled outside the inspector ScrollViewer.

### Phase 9 — HelperSuite removal + post-processing migration

- **HelperSuite project deleted.** `HelperSuite/` removed from disk;
  `ProjectReference` dropped from `Engine.csproj`; project + per-config
  entries dropped from `Engine.sln`. `Engine/Logic/GUILogic.cs` deleted
  (entirely depended on HelperSuite controls). All `using HelperSuite.*`
  removed from `Engine.cs`, `ScreenManager.cs`, `EditorLogic.cs`,
  `ShaderManager.cs`. `GUIControl.Initialize` call removed from
  `Engine.Initialize`; the `!GUIControl.UIWasUsed` gate in `EditorLogic.Update`
  is gone. `ScreenManager` no longer holds `_guiLogic` or `_guiRenderer` —
  Load/Initialize/Update/Draw/Dispose simplified accordingly.
- **`IEditorBridge.EnqueueGameThreadAction(Action)`** — generic queue for
  arbitrary engine-thread work. The post-processing VM uses it so shader
  parameter setters in `GameSettings`/`Shaders` are pushed from the UI
  thread but actually executed between frames on the game thread.
- **`PostProcessingViewModel`** (Anvil) — mirrors the toggles that used to
  live in HelperSuite's right-side panel: TAA/Tonemap/WhitePoint/Exposure/
  S-Curve/Chromatic Aberration/Color Grading, SSR (enable + stochastic +
  temporal noise + firefly + thresholds + sample counts), SSAO (enable +
  blur + samples + radius + strength), Bloom (enable + threshold + 5 MIP
  radius/strength pairs), Viewport (highlight meshes + SDF distance/volume).
  One-shot read from `GameSettings` on attach; setters marshal writes
  through `EnqueueGameThreadAction`.
- **Inspector view switch.** Added `InspectorView` to
  `MainWindowViewModel` (`"Selection"` / `"PostProcessing"`) +
  `SetInspectorViewCommand`. `IsInspectorSelectionView` /
  `IsInspectorPostProcessingView` gate the two ScrollViewers in the
  inspector pane. Header chips ("Selection" / "Post FX") flip between them.
- **`Window > Post Processing` menu entry** replaces the redundant
  `Window > Assets` item (Assets was already a top-level menu). Clicking it
  invokes `SetInspectorViewCommand` with `"PostProcessing"`.
- **Docs**: `CLAUDE.md` solution-projects list pruned. `Docs/TODO.md`
  cleared — the previous items all landed in Phase 8 / Phase 9.
