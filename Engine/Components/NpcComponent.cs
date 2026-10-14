using System.Text.Json.Serialization;
using Engine.AI;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Renderer.Helper.HelperGeometry;
using Microsoft.Xna.Framework;

namespace Engine.Components;

/// <summary>What an NPC does while it hasn't noticed its target. Saved by name: never rename a value.</summary>
public enum NpcIdleBehaviour
{
    /// <summary>Stays at its start position, facing its start direction.</summary>
    Stand,
    /// <summary>Walks to random reachable points within Wander Radius of its start position.</summary>
    Wander,
    /// <summary>Walks from one Patrol Point gameobject to the next, in a loop.</summary>
    Patrol,
}

/// <summary>What an NPC does once it has noticed its target. Saved by name: never rename a value.</summary>
public enum NpcReaction
{
    /// <summary>Runs after the target and attacks it (a shove) within Attack Range, then searches where it was last seen.</summary>
    Chase,
    /// <summary>Walks up to the target and stays Follow Distance away, facing it.</summary>
    Follow,
    /// <summary>Runs away from the target until it is out of sight, then goes back to its idle behaviour.</summary>
    Flee,
    /// <summary>Stands still and turns to keep facing the target.</summary>
    Watch,
}

/// <summary>An NPC's current activity (<see cref="NpcComponent.State"/>).</summary>
public enum NpcState
{
    Idle,
    Wandering,
    Patrolling,
    /// <summary>Just noticed the target: stops and turns to it for Reaction Time before reacting.</summary>
    Alerted,
    Chasing,
    Attacking,
    Following,
    Fleeing,
    Watching,
    /// <summary>Lost the target (or was sent by <see cref="NpcComponent.Investigate"/>): walks to where it was and looks around.</summary>
    Searching,
    /// <summary>Walking back to its start position.</summary>
    Returning,
    /// <summary>Walking where a script sent it (<see cref="NpcComponent.MoveTo"/>).</summary>
    Scripted,
}

/// <summary>
/// A computer-controlled character. In Play it walks the scene's navigation grid (<see cref="NavGrid"/>: A* over
/// the static colliders), idles (stand, wander or patrol), watches for its target (the gameobject named
/// <see cref="Target"/>, normally "Player") with a view cone and line-of-sight rays, and reacts when it sees it
/// (<see cref="NpcReaction"/>). Best on a Dynamic capsule with Freeze Rotation, which it moves by velocity so
/// physics keeps it on the ground and out of walls; without a Dynamic body it moves the gameobject directly,
/// following the grid's ground height. Its scripts get OnTargetSpotted, OnTargetLost, OnNpcStateChanged and
/// OnNpcAttack. Turning uses the gameobject's +Y as its forward.
/// </summary>
public sealed class NpcComponent : GameComponent
{
    public const string TypeId = "npc";
    /// <summary>Seconds between two looks for the target (line-of-sight rays).</summary>
    public const float PerceptionInterval = 0.1f;
    /// <summary>Volumetric smoke hiding more than this share of the view blocks sight (<see cref="ParticleSystemComponent.VolumeOpacity"/>).</summary>
    public const float SmokeBlocksSight = 0.9f;

    // ---- Movement ----
    /// <summary>Speed while idling, following, searching and returning (m/s).</summary>
    public float WalkSpeed { get; set; } = 2.5f;
    /// <summary>Speed while chasing and fleeing (m/s).</summary>
    public float RunSpeed { get; set; } = 5.5f;
    /// <summary>How fast it turns, in degrees per second.</summary>
    public float TurnSpeed { get; set; } = 360f;
    /// <summary>Largest change of speed per second (m/s²), so it can't stop or turn on the spot.</summary>
    public float Acceleration { get; set; } = 20f;
    /// <summary>Clearance it keeps from walls for paths (m); 0 = the collider's horizontal half size.</summary>
    public float Radius { get; set; }

    // ---- Idle ----
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NpcIdleBehaviour Idle { get; set; } = NpcIdleBehaviour.Wander;
    /// <summary>Wander: how far from its start position it roams (m).</summary>
    public float WanderRadius { get; set; } = 8f;
    /// <summary>Seconds it waits at each wander or patrol point (varies by up to half either way).</summary>
    public float WaitTime { get; set; } = 2f;
    /// <summary>Patrol: names of the gameobjects to walk between, separated by commas, in order.</summary>
    public string PatrolPoints { get; set; } = "";

    // ---- Perception ----
    /// <summary>Name of the gameobject it watches for.</summary>
    public string Target { get; set; } = "Player";
    /// <summary>Farthest distance it sees (m).</summary>
    public float SightRange { get; set; } = 15f;
    /// <summary>Width of its view cone in degrees, centred on its forward (+Y).</summary>
    public float FieldOfView { get; set; } = 110f;
    /// <summary>Within this distance it notices the target in any direction (m), still not through walls.</summary>
    public float HearingRange { get; set; } = 2.5f;
    /// <summary>Height of its eyes above the gameobject's origin (m); line-of-sight rays start there.</summary>
    public float EyeHeight { get; set; } = 0.7f;
    /// <summary>Seconds between noticing the target and reacting.</summary>
    public float ReactionTime { get; set; } = 0.5f;
    /// <summary>Seconds it keeps going for the target after losing sight of it before it gives up.</summary>
    public float MemoryTime { get; set; } = 4f;

    // ---- Reaction ----
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NpcReaction Reaction { get; set; } = NpcReaction.Chase;
    /// <summary>Chase: distance between the two origins from which it attacks (m).</summary>
    public float AttackRange { get; set; } = 1.6f;
    /// <summary>Chase: seconds between attacks.</summary>
    public float AttackCooldown { get; set; } = 1.2f;
    /// <summary>Chase: impulse an attack gives the target's Dynamic body (N·s), away and slightly up.</summary>
    public float AttackForce { get; set; } = 350f;
    /// <summary>Follow: how close it comes (m).</summary>
    public float FollowDistance { get; set; } = 2.5f;
    /// <summary>Flee: how far each escape run goes (m).</summary>
    public float FleeDistance { get; set; } = 10f;
    /// <summary>Seconds it looks around where it lost the target before going back to idling.</summary>
    public float SearchTime { get; set; } = 4f;

    /// <summary>Draws its path, view cone and sight line in Play (debug lines must be on).</summary>
    public bool ShowDebug { get; set; }

    // ---- Clamped values ----
    [JsonIgnore] public float ClampedWalkSpeed => Clamp(WalkSpeed, 0f, 50f, 2.5f);
    [JsonIgnore] public float ClampedRunSpeed => Clamp(RunSpeed, 0f, 50f, 5.5f);
    [JsonIgnore] public float ClampedSightRange => Clamp(SightRange, 0f, 500f, 15f);
    [JsonIgnore] public float ClampedFieldOfView => Clamp(FieldOfView, 0f, 360f, 110f);
    [JsonIgnore] public float ClampedHearingRange => Clamp(HearingRange, 0f, 500f, 2.5f);
    [JsonIgnore] public float ClampedAttackRange => Clamp(AttackRange, 0f, 50f, 1.6f);

    // ---- Runtime state (never saved) ----
    [JsonIgnore] public NpcState State { get; private set; } = NpcState.Idle;
    /// <summary>Seconds since <see cref="State"/> last changed.</summary>
    [JsonIgnore] public float StateTime { get; private set; }
    /// <summary>The gameobject it watches: <see cref="TargetObject"/> when a script set one, else the one named <see cref="Target"/>.</summary>
    [JsonIgnore] public BasicEntity CurrentTarget { get; private set; }
    /// <summary>Set by a script to watch a gameobject directly instead of looking it up by <see cref="Target"/>.</summary>
    [JsonIgnore] public BasicEntity TargetObject { get; set; }
    /// <summary>True while the target is in view (as of the last look, every <see cref="PerceptionInterval"/>).</summary>
    [JsonIgnore] public bool CanSeeTarget { get; private set; }
    /// <summary>Where it last saw (or was told about) the target.</summary>
    [JsonIgnore] public Vector3 LastKnownTargetPosition { get; private set; }
    /// <summary>Where it was when Play started: the centre of its wandering and where Stand returns to.</summary>
    [JsonIgnore] public Vector3 Home { get; private set; }
    /// <summary>The corners of the path it is walking (ground points), empty when it stands.</summary>
    [JsonIgnore] public IReadOnlyList<Vector3> Path => _path;
    /// <summary>Where the current path leads (the requested goal); only meaningful while <see cref="HasPath"/>.</summary>
    [JsonIgnore] public Vector3 Destination { get; private set; }
    [JsonIgnore] public bool HasPath => _pathIndex < _path.Count;
    /// <summary>False when the destination can't be reached (walled off, up a ledge) and the path only leads as close as it can.</summary>
    [JsonIgnore] public bool PathComplete => _pathComplete;
    /// <summary>Times it attacked during this Play session.</summary>
    [JsonIgnore] public int Attacks { get; private set; }

    /// <summary>Raised on the game thread after its scripts ran the matching hook. Runtime only, never saved.</summary>
    public event Action<NpcState, NpcState> StateChanged;
    public event Action<BasicEntity> TargetSpotted;
    public event Action<BasicEntity> TargetLost;
    public event Action<BasicEntity> Attacked;

    private BasicEntity _owner;
    private bool _running;
    private Random _random;
    private float _clock, _lookTimer, _lastSeen, _attackTimer, _waitTimer;
    private bool _waiting;
    private Matrix _baseRotation;
    private float _baseYaw, _yaw, _groundOffset;
    private Vector3? _faceTarget;
    private Vector3 _searchPoint;
    private int _patrolIndex;
    private List<BasicEntity> _patrolPoints;
    private string _patrolPointsResolved;

    private readonly List<Vector3> _path = new();
    private int _pathIndex;
    private bool _pathComplete;
    private float _speed, _repathTimer;
    private Vector3 _pathGoal;
    // Stuck detection: progress over the last second while it means to move.
    private float _stuckTimer;
    private Vector3 _stuckFrom;
    private int _stuckCount;

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    ////////////////////////////////////////////////////////////////////////////////
    //  LIFECYCLE
    ////////////////////////////////////////////////////////////////////////////////

    public override void OnStart(BasicEntity owner)
    {
        _owner = owner;
        if (owner == null) return;
        _running = true;
        _random = new Random(InstanceId.GetHashCode());
        _clock = _lookTimer = _attackTimer = _waitTimer = 0f;
        _lastSeen = float.NegativeInfinity;
        _waiting = false;
        Home = owner.Position;
        _baseRotation = owner.RotationMatrix;
        _baseYaw = _yaw = YawOf(owner.RotationMatrix.Up, 0f);
        _groundOffset = float.NaN;
        _faceTarget = null;
        _patrolIndex = 0;
        _patrolPoints = null;
        _patrolPointsResolved = null;
        CurrentTarget = null;
        CanSeeTarget = false;
        LastKnownTargetPosition = owner.Position;
        Attacks = 0;
        ClearPath();
        State = NpcState.Idle;
        StateTime = 0f;
        BeginIdle();
    }

    public override void OnStop()
    {
        if (!_running) return;
        _running = false;
        if (_owner?.PhysicsScene is { } physics && physics.TryGetVelocity(_owner, out Vector3 velocity, out Vector3 spin))
            physics.SetVelocity(_owner, new Vector3(0f, 0f, velocity.Z), spin);
        ClearPath();
        CanSeeTarget = false;
        CurrentTarget = null;
        State = NpcState.Idle;
        StateTime = 0f;
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (!_running || owner != _owner) OnStart(owner);
        float dt = (float)time.ElapsedGameTime.TotalSeconds;
        if (!(dt > 0f)) return;
        _clock += dt;
        StateTime += dt;

        _lookTimer -= dt;
        if (_lookTimer <= 0f)
        {
            _lookTimer = PerceptionInterval;
            Look();
        }
        Think(dt);
        Move(dt);
        if (ShowDebug && GameSettings.d_drawlines) DrawDebug();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  SCRIPT API
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>Walks to a point (state Scripted), then stands there. Seeing the target still interrupts it.</summary>
    public void MoveTo(Vector3 position)
    {
        if (!_running) return;
        SetState(NpcState.Scripted);
        SetDestination(position, ClampedWalkSpeed);
    }

    /// <summary>Goes to look around a point (state Searching), e.g. where another NPC raised the alarm.</summary>
    public void Investigate(Vector3 position)
    {
        if (!_running || IsAware) return;
        _searchPoint = position;
        SetState(NpcState.Searching);
        SetDestination(position, ClampedRunSpeed);
    }

    /// <summary>Notices <paramref name="target"/> now, wherever it is, as if it had just seen it.</summary>
    public void Alert(BasicEntity target)
    {
        if (!_running || target == null) return;
        TargetObject = target;
        CurrentTarget = target;
        LastKnownTargetPosition = target.Position;
        _lastSeen = _clock;
        if (!IsAware) Spot();
    }

    /// <summary>Forgets the target and goes back to its idle behaviour.</summary>
    public void Calm()
    {
        if (!_running) return;
        _lastSeen = float.NegativeInfinity;
        CanSeeTarget = false;
        BeginIdle();
    }

    /// <summary>True while it reacts to the target (from Alerted until it gives up).</summary>
    [JsonIgnore]
    public bool IsAware => State is NpcState.Alerted or NpcState.Chasing or NpcState.Attacking or NpcState.Following
        or NpcState.Fleeing or NpcState.Watching;

    /// <summary>
    /// True when <paramref name="target"/> is in view from the eyes: within Sight Range and the view cone
    /// (or Hearing Range), with nothing solid between them. Triggers are see-through.
    /// </summary>
    public bool CanSee(BasicEntity target)
    {
        if (_owner == null || target == null || !target.IsEnabled || target == _owner) return false;
        Vector3 eye = Eye;
        Vector3 center = Center(target);
        float range = ClampedSightRange;
        if (Vector3.Distance(eye, center) > range + 1f) return false;

        Vector2 flat = new(center.X - eye.X, center.Y - eye.Y);
        bool heard = flat.Length() <= ClampedHearingRange;
        if (!heard && flat.LengthSquared() > 1e-6f)
        {
            Vector3 forward = Facing;
            float cos = Vector2.Dot(Vector2.Normalize(flat), Vector2.Normalize(new Vector2(forward.X, forward.Y)));
            if (cos < MathF.Cos(MathHelper.ToRadians(ClampedFieldOfView * 0.5f)) - 1e-4f) return false;
        }
        // Its middle or its top: either one visible counts.
        float top = Math.Max(0.1f, (Bounds(target).Max.Z - center.Z) * 0.7f);
        Vector3 head = center + new Vector3(0f, 0f, top);
        // Thick volumetric smoke hides the target from the eyes (not from the ears).
        if (!heard && ParticleSystemComponent.VolumeOpacity(eye, center) > SmokeBlocksSight &&
            ParticleSystemComponent.VolumeOpacity(eye, head) > SmokeBlocksSight) return false;
        return InSight(eye, center, target, range) || InSight(eye, head, target, range);
    }

    private bool InSight(Vector3 eye, Vector3 point, BasicEntity target, float range)
    {
        Vector3 toPoint = point - eye;
        float distance = toPoint.Length();
        if (distance > range) return false;
        if (distance < 1e-3f) return true;
        ScenePhysics physics = Physics;
        if (physics == null || !physics.Raycast(eye, toPoint, distance, out RaycastHit hit, _owner)) return true;
        // A target without a collider is seen when nothing stands in front of the point.
        return hit.GameObject == target || hit.Distance >= distance - 0.05f;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  PERCEPTION
    ////////////////////////////////////////////////////////////////////////////////

    private void Look()
    {
        BasicEntity target = ResolveTarget();
        if (target != CurrentTarget)
        {
            CurrentTarget = target;
            CanSeeTarget = false;
        }
        CanSeeTarget = target != null && CanSee(target);
        if (!CanSeeTarget) return;
        LastKnownTargetPosition = target.Position;
        _lastSeen = _clock;
    }

    private BasicEntity ResolveTarget()
    {
        if (TargetObject != null)
            return GameFlow.SceneLogic?.BasicEntities.Contains(TargetObject) != false ? TargetObject : null;
        if (string.IsNullOrWhiteSpace(Target)) return null;
        string name = Target.Trim();
        if (CurrentTarget != null && CurrentTarget.Name == name && GameFlow.SceneLogic?.BasicEntities.Contains(CurrentTarget) != false)
            return CurrentTarget;
        return GameFlow.SceneLogic?.BasicEntities.FirstOrDefault(e => e != _owner && e.Name == name);
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  DECISIONS
    ////////////////////////////////////////////////////////////////////////////////

    private float SinceSeen => _clock - _lastSeen;

    private void Think(float dt)
    {
        _faceTarget = null;
        BasicEntity target = CurrentTarget;
        float distance = target != null ? FlatDistance(_owner.Position, target.Position) : float.MaxValue;

        if (!IsAware && CanSeeTarget)
        {
            Spot();
            return;
        }

        switch (State)
        {
            case NpcState.Idle:
                if (FlatDistance(_owner.Position, Home) > 1f) BeginIdle();
                else _faceTarget = _owner.Position + YawDirection(_baseYaw);
                break;

            case NpcState.Wandering:
                if (!HasPath && Wait(dt))
                {
                    Vector3 point = Home;
                    NavGrid grid = Grid;
                    if (grid == null || !grid.TryRandomPoint(Home, Math.Max(0.5f, Clamp(WanderRadius, 0f, 500f, 8f)), _random, out point))
                        point = Home + new Vector3(((float)_random.NextDouble() - 0.5f) * 4f, ((float)_random.NextDouble() - 0.5f) * 4f, 0f);
                    SetDestination(point, ClampedWalkSpeed);
                }
                break;

            case NpcState.Patrolling:
            {
                List<BasicEntity> points = ResolvePatrolPoints();
                if (points.Count == 0)
                {
                    SetState(NpcState.Idle);
                    break;
                }
                if (!HasPath && Wait(dt))
                {
                    _patrolIndex = (_patrolIndex + 1) % points.Count;
                    SetDestination(points[_patrolIndex].Position, ClampedWalkSpeed);
                }
                break;
            }

            case NpcState.Returning:
                if (!HasPath) SetState(NpcState.Idle);
                break;

            case NpcState.Scripted:
                break;

            case NpcState.Alerted:
                StopMoving();
                _faceTarget = LastKnownTargetPosition;
                if (StateTime >= Clamp(ReactionTime, 0f, 10f, 0.5f)) React();
                break;

            case NpcState.Chasing:
                if (target == null || (!CanSeeTarget && SinceSeen > Clamp(MemoryTime, 0f, 60f, 4f)))
                {
                    Lose();
                    break;
                }
                if (CanSeeTarget && distance <= ClampedAttackRange)
                {
                    SetState(NpcState.Attacking);
                    // A short wind-up before the first blow.
                    _attackTimer = Math.Min(0.35f, Clamp(AttackCooldown, 0.05f, 30f, 1.2f));
                    StopMoving();
                    break;
                }
                // Reached where it last saw the target, and it isn't there.
                if (!CanSeeTarget && !HasPath && StateTime > 0.25f)
                {
                    Lose();
                    break;
                }
                Pursue(CanSeeTarget ? target.Position : LastKnownTargetPosition, ClampedRunSpeed, dt);
                break;

            case NpcState.Attacking:
                StopMoving();
                _faceTarget = target?.Position;
                if (target == null || (!CanSeeTarget && SinceSeen > 0.5f) || distance > ClampedAttackRange * 1.2f + 0.2f)
                {
                    SetState(NpcState.Chasing);
                    if (target != null) Pursue(LastKnownTargetPosition, ClampedRunSpeed, dt, force: true);
                    break;
                }
                _attackTimer -= dt;
                if (_attackTimer <= 0f)
                {
                    _attackTimer = Clamp(AttackCooldown, 0.05f, 30f, 1.2f);
                    Attack(target);
                }
                break;

            case NpcState.Following:
            {
                if (target == null || (!CanSeeTarget && SinceSeen > Clamp(MemoryTime, 0f, 60f, 4f)))
                {
                    Lose();
                    break;
                }
                float follow = Clamp(FollowDistance, 0.5f, 100f, 2.5f);
                Vector3 goal = CanSeeTarget ? target.Position : LastKnownTargetPosition;
                if (FlatDistance(_owner.Position, goal) <= follow)
                {
                    StopMoving();
                    _faceTarget = goal;
                }
                else if (HasPath || FlatDistance(_owner.Position, goal) > follow + 0.5f)
                {
                    // Runs to catch up when far behind.
                    float speed = FlatDistance(_owner.Position, goal) > follow * 3f ? ClampedRunSpeed : ClampedWalkSpeed;
                    Pursue(goal, speed, dt);
                }
                else _faceTarget = goal;
                break;
            }

            case NpcState.Fleeing:
            {
                float flee = Clamp(FleeDistance, 1f, 200f, 10f);
                if (target == null || (!CanSeeTarget && SinceSeen > Clamp(MemoryTime, 0f, 60f, 4f)))
                {
                    Lose();
                    break;
                }
                _repathTimer -= dt;
                // A new escape run when this one ended, or now and then while the target is close.
                if (!HasPath || (_repathTimer <= 0f && distance < flee * 0.6f))
                {
                    _repathTimer = 1f;
                    NavGrid grid = Grid;
                    Vector3 threat = CanSeeTarget ? target.Position : LastKnownTargetPosition;
                    if (grid != null && grid.TryFleePoint(_owner.Position, threat, flee, out Vector3 point))
                        SetDestination(point, ClampedRunSpeed);
                    else
                    {
                        Vector3 away = _owner.Position - threat;
                        away.Z = 0f;
                        if (away.LengthSquared() < 1e-6f) away = Vector3.UnitX;
                        SetDestination(_owner.Position + Vector3.Normalize(away) * flee, ClampedRunSpeed);
                    }
                }
                break;
            }

            case NpcState.Watching:
                StopMoving();
                if (target == null || (!CanSeeTarget && SinceSeen > Clamp(MemoryTime, 0f, 60f, 4f)))
                {
                    Lose();
                    break;
                }
                _faceTarget = CanSeeTarget ? target.Position : LastKnownTargetPosition;
                break;

            case NpcState.Searching:
                if (HasPath) break;
                // Looks left and right where it lost the trail.
                float search = Clamp(SearchTime, 0f, 60f, 4f);
                if (_waitTimer <= 0f) _searchYaw = _yaw;
                _waitTimer += dt;
                float sweep = MathF.Sin(_waitTimer * 2.2f) * MathHelper.PiOver2;
                _faceTarget = _owner.Position + YawDirection(_searchYaw + sweep);
                if (_waitTimer >= search) BeginIdle();
                break;
        }
    }

    private float _searchYaw;

    /// <summary>Counts down the wait at a wander or patrol point; true when it's time to walk on.</summary>
    private bool Wait(float dt)
    {
        if (!_waiting)
        {
            _waiting = true;
            float wait = Clamp(WaitTime, 0f, 120f, 2f);
            _waitTimer = wait * (0.5f + (float)_random.NextDouble());
        }
        _waitTimer -= dt;
        if (_waitTimer > 0f) return false;
        _waiting = false;
        return true;
    }

    private void Spot()
    {
        SetState(NpcState.Alerted);
        StopMoving();
        BasicEntity target = CurrentTarget;
        Notify("OnTargetSpotted", s => s.OnTargetSpotted(target));
        TargetSpotted?.Invoke(target);
    }

    private void React()
    {
        switch (Reaction)
        {
            case NpcReaction.Follow: SetState(NpcState.Following); break;
            case NpcReaction.Flee: SetState(NpcState.Fleeing); _repathTimer = 0f; break;
            case NpcReaction.Watch: SetState(NpcState.Watching); break;
            default: SetState(NpcState.Chasing); break;
        }
    }

    /// <summary>Gives up on the target: chasers and followers search where it was last seen, the rest go back to idling.</summary>
    private void Lose()
    {
        BasicEntity target = CurrentTarget;
        CanSeeTarget = false;
        if (Reaction is NpcReaction.Chase or NpcReaction.Follow && target != null)
        {
            _searchPoint = LastKnownTargetPosition;
            SetState(NpcState.Searching);
            SetDestination(_searchPoint, ClampedWalkSpeed);
        }
        else BeginIdle();
        Notify("OnTargetLost", s => s.OnTargetLost(target));
        TargetLost?.Invoke(target);
    }

    /// <summary>Back to the idle behaviour: Stand walks home first, Patrol resumes at the nearest point.</summary>
    private void BeginIdle()
    {
        _waiting = false;
        switch (Idle)
        {
            case NpcIdleBehaviour.Wander:
                SetState(NpcState.Wandering);
                ClearPath();
                break;
            case NpcIdleBehaviour.Patrol:
            {
                SetState(NpcState.Patrolling);
                List<BasicEntity> points = ResolvePatrolPoints();
                if (points.Count == 0) break;
                // Resume at the closest point (also the first one at the start).
                int nearest = 0;
                for (int i = 1; i < points.Count; i++)
                    if (FlatDistance(_owner.Position, points[i].Position) < FlatDistance(_owner.Position, points[nearest].Position))
                        nearest = i;
                _patrolIndex = nearest;
                SetDestination(points[nearest].Position, ClampedWalkSpeed);
                break;
            }
            default:
                if (FlatDistance(_owner.Position, Home) > 0.5f)
                {
                    SetState(NpcState.Returning);
                    SetDestination(Home, ClampedWalkSpeed);
                }
                else
                {
                    SetState(NpcState.Idle);
                    ClearPath();
                }
                break;
        }
    }

    private void Attack(BasicEntity target)
    {
        Attacks++;
        Vector3 push = target.Position - _owner.Position;
        push.Z = 0f;
        push = push.LengthSquared() > 1e-6f ? Vector3.Normalize(push) : Facing;
        float force = Clamp(AttackForce, 0f, 100000f, 350f);
        target.PhysicsScene?.ApplyImpulse(target, (push + new Vector3(0f, 0f, 0.35f)) * force);
        Notify("OnNpcAttack", s => s.OnNpcAttack(target));
        Attacked?.Invoke(target);
    }

    private void SetState(NpcState state)
    {
        if (state == State) return;
        NpcState previous = State;
        State = state;
        StateTime = 0f;
        _waitTimer = 0f;
        Notify("OnNpcStateChanged", s => s.OnNpcStateChanged(previous, state));
        StateChanged?.Invoke(previous, state);
    }

    private void Notify(string hook, Action<Scripting.ScriptBehaviour> call)
    {
        if (_owner == null) return;
        foreach (var script in _owner.GetComponents<ScriptBehaviourComponent>().ToArray()) script.Notify(hook, call);
    }

    private List<BasicEntity> ResolvePatrolPoints()
    {
        string names = PatrolPoints ?? "";
        var entities = GameFlow.SceneLogic?.BasicEntities;
        if (_patrolPoints != null && _patrolPointsResolved == names && _patrolPoints.All(p => entities?.Contains(p) != false))
            return _patrolPoints;
        _patrolPointsResolved = names;
        _patrolPoints = new List<BasicEntity>();
        foreach (string part in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            BasicEntity point = entities?.FirstOrDefault(e => e.Name == part);
            if (point != null) _patrolPoints.Add(point);
            else EditorBridge.Log($"[{_owner?.Name}] NPC: no patrol point named '{part}'");
        }
        return _patrolPoints;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  PATHS & MOVEMENT
    ////////////////////////////////////////////////////////////////////////////////

    private ScenePhysics Physics => _owner?.PhysicsScene ?? ScenePhysics.Current;

    /// <summary>The navigation grid for this NPC's size, or null without static colliders.</summary>
    [JsonIgnore]
    public NavGrid Grid
    {
        get
        {
            var entities = GameFlow.SceneLogic?.BasicEntities;
            return _owner == null || entities == null ? null
                : Navigation.For(Physics, entities, new NavGridSettings(AgentRadius), _owner.Position);
        }
    }

    /// <summary>Path clearance: <see cref="Radius"/>, or the collider's horizontal half size.</summary>
    [JsonIgnore]
    public float AgentRadius
    {
        get
        {
            if (float.IsFinite(Radius) && Radius > 0f) return Math.Min(Radius, 3f);
            if (_owner == null) return 0.5f;
            // The collider's own X/Y half size (it turns about Z, so its rotation doesn't matter).
            Vector3 size = (_owner.BoundingBox.Max - _owner.BoundingBox.Min) * _owner.Scale
                           * (_owner.Physics?.ColliderStretchFor(_owner) ?? Vector3.One);
            float radius = Math.Max(Math.Abs(size.X), Math.Abs(size.Y)) * 0.5f;
            return float.IsFinite(radius) && radius > 0.05f ? Math.Min(radius, 3f) : 0.5f;
        }
    }

    /// <summary>Plans a path; keeps walking towards the goal in a straight line when there is no grid.</summary>
    private void SetDestination(Vector3 goal, float speed)
    {
        _speed = speed;
        _pathGoal = goal;
        Destination = goal;
        _pathIndex = 0;
        _path.Clear();
        _stuckTimer = 0f;
        _stuckFrom = _owner.Position;
        NavGrid grid = Grid;
        if (grid == null)
        {
            _path.Add(goal);
            _pathComplete = true;
            return;
        }
        if (!grid.FindPath(_owner.Position, goal, _path, out _pathComplete)) _path.Clear();
    }

    /// <summary>Paths towards a moving goal, planning again when it moved away from the path's end.</summary>
    private void Pursue(Vector3 goal, float speed, float dt, bool force = false)
    {
        _speed = speed;
        _repathTimer -= dt;
        bool moved = Vector3.DistanceSquared(new Vector3(goal.X, goal.Y, 0f), new Vector3(_pathGoal.X, _pathGoal.Y, 0f)) > 0.75f * 0.75f;
        if (force || !HasPath || (moved && _repathTimer <= 0f))
        {
            _repathTimer = 0.4f;
            SetDestination(goal, speed);
        }
    }

    private void StopMoving() => ClearPath();

    private void ClearPath()
    {
        _path.Clear();
        _pathIndex = 0;
        _stuckCount = 0;
    }

    private void Move(float dt)
    {
        Vector3 position = _owner.Position;
        Vector3 wanted = Vector3.Zero;
        Vector3? face = _faceTarget;

        if (HasPath)
        {
            // On to the next corner once it can be walked to straight (no clipping the one before), or when this one is reached.
            NavGrid grid = Grid;
            Vector3 to = Flat(_path[_pathIndex] - position);
            while (_pathIndex < _path.Count - 1 && (to.Length() < 0.2f || grid?.LineWalkable(position, _path[_pathIndex + 1]) == true))
                to = Flat(_path[++_pathIndex] - position);
            float remaining = to.Length();
            bool last = _pathIndex == _path.Count - 1;
            if (last && remaining < 0.25f)
            {
                ClearPath();
            }
            else
            {
                float speed = _speed;
                // Ease into the last point instead of overshooting it.
                if (last) speed *= Math.Clamp(remaining / 1.2f, 0.3f, 1f);
                wanted = to / Math.Max(remaining, 1e-4f) * speed;
                face ??= position + to;
                CheckStuck(dt, speed);
            }
        }

        if (_owner.DynamicBody != null && Physics is { } physics && physics.TryGetVelocity(_owner, out Vector3 velocity, out Vector3 spin))
        {
            // Steer the horizontal velocity, at most one acceleration step per frame; gravity keeps the vertical.
            Vector3 change = wanted - Flat(velocity);
            float limit = Clamp(Acceleration, 0.1f, 500f, 20f) * dt;
            if (change.LengthSquared() > limit * limit) change = Vector3.Normalize(change) * limit;
            if (change.LengthSquared() > 1e-8f) physics.SetVelocity(_owner, velocity + change, spin);
        }
        else if (wanted != Vector3.Zero)
        {
            // No body to push: move directly, keeping the start height above the grid's ground.
            Vector3 next = position + wanted * dt;
            NavGrid grid = Grid;
            if (grid != null && grid.TryGetHeight(next, out float ground))
            {
                if (float.IsNaN(_groundOffset) && grid.TryGetHeight(position, out float start)) _groundOffset = position.Z - start;
                if (!float.IsNaN(_groundOffset)) next.Z = ground + _groundOffset;
            }
            _owner.Position = next;
        }

        if (face is { } point) TurnTowards(point - position, dt);
    }

    /// <summary>Replans when it barely moved for a second (pushed against a crate); drops the path after three tries.</summary>
    private void CheckStuck(float dt, float speed)
    {
        _stuckTimer += dt;
        if (_stuckTimer < 1f) return;
        float progress = FlatDistance(_owner.Position, _stuckFrom);
        _stuckTimer = 0f;
        _stuckFrom = _owner.Position;
        if (progress > Math.Max(0.2f, speed * 0.15f))
        {
            _stuckCount = 0;
            return;
        }
        int tries = _stuckCount + 1;
        if (tries >= 3)
        {
            ClearPath();
            return;
        }
        SetDestination(_pathGoal, _speed);
        _stuckCount = tries;
    }

    private void TurnTowards(Vector3 direction, float dt)
    {
        direction.Z = 0f;
        if (direction.LengthSquared() < 1e-6f) return;
        float goal = YawOf(direction, _yaw);
        float delta = MathHelper.WrapAngle(goal - _yaw);
        float step = MathHelper.ToRadians(Clamp(TurnSpeed, 1f, 3600f, 360f)) * dt;
        float yaw = Math.Abs(delta) <= step ? goal : _yaw + Math.Sign(delta) * step;
        if (Math.Abs(MathHelper.WrapAngle(yaw - _yaw)) < 1e-4f) return;
        _yaw = MathHelper.WrapAngle(yaw);
        _owner.RotationMatrix = _baseRotation * Matrix.CreateRotationZ(_yaw - _baseYaw);
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  HELPERS
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>Where its eyes are: <see cref="EyeHeight"/> above the gameobject's origin.</summary>
    [JsonIgnore] public Vector3 Eye => (_owner?.Position ?? Vector3.Zero) + new Vector3(0f, 0f, Clamp(EyeHeight, -100f, 100f, 0.7f));

    /// <summary>The flat direction it faces (its +Y turned by its yaw).</summary>
    [JsonIgnore] public Vector3 Facing => _owner == null ? Vector3.UnitY : YawDirection(_running ? _yaw : YawOf(_owner.RotationMatrix.Up, 0f));

    // Matrix.CreateRotationZ(yaw) turns +Y to (-sin, cos).
    private static float YawOf(Vector3 direction, float fallback) =>
        direction.X * direction.X + direction.Y * direction.Y > 1e-8f ? MathF.Atan2(-direction.X, direction.Y) : fallback;

    private static Vector3 YawDirection(float yaw) => new(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);

    private static Vector3 Flat(Vector3 v) => new(v.X, v.Y, 0f);

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    private static BoundingBox Bounds(BasicEntity e)
    {
        NavGrid.WorldBounds(e, out Vector3 min, out Vector3 max);
        return new BoundingBox(min, max);
    }

    private static Vector3 Center(BasicEntity e)
    {
        BoundingBox box = Bounds(e);
        Vector3 center = (box.Min + box.Max) * 0.5f;
        return float.IsFinite(center.X) ? center : e.Position;
    }

    private void DrawDebug()
    {
        HelperGeometryManager lines = HelperGeometryManager.GetInstance();
        Vector3 lift = new(0f, 0f, 0.05f);
        Vector3 from = _owner.Position;
        for (int i = _pathIndex; i < _path.Count; i++)
        {
            lines.AddLineStartEnd(i == _pathIndex ? from : _path[i - 1] + lift, _path[i] + lift, 1, Color.Cyan, Color.Cyan);
            from = _path[i];
        }
        Color cone = IsAware ? Color.OrangeRed : Color.Yellow;
        float half = MathHelper.ToRadians(ClampedFieldOfView * 0.5f);
        Vector3 eye = Eye;
        float range = ClampedSightRange;
        for (int i = -1; i <= 1; i += 2)
            lines.AddLineStartEnd(eye, eye + YawDirection(_yaw + i * half) * range, 1, cone, cone);
        if (CanSeeTarget && CurrentTarget != null)
            lines.AddLineStartEnd(eye, Center(CurrentTarget), 1, Color.Red, Color.Red);
    }
}
