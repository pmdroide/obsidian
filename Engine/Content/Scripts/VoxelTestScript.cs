using System.Globalization;
using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Voxels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Vista;

namespace Engine.Scripting;

/// <summary>
/// First-person builder of the VoxelTest sample scene. Runs on the "Player" capsule (a Dynamic body with
/// Freeze Rotation) and drives the main camera from its head. The world is the scene's voxel world
/// (Inspector > World > Voxel World), reached through <see cref="ScriptBehaviour.Voxels"/>.
///   W A S D        move            Shift   run            Space   jump (fly: up, Ctrl down)
///   Hold right mouse  look (or the arrow keys)            V       fly on/off
///   Left mouse     dig (hold to keep digging)             Right click / F   place the selected block
///   1-9, 0 / wheel pick the block                         B       blast a crater at the crosshair
///   G              throw a physics ball                   N       new world (random seed)
///   T              time of day +3 h                       R       reload the scene
/// Everything it changes (blocks, seed, time) is put back when Play stops.
/// </summary>
public sealed class VoxelTestScript : ScriptBehaviour
{
    public const string ScriptId = "voxel-test";
    private const string DocumentPath = "UI/VoxelTest";

    private const float WalkSpeed = 5f, RunSpeed = 9f, FlySpeed = 14f, JumpSpeed = 6.4f;
    private const float GroundAcceleration = 45f, AirAcceleration = 10f;
    // The capsule is 2 units tall and centred on its origin.
    private const float HalfHeight = 1f, HalfWidth = 0.45f, EyeHeight = 0.7f, FootProbe = 0.3f;
    private const float LookDegreesPerPixel = 0.15f, KeyLookDegreesPerSecond = 120f;
    private const float RepeatSeconds = 0.2f, TapSeconds = 0.3f, TapPixels = 6f;
    private const int MaxBalls = 12;

    [Range(2, 12)]
    [Tooltip("How far away blocks can be dug and placed, in metres.")]
    public float Reach = 7;
    [Range(1, 8)]
    [Tooltip("Radius of the crater B blasts, in metres.")]
    public float BlastRadius = 3;

    private VoxelWorld _world;
    private Vector3 _spawn;
    private float _yaw, _pitch;      // degrees; yaw 0 looks along +X
    private bool _grounded, _flying;
    private int _selected;
    private float _digTimer, _placeTimer;
    private float _rightHeld, _rightTravel;
    private int _dug, _placed, _blasts;
    private readonly Queue<BasicEntity> _balls = new();
    private readonly Dictionary<BasicEntity, Vector3> _throws = new();
    private int _ballsThrown;
    // Four thin glowing bars framing the face under the crosshair.
    private readonly List<BasicEntity> _cursor = new();
    private static readonly Vector3 Hidden = new(0, 0, -10000);
    private bool _hasTarget;
    private VoxelHit _target;

    private UIManager _ui;
    private string _message;
    private float _messageTimer;

    public VoxelBlock SelectedBlock => VoxelBlocks.Placeable[_selected];
    public bool IsFlying => _flying;
    public int Dug => _dug;
    public int Placed => _placed;
    /// <summary>The block under the crosshair this frame, if any.</summary>
    public VoxelHit? Target => _hasTarget ? _target : null;

    public override void Start()
    {
        _spawn = Position;
        Vector3 forward = MainCamera?.Forward ?? Forward;
        _yaw = MathHelper.ToDegrees(MathF.Atan2(forward.Y, forward.X));
        _pitch = Math.Clamp(MathHelper.ToDegrees(MathF.Asin(Math.Clamp(forward.Z, -1f, 1f))), -85f, 85f);
        _selected = 0;
        _flying = false;
        _dug = _placed = _blasts = _ballsThrown = 0;
        _balls.Clear();
        _throws.Clear();
        _world = null;

        _cursor.Clear();
        for (int i = 0; i < 4; i++)
        {
            BasicEntity bar = Spawn("Cube", Hidden, "Block Cursor");
            if (bar == null) break;
            bar.AddComponent(new MaterialComponent
            {
                MaterialType = MaterialEffect.MaterialTypes.Emissive, Red = 1f, Green = 0.95f, Blue = 0.82f,
                EmissiveStrength = 1.6f, CastShadows = false,
            });
            _cursor.Add(bar);
        }

        _ui = GameUI.Open(DocumentPath);
        _shown.Clear();
        Show("Dig with the left mouse button, build with a right click.");
        SyncWorld();
        UpdateCamera();
    }

    public override void Stop()
    {
        GameUI.Close(_ui);
        _ui = null;
        _cursor.Clear();
        _world = null;
    }

    public override void Update()
    {
        SyncWorld();
        if (!DebugScreen.ConsoleOpen)
        {
            Look();
            Move();
            HandleKeys();
        }
        FindTarget();
        if (!DebugScreen.ConsoleOpen) HandleBlocks();
        float floor = _world != null ? _world.Origin.Z - 10 : -60;
        if (Position.Z < floor) Respawn("Fell out of the world - back on the ground");
        _balls.TryPeek(out BasicEntity oldest);
        if (oldest != null && oldest.Position.Z < floor)
        {
            _throws.Remove(oldest);
            Destroy(_balls.Dequeue());
        }
        foreach (BasicEntity ball in _throws.Keys.Where(b => b.DynamicBody != null).ToList())
        {
            SetVelocity(ball, _throws[ball]);
            _throws.Remove(ball);
        }
        UpdateCursor();
        UpdateCamera();
        UpdateHud();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  WORLD
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>A new world (first frame, new seed, reload): stand on its surface again.</summary>
    private void SyncWorld()
    {
        VoxelWorld world = Voxels;
        if (ReferenceEquals(world, _world)) return;
        bool replaced = _world != null;
        _world = world;
        if (world == null) return;
        PlaceOnSurface(_spawn);
        if (replaced) Show($"New world: seed {world.Settings.Seed}");
    }

    private void PlaceOnSurface(Vector3 at)
    {
        Position = SurfaceUnder(_world, at, out Vector3 surface) ? surface + new Vector3(0, 0, HalfHeight + 0.05f) : at;
        Velocity = Vector3.Zero;
    }

    /// <summary>The highest block top under a capsule's footprint centred on <paramref name="at"/> (it may straddle four columns).</summary>
    public static bool SurfaceUnder(VoxelWorld world, Vector3 at, out Vector3 surface)
    {
        surface = at;
        bool found = false;
        if (world == null) return false;
        foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            if (!world.TryGetSurface(at + new Vector3(corner * HalfWidth, 0), out Vector3 top)) continue;
            if (!found || top.Z > surface.Z) surface = new Vector3(at.X, at.Y, top.Z);
            found = true;
        }
        return found;
    }

    private void Respawn(string message)
    {
        PlaceOnSurface(_spawn);
        Show(message);
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  MOVEMENT
    ////////////////////////////////////////////////////////////////////////////////

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
        _pitch = Math.Clamp(_pitch, -85f, 85f);
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

        if (_flying)
        {
            // Setting the velocity every frame cancels gravity, so the capsule hovers (it still collides).
            float up = (GameInput.IsDown(Keys.Space) ? 1 : 0) - (GameInput.IsDown(Keys.LeftControl) || GameInput.IsDown(Keys.C) ? 1 : 0);
            Velocity = (wish + Vector3.UnitZ * up) * (run ? FlySpeed * 2 : FlySpeed);
            return;
        }

        UpdateGround();
        Vector3 velocity = Velocity;
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

    /// <summary>Five short rays down from the capsule's centre; the voxel colliders answer them like any static.</summary>
    private void UpdateGround()
    {
        _grounded = false;
        Span<Vector2> offsets = stackalloc Vector2[] { Vector2.Zero, new(FootProbe, 0), new(-FootProbe, 0), new(0, FootProbe), new(0, -FootProbe) };
        foreach (Vector2 offset in offsets)
        {
            if (!Raycast(Position + new Vector3(offset, 0f), -Vector3.UnitZ, HalfHeight + 0.12f, out RaycastHit _)) continue;
            _grounded = true;
            return;
        }
    }

    private Vector3 LookDirection()
    {
        float yaw = MathHelper.ToRadians(_yaw), pitch = MathHelper.ToRadians(_pitch);
        return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
    }

    private Vector3 EyePosition => Position + new Vector3(0f, 0f, EyeHeight);

    private void UpdateCamera()
    {
        Camera camera = MainCamera;
        if (camera == null) return;
        camera.Position = EyePosition;
        camera.Forward = LookDirection();
        camera.Up = Vector3.UnitZ;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  BLOCKS
    ////////////////////////////////////////////////////////////////////////////////

    private void HandleKeys()
    {
        Keys[] numbers = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0 };
        for (int i = 0; i < numbers.Length && i < VoxelBlocks.Placeable.Length; i++)
            if (GameInput.WasPressed(numbers[i])) _selected = i;
        int scroll = GameInput.MouseScroll;
        if (scroll != 0)
            _selected = ((_selected - scroll) % VoxelBlocks.Placeable.Length + VoxelBlocks.Placeable.Length) % VoxelBlocks.Placeable.Length;

        if (GameInput.WasPressed(Keys.V))
        {
            _flying = !_flying;
            Show(_flying ? "Flying: Space up, Ctrl down" : "Walking");
        }
        if (GameInput.WasPressed(Keys.G)) ThrowBall();
        if (GameInput.WasPressed(Keys.B)) Blast();
        if (GameInput.WasPressed(Keys.N) && SceneEnvironment != null)
        {
            // Changing a voxel setting regenerates the world (here at once, as Play needs the ground).
            SceneEnvironment.VoxelSeed = Random.Shared.Next(1, 100000);
            SyncWorld();
        }
        if (GameInput.WasPressed(Keys.T) && SceneEnvironment != null)
        {
            SceneEnvironment.TimeOfDay = (SceneEnvironment.TimeOfDay + 3) % 24;
            Show(string.Format(CultureInfo.InvariantCulture, "Time of day {0:00}:00", Math.Floor(SceneEnvironment.TimeOfDay)));
        }
        if (GameInput.WasPressed(Keys.R)) GameFlow.ReloadScene();
    }

    private void FindTarget()
    {
        _hasTarget = _world != null && _world.Raycast(EyePosition, LookDirection(), Reach, out _target);
    }

    private void HandleBlocks()
    {
        MouseState mouse = Input.mouseState, last = Input.mouseLastState;

        // Dig: on the click, then again every RepeatSeconds while held.
        _digTimer -= DeltaTime;
        bool digHeld = mouse.LeftButton == ButtonState.Pressed;
        if (digHeld && (last.LeftButton == ButtonState.Released || _digTimer <= 0))
        {
            Dig();
            _digTimer = RepeatSeconds;
        }

        // Place: a short right click (a longer hold or a drag turns the view instead), or F.
        if (mouse.RightButton == ButtonState.Pressed)
        {
            if (last.RightButton == ButtonState.Released) _rightHeld = _rightTravel = 0;
            _rightHeld += DeltaTime;
            _rightTravel += Math.Abs(mouse.X - last.X) + Math.Abs(mouse.Y - last.Y);
        }
        else if (last.RightButton == ButtonState.Pressed && _rightHeld < TapSeconds && _rightTravel < TapPixels)
            Place();

        _placeTimer -= DeltaTime;
        if (GameInput.IsDown(Keys.F) && (GameInput.WasPressed(Keys.F) || _placeTimer <= 0))
        {
            Place();
            _placeTimer = RepeatSeconds;
        }
    }

    private void Dig()
    {
        if (!_hasTarget) return;
        if (!_world.IsEditable(_target.Block))
        {
            Show("The bottom layer can't be dug");
            return;
        }
        if (_world.SetBlock(_target.Block, VoxelBlock.Air)) _dug++;
        FindTarget();
    }

    private void Place()
    {
        if (!_hasTarget) return;
        VoxelCoord cell = _target.Adjacent;
        if (!_world.IsEditable(cell) || _world.GetBlock(cell) != VoxelBlock.Air)
        {
            Show("Can't build outside the world");
            return;
        }
        var player = new BoundingBox(Position - new Vector3(HalfWidth, HalfWidth, HalfHeight),
            Position + new Vector3(HalfWidth, HalfWidth, HalfHeight));
        BoundingBox block = _world.BlockBounds(cell);
        // Shrink a little so standing right next to a block still allows building against it.
        block.Min += new Vector3(0.02f);
        block.Max -= new Vector3(0.02f);
        if (player.Intersects(block))
        {
            Show("That's where you're standing");
            return;
        }
        if (_world.SetBlock(cell, SelectedBlock)) _placed++;
        FindTarget();
    }

    private void Blast()
    {
        if (!_hasTarget)
        {
            Show("Aim at the ground to blast a crater");
            return;
        }
        Vector3 center = _target.Point;
        int removed = _world.FillSphere(center, BlastRadius, VoxelBlock.Air);
        _blasts++;
        _dug += removed;
        // Knock nearby balls away from the blast.
        foreach (BasicEntity ball in _balls)
        {
            Vector3 offset = ball.Position - center;
            float distance = offset.Length();
            if (distance > BlastRadius * 2.5f || distance < 1e-3f) continue;
            AddImpulse(ball, Vector3.Normalize(offset + Vector3.UnitZ) * 60f * (1 - distance / (BlastRadius * 2.5f)));
        }
        Show($"Blasted {removed} blocks");
        FindTarget();
    }

    private void ThrowBall()
    {
        Vector3 direction = LookDirection();
        BasicEntity ball = Spawn("IsoSphere", EyePosition + direction * 1.2f, $"Ball {++_ballsThrown}");
        if (ball == null) return;
        ball.Scale = new Vector3(0.35f);
        ball.AddComponent(new PhysicsComponent { BodyType = PhysicsBodyType.Dynamic, Mass = 10f });
        Color color = VoxelBlocks.SwatchColor(SelectedBlock);
        ball.AddComponent(new MaterialComponent { Red = color.R / 255f, Green = color.G / 255f, Blue = color.B / 255f, Roughness = 0.35f });
        // Its body is created at the next physics update; the throw is applied once it exists.
        _throws[ball] = direction * 12f + Velocity;
        _balls.Enqueue(ball);
        if (_balls.Count > MaxBalls)
        {
            BasicEntity dropped = _balls.Dequeue();
            _throws.Remove(dropped);
            Destroy(dropped);
        }
    }

    /// <summary>Frames the face the crosshair is on; the bars are parked far below while nothing is targeted.</summary>
    private void UpdateCursor()
    {
        if (_cursor.Count < 4) return;
        if (!_hasTarget)
        {
            foreach (BasicEntity bar in _cursor) bar.Position = Hidden;
            return;
        }
        float size = _world.BlockSize, thickness = size * 0.035f;
        Vector3 n = _target.Normal;
        Vector3 face = _world.BlockCenter(_target.Block) + n * (size * 0.5f + size * 0.006f);
        // The face's two in-plane axes.
        Vector3 a = n.X != 0 ? Vector3.UnitY : Vector3.UnitX;
        Vector3 b = n.Z != 0 ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 Abs(Vector3 v) => new(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));
        for (int i = 0; i < 4; i++)
        {
            // Bars 0/1 run along a (on either side along b), bars 2/3 along b. The cube model spans -1..1.
            Vector3 along = i < 2 ? a : b, across = i < 2 ? b : a;
            float side = i % 2 == 0 ? -1 : 1;
            _cursor[i].Position = face + across * side * (size * 0.5f - thickness * 0.5f);
            _cursor[i].Scale = Abs(along) * (size * 0.5f) + Abs(across) * (thickness * 0.5f) + Abs(n) * (thickness * 0.25f);
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  HUD (Content/UI/VoxelTest.xml + .css)
    ////////////////////////////////////////////////////////////////////////////////

    private readonly Dictionary<string, string> _shown = new();

    private void Show(string message)
    {
        _message = message;
        _messageTimer = 3f;
    }

    private void SetText(string id, string text)
    {
        if (_shown.TryGetValue(id, out string old) && old == text) return;
        _shown[id] = text;
        _ui.SetText(id, text);
    }

    private void SetClass(string id, string className, bool enabled)
    {
        string key = id + "." + className;
        string value = enabled ? "1" : "0";
        if (_shown.TryGetValue(key, out string old) && old == value) return;
        _shown[key] = value;
        _ui.SetClass(id, className, enabled);
    }

    private void UpdateHud()
    {
        if (_ui == null) return;
        CultureInfo c = CultureInfo.InvariantCulture;
        VoxelSystem system = GameFlow.SceneLogic?.Voxels;
        if (_world == null)
        {
            SetText("#terrain", "No voxel world: turn it on in Inspector > Environment");
            SetText("#stats", "");
        }
        else
        {
            VoxelWorldSettings s = _world.Settings;
            SetText("#terrain", string.Format(c, "{0}  ·  seed {1}  ·  {2} x {3} x {4} blocks", s.Terrain, s.Seed,
                _world.SizeX, _world.SizeY, _world.SizeZ));
            if (system != null)
                SetText("#stats", string.Format(c, "{0} chunks  ·  {1:#,0} triangles  ·  {2:#,0} collider  ·  built in {3:0} ms",
                    system.VisibleChunkCount, system.TriangleCount, system.ColliderTriangleCount, system.LastBuildMilliseconds));
        }
        SetText("#looking", _hasTarget
            ? string.Format(c, "Looking at {0} {1}  ·  {2:0.0} m", VoxelBlocks.Name(_target.Type), _target.Block, _target.Distance)
            : "Looking at the sky");
        SetText("#counts", string.Format(c, "Dug {0}  ·  placed {1}  ·  craters {2}  ·  balls {3}", _dug, _placed, _blasts, _balls.Count));
        SetText("#mode", (_flying ? "Flying" : _grounded ? "Walking" : "In the air") + "  ·  V to " + (_flying ? "walk" : "fly"));
        SetText("#block-name", VoxelBlocks.Name(SelectedBlock));
        for (int i = 0; i < VoxelBlocks.Placeable.Length; i++) SetClass("#slot" + i, "selected", i == _selected);
        SetClass("#crosshair", "active", _hasTarget);

        _messageTimer -= DeltaTime;
        SetText("#message", _message ?? "");
        SetClass("#message", "visible", _messageTimer > 0 && !string.IsNullOrEmpty(_message));
    }
}
