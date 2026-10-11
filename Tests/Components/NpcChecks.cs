using System.Collections.ObjectModel;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.AI;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

/// <summary>
/// NPC pathfinding and behaviour: the navigation grid (walls, agent radius, steps, ramps, unreachable goals,
/// dynamic bodies ignored, rebuilds), the NPC component's sight checks, idle behaviours and reactions in Play
/// (patrol, chase and attack, search after losing the target, follow, flee, watch, paths around walls, a body-less
/// NPC), its editor/scene plumbing, and a scripted Play run of Engine/Content/Scenes/NpcTest.obsc. Colliders are
/// boxes from each model's bounds (ScenePhysics.FallbackGeometry), so no GPU or model files are needed.
/// </summary>
internal static class NpcChecks
{
    private const float Dt = 1 / 60f;
    private static readonly GameTime Frame = new(TimeSpan.Zero, TimeSpan.FromSeconds(Dt));

    public static void Run()
    {
        ScriptRegistry.Register<NpcRecorder>(NpcRecorder.Id, "Test NPC Recorder");
        CheckGrid();
        CheckGridCache();
        CheckSight();
        CheckPatrolChaseAndSearch();
        CheckPathAroundWall();
        CheckFollowFleeWatch();
        CheckBodylessNpc();
        CheckEditorAndSaving();
        CheckSampleScene();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  NAVIGATION GRID
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckGrid()
    {
        using var world = new CollisionChecks.World();
        world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(15, 15, 0.5f), PhysicsBodyType.Static, recorder: false);
        // 12 m long, 2 m high: x -6..6 at y = 0.
        world.Add("Wall", new Vector3(0, 0, 1), new Vector3(6, 0.25f, 1), PhysicsBodyType.Static, recorder: false);
        BasicEntity crate = world.Add("Crate", new Vector3(5, -5, 0.5f), new Vector3(0.5f), PhysicsBodyType.Dynamic, 20, recorder: false);
        world.Add("Low Step", new Vector3(-10, 10, 0.15f), new Vector3(1.5f, 1.5f, 0.15f), PhysicsBodyType.Static, recorder: false);
        // A 1.5 m high deck at x 8..12, y 8..12, with no way up yet.
        world.Add("Deck", new Vector3(10, 10, 0.75f), new Vector3(2, 2, 0.75f), PhysicsBodyType.Static, recorder: false);
        world.Step(1);

        NavGrid grid = NavGrid.Build(world.Scene, world.Entities, new NavGridSettings(0.5f));
        Check(grid is { ObstacleCount: 4, Width: 120, Height: 120 } && grid.WalkableCount > 10000,
            $"the grid covers the static colliders in {NavGrid.DefaultCellSize} m cells ({grid?.Width} x {grid?.Height}, {grid?.WalkableCount} walkable)");
        Check(!grid.IsWalkable(new Vector3(0, 0, 0)) && !grid.IsWalkable(new Vector3(0, 0.6f, 0)) && grid.IsWalkable(new Vector3(0, 1.25f, 0)),
            "the wall and the agent radius beside it are blocked, the floor beyond is walkable");
        Check(!grid.IsWalkable(new Vector3(14.8f, 0, 0)) && grid.IsWalkable(new Vector3(14f, 0, 0)),
            "the agent keeps its radius from the floor's edge");
        Check(grid.IsWalkable(crate.Position) && grid.TryGetHeight(crate.Position, out float under) && Math.Abs(under) < 0.01f,
            "dynamic bodies (a crate) don't block the grid: physics pushes them aside");
        Check(grid.IsWalkable(new Vector3(-10, 10, 0)) && grid.TryGetHeight(new Vector3(-10, 10, 0), out float step) && Math.Abs(step - 0.3f) < 0.01f &&
              grid.IsReachable(new Vector3(-10, 6, 0), new Vector3(-10, 10, 0)),
            "a 0.3 m step is low enough to walk onto");
        Check(grid.IsWalkable(new Vector3(10, 10, 1.5f)) && !grid.IsReachable(Vector3.Zero + new Vector3(0, -5, 0), new Vector3(10, 10, 1.5f)),
            "the top of the 1.5 m deck is walkable but can't be reached without a ramp");

        var path = new List<Vector3>();
        Vector3 from = new(0, -5, 0), to = new(0, 5, 0);
        Check(grid.FindPath(from, to, path, out bool complete) && complete && path.Count is > 0 and <= 4 &&
              Vector3.Distance(path[^1], to) < 0.01f,
            $"a path leads around the wall to the exact goal in {path.Count} straight segments");
        float length = Vector3.Distance(from, path[0]);
        for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
        bool segmentsClear = grid.LineWalkable(from, path[0]);
        for (int i = 1; i < path.Count; i++) segmentsClear &= grid.LineWalkable(path[i - 1], path[i]);
        Check(segmentsClear && path.Max(p => Math.Abs(p.X)) > 6.4f && length is > 16f and < 19f,
            $"its segments only cross walkable cells and it rounds the wall's end ({length:0.0} m, the shortest is about 16.4 m)");

        Check(grid.FindPath(new Vector3(0, -5, 0), new Vector3(0, -0.3f, 0), path, out complete) && complete &&
              grid.IsWalkable(path[^1]) && path[^1].Y < -0.5f,
            "a goal inside a wall's clearance leads to the nearest walkable cell");
        Check(grid.FindPath(new Vector3(0, -5, 0), new Vector3(10, 10, 1.5f), path, out complete) && !complete &&
              Vector2.Distance(new Vector2(path[^1].X, path[^1].Y), new Vector2(10, 10)) < 3.5f && path[^1].Z < 0.01f,
            "an unreachable goal (the deck) gives a path as close as possible, marked incomplete");
        Check(!grid.FindPath(new Vector3(60, 60, 0), Vector3.Zero, path, out _) && path.Count == 0,
            "no path from far outside the walkable area");

        // A ramp up the deck's west side: 13.6 degrees, its top surface from (2, 0) up to (8.2, 1.5).
        float run = 6.2f, rise = 1.5f, theta = MathF.Atan2(rise, run), half = MathF.Sqrt(run * run + rise * rise) / 2;
        BasicEntity ramp = world.Add("Ramp", new Vector3(5.1f + 0.15f * MathF.Sin(theta), 10, 0.75f - 0.15f * MathF.Cos(theta)),
            new Vector3(half, 1.5f, 0.15f), PhysicsBodyType.Static, recorder: false);
        ramp.RotationMatrix = Matrix.CreateRotationY(-theta);
        world.Step(1);
        grid = NavGrid.Build(world.Scene, world.Entities, new NavGridSettings(0.5f));
        Check(grid.TryGetHeight(new Vector3(5.1f, 10, 0), out float middle) && Math.Abs(middle - 0.75f) < 0.05f &&
              grid.IsWalkable(new Vector3(5.1f, 10, 0.75f)),
            $"the ramp's slope is walkable (height {middle:0.00} m half way up)");
        Check(grid.FindPath(new Vector3(0, -5, 0), new Vector3(10, 10, 1.5f), path, out complete) && complete &&
              Math.Abs(path[^1].Z - 1.5f) < 0.01f && grid.IsReachable(new Vector3(0, -5, 0), new Vector3(10, 10, 1.5f)),
            "with the ramp the deck is reachable and the path ends on top of it");

        var random = new Random(7);
        var center = new Vector3(-8, -8, 0);
        bool allGood = true;
        for (int i = 0; i < 30; i++)
            allGood &= grid.TryRandomPoint(center, 5, random, out Vector3 point) && grid.IsWalkable(point) &&
                       Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(center.X, center.Y)) < 5.3f && grid.IsReachable(center, point);
        Check(allGood, "random wander points are walkable, within the radius and reachable");
        Check(grid.TryFleePoint(new Vector3(0, -5, 0), new Vector3(0, -8, 0), 8, out Vector3 flee) && grid.IsWalkable(flee) &&
              Vector3.Distance(flee, new Vector3(0, -8, 0)) > 7f,
            $"a flee point leads away from the threat ({Vector3.Distance(flee, new Vector3(0, -8, 0)):0.0} m from it)");

        var wide = NavGrid.Build(world.Scene, world.Entities, new NavGridSettings(1.2f));
        Check(wide.WalkableCount < grid.WalkableCount && grid.IsWalkable(new Vector3(0, 1.1f, 0)) && !wide.IsWalkable(new Vector3(0, 1.1f, 0)) &&
              wide.IsWalkable(new Vector3(0, 1.6f, 0)),
            "a wider agent keeps further from walls");
    }

    private static void CheckGridCache()
    {
        Check(new NavGridSettings().Normalized() == new NavGridSettings(0.5f, 0.45f, 40f, NavGrid.DefaultCellSize).Normalized() &&
              default(NavGridSettings).Normalized() == new NavGridSettings().Normalized(),
            "new NavGridSettings() and default both mean the default agent");

        using var world = new CollisionChecks.World();
        var empty = new List<BasicEntity>();
        Check(Navigation.For(world.Scene, empty, new NavGridSettings()) == null, "a scene without static colliders has no grid");

        world.Add("Floor", new Vector3(0, 0, -0.5f), new Vector3(10, 10, 0.5f), PhysicsBodyType.Static, recorder: false);
        BasicEntity wall = world.Add("Wall", new Vector3(0, 0, 1), new Vector3(4, 0.25f, 1), PhysicsBodyType.Static, recorder: false);
        // Bodies attach on the first update: until then there is nothing to build from.
        Check(Navigation.For(world.Scene, world.Entities, new NavGridSettings()) == null, "no grid before the bodies exist");
        world.Step(1);
        NavGrid first = Navigation.For(world.Scene, world.Entities, new NavGridSettings());
        Check(first != null && ReferenceEquals(first, Navigation.For(world.Scene, world.Entities, new NavGridSettings(0.51f))),
            "the grid is built as soon as the colliders appear and shared by agents of (nearly) the same size");
        Check(!ReferenceEquals(first, Navigation.For(world.Scene, world.Entities, new NavGridSettings(1f))), "a different agent size gets its own grid");

        wall.Position = new Vector3(0, 5, 1);
        world.Step(1);
        Check(ReferenceEquals(first, Navigation.For(world.Scene, world.Entities, new NavGridSettings())),
            "moving a static doesn't rebuild every frame");
        world.Step(Navigation.RebuildFrames);
        NavGrid moved = Navigation.For(world.Scene, world.Entities, new NavGridSettings());
        Check(!ReferenceEquals(first, moved) && moved.IsWalkable(new Vector3(0, 1.5f, 0)) && !moved.IsWalkable(new Vector3(0, 5, 0)),
            $"a moved static rebuilds the grid within {Navigation.RebuildFrames} frames");
        world.Step(1);
        Check(ReferenceEquals(moved, Navigation.For(world.Scene, world.Entities, new NavGridSettings())), "an unchanged scene keeps its grid");
        Navigation.Invalidate();
        Check(!ReferenceEquals(moved, Navigation.For(world.Scene, world.Entities, new NavGridSettings())), "Invalidate forces a rebuild");
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  BEHAVIOUR IN PLAY
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckSight()
    {
        using var s = new Sandbox();
        BasicEntity sentry = s.Npc("Sentry", new Vector3(0, -4, 1), n =>
        {
            n.Idle = NpcIdleBehaviour.Stand;
            n.FieldOfView = 90;
            n.SightRange = 10;
            n.HearingRange = 2;
            n.ReactionTime = 100;
            // Barely turns towards what it notices, so each look below is from the same direction.
            n.TurnSpeed = 1;
        });
        BasicEntity player = s.Player(new Vector3(0, 4, 1));
        s.Begin();
        NpcComponent npc = sentry.GetComponent<NpcComponent>();
        bool SeesAt(Vector3 position)
        {
            player.Position = position;
            s.Step(1);
            return npc.CanSee(player);
        }

        Check(!SeesAt(new Vector3(0, 4, 1)), "a wall between them hides the player");
        Check(SeesAt(new Vector3(2, -1, 1)), "in the view cone with a clear line, the player is seen");
        Check(!SeesAt(new Vector3(8, -4, 1)), "beside it (90 degrees off its forward, outside a 90 degree cone) the player isn't seen");
        Check(SeesAt(new Vector3(0, -5.5f, 1)) && !SeesAt(new Vector3(0, -7, 1)), "right behind it within Hearing Range it notices the player, further back it doesn't");
        npc.FieldOfView = 360;
        Check(SeesAt(new Vector3(0, -12, 1)) && !SeesAt(new Vector3(0, -15.5f, 1)), "beyond Sight Range the player isn't seen");
        npc.FieldOfView = 90;
        SeesAt(new Vector3(2, -1, 1));
        s.Step(10);
        Check(npc.State == NpcState.Alerted && npc.CanSeeTarget && npc.CurrentTarget == player &&
              Vector3.Distance(npc.LastKnownTargetPosition, player.Position) < 0.05f &&
              NpcRecorder.Of(sentry).Events.SequenceEqual(new[] { "spotted Player" }),
            "seeing the player alerts it, remembers where, and runs OnTargetSpotted on its scripts");
    }

    private static void CheckPatrolChaseAndSearch()
    {
        using var s = new Sandbox();
        BasicEntity postA = s.Marker("Post A", new Vector3(-4, -6, 0));
        BasicEntity postB = s.Marker("Post B", new Vector3(4, -6, 0));
        BasicEntity guard = s.Npc("Guard", new Vector3(0, -8, 1), n =>
        {
            n.Idle = NpcIdleBehaviour.Patrol;
            n.PatrolPoints = "Post A, Post B";
            n.WaitTime = 0.2f;
            n.SightRange = 12;
            n.SearchTime = 1;
            n.MemoryTime = 1.5f;
        });
        BasicEntity player = s.Player(new Vector3(0, 8, 1));
        s.Begin();
        NpcComponent npc = guard.GetComponent<NpcComponent>();
        NpcRecorder events = NpcRecorder.Of(guard);

        float nearA = float.MaxValue, nearB = float.MaxValue;
        for (int i = 0; i < 60 * 10; i++)
        {
            s.Step(1);
            nearA = Math.Min(nearA, Flat(guard.Position, postA.Position));
            nearB = Math.Min(nearB, Flat(guard.Position, postB.Position));
        }
        Check(npc.State == NpcState.Patrolling && nearA < 0.4f && nearB < 0.4f && events.Events.Count == 0 &&
              Math.Abs(guard.Position.Z - 1) < 0.1f && Vector3.Distance(guard.RotationMatrix.Backward, Vector3.UnitZ) < 1e-3f,
            $"unseen behind the wall, the guard patrols between both posts (closest {nearA:0.00} m and {nearB:0.00} m), upright on the floor");

        // Right next to it: within Hearing Range it notices the player whichever way it faces.
        player.Position = guard.Position + new Vector3(0.3f, 2.2f, 0);
        player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
        Vector3 before = player.Position;
        var states = new List<NpcState> { npc.State };
        for (int i = 0; i < 60 * 3 && npc.Attacks == 0; i++)
        {
            s.Step(1);
            if (states.Count == 0 || states[^1] != npc.State) states.Add(npc.State);
        }
        Check(states.Take(4).SequenceEqual(new[] { NpcState.Patrolling, NpcState.Alerted, NpcState.Chasing, NpcState.Attacking }) &&
              events.Events.FirstOrDefault() == "spotted Player",
            $"seeing the player close by, it stops, reacts and closes in to attack ({string.Join(" > ", states)})");
        s.Step(30);
        Check(npc.Attacks >= 1 && events.Events.Contains("attack Player") && Flat(player.Position, before) > 0.5f,
            $"the attack runs OnNpcAttack and shoves the player away ({Flat(player.Position, before):0.0} m)");
        s.Step(120);
        Check(npc.Attacks >= 2 && npc.State is NpcState.Attacking or NpcState.Chasing, "it keeps chasing and attacking while the player stays in reach");

        // Gone behind the wall: it runs to where it last saw the player, searches, gives up and resumes its patrol.
        player.Position = new Vector3(0, 10, 1);
        player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
        states.Clear();
        for (int i = 0; i < 60 * 12; i++)
        {
            s.Step(1);
            if (states.Count == 0 || states[^1] != npc.State) states.Add(npc.State);
        }
        Check(states.Contains(NpcState.Searching) && npc.State == NpcState.Patrolling && events.Events.Contains("lost Player") &&
              events.States.Contains(NpcState.Searching),
            $"after losing the player it searches, runs OnTargetLost and goes back on patrol ({string.Join(" > ", states)})");

        s.Play.Stop();
        Check(npc.State == NpcState.Idle && !npc.HasPath && npc.CurrentTarget == null && Vector3.Distance(guard.Position, new Vector3(0, -8, 1)) < 1e-3f,
            "Stop resets the NPC and puts the guard back where it was");
    }

    private static void CheckPathAroundWall()
    {
        using var s = new Sandbox();
        BasicEntity hunter = s.Npc("Hunter", new Vector3(0, -4, 1), n =>
        {
            n.Idle = NpcIdleBehaviour.Stand;
            n.ReactionTime = 0.1f;
            n.MemoryTime = 20;
        });
        BasicEntity player = s.Player(new Vector3(0, 4, 1));
        s.Begin();
        NpcComponent npc = hunter.GetComponent<NpcComponent>();
        s.Step(5);
        Check(npc.State == NpcState.Idle && !npc.CanSeeTarget, "the player behind the wall goes unnoticed");

        npc.Alert(player);
        float widest = 0;
        bool inWall = false;
        for (int i = 0; i < 60 * 12 && npc.Attacks == 0; i++)
        {
            s.Step(1);
            if (npc.HasPath) widest = Math.Max(widest, npc.Path.Max(p => Math.Abs(p.X)));
            inWall |= Math.Abs(hunter.Position.X) < 6.3f && Math.Abs(hunter.Position.Y) < 0.6f;
        }
        Check(NpcRecorder.Of(hunter).Events.FirstOrDefault() == "spotted Player" && widest > 6.2f && !inWall && npc.Attacks >= 1,
            "alerted to a player behind the wall, it paths around the wall's end and reaches it");
    }

    private static void CheckFollowFleeWatch()
    {
        using var s = new Sandbox();
        BasicEntity villager = s.Npc("Villager", new Vector3(-8, -10, 1), n =>
        {
            n.Idle = NpcIdleBehaviour.Stand;
            n.Reaction = NpcReaction.Follow;
            n.FollowDistance = 2.5f;
        });
        BasicEntity critter = s.Npc("Critter", new Vector3(8, -6, 1), n =>
        {
            n.Idle = NpcIdleBehaviour.Stand;
            n.Reaction = NpcReaction.Flee;
            n.FleeDistance = 8;
            n.RunSpeed = 6;
            n.ReactionTime = 0.1f;
        }, facing: new Vector3(0, -1, 0));
        BasicEntity watcher = s.Npc("Watcher", new Vector3(2, -13, 1), n =>
        {
            n.Idle = NpcIdleBehaviour.Stand;
            n.Reaction = NpcReaction.Watch;
            n.FieldOfView = 160;
        });
        BasicEntity player = s.Player(new Vector3(-8, -3, 1));
        s.Begin();
        NpcComponent follow = villager.GetComponent<NpcComponent>(), flee = critter.GetComponent<NpcComponent>(), watch = watcher.GetComponent<NpcComponent>();
        Vector3 watcherStart = watcher.Position;

        s.Step(60 * 6);
        float followGap = Flat(villager.Position, player.Position);
        Vector3 toPlayer = Vector3.Normalize(new Vector3(player.Position.X - villager.Position.X, player.Position.Y - villager.Position.Y, 0));
        Check(follow.State == NpcState.Following && followGap is > 2.2f and < 3.2f && Vector3.Dot(follow.Facing, toPlayer) > 0.9f,
            $"Follow walks up to the player, stops {followGap:0.0} m away and faces it");
        Vector3 toPlayerFromWatcher = Vector3.Normalize(new Vector3(player.Position.X - watcher.Position.X, player.Position.Y - watcher.Position.Y, 0));
        Check(watch.State == NpcState.Watching && Vector3.Dot(watch.Facing, toPlayerFromWatcher) > 0.97f && Flat(watcher.Position, watcherStart) < 0.2f,
            "Watch stays put and turns to face the player");
        Check(flee.State == NpcState.Idle, "the critter facing away hasn't noticed the player");

        // Walk the player up behind the critter, into its hearing range.
        player.Position = critter.Position + new Vector3(0, 2, 0);
        player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
        bool fled = false;
        for (int i = 0; i < 60 * 3; i++)
        {
            s.Step(1);
            fled |= flee.State == NpcState.Fleeing;
        }
        Check(fled && Flat(critter.Position, player.Position) > 6f,
            $"Flee runs away from the player ({Flat(critter.Position, player.Position):0.0} m away after 3 s)");

        // The follower lost sight (the player moved away behind the wall): it searches, then goes home.
        player.Position = new Vector3(4, 8, 1);
        player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
        follow.MemoryTime = 0.5f;
        follow.SearchTime = 0.5f;
        bool searched = false;
        for (int i = 0; i < 60 * 15 && !(searched && follow.State == NpcState.Idle); i++)
        {
            s.Step(1);
            searched |= follow.State == NpcState.Searching;
        }
        Check(searched && follow.State == NpcState.Idle && Flat(villager.Position, follow.Home) < 1f,
            "a Stand NPC that lost its target searches, then walks back home");
    }

    private static void CheckBodylessNpc()
    {
        using var s = new Sandbox();
        BasicEntity drone = s.Npc("Drone", new Vector3(0, -5, 1), n => n.Idle = NpcIdleBehaviour.Stand, physics: false);
        s.Player(new Vector3(-14, 14, 1));
        s.Begin();
        NpcComponent npc = drone.GetComponent<NpcComponent>();
        npc.MoveTo(new Vector3(0, 5, 0));
        bool inWall = false;
        float lowest = float.MaxValue, highest = float.MinValue;
        for (int i = 0; i < 60 * 12 && npc.HasPath; i++)
        {
            s.Step(1);
            inWall |= Math.Abs(drone.Position.X) < 6.3f && Math.Abs(drone.Position.Y) < 0.6f;
            lowest = Math.Min(lowest, drone.Position.Z);
            highest = Math.Max(highest, drone.Position.Z);
        }
        Check(npc.State == NpcState.Scripted && !npc.HasPath && Flat(drone.Position, new Vector3(0, 5, 0)) < 0.3f && !inWall &&
              Math.Abs(lowest - 1) < 0.01f && Math.Abs(highest - 1) < 0.01f,
            "without a body MoveTo moves the gameobject itself around the wall, keeping its height above the ground");
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  EDITOR & SAVING
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckEditorAndSaving()
    {
        Check(ComponentRegistry.Find(NpcComponent.TypeId)?.ComponentType == typeof(NpcComponent) && ComponentEditorRegistry.Supports(NpcComponent.TypeId),
            "NPC is registered and offered in Add Component");

        var assets = new Assets { Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))) };
        var logic = new MainSceneLogic();
        var entity = new BasicEntity(assets.Capsule, null, new Vector3(0, 0, 1), Matrix.Identity, Vector3.One) { Name = "Guard", IsEnabled = true };
        logic.BasicEntities.Add(entity);
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();
        var owner = objects.Single();
        owner.AddableComponents.Single(c => c.DisplayName == "NPC").AddCommand.Execute(null);
        Publish();
        var npc = entity.GetComponent<NpcComponent>();
        var editor = owner.Components.OfType<NpcComponentViewModel>().Single();
        Check(npc is { Idle: NpcIdleBehaviour.Wander, Reaction: NpcReaction.Chase, Target: "Player", SightRange: 15, FieldOfView: 110 } &&
              npc.AgentRadius == 0.5f && editor.IsWander && editor.IsChase && !editor.IsPatrol &&
              !owner.AddableComponents.Single(c => c.DisplayName == "NPC").AddCommand.CanExecute(null),
            "NPC defaults to a wandering chaser watching for 'Player', sized from its collider, and is unique per gameobject");

        editor.Idle = NpcIdleBehaviour.Patrol;
        editor.PatrolPoints = " Post 1, Post 2 ";
        editor.Reaction = NpcReaction.Flee;
        editor.WalkSpeed = 3;
        editor.RunSpeed = 6;
        editor.SightRange = 20;
        editor.FieldOfView = 80;
        editor.HearingRange = 4;
        editor.EyeHeight = 0.9;
        editor.ReactionTime = 0.25;
        editor.MemoryTime = 6;
        editor.FleeDistance = 14;
        editor.Target = "Hero";
        editor.ShowDebug = true;
        Publish();
        Check(npc is { Idle: NpcIdleBehaviour.Patrol, PatrolPoints: "Post 1, Post 2", Reaction: NpcReaction.Flee, WalkSpeed: 3, RunSpeed: 6,
                  SightRange: 20, FieldOfView: 80, HearingRange: 4, ReactionTime: 0.25f, MemoryTime: 6, FleeDistance: 14, Target: "Hero", ShowDebug: true } &&
              Math.Abs(npc.EyeHeight - 0.9f) < 1e-5f && editor.IsPatrol && editor.IsFlee && !editor.SearchesAfterLosing,
            "inspector edits reach the engine component and switch the shown settings");
        editor.SightRange = double.NaN;
        editor.FieldOfView = 900;
        editor.WalkSpeed = -2;
        Publish();
        Check(npc is { SightRange: 20, FieldOfView: 360, WalkSpeed: 0 }, "invalid editor values are ignored or clamped");

        var record = ComponentRegistry.Capture(npc);
        Check(record.Data.GetProperty(nameof(NpcComponent.Idle)).GetString() == "Patrol" &&
              record.Data.GetProperty(nameof(NpcComponent.Reaction)).GetString() == "Flee" &&
              !record.Data.TryGetProperty(nameof(NpcComponent.State), out _) && !record.Data.TryGetProperty(nameof(NpcComponent.Path), out _) &&
              !record.Data.TryGetProperty(nameof(NpcComponent.CurrentTarget), out _) &&
              ComponentRegistry.Restore(record) is NpcComponent { Idle: NpcIdleBehaviour.Patrol, Reaction: NpcReaction.Flee, PatrolPoints: "Post 1, Post 2", FleeDistance: 14 },
            "saved NPC records round-trip their settings (behaviours by name) and exclude runtime state");
        var copied = ((BasicEntity)entity.Clone).GetComponent<NpcComponent>();
        copied.SightRange = 3;
        Check(!ReferenceEquals(copied, npc) && npc.SightRange == 20, "cloning copies NPC settings independently");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  SAMPLE SCENE
    ////////////////////////////////////////////////////////////////////////////////

    private static void CheckSampleScene()
    {
        var assets = new Assets
        {
            Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)),
            Capsule = new ModelDefinition(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1))),
        };
        string path = Path.Combine(new EditorBridge().ContentSourceRoot, "Scenes", "NpcTest.obsc");
        var scene = SceneSerialization.LoadFromFile(path, assets);
        BasicEntity Named(string name) => scene.BasicEntities.Single(e => e.Name == name);

        BasicEntity player = Named("Player"), guard = Named("Guard"), villager = Named("Villager"), critter = Named("Critter"), sentry = Named("Sentry");
        var npcs = scene.BasicEntities.Where(e => e.GetComponent<NpcComponent>() != null).ToList();
        Check(scene.BasicEntities.Count == 27 && npcs.Count == 4 &&
              player.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Dynamic, FreezeRotation: true } &&
              player.GetComponents<ScriptBehaviourComponent>().Select(c => c.ScriptId).SequenceEqual(new[] { NpcTestPlayerScript.ScriptId, PauseMenuScript.ScriptId }),
            "the NPC sample loads with a third-person player and four NPCs");
        Check(npcs.All(e => e.GetComponent<PhysicsComponent>() is { BodyType: PhysicsBodyType.Dynamic, FreezeRotation: true } &&
                            e.GetComponent<ScriptBehaviourComponent>()?.ScriptId == NpcReporterScript.ScriptId &&
                            e.GetComponent<NpcComponent>().Target == "Player"),
            "every NPC is an upright dynamic capsule watching for the Player, with a reporter script");
        NpcComponent guardNpc = guard.GetComponent<NpcComponent>();
        Check(guardNpc is { Idle: NpcIdleBehaviour.Patrol, Reaction: NpcReaction.Chase } &&
              guardNpc.PatrolPoints.Split(',').Select(p => p.Trim()).All(p => scene.BasicEntities.Any(e => e.Name == p && e.Physics == null)) &&
              villager.GetComponent<NpcComponent>() is { Idle: NpcIdleBehaviour.Wander, Reaction: NpcReaction.Follow } &&
              critter.GetComponent<NpcComponent>() is { Idle: NpcIdleBehaviour.Wander, Reaction: NpcReaction.Flee } &&
              sentry.GetComponent<NpcComponent>() is { Idle: NpcIdleBehaviour.Stand, Reaction: NpcReaction.Watch },
            "the guard patrols its four posts and chases, the villager follows, the critter flees and the sentry watches");
        Check(sentry.GetComponent<ScriptBehaviourComponent>().Fields[nameof(NpcReporterScript.AlarmTarget)].GetInt32() == guard.Id,
            "the sentry's reporter raises the alarm with the guard");
        SceneList.Load();
        Check(SceneList.Find("NpcTest") >= 0, "the NPC sample is in the scene list");
        string ui = GameInfo.ResolveContentFile("UI/NpcTest");
        Check(File.Exists(ui + ".xml") && File.Exists(ui + ".css"), "the NPC sample's HUD ships with the scene");

        // ---- Play ----
        KeyboardState keyboard = Input.keyboardState;
        MainSceneLogic previousLogic = GameFlow.SceneLogic;
        bool editor = GameSettings.e_enableeditor, physicsOn = GameSettings.p_physics, lines = GameSettings.d_drawlines;
        using var physicsSystem = new PhysicsSystem(new Vector3(0, 0, -9.81f));
        try
        {
            GameSettings.e_enableeditor = false;
            GameSettings.p_physics = true;
            GameSettings.d_drawlines = false;
            var logic = new MainSceneLogic();
            typeof(MainSceneLogic).GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(logic, assets);
            typeof(MainSceneLogic).GetField("_scenePhysics", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(logic, new ScenePhysics(physicsSystem) { FallbackGeometry = CollisionChecks.World.BoxGeometry });
            logic.SceneManager.Assets = assets;
            logic.PlayMode = new PlayModeController(logic);
            GameFlow.SceneLogic = logic;
            logic.SceneManager.SetActiveScene(scene);

            void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    GameInput.Update(Dt);
                    logic.PlayMode.UpdateScripts(Frame);
                    logic.UpdatePhysics(Dt);
                }
            }

            Vector3 guardStart = guard.Position;
            logic.PlayMode.Play();
            Frames(60 * 6);
            Check(Flat(guard.Position, guardStart) > 2f && guardNpc.State == NpcState.Patrolling,
                "on Play the guard sets off on its patrol");
            NpcComponent villagerNpc = villager.GetComponent<NpcComponent>();
            Check(villagerNpc.State == NpcState.Following && Flat(villager.Position, player.Position) < 4f &&
                  NpcTestFeed.Recent.Contains("Villager spotted Player") && NpcTestFeed.Recent.Contains("Villager follows you"),
                $"the villager spots the player at the start and walks up to it ({Flat(villager.Position, player.Position):0.0} m)");
            Check(villager.GetComponent<MaterialComponent>() is { Red: 0.45f, Green: 1f, Blue: 0.45f },
                "its reporter script tints it by state (green while following)");

            // Shouting (F) alerts the NPCs within reach, even those that can't see the player.
            Input.keyboardState = new KeyboardState(Keys.F);
            Frames(1);
            Input.keyboardState = new KeyboardState();
            Frames(1);
            Check(NpcTestFeed.Recent.Any(l => l.StartsWith("You shout: ")), "the player's shout reaches the NPCs nearby");

            // Into the sentry's view: it watches and calls the guard over.
            player.Position = new Vector3(-4, 6, 1);
            player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
            Frames(60);
            NpcComponent sentryNpc = sentry.GetComponent<NpcComponent>();
            Check(sentryNpc.State == NpcState.Watching && NpcTestFeed.Recent.Contains("Sentry raised the alarm: Guard is on the way") &&
                  guardNpc.State is NpcState.Searching or NpcState.Alerted or NpcState.Chasing,
                $"from the lookout the sentry watches the player and sends the guard ({guardNpc.State})");

            // In front of the guard: it chases and hits the player until it is knocked out.
            player.Position = guard.Position + guardNpc.Facing * 2f;
            player.PhysicsScene!.SetVelocity(player, Vector3.Zero, Vector3.Zero);
            bool knockedOut = false, hitPosted = false;
            for (int i = 0; i < 60 * 15 && !knockedOut; i++)
            {
                Frames(1);
                hitPosted |= NpcTestFeed.Recent.Contains("Guard hits Player!");
                knockedOut = NpcTestFeed.Recent.Any(l => l.StartsWith("Knocked out by Guard"));
            }
            Check(guardNpc.Attacks >= 3 && hitPosted && knockedOut &&
                  Flat(player.Position, new Vector3(-16, -17, 1)) < 1f,
                $"the guard chases and hits the player; after three hits the player wakes up at the start ({guardNpc.Attacks} attacks)");
            Check(guardNpc.State is not (NpcState.Chasing or NpcState.Attacking), $"a knock-out calms the guard down ({guardNpc.State})");

            logic.PlayMode.Stop();
            Check(npcs.All(e => e.GetComponent<NpcComponent>().State == NpcState.Idle) && Vector3.Distance(guard.Position, guardStart) < 1e-3f &&
                  guard.GetComponent<MaterialComponent>() is { Red: 0.55f, Green: 0.13f, Blue: 0.11f },
                "Stop puts every NPC back, with its own colour");
        }
        finally
        {
            Input.keyboardState = keyboard;
            GameInput.Update(Dt);
            GameFlow.SceneLogic = previousLogic;
            GameSettings.e_enableeditor = editor;
            GameSettings.p_physics = physicsOn;
            GameSettings.d_drawlines = lines;
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  FIXTURES
    ////////////////////////////////////////////////////////////////////////////////

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    /// <summary>
    /// A Play-mode scene of box colliders: a 30 x 30 m floor with a 12 m wall across the middle (x -6..6 at y 0),
    /// driven like the engine (scripts and components, then physics), with GameFlow pointing at it.
    /// </summary>
    private sealed class Sandbox : IDisposable
    {
        private static readonly ModelDefinition Cube = new(null, new BoundingBox(-Vector3.One, Vector3.One));
        private static readonly ModelDefinition Capsule = new(null, new BoundingBox(new Vector3(-0.5f, -0.5f, -1), new Vector3(0.5f, 0.5f, 1)));

        public readonly PhysicsSystem Physics = new(new Vector3(0, 0, -9.81f));
        public readonly MainSceneLogic Logic = new();
        public readonly PlayModeController Play;
        private readonly MainSceneLogic _previous = GameFlow.SceneLogic;
        private readonly bool _editor = GameSettings.e_enableeditor, _physicsOn = GameSettings.p_physics, _lines = GameSettings.d_drawlines;

        public Sandbox()
        {
            GameSettings.e_enableeditor = false;
            GameSettings.p_physics = true;
            GameSettings.d_drawlines = false;
            typeof(MainSceneLogic).GetField("_scenePhysics", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(Logic, new ScenePhysics(Physics) { FallbackGeometry = CollisionChecks.World.BoxGeometry });
            Logic.PlayMode = Play = new PlayModeController(Logic);
            GameFlow.SceneLogic = Logic;
            Box("Floor", new Vector3(0, 0, -0.5f), new Vector3(15, 15, 0.5f));
            Box("Wall", new Vector3(0, 0, 1), new Vector3(6, 0.25f, 1));
        }

        public BasicEntity Box(string name, Vector3 position, Vector3 scale)
        {
            var entity = new BasicEntity(Cube, null, position, Matrix.Identity, scale) { Name = name, IsEnabled = true };
            entity.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Static });
            Logic.BasicEntities.Add(entity);
            return entity;
        }

        /// <summary>A floor marker without a collider (a patrol point).</summary>
        public BasicEntity Marker(string name, Vector3 position)
        {
            var entity = new BasicEntity(Cube, null, position, Matrix.Identity, new Vector3(0.2f, 0.2f, 0.02f)) { Name = name, IsEnabled = true };
            Logic.BasicEntities.Add(entity);
            return entity;
        }

        public BasicEntity Player(Vector3 position)
        {
            var entity = new BasicEntity(Capsule, null, position, Matrix.Identity, Vector3.One) { Name = "Player", IsEnabled = true };
            entity.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = 80, FreezeRotation = true });
            Logic.BasicEntities.Add(entity);
            return entity;
        }

        /// <summary>A capsule NPC (Dynamic, Freeze Rotation, 70 kg unless <paramref name="physics"/> is false) with a recorder script.</summary>
        public BasicEntity Npc(string name, Vector3 position, Action<NpcComponent> configure, Vector3? facing = null, bool physics = true)
        {
            Vector3 forward = facing ?? Vector3.UnitY;
            Matrix rotation = Matrix.CreateRotationZ(MathF.Atan2(-forward.X, forward.Y));
            var entity = new BasicEntity(Capsule, null, position, rotation, Vector3.One) { Name = name, IsEnabled = true };
            if (physics) entity.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = 70, FreezeRotation = true });
            var npc = new NpcComponent();
            configure(npc);
            entity.AddComponent(npc);
            entity.AddComponent(new ScriptBehaviourComponent { ScriptId = NpcRecorder.Id });
            Logic.BasicEntities.Add(entity);
            return entity;
        }

        /// <summary>Attaches the bodies, then starts Play.</summary>
        public void Begin()
        {
            Logic.UpdatePhysics(0);
            Play.Play();
        }

        public void Step(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Play.UpdateScripts(Frame);
                Logic.UpdatePhysics(Dt);
            }
        }

        public void Dispose()
        {
            Play.Stop();
            GameFlow.SceneLogic = _previous;
            GameSettings.e_enableeditor = _editor;
            GameSettings.p_physics = _physicsOn;
            GameSettings.d_drawlines = _lines;
            Physics.Dispose();
        }
    }

    public sealed class NpcRecorder : ScriptBehaviour
    {
        public const string Id = "test-npc-recorder";
        private static readonly List<NpcRecorder> Instances = new();
        public readonly List<string> Events = new();
        public readonly List<NpcState> States = new();

        public NpcRecorder() => Instances.Add(this);

        public static NpcRecorder Of(BasicEntity entity) => Instances.Last(s => s.GameObject == entity);

        public override void OnTargetSpotted(BasicEntity target) => Events.Add("spotted " + target?.Name);
        public override void OnTargetLost(BasicEntity target) => Events.Add("lost " + target?.Name);
        public override void OnNpcAttack(BasicEntity target) => Events.Add("attack " + target?.Name);
        public override void OnNpcStateChanged(NpcState previous, NpcState current) => States.Add(current);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
