using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// Third-person player of the NpcTest sample scene. Runs on the "Player" capsule (a Dynamic body with Freeze
/// Rotation, the name every NPC watches for) and shows the NPCs' states and events on a HUD.
///   W A S D / left stick   move          Shift   run          Space / A   jump
///   Hold right mouse       orbit the camera (or the arrow keys)    Mouse wheel   zoom
///   F                      shout: every NPC within Shout Range notices you, walls or not
///   T                      show or hide the NPCs' paths and view cones (debug lines)
///   R                      reload the scene
/// A guard's attack shoves the player and staggers it briefly; after Max Hits it is knocked out, wakes up at
/// its start and every NPC calms down.
/// </summary>
public sealed class NpcTestPlayerScript : ScriptBehaviour
{
    public const string ScriptId = "npc-test-player";
    private const string DocumentPath = "UI/NpcTest";

    [Range(1, 15)] public float WalkSpeed = 4.5f;
    [Range(1, 20)] public float RunSpeed = 7.5f;
    [Range(1, 12)] public float JumpSpeed = 5.5f;
    [Range(2, 30)]
    [Tooltip("How far a shout (F) carries: NPCs within it notice the player.")]
    public float ShoutRange = 12f;
    [Range(1, 10)]
    [Tooltip("Hits before the player is knocked out and wakes up at the start.")]
    public int MaxHits = 3;

    // The capsule is 2 units tall and centred on its origin.
    private const float HalfHeight = 1f, FootProbe = 0.3f, PivotHeight = 0.8f, StaggerTime = 0.45f;
    private const float GroundAcceleration = 40f, AirAcceleration = 10f;
    private const float LookDegreesPerPixel = 0.2f, KeyLookDegreesPerSecond = 110f;
    private const float FallLimit = -20f;

    /// <summary>Hits taken since the last knock-out.</summary>
    public int Hits { get; private set; }
    /// <summary>Times the player was knocked out this Play session.</summary>
    public int KnockOuts { get; private set; }

    private readonly List<(BasicEntity Owner, NpcComponent Npc)> _npcs = new();
    private Vector3 _start;
    private float _yaw, _pitch = -20f, _distance = 8f, _stagger;   // degrees; yaw 0 looks along +X
    private bool _grounded, _knockOutPending;
    private string _knockedOutBy;
    private UIManager _ui;

    public override void Start()
    {
        _start = Position;
        Vector3 forward = MainCamera?.Forward ?? Forward;
        _yaw = MathHelper.ToDegrees(MathF.Atan2(forward.Y, forward.X));
        Hits = KnockOuts = 0;
        _stagger = 0f;
        _knockOutPending = false;

        _npcs.Clear();
        foreach (BasicEntity entity in GameFlow.SceneLogic?.BasicEntities ?? new List<BasicEntity>())
            foreach (NpcComponent npc in entity.GetComponents<NpcComponent>())
            {
                _npcs.Add((entity, npc));
                npc.Attacked += OnAttacked;
            }

        NpcTestFeed.Clear();
        NpcTestFeed.Post($"{_npcs.Count} NPCs about. Sneak past the guard's patrol, or let the villager follow you.");
        _ui = GameUI.Open(DocumentPath);
        UpdateCamera();
    }

    public override void Stop()
    {
        foreach (var (_, npc) in _npcs) npc.Attacked -= OnAttacked;
        _npcs.Clear();
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        _stagger = Math.Max(0f, _stagger - DeltaTime);
        if (_knockOutPending) KnockOut();
        if (!DebugScreen.ConsoleOpen)
        {
            Look();
            Move();
            if (GameInput.WasPressed(Keys.F) || GameInput.WasPressed(Buttons.Y)) Shout();
            if (GameInput.WasPressed(Keys.T)) ToggleDebug();
            if (GameInput.WasPressed(Keys.R)) GameFlow.ReloadScene();
        }
        if (Position.Z < FallLimit) Respawn();
        UpdateCamera();
        UpdateHud();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  NPC INTERACTION
    ////////////////////////////////////////////////////////////////////////////////

    // Raised by an NPC's component in the middle of its update: only note it here.
    private void OnAttacked(BasicEntity target)
    {
        if (target != GameObject) return;
        Hits++;
        _stagger = StaggerTime;
        if (Hits < Math.Max(1, MaxHits)) return;
        _knockOutPending = true;
        _knockedOutBy = _npcs.FirstOrDefault(n => n.Npc.CurrentTarget == GameObject && n.Npc.State == NpcState.Attacking).Owner?.Name ?? "an NPC";
    }

    private void KnockOut()
    {
        _knockOutPending = false;
        KnockOuts++;
        Hits = 0;
        NpcTestFeed.Post($"Knocked out by {_knockedOutBy}: you wake up at the start");
        Respawn();
        foreach (var (_, npc) in _npcs) npc.Calm();
    }

    /// <summary>Every NPC within Shout Range notices the player, even behind a wall.</summary>
    public void Shout()
    {
        int heard = 0;
        foreach (var (owner, npc) in _npcs)
        {
            if (!npc.Enabled || !owner.IsEnabled || Vector3.Distance(owner.Position, Position) > ShoutRange) continue;
            npc.Alert(GameObject);
            heard++;
        }
        NpcTestFeed.Post(heard == 0 ? "You shout. Nobody is close enough to hear." : $"You shout: {heard} NPC{(heard == 1 ? "" : "s")} heard you");
    }

    private void ToggleDebug()
    {
        bool show = !_npcs.Any(n => n.Npc.ShowDebug);
        foreach (var (_, npc) in _npcs) npc.ShowDebug = show;
        NpcTestFeed.Post(show ? "Showing NPC paths and view cones" : "NPC debug lines hidden");
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  MOVEMENT
    ////////////////////////////////////////////////////////////////////////////////

    private void Respawn()
    {
        Position = _start;
        Velocity = Vector3.Zero;
        _stagger = 0f;
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
        // Staggered by a hit: the shove carries the player, no steering.
        if (_stagger > 0f) return;
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

    ////////////////////////////////////////////////////////////////////////////////
    //  CAMERA: orbits the player
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
    //  HUD (Content/UI/NpcTest.xml + .css)
    ////////////////////////////////////////////////////////////////////////////////

    private string _shownNpcs, _shownStatus;
    private int _feedVersion = -1;

    private void UpdateHud()
    {
        if (_ui == null) return;
        string npcs = string.Join("\n", _npcs.Select(n => $"{n.Owner.Name}  ·  {Describe(n.Npc)}"));
        if (npcs != _shownNpcs)
        {
            _shownNpcs = npcs;
            _ui.SetText("#npcs", npcs);
        }
        string status = $"Hits {Hits} / {Math.Max(1, MaxHits)}  ·  knocked out {KnockOuts}x";
        if (status != _shownStatus)
        {
            _shownStatus = status;
            _ui.SetText("#status", status);
        }
        if (NpcTestFeed.Version != _feedVersion)
        {
            _feedVersion = NpcTestFeed.Version;
            _ui.SetText("#feed", string.Join("\n", NpcTestFeed.Recent));
        }
    }

    private static string Describe(NpcComponent npc)
    {
        string state = npc.State.ToString();
        if (!npc.IsAware) return state;
        return $"{state} ({(npc.CanSeeTarget ? "sees you" : "lost sight")})";
    }
}
