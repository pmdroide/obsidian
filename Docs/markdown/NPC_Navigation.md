# NPCs and navigation

The **NPC** component makes a gameobject a computer-controlled character during Play. It walks
around walls on a navigation grid, idles (stands, wanders or patrols), watches for its target
(normally the gameobject named `Player`) and reacts when it sees it: it chases and attacks, follows,
flees or watches. Try it in the `NpcTest` sample scene.

Outside Play nothing moves. Stopping Play puts every NPC back where it was.

## Making an NPC

1. Add a gameobject with the **Capsule** model (or any model) and a **Physics** component:
   **Dynamic**, about 70 kg, with **Freeze Rotation** so it stays upright.
2. Add **Inspector > Add Component > NPC**.
3. Turn the gameobject so its **+Y** points where it should look at the start. +Y is its forward.
4. Name the player `Player`, or drag it from the Hierarchy into the NPC's **Target** slot. The player needs a
   collider (a Physics component) so that sight rays can hit it.
5. Give the scene's floor and walls **Static** Physics components. The navigation grid is built from
   them.

The NPC moves its Dynamic body by setting its velocity, like the sample players do. Gravity keeps it on
the ground and the solver keeps it out of walls, so it pushes crates and gets shoved itself. Without a
Dynamic body it moves the gameobject directly and keeps its starting height above the grid's ground.
Don't give an NPC a **Static** body: a moving static rebuilds the grid.

## Settings

| Setting | Effect |
| --- | --- |
| **Walk Speed** / **Run Speed** | m/s. It runs while chasing and fleeing, and while catching up with a target it follows that is far ahead. Otherwise it walks. |
| **Turn Speed** | Degrees per second. |
| **Acceleration** | The largest change of speed per second (m/s²), so it can't stop or turn on the spot. |
| **Radius** | The clearance it keeps from walls on paths, in m. 0 uses the collider's half width. |
| **Behaviour** (Idle) | **Stand**: stays at its start position, facing its start direction, and walks back there after a chase. **Wander**: walks to random reachable points within **Wander Radius** of its start. **Patrol**: walks between the gameobjects listed in **Patrol Points** in a loop. |
| **Patrol Points** | Drop Hierarchy gameobjects to append waypoints in order. Clear to rebuild the route. Stored as comma-separated names; markers without a collider work well. |
| **Wait Time** | Seconds it waits at each wander or patrol point. Each wait varies by up to half of this either way. |
| **Target** | Drop the gameobject it watches for from the Hierarchy; stored by name. |
| **Sight Range** / **Field of View** | How far it sees (m) and how wide its view cone is (degrees, centred on +Y). |
| **Hearing Range** | Within this distance it notices the target in any direction. Walls still block it. |
| **Eye Height** | Height above the gameobject's origin where sight rays start. |
| **Reaction Time** | Seconds between noticing the target and reacting. |
| **Memory Time** | Seconds it keeps going for the target after losing sight of it. |
| **On Sight** (Reaction) | **Chase**: runs after the target and attacks it within **Attack Range**, every **Attack Cooldown** seconds. An attack is an impulse of **Attack Force** N·s on the target's Dynamic body, pushing it away and slightly up. **Follow**: walks up to the target and stays **Follow Distance** away, facing it. **Flee**: runs **Flee Distance** away from the target, again and again, until it is out of sight. **Watch**: stands still and turns to face the target. |
| **Search Time** | Chase and Follow only. Seconds it looks around where it lost the target before it goes back to idling. |
| **Show Debug** | Draws its path (cyan), its view cone (yellow, or orange-red while aware) and a red line to the target while it sees it. Debug lines must be on. |

`Behaviour` and `On Sight` are saved by name, so their values are never renamed.

## States

`NpcComponent.State` tells what it is doing:

```
Idle / Wandering / Patrolling ──sees target──> Alerted ──Reaction Time──> Chasing <──> Attacking
        ^                                                              ├─> Following
        |                                                              ├─> Fleeing
        |                                                              └─> Watching
        |                                                 lost for Memory Time
        └──── Returning <── (Stand) ── Searching <───────────(Chase, Follow)
                                                  (Flee, Watch go straight back to idling)
```

- **Alerted**: it stops and turns to where it saw the target, then reacts.
- **Chasing**: it paths to the target while it sees it, otherwise to where it last saw it. If it gets
  there and the target is gone, it searches.
- **Searching**: it walks to the last known position and looks left and right for **Search Time**.
- **Scripted**: it walks where a script sent it with `MoveTo`.

Seeing the target interrupts every unaware state, including Searching and Scripted.

## Sight

Every 0.1 s the NPC looks for its target. The target is seen when all of these hold:

- It is within **Sight Range** of the eyes.
- It is inside the view cone (**Field of View** around the NPC's facing), or within **Hearing Range**.
- A ray from the eyes reaches its middle or its upper part without hitting another collider first.
  Triggers are see-through. A target without a collider counts as seen when nothing stands in front of
  that point.

A 0.6 m hedge blocks walking but not sight. A 2 m wall blocks both.

## The navigation grid

`Engine/AI/NavGrid.cs` is a height field of 0.25 m cells seen from above (Z is up). For every cell a
ray goes straight down through the scene's **static** colliders (`ScenePhysics.RaycastStatic`). The
first surface it hits is the cell's ground, unless it is steeper than 40°.

- A cell is **walkable** when the cells within the agent's clearance (its radius plus half a cell) have
  ground within a step of it. The step is 0.45 m plus the slope over the distance. So walls (a jump up),
  ledges (a drop), holes and the ground's edge keep their distance. A ramp is fine.
- Dynamic bodies (crates, characters) are see-through for the grid. NPCs push light ones aside. When an
  NPC barely moves for a second, it plans again, and after three tries it drops the path.
- Only the topmost surface counts, so there is one walking level. There are no paths under bridges or
  tables, and a table top is its own island.
- Connected walkable areas get a region number, so an unreachable goal is known straight away. A goal
  on another island (a platform without a ramp) gives a path that ends as close as possible, with
  `complete` (`NpcComponent.PathComplete`) false.

`FindPath` runs A* over the 8 neighbours of each cell, with no squeezing diagonally past a blocked
corner. It then shortens the result to the corners that matter, where every straight segment crosses
only walkable cells. While walking, an NPC heads for the next corner as soon as it can walk to it in a
straight line.

`Navigation.For(physics, entities, settings)` (`Engine/AI/Navigation.cs`) shares one grid per physics
scene and agent size (radius rounded to 5 cm). Once per physics frame it checks whether a static
collider was added, removed, moved, turned or resized. If one was, it rebuilds the grid, at most every
30 frames. A level reshaped by a script can force a rebuild with `Navigation.Invalidate()`. Scenes
larger than 160,000 cells get bigger cells (up to 1 m). Beyond that, the grid covers a square around the
first NPC that asks.

## Scripts

An NPC's own Script Behaviours get four hooks:

| Hook | When |
| --- | --- |
| `OnTargetSpotted(BasicEntity target)` | It noticed the target (saw it, heard it close by, or was alerted). |
| `OnTargetLost(BasicEntity target)` | It gave up after losing sight of the target for Memory Time. |
| `OnNpcStateChanged(NpcState previous, NpcState current)` | Its state changed, for example to switch an animation or a colour. |
| `OnNpcAttack(BasicEntity target)` | It attacked (Chase, within Attack Range). |

The component also raises the C# events `StateChanged`, `TargetSpotted`, `TargetLost` and `Attacked`,
so other gameobjects' scripts can listen. The sample player subscribes to `Attacked` to count hits.

From any script, on `GetComponent<NpcComponent>()` or another gameobject's NPC:

| Member | What it does |
| --- | --- |
| `MoveTo(position)` | Walks there (state Scripted) and stands. |
| `Investigate(position)` | Goes to look around a point (state Searching), unless it is already busy with the target. |
| `Alert(target)` | Notices `target` now, wherever it is. |
| `Calm()` | Forgets the target and goes back to idling. |
| `CanSee(gameobject)` | The sight test above, right now. |
| `TargetObject` | Set it to watch a gameobject directly instead of looking one up by name. |
| `State`, `IsAware`, `CanSeeTarget`, `CurrentTarget`, `LastKnownTargetPosition`, `Home`, `Path`, `HasPath`, `PathComplete`, `Facing`, `Attacks` | Runtime state, never saved. |

Any script can also plan a path without an NPC component:

```csharp
var path = new List<Vector3>();
if (FindPath(Position, FindGameObject("Exit").Position, path))
    foreach (Vector3 corner in path) Log($"walk to {corner}");
```

A sentry that sends a guard over, as in the sample's **NPC Reporter** script:

```csharp
public BasicEntity AlarmTarget;   // Inspector: pick the guard

public override void OnTargetSpotted(BasicEntity target)
{
    NpcComponent guard = AlarmTarget?.GetComponent<NpcComponent>();
    if (guard != null && !guard.IsAware) guard.Investigate(target.Position);
}
```

## The NpcTest sample

`Content/Scenes/NpcTest.obsc` is a 40 x 40 m walled yard. It is last in the scene list. It contains:

- **Guard** (dark red) patrols four floor markers in the south-east yard (Patrol, Chase). It is slower
  than a running player. Three hits knock the player out, and the player wakes up at the start.
- **Villager** (green) wanders in the south-west and follows the player once it sees it (Wander, Follow).
- **Critter** (small, orange) wanders and runs away (Wander, Flee). It has a wide view and good hearing.
- **Sentry** (blue) stands on the north-west lookout and watches (Stand, Watch). Its reporter script
  raises the alarm: the guard comes to search where the sentry saw the player.
- Walls, a hedge you can see over but not walk through, pillars, crates the NPCs shove aside, and the
  lookout with a ramp, so you can lead NPCs up it.

The player (`NpcTestPlayerScript`) is a third-person capsule:

- **WASD** to move, **Shift** to run, **Space** to jump.
- Hold the right mouse button (or the arrow keys) to orbit the camera.
- **F** shouts: every NPC within 12 m notices you, even through walls.
- **T** toggles every NPC's debug lines.
- **R** reloads the scene, and **Esc** pauses.

The HUD (`Content/UI/NpcTest.xml`/`.css`) lists each NPC's state, your hits, and the event feed
(`NpcTestFeed`).

## Verification

`Tests/Components/NpcChecks.cs` checks:

- the grid: walls, clearance, steps, ramps, dynamic bodies, unreachable goals, random and flee points,
  and rebuilds;
- sight;
- patrol, chase and attack, searching and resuming the patrol, a path around a wall, follow, flee and
  watch, and a body-less NPC;
- the Inspector, saving and cloning;
- a scripted Play run of the sample.

```powershell
dotnet run --project Tests\Components\Components.csproj
```
