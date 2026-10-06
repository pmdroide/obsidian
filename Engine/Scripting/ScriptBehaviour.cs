using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Physics;
using Engine.Recources;
using Microsoft.Xna.Framework;
using GameComponent = Engine.Components.GameComponent;

namespace Engine.Scripting;

/// <summary>
/// A C# behaviour with a fresh instance for each attached gameobject and Play session.
/// The helpers below cover transform, physics and audio; see Docs/markdown/Script_Behaviours.md.
/// </summary>
public abstract class ScriptBehaviour
{
    /// <summary>The gameobject this script runs on; null when it runs on the main camera.</summary>
    public BasicEntity GameObject { get; private set; }
    /// <summary>The camera this script runs on (Main Camera > Add Component); null on a gameobject.</summary>
    public Camera Camera { get; private set; }
    /// <summary>Seconds since the previous frame. Use this for frame-rate independent motion.</summary>
    public float DeltaTime { get; private set; }

    public virtual void Start() { }
    public virtual void Update() { }
    public virtual void Stop() { }

    internal void Attach(BasicEntity owner, Camera camera = null)
    {
        GameObject = owner;
        Camera = owner == null ? camera : null;
    }
    internal void Tick(float deltaTime)
    {
        DeltaTime = deltaTime;
        Update();
    }

    /// <summary>Runs <see cref="Stop"/>, then releases sounds this script started.</summary>
    internal void Shutdown()
    {
        try { Stop(); }
        finally { StopAllSounds(); }
    }

    /// <summary>Writes a line to the engine/editor log.</summary>
    protected void Log(string message) =>
        EditorBridge.Log($"[{GameObject?.Name ?? (Camera != null ? "Main Camera" : null)}] {message}");

    public T GetComponent<T>() where T : GameComponent =>
        GameObject != null ? GameObject.GetComponent<T>() : Camera?.GetComponent<T>();

    ////////////////////////////////////////////////////////////////////////////////
    //  TRANSFORM (world space, Z up, rotations in degrees)
    //  On the main camera, Forward is the view direction and Scale is always one.
    ////////////////////////////////////////////////////////////////////////////////

    public Vector3 Position
    {
        get => GameObject?.Position ?? Camera?.Position ?? Vector3.Zero;
        set
        {
            if (GameObject != null) GameObject.Position = value;
            else if (Camera != null) Camera.Position = value;
        }
    }

    /// <summary>Rotation only (no scale or translation).</summary>
    public Matrix Rotation
    {
        get => GameObject?.RotationMatrix ?? (Camera != null ? CameraRotation(Camera) : Matrix.Identity);
        set
        {
            if (GameObject != null) GameObject.RotationMatrix = value;
            else if (Camera != null)
            {
                // A camera stores its local +Y as Forward and +Z as Up.
                Camera.Forward = SafeNormalize(value.Up, Vector3.UnitY);
                Camera.Up = SafeNormalize(value.Backward, Vector3.UnitZ);
            }
        }
    }

    public Vector3 Scale
    {
        get => GameObject?.Scale ?? Vector3.One;
        set { if (GameObject != null) GameObject.Scale = value; }
    }

    /// <summary>The local +X axis in world space.</summary>
    public Vector3 Right => SafeNormalize(Rotation.Right, Vector3.UnitX);
    /// <summary>The local +Y axis in world space (the view direction on a camera).</summary>
    public Vector3 Forward => SafeNormalize(Rotation.Up, Vector3.UnitY);
    /// <summary>The local +Z axis in world space.</summary>
    public Vector3 Up => SafeNormalize(Rotation.Backward, Vector3.UnitZ);

    /// <summary>Moves by <paramref name="offset"/>, in world axes or (local) the object's own axes.</summary>
    public void Translate(Vector3 offset, bool local = false) =>
        Position += local ? Vector3.TransformNormal(offset, Rotation) : offset;

    /// <summary>Rotates around <paramref name="axis"/>, a world axis or (local) one of the gameobject's own axes.</summary>
    public void Rotate(Vector3 axis, float degrees, bool local = false)
    {
        if (axis == Vector3.Zero) return;
        Matrix turn = Matrix.CreateFromAxisAngle(Vector3.Normalize(axis), MathHelper.ToRadians(degrees));
        Rotation = local ? turn * Rotation : Rotation * turn;
    }

    /// <summary>Sets the rotation from angles around the world X, Y, then Z axes.</summary>
    public void SetRotation(float xDegrees, float yDegrees, float zDegrees) =>
        Rotation = Matrix.CreateRotationX(MathHelper.ToRadians(xDegrees))
                 * Matrix.CreateRotationY(MathHelper.ToRadians(yDegrees))
                 * Matrix.CreateRotationZ(MathHelper.ToRadians(zDegrees));

    /// <summary>Turns the gameobject so <see cref="Forward"/> (+Y) points at <paramref name="target"/>, keeping +Z near <paramref name="up"/>.</summary>
    public void LookAt(Vector3 target, Vector3? up = null)
    {
        Vector3 forward = target - Position;
        if (forward.LengthSquared() < 1e-12f) return;
        forward.Normalize();
        Vector3 worldUp = up ?? Vector3.UnitZ;
        Vector3 right = Vector3.Cross(forward, worldUp);
        // Looking straight along the up axis: any perpendicular will do.
        if (right.LengthSquared() < 1e-8f) right = Vector3.Cross(forward, Math.Abs(forward.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
        right.Normalize();
        Vector3 newUp = Vector3.Cross(right, forward);
        var m = Matrix.Identity;
        m.Right = right;      // local +X
        m.Up = forward;       // local +Y
        m.Backward = newUp;   // local +Z
        Rotation = m;
    }

    /// <summary>Converts a point from this gameobject's local space (scaled, rotated, translated) to world space.</summary>
    public Vector3 TransformPoint(Vector3 localPoint) =>
        Vector3.Transform(localPoint * Scale, Rotation) + Position;

    /// <summary>Rotates a local direction into world space (no scale or translation).</summary>
    public Vector3 TransformDirection(Vector3 localDirection) =>
        Vector3.TransformNormal(localDirection, Rotation);

    private static Matrix CameraRotation(Camera camera)
    {
        Vector3 forward = SafeNormalize(camera.Forward, Vector3.UnitY);
        Vector3 right = Vector3.Cross(forward, camera.Up);
        if (right.LengthSquared() < 1e-8f) right = Vector3.Cross(forward, Math.Abs(forward.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
        right.Normalize();
        var m = Matrix.Identity;
        m.Right = right;
        m.Up = forward;
        m.Backward = Vector3.Cross(right, forward);
        return m;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  PHYSICS (needs a Physics component; forces only affect Dynamic bodies)
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The gameobject's Physics component, or null. Change BodyType/Mass/Buoyancy here.</summary>
    public PhysicsComponent Physics => GameObject?.Physics;

    /// <summary>True while a Dynamic body exists, so velocity and forces take effect.</summary>
    public bool HasRigidbody => GameObject?.DynamicBody != null && GameObject.PhysicsScene != null;

    /// <summary>Linear velocity in units per second. Zero (and ignored) without a Dynamic body.</summary>
    public Vector3 Velocity
    {
        get => GameObject?.PhysicsScene?.TryGetVelocity(GameObject, out var linear, out _) == true ? linear : Vector3.Zero;
        set
        {
            var scene = GameObject?.PhysicsScene;
            if (scene != null && scene.TryGetVelocity(GameObject, out _, out var angular)) scene.SetVelocity(GameObject, value, angular);
        }
    }

    /// <summary>Spin in radians per second around each world axis. Zero (and ignored) without a Dynamic body.</summary>
    public Vector3 AngularVelocity
    {
        get => GameObject?.PhysicsScene?.TryGetVelocity(GameObject, out _, out var angular) == true ? angular : Vector3.Zero;
        set
        {
            var scene = GameObject?.PhysicsScene;
            if (scene != null && scene.TryGetVelocity(GameObject, out var linear, out _)) scene.SetVelocity(GameObject, linear, value);
        }
    }

    /// <summary>Continuous push in newtons for this frame; call it every Update while the force should act.</summary>
    public void AddForce(Vector3 force) => GameObject?.PhysicsScene?.ApplyImpulse(GameObject, force * DeltaTime);

    /// <summary>Continuous push in newtons applied at a world-space point, which also makes the body spin.</summary>
    public void AddForceAtPosition(Vector3 force, Vector3 worldPoint) =>
        GameObject?.PhysicsScene?.ApplyImpulse(GameObject, force * DeltaTime, worldPoint);

    /// <summary>Instant kick (mass x velocity change), e.g. a jump or an explosion. Call once.</summary>
    public void AddImpulse(Vector3 impulse) => GameObject?.PhysicsScene?.ApplyImpulse(GameObject, impulse);

    /// <summary>Continuous twist in newton-metres around a world axis for this frame; call it every Update.</summary>
    public void AddTorque(Vector3 torque) => GameObject?.PhysicsScene?.ApplyAngularImpulse(GameObject, torque * DeltaTime);

    /// <summary>Instant spin kick around a world axis. Call once.</summary>
    public void AddAngularImpulse(Vector3 impulse) => GameObject?.PhysicsScene?.ApplyAngularImpulse(GameObject, impulse);

    /// <summary>
    /// Closest collider along a ray, ignoring this gameobject. Only gameobjects with an enabled
    /// Physics component (and not the Water role) can be hit.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        var scene = GameObject?.PhysicsScene ?? ScenePhysics.Current;
        return scene != null && scene.Raycast(origin, direction, maxDistance, out hit, GameObject);
    }

    /// <summary>Raycast without hit details.</summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance) =>
        Raycast(origin, direction, maxDistance, out _);

    ////////////////////////////////////////////////////////////////////////////////
    //  AUDIO (paths are relative to Content, with extension, e.g. "Audio/blip.wav")
    ////////////////////////////////////////////////////////////////////////////////

    private readonly List<ScriptSound> _sounds = new();

    /// <summary>Plays the first enabled Audio component on this gameobject with its Inspector settings.</summary>
    public bool PlayAudio()
    {
        var audio = GameObject?.GetComponents<AudioComponent>().FirstOrDefault(a => a.Enabled);
        if (audio == null) return false;
        audio.Play(GameObject);
        return audio.IsPlaying;
    }

    /// <summary>Stops every Audio component on this gameobject.</summary>
    public void StopAudio()
    {
        foreach (var audio in GameObject?.GetComponents<AudioComponent>() ?? Enumerable.Empty<AudioComponent>()) audio.OnStop();
    }

    /// <summary>True while any Audio component on this gameobject is playing.</summary>
    public bool IsAudioPlaying => GameObject?.GetComponents<AudioComponent>().Any(a => a.IsPlaying) == true;

    /// <summary>Plays a non-positional (2D) sound. Returns null when audio is unavailable or the clip can't load.</summary>
    public ScriptSound PlaySound(string clipPath, float volume = 1f, bool loop = false, float pitch = 1f) =>
        Track(Audio.Instance?.PlaySound(clipPath, Math.Clamp(volume, 0f, 1f), pitch, loop, contentRelativePath: true));

    /// <summary>Plays a 3D sound that follows this gameobject (or camera): full volume within <paramref name="minDistance"/>, quieter beyond.</summary>
    public ScriptSound PlaySound3D(string clipPath, float volume = 1f, bool loop = false,
                                   float minDistance = 10f, float maxDistance = 1000f)
    {
        var self = this; // follows the gameobject or camera
        return Track(Audio.Instance?.PlaySound3D(clipPath, () => self.Position, Math.Clamp(volume, 0f, 1f), loop,
            minDistance, Math.Max(maxDistance, minDistance), contentRelativePath: true));
    }

    /// <summary>Plays a 3D sound at a fixed world position.</summary>
    public ScriptSound PlaySoundAt(string clipPath, Vector3 position, float volume = 1f, bool loop = false,
                                   float minDistance = 10f, float maxDistance = 1000f) =>
        Track(Audio.Instance?.PlaySound3D(clipPath, position, Math.Clamp(volume, 0f, 1f), loop,
            minDistance, Math.Max(maxDistance, minDistance), contentRelativePath: true));

    /// <summary>Stops every sound this script started (also happens automatically when it stops).</summary>
    public void StopAllSounds()
    {
        foreach (var sound in _sounds) sound.Stop();
        _sounds.Clear();
    }

    private ScriptSound Track(FmodForFoxes.Channel? channel)
    {
        if (channel == null) return null;
        _sounds.RemoveAll(s => !s.IsPlaying);
        var sound = new ScriptSound(channel.Value);
        _sounds.Add(sound);
        return sound;
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback) =>
        v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : fallback;
}
