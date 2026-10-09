using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// Third-person player of the PersistenceTest sample scenes. Its gameobject ("Player", a Dynamic capsule
/// with Freeze Rotation) is marked Persistent in the Inspector, so walking into a portal carries it, with
/// this script still running, into the next scene. Everything on the HUD lives in this instance and keeps
/// counting across scenes: play time, distance walked, the route, and the HUD layer itself.
///   W A S D / left stick   move          Shift   run          Space / A   jump
///   Hold right mouse       orbit the camera (or the arrow keys)    Mouse wheel   zoom
///   R                      reload the scene (the player carries on as it is)
/// </summary>
public sealed class PersistentPlayerScript : ScriptBehaviour
{
    public const string ScriptId = "persistent-player";
    /// <summary>Where the player appears when it arrives in a scene (a gameobject with this name; +Y faces forward).</summary>
    public const string SpawnName = "Player Spawn";
    private const string DocumentPath = "UI/PersistenceTest";

    [Range(1, 15)] public float WalkSpeed = 5f;
    [Range(1, 20)] public float RunSpeed = 9f;
    [Range(1, 12)] public float JumpSpeed = 5.5f;
    [Range(2, 20)]
    [Tooltip("Starting distance of the camera behind the player (the mouse wheel changes it).")]
    public float CameraDistance = 7f;

    // The capsule is 2 units tall and centred on its origin.
    private const float HalfHeight = 1f, FootProbe = 0.3f, PivotHeight = 0.8f;
    private const float GroundAcceleration = 40f, AirAcceleration = 10f;
    private const float LookDegreesPerPixel = 0.2f, KeyLookDegreesPerSecond = 110f;
    private const float FallLimit = -20f;

    /// <summary>Seconds since this instance started: it keeps running through scene loads.</summary>
    public float PlayTime { get; private set; }
    /// <summary>Metres walked on the ground plane, across every scene.</summary>
    public float DistanceWalked { get; private set; }
    /// <summary>Scene loads this player came through (<see cref="OnSceneLoaded"/>).</summary>
    public int ScenesLoaded { get; private set; }
    /// <summary>The scenes visited, in order.</summary>
    public IReadOnlyList<string> Route => _route;

    private readonly List<string> _route = new();
    private Vector3 _start, _lastPosition;
    private float _yaw, _pitch = -15f, _distance;   // degrees; yaw 0 looks along +X
    private bool _grounded;
    private UIManager _ui;

    public override void Start()
    {
        _route.Clear();
        _route.Add(GameFlow.ActiveSceneName ?? "?");
        _start = _lastPosition = Position;
        _distance = CameraDistance;
        Vector3 forward = MainCamera?.Forward ?? Forward;
        _yaw = MathHelper.ToDegrees(MathF.Atan2(forward.Y, forward.X));

        PersistenceTestFeed.Clear();
        PersistenceTestFeed.Post(IsPersistent
            ? "Walk into the glowing portal: this player comes along to the next scene."
            : "This Player isn't Persistent (Inspector): a scene load would leave it behind.");
        // Opened by a persistent gameobject's script, so the layer stays open across scene loads.
        _ui = GameUI.Open(DocumentPath);
        UpdateCamera();
    }

    public override void Stop()
    {
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void OnSceneLoaded()
    {
        ScenesLoaded++;
        _route.Add(GameFlow.ActiveSceneName ?? "?");
        MoveToSpawn();
        PersistenceTestFeed.Post($"Arrived in {GameFlow.ActiveSceneName}: same player, {DistanceWalked:0} m walked so far");
    }

    public override void Update()
    {
        PlayTime += DeltaTime;
        if (!DebugScreen.ConsoleOpen)
        {
            Look();
            Move();
            if (GameInput.WasPressed(Keys.R)) GameFlow.ReloadScene();
        }
        if (Position.Z < FallLimit)
        {
            MoveToSpawn();
            PersistenceTestFeed.Post("Fell off the world: back to the spawn");
        }
        Odometer();
        UpdateCamera();
        UpdateHud();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  MOVEMENT
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>To the scene's Player Spawn (or where this player started), facing along its +Y, at rest.</summary>
    private void MoveToSpawn()
    {
        BasicEntity spawn = FindGameObject(SpawnName);
        if (spawn != null)
        {
            Position = spawn.Position + new Vector3(0f, 0f, HalfHeight + 0.1f);
            Vector3 facing = spawn.RotationMatrix.Up;
            if (new Vector2(facing.X, facing.Y).LengthSquared() > 1e-4f) _yaw = MathHelper.ToDegrees(MathF.Atan2(facing.Y, facing.X));
        }
        else Position = _start;
        Velocity = Vector3.Zero;
        _lastPosition = Position;
    }

    private void Look()
    {
        MouseState mouse = Input.mouseState, last = Input.mouseLastState;
        // Both frames held, so pressing the button doesn't jump the view.
        if (mouse.RightButton == ButtonState.Pressed && last.RightButton == ButtonState.Pressed)
        {
            _yaw -= (mouse.X - last.X) * LookDegreesPerPixel;
            _pitch -= (mouse.Y - last.Y) * LookDegreesPerPixel;
        }
        float turn = KeyLookDegreesPerSecond * DeltaTime;
        if (GameInput.IsDown(Keys.Left)) _yaw += turn;
        if (GameInput.IsDown(Keys.Right)) _yaw -= turn;
        if (GameInput.IsDown(Keys.Up)) _pitch += turn;
        if (GameInput.IsDown(Keys.Down)) _pitch -= turn;
        _pitch = Math.Clamp(_pitch, -70f, 35f);
        if (GameInput.MouseScroll != 0) _distance = Math.Clamp(_distance - Math.Sign(GameInput.MouseScroll) * 0.75f, 2f, 20f);
    }

    private void Move()
    {
        float yaw = MathHelper.ToRadians(_yaw);
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        var right = new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0f);

        Vector2 stick = GameInput.LeftStick;
        Vector3 wish = forward * stick.Y + right * stick.X;
        if (GameInput.IsDown(Keys.W)) wish += forward;
        if (GameInput.IsDown(Keys.S)) wish -= forward;
        if (GameInput.IsDown(Keys.D)) wish += right;
        if (GameInput.IsDown(Keys.A)) wish -= right;
        if (wish.LengthSquared() > 1f) wish.Normalize();
        bool run = GameInput.IsDown(Keys.LeftShift) || GameInput.IsDown(Keys.RightShift);

        UpdateGround();
        Vector3 velocity = Velocity;
        // Steer the horizontal velocity towards the wanted one, at most one acceleration step per frame.
        if (_grounded || wish != Vector3.Zero)
        {
            Vector3 change = wish * (run ? RunSpeed : WalkSpeed) - new Vector3(velocity.X, velocity.Y, 0f);
            float limit = (_grounded ? GroundAcceleration : AirAcceleration) * DeltaTime;
            if (change.LengthSquared() > limit * limit) change = Vector3.Normalize(change) * limit;
            velocity += change;
        }
        if (_grounded && (GameInput.WasPressed(Keys.Space) || GameInput.WasPressed(Buttons.A))) velocity.Z = JumpSpeed;
        Velocity = velocity;
    }

    /// <summary>Five short rays down from the capsule's centre: the middle and four around it.</summary>
    private void UpdateGround()
    {
        _grounded = false;
        Span<Vector2> offsets = stackalloc Vector2[] { Vector2.Zero, new(FootProbe, 0), new(-FootProbe, 0), new(0, FootProbe), new(0, -FootProbe) };
        foreach (Vector2 offset in offsets)
        {
            if (!Raycast(Position + new Vector3(offset, 0f), -Vector3.UnitZ, HalfHeight + 0.12f)) continue;
            _grounded = true;
            return;
        }
    }

    private void Odometer()
    {
        Vector3 step = Position - _lastPosition;
        float flat = new Vector2(step.X, step.Y).Length();
        // Teleports (spawn, respawn) don't count as walking.
        if (flat < 2f) DistanceWalked += flat;
        _lastPosition = Position;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  CAMERA: orbits the player; whichever scene is loaded, its main camera follows
    ////////////////////////////////////////////////////////////////////////////////

    private void UpdateCamera()
    {
        Camera camera = MainCamera;
        if (camera == null) return;
        float yaw = MathHelper.ToRadians(_yaw), pitch = MathHelper.ToRadians(_pitch);
        var look = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        Vector3 pivot = Position + new Vector3(0f, 0f, PivotHeight);
        // Pull in when a wall is between the player and the camera.
        float distance = _distance;
        if (Raycast(pivot, -look, distance + 0.3f, out RaycastHit hit)) distance = Math.Max(0.5f, hit.Distance - 0.3f);
        camera.Position = pivot - look * distance;
        camera.Forward = look;
        camera.Up = Vector3.UnitZ;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  HUD (Content/UI/PersistenceTest.xml + .css)
    ////////////////////////////////////////////////////////////////////////////////

    private string _shownScene, _shownStats, _shownRoute, _shownCompanion;
    private int _feedVersion = -1;

    private void UpdateHud()
    {
        if (_ui == null) return;
        string scene = GameFlow.ActiveSceneName ?? "";
        if (scene != _shownScene)
        {
            _shownScene = scene;
            _ui.SetText("#scene", scene);
        }
        string stats = $"Alive {PlayTime:0} s  ·  walked {DistanceWalked:0} m  ·  {ScenesLoaded} scene load{(ScenesLoaded == 1 ? "" : "s")} survived";
        if (stats != _shownStats)
        {
            _shownStats = stats;
            _ui.SetText("#stats", stats);
        }
        string route = "Route: " + string.Join("  >  ", _route.TakeLast(4));
        if (route != _shownRoute)
        {
            _shownRoute = route;
            _ui.SetText("#route", route);
        }
        BasicEntity orb = FindGameObject(CompanionOrbScript.OrbName);
        string companion = orb == null ? "Companion: not in this scene"
            : orb.IsPersistent ? "Companion: following you (DontDestroyOnLoad)"
            : "Companion: waiting here, walk up to it";
        if (companion != _shownCompanion)
        {
            _shownCompanion = companion;
            _ui.SetText("#companion", companion);
        }
        if (PersistenceTestFeed.Version != _feedVersion)
        {
            _feedVersion = PersistenceTestFeed.Version;
            _ui.SetText("#feed", string.Join("\n", PersistenceTestFeed.Recent));
        }
    }
}
