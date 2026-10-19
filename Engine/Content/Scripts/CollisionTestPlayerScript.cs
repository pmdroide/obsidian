using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// First-person player of the CollisionTest sample scene. Runs on the "Player" capsule, a Dynamic
/// body with Freeze Rotation, and drives the main camera from the capsule's head.
/// Reads the project's input actions (Anvil > Window > Input Actions), so it plays on keyboard/mouse or a gamepad:
///   Move      W A S D / left stick          Sprint   Shift / left stick press     Jump   Space / A
///   Look      hold right mouse and drag, the arrow keys, or the right stick
///   Interact  E / X: use the Interactable under the crosshair (a ray from the camera)
///   Attack    hold left mouse / RT: carry a light object      F   throw it (through a window to break it)
///             on Breakable Glass: shoot it (Half-Life: Alyx style shatter at the crosshair)
///   Q         shove what the crosshair is on (impulse at the hit point)
///   R         reset the scene
/// Movement sets the body's velocity with a limited acceleration, so the solver does the pushing:
/// crates resist by their mass and friction, and the 2-tonne anchor doesn't budge.
/// </summary>
public sealed class CollisionTestPlayerScript : ScriptBehaviour
{
    public const string ScriptId = "collision-test-player";
    private const string DocumentPath = "UI/CollisionTest";

    private const float WalkSpeed = 4.5f, RunSpeed = 7.5f, JumpSpeed = 5.5f;
    // The acceleration limit is what makes the player a force rather than a bulldozer:
    // 80 kg x 40 m/s² is about 3200 N against a crate's friction.
    private const float GroundAcceleration = 40f, AirAcceleration = 8f;
    // The capsule is 2 units tall and centred on its origin.
    private const float HalfHeight = 1f, EyeHeight = 0.7f, FootProbe = 0.3f;
    // A jump pressed this long before landing still happens (input buffering).
    private const float JumpBuffer = 0.15f;
    private const float ReachDistance = 6f;   // longest ray; each Interactable also has its own Range
    private const float CarryReach = 3.5f, CarryDistance = 2.4f, CarryMaxMass = 40f;
    private const float CarryGain = 12f, CarryMaxSpeed = 14f, CarryBreakDistance = 4f;
    private const float ThrowSpeed = 14f, ShoveMaxImpulse = 600f, ShoveMaxSpeedChange = 8f;
    private const float FallLimit = -15f;

    private Vector3 _spawn;
    private float _yaw, _pitch;      // degrees; yaw 0 looks along +X
    private bool _grounded;
    private BasicEntity _ground;
    private BasicEntity _carried;
    private readonly List<BasicEntity> _touching = new();

    private UIManager _ui;
    private string _prompt, _touchingText, _carryText;
    private bool _crosshairActive;
    private int _feedVersion = -1;

    public override void Start()
    {
        _spawn = Position;
        Vector3 forward = MainCamera?.Forward ?? Forward;
        _yaw = MathHelper.ToDegrees(MathF.Atan2(forward.Y, forward.X));
        _pitch = Math.Clamp(MathHelper.ToDegrees(MathF.Asin(Math.Clamp(forward.Z, -1f, 1f))), -85f, 85f);
        _touching.Clear();

        CollisionTestFeed.Clear();
        CollisionTestFeed.Post("Push a crate into the green goal zone.");
        _ui = GameUI.Open(DocumentPath);
        UpdateCamera();
    }

    public override void Stop()
    {
        SetHovered(null);
        _carried = null;
        GameUI.Close(_ui);
        _ui = null;
    }

    public override void Update()
    {
        if (!DebugScreen.ConsoleOpen)
        {
            Look();
            Move();
            HandleActions();
            if (GameInput.WasPressed(Keys.R)) GameFlow.ReloadScene();
        }
        if (Position.Z < FallLimit) Respawn();
        Carry();
        UpdateCamera();
        UpdateHud();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  COLLISION EVENTS (this capsule's Physics component)
    ////////////////////////////////////////////////////////////////////////////////

    public override void OnCollisionEnter(Collision collision)
    {
        if (!_touching.Contains(collision.GameObject)) _touching.Add(collision.GameObject);
        // Normal points from the other gameobject towards us, so +Z means we came down on it.
        if (collision.Normal.Z > 0.7f && collision.ImpactSpeed > 6f)
            CollisionTestFeed.Post($"Landed on {collision.GameObject.Name} at {collision.ImpactSpeed:0.0} m/s");
        else if (collision.GameObject.DynamicBody != null && collision.ImpactSpeed > 2f)
            CollisionTestFeed.Post($"Bumped into {collision.GameObject.Name} at {collision.ImpactSpeed:0.0} m/s");
    }

    public override void OnCollisionExit(Collision collision) => _touching.Remove(collision.GameObject);

    public override void OnTriggerEnter(BasicEntity other) => CollisionTestFeed.Post($"Player entered trigger '{other.Name}'");

    public override void OnTriggerExit(BasicEntity other) => CollisionTestFeed.Post($"Player left trigger '{other.Name}'");

    ////////////////////////////////////////////////////////////////////////////////
    //  MOVEMENT
    ////////////////////////////////////////////////////////////////////////////////

    private void Look()
    {
        // Already in degrees: each Look binding has its own Scale (0.15 per pixel while the right mouse
        // button is held, 120/s on the arrows, 150/s on the right stick). +X turns right, +Y looks up.
        Vector2 look = GetVector("Look");
        _yaw -= look.X;
        _pitch = Math.Clamp(_pitch + look.Y, -85f, 85f);
    }

    private void Move()
    {
        float yaw = MathHelper.ToRadians(_yaw);
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        var right = new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0f);

        // W A S D or the left stick, at most length 1 (+Y forward).
        Vector2 move = GetVector("Move");
        Vector3 wish = forward * move.Y + right * move.X;
        bool run = IsActionDown("Sprint");

        UpdateGround();
        Vector3 velocity = Velocity;
        // Steer the horizontal velocity towards the wanted one, at most one acceleration step per frame.
        // In the air without input, keep the momentum (a launch pad's throw carries you off the pad).
        if (_grounded || wish != Vector3.Zero)
        {
            Vector3 change = wish * (run ? RunSpeed : WalkSpeed) - new Vector3(velocity.X, velocity.Y, 0f);
            float limit = (_grounded ? GroundAcceleration : AirAcceleration) * DeltaTime;
            if (change.LengthSquared() > limit * limit) change = Vector3.Normalize(change) * limit;
            velocity += change;
        }
        InputAction jump = GetAction("Jump");
        if (_grounded && jump.WasPressedWithin(JumpBuffer))
        {
            velocity.Z = JumpSpeed;
            jump.ConsumePress();
        }
        Velocity = velocity;
    }

    /// <summary>Five short rays down from the capsule's centre: the middle and four around it.</summary>
    private void UpdateGround()
    {
        _grounded = false;
        _ground = null;
        Span<Vector2> offsets = stackalloc Vector2[] { Vector2.Zero, new(FootProbe, 0), new(-FootProbe, 0), new(0, FootProbe), new(0, -FootProbe) };
        foreach (Vector2 offset in offsets)
        {
            if (!Raycast(Position + new Vector3(offset, 0f), -Vector3.UnitZ, HalfHeight + 0.12f, out RaycastHit hit)) continue;
            _grounded = true;
            _ground = hit.GameObject;
            return;
        }
    }

    private void Respawn()
    {
        Drop();
        Position = _spawn;
        Velocity = Vector3.Zero;
        CollisionTestFeed.Post("Fell off the world - back to the start");
    }

    private Vector3 LookDirection(float minPitch = -85f, float maxPitch = 85f)
    {
        float yaw = MathHelper.ToRadians(_yaw), pitch = MathHelper.ToRadians(Math.Clamp(_pitch, minPitch, maxPitch));
        return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
    }

    private void UpdateCamera()
    {
        Camera camera = MainCamera;
        if (camera == null) return;
        camera.Position = Position + new Vector3(0f, 0f, EyeHeight);
        camera.Forward = LookDirection();
        camera.Up = Vector3.UnitZ;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  RAYCAST ACTIONS: interact, carry, throw, shove
    ////////////////////////////////////////////////////////////////////////////////

    private void HandleActions()
    {
        Ray ray = CameraRay;
        // An Interactable wins when it is the first thing the ray hits and within its Range.
        InteractableComponent interactable = FindInteractable(ray, ReachDistance, out RaycastHit useHit);
        bool aimed = Raycast(ray, ReachDistance, out RaycastHit aimHit);
        BasicEntity movable = aimed && aimHit.GameObject?.DynamicBody != null ? aimHit.GameObject : null;
        bool canCarry = _carried == null && movable != null && aimHit.Distance <= CarryReach;
        BreakableGlassComponent glass = _carried == null && aimed && interactable == null
            ? aimHit.GameObject?.GetComponent<BreakableGlassComponent>() : null;

        SetHovered(interactable != null ? useHit.GameObject : canCarry ? movable : null);

        // BindingText shows the key or button of the device the player is using ("E" or "X").
        if (_carried != null) _prompt = $"Release {BindingText("Attack")}  drop     F  throw";
        else if (interactable != null) _prompt = $"{BindingText("Interact")}  {interactable.Prompt}";
        else if (canCarry && movable.Mass <= CarryMaxMass) _prompt = $"Hold {BindingText("Attack")}  carry {movable.Name}";
        else if (movable != null) _prompt = $"Q  shove {movable.Name} ({movable.Mass:0} kg)";
        else if (glass != null) _prompt = $"{BindingText("Attack")}  shoot {aimHit.GameObject.Name}";
        else _prompt = "";
        _crosshairActive = interactable != null || movable != null || glass != null;

        if (interactable != null && WasActionPressed("Interact"))
            Interact(useHit);

        if (WasActionPressed("Attack") && glass != null && glass.Shatter(aimHit, ray.Direction, GlassStress.Ballistic, speed: 5f))
            CollisionTestFeed.Post($"Shot {aimHit.GameObject.Name}: {glass.ShardCount} panels left in the frame");

        if (WasActionPressed("Attack") && canCarry)
        {
            if (movable.Mass > CarryMaxMass)
                CollisionTestFeed.Post($"{movable.Name} is too heavy to carry ({movable.Mass:0} kg): push it or shove it with Q");
            else if (movable == _ground) CollisionTestFeed.Post("Can't lift what you're standing on");
            else
            {
                _carried = movable;
                CollisionTestFeed.Post($"Picked up {movable.Name}");
            }
        }
        if (_carried != null && !IsActionDown("Attack")) Drop();
        if (_carried != null && GameInput.WasPressed(Keys.F)) Throw();

        if (movable != null && GameInput.WasPressed(Keys.Q))
        {
            // At the hit point, so an off-centre shove also spins it.
            float impulse = Math.Min(ShoveMaxImpulse, movable.Mass * ShoveMaxSpeedChange);
            AddImpulseAtPosition(movable, ray.Direction * impulse, aimHit.Point);
            CollisionTestFeed.Post($"Shoved {movable.Name} ({movable.Mass:0} kg) with {impulse:0} N·s");
        }
    }

    private void Carry()
    {
        if (_carried == null) return;
        if (_carried.DynamicBody == null || _carried == _ground)
        {
            Drop();
            return;
        }
        Camera camera = MainCamera;
        Vector3 eye = camera?.Position ?? Position;
        // Limit how far down it is held, so it can't end up under the player's own feet.
        Vector3 hold = eye + LookDirection(-35f, 70f) * CarryDistance;
        Vector3 offset = hold - _carried.Position;
        if (offset.Length() > CarryBreakDistance)
        {
            CollisionTestFeed.Post($"{_carried.Name} got stuck and slipped away");
            Drop();
            return;
        }
        Vector3 velocity = offset * CarryGain;
        if (velocity.Length() > CarryMaxSpeed) velocity = Vector3.Normalize(velocity) * CarryMaxSpeed;
        SetVelocity(_carried, velocity);
        SetAngularVelocity(_carried, Vector3.Zero);
    }

    private void Drop() => _carried = null;

    private void Throw()
    {
        SetVelocity(_carried, LookDirection() * ThrowSpeed + Velocity);
        CollisionTestFeed.Post($"Threw {_carried.Name}");
        _carried = null;
    }

    /// <summary>Brightens what the crosshair is on and restores the previous one.</summary>
    private static void SetHovered(BasicEntity entity) => CollisionTestFeed.Highlight(entity);

    ////////////////////////////////////////////////////////////////////////////////
    //  HUD (Content/UI/CollisionTest.xml + .css)
    ////////////////////////////////////////////////////////////////////////////////

    private string _shownPrompt, _shownTouching, _shownCarry;
    private bool? _shownCrosshair;

    private void UpdateHud()
    {
        if (_ui == null) return;
        // A destroyed gameobject has no collider left (its Exit event already came, this is a safety net).
        _touching.RemoveAll(e => e.DynamicBody == null && e.StaticBody == null);
        _touchingText = _touching.Count == 0 ? "nothing" : string.Join(", ", _touching.Select(e => e.Name));
        _carryText = _carried?.Name ?? (_grounded ? "on the ground" : "in the air");

        if (_prompt != _shownPrompt)
        {
            _shownPrompt = _prompt;
            _ui.SetText("#prompt", _prompt ?? "");
            _ui.SetClass("#prompt", "visible", !string.IsNullOrEmpty(_prompt));
        }
        if (_crosshairActive != _shownCrosshair)
        {
            _shownCrosshair = _crosshairActive;
            _ui.SetClass("#crosshair", "active", _crosshairActive);
        }
        if (_touchingText != _shownTouching)
        {
            _shownTouching = _touchingText;
            _ui.SetText("#touching", "Touching: " + _touchingText);
        }
        if (_carryText != _shownCarry)
        {
            _shownCarry = _carryText;
            _ui.SetText("#carrying", (_carried != null ? "Carrying: " : "Player: ") + _carryText);
        }
        if (CollisionTestFeed.Version != _feedVersion)
        {
            _feedVersion = CollisionTestFeed.Version;
            _ui.SetText("#feed", string.Join("\n", CollisionTestFeed.Recent));
        }
    }
}
