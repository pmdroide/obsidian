# Persistent gameobjects

A persistent gameobject survives scene loads during Play. When a script calls
`GameFlow.LoadScene(...)`, ordinary gameobjects unload with their scene. A persistent one moves
into the new scene **still running**:

- its scripts keep their instance and state, with no `Stop`/`Start`;
- its physics body keeps its velocity;
- HUD layers its scripts opened stay open.

Use this for a player that walks from level to level, a music or game-state manager, or a companion.

This is the engine's version of Unity's `DontDestroyOnLoad`.

## Making a gameobject persistent

- **In Anvil:** select the gameobject and tick **Persistent** in the Inspector, under Role. The
  flag is saved with the scene (`"Persistent": true` on the entity record).
- **From a script:** call `DontDestroyOnLoad()` for the script's own gameobject, or
  `DontDestroyOnLoad(entity)` for another one, such as an object it spawned. This lasts for the current
  Play session only and is never saved.

```csharp
public sealed class MusicManagerScript : ScriptBehaviour
{
    public override void Start() => DontDestroyOnLoad();
}
```

`IsPersistent` (on the script, or `BasicEntity.IsPersistent`) tells you whether either one applies.
Only gameobjects can be persistent. Each scene has its own Main Camera, so a script that drives the camera
should write to `MainCamera` every frame; it then follows whichever scene is loaded.

## What happens on a scene load

The load is queued and happens at the start of the next frame:

1. Persistent gameobjects leave the departing scene.
2. The departing scene's scripts stop and its transforms rewind, as before. The persistent ones are not
   touched.
3. The new scene loads. Its own copies of the carried gameobjects are left out (see below).
4. The carried gameobjects join the new scene, after its own gameobjects. A carried gameobject whose
   ID the new scene already uses gets a new one.
5. The new scene's scripts start. They can already find the carried gameobjects, for example with
   `FindGameObject("Player")`.
6. `OnSceneLoaded()` runs on the carried gameobjects' scripts. Move them to a spawn point here:

```csharp
public override void OnSceneLoaded()
{
    BasicEntity spawn = FindGameObject("Player Spawn");
    if (spawn != null) Position = spawn.Position + new Vector3(0, 0, 1.1f);
    Velocity = Vector3.Zero;
}
```

Contacts with the old scene's colliders end on the next physics step, with the usual
`OnCollisionExit` / `OnTriggerExit`.

### Leaving them behind

`GameFlow.LoadScene(index or name, carryPersistent: false)` doesn't carry anything. Persistent gameobjects
stop with the departing scene and unload with it, so the new scene starts with its own copies. Use it for
**Main menu** or **Quit to title**, where the player shouldn't follow. Inside Anvil, the ones from the
edited scene go straight back to it, as Stop would put them.

## Duplicates: the copy that came along wins

When the new scene contains its own copy of a carried gameobject, that copy is left out. A copy is
either:

- **the same gameobject from its file.** Returning to the scene a gameobject came from, or reloading it
  with `GameFlow.ReloadScene()`, doesn't create a second one. The match is the scene file plus the ID the
  gameobject had there.
- **a gameobject marked Persistent with the same name.** You can put a Persistent "Player" in every
  level, so each level can be played on its own. When a player arrives from another level, the level's
  own Player is left out.

## Inside Anvil

Switching scenes during Play already returns you to the scene you were editing when you press **Stop**.
Persistent gameobjects follow the same rule:

- Gameobjects that came from the edited scene go back to it. They return to their place in the
  Hierarchy, with their ID and the transform they had when Play started.
- Gameobjects picked up in other scenes, and ones spawned at runtime, are dropped.
- `DontDestroyOnLoad` is cleared.

In the standalone game there is nothing to return to: a carried gameobject simply belongs to the scene
it is in.

## Try it: the PersistenceTest sample

The sample is `Content/Scenes/PersistenceTest.obsc` and `PersistenceTest2.obsc`, both in the scene list.
Open **PersistenceTest** and press Play.

- **Player:** a Persistent third-person capsule running the **Persistent Player** script
  (`Content/Scripts/PersistentPlayerScript.cs`).
  - Controls: WASD to move, Shift to run, Space to jump. Hold the right mouse button (or use the arrow
    keys) to orbit; the mouse wheel zooms. R reloads the scene.
  - Its HUD (`Content/UI/PersistenceTest.xml` + `.css`) shows the play time, the distance walked, the
    scene loads it survived and its route. All of these live in the one script instance.
- **Portal:** walk into the glowing panel (a static trigger running **Scene Portal**,
  `ScenePortalScript.cs`). It loads PersistenceTest2, a canyon at sunset.
  - The same player arrives at the canyon's **Player Spawn**, with its counters still going.
  - The canyon's own Player is left out. That Player is there so the canyon can also be played on its
    own.
  - The canyon's portal leads back to the courtyard.
- **Companion Orb:** the golden orb on the plinth is *not* marked Persistent. Walk up to it and its script
  (`CompanionOrbScript.cs`) calls `DontDestroyOnLoad()` and follows you through the portals. Leave it,
  and it stays behind.
- **Crates:** they are not persistent, so they stay in the courtyard.
- **Pause menu:** press Esc. The Player also runs the **Pause Menu** script. See below.

`dotnet run --project Tests/Components/Components.csproj` plays this route headlessly
(`Tests/Components/PersistenceChecks.cs`). Add `--graphics` to also check that the HUD layer stays open.

## Example: a pause menu that comes along

A pause menu has to work in every scene. Put it on a persistent gameobject and one instance does. The
PersistenceTest Player has a second Script Behaviour, **Pause Menu** (`Content/Scripts/PauseMenuScript.cs`).
Its document is `Content/UI/PauseMenu.xml` + `.css`.

- **Esc** (the `PauseKey` field) or gamepad **Start** sets `GameFlow.Paused`. That freezes every other
  script, component and physics body. The menu keeps running because it returns true from
  `UpdateWhilePaused`.
- It opens its Vista layer when you pause and closes it when you resume, so it draws above the player's HUD.
- **Resume**, **Restart scene** (`GameFlow.ReloadScene()`: the player and this menu carry on),
  **Settings** (master volume and rendering options, shared with the main menu), **Main menu**
  (`LoadScene(0, carryPersistent: false)`: the player stays behind) and **Quit game** (standalone only;
  inside Anvil, press Stop).
- Its **This session** card shows state that lives in the one script instance and keeps counting across
  scene loads: time played, time in this scene, scene loads, pauses and the route.

To use it in your own game, add **Pause Menu** to your persistent player (or to a persistent manager
gameobject). If its gameobject isn't persistent, the menu still works, but each scene gets a fresh one.

`dotnet run --project Tests/Components/Components.csproj` checks it headlessly
(`Tests/Components/PauseChecks.cs`): pausing freezes a falling player in mid-air, Restart carries the menu
along, and Main menu leaves the player behind. With `--graphics`, it also checks the layer and the Settings
page.

## Limits

- Only gameobjects can be persistent: lights, decals and the Main Camera belong to their scene.
- Nothing carries over between Play sessions. Stop (or quitting the game) ends persistence. So does a
  `LoadScene(..., carryPersistent: false)`.
- Opening a scene from Anvil's Assets panel during Play replaces everything, persistent gameobjects
  included.
- The legacy `IScript` list on `BasicEntity.Scripts` is carried along but gets no `OnSceneLoaded`.
